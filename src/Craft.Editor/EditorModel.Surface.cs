// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>What the pointer did over an editor, for a host that gives the text's places a meaning of its own (X part 10).</summary>
public enum EditorPointerKind
{
    /// <summary>The pointer rests over <see cref="EditorPointerEventArgs.Position"/>.</summary>
    Hover,

    /// <summary>The pointer left the editor.</summary>
    Exit,

    /// <summary>A press and release at one place, nothing dragged (a click).</summary>
    Click,
}

/// <summary>A pointer's move, exit or click over an editor: the text position under it.</summary>
public sealed class EditorPointerEventArgs(EditorPointerKind kind, int position, KeyModifiers modifiers = KeyModifiers.None) : EventArgs
{
    /// <summary>What happened.</summary>
    public EditorPointerKind Kind { get; } = kind;

    /// <summary>The text position under the pointer (-1 on <see cref="EditorPointerKind.Exit"/>).</summary>
    public int Position { get; } = position;

    /// <summary>The keys held at the click.</summary>
    public KeyModifiers Modifiers { get; } = modifiers;
}

/// <summary>
/// Fills drawn behind a line's text (and its selection), for a read-only surface whose lines have a shape of their own (the
/// conversation's bubbles and code blocks, X part 10). Called by the renderer for each shown line, inside the frame: it
/// must allocate nothing.
/// </summary>
public interface ILineBackdrop
{
    /// <summary>Draws <paramref name="line"/>'s fills at <paramref name="left"/> (the text's left, scrolled) and <paramref name="top"/>.</summary>
    void DrawLine(IEditorCanvas canvas, int line, float left, float top, float cellWidth, float height, float viewportWidth);
}

public sealed partial class EditorModel
{
    private int _pressPosition = -1;
    private KeyModifiers _pressModifiers;
    private int _pointerOver = -1;

    /// <summary>Fills drawn behind each line's text (the conversation's bubbles and code blocks), or null for none.</summary>
    public ILineBackdrop? LineBackdrop { get; set; }

    /// <summary>Raised when the pointer moves to another text position, leaves, or clicks without dragging (on the UI thread).</summary>
    public event EventHandler<EditorPointerEventArgs>? PointerActivity;

    // A press: where it was, so the release can tell a click from a drag.
    private void NotePress(float viewX, float viewY, KeyModifiers modifiers)
    {
        if (PointerActivity is null)
        {
            return;
        }

        _pressPosition = viewX < TextLeft ? -1 : PositionFromView(viewX, viewY);
        _pressModifiers = modifiers;
    }

    // A release: a click when the selection is still empty at the press's place (nothing was dragged).
    private void NoteRelease()
    {
        int at = _pressPosition;
        _pressPosition = -1;
        if (at >= 0 && PointerActivity is { } activity && _selection.IsEmpty && _selection.Caret == at && (_pressModifiers & KeyModifiers.Shift) == 0)
        {
            activity(this, new EditorPointerEventArgs(EditorPointerKind.Click, at, _pressModifiers));
        }
    }

    // A hover: raised once per position.
    private void NoteHover(float viewX, float viewY)
    {
        if (PointerActivity is not { } activity)
        {
            return;
        }

        int position = viewX < TextLeft ? -1 : PositionFromView(viewX, viewY);
        if (position != _pointerOver)
        {
            _pointerOver = position;
            activity(this, new EditorPointerEventArgs(EditorPointerKind.Hover, position));
        }
    }

    private void NoteExit()
    {
        _pointerOver = -1;
        PointerActivity?.Invoke(this, new EditorPointerEventArgs(EditorPointerKind.Exit, -1));
    }
}
