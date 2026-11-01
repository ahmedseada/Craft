// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers.Binary;

namespace Craft.Editor.Shell;

/// <summary>
/// The four faces a code font is drawn with, each as the drawn surface names it: a family name the platform has, or (where
/// the platform's canvas takes one) a font file's reference ("file:///…/JetBrainsMono-Bold.ttf#JetBrains Mono").
/// </summary>
/// <param name="Regular">The regular face.</param>
/// <param name="Bold">The bold face.</param>
/// <param name="Italic">The italic face.</param>
/// <param name="BoldItalic">The bold italic face.</param>
/// <param name="Source">Where the faces come from, as the user reads it ("the bundled JetBrains Mono", "Cascadia Mono").</param>
public sealed record CodeFontFaces(string Regular, string Bold, string Italic, string BoldItalic, string Source)
{
    /// <summary>The design's advance of a cell, in ems (JetBrains Mono's 600/1000), when the faces come from a file whose metrics were read; null for a family by name.</summary>
    public double? AdvanceEm { get; init; }

    /// <summary>A family by name: the platform's own faces of it.</summary>
    public static CodeFontFaces Named(string family) => new(family, family, family, family, family);
}

/// <summary>
/// What a TrueType or OpenType font file says about its cells (read from its bytes: the <c>head</c>, <c>hhea</c> and
/// <c>post</c> tables): the units per em, the widest advance and whether it declares a fixed pitch. For a monospaced
/// font the widest advance is every glyph's, so <see cref="AdvanceEm"/> is the cell's width in ems that a canvas drawing
/// that file must measure (the owner's note 14: the bundled font is checked to be the one drawn, by its glyph widths).
/// </summary>
/// <param name="UnitsPerEm">The design units per em.</param>
/// <param name="AdvanceWidthMax">The widest advance, in design units.</param>
/// <param name="IsFixedPitch">Whether the file declares a fixed pitch (<c>post.isFixedPitch</c>).</param>
public readonly record struct FontFileMetrics(int UnitsPerEm, int AdvanceWidthMax, bool IsFixedPitch)
{
    /// <summary>The widest advance in ems.</summary>
    public double AdvanceEm => UnitsPerEm > 0 ? (double)AdvanceWidthMax / UnitsPerEm : 0;

    /// <summary>The metrics of the font file <paramref name="bytes"/>, or null when it is not one (or lacks the tables).</summary>
    public static FontFileMetrics? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return null;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (version is not (0x00010000u or 0x4F54544Fu or 0x74727565u))   // TrueType 1.0, 'OTTO', 'true'
        {
            return null;
        }

        int tables = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
        int head = -1, hhea = -1, post = -1;
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + (i * 16);
            if (record + 16 > bytes.Length)
            {
                return null;
            }

            var tag = bytes.Slice(record, 4);
            int offset = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(bytes[(record + 8)..]));
            if (tag.SequenceEqual("head"u8))
            {
                head = offset;
            }
            else if (tag.SequenceEqual("hhea"u8))
            {
                hhea = offset;
            }
            else if (tag.SequenceEqual("post"u8))
            {
                post = offset;
            }
        }

        if (head < 0 || head + 20 > bytes.Length || hhea < 0 || hhea + 12 > bytes.Length)
        {
            return null;
        }

        int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(bytes[(head + 18)..]);
        int advanceMax = BinaryPrimitives.ReadUInt16BigEndian(bytes[(hhea + 10)..]);
        bool fixedPitch = post >= 0 && post + 16 <= bytes.Length && BinaryPrimitives.ReadUInt32BigEndian(bytes[(post + 12)..]) != 0;
        return new FontFileMetrics(unitsPerEm, advanceMax, fixedPitch);
    }

    /// <summary>The metrics of the font file at <paramref name="path"/>, or null when it cannot be read or is not one.</summary>
    public static FontFileMetrics? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? Read(File.ReadAllBytes(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// How the editor and the terminal choose the code font they draw with (the owner's note 14): the faces to try in order
/// (<see cref="Candidates"/>) and the measure that accepts one (<see cref="Accepts"/>). The bundled JetBrains Mono comes
/// first, as its files where the platform's canvas takes a file (Windows' Win2D does not see the app's MAUI fonts by their
/// alias), then by its name; then <c>CRAFT_FONT_FALLBACK</c>'s families, in order. A candidate is taken when it draws
/// monospaced (a run of "i"s as wide as the "M"s) and, for a file, when its cell is the file's own advance, so a
/// substituted font is never taken for the bundled one.
/// </summary>
public static class CodeFonts
{
    /// <summary>The folder beside the program the app copies its code fonts to (<c>Craft.App.csproj</c>).</summary>
    public const string Folder = "fonts";

    /// <summary>How far a measured cell may be from the file's advance and still be that file's (the canvas rounds).</summary>
    public const double AdvanceTolerance = 0.02;

    /// <summary>How far the "i"s may be from the "M"s and still be monospaced.</summary>
    public const double MonospaceTolerance = 0.005;

    /// <summary>The bundled code font's family and its four files, regular, bold, italic, bold italic (phase D: JetBrains Mono 2.304, SIL OFL 1.1).</summary>
    public static (string Family, string Regular, string Bold, string Italic, string BoldItalic) Bundled { get; } =
        ("JetBrains Mono", "JetBrainsMono-Regular.ttf", "JetBrainsMono-Bold.ttf", "JetBrainsMono-Italic.ttf", "JetBrainsMono-BoldItalic.ttf");

    /// <summary>
    /// The bundled code font's faces as files in <paramref name="folder"/>, each named by <paramref name="fileFace"/> (the
    /// platform's reference to a face of a file, from its path and family), with the regular file's advance; null when
    /// the files are not there (or not a fixed-pitch font). Reads the file: call it once, off the frame (at start).
    /// </summary>
    public static CodeFontFaces? BundledFaces(string folder, Func<string, string, string> fileFace)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(fileFace);
        var bundled = Bundled;
        string regular = Path.Combine(folder, bundled.Regular);
        if (FontFileMetrics.ReadFile(regular) is not { IsFixedPitch: true } metrics)
        {
            return null;
        }

        string Face(string file) => File.Exists(Path.Combine(folder, file)) ? fileFace(Path.Combine(folder, file), bundled.Family) : fileFace(regular, bundled.Family);
        return new CodeFontFaces(Face(bundled.Regular), Face(bundled.Bold), Face(bundled.Italic), Face(bundled.BoldItalic), $"the bundled {bundled.Family} ({folder})")
        {
            AdvanceEm = metrics.AdvanceEm,
        };
    }

    /// <summary>
    /// The faces to try for <paramref name="family"/> with the fallbacks <paramref name="fallback"/> (comma-separated
    /// families): <paramref name="bundled"/> (<see cref="BundledFaces"/>, each way the platform can name the files, in its
    /// order) first when <paramref name="family"/> is the bundled one; then <paramref name="family"/> by name; then each
    /// fallback by name. Never empty, no family twice.
    /// </summary>
    public static IReadOnlyList<CodeFontFaces> Candidates(string family, string fallback, IReadOnlyList<CodeFontFaces>? bundled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        var list = new List<CodeFontFaces>(4);
        var names = new List<string> { family.Trim() };
        foreach (var name in (fallback ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        // The bundled faces stand where their family is named (the default names it last, after the system's own: on
        // Windows a font read from a file draws about 200 times slower than an installed one, the owner's run 2026-11-01).
        foreach (var name in names)
        {
            if (bundled is not null && name.Equals(Bundled.Family, StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(bundled);
            }

            list.Add(CodeFontFaces.Named(name));
        }

        return list;
    }

    /// <summary>
    /// Whether a candidate measured at <paramref name="size"/> is taken: its run of <paramref name="sample"/> "M"s measured
    /// <paramref name="wide"/> and its run of as many "i"s <paramref name="narrow"/> (monospaced), and, with
    /// <paramref name="advanceEm"/> (a file's), its cell within <see cref="AdvanceTolerance"/> of the file's advance.
    /// </summary>
    public static bool Accepts(double wide, double narrow, int sample, double size, double? advanceEm)
    {
        if (!(wide > 0) || sample <= 0 || !(size > 0))
        {
            return false;
        }

        bool monospaced = !(narrow > 0) || Math.Abs(narrow - wide) <= wide * MonospaceTolerance;
        if (!monospaced)
        {
            return false;
        }

        if (advanceEm is not { } em || !(em > 0))
        {
            return true;
        }

        double cell = wide / sample / size;
        return Math.Abs(cell - em) <= em * AdvanceTolerance;
    }
}
