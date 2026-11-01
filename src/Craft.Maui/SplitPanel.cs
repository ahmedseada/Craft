// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>Where a <see cref="SplitPanel"/>'s splitter sits against its panel.</summary>
public enum SplitEdge
{
    /// <summary>At the panel's left: a column at the window's right (the chat and agent column); dragging left widens it.</summary>
    Start,

    /// <summary>At the panel's right: a column at the window's left (the rail's side view); dragging right widens it.</summary>
    End,

    /// <summary>Above the panel: a row at the bottom (the bottom panel); dragging up makes it taller.</summary>
    Top,
}

/// <summary>
/// A panel of the shell that collapses and resizes (phase U2, made easy to drag in B8): its content beside a one-unit
/// line, the one control the side view, the chat and agent column and the bottom panel all use. The grab area is wider
/// than the line (<c>CRAFT_SPLITTER_GRIP</c>, centred on it, over both neighbours, taking no room); the line takes the
/// accent once the pointer rests on it (<c>CRAFT_SPLITTER_HOVER_MS</c>), while it is dragged and while it has the
/// keyboard. With the platform's pointer (<see cref="Pointer"/>: Windows) the pointer shows the resize arrows and a
/// drag captures it, so it starts at the press and never drops over the editor; elsewhere MAUI's pan drags it. The
/// behaviour is <see cref="SplitterModel"/>'s (headless): kept within <see cref="Clamp"/>, snapped closed below the
/// panel's smallest size and open again past it (<see cref="CollapseRequested"/> when a drag ends closed), a double-click
/// restores the default size, and the arrows step it (Shift: five steps). A drag sizes the panel live
/// (<see cref="Resizing"/>) and raises <see cref="Resized"/> once at its end. <see cref="Fill"/> (the narrow layout)
/// gives the content all the room, with no splitter.
/// </summary>
public sealed class SplitPanel : Grid, IPointerDrag
{
    private const string AnimationName = "craft-split";
    private readonly SplitEdge _edge;
    private readonly ContentView _host = new();
    private readonly GraphicsView _grip = new() { BackgroundColor = Colors.Transparent, Drawable = new NothingDrawn() };
    private readonly BoxView _line = new();
    private readonly SplitterModel _model;
    private IPointerCursor? _pointer;
    private IDisposable? _pointerHook;
    private IDisposable? _keys;
    private PanGestureRecognizer? _pan;
    private TapGestureRecognizer? _doubleTap;
    private IDispatcherTimer? _hoverTimer;
    private double _size;
    private double _gripWidth = 10;
    private int _hoverMilliseconds = 100;
    private bool? _target;
    private bool _animating;
    private bool _fill;
    private bool _resizable = true;
    private bool _hovered;
    private bool _lit;
    private bool _focused;
    private bool _collapsedByDrag;
    private Color _lineColor = Colors.Gray;
    private Color _hoverColor = Colors.Orange;

    /// <summary>A panel with its splitter at <paramref name="edge"/>.</summary>
    public SplitPanel(SplitEdge edge)
    {
        _edge = edge;
        bool vertical = edge != SplitEdge.Top;
        _model = new SplitterModel(edge == SplitEdge.End ? 1 : -1, vertical);
        if (vertical)
        {
            _line.WidthRequest = 1;
            _line.VerticalOptions = LayoutOptions.Fill;
            _grip.VerticalOptions = LayoutOptions.Fill;
            _grip.HorizontalOptions = edge == SplitEdge.Start ? LayoutOptions.Start : LayoutOptions.End;
            ColumnDefinitions = edge == SplitEdge.Start
                ? [new ColumnDefinition(new GridLength(1)), new ColumnDefinition(GridLength.Star)]
                : [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(1))];
            int line = edge == SplitEdge.Start ? 0 : 1;
            this.Add(_line, line, 0);
            this.Add(_host, 1 - line, 0);
            this.Add(_grip, 0, 0);
            Grid.SetColumnSpan((BindableObject)_grip, 2);
        }
        else
        {
            _line.HeightRequest = 1;
            _line.HorizontalOptions = LayoutOptions.Fill;
            _grip.HorizontalOptions = LayoutOptions.Fill;
            _grip.VerticalOptions = LayoutOptions.Start;
            RowDefinitions = [new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)];
            this.Add(_line, 0, 0);
            this.Add(_host, 0, 1);
            this.Add(_grip, 0, 0);
            Grid.SetRowSpan((BindableObject)_grip, 2);
        }

        ApplyGrip();
        UsePan();
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHovered(true);
        pointer.PointerExited += (_, _) => SetHovered(false);
        _grip.GestureRecognizers.Add(pointer);
        _grip.Focused += (_, _) => SetFocused(true);
        _grip.Unfocused += (_, _) => SetFocused(false);
        _grip.HandlerChanged += OnGripHandlerChanged;
        SemanticProperties.SetDescription(_grip, vertical
            ? "Splitter: drag, or use Left and Right (Shift for larger steps), to resize the panel's width; double-click restores it"
            : "Splitter: drag, or use Up and Down (Shift for larger steps), to resize the panel's height; double-click restores it");
        ToolTipProperties.SetText(_grip, "Drag to resize · double-click to restore the size");
    }

    /// <summary>The panel's content.</summary>
    public View? Panel
    {
        get => _host.Content;
        set => _host.Content = value;
    }

    /// <summary>The panel's width (a column) or height (the bottom row), device-independent units.</summary>
    public double Size
    {
        get => _size;
        set
        {
            if (value != _size)
            {
                _size = value;
                _model.SetSize(value);
                ApplySize();
            }
        }
    }

    /// <summary>Whether the content takes all the room it is given, with no splitter (the narrow layout).</summary>
    public bool Fill
    {
        get => _fill;
        set
        {
            if (value != _fill)
            {
                _fill = value;
                ApplySize();
            }
        }
    }

    /// <summary>Whether the splitter shows and takes drags.</summary>
    public bool Resizable
    {
        get => _resizable;
        set
        {
            _resizable = value;
            ApplySplitter();
        }
    }

    /// <summary>Keeps a dragged size within what the shell allows (the minimum, the room the editor keeps); none: any positive size.</summary>
    public Func<double, double>? Clamp
    {
        get => _model.Clamp;
        set => _model.Clamp = value;
    }

    /// <summary>The size a double-click restores.</summary>
    public double DefaultSize
    {
        get => _model.DefaultSize;
        set => _model.DefaultSize = value;
    }

    /// <summary>The panel's smallest size: a drag below it snaps the panel closed (<c>CRAFT_SPLITTER_SNAP</c>).</summary>
    public double Minimum
    {
        get => _model.Minimum;
        set => _model.Minimum = value;
    }

    /// <summary>
    /// The platform's pointer (<see cref="IShellPlatform.PointerCursor"/>): the resize arrows and a captured drag; null
    /// keeps MAUI's pan gesture.
    /// </summary>
    public IPointerCursor? Pointer
    {
        get => _pointer;
        set
        {
            if (ReferenceEquals(value, _pointer))
            {
                return;
            }

            _pointerHook?.Dispose();
            _pointerHook = null;
            _pointer = value;
            if (value is null)
            {
                UsePan();
                return;
            }

            if (_pan is not null)
            {
                _grip.GestureRecognizers.Remove(_pan);
                _pan = null;
            }

            if (_doubleTap is not null)
            {
                _grip.GestureRecognizers.Remove(_doubleTap);
                _doubleTap = null;
            }

            _pointerHook = value.Attach(_grip, _edge == SplitEdge.Top ? PointerShape.ResizeNorthSouth : PointerShape.ResizeWestEast, this);
        }
    }

    /// <summary>Raised on each move of a drag (and each key step) with the size now.</summary>
    public event EventHandler<double>? Resizing;

    /// <summary>Raised once when a drag ends, a double-click restored the size or a key stepped it, with the size it left.</summary>
    public event EventHandler<double>? Resized;

    /// <summary>Raised when a drag ended below the panel's smallest size: the shell closes the panel (its size kept).</summary>
    public event EventHandler? CollapseRequested;

    /// <summary>Takes the splitters' options: the grab area, the hover delay, the key step, the snap.</summary>
    public void Apply(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gripWidth = options.SplitterGrip;
        _hoverMilliseconds = options.SplitterHoverMilliseconds;
        _model.KeyStep = options.SplitterKeyStep;
        _model.Snap = options.SplitterSnap;
        ApplyGrip();
    }

    /// <summary>The splitter's line colour and the colour it takes under the pointer, while dragged and with the keyboard.</summary>
    public void SetColors(Color line, Color hover)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(hover);
        _lineColor = line;
        _hoverColor = hover;
        Highlight();
    }

    /// <summary>Whether it is shown or opening (false while it closes).</summary>
    public bool IsShown => _target ?? IsVisible;

    /// <summary>
    /// Shows or hides the panel, its size growing from nothing or shrinking to nothing over <paramref name="milliseconds"/>
    /// (phase X's motion; 0, the first call, or a panel that fills its place: at once). The same state again does nothing.
    /// </summary>
    public void SetVisible(bool visible, int milliseconds)
    {
        if (_target == visible)
        {
            return;
        }

        bool first = _target is null;
        _target = visible;
        this.AbortAnimation(AnimationName);
        _animating = false;
        bool snapped = _collapsedByDrag;
        _collapsedByDrag = false;
        if (first || milliseconds <= 0 || _fill || _size <= 0 || (visible == IsVisible && !visible) || snapped)
        {
            IsVisible = visible;
            ApplySize();
            return;
        }

        double full = Math.Max(0, _size);
        double from = visible ? 0 : CurrentSize();
        if (visible)
        {
            SetHostSize(0);
            IsVisible = true;
        }

        _animating = true;
        var animation = new Animation(SetHostSize, from, visible ? full : 0, visible ? Easing.CubicOut : Easing.CubicIn);
        animation.Commit(this, AnimationName, 16, (uint)milliseconds, finished: (_, cancelled) =>
        {
            if (cancelled)
            {
                return;
            }

            _animating = false;
            IsVisible = visible;
            ApplySize();
        });
    }

    // IPointerDrag: the platform's captured pointer, in the window's coordinates.
    void IPointerDrag.Pressed(double x, double y) => Guard(() =>
    {
        if (CanDrag())
        {
            _model.Begin(Along(x, y));
            Highlight();
        }
    });

    void IPointerDrag.Moved(double x, double y) => Guard(() => Show(_model.Move(Along(x, y))));

    void IPointerDrag.Released(double x, double y) => Guard(() => EndDrag(false));

    void IPointerDrag.Lost() => Guard(() => EndDrag(false));

    void IPointerDrag.DoubleClicked() => Guard(Reset);

    private double Along(double x, double y) => _edge == SplitEdge.Top ? y : x;

    private bool CanDrag() => !_fill && _resizable && !_animating;

    // Raised from the platform's pointer callback: nothing may leave it (the owner's note 20).
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Reported by the guard; the splitter goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("a splitter: the pointer", error);
        }
    }

    private void Show(SplitterState state)
    {
        if (!_model.Dragging)
        {
            return;
        }

        if (state.Collapsed)
        {
            SetHostSize(0);
            return;
        }

        if (Math.Abs(state.Size - _size) >= 0.5 || CurrentSize() < 0.5)
        {
            _size = state.Size;
            ApplySize();
            Resizing?.Invoke(this, state.Size);
        }
    }

    private void EndDrag(bool cancelled)
    {
        if (!_model.Dragging)
        {
            return;
        }

        var state = cancelled ? _model.Cancel() : _model.End();
        Highlight();
        if (state.Collapsed)
        {
            _collapsedByDrag = true;
            _size = state.Size;                              // the size it opens at again
            CollapseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _size = state.Size;
        ApplySize();
        Resized?.Invoke(this, _size);
    }

    private void Reset()
    {
        if (!CanDrag())
        {
            return;
        }

        var state = _model.Reset();
        _size = state.Size;
        ApplySize();
        Resizing?.Invoke(this, _size);
        Resized?.Invoke(this, _size);
    }

    private void OnKey(EditorKey key, KeyModifiers modifiers, out bool handled)
    {
        handled = false;
        if (!CanDrag() || _model.Key(key, modifiers) is not { } state)
        {
            return;
        }

        handled = true;
        if (key == EditorKey.Escape)
        {
            _size = state.Size;
            ApplySize();
            return;
        }

        _size = state.Size;
        ApplySize();
        Resizing?.Invoke(this, _size);
        Resized?.Invoke(this, _size);
    }

    private void UsePan()
    {
        if (_pan is null)
        {
            _pan = new PanGestureRecognizer();
            _pan.PanUpdated += OnPan;
            _grip.GestureRecognizers.Add(_pan);
        }

        if (_doubleTap is null)
        {
            _doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            _doubleTap.Tapped += (_, _) => Reset();
            _grip.GestureRecognizers.Add(_doubleTap);
        }
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started when CanDrag():
                _model.Begin(0);
                Highlight();
                break;
            case GestureStatus.Running:
                Show(_model.Move(_edge == SplitEdge.Top ? e.TotalY : e.TotalX));
                break;
            case GestureStatus.Completed:
                EndDrag(false);
                break;
            case GestureStatus.Canceled:
                EndDrag(true);
                break;
        }
    }

    private void OnGripHandlerChanged(object? sender, EventArgs e)
    {
        _keys?.Dispose();
        _keys = null;
#if WINDOWS
        // The splitter takes the keyboard (Tab reaches it): the arrows step it, Home and End, Enter restores, Escape cancels.
        if (_grip.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            _keys = new WinUI.WindowsTextInputAdapter().Attach(element, new KeySink(this));
        }
#endif
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        if (!hovered)
        {
            _hoverTimer?.Stop();
            _lit = false;
            Highlight();
            return;
        }

        if (_hoverMilliseconds <= 0)
        {
            _lit = true;
            Highlight();
            return;
        }

        if (_hoverTimer is null && Dispatcher is { } dispatcher)
        {
            _hoverTimer = dispatcher.CreateTimer();
            _hoverTimer.IsRepeating = false;
            _hoverTimer.Tick += (_, _) =>
            {
                _lit = _hovered;
                Highlight();
            };
        }

        if (_hoverTimer is not null)
        {
            _hoverTimer.Stop();
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(_hoverMilliseconds);
            _hoverTimer.Start();
        }
    }

    private void SetFocused(bool focused)
    {
        _focused = focused;
        Highlight();
    }

    private void Highlight() => _line.Color = _lit || _focused || _model.Dragging ? _hoverColor : _lineColor;

    private void ApplyGrip()
    {
        double offset = (_gripWidth - 1) / 2;
        if (_edge == SplitEdge.Top)
        {
            _grip.HeightRequest = _gripWidth;
            _grip.TranslationY = -offset;
        }
        else
        {
            _grip.WidthRequest = _gripWidth;
            _grip.TranslationX = _edge == SplitEdge.Start ? -offset : offset;
        }
    }

    private double CurrentSize() => _edge == SplitEdge.Top ? _host.Height : _host.Width;

    private void SetHostSize(double size)
    {
        if (_edge == SplitEdge.Top)
        {
            _host.HeightRequest = size;
        }
        else
        {
            _host.WidthRequest = size;
        }
    }

    private void ApplySize()
    {
        if (_animating)
        {
            return;                                      // the open or close animation sets the size until it ends
        }

        SetHostSize(_fill ? -1 : Math.Max(0, _size));
        ApplySplitter();
    }

    private void ApplySplitter()
    {
        bool shown = _resizable && !_fill;
        _grip.IsVisible = shown;
        _line.IsVisible = shown;
        var room = new GridLength(shown ? 1 : 0);
        if (_edge == SplitEdge.Top)
        {
            RowDefinitions[0].Height = room;
        }
        else
        {
            ColumnDefinitions[_edge == SplitEdge.Start ? 0 : 1].Width = room;
        }
    }

    /// <summary>The grip draws nothing: the line beside it is the splitter's look.</summary>
    private sealed class NothingDrawn : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
        }
    }

#if WINDOWS
    /// <summary>The focused splitter's keys.</summary>
    private sealed class KeySink(SplitPanel owner) : IEditorInputSink
    {
        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            bool handled = false;
            Guard(() => owner.OnKey(key, modifiers, out handled));
            return handled;
        }

        public void OnText(string text)
        {
        }
    }
#endif
}
