// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Plugins.Sandbox;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(SandboxPackage))]

namespace Craft.Plugins.Sandbox;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Sandbox\.</summary>
public sealed class SandboxPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Sandbox";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new SandboxRegistration()];
}

/// <summary>Registers "approve-each" in Idrak's <see cref="CommandSandboxes"/>, the editor plug-in "sandbox" and its commands, each with how to take it back.</summary>
public sealed class SandboxRegistration() : IdrakPlugin(SandboxSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => CommandSandboxes.Register(ApproveEachSandbox.Instance), () => CommandSandboxes.Unregister(ApproveEachSandbox.SandboxName));
        Part(() => EditorPlugins.Register(SandboxPlugin.Instance), () => EditorPlugins.Unregister(SandboxSettings.PluginName));
        foreach (var command in SandboxPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }
    }
}

/// <summary>The plug-in's settings (plug-in "sandbox", variables <c>CRAFT_PLUGIN_SANDBOX_&lt;KEY&gt;</c>).</summary>
public sealed record SandboxSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "sandbox";

    /// <summary>The sandbox an allowed command runs in (<c>inner</c>): "process" (Idrak.Text's) unless set; "container" turns the network off.</summary>
    public string Inner { get; init; } = CommandSandboxes.Process;

    /// <summary>Minutes between two commands after which "for this run" ends and the next command asks again (<c>runGapMinutes</c>).</summary>
    public int RunGapMinutes { get; init; } = 15;

    /// <summary>Lines the terminal panel keeps (<c>panelLines</c>).</summary>
    public int PanelLines { get; init; } = 400;

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("inner", CommandSandboxes.Process, "The sandbox a command runs in once you allow it: process (a plain process), container (the network off; Idrak.Reinforcement.Plugins)."),
        new("runGapMinutes", "15", "Minutes between two commands after which 'Allow PROGRAM for this run' ends and the next command asks again."),
        new("panelLines", "400", "Lines the sandbox terminal panel keeps."),
    ];

    /// <summary>The settings from the host's.</summary>
    /// <exception cref="ArgumentException">A value that does not parse, naming its variable.</exception>
    public static SandboxSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new SandboxSettings();
        int Whole(string key, int fallback, int min, int max)
        {
            string text = settings.Get(PluginName, key).Trim();
            if (text.Length == 0)
            {
                return fallback;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max
                ? number
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a whole number from {min} to {max}.", key);
        }

        string inner = settings.Get(PluginName, "inner").Trim();
        return new SandboxSettings
        {
            Inner = inner.Length == 0 ? defaults.Inner : inner,
            RunGapMinutes = Whole("runGapMinutes", defaults.RunGapMinutes, 1, 1_000_000),
            PanelLines = Whole("panelLines", defaults.PanelLines, 10, 1_000_000),
        };
    }
}

/// <summary>
/// The approve-each sandbox as an editor plug-in ("sandbox"): while active, <see cref="ApproveEachSandbox"/> asks this
/// host's user and shows each command's output in the panel "Terminal (sandbox)"; its commands end the run (the programs
/// allowed for it are forgotten) and clear the panel.
/// </summary>
public sealed class SandboxPlugin : IEditorPlugin
{
    /// <summary>Forgets the programs allowed for this run.</summary>
    public const string EndRunCommand = "sandbox.endRun";

    /// <summary>Empties the terminal panel.</summary>
    public const string ClearCommand = "sandbox.clear";

    private SandboxPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static SandboxPlugin Instance { get; } = new();

    /// <summary>Its commands.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(EndRunCommand, "Sandbox: end this run (ask again for every command)", (_, _) =>
        {
            ApproveEachSandbox.Session?.EndRun();
            return Task.CompletedTask;
        }, _ => ApproveEachSandbox.Session is not null, "Forgets the programs allowed for this run"),
        new EditorCommand(ClearCommand, "Sandbox: clear the terminal", (_, _) =>
        {
            ApproveEachSandbox.Session?.Clear();
            return Task.CompletedTask;
        }, _ => ApproveEachSandbox.Session is not null, "Empties the sandbox terminal panel"),
    ];

    /// <inheritdoc />
    public string Name => SandboxSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "The approve-each command sandbox: each command an agent wants to run is shown to you first, its output live in the sandbox terminal";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => SandboxSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new SandboxSession(host);
        ApproveEachSandbox.Session = session;
        return new Deactivation(session);
    }

    private sealed class Deactivation(SandboxSession session) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(ApproveEachSandbox.Session, session))
            {
                ApproveEachSandbox.Session = null;
            }

            session.Dispose();
        }
    }
}
