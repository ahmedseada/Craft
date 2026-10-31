// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;

namespace Craft.Editor;

/// <summary>
/// The interface's motion (phase X: "motion that explains"): how long a transition lasts and its curve, from the options;
/// <c>CRAFT_REDUCED_MOTION</c> (or a length of 0) turns every transition into a jump. Headless, so the views and the tests
/// read the same numbers.
/// </summary>
public static class Motion
{
    /// <summary>The transitions' length in milliseconds (panels, the notification centre, toasts, menus, the completion list); 0 when motion is reduced.</summary>
    public static int Duration(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ReducedMotion ? 0 : Math.Max(0, options.MotionMilliseconds);
    }

    /// <summary>The editor's smooth scroll's length in milliseconds; 0 (scroll at once) when motion is reduced.</summary>
    public static int ScrollDuration(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ReducedMotion ? 0 : Math.Max(0, options.ScrollMilliseconds);
    }

    /// <summary>The ease-out curve (cubic): fast at first, settling at the end; <paramref name="t"/> from 0 to 1.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double u = 1 - t;
        return 1 - (u * u * u);
    }

    /// <summary>How far a transition of <paramref name="milliseconds"/> is after <paramref name="elapsed"/>, eased: 1 when it is over or there is none.</summary>
    public static double Progress(TimeSpan elapsed, int milliseconds) =>
        milliseconds <= 0 ? 1 : Ease(elapsed.TotalMilliseconds / milliseconds);
}

/// <summary>
/// The editor's smooth scrolling (phase X): offsets in pixels that move toward a target over the scroll's length on the
/// ease-out curve, a wheel turn adding to the target while it moves. A few numbers made once per editor view and stepped in place, so nothing is allocated per frame, and the drawing lays out only the
/// lines the offset shows.
/// </summary>
public sealed class SmoothScroll
{
    private double _fromX;
    private double _fromY;
    private long _start;

    /// <summary>The offsets drawn now.</summary>
    public double X { get; private set; }

    /// <summary>The offsets drawn now.</summary>
    public double Y { get; private set; }

    /// <summary>Where the scroll is going.</summary>
    public double TargetX { get; private set; }

    /// <summary>Where the scroll is going.</summary>
    public double TargetY { get; private set; }

    /// <summary>Whether it has not reached its target yet.</summary>
    public bool IsMoving => X != TargetX || Y != TargetY;

    /// <summary>The pixels a wheel turn of <paramref name="delta"/> (120 a notch, positive toward the start) scrolls: <paramref name="lines"/> lines of <paramref name="lineHeight"/> a notch, toward the end for a negative delta.</summary>
    public static double WheelPixels(double delta, int lines, double lineHeight) => -delta / 120.0 * Math.Max(1, lines) * lineHeight;

    /// <summary>Puts it at (<paramref name="x"/>, <paramref name="y"/>) at once (the scroll bar was dragged, a jump with no motion).</summary>
    public void Jump(double x, double y)
    {
        X = TargetX = _fromX = x;
        Y = TargetY = _fromY = y;
    }

    /// <summary>Sets the target (clamped to 0 and the maxima), starting the move at <paramref name="now"/> (a <see cref="TimeProvider"/> timestamp) from where it is drawn.</summary>
    public void ScrollTo(double x, double y, double maxX, double maxY, long now)
    {
        _fromX = X;
        _fromY = Y;
        _start = now;
        TargetX = Math.Clamp(x, 0, Math.Max(0, maxX));
        TargetY = Math.Clamp(y, 0, Math.Max(0, maxY));
    }

    /// <summary>Moves the target by (<paramref name="dx"/>, <paramref name="dy"/>) from where it is going (a wheel turn while it moves goes on from there).</summary>
    public void ScrollBy(double dx, double dy, double maxX, double maxY, long now) => ScrollTo(TargetX + dx, TargetY + dy, maxX, maxY, now);

    /// <summary>Steps it to where it is at <paramref name="now"/> on a move of <paramref name="milliseconds"/> (0: at the target); true while it still moves.</summary>
    public bool Step(long now, int milliseconds, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        double t = Motion.Progress(time.GetElapsedTime(_start, now), milliseconds);
        if (t >= 1)
        {
            X = TargetX;
            Y = TargetY;
            return false;
        }

        X = _fromX + ((TargetX - _fromX) * t);
        Y = _fromY + ((TargetY - _fromY) * t);
        return IsMoving;
    }
}
