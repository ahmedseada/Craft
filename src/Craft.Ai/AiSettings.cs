// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// The AI plug-in's settings (plug-in "ai"; each a variable <c>CRAFT_PLUGIN_AI_&lt;KEY&gt;</c> on the settings page and a
/// key of <c>.craft/settings.json</c>): the MCP server's address and token, and three model roles (note 25), each with the
/// same set (enabled, model, device, weights, context, keep-alive, temperature, most reply tokens): the chat's (the
/// unprefixed <c>model</c>, <c>device</c>, <c>weights</c>, <c>context</c>, <c>keepAliveMinutes</c>, kept from before),
/// inline completion's (<c>completion*</c>) and the agent's (<c>agent*</c>, an empty one taking the chat's, so by default
/// the two share one copy on the device); <see cref="ChatRole"/>, <see cref="AgentRole"/> and
/// <see cref="CompletionRole"/> resolve them. The library's limits (the editor tools' own are Craft.Plugins.Tools'). Inside the app they come from the
/// host's settings (environment, the workspace's and the user's settings.json); in another process (the folder backing
/// under the idrak tool) from the environment.
/// </summary>
public sealed record AiSettings
{
    /// <summary>The plug-in's name, the prefix of its settings' variables.</summary>
    public const string PluginName = "ai";

    /// <summary>Where the MCP server listens, host:port (<c>mcpAddress</c>); port 0 lets the system pick one.</summary>
    public string McpAddress { get; init; } = "127.0.0.1:0";

    /// <summary>The bearer token MCP clients send (<c>mcpToken</c>); empty for a new random one each start, "none" for no token.</summary>
    public string McpToken { get; init; } = "";

    /// <summary>Whether the MCP server starts with the editor (<c>mcpOnStart</c>); off: the command "ai.mcpStart" starts it.</summary>
    public bool McpOnStart { get; init; }

    /// <summary>Whether the chat answers (<c>chatEnabled</c>; the chat panel and the code actions); off: it loads no model and its panel says so.</summary>
    public bool ChatEnabled { get; init; } = true;

    /// <summary>
    /// The chat model's name (<c>model</c>: the chat and the code actions, and the agent's when <see cref="AgentModel"/>
    /// is empty), resolved by Idrak's model sources; empty for none. It names the model; it does not load it (see
    /// <see cref="LoadOnStart"/>).
    /// </summary>
    public string Model { get; init; } = "";

    /// <summary>The device the chat model loads on (<c>device</c>): "auto" (the best found), "cpu", "vulkan:0" and the like.</summary>
    public string Device { get; init; } = "auto";

    /// <summary>The chat model's context window in tokens (<c>context</c>); 0 for the model's own (its MaxPositions).</summary>
    public int Context { get; init; }

    /// <summary>The chat model's projections once loaded (<c>weights</c>): "int8", "int4", "bfloat16", or empty for float32.</summary>
    public string Weights { get; init; } = "";

    /// <summary>Minutes the chat model stays loaded once unused (<c>keepAliveMinutes</c>); 0 unloads it when the last lease ends.</summary>
    public int KeepAliveMinutes { get; init; } = 10;

    /// <summary>The chat's sampling temperature (<c>chatTemperature</c>); 0 takes the likeliest token. The default is the library's.</summary>
    public float ChatTemperature { get; init; } = DefaultTemperature;

    /// <summary>The library's default sampling temperature (Idrak's <see cref="GenerationOptions.Temperature"/>), the chat's default.</summary>
    public static float DefaultTemperature { get; } = new GenerationOptions().Temperature;

    /// <summary>Whether the agent runs (<c>agentEnabled</c>); off: it loads no model and its panel says so.</summary>
    public bool AgentEnabled { get; init; } = true;

    /// <summary>The agent's model (<c>agentModel</c>); empty: the chat's (<see cref="Model"/>), one copy shared on the device.</summary>
    public string AgentModel { get; init; } = "";

    /// <summary>The device the agent's model loads on (<c>agentDevice</c>); empty: the chat's (<see cref="Device"/>).</summary>
    public string AgentDevice { get; init; } = "";

    /// <summary>The agent's model's weights once loaded (<c>agentWeights</c>: "float32", "int8", "int4", "bfloat16"); empty: the chat's.</summary>
    public string AgentWeights { get; init; } = "";

    /// <summary>The agent's model's context window in tokens (<c>agentContext</c>; 0: the model's own); null (empty): the chat's.</summary>
    public int? AgentContext { get; init; }

    /// <summary>Minutes the agent's model stays loaded once unused (<c>agentKeepAliveMinutes</c>); null (empty): the chat's.</summary>
    public int? AgentKeepAliveMinutes { get; init; }

    /// <summary>The agent's sampling temperature (<c>agentTemperature</c>); null (empty): the chat's.</summary>
    public float? AgentTemperature { get; init; }

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

    /// <summary>
    /// The model inline completion uses (<c>completionModel</c>): a small one, so it sits on the device beside the chat
    /// model (<see cref="Model"/>, the big one) with room for the agent's cache. Empty: completion uses the chat model,
    /// only while it is loaded (completion never loads it). The default is a setting's default, not a family's code: its
    /// fill-in-the-middle format comes from the family's Idrak plug-in like any other model's.
    /// </summary>
    public string CompletionModel { get; init; } = DefaultCompletionModel;

    /// <summary>The default of <see cref="CompletionModel"/>: a small coder model with a fill-in-the-middle format.</summary>
    public const string DefaultCompletionModel = "Qwen/Qwen2.5-Coder-1.5B";

    /// <summary>The device completion's own model loads on (<c>completionDevice</c>); empty: auto (the best found).</summary>
    public string CompletionDevice { get; init; } = "";

    /// <summary>
    /// Completion's own model's weights once loaded (<c>completionWeights</c>: "float32", "int8", "int4", "bfloat16");
    /// empty: the chat's (<see cref="Weights"/>), as before note 25, so a small model sits beside the big one as it did.
    /// </summary>
    public string CompletionWeights { get; init; } = "";

    /// <summary>Completion's own model's context window in tokens (<c>completionContext</c>; 0: the model's own); null (empty): the chat's.</summary>
    public int? CompletionContext { get; init; }

    /// <summary>Minutes completion's own model stays loaded once unused (<c>completionKeepAliveMinutes</c>); null (empty): the chat's.</summary>
    public int? CompletionKeepAliveMinutes { get; init; }

    /// <summary>
    /// Whether inline completion loads <see cref="CompletionModel"/> on its first request (<c>completionLoad</c>); off:
    /// it uses a model already loaded (by the load command, or the chat model) and does nothing without one, said once in
    /// the status bar. Nothing loads when the editor starts either way.
    /// </summary>
    public bool CompletionLoad { get; init; } = true;

    /// <summary>
    /// Whether the chat model loads when the editor starts (<c>loadOnStart</c>); off (the default): it loads when the
    /// user picks or confirms it at the composer's model chip, sends the first message, or runs the load command.
    /// </summary>
    public bool LoadOnStart { get; init; }

    /// <summary>
    /// The pause after typing before a completion is asked for, ms (<c>completionDelayMs</c>): a debounce, each keystroke
    /// starts the wait again, so nothing is asked while typing goes on.
    /// </summary>
    public int CompletionDelayMilliseconds { get; init; } = 600;

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

    /// <summary>
    /// The most tokens one chat reply samples (<c>chatMaxTokens</c>); the model's context window may leave fewer (a reply
    /// cut there is said so in the panel). A reply otherwise ends only at the model's end of turn or the user's Stop.
    /// </summary>
    public int ChatMaxTokens { get; init; } = 2048;

    /// <summary>Texts that end a chat reply beside the chat template's own end of turn (<c>chatStops</c>); none by default.</summary>
    public IReadOnlyList<string> ChatStops { get; init; } = [];

    /// <summary>How many times a chat reply may call the editor's tools before it must answer (<c>chatToolRounds</c>).</summary>
    public int ChatToolRounds { get; init; } = 6;

    /// <summary>Whether the chat is kept per workspace (<c>chatKeep</c>), in <see cref="ChatFile"/>.</summary>
    public bool ChatKeep { get; init; } = true;

    /// <summary>Where the chat is kept, relative to the workspace (<c>chatFile</c>).</summary>
    public string ChatFile { get; init; } = Path.Combine(".craft", "chat.json");

    /// <summary>The most messages kept and sent back to the model (<c>chatHistory</c>); older ones are dropped first.</summary>
    public int ChatHistory { get; init; } = 40;

    /// <summary>The most lines of a file a chat context chip sends to the model (<c>chatFileLines</c>).</summary>
    public int ChatFileLines { get; init; } = 2000;

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

    /// <summary>The most tokens one agent turn samples (<c>agentMaxTokens</c>); the model's context window may leave fewer.</summary>
    public int AgentMaxTokens { get; init; } = 2048;

    /// <summary>Texts that end an agent turn beside the chat template's own end of turn (<c>agentStops</c>); none by default.</summary>
    public IReadOnlyList<string> AgentStops { get; init; } = [];

    /// <summary>Where the agent's working copies go (<c>agentFolder</c>); empty: the system's temporary folder, under craft-agent.</summary>
    public string AgentFolder { get; init; } = "";

    /// <summary>The folder names the agent's working copy leaves out (<c>agentCopyIgnore</c>; obj stays, so a restore is not needed).</summary>
    public IReadOnlyList<string> AgentCopyIgnore { get; init; } = ["bin", ".git", ".vs", "node_modules", "test-output"];

    /// <summary>
    /// The folder names whose files the agent's changes leave out when they are found by the files written (<c>agentChangesIgnore</c>:
    /// build output a command wrote, never a change to review).
    /// </summary>
    public IReadOnlyList<string> AgentChangesIgnore { get; init; } = ["bin", "obj", ".git", ".vs", "node_modules", "test-output"];

    /// <summary>
    /// Whether an existing file's accepted agent change is saved to the workspace at once (<c>agentSaveOnAccept</c>, on);
    /// never when the editor held unsaved edits of the user's before the diff. A new file accepted whole is always created.
    /// </summary>
    public bool AgentSaveOnAccept { get; init; } = true;

    /// <summary>
    /// Corrections in a row the agent sends when a reply holds tool calls it could not read (note 27: calls written as text
    /// in a shape the template's format does not read), each telling the model how its template writes a call
    /// (<c>agentNudges</c>, 0-20; Idrak's <c>AgentOptions.Nudges</c>); 0 sends none.
    /// </summary>
    public int AgentNudges { get; init; } = 2;

    /// <summary>
    /// Whether tool calls the model writes as text (markdown fences, JSON in its prose) naming an offered tool are read and
    /// run (<c>agentCallsInText</c>, on; Idrak's <c>AgentOptions.CallsInText</c>); off: only calls in the template's format.
    /// </summary>
    public bool AgentCallsInText { get; init; } = true;

    /// <summary>The largest image a chat may carry, MB (<c>chatImageMaxMb</c>); a larger one dropped on the chat is not added, said so.</summary>
    public int ChatImageMaxMegabytes { get; init; } = 20;

    /// <summary>The chat's model settings, resolved (note 25).</summary>
    public AiRoleSettings ChatRole => new(ModelRoleKeys.Chat, ChatEnabled, Model.Trim(), DeviceOf(Device), Context, WeightsOf(Weights), KeepAliveMinutes, ChatTemperature, ChatMaxTokens);

    /// <summary>The agent's model settings, resolved: an empty model, device, weights, context, keep-alive or temperature is the chat's.</summary>
    public AiRoleSettings AgentRole => new(ModelRoleKeys.Agent, AgentEnabled,
        AgentModel.Trim() is { Length: > 0 } model ? model : Model.Trim(),
        AgentDevice.Trim() is { Length: > 0 } device ? DeviceOf(device) : DeviceOf(Device),
        AgentContext ?? Context,
        AgentWeights.Trim() is { Length: > 0 } weights ? WeightsOf(weights) : WeightsOf(Weights),
        AgentKeepAliveMinutes ?? KeepAliveMinutes,
        AgentTemperature ?? ChatTemperature,
        AgentMaxTokens)
    {
        InheritsModel = AgentModel.Trim().Length == 0,
        InheritsDevice = AgentDevice.Trim().Length == 0,
    };

    /// <summary>
    /// Inline completion's model settings, resolved: its own model (empty: none of its own, the chat model while loaded),
    /// its device (empty: auto), and weights, context and keep-alive that are the chat's when empty.
    /// </summary>
    public AiRoleSettings CompletionRole => new(ModelRoleKeys.Completion, Completion, CompletionModel.Trim(), DeviceOf(CompletionDevice),
        CompletionContext ?? Context,
        CompletionWeights.Trim() is { Length: > 0 } weights ? WeightsOf(weights) : WeightsOf(Weights),
        CompletionKeepAliveMinutes ?? KeepAliveMinutes,
        CompletionTemperature, CompletionMaxTokens);

    /// <summary>The role of key <paramref name="key"/> (<see cref="ModelRoleKeys"/>).</summary>
    /// <exception cref="ArgumentException">No role has that key.</exception>
    public AiRoleSettings RoleOf(string key) => key switch
    {
        ModelRoleKeys.Chat => ChatRole,
        ModelRoleKeys.Agent => AgentRole,
        ModelRoleKeys.Completion => CompletionRole,
        _ => throw new ArgumentException($"No model role '{key}': the roles are {ModelRoleKeys.Chat}, {ModelRoleKeys.Agent} and {ModelRoleKeys.Completion}.", nameof(key)),
    };

    // An empty device is auto; one name for float32 (empty), so equal settings make equal keys and share one copy.
    private static string DeviceOf(string device) => device.Trim() is { Length: > 0 } named ? named : "auto";

    private static string WeightsOf(string weights)
    {
        string named = weights.Trim();
        return named.Equals("float32", StringComparison.OrdinalIgnoreCase) || named.Equals("f32", StringComparison.OrdinalIgnoreCase) ? "" : named;
    }

    /// <summary>The settings the plug-in declares, with their defaults and meanings (the settings page lists them).</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("mcpAddress", "127.0.0.1:0", "Where the MCP server of the live tools listens (host:port; port 0 picks one); loopback only."),
        new("mcpToken", "", "The bearer token MCP clients send; empty: a new random one each start; 'none': no token."),
        new("mcpOnStart", "false", "Whether the MCP server starts with the editor (else the command 'AI: Serve the editor's tools over MCP')."),
        new("chatEnabled", "true", "Whether the chat answers (the chat panel and the code actions); off: it loads no model."),
        new("model", "", "The chat's model (and the agent's when agentModel is empty): a folder, a .gguf file, store:NAME or a Hugging Face id (Idrak's model sources)."),
        new("device", "auto", "The device the chat's model loads on: auto, cpu, cuda:0, vulkan:0."),
        new("weights", "", "The chat's model's projections once loaded: int8, int4, bfloat16; empty: float32."),
        new("context", "0", "The chat's model's context window in tokens; 0: the model's own."),
        new("keepAliveMinutes", "10", "Minutes the chat's model stays loaded once unused; 0 unloads it when no plug-in uses it."),
        new("chatTemperature", DefaultTemperature.ToString(CultureInfo.InvariantCulture), "The chat's sampling temperature; 0 takes the likeliest token."),
        new("threads", "0", "CPU threads the library may use (Idrak's ComputeResources); 0: every core."),
        new("cpuMemoryMb", "0", "CPU memory the library may hold, MiB; 0: no limit."),
        new("gpuMemoryMb", "0", "GPU memory the library may hold per device, MiB; 0: no limit."),
        new("idrakPlugins", "Idrak.Text.Plugins", "Idrak plug-in packages (the model families) registered before the first local load; empty: none."),
        new("modelProvider", "local", "The model provider the AI plug-ins ask: local, or one a plug-in registers."),
        new("completion", "true", "Whether inline completion shows ghost text after a pause in typing; off: it asks for nothing and loads nothing."),
        new("completionModel", DefaultCompletionModel, "The model inline completion uses: a small one beside the chat model (model); empty: the chat model, only while it is loaded."),
        new("completionDevice", "", "The device completion's model loads on: auto, cpu, cuda:0, vulkan:0; empty: auto."),
        new("completionWeights", "", "Completion's model's projections once loaded: float32, int8, int4, bfloat16; empty: the chat's (weights)."),
        new("completionContext", "", "Completion's model's context window in tokens (0: the model's own); empty: the chat's (context)."),
        new("completionKeepAliveMinutes", "", "Minutes completion's model stays loaded once unused; empty: the chat's (keepAliveMinutes)."),
        new("completionLoad", "true", "Whether inline completion loads its model on its first request; off: it uses a model already loaded, else does nothing."),
        new("loadOnStart", "false", "Whether the chat model loads when the editor starts; off: when you pick it at the composer, send a message or run 'AI: Load the local model'."),
        new("completionDelayMs", "600", "The pause after the last keystroke before a completion is asked for, ms; each keystroke starts the wait again."),
        new("completionMaxTokens", "64", "The most tokens one completion samples."),
        new("completionMaxLines", "8", "The most lines one completion shows."),
        new("completionSuffixChars", "512", "Characters after the caret a fill-in-the-middle prompt carries (a prefix-first model processes them again each request); 0: none."),
        new("completionPrefixChars", "0", "Characters before the caret a prompt carries; 0: what the model's context leaves."),
        new("completionChunk", "1", "Tokens sampled between host synchronizations while a completion streams; 1 shows each token."),
        new("completionTemperature", "0", "Completion's sampling temperature; 0 takes the likeliest token."),
        new("chatMaxTokens", "2048", "The most tokens one chat reply samples (the model's context may leave fewer); a reply otherwise ends at the model's end of turn or Stop."),
        new("chatStops", "", "Texts that end a chat reply beside the template's own end of turn ('|' between them); empty: none."),
        new("chatToolRounds", "6", "How many times a chat reply may call the editor's tools before it answers."),
        new("chatKeep", "true", "Whether the chat is kept per workspace (in chatFile)."),
        new("chatFile", Path.Combine(".craft", "chat.json"), "Where the chat is kept, relative to the workspace."),
        new("chatHistory", "40", "The most chat messages kept and sent back to the model."),
        new("chatFileLines", "2000", "The most lines of a file a chat context chip sends to the model."),
        new("chatImageMaxMb", "20", "The largest image a chat may carry, MB; a larger one dropped on the chat is not added."),
        new("placement", "semi-global", "The Idrak text matcher that places a model's snippet in a document."),
        new("placementMaxError", "0.35", "The largest share of a snippet's characters its placed span may differ in; more: it goes at the selection or caret."),
        new("agentEnabled", "true", "Whether the agent runs; off: it loads no model."),
        new("agentModel", "", "The agent's model; empty: the chat's (model), so one copy is shared on the device."),
        new("agentDevice", "", "The device the agent's model loads on: auto, cpu, cuda:0, vulkan:0; empty: the chat's (device)."),
        new("agentWeights", "", "The agent's model's projections once loaded: float32, int8, int4, bfloat16; empty: the chat's (weights)."),
        new("agentContext", "", "The agent's model's context window in tokens (0: the model's own); empty: the chat's (context)."),
        new("agentKeepAliveMinutes", "", "Minutes the agent's model stays loaded once unused; empty: the chat's (keepAliveMinutes)."),
        new("agentTemperature", "", "The agent's sampling temperature (0 takes the likeliest token); empty: the chat's (chatTemperature)."),
        new("agentSandbox", "approve-each", "The command sandbox the agent's commands run in; none registered under it: the agent runs no command."),
        new("agentMaxTurns", "20", "The agent's most turns."),
        new("agentMinutes", "20", "The agent's time limit in minutes."),
        new("agentMaxTokens", "2048", "The most tokens one agent turn samples (the model's context may leave fewer)."),
        new("agentStops", "", "Texts that end an agent turn beside the template's own end of turn ('|' between them); empty: none."),
        new("agentFolder", "", "Where the agent's working copies go; empty: the temporary folder."),
        new("agentCopyIgnore", "bin;.git;.vs;node_modules;test-output", "Folder names the agent's working copy leaves out (';' or ',' between them)."),
        new("agentChangesIgnore", "bin;obj;.git;.vs;node_modules;test-output", "Folder names whose files written by the agent's commands are not listed as changes (build output)."),
        new("agentSaveOnAccept", "true", "Save an existing file's accepted agent change to the workspace at once (never over your own unsaved edits)."),
        new("agentNudges", "2", "Corrections in a row the agent sends when the model writes tool calls it cannot read (0-20); 0: none, the run ends."),
        new("agentCallsInText", "true", "Read tool calls the model writes as text (markdown fences, JSON) naming an offered tool; off: only its template's format."),
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

        int? Inherited(string key, int min, int max) => Text(key) is null ? null : Whole(key, 0, min, max);

        bool Flag(string key, bool fallback) => Text(key) switch
        {
            null => fallback,
            var t when t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1" => true,
            var t when t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0" => false,
            var t => throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{t}': it takes true or false.", key),
        };

        IReadOnlyList<string> List(string key, IReadOnlyList<string> fallback) =>
            value(key) is { } text ? text.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : fallback;

        // Stop texts are taken as written ('|' between them: a stop may hold ';' or ','), empty ones dropped.
        IReadOnlyList<string> Stops(string key) => value(key) is { Length: > 0 } text ? text.Split('|', StringSplitOptions.RemoveEmptyEntries) : [];

        return new AiSettings
        {
            McpAddress = Text("mcpAddress") ?? defaults.McpAddress,
            McpToken = Text("mcpToken") ?? "",
            McpOnStart = Flag("mcpOnStart", false),
            ChatEnabled = Flag("chatEnabled", defaults.ChatEnabled),
            Model = Text("model") ?? "",
            Device = Text("device") ?? defaults.Device,
            Context = Whole("context", 0, 0, 16_777_216),
            Weights = Text("weights") ?? "",
            KeepAliveMinutes = Whole("keepAliveMinutes", defaults.KeepAliveMinutes, 0, 1_000_000),
            ChatTemperature = (float)Real("chatTemperature", DefaultTemperature, 0, 10),
            AgentEnabled = Flag("agentEnabled", defaults.AgentEnabled),
            AgentModel = Text("agentModel") ?? "",
            AgentDevice = Text("agentDevice") ?? "",
            AgentWeights = Text("agentWeights") ?? "",
            AgentContext = Inherited("agentContext", 0, 16_777_216),
            AgentKeepAliveMinutes = Inherited("agentKeepAliveMinutes", 0, 1_000_000),
            AgentTemperature = Text("agentTemperature") is null ? null : (float)Real("agentTemperature", 0, 0, 10),
            CompletionDevice = Text("completionDevice") ?? "",
            CompletionWeights = Text("completionWeights") ?? "",
            CompletionContext = Inherited("completionContext", 0, 16_777_216),
            CompletionKeepAliveMinutes = Inherited("completionKeepAliveMinutes", 0, 1_000_000),
            Threads = Whole("threads", 0, 0, 1_000_000),
            CpuMemoryMegabytes = Whole("cpuMemoryMb", 0, 0, int.MaxValue),
            GpuMemoryMegabytes = Whole("gpuMemoryMb", 0, 0, int.MaxValue),
            IdrakPlugins = List("idrakPlugins", defaults.IdrakPlugins),
            ModelProvider = Text("modelProvider") ?? defaults.ModelProvider,
            Completion = Flag("completion", defaults.Completion),
            CompletionModel = value("completionModel") is { } completionModel ? completionModel.Trim() : defaults.CompletionModel,
            CompletionLoad = Flag("completionLoad", defaults.CompletionLoad),
            LoadOnStart = Flag("loadOnStart", false),
            CompletionDelayMilliseconds = Whole("completionDelayMs", defaults.CompletionDelayMilliseconds, 0, 60_000),
            CompletionMaxTokens = Whole("completionMaxTokens", defaults.CompletionMaxTokens, 1, 100_000),
            CompletionMaxLines = Whole("completionMaxLines", defaults.CompletionMaxLines, 1, 10_000),
            CompletionSuffixCharacters = Whole("completionSuffixChars", defaults.CompletionSuffixCharacters, 0, 10_000_000),
            CompletionPrefixCharacters = Whole("completionPrefixChars", 0, 0, 100_000_000),
            CompletionChunk = Whole("completionChunk", defaults.CompletionChunk, 1, 4096),
            CompletionTemperature = (float)Real("completionTemperature", 0, 0, 10),
            ChatMaxTokens = Whole("chatMaxTokens", defaults.ChatMaxTokens, 1, 1_000_000),
            ChatStops = Stops("chatStops"),
            ChatToolRounds = Whole("chatToolRounds", defaults.ChatToolRounds, 0, 1000),
            ChatKeep = Flag("chatKeep", defaults.ChatKeep),
            ChatFile = Text("chatFile") ?? defaults.ChatFile,
            ChatHistory = Whole("chatHistory", defaults.ChatHistory, 2, 100_000),
            ChatFileLines = Whole("chatFileLines", defaults.ChatFileLines, 1, 1_000_000),
            ChatImageMaxMegabytes = Whole("chatImageMaxMb", defaults.ChatImageMaxMegabytes, 1, 4096),
            Placement = Text("placement") ?? defaults.Placement,
            PlacementMaxError = Real("placementMaxError", defaults.PlacementMaxError, 0, 1),
            AgentSandbox = Text("agentSandbox") ?? defaults.AgentSandbox,
            AgentMaxTurns = Whole("agentMaxTurns", defaults.AgentMaxTurns, 1, 10_000),
            AgentMinutes = Whole("agentMinutes", defaults.AgentMinutes, 1, 100_000),
            AgentMaxTokens = Whole("agentMaxTokens", defaults.AgentMaxTokens, 1, 1_000_000),
            AgentStops = Stops("agentStops"),
            AgentFolder = Text("agentFolder") ?? "",
            AgentCopyIgnore = List("agentCopyIgnore", defaults.AgentCopyIgnore),
            AgentChangesIgnore = List("agentChangesIgnore", defaults.AgentChangesIgnore),
            AgentSaveOnAccept = Flag("agentSaveOnAccept", defaults.AgentSaveOnAccept),
            AgentNudges = Whole("agentNudges", defaults.AgentNudges, 0, 20),
            AgentCallsInText = Flag("agentCallsInText", defaults.AgentCallsInText),
        };
    }
}

/// <summary>
/// One model role's settings, resolved (note 25: <see cref="AiSettings.ChatRole"/>, <see cref="AiSettings.AgentRole"/>,
/// <see cref="AiSettings.CompletionRole"/>): what its lease is kept under (the model, the device, the context, the weights;
/// two roles equal in those share one copy) and how it samples.
/// </summary>
/// <param name="Key">The role (<see cref="ModelRoleKeys"/>).</param>
/// <param name="Enabled">Whether it is on.</param>
/// <param name="Model">The model's name; empty for none.</param>
/// <param name="Device">The device ("auto", "cpu", "cuda:0").</param>
/// <param name="Context">The context window in tokens; 0 for the model's own.</param>
/// <param name="Weights">The weights once loaded ("int8", "int4", "bfloat16"); empty for float32.</param>
/// <param name="KeepAliveMinutes">Minutes its model stays loaded once unused.</param>
/// <param name="Temperature">Its sampling temperature.</param>
/// <param name="MaxTokens">The most tokens one reply samples.</param>
public sealed record AiRoleSettings(string Key, bool Enabled, string Model, string Device, int Context, string Weights, int KeepAliveMinutes, float Temperature, int MaxTokens)
{
    /// <summary>Whether the model is the chat's (the agent's own setting is empty).</summary>
    public bool InheritsModel { get; init; }

    /// <summary>Whether the device is the chat's (the agent's own setting is empty).</summary>
    public bool InheritsDevice { get; init; }
}
