// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// One change to a text: <paramref name="Length"/> characters at <paramref name="Start"/> replaced by
/// <paramref name="Text"/>. An insertion has a length of 0, a deletion an empty text.
/// </summary>
/// <param name="Start">Where the change starts, in the snapshot it is applied to.</param>
/// <param name="Length">How many characters it replaces.</param>
/// <param name="Text">What it puts there.</param>
public readonly record struct TextEdit(int Start, int Length, string Text)
{
    /// <summary>The end of the replaced range (exclusive), in the snapshot the edit is applied to.</summary>
    public int End => Start + Length;

    /// <summary>How much longer the text is after the edit (negative when shorter).</summary>
    public int Delta => Text.Length - Length;

    /// <summary>An insertion of <paramref name="text"/> at <paramref name="position"/>.</summary>
    public static TextEdit Insert(int position, string text) => new(position, 0, text);

    /// <summary>A deletion of <paramref name="length"/> characters at <paramref name="start"/>.</summary>
    public static TextEdit Delete(int start, int length) => new(start, length, "");
}

/// <summary>A line of a snapshot: where it starts, its length without its line break, and the break's length (0, 1 or 2).</summary>
/// <param name="Index">The line's index, from 0.</param>
/// <param name="Start">The position of its first character.</param>
/// <param name="Length">Its characters, without the line break.</param>
/// <param name="BreakLength">The line break's characters: 2 for "\r\n", 1 for "\n" or "\r", 0 on the last line.</param>
public readonly record struct TextLine(int Index, int Start, int Length, int BreakLength)
{
    /// <summary>The position just after its last character, before the line break.</summary>
    public int End => Start + Length;

    /// <summary>The position just after its line break (the next line's start).</summary>
    public int EndIncludingBreak => Start + Length + BreakLength;
}

/// <summary>Why a buffer's text changed.</summary>
public enum TextChangeReason
{
    /// <summary>Typed, pasted, or an accepted diff.</summary>
    Edit,

    /// <summary>An undo.</summary>
    Undo,

    /// <summary>A redo.</summary>
    Redo,

    /// <summary>The file was read again from disk.</summary>
    Reload,
}

/// <summary>
/// An immutable version of a buffer's text. Any thread may read it; it never changes. Read it by character, by line or by
/// range into a span; <see cref="GetText"/> makes a string, for the edges only (a hot path reads spans).
/// </summary>
public interface ITextSnapshot
{
    /// <summary>The buffer it is a version of.</summary>
    ITextBuffer Buffer { get; }

    /// <summary>Its version: 0 for the text the buffer was made with, one more for each change.</summary>
    int Version { get; }

    /// <summary>Its length in UTF-16 characters.</summary>
    int Length { get; }

    /// <summary>Its lines: at least 1 (an empty text is one empty line).</summary>
    int LineCount { get; }

    /// <summary>The character at <paramref name="position"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside 0 to <see cref="Length"/> - 1.</exception>
    char this[int position] { get; }

    /// <summary>The line of index <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 0 to <see cref="LineCount"/> - 1.</exception>
    TextLine GetLine(int index);

    /// <summary>The index of the line holding <paramref name="position"/> (0 to <see cref="Length"/>; a position on a line break belongs to the line it ends).</summary>
    int GetLineIndex(int position);

    /// <summary>Copies the characters from <paramref name="start"/> into <paramref name="destination"/>, as many as it holds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range runs outside the text.</exception>
    void CopyTo(int start, Span<char> destination);

    /// <summary>The characters of a range as a string.</summary>
    string GetText(int start, int length);
}

/// <summary>What a change did: the snapshot before and after it, and its edits in the positions of <see cref="Before"/>.</summary>
public sealed class TextChangedEventArgs(ITextSnapshot before, ITextSnapshot after, IReadOnlyList<TextEdit> edits, TextChangeReason reason) : EventArgs
{
    /// <summary>The text before the change.</summary>
    public ITextSnapshot Before { get; } = before;

    /// <summary>The text after it.</summary>
    public ITextSnapshot After { get; } = after;

    /// <summary>The edits, in order, non-overlapping, in the positions of <see cref="Before"/>.</summary>
    public IReadOnlyList<TextEdit> Edits { get; } = edits;

    /// <summary>Why it changed.</summary>
    public TextChangeReason Reason { get; } = reason;
}

/// <summary>
/// A text that changes: each change makes a new <see cref="ITextSnapshot"/>, and old snapshots stay valid. Changes are
/// applied on one thread (the editor's UI thread); snapshots are read on any.
/// </summary>
public interface ITextBuffer
{
    /// <summary>The current text.</summary>
    ITextSnapshot Current { get; }

    /// <summary>
    /// Applies <paramref name="edits"/> to <see cref="Current"/> as one change and returns the new snapshot. The edits are
    /// in the positions of <see cref="Current"/>, in order and non-overlapping; none changes nothing and returns
    /// <see cref="Current"/>.
    /// </summary>
    /// <exception cref="ArgumentException">An edit runs outside the text, or the edits overlap or are out of order.</exception>
    ITextSnapshot Apply(ReadOnlySpan<TextEdit> edits, TextChangeReason reason = TextChangeReason.Edit);

    /// <summary>Raised after each change, on the thread that applied it.</summary>
    event EventHandler<TextChangedEventArgs>? Changed;
}
