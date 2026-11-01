// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Activities;

/// <summary>What the window's global loader shows now (B10).</summary>
/// <param name="Visible">Whether it shows.</param>
/// <param name="Fraction">How far the work it follows is, from 0 to 1, or null (a moving segment: the work reports none).</param>
/// <param name="Title">The work it follows ("C#: building the workspace of Craft").</param>
/// <param name="Count">How many workspace activities run past the delay (it follows one of them).</param>
public sealed record LoaderState(bool Visible, double? Fraction, string Title, int Count)
{
    /// <summary>Nothing to show.</summary>
    public static LoaderState Hidden { get; } = new(false, null, "", 0);

    /// <summary>The tooltip: the work and its percent, and how many more run ("C#: building … · 40% · and 1 more").</summary>
    public string Tooltip
    {
        get
        {
            if (!Visible)
            {
                return "";
            }

            var text = Fraction is { } f ? string.Create(CultureInfo.InvariantCulture, $"{Title} · {Math.Round(f * 100):0}%") : Title;
            return Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{text} · and {Count - 1} more") : text;
        }
    }
}

/// <summary>
/// The window's global loader (B10), headless: from the running activities, those of the loader's kinds
/// (<c>CRAFT_LOADER_KINDS</c>, <see cref="ActivityKinds.Workspace"/> by default) or marked <see cref="IActivity.Global"/>
/// that have run at least <c>CRAFT_LOADER_DELAY_MS</c> (so quick work never flashes). One loader whatever runs: it follows
/// the most advanced of them (the highest fraction reported; with none reported, the one running longest) and counts
/// them; determinate when the one it follows reports a fraction. <see cref="DueIn"/> says when a waiting one passes the
/// delay, so the view looks again then.
/// </summary>
public static class GlobalLoader
{
    /// <summary>Whether <paramref name="activity"/> is the loader's: marked global, or of one of <paramref name="options"/>' kinds.</summary>
    public static bool Counts(ActivitySnapshot activity, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(options);
        if (activity.Global)
        {
            return true;
        }

        foreach (var kind in options.LoaderKinds)
        {
            if (string.Equals(kind, activity.Kind, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What the loader shows for <paramref name="running"/> (the activities running now).</summary>
    public static LoaderState Of(IReadOnlyList<ActivitySnapshot> running, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(options);
        var delay = TimeSpan.FromMilliseconds(options.LoaderDelayMilliseconds);
        ActivitySnapshot? best = null;
        int count = 0;
        foreach (var activity in running)
        {
            if (activity.State != ActivityState.Running || activity.Elapsed < delay || !Counts(activity, options))
            {
                continue;
            }

            count++;
            if (best is null || Ahead(activity, best))
            {
                best = activity;
            }
        }

        return best is null ? LoaderState.Hidden : new LoaderState(true, best.Fraction, best.Title, count);
    }

    /// <summary>How long until a loader activity still within the delay passes it (the view looks again then), or null when none waits.</summary>
    public static TimeSpan? DueIn(IReadOnlyList<ActivitySnapshot> running, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(options);
        var delay = TimeSpan.FromMilliseconds(options.LoaderDelayMilliseconds);
        TimeSpan? soonest = null;
        foreach (var activity in running)
        {
            if (activity.State == ActivityState.Running && activity.Elapsed < delay && Counts(activity, options))
            {
                var left = delay - activity.Elapsed;
                if (soonest is null || left < soonest)
                {
                    soonest = left;
                }
            }
        }

        return soonest;
    }

    // The most advanced: a reported fraction beats none, a higher one a lower; among none reported, the longest running.
    private static bool Ahead(ActivitySnapshot candidate, ActivitySnapshot best) => (candidate.Fraction, best.Fraction) switch
    {
        ({ } a, { } b) => a > b,
        ({ }, null) => true,
        (null, { }) => false,
        _ => candidate.Elapsed > best.Elapsed,
    };
}
