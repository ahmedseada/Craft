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

/// <summary>The keys the editor handles as commands; text itself arrives through <see cref="IEditorInputSink.OnText"/>.</summary>
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
/// Connects a platform view's keyboard and input-method events to an <see cref="IEditorInputSink"/>. One per platform,
/// under the UI project's <c>Platforms/</c> folder. Dispose the result to disconnect.
/// </summary>
public interface ITextInputAdapter
{
    /// <summary>Connects <paramref name="platformView"/> (the platform's native view of the editor) to <paramref name="sink"/>.</summary>
    IDisposable Attach(object platformView, IEditorInputSink sink);
}
