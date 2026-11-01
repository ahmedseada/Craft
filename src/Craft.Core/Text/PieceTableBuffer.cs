// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Core.Text;

/// <summary>
/// A text buffer as a piece table: the original text (kept as it was given, never copied again) and an append-only add
/// buffer grown by chunks (<see cref="CraftOptions.BufferChunkCharacters"/>), with the pieces in a persistent balanced
/// tree that carries each subtree's length and line breaks. A change costs O(k log n) for k edits over n pieces and makes
/// a new snapshot that shares every node the change did not touch; old snapshots stay valid and readable from any
/// thread. Reading a character, a line, a line's index or a range is O(log n). Line breaks are "\r\n", "\n" and "\r"
/// ("\r\n" split across pieces is still one break). Changes are applied on one thread.
/// <para>
/// A change of many edits (typing at many carets, X restart piece 2a) is built in one batch from
/// <see cref="CraftOptions.PieceBatchMinEdits"/> edits: the tree is split once before the first edit and once after the
/// last, the middle flattened into a pooled array, every inserted text appended to the add buffer in one run, continuing
/// pieces joined, an insertion next to a short piece of the add buffer copied with it into one piece while the two stay
/// within <see cref="CraftOptions.PieceCoalesceCharacters"/> (so a caret's typing stays one piece, not one a keystroke),
/// and the middle rebuilt balanced. A middle of more than <see cref="CraftOptions.PieceBatchPiecesPerEdit"/> pieces an
/// edit (carets far apart in a much-edited file) takes the per-edit path, whose cost does not grow with the middle.
/// </para>
/// </summary>
public sealed class PieceTableBuffer : ITextBuffer
{
    private readonly int _chunkCharacters;
    private readonly int _coalesceCharacters;
    private readonly int _batchMinEdits;
    private readonly int _batchPiecesPerEdit;
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
        options ??= Defaults;
        _chunkCharacters = options.BufferChunkCharacters;
        _coalesceCharacters = options.PieceCoalesceCharacters;
        _batchMinEdits = Math.Max(1, options.PieceBatchMinEdits);
        _batchPiecesPerEdit = Math.Max(1, options.PieceBatchPiecesPerEdit);
        _current = new Snapshot(this, 0, original.Used == 0 ? null : PieceNode.Leaf(WholeOf(original)));
    }

    /// <summary>A buffer that takes <paramref name="chars"/> as its original text without copying it (the caller gives it up).</summary>
    internal static PieceTableBuffer FromOwnedArray(char[] chars, CraftOptions? options) => new(TextChunk.FromArray(chars), options);

    private static readonly CraftOptions Defaults = new();

    /// <summary>The characters appended to the add buffer since the buffer was made (typed, pasted, and copied by coalescing).</summary>
    internal long AddedCharacters { get; private set; }

    /// <summary>The changes built in one batch (<see cref="CraftOptions.PieceBatchMinEdits"/>).</summary>
    internal long BatchedChanges { get; private set; }

    /// <summary>The pieces of <paramref name="snapshot"/>'s tree (walks it: tests and telemetry).</summary>
    internal static int PieceCount(ITextSnapshot snapshot) => snapshot is Snapshot own ? PieceNode.CountPieces(own.Root) : 0;

    /// <summary>What is wrong with <paramref name="snapshot"/>'s tree (height, balance, lengths, line breaks), or null.</summary>
    internal static string? TreeProblem(ITextSnapshot snapshot) => snapshot is Snapshot own ? PieceNode.Problem(own.Root) : "not a piece table's snapshot";

    /// <summary>The height of <paramref name="snapshot"/>'s tree.</summary>
    internal static int TreeHeight(ITextSnapshot snapshot) => snapshot is Snapshot own ? PieceNode.HeightOf(own.Root) : 0;

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

        if (edits.Length >= _batchMinEdits && root is not null && TryBuildBatch(root, edits, out var batched))
        {
            BatchedChanges++;
            return batched;
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

    // The batch: one split before the first edit and one after the last; the middle flattened, edited as an array of
    // pieces and rebuilt balanced. False when the middle holds more pieces than the batch pays for (the per-edit path then).
    private bool TryBuildBatch(PieceNode root, ReadOnlySpan<TextEdit> edits, out PieceNode? result)
    {
        // The middle starts up to the coalescing limit before the first edit, so the first caret's typing so far is in it
        // and joins too (the seam's halves of a piece cut there join again in the Concat below).
        int from = Math.Max(0, edits[0].Start - _coalesceCharacters);
        int to = edits[^1].End;
        var (head, rest) = PieceNode.Split(root, from);
        var (middle, tail) = PieceNode.Split(rest, to - from);
        var source = ArrayPool<Piece>.Shared.Rent(Math.Max(16, edits.Length * 2));
        var pieces = new PieceRun(ArrayPool<Piece>.Shared.Rent(Math.Max(16, edits.Length * 4)));
        try
        {
            int count = PieceNode.Flatten(middle, ref source, 0);
            if (count > (long)edits.Length * _batchPiecesPerEdit)
            {
                result = null;
                return false;
            }

            int index = 0;
            int offset = 0;                      // into source[index]
            int position = from;
            foreach (ref readonly var edit in edits)
            {
                // The kept text up to the edit, then the edit's removed text skipped.
                while (position < edit.Start)
                {
                    ref readonly var piece = ref source[index];
                    int take = Math.Min(piece.Length - offset, edit.Start - position);
                    pieces.Add(take == piece.Length ? piece : Piece.Of(piece.Chunk, piece.Start + offset, take));
                    offset += take;
                    position += take;
                    if (offset == piece.Length)
                    {
                        index++;
                        offset = 0;
                    }
                }

                int skip = edit.Length;
                while (skip > 0)
                {
                    int take = Math.Min(source[index].Length - offset, skip);
                    offset += take;
                    skip -= take;
                    position += take;
                    if (offset == source[index].Length)
                    {
                        index++;
                        offset = 0;
                    }
                }

                if (edit.Text.Length > 0)
                {
                    // A short piece of the add buffer just before (this caret's typing so far) goes again with the new text.
                    if (pieces.Count > 0 && pieces.Last.Chunk.IsAdd && pieces.Last.Length + edit.Text.Length <= _coalesceCharacters)
                    {
                        var previous = pieces.RemoveLast();
                        AppendRun(previous.Text, ref pieces);
                    }

                    AppendRun(edit.Text, ref pieces);
                }
            }

            var rebuilt = PieceNode.FromSorted(pieces.Array.AsSpan(0, pieces.Count));
            result = PieceNode.Concat(PieceNode.Concat(head, rebuilt), tail);
            return true;
        }
        finally
        {
            ArrayPool<Piece>.Shared.Return(source, clearArray: true);
            ArrayPool<Piece>.Shared.Return(pieces.Array, clearArray: true);
        }
    }

    // Appends `text` to the add buffer (a new block when the last is full) and its pieces to `pieces`.
    private void AppendRun(ReadOnlySpan<char> text, ref PieceRun pieces)
    {
        while (!text.IsEmpty)
        {
            int start = 0, taken = 0;
            if (_add is not null)
            {
                taken = _add.Append(text, out start);
            }

            if (taken == 0)
            {
                _add = TextChunk.ForAppend(Math.Max(_chunkCharacters, text.Length + 1));
                taken = _add.Append(text, out start);
            }

            pieces.Add(Piece.Of(_add!, start, taken));
            AddedCharacters += taken;
            text = text[taken..];
        }
    }

    // The batch's pieces in order, in a pooled array that grows by renting (the old one returned); a piece that continues
    // the last one in its chunk joins it.
    private struct PieceRun(Piece[] array)
    {
        public Piece[] Array = array;
        public int Count;

        public readonly ref readonly Piece Last => ref Array[Count - 1];

        public void Add(in Piece piece)
        {
            if (Count > 0)
            {
                ref var last = ref Array[Count - 1];
                if (last.IsContinuedBy(piece))
                {
                    last = Piece.Of(last.Chunk, last.Start, last.Length + piece.Length);
                    return;
                }
            }

            if (Count == Array.Length)
            {
                var bigger = ArrayPool<Piece>.Shared.Rent(Array.Length * 2);
                Array.AsSpan(0, Count).CopyTo(bigger);
                ArrayPool<Piece>.Shared.Return(Array, clearArray: true);
                Array = bigger;
            }

            Array[Count++] = piece;
        }

        public Piece RemoveLast() => Array[--Count];
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
            AddedCharacters += taken;
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
