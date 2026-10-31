// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
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
    private string? _fellBackFrom;

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

        string family = string.Equals(_fellBackFrom, Options.FontFamily, StringComparison.Ordinal) ? Options.FontFallback : Options.FontFamily;
        if (_canvas is null || _canvas.Font.Name != family || _canvas.FontSize != Options.FontSize)
        {
            _canvas = new MauiEditorCanvas(family, Options.FontSize);
        }

        if (_measurer.EnsureMeasured(canvas, Font, _canvas.Font))
        {
            if (!_measurer.IsMonospaced && family != Options.FontFallback)
            {
                _fellBackFrom = Options.FontFamily;
                _canvas = new MauiEditorCanvas(Options.FontFallback, Options.FontSize);
                _measurer.Reset();
                _measurer.EnsureMeasured(canvas, Font, _canvas.Font);
            }

            Measured?.Invoke(this, EventArgs.Empty);
        }

        var metrics = Metrics;
        _canvas.SetMetrics(metrics);
        _canvas.Target = canvas;
        try
        {
            float width = ViewportWidth > 0 ? ViewportWidth : dirtyRect.Width;
            float height = ViewportHeight > 0 ? ViewportHeight : dirtyRect.Height;
            lock (terminal.Screen.Sync)
            {
                _renderer.Draw(_canvas, terminal.Screen, palette, metrics, width, height, terminal.ScrollBack, terminal.Selection, Focused, CursorOn);
            }
        }
        finally
        {
            _canvas.Target = null;
        }
    }
}

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
        _drawable.Measured += (_, _) => Dispatcher.Dispatch(UpdateSize);
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
        _graphics.HandlerChanged += OnGraphicsHandlerChanged;
        _graphics.Focused += (_, _) => SetFocused(true);
        _graphics.Unfocused += (_, _) => SetFocused(false);
        Unloaded += (_, _) => _blink?.Stop();
        Loaded += (_, _) => RestartBlink();
    }

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
        BackgroundColor = ThemeColors.Of(_drawable.Palette.Background);
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

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (_terminal is null || e.Touches.Length == 0)
        {
            return;
        }

        _anchor = CellAt(e.Touches[0]);
        _terminal.Selection = null;
        _graphics.Invalidate();
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (_terminal is null || e.Touches.Length == 0 || _anchor is not { } anchor)
        {
            return;
        }

        var (line, column) = CellAt(e.Touches[0]);
        _terminal.Selection = new TerminalSelection(anchor.Line, anchor.Column, line, column);
        _graphics.Invalidate();
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
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
        if (ms <= 0)
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

/// <summary>
/// The bottom panel's Terminal tab (phase T): a strip of the open terminals (each its title, ✕ to kill it, the active one
/// over the accent's line), "+" for a new one, and the active terminal's <see cref="TerminalView"/>; with none open, where
/// the next one starts and how to open it. A plug-in's read-only output tab (phase U2: "Agent sandbox") is marked so.
/// </summary>
public sealed class TerminalPanelView : ContentView, IDisposable
{
    private readonly TerminalManager _manager;
    private readonly TerminalView _view;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 14, Padding = new Thickness(16, 0) };
    private readonly ContentView _body = new();
    private readonly Label _none;
    private ShellPalette _palette;

    /// <summary>The tab over <paramref name="manager"/>.</summary>
    public TerminalPanelView(TerminalManager manager, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(palette);
        _manager = manager;
        _palette = palette;
        _view = new TerminalView(manager);
        _none = palette.Label("", palette.Secondary);
        _none.Margin = new Thickness(16, 8);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = _manager.ExecuteNewAsync();
        _none.GestureRecognizers.Add(tap);
        var grid = new Grid { RowDefinitions = [new RowDefinition(new GridLength(26)), new RowDefinition(GridLength.Star)] };
        grid.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _tabs }, 0, 0);
        grid.Add(_body, 0, 1);
        Content = grid;
        manager.Changed += OnChanged;
        Apply(palette);
    }

    /// <summary>Gives the active terminal the keyboard.</summary>
    public void FocusTerminal() => _view.FocusTerminal();

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _view.Apply(palette.Theme, palette.Options);
        _none.TextColor = palette.Secondary;
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _manager.Changed -= OnChanged;
        _view.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        _view.Terminal = _manager.ActiveTerminal;
        _tabs.Children.Clear();
        foreach (var terminal in _manager.Terminals)
        {
            bool active = ReferenceEquals(terminal, _manager.ActiveTerminal);
            string title = terminal.IsReadOnly ? terminal.Title + " · read-only" : terminal.HasExited ? $"{terminal.Title} (ended {terminal.ExitCode})" : terminal.Title;
            var label = _palette.Label(title, active ? _palette.Text : _palette.Secondary, _palette.CaptionSize);
            if (terminal.IsReadOnly)
            {
                ToolTipProperties.SetText(label, "A plug-in's output: nothing typed here runs (✕ closes it; it opens again with the next output)");
            }
            var close = _palette.Label("✕", _palette.Secondary, _palette.CaptionSize);
            var row = new HorizontalStackLayout { Spacing = 6, Children = { label, close } };
            var cell = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(new GridLength(1))] };
            cell.Add(row, 0, 0);
            cell.Add(new BoxView { Color = active ? _palette.Accent : Colors.Transparent, HeightRequest = 1 }, 0, 1);
            var chosen = terminal;
            var pick = new TapGestureRecognizer();
            pick.Tapped += (_, _) =>
            {
                _manager.Activate(chosen);
                _view.FocusTerminal();
            };
            label.GestureRecognizers.Add(pick);
            var kill = new TapGestureRecognizer();
            kill.Tapped += (_, _) => _manager.Close(chosen);
            close.GestureRecognizers.Add(kill);
            _tabs.Children.Add(cell);
        }

        var add = _palette.Label("+", _palette.Secondary, _palette.FontSize);
        var addTap = new TapGestureRecognizer();
        addTap.Tapped += (_, _) => _ = _manager.ExecuteNewAsync();
        add.GestureRecognizers.Add(addTap);
        _tabs.Children.Add(add);
        if (_manager.ActiveTerminal is null)
        {
            var options = _palette.Options;
            var start = TerminalLaunch.StartFolder(null, options.TerminalWorkingDirectory, _manager.WorkspaceFolder, _manager.HomeFolder);
            _none.Text = $"No terminal is open. Ctrl+Shift+` (or + above, or a click here) opens your shell in {start.Folder} ({start.Reason}).";
            _body.Content = _none;
        }
        else
        {
            _body.Content = _view;
        }
    }
}
