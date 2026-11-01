// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>
/// The lint plug-in's settings (plug-in "lint", variables <c>CRAFT_PLUGIN_LINT_&lt;KEY&gt;</c>, on the settings page's
/// Editor section under "Lint"). <c>allowGlobal</c>, <c>sandbox</c>, <c>autoSandbox</c> and <c>allowed</c> are this
/// machine's only (<see cref="PluginSetting.MachineOnly"/>): a workspace names no program to run, no place to run it and
/// cannot allow itself.
/// </summary>
public sealed record LintSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "lint";

    private const string Group = "Lint";

    /// <summary>Which linter runs (<c>linter</c>): auto (ESLint where configured, else TSLint where only tslint.json is), eslint, tslint, off.</summary>
    public string Linter { get; init; } = "auto";

    /// <summary>When files are linted (<c>runOn</c>): save, or manual (only "Lint this file" and "Lint workspace").</summary>
    public string RunOn { get; init; } = "save";

    /// <summary>Saves within this many milliseconds are linted in one run (<c>delayMs</c>); a newer run cancels an older one.</summary>
    public int DelayMilliseconds { get; init; } = 500;

    /// <summary>Whether a globally installed ESLint or TSLint runs where the project has none (<c>allowGlobal</c>; this machine's only).</summary>
    public bool AllowGlobal { get; init; }

    /// <summary>The time limit of one lint run, in seconds (<c>timeLimitSeconds</c>); 0: none.</summary>
    public int TimeLimitSeconds { get; init; } = 120;

    /// <summary>The command sandbox a run the user asked for goes through (<c>sandbox</c>; approve-each asks first; this machine's only).</summary>
    public string Sandbox { get; init; } = "approve-each";

    /// <summary>The sandbox runs go through once the workspace allowed the program to run on its own (<c>autoSandbox</c>; this machine's only).</summary>
    public string AutoSandbox { get; init; } = "process";

    /// <summary>
    /// The workspaces where a program runs on its own (<c>allowed</c>), each "folder|program" ("D:\web|eslint"), separated
    /// by ';': kept in the user's settings in the data folder (never a workspace's); a row removed asks again.
    /// </summary>
    public IReadOnlyList<string> Allowed { get; init; } = [];

    /// <summary>The folders "Lint workspace" never walks for configurations (<c>ignore</c>, names separated by ';').</summary>
    public IReadOnlyList<string> Ignore { get; init; } = ["node_modules", ".git", "bin", "obj", "dist", "out", "build", ".angular", ".next", ".nuxt", "coverage", "test-output"];

    /// <summary>The settings this plug-in declares, with their shapes on the settings page.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        Setting("linter", "auto", "Which linter runs: auto (ESLint where the project configures it, else TSLint where it only has tslint.json), eslint, tslint, or off",
            SettingShape.Choice(SettingSections.Editor, "Linter", ["auto", "eslint", "tslint", "off"], Group)),
        Setting("runOn", "save", "When files are linted: on save, or only when asked (\"Lint: Lint this file\", \"Lint: Lint workspace\")",
            SettingShape.Choice(SettingSections.Editor, "Lint on", ["save", "manual"], Group)),
        Setting("delayMs", "500", "Saves within this pause are linted in one run; a newer run cancels an older one",
            SettingShape.Whole(SettingSections.Editor, "Join saves within", 0, 60_000, "ms", Group, 50)),
        Setting("allowGlobal", "false", "Run a globally installed ESLint or TSLint where the project has none; this machine's only",
            SettingShape.Toggle(SettingSections.Editor, "Use a global ESLint", Group), machineOnly: true),
        Setting("timeLimitSeconds", "120", "The time limit of one lint run (0: none)",
            SettingShape.Whole(SettingSections.Editor, "Time limit", 0, 3_600, "s", Group)),
        Setting("sandbox", "approve-each", "The command sandbox a lint you asked for runs through (approve-each asks you first); this machine's only",
            SettingShape.Text(SettingSections.Editor, "Runs go through the sandbox", Group), machineOnly: true),
        Setting("autoSandbox", "process", "The sandbox lint on save and Prettier run through once you allowed them in a workspace; this machine's only",
            SettingShape.Text(SettingSections.Editor, "Allowed runs go through", Group), machineOnly: true),
        Setting("allowed", "", "The workspaces where ESLint, TSLint or Prettier run on their own, each folder|program, separated by ';'; remove a row to be asked again; this machine's only (never a workspace's)",
            SettingShape.ListOf(SettingSections.Editor, "Allowed to run on their own", Group), machineOnly: true),
        Setting("ignore", "node_modules;.git;bin;obj;dist;out;build;.angular;.next;.nuxt;coverage;test-output", "The folders \"Lint workspace\" never walks for configurations (names separated by ';')",
            SettingShape.Text(SettingSections.Editor, "Folders passed over", Group)),
    ];

    /// <summary>The settings as <paramref name="settings"/> holds them now; a value that does not parse keeps its default.</summary>
    public static LintSettings Read(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Get(string key) => settings.Get(PluginName, key).Trim();
        var defaults = new LintSettings();
        string linter = Get("linter").ToLowerInvariant();
        string runOn = Get("runOn").ToLowerInvariant();
        return new LintSettings
        {
            Linter = linter is "auto" or "eslint" or "tslint" or "off" ? linter : defaults.Linter,
            RunOn = runOn is "save" or "manual" ? runOn : defaults.RunOn,
            DelayMilliseconds = Whole(Get("delayMs"), defaults.DelayMilliseconds),
            AllowGlobal = bool.TryParse(Get("allowGlobal"), out bool global) && global,
            TimeLimitSeconds = Whole(Get("timeLimitSeconds"), defaults.TimeLimitSeconds),
            Sandbox = Get("sandbox") is { Length: > 0 } sandbox ? sandbox : defaults.Sandbox,
            AutoSandbox = Get("autoSandbox") is { Length: > 0 } auto ? auto : defaults.AutoSandbox,
            Allowed = Get("allowed").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Ignore = Get("ignore").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };
    }

    /// <summary>The time limit of a run: none for 0.</summary>
    public TimeSpan TimeLimit => TimeLimitSeconds <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(TimeLimitSeconds);

    /// <summary>Whether <paramref name="linter"/> may run (the setting names it, or is auto).</summary>
    public bool Picks(string linter) => Linter == "auto" || Linter.Equals(linter, StringComparison.OrdinalIgnoreCase);

    internal static int Whole(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0 ? value : fallback;

    internal static PluginSetting Setting(string key, string value, string description, SettingShape shape, bool machineOnly = false) =>
        new(key, value, description) { Shape = shape, MachineOnly = machineOnly };
}

/// <summary>
/// The Prettier plug-in's settings (plug-in "prettier", variables <c>CRAFT_PLUGIN_PRETTIER_&lt;KEY&gt;</c>, on the settings
/// page's Editor section under "Prettier"). Its runs go through the lint plug-in's sandboxes and approvals.
/// </summary>
public sealed record PrettierSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "prettier";

    private const string Group = "Prettier";

    /// <summary>Whether the project's Prettier formats its files (<c>enabled</c>).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether Prettier formats only where the project has a Prettier configuration (<c>requireConfig</c>; else its install is enough).</summary>
    public bool RequireConfig { get; init; } = true;

    /// <summary>Whether a globally installed Prettier formats where the project has none (<c>allowGlobal</c>; this machine's only).</summary>
    public bool AllowGlobal { get; init; }

    /// <summary>The time limit of one format, in seconds (<c>timeLimitSeconds</c>); 0: none.</summary>
    public int TimeLimitSeconds { get; init; } = 30;

    /// <summary>The file extensions Prettier formats (<c>extensions</c>, separated by ';').</summary>
    public IReadOnlyList<string> Extensions { get; init; } = DefaultExtensions;

    /// <summary>JS, TS, JSX, TSX, CSS, SCSS, LESS, HTML, JSON, Markdown and YAML.</summary>
    public static IReadOnlyList<string> DefaultExtensions { get; } =
        [".js", ".mjs", ".cjs", ".jsx", ".ts", ".mts", ".cts", ".tsx", ".css", ".scss", ".less", ".html", ".htm", ".json", ".jsonc", ".json5", ".md", ".markdown", ".yaml", ".yml"];

    /// <summary>The settings this plug-in declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        LintSettings.Setting("enabled", "true", "Format with the project's own Prettier (node_modules/prettier) where it applies, instead of Craft's built-in formatter",
            SettingShape.Toggle(SettingSections.Editor, "Format with the project's Prettier", Group)),
        LintSettings.Setting("requireConfig", "true", "Only where the project has a Prettier configuration (.prettierrc*, prettier.config.*, or \"prettier\" in package.json)",
            SettingShape.Toggle(SettingSections.Editor, "Only with a Prettier configuration", Group)),
        LintSettings.Setting("allowGlobal", "false", "Format with a globally installed Prettier where the project has none; this machine's only",
            SettingShape.Toggle(SettingSections.Editor, "Use a global Prettier", Group), machineOnly: true),
        LintSettings.Setting("timeLimitSeconds", "30", "The time limit of one format (0: none)",
            SettingShape.Whole(SettingSections.Editor, "Time limit", 0, 600, "s", Group)),
        LintSettings.Setting("extensions", string.Join(';', DefaultExtensions), "The file extensions Prettier formats (separated by ';')",
            SettingShape.Text(SettingSections.Editor, "Files Prettier formats", Group)),
    ];

    /// <summary>The settings as <paramref name="settings"/> holds them now.</summary>
    public static PrettierSettings Read(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Get(string key) => settings.Get(PluginName, key).Trim();
        var defaults = new PrettierSettings();
        var extensions = Get("extensions").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
        return new PrettierSettings
        {
            Enabled = !bool.TryParse(Get("enabled"), out bool enabled) || enabled,
            RequireConfig = !bool.TryParse(Get("requireConfig"), out bool require) || require,
            AllowGlobal = bool.TryParse(Get("allowGlobal"), out bool global) && global,
            TimeLimitSeconds = LintSettings.Whole(Get("timeLimitSeconds"), defaults.TimeLimitSeconds),
            Extensions = extensions.Length > 0 ? extensions : defaults.Extensions,
        };
    }

    /// <summary>Whether Prettier formats files with the extension of <paramref name="path"/>.</summary>
    public bool Formats(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());
        foreach (var each in Extensions)
        {
            if (extension.Equals(each, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
