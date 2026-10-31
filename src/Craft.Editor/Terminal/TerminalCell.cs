// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Craft.Editor.Terminal;

/// <summary>How a terminal cell's character is drawn (SGR's renditions).</summary>
[Flags]
public enum TerminalAttributes
{
    /// <summary>Plain.</summary>
    None = 0,

    /// <summary>Bold (SGR 1); a basic colour is drawn in its bright form.</summary>
    Bold = 1,

    /// <summary>Faint (SGR 2): drawn half-way to the background.</summary>
    Dim = 2,

    /// <summary>Italic (SGR 3).</summary>
    Italic = 4,

    /// <summary>Underlined (SGR 4).</summary>
    Underline = 8,

    /// <summary>Blinking (SGR 5); kept, drawn steady.</summary>
    Blink = 16,

    /// <summary>Foreground and background swapped (SGR 7).</summary>
    Inverse = 32,

    /// <summary>Hidden (SGR 8): the background only.</summary>
    Hidden = 64,

    /// <summary>Struck through (SGR 9).</summary>
    Strike = 128,
}

/// <summary>
/// A cell's colour, packed in a <see cref="uint"/>: 0 the terminal's default, <c>0x01000000 | index</c> one of the 256
/// indexed colours (0-15 the theme's, 16-231 the 6x6x6 cube, 232-255 the greys), <c>0x02000000 | rrggbb</c> a true colour.
/// Resolved against <see cref="TerminalPalette"/> only when drawn, so a theme change recolours what is on the screen.
/// </summary>
public static class TerminalColor
{
    /// <summary>The terminal's default colour (its foreground or background).</summary>
    public const uint Default = 0;

    private const uint IndexedTag = 0x0100_0000;
    private const uint RgbTag = 0x0200_0000;

    /// <summary>Indexed colour <paramref name="index"/> (0-255).</summary>
    public static uint Indexed(int index) => IndexedTag | (uint)(index & 0xFF);

    /// <summary>The true colour <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>.</summary>
    public static uint Rgb(int r, int g, int b) => RgbTag | ((uint)(r & 0xFF) << 16) | ((uint)(g & 0xFF) << 8) | (uint)(b & 0xFF);

    /// <summary>Whether <paramref name="color"/> is an indexed colour.</summary>
    public static bool IsIndexed(uint color) => (color & 0xFF00_0000) == IndexedTag;

    /// <summary>Whether <paramref name="color"/> is a true colour.</summary>
    public static bool IsRgb(uint color) => (color & 0xFF00_0000) == RgbTag;

    /// <summary>The index of an indexed colour.</summary>
    public static int IndexOf(uint color) => (int)(color & 0xFF);
}

/// <summary>
/// One cell of the terminal's grid, 12 bytes: its character (a Unicode scalar, 0 for a cell never written, drawn blank),
/// whether it is the first or the second cell of a wide character, its renditions, and its two colours
/// (<see cref="TerminalColor"/>).
/// </summary>
public readonly struct TerminalCell : IEquatable<TerminalCell>
{
    private const uint CodeMask = 0x1F_FFFF;
    private const uint WideBit = 1u << 21;
    private const uint TailBit = 1u << 22;
    private const int AttributeShift = 23;

    private readonly uint _data;

    /// <summary>A cell of <paramref name="codePoint"/> drawn with <paramref name="attributes"/> and the two colours.</summary>
    public TerminalCell(int codePoint, TerminalAttributes attributes, uint foreground, uint background, bool wide = false, bool wideTail = false)
    {
        _data = ((uint)codePoint & CodeMask) | (wide ? WideBit : 0) | (wideTail ? TailBit : 0) | ((uint)attributes << AttributeShift);
        Foreground = foreground;
        Background = background;
    }

    /// <summary>The character (a Unicode scalar), or 0 for a blank cell.</summary>
    public int CodePoint => (int)(_data & CodeMask);

    /// <summary>Whether it is the first of a wide character's two cells.</summary>
    public bool IsWide => (_data & WideBit) != 0;

    /// <summary>Whether it is the second cell of a wide character (nothing is drawn in it).</summary>
    public bool IsWideTail => (_data & TailBit) != 0;

    /// <summary>The renditions.</summary>
    public TerminalAttributes Attributes => (TerminalAttributes)(_data >> AttributeShift);

    /// <summary>The foreground colour.</summary>
    public uint Foreground { get; }

    /// <summary>The background colour.</summary>
    public uint Background { get; }

    /// <summary>Whether it shows no character (never written, or a space).</summary>
    public bool IsBlank => CodePoint is 0 or ' ';

    /// <summary>A blank cell on <paramref name="background"/> (what an erase leaves: the current background, as xterm does).</summary>
    public static TerminalCell Blank(uint background) => new(0, TerminalAttributes.None, TerminalColor.Default, background);

    /// <inheritdoc />
    public bool Equals(TerminalCell other) => _data == other._data && Foreground == other.Foreground && Background == other.Background;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalCell other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_data, Foreground, Background);

    /// <summary>Equality.</summary>
    public static bool operator ==(TerminalCell left, TerminalCell right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(TerminalCell left, TerminalCell right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => CodePoint == 0 ? " " : Rune.IsValid(CodePoint) ? new Rune(CodePoint).ToString() : "?";
}

/// <summary>
/// How many cells a character takes: 0 for combining marks and other zero-width characters (they are not drawn as cells
/// of their own), 2 for East Asian wide and full-width characters and emoji shown as pictures, 1 for the rest. The table
/// follows Unicode 15's East_Asian_Width W and F ranges and Emoji_Presentation, as terminals do (wcwidth).
/// </summary>
public static class TerminalCharWidth
{
    // Sorted, non-overlapping inclusive ranges of wide characters (pairs: first, last).
    private static readonly int[] Wide =
    [
        0x1100, 0x115F, 0x231A, 0x231B, 0x2329, 0x232A, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3, 0x25FD, 0x25FE,
        0x2614, 0x2615, 0x2648, 0x2653, 0x267F, 0x267F, 0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE,
        0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA, 0x26F2, 0x26F3, 0x26F5, 0x26F5, 0x26FA, 0x26FA,
        0x26FD, 0x26FD, 0x2705, 0x2705, 0x270A, 0x270B, 0x2728, 0x2728, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755,
        0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55,
        0x2E80, 0x303E, 0x3041, 0x33FF, 0x3400, 0x4DBF, 0x4E00, 0x9FFF, 0xA000, 0xA4CF, 0xA960, 0xA97F, 0xAC00, 0xD7A3,
        0xF900, 0xFAFF, 0xFE10, 0xFE19, 0xFE30, 0xFE6F, 0xFF00, 0xFF60, 0xFFE0, 0xFFE6,
        0x16FE0, 0x16FE4, 0x16FF0, 0x16FF1, 0x17000, 0x18CFF, 0x18D00, 0x18D08, 0x1AFF0, 0x1B2FF, 0x1F004, 0x1F004,
        0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F200, 0x1F202, 0x1F210, 0x1F23B, 0x1F240, 0x1F248,
        0x1F250, 0x1F251, 0x1F260, 0x1F265, 0x1F300, 0x1F320, 0x1F32D, 0x1F335, 0x1F337, 0x1F37C, 0x1F37E, 0x1F393,
        0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E, 0x1F440, 0x1F440,
        0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D, 0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596,
        0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D7,
        0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A,
        0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA89, 0x1FA8F, 0x1FAC6, 0x1FACE, 0x1FADC,
        0x1FADF, 0x1FAE9, 0x1FAF0, 0x1FAF8, 0x20000, 0x2FFFD, 0x30000, 0x3FFFD,
    ];

    /// <summary>The cells <paramref name="codePoint"/> takes: 0, 1 or 2.</summary>
    public static int Of(int codePoint)
    {
        if (codePoint < 0x300)
        {
            return codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0) ? 0 : 1;
        }

        if (codePoint == 0x200D || (codePoint >= 0x200B && codePoint <= 0x200F) || (codePoint >= 0xFE00 && codePoint <= 0xFE0F))
        {
            return 0;
        }

        if (codePoint >= 0x1100 && InWide(codePoint))
        {
            return 2;
        }

        if (!Rune.IsValid(codePoint))
        {
            return 1;
        }

        var category = Rune.GetUnicodeCategory(new Rune(codePoint));
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format ? 0 : 1;
    }

    private static bool InWide(int codePoint)
    {
        int low = 0, high = (Wide.Length / 2) - 1;
        while (low <= high)
        {
            int middle = (low + high) >> 1;
            if (codePoint < Wide[middle * 2])
            {
                high = middle - 1;
            }
            else if (codePoint > Wide[(middle * 2) + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
