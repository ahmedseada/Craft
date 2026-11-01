// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Core.Diagnostics;

/// <summary>
/// The guard of the drawn surfaces' frames (the owner's third Windows run, B2): a frame is the platform's draw callback
/// (Win2D's CanvasControl raising MAUI's draw), and an exception left to leave it ends the process with a stowed exception
/// in WinUI (0xc000027b) that no managed handler sees. A frame that throws is caught here, reported with its exception
/// type and the step it was at (rule 90) to the run's trail, a callback report and Output (<see cref="CallbackGuard"/>),
/// and skipped; the next frame draws again. A lost graphics device is the one exception let through: Win2D's control
/// catches it itself and makes its device again (its documented recovery), after the surface dropped its layouts.
/// </summary>
public static class PaintGuard
{
    private static long s_failures;

    /// <summary>The frames that threw and were skipped since the app started (any surface).</summary>
    public static long Failures => Interlocked.Read(ref s_failures);

    /// <summary>
    /// Handles <paramref name="error"/>, thrown by the frame of <paramref name="surface"/> ("editor: paint") at
    /// <paramref name="step"/>: reported and swallowed (false), or, for a lost device, noted and to be rethrown (true).
    /// </summary>
    public static bool Handle(string surface, string step, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (IsDeviceLost(error))
        {
            CrashWatch.CurrentTrail?.Note($"{surface}: the graphics device was lost at {step} (0x{error.HResult:X8}); the canvas makes it again");
            return true;
        }

        Interlocked.Increment(ref s_failures);
        CallbackGuard.Report($"{surface} (at: {step}; the frame was skipped)", error);
        return false;
    }

    /// <summary>
    /// Whether <paramref name="error"/> (or an exception inside it) is a lost graphics device: DXGI's device removed, hung,
    /// reset or internal error, or Direct2D's "recreate the target". Numbers only: no platform API.
    /// </summary>
    public static bool IsDeviceLost(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e.HResult is unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or unchecked((int)0x887A0007)
                or unchecked((int)0x887A0020) or unchecked((int)0x8899000C))
            {
                return true;
            }
        }

        return false;
    }
}
