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
        return true;
    }

    /// <summary>A platform font for <paramref name="family"/>.</summary>
    public static IFont PlatformFont(string family) => new MauiFont(family);
}
