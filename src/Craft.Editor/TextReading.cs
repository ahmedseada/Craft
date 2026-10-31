// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>The kind of a character for word moves and word selection.</summary>
internal enum CharKind
{
    Space,
    Word,
    Other,
}

/// <summary>
/// Reading a snapshot's lines into a pooled buffer (the caller returns it in <c>finally</c> with <see cref="Return"/>), and
/// the column arithmetic of a monospaced line: a tab runs to the next tab stop, a surrogate pair takes one cell.
/// </summary>
internal static class TextReading
{
    /// <summary>The characters of a range, copied into <paramref name="buffer"/> (rented or grown as needed).</summary>
    public static ReadOnlySpan<char> Read(ITextSnapshot snapshot, int start, int length, ref char[]? buffer)
    {
        if (length <= 0)
        {
            return default;
        }

        if (buffer is null || buffer.Length < length)
        {
            if (buffer is not null)
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            buffer = ArrayPool<char>.Shared.Rent(length);
        }

        var span = buffer.AsSpan(0, length);
        snapshot.CopyTo(start, span);
        return span;
    }

    /// <summary>A line's characters without its break.</summary>
    public static ReadOnlySpan<char> ReadLine(ITextSnapshot snapshot, in TextLine line, ref char[]? buffer) =>
        Read(snapshot, line.Start, line.Length, ref buffer);

    /// <summary>Returns a buffer rented by <see cref="Read"/> (nothing when none was).</summary>
    public static void Return(char[]? buffer)
    {
        if (buffer is not null)
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>The column after <paramref name="c"/> when it starts at <paramref name="column"/>.</summary>
    public static int Advance(int column, char c, int tabSize) =>
        c == '\t' ? column + tabSize - (column % tabSize) : char.IsLowSurrogate(c) ? column : column + 1;

    /// <summary>The column of the character at <paramref name="index"/> of <paramref name="line"/>.</summary>
    public static int ColumnOf(ReadOnlySpan<char> line, int index, int tabSize)
    {
        int column = 0;
        int end = Math.Min(index, line.Length);
        for (int i = 0; i < end; i++)
        {
            column = Advance(column, line[i], tabSize);
        }

        return column;
    }

    /// <summary>
    /// The index of <paramref name="line"/> at <paramref name="column"/>: the last character boundary at or before it, or
    /// the nearest boundary when <paramref name="nearest"/>. Never the middle of a surrogate pair.
    /// </summary>
    public static int IndexAtColumn(ReadOnlySpan<char> line, float column, int tabSize, bool nearest)
    {
        int at = 0;
        for (int i = 0; i < line.Length; i++)
        {
            int next = Advance(at, line[i], tabSize);
            if (next > column)
            {
                int index = i;
                if (nearest && column - at >= (next - at) / 2f)
                {
                    index = i + 1;
                }

                if (index < line.Length && char.IsLowSurrogate(line[index]) && index > 0 && char.IsHighSurrogate(line[index - 1]))
                {
                    index++;
                }

                return index;
            }

            at = next;
        }

        return line.Length;
    }

    /// <summary>The kind of <paramref name="c"/>.</summary>
    public static CharKind KindOf(char c) =>
        c is ' ' or '\t' ? CharKind.Space
        : char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c) ? CharKind.Word
        : char.IsWhiteSpace(c) ? CharKind.Space
        : CharKind.Other;

    /// <summary>Whether <paramref name="c"/> is part of a word (a letter, a digit or an underscore).</summary>
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c);

    /// <summary>The length of the leading spaces and tabs of <paramref name="line"/>.</summary>
    public static int IndentLength(ReadOnlySpan<char> line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        return i;
    }

    /// <summary>The decimal digits of <paramref name="value"/> (at least 1).</summary>
    public static int Digits(int value)
    {
        int digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }
}
