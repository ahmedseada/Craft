// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Maui;

namespace Craft.App;

/// <summary>Builds the MAUI app: the settings (environment and the user's file), the installed plug-ins (C# among them), the recent list, the bundled fonts, the window.</summary>
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
        var startup = new ShellStartup(options, settings, plugins, recent, ShellPlatforms.Create(), [.. problems, .. recent.Problems]);
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp(_ => new App(startup));

        // Inter (the interface) and JetBrains Mono (the code), both SIL OFL 1.1, from Resources/Fonts (the MauiFont items).
        builder.ConfigureFonts(fonts =>
        {
            foreach (var (file, alias) in CraftFonts.Files)
            {
                fonts.AddFont(file, alias);
            }
        });
        return builder.Build();
    }
}
