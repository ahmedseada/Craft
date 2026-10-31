// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Tools;

/// <summary>
/// The editor tools' settings (plug-in "tools"; each a variable <c>CRAFT_PLUGIN_TOOLS_&lt;KEY&gt;</c> on the settings page):
/// how much one call reads, finds or lists, what it skips, where a folder's proposed edit goes, and which folder the tools
/// serve outside the app. Inside the app they come from the host's settings (environment, the workspace's and the user's
/// settings.json); in another process (the idrak tool) from the environment.
/// </summary>
public sealed record ToolSettings
{
    /// <summary>The plug-in's name, the prefix of its settings' variables.</summary>
    public const string PluginName = "tools";

    /// <summary>The most lines one read_document call returns (<c>readLines</c>).</summary>
    public int ReadLines { get; init; } = 2000;

    /// <summary>The most matches one search_workspace call returns (<c>searchResults</c>).</summary>
    public int SearchResults { get; init; } = 100;

    /// <summary>The most symbols one find_symbol call returns (<c>symbolResults</c>).</summary>
    public int SymbolResults { get; init; } = 50;

    /// <summary>Files larger than this many KiB are not searched (<c>searchFileKb</c>).</summary>
    public int SearchFileKilobytes { get; init; } = 2048;

    /// <summary>The folder names the tools skip, ignoring case (<c>ignore</c>, separated by ';' or ',').</summary>
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj", ".git", ".vs", "node_modules", "test-output"];

    /// <summary>Where the folder backing writes a proposed edit as a patch, relative to its folder (<c>patchFolder</c>).</summary>
    public string PatchFolder { get; init; } = Path.Combine(".craft", "patches");

    /// <summary>The folder the folder backing serves (<c>toolsFolder</c>); empty for the current folder.</summary>
    public string ToolsFolder { get; init; } = "";

    /// <summary>The settings the plug-in declares, with their defaults and meanings (the settings page lists them).</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("readLines", "2000", "The most lines one read_document call of the editor's tools returns."),
        new("searchResults", "100", "The most matches one search_workspace call returns."),
        new("symbolResults", "50", "The most symbols one find_symbol call returns."),
        new("searchFileKb", "2048", "Files larger than this many KiB are not searched."),
        new("ignore", "bin;obj;.git;.vs;node_modules;test-output", "Folder names the editor's tools skip (';' or ',' between them)."),
        new("patchFolder", Path.Combine(".craft", "patches"), "Where the folder backing (outside the app) writes a proposed edit as a patch, relative to its folder."),
        new("toolsFolder", "", "The folder the editor's tools serve outside the app (idrak tools test, idrak mcp serve); empty: the current folder."),
    ];

    /// <summary>The settings from the environment (a process outside the app).</summary>
    public static ToolSettings FromEnvironment() =>
        Read(key => Environment.GetEnvironmentVariable(PluginSetting.VariableName(PluginName, key)));

    /// <summary>The settings from <paramref name="settings"/> (the host's: environment, settings files, defaults).</summary>
    public static ToolSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Read(key => settings.Get(PluginName, key));
    }

    /// <summary>
    /// The settings from <paramref name="value"/> (a key's text, or null for its default). A value that does not parse
    /// throws, naming the variable and what it takes.
    /// </summary>
    public static ToolSettings Read(Func<string, string?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var defaults = new ToolSettings();
        string? Text(string key) => value(key) is { } text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
        int Whole(string key, int fallback, int min, int max)
        {
            if (Text(key) is not { } text)
            {
                return fallback;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max
                ? number
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a whole number from {min} to {max}.", key);
        }

        IReadOnlyList<string> List(string key, IReadOnlyList<string> fallback) =>
            value(key) is { } text ? text.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : fallback;

        return new ToolSettings
        {
            ReadLines = Whole("readLines", defaults.ReadLines, 1, 1_000_000),
            SearchResults = Whole("searchResults", defaults.SearchResults, 1, 1_000_000),
            SymbolResults = Whole("symbolResults", defaults.SymbolResults, 1, 100_000),
            SearchFileKilobytes = Whole("searchFileKb", defaults.SearchFileKilobytes, 1, 1_048_576),
            Ignore = List("ignore", defaults.Ignore),
            PatchFolder = Text("patchFolder") ?? defaults.PatchFolder,
            ToolsFolder = Text("toolsFolder") ?? "",
        };
    }
}
