// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.UI.Windowing;
using MauiWindow = Microsoft.Maui.Controls.Window;
using NativeWindow = Microsoft.UI.Xaml.Window;

namespace Craft.Maui.WinUI;

/// <summary>The window's full screen on Windows (X part 9): the app window's full-screen presenter, and the overlapped one back.</summary>
public sealed class WindowsFullScreen : IWindowFullScreen
{
    /// <inheritdoc />
    public bool Set(MauiWindow window, bool fullScreen)
    {
        if (window?.Handler?.PlatformView is not NativeWindow native)
        {
            return false;
        }

        native.AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        return true;
    }
}
