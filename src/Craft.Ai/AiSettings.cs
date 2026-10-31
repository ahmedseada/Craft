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

    /// <summary>The model provider the AI plug-ins ask (<c>modelProvider</c>): a name in Craft's ModelProviders ("local").</summary>
    public string ModelProvider { get; init; } = ModelProviders.Local;

    /// <summary>Whether inline completion shows ghost text (<c>completion</c>).</summary>
    public bool Completion { get; init; } = true;

    /// <summary>The model inline completion uses (<c>completionModel</c>); empty for <see cref="Model"/>.</summary>
    public string CompletionModel { get; init; } = "";

    /// <summary>The pause after typing before a completion is asked for, ms (<c>completionDelayMs</c>).</summary>
    public int CompletionDelayMilliseconds { get; init; } = 300;

    /// <summary>The most tokens one completion samples (<c>completionMaxTokens</c>).</summary>
    public int CompletionMaxTokens { get; init; } = 64;

    /// <summary>The most lines one completion shows; the request stops there (<c>completionMaxLines</c>).</summary>
    public int CompletionMaxLines { get; init; } = 8;

    /// <summary>
    /// How much text after the caret a fill-in-the-middle prompt carries, in characters (<c>completionSuffixChars</c>; 0:
    /// none). A prefix-first format processes the suffix again on every request (A0: about 25% of the prompt reused), so
    /// this sets a request's cost on such a model; a suffix-first one reuses up to the caret either way.
    /// </summary>
    public int CompletionSuffixCharacters { get; init; } = 512;

    /// <summary>
    /// How much text before the caret a prompt carries, in characters (<c>completionPrefixChars</c>); 0: as much as the
    /// model's context leaves after the answer and the suffix (its MaxPositions, never a fixed number).
    /// </summary>
    public int CompletionPrefixCharacters { get; init; }

    /// <summary>Tokens sampled between the generator's host synchronizations while a completion streams (<c>completionChunk</c>); 1 shows each token.</summary>
    public int CompletionChunk { get; init; } = 1;

    /// <summary>The sampling temperature of completion (<c>completionTemperature</c>); 0 takes the likeliest token.</summary>
    public float CompletionTemperature { get; init; }

    /// <summary>The most tokens one chat reply samples (<c>chatMaxTokens</c>).</summary>
    public int ChatMaxTokens { get; init; } = 1024;

    /// <summary>How many times a chat reply may call the editor's tools before it must answer (<c>chatToolRounds</c>).</summary>
    public int ChatToolRounds { get; init; } = 6;

    /// <summary>Whether the chat is kept per workspace (<c>chatKeep</c>), in <see cref="ChatFile"/>.</summary>
    public bool ChatKeep { get; init; } = true;

    /// <summary>Where the chat is kept, relative to the workspace (<c>chatFile</c>).</summary>
    public string ChatFile { get; init; } = Path.Combine(".craft", "chat.json");

    /// <summary>The most messages kept and sent back to the model (<c>chatHistory</c>); older ones are dropped first.</summary>
    public int ChatHistory { get; init; } = 40;

    /// <summary>The Idrak text matcher that places a model's snippet in a document (<c>placement</c>; Idrak's TextMatchers).</summary>
    public string Placement { get; init; } = "semi-global";

    /// <summary>The largest share of a snippet's characters its placed span may differ in (<c>placementMaxError</c>); more: it goes at the selection or the caret.</summary>
    public double PlacementMaxError { get; init; } = 0.35;

    /// <summary>The command sandbox the agent's commands run in (<c>agentSandbox</c>; Idrak's CommandSandboxes): none registered under it, the agent runs no command.</summary>
    public string AgentSandbox { get; init; } = "approve-each";

    /// <summary>The agent's most turns (<c>agentMaxTurns</c>).</summary>
    public int AgentMaxTurns { get; init; } = 20;

    /// <summary>The agent's time limit, minutes (<c>agentMinutes</c>).</summary>
    public int AgentMinutes { get; init; } = 20;

    /// <summary>The most tokens one agent turn samples (<c>agentMaxTokens</c>).</summary>
    public int AgentMaxTokens { get; init; } = 2048;

    /// <summary>Where the agent's working copies go (<c>agentFolder</c>); empty: the system's temporary folder, under craft-agent.</summary>
    public string AgentFolder { get; init; } = "";

    /// <summary>The folder names the agent's working copy leaves out (<c>agentCopyIgnore</c>; obj stays, so a restore is not needed).</summary>
    public IReadOnlyList<string> AgentCopyIgnore { get; init; } = ["bin", ".git", ".vs", "node_modules", "test-output"];

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
        new("modelProvider", "local", "The model provider the AI plug-ins ask: local, or one a plug-in registers."),
        new("completion", "true", "Whether inline completion shows ghost text after a pause in typing."),
        new("completionModel", "", "The model inline completion uses; empty: the model."),
        new("completionDelayMs", "300", "Milliseconds after typing before a completion is asked for."),
        new("completionMaxTokens", "64", "The most tokens one completion samples."),
        new("completionMaxLines", "8", "The most lines one completion shows."),
        new("completionSuffixChars", "512", "Characters after the caret a fill-in-the-middle prompt carries (a prefix-first model processes them again each request); 0: none."),
        new("completionPrefixChars", "0", "Characters before the caret a prompt carries; 0: what the model's context leaves."),
        new("completionChunk", "1", "Tokens sampled between host synchronizations while a completion streams; 1 shows each token."),
        new("completionTemperature", "0", "Completion's sampling temperature; 0 takes the likeliest token."),
        new("chatMaxTokens", "1024", "The most tokens one chat reply samples."),
        new("chatToolRounds", "6", "How many times a chat reply may call the editor's tools before it answers."),
        new("chatKeep", "true", "Whether the chat is kept per workspace (in chatFile)."),
        new("chatFile", Path.Combine(".craft", "chat.json"), "Where the chat is kept, relative to the workspace."),
        new("chatHistory", "40", "The most chat messages kept and sent back to the model."),
        new("placement", "semi-global", "The Idrak text matcher that places a model's snippet in a document."),
        new("placementMaxError", "0.35", "The largest share of a snippet's characters its placed span may differ in; more: it goes at the selection or caret."),
        new("agentSandbox", "approve-each", "The command sandbox the agent's commands run in; none registered under it: the agent runs no command."),
        new("agentMaxTurns", "20", "The agent's most turns."),
        new("agentMinutes", "20", "The agent's time limit in minutes."),
        new("agentMaxTokens", "2048", "The most tokens one agent turn samples."),
        new("agentFolder", "", "Where the agent's working copies go; empty: the temporary folder."),
        new("agentCopyIgnore", "bin;.git;.vs;node_modules;test-output", "Folder names the agent's working copy leaves out (';' or ',' between them)."),
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

        double Real(string key, double fallback, double min, double max)
        {
            if (Text(key) is not { } text)
            {
                return fallback;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number >= min && number <= max
                ? number
                : throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a number from {min} to {max}."), key);
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
            ModelProvider = Text("modelProvider") ?? defaults.ModelProvider,
            Completion = Flag("completion", defaults.Completion),
            CompletionModel = Text("completionModel") ?? "",
            CompletionDelayMilliseconds = Whole("completionDelayMs", defaults.CompletionDelayMilliseconds, 0, 60_000),
            CompletionMaxTokens = Whole("completionMaxTokens", defaults.CompletionMaxTokens, 1, 100_000),
            CompletionMaxLines = Whole("completionMaxLines", defaults.CompletionMaxLines, 1, 10_000),
            CompletionSuffixCharacters = Whole("completionSuffixChars", defaults.CompletionSuffixCharacters, 0, 10_000_000),
            CompletionPrefixCharacters = Whole("completionPrefixChars", 0, 0, 100_000_000),
            CompletionChunk = Whole("completionChunk", defaults.CompletionChunk, 1, 4096),
            CompletionTemperature = (float)Real("completionTemperature", 0, 0, 10),
            ChatMaxTokens = Whole("chatMaxTokens", defaults.ChatMaxTokens, 1, 1_000_000),
            ChatToolRounds = Whole("chatToolRounds", defaults.ChatToolRounds, 0, 1000),
            ChatKeep = Flag("chatKeep", defaults.ChatKeep),
            ChatFile = Text("chatFile") ?? defaults.ChatFile,
            ChatHistory = Whole("chatHistory", defaults.ChatHistory, 2, 100_000),
            Placement = Text("placement") ?? defaults.Placement,
            PlacementMaxError = Real("placementMaxError", defaults.PlacementMaxError, 0, 1),
            AgentSandbox = Text("agentSandbox") ?? defaults.AgentSandbox,
            AgentMaxTurns = Whole("agentMaxTurns", defaults.AgentMaxTurns, 1, 10_000),
            AgentMinutes = Whole("agentMinutes", defaults.AgentMinutes, 1, 100_000),
            AgentMaxTokens = Whole("agentMaxTokens", defaults.AgentMaxTokens, 1, 1_000_000),
            AgentFolder = Text("agentFolder") ?? "",
            AgentCopyIgnore = List("agentCopyIgnore", defaults.AgentCopyIgnore),
        };
    }
}
