// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Activities;

/// <summary>Where a progress bar draws its fill in a bar of a width and height: the fill's left and width, its rounded ends' radius and its strength.</summary>
/// <param name="FillX">The fill's left, from the bar's left.</param>
/// <param name="FillWidth">The fill's width (0: none).</param>
/// <param name="Radius">The rounded ends' radius (half the height).</param>
/// <param name="Opacity">The fill's strength (half for a still bar under reduced motion).</param>
public readonly record struct ProgressBarGeometry(float FillX, float FillWidth, float Radius, float Opacity);

/// <summary>
/// Craft's own progress bar, headless (the owner, 2026-11-01: "add a progress bar to the status bar … don't use the
/// default one"): a thin bar of the accent on a track with rounded ends. Determinate when the work reports a fraction (the
/// fill eases toward it), indeterminate otherwise (a soft segment crosses the bar once a <see cref="CycleMilliseconds"/>);
/// with reduced motion the fill jumps and the indeterminate bar stands still at half strength. It animates only while
/// <see cref="Animating"/>: the view runs its frame timer then and stops it otherwise, and a frame
/// (<see cref="Advance"/>, <see cref="Geometry"/>) allocates nothing.
/// </summary>
public sealed class ProgressBarModel
{
    /// <summary>The share of the bar the indeterminate segment covers.</summary>
    public const double SegmentShare = 0.3;

    /// <summary>How close the eased fill comes to the fraction before it stops moving.</summary>
    public const double Settled = 0.002;

    private double? _target;
    private double _shown;
    private double _phase;

    /// <summary>A bar whose indeterminate segment crosses it once every <paramref name="cycleMilliseconds"/>.</summary>
    public ProgressBarModel(double cycleMilliseconds = 1400) => CycleMilliseconds = Math.Max(200, cycleMilliseconds);

    /// <summary>How long the indeterminate segment takes to cross the bar.</summary>
    public double CycleMilliseconds { get; set; }

    /// <summary>The time the determinate fill takes to come most of the way to a new fraction.</summary>
    public double EaseMilliseconds { get; set; } = 160;

    /// <summary>Whether motion is reduced (<c>CRAFT_REDUCED_MOTION</c>): nothing moves, so no frame timer runs.</summary>
    public bool Reduced { get; set; }

    /// <summary>The fraction reported (0 to 1), or null when the work reports none (indeterminate).</summary>
    public double? Fraction => _target;

    /// <summary>The fill drawn now (it eases toward <see cref="Fraction"/>).</summary>
    public double Shown => _shown;

    /// <summary>Where the indeterminate segment is in its cycle, from 0 to 1.</summary>
    public double Phase => _phase;

    /// <summary>Whether a frame would draw something new: the indeterminate segment moves, or the fill has not reached its fraction (never with reduced motion).</summary>
    public bool Animating => !Reduced && (_target is not { } target || Math.Abs(target - _shown) > Settled);

    /// <summary>Takes <paramref name="fraction"/> (clamped to 0 to 1; a value below 0, NaN or null: not known); true when it changed what the bar shows.</summary>
    public bool Set(double? fraction)
    {
        double? next = fraction is { } f && f >= 0 && double.IsFinite(f) ? Math.Min(1, f) : null;
        if (next == _target)
        {
            return false;
        }

        if (_target is null && next is not null)
        {
            _shown = 0;                                          // the fill grows from the start once a fraction is known
        }

        if (next is { } known && (Reduced || known < _shown))
        {
            _shown = known;                                      // no motion, or work that went back: no easing down
        }

        _target = next;
        return true;
    }

    /// <summary>Moves the bar on by <paramref name="milliseconds"/>: the segment along its cycle, the fill toward its fraction; whether it still moves.</summary>
    public bool Advance(double milliseconds)
    {
        if (Reduced || milliseconds <= 0)
        {
            return Animating;
        }

        if (_target is not { } target)
        {
            _phase = (_phase + (milliseconds / CycleMilliseconds)) % 1;
            return true;
        }

        _shown += (target - _shown) * (1 - Math.Exp(-milliseconds / Math.Max(1, EaseMilliseconds)));
        if (Math.Abs(target - _shown) <= Settled)
        {
            _shown = target;
        }

        return Animating;
    }

    /// <summary>
    /// Where the fill goes in a bar <paramref name="width"/> by <paramref name="height"/>: determinate, from the left to the
    /// shown fraction (at least a dot once anything is done); indeterminate, a segment of <see cref="SegmentShare"/> that
    /// enters at the left and leaves at the right, cut to the bar; reduced motion, the whole bar at half strength.
    /// </summary>
    public ProgressBarGeometry Geometry(float width, float height)
    {
        float radius = Math.Max(0, height / 2);
        width = Math.Max(0, width);
        if (_target is not null)
        {
            float fill = (float)(_shown * width);
            return new ProgressBarGeometry(0, _shown > 0 ? Math.Max(fill, Math.Min(width, height)) : 0, radius, 1);
        }

        if (Reduced)
        {
            return new ProgressBarGeometry(0, width, radius, 0.5f);
        }

        double start = (-SegmentShare + (_phase * (1 + SegmentShare))) * width;
        double end = start + (SegmentShare * width);
        float left = (float)Math.Max(0, start);
        float right = (float)Math.Min(width, end);
        return new ProgressBarGeometry(left, Math.Max(0, right - left), radius, 1);
    }
}

/// <summary>Which running activity the status bar shows (the owner, 2026-11-01): the one the user picked while it runs, else the newest.</summary>
public static class ActivityStatus
{
    /// <summary>The index in <paramref name="running"/> (newest first) of the activity to show: <paramref name="pinned"/>'s while it runs, else 0; -1 when none runs.</summary>
    public static int Chosen(IReadOnlyList<ActivitySnapshot> running, long? pinned)
    {
        ArgumentNullException.ThrowIfNull(running);
        if (running.Count == 0)
        {
            return -1;
        }

        if (pinned is { } id)
        {
            for (int i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id)
                {
                    return i;
                }
            }
        }

        return 0;
    }
}
