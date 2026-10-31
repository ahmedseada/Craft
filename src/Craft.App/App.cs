// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.App;

/// <summary>The MAUI application: one window with the shell, on the folder or file named on the command line if any.</summary>
public sealed class App : Application
{
    private readonly AppStartup _startup;

    /// <summary>The application with what it started with.</summary>
    public App(AppStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var args = Environment.GetCommandLineArgs();
        string? path = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;
        var page = new EditorPage(_startup, path);
        var window = new Window(page) { Title = "Craft" };
        page.AttachWindow(window);
        return window;
    }
}
