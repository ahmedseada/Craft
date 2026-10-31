// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;
using Craft.Core;
using Craft.Core.Diagnostics;

namespace Craft.App.WinUI;

/// <summary>
/// Windows' crash dialog: an exception nobody caught (on the UI thread, on any thread, or in a task nobody awaited) is
/// written as a report (<see cref="CrashReports"/>) and shown in a message box, with the report's path, before the app
/// closes. The message box is Win32's own, so it shows even when the window's XAML is what failed.
/// </summary>
internal static partial class CrashDialog
{
    private const uint MbOk = 0x0, MbIconError = 0x10, MbTopMost = 0x40000, MbSetForeground = 0x10000;
    private const int MaxShown = 3000;
    private static int _shown;

    /// <summary>The settings, for the crash folder; set once they are read.</summary>
    public static CraftOptions? Options { get; set; }

    /// <summary>Hooks the process's and the task scheduler's unhandled exceptions; <paramref name="app"/>'s are hooked too.</summary>
    public static void Install(Microsoft.UI.Xaml.Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UnhandledException += (_, e) =>
        {
            // WinUI's exception may be a bare COM error; its Message carries the original text and stack when it has one.
            var extra = e.Message is { Length: > 0 } message && message != e.Exception?.Message ? "WinUI message: " + message : null;
            Show(e.Exception ?? new InvalidOperationException(e.Message), "the window (WinUI, UI thread)", extra);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Show(e.ExceptionObject as Exception ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject, System.Globalization.CultureInfo.InvariantCulture)), "the process (a thread)", null);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // A task nobody awaited failed: reported, and the app goes on (it would not close for it).
            var path = CrashReports.Write(Options, e.Exception, "a task nobody awaited (the app went on)");
            CrashWatch.CurrentTrail?.Note($"A task nobody awaited failed: {e.Exception.GetBaseException().GetType().Name}: {e.Exception.GetBaseException().Message} (report {path})");
            e.SetObserved();
        };
    }

    private static void Show(Exception error, string source, string? extra)
    {
        if (Interlocked.Exchange(ref _shown, 1) != 0)
        {
            return;                                                  // one dialog per crash, not one per handler
        }

        var path = CrashReports.Write(Options, error, source, extra);
        var text = CrashReports.Format(error, source, extra);

        // The owner's note 21: the crash goes into the trail, flushed before the dialog (the process ends after it). A bounded
        // wait on the crash path only: the trail's writer runs on the thread pool, never on this thread.
        if (CrashWatch.CurrentTrail is { } trail)
        {
            trail.Note($"Craft crashed ({source}): {error.GetType().FullName}: {error.Message} (report {path})");
            _ = trail.FlushAsync().Wait(TimeSpan.FromMilliseconds(500));
        }
        if (text.Length > MaxShown)
        {
            text = string.Concat(text.AsSpan(0, MaxShown), "\n…");
        }

        var head = path is not null ? $"Craft has to close. The full report is in:\n{path}\n\n" : "Craft has to close (the report could not be written).\n\n";
        _ = MessageBoxW(IntPtr.Zero, head + text, "Craft crashed", MbOk | MbIconError | MbTopMost | MbSetForeground);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
