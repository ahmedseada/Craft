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
/// in), and raised as <see cref="Failed"/> (the page logs it to Output and shows Craft's dialog); the app goes on. Any
/// thread; never throws. The failed thread (often the UI thread) only notes the failure in the trail: the report is written
/// and <see cref="Failed"/> raised on the thread pool, one failure at a time in the order reported (the owner, 2026-11-02:
/// the crash kept apart from the UI thread), and <see cref="WhenReported"/> waits for those reported so far.
/// </summary>
public static class CallbackGuard
{
    private static readonly HashSet<(string, Type)> Reported = [];
    private static readonly Lock Gate = new();
    private static Task _delivered = Task.CompletedTask;

    /// <summary>The settings, for the crash folder (the app sets them at start).</summary>
    public static CraftOptions? Options { get; set; }

    /// <summary>Raised on the thread pool, after the failure's report is written, one failure at a time (dispatch to show it).</summary>
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

            lock (Gate)
            {
                var failure = new Pending(where, error, Reported.Add((where, error.GetType())), DateTime.Now, Options);
                _delivered = _delivered.ContinueWith(static (_, state) => Deliver((Pending)state!), failure, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
#pragma warning disable CA1031 // The report of a failure never fails the callback it guards.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>A task that ends when every failure reported so far is written and raised.</summary>
    public static Task WhenReported()
    {
        lock (Gate)
        {
            return _delivered;
        }
    }

    // On the thread pool: the report written (the first of its place and type), then Failed raised.
    private static void Deliver(Pending failure)
    {
        try
        {
            string? path = null;
            if (failure.First)
            {
                var folder = CrashReports.Folder(failure.Options);
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"callback-{failure.At:yyyyMMdd-HHmmss-fff}.txt"));
                File.WriteAllText(path, CrashReports.Format(failure.Error, $"a platform callback, {failure.Where} (caught: the app went on)"));
            }

            Failed?.Invoke(null, new CallbackFailure(failure.Where, failure.Error, path));
        }
#pragma warning disable CA1031 // The report of a failure never fails anything: the next one is still delivered.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private sealed record Pending(string Where, Exception Error, bool First, DateTime At, CraftOptions? Options);

    /// <summary>Forgets which failures were reported (tests).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Reported.Clear();
        }
    }
}
