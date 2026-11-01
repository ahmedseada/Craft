// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Bracket pair colours for one editor (X part 9, <c>CRAFT_BRACKET_COLORS</c>): each bracket of the language's pairs in the
/// colour of its nesting depth (<see cref="EditorColors.BracketDepth(int)"/>), one with no partner in
/// <see cref="EditorColors.BracketUnmatched"/>. Brackets inside comments and strings (the language's
/// <see cref="LanguageRules"/>: its comment tokens and <see cref="LanguageRules.StringDelimiters"/>) are skipped, so a depth
/// needs no parse and no classification. The state at each line's start (the depth, and whether a block comment is open)
/// is kept per line: a frame reads only the lines it has not read yet, and only up to
/// <see cref="Core.CraftOptions.BracketColorsScanLines"/> of them (never a scan from the file's top per frame); an edit
/// splices the states of the lines it touched and the lines after it are read again only until one starts in the state it
/// had (rule 73). An opening bracket is unmatched when the depth never comes back below it before the file's end, known
/// once every line was read (a least depth from each line to the end, one pass back). Used on the UI thread.
/// </summary>
public sealed class BracketColors
{
    private readonly EditorModel _model;
    private readonly List<(int First, int Removed, int Added)> _splices = [];
    private int[] _state = [];
    private int[] _least = [];
    private int[] _suffix = [];
    private int _count;
    private int _valid;
    private int _tailFrom = -1;
    private int _tailTo = -1;
    private bool _suffixValid;
    private LanguageRules? _rules;
    private SearchValues<char>? _interesting;
    private int[] _events = new int[64];

    internal BracketColors(EditorModel model) => _model = model;

    /// <summary>Whether brackets are coloured (the option, and a language with brackets).</summary>
    public bool IsEnabled => _model.Options.BracketColors && _model.Rules.Brackets.Count > 0;

    /// <summary>Whether every line's state is known (an opening bracket's match can then be told).</summary>
    public bool IsComplete => _count > 0 && _valid >= _count - 1;

    /// <summary>The lines whose state it has read since it was made (a warm frame reads none).</summary>
    public long LinesScanned { get; private set; }

    /// <summary>The depth at the start of <paramref name="line"/>, or -1 when it is not known yet.</summary>
    public int DepthAt(int line) => _count > 0 && line >= 0 && line <= _valid && line < _count ? _state[line] >> 1 : -1;

    /// <summary>
    /// Reads the states up to <paramref name="line"/> while <paramref name="budget"/> lines remain (each line read takes one);
    /// true when the state at its start is known.
    /// </summary>
    public bool Ensure(int line, ref int budget)
    {
        Prepare();
        line = Math.Clamp(line, 0, _count - 1);
        if (line <= _valid)
        {
            return true;
        }

        var snapshot = _model.Snapshot;
        char[]? buffer = null;
        try
        {
            while (_valid < line && budget > 0)
            {
                int at = _valid;
                var text = TextReading.ReadLine(snapshot, snapshot.GetLine(at), ref buffer);
                int end = Scan(text, _state[at], out int least, Span<int>.Empty, out _);
                _least[at] = least;
                LinesScanned++;
                budget--;
                int next = at + 1;
                _suffixValid = false;
                if (next == _tailFrom && _tailTo >= next && _state[next] == end)
                {
                    _valid = _tailTo;                               // the lines after the edit start as they did: still right
                    _tailFrom = _tailTo = -1;
                    continue;
                }

                if (next >= _tailFrom)
                {
                    _tailFrom = _tailTo = -1;
                }

                _state[next] = end;
                _valid = next;
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return line <= _valid;
    }

    /// <summary>
    /// The coloured brackets of <paramref name="line"/> (its text <paramref name="text"/>) into <paramref name="into"/> as
    /// pairs (character index, colour), in order; how many pairs. None when the line's start state is not known yet.
    /// </summary>
    internal int ColorsOf(int line, ReadOnlySpan<char> text, EditorColors colors, ref int[] into)
    {
        if (!IsEnabled || _count == 0 || line > _valid || line >= _count)
        {
            return 0;
        }

        int end = Scan(text, _state[line], out _, _events, out int events);
        if (events * 3 > _events.Length)
        {
            _events = new int[Math.Max(events * 3, _events.Length * 2)];
            end = Scan(text, _state[line], out _, _events, out events);
        }

        if (events == 0)
        {
            return 0;
        }

        if (into.Length < events * 2)
        {
            into = new int[Math.Max(events * 2, into.Length * 2)];
        }

        // Back from the line's end: an opening is unmatched when the depth after it never comes back to where it was before
        // it (known once every line was read: the least depth from the next line to the file's end).
        bool known = IsComplete && EnsureSuffix();
        int least = end >> 1;
        if (known && line + 1 < _count)
        {
            least = Math.Min(least, _suffix[line + 1]);
        }

        for (int e = events - 1; e >= 0; e--)
        {
            int index = _events[3 * e];
            int kind = _events[(3 * e) + 1];                      // 0 open, 1 close, 2 a close with no opening
            int level = _events[(3 * e) + 2];
            uint color;
            if (kind == 2)
            {
                color = colors.BracketUnmatched;
            }
            else if (kind == 0)
            {
                color = known && least > level - 1 ? colors.BracketUnmatched : colors.BracketDepth(level);
                least = Math.Min(least, level);
            }
            else
            {
                color = colors.BracketDepth(level);
                least = Math.Min(least, level - 1);
            }

            into[2 * e] = index;
            into[(2 * e) + 1] = unchecked((int)color);
        }

        return events;
    }

    /// <summary>A change of the text: the states of the lines it touched are spliced; the lines after it are read again until one starts as it did.</summary>
    internal void OnBufferChanged(TextChangedEventArgs change)
    {
        if (_count == 0)
        {
            return;
        }

        LineEdits.Splices(change, _splices);
        if (_splices.Count == 0)
        {
            return;
        }

        int oldValid = _valid;
        int stateCount = _count;
        int leastCount = _count;
        for (int i = _splices.Count - 1; i >= 0; i--)
        {
            var (first, removed, added) = _splices[i];
            LineEdits.Splice(ref _state, ref stateCount, first, removed, added, 0);
            LineEdits.Splice(ref _least, ref leastCount, first, removed, added, 0);
        }

        if (stateCount != change.After.LineCount)
        {
            _count = 0;                                             // never expected: start again from the top
            return;
        }

        _count = stateCount;
        if (_state.Length < _count + 1)
        {
            Array.Resize(ref _state, _count + 1);
        }

        if (_least.Length < _count + 1)
        {
            Array.Resize(ref _least, _count + 1);
        }

        var (lastFirst, lastRemoved, lastAdded) = _splices[^1];
        int delta = _count - change.Before.LineCount;
        int firstChanged = _splices[0].First;
        _valid = Math.Min(_valid, firstChanged);
        int tailFrom = lastFirst + lastAdded + 1;
        int tailTo = oldValid > lastFirst + lastRemoved ? oldValid + delta : -1;
        _tailFrom = tailTo >= tailFrom ? tailFrom : -1;
        _tailTo = tailTo >= tailFrom ? tailTo : -1;
        _suffixValid = false;
    }

    /// <summary>Forgets every state (the options or the language changed).</summary>
    internal void Release()
    {
        _count = 0;
        _valid = 0;
        _tailFrom = _tailTo = -1;
        _suffixValid = false;
    }

    private void Prepare()
    {
        var rules = _model.Rules;
        if (!ReferenceEquals(rules, _rules))
        {
            _rules = rules;
            _interesting = Interesting(rules);
            _count = 0;
        }

        int lines = _model.Snapshot.LineCount;
        if (_count != lines)
        {
            _count = lines;
            if (_state.Length < lines + 1)
            {
                _state = new int[Math.Max(lines + 1, _state.Length * 2)];
                _least = new int[_state.Length];
            }

            _state[0] = 0;
            _valid = 0;
            _tailFrom = _tailTo = -1;
            _suffixValid = false;
        }
    }

    // The least depth from each line's start to the file's end, in one pass back (rule 73), once every line is read.
    private bool EnsureSuffix()
    {
        if (_suffixValid)
        {
            return true;
        }

        if (_suffix.Length < _count + 1)
        {
            _suffix = new int[Math.Max(_count + 1, _suffix.Length * 2)];
        }

        var snapshot = _model.Snapshot;
        int last = _count - 1;
        char[]? buffer = null;
        int end;
        try
        {
            end = Scan(TextReading.ReadLine(snapshot, snapshot.GetLine(last), ref buffer), _state[last], out int lastLeast, Span<int>.Empty, out _);
            _least[last] = lastLeast;
        }
        finally
        {
            TextReading.Return(buffer);
        }

        int running = end >> 1;
        _suffix[_count] = running;
        for (int line = last; line >= 0; line--)
        {
            running = Math.Min(running, _least[line]);
            _suffix[line] = running;
        }

        _suffixValid = true;
        return true;
    }

    // One line from `state` (depth << 1 | in a block comment): the state at its end, the least depth it reached, and its
    // brackets as (index, kind, level) triples while `events` has room (`eventCount` counts them all).
    private int Scan(ReadOnlySpan<char> text, int state, out int least, Span<int> events, out int eventCount)
    {
        var rules = _rules ?? _model.Rules;
        var interesting = _interesting ??= Interesting(rules);
        int depth = state >> 1;
        bool inComment = (state & 1) != 0;
        least = depth;
        eventCount = 0;
        string? lineComment = rules.LineComment;
        string? open = rules.BlockCommentOpen;
        string? close = rules.BlockCommentClose;
        string delimiters = rules.StringDelimiters;
        char escape = rules.StringEscape;
        var brackets = rules.Brackets;
        int i = 0;
        while (i < text.Length)
        {
            if (inComment)
            {
                int end = close is null ? -1 : text[i..].IndexOf(close.AsSpan(), StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                i += end + close!.Length;
                inComment = false;
                continue;
            }

            int next = text[i..].IndexOfAny(interesting);
            if (next < 0)
            {
                break;
            }

            i += next;
            var rest = text[i..];
            if (lineComment is not null && rest.StartsWith(lineComment.AsSpan(), StringComparison.Ordinal))
            {
                break;
            }

            if (open is not null && close is not null && rest.StartsWith(open.AsSpan(), StringComparison.Ordinal))
            {
                inComment = true;
                i += open.Length;
                continue;
            }

            char c = text[i];
            if (delimiters.Length > 0 && delimiters.Contains(c, StringComparison.Ordinal))
            {
                int j = i + 1;
                while (j < text.Length && text[j] != c)
                {
                    j += escape != '\0' && text[j] == escape ? 2 : 1;
                }

                i = j + 1;
                continue;
            }

            for (int b = 0; b < brackets.Count; b++)
            {
                var pair = brackets[b];
                if (c == pair.Open)
                {
                    depth++;
                    Record(events, ref eventCount, i, 0, depth);
                    break;
                }

                if (c == pair.Close)
                {
                    if (depth > 0)
                    {
                        Record(events, ref eventCount, i, 1, depth);
                        depth--;
                        least = Math.Min(least, depth);
                    }
                    else
                    {
                        Record(events, ref eventCount, i, 2, 0);
                    }

                    break;
                }
            }

            i++;
        }

        return (depth << 1) | (inComment ? 1 : 0);
    }

    private static void Record(Span<int> events, ref int count, int index, int kind, int level)
    {
        if ((count * 3) + 2 < events.Length)
        {
            events[count * 3] = index;
            events[(count * 3) + 1] = kind;
            events[(count * 3) + 2] = level;
        }

        count++;
    }

    private static SearchValues<char> Interesting(LanguageRules rules)
    {
        var chars = new HashSet<char>();
        foreach (var pair in rules.Brackets)
        {
            chars.Add(pair.Open);
            chars.Add(pair.Close);
        }

        foreach (char c in rules.StringDelimiters)
        {
            chars.Add(c);
        }

        if (rules.LineComment is { Length: > 0 } line)
        {
            chars.Add(line[0]);
        }

        if (rules.BlockCommentOpen is { Length: > 0 } block && rules.BlockCommentClose is { Length: > 0 })
        {
            chars.Add(block[0]);
        }

        return SearchValues.Create([.. chars]);
    }
}
