// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Core.Text;

/// <summary>
/// A block of a piece table's text: the original text (one chunk, never changed) or a block of the add buffer (filled at
/// its end, never moved or copied). It keeps the positions just after each line break it holds (its line starts), in its
/// own context: "\r\n" is one break, a "\r" at the end of what was appended is a break of its own (no "\n" ever follows
/// it in the same chunk: a padding character goes between them). Written on the buffer's thread only; read on any, up to
/// what a published snapshot references.
/// </summary>
internal sealed class TextChunk
{
    private const char Padding = '\0';

    private readonly string? _text;
    private readonly char[]? _chars;
    private int[] _lineStarts;
    private int _lineStartCount;
    private int _used;

    private TextChunk(string? text, char[]? chars, int used, int lineStartCapacity, bool isAdd = false)
    {
        IsAdd = isAdd;
        _text = text;
        _chars = chars;
        _used = used;
        _lineStarts = lineStartCapacity == 0 ? [] : new int[lineStartCapacity];
    }

    /// <summary>Whether it is a block of the add buffer (typed or pasted text), not the original text.</summary>
    public bool IsAdd { get; }

    /// <summary>The characters it can hold.</summary>
    public int Capacity => _text?.Length ?? _chars!.Length;

    /// <summary>The characters written so far.</summary>
    public int Used => _used;

    /// <summary>The character at <paramref name="index"/>.</summary>
    public char this[int index] => _text is not null ? _text[index] : _chars![index];

    /// <summary>The line starts, as far as they are written (read through a volatile read: the array may have been replaced by a bigger one).</summary>
    public int[] LineStarts => Volatile.Read(ref _lineStarts);

    /// <summary>A range of its characters.</summary>
    public ReadOnlySpan<char> Slice(int start, int length) => _text is not null ? _text.AsSpan(start, length) : _chars.AsSpan(start, length);

    /// <summary>The original text, kept as the string it is (no copy).</summary>
    public static TextChunk FromString(string text)
    {
        var chunk = new TextChunk(text, null, text.Length, 0);
        chunk.IndexLines(0, text.Length);
        return chunk;
    }

    /// <summary>The original text, owning <paramref name="chars"/> (no copy).</summary>
    public static TextChunk FromArray(char[] chars)
    {
        var chunk = new TextChunk(null, chars, chars.Length, 0);
        chunk.IndexLines(0, chars.Length);
        return chunk;
    }

    /// <summary>An empty block of the add buffer, holding up to <paramref name="capacity"/> characters.</summary>
    public static TextChunk ForAppend(int capacity) => new(null, new char[capacity], 0, 16, isAdd: true);

    /// <summary>
    /// Appends as much of <paramref name="text"/> as fits; returns how many characters it took (0 when none fit) and,
    /// in <paramref name="start"/>, where they went.
    /// </summary>
    public int Append(ReadOnlySpan<char> text, out int start)
    {
        var chars = _chars!;
        int used = _used;
        if (used > 0 && chars[used - 1] == '\r' && text[0] == '\n')
        {
            if (chars.Length - used < 2)
            {
                start = used;
                return 0;
            }

            chars[used++] = Padding;
        }

        int take = Math.Min(text.Length, chars.Length - used);
        start = used;
        if (take == 0)
        {
            _used = used;
            return 0;
        }

        text[..take].CopyTo(chars.AsSpan(used));
        _used = used + take;
        IndexLines(used, take);
        return take;
    }

    /// <summary>The index of the first line start at or after <paramref name="position"/> (its count when none is).</summary>
    public int FirstLineStartAtOrAfter(int position) => LowerBound(_lineStarts.AsSpan(0, _lineStartCount), position);

    /// <summary>The index of the first value at or after <paramref name="value"/> in a sorted span (its length when none is).</summary>
    public static int LowerBound(ReadOnlySpan<int> sorted, int value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (sorted[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void IndexLines(int start, int length)
    {
        var text = Slice(start, length);
        int at = 0;
        while (true)
        {
            int found = text[at..].IndexOfAny('\r', '\n');
            if (found < 0)
            {
                return;
            }

            at += found;
            if (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n')
            {
                at++;
            }

            at++;
            AddLineStart(start + at);
        }
    }

    private void AddLineStart(int position)
    {
        var starts = _lineStarts;
        if (_lineStartCount == starts.Length)
        {
            var bigger = new int[Math.Max(16, starts.Length * 2)];
            starts.AsSpan(0, _lineStartCount).CopyTo(bigger);
            bigger[_lineStartCount++] = position;
            Volatile.Write(ref _lineStarts, bigger);
            return;
        }

        starts[_lineStartCount++] = position;
    }
}
