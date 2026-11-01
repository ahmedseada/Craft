// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Graphics;
using MauiFont = Microsoft.Maui.Graphics.Font;

namespace Craft.Maui;

/// <summary>
/// The code font a platform's drawn surface can reach: on Windows the canvas (Win2D through MAUI Graphics) does not see the
/// app's MAUI fonts by their alias, so the platform names the bundled files instead (<c>Craft.Maui.WinUI.WindowsDrawnFonts</c>).
/// A platform without one draws the families by name.
/// </summary>
public interface IDrawnFonts
{
    /// <summary>The bundled code font's faces as the platform's canvas may take them (each way it can name the files, read once at start), or empty.</summary>
    IReadOnlyList<CodeFontFaces> Bundled { get; }
}

/// <summary>
/// Picks the code font the editor and the terminal draw with (the owner's note 14), inside the first frame: each of
/// <see cref="CodeFonts.Candidates"/> in turn is measured on the frame's canvas (a run of "M"s and one of "i"s), and the
/// first <see cref="CodeFonts.Accepts"/> takes (the bundled file when its cell is the file's own advance, else a family
/// that draws monospaced); none taken, the last. A canvas that throws for a face counts as refusing it. The pick is kept
/// until the family, the fallbacks or the size change; the measure goes to the <see cref="MauiTextMeasurer"/>.
/// </summary>
public sealed class CodeFontPicker
{
    private const int Sample = 64;
    private static readonly string Wide = new('M', Sample);
    private static readonly string Narrow = new('i', Sample);
    private string? _family;
    private string? _fallback;
    private float _size;
    private MauiEditorCanvas? _canvas;

    /// <summary>The platform's drawn fonts (set by the shell from its platform at start), or null.</summary>
    public static IDrawnFonts? DrawnFonts { get; set; }

    /// <summary>The canvas of the faces picked (null before the first frame).</summary>
    public MauiEditorCanvas? Canvas => _canvas;

    /// <summary>The faces picked, or null.</summary>
    public CodeFontFaces? Picked { get; private set; }

    /// <summary>The family asked for, when the pick is not it (a fallback was taken), else null.</summary>
    public string? FellBackFrom { get; private set; }

    /// <summary>
    /// Picks the faces for <paramref name="family"/> (with <paramref name="fallback"/>) at <paramref name="size"/> when
    /// not picked yet, measuring on <paramref name="canvas"/> into <paramref name="measurer"/>; true when the pick (and so
    /// the measure) changed in this call.
    /// </summary>
    public bool Ensure(ICanvas canvas, string family, string fallback, float size, MauiTextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(measurer);
        // Compared field by field (B1, rule 75: no key string made in every frame).
        if (_canvas is not null && size == _size && string.Equals(family, _family, StringComparison.Ordinal) && string.Equals(fallback, _fallback, StringComparison.Ordinal))
        {
            return false;
        }

        var candidates = CodeFonts.Candidates(family, fallback, DrawnFonts?.Bundled);
        CodeFontFaces picked = candidates[^1];
        SizeF measured = default;
        foreach (var faces in candidates)
        {
            if (Measure(canvas, faces.Regular, size) is { } m && CodeFonts.Accepts(m.Wide.Width, m.Narrow, Sample, size, faces.AdvanceEm))
            {
                picked = faces;
                measured = m.Wide;
                break;
            }
        }

        if (!(measured.Width > 0) && Measure(canvas, picked.Regular, size) is { } last)
        {
            measured = last.Wide;
        }

        _family = family;
        _fallback = fallback;
        _size = size;
        Picked = picked;
        FellBackFrom = picked.AdvanceEm is not null || picked.Regular.Equals(family.Trim(), StringComparison.OrdinalIgnoreCase) ? null : family;
        if (_canvas is { Target: null } old)
        {
            old.Dispose();                                   // its kept layouts go with it (a canvas inside a frame is left to that frame)
        }

        _canvas = new MauiEditorCanvas(picked, size);
        measurer.SetMeasured(new EditorFont(family, size), measured.Width / Sample, measured.Height);
        return true;
    }

    private static (SizeF Wide, float Narrow)? Measure(ICanvas canvas, string face, float size)
    {
        try
        {
            var font = new MauiFont(face);
            var wide = canvas.GetStringSize(Wide, font, size);
            if (!(wide.Width > 0) || !(wide.Height > 0))
            {
                return null;
            }

            return (wide, canvas.GetStringSize(Narrow, font, size).Width);
        }
#pragma warning disable CA1031 // A face the platform's canvas refuses (a file reference it cannot read) is not taken; the next is tried.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
