// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Editor.Activities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Craft's own progress bar (the owner, 2026-11-01: never the platform's): a thin bar of the accent on a track with rounded
/// ends, drawn on a canvas from <see cref="ProgressBarModel"/>. Determinate when <see cref="Fraction"/> is set (the fill eases
/// to it), a soft moving segment when it is null. Its frame timer runs only while the bar moves and is shown (visible and
/// in the window), and stops when it settles or is hidden; a frame allocates nothing. The status bar's activity, the
/// notification centre's rows, the global loader, the Models page's rows and the panels' meters all draw with it.
/// </summary>
public sealed class ProgressBarView : GraphicsView
{
    private readonly ProgressBarModel _model;
    private readonly BarDrawable _drawable;
    private IDispatcherTimer? _timer;
    private long _last;
    private bool _loaded;

    /// <summary>A bar <paramref name="height"/> tall whose moving segment crosses it in <paramref name="cycleMilliseconds"/>.</summary>
    public ProgressBarView(double height = 4, double cycleMilliseconds = 1400)
    {
        _model = new ProgressBarModel(cycleMilliseconds);
        _drawable = new BarDrawable(_model);
        Drawable = _drawable;
        HeightRequest = height;
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
        Loaded += (_, _) =>
        {
            _loaded = true;
            Run();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            Stop();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
            {
                Run();
            }
        };
    }

    /// <summary>The share done (0 to 1), or null when the work reports none (the moving segment).</summary>
    public double? Fraction
    {
        get => _model.Fraction;
        set
        {
            if (_model.Set(value))
            {
                Invalidate();
                Run();
            }
        }
    }

    /// <summary>The fill's colour (the accent).</summary>
    public Color Fill
    {
        get => _drawable.Fill;
        set
        {
            _drawable.Fill = value;
            Invalidate();
        }
    }

    /// <summary>The track's colour (a quiet line from the theme; transparent for the window's loader).</summary>
    public Color Track
    {
        get => _drawable.Track;
        set
        {
            _drawable.Track = value;
            Invalidate();
        }
    }

    /// <summary>Whether motion is reduced (<c>CRAFT_REDUCED_MOTION</c>): nothing moves and no timer runs.</summary>
    public bool ReducedMotion
    {
        get => _model.Reduced;
        set
        {
            _model.Reduced = value;
            Invalidate();
            Run();
        }
    }

    /// <summary>How long the moving segment takes to cross the bar.</summary>
    public double CycleMilliseconds
    {
        get => _model.CycleMilliseconds;
        set => _model.CycleMilliseconds = Math.Max(200, value);
    }

    /// <summary>Whether its frame timer runs now (the tests of the page read it).</summary>
    public bool IsAnimating => _timer?.IsRunning == true;

    // The frame timer starts only while the bar moves and is shown; it stops itself once the bar settles.
    private void Run()
    {
        if (!_model.Animating || !IsVisible || !_loaded || Dispatcher is not { } dispatcher)
        {
            Stop();
            return;
        }

        if (_timer is null)
        {
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += OnTick;
        }

        if (!_timer.IsRunning)
        {
            _last = Stopwatch.GetTimestamp();
            _timer.Start();
        }
    }

    private void Stop() => _timer?.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        bool moving = _model.Advance(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        _last = now;
        Invalidate();
        if (!moving || !IsVisible || !_loaded)
        {
            Stop();
        }
    }

    // Draws the model's geometry: the track, then the fill, both with rounded ends; nothing allocated.
    private sealed class BarDrawable(ProgressBarModel model) : IDrawable
    {
        public Color Fill { get; set; } = Colors.Orange;

        public Color Track { get; set; } = Colors.Gray;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var geometry = model.Geometry(dirtyRect.Width, dirtyRect.Height);
            if (Track.Alpha > 0)
            {
                canvas.FillColor = Track;
                canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height, geometry.Radius);
            }

            if (geometry.FillWidth > 0)
            {
                canvas.Alpha = geometry.Opacity;
                canvas.FillColor = Fill;
                canvas.FillRoundedRectangle(dirtyRect.X + geometry.FillX, dirtyRect.Y, geometry.FillWidth, dirtyRect.Height, Math.Min(geometry.Radius, geometry.FillWidth / 2));
                canvas.Alpha = 1;
            }
        }
    }
}
