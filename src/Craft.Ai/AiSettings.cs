// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// The AI plug-in's settings (plug-in "ai"; each a variable <c>CRAFT_PLUGIN_AI_&lt;KEY&gt;</c> on the settings page): the
/// tools' limits, the MCP server's address and token, the local model and its limits. Inside the app they come from the
/// host's settings (environment, the workspace's and the user's settings.json); in another process (the folder backing
/// under the idrak tool) from the environment.
/// </summary>
public sealed record AiSettings
{
    /// <summary>The plug-in's name, the prefix of its settings' variables.</summary>
    public const string PluginName = "ai";

    /// <summary>The most lines one read_document call returns (<c>readLines</c>).</summary>
    public int ReadLines { get; init; } = 2000;

    /// <summary>The most matches one search_workspace call returns (<c>searchResults</c>).</summary>
    public int SearchResults { get; init; } = 100;

    /// <summary>Files larger than this many KiB are not searched (<c>searchFileKb</c>).</summary>
    public int SearchFileKilobytes { get; init; } = 2048;

    /// <summary>The folder names the tools skip, ignoring case (<c>ignore</c>, separated by ';' or ',').</summary>
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj", ".git", ".vs", "node_modules", "test-output"];

    /// <summary>Where the folder backing writes a proposed edit as a patch, relative to its folder (<c>patchFolder</c>).</summary>
    public string PatchFolder { get; init; } = Path.Combine(".craft", "patches");

    /// <summary>The folder the folder backing serves (<c>toolsFolder</c>); empty for the current folder.</summary>
    public string ToolsFolder { get; init; } = "";

    /// <summary>Where the MCP server listens, host:port (<c>mcpAddress</c>); port 0 lets the system pick one.</summary>
    public string McpAddress { get; init; } = "127.0.0.1:0";

    /// <summary>The bearer token MCP clients send (<c>mcpToken</c>); empty for a new random one each start, "none" for no token.</summary>
    public string McpToken { get; init; } = "";

    /// <summary>Whether the MCP server starts with the editor (<c>mcpOnStart</c>); off: the command "ai.mcpStart" starts it.</summary>
    public bool McpOnStart { get; init; }

    /// <summary>The local model's name, resolved by Idrak's model sources (<c>model</c>); empty for none.</summary>
    public string Model { get; init; } = "";

    /// <summary>The device the local model loads on (<c>device</c>): "auto" (the best found), "cpu", "vulkan:0" and the like.</summary>
    public string Device { get; init; } = "auto";

    /// <summary>The local model's context window in tokens (<c>context</c>); 0 for the model's own (its MaxPositions).</summary>
    public int Context { get; init; }

    /// <summary>The local model's projections once loaded (<c>weights</c>): "int8", "int4", "bfloat16", or empty for float32.</summary>
    public string Weights { get; init; } = "";

    /// <summary>Minutes an unused local model stays loaded (<c>keepAliveMinutes</c>); 0 unloads it when the last lease ends.</summary>
    public int KeepAliveMinutes { get; init; } = 10;

    /// <summary>The CPU threads the library may use (<c>threads</c>); 0 for every core (Idrak's ComputeResources.MaxCpuThreads).</summary>
    public int Threads { get; init; }

    /// <summary>The CPU memory the library may hold, MiB (<c>cpuMemoryMb</c>); 0 for no limit (ComputeResources.CpuMemoryLimit).</summary>
    public long CpuMemoryMegabytes { get; init; }

    /// <summary>The GPU memory the library may hold per device, MiB (<c>gpuMemoryMb</c>); 0 for no limit (ComputeResources.GpuMemoryLimit).</summary>
    public long GpuMemoryMegabytes { get; init; }

    /// <summary>The Idrak plug-in packages registered before the first local load (<c>idrakPlugins</c>; the model families); empty for none.</summary>
    public IReadOnlyList<string> IdrakPlugins { get; init; } = ["Idrak.Text.Plugins"];

    /// <summary>The settings the plug-in declares, with their defaults and meanings (the settings page lists them).</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("readLines", "2000", "The most lines one read_document call of the editor's tools returns."),
        new("searchResults", "100", "The most matches one search_workspace call returns."),
        new("searchFileKb", "2048", "Files larger than this many KiB are not searched."),
        new("ignore", "bin;obj;.git;.vs;node_modules;test-output", "Folder names the editor's tools skip (';' or ',' between them)."),
        new("patchFolder", Path.Combine(".craft", "patches"), "Where the folder backing (outside the app) writes a proposed edit as a patch, relative to its folder."),
        new("toolsFolder", "", "The folder the editor's tools serve outside the app (idrak tools test, idrak mcp serve); empty: the current folder."),
        new("mcpAddress", "127.0.0.1:0", "Where the MCP server of the live tools listens (host:port; port 0 picks one); loopback only."),
        new("mcpToken", "", "The bearer token MCP clients send; empty: a new random one each start; 'none': no token."),
        new("mcpOnStart", "false", "Whether the MCP server starts with the editor (else the command 'AI: Serve the editor's tools over MCP')."),
        new("model", "", "The local model: a folder, a .gguf file, store:NAME or a Hugging Face id (Idrak's model sources)."),
        new("device", "auto", "The device the local model loads on: auto, cpu, vulkan:0, cuda:0."),
        new("context", "0", "The local model's context window in tokens; 0: the model's own."),
        new("weights", "", "The local model's projections once loaded: int8, int4, bfloat16; empty: float32."),
        new("keepAliveMinutes", "10", "Minutes an unused local model stays loaded; 0 unloads it when no plug-in uses it."),
        new("threads", "0", "CPU threads the library may use (Idrak's ComputeResources); 0: every core."),
        new("cpuMemoryMb", "0", "CPU memory the library may hold, MiB; 0: no limit."),
        new("gpuMemoryMb", "0", "GPU memory the library may hold per device, MiB; 0: no limit."),
        new("idrakPlugins", "Idrak.Text.Plugins", "Idrak plug-in packages (the model families) registered before the first local load; empty: none."),
    ];

    /// <summary>The settings from the environment (a process outside the app).</summary>
    public static AiSettings FromEnvironment() =>
        Read(key => Environment.GetEnvironmentVariable(PluginSetting.VariableName(PluginName, key)));

    /// <summary>The settings from <paramref name="settings"/> (the host's: environment, settings files, defaults).</summary>
    public static AiSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Read(key => settings.Get(PluginName, key));
    }

    /// <summary>
    /// The settings from <paramref name="value"/> (a key's text, or null for its default). A value that does not parse
    /// throws, naming the variable and what it takes.
    /// </summary>
    public static AiSettings Read(Func<string, string?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var defaults = new AiSettings();
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

        bool Flag(string key, bool fallback) => Text(key) switch
        {
            null => fallback,
            var t when t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1" => true,
            var t when t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0" => false,
            var t => throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{t}': it takes true or false.", key),
        };

        IReadOnlyList<string> List(string key, IReadOnlyList<string> fallback) =>
            value(key) is { } text ? text.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : fallback;

        return new AiSettings
        {
            ReadLines = Whole("readLines", defaults.ReadLines, 1, 1_000_000),
            SearchResults = Whole("searchResults", defaults.SearchResults, 1, 1_000_000),
            SearchFileKilobytes = Whole("searchFileKb", defaults.SearchFileKilobytes, 1, 1_048_576),
            Ignore = List("ignore", defaults.Ignore),
            PatchFolder = Text("patchFolder") ?? defaults.PatchFolder,
            ToolsFolder = Text("toolsFolder") ?? "",
            McpAddress = Text("mcpAddress") ?? defaults.McpAddress,
            McpToken = Text("mcpToken") ?? "",
            McpOnStart = Flag("mcpOnStart", false),
            Model = Text("model") ?? "",
            Device = Text("device") ?? defaults.Device,
            Context = Whole("context", 0, 0, 16_777_216),
            Weights = Text("weights") ?? "",
            KeepAliveMinutes = Whole("keepAliveMinutes", defaults.KeepAliveMinutes, 0, 1_000_000),
            Threads = Whole("threads", 0, 0, 1_000_000),
            CpuMemoryMegabytes = Whole("cpuMemoryMb", 0, 0, int.MaxValue),
            GpuMemoryMegabytes = Whole("gpuMemoryMb", 0, 0, int.MaxValue),
            IdrakPlugins = List("idrakPlugins", defaults.IdrakPlugins),
        };
    }
}
