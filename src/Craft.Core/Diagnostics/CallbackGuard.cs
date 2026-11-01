// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Core.Diagnostics;

/// <summary>A platform callback that threw (caught by <see cref="CallbackGuard"/>).</summary>
/// <param name="Where">The callback ("the keyboard: a key press").</param>
/// <param name="Error">What it threw.</param>
/// <param name="ReportPath">Its report's file, or null when none was written (the same failure was reported already).</param>
public sealed record CallbackFailure(string Where, Exception Error, string? ReportPath);

/// <summary>
/// The guard of the platform's callbacks (the owner's note 20): an exception that leaves a WinUI or WinRT event handler
/// (a key, a pointer, a dispatcher's callback) can end the process with a native fail-fast that no managed handler sees.
/// Every such handler catches what its body throws and hands it here: it is noted in the crash trail, written once per
/// place and exception type as a <c>callback-&lt;time&gt;.txt</c> report in the crash folder (so the problem report folds it
/// in), and raised as <see cref="Failed"/> (the page logs it to Output); the app goes on. Any thread; never throws.
/// </summary>
public static class CallbackGuard
{
    private static readonly HashSet<(string, Type)> Reported = [];
    private static readonly Lock Gate = new();

    /// <summary>The settings, for the crash folder (the app sets them at start).</summary>
    public static CraftOptions? Options { get; set; }

    /// <summary>Raised on the thread of the callback that failed.</summary>
    public static event EventHandler<CallbackFailure>? Failed;

    /// <summary>Runs <paramref name="action"/>; what it throws is reported (<see cref="Report"/>) and false returned.</summary>
    public static bool Run(string where, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
            return true;
        }
#pragma warning disable CA1031 // Nothing may leave a platform callback: it is reported and the app goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Report(where, error);
            return false;
        }
    }

    /// <summary>Reports <paramref name="error"/>, caught in the callback <paramref name="where"/>.</summary>
    public static void Report(string where, Exception error)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(where);
            ArgumentNullException.ThrowIfNull(error);
            if (CrashWatch.CurrentTrail is { } trail)
            {
                // B2: the inner exceptions too (the cause of a wrapped one).
                var text = new System.Text.StringBuilder(256).Append("A platform callback failed and was caught (").Append(where).Append("): ");
                trail.Note(CrashTrail.AppendWithInner(text, error).ToString());
            }

            bool first;
            lock (Gate)
            {
                first = Reported.Add((where, error.GetType()));
            }

            string? path = null;
            if (first)
            {
                var folder = CrashReports.Folder(Options);
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"callback-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt"));
                File.WriteAllText(path, CrashReports.Format(error, $"a platform callback, {where} (caught: the app went on)"));
            }

            Failed?.Invoke(null, new CallbackFailure(where, error, path));
        }
#pragma warning disable CA1031 // The report of a failure never fails the callback it guards.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Forgets which failures were reported (tests).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Reported.Clear();
        }
    }
}
