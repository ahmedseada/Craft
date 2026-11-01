// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Activities;

/// <summary>How the status bar and the notification centre word an activity (headless, so the tests read the same words the window shows).</summary>
public static class ActivityText
{
    /// <summary>The failed operation's button that tries it again.</summary>
    public const string Retry = "Retry";

    /// <summary>A failure's button that shows the Output tab.</summary>
    public const string OpenLog = "Open log";

    /// <summary>A running operation's button that cancels it.</summary>
    public const string Cancel = "Cancel";

    /// <summary>A duration as the bar shows it: "0.4 s", "12 s", "3:05", "1:02:09".</summary>
    public static string Duration(TimeSpan time) =>
        time.TotalSeconds < 10 ? string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.0} s")
        : time.TotalMinutes < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalSeconds} s")
        : time.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:D2}")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}");

    /// <summary>A running activity's progress: "42% · 12.3 MB/s · 1:20 left · resolving the files", or its elapsed time when nothing is known.</summary>
    public static string Progress(ActivitySnapshot activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var text = new StringBuilder(64);
        if (activity.Fraction is { } fraction)
        {
            text.Append(CultureInfo.InvariantCulture, $"{fraction * 100:0}%");
        }

        if (activity.Rate is { } rate)
        {
            Separate(text).Append(rate.ToString());
        }

        if (activity.Left is { } left)
        {
            Separate(text).Append(Duration(left)).Append(" left");
        }
        else
        {
            Separate(text).Append(Duration(activity.Elapsed));
        }

        if (activity.Text is { Length: > 0 } now)
        {
            Separate(text).Append(now);
        }

        return text.ToString();
    }

    /// <summary>An ended activity's outcome: "done in 4.2 s · loaded on cpu", "failed after 2.0 s: the file is missing", "cancelled after 12 s".</summary>
    public static string Outcome(ActivitySnapshot activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        string took = Duration(activity.Elapsed);
        return activity.State switch
        {
            ActivityState.Failed => $"failed after {took}: {activity.Error ?? activity.Text ?? "no reason given"}",
            ActivityState.Cancelled => $"cancelled after {took}",
            ActivityState.Running => Progress(activity),
            _ => activity.Text is { Length: > 0 } outcome ? $"done in {took} · {outcome}" : $"done in {took}",
        };
    }

    /// <summary>
    /// The status bar's line for the running activities (the newest first): the newest one's title and progress, and
    /// "+N" for the others; empty when none runs.
    /// </summary>
    public static string StatusLine(IReadOnlyList<ActivitySnapshot> running) => StatusLine(running, 0);

    /// <summary>
    /// The status bar's line for the running activities (the newest first) showing the one at <paramref name="chosen"/> (the
    /// one the user picked, <see cref="ActivityStatus.Chosen"/>): its title and progress (its percent and time left when it
    /// reports a fraction), and "+N" for the others; empty when none runs.
    /// </summary>
    public static string StatusLine(IReadOnlyList<ActivitySnapshot> running, int chosen)
    {
        ArgumentNullException.ThrowIfNull(running);
        if (running.Count == 0)
        {
            return "";
        }

        var newest = running[Math.Clamp(chosen, 0, running.Count - 1)];
        string line = newest.Title + " · " + Progress(newest);
        return running.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{line}  +{running.Count - 1}") : line;
    }

    /// <summary>The status bar's line after a stall: "The window was blocked 3.2 s by chat: show the reply".</summary>
    public static string Stall(TimeSpan duration, string label) => $"The window was blocked {Duration(duration)} by {label}";

    private static StringBuilder Separate(StringBuilder text) => text.Length > 0 ? text.Append(" · ") : text;
}
