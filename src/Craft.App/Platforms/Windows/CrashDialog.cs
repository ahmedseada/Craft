// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;
using Craft.Core;
using Craft.Core.Diagnostics;

namespace Craft.App.WinUI;

/// <summary>
/// Windows' exceptions nobody caught (the owner, 2026-11-01: "at any circumstances don't kill the app, keep the UI
/// thread"). On the UI thread (WinUI's <c>UnhandledException</c>) and in a task nobody awaited, the exception is handled:
/// written as a report, noted in the trail, and handed to <see cref="CallbackGuard"/>, whose page shows it in Craft's own
/// dialog with Copy report, and in Output; the app goes on. Only an exception that leaves a raw thread of its own ends
/// the process (.NET gives no way to go on): its report is written and shown in Win32's message box (Ctrl+C copies it),
/// which shows even when the window's XAML is what failed. The UI thread's synchronization context is wrapped in
/// <see cref="GuardedSynchronizationContext"/>, so an awaited continuation or an async handler that throws is reported too;
/// queued UI work is guarded where it is queued (the host's Dispatch, the views' UiDispatch).
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
        // The UI thread's context guarded (the owner, 2026-11-02): an awaited continuation's or an async handler's exception
        // is reported there and the thread goes on, instead of leaving WinUI's dispatcher, which ends the process for it.
        GuardedSynchronizationContext.Install();
        app.UnhandledException += (_, e) =>
        {
            // Handled: the window and its UI thread go on. WinUI's exception may be a bare COM error; its Message carries the
            // original text and stack when it has one.
            e.Handled = true;
            var error = e.Exception ?? new InvalidOperationException(e.Message);
            var where = e.Message is { Length: > 0 } message && message != e.Exception?.Message
                ? "the window (WinUI, UI thread; WinUI message: " + message + ")"
                : "the window (WinUI, UI thread)";
            CallbackGuard.Report(where, error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Show(e.ExceptionObject as Exception ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject, System.Globalization.CultureInfo.InvariantCulture)), "the process (a thread)", null);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // A task nobody awaited failed: reported, and the app goes on (it would not close for it).
            e.SetObserved();
            CallbackGuard.Report("a background task nobody awaited", e.Exception.GetBaseException());
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

        var head = (path is not null ? $"A thread of its own failed and .NET ends the process for it. The full report is in:\n{path}\n" : "A thread of its own failed and .NET ends the process for it (the report could not be written).\n")
            + "Ctrl+C copies this message.\n\n";
        _ = MessageBoxW(IntPtr.Zero, head + text, "Craft crashed", MbOk | MbIconError | MbTopMost | MbSetForeground);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
