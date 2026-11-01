// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>The font the editor draws with.</summary>
/// <param name="Family">The family's name ("Cascadia Mono"); the platform falls back to its own monospaced font when it has none of that name.</param>
/// <param name="Size">The size in device-independent units.</param>
public sealed record EditorFont(string Family, float Size);

/// <summary>A monospaced font's measures, as the platform measured them.</summary>
/// <param name="CellWidth">One character cell's width.</param>
/// <param name="LineHeight">One line's height (the font's height times the line spacing setting).</param>
/// <param name="Baseline">The distance from a line's top to its baseline.</param>
public readonly record struct FontMetrics(float CellWidth, float LineHeight, float Baseline);

/// <summary>Measures a font on the platform (the editor never assumes a size).</summary>
public interface ITextMeasurer
{
    /// <summary>The measures of <paramref name="font"/> at <paramref name="lineSpacing"/> times its height.</summary>
    FontMetrics Measure(EditorFont font, float lineSpacing);
}

/// <summary>
/// What the editor draws with: rectangles, lines and runs of text at a position. The platform canvas implements it (MAUI's
/// cross-platform canvas on Windows and Android); the tests use a recording fake. Coordinates are device-independent,
/// from the surface's top-left.
/// </summary>
public interface IEditorCanvas
{
    /// <summary>Fills a rectangle.</summary>
    void FillRect(float x, float y, float width, float height, uint argb);

    /// <summary>Draws a line.</summary>
    void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness);

    /// <summary>Draws <paramref name="text"/> with its left edge at <paramref name="x"/> and its line's top at <paramref name="top"/>.</summary>
    void DrawText(string text, float x, float top, in TextStyle style);
}

/// <summary>
/// A canvas that draws file icons' shapes (W: rich icons): the platform's parses each layer's path once and keeps it per
/// size (MAUI Graphics' own parser), so a warm frame draws without allocating. A drawing that meets a canvas without it
/// draws the icon's badge as text (<see cref="FileIconDrawing.Draw"/>).
/// </summary>
public interface IIconCanvas
{
    /// <summary>
    /// Draws <paramref name="icon"/> (its open shape when <paramref name="open"/>, in its <paramref name="dark"/> or light
    /// colours) in the square of <paramref name="size"/> at (<paramref name="x"/>, <paramref name="y"/>); false when it has
    /// no shape this canvas can draw (the caller draws the badge).
    /// </summary>
    bool DrawIcon(FileIcon icon, float x, float y, float size, bool open, bool dark);
}

/// <summary>Draws a file icon on any <see cref="IEditorCanvas"/>: its shape where the canvas can (<see cref="IIconCanvas"/>), else its badge.</summary>
public static class FileIconDrawing
{
    /// <summary>
    /// Draws <paramref name="icon"/> at (<paramref name="x"/>, <paramref name="top"/>) in a square of <paramref name="size"/>:
    /// the shape through <see cref="IIconCanvas"/>, else the badge's text in its colour. Allocates nothing.
    /// </summary>
    public static void Draw(IEditorCanvas canvas, FileIcon icon, float x, float top, float size, bool open, bool dark)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(icon);
        if (canvas is IIconCanvas icons && icon.ShapeFor(open) is not null && icons.DrawIcon(icon, x, top, size, open, dark))
        {
            return;
        }

        canvas.DrawText(icon.Badge, x, top, new TextStyle(icon.BadgeArgb(dark)));
    }
}

/// <summary>A run of a <see cref="StyledLine"/>: its characters (tabs already expanded) from <paramref name="Column"/> on, in one style.</summary>
/// <param name="Text">The run's characters.</param>
/// <param name="Column">The column its first character is drawn at.</param>
/// <param name="Style">Its colour, weight and slant.</param>
public readonly record struct StyledRun(string Text, int Column, TextStyle Style);

/// <summary>
/// One shown line as coloured runs, drawn as one piece (the owner's third Windows run, B1): a canvas that implements
/// <see cref="IStyledLineCanvas"/> lays the whole line out once (each run's colour, weight and slant on its range) and keeps
/// that layout here (<see cref="Layout"/>) while the line, its colours and the font stay the same, so a frame that draws
/// the line again lays nothing out. The line's owner (the editor's renderer) keeps it while the line is on screen or near
/// it, and releases its layout when it drops it (<see cref="ReleaseLayout"/>). The UI thread's; not thread-safe.
/// </summary>
public sealed class StyledLine
{
    private StyledRun[] _runs = [];
    private int[] _starts = [];
    private string? _text;
    private int _textColumns;

    /// <summary>The runs, in column order.</summary>
    public ReadOnlySpan<StyledRun> Runs => _runs.AsSpan(0, Count);

    /// <summary>How many runs it holds.</summary>
    public int Count { get; private set; }

    /// <summary>The line's width in columns (its last character's end, white space included).</summary>
    public int Columns { get; set; }

    /// <summary>
    /// The runs' characters at their columns with spaces between them, made once when first asked (the text a canvas lays
    /// out); run <c>i</c> starts at <see cref="TextIndexOf"/>(i).
    /// </summary>
    public string Text => _text ??= Join();

    /// <summary>The columns <see cref="Text"/> spans (a surrogate pair is one column and two characters).</summary>
    public int TextColumns
    {
        get
        {
            _ = Text;
            return _textColumns;
        }
    }

    /// <summary>The canvas's layout of this line, or null when none is kept.</summary>
    public IDisposable? Layout { get; private set; }

    /// <summary>The key of the canvas state (its font and measures) <see cref="Layout"/> was made for; 0 when none.</summary>
    public int LayoutKey { get; private set; }

    /// <summary>Adds a run after the others (the text and any kept layout are made again).</summary>
    public void Add(StyledRun run)
    {
        ArgumentNullException.ThrowIfNull(run.Text);
        if (Count == _runs.Length)
        {
            Array.Resize(ref _runs, Math.Max(4, _runs.Length * 2));
        }

        _runs[Count++] = run;
        _text = null;
        ReleaseLayout();
    }

    /// <summary>Where run <paramref name="index"/> starts in <see cref="Text"/>.</summary>
    public int TextIndexOf(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        _ = Text;
        return _starts[index];
    }

    /// <summary>Keeps <paramref name="layout"/> as this line's layout for canvas state <paramref name="key"/>; the one kept before is disposed.</summary>
    public void KeepLayout(IDisposable? layout, int key)
    {
        if (!ReferenceEquals(layout, Layout))
        {
            Layout?.Dispose();
        }

        Layout = layout;
        LayoutKey = layout is null ? 0 : key;
    }

    /// <summary>Disposes the kept layout, if any (the line is dropped, or its memory is wanted back).</summary>
    public void ReleaseLayout()
    {
        var layout = Layout;
        Layout = null;
        LayoutKey = 0;
        layout?.Dispose();
    }

    private string Join()
    {
        int length = 0;
        int column = 0;
        var runs = Runs;
        foreach (ref readonly var run in runs)
        {
            length += Math.Max(0, run.Column - column) + run.Text.Length;
            column = Math.Max(column, run.Column) + ColumnsOf(run.Text);
        }

        if (_starts.Length < Count)
        {
            _starts = new int[_runs.Length];
        }

        _textColumns = column;
        return string.Create(length, this, static (span, line) =>
        {
            int at = 0;
            int column = 0;
            var runs = line.Runs;
            for (int i = 0; i < runs.Length; i++)
            {
                ref readonly var run = ref runs[i];
                int gap = Math.Max(0, run.Column - column);
                span.Slice(at, gap).Fill(' ');
                at += gap;
                line._starts[i] = at;
                run.Text.AsSpan().CopyTo(span[at..]);
                at += run.Text.Length;
                column = Math.Max(column, run.Column) + ColumnsOf(run.Text);
            }
        });
    }

    // A run's columns: its characters, a surrogate pair counting once (tabs are expanded before a run is made).
    private static int ColumnsOf(string text)
    {
        int columns = text.Length;
        foreach (char c in text)
        {
            if (char.IsLowSurrogate(c))
            {
                columns--;
            }
        }

        return columns;
    }
}

/// <summary>
/// A canvas that draws a whole <see cref="StyledLine"/> at once (B1: one cached layout per shown line on Windows). The
/// editor's renderer draws through <see cref="StyledLines.Draw"/>, which falls back to one <see cref="IEditorCanvas.DrawText"/>
/// per run on a canvas without it.
/// </summary>
public interface IStyledLineCanvas
{
    /// <summary>
    /// Draws <paramref name="line"/> with its column 0 at <paramref name="x"/> and its top at <paramref name="top"/>, one
    /// column <paramref name="cell"/> wide; what lies outside <paramref name="clipLeft"/> to <paramref name="clipRight"/> need
    /// not be drawn (a run wholly outside is skipped when drawn run by run).
    /// </summary>
    void DrawStyledLine(StyledLine line, float x, float top, float cell, float clipLeft, float clipRight);
}

/// <summary>Draws <see cref="StyledLine"/>s on any <see cref="IEditorCanvas"/>.</summary>
public static class StyledLines
{
    /// <summary>
    /// Draws <paramref name="line"/> on <paramref name="canvas"/>: as one piece when it is an <see cref="IStyledLineCanvas"/>,
    /// else run by run (<see cref="DrawRuns"/>).
    /// </summary>
    public static void Draw(IEditorCanvas canvas, StyledLine line, float x, float top, float cell, float clipLeft, float clipRight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(line);
        if (canvas is IStyledLineCanvas lines)
        {
            lines.DrawStyledLine(line, x, top, cell, clipLeft, clipRight);
        }
        else
        {
            DrawRuns(canvas, line, x, top, cell, clipLeft, clipRight);
        }
    }

    /// <summary>
    /// Draws each run of <paramref name="line"/> with <see cref="IEditorCanvas.DrawText"/> at <paramref name="x"/> plus its
    /// column times <paramref name="cell"/>, skipping a run that starts right of <paramref name="clipRight"/> or ends left of
    /// <paramref name="clipLeft"/>.
    /// </summary>
    public static void DrawRuns(IEditorCanvas canvas, StyledLine line, float x, float top, float cell, float clipLeft, float clipRight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(line);
        foreach (ref readonly var run in line.Runs)
        {
            float at = x + (run.Column * cell);
            if (at > clipRight || at + (run.Text.Length * cell) < clipLeft)
            {
                continue;
            }

            canvas.DrawText(run.Text, at, top, run.Style);
        }
    }
}

/// <summary>Modifier keys held during an input.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Shift.</summary>
    Shift = 1,

    /// <summary>Control (Command on a Mac keyboard maps here too).</summary>
    Control = 2,

    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>
/// The keys the editor handles as commands; text itself arrives through <see cref="IEditorInputSink.OnText"/>. Each
/// value is part of the plug-in contract (a plug-in's compiled binding holds the number): a new key is added at the end.
/// </summary>
public enum EditorKey
{
    /// <summary>A key the editor does not handle.</summary>
    None,

    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Up arrow.</summary>
    Up,

    /// <summary>Down arrow.</summary>
    Down,

    /// <summary>Home.</summary>
    Home,

    /// <summary>End.</summary>
    End,

    /// <summary>Page up.</summary>
    PageUp,

    /// <summary>Page down.</summary>
    PageDown,

    /// <summary>Backspace.</summary>
    Backspace,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>Enter.</summary>
    Enter,

    /// <summary>Tab.</summary>
    Tab,

    /// <summary>Escape.</summary>
    Escape,

    /// <summary>Insert.</summary>
    Insert,

    /// <summary>F3 (find next).</summary>
    F3,

    /// <summary>A (select all with Control).</summary>
    A,

    /// <summary>C (copy with Control).</summary>
    C,

    /// <summary>F (find with Control).</summary>
    F,

    /// <summary>H (replace with Control).</summary>
    H,

    /// <summary>S (save with Control).</summary>
    S,

    /// <summary>V (paste with Control).</summary>
    V,

    /// <summary>X (cut with Control).</summary>
    X,

    /// <summary>Y (redo with Control).</summary>
    Y,

    /// <summary>Z (undo with Control, redo with Control+Shift).</summary>
    Z,

    // Phase K: the other letters, the digits and the function keys, for key bindings (EditorKeyBindings). Added at the end
    // so the values above keep theirs.

    /// <summary>B (free for key bindings).</summary>
    B,

    /// <summary>D (free for key bindings).</summary>
    D,

    /// <summary>E (free for key bindings).</summary>
    E,

    /// <summary>G (free for key bindings).</summary>
    G,

    /// <summary>I (free for key bindings).</summary>
    I,

    /// <summary>J (free for key bindings).</summary>
    J,

    /// <summary>K (free for key bindings).</summary>
    K,

    /// <summary>L (free for key bindings).</summary>
    L,

    /// <summary>M (free for key bindings).</summary>
    M,

    /// <summary>N (free for key bindings).</summary>
    N,

    /// <summary>O (free for key bindings).</summary>
    O,

    /// <summary>P (free for key bindings).</summary>
    P,

    /// <summary>Q (free for key bindings).</summary>
    Q,

    /// <summary>R (free for key bindings).</summary>
    R,

    /// <summary>T (free for key bindings).</summary>
    T,

    /// <summary>U (free for key bindings).</summary>
    U,

    /// <summary>W (free for key bindings).</summary>
    W,

    /// <summary>The digit 0 of the main keyboard.</summary>
    D0,

    /// <summary>The digit 1 of the main keyboard.</summary>
    D1,

    /// <summary>The digit 2 of the main keyboard.</summary>
    D2,

    /// <summary>The digit 3 of the main keyboard.</summary>
    D3,

    /// <summary>The digit 4 of the main keyboard.</summary>
    D4,

    /// <summary>The digit 5 of the main keyboard.</summary>
    D5,

    /// <summary>The digit 6 of the main keyboard.</summary>
    D6,

    /// <summary>The digit 7 of the main keyboard.</summary>
    D7,

    /// <summary>The digit 8 of the main keyboard.</summary>
    D8,

    /// <summary>The digit 9 of the main keyboard.</summary>
    D9,

    /// <summary>F1.</summary>
    F1,

    /// <summary>F2.</summary>
    F2,

    /// <summary>F4.</summary>
    F4,

    /// <summary>F5.</summary>
    F5,

    /// <summary>F6.</summary>
    F6,

    /// <summary>F7.</summary>
    F7,

    /// <summary>F8.</summary>
    F8,

    /// <summary>F9.</summary>
    F9,

    /// <summary>F10.</summary>
    F10,

    /// <summary>F11.</summary>
    F11,

    /// <summary>F12.</summary>
    F12,

    // Phase E4: the comma, for the settings' Ctrl+Comma (added at the end, so the values above keep theirs).

    /// <summary>The comma key of the main keyboard.</summary>
    Comma,

    // B3 (2026-11-01): Space was added before A, which moved every letter, digit and function key one up, so a plug-in
    // built against contract 1.0 bound other keys (the sample's Ctrl+Shift+W read as Ctrl+Shift+U). A key's value is part
    // of the contract: the 1.0 values are back, and a new key goes only at the end (the API guard's contract check).

    /// <summary>Space, as a key only with a modifier (Ctrl+Space asks for completion); typed, it arrives as text.</summary>
    Space,

    // Phase T: the backquote, for the terminal's Ctrl+` and Ctrl+Shift+` (added at the end, so the values above keep theirs).

    /// <summary>The backquote (grave accent) key of the main keyboard; "`" in a chord's text.</summary>
    Backquote,

    // The owner's note 16: the keys of the shortcuts a C# developer expects (Ctrl+/, Ctrl+., Ctrl+=, Ctrl+-, Ctrl+[ and
    // Ctrl+]), added at the end so the values above keep theirs.

    /// <summary>The period key of the main keyboard; "." in a chord's text.</summary>
    Period,

    /// <summary>The slash key of the main keyboard; "/" in a chord's text.</summary>
    Slash,

    /// <summary>The minus key (the main keyboard's or the keypad's); "-" in a chord's text.</summary>
    Minus,

    /// <summary>The equals-and-plus key of the main keyboard (VK_OEM_PLUS), or the keypad's plus; "=" in a chord's text.</summary>
    Plus,

    /// <summary>The opening square bracket key; "[" in a chord's text.</summary>
    BracketLeft,

    /// <summary>The closing square bracket key; "]" in a chord's text.</summary>
    BracketRight,

    // The X restart (piece 3a, 2026-11-01): the backslash, for Split right (Ctrl+\), added at the end so the values above
    // keep theirs (contract 1.1 adds it).

    /// <summary>The backslash key (VK_OEM_5 on US keyboards); "\" in a chord's text.</summary>
    Backslash,
}

/// <summary>Where the platform's input goes: the editor implements it, a <see cref="ITextInputAdapter"/> calls it.</summary>
public interface IEditorInputSink
{
    /// <summary>A key went down; true when the editor handled it (the platform then does nothing else with it).</summary>
    bool OnKey(EditorKey key, KeyModifiers modifiers);

    /// <summary>Text was typed or committed by an input method (one character or several).</summary>
    void OnText(string text);
}

/// <summary>
/// An <see cref="IEditorInputSink"/> that also takes the pointer's wheel (phase X's smooth scrolling): a platform's
/// <see cref="ITextInputAdapter"/> sends a sink that is one the wheel's turns over the editor, and leaves the wheel to the
/// platform's own scrolling when the sink answers false.
/// </summary>
public interface IEditorScrollSink
{
    /// <summary>
    /// The wheel turned by <paramref name="deltaX"/> and <paramref name="deltaY"/> in the wheel's units (120 a notch;
    /// positive Y scrolls toward the start, positive X toward the end of a line); <paramref name="precise"/> when the
    /// deltas are not whole notches (a touchpad's). True when the editor scrolled.
    /// </summary>
    bool OnWheel(double deltaX, double deltaY, bool precise);
}

/// <summary>
/// Connects a platform view's keyboard and input-method events to an <see cref="IEditorInputSink"/>. One per platform,
/// under the UI project's <c>Platforms/</c> folder. Dispose the result to disconnect.
/// </summary>
public interface ITextInputAdapter
{
    /// <summary>Connects <paramref name="platformView"/> (the platform's native view of the editor) to <paramref name="sink"/>.</summary>
    IDisposable Attach(object platformView, IEditorInputSink sink);
}
