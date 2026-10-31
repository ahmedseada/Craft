// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.Maui.Controls;
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
/// A panel of the shell that collapses and resizes (phase U2): its content beside a splitter, the one control the side
/// view, the chat and agent column and the bottom panel all use. The splitter is a thin line inside a wider grip a drag
/// takes (the pointer over it shows the accent); a drag sizes the panel live (<see cref="Resizing"/>), kept within
/// <see cref="Clamp"/>, and its end raises <see cref="Resized"/> once (the shell writes the size to the workspace's
/// settings then, not on every move). <see cref="Fill"/> (the narrow layout) gives the content all the room, with no
/// splitter. Collapsing is the shell's: it hides the control. No platform code (MAUI's pan and pointer gestures).
/// </summary>
public sealed class SplitPanel : Grid
{
    private const double GripThickness = 6;          // the grab area around the one-unit line
    private readonly SplitEdge _edge;
    private readonly ContentView _host = new();
    private readonly Grid _grip = new() { BackgroundColor = Colors.Transparent };
    private readonly BoxView _line = new();
    private const string AnimationName = "craft-split";
    private double _size;
    private bool? _target;
    private bool _animating;
    private double _dragStart;
    private bool _fill;
    private bool _resizable = true;
    private bool _hovered;
    private Color _lineColor = Colors.Gray;
    private Color _hoverColor = Colors.Orange;

    /// <summary>A panel with its splitter at <paramref name="edge"/>.</summary>
    public SplitPanel(SplitEdge edge)
    {
        _edge = edge;
        bool vertical = edge != SplitEdge.Top;
        if (vertical)
        {
            _grip.WidthRequest = GripThickness;
            _line.WidthRequest = 1;
            _line.HorizontalOptions = LayoutOptions.Center;
            _line.VerticalOptions = LayoutOptions.Fill;
            ColumnDefinitions = edge == SplitEdge.Start
                ? [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)]
                : [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];
            this.Add(_grip, edge == SplitEdge.Start ? 0 : 1, 0);
            this.Add(_host, edge == SplitEdge.Start ? 1 : 0, 0);
        }
        else
        {
            _grip.HeightRequest = GripThickness;
            _line.HeightRequest = 1;
            _line.VerticalOptions = LayoutOptions.Center;
            _line.HorizontalOptions = LayoutOptions.Fill;
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)];
            this.Add(_grip, 0, 0);
            this.Add(_host, 0, 1);
        }

        _grip.Add(_line);
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        _grip.GestureRecognizers.Add(pan);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHovered(true);
        pointer.PointerExited += (_, _) => SetHovered(false);
        _grip.GestureRecognizers.Add(pointer);
        SemanticProperties.SetDescription(_grip, vertical ? "Drag to resize the panel's width" : "Drag to resize the panel's height");
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
            _grip.IsVisible = value && !_fill;
        }
    }

    /// <summary>Keeps a dragged size within what the shell allows (the minimum, the room the editor keeps); none: any positive size.</summary>
    public Func<double, double>? Clamp { get; set; }

    /// <summary>Raised on each move of a drag with the size now.</summary>
    public event EventHandler<double>? Resizing;

    /// <summary>Raised once when a drag ends, with the size it left.</summary>
    public event EventHandler<double>? Resized;

    /// <summary>The splitter's line colour and the colour it takes under the pointer and while dragged.</summary>
    public void SetColors(Color line, Color hover)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(hover);
        _lineColor = line;
        _hoverColor = hover;
        _line.Color = _hovered ? hover : line;
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        _line.Color = hovered ? _hoverColor : _lineColor;
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
        if (first || milliseconds <= 0 || _fill || _size <= 0 || (visible == IsVisible && !visible))
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

        double size = _fill ? -1 : Math.Max(0, _size);
        if (_edge == SplitEdge.Top)
        {
            _host.HeightRequest = size;
        }
        else
        {
            _host.WidthRequest = size;
        }

        _grip.IsVisible = _resizable && !_fill;
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (_fill || !_resizable)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragStart = _size;
                SetHovered(true);
                break;
            case GestureStatus.Running:
                double delta = _edge switch
                {
                    SplitEdge.Start => -e.TotalX,
                    SplitEdge.End => e.TotalX,
                    _ => -e.TotalY,
                };
                double size = _dragStart + delta;
                size = Clamp is { } clamp ? clamp(size) : Math.Max(1, size);
                if (Math.Abs(size - _size) >= 0.5)
                {
                    Size = size;
                    Resizing?.Invoke(this, size);
                }

                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SetHovered(false);
                if (Math.Abs(_size - _dragStart) >= 0.5)
                {
                    Resized?.Invoke(this, _size);
                }

                break;
        }
    }
}
