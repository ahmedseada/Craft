// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Npm;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(NpmPluginPackage))]

namespace Craft.Plugins.Npm;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Npm\.</summary>
public sealed class NpmPluginPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Npm";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new NpmRegistration()];
}

/// <summary>
/// Registers, each with how to take it back: the editor plug-in "npm" and the file icons of .npmrc and .nvmrc
/// (<see cref="FileIcons"/>, by extension: package.json, package-lock.json and the node_modules folder take theirs once
/// file icons go by name, phase W's contract 5). Everything else (the Run view's provider "npm", the commands, the menus)
/// the plug-in adds in a host once it has found Node there, and takes back when it is deactivated.
/// </summary>
public sealed class NpmRegistration() : IdrakPlugin(NpmSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(NpmPlugin.Instance), () => EditorPlugins.Unregister(NpmSettings.PluginName));
        foreach (var icon in NpmPlugin.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>
/// The npm plug-in ("npm", phase W): while active, one <see cref="NpmSession"/> per host finds Node and offers the
/// workspace's npm scripts as commands, menu rows and Run view targets, each run through the sandbox its setting names.
/// </summary>
public sealed class NpmPlugin : IEditorPlugin
{
    /// <summary>Runs the script of a package.json menu's row (its argument).</summary>
    public const string RunScriptCommand = "npm.runScript";

    /// <summary>Runs the script of a package.json menu's row in a terminal tab that takes the keyboard.</summary>
    public const string RunScriptInTerminalCommand = "npm.runScriptInTerminal";

    /// <summary>"npm install" for the menu's package.json (else the active one, the root's, the only one).</summary>
    public const string InstallHereCommand = "npm.install";

    /// <summary>Reads the workspace's package.json files again.</summary>
    public const string RefreshCommand = "npm.refresh";

    /// <summary>Cancels every npm run.</summary>
    public const string StopCommand = "npm.stop";

    /// <summary>The submenu of a package.json's scripts (rows made as it opens).</summary>
    public const string ScriptsMenu = "context:npm-scripts";

    /// <summary>The submenu of a package.json's scripts, each run in a terminal tab.</summary>
    public const string TerminalMenu = "context:npm-terminal";

    /// <summary>The Run view as the side view: the status item's click.</summary>
    public const string RunViewCommand = "craft.sideView.craft.run";

    /// <summary>An install's icon (an arrow down into a tray).</summary>
    public const string InstallIcon = "M12 4v11M7 10l5 5l5-5M5 20h14";

    private NpmPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static NpmPlugin Instance { get; } = new();

    /// <summary>The Run view's provider "npm" (registered while a session has found Node).</summary>
    public static NpmRunProvider Provider { get; } = new();

    /// <summary>The file icons it registers (by extension, today's <see cref="FileIcons"/>).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        new(".npmrc", "npm", 0xFFE06C75, "npm configuration"),
        new(".nvmrc", "⬢", 0xFF8CC84B, "Node version (nvm)"),
    ];

    /// <summary>The active session (the host the plug-in was activated in last), or null.</summary>
    internal static NpmSession? Session { get; private set; }

    /// <inheritdoc />
    public string Name => NpmSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "npm: your package.json scripts in the palette, the explorer's menu and the Run view, each run asked first and shown in its own tab; npm install; Node's version";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => NpmSettings.Declared;

    /// <summary>The palette command of the script <paramref name="script"/> of the package.json <paramref name="file"/>.</summary>
    public static string ScriptCommandId(string file, string script) => $"npm.run:{file}#{script}";

    /// <summary>Its command that runs it in a terminal tab.</summary>
    public static string TerminalCommandId(string file, string script) => $"npm.terminal:{file}#{script}";

    /// <summary>The palette command that installs the package.json <paramref name="file"/>'s packages.</summary>
    public static string InstallCommandId(string file) => $"npm.install:{file}";

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new NpmSession(host);
        Session = session;
        return new Deactivation(session);
    }

    private sealed class Deactivation(NpmSession session) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(Session, session))
            {
                Session = null;
            }

            session.Dispose();
        }
    }
}
