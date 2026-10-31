// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Maui;
using Craft.Maui.WinUI;

namespace Craft.App;

/// <summary>Windows: the WinUI file pickers, the window's key routing and its closing guard.</summary>
internal static partial class ShellPlatforms
{
    /// <inheritdoc cref="ShellPlatforms.Create"/>
    public static partial IShellPlatform Create() => new WindowsShellPlatform();
}
