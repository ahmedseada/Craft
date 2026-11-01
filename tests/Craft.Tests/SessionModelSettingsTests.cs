// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// The owner, 2026-11-01 ("2a"): a chat conversation's and an agent session's own model settings, edited from the chip's
/// panel. Sampling reaches only the session's own requests (no reload); Save keeps the shared settings unchanged and Apply
/// writes them (the user's or the workspace's); a load-time change makes a separate lease only after a confirm showing its
/// measured memory, let go when the session ends; a copy that does not fit is said, offload or a smaller format offered.
/// </summary>
[TestGroup("SessionModelSettings")]
public static class SessionModelSettingsTests
{
    private const string ProviderName = "session-fake";

    [Test]
    public static async Task ASessionsSamplingReachesOnlyItsOwnRequestsAndIsKeptWithTheConversation()
    {
        using var temp = new TempFolder();
        using var fixture = new Fixture(temp.Path, keep: true);
        var chat = fixture.Chat;
        Check.True(await chat.SaveModelSessionAsync(new ModelSessionSettings { Temperature = 0.1f, TopK = 5, Seed = 7 }), "sampling only: saved without a question");
        Check.True(fixture.Presenter.Asked.Count == 0 && fixture.Provider.Requests.Count == 0, "no question, no load: sampling needs no reload");
        await fixture.SendAsync("hi");
        var mine = fixture.Provider.ChatOf("auto").Requests[0].Options!;
        Check.True(mine is { Temperature: 0.1f, TopK: 5, Seed: 7, TopP: 0.9f, NumPredict: 333 }, $"its own fields over the shared ones ({mine.Temperature}, {mine.TopK}, {mine.Seed}, {mine.TopP})");
        string first = chat.CurrentConversation!;
        await Until(() => File.Exists(fixture.ChatFile) && File.ReadAllText(fixture.ChatFile).Contains("\"topK\":5", StringComparison.Ordinal), "kept with the conversation in its file");

        chat.NewConversation();
        Check.True(chat.ModelSession.IsEmpty, "a new conversation takes the shared settings");
        await fixture.SendAsync("again");
        var shared = fixture.Provider.ChatOf("auto").Requests[1].Options!;
        Check.True(shared is { Temperature: 0.25f, TopK: 40, Seed: null }, $"another conversation's request: the shared settings ({shared.Temperature}, {shared.TopK}, {shared.Seed})");
        Check.True(fixture.Provider.Requests.Count == 1, "one copy for both: sampling made no second one");

        Check.True(chat.OpenConversation(first) && chat.ModelSession is { Temperature: 0.1f, TopK: 5, Seed: 7 }, "opened again, the conversation has its settings back");
        Check.True(fixture.Agent.ModelSession.IsEmpty, "the agent's session is not touched");
    }

    [Test]
    public static async Task SaveKeepsTheSharedSettingsAndApplyWritesThemToTheUsersOrTheWorkspaces()
    {
        using var fixture = new Fixture(null, keep: false);
        bool closed = false;
        using var panel = new ModelSessionPanel(fixture.Chat, workspaceOpen: true, () => closed = true);
        await panel.ActAsync(ModelSessionPanel.SetPrefix + "temperature", "0.3");
        var slider = panel.Blocks.OfType<PanelSlider>().Single(s => s.ActionId == ModelSessionPanel.SetPrefix + "temperature");
        Check.True(slider is { Value: 0.3, Overridden: true, ResetActionId: ModelSessionPanel.ResetPrefix + "temperature", Detail: "shared: 0.25" }, "the slider moved: its own value, Reset, the shared one beside it");
        Check.True(panel.Blocks.OfType<PanelPicker>().Any(p => p is { Title: "Weights", Drawn: true, Selected: "auto" }) && panel.Blocks.OfType<PanelSwitch>().Any(s => s.Title.StartsWith("Offload", StringComparison.Ordinal)),
            "weights, device and draft as Craft's drop-downs, offload as a switch");
        await panel.ActAsync(ModelSessionPanel.Save, null);
        Check.True(fixture.Chat.ModelSession.Temperature == 0.3f && panel.Note!.Contains("only", StringComparison.Ordinal), $"Save keeps it for this chat ({panel.Note})");
        Check.True(fixture.Written.Count == 0 && fixture.WorkspaceWritten.Count == 0 && fixture.Settings.ChatTemperature == 0.25f, "the shared settings are unchanged");

        await panel.ActAsync(ModelSessionPanel.Apply, null);
        Check.True(fixture.Written.TryGetValue("CRAFT_PLUGIN_AI_CHATTEMPERATURE", out var written) && written == "0.3" && fixture.WorkspaceWritten.Count == 0,
            $"Apply writes it as the user's shared setting ({string.Join(", ", fixture.Written)})");
        Check.True(fixture.Chat.ModelSession.IsEmpty && panel.Draft.IsEmpty && panel.Note!.Contains("your settings", StringComparison.Ordinal), $"and says so; the chat takes the shared settings again ({panel.Note})");

        await panel.ActAsync(ModelSessionPanel.Scope, "workspace");
        await panel.ActAsync(ModelSessionPanel.SetPrefix + "topK", "5");
        await panel.ActAsync(ModelSessionPanel.SetPrefix + "seed", "42");
        await panel.ActAsync(ModelSessionPanel.Apply, null);
        Check.True(fixture.WorkspaceWritten.GetValueOrDefault("CRAFT_PLUGIN_AI_CHATTOPK") == "5" && fixture.WorkspaceWritten.GetValueOrDefault("CRAFT_PLUGIN_AI_CHATSEED") == "42"
                   && panel.Note!.Contains(".craft/settings.json", StringComparison.Ordinal), $"to the workspace's settings, said ({panel.Note})");

        await panel.ActAsync(ModelSessionPanel.SetPrefix + "minP", "0.05");
        await panel.ActAsync(ModelSessionPanel.ResetPrefix + "minP", null);
        Check.True(panel.Draft.MinP is null, "Reset of one field");
        await panel.ActAsync(ModelSessionPanel.SetPrefix + "topP", "0.5");
        await panel.ActAsync(ModelSessionPanel.ResetAll, null);
        Check.True(panel.Draft.IsEmpty, "Reset all");
        await panel.ActAsync(ModelSessionPanel.Close, null);
        Check.True(closed, "Close");
        Check.True(fixture.Agent.Session.Fields(new ModelSessionSettings { TopK = 3, Weights = "int4", Offload = true }).SequenceEqual([("agentTopK", "3"), ("agentWeights", "int4"), ("agentOffload", "true")]),
            "the agent's fields are the agent's settings");
    }

    [Test]
    public static async Task ALoadTimeChangeMakesASeparateLeaseOnlyAfterTheConfirmAndLetsItGoWhenTheSessionEnds()
    {
        using var fixture = new Fixture(null, keep: false);
        var chat = fixture.Chat;
        await fixture.SendAsync("hi");
        Check.True(fixture.Provider.Requests is [{ Weights: "auto" }] && fixture.Models.LeaseCount == 1, "the shared copy");

        fixture.Presenter.Answers.Enqueue(1);                           // Cancel
        Check.True(!await chat.SaveModelSessionAsync(new ModelSessionSettings { Weights = "int8" }), "the user said no");
        var asked = fixture.Presenter.Asked.Single();
        Check.True(asked.Title.Contains("separate copy", StringComparison.Ordinal) && asked.Message.Contains("2.5 GB", StringComparison.Ordinal) && asked.Message.Contains("it fits", StringComparison.Ordinal)
                   && asked.Choices.SequenceEqual(["Load it", "Cancel"]), $"the confirm shows its measured memory and that it fits ({asked.Message})");
        Check.True(chat.ModelSession.IsEmpty && fixture.Provider.Requests.Count == 1, "said no: nothing changed, nothing loaded");

        fixture.Presenter.Answers.Enqueue(0);                           // Load it
        Check.True(await chat.SaveModelSessionAsync(new ModelSessionSettings { Weights = "int8" }), "said yes");
        await Until(() => fixture.Provider.Requests.Count == 2, "its own copy loads");
        Check.True(fixture.Provider.Requests[1] is { Weights: "int8" } && fixture.Models.LeaseCount == 2, "a separate lease: one copy per model, weights, device and offload");
        await fixture.SendAsync("on its own copy");
        Check.True(fixture.Provider.ChatOf("int8").Requests.Count == 1 && fixture.Provider.ChatOf("auto").Requests.Count == 1, "its requests go to its own copy");

        chat.NewConversation();
        await Until(() => fixture.Provider.Released == 1, "the session ended: its copy is let go");
        Check.True(fixture.Models.LeaseCount == 1, "the shared copy stays");
        await fixture.SendAsync("shared again");
        Check.True(fixture.Provider.ChatOf("auto").Requests.Count == 2 && fixture.Provider.Requests.Count == 2, "the other sessions keep theirs: no new load");
    }

    [Test]
    public static async Task ACopyThatDoesNotFitIsSaidWithOffloadAndASmallerFormatOffered()
    {
        using var fixture = new Fixture(null, keep: false);
        fixture.Provider.Free = 1_000_000_000;
        using var panel = new ModelSessionPanel(fixture.Chat, workspaceOpen: false, () => { });
        await panel.ActAsync(ModelSessionPanel.SetPrefix + "weights", "bfloat16");
        Check.True(panel.Blocks.OfType<PanelText>().Any(t => t.Tone == PanelTone.Warning && t.Text.Contains("separate copy", StringComparison.Ordinal)), "the panel says Save loads a separate copy");
        fixture.Presenter.Answers.Enqueue(0);                           // Load with offload
        await panel.ActAsync(ModelSessionPanel.Save, null);
        var asked = fixture.Presenter.Asked.Single();
        Check.True(asked.Title.Contains("does not fit", StringComparison.Ordinal) && asked.Message.Contains("does NOT fit", StringComparison.Ordinal) && asked.Message.Contains("1.0 GB is free", StringComparison.Ordinal)
                   && asked.Choices.SequenceEqual(["Load with offload", "Use int4", "Cancel"]), $"not fitting is said, offload and int4 offered ({asked.Message})");
        Check.True(fixture.Chat.ModelSession is { Weights: "bfloat16", Offload: true }, "offload taken");
        await Until(() => fixture.Provider.Requests.Any(r => r is { Weights: "bfloat16", Offload: true }), "its copy loads with offload");
    }

    private static async Task Until(Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new CheckFailedException($"{what}: not within 10 s");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>A host whose settings writes are recorded, the chat and the agent panels over a fake provider.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly bool _registered;

        public Fixture(string? folder, bool keep)
        {
            ModelProviders.Register(Provider);
            _registered = EditorPlugins.Find(AiSettings.PluginName) is null;
            if (_registered)
            {
                EditorPlugins.Register(AiPlugin.Instance);                     // its settings are written through the registry
            }

            Settings = new AiSettings { ModelProvider = ProviderName, Model = "fake-7b", CompletionModel = "", Completion = false, ChatTemperature = 0.25f, ChatMaxTokens = 333, ChatKeep = keep };
            Models = new AiModels(() => Settings);
            var gate = new Lock();
            Host = new EditorHost(dispatch: action =>
            {
                lock (gate)
                {
                    action();
                }
            }, presenter: Presenter)
            {
                WorkspaceFolder = folder,
                SettingWriter = (variable, value) =>
                {
                    Written[variable] = value;
                    return Task.FromResult<string?>(null);
                },
                WorkspaceSettingWriter = (variable, value) =>
                {
                    WorkspaceWritten[variable] = value;
                    return Task.FromResult<string?>(null);
                },
            };
            Chat = new AiChatPanel(Host, () => Settings, Models);
            Agent = new AiAgentPanel(Host, () => Settings, Models);
            ChatFile = folder is null ? "" : Path.Combine(folder, Settings.ChatFile);
        }

        public SessionProvider Provider { get; } = new();

        public AskingPresenter Presenter { get; } = new();

        public AiSettings Settings { get; }

        public AiModels Models { get; }

        public EditorHost Host { get; }

        public AiChatPanel Chat { get; }

        public AiAgentPanel Agent { get; }

        public string ChatFile { get; }

        public Dictionary<string, string?> Written { get; } = [];

        public Dictionary<string, string?> WorkspaceWritten { get; } = [];

        public async Task SendAsync(string text)
        {
            await Chat.SubmitAsync(text);
            await Chat.LastReply.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Chat.Dispose();
            Agent.Dispose();
            Host.Dispose();
            Models.Dispose();
            ModelProviders.Unregister(ProviderName);
            if (_registered)
            {
                EditorPlugins.Unregister(AiSettings.PluginName);
            }
        }
    }

    /// <summary>A provider of one scripted chat model per weights, counting its loads and the leases let go; its estimate a fixed size against <see cref="Free"/>.</summary>
    internal sealed class SessionProvider : IModelProvider
    {
        private readonly Dictionary<string, FakeChatModel> _chats = [];

        public List<ModelRequest> Requests { get; } = [];

        public int Released;

        public long? Free { get; set; } = 10_000_000_000;

        public string Name => ProviderName;

        public string Summary => "the session settings tests' models";

        public FakeChatModel ChatOf(string weights)
        {
            lock (_chats)
            {
                if (!_chats.TryGetValue(weights, out var chat))
                {
                    chat = FakeChatModel.Script([.. Enumerable.Range(0, 20).Select(i => FakeChatModel.Answer("ok " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
                    _chats[weights] = chat;
                }

                return chat;
            }
        }

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(new ModelLease(request.Model, ChatOf(request.Weights ?? "auto"), null, 4096, TimeSpan.FromMilliseconds(1), new Release(this)));
        }

        public Task<ModelMemoryEstimate?> EstimateAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelMemoryEstimate?>(new ModelMemoryEstimate(request.Model, request.Device ?? "auto", request.Weights ?? "auto", false, 2_000_000_000, 500_000_000, Free));

        private sealed class Release(SessionProvider provider) : IDisposable
        {
            public void Dispose() => Interlocked.Increment(ref provider.Released);
        }
    }

    /// <summary>Answers each question from its queue (else dismisses it), recording them.</summary>
    internal sealed class AskingPresenter : IEditorPresenter
    {
        public Queue<int> Answers { get; } = new();

        public List<EditorQuestion> Asked { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(Answers.TryDequeue(out int answer) ? answer : question.Dismissed);
        }
    }
}
