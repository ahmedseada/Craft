// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>A selection: where it was started (<paramref name="Anchor"/>) and where the caret is; equal when nothing is selected.</summary>
/// <param name="Anchor">The fixed end.</param>
/// <param name="Caret">The moving end, where the caret is drawn.</param>
public readonly record struct SelectionState(int Anchor, int Caret)
{
    /// <summary>The smaller end.</summary>
    public int Start => Math.Min(Anchor, Caret);

    /// <summary>The larger end.</summary>
    public int End => Math.Max(Anchor, Caret);

    /// <summary>The selected characters (0 when nothing is selected).</summary>
    public int Length => End - Start;

    /// <summary>Whether nothing is selected.</summary>
    public bool IsEmpty => Anchor == Caret;

    /// <summary>A caret at <paramref name="position"/> with nothing selected.</summary>
    public static SelectionState At(int position) => new(position, position);
}

/// <summary>What kind of edit it is: typing and deleting group into one undo step while the pauses stay short.</summary>
public enum EditKind
{
    /// <summary>Typed characters.</summary>
    Typing,

    /// <summary>Backspace or Delete.</summary>
    Deleting,

    /// <summary>A paste or a drop.</summary>
    Paste,

    /// <summary>Anything else (a cut, a replace, an indent, an accepted diff): always its own undo step.</summary>
    Command,
}

/// <summary>
/// An open document: its buffer, its file, whether it is saved, and its undo history. Edits go through
/// <see cref="Edit"/> (never straight to the buffer), so every change is undoable and restores the selection it came with.
/// Used on the UI thread.
/// </summary>
public interface ITextDocument : IDisposable
{
    /// <summary>The text.</summary>
    ITextBuffer Buffer { get; }

    /// <summary>The file it was read from or saved to; null for a new document.</summary>
    string? FilePath { get; }

    /// <summary>Whether its text differs from the file's (or it is new and not empty).</summary>
    bool IsDirty { get; }

    /// <summary>Whether <see cref="Undo"/> has a step to undo.</summary>
    bool CanUndo { get; }

    /// <summary>Whether <see cref="Redo"/> has a step to redo.</summary>
    bool CanRedo { get; }

    /// <summary>Raised when <see cref="FilePath"/>, <see cref="IsDirty"/>, <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Applies <paramref name="edits"/> (in the positions of the current snapshot, in order, non-overlapping) as one change
    /// and records it for undo with the selection before and after it. Consecutive edits of the same
    /// <see cref="EditKind.Typing"/> or <see cref="EditKind.Deleting"/> kind join one undo step while the pause between
    /// them is shorter than the document's grouping setting.
    /// </summary>
    ITextSnapshot Edit(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState before, SelectionState after);

    /// <summary>Undoes the last step; returns the selection to restore, or null when there was nothing to undo.</summary>
    SelectionState? Undo();

    /// <summary>Redoes the last undone step; returns the selection to restore, or null when there was nothing to redo.</summary>
    SelectionState? Redo();

    /// <summary>Saves to <see cref="FilePath"/> (or <paramref name="path"/>, which becomes the file), keeping its encoding and line endings.</summary>
    /// <exception cref="InvalidOperationException">The document has no file and no path was given.</exception>
    Task SaveAsync(string? path = null, CancellationToken cancellationToken = default);
}
