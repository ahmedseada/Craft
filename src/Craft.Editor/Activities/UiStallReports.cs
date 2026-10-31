// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Craft.Core;
using Craft.Core.Diagnostics;

namespace Craft.Editor.Activities;

/// <summary>
/// The UI watchdog's stall reports (phase U): a stall of the window written as text to a file of its own in the crash
/// folder (<c>CRAFT_CRASH_FOLDER</c>), at most <see cref="CraftOptions.UiStallReports"/> a session, so the owner pastes
/// what blocked the window: when, how long, the UI work running then and before, the collections it held, the thread
/// pool and the memory. Writing never throws.
/// </summary>
public sealed class UiStallReports(CraftOptions? options)
{
    private int _written;

    /// <summary>The reports written so far.</summary>
    public int Written => Volatile.Read(ref _written);

    /// <summary>The report's text.</summary>
    public static string Format(UiStall stall)
    {
        ArgumentNullException.ThrowIfNull(stall);
        var text = new StringBuilder(1024);
        text.Append(CultureInfo.InvariantCulture, $"Craft UI stall report, {stall.At:yyyy-MM-dd HH:mm:ss.fff zzz}\n");
        text.Append(CultureInfo.InvariantCulture, $"The window was blocked {stall.Duration.TotalMilliseconds:F0} ms by: {stall.Label}\n");
        text.Append(CultureInfo.InvariantCulture, $"App: {typeof(UiStallReports).Assembly.GetName().Version}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})\n");
        text.Append(CultureInfo.InvariantCulture, $"Full collections during the stall: {stall.FullCollections}; the runtime's pauses during it: {stall.CollectionPause.TotalMilliseconds:F1} ms\n");
        ThreadPool.GetAvailableThreads(out int workers, out int ports);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        text.Append(CultureInfo.InvariantCulture, $"Thread pool: {ThreadPool.ThreadCount} threads, {ThreadPool.PendingWorkItemCount} items waiting, {maxWorkers - workers} workers busy, {ports} I/O ports free\n");
        using (var process = Process.GetCurrentProcess())
        {
            text.Append(CultureInfo.InvariantCulture, $"Memory: working set {process.WorkingSet64 / (1024 * 1024)} MiB, managed heap {GC.GetTotalMemory(false) / (1024 * 1024)} MiB\n");
        }

        text.Append("\nThe UI work before it, the newest first:\n");
        foreach (var label in stall.RecentWork)
        {
            text.Append("  ").Append(label).Append('\n');
        }

        text.Append("\nA stack of the UI thread at the stall: take one with `dotnet-stack report -p <pid>` while it happens.\n");
        return text.ToString();
    }

    /// <summary>Writes <paramref name="stall"/>'s report to a new file in the crash folder; its path, or null when the session's limit is reached or it could not be written.</summary>
    public string? Write(UiStall stall)
    {
        ArgumentNullException.ThrowIfNull(stall);
        int limit = options?.UiStallReports ?? new CraftOptions().UiStallReports;
        if (Interlocked.Increment(ref _written) > limit)
        {
            Interlocked.Decrement(ref _written);
            return null;
        }

        try
        {
            var folder = CrashReports.Folder(options);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"stall-{stall.At.LocalDateTime:yyyyMMdd-HHmmss-fff}.txt"));
            File.WriteAllText(path, Format(stall));
            return path;
        }
#pragma warning disable CA1031 // A report that cannot be written must not hide the stall it reports.
        catch (Exception)
#pragma warning restore CA1031
        {
            Interlocked.Decrement(ref _written);
            return null;
        }
    }
}
