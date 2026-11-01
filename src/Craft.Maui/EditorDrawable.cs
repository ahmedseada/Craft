// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Craft.Core.Diagnostics;
using Craft.Editor;
using Craft.Editor.Activities;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The <see cref="IDrawable"/> of the editor's GraphicsView: measures the font on the first frame (and after a font
/// change), then draws the model through <see cref="EditorRenderer"/> at the view's scroll offsets and viewport.
/// B1: with <see cref="CaretsOnOwnLayer"/> the carets are left to the view's caret layer (<see cref="CaretDrawable"/>)
/// unless a popup or ghost text shows; each frame is timed (<see cref="LastFrameMilliseconds"/>, cold when it laid text
/// out). B2: a frame that throws is reported with its step and skipped (<see cref="PaintGuard"/>).
/// </summary>
public sealed class EditorDrawable : IDrawable, IDisposable
{
    private readonly EditorRenderer _renderer = new();
    private readonly MauiTextMeasurer _measurer;
    private readonly CodeFontPicker _picker = new();
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

    /// <summary>The faces the editor draws with (the bundled files, or a family by name), once the first frame picked them.</summary>
    public Craft.Editor.Shell.CodeFontFaces? Faces => _picker.Picked;

    /// <summary>The code font that did not draw monospaced and gave way to <c>CRAFT_FONT_FALLBACK</c>, or null.</summary>
    public string? FellBackFrom => _fellBackFrom;

    /// <summary>Raised inside a frame, with the family, when the code font gave way to the fallback.</summary>
    public event EventHandler<string>? FontFellBack;

    /// <summary>Raised at the end of each frame, inside it (a handler that changes the UI dispatches the change).</summary>
    public event EventHandler? Drawn;

    /// <summary>Whether a caret layer draws the carets (the text frame then draws them only while a popup or ghost text shows).</summary>
    public bool CaretsOnOwnLayer { get; set; }

    /// <summary>Whether the last frame drew the carets with the text (no caret layer, or a popup or ghost text showed).</summary>
    public bool LastFrameDrewCarets { get; private set; } = true;

    /// <summary>The last frame's time in milliseconds (the paint's telemetry: <c>editor.paint.cold</c> and <c>editor.paint.warm</c>).</summary>
    public double LastFrameMilliseconds { get; private set; }

    /// <summary>Whether the last frame laid text out or built runs (a cold frame, rule 76), else it only drew what was kept.</summary>
    public bool LastFrameCold { get; private set; }

    /// <summary>The canvas of the last frame (its kept layouts), or null before the first.</summary>
    public MauiEditorCanvas? Canvas => _canvas;

    /// <summary>Drops the kept lines and their layouts and the lone runs' layouts (memory pressure); the next frame lays out what it shows.</summary>
    public void ReleaseLayouts()
    {
        _renderer.ReleaseLayouts();
        _canvas?.ReleaseLayouts();
    }

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
        long start = Stopwatch.GetTimestamp();
        long runsBefore = _renderer.RunStringsBuilt;
        string step = "pick and measure the code font";
        try
        {
            var options = model.Options;
            // The owner's note 14: the bundled JetBrains Mono as the platform's canvas reaches it (its files on Windows),
            // checked by its glyph widths, else the first monospaced fallback; picked once per family and size.
            bool picked;
            using (Labels?.Enter("editor: pick and measure the code font"))
            {
                picked = _picker.Ensure(canvas, options.FontFamily, options.FontFallback, options.FontSize, _measurer);
            }

            if (picked)
            {
                if (_picker.FellBackFrom is { } from && !string.Equals(_fellBackFrom, from, StringComparison.Ordinal))
                {
                    _fellBackFrom = from;
                    FontFellBack?.Invoke(this, from);
                }
                else if (_picker.FellBackFrom is null)
                {
                    _fellBackFrom = null;
                }

                model.Remeasure();
            }

            step = "bind the canvas";
            _canvas = _picker.Canvas!;
            _canvas.SetMetrics(model.Metrics);
            _canvas.Target = canvas;
            float width = ViewportWidth > 0 ? ViewportWidth : dirtyRect.Width;
            float height = ViewportHeight > 0 ? ViewportHeight : dirtyRect.Height;
            bool carets = !CaretsOnOwnLayer || EditorRenderer.CaretsOnTextLayer(model);
            LastFrameDrewCarets = carets;
            step = "render";
            _renderer.Draw(_canvas, model, ScrollX, ScrollY, width, height, carets && CaretVisible);
            step = "end the frame";
            _canvas.Target = null;                       // the frame ends here, so its kept layouts are counted in it
            LastFrameCold = _canvas.Layouts.LastFrameBuilt > 0 || _renderer.RunStringsBuilt != runsBefore;
            LastFrameMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Drawn?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // B2: nothing may leave the platform's draw callback (a stowed exception ends the process); reported with its step and skipped.
        catch (Exception error)
#pragma warning restore CA1031
        {
            if (HandleFailure(step, error))
            {
                throw;                                   // a lost device: Win2D's control makes it again
            }
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

    // True when the error is to be rethrown (a lost device: Win2D's control makes the device again), after dropping the layouts.
    private bool HandleFailure(string step, Exception error)
    {
        string at = step == "render" ? "render: " + _renderer.Step : step;
        bool rethrow = PaintGuard.Handle("editor: paint", at, error);
        if (rethrow)
        {
            _canvas?.Target = null;
            ReleaseLayouts();
        }

        return rethrow;
    }
}

/// <summary>
/// The <see cref="IDrawable"/> of the editor's caret layer (B1): a transparent GraphicsView over the text that draws only
/// the carets (<see cref="EditorRenderer.DrawCarets"/>), so the blink repaints this layer and no text. While a popup or
/// ghost text shows, the text frame draws the carets under them and this layer draws none.
/// </summary>
public sealed class CaretDrawable(EditorDrawable text) : IDrawable
{
    /// <summary>The frames that drew carets (the caret layer's telemetry).</summary>
    public long Frames { get; private set; }

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (text.Model is not { } model || !text.CaretVisible || text.Canvas is not { } target || text.IsDrawing || EditorRenderer.CaretsOnTextLayer(model))
        {
            return;
        }

        using var label = text.Labels?.Enter("editor: caret");
        try
        {
            target.Target = canvas;
            float width = text.ViewportWidth > 0 ? text.ViewportWidth : dirtyRect.Width;
            float height = text.ViewportHeight > 0 ? text.ViewportHeight : dirtyRect.Height;
            text.Renderer.DrawCarets(target, model, text.ScrollX, text.ScrollY, width, height);
            Frames++;
        }
#pragma warning disable CA1031 // B2: nothing may leave the platform's draw callback; reported and skipped.
        catch (Exception error)
#pragma warning restore CA1031
        {
            if (PaintGuard.Handle("editor: caret", "draw the carets", error))
            {
                throw;                                   // a lost device: Win2D's control makes it again
            }
        }
        finally
        {
            target.Target = null;
        }
    }
}
