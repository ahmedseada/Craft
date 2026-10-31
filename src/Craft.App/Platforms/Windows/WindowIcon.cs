// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.Maui.LifecycleEvents;

namespace Craft.App.WinUI;

/// <summary>
/// The window's icon on Windows. Craft runs unpackaged (a plain .exe), and the SVG MauiIcon only reaches MSIX packages and
/// Android: the taskbar and the title bar take an unpackaged app's icon from its .exe (ApplicationIcon, appicon.ico) and
/// from its window. This sets the same appicon.ico, copied beside the program, on every window WinUI creates.
/// </summary>
internal static class WindowIcon
{
    public static void Configure(MauiAppBuilder builder) =>
        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(path))
            {
                window.AppWindow.SetIcon(path);
            }
        })));
}
