// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// A bottom panel's list drawn on one surface (the owner's note 26: the metrics table, Problems): a vertical
/// <see cref="ScrollView"/> over a spacer of the list's height, with a viewport-sized <see cref="GraphicsView"/> kept over
/// the part in view, which draws only the rows there (<see cref="PanelList.Draw"/>) in the code font. The pointer picks
/// and hovers through the GraphicsView's interactions; the keyboard comes through the platform's input adapter as the
/// editor's does (Up, Down, Ctrl+A, Ctrl+C, Enter, Escape go to the list, any other key to the window's keys). No
/// checkboxes, no row chrome.
/// </summary>
public sealed class PanelListView : ContentView, IDisposable
{
    private readonly PanelList _list;
    private readonly ScrollView _scroll;
    private readonly Grid _content;
    private readonly GraphicsView _graphics;
    private readonly ListDrawable _drawable;
    private readonly Sink _sink;
    private IDisposable? _input;
    private CraftOptions _options;
    private ITheme _theme;
    private bool _disposed;

    /// <summary>A view of <paramref name="list"/> in <paramref name="options"/>' code font and <paramref name="theme"/>'s colours.</summary>
    public PanelListView(PanelList list, CraftOptions options, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);
        _list = list;
        _options = options;
        _theme = theme;
        _drawable = new ListDrawable(this);
        _sink = new Sink(this);
        _graphics = new GraphicsView { Drawable = _drawable, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Start };
        _content = new Grid { VerticalOptions = LayoutOptions.Start };
        _content.Children.Add(_graphics);
        _scroll = new ScrollView { Orientation = ScrollOrientation.Vertical, Content = _content };
        Content = _scroll;
        _scroll.Scrolled += (_, e) =>
        {
            _graphics.TranslationY = e.ScrollY;
            _graphics.Invalidate();
        };
        _scroll.SizeChanged += (_, _) => UpdateSize();
        _graphics.StartInteraction += OnPress;
        _graphics.MoveHoverInteraction += OnHover;
        _graphics.EndHoverInteraction += (_, _) => SetHover(-1);
        _graphics.HandlerChanged += OnGraphicsHandlerChanged;
        list.Changed += OnListChanged;
        list.RevealRequested += OnReveal;
        list.CopyRequested += OnCopy;
    }

    /// <summary>Where a key the list does not take goes (the window's keys: the bindings, the menus), or null.</summary>
    public IEditorKeyHandler? WindowKeys { get; set; }

    /// <summary>The list shown.</summary>
    public PanelList List => _list;

    /// <summary>Takes new settings (the code font) and colours.</summary>
    public void Apply(CraftOptions options, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);
        _options = options;
        _theme = theme;
        UpdateSize();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _list.Changed -= OnListChanged;
        _list.RevealRequested -= OnReveal;
        _list.CopyRequested -= OnCopy;
        _input?.Dispose();
        _input = null;
    }

    private void UpdateSize()
    {
        var metrics = _drawable.Metrics;
        double height = Math.Max(_scroll.Height, _list.ContentHeight(metrics));
        _content.HeightRequest = Math.Max(0, height);
        _graphics.HeightRequest = Math.Max(0, _scroll.Height);
        _graphics.Invalidate();
    }

    private void OnListChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("panel list: changed", () => OnListChanged(sender, e));
            return;
        }

        UpdateSize();
    }

    private void OnReveal(object? sender, int row)
    {
        var metrics = _drawable.Metrics;
        float rowHeight = PanelList.RowHeight(metrics);
        double top = row * rowHeight;
        double view = Math.Max(0, _scroll.Height - _list.HeaderHeight(metrics));
        double y = _scroll.ScrollY;
        if (top < y)
        {
            y = top;
        }
        else if (top + rowHeight > y + view)
        {
            y = top + rowHeight - view;
        }

        if (y != _scroll.ScrollY)
        {
            _ = _scroll.ScrollToAsync(0, y, false);
        }
    }

    private void OnCopy(object? sender, string text) => _ = CopyAsync(text);

    private static async Task CopyAsync(string text)
    {
        try
        {
            await MauiEditorClipboard.Shared.SetTextAsync(text);
        }
#pragma warning disable CA1031 // A copy that fails is logged by the dispatched clipboard; the panel goes on.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnPress(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }

        var point = e.Touches[0];
        var modifiers = _input is IKeyModifierSource source ? source.CurrentModifiers : KeyModifiers.None;
        using var label = UiDispatch.Enter("panel list: pointer pressed");
        _list.Press(_drawable.Metrics, point.X, point.Y, (float)_scroll.ScrollY, (float)_graphics.Width, modifiers);
        _graphics.Invalidate();
    }

    private void OnHover(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length > 0)
        {
            SetHover(_list.RowAt(_drawable.Metrics, e.Touches[0].Y, (float)_scroll.ScrollY));
        }
    }

    private void SetHover(int row)
    {
        row = row < 0 ? -1 : row;
        if (row != _list.Hover)
        {
            _list.Hover = row;
            _graphics.Invalidate();
        }
    }

    private void OnGraphicsHandlerChanged(object? sender, EventArgs e)
    {
        _input?.Dispose();
        _input = null;
#if WINDOWS
        if (_graphics.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            _input = new WinUI.WindowsTextInputAdapter().Attach(element, _sink);
        }
#endif
    }

    /// <summary>The keyboard's sink: the list's keys first, the window's after.</summary>
    private sealed class Sink(PanelListView view) : IEditorInputSink
    {
        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            using var label = UiDispatch.Enter("panel list: key");
            if (view._list.OnKey(key, modifiers))
            {
                view._graphics.Invalidate();
                return true;
            }

            return view.WindowKeys?.HandleKey(key, modifiers, null) ?? false;
        }

        public void OnText(string text)
        {
        }
    }

    /// <summary>Picks and measures the code font on the first frame, then draws the list at the scroll offset.</summary>
    private sealed class ListDrawable(PanelListView view) : IDrawable
    {
        private readonly CodeFontPicker _picker = new();
        private readonly MauiTextMeasurer _measurer = new();

        public FontMetrics Metrics { get; private set; } = new(8, 18, 14);

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            using var label = UiDispatch.Enter("panel list: paint");
            string step = "pick and measure the code font";
            MauiEditorCanvas? target = null;
            try
            {
                var options = view._options;
                if (_picker.Ensure(canvas, options.FontFamily, options.FontFallback, options.FontSize, _measurer))
                {
                    var metrics = _measurer.Measure(new EditorFont(options.FontFamily, options.FontSize), options.LineSpacing);
                    if (metrics != Metrics)
                    {
                        Metrics = metrics;
                        view.Dispatcher.DispatchLabelled("panel list: measured", view.UpdateSize);
                    }
                }

                target = _picker.Canvas;
                if (target is null)
                {
                    return;
                }

                step = "bind the canvas";
                target.SetMetrics(Metrics);
                target.Target = canvas;
                step = "draw the rows";
                view._list.Draw(target, Metrics, view._theme.Colors, (float)view._scroll.ScrollY, dirtyRect.Width, dirtyRect.Height);
            }
#pragma warning disable CA1031 // B2: nothing may leave the platform's draw callback (a stowed exception ends the process); reported with its step and skipped.
            catch (Exception error)
#pragma warning restore CA1031
            {
                if (PaintGuard.Handle("panel list: paint", step, error))
                {
                    target?.Target = null;
                    target?.ReleaseLayouts();
                    throw;                               // a lost device: Win2D's control makes it again
                }
            }
            finally
            {
                target?.Target = null;
            }
        }
    }
}
