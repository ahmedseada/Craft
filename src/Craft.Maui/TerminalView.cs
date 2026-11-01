// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Editor.Terminal;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The <see cref="IDrawable"/> of a terminal: measures the code font inside the first frame (as the editor does), then
/// draws the visible rows through <see cref="TerminalRenderer"/> holding the screen's lock.
/// </summary>
public sealed class TerminalDrawable : IDrawable
{
    private readonly MauiTextMeasurer _measurer = new();
    private readonly TerminalRenderer _renderer = new();
    private MauiEditorCanvas? _canvas;
    private readonly CodeFontPicker _picker = new();

    /// <summary>The terminal drawn (nothing while null).</summary>
    public TerminalInstance? Terminal { get; set; }

    /// <summary>The colours.</summary>
    public TerminalPalette? Palette { get; set; }

    /// <summary>The settings (the code font, its fallback, the terminal's line spacing).</summary>
    public CraftOptions Options { get; set; } = new();

    /// <summary>Whether the terminal has the keyboard (a hollow cursor when not).</summary>
    public bool Focused { get; set; }

    /// <summary>Whether the cursor shows in this frame (the blink).</summary>
    public bool CursorOn { get; set; } = true;

    /// <summary>The viewport's width.</summary>
    public float ViewportWidth { get; set; }

    /// <summary>The viewport's height.</summary>
    public float ViewportHeight { get; set; }

    /// <summary>The font's measures (an estimate until the first frame measured it).</summary>
    public FontMetrics Metrics => _measurer.Measure(Font, Options.TerminalLineSpacing);

    /// <summary>The renderer (its counters are the paint's telemetry).</summary>
    public TerminalRenderer Renderer => _renderer;

    /// <summary>Raised inside a frame when the font's measure changed (the view resizes the terminal after the frame).</summary>
    public event EventHandler? Measured;

    private EditorFont Font => new(Options.FontFamily, Options.FontSize);

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (Terminal is not { } terminal || Palette is not { } palette)
        {
            return;
        }

        using var label = UiDispatch.Enter("terminal: paint");
        string step = "pick and measure the code font";
        try
        {
            bool picked;
            using (UiDispatch.Enter("terminal: pick and measure the code font"))
            {
                picked = _picker.Ensure(canvas, Options.FontFamily, Options.FontFallback, Options.FontSize, _measurer);
            }

            if (picked)
            {
                Measured?.Invoke(this, EventArgs.Empty);
            }

            step = "bind the canvas";
            _canvas = _picker.Canvas!;
            var metrics = Metrics;
            _canvas.SetMetrics(metrics);
            _canvas.Target = canvas;
            step = "draw the screen";
            float width = ViewportWidth > 0 ? ViewportWidth : dirtyRect.Width;
            float height = ViewportHeight > 0 ? ViewportHeight : dirtyRect.Height;
            lock (terminal.Screen.Sync)
            {
                _renderer.Draw(_canvas, terminal.Screen, palette, metrics, width, height, terminal.ScrollBack, terminal.Selection, Focused, CursorOn);
            }
        }
#pragma warning disable CA1031 // B2: nothing may leave the platform's draw callback (a stowed exception ends the process); reported with its step and skipped.
        catch (Exception error)
#pragma warning restore CA1031
        {
            if (PaintGuard.Handle("terminal: paint", step, error))
            {
                _canvas?.Target = null;
                _canvas?.ReleaseLayouts();
                throw;                                   // a lost device: Win2D's control makes it again
            }
        }
        finally
        {
            _canvas?.Target = null;
        }
    }
}

/// <summary>A link Ctrl+clicked in a terminal (X part 9): the path as written, the place it names (from 1), and the terminal's folder.</summary>
/// <param name="Path">The path as written.</param>
/// <param name="TargetLine">The line it names, from 1.</param>
/// <param name="TargetColumn">The column it names, from 1; 0 when none.</param>
/// <param name="Folder">The terminal's folder now (its shell's, else where it started): a relative path is read against it.</param>
public sealed record TerminalLinkClick(string Path, int TargetLine, int TargetColumn, string? Folder);

/// <summary>
/// A terminal's surface: a <see cref="ScrollView"/> over a spacer as tall as the scrollback and the screen (the
/// platform's scroll bar and wheel), with a <see cref="GraphicsView"/> kept over the viewport that draws the visible rows.
/// The size in cells follows the measured viewport; a drag selects; the keyboard comes through the platform's
/// <see cref="ITextInputAdapter"/> into <see cref="TerminalManager.Input"/>. It follows the output while at the bottom
/// and keeps its place when scrolled back.
/// </summary>
public sealed class TerminalView : ContentView, IDisposable
{
    private readonly TerminalManager _manager;
    private readonly ScrollView _scroll;
    private readonly Grid _content;
    private readonly GraphicsView _graphics;
    private readonly TerminalDrawable _drawable = new();
    private IDisposable? _inputConnection;
    private IDispatcherTimer? _blink;
    private TerminalInstance? _terminal;
    private bool _scrolling;
    private bool _disposed;
    private (long Line, int Column)? _anchor;

    /// <summary>A view of <paramref name="manager"/>'s active terminal.</summary>
    public TerminalView(TerminalManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _drawable.Measured += (_, _) => Dispatcher.DispatchLabelled("terminal: size", UpdateSize);
        _graphics = new GraphicsView { Drawable = _drawable, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        _content = new Grid { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Start };
        _content.Children.Add(_graphics);
        _scroll = new ScrollView { Orientation = ScrollOrientation.Vertical, VerticalScrollBarVisibility = ScrollBarVisibility.Default, Content = _content };
        Content = _scroll;
        _scroll.Scrolled += OnScrolled;
        _scroll.SizeChanged += (_, _) => UpdateSize();
        _graphics.StartInteraction += OnStartInteraction;
        _graphics.DragInteraction += OnDragInteraction;
        _graphics.EndInteraction += OnEndInteraction;
        _graphics.MoveHoverInteraction += OnMoveHoverInteraction;
        _graphics.EndHoverInteraction += OnEndHoverInteraction;
        _graphics.HandlerChanged += OnGraphicsHandlerChanged;
        _graphics.Focused += (_, _) =>
        {
            SetFocused(true);
            if (_terminal is { } focused)
            {
                Activated?.Invoke(this, focused);           // B9: a click on a split's pane makes it the active terminal
            }
        };
        _graphics.Unfocused += (_, _) => SetFocused(false);
        // B9: the terminal's context menu (Copy, Paste, Select all, Clear, Split, Rename, Kill).
        var context = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        context.Tapped += (_, e) =>
        {
            if (_terminal is { } target)
            {
                ContextRequested?.Invoke(this, (target, e));
            }
        };
        _graphics.GestureRecognizers.Add(context);
        Unloaded += (_, _) => _blink?.Stop();
        Loaded += (_, _) => RestartBlink();
    }

    /// <summary>Raised when the view took the keyboard, with its terminal (B9: the split pane clicked becomes the active one).</summary>
    public event EventHandler<TerminalInstance>? Activated;

    /// <summary>Raised on Ctrl+click on a file:line link (X part 9: the page opens the file there).</summary>
    public event EventHandler<TerminalLinkClick>? LinkActivated;

    /// <summary>Raised on a right-click, with its terminal and where (B9: the page opens the terminal's context menu).</summary>
    public event EventHandler<(TerminalInstance Terminal, TappedEventArgs Where)>? ContextRequested;

    /// <summary>The terminal shown, or null.</summary>
    public TerminalInstance? Terminal
    {
        get => _terminal;
        set
        {
            if (ReferenceEquals(_terminal, value))
            {
                return;
            }

            if (_terminal is not null)
            {
                _terminal.Invalidated -= OnTerminalInvalidated;
            }

            _terminal = value;
            _drawable.Terminal = value;
            _anchor = null;
            if (value is not null)
            {
                value.Invalidated += OnTerminalInvalidated;
            }

            UpdateSize();
            Follow();
            _graphics.Invalidate();
        }
    }

    /// <summary>Takes the theme's colours and the options' font.</summary>
    public void Apply(ITheme theme, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);
        _drawable.Palette = new TerminalPalette(theme);
        _drawable.Options = options;
        _drawable.Renderer.LinksUnderline = options.LinksUnderline;
        BackgroundColor = ThemeColors.Of(_drawable.Palette.Background);
        // B9: room around the text, as an inset of the view (the ground's colour fills it; the paint is unchanged).
        Padding = new Thickness(options.TerminalPadding);
        RestartBlink();
        UpdateSize();
        _graphics.Invalidate();
    }

    /// <summary>Gives the terminal the keyboard.</summary>
    public void FocusTerminal() => _graphics.Focus();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _blink?.Stop();
        Terminal = null;
        _inputConnection?.Dispose();
        _inputConnection = null;
    }

    private void SetFocused(bool focused)
    {
        _drawable.Focused = focused;
        _drawable.CursorOn = true;
        _graphics.Invalidate();
    }

    private void OnTerminalInvalidated(object? sender, EventArgs e)
    {
        UpdateContentHeight();
        Follow();
        _graphics.Invalidate();
    }

    /// <summary>The terminal's size in cells from the viewport and the font; the next terminal starts at it too.</summary>
    private void UpdateSize()
    {
        double width = Math.Max(0, _scroll.Width);
        double height = Math.Max(0, _scroll.Height);
        _graphics.WidthRequest = width;
        _graphics.HeightRequest = height;
        _drawable.ViewportWidth = (float)width;
        _drawable.ViewportHeight = (float)height;
        var metrics = _drawable.Metrics;
        if (width > 0 && height > 0 && metrics.CellWidth > 0 && metrics.LineHeight > 0)
        {
            var size = new TerminalSize(Math.Max(2, (int)(width / metrics.CellWidth)), Math.Max(1, (int)(height / metrics.LineHeight))).Clamped();
            _manager.LastSize = size;
            _terminal?.Resize(size);
        }

        UpdateContentHeight();
        Follow();
        _graphics.Invalidate();
    }

    private void UpdateContentHeight()
    {
        float line = Math.Max(1, _drawable.Metrics.LineHeight);
        int lines = 0;
        if (_terminal is { } terminal)
        {
            lock (terminal.Screen.Sync)
            {
                lines = terminal.Screen.ScrollbackLines;
            }
        }

        _content.HeightRequest = Math.Max(0, _scroll.Height) + (lines * line);
    }

    /// <summary>Brings the scroll view to the terminal's scroll position (the bottom when it follows the output).</summary>
    private void Follow()
    {
        if (_terminal is not { } terminal)
        {
            return;
        }

        float line = Math.Max(1, _drawable.Metrics.LineHeight);
        int scrollback;
        lock (terminal.Screen.Sync)
        {
            scrollback = terminal.Screen.ScrollbackLines;
        }

        double target = Math.Max(0, (scrollback - terminal.ScrollBack) * line);
        _graphics.TranslationY = target;
        if (Math.Abs(_scroll.ScrollY - target) > line / 2)
        {
            _scrolling = true;
            _ = ScrollAsync(target);
        }
    }

    private async Task ScrollAsync(double target)
    {
        try
        {
            await _scroll.ScrollToAsync(0, target, false);
        }
        finally
        {
            _scrolling = false;
        }
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        _graphics.TranslationY = e.ScrollY;
        if (!_scrolling && _terminal is { } terminal)
        {
            float line = Math.Max(1, _drawable.Metrics.LineHeight);
            int scrollback;
            lock (terminal.Screen.Sync)
            {
                scrollback = terminal.Screen.ScrollbackLines;
            }

            terminal.ScrollBack = scrollback - (int)Math.Round(e.ScrollY / line);
        }

        _graphics.Invalidate();
    }

    private (long Line, int Column) CellAt(PointF point)
    {
        var metrics = _drawable.Metrics;
        var terminal = _terminal!;
        lock (terminal.Screen.Sync)
        {
            long top = terminal.Screen.ScreenTop - terminal.ScrollBack;
            long line = top + (long)Math.Floor(point.Y / Math.Max(1, metrics.LineHeight));
            int column = Math.Clamp((int)Math.Round(point.X / Math.Max(1, metrics.CellWidth)), 0, terminal.Screen.Columns);
            return (line, column);
        }
    }

    // Raised from the platform's pointer callback: nothing may leave it (the owner's note 20).
    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            OnStartInteractionCore(e);
        }
#pragma warning disable CA1031 // Reported by the guard; the terminal goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the terminal: a pointer", error);
        }
    }

    private void OnStartInteractionCore(TouchEventArgs e)
    {
        if (_terminal is null || e.Touches.Length == 0)
        {
            return;
        }

        // X part 9: Ctrl+click on a link opens it (no selection starts).
        if (Control && LinkAt(e.Touches[0]) is { } link)
        {
            _anchor = null;
            var terminal = _terminal;
            string? folder;
            lock (terminal.Screen.Sync)
            {
                folder = terminal.Screen.WorkingDirectory;
            }

            LinkActivated?.Invoke(this, new TerminalLinkClick(link.Path, link.TargetLine, link.TargetColumn, folder ?? terminal.WorkingDirectory));
            return;
        }

        _anchor = CellAt(e.Touches[0]);
        _terminal.Selection = null;
        _graphics.Invalidate();
    }

    // Whether Ctrl is held now (the platform's input connection knows; elsewhere never).
    private bool Control => _inputConnection is IKeyModifierSource source && (source.CurrentModifiers & KeyModifiers.Control) != 0;

    // The link on the cell under the point, or null (links off: none).
    private TerminalLink? LinkAt(PointF point)
    {
        if (_terminal is not { } terminal || string.Equals(_drawable.Options.LinksUnderline, "off", StringComparison.Ordinal))
        {
            return null;
        }

        var metrics = _drawable.Metrics;
        lock (terminal.Screen.Sync)
        {
            long line = terminal.Screen.ScreenTop - terminal.ScrollBack + (long)Math.Floor(point.Y / Math.Max(1, metrics.LineHeight));
            int column = (int)Math.Floor(point.X / Math.Max(1, metrics.CellWidth));
            return TerminalLinks.At(terminal.Screen, line, column);
        }
    }

    // X part 9: the link under the pointer is underlined while Ctrl is held (the click that opens it needs Ctrl).
    private void OnMoveHoverInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            (long, int, int)? hovered = null;
            if (e.Touches.Length > 0 && Control && LinkAt(e.Touches[0]) is { } link)
            {
                hovered = (link.Line, link.StartColumn, link.EndColumn);
            }

            if (_drawable.Renderer.HoveredLink != hovered)
            {
                _drawable.Renderer.HoveredLink = hovered;
                _graphics.Invalidate();
            }
        }
#pragma warning disable CA1031 // Reported by the guard; the terminal goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the terminal: a hover", error);
        }
    }

    private void OnEndHoverInteraction(object? sender, EventArgs e)
    {
        if (_drawable.Renderer.HoveredLink is not null)
        {
            _drawable.Renderer.HoveredLink = null;
            _graphics.Invalidate();
        }
    }

    // Raised from the platform's pointer callback: nothing may leave it (the owner's note 20).
    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            OnDragInteractionCore(e);
        }
#pragma warning disable CA1031 // Reported by the guard; the terminal goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the terminal: a pointer", error);
        }
    }

    private void OnDragInteractionCore(TouchEventArgs e)
    {
        if (_terminal is null || e.Touches.Length == 0 || _anchor is not { } anchor)
        {
            return;
        }

        var (line, column) = CellAt(e.Touches[0]);
        _terminal.Selection = new TerminalSelection(anchor.Line, anchor.Column, line, column);
        _graphics.Invalidate();
    }

    // Raised from the platform's pointer callback: nothing may leave it (the owner's note 20).
    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        try
        {
            OnEndInteractionCore();
        }
#pragma warning disable CA1031 // Reported by the guard; the terminal goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the terminal: a pointer", error);
        }
    }

    private void OnEndInteractionCore()
    {
        _anchor = null;
        if (_terminal?.Selection is { IsEmpty: true })
        {
            _terminal.Selection = null;
        }

        _graphics.Focus();
    }

    private void OnGraphicsHandlerChanged(object? sender, EventArgs e)
    {
        _inputConnection?.Dispose();
        _inputConnection = null;
#if WINDOWS
        if (_graphics.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            _inputConnection = new WinUI.WindowsTextInputAdapter().Attach(element, _manager.Input);
        }
#endif
    }

    private void RestartBlink()
    {
        int ms = _drawable.Options.CaretBlinkMilliseconds;
        if (ms <= 0 || !_drawable.Options.TerminalCursorBlink)
        {
            _blink?.Stop();
            _drawable.CursorOn = true;
            return;
        }

        if (_blink is null)
        {
            _blink = Dispatcher.CreateTimer();
            _blink.Tick += (_, _) =>
            {
                _drawable.CursorOn = !_drawable.CursorOn;
                _graphics.Invalidate();
            };
        }

        _blink.Stop();
        _blink.Interval = TimeSpan.FromMilliseconds(ms);
        _blink.Start();
    }
}
