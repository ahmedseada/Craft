// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// One line's wrap (X part 9): where each of its rows starts (a character index and the line's column there), the
/// indent its continuation rows keep, and the line's text it was made from (so it is kept while the line stays the same,
/// whatever its version). Rows break after white space where they can, inside a word only when a word is wider than a
/// row; white space at a row's end may run past the width (it is never moved to the next row's start).
/// </summary>
public sealed class WrappedLine
{
    private readonly int[] _starts;
    private readonly int[] _columns;

    private WrappedLine(char[] source, int width, int tabSize, int indent, int[] starts, int[] columns, int count, int lineColumns)
    {
        Source = source;
        Width = width;
        TabSize = tabSize;
        Indent = indent;
        _starts = starts;
        _columns = columns;
        Rows = count;
        Columns = lineColumns;
    }

    /// <summary>The rows it takes (1 when it fits).</summary>
    public int Rows { get; }

    /// <summary>The width it was wrapped to, in columns.</summary>
    public int Width { get; }

    /// <summary>The tab size it was wrapped with.</summary>
    public int TabSize { get; }

    /// <summary>The columns a continuation row is indented by (the line's own indent, when it is at most half the width).</summary>
    public int Indent { get; }

    /// <summary>The line's width in columns, unwrapped.</summary>
    public int Columns { get; }

    internal char[] Source { get; }

    /// <summary>The character index where <paramref name="row"/> starts.</summary>
    public int RowStart(int row) => _starts[Math.Clamp(row, 0, Rows - 1)];

    /// <summary>The character index where <paramref name="row"/> ends (the next row's start, or the line's length).</summary>
    public int RowEnd(int row) => row + 1 < Rows ? _starts[row + 1] : Source.Length;

    /// <summary>The line's column (tabs expanded, unwrapped) where <paramref name="row"/> starts.</summary>
    public int StartColumn(int row) => _columns[Math.Clamp(row, 0, Rows - 1)];

    /// <summary>The column a row's text starts at: 0 for the first, <see cref="Indent"/> for a continuation.</summary>
    public int RowIndent(int row) => row == 0 ? 0 : Indent;

    /// <summary>The row holding character <paramref name="index"/> (a row's start index belongs to that row; the line's end to the last).</summary>
    public int RowOf(int index)
    {
        int low = 0, high = Rows - 1;
        while (low < high)
        {
            int middle = (low + high + 1) >>> 1;
            if (_starts[middle] <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The row holding the character before <paramref name="end"/> (where a range ending there is drawn last).</summary>
    public int RowOfEnd(int end) => end <= 0 ? 0 : RowOf(Math.Min(end, Source.Length) - 1);

    /// <summary>The visual column on <paramref name="row"/> of the line's column <paramref name="column"/>.</summary>
    public int VisualColumn(int row, int column) => RowIndent(row) + column - StartColumn(row);

    /// <summary>The visual width of <paramref name="row"/> in columns (its indent included).</summary>
    public int RowWidth(int row) => RowIndent(row) + (row + 1 < Rows ? _columns[row + 1] : Columns) - StartColumn(row);

    /// <summary>Whether it was made from <paramref name="text"/> at <paramref name="width"/> and <paramref name="tabSize"/>.</summary>
    internal bool Fits(ReadOnlySpan<char> text, int width, int tabSize) => width == Width && tabSize == TabSize && text.SequenceEqual(Source);

    /// <summary>Wraps <paramref name="text"/> to <paramref name="width"/> columns.</summary>
    public static WrappedLine Wrap(ReadOnlySpan<char> text, int width, int tabSize)
    {
        width = Math.Max(1, width);
        tabSize = Math.Max(1, tabSize);
        int lead = TextReading.IndentLength(text);
        int indentColumns = lead < text.Length ? TextReading.ColumnOf(text, lead, tabSize) : 0;
        int indent = indentColumns <= width / 2 ? indentColumns : 0;
        var starts = new List<int>(4) { 0 };
        var columns = new List<int>(4) { 0 };
        int rowStart = 0;
        int rowStartColumn = 0;
        int rowIndent = 0;
        int column = 0;
        int lastBreak = -1;
        int lastBreakColumn = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int next = TextReading.Advance(column, c, tabSize);
            bool space = c is ' ' or '\t';
            if (!space && !char.IsLowSurrogate(c) && i > rowStart && rowIndent + (next - rowStartColumn) > width)
            {
                // A break: after the last white space of the row, else before this character.
                int at = lastBreak > rowStart ? lastBreak : i;
                int atColumn = lastBreak > rowStart ? lastBreakColumn : column;
                starts.Add(at);
                columns.Add(atColumn);
                rowStart = at;
                rowStartColumn = atColumn;
                rowIndent = indent;
                lastBreak = -1;
                i = at;
                column = atColumn;
                continue;
            }

            if (space)
            {
                lastBreak = i + 1;
                lastBreakColumn = next;
            }

            column = next;
            i++;
        }

        return new WrappedLine(text.ToArray(), width, tabSize, indent, [.. starts], [.. columns], starts.Count, column);
    }
}

/// <summary>
/// Word wrap for one editor (X part 9, <c>CRAFT_WORD_WRAP</c>, Alt+Z): the rows each line takes and the map between rows
/// and lines that the layout, the paint, the hit tests and the caret's moves follow. Only lines that are shown (or that a
/// caret is on) are wrapped, each kept by its text, width and tab size, so a frame of a long file wraps nothing it does not
/// draw and a warm frame wraps nothing; a line never shown counts one row until it is. The rows of all lines are a binary
/// indexed tree (a folded line counts none), so a line's row and a row's line are each one walk of its height (rule 73),
/// and an edit splices the counts of the lines it touched. Used on the UI thread.
/// </summary>
public sealed class WordWrap
{
    /// <summary>The fewest columns a row is wrapped to (a degenerate narrow window does not make a row a character).</summary>
    public const int MinimumWidth = 8;

    private readonly EditorModel _model;
    private readonly Dictionary<int, WrappedLine> _wrapped = [];
    private readonly List<(int First, int Removed, int Added)> _splices = [];
    private int[] _rows = [];
    private int[] _tree = [];
    private int _lines;
    private int _width;
    private int _tabSize;
    private int _foldsStamp = -1;
    private int _total;
    private int _reportedTotal = -1;
    private string? _mode;
    private int _topLine = -1;
    private int _topRow;
    private float _topFraction;

    internal WordWrap(EditorModel model) => _model = model;

    /// <summary>The mode in effect: "off", "on" or "column" (the options', or what Alt+Z set for this editor).</summary>
    public string Mode => _mode ?? _model.Options.WordWrap;

    /// <summary>Whether lines wrap now (on, and the editor has a width to wrap to).</summary>
    public bool IsActive => Sync();

    /// <summary>The width rows wrap to, in columns (0 when off).</summary>
    public int Width => Sync() ? _width : 0;

    /// <summary>The lines whose wrap it keeps now (those on screen and near them).</summary>
    public int LinesHeld => _wrapped.Count;

    /// <summary>The wraps it computed since it was made (a warm frame computes none).</summary>
    public long LinesWrapped { get; private set; }

    /// <summary>The rows of every line, folded lines counting none.</summary>
    public int RowCount => Sync() ? _total : 0;

    /// <summary>The rows <paramref name="line"/> takes (1 when it is not wrapped yet or wrap is off).</summary>
    public int RowsOf(int line) => Sync() && (uint)line < (uint)_lines ? _rows[line] : 1;

    /// <summary>Switches wrap on (in the options' wrapping mode, else at the width) or off for this editor (Alt+Z).</summary>
    internal void Toggle()
    {
        string options = _model.Options.WordWrap;
        _mode = Mode != "off" ? "off" : options != "off" ? options : "on";
        Release();
    }

    /// <summary>The first row of <paramref name="line"/> (its fold's header's when folded away).</summary>
    internal int RowOfLine(int line)
    {
        line = Math.Clamp(line, 0, _lines - 1);
        var map = _model.Folding.Map;
        if (map.HasHidden && map.IsHidden(line))
        {
            line = map.LineAt(map.RowOf(line));
        }

        return Prefix(line);
    }

    /// <summary>The line drawn on <paramref name="row"/>, and which of its rows that is.</summary>
    internal int LineAtRow(int row, out int subRow)
    {
        if (_total <= 0)
        {
            subRow = 0;
            return 0;
        }

        if (row >= _total)
        {
            int last = LastShown();
            subRow = _rows[last] - 1;
            return last;
        }

        row = Math.Max(0, row);
        int position = 0;
        int rest = row;
        for (int step = HighestBit(_lines); step > 0; step >>= 1)
        {
            int next = position + step;
            if (next <= _lines && _tree[next] <= rest)
            {
                position = next;
                rest -= _tree[next];
            }
        }

        subRow = rest;
        return Math.Min(position, _lines - 1);
    }

    /// <summary>The wrap of <paramref name="line"/> (made now when its text, the width or the tab size changed), or null when wrap is off.</summary>
    public WrappedLine? Line(int line)
    {
        if (!Sync() || (uint)line >= (uint)_lines)
        {
            return null;
        }

        var snapshot = _model.Snapshot;
        char[]? buffer = null;
        try
        {
            return Line(line, TextReading.ReadLine(snapshot, snapshot.GetLine(line), ref buffer));
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>The wrap of <paramref name="line"/>, whose text the caller read (<paramref name="text"/>), or null when wrap is off.</summary>
    internal WrappedLine? Line(int line, ReadOnlySpan<char> text)
    {
        if (!Sync() || (uint)line >= (uint)_lines)
        {
            return null;
        }

        if (!_wrapped.TryGetValue(line, out var wrapped) || !wrapped.Fits(text, _width, _tabSize))
        {
            wrapped = WrappedLine.Wrap(text, _width, _tabSize);
            _wrapped[line] = wrapped;
            LinesWrapped++;
        }

        if (_rows[line] != wrapped.Rows)
        {
            int delta = wrapped.Rows - _rows[line];
            _rows[line] = wrapped.Rows;
            if (!IsFoldedAway(line))
            {
                Add(line, delta);
            }
        }

        return wrapped;
    }

    /// <summary>
    /// Before a frame: follows the width (a new one wraps again, keeping the line that was at the top where it was), wraps the
    /// lines the viewport shows until their rows stop moving, and notes the top line for the next frame. Returns the scroll
    /// offset the frame draws at (changed only when the rows were wrapped again).
    /// </summary>
    internal float PrepareFrame(float scrollY, float viewportHeight)
    {
        bool reset = _lines > 0 && WidthNow() != _width;
        if (!Sync())
        {
            NoteTop(scrollY);
            return scrollY;
        }

        float height = _model.Metrics.LineHeight;
        if (reset && _topLine >= 0 && _topLine < _lines)
        {
            // The rows were wrapped again (another width): the line that was at the top stays there.
            var top = Line(_topLine);
            scrollY = ((RowOfLine(_topLine) + Math.Min(_topRow, (top?.Rows ?? 1) - 1)) * height) + _topFraction;
        }

        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            var rows = _model.VisibleRows(scrollY, viewportHeight);
            int line = LineAtRow(rows.First, out _);
            int row = RowOfLine(line);
            while (line < _lines && row <= rows.Last)
            {
                int before = _rows[line];
                int now = Line(line)?.Rows ?? 1;
                changed |= now != before;
                row += now;
                line = _model.NextShownLine(line);
            }

            if (!changed)
            {
                break;
            }
        }

        NoteTop(scrollY);
        if (_total != _reportedTotal)
        {
            _reportedTotal = _total;
            _model.Post(_model.RaiseContentSizeChanged);
        }

        return scrollY;
    }

    /// <summary>Drops the wraps of lines far from <paramref name="visible"/> (kept: the lines on screen and <paramref name="margin"/> above and below).</summary>
    internal void Trim(LineRange visible, int margin)
    {
        margin = Math.Max(0, margin);
        int keep = visible.Count + (2 * margin);
        if (_wrapped.Count <= keep + (keep / 2) + 8)
        {
            return;
        }

        int first = visible.First - margin;
        int last = visible.Last + margin;
        int caret = _model.CaretLine;
        foreach (var key in _wrapped.Keys)
        {
            if ((key < first || key > last) && key != caret)
            {
                _wrapped.Remove(key);
            }
        }
    }

    /// <summary>A change of the text: the row counts of the lines it touched are spliced (each touched line wraps again when shown).</summary>
    internal void OnBufferChanged(TextChangedEventArgs change)
    {
        if (_lines == 0)
        {
            return;
        }

        if (change.After.LineCount == change.Before.LineCount && change.Edits.Count > 0)
        {
            bool sameLines = true;
            foreach (var edit in change.Edits)
            {
                sameLines &= edit.Text.AsSpan().IndexOfAny('\r', '\n') < 0
                    && change.Before.GetLineIndex(Math.Min(edit.End, change.Before.Length)) == change.Before.GetLineIndex(Math.Min(edit.Start, change.Before.Length));
            }

            if (sameLines)
            {
                return;                                             // the same lines: each wraps again when it is next shown
            }
        }

        LineEdits.Splices(change, _splices);
        int count = _lines;
        for (int i = _splices.Count - 1; i >= 0; i--)
        {
            var (first, removed, added) = _splices[i];
            LineEdits.Splice(ref _rows, ref count, first, removed, added, 1);
        }

        if (count != change.After.LineCount)
        {
            Release();                                              // never expected: start again from one row a line
            return;
        }

        _lines = count;
        Build();
    }

    /// <summary>Forgets every wrap and count (wrap went off, or the counts cannot be followed); the next use starts again.</summary>
    internal void Release()
    {
        _lines = 0;
        _width = 0;
        _total = 0;
        _wrapped.Clear();
        _model.RaiseContentSizeChanged();
        _model.Invalidate();
    }

    // Whether wrap is on, keeping the counts in step with the width, the tab size, the text's lines and the folds.
    private bool Sync()
    {
        int width = WidthNow();
        if (width <= 0)
        {
            if (_lines > 0)
            {
                _lines = 0;
                _width = 0;
                _total = 0;
                _wrapped.Clear();
            }

            return false;
        }

        int lines = _model.Snapshot.LineCount;
        int tabSize = _model.Options.TabSize;
        if (_lines != lines || width != _width || tabSize != _tabSize)
        {
            _lines = lines;
            _width = width;
            _tabSize = tabSize;
            if (_rows.Length < lines)
            {
                _rows = new int[Math.Max(lines, _rows.Length * 2)];
            }

            _rows.AsSpan(0, lines).Fill(1);
            _wrapped.Clear();
            Build();
        }
        else if (_foldsStamp != _model.FoldsStamp)
        {
            Build();
        }

        return true;
    }

    private int WidthNow()
    {
        string mode = Mode;
        if (mode == "off")
        {
            return 0;
        }

        if (mode == "column")
        {
            return Math.Max(MinimumWidth, _model.Options.WordWrapColumn);
        }

        float viewport = _model.ViewportWidth;
        float cell = _model.Metrics.CellWidth;
        if (viewport <= 0 || cell <= 0)
        {
            return 0;
        }

        float text = viewport - _model.TextLeft - _model.OverviewRulerWidth - cell;
        return Math.Max(MinimumWidth, (int)MathF.Floor(text / cell));
    }

    // The tree from the counts in O(lines): each node adds itself to its parent once; folded lines count none.
    private void Build()
    {
        _foldsStamp = _model.FoldsStamp;
        int n = _lines;
        if (_tree.Length < n + 1)
        {
            _tree = new int[Math.Max(n + 1, _tree.Length * 2)];
        }

        _rows.AsSpan(0, n).CopyTo(_tree.AsSpan(1, n));
        var map = _model.Folding.Map;
        for (int k = 0; k < map.IntervalCount; k++)
        {
            var (first, last) = map.Interval(k);
            for (int line = Math.Max(0, first); line <= last && line < n; line++)
            {
                _tree[line + 1] = 0;
            }
        }

        long total = 0;
        foreach (int rows in _tree.AsSpan(1, n))
        {
            total += rows;
        }

        for (int i = 1; i <= n; i++)
        {
            int parent = i + (i & -i);
            if (parent <= n)
            {
                _tree[parent] += _tree[i];
            }
        }

        _total = (int)Math.Min(int.MaxValue, total);
    }

    private void Add(int line, int delta)
    {
        for (int i = line + 1; i <= _lines; i += i & -i)
        {
            _tree[i] += delta;
        }

        _total += delta;
    }

    // The rows of the lines before `line`.
    private int Prefix(int line)
    {
        int sum = 0;
        for (int i = Math.Min(line, _lines); i > 0; i -= i & -i)
        {
            sum += _tree[i];
        }

        return sum;
    }

    private bool IsFoldedAway(int line)
    {
        var map = _model.Folding.Map;
        return map.HasHidden && map.IsHidden(line);
    }

    private int LastShown()
    {
        int last = _lines - 1;
        var map = _model.Folding.Map;
        while (last > 0 && map.HasHidden && map.IsHidden(last))
        {
            last--;
        }

        return last;
    }

    private void NoteTop(float scrollY)
    {
        float height = _model.Metrics.LineHeight;
        int row = Math.Max(0, (int)MathF.Floor(scrollY / height));
        _topFraction = scrollY - (row * height);
        if (_lines > 0)
        {
            _topLine = LineAtRow(row, out _topRow);
        }
        else
        {
            _topLine = _model.LineAtRow(row);
            _topRow = 0;
        }
    }

    private static int HighestBit(int n)
    {
        int bit = 1;
        while (bit <= n >> 1)
        {
            bit <<= 1;
        }

        return bit;
    }
}
