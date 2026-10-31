// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Maui;

namespace Craft.App;

/// <summary>The running platform's shell pieces; each platform implements <see cref="Create"/> under its <c>Platforms/</c> folder.</summary>
internal static partial class ShellPlatforms
{
    /// <summary>The platform's file dialogs, key routing and close guard.</summary>
    public static partial IShellPlatform Create();
}
