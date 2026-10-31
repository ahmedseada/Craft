// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Find and replace in an editor's document: a query (on one line), matched by case or not, as a whole word or not. The
/// snapshot is searched line by line through a pooled buffer, never as one string. Replace all is one undo step.
/// </summary>
public sealed class EditorFind
{
    private readonly EditorModel _editor;
    private string _query = "";
    private bool _matchCase;
    private bool _wholeWord;
    private FindCount _count;
    private int _countVersion = -1;
    private string? _countQuery;
    private bool _countCase;
    private bool _countWord;

    internal EditorFind(EditorModel editor) => _editor = editor;

    /// <summary>The text to find (a query with a line break finds nothing); setting it shows the matches.</summary>
    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? "";
            IsHighlighting = _query.Length > 0;
            _editor.Invalidate();
        }
    }

    /// <summary>Whether case must match.</summary>
    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            _matchCase = value;
            _editor.Invalidate();
        }
    }

    /// <summary>Whether a match must be a whole word (no letter, digit or underscore just before or after it).</summary>
    public bool WholeWord
    {
        get => _wholeWord;
        set
        {
            _wholeWord = value;
            _editor.Invalidate();
        }
    }

    /// <summary>Whether the matches are drawn (Escape clears it; setting the query sets it).</summary>
    public bool IsHighlighting { get; set; }

    /// <summary>Whether the last find went past the end (or the start) of the text and came round.</summary>
    public bool LastWrapped { get; private set; }

    internal bool Searchable => _query.Length > 0 && _query.AsSpan().IndexOfAny('\r', '\n') < 0;

    private StringComparison Comparison => _matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Selects the next match after the selection, coming round from the start; false when there is none.</summary>
    public bool FindNext()
    {
        if (!Searchable)
        {
            return false;
        }

        IsHighlighting = true;
        var snapshot = _editor.Snapshot;
        int from = _editor.Selection.End;
        int startLine = snapshot.GetLineIndex(from);
        int lines = snapshot.LineCount;
        char[]? buffer = null;
        try
        {
            for (int n = 0; n <= lines; n++)
            {
                int index = (startLine + n) % lines;
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int offset = n == 0 ? from - line.Start : 0;
                int found = NextIn(text, offset);
                if (found >= 0 && (n < lines || line.Start + found < from))
                {
                    LastWrapped = n > 0 && index <= startLine;
                    Select(line.Start + found);
                    return true;
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return false;
    }

    /// <summary>Selects the match before the selection, coming round from the end; false when there is none.</summary>
    public bool FindPrevious()
    {
        if (!Searchable)
        {
            return false;
        }

        IsHighlighting = true;
        var snapshot = _editor.Snapshot;
        int from = _editor.Selection.Start;
        int startLine = snapshot.GetLineIndex(from);
        int lines = snapshot.LineCount;
        char[]? buffer = null;
        try
        {
            for (int n = 0; n <= lines; n++)
            {
                int index = ((startLine - n) % lines + lines) % lines;
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int limit = n == 0 ? from - line.Start : text.Length;
                int found = PreviousIn(text, limit);
                if (found >= 0 && (n < lines || line.Start + found + _query.Length > from))
                {
                    LastWrapped = n > 0 && index >= startLine;
                    Select(line.Start + found);
                    return true;
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return false;
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
        if (selection.Length == _query.Length && IsMatchAt(selection.Start))
        {
            _editor.Replace(selection.Start, selection.Length, replacement, EditKind.Command, SelectionState.At(selection.Start + replacement.Length));
            replaced = true;
        }

        FindNext();
        return replaced;
    }

    /// <summary>Replaces every match with <paramref name="replacement"/> as one undo step; returns how many were replaced.</summary>
    public int ReplaceAll(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Searchable)
        {
            return 0;
        }

        var snapshot = _editor.Snapshot;
        var edits = ArrayPool<TextEdit>.Shared.Rent(64);
        char[]? buffer = null;
        int count = 0;
        try
        {
            for (int i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLine(i);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int from = 0;
                int found;
                while ((found = NextIn(text, from)) >= 0)
                {
                    if (count == edits.Length)
                    {
                        var bigger = ArrayPool<TextEdit>.Shared.Rent(edits.Length * 2);
                        edits.AsSpan(0, count).CopyTo(bigger);
                        ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
                        edits = bigger;
                    }

                    edits[count++] = new TextEdit(line.Start + found, _query.Length, replacement);
                    from = found + _query.Length;
                }
            }

            if (count > 0)
            {
                int caret = _editor.Selection.Caret;
                int delta = 0;
                for (int i = 0; i < count && edits[i].End <= caret; i++)
                {
                    delta += edits[i].Delta;
                }

                _editor.ApplyEdits(edits.AsSpan(0, count), EditKind.Command, SelectionState.At(caret + delta));
            }
        }
        finally
        {
            TextReading.Return(buffer);
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
        }

        return count;
    }

    /// <summary>The matches in the document, counted up to <c>CraftOptions.FindCountLimit</c> (kept until the text or the query changes).</summary>
    public FindCount CountMatches()
    {
        if (!Searchable)
        {
            return default;
        }

        var snapshot = _editor.Snapshot;
        if (_countVersion == snapshot.Version && ReferenceEquals(_countQuery, _query) && _countCase == _matchCase && _countWord == _wholeWord)
        {
            return _count;
        }

        int limit = _editor.Options.FindCountLimit;
        int count = 0;
        char[]? buffer = null;
        try
        {
            for (int i = 0; i < snapshot.LineCount && count < limit; i++)
            {
                var text = TextReading.ReadLine(snapshot, snapshot.GetLine(i), ref buffer);
                int from = 0;
                int found;
                while (count < limit && (found = NextIn(text, from)) >= 0)
                {
                    count++;
                    from = found + _query.Length;
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        _count = new FindCount(count, count >= limit);
        _countVersion = snapshot.Version;
        _countQuery = _query;
        _countCase = _matchCase;
        _countWord = _wholeWord;
        return _count;
    }

    /// <summary>The first match in <paramref name="line"/> at or after <paramref name="from"/>, or -1.</summary>
    internal int NextIn(ReadOnlySpan<char> line, int from)
    {
        var query = _query.AsSpan();
        while (from <= line.Length - query.Length)
        {
            int found = line[from..].IndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            found += from;
            if (!_wholeWord || IsWholeWord(line, found, query.Length))
            {
                return found;
            }

            from = found + 1;
        }

        return -1;
    }

    private int PreviousIn(ReadOnlySpan<char> line, int limit)
    {
        var query = _query.AsSpan();
        limit = Math.Min(limit, line.Length);
        while (limit >= query.Length)
        {
            int found = line[..limit].LastIndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            if (!_wholeWord || IsWholeWord(line, found, query.Length))
            {
                return found;
            }

            limit = found + query.Length - 1;
        }

        return -1;
    }

    private static bool IsWholeWord(ReadOnlySpan<char> line, int start, int length) =>
        (start == 0 || !TextReading.IsWordChar(line[start - 1])) && (start + length >= line.Length || !TextReading.IsWordChar(line[start + length]));

    private bool IsMatchAt(int position)
    {
        var snapshot = _editor.Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        if (position + _query.Length > line.End)
        {
            return false;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            return NextIn(text, position - line.Start) == position - line.Start;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private void Select(int start)
    {
        _editor.SetSelection(new SelectionState(start, start + _query.Length));
        _editor.EnsureCaretVisible();
    }
}
