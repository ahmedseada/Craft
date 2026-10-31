// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;
using Craft.Core.Plugins;

namespace Craft.App;

/// <summary>What the app starts with: the settings, the plug-ins it loaded (or could not), and a settings error to show.</summary>
/// <param name="Options">The settings (the defaults when the environment's did not parse).</param>
/// <param name="Plugins">Each installed plug-in's load result.</param>
/// <param name="OptionsError">Why the environment's settings were not taken, or null.</param>
public sealed record AppStartup(CraftOptions Options, IReadOnlyList<PluginLoadResult> Plugins, string? OptionsError);

/// <summary>Builds the MAUI app: the settings from the environment, the installed plug-ins (C# among them), the window.</summary>
public static class MauiProgram
{
    /// <summary>The MAUI app.</summary>
    public static MauiApp CreateMauiApp()
    {
        CraftOptions options;
        string? optionsError = null;
        try
        {
            options = CraftOptions.FromEnvironment();
        }
        catch (ArgumentException e)
        {
            options = new CraftOptions();
            optionsError = e.Message;
        }

#if WINDOWS
        WinUI.CrashDialog.Options = options;
#endif

        // The pre-installed plug-ins (plugins/<Name>/<Name>.dll beside the program; C# is one, removable): a plug-in that
        // fails is reported on the page, never a crash.
        var plugins = CraftPlugins.LoadInstalled(options);
        var startup = new AppStartup(options, plugins, optionsError);
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp(_ => new App(startup));
        return builder.Build();
    }
}
