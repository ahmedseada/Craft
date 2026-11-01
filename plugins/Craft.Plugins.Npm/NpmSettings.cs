// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>
/// The npm plug-in's settings (plug-in "npm", variables <c>CRAFT_PLUGIN_NPM_&lt;KEY&gt;</c>, on the settings page's
/// Terminal section under "npm"). <c>node</c>, <c>npm</c> and <c>sandbox</c> are this machine's only
/// (<see cref="PluginSetting.MachineOnly"/>): a workspace's <c>.craft/settings.json</c> names no program to run and no
/// place for it to run in.
/// </summary>
public sealed record NpmSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "npm";

    /// <summary>The settings page's group of these settings.</summary>
    private const string Group = "npm";

    /// <summary>The Node program (<c>node</c>): a full path, or a name found on PATH; empty: "node" on PATH.</summary>
    public string Node { get; init; } = "";

    /// <summary>npm's own <c>npm-cli.js</c> (<c>npm</c>), or the folder of npm's package; empty: found beside Node.</summary>
    public string Npm { get; init; } = "";

    /// <summary>The command sandbox every run goes through (<c>sandbox</c>; a name in Idrak's CommandSandboxes).</summary>
    public string Sandbox { get; init; } = "approve-each";

    /// <summary>What "npm: install" runs (<c>install</c>): "install", or "ci" (the lock file as it is; install without one, said).</summary>
    public string Install { get; init; } = "install";

    /// <summary>
    /// The scripts that run until stopped (<c>longRunning</c>, names separated by ';'): no time limit for them. A name
    /// matches itself and its variants after a ':' ("dev" matches "dev:server").
    /// </summary>
    public IReadOnlyList<string> LongRunning { get; init; } = ["start", "serve", "watch", "dev"];

    /// <summary>The time limit of every other script and of an install, in minutes (<c>timeLimitMinutes</c>); 0: none.</summary>
    public int TimeLimitMinutes { get; init; } = 30;

    /// <summary>Whether a run's output keeps its colours (<c>color</c>: FORCE_COLOR=1 for it).</summary>
    public bool Color { get; init; } = true;

    /// <summary>The folders never walked for package.json (<c>ignore</c>, names separated by ';'); each folder's .gitignore applies too.</summary>
    public IReadOnlyList<string> Ignore { get; init; } = ["node_modules", ".git", "bin", "obj", "dist", "out", "build", ".angular", ".next", ".nuxt", "coverage", "test-output"];

    /// <summary>The pause after a package.json changed on disk before its scripts are read again, in ms (<c>scanDelayMs</c>).</summary>
    public int ScanDelayMilliseconds { get; init; } = 300;

    /// <summary>The settings this plug-in declares, with their shapes on the settings page.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        Setting("node", "", "The Node program: a full path or a name on PATH (\"node\" on PATH when empty); this machine's only, never a workspace's",
            SettingShape.PathTo(SettingSections.Terminal, "Node", Group), machineOnly: true),
        Setting("npm", "", "npm's npm-cli.js, or the folder of npm's package (found beside Node when empty); this machine's only",
            SettingShape.PathTo(SettingSections.Terminal, "npm-cli.js", Group), machineOnly: true),
        Setting("sandbox", "approve-each", "The command sandbox every npm run goes through (approve-each asks you first; process runs at once); this machine's only",
            SettingShape.Text(SettingSections.Terminal, "Runs go through the sandbox", Group), machineOnly: true),
        Setting("install", "install", "What \"npm: install\" runs: install, or ci (the lock file as it is; install where there is none, said)",
            SettingShape.Choice(SettingSections.Terminal, "Install with", ["install", "ci"], Group)),
        Setting("longRunning", "start;serve;watch;dev", "The scripts that run until stopped, with no time limit (names separated by ';'; \"dev\" also matches \"dev:server\")",
            SettingShape.Text(SettingSections.Terminal, "Run until stopped", Group)),
        Setting("timeLimitMinutes", "30", "The time limit of every other script and of an install (0: none)",
            SettingShape.Whole(SettingSections.Terminal, "Time limit", 0, 10_080, "min", Group)),
        Setting("color", "true", "A run's output keeps its colours (FORCE_COLOR=1)",
            SettingShape.Toggle(SettingSections.Terminal, "Colours in the output", Group)),
        Setting("ignore", "node_modules;.git;bin;obj;dist;out;build;.angular;.next;.nuxt;coverage;test-output", "The folders never walked for package.json (names separated by ';'; .gitignore applies too)",
            SettingShape.Text(SettingSections.Terminal, "Folders passed over", Group)),
        Setting("scanDelayMs", "300", "The pause after a package.json changed on disk before its scripts are read again",
            SettingShape.Whole(SettingSections.Terminal, "Read scripts again after", 0, 60_000, "ms", Group, 50)),
    ];

    /// <summary>The settings as <paramref name="settings"/> holds them now; a value that does not parse keeps its default.</summary>
    public static NpmSettings Read(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Get(string key) => settings.Get(PluginName, key).Trim();
        var defaults = new NpmSettings();
        string install = Get("install").ToLowerInvariant();
        return new NpmSettings
        {
            Node = Get("node"),
            Npm = Get("npm"),
            Sandbox = Get("sandbox") is { Length: > 0 } sandbox ? sandbox : defaults.Sandbox,
            Install = install is "install" or "ci" ? install : defaults.Install,
            LongRunning = Names(Get("longRunning")),
            TimeLimitMinutes = Whole(Get("timeLimitMinutes"), defaults.TimeLimitMinutes),
            Color = bool.TryParse(Get("color"), out bool color) ? color : defaults.Color,
            Ignore = Names(Get("ignore")),
            ScanDelayMilliseconds = Whole(Get("scanDelayMs"), defaults.ScanDelayMilliseconds),
        };
    }

    /// <summary>Whether the script <paramref name="name"/> runs until stopped (<see cref="LongRunning"/>: the name, or the name and a ':' variant).</summary>
    public bool IsLongRunning(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var each in LongRunning)
        {
            if (name.Equals(each, StringComparison.OrdinalIgnoreCase)
                || (name.Length > each.Length && name[each.Length] == ':' && name.StartsWith(each, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The time limit of the script <paramref name="name"/> (or of an install, null): none for a long-running one or a limit of 0.</summary>
    public TimeSpan TimeLimitOf(string? name) =>
        (name is not null && IsLongRunning(name)) || TimeLimitMinutes <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(TimeLimitMinutes);

    private static string[] Names(string text) => text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int Whole(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0 ? value : fallback;

    private static PluginSetting Setting(string key, string value, string description, SettingShape shape, bool machineOnly = false) =>
        new(key, value, description) { Shape = shape, MachineOnly = machineOnly };
}
