// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Microsoft.Maui.Graphics;
using MauiFont = Microsoft.Maui.Graphics.Font;

namespace Craft.Maui;

/// <summary>
/// The platform's laid-out text for the drawn surfaces (B1): Windows lays a line out once on Win2D and draws that layout
/// in every frame after (<c>Craft.Maui.WinUI.Win2DTextLayouts</c>); a platform without one draws each run with MAUI's
/// <c>DrawString</c>. Set by the shell from its platform (<see cref="MauiEditorCanvas.TextLayouts"/>).
/// </summary>
public interface IDrawnTextLayouts
{
    /// <summary>A surface laying text out in <paramref name="faces"/> at <paramref name="fontSize"/> (one per canvas and font).</summary>
    IDrawnTextSurface Create(Craft.Editor.Shell.CodeFontFaces faces, float fontSize);
}

/// <summary>An <see cref="ITextLayoutSurface"/> for one font, bound to a frame's MAUI canvas while the frame is drawn.</summary>
public interface IDrawnTextSurface : ITextLayoutSurface, IDisposable
{
    /// <summary>Binds the surface to <paramref name="target"/> for a frame with <paramref name="metrics"/>; false when it is not a canvas this platform lays text on (the runs are then drawn with MAUI's <c>DrawString</c>).</summary>
    bool Begin(ICanvas target, FontMetrics metrics);

    /// <summary>Ends the frame (the surface lets go of the canvas).</summary>
    void End();
}

/// <summary>
/// <see cref="IEditorCanvas"/> over MAUI's cross-platform <see cref="ICanvas"/> (Win2D on Windows, the platform's canvas
/// elsewhere). A run is drawn left-aligned and centred in its line's height. Colours and the four fonts (plain, bold,
/// italic, bold italic) are made once and reused, so a frame allocates nothing here.
/// <para>
/// B1: where the platform lays text out (<see cref="TextLayouts"/>, Windows), a shown line is drawn from one layout kept on
/// it (<see cref="IStyledLineCanvas"/>) and a lone run (a line number, a panel's row, the terminal's text) from one kept by
/// its text and style while it stays drawn (<see cref="Layouts"/>); a warm frame lays nothing out. Elsewhere, or with
/// <c>CRAFT_PAINT_LINE_LAYOUTS</c> off, each run is drawn with MAUI's <c>DrawString</c> as before.
/// </para>
/// </summary>
public sealed class MauiEditorCanvas : IEditorCanvas, IStyledLineCanvas, IIconCanvas, IDisposable
{
    private readonly Dictionary<uint, Color> _colors = [];
    private readonly TextLayoutCache _layouts = new();
    private Craft.Editor.Shell.CodeFontFaces _faces = null!;
    private IDrawnTextSurface? _surface;
    private IDrawnTextLayouts? _surfaceFrom;
    private ICanvas? _target;
    private bool _laying;
    private MauiFont _regular;
    private MauiFont _bold;
    private MauiFont _italic;
    private MauiFont _boldItalic;
    private float _fontSize;
    private float _lineHeight;
    private float _cellWidth;

    /// <summary>A canvas for the font <paramref name="family"/> at <paramref name="fontSize"/>.</summary>
    public MauiEditorCanvas(string family, float fontSize) => SetFont(family, fontSize);

    /// <summary>A canvas drawing with <paramref name="faces"/> (the code font's four faces, by name or by file) at <paramref name="fontSize"/>.</summary>
    public MauiEditorCanvas(Craft.Editor.Shell.CodeFontFaces faces, float fontSize) => SetFaces(faces, fontSize);

    /// <summary>The platform's laid-out text (set by the shell from its platform; null while <c>CRAFT_PAINT_LINE_LAYOUTS</c> is off or the platform has none).</summary>
    public static IDrawnTextLayouts? TextLayouts { get; set; }

    /// <summary>
    /// The MAUI canvas of the frame being drawn: set before each draw (the frame starts) and set back to null after it
    /// (the frame ends: the lone runs it did not draw are dropped).
    /// </summary>
    public ICanvas? Target
    {
        get => _target;
        set
        {
            if (_target is not null)
            {
                EndFrame();
            }

            _target = value;
            if (value is not null)
            {
                BeginFrame(value);
            }
        }
    }

    /// <summary>The kept layouts (their counters are the paint's telemetry).</summary>
    public TextLayoutCache Layouts => _layouts;

    /// <summary>Whether the frame being drawn lays text out through the platform (else MAUI's <c>DrawString</c> per run).</summary>
    public bool LaysOutText => _laying;

    /// <summary>The regular font.</summary>
    public IFont Font => _regular;

    /// <summary>The font size.</summary>
    public float FontSize => _fontSize;

    /// <summary>Sets the font the runs are drawn with.</summary>
    public void SetFont(string family, float fontSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        SetFaces(Craft.Editor.Shell.CodeFontFaces.Named(family), fontSize);
    }

    /// <summary>
    /// Sets the faces the runs are drawn with: a face that names a file is that file's weight and style already, so it is
    /// asked for as it is (no synthetic bold or slant on top); a face by family name asks the platform for the weight and style.
    /// </summary>
    public void SetFaces(Craft.Editor.Shell.CodeFontFaces faces, float fontSize)
    {
        ArgumentNullException.ThrowIfNull(faces);
        bool files = faces.AdvanceEm is not null;
        _regular = new MauiFont(faces.Regular, FontWeights.Normal, FontStyleType.Normal);
        _bold = new MauiFont(faces.Bold, files ? FontWeights.Normal : FontWeights.Bold, FontStyleType.Normal);
        _italic = new MauiFont(faces.Italic, FontWeights.Normal, files ? FontStyleType.Normal : FontStyleType.Italic);
        _boldItalic = new MauiFont(faces.BoldItalic, files ? FontWeights.Normal : FontWeights.Bold, files ? FontStyleType.Normal : FontStyleType.Italic);
        _fontSize = fontSize;
        _faces = faces;
        DropSurface();
    }

    /// <summary>Sets the measures a run is laid out with (a run's box is its cells wide and a line high); new measures lay every line out again.</summary>
    public void SetMetrics(FontMetrics metrics)
    {
        if (metrics.CellWidth != _cellWidth || metrics.LineHeight != _lineHeight)
        {
            _layouts.Invalidate();
        }

        _cellWidth = metrics.CellWidth;
        _lineHeight = metrics.LineHeight;
    }

    /// <summary>Drops the lone runs' layouts (memory pressure); the lines' belong to their renderer.</summary>
    public void ReleaseLayouts() => _layouts.ReleaseRuns();

    /// <inheritdoc />
    public void DrawStyledLine(StyledLine line, float x, float top, float cell, float clipLeft, float clipRight)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (_laying)
        {
            _layouts.DrawLine(_surface!, line, x, top, cell, clipLeft, clipRight);
        }
        else
        {
            StyledLines.DrawRuns(this, line, x, top, cell, clipLeft, clipRight);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_target is not null)
        {
            Target = null;
        }

        DropSurface();
        _layouts.Dispose();
    }

    private void BeginFrame(ICanvas target)
    {
        _layouts.BeginFrame();
        _laying = false;
        var layouts = TextLayouts;
        if (layouts is null)
        {
            DropSurface();
            return;
        }

        if (!ReferenceEquals(layouts, _surfaceFrom) || _surface is null)
        {
            DropSurface();
            _surface = layouts.Create(_faces, _fontSize);
            _surfaceFrom = layouts;
        }

        _laying = _surface.Begin(target, new FontMetrics(Math.Max(1, _cellWidth), Math.Max(1, _lineHeight), 0));
    }

    private void EndFrame()
    {
        if (_laying)
        {
            _surface?.End();
        }

        _laying = false;
        _layouts.EndFrame();
    }

    // A new font or another platform's layouts: every layout is made again (the lone runs' dropped now, the lines' on their next draw).
    private void DropSurface()
    {
        if (_surface is null && _surfaceFrom is null)
        {
            return;
        }

        _layouts.Invalidate();
        _surface?.Dispose();
        _surface = null;
        _surfaceFrom = null;
    }

    /// <inheritdoc />
    public bool DrawIcon(FileIcon icon, float x, float y, float size, bool open, bool dark) =>
        Target is { } canvas && MauiFileIcons.Draw(canvas, icon, x, y, size, open, dark);

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
        if (_laying)
        {
            _layouts.DrawText(_surface!, text, style, x, top);
            return;
        }

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
