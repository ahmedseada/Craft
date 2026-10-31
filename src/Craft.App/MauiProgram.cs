// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Maui;

namespace Craft.App;

/// <summary>What the app starts with: the settings and the options built from them, the plug-ins it loaded (or could not), the recent list and the platform's pieces.</summary>
/// <param name="Options">The options (the environment over the user's settings.json over the defaults; a value that did not parse keeps its default).</param>
/// <param name="Settings">The layered settings (a folder's .craft/settings.json joins them when it opens).</param>
/// <param name="Plugins">Each installed plug-in's load result.</param>
/// <param name="Recent">The recent files and folders.</param>
/// <param name="Platform">The platform's file dialogs, key routing and close guard.</param>
/// <param name="Problems">What did not parse in the settings (each naming its variable and where it came from).</param>
public sealed record AppStartup(CraftOptions Options, CraftSettings Settings, IReadOnlyList<PluginLoadResult> Plugins, RecentList Recent, IShellPlatform Platform, IReadOnlyList<string> Problems);

/// <summary>Builds the MAUI app: the settings (environment and the user's file), the installed plug-ins (C# among them), the recent list, the window.</summary>
public static class MauiProgram
{
    /// <summary>The MAUI app.</summary>
    public static MauiApp CreateMauiApp()
    {
        // The data folder comes from the environment only (the user's settings file lives in it).
        var dataOptions = new CraftOptions { DataFolder = Environment.GetEnvironmentVariable("CRAFT_DATA_FOLDER") is { Length: > 0 } folder ? folder.Trim() : null };
        var user = SettingsFile.Load(CraftSettings.UserFilePath(dataOptions));
        var settings = new CraftSettings(Environment.GetEnvironmentVariable, user);
        var options = settings.BuildOptions(out var problems);

#if WINDOWS
        WinUI.CrashDialog.Options = options;
#endif

        // The pre-installed plug-ins (plugins/<Name>/<Name>.dll beside the program; C# is one, removable): a plug-in that
        // fails is reported on the page, never a crash.
        var plugins = CraftPlugins.LoadInstalled(options);
        var recent = RecentList.Load(options);
        var startup = new AppStartup(options, settings, plugins, recent, ShellPlatforms.Create(), [.. problems, .. recent.Problems]);
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp(_ => new App(startup));
        return builder.Build();
    }
}
