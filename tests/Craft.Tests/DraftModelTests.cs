// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Editor.Shell;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 11 item 1 in Craft: speculative decoding (Idrak's <see cref="GenerationOptions.Draft"/>) per role. The draft is
/// the completion model's copy unless <c>draftModel</c> names another, reached through the role's lease and loaded only
/// when the role's model loads; a draft of another vocabulary is refused and said; the chip and the status bar show
/// "draft: 1.5B, 72% accepted" from each reply's statistics.
/// </summary>
[TestGroup("DraftModel")]
public static class DraftModelTests
{
    private const string ProviderName = "draft-model-test";

    [Test]
    public static void EachRoleReadsItsDraftAndTheSettingsPageShowsThem()
    {
        var defaults = AiSettings.Read(_ => null);
        Check.True(defaults.ChatRole is { DraftModel: "", DraftTokens: 0, DraftOff: false }, $"the chat drafts with the completion model by default, rounds adapted ({defaults.ChatRole})");
        Check.True(defaults.AgentRole is { DraftModel: "", DraftTokens: 0 }, "the agent's draft is the chat's by default");
        Check.True(defaults.CompletionRole.DraftOff, "completion is the small model: it drafts with none");

        var variables = new Dictionary<string, string>
        {
            ["CRAFT_PLUGIN_AI_DRAFTMODEL"] = "store:coder-1.5B",
            ["CRAFT_PLUGIN_AI_DRAFTTOKENS"] = "6",
        };
        var read = AiSettings.Read(key => variables.GetValueOrDefault(PluginSetting.VariableName(AiSettings.PluginName, key)));
        Check.True(read.ChatRole is { DraftModel: "store:coder-1.5B", DraftTokens: 6 }, $"CRAFT_PLUGIN_AI_DRAFTMODEL and _DRAFTTOKENS are the chat's ({read.ChatRole})");
        Check.True(read.AgentRole is { DraftModel: "store:coder-1.5B", DraftTokens: 6 }, "the agent takes the chat's while its own are empty");

        variables["CRAFT_PLUGIN_AI_AGENTDRAFTMODEL"] = "off";
        variables["CRAFT_PLUGIN_AI_AGENTDRAFTTOKENS"] = "3";
        read = AiSettings.Read(key => variables.GetValueOrDefault(PluginSetting.VariableName(AiSettings.PluginName, key)));
        Check.True(read.AgentRole is { DraftOff: true, DraftTokens: 3 } && read.ChatRole.DraftModel == "store:coder-1.5B",
            $"CRAFT_PLUGIN_AI_AGENTDRAFTMODEL and _AGENTDRAFTTOKENS are the agent's own ({read.AgentRole})");

        bool threw = false;
        try
        {
            _ = AiSettings.Read(key => key == "draftTokens" ? "many" : null);
        }
        catch (ArgumentException error)
        {
            threw = error.Message.Contains("CRAFT_PLUGIN_AI_DRAFTTOKENS", StringComparison.Ordinal);
        }

        Check.True(threw, "a draftTokens that does not parse names its variable");

        var declared = AiSettingShapes.Declared.ToDictionary(s => s.Key, StringComparer.Ordinal);
        foreach (var (key, section) in (ReadOnlySpan<(string, string)>)[("draftModel", SettingSections.Chat), ("draftTokens", SettingSections.Chat),
                     ("agentDraftModel", SettingSections.Agent), ("agentDraftTokens", SettingSections.Agent)])
        {
            Check.True(declared.TryGetValue(key, out var setting) && setting.Shape?.Section == section && setting.Shape.Group == "Speculative decoding",
                $"{key} ({PluginSetting.VariableName(AiSettings.PluginName, key)}) is in the {section} section, under Speculative decoding");
        }

        Check.True(declared["draftModel"].Shape is { Kind: SettingKind.Text }, "the draft model is typed (a name, empty or off)");
        Check.True(declared["draftTokens"].Shape is { Kind: SettingKind.Whole, Unit: "tokens", Min: 0 } && declared["agentDraftTokens"].Shape is { Kind: SettingKind.Whole, Min: 0 },
            "the draft's tokens a round are a number from 0 (adapted)");
    }

    [Test]
    public static async Task TheDraftReachesTheGeneratorThroughTheCompletionCopyLoadedWithTheRole()
    {
        var provider = new DraftProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "small", DraftTokens = 5 };
            using var models = new AiModels(() => settings);
            Check.True(provider.Acquired.IsEmpty, "nothing loads before a role asks");
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { Model: null, State: { } waiting } && waiting.Contains("once the chat model loads", StringComparison.Ordinal),
                $"before: the draft waits for the chat's load ({models.DraftOf(ModelRoleKeys.Chat)})");

            var ready = DraftChanged(models);
            var chat = await models.ChatAsync(CancellationToken.None);
            await ready.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { Model: "small", State: null, Acceptance: null }, $"the completion model drafts for the chat ({models.DraftOf(ModelRoleKeys.Chat)})");

            var final = await chat.Chat!.ChatAsync(new ChatRequest([new ChatMessage("user", "hi")], Options: new GenerationOptions { Temperature = 0.3f }));
            var options = provider.Chats["big"].Options.Last();
            Check.True(options is { DraftTokens: 5, Temperature: 0.3f } && ReferenceEquals(options.Draft, provider.Drafts["small"]),
                $"the request reached the generator with the small copy's draft and draftTokens 5 (draft {options?.Draft}, tokens {options?.DraftTokens})");
            Check.True(final.Stats is { DraftTokens: 100, DraftAccepted: 72 }, "the reply's statistics come back");
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { Acceptance: { } kept } && Math.Abs(kept - 0.72) < 1e-9, $"72% accepted, from the reply's statistics ({models.DraftOf(ModelRoleKeys.Chat)})");

            var completion = await models.CompletionAsync(CancellationToken.None);
            Check.Equal(1, provider.Acquired.Count(m => m == "small"), "completion and the draft share one copy of the small model");
            Check.True(completion is { Model: "small" }, "completion's lease is that copy");

            var agent = await models.AgentAsync(CancellationToken.None);
            Check.True(ReferenceEquals(chat, agent), "the agent inherits the chat's model and draft: the same lease");

            settings = settings with { DraftTokens = 0 };
            var adapted = await models.ChatAsync(CancellationToken.None);
            await adapted.Chat!.ChatAsync(new ChatRequest([new ChatMessage("user", "again")]));
            Check.True(provider.Chats["big"].Options.Last() is { DraftTokens: 0 } again && ReferenceEquals(again.Draft, provider.Drafts["small"]),
                "draftTokens 0 (adapted) reaches the generator with the same copy");
            Check.Equal(1, provider.Acquired.Count(m => m == "small"), "still one copy of the small model");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static async Task AVocabularyThatDiffersIsSaidAndNothingDrafts()
    {
        var provider = new DraftProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "stranger" };
            using var models = new AiModels(() => settings);
            var ready = DraftChanged(models);
            var chat = await models.ChatAsync(CancellationToken.None);
            await ready.WaitAsync(TimeSpan.FromSeconds(20));
            var status = models.DraftOf(ModelRoleKeys.Chat);
            Check.True(status is { Model: null, State: { } why } && why.Contains("stranger cannot draft for big", StringComparison.Ordinal)
                       && why.Contains("vocabulary is not the target's", StringComparison.Ordinal), $"the mismatch is said with Idrak's reason ({status})");
            await chat.Chat!.ChatAsync(new ChatRequest([new ChatMessage("user", "hi")]));
            Check.True(provider.Chats["big"].Options.Last() is { Draft: null }, "the reply goes without a draft");

            settings = settings with { CompletionModel = "small", DraftModel = "other-stranger" };
            ready = DraftChanged(models);
            chat = await models.ChatAsync(CancellationToken.None);
            await ready.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { Model: null, State: { } named } && named.Contains("other-stranger cannot draft", StringComparison.Ordinal),
                $"a draftModel named of another vocabulary is said too ({models.DraftOf(ModelRoleKeys.Chat)})");

            settings = settings with { DraftModel = "off" };
            var before = provider.Acquired.Count;
            chat = await models.ChatAsync(CancellationToken.None);
            await chat.Chat!.ChatAsync(new ChatRequest([new ChatMessage("user", "hi")]));
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { Model: null, State: "off (draftModel)" } && provider.Chats["big"].Options.Last() is { Draft: null },
                $"draftModel off: no draft ({models.DraftOf(ModelRoleKeys.Chat)})");
            Check.Equal(before, provider.Acquired.Count, "and nothing more loads");

            settings = settings with { DraftModel = "", Completion = false };
            Check.True(models.DraftOf(ModelRoleKeys.Chat) is { State: { } off } && off.Contains("inline completion is off", StringComparison.Ordinal),
                "with completion off, its model does not draft (it would load what the user turned off)");
            Check.True(models.DraftOf(ModelRoleKeys.Completion) is null, "completion itself has no draft");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static void TheChipAndTheStatusBarShowTheDraftAndItsAcceptance()
    {
        Check.Equal("1.5B", ShellText.ShortModelName("store:coder-1.5B"), "a size in the name");
        Check.Equal("7B", ShellText.ShortModelName("owner/some-coder-7B-instruct"), "a size before a suffix");
        Check.Equal("coder-q4", ShellText.ShortModelName("store:coder-q4"), "no size: the last part");
        Check.Equal("tiny", ShellText.ShortModelName(@"D:\models\tiny.gguf"), "a file: its name without .gguf");

        var chat = new ModelRole("Chat", "big", "loaded") { Key = ModelRoleKeys.Chat, DeviceChoice = "cpu", Draft = "store:coder-1.5B", DraftAcceptance = 0.724 };
        var agent = new ModelRole("Agent", "big", "loaded") { Key = ModelRoleKeys.Agent, Inherits = "Chat", InheritsModel = true, InheritsDevice = true, DraftState = "off (agentDraftModel)" };
        var status = new ModelProviderStatus { Where = "local", Roles = [chat, agent], DeviceChoice = "cpu" };
        Check.Equal("draft: 1.5B, 72% accepted", ShellText.DraftText(chat), "the draft's text");
        Check.Equal("big · cpu · draft: 1.5B, 72% accepted", ShellText.ModelChip(status, ModelRoleKeys.Chat), "the chat's chip");
        Check.True(ShellText.ModelUsage(status).EndsWith("draft: 1.5B, 72% accepted", StringComparison.Ordinal), $"the status bar ({ShellText.ModelUsage(status)})");
        Check.True(!ShellText.ModelChip(status, ModelRoleKeys.Agent).Contains("draft", StringComparison.Ordinal), "a role without a draft shows none");
        string tooltip = ShellText.ModelRolesTooltip(status);
        Check.True(tooltip.Contains("draft: 1.5B, 72% accepted (store:coder-1.5B)", StringComparison.Ordinal) && tooltip.Contains("no draft: off (agentDraftModel)", StringComparison.Ordinal),
            $"the tooltip names the draft in full and why a role has none: {tooltip}");
        Check.Equal("draft: 1.5B", ShellText.DraftText(chat with { DraftAcceptance = null }), "before a reply measured it");

        var settings = new AiSettings { Model = "big", CompletionModel = "store:coder-1.5B" };
        using var provider = new LocalModelProvider
        {
            SettingsSource = () => settings,
            DraftSource = role => role == ModelRoleKeys.Chat ? new AiModels.DraftStatus("store:coder-1.5B", null, 0.5) : null,
        };
        var shown = provider.Status;
        string device = ShellText.PlannedDevice(shown, ModelRoleKeys.Chat) is { } planned ? " · " + planned : "";
        Check.Equal($"big{device} · draft: 1.5B, 50% accepted", ShellText.ModelChip(shown, ModelRoleKeys.Chat), "the provider's status carries the session's drafts to the chip");
    }

    [Test]
    public static async Task AGatedDraftNeverWaitsForTheCopy()
    {
        var gate = new CopyGate();
        var inner = new FakeDraft(new CharTokenizer("abc"));
        var draft = new GatedDraftModel(inner, gate);
        using (var session = draft.StartDraft([1, 2], new GenerationOptions()))
        {
            Check.Equal(1, inner.Started, "the copy is free: the draft's own session");
        }

        using (await gate.ExclusiveAsync(CancellationToken.None))
        {
            using var session = draft.StartDraft([1, 2], new GenerationOptions());
            Check.Equal(0, session.Propose(new int[4], [], 3, CancellationToken.None), "held alone (a training round): it proposes nothing, at once");
            Check.Equal(1, inner.Started, "and never started the copy's session");
        }

        using var held = draft.StartDraft([1], new GenerationOptions());
        var exclusive = gate.ExclusiveAsync(CancellationToken.None);
        Check.True(!exclusive.IsCompleted, "a drafting session passes the gate as a generation does: an exclusive hold waits for it");
        held.Dispose();
        (await exclusive.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
    }

    private static Task DraftChanged(AiModels models)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            models.DraftChanged -= handler;
            changed.TrySetResult();
        };
        models.DraftChanged += handler;
        return changed.Task;
    }

    // A draft model that only counts its sessions (Craft never drafts itself: Idrak's generator does).
    private sealed class FakeDraft(ITokenizer tokenizer) : IDraftModel
    {
        public int Started;

        public ITokenizer Tokenizer => tokenizer;

        public int Vocabulary => tokenizer.VocabularySize;

        public IDraftSession StartDraft(IReadOnlyList<int> prompt, GenerationOptions options)
        {
            Interlocked.Increment(ref Started);
            return new Session();
        }

        private sealed class Session : IDraftSession
        {
            public int Propose(Span<int> ids, Span<float> probabilities, int width, CancellationToken cancellationToken) => 0;

            public void Accept(int accepted, int next)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    // A chat model that records each request's options and answers with a reply whose statistics say 72 of 100 drafted
    // tokens were kept when a draft came with the request.
    private sealed class RecordingChat : IChatModel
    {
        public ConcurrentQueue<GenerationOptions> Options { get; } = new();

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var options = request.Options ?? new GenerationOptions();
            Options.Enqueue(options);
            var stats = new GenerationStats(3, TimeSpan.FromMilliseconds(1), 120, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(11), 0)
            {
                DraftTokens = options.Draft is null ? 0 : 100,
                DraftAccepted = options.Draft is null ? 0 : 72,
            };
            yield return new ChatChunk(new ChatDelta("ok", "", []));
            yield return new ChatChunk(new ChatDelta("", "", []), true, "stop", new ChatMessage("assistant", "ok"), stats);
        }
    }

    // Models by name, each lending a chat model that records its requests and a draft over its tokenizer: "big" and "small"
    // share a vocabulary, "stranger" and "other-stranger" differ at one id.
    private sealed class DraftProvider : IModelProvider
    {
        private const string Letters = "abcdefghij";

        public ConcurrentQueue<string> Acquired { get; } = new();

        public ConcurrentDictionary<string, RecordingChat> Chats { get; } = new();

        public ConcurrentDictionary<string, IDraftModel> Drafts { get; } = new();

        public string Name => ProviderName;

        public string Summary => "the draft model tests' provider";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            Acquired.Enqueue(request.Model);
            var tokenizer = new CharTokenizer(request.Model.Contains("stranger", StringComparison.Ordinal) ? Letters.Replace('e', 'E') : Letters);
            var chat = Chats.GetOrAdd(request.Model, _ => new RecordingChat());
            var draft = Drafts.GetOrAdd(request.Model, _ => new FakeDraft(tokenizer));
            return Task.FromResult(new ModelLease(request.Model, chat, null, 4096, TimeSpan.FromMilliseconds(1)) { Draft = draft });
        }
    }
}
