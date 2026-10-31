// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.Maui.Graphics;
using MauiFont = Microsoft.Maui.Graphics.Font;

namespace Craft.Maui;

/// <summary>
/// <see cref="ITextMeasurer"/> on MAUI: the font is measured on the platform's canvas inside the first draw (a long run of
/// "M"s, divided by its length, gives the cell's width) and kept until the font settings change. Before that first draw
/// it answers an estimate from the font size, and the editor measures again once the real measure is in
/// (<see cref="Craft.Editor.EditorModel.Remeasure"/>).
/// </summary>
public sealed class MauiTextMeasurer : ITextMeasurer
{
    private const int SampleLength = 64;
    private static readonly string Sample = new('M', SampleLength);
    private static readonly string NarrowSample = new('i', SampleLength);
    private EditorFont? _measuredFont;
    private float _cellWidth;
    private float _textHeight;

    /// <summary>Whether the font was measured on a canvas (not only estimated).</summary>
    public bool IsMeasured => _measuredFont is not null;

    /// <inheritdoc />
    public FontMetrics Measure(EditorFont font, float lineSpacing)
    {
        ArgumentNullException.ThrowIfNull(font);
        bool measured = _measuredFont == font;
        float cell = measured ? _cellWidth : font.Size * 0.6f;
        float textHeight = measured ? _textHeight : font.Size * 1.2f;
        float lineHeight = MathF.Ceiling(Math.Max(textHeight, font.Size * lineSpacing));
        float baseline = ((lineHeight - textHeight) / 2) + (textHeight * 0.8f);
        return new FontMetrics(cell, lineHeight, baseline);
    }

    /// <summary>Measures <paramref name="font"/> on <paramref name="canvas"/> when it was not yet; true when the measure changed.</summary>
    public bool EnsureMeasured(ICanvas canvas, EditorFont font, IFont platformFont)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(font);
        if (_measuredFont == font)
        {
            return false;
        }

        var size = canvas.GetStringSize(Sample, platformFont, font.Size);
        if (!(size.Width > 0) || !(size.Height > 0))
        {
            return false;
        }

        _cellWidth = size.Width / SampleLength;
        _textHeight = size.Height;
        _measuredFont = font;
        // A family the platform does not have is drawn in its default font, often proportional: a run of "i"s then
        // measures narrower than the "M"s (more than half a percent off), and the editor's columns would not line up.
        var narrow = canvas.GetStringSize(NarrowSample, platformFont, font.Size);
        IsMonospaced = !(narrow.Width > 0) || Math.Abs(narrow.Width - size.Width) <= size.Width * 0.005;
        return true;
    }

    /// <summary>
    /// Takes a measure made elsewhere (<see cref="CodeFontPicker"/>: the cell's width and the text's height of the faces it
    /// picked) for <paramref name="font"/>; nothing when the measure is not a positive size (the estimate stays).
    /// </summary>
    public void SetMeasured(EditorFont font, float cellWidth, float textHeight)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!(cellWidth > 0) || !(textHeight > 0))
        {
            _measuredFont = null;
            return;
        }

        _cellWidth = cellWidth;
        _textHeight = textHeight;
        _measuredFont = font;
        IsMonospaced = true;
    }

    /// <summary>Whether the last font measured draws monospaced (its "i" as wide as its "M").</summary>
    public bool IsMonospaced { get; private set; } = true;

    /// <summary>Forgets the measure, so the next draw measures again (the drawable changed the platform font).</summary>
    public void Reset() => _measuredFont = null;

    /// <summary>A platform font for <paramref name="family"/>.</summary>
    public static IFont PlatformFont(string family) => new MauiFont(family);
}
