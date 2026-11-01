// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>
/// The git plug-in's settings (plug-in "git", variables <c>CRAFT_PLUGIN_GIT_&lt;KEY&gt;</c>, on the settings page's Source
/// Control section). <c>path</c> is the machine's only: a workspace's <c>.craft/settings.json</c> cannot name a program to run.
/// </summary>
public sealed record GitSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "git";

    /// <summary>The git program (<c>path</c>); empty: found on PATH.</summary>
    public string Path { get; init; } = "";

    /// <summary>The pause after a change on disk or a save before the status is read again, in ms (<c>refreshMs</c>).</summary>
    public int RefreshMilliseconds { get; init; } = 500;

    /// <summary>Minutes between fetches in the background (<c>autofetchMinutes</c>); 0: never.</summary>
    public int AutofetchMinutes { get; init; }

    /// <summary>Whether open files show their lines' changes against the index in the gutter (<c>gutter</c>).</summary>
    public bool Gutter { get; init; } = true;

    /// <summary>The pause after typing before an open file's gutter marks are worked out again, in ms (<c>gutterDelayMs</c>).</summary>
    public int GutterDelayMilliseconds { get; init; } = 300;

    /// <summary>Whether Sync asks before it pulls and pushes (<c>confirmSync</c>).</summary>
    public bool ConfirmSync { get; init; } = true;

    /// <summary>The most a discard keeps in memory for its Undo, in MiB (<c>discardUndoMb</c>); a larger discard asks first.</summary>
    public int DiscardUndoMegabytes { get; init; } = 16;

    /// <summary>
    /// Which model writes a commit message (<c>commitModel</c>): "loaded" (the first loaded of the chat's, the agent's,
    /// the completion's), "chat", "agent", "completion", or a model's name; only a model already loaded writes.
    /// </summary>
    public string CommitModel { get; init; } = "loaded";

    /// <summary>The instructions a commit message is written with (<c>commitPrompt</c>); empty: the plug-in's own.</summary>
    public string CommitPrompt { get; init; } = "";

    /// <summary>The message's style (<c>commitStyle</c>): auto (the repository's recent subjects), conventional, plain.</summary>
    public string CommitStyle { get; init; } = "auto";

    /// <summary>The most tokens a message takes (<c>commitMaxTokens</c>); 0: the model's role's own reply limit.</summary>
    public int CommitMaxTokens { get; init; }

    /// <summary>The most tokens of changes a message is written from (<c>commitDiffTokens</c>); the largest files are summarised beyond it.</summary>
    public int CommitDiffTokens { get; init; } = 3000;

    /// <summary>The recent subjects shown to the model as the repository's style (<c>commitExamples</c>).</summary>
    public int CommitExamples { get; init; } = 10;

    /// <summary>The settings this plug-in declares, with their shapes on the settings page.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        Setting("path", "", "The git program (found on PATH when empty); this machine's only, never a workspace's",
            SettingShape.PathTo(SettingSections.SourceControl, "Git program", "Git")),
        Setting("refreshMs", "500", "The pause after a change on disk or a save before the status is read again",
            SettingShape.Whole(SettingSections.SourceControl, "Refresh after", 50, 60_000, "ms", "Git", 50)),
        Setting("autofetchMinutes", "0", "Minutes between fetches in the background (0: never)",
            SettingShape.Whole(SettingSections.SourceControl, "Fetch every", 0, 1440, "min", "Git")),
        Setting("confirmSync", "true", "Sync asks before it pulls and pushes",
            SettingShape.Toggle(SettingSections.SourceControl, "Ask before Sync", "Git")),
        Setting("discardUndoMb", "16", "The most a discard keeps in memory for its Undo (a larger one asks first)",
            SettingShape.Whole(SettingSections.SourceControl, "Undo of a discard keeps up to", 0, 4096, "MiB", "Git")),
        Setting("gutter", "true", "Open files show their lines' changes against the index in the gutter and on the overview ruler",
            SettingShape.Toggle(SettingSections.SourceControl, "Changes in the gutter", "Editor")),
        Setting("gutterDelayMs", "300", "The pause after typing before the gutter's changes are worked out again",
            SettingShape.Whole(SettingSections.SourceControl, "Gutter after", 0, 10_000, "ms", "Editor", 50)),
        Setting("commitModel", "loaded", "The model that writes a commit message: loaded (the first loaded of chat, agent, completion), chat, agent, completion, or a model's name",
            SettingShape.From(SettingSections.SourceControl, "Model", "commit-models", ["loaded", "chat", "agent", "completion"], other: true, group: "Generate commit message")),
        Setting("commitStyle", "auto", "The message's style: auto (the repository's recent subjects), conventional, plain",
            SettingShape.Choice(SettingSections.SourceControl, "Style", ["auto", "conventional", "plain"], "Generate commit message")),
        Setting("commitPrompt", "", "The instructions a message is written with (empty: the plug-in's own)",
            SettingShape.Text(SettingSections.SourceControl, "Instructions", "Generate commit message")),
        Setting("commitMaxTokens", "0", "The most tokens a message takes (0: the model's own reply limit)",
            SettingShape.Whole(SettingSections.SourceControl, "Message tokens", 0, 4096, "tokens", "Generate commit message")),
        Setting("commitDiffTokens", "3000", "The most tokens of changes a message is written from (the largest files summarised beyond it)",
            SettingShape.Whole(SettingSections.SourceControl, "Changes budget", 200, 200_000, "tokens", "Generate commit message", 100)),
        Setting("commitExamples", "10", "The recent subjects shown to the model as the repository's style",
            SettingShape.Whole(SettingSections.SourceControl, "Style examples", 0, 100, "subjects", "Generate commit message")),
    ];

    /// <summary>The settings as <paramref name="settings"/> holds them now; a value that does not parse keeps its default.</summary>
    public static GitSettings Read(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Get(string key) => settings.Get(PluginName, key);
        var defaults = new GitSettings();
        return new GitSettings
        {
            Path = Get("path").Trim(),
            RefreshMilliseconds = Whole(Get("refreshMs"), defaults.RefreshMilliseconds, 0),
            AutofetchMinutes = Whole(Get("autofetchMinutes"), defaults.AutofetchMinutes, 0),
            Gutter = Toggle(Get("gutter"), defaults.Gutter),
            GutterDelayMilliseconds = Whole(Get("gutterDelayMs"), defaults.GutterDelayMilliseconds, 0),
            ConfirmSync = Toggle(Get("confirmSync"), defaults.ConfirmSync),
            DiscardUndoMegabytes = Whole(Get("discardUndoMb"), defaults.DiscardUndoMegabytes, 0),
            CommitModel = Get("commitModel").Trim() is { Length: > 0 } model ? model : defaults.CommitModel,
            CommitPrompt = Get("commitPrompt").Trim(),
            CommitStyle = Style(Get("commitStyle"), defaults.CommitStyle),
            CommitMaxTokens = Whole(Get("commitMaxTokens"), defaults.CommitMaxTokens, 0),
            CommitDiffTokens = Whole(Get("commitDiffTokens"), defaults.CommitDiffTokens, 1),
            CommitExamples = Whole(Get("commitExamples"), defaults.CommitExamples, 0),
        };
    }

    private static string Style(string text, string fallback)
    {
        string style = text.Trim().ToLowerInvariant();
        return style is "conventional" or "plain" or "auto" ? style : fallback;
    }

    private static PluginSetting Setting(string key, string value, string description, SettingShape shape) => new(key, value, description) { Shape = shape };

    private static int Whole(string text, int fallback, int least) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= least ? value : fallback;

    private static bool Toggle(string text, bool fallback) =>
        bool.TryParse(text.Trim(), out bool value) ? value : text.Trim() switch { "1" or "on" or "yes" => true, "0" or "off" or "no" => false, _ => fallback };
}
