// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.Maui.Graphics;
using MauiFont = Microsoft.Maui.Graphics.Font;

namespace Craft.Maui;

/// <summary>
/// <see cref="IEditorCanvas"/> over MAUI's cross-platform <see cref="ICanvas"/> (Win2D on Windows, the platform's canvas
/// elsewhere). A run is drawn left-aligned and centred in its line's height. Colours and the four fonts (plain, bold,
/// italic, bold italic) are made once and reused, so a frame allocates nothing here.
/// </summary>
public sealed class MauiEditorCanvas : IEditorCanvas
{
    private readonly Dictionary<uint, Color> _colors = [];
    private MauiFont _regular;
    private MauiFont _bold;
    private MauiFont _italic;
    private MauiFont _boldItalic;
    private float _fontSize;
    private float _lineHeight;
    private float _cellWidth;

    /// <summary>A canvas for the font <paramref name="family"/> at <paramref name="fontSize"/>.</summary>
    public MauiEditorCanvas(string family, float fontSize) => SetFont(family, fontSize);

    /// <summary>The MAUI canvas of the frame being drawn (set before each draw).</summary>
    public ICanvas? Target { get; set; }

    /// <summary>The regular font.</summary>
    public IFont Font => _regular;

    /// <summary>The font size.</summary>
    public float FontSize => _fontSize;

    /// <summary>Sets the font the runs are drawn with.</summary>
    public void SetFont(string family, float fontSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        _regular = new MauiFont(family, FontWeights.Normal, FontStyleType.Normal);
        _bold = new MauiFont(family, FontWeights.Bold, FontStyleType.Normal);
        _italic = new MauiFont(family, FontWeights.Normal, FontStyleType.Italic);
        _boldItalic = new MauiFont(family, FontWeights.Bold, FontStyleType.Italic);
        _fontSize = fontSize;
    }

    /// <summary>Sets the measures a run is laid out with (a run's box is its cells wide and a line high).</summary>
    public void SetMetrics(FontMetrics metrics)
    {
        _cellWidth = metrics.CellWidth;
        _lineHeight = metrics.LineHeight;
    }

    /// <inheritdoc />
    public void FillRect(float x, float y, float width, float height, uint argb)
    {
        var canvas = Target ?? throw new InvalidOperationException("MauiEditorCanvas.Target is not set: set it to the frame's ICanvas before drawing.");
        canvas.FillColor = ColorOf(argb);
        canvas.FillRectangle(x, y, width, height);
    }

    /// <inheritdoc />
    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness)
    {
        var canvas = Target ?? throw new InvalidOperationException("MauiEditorCanvas.Target is not set: set it to the frame's ICanvas before drawing.");
        canvas.StrokeColor = ColorOf(argb);
        canvas.StrokeSize = thickness;
        canvas.DrawLine(x1, y1, x2, y2);
    }

    /// <inheritdoc />
    public void DrawText(string text, float x, float top, in TextStyle style)
    {
        var canvas = Target ?? throw new InvalidOperationException("MauiEditorCanvas.Target is not set: set it to the frame's ICanvas before drawing.");
        canvas.FontColor = ColorOf(style.Argb);
        canvas.Font = style.Bold ? (style.Italic ? _boldItalic : _bold) : style.Italic ? _italic : _regular;
        canvas.FontSize = _fontSize;
        float width = (text.Length + 1) * Math.Max(1, _cellWidth);
        canvas.DrawString(text, x, top, width, Math.Max(1, _lineHeight), HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.OverflowBounds, 0);
    }

    private Color ColorOf(uint argb)
    {
        if (!_colors.TryGetValue(argb, out var color))
        {
            color = new Color((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            _colors[argb] = color;
        }

        return color;
    }
}
