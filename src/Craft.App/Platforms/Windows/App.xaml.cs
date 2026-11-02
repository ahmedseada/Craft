// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.App.WinUI;

/// <summary>The Windows application (WinUI): builds the MAUI app (<see cref="MauiProgram.CreateMauiApp"/>).</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>The WinUI application, its XAML loaded.</summary>
    public App()
    {
        CrashDialog.Install(this);
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <inheritdoc />
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Again at launch, in case the platform set the UI thread's context after the constructor (once: a guarded one stays).
        Craft.Core.Diagnostics.GuardedSynchronizationContext.Install();
        base.OnLaunched(args);
    }
}
