// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>
/// The terminal's colours from the editor's theme (the design's tokens): the ground is the panel's surface, the text the
/// interface's text, the cursor the caret, the selection the theme's; the 16 basic colours are the theme's own where it
/// has one (red the error, green the success, yellow the warning, blue the keyword, cyan the type names) and each is moved
/// until it reads at 4.5:1 on the ground (black and white excepted); 16-255 are xterm's cube and greys.
/// </summary>
public sealed class TerminalPalette
{
    private static int _generations;
    private readonly uint[] _indexed = new uint[256];

    /// <summary>The palette of <paramref name="theme"/>.</summary>
    public TerminalPalette(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var colors = theme.Colors;
        Background = colors.Surface;
        Foreground = colors.Text;
        Cursor = colors.Caret;
        CursorText = colors.Surface;
        Selection = colors.Selection;
        Generation = Interlocked.Increment(ref _generations);
        ReadOnlySpan<uint> ground = [Background];
        bool dark = theme.IsDark;
        uint black = dark ? ColorContrast.Mix(colors.Surface, 0xFF000000, 0.4) : colors.Text;
        uint white = dark ? colors.SecondaryText : colors.Border;
        uint red = colors.Error;
        uint green = colors.Success;
        uint yellow = colors.Warning;
        uint blue = theme.StyleOf(TokenClasses.Keyword).Argb;
        uint magenta = dark ? 0xFFC792EAu : 0xFF8E3CB4u;
        uint cyan = dark ? 0xFF89DDFFu : 0xFF1F7A8Cu;
        uint[] basic = [black, red, green, yellow, blue, magenta, cyan, white];
        for (int i = 0; i < 8; i++)
        {
            uint normal = i is 0 or 7 ? basic[i] : ColorContrast.Ensure(basic[i], ColorContrast.Text, ground);
            uint bright = i switch
            {
                0 => dark ? colors.GutterText : colors.SecondaryText,
                7 => dark ? colors.Text : colors.Background,
                _ => ColorContrast.Ensure(ColorContrast.Mix(basic[i], dark ? 0xFFFFFFFFu : 0xFF000000u, 0.25), ColorContrast.Text, ground),
            };
            _indexed[i] = normal;
            _indexed[i + 8] = bright;
        }

        ReadOnlySpan<int> steps = [0, 95, 135, 175, 215, 255];
        for (int i = 16; i < 232; i++)
        {
            int n = i - 16;
            _indexed[i] = 0xFF000000u | ((uint)steps[n / 36] << 16) | ((uint)steps[n / 6 % 6] << 8) | (uint)steps[n % 6];
        }

        for (int i = 232; i < 256; i++)
        {
            uint grey = (uint)(8 + ((i - 232) * 10));
            _indexed[i] = 0xFF000000u | (grey << 16) | (grey << 8) | grey;
        }
    }

    /// <summary>The ground.</summary>
    public uint Background { get; }

    /// <summary>The default text colour.</summary>
    public uint Foreground { get; }

    /// <summary>The cursor.</summary>
    public uint Cursor { get; }

    /// <summary>The character under a block cursor.</summary>
    public uint CursorText { get; }

    /// <summary>The selection's fill.</summary>
    public uint Selection { get; }

    /// <summary>A number of its own (the rows' kept runs are made again for another palette).</summary>
    public int Generation { get; }

    /// <summary>Indexed colour <paramref name="index"/>.</summary>
    public uint this[int index] => _indexed[index & 0xFF];

    /// <summary>The colour <paramref name="color"/> (<see cref="TerminalColor"/>) means; <paramref name="fallback"/> for the default.</summary>
    public uint Resolve(uint color, uint fallback) =>
        color == TerminalColor.Default ? fallback : TerminalColor.IsIndexed(color) ? _indexed[color & 0xFF] : 0xFF000000u | (color & 0xFFFFFF);
}

/// <summary>A selection in the terminal: from one cell to another, in absolute line numbers (<see cref="TerminalScreen.ScreenTop"/>).</summary>
/// <param name="StartLine">The line it started on.</param>
/// <param name="StartColumn">The cell it started at.</param>
/// <param name="EndLine">The line it ends on.</param>
/// <param name="EndColumn">The cell it ends before.</param>
public readonly record struct TerminalSelection(long StartLine, int StartColumn, long EndLine, int EndColumn)
{
    /// <summary>The selection with its start before its end.</summary>
    public TerminalSelection Ordered() =>
        EndLine < StartLine || (EndLine == StartLine && EndColumn < StartColumn) ? new(EndLine, EndColumn, StartLine, StartColumn) : this;

    /// <summary>Whether it covers nothing.</summary>
    public bool IsEmpty => StartLine == EndLine && StartColumn == EndColumn;
}

/// <summary>
/// Draws a terminal's visible rows through <see cref="IEditorCanvas"/>: the ground, each row's background runs, its text
/// runs (one per change of colour or rendition; a wide character on its own, at its cell), underlines and strikes, the
/// selection and the cursor (a hollow box when the terminal does not have the keyboard). A row's runs are kept on the row
/// while its version and the palette are the same, and dropped once it leaves the view, so a still frame makes no
/// string and memory follows the rows on screen, not the scrollback. Call it holding the screen's <see cref="TerminalScreen.Sync"/>.
/// </summary>
public sealed class TerminalRenderer
{
    private static readonly string[] Ascii = MakeAscii();
    private readonly List<TerminalRow> _drawn = [];
    private readonly List<TerminalRow> _drawing = [];
    private char[] _scratch = new char[256];
    private long _frame;

    /// <summary>Run strings made since the renderer was created (the paint's telemetry).</summary>
    public long RunStringsBuilt { get; private set; }

    /// <summary>Rows drawn in the last frame.</summary>
    public int RowsDrawn { get; private set; }

    /// <summary>How links show (<c>CRAFT_LINKS_UNDERLINE</c>: "hover", "always", "off"); X part 9.</summary>
    public string LinksUnderline { get; set; } = "hover";

    /// <summary>The link to underline (the view's hover with Ctrl held): its line and cells, or null.</summary>
    public (long Line, int StartColumn, int EndColumn)? HoveredLink { get; set; }

    /// <summary>The links the last frame underlined.</summary>
    public int LinksDrawn { get; private set; }

    private char[] _linkChars = [];
    private readonly OutputLink[] _links = new OutputLink[32];

    /// <summary>
    /// Draws <paramref name="screen"/> into <paramref name="width"/> by <paramref name="height"/>, its view
    /// <paramref name="scrollBack"/> lines above the bottom, with cells of <paramref name="metrics"/>.
    /// </summary>
    public void Draw(IEditorCanvas canvas, TerminalScreen screen, TerminalPalette palette, FontMetrics metrics, float width, float height,
        int scrollBack, TerminalSelection? selection, bool focused, bool cursorOn)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(palette);
        _frame++;
        float cell = Math.Max(1, metrics.CellWidth);
        float line = Math.Max(1, metrics.LineHeight);
        canvas.FillRect(0, 0, width, height, palette.Background);
        scrollBack = Math.Clamp(scrollBack, 0, screen.ScrollbackLines);
        long top = screen.ScreenTop - scrollBack;
        int rows = Math.Min(screen.Rows, (int)Math.Ceiling(height / line));
        var ordered = selection?.Ordered();
        _drawing.Clear();
        LinksDrawn = 0;
        bool allLinks = string.Equals(LinksUnderline, "always", StringComparison.Ordinal);
        var hovered = string.Equals(LinksUnderline, "off", StringComparison.Ordinal) ? null : HoveredLink;
        for (int r = 0; r < rows; r++)
        {
            long number = top + r;
            if (screen.LineAt(number) is not { } row)
            {
                continue;
            }

            float y = r * line;
            var runs = RunsOf(row, palette);
            foreach (ref readonly var fill in runs.Fills.AsSpan(0, runs.FillCount))
            {
                canvas.FillRect(fill.Column * cell, y, fill.Cells * cell, line, fill.Argb);
            }

            if (ordered is { IsEmpty: false } s && number >= s.StartLine && number <= s.EndLine)
            {
                int from = number == s.StartLine ? s.StartColumn : 0;
                int to = number == s.EndLine ? s.EndColumn : screen.Columns;
                if (to > from)
                {
                    canvas.FillRect(from * cell, y, (to - from) * cell, line, palette.Selection);
                }
            }

            foreach (ref readonly var run in runs.Texts.AsSpan(0, runs.TextCount))
            {
                float x = run.Column * cell;
                canvas.DrawText(run.Text, x, y, new TextStyle(run.Argb, run.Bold, run.Italic));
                if (run.Underline)
                {
                    canvas.DrawLine(x, y + line - 1.5f, x + (run.Cells * cell), y + line - 1.5f, run.Argb, 1);
                }

                if (run.Strike)
                {
                    canvas.DrawLine(x, y + (line / 2), x + (run.Cells * cell), y + (line / 2), run.Argb, 1);
                }
            }

            if (allLinks)
            {
                DrawLinks(canvas, row, y, cell, line, palette.Foreground);
            }
            else if (hovered is { } link && link.Line == number)
            {
                canvas.DrawLine(link.StartColumn * cell, y + line - 1.5f, link.EndColumn * cell, y + line - 1.5f, palette.Foreground, 1);
                LinksDrawn++;
            }

            row.DrawnFrame = _frame;
            _drawing.Add(row);
        }

        RowsDrawn = _drawing.Count;
        if (scrollBack == 0 && screen.CursorVisible && screen.CursorRow < rows)
        {
            DrawCursor(canvas, screen, palette, cell, line, focused, cursorOn);
        }

        // Rows that left the view drop their runs: memory follows the screen, not the scrollback.
        foreach (var row in _drawn)
        {
            if (row.DrawnFrame != _frame)
            {
                row.Runs = null;
            }
        }

        _drawn.Clear();
        _drawn.AddRange(_drawing);
    }

    private static void DrawCursor(IEditorCanvas canvas, TerminalScreen screen, TerminalPalette palette, float cell, float line, bool focused, bool cursorOn)
    {
        int row = screen.CursorRow;
        int column = screen.CursorColumn;
        var under = screen.CellAt(row, column);
        float width = (under.IsWide ? 2 : 1) * cell;
        float x = column * cell;
        float y = row * line;
        if (!focused)
        {
            canvas.DrawLine(x, y + 0.5f, x + width, y + 0.5f, palette.Cursor, 1);
            canvas.DrawLine(x, y + line - 0.5f, x + width, y + line - 0.5f, palette.Cursor, 1);
            canvas.DrawLine(x + 0.5f, y, x + 0.5f, y + line, palette.Cursor, 1);
            canvas.DrawLine(x + width - 0.5f, y, x + width - 0.5f, y + line, palette.Cursor, 1);
            return;
        }

        if (!cursorOn)
        {
            return;
        }

        switch (screen.CursorStyle)
        {
            case TerminalCursorStyle.Bar:
                canvas.FillRect(x, y, 2, line, palette.Cursor);
                break;
            case TerminalCursorStyle.Underline:
                canvas.FillRect(x, y + line - 2, width, 2, palette.Cursor);
                break;
            default:
                canvas.FillRect(x, y, width, line, palette.Cursor);
                if (!under.IsBlank && !under.IsWideTail)
                {
                    int code = under.CodePoint;
                    string text = code < 128 ? Ascii[code] : Rune.IsValid(code) ? new Rune(code).ToString() : "?";
                    var attributes = under.Attributes;
                    canvas.DrawText(text, x, y, new TextStyle(palette.CursorText, (attributes & TerminalAttributes.Bold) != 0, (attributes & TerminalAttributes.Italic) != 0));
                }

                break;
        }
    }

    private static string[] MakeAscii()
    {
        var table = new string[128];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = ((char)i).ToString();
        }

        return table;
    }

    private RowRuns RunsOf(TerminalRow row, TerminalPalette palette)
    {
        if (row.Runs is RowRuns kept && row.RunsVersion == row.Version && row.RunsPalette == palette.Generation)
        {
            return kept;
        }

        var runs = row.Runs as RowRuns ?? new RowRuns();
        runs.FillCount = 0;
        runs.TextCount = 0;
        var cells = row.Cells.AsSpan();
        int fillStart = -1;
        uint fillArgb = 0;
        int textStart = -1;
        int textEnd = -1;
        uint textArgb = 0;
        var textAttributes = TerminalAttributes.None;
        for (int i = 0; i <= cells.Length; i++)
        {
            uint fg = 0, bg = 0;
            var attributes = TerminalAttributes.None;
            bool blank = true, wide = false, tail = false;
            if (i < cells.Length)
            {
                var c = cells[i];
                attributes = c.Attributes;
                fg = Foreground(c, palette, attributes);
                bg = palette.Resolve(c.Background, palette.Background);
                if ((attributes & TerminalAttributes.Inverse) != 0)
                {
                    (fg, bg) = (bg, fg);
                }

                blank = c.IsBlank || (attributes & TerminalAttributes.Hidden) != 0;
                wide = c.IsWide;
                tail = c.IsWideTail;
            }

            // Background runs: every cell whose fill is not the ground.
            bool filled = i < cells.Length && bg != palette.Background;
            if (fillStart >= 0 && (!filled || bg != fillArgb))
            {
                runs.AddFill(fillStart, i - fillStart, fillArgb);
                fillStart = -1;
            }

            if (filled && fillStart < 0)
            {
                fillStart = i;
                fillArgb = bg;
            }

            if (tail)
            {
                continue;
            }

            // Text runs: a change of colour or rendition, a wide character, or the end closes a run; an undecorated blank
            // inside an undecorated run stays in it (one string per run), blanks after the last character are not drawn.
            var drawn = attributes & (TerminalAttributes.Bold | TerminalAttributes.Italic | TerminalAttributes.Underline | TerminalAttributes.Strike);
            bool decorated = (drawn & (TerminalAttributes.Underline | TerminalAttributes.Strike)) != 0;
            bool inkless = i == cells.Length || (blank && !decorated);
            if (textStart >= 0)
            {
                bool sameStyle = !inkless && !wide && fg == textArgb && drawn == textAttributes;
                bool blankInside = i < cells.Length && inkless && (textAttributes & (TerminalAttributes.Underline | TerminalAttributes.Strike)) == 0;
                if (!sameStyle && !blankInside)
                {
                    AddText(runs, cells, textStart, textEnd, textArgb, textAttributes);
                    textStart = -1;
                }
            }

            if (inkless)
            {
                continue;
            }

            if (textStart < 0)
            {
                textStart = i;
                textArgb = fg;
                textAttributes = drawn;
            }

            textEnd = wide ? i + 2 : i + 1;
            if (wide)
            {
                AddText(runs, cells, textStart, textEnd, textArgb, textAttributes);
                textStart = -1;
            }
        }

        row.Runs = runs;
        row.RunsVersion = row.Version;
        row.RunsPalette = palette.Generation;
        return runs;
    }

    private static uint Foreground(TerminalCell cell, TerminalPalette palette, TerminalAttributes attributes)
    {
        uint color = cell.Foreground;
        if ((attributes & TerminalAttributes.Bold) != 0 && TerminalColor.IsIndexed(color) && TerminalColor.IndexOf(color) < 8)
        {
            color = TerminalColor.Indexed(TerminalColor.IndexOf(color) + 8);
        }

        uint argb = palette.Resolve(color, palette.Foreground);
        if ((attributes & TerminalAttributes.Dim) != 0)
        {
            argb = ColorContrast.Mix(argb, palette.Resolve(cell.Background, palette.Background), 0.5);
        }

        return argb;
    }

    private void AddText(RowRuns runs, ReadOnlySpan<TerminalCell> cells, int start, int end, uint argb, TerminalAttributes attributes)
    {
        if (_scratch.Length < (end - start) * 2)
        {
            _scratch = new char[(end - start) * 2];
        }

        // The run's text: its blanks as spaces (so the next character lands on its cell), trailing ones left out.
        int length = 0;
        int lastInk = 0;
        for (int i = start; i < end; i++)
        {
            var cell = cells[i];
            if (cell.IsWideTail)
            {
                continue;
            }

            int code = cell.CodePoint;
            bool hidden = (cell.Attributes & TerminalAttributes.Hidden) != 0;
            if (code is 0 or ' ' || hidden)
            {
                _scratch[length++] = ' ';
                continue;
            }

            if (code < 0x10000)
            {
                _scratch[length++] = (char)code;
            }
            else
            {
                var rune = new Rune(code);
                rune.EncodeToUtf16(_scratch.AsSpan(length));
                length += rune.Utf16SequenceLength;
            }

            lastInk = length;
        }

        bool decorated = (attributes & (TerminalAttributes.Underline | TerminalAttributes.Strike)) != 0;
        if (!decorated)
        {
            length = lastInk;
        }

        if (length == 0)
        {
            return;
        }

        string text = length == 1 && _scratch[0] < 128 ? Ascii[_scratch[0]] : new string(_scratch, 0, length);
        if (length > 1 || _scratch[0] >= 128)
        {
            RunStringsBuilt++;
        }

        runs.AddText(new TextRun(start, end - start, text, argb, (attributes & TerminalAttributes.Bold) != 0, (attributes & TerminalAttributes.Italic) != 0,
            (attributes & TerminalAttributes.Underline) != 0, (attributes & TerminalAttributes.Strike) != 0));
    }

    private readonly record struct FillRun(int Column, int Cells, uint Argb);

    // X part 9: every link of a row underlined (CRAFT_LINKS_UNDERLINE "always"): the row's cells as characters in a buffer
    // kept between frames, the detectors' span scan; nothing allocated once the buffer fits the widest row.
    private void DrawLinks(IEditorCanvas canvas, TerminalRow row, float y, float cell, float line, uint color)
    {
        if (_linkChars.Length < row.Length)
        {
            _linkChars = new char[Math.Max(row.Length, 256)];
        }

        int length = TerminalLinks.CharsOf(row, _linkChars);
        int count = OutputLinkDetectors.FindAll(_linkChars.AsSpan(0, length), _links);
        for (int i = 0; i < count; i++)
        {
            canvas.DrawLine(_links[i].Start * cell, y + line - 1.5f, _links[i].End * cell, y + line - 1.5f, color, 1);
            LinksDrawn++;
        }
    }

    private readonly record struct TextRun(int Column, int Cells, string Text, uint Argb, bool Bold, bool Italic, bool Underline, bool Strike);

    /// <summary>A row's runs, kept on the row (its arrays reused when it is made again).</summary>
    private sealed class RowRuns
    {
        public FillRun[] Fills = [];
        public int FillCount;
        public TextRun[] Texts = [];
        public int TextCount;

        public void AddFill(int column, int cells, uint argb)
        {
            if (FillCount == Fills.Length)
            {
                Array.Resize(ref Fills, Math.Max(4, Fills.Length * 2));
            }

            Fills[FillCount++] = new FillRun(column, cells, argb);
        }

        public void AddText(TextRun run)
        {
            if (TextCount == Texts.Length)
            {
                Array.Resize(ref Texts, Math.Max(4, Texts.Length * 2));
            }

            Texts[TextCount++] = run;
        }
    }
}
