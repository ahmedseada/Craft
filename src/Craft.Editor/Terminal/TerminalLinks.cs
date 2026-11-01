// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>A link found in a terminal's line: the line's number, the cells it covers, the path as written, and the place it names.</summary>
/// <param name="Line">The line's number (<see cref="TerminalScreen.LineAt"/>'s).</param>
/// <param name="StartColumn">Its first cell.</param>
/// <param name="EndColumn">The cell after it.</param>
/// <param name="Path">The path as written.</param>
/// <param name="TargetLine">The line it names, from 1.</param>
/// <param name="TargetColumn">The column it names, from 1; 0 when none.</param>
public readonly record struct TerminalLink(long Line, int StartColumn, int EndColumn, string Path, int TargetLine, int TargetColumn);

/// <summary>
/// The terminal's links (X part 9, item 17): a row read as one character per cell (a wide character's second cell a space, a
/// character past the basic plane a stand-in), so a link's characters are its cells, scanned by the registered
/// <see cref="OutputLinkDetectors"/>. Called holding the screen's lock.
/// </summary>
public static class TerminalLinks
{
    /// <summary>Writes <paramref name="row"/>'s cells as characters into <paramref name="into"/> (as many as it holds); returns how many, its trailing blanks left out.</summary>
    public static int CharsOf(TerminalRow row, Span<char> into)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cells = row.Cells;
        int count = Math.Min(cells.Length, into.Length);
        int last = 0;
        for (int i = 0; i < count; i++)
        {
            var cell = cells[i];
            int code = cell.CodePoint;
            char c = cell.IsWideTail || code == 0 ? ' ' : code < 0x10000 ? (char)code : '�';
            into[i] = c;
            if (c != ' ')
            {
                last = i + 1;
            }
        }

        return last;
    }

    /// <summary>The link of <paramref name="screen"/>'s line <paramref name="line"/> on cell <paramref name="column"/>, or null (call it holding the screen's lock).</summary>
    public static TerminalLink? At(TerminalScreen screen, long line, int column)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (screen.LineAt(line) is not { } row || column < 0 || column >= row.Length)
        {
            return null;
        }

        char[] chars = System.Buffers.ArrayPool<char>.Shared.Rent(row.Length);
        try
        {
            int length = CharsOf(row, chars);
            var text = chars.AsSpan(0, length);
            if (column >= length || OutputLinkDetectors.At(text, column) is not { } link)
            {
                return null;
            }

            return new TerminalLink(line, link.Start, link.End, text.Slice(link.PathStart, link.PathLength).ToString(), link.Line, link.Column);
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(chars);
        }
    }
}
