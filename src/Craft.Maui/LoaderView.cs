// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;
using Craft.Editor;
using Craft.Editor.Activities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The window's global loader (B10): a thin progress line under the title bar, across the window, while a workspace's
/// work runs (<see cref="GlobalLoader"/>: a folder opening, a language building its workspace, any activity a plug-in
/// marks global), shown only after <c>CRAFT_LOADER_DELAY_MS</c>. Determinate when the work it follows reports a fraction
/// (the line grows), else a segment crosses the window every <c>CRAFT_LOADER_CYCLE_MS</c>; with reduced motion the
/// segment stands still at half strength and the line jumps. Its tooltip names the work and its percent; a click opens
/// the notification centre's activities (<see cref="Tapped"/>).
/// </summary>
public sealed class LoaderView : Grid
{
    private const string MotionName = "craft-loader";
    private const double LineHeight = 2;
    private readonly ActivityCenter _activities;
    private readonly BoxView _bar = new() { HeightRequest = LineHeight, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
    private IDispatcherTimer? _due;
    private CraftOptions _options;
    private LoaderState _shown = LoaderState.Hidden;
    private bool _moving;

    /// <summary>The loader over <paramref name="activities"/>, with <paramref name="options"/>' delay, kinds and motion.</summary>
    public LoaderView(ActivityCenter activities, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(options);
        _activities = activities;
        _options = options;
        HeightRequest = 6;                                // a 2-unit line in a 6-unit band the pointer can find
        VerticalOptions = LayoutOptions.Start;
        IsVisible = false;
        BackgroundColor = Colors.Transparent;
        Children.Add(_bar);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
        SizeChanged += (_, _) => Place(animate: false);
        activities.Changed += (_, _) => Refresh();
        SemanticProperties.SetDescription(this, "Workspace loading");
    }

    /// <summary>Raised on a click: the page opens the notification centre.</summary>
    public event EventHandler? Tapped;

    /// <summary>What it shows now (the tests of the page read it).</summary>
    public LoaderState State => _shown;

    /// <summary>Takes the options (the delay, the kinds, the cycle, reduced motion) and the accent.</summary>
    public void Apply(CraftOptions options, Color accent)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accent);
        _options = options;
        _bar.Color = accent;
        StopMoving();
        Refresh();
    }

    /// <summary>Reads the running activities again: shows, follows or hides the work; looks again when a waiting one passes the delay.</summary>
    public void Refresh()
    {
        var running = _activities.Running;
        var state = GlobalLoader.Of(running, _options);
        ScheduleDue(GlobalLoader.DueIn(running, _options));
        if (state == _shown)
        {
            return;
        }

        bool fractionKind = state.Fraction is null != _shown.Fraction is null;
        _shown = state;
        ToolTipProperties.SetText(this, state.Tooltip);
        IsVisible = state.Visible;
        if (!state.Visible)
        {
            StopMoving();
            return;
        }

        Place(animate: !fractionKind);
    }

    private void Place(bool animate)
    {
        if (!_shown.Visible || Width <= 0)
        {
            return;
        }

        bool reduced = _options.ReducedMotion || Craft.Editor.Motion.Duration(_options) == 0;
        if (_shown.Fraction is { } fraction)
        {
            StopMoving();
            _bar.Opacity = 1;
            _bar.TranslationX = 0;
            double target = Math.Max(LineHeight, Width * fraction);
            if (!animate || reduced)
            {
                _bar.WidthRequest = target;
                return;
            }

            double from = _bar.WidthRequest > 0 ? _bar.WidthRequest : 0;
            new Animation(w => _bar.WidthRequest = w, from, target, Easing.CubicOut).Commit(_bar, MotionName + "-grow", 16, (uint)Craft.Editor.Motion.Duration(_options));
            return;
        }

        // Nothing reported: a segment a third of the window wide crosses it, again and again (still, with reduced motion).
        double segment = Math.Max(24, Width / 3);
        _bar.WidthRequest = segment;
        if (reduced)
        {
            StopMoving();
            _bar.WidthRequest = Width;
            _bar.Opacity = 0.5;
            _bar.TranslationX = 0;
            return;
        }

        _bar.Opacity = 1;
        if (_moving)
        {
            return;
        }

        _moving = true;
        new Animation(x => _bar.TranslationX = x, -segment, Width, Easing.Linear)
            .Commit(_bar, MotionName, 16, (uint)Math.Max(200, _options.LoaderCycleMilliseconds), repeat: () => _moving && _shown is { Visible: true, Fraction: null });
    }

    private void StopMoving()
    {
        _moving = false;
        _bar.AbortAnimation(MotionName);
        _bar.AbortAnimation(MotionName + "-grow");
    }

    private void ScheduleDue(TimeSpan? due)
    {
        if (due is not { } wait || Dispatcher is not { } dispatcher)
        {
            _due?.Stop();
            return;
        }

        if (_due is null)
        {
            _due = dispatcher.CreateTimer();
            _due.IsRepeating = false;
            _due.Tick += (_, _) => UiDispatch.Run("loader: the delay passed", Refresh);
        }

        _due.Stop();
        _due.Interval = wait + TimeSpan.FromMilliseconds(1);
        _due.Start();
    }
}
