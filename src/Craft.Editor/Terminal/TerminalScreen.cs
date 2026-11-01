// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;

namespace Craft.Editor.Terminal;

/// <summary>Bytes the terminal answers the shell with (a cursor position report, the device attributes).</summary>
public delegate void TerminalResponder(ReadOnlySpan<byte> reply);

/// <summary>The cursor's shape (DECSCUSR).</summary>
public enum TerminalCursorStyle
{
    /// <summary>A block over the cell.</summary>
    Block,

    /// <summary>A line under the cell.</summary>
    Underline,

    /// <summary>A bar at the cell's left.</summary>
    Bar,
}

/// <summary>
/// One row of the terminal: its cells (as wide as the screen was when it was last written; a cell past them is blank),
/// whether it wrapped into the next row, and a version that changes with every write (the renderer's runs are kept while
/// it does not).
/// </summary>
public sealed class TerminalRow
{
    internal TerminalCell[] Cells;

    internal TerminalRow(int columns, TerminalCell blank)
    {
        Cells = new TerminalCell[columns];
        Cells.AsSpan().Fill(blank);
    }

    /// <summary>Changes with every write to the row.</summary>
    public int Version { get; internal set; }

    /// <summary>Whether the text runs on into the next row (an automatic wrap, not a line break).</summary>
    public bool Wrapped { get; internal set; }

    /// <summary>The cells it holds.</summary>
    public int Length => Cells.Length;

    /// <summary>The renderer's runs of the row and the version and palette they were made for (null when none are kept).</summary>
    internal object? Runs { get; set; }

    internal int RunsVersion { get; set; }

    internal int RunsPalette { get; set; }

    internal long DrawnFrame { get; set; }

    /// <summary>The cell at <paramref name="column"/> (a blank one past the row's end).</summary>
    public TerminalCell this[int column] => (uint)column < (uint)Cells.Length ? Cells[column] : default;

    /// <summary>The row's text, its trailing blanks left out (a wide character's second cell adds nothing).</summary>
    public string Text() => Text(0, Cells.Length);

    /// <summary>The text of the cells <paramref name="start"/> to <paramref name="end"/> (exclusive), trailing blanks left out.</summary>
    public string Text(int start, int end)
    {
        end = Math.Min(end, Cells.Length);
        while (end > start && Cells[end - 1].IsBlank && !Cells[end - 1].IsWideTail)
        {
            end--;
        }

        var text = new StringBuilder(Math.Max(0, end - start));
        for (int i = Math.Max(0, start); i < end; i++)
        {
            var cell = Cells[i];
            if (cell.IsWideTail)
            {
                continue;
            }

            int code = cell.CodePoint;
            if (code == 0)
            {
                text.Append(' ');
            }
            else if (Rune.IsValid(code))
            {
                text.Append(new Rune(code).ToString());
            }
        }

        return text.ToString();
    }

    internal void Touch()
    {
        unchecked
        {
            Version++;
        }
    }

    /// <summary>Makes the row <paramref name="columns"/> blank cells wide (reusing its array when it already is).</summary>
    internal void Reset(int columns, TerminalCell blank)
    {
        if (Cells.Length != columns)
        {
            Cells = new TerminalCell[columns];
        }

        Cells.AsSpan().Fill(blank);
        Wrapped = false;
        Touch();
    }

    /// <summary>Makes the row <paramref name="columns"/> wide, keeping its cells (a wide character cut in half is blanked).</summary>
    internal void Fit(int columns)
    {
        if (Cells.Length == columns)
        {
            return;
        }

        int old = Cells.Length;
        var cells = Cells;
        Array.Resize(ref cells, columns);
        Cells = cells;
        if (columns > old)
        {
            Cells.AsSpan(old).Fill(TerminalCell.Blank(TerminalColor.Default));
        }
        else if (columns > 0 && Cells[columns - 1].IsWide)
        {
            Cells[columns - 1] = TerminalCell.Blank(TerminalColor.Default);
        }

        Touch();
    }
}

/// <summary>
/// A terminal's screen: a grid of <see cref="Columns"/> by <see cref="Rows"/> cells over a ring of
/// <c>CRAFT_TERMINAL_SCROLLBACK</c> lines above it (a full-screen scroll moves the ring's start, nothing is copied; once
/// full, the oldest line's row is reused for the new one, so memory stays flat), the alternate screen (1049, no
/// scrollback), the cursor, the renditions, scroll regions, tab stops and the modes shells set. Bytes from the shell are
/// parsed by <see cref="Feed"/> (UTF-8 and the VT sequences; see the parser's part). Not thread-safe: the reader thread
/// feeds it and the UI thread draws it, each holding <see cref="Sync"/>.
/// </summary>
public sealed partial class TerminalScreen
{
    private TerminalRow[] _ring = [];
    private int _first;
    private int _count;
    private long _dropped;
    private TerminalRow[] _alt = [];
    private bool _altActive;
    private int _scrollback;
    private int _row;
    private int _col;
    private bool _pendingWrap;
    private TerminalAttributes _attr;
    private uint _fg;
    private uint _bg;
    private bool _g0Graphics;
    private bool _g1Graphics;
    private bool _shifted;
    private SavedCursor _savedMain;
    private SavedCursor _savedAlt;
    private int _top;
    private int _bottom;
    private bool[] _tabs = [];
    private int _lastPrinted = ' ';

    /// <summary>A screen of <paramref name="columns"/> by <paramref name="rows"/> keeping <paramref name="scrollback"/> lines above it.</summary>
    public TerminalScreen(int columns, int rows, int scrollback)
    {
        Columns = Math.Clamp(columns, 1, 10_000);
        Rows = Math.Clamp(rows, 1, 10_000);
        _scrollback = Math.Max(0, scrollback);
        _ring = new TerminalRow[_scrollback + Rows];
        for (int i = 0; i < Rows; i++)
        {
            _ring[i] = new TerminalRow(Columns, Blank);
        }

        _count = Rows;
        _alt = new TerminalRow[Rows];
        for (int i = 0; i < Rows; i++)
        {
            _alt[i] = new TerminalRow(Columns, Blank);
        }

        _bottom = Rows - 1;
        ResetTabs();
        _savedMain = _savedAlt = new SavedCursor(0, 0, TerminalAttributes.None, 0, 0, false, false, false, false, false);
    }

    /// <summary>What the reader and the renderer hold while they use the screen.</summary>
    public Lock Sync { get; } = new();

    /// <summary>The cells of a row.</summary>
    public int Columns { get; private set; }

    /// <summary>The rows of the screen.</summary>
    public int Rows { get; private set; }

    /// <summary>The lines kept above the screen at most.</summary>
    public int Scrollback
    {
        get => _scrollback;
        set
        {
            int scrollback = Math.Max(0, value);
            if (scrollback != _scrollback)
            {
                _scrollback = scrollback;
                Reshape(_scrollback + Rows);
            }
        }
    }

    /// <summary>The lines above the screen now.</summary>
    public int ScrollbackLines => _altActive ? 0 : _count - Rows;

    /// <summary>The number every line has had (an absolute line number) of the screen's first row.</summary>
    public long ScreenTop => _dropped + _count - Rows;

    /// <summary>The absolute line number of the oldest line kept.</summary>
    public long FirstLine => _altActive ? ScreenTop : _dropped;

    /// <summary>The cursor's row (0 at the top).</summary>
    public int CursorRow => _row;

    /// <summary>The cursor's column (0 at the left).</summary>
    public int CursorColumn => _col;

    /// <summary>Whether the cursor shows (DECTCEM, mode 25).</summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>The cursor's shape (DECSCUSR; <see cref="DefaultCursorStyle"/> until the shell asks for one).</summary>
    public TerminalCursorStyle CursorStyle { get; private set; }

    /// <summary>
    /// The cursor's shape when the shell asks for none, or for its default (DECSCUSR 0) or resets (B9: <c>CRAFT_TERMINAL_CURSOR</c>).
    /// Setting it changes the cursor now unless the shell chose a shape.
    /// </summary>
    public TerminalCursorStyle DefaultCursorStyle
    {
        get => _defaultCursorStyle;
        set
        {
            _defaultCursorStyle = value;
            if (!_cursorStyleChosen)
            {
                CursorStyle = value;
            }
        }
    }

    /// <summary>
    /// Whether a command runs, as a shell that marks its prompts and commands says (OSC 133: C starts a command, D or A
    /// ends it); null while the shell has marked none (the tab's busy dot then follows the output, B9).
    /// </summary>
    public bool? CommandRunning { get; private set; }

    /// <summary>Whether the alternate screen is shown (mode 1049, 1047 or 47).</summary>
    public bool IsAlternate => _altActive;

    /// <summary>Whether the arrows send their application form (DECCKM, mode 1).</summary>
    public bool ApplicationCursorKeys { get; private set; }

    /// <summary>Whether the keypad is in application mode (DECKPAM).</summary>
    public bool ApplicationKeypad { get; private set; }

    /// <summary>Whether a paste is bracketed (mode 2004).</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>Whether text wraps at the right margin (DECAWM, mode 7).</summary>
    public bool AutoWrap { get; private set; } = true;

    /// <summary>The mouse tracking mode the shell asked for (1000, 1002, 1003; 0 for none). Kept, not reported yet.</summary>
    public int MouseTracking { get; private set; }

    /// <summary>The title the shell set (OSC 0 or 2), or null.</summary>
    public string? Title { get; private set; }

    /// <summary>The working folder the shell reported (OSC 7, or OSC 9;9), or null.</summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>How many times the shell rang the bell.</summary>
    public int Bells { get; private set; }

    /// <summary>Changes with every feed that changed what is shown (the view redraws when it moved).</summary>
    public long Changes { get; private set; }

    /// <summary>Lines pushed into the scrollback since the start (a view scrolled back keeps its place by it).</summary>
    public long LinesScrolled { get; private set; }

    /// <summary>Where the replies to the shell's queries go (the session's input), or null to drop them.</summary>
    public TerminalResponder? Respond { get; set; }

    /// <summary>Raised (on the feeding thread, inside <see cref="Feed"/>) when the title or the working folder changed.</summary>
    public event EventHandler? TitleChanged;

    private TerminalCell Blank => TerminalCell.Blank(_bg);

    private int Capacity => _ring.Length;

    /// <summary>The screen's row <paramref name="row"/> (0 at the top).</summary>
    public TerminalRow ScreenRow(int row) => _altActive ? _alt[row] : _ring[(_first + _count - Rows + row) % Capacity];

    /// <summary>The line of absolute number <paramref name="line"/> (the scrollback's, or the screen's), or null when it is not kept.</summary>
    public TerminalRow? LineAt(long line)
    {
        long top = ScreenTop;
        if (_altActive)
        {
            return line >= top && line < top + Rows ? _alt[(int)(line - top)] : null;
        }

        long index = line - _dropped;
        return index >= 0 && index < _count ? _ring[(int)((_first + index) % Capacity)] : null;
    }

    /// <summary>The text of the screen's row <paramref name="row"/>, trailing blanks left out.</summary>
    public string RowText(int row) => ScreenRow(row).Text();

    /// <summary>The cell at the screen's <paramref name="row"/> and <paramref name="column"/>.</summary>
    public TerminalCell CellAt(int row, int column) => ScreenRow(row)[column];

    /// <summary>
    /// The text from <paramref name="startLine"/>, <paramref name="startColumn"/> to <paramref name="endLine"/>,
    /// <paramref name="endColumn"/> (absolute lines, the end exclusive): lines joined by "\n" unless one wrapped into the
    /// next, each line's trailing blanks left out.
    /// </summary>
    public string TextOf(long startLine, int startColumn, long endLine, int endColumn)
    {
        if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
        {
            (startLine, startColumn, endLine, endColumn) = (endLine, endColumn, startLine, startColumn);
        }

        var text = new StringBuilder();
        for (long line = Math.Max(startLine, FirstLine); line <= endLine; line++)
        {
            if (LineAt(line) is not { } row)
            {
                break;
            }

            int from = line == startLine ? startColumn : 0;
            int to = line == endLine ? endColumn : row.Length;
            text.Append(row.Text(from, to));
            if (line < endLine && !row.Wrapped)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Makes the screen <paramref name="columns"/> by <paramref name="rows"/>. Narrower rows are cut (they do not reflow);
    /// fewer rows drop the blank rows under the cursor first, then move the top rows into the scrollback; more rows bring
    /// lines back from the scrollback first. The cursor stays on its text; the scroll region becomes the whole screen.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Clamp(columns, 1, 10_000);
        rows = Math.Clamp(rows, 1, 10_000);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        if (columns != Columns)
        {
            for (int r = 0; r < Rows; r++)
            {
                MainRow(r).Fit(columns);
                _alt[r].Fit(columns);
            }

            Columns = columns;
            ResetTabs();
        }

        if (rows != Rows)
        {
            ResizeRows(rows);
        }

        _row = Math.Clamp(_row, 0, Rows - 1);
        _col = Math.Clamp(_col, 0, Columns - 1);
        _top = 0;
        _bottom = Rows - 1;
        _pendingWrap = false;
        Changes++;
    }

    /// <summary>
    /// The Clear command (not a shell's erase): the scrollback is dropped and the cursor's row moves to the top with the
    /// rest of the screen blank, so the prompt stays where the user types.
    /// </summary>
    public void Clear()
    {
        if (_altActive)
        {
            for (int r = 0; r < Rows; r++)
            {
                _alt[r].Reset(Columns, Blank);
            }

            _row = 0;
            Changes++;
            return;
        }

        var keep = MainRow(_row);
        var rows = new TerminalRow[_scrollback + Rows];
        rows[0] = keep;
        keep.Touch();
        int next = 1;
        for (int r = 0; r < Rows && next < Rows; r++)
        {
            var row = MainRow(r);
            if (!ReferenceEquals(row, keep))
            {
                row.Reset(Columns, Blank);
                rows[next++] = row;
            }
        }

        _dropped += _count;
        _ring = rows;
        _first = 0;
        _count = Rows;
        _row = 0;
        Changes++;
    }

    /// <summary>The full reset (RIS): the screen and the scrollback blank, every mode at its default.</summary>
    public void Reset()
    {
        _altActive = false;
        _attr = TerminalAttributes.None;
        _fg = _bg = TerminalColor.Default;
        _dropped += _count;
        _first = 0;
        for (int r = 0; r < Rows; r++)
        {
            (_ring[r] ??= new TerminalRow(Columns, Blank)).Reset(Columns, Blank);
            _alt[r].Reset(Columns, Blank);
        }

        _count = Rows;
        SoftReset();
        _row = _col = 0;
        Title = null;
        Changes++;
    }

    private void SoftReset()
    {
        _attr = TerminalAttributes.None;
        _fg = _bg = TerminalColor.Default;
        _g0Graphics = _g1Graphics = _shifted = false;
        _top = 0;
        _bottom = Rows - 1;
        _pendingWrap = false;
        _insertMode = false;
        _originMode = false;
        AutoWrap = true;
        CursorVisible = true;
        CursorStyle = _defaultCursorStyle;
        _cursorStyleChosen = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPaste = false;
        MouseTracking = 0;
        _savedMain = _savedAlt = new SavedCursor(0, 0, TerminalAttributes.None, 0, 0, false, false, false, false, false);
        ResetTabs();
    }

    private TerminalRow MainRow(int row) => _ring[(_first + _count - Rows + row) % Capacity];

    private void SetRow(int row, TerminalRow value)
    {
        if (_altActive)
        {
            _alt[row] = value;
        }
        else
        {
            _ring[(_first + _count - Rows + row) % Capacity] = value;
        }
    }

    private void ResetTabs()
    {
        _tabs = new bool[Columns];
        for (int i = 8; i < Columns; i += 8)
        {
            _tabs[i] = true;
        }
    }

    private void ResizeRows(int rows)
    {
        int cursorMain = _altActive ? _savedMain.Row : _row;
        if (rows < Rows)
        {
            int remove = Rows - rows;
            int trimmed = 0;

            // Blank rows under the cursor hold nothing: they go first.
            for (int r = Rows - 1; r > cursorMain && trimmed < remove && IsBlankRow(MainRow(r)); r--)
            {
                _count--;
                trimmed++;
                Rows--;
            }

            int moved = remove - trimmed;
            Rows -= moved;
            cursorMain -= moved;
            _alt = Shrink(_alt, rows);
            Reshape(_scrollback + Rows);
        }
        else
        {
            int add = rows - Rows;
            int pull = Math.Min(add, _count - Rows);
            Reshape(_scrollback + rows);
            Rows += pull;
            cursorMain += pull;
            for (int i = pull; i < add; i++)
            {
                AppendLine();
            }

            var alt = new TerminalRow[rows];
            Array.Copy(_alt, alt, _alt.Length);
            for (int i = _alt.Length; i < rows; i++)
            {
                alt[i] = new TerminalRow(Columns, TerminalCell.Blank(TerminalColor.Default));
            }

            _alt = alt;
            Rows = rows;
        }

        cursorMain = Math.Clamp(cursorMain, 0, Rows - 1);
        if (_altActive)
        {
            _savedMain = _savedMain with { Row = cursorMain };
        }
        else
        {
            _row = cursorMain;
        }
    }

    private static TerminalRow[] Shrink(TerminalRow[] rows, int count)
    {
        var kept = new TerminalRow[count];
        Array.Copy(rows, kept, count);
        return kept;
    }

    private static bool IsBlankRow(TerminalRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!cell.IsBlank || cell.Background != TerminalColor.Default)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A ring of <paramref name="capacity"/> lines holding the newest of the current ones, in order.</summary>
    private void Reshape(int capacity)
    {
        capacity = Math.Max(capacity, Rows);
        if (capacity == Capacity && _first == 0)
        {
            return;
        }

        int kept = Math.Min(_count, capacity);
        var ring = new TerminalRow[capacity];
        for (int i = 0; i < kept; i++)
        {
            ring[i] = _ring[(_first + _count - kept + i) % Capacity];
        }

        _dropped += _count - kept;
        _ring = ring;
        _first = 0;
        _count = kept;
    }

    /// <summary>Adds a blank line at the ring's end (the oldest line's row reused when the ring is full).</summary>
    private void AppendLine()
    {
        TerminalRow row;
        if (_count < Capacity)
        {
            int slot = (_first + _count) % Capacity;
            row = _ring[slot] ??= new TerminalRow(Columns, Blank);
            _count++;
        }
        else
        {
            row = _ring[_first];
            _first = (_first + 1) % Capacity;
            _dropped++;
        }

        row.Reset(Columns, Blank);
        row.Runs = null;
    }

    /// <summary>Scrolls the region <see cref="_top"/>..<see cref="_bottom"/> up by <paramref name="lines"/>; a full-screen scroll of the main screen keeps the lines in the scrollback.</summary>
    private void ScrollUp(int lines)
    {
        lines = Math.Clamp(lines, 1, _bottom - _top + 1);
        if (!_altActive && _top == 0 && _bottom == Rows - 1)
        {
            for (int i = 0; i < lines; i++)
            {
                AppendLine();
            }

            LinesScrolled += lines;
            return;
        }

        for (int i = 0; i < lines; i++)
        {
            var top = ScreenRow(_top);
            for (int r = _top; r < _bottom; r++)
            {
                SetRow(r, ScreenRow(r + 1));
            }

            top.Reset(Columns, Blank);
            SetRow(_bottom, top);
        }
    }

    /// <summary>Scrolls the region down by <paramref name="lines"/> (new blank lines at its top).</summary>
    private void ScrollDown(int lines) => ScrollDownFrom(_top, lines);

    private void ScrollDownFrom(int from, int lines)
    {
        lines = Math.Clamp(lines, 1, _bottom - from + 1);
        for (int i = 0; i < lines; i++)
        {
            var bottom = ScreenRow(_bottom);
            for (int r = _bottom; r > from; r--)
            {
                SetRow(r, ScreenRow(r - 1));
            }

            bottom.Reset(Columns, Blank);
            SetRow(from, bottom);
        }
    }

    private void ScrollUpFrom(int from, int lines)
    {
        lines = Math.Clamp(lines, 1, _bottom - from + 1);
        for (int i = 0; i < lines; i++)
        {
            var top = ScreenRow(from);
            for (int r = from; r < _bottom; r++)
            {
                SetRow(r, ScreenRow(r + 1));
            }

            top.Reset(Columns, Blank);
            SetRow(_bottom, top);
        }
    }

    private void LineFeed()
    {
        _pendingWrap = false;
        if (_row == _bottom)
        {
            ScrollUp(1);
        }
        else if (_row < Rows - 1)
        {
            _row++;
        }
    }

    private void ReverseIndex()
    {
        _pendingWrap = false;
        if (_row == _top)
        {
            ScrollDown(1);
        }
        else if (_row > 0)
        {
            _row--;
        }
    }

    private void CarriageReturn()
    {
        _col = 0;
        _pendingWrap = false;
    }

    private void Backspace()
    {
        if (_pendingWrap)
        {
            _pendingWrap = false;
            return;
        }

        if (_col > 0)
        {
            _col--;
        }
    }

    private void Tab(int count)
    {
        _pendingWrap = false;
        for (int i = 0; i < count && _col < Columns - 1; i++)
        {
            do
            {
                _col++;
            }
            while (_col < Columns - 1 && !_tabs[_col]);
        }
    }

    private void BackTab(int count)
    {
        _pendingWrap = false;
        for (int i = 0; i < count && _col > 0; i++)
        {
            do
            {
                _col--;
            }
            while (_col > 0 && !_tabs[_col]);
        }
    }

    /// <summary>Writes a character (its width in cells) at the cursor, wrapping first when a write is pending.</summary>
    private void Print(int codePoint)
    {
        int width = TerminalCharWidth.Of(codePoint);
        if (width == 0)
        {
            return;
        }

        if (codePoint is >= 0x5F and <= 0x7E && (_shifted ? _g1Graphics : _g0Graphics))
        {
            codePoint = DecGraphics[codePoint - 0x5F];
        }

        if (width > Columns)
        {
            return;
        }

        if (_pendingWrap && AutoWrap)
        {
            WrapLine();
        }

        if (width == 2 && _col == Columns - 1)
        {
            if (!AutoWrap)
            {
                return;
            }

            var edge = ScreenRow(_row);
            ClearWide(edge, _col);
            edge.Cells[_col] = Blank;
            WrapLine();
        }

        var row = ScreenRow(_row);
        if (_insertMode)
        {
            InsertBlanks(row, _col, width);
        }

        ClearWide(row, _col);
        if (width == 2)
        {
            ClearWide(row, _col + 1);
            row.Cells[_col] = new TerminalCell(codePoint, _attr, _fg, _bg, wide: true);
            row.Cells[_col + 1] = new TerminalCell(0, _attr, _fg, _bg, wideTail: true);
        }
        else
        {
            row.Cells[_col] = new TerminalCell(codePoint, _attr, _fg, _bg);
        }

        row.Touch();
        _lastPrinted = codePoint;
        Advance(width);
    }

    /// <summary>Writes printable ASCII (0x20-0x7E) at the cursor: whole stretches of a row at a time.</summary>
    private void PrintAscii(ReadOnlySpan<byte> text)
    {
        if (_insertMode || (_shifted ? _g1Graphics : _g0Graphics))
        {
            foreach (var b in text)
            {
                Print(b);
            }

            return;
        }

        while (!text.IsEmpty)
        {
            if (_pendingWrap)
            {
                if (!AutoWrap)
                {
                    // The last column is written over until the cursor moves.
                    var last = ScreenRow(_row);
                    last.Cells[_col] = new TerminalCell(text[^1], _attr, _fg, _bg);
                    last.Touch();
                    _lastPrinted = text[^1];
                    return;
                }

                WrapLine();
            }

            var row = ScreenRow(_row);
            int room = Columns - _col;
            int count = Math.Min(room, text.Length);
            ClearWide(row, _col);
            ClearWide(row, _col + count - 1);
            var cells = row.Cells.AsSpan(_col, count);
            var attr = _attr;
            uint fg = _fg, bg = _bg;
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new TerminalCell(text[i], attr, fg, bg);
            }

            row.Touch();
            _lastPrinted = text[count - 1];
            text = text[count..];
            Advance(count);
        }
    }

    private void Advance(int cells)
    {
        if (_col + cells >= Columns)
        {
            _col = Columns - 1;
            _pendingWrap = AutoWrap;
        }
        else
        {
            _col += cells;
        }
    }

    private void WrapLine()
    {
        ScreenRow(_row).Wrapped = true;
        _pendingWrap = false;
        _col = 0;
        if (_row == _bottom)
        {
            ScrollUp(1);
        }
        else if (_row < Rows - 1)
        {
            _row++;
        }
    }

    /// <summary>Blanks the other half of a wide character that a write at <paramref name="column"/> cuts.</summary>
    private void ClearWide(TerminalRow row, int column)
    {
        if ((uint)column >= (uint)row.Cells.Length)
        {
            return;
        }

        var cell = row.Cells[column];
        if (cell.IsWideTail && column > 0)
        {
            row.Cells[column - 1] = Blank;
        }
        else if (cell.IsWide && column + 1 < row.Cells.Length)
        {
            row.Cells[column + 1] = Blank;
        }
    }

    private void InsertBlanks(TerminalRow row, int column, int count)
    {
        var cells = row.Cells.AsSpan();
        count = Math.Min(count, Columns - column);
        cells[column..(Columns - count)].CopyTo(cells[(column + count)..]);
        cells.Slice(column, count).Fill(Blank);
        row.Touch();
    }

    private void DeleteCells(int count)
    {
        var row = ScreenRow(_row);
        var cells = row.Cells.AsSpan();
        count = Math.Min(count, Columns - _col);
        cells[(_col + count)..Columns].CopyTo(cells[_col..]);
        cells[(Columns - count)..Columns].Fill(Blank);
        row.Touch();
        _pendingWrap = false;
    }

    private void EraseCells(TerminalRow row, int from, int to)
    {
        from = Math.Clamp(from, 0, row.Cells.Length);
        to = Math.Clamp(to, from, row.Cells.Length);
        if (from < to)
        {
            ClearWide(row, from);
            ClearWide(row, to - 1);
            row.Cells.AsSpan(from, to - from).Fill(Blank);
            row.Touch();
        }
    }

    private void EraseInLine(int mode)
    {
        var row = ScreenRow(_row);
        switch (mode)
        {
            case 0:
                EraseCells(row, _col, Columns);
                row.Wrapped = false;
                break;
            case 1:
                EraseCells(row, 0, _col + 1);
                break;
            case 2:
                EraseCells(row, 0, Columns);
                row.Wrapped = false;
                break;
        }
    }

    private void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (int r = _row + 1; r < Rows; r++)
                {
                    ScreenRow(r).Reset(Columns, Blank);
                }

                break;
            case 1:
                for (int r = 0; r < _row; r++)
                {
                    ScreenRow(r).Reset(Columns, Blank);
                }

                EraseInLine(1);
                break;
            case 2:
                for (int r = 0; r < Rows; r++)
                {
                    ScreenRow(r).Reset(Columns, Blank);
                }

                break;
            case 3 when !_altActive:
                // The scrollback goes; the screen stays.
                var screen = new TerminalRow[Capacity];
                for (int r = 0; r < Rows; r++)
                {
                    screen[r] = MainRow(r);
                }

                _dropped += _count - Rows;
                _ring = screen;
                _first = 0;
                _count = Rows;
                break;
        }
    }

    private void InsertLines(int count)
    {
        if (_row < _top || _row > _bottom)
        {
            return;
        }

        ScrollDownFrom(_row, count);
        _col = 0;
        _pendingWrap = false;
    }

    private void DeleteLines(int count)
    {
        if (_row < _top || _row > _bottom)
        {
            return;
        }

        ScrollUpFrom(_row, count);
        _col = 0;
        _pendingWrap = false;
    }

    private void MoveTo(int row, int column)
    {
        int top = _originMode ? _top : 0;
        int bottom = _originMode ? _bottom : Rows - 1;
        _row = Math.Clamp(top + row, top, bottom);
        _col = Math.Clamp(column, 0, Columns - 1);
        _pendingWrap = false;
    }

    private void MoveUp(int count)
    {
        int limit = _row >= _top ? _top : 0;
        _row = Math.Max(limit, _row - count);
        _pendingWrap = false;
    }

    private void MoveDown(int count)
    {
        int limit = _row <= _bottom ? _bottom : Rows - 1;
        _row = Math.Min(limit, _row + count);
        _pendingWrap = false;
    }

    private void SetRegion(int top, int bottom)
    {
        top = top <= 0 ? 0 : top - 1;
        bottom = bottom <= 0 ? Rows - 1 : Math.Min(bottom - 1, Rows - 1);
        if (top >= bottom)
        {
            return;
        }

        _top = top;
        _bottom = bottom;
        MoveTo(0, 0);
    }

    private void SaveCursor()
    {
        var saved = new SavedCursor(_row, _col, _attr, _fg, _bg, _pendingWrap, _originMode, _g0Graphics, _g1Graphics, _shifted);
        if (_altActive)
        {
            _savedAlt = saved;
        }
        else
        {
            _savedMain = saved;
        }
    }

    private void RestoreCursor()
    {
        var saved = _altActive ? _savedAlt : _savedMain;
        _row = Math.Clamp(saved.Row, 0, Rows - 1);
        _col = Math.Clamp(saved.Column, 0, Columns - 1);
        _attr = saved.Attributes;
        _fg = saved.Foreground;
        _bg = saved.Background;
        _pendingWrap = saved.PendingWrap;
        _originMode = saved.OriginMode;
        _g0Graphics = saved.G0Graphics;
        _g1Graphics = saved.G1Graphics;
        _shifted = saved.Shifted;
    }

    private void EnterAlternate(bool saveCursor, bool clear)
    {
        if (_altActive)
        {
            return;
        }

        if (saveCursor)
        {
            SaveCursor();
        }

        _altActive = true;
        if (clear)
        {
            for (int r = 0; r < Rows; r++)
            {
                _alt[r].Reset(Columns, TerminalCell.Blank(TerminalColor.Default));
            }
        }

        for (int r = 0; r < Rows; r++)
        {
            _alt[r].Touch();
        }
    }

    private void LeaveAlternate(bool restoreCursor)
    {
        if (!_altActive)
        {
            return;
        }

        _altActive = false;
        for (int r = 0; r < Rows; r++)
        {
            MainRow(r).Touch();
        }

        if (restoreCursor)
        {
            RestoreCursor();
        }
    }

    private void FillWithE()
    {
        var cell = new TerminalCell('E', TerminalAttributes.None, TerminalColor.Default, TerminalColor.Default);
        for (int r = 0; r < Rows; r++)
        {
            var row = ScreenRow(r);
            row.Cells.AsSpan(0, Columns).Fill(cell);
            row.Touch();
        }

        MoveTo(0, 0);
    }

    // DEC special graphics for 0x5F-0x7E (ESC ( 0): the line-drawing set full-screen programs draw boxes with.
    private static readonly int[] DecGraphics =
    [
        0x00A0, 0x25C6, 0x2592, 0x2409, 0x240C, 0x240D, 0x240A, 0x00B0, 0x00B1, 0x2424, 0x240B, 0x2518, 0x2510, 0x250C, 0x2514, 0x253C,
        0x23BA, 0x23BB, 0x2500, 0x23BC, 0x23BD, 0x251C, 0x2524, 0x2534, 0x252C, 0x2502, 0x2264, 0x2265, 0x03C0, 0x2260, 0x00A3, 0x00B7,
    ];

    private readonly record struct SavedCursor(int Row, int Column, TerminalAttributes Attributes, uint Foreground, uint Background, bool PendingWrap,
        bool OriginMode, bool G0Graphics, bool G1Graphics, bool Shifted);
}
