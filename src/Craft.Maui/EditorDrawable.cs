// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Craft.Editor.Activities;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The <see cref="IDrawable"/> of the editor's GraphicsView: measures the font on the first frame (and after a font
/// change), then draws the model through <see cref="EditorRenderer"/> at the view's scroll offsets and viewport.
/// </summary>
public sealed class EditorDrawable : IDrawable, IDisposable
{
    private readonly EditorRenderer _renderer = new();
    private readonly MauiTextMeasurer _measurer;
    private string? _fellBackFrom;
    private MauiEditorCanvas? _canvas;

    /// <summary>A drawable measuring with <paramref name="measurer"/> (the one the model was made with).</summary>
    public EditorDrawable(MauiTextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        _measurer = measurer;
    }

    /// <summary>The editor it draws (nothing is drawn while null).</summary>
    public EditorModel? Model { get; set; }

    /// <summary>The horizontal scroll offset.</summary>
    public float ScrollX { get; set; }

    /// <summary>The vertical scroll offset.</summary>
    public float ScrollY { get; set; }

    /// <summary>The viewport's width.</summary>
    public float ViewportWidth { get; set; }

    /// <summary>The viewport's height.</summary>
    public float ViewportHeight { get; set; }

    /// <summary>Whether the caret shows in this frame (the blink).</summary>
    public bool CaretVisible { get; set; } = true;

    /// <summary>Whether a frame is being drawn now.</summary>
    public bool IsDrawing { get; private set; }

    /// <summary>The labels of the UI work (the watchdog names a paint that blocks the window), or null.</summary>
    public UiWorkLabels? Labels { get; set; }

    /// <summary>The renderer (its counters are the paint's telemetry).</summary>
    public EditorRenderer Renderer => _renderer;

    /// <summary>The code font that did not draw monospaced and gave way to <c>CRAFT_FONT_FALLBACK</c>, or null.</summary>
    public string? FellBackFrom => _fellBackFrom;

    /// <summary>Raised inside a frame, with the family, when the code font gave way to the fallback.</summary>
    public event EventHandler<string>? FontFellBack;

    /// <summary>Raised at the end of each frame, inside it (a handler that changes the UI dispatches the change).</summary>
    public event EventHandler? Drawn;

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var model = Model;
        if (model is null)
        {
            return;
        }

        IsDrawing = true;
        using var label = Labels?.Enter("editor: paint");
        try
        {
            var options = model.Options;
            string family = string.Equals(_fellBackFrom, options.FontFamily, StringComparison.Ordinal) ? options.FontFallback : options.FontFamily;
            if (_canvas is null || _canvas.Font.Name != family || _canvas.FontSize != options.FontSize)
            {
                _canvas = new MauiEditorCanvas(family, options.FontSize);
            }

            var font = new EditorFont(options.FontFamily, options.FontSize);
            if (_measurer.EnsureMeasured(canvas, font, _canvas.Font))
            {
                if (!_measurer.IsMonospaced && family != options.FontFallback)
                {
                    // Measured once per family: the code font is not on this platform (or not monospaced); the fallback is.
                    _fellBackFrom = options.FontFamily;
                    _canvas = new MauiEditorCanvas(options.FontFallback, options.FontSize);
                    _measurer.Reset();
                    _measurer.EnsureMeasured(canvas, font, _canvas.Font);
                    FontFellBack?.Invoke(this, options.FontFamily);
                }

                model.Remeasure();
            }

            _canvas.SetMetrics(model.Metrics);
            _canvas.Target = canvas;
            float width = ViewportWidth > 0 ? ViewportWidth : dirtyRect.Width;
            float height = ViewportHeight > 0 ? ViewportHeight : dirtyRect.Height;
            _renderer.Draw(_canvas, model, ScrollX, ScrollY, width, height, CaretVisible);
            Drawn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (_canvas is not null)
            {
                _canvas.Target = null;
            }

            IsDrawing = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _renderer.Dispose();
}
