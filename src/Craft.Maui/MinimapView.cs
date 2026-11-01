// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Diagnostics;
using Craft.Editor;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The minimap beside one editor's text (the X restart, piece 2b): a <see cref="GraphicsView"/> drawing its editor's model
/// through its own <see cref="MinimapRenderer"/> (one per view), with the overview ruler at its right edge. It redraws only
/// when <see cref="Refresh"/> finds the frame would differ (a scroll, an edit, a theme, its marks), never on the caret's
/// blink; a press or a drag scrolls the text (<see cref="MinimapRenderer.PointerPressed"/>). A frame that throws is reported
/// with its step and skipped (B2's <see cref="PaintGuard"/>).
/// </summary>
public sealed class MinimapView : GraphicsView, IDisposable
{
    private readonly MinimapDrawable _drawable = new();
    private bool _disposed;

    /// <summary>An empty minimap (set <see cref="Model"/>).</summary>
    public MinimapView()
    {
        Drawable = _drawable;
        // Colours still to check after an edit, a landed parse or a theme (a budget a frame): the next frame goes on.
        _drawable.AfterPending = () => Dispatcher.DispatchLabelled("editor: the minimap's colours", Refresh);
        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        CancelInteraction += OnCancelInteraction;
        SizeChanged += (_, _) =>
        {
            _drawable.Width = (float)Width;
            _drawable.Height = (float)Height;
            Invalidate();
        };
    }

    /// <summary>The editor it shows (nothing is drawn while null).</summary>
    public EditorModel? Model
    {
        get => _drawable.Model;
        set
        {
            _drawable.Model = value;
            Invalidate();
        }
    }

    /// <summary>Its renderer (the minimap's counters).</summary>
    public MinimapRenderer Renderer => _drawable.Renderer;

    /// <summary>Redraws when the frame would differ from the last (a scroll, an edit, a theme, the marks), else nothing.</summary>
    public void Refresh()
    {
        if (_drawable.Model is { } model && IsVisible && Width > 0 && Height > 0 && _drawable.Renderer.NeedsRedraw(model, (float)Width, (float)Height))
        {
            Invalidate();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _drawable.Dispose();
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (_drawable.Model is { } model && e.Touches.Length > 0)
        {
            var point = e.Touches[0];
            _drawable.Renderer.PointerPressed(model, point.X, point.Y, (float)Width, (float)Height);
            Invalidate();
        }
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (_drawable.Model is { } model && e.Touches.Length > 0)
        {
            var point = e.Touches[0];
            _drawable.Renderer.PointerMoved(model, point.X, point.Y, (float)Width, (float)Height);
        }
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        _drawable.Renderer.PointerReleased();
        Invalidate();
    }

    private void OnCancelInteraction(object? sender, EventArgs e)
    {
        _drawable.Renderer.PointerReleased();
        Invalidate();
    }

    private sealed class MinimapDrawable : IDrawable, IDisposable
    {
        private MauiEditorCanvas? _canvas;

        public MinimapRenderer Renderer { get; } = new();

        public EditorModel? Model { get; set; }

        public float Width { get; set; }

        public Action? AfterPending { get; set; }

        public float Height { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Model is not { } model)
            {
                return;
            }

            try
            {
                // Rects and lines only: the canvas's font is never used, so the editor's family and size do.
                _canvas ??= new MauiEditorCanvas(model.Options.FontFamily, model.Options.FontSize);
                _canvas.Target = canvas;
                Renderer.Draw(_canvas, model, Width > 0 ? Width : dirtyRect.Width, Height > 0 ? Height : dirtyRect.Height);
                if (Renderer.Pending)
                {
                    AfterPending?.Invoke();
                }
            }
#pragma warning disable CA1031 // B2: nothing may leave the platform's draw callback; reported with its step and skipped.
            catch (Exception error)
#pragma warning restore CA1031
            {
                if (PaintGuard.Handle("editor: minimap", "draw the minimap", error))
                {
                    throw;                               // a lost device: Win2D's control makes it again
                }
            }
            finally
            {
                if (_canvas is not null)
                {
                    _canvas.Target = null;
                }
            }
        }

        public void Dispose()
        {
            Renderer.Dispose();
            _canvas?.Dispose();
        }
    }
}
