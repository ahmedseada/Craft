// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Diagnostics;
using Craft.Editor.Activities;
using Microsoft.Maui.Dispatching;

namespace Craft.Maui;

/// <summary>
/// Labelled dispatches for the views (the owner's start-up stalls were "unlabelled UI work"): every view's
/// <c>Dispatcher.Dispatch</c> goes through <see cref="DispatchLabelled"/>, so the UI watchdog names the work that blocked
/// the window. The page sets <see cref="Labels"/> to its host's; until then the work runs unlabelled, as before.
/// </summary>
internal static class UiDispatch
{
    /// <summary>The window's UI work labels (the host's), or null before the page made its host.</summary>
    public static UiWorkLabels? Labels { get; set; }

    /// <summary>Posts <paramref name="action"/> to <paramref name="dispatcher"/>, run under <paramref name="label"/>.</summary>
    public static void DispatchLabelled(this IDispatcher dispatcher, string label, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.Dispatch(() => Run(label, action));
    }

    /// <summary>Posts <paramref name="action"/> after <paramref name="delay"/>, run under <paramref name="label"/>.</summary>
    public static void DispatchDelayedLabelled(this IDispatcher dispatcher, TimeSpan delay, string label, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.DispatchDelayed(delay, () => Run(label, action));
    }

    /// <summary>
    /// Runs <paramref name="action"/> now under <paramref name="label"/> (a window event's handler, a dispatched callback).
    /// What it throws is reported (<see cref="CallbackGuard"/>: a report written off the UI thread, Craft's dialog, Output)
    /// and the UI thread goes on: an exception left to the dispatcher's callback ends the process with WinUI's native
    /// fail-fast, which no handler sees (the owner's run 2026-11-02: the Models page's refresh threw, the app closed).
    /// </summary>
    public static void Run(string label, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            if (Labels is { } labels)
            {
                labels.Run(label, action);
            }
            else
            {
                action();
            }
        }
#pragma warning disable CA1031 // Nothing may leave a UI callback: reported, and the UI thread goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the UI dispatcher: " + label, error);
        }
    }

    /// <summary>Enters <paramref name="label"/> until the scope ends (a window event's handler with awaits between its parts).</summary>
    public static UiWorkLabels.Scope Enter(string label) => Labels is { } labels ? labels.Enter(label) : default;
}
