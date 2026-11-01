// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.Maui.Controls;

namespace Craft.Maui;

/// <summary>
/// The window's own full screen (X part 9, F11 and Zen mode), each platform's under its <c>Platforms/</c> folder (Windows:
/// the app window's full-screen presenter, <c>Craft.Maui.WinUI.WindowsFullScreen</c>); a platform without one gives null
/// (<see cref="IShellPlatform.FullScreen"/>) and the shell says so.
/// </summary>
public interface IWindowFullScreen
{
    /// <summary>Puts <paramref name="window"/> in full screen, or back to its ordinary frame; false when it could not (no platform window yet).</summary>
    bool Set(Window window, bool fullScreen);
}
