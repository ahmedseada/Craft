// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;

namespace Craft.Core.Text;

/// <summary>
/// A run of a chunk's characters, with its line breaks counted as if it stood alone: "\r\n" is one break, a "\r" at its
/// end is one. <see cref="FirstLineStart"/> and <see cref="LineStartCount"/> index the chunk's line starts that fall in
/// it; a "\r" at its end that the chunk follows with "\n" is the one break those do not hold (<see cref="TrailingBreak"/>).
/// </summary>
internal readonly struct Piece
{
    public const byte StartsWithLineFeed = 1;
    public const byte EndsWithCarriageReturn = 2;
    public const byte TrailingBreak = 4;

    public readonly TextChunk Chunk;
    public readonly int Start;
    public readonly int Length;
    public readonly int FirstLineStart;
    public readonly int LineStartCount;
    public readonly byte Flags;

    private Piece(TextChunk chunk, int start, int length, int firstLineStart, int lineStartCount, byte flags)
    {
        Chunk = chunk;
        Start = start;
        Length = length;
        FirstLineStart = firstLineStart;
        LineStartCount = lineStartCount;
        Flags = flags;
    }

    /// <summary>Its breaks, standing alone.</summary>
    public int Breaks => LineStartCount + ((Flags & TrailingBreak) != 0 ? 1 : 0);

    public bool StartsLf => (Flags & StartsWithLineFeed) != 0;

    public bool EndsCr => (Flags & EndsWithCarriageReturn) != 0;

    /// <summary>The piece of <paramref name="length"/> characters of <paramref name="chunk"/> at <paramref name="start"/> (on the buffer's thread).</summary>
    public static Piece Of(TextChunk chunk, int start, int length)
    {
        int first = chunk.FirstLineStartAtOrAfter(start + 1);
        int after = chunk.FirstLineStartAtOrAfter(start + length + 1);
        int end = start + length;
        byte flags = 0;
        if (chunk[start] == '\n')
        {
            flags |= StartsWithLineFeed;
        }

        if (chunk[end - 1] == '\r')
        {
            flags |= EndsWithCarriageReturn;
            if (end < chunk.Used && chunk[end] == '\n')
            {
                flags |= TrailingBreak;
            }
        }

        return new Piece(chunk, start, length, first, after - first, flags);
    }

    /// <summary>The span of its characters.</summary>
    public ReadOnlySpan<char> Text => Chunk.Slice(Start, Length);

    /// <summary>The breaks among its first <paramref name="count"/> characters, standing alone (1 to <see cref="Length"/>).</summary>
    public int BreaksInPrefix(int count)
    {
        if (count >= Length)
        {
            return Breaks;
        }

        var starts = Chunk.LineStarts.AsSpan(FirstLineStart, LineStartCount);
        int end = Start + count;
        int inside = TextChunk.LowerBound(starts, end + 1);
        if (Chunk[end - 1] == '\r' && Chunk[end] == '\n')
        {
            inside++;
        }

        return inside;
    }

    /// <summary>The offset just after its <paramref name="index"/>-th break (from 1), standing alone.</summary>
    public int BreakEnd(int index) => index <= LineStartCount ? Chunk.LineStarts[FirstLineStart + index - 1] - Start : Length;

    /// <summary>Whether <paramref name="next"/> continues it in the same chunk (the two can be one piece).</summary>
    public bool IsContinuedBy(in Piece next) => ReferenceEquals(Chunk, next.Chunk) && Start + Length == next.Start;
}

/// <summary>
/// An immutable node of a persistent AVL tree of pieces, in text order, with its subtree's length and line breaks. A
/// change builds new nodes along the paths it touches and shares the rest, so every snapshot keeps its own tree.
/// </summary>
internal sealed class PieceNode
{
    private const byte SubtreeStartsLf = 1;
    private const byte SubtreeEndsCr = 2;

    public readonly PieceNode? Left;
    public readonly PieceNode? Right;
    public readonly Piece Piece;
    public readonly int Length;
    public readonly int Breaks;
    public readonly byte Height;
    private readonly byte _flags;

    private PieceNode(PieceNode? left, in Piece piece, PieceNode? right)
    {
        Left = left;
        Right = right;
        Piece = piece;
        Length = (left?.Length ?? 0) + piece.Length + (right?.Length ?? 0);
        int breaks = piece.Breaks;
        bool startsLf = piece.StartsLf;
        bool endsCr = piece.EndsCr;
        if (left is not null)
        {
            breaks += left.Breaks - (left.EndsCr && piece.StartsLf ? 1 : 0);
            startsLf = left.StartsLf;
        }

        if (right is not null)
        {
            breaks += right.Breaks - (piece.EndsCr && right.StartsLf ? 1 : 0);
            endsCr = right.EndsCr;
        }

        Breaks = breaks;
        Height = (byte)(1 + Math.Max(HeightOf(left), HeightOf(right)));
        _flags = (byte)((startsLf ? SubtreeStartsLf : 0) | (endsCr ? SubtreeEndsCr : 0));
    }

    public bool StartsLf => (_flags & SubtreeStartsLf) != 0;

    public bool EndsCr => (_flags & SubtreeEndsCr) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HeightOf(PieceNode? node) => node?.Height ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LengthOf(PieceNode? node) => node?.Length ?? 0;

    public static PieceNode Leaf(in Piece piece) => new(null, piece, null);

    // ---- building (the buffer's thread) ----

    /// <summary>The tree of <paramref name="left"/>, then <paramref name="piece"/>, then <paramref name="right"/>, balanced.</summary>
    public static PieceNode Join(PieceNode? left, in Piece piece, PieceNode? right)
    {
        int hl = HeightOf(left), hr = HeightOf(right);
        if (hl > hr + 1)
        {
            return JoinRight(left!, piece, right);
        }

        if (hr > hl + 1)
        {
            return JoinLeft(left, piece, right!);
        }

        return new PieceNode(left, piece, right);
    }

    /// <summary>The tree of <paramref name="left"/> then <paramref name="right"/>; a piece that continues the one before it in its chunk is merged with it.</summary>
    public static PieceNode? Concat(PieceNode? left, PieceNode? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        var rest = RemoveFirst(right, out var first);
        ref readonly var last = ref Last(left).Piece;
        if (last.IsContinuedBy(first))
        {
            var merged = Piece.Of(last.Chunk, last.Start, last.Length + first.Length);
            return Join(RemoveLast(left, out _), merged, rest);
        }

        return Join(left, first, rest);
    }

    /// <summary>Splits <paramref name="node"/> into its first <paramref name="position"/> characters and the rest.</summary>
    public static (PieceNode? Before, PieceNode? After) Split(PieceNode? node, int position)
    {
        if (node is null)
        {
            return (null, null);
        }

        if (position <= 0)
        {
            return (null, node);
        }

        if (position >= node.Length)
        {
            return (node, null);
        }

        int leftLength = LengthOf(node.Left);
        if (position <= leftLength)
        {
            var (a, b) = Split(node.Left, position);
            return (a, Join(b, node.Piece, node.Right));
        }

        int pieceEnd = leftLength + node.Piece.Length;
        if (position >= pieceEnd)
        {
            var (a, b) = Split(node.Right, position - pieceEnd);
            return (Join(node.Left, node.Piece, a), b);
        }

        int cut = position - leftLength;
        ref readonly var piece = ref node.Piece;
        var head = Piece.Of(piece.Chunk, piece.Start, cut);
        var tail = Piece.Of(piece.Chunk, piece.Start + cut, piece.Length - cut);
        return (Join(node.Left, head, null), Join(null, tail, node.Right));
    }

    /// <summary>
    /// The tree with the piece that ends at <paramref name="position"/> extended by <paramref name="next"/>, when
    /// <paramref name="next"/> continues it in its chunk (typing after typing): one path of new nodes, no rebalancing.
    /// Null when no piece ends there or it does not continue.
    /// </summary>
    public static PieceNode? TryExtendAt(PieceNode node, int position, in Piece next)
    {
        int leftLength = LengthOf(node.Left);
        if (position <= leftLength)
        {
            return node.Left is not null && TryExtendAt(node.Left, position, next) is { } left ? new PieceNode(left, node.Piece, node.Right) : null;
        }

        int pieceEnd = leftLength + node.Piece.Length;
        if (position == pieceEnd)
        {
            ref readonly var piece = ref node.Piece;
            return piece.IsContinuedBy(next) ? new PieceNode(node.Left, Piece.Of(piece.Chunk, piece.Start, piece.Length + next.Length), node.Right) : null;
        }

        if (position > pieceEnd && node.Right is not null && TryExtendAt(node.Right, position - pieceEnd, next) is { } right)
        {
            return new PieceNode(node.Left, node.Piece, right);
        }

        return null;
    }

    /// <summary>
    /// The tree with <paramref name="length"/> characters at <paramref name="start"/> removed, when they lie at the start
    /// or the end of one piece and leave some of it (a backspace, a delete): one path of new nodes, no rebalancing. Null
    /// otherwise.
    /// </summary>
    public static PieceNode? TryTrim(PieceNode node, int start, int length)
    {
        int leftLength = LengthOf(node.Left);
        if (start < leftLength)
        {
            return start + length <= leftLength && TryTrim(node.Left!, start, length) is { } left ? new PieceNode(left, node.Piece, node.Right) : null;
        }

        ref readonly var piece = ref node.Piece;
        int offset = start - leftLength;
        if (offset >= piece.Length)
        {
            return node.Right is not null && TryTrim(node.Right, offset - piece.Length, length) is { } right ? new PieceNode(node.Left, node.Piece, right) : null;
        }

        if (length >= piece.Length || offset + length > piece.Length)
        {
            return null;
        }

        if (offset == 0)
        {
            return new PieceNode(node.Left, Piece.Of(piece.Chunk, piece.Start + length, piece.Length - length), node.Right);
        }

        return offset + length == piece.Length ? new PieceNode(node.Left, Piece.Of(piece.Chunk, piece.Start, piece.Length - length), node.Right) : null;
    }

    private static PieceNode Last(PieceNode node)
    {
        while (node.Right is not null)
        {
            node = node.Right;
        }

        return node;
    }

    private static PieceNode? RemoveFirst(PieceNode node, out Piece first)
    {
        if (node.Left is null)
        {
            first = node.Piece;
            return node.Right;
        }

        var left = RemoveFirst(node.Left, out first);
        return Balance(left, node.Piece, node.Right);
    }

    private static PieceNode? RemoveLast(PieceNode node, out Piece last)
    {
        if (node.Right is null)
        {
            last = node.Piece;
            return node.Left;
        }

        var right = RemoveLast(node.Right, out last);
        return Balance(node.Left, node.Piece, right);
    }

    private static PieceNode JoinRight(PieceNode left, in Piece piece, PieceNode? right)
    {
        var inner = HeightOf(left.Right) <= HeightOf(right) + 1
            ? new PieceNode(left.Right, piece, right)
            : JoinRight(left.Right!, piece, right);
        return Balance(left.Left, left.Piece, inner);
    }

    private static PieceNode JoinLeft(PieceNode? left, in Piece piece, PieceNode right)
    {
        var inner = HeightOf(right.Left) <= HeightOf(left) + 1
            ? new PieceNode(left, piece, right.Left)
            : JoinLeft(left, piece, right.Left!);
        return Balance(inner, right.Piece, right.Right);
    }

    private static PieceNode Balance(PieceNode? left, in Piece piece, PieceNode? right)
    {
        int hl = HeightOf(left), hr = HeightOf(right);
        if (hr > hl + 1)
        {
            var r = right!;
            if (HeightOf(r.Left) > HeightOf(r.Right))
            {
                var rl = r.Left!;
                return new PieceNode(new PieceNode(left, piece, rl.Left), rl.Piece, new PieceNode(rl.Right, r.Piece, r.Right));
            }

            return new PieceNode(new PieceNode(left, piece, r.Left), r.Piece, r.Right);
        }

        if (hl > hr + 1)
        {
            var l = left!;
            if (HeightOf(l.Right) > HeightOf(l.Left))
            {
                var lr = l.Right!;
                return new PieceNode(new PieceNode(l.Left, l.Piece, lr.Left), lr.Piece, new PieceNode(lr.Right, piece, right));
            }

            return new PieceNode(l.Left, l.Piece, new PieceNode(l.Right, piece, right));
        }

        return new PieceNode(left, piece, right);
    }

    // ---- reading (any thread) ----

    /// <summary>The character at <paramref name="position"/> (inside the tree).</summary>
    public static char CharAt(PieceNode node, int position)
    {
        while (true)
        {
            int leftLength = LengthOf(node.Left);
            if (position < leftLength)
            {
                node = node.Left!;
                continue;
            }

            position -= leftLength;
            if (position < node.Piece.Length)
            {
                return node.Piece.Chunk[node.Piece.Start + position];
            }

            position -= node.Piece.Length;
            node = node.Right!;
        }
    }

    /// <summary>Copies <paramref name="destination"/>'s length of characters from <paramref name="start"/> (all inside the tree).</summary>
    public static void CopyTo(PieceNode? node, int start, Span<char> destination)
    {
        while (!destination.IsEmpty && node is not null)
        {
            int leftLength = LengthOf(node.Left);
            if (start < leftLength)
            {
                int take = Math.Min(destination.Length, leftLength - start);
                CopyTo(node.Left, start, destination[..take]);
                destination = destination[take..];
                start = leftLength;
                if (destination.IsEmpty)
                {
                    return;
                }
            }

            int offset = start - leftLength;
            ref readonly var piece = ref node.Piece;
            if (offset < piece.Length)
            {
                int take = Math.Min(destination.Length, piece.Length - offset);
                piece.Chunk.Slice(piece.Start + offset, take).CopyTo(destination);
                destination = destination[take..];
                offset += take;
            }

            start = offset - piece.Length;
            node = node.Right;
        }
    }

    /// <summary>
    /// The breaks among the tree's first <paramref name="count"/> characters, counted as if those stood alone (a "\r" at
    /// their end is a break even when a "\n" follows it).
    /// </summary>
    public static int BreaksInPrefix(PieceNode? node, int count)
    {
        int breaks = 0;
        bool afterCr = false;
        while (node is not null && count > 0)
        {
            int leftLength = LengthOf(node.Left);
            if (count <= leftLength)
            {
                node = node.Left;
                continue;
            }

            if (node.Left is { } left)
            {
                breaks += left.Breaks - (afterCr && left.StartsLf ? 1 : 0);
                afterCr = left.EndsCr;
            }

            count -= leftLength;
            ref readonly var piece = ref node.Piece;
            int take = Math.Min(count, piece.Length);
            breaks += piece.BreaksInPrefix(take) - (afterCr && piece.StartsLf ? 1 : 0);
            afterCr = take == piece.Length ? piece.EndsCr : piece.Chunk[piece.Start + take - 1] == '\r';
            count -= take;
            node = node.Right;
        }

        return breaks;
    }

    /// <summary>The position just after the tree's <paramref name="index"/>-th line break (from 1 to <see cref="Breaks"/>).</summary>
    public static int BreakEnd(PieceNode root, int index)
    {
        int position = 0;
        bool nextLf = false;
        PieceNode? node = root;
        while (node is not null)
        {
            ref readonly var piece = ref node.Piece;
            var left = node.Left;
            int leftBreaks = left is null ? 0 : left.Breaks - (left.EndsCr && piece.StartsLf ? 1 : 0);
            if (index <= leftBreaks)
            {
                nextLf = piece.StartsLf;
                node = left;
                continue;
            }

            index -= leftBreaks;
            position += LengthOf(left);
            bool followedByLf = node.Right is { } right ? right.StartsLf : nextLf;
            int pieceBreaks = piece.Breaks - (piece.EndsCr && followedByLf ? 1 : 0);
            if (index <= pieceBreaks)
            {
                return position + piece.BreakEnd(index);
            }

            index -= pieceBreaks;
            position += piece.Length;
            node = node.Right;
        }

        throw new InvalidOperationException("The piece tree's line counts are inconsistent (a bug in the piece table).");
    }
}
