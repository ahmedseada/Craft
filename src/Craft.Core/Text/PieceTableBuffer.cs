// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Core.Text;

/// <summary>
/// A text buffer as a piece table: the original text (kept as it was given, never copied again) and an append-only add
/// buffer grown by chunks (<see cref="CraftOptions.BufferChunkCharacters"/>), with the pieces in a persistent balanced
/// tree that carries each subtree's length and line breaks. A change costs O(k log n) for k edits over n pieces and makes
/// a new snapshot that shares every node the change did not touch; old snapshots stay valid and readable from any
/// thread. Reading a character, a line, a line's index or a range is O(log n). Line breaks are "\r\n", "\n" and "\r"
/// ("\r\n" split across pieces is still one break). Changes are applied on one thread.
/// </summary>
public sealed class PieceTableBuffer : ITextBuffer
{
    private readonly int _chunkCharacters;
    private TextChunk? _add;
    private volatile Snapshot _current;

    /// <summary>A buffer holding <paramref name="text"/> (kept as the string it is).</summary>
    public PieceTableBuffer(string text) : this(text, null)
    {
    }

    /// <summary>A buffer holding <paramref name="text"/>, its add buffer grown by <paramref name="options"/>' chunk size.</summary>
    public PieceTableBuffer(string text, CraftOptions? options) : this(TextChunk.FromString(text ?? throw new ArgumentNullException(nameof(text))), options)
    {
    }

    /// <summary>A buffer holding a copy of <paramref name="text"/>.</summary>
    public PieceTableBuffer(ReadOnlySpan<char> text) : this(text, null)
    {
    }

    /// <summary>A buffer holding a copy of <paramref name="text"/>, its add buffer grown by <paramref name="options"/>' chunk size.</summary>
    public PieceTableBuffer(ReadOnlySpan<char> text, CraftOptions? options) : this(TextChunk.FromArray(text.ToArray()), options)
    {
    }

    private PieceTableBuffer(TextChunk original, CraftOptions? options)
    {
        _chunkCharacters = (options ?? Defaults).BufferChunkCharacters;
        _current = new Snapshot(this, 0, original.Used == 0 ? null : PieceNode.Leaf(WholeOf(original)));
    }

    /// <summary>A buffer that takes <paramref name="chars"/> as its original text without copying it (the caller gives it up).</summary>
    internal static PieceTableBuffer FromOwnedArray(char[] chars, CraftOptions? options) => new(TextChunk.FromArray(chars), options);

    private static readonly CraftOptions Defaults = new();

    /// <inheritdoc />
    public ITextSnapshot Current => _current;

    /// <inheritdoc />
    public event EventHandler<TextChangedEventArgs>? Changed;

    /// <inheritdoc />
    public ITextSnapshot Apply(ReadOnlySpan<TextEdit> edits, TextChangeReason reason = TextChangeReason.Edit)
    {
        var before = _current;
        int length = before.Length;
        int previousEnd = 0;
        bool changes = false;
        for (int i = 0; i < edits.Length; i++)
        {
            ref readonly var edit = ref edits[i];
            if (edit.Text is null)
            {
                throw new ArgumentException($"Edit {i} has no text (use \"\" for a deletion).", nameof(edits));
            }

            if (edit.Start < 0 || edit.Length < 0 || edit.Start > length || edit.Length > length - edit.Start)
            {
                throw new ArgumentException($"Edit {i} ({edit.Start}, {edit.Length}) runs outside the text of {length} characters.", nameof(edits));
            }

            if (edit.Start < previousEnd)
            {
                throw new ArgumentException($"Edit {i} starts at {edit.Start}, before the end of edit {i - 1} ({previousEnd}): edits must be in order and must not overlap.", nameof(edits));
            }

            previousEnd = edit.End;
            changes |= edit.Length > 0 || edit.Text.Length > 0;
        }

        if (!changes)
        {
            return before;
        }

        var after = new Snapshot(this, before.Version + 1, Build(before.Root, edits));
        _current = after;
        Changed?.Invoke(this, new TextChangedEventArgs(before, after, edits.ToArray(), reason));
        return after;
    }

    private PieceNode? Build(PieceNode? root, ReadOnlySpan<TextEdit> edits)
    {
        if (edits.Length == 1 && root is not null)
        {
            // The keystroke's paths: typing that continues the piece before the caret, and a deletion at a piece's edge.
            ref readonly var edit = ref edits[0];
            if (edit.Length == 0)
            {
                var inserted = AppendText(edit.Text)!;
                if (inserted.Left is null && inserted.Right is null && edit.Start > 0 && PieceNode.TryExtendAt(root, edit.Start, inserted.Piece) is { } extended)
                {
                    return extended;
                }

                var (head, tail) = PieceNode.Split(root, edit.Start);
                return PieceNode.Concat(PieceNode.Concat(head, inserted), tail);
            }

            if (edit.Text.Length == 0 && PieceNode.TryTrim(root, edit.Start, edit.Length) is { } trimmed)
            {
                return trimmed;
            }
        }

        PieceNode? result = null;
        PieceNode? rest = root;
        int consumed = 0;
        foreach (ref readonly var edit in edits)
        {
            var (kept, tail) = PieceNode.Split(rest, edit.Start - consumed);
            (_, rest) = PieceNode.Split(tail, edit.Length);
            consumed = edit.End;
            result = PieceNode.Concat(result, kept);
            if (edit.Text.Length > 0)
            {
                result = PieceNode.Concat(result, AppendText(edit.Text));
            }
        }

        return PieceNode.Concat(result, rest);
    }

    private static Piece WholeOf(TextChunk chunk) => Piece.Of(chunk, 0, chunk.Used);

    private PieceNode? AppendText(string text)
    {
        PieceNode? pieces = null;
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            int start = 0, taken = 0;
            if (_add is not null)
            {
                taken = _add.Append(rest, out start);
            }

            if (taken == 0)
            {
                _add = TextChunk.ForAppend(Math.Max(_chunkCharacters, rest.Length + 1));
                taken = _add.Append(rest, out start);
            }

            pieces = PieceNode.Concat(pieces, PieceNode.Leaf(Piece.Of(_add!, start, taken)));
            rest = rest[taken..];
        }

        return pieces;
    }

    /// <summary>An immutable version of the buffer: the root of its piece tree.</summary>
    private sealed class Snapshot(PieceTableBuffer buffer, int version, PieceNode? root) : ITextSnapshot
    {
        public PieceNode? Root { get; } = root;

        public ITextBuffer Buffer => buffer;

        public int Version { get; } = version;

        public int Length => PieceNode.LengthOf(Root);

        public int LineCount => (Root?.Breaks ?? 0) + 1;

        public char this[int position]
        {
            get
            {
                if ((uint)position >= (uint)Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(position), position, $"The position is outside the text of {Length} characters.");
                }

                return PieceNode.CharAt(Root!, position);
            }
        }

        public TextLine GetLine(int index)
        {
            int count = LineCount;
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The line index is outside the text's {count} lines.");
            }

            int start = index == 0 ? 0 : PieceNode.BreakEnd(Root!, index);
            if (index == count - 1)
            {
                return new TextLine(index, start, Length - start, 0);
            }

            int next = PieceNode.BreakEnd(Root!, index + 1);
            int breakLength = next - start >= 2 && PieceNode.CharAt(Root!, next - 1) == '\n' && PieceNode.CharAt(Root!, next - 2) == '\r' ? 2 : 1;
            return new TextLine(index, start, next - breakLength - start, breakLength);
        }

        public int GetLineIndex(int position)
        {
            int length = Length;
            if ((uint)position > (uint)length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, $"The position is outside 0 to {length}.");
            }

            int breaks = PieceNode.BreaksInPrefix(Root, position);
            if (position > 0 && position < length && PieceNode.CharAt(Root!, position) == '\n' && PieceNode.CharAt(Root!, position - 1) == '\r')
            {
                breaks--;
            }

            return breaks;
        }

        public void CopyTo(int start, Span<char> destination)
        {
            if (start < 0 || start > Length - destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {destination.Length} characters at {start} runs outside the text of {Length}.");
            }

            PieceNode.CopyTo(Root, start, destination);
        }

        public string GetText(int start, int length)
        {
            if (start < 0 || length < 0 || start > Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {Length}.");
            }

            return length == 0 ? "" : string.Create(length, (Root, start), static (span, state) => PieceNode.CopyTo(state.Root, state.start, span));
        }

        public override string ToString() => GetText(0, Length);
    }
}
