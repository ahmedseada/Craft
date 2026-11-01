// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.RegularExpressions;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// What find looks for, fixed once made (any thread may search with it): the query, matched by case or not, as a whole
/// word or not, as text or as a regular expression (one line at a time; a match of no characters is never one). A
/// pattern that does not parse is not <see cref="Searchable"/> and says why in <see cref="Problem"/>.
/// </summary>
public sealed class FindPattern
{
    private readonly Regex? _regex;
    private volatile bool _timedOut;

    /// <summary>A pattern for <paramref name="query"/>; a regular expression stops after <paramref name="regexTimeout"/> on one line.</summary>
    public FindPattern(string query, bool matchCase, bool wholeWord, bool regex, TimeSpan regexTimeout)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query;
        MatchCase = matchCase;
        WholeWord = wholeWord;
        IsRegex = regex;
        if (query.Length == 0 || query.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return;
        }

        if (regex)
        {
            try
            {
                _regex = new Regex(query, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), regexTimeout);
            }
            catch (ArgumentException e)
            {
                Problem = "The regular expression does not parse: " + e.Message;
                return;
            }
        }

        Searchable = true;
    }

    /// <summary>No query: finds nothing.</summary>
    public static FindPattern None { get; } = new("", false, false, false, TimeSpan.FromSeconds(1));

    /// <summary>The text (or the expression) to find.</summary>
    public string Query { get; }

    /// <summary>Whether case must match.</summary>
    public bool MatchCase { get; }

    /// <summary>Whether a match must be a whole word (no letter, digit or underscore just before or after it).</summary>
    public bool WholeWord { get; }

    /// <summary>Whether <see cref="Query"/> is a regular expression.</summary>
    public bool IsRegex { get; }

    /// <summary>Whether it can find anything (a query on one line that parses).</summary>
    public bool Searchable { get; }

    /// <summary>Why it cannot search (the expression does not parse), or null.</summary>
    public string? Problem { get; }

    /// <summary>Whether a line's search ran past the time limit (that line was taken as having no match).</summary>
    public bool TimedOut => _timedOut;

    private StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The first match in <paramref name="line"/> at or after <paramref name="from"/>, or -1; its length in <paramref name="length"/>.</summary>
    public int NextIn(ReadOnlySpan<char> line, int from, out int length)
    {
        length = 0;
        if (!Searchable || from > line.Length)
        {
            return -1;
        }

        if (_regex is { } regex)
        {
            try
            {
                foreach (var match in regex.EnumerateMatches(line, from))
                {
                    if (match.Length > 0 && (!WholeWord || IsWholeWord(line, match.Index, match.Length)))
                    {
                        length = match.Length;
                        return match.Index;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _timedOut = true;
            }

            return -1;
        }

        var query = Query.AsSpan();
        while (from <= line.Length - query.Length)
        {
            int found = line[from..].IndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            found += from;
            if (!WholeWord || IsWholeWord(line, found, query.Length))
            {
                length = query.Length;
                return found;
            }

            from = found + 1;
        }

        return -1;
    }

    /// <summary>The last match in <paramref name="line"/> that ends at or before <paramref name="limit"/>, or -1; its length in <paramref name="length"/>.</summary>
    public int PreviousIn(ReadOnlySpan<char> line, int limit, out int length)
    {
        length = 0;
        if (!Searchable)
        {
            return -1;
        }

        limit = Math.Min(limit, line.Length);
        if (_regex is not null)
        {
            int last = -1;
            int from = 0;
            int found;
            while ((found = NextIn(line, from, out int matched)) >= 0 && found + matched <= limit)
            {
                last = found;
                length = matched;
                from = found + 1;
            }

            return last;
        }

        var query = Query.AsSpan();
        while (limit >= query.Length)
        {
            int found = line[..limit].LastIndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            if (!WholeWord || IsWholeWord(line, found, query.Length))
            {
                length = query.Length;
                return found;
            }

            limit = found + query.Length - 1;
        }

        return -1;
    }

    /// <summary>
    /// The text that replaces the match at <paramref name="start"/> of <paramref name="line"/>: <paramref name="replacement"/>
    /// as it is, or for a regular expression with its groups put in ($1, ${name}, $$).
    /// </summary>
    public string Replacement(ReadOnlySpan<char> line, int start, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_regex is not { } regex || replacement.AsSpan().IndexOf('$') < 0)
        {
            return replacement;
        }

        try
        {
            var match = regex.Match(line.ToString(), start);
            return match.Success && match.Index == start ? match.Result(replacement) : replacement;
        }
        catch (RegexMatchTimeoutException)
        {
            _timedOut = true;
            return replacement;
        }
    }

    private static bool IsWholeWord(ReadOnlySpan<char> line, int start, int length) =>
        (start == 0 || !TextReading.IsWordChar(line[start - 1])) && (start + length >= line.Length || !TextReading.IsWordChar(line[start + length]));
}

/// <summary>
/// Find and replace in an editor's document (the find overlay's model): a query on one line, matched by case or not, as a
/// whole word or not, as text or as a regular expression. The snapshot is searched line by line through a pooled buffer,
/// never as one string. The matches of a snapshot are listed once (<see cref="CountMatches"/>, up to
/// <c>CRAFT_FIND_COUNT_LIMIT</c>) and kept while the text and the pattern stay the same, so "3 of 17", next and previous
/// read the list; a document longer than <c>CRAFT_FIND_BACKGROUND_CHARS</c> is listed off the UI thread
/// (<see cref="CountInBackgroundAsync"/>), by snapshot version. Replace all is one undo step.
/// </summary>
public sealed class EditorFind
{
    private readonly EditorModel _editor;
    private string _query = "";
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regex;
    private FindPattern _pattern = FindPattern.None;
    private MatchList? _matches;

    internal EditorFind(EditorModel editor) => _editor = editor;

    /// <summary>The text to find (a query with a line break finds nothing); setting it shows the matches.</summary>
    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? "";
            IsHighlighting = _query.Length > 0;
            Rebuild();
        }
    }

    /// <summary>Whether case must match.</summary>
    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            _matchCase = value;
            Rebuild();
        }
    }

    /// <summary>Whether a match must be a whole word (no letter, digit or underscore just before or after it).</summary>
    public bool WholeWord
    {
        get => _wholeWord;
        set
        {
            _wholeWord = value;
            Rebuild();
        }
    }

    /// <summary>Whether the query is a regular expression (one line at a time; replacements take $1, ${name}).</summary>
    public bool UseRegex
    {
        get => _regex;
        set
        {
            _regex = value;
            Rebuild();
        }
    }

    /// <summary>The pattern in use (immutable: a background search keeps the one it started with).</summary>
    public FindPattern Pattern => _pattern;

    /// <summary>Why the query cannot search (a regular expression that does not parse), or null.</summary>
    public string? Problem => _pattern.Problem;

    /// <summary>Whether the matches are drawn (Escape clears it; setting the query sets it).</summary>
    public bool IsHighlighting { get; set; }

    /// <summary>Whether the last find went past the end (or the start) of the text and came round.</summary>
    public bool LastWrapped { get; private set; }

    internal bool Searchable => _pattern.Searchable;

    private bool Large => _editor.Snapshot.Length > _editor.Options.FindBackgroundCharacters;

    /// <summary>Selects the next match after the selection, coming round from the start; false when there is none.</summary>
    public bool FindNext() => Find(forward: true, _editor.Selection.End);

    /// <summary>Selects the match before the selection, coming round from the end; false when there is none.</summary>
    public bool FindPrevious() => Find(forward: false, _editor.Selection.Start);

    /// <summary>Selects the first match at or after the selection's start (as the query is typed); false when there is none.</summary>
    public bool FindFromSelection() => Find(forward: true, _editor.Selection.Start);

    /// <summary>
    /// As <see cref="FindNext"/> (or <see cref="FindPrevious"/>), searching a large document off the calling thread when its
    /// matches are not listed yet; the selection moves on the calling thread's context, only if the text did not change.
    /// </summary>
    public async Task<bool> FindAsync(bool forward, bool fromSelectionStart = false, CancellationToken cancellationToken = default)
    {
        int from = forward ? (fromSelectionStart ? _editor.Selection.Start : _editor.Selection.End) : _editor.Selection.Start;
        if (!Searchable)
        {
            return false;
        }

        var snapshot = _editor.Snapshot;
        if (!Large || Listed(snapshot) is not null)
        {
            return Find(forward, from);
        }

        var pattern = _pattern;
        var found = await Task.Run(() => Search(snapshot, pattern, from, forward), cancellationToken).ConfigureAwait(true);
        if (!ReferenceEquals(pattern, _pattern) || _editor.Snapshot.Version != snapshot.Version || found.Start < 0)
        {
            return false;
        }

        IsHighlighting = true;
        LastWrapped = found.Wrapped;
        Select(found.Start, found.Length);
        return true;
    }

    /// <summary>
    /// Replaces the selection with <paramref name="replacement"/> when it is a match, then finds the next match; true when
    /// one was replaced.
    /// </summary>
    public bool ReplaceOne(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Searchable)
        {
            return false;
        }

        var selection = _editor.Selection;
        bool replaced = false;
        if (ReplacementAt(selection.Start, selection.Length, replacement) is { } text)
        {
            _editor.Replace(selection.Start, selection.Length, text, EditKind.Command, SelectionState.At(selection.Start + text.Length));
            replaced = true;
        }

        FindNext();
        return replaced;
    }

    /// <summary>Replaces every match with <paramref name="replacement"/> as one undo step; returns how many were replaced.</summary>
    public int ReplaceAll(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return Searchable ? Apply(Edits(_editor.Snapshot, _pattern, replacement)) : 0;
    }

    /// <summary>
    /// As <see cref="ReplaceAll"/>, finding a large document's matches off the calling thread; the edits are applied (one
    /// undo step) on the calling thread's context only if the text did not change meanwhile (else 0, nothing changed).
    /// </summary>
    public async Task<int> ReplaceAllInBackgroundAsync(string replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Searchable)
        {
            return 0;
        }

        if (!Large)
        {
            return ReplaceAll(replacement);
        }

        var snapshot = _editor.Snapshot;
        var pattern = _pattern;
        var edits = await Task.Run(() => Edits(snapshot, pattern, replacement), cancellationToken).ConfigureAwait(true);
        return _editor.Snapshot.Version == snapshot.Version && ReferenceEquals(pattern, _pattern) ? Apply(edits) : 0;
    }

    /// <summary>
    /// The matches in the document, counted up to <c>CRAFT_FIND_COUNT_LIMIT</c>, and which one the selection is ("3 of
    /// 17"); listed once per snapshot and pattern, on the calling thread.
    /// </summary>
    public FindCount CountMatches()
    {
        if (!Searchable)
        {
            return default;
        }

        var snapshot = _editor.Snapshot;
        var list = Listed(snapshot) ?? (_matches = List(snapshot, _pattern, _editor.Options.FindCountLimit));
        return list.CountAt(_editor.Selection);
    }

    /// <summary>
    /// As <see cref="CountMatches"/>, listing a large document's matches off the calling thread (the list is kept for the
    /// snapshot's version it was made from, and the count comes back on the calling thread's context).
    /// </summary>
    public async Task<FindCount> CountInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        if (!Searchable)
        {
            return default;
        }

        var snapshot = _editor.Snapshot;
        if (Listed(snapshot) is { } listed)
        {
            return listed.CountAt(_editor.Selection);
        }

        if (!Large)
        {
            return CountMatches();
        }

        var pattern = _pattern;
        int limit = _editor.Options.FindCountLimit;
        var list = await Task.Run(() => List(snapshot, pattern, limit, cancellationToken), cancellationToken).ConfigureAwait(true);
        if (ReferenceEquals(pattern, _pattern) && _editor.Snapshot.Version == snapshot.Version)
        {
            _matches = list;
        }

        return list.CountAt(_editor.Selection);
    }

    /// <summary>
    /// The listed matches' starts of the current snapshot and pattern while the find overlay highlights them (empty when not
    /// highlighting or not listed: the ruler never lists them itself), and the list itself as the key of what it shows.
    /// </summary>
    internal ReadOnlySpan<int> HighlightedStarts(out object? list)
    {
        if (IsHighlighting && Searchable && Listed(_editor.Snapshot) is { } listed)
        {
            list = listed;
            return listed.Starts;
        }

        list = null;
        return [];
    }

    /// <summary>Whether the matches of the current snapshot and pattern are listed (the count and next read the list).</summary>
    public bool IsListed => Listed(_editor.Snapshot) is not null;

    /// <summary>The first match in <paramref name="line"/> at or after <paramref name="from"/>, or -1 (the highlight layer's).</summary>
    internal int NextIn(ReadOnlySpan<char> line, int from, out int length) => _pattern.NextIn(line, from, out length);

    private void Rebuild()
    {
        var timeout = TimeSpan.FromMilliseconds(_editor.Options.FindRegexTimeoutMilliseconds);
        _pattern = new FindPattern(_query, _matchCase, _wholeWord, _regex, timeout);
        _matches = null;
        _editor.Invalidate();
    }

    private MatchList? Listed(ITextSnapshot snapshot) =>
        _matches is { } list && list.Version == snapshot.Version && ReferenceEquals(list.Pattern, _pattern) ? list : null;

    private bool Find(bool forward, int from)
    {
        if (!Searchable)
        {
            return false;
        }

        IsHighlighting = true;
        var snapshot = _editor.Snapshot;
        if (Listed(snapshot) is { Capped: false } list)
        {
            if (list.Count == 0)
            {
                return false;
            }

            var (start, length, wrapped) = list.Step(from, forward);
            LastWrapped = wrapped;
            Select(start, length);
            return true;
        }

        var found = Search(snapshot, _pattern, from, forward);
        if (found.Start < 0)
        {
            return false;
        }

        LastWrapped = found.Wrapped;
        Select(found.Start, found.Length);
        return true;
    }

    // The next (or previous) match from a position, coming round the document once: any thread.
    private static (int Start, int Length, bool Wrapped) Search(ITextSnapshot snapshot, FindPattern pattern, int from, bool forward)
    {
        int startLine = snapshot.GetLineIndex(from);
        int lines = snapshot.LineCount;
        char[]? buffer = null;
        try
        {
            for (int n = 0; n <= lines; n++)
            {
                int index = forward ? (startLine + n) % lines : ((startLine - n) % lines + lines) % lines;
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int found;
                int length;
                if (forward)
                {
                    found = pattern.NextIn(text, n == 0 ? from - line.Start : 0, out length);
                    if (found >= 0 && (n < lines || line.Start + found < from))
                    {
                        return (line.Start + found, length, n > 0 && index <= startLine);
                    }
                }
                else
                {
                    found = pattern.PreviousIn(text, n == 0 ? from - line.Start : text.Length, out length);
                    if (found >= 0 && (n < lines || line.Start + found + length > from))
                    {
                        return (line.Start + found, length, n > 0 && index >= startLine);
                    }
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return (-1, 0, false);
    }

    // Every match of a snapshot, up to the limit: any thread.
    private static MatchList List(ITextSnapshot snapshot, FindPattern pattern, int limit, CancellationToken token = default)
    {
        var starts = new List<int>();
        var lengths = new List<int>();
        char[]? buffer = null;
        try
        {
            for (int i = 0; i < snapshot.LineCount && starts.Count < limit; i++)
            {
                if ((i & 0x3FF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                var line = snapshot.GetLine(i);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int from = 0;
                int found;
                while (starts.Count < limit && (found = pattern.NextIn(text, from, out int length)) >= 0)
                {
                    starts.Add(line.Start + found);
                    lengths.Add(length);
                    from = found + Math.Max(1, length);
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return new MatchList(snapshot.Version, pattern, [.. starts], [.. lengths], starts.Count >= limit);
    }

    // Every match's edit, in order: any thread.
    private static TextEdit[] Edits(ITextSnapshot snapshot, FindPattern pattern, string replacement)
    {
        var edits = new List<TextEdit>();
        char[]? buffer = null;
        try
        {
            for (int i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLine(i);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int from = 0;
                int found;
                while ((found = pattern.NextIn(text, from, out int length)) >= 0)
                {
                    edits.Add(new TextEdit(line.Start + found, length, pattern.Replacement(text, found, replacement)));
                    from = found + Math.Max(1, length);
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return [.. edits];
    }

    private int Apply(TextEdit[] edits)
    {
        if (edits.Length == 0 || _editor.IsReadOnly)
        {
            return 0;                                        // a read-only text replaces nothing (its edit is refused): none said
        }

        int caret = _editor.Selection.Caret;
        int delta = 0;
        for (int i = 0; i < edits.Length && edits[i].End <= caret; i++)
        {
            delta += edits[i].Delta;
        }

        _editor.ApplyEdits(edits, EditKind.Command, SelectionState.At(caret + delta));
        return edits.Length;
    }

    // The replacement for the match exactly at (position, length), or null when that is not a match.
    private string? ReplacementAt(int position, int length, string replacement)
    {
        if (length == 0)
        {
            return null;
        }

        var snapshot = _editor.Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        if (position + length > line.End)
        {
            return null;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int offset = position - line.Start;
            // A regular expression may match there only when searched from before it (a lookbehind): from the line's start.
            int from = _pattern.IsRegex ? 0 : offset;
            int found;
            while ((found = _pattern.NextIn(text, from, out int matched)) >= 0 && found <= offset)
            {
                if (found == offset && matched == length)
                {
                    return _pattern.Replacement(text, found, replacement);
                }

                from = found + 1;
            }

            return null;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private void Select(int start, int length)
    {
        _editor.SetSelection(new SelectionState(start, start + length));
        _editor.EnsureCaretVisible();
    }

    // A snapshot's matches in order, with the selection's place found by a binary search.
    private sealed class MatchList(int version, FindPattern pattern, int[] starts, int[] lengths, bool capped)
    {
        public int Version { get; } = version;

        public FindPattern Pattern { get; } = pattern;

        public bool Capped { get; } = capped;

        public int Count => starts.Length;

        public ReadOnlySpan<int> Starts => starts;

        public FindCount CountAt(SelectionState selection)
        {
            int at = Array.BinarySearch(starts, selection.Start);
            int current = at >= 0 && lengths[at] == selection.Length ? at + 1 : 0;
            return new FindCount(starts.Length, Capped, current);
        }

        // The match after (or before) a position, coming round.
        public (int Start, int Length, bool Wrapped) Step(int from, bool forward)
        {
            if (forward)
            {
                int at = Array.BinarySearch(starts, from);
                int next = at >= 0 ? at : ~at;
                bool wrapped = next >= starts.Length;
                next = wrapped ? 0 : next;
                return (starts[next], lengths[next], wrapped);
            }

            // The last match that ends at or before the position.
            int index = -1;
            int low = 0, high = starts.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >>> 1;
                if (starts[mid] + lengths[mid] <= from)
                {
                    index = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            bool back = index < 0;
            index = back ? starts.Length - 1 : index;
            return (starts[index], lengths[index], back);
        }
    }
}
