// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's note 25 ("all models should have settings: inline completion (enable / disable) and device to run on and
/// each respective settings; chat, agent model as well"): three roles, each with its own set; the agent takes the chat's
/// model and device when its own are empty, so one copy is shared; a lease per role keyed on its own settings; a role
/// turned off loads nothing.
/// </summary>
[TestGroup("ModelLoading")]
public static class ModelRolesTests
{
    private const string ProviderName = "model-roles-test";

    [Test]
    public static void EachRoleReadsItsOwnSettingsAndTheChatKeepsItsOldNames()
    {
        var defaults = AiSettings.Read(_ => null);
        Check.True(defaults.ChatRole is { Enabled: true, Model: "", Device: "auto", Context: 0, Weights: "auto", KeepAliveMinutes: 10 },
            $"the chat's defaults ({defaults.ChatRole})");
        Check.Equal(AiSettings.DefaultTemperature, defaults.ChatRole.Temperature, "the chat's temperature is the library's default");
        Check.True(defaults.CompletionRole is { Enabled: true, Model: AiSettings.DefaultCompletionModel, Device: "auto" }, $"completion's defaults: the 1.5B on auto ({defaults.CompletionRole})");
        Check.True(defaults.AgentRole is { Enabled: true, InheritsModel: true, InheritsDevice: true }, "the agent takes the chat's model and device by default");

        // The old variables are the chat's (CRAFT_PLUGIN_AI_MODEL keeps working).
        var variables = new Dictionary<string, string>
        {
            ["CRAFT_PLUGIN_AI_MODEL"] = "Qwen/Qwen2.5-Coder-7B-Instruct",
            ["CRAFT_PLUGIN_AI_DEVICE"] = "cuda:0",
            ["CRAFT_PLUGIN_AI_WEIGHTS"] = "int8",
            ["CRAFT_PLUGIN_AI_CONTEXT"] = "8192",
            ["CRAFT_PLUGIN_AI_KEEPALIVEMINUTES"] = "5",
            ["CRAFT_PLUGIN_AI_CHATTEMPERATURE"] = "0.2",
            ["CRAFT_PLUGIN_AI_CHATMAXTOKENS"] = "1024",
            ["CRAFT_PLUGIN_AI_COMPLETIONDEVICE"] = "cpu",
            ["CRAFT_PLUGIN_AI_COMPLETIONWEIGHTS"] = "float32",
            ["CRAFT_PLUGIN_AI_COMPLETIONCONTEXT"] = "2048",
            ["CRAFT_PLUGIN_AI_COMPLETIONKEEPALIVEMINUTES"] = "1",
        };
        var read = AiSettings.Read(key => variables.GetValueOrDefault(PluginSetting.VariableName(AiSettings.PluginName, key)));
        Check.True(read.ChatRole is { Model: "Qwen/Qwen2.5-Coder-7B-Instruct", Device: "cuda:0", Weights: "int8", Context: 8192, KeepAliveMinutes: 5, MaxTokens: 1024 },
            $"model, device, weights, context and keepAliveMinutes are the chat's ({read.ChatRole})");
        Check.True(Math.Abs(read.ChatRole.Temperature - 0.2f) < 1e-6, "chatTemperature");
        Check.True(read.CompletionRole is { Model: AiSettings.DefaultCompletionModel, Device: "cpu", Weights: "float32", Context: 2048, KeepAliveMinutes: 1 },
            $"completion's own device, weights (float32 named), context and keep-alive ({read.CompletionRole})");
        Check.True(read.AgentRole is { Model: "Qwen/Qwen2.5-Coder-7B-Instruct", Device: "cuda:0", Weights: "int8", Context: 8192, KeepAliveMinutes: 5, InheritsModel: true },
            $"the agent inherits every empty one from the chat ({read.AgentRole})");
        Check.True(AiSettings.Read(key => key == "completionWeights" ? "" : variables.GetValueOrDefault(PluginSetting.VariableName(AiSettings.PluginName, key))).CompletionRole.Weights == "int8",
            "an empty completionWeights is the chat's, as before note 25");

        var own = AiSettings.Read(key => key switch
        {
            "model" => "big", "agentModel" => "agent-model", "agentDevice" => "cpu", "agentWeights" => "int4", "agentContext" => "4096",
            "agentKeepAliveMinutes" => "0", "agentTemperature" => "0", "agentMaxTokens" => "512", "agentEnabled" => "false", "chatEnabled" => "false", "completion" => "false",
            _ => null,
        });
        Check.True(own.AgentRole is { Enabled: false, Model: "agent-model", Device: "cpu", Weights: "int4", Context: 4096, KeepAliveMinutes: 0, Temperature: 0, MaxTokens: 512, InheritsModel: false, InheritsDevice: false },
            $"the agent's own set ({own.AgentRole})");
        Check.True(own is { ChatEnabled: false, Completion: false }, "chatEnabled and completion turn their roles off");

        foreach (var key in (string[])["chatEnabled", "chatTemperature", "completionDevice", "completionWeights", "completionContext", "completionKeepAliveMinutes",
                     "agentEnabled", "agentModel", "agentDevice", "agentWeights", "agentContext", "agentKeepAliveMinutes", "agentTemperature"])
        {
            Check.True(AiSettings.Declared.Any(s => s.Key == key), $"{key} is declared ({PluginSetting.VariableName(AiSettings.PluginName, key)}, a .craft/settings.json key)");
        }

        bool threw = false;
        try
        {
            _ = AiSettings.Read(key => key == "agentContext" ? "lots" : null);
        }
        catch (ArgumentException error)
        {
            threw = error.Message.Contains("CRAFT_PLUGIN_AI_AGENTCONTEXT", StringComparison.Ordinal);
        }

        Check.True(threw, "a value that does not parse names its variable");
    }

    [Test]
    public static async Task TheAgentSharesTheChatsCopyUntilItsOwnSettingsDiffer()
    {
        var provider = new CountingProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", Weights = "int8", CompletionModel = "small" };
            using var models = new AiModels(() => settings);
            var chat = await models.ChatAsync(CancellationToken.None);
            var agent = await models.AgentAsync(CancellationToken.None);
            Check.True(ReferenceEquals(chat, agent), "the agent inherits the chat's model and settings: one lease");
            Check.Equal(1, provider.Acquired.Count, "one copy on the card");
            Check.True(models.IsAgentLoaded, "and the agent's model reads as loaded");

            settings = settings with { AgentWeights = "int4" };                  // the agent's own weights
            var own = await models.AgentAsync(CancellationToken.None);
            Check.True(!ReferenceEquals(chat, own), "other weights: a copy of its own");
            Check.True(provider.Requests.Any(r => r is { Model: "big", Weights: "int4" }), "under the agent's settings");
            Check.True(provider.Released.IsEmpty, "the chat still holds its copy: nothing given back");

            settings = settings with { AgentWeights = "" };                      // back to the chat's
            var back = await models.AgentAsync(CancellationToken.None);
            Check.True(ReferenceEquals(chat, back), "equal settings again: the chat's lease");
            Check.True(provider.Released.Contains("big int4"), $"the agent's own copy is let go ({string.Join(", ", provider.Released)})");

            settings = settings with { AgentModel = "other", AgentDevice = "cpu", AgentKeepAliveMinutes = 1 };
            _ = await models.AgentAsync(CancellationToken.None);
            Check.True(provider.Requests.Any(r => r is { Model: "other", Device: "cpu" } && r.KeepAlive == TimeSpan.FromMinutes(1)),
                "the agent's own model, device and keep-alive go to the provider");

            settings = settings with { CompletionDevice = "cpu", CompletionContext = 2048 };
            _ = await models.CompletionAsync(CancellationToken.None);
            Check.True(provider.Requests.Any(r => r is { Model: "small", Device: "cpu", Context: 2048, Weights: "int8" }),
                "completion's own device and context, the chat's weights while its own are empty");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static async Task ARoleTurnedOffLoadsNothing()
    {
        var provider = new CountingProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "small", ChatEnabled = false, AgentEnabled = false, Completion = false };
            using var models = new AiModels(() => settings);
            var completion = new AiInlineCompletion(() => settings, models);
            foreach (var (name, ask) in ((string, Func<Task<ModelLease>>)[])[("the chat", () => models.ChatAsync(CancellationToken.None)), ("the agent", () => models.AgentAsync(CancellationToken.None))])
            {
                string? said = null;
                try
                {
                    _ = await ask();
                }
                catch (InvalidOperationException error)
                {
                    said = error.Message;
                }

                Check.True(said is not null && said.Contains("turned off", StringComparison.Ordinal), $"{name} turned off says so ({said})");
            }

            Check.True(!completion.Accepts(Request()), "completion turned off asks for nothing");
            Check.True(await models.CompletionAsync(CancellationToken.None) is null, "and leases nothing");
            Check.True(models.CompletionUnavailable!.Contains("turned off", StringComparison.Ordinal), "its reason says it is off");
            Check.Equal(0, provider.Acquired.Count, "nothing was loaded");

            settings = settings with { Completion = true };
            _ = await models.CompletionAsync(CancellationToken.None);
            Check.Equal(1, provider.Acquired.Count, "turned on, completion loads its model");
            settings = settings with { Completion = false };
            Check.True(!completion.Accepts(Request()), "turned off again");
            Check.True(provider.Released.Contains("small auto"), "and its copy is given back");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static async Task ATurnedOffPanelSaysSoWithALinkToItsSettings()
    {
        var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", ChatEnabled = false, AgentEnabled = false };
        using var models = new AiModels(() => settings);
        using var host = new EditorHost(dispatch: action => action()) { WorkspaceFolder = Path.GetTempPath() };
        using var chat = new AiChatPanel(host, () => settings, models);
        await chat.SubmitAsync("hello");
        Check.True(chat.Entries is [.., { Role: ConversationRole.Error } said] && said.Text.Contains("chatEnabled", StringComparison.Ordinal) && said.Actions.Count == 1,
            "the chat says it is off, with a button to its settings");
        var agent = new AiAgentPanel(host, () => settings, models);
        try
        {
            await agent.SubmitAsync("make a class");
            await agent.Run;
            Check.True(agent.Entries.Any(e => e.Role == ConversationRole.Error && e.Text.Contains("agentEnabled", StringComparison.Ordinal) && e.Actions.Count == 1),
                "the agent says it is off, with a button to its settings");
        }
        finally
        {
            agent.Dispose();
        }

        Check.Equal(ModelRoleKeys.Chat, ((IConversationPanel)chat).ModelRoleKey, "the chat's chip is the chat's role");
        Check.Equal(ModelRoleKeys.Agent, ((IConversationPanel)agent).ModelRoleKey, "the agent's chip is the agent's role");

        var status = new ModelProviderStatus { Roles = [new ModelRole("Chat", "big", "off") { Key = ModelRoleKeys.Chat, Enabled = false, SettingsCommand = SettingSections.CommandOf(SettingSections.Chat) }] };
        var empty = ShellEmptyStates.Conversation(chat, status);
        Check.True(empty.Text.Contains("turned off", StringComparison.Ordinal) && empty.Actions is [{ CommandId: "craft.settings.chat" }],
            $"the empty panel says so and links to Settings › Chat ({empty.Text})");
    }

    internal static InlineCompletionRequest Request() => new(new PieceTableBuffer("int x = ").Current, 8, null, "csharp", null);

    /// <summary>A provider that counts its loads, the requests' settings and the leases given back ("model weights|device").</summary>
    private sealed class CountingProvider : IModelProvider
    {
        public ConcurrentQueue<string> Acquired { get; } = new();

        public ConcurrentQueue<ModelRequest> Requests { get; } = new();

        public ConcurrentBag<string> Released { get; } = [];

        public string Name => ProviderName;

        public string Summary => "the model roles tests' provider";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            Acquired.Enqueue(request.Model);
            Requests.Enqueue(request);
            string held = request.Weights is { Length: > 0 } weights && request.Model != "small" ? $"{request.Model} {weights}" : $"{request.Model} {request.Device}";
            return Task.FromResult(new ModelLease(request.Model, null, null, 4096, TimeSpan.FromMilliseconds(1), new Release(() => Released.Add(held))));
        }

        private sealed class Release(Action released) : IDisposable
        {
            public void Dispose() => released();
        }
    }
}

/// <summary>Note 25 on the settings page: the Completion, Chat and Agent sections each show the role's set with its shapes.</summary>
[TestGroup("Settings")]
public static class ModelRoleSettingsTests
{
    [Test]
    public static void EachRolesSectionShowsItsSetWithShapes()
    {
        var declared = AiSettingShapes.Declared.ToDictionary(s => s.Key, StringComparer.Ordinal);
        var sets = new (string Section, string Enabled, string Model, string Device, string Weights, string Context, string KeepAlive, string Temperature, string MaxTokens)[]
        {
            (SettingSections.Completion, "completion", "completionModel", "completionDevice", "completionWeights", "completionContext", "completionKeepAliveMinutes", "completionTemperature", "completionMaxTokens"),
            (SettingSections.Chat, "chatEnabled", "model", "device", "weights", "context", "keepAliveMinutes", "chatTemperature", "chatMaxTokens"),
            (SettingSections.Agent, "agentEnabled", "agentModel", "agentDevice", "agentWeights", "agentContext", "agentKeepAliveMinutes", "agentTemperature", "agentMaxTokens"),
        };
        foreach (var set in sets)
        {
            foreach (var key in (string[])[set.Enabled, set.Model, set.Device, set.Weights, set.Context, set.KeepAlive, set.Temperature, set.MaxTokens])
            {
                Check.True(declared.TryGetValue(key, out var setting) && setting.Shape?.Section == set.Section, $"{key} is in the {set.Section} section");
            }

            Check.True(declared[set.Enabled].Shape is { Kind: SettingKind.Toggle }, $"{set.Enabled} is a switch");
            Check.True(declared[set.Device].Shape is { Kind: SettingKind.Choice, ChoicesFrom: "devices", OtherAllowed: true }, $"{set.Device} is a choice of the measured devices, others typed");
            Check.True(declared[set.Weights].Shape is { Kind: SettingKind.Choice } weights && weights.Choices.Contains("int8") && weights.Choices.Contains("int4"), $"{set.Weights} is a choice");
            Check.True(declared[set.Context].Shape is { Kind: SettingKind.Whole, Unit: "tokens", Min: 0 }, $"{set.Context} is a number of tokens");
            Check.True(declared[set.KeepAlive].Shape is { Kind: SettingKind.Whole, Unit: "min", Min: 0 }, $"{set.KeepAlive} is minutes");
            Check.True(declared[set.Temperature].Shape is { Kind: SettingKind.Number, Min: 0, Max: 2, Step: 0.05 }, $"{set.Temperature} is a number with a range and a step");
            Check.True(declared[set.MaxTokens].Shape is { Kind: SettingKind.Whole, Unit: "tokens", Min: 1 }, $"{set.MaxTokens} is a number of tokens");
        }

        Check.True(declared["completionDelayMs"].Shape is { Kind: SettingKind.Whole, Unit: "ms" }, "the completion delay is in ms");
        Check.True(SettingSections.All.Any(s => s.Id == SettingSections.Chat), "the Chat section is on the page");
        Check.Equal("craft.settings.agent", SettingSections.CommandOf(SettingSections.Agent), "each section has a command a plug-in links to");
    }
}

/// <summary>Note 25 at the composer: each chip shows and writes its own role's model and device; the status bar's tooltip each role's.</summary>
[TestGroup("Ui")]
public static class ModelRoleChipTests
{
    [Test]
    public static void EachChipWritesItsOwnRoleAndTheTooltipShowsEveryRole()
    {
        var settings = new AiSettings { Model = "Qwen/Qwen2.5-Coder-7B-Instruct", Device = "cuda:0" };
        using var provider = new LocalModelProvider { SettingsSource = () => settings };
        var status = provider.Status;
        Check.True(status.RoleOf(ModelRoleKeys.Agent) is { Key: ModelRoleKeys.Agent, InheritsModel: true, Model: "Qwen/Qwen2.5-Coder-7B-Instruct" },
            "the agent's role takes the chat's model");
        Check.Equal("same as chat: Qwen/Qwen2.5-Coder-7B-Instruct · cuda:0", ShellText.ModelChip(status, ModelRoleKeys.Agent), "the agent's chip says it is the chat's");
        Check.Equal("Qwen/Qwen2.5-Coder-7B-Instruct · cuda:0", ShellText.ModelChip(status, ModelRoleKeys.Chat), "the chat's chip");

        var agentPicks = ShellText.ModelPicks(status, ModelRoleKeys.Agent);
        Check.True(agentPicks[0] is { Kind: ModelPickKind.Model, Value: "", Label: "✓ Same as chat" }, $"the agent's picker starts with 'Same as chat', marked ({agentPicks[0].Label})");
        var pickModel = new ModelPick(ModelPickKind.Model, "Qwen/Qwen2.5-Coder-1.5B-Instruct", "");
        Check.True(ShellText.WriteOf(status, ModelRoleKeys.Agent, pickModel) is { Variable: "CRAFT_PLUGIN_AI_AGENTMODEL", Value: "Qwen/Qwen2.5-Coder-1.5B-Instruct", LoadCommand: AiPlugin.LoadAgentModelCommand },
            "the agent's chip writes the agent's model and loads the agent's");
        Check.True(ShellText.WriteOf(status, ModelRoleKeys.Chat, pickModel) is { Variable: "CRAFT_PLUGIN_AI_MODEL", LoadCommand: AiPlugin.LoadModelCommand },
            "the chat's chip writes the chat's model (the old variable)");
        Check.True(ShellText.WriteOf(status, ModelRoleKeys.Agent, agentPicks[0]) is { Variable: "CRAFT_PLUGIN_AI_AGENTMODEL", Value: null }, "'Same as chat' clears the agent's model");
        var auto = new ModelPick(ModelPickKind.Device, "auto", "");
        Check.True(ShellText.WriteOf(status, ModelRoleKeys.Agent, auto) is { Variable: "CRAFT_PLUGIN_AI_AGENTDEVICE", Value: "auto" }, "the agent's auto is written (empty is the chat's)");
        Check.True(ShellText.WriteOf(status, ModelRoleKeys.Chat, auto) is { Variable: "CRAFT_PLUGIN_AI_DEVICE", Value: null }, "the chat's auto is its default");

        settings = settings with { AgentModel = "small-agent", AgentDevice = "cpu" };
        status = provider.Status;
        Check.Equal("small-agent · cpu", ShellText.ModelChip(status, ModelRoleKeys.Agent), "the agent's own model and device");
        Check.Equal("Qwen/Qwen2.5-Coder-7B-Instruct · cuda:0", ShellText.ModelChip(status, ModelRoleKeys.Chat), "the chat's chip is unchanged");

        string tooltip = ShellText.ModelRolesTooltip(new ModelProviderStatus
        {
            Roles =
            [
                new ModelRole("Chat", "big", "loaded") { Key = ModelRoleKeys.Chat, Device = "cuda:0" },
                new ModelRole("Agent", "big", "loaded") { Key = ModelRoleKeys.Agent, Device = "cuda:0", Inherits = "Chat", InheritsModel = true, InheritsDevice = true },
                new ModelRole("Inline completion", "small", "loaded") { Key = ModelRoleKeys.Completion, Device = "cpu" },
            ],
        });
        var lines = tooltip.Split(Environment.NewLine);
        Check.True(lines.Length == 4 && lines[0] == "Chat: big on cuda:0 · loaded" && lines[1].StartsWith("Agent: big on cuda:0", StringComparison.Ordinal) && lines[2] == "Inline completion: small on cpu · loaded",
            $"each loaded role's model and device, a line each ({tooltip})");
    }
}
