// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction;

namespace Craft.Tests;

/// <summary>
/// The owner's Windows run (2026-10-31): the model loads only when asked (the composer's chip, the first message, the
/// load command), never at start-up nor while typing; inline completion uses its own small model (<c>completionModel</c>)
/// or a chat model already loaded, and never retries a failed load on every pause; a role whose settings change lets
/// its old copy go (two copies of a 7B model filled a 16 GB card); an out-of-memory failure says what to do.
/// </summary>
[TestGroup("ModelLoading")]
public static class ModelLoadingTests
{
    private const string ProviderName = "model-loading-test";

    [Test]
    public static async Task NothingLoadsUntilAskedAndCompletionNeverLoadsTheChatModel()
    {
        var provider = new CountingProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "" };
            using var models = new AiModels(() => settings);
            var completion = new AiInlineCompletion(() => settings, models);
            var reasons = new List<string?>();
            completion.Unavailable += (_, reason) => reasons.Add(reason);
            var request = Request();

            Check.True(!completion.Accepts(request), "no completion model of its own and the chat model not loaded: completion does not answer");
            Check.True(!completion.Accepts(request), "asked again");
            Check.Equal(1, reasons.Count, "said once, not on every pause");
            Check.True(reasons[0]!.Contains("big is not loaded", StringComparison.Ordinal), $"it says why ({reasons[0]})");
            Check.True(await models.CompletionAsync(CancellationToken.None) is null, "and leases nothing");
            Check.Equal(0, provider.Acquired.Count, "nothing was loaded by asking");

            var chat = await models.ChatAsync(CancellationToken.None);            // the user's first message (or the chip)
            Check.SequenceEqual(["big"], provider.Acquired.ToList(), "the chat model loads when asked");
            Check.True(completion.Accepts(request) && reasons[^1] is null, "loaded, completion uses it (and says it can again)");
            Check.True(ReferenceEquals(await models.CompletionAsync(CancellationToken.None), chat), "the same lease, no second copy");
            Check.Equal(1, provider.Acquired.Count, "still one load");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static async Task TheSmallCompletionModelLoadsOnItsFirstRequestAndAFailureIsNotRetriedOnEveryPause()
    {
        var provider = new CountingProvider { Fails = name => name == "small" ? new ResourceLimitExceededException("cuda:0 is out of memory: 16,973,824 more bytes needed") : null };
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "small", CompletionLoad = false };
            using var models = new AiModels(() => settings);
            var completion = new AiInlineCompletion(() => settings, models);
            Check.True(!completion.Accepts(Request()) && completion.UnavailableReason!.Contains("completionLoad is off", StringComparison.Ordinal),
                $"completionLoad off: its model is not loaded by it ({completion.UnavailableReason})");
            Check.Equal(0, provider.Acquired.Count, "nothing loaded");

            settings = settings with { CompletionLoad = true };
            Check.True(completion.Accepts(Request()), "completionLoad on: it asks");
            Exception? failed = null;
            try
            {
                _ = await models.CompletionAsync(CancellationToken.None);
            }
            catch (ResourceLimitExceededException error)
            {
                failed = error;
            }

            Check.True(failed is not null, "the small model's load fails (out of memory)");
            Check.SequenceEqual(["small"], provider.Acquired.ToList(), "its own model, never the chat model");
            Check.True(!completion.Accepts(Request()), "after the failure completion stops asking");
            Check.True(completion.UnavailableReason!.Contains("set a smaller completionModel", StringComparison.Ordinal) && completion.UnavailableReason.Contains("close other programs that use the GPU", StringComparison.Ordinal),
                $"and says what to do ({completion.UnavailableReason})");
            for (int pause = 0; pause < 5; pause++)
            {
                _ = completion.Accepts(Request());
                Check.True(await models.CompletionAsync(CancellationToken.None) is null, "no lease on a later pause");
            }

            Check.Equal(1, provider.Acquired.Count, "the failed load is not tried again on every pause in typing");
            Check.True(Environment.GetEnvironmentVariable("IDRAK_OFFLOAD") is null or "", "offloading is never set behind the user's back");

            settings = settings with { CompletionModel = "tiny" };                 // the settings change: it may try again
            Check.True(completion.Accepts(Request()), "a changed completionModel is tried");
            Check.True(await models.CompletionAsync(CancellationToken.None) is { Model: "tiny" }, "and loads");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static async Task ARoleWhoseSettingsChangeLetsItsOldCopyGo()
    {
        var provider = new CountingProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "small", Device = "auto", Weights = "int8" };
            using var models = new AiModels(() => settings);
            var first = await models.ChatAsync(CancellationToken.None);
            _ = await models.CompletionAsync(CancellationToken.None);
            Check.True(provider.Requests.Any(r => r is { Model: "big", Device: "auto", Weights: "int8" }), "the request carries the settings it is kept under");

            settings = settings with { Device = "cpu" };                           // the chip's device pick
            var second = await models.ChatAsync(CancellationToken.None);
            Check.True(!ReferenceEquals(first, second), "another device is another lease");
            Check.True(provider.Released.Contains("big auto"), $"the old copy is given back, not kept beside the new ({string.Join(", ", provider.Released)})");
            Check.True(!provider.Released.Contains("small auto"), "completion's own lease is not touched by the chat model's change");
            Check.Equal(1, provider.Acquired.Count(n => n == "small"), "completion's model loaded once");
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    [Test]
    public static void TheSettingsDeclareTheTwoModelsAndTheLoadOptions()
    {
        var defaults = AiSettings.Read(_ => null);
        Check.True(defaults is { LoadOnStart: false, CompletionLoad: true, CompletionModel: AiSettings.DefaultCompletionModel, Model: "" },
            "the defaults: nothing loads at start, completion has a small model of its own and loads it on its first request");
        var read = AiSettings.Read(key => key switch { "loadOnStart" => "true", "completionLoad" => "false", "completionModel" => " ", _ => null });
        Check.True(read is { LoadOnStart: true, CompletionLoad: false, CompletionModel: "" }, "each is a setting (an empty completionModel: the chat model while loaded)");
        foreach (var key in (string[])["model", "completionModel", "completionLoad", "loadOnStart"])
        {
            Check.True(AiSettings.Declared.Any(s => s.Key == key), $"{key} is declared for the settings page ({PluginSetting.VariableName(AiSettings.PluginName, key)})");
        }

        var oom = new ResourceLimitExceededException("NVIDIA GeForce is out of memory: 16,973,824 more bytes needed");
        string said = AiErrors.Describe(oom, new AiSettings { Model = "Qwen/Qwen2.5-Coder-7B-Instruct", Weights = "int8" });
        Check.True(said.Contains("pick a smaller model at the composer's model chip", StringComparison.Ordinal) && said.Contains("weights int4", StringComparison.Ordinal)
            && said.Contains("close other programs that use the GPU", StringComparison.Ordinal), $"an out-of-memory failure says what to do ({said})");
        Check.Equal("plain", AiErrors.Describe(new InvalidOperationException("plain"), new AiSettings()), "another failure is its own message");
    }

    [Test]
    public static async Task TheAiPluginLoadsNothingWhenItStartsUnlessLoadOnStartIsOn()
    {
        var provider = new CountingProvider();
        ModelProviders.Register(provider);
        bool registered = EditorPlugins.Find(AiSettings.PluginName) is null;
        if (registered)
        {
            EditorPlugins.Register(AiPlugin.Instance);                         // its settings are read through the registry
        }

        try
        {
            foreach (bool onStart in (bool[])[false, true])
            {
                var variables = new Dictionary<string, string>
                {
                    ["CRAFT_PLUGIN_AI_MODEL"] = "big",
                    ["CRAFT_PLUGIN_AI_MODELPROVIDER"] = ProviderName,
                    ["CRAFT_PLUGIN_AI_LOADONSTART"] = onStart ? "true" : "false",
                };
                var ui = new ConcurrentQueue<Action>();
                using var host = new EditorHost(dispatch: ui.Enqueue, readVariable: name => variables.GetValueOrDefault(name));
                int before = provider.Acquired.Count;
                using (AiPlugin.Instance.Activate(host))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < (onStart ? 5000 : 300) && (!onStart || provider.Acquired.Count == before))
                    {
                        while (ui.TryDequeue(out var action))
                        {
                            action();
                        }

                        await Task.Delay(5);
                    }

                    Check.Equal(onStart ? 1 : 0, provider.Acquired.Count - before, onStart ? "loadOnStart on: the chat model loads once at start" : "off (the default): the plug-in's start loads nothing");
                }
            }
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
            if (registered)
            {
                EditorPlugins.Unregister(AiSettings.PluginName);
            }
        }
    }

    [Test]
    public static async Task AnAgentEnginesModelNeverReachesAModelProvider()
    {
        // The owner's run (2026-11-01, callback-20261101-182224-292): the agent's load sent "Claude Code (default)" to the
        // local provider, whose model sources threw DirectoryNotFoundException.
        const string engineModel = "Claude Code (default)";
        var provider = new CountingProvider();
        var engine = new EngineOfModels(engineModel);
        ModelProviders.Register(provider);
        AgentEngines.Register(engine);
        bool registered = EditorPlugins.Find(AiSettings.PluginName) is null;
        if (registered)
        {
            EditorPlugins.Register(AiPlugin.Instance);
        }

        var source = AiPlugin.Local.SettingsSource;
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "big", CompletionModel = "", AgentModel = engineModel };
            using var models = new AiModels(() => settings);
            var refused = await Failure(models.AgentAsync(CancellationToken.None));
            Check.True(refused is InvalidOperationException && refused.Message.Contains("nothing to load", StringComparison.Ordinal) && refused.Message.Contains(engine.Name, StringComparison.Ordinal),
                $"the agent's lease is refused in words: the engine runs it ({refused?.GetType().Name}: {refused?.Message})");
            var session = await Failure(models.SessionAsync(ModelRoleKeys.Agent, "s1", new ModelSessionSettings { Context = 2048 }, CancellationToken.None));
            Check.True(session is InvalidOperationException, $"a session's own copy of it too ({session?.GetType().Name})");
            Check.True(await models.EstimateAsync(ModelRoleKeys.Agent, new ModelSessionSettings { Context = 2048 }, CancellationToken.None) is null, "and no memory is measured for it");
            Check.True(models.DraftOf(ModelRoleKeys.Agent) is null, "nor a draft planned for it");
            Check.Equal(0, provider.Acquired.Count, $"the provider was never asked ({string.Join(", ", provider.Acquired)})");

            var local = await Failure(AiPlugin.Local.AcquireAsync(new ModelRequest(engineModel)));
            Check.True(local is InvalidOperationException && local.Message.Contains("nothing to load", StringComparison.Ordinal),
                $"the local provider refuses it itself (the learning and voice plug-ins acquire through it), never a DirectoryNotFoundException ({local?.GetType().Name}: {local?.Message})");

            AiPlugin.Local.SettingsSource = () => settings;
            var agent = AiPlugin.Local.Status.Roles.Single(r => r.Key == ModelRoleKeys.Agent);
            Check.True(agent.LoadCommand is null, $"the agent's row offers no Load for an engine's model ({agent.LoadCommand})");
            AiPlugin.Local.SettingsSource = () => settings with { AgentModel = "" };
            Check.Equal(AiPlugin.LoadAgentModelCommand, AiPlugin.Local.Status.Roles.Single(r => r.Key == ModelRoleKeys.Agent).LoadCommand, "a local agent model keeps its Load");

            // The load command (the chip, the palette) through the plug-in's session: said, nothing loaded.
            var variables = new Dictionary<string, string>
            {
                ["CRAFT_PLUGIN_AI_MODEL"] = "big",
                ["CRAFT_PLUGIN_AI_MODELPROVIDER"] = ProviderName,
                ["CRAFT_PLUGIN_AI_AGENTMODEL"] = engineModel,
                ["CRAFT_PLUGIN_AI_COMPLETIONMODEL"] = " ",
            };
            using var host = new EditorHost(dispatch: _ => { }, readVariable: name => variables.GetValueOrDefault(name));
            using (AiPlugin.Instance.Activate(host))
            {
                await AiPlugin.Current!.LoadModelAsync(ModelRoleKeys.Agent, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            }

            Check.Equal(0, provider.Acquired.Count, "the agent's load command asked the provider nothing");
        }
        finally
        {
            AiPlugin.Local.SettingsSource = source;
            AgentEngines.Unregister(engine.Name);
            ModelProviders.Unregister(ProviderName);
            if (registered)
            {
                EditorPlugins.Unregister(AiSettings.PluginName);
            }
        }
    }

    [Test]
    public static async Task AFailedLoadLeavesNoTaskNobodyObserved()
    {
        // The owner's run: a failed load came back later as "a background task nobody awaited". Craft's own tasks of a load
        // (the load, the lease, the waiter that stopped waiting) are all observed, whoever was waiting.
        string marker = "test load failed " + Guid.NewGuid().ToString("N");
        var unobserved = new ConcurrentQueue<string>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Any(x => x.GetBaseException().Message == marker))
            {
                unobserved.Enqueue(e.Exception.GetBaseException().StackTrace ?? "");
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await FailTwice(marker);
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Check.Equal(0, unobserved.Count, $"no task of a failed load is left unobserved ({string.Join(" | ", unobserved)})");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    // A load that fails while its caller waits, and one that fails after its only waiter stopped waiting (its token
    // cancelled first); nothing of either is kept by the caller.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task FailTwice(string marker)
    {
        var late = new TaskCompletionSource<ModelLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CountingProvider
        {
            Fails = name => name == "now" ? new ResourceLimitExceededException(marker) : null,
            Pending = name => name == "late" ? late.Task : null,
        };
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ProviderName, Model = "now", CompletionModel = "" };
            using var models = new AiModels(() => settings);
            Check.True(await Failure(models.ChatAsync(CancellationToken.None)) is ResourceLimitExceededException, "the load fails while waited for");

            settings = settings with { Model = "late" };
            using var stop = new CancellationTokenSource();
            var waiting = models.ChatAsync(stop.Token);
            await stop.CancelAsync();
            Check.True(await Failure(waiting) is OperationCanceledException, "the waiter stops waiting");
            late.SetException(new ResourceLimitExceededException(marker));     // then the load fails, nobody waiting
            await Task.Delay(50);
        }
        finally
        {
            ModelProviders.Unregister(ProviderName);
        }
    }

    private static async Task<Exception?> Failure(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(30));
            return null;
        }
#pragma warning disable CA1031 // The test reads what failed.
        catch (Exception error)
#pragma warning restore CA1031
        {
            return error;
        }
    }

    private sealed class EngineOfModels(params string[] models) : IAgentEngine
    {
        public string Name => "test-loading-engine";

        public string Summary => "an agent engine's models";

        public IReadOnlyList<string> Models { get; } = models;

        public Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentEngineResult("", null));
    }

    private static InlineCompletionRequest Request() => new(new PieceTableBuffer("int x = ").Current, 8, null, "csharp", null);

    /// <summary>A provider that counts its loads (by name), the requests' settings, and the leases given back.</summary>
    private sealed class CountingProvider : IModelProvider
    {
        public ConcurrentQueue<string> Acquired { get; } = new();

        public ConcurrentQueue<ModelRequest> Requests { get; } = new();

        public ConcurrentBag<string> Released { get; } = [];

        public Func<string, Exception?> Fails { get; init; } = _ => null;

        public Func<string, Task<ModelLease>?> Pending { get; init; } = _ => null;

        public string Name => ProviderName;

        public string Summary => "the model loading tests' provider";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            Acquired.Enqueue(request.Model);
            Requests.Enqueue(request);
            if (Fails(request.Model) is { } error)
            {
                return Task.FromException<ModelLease>(error);
            }

            if (Pending(request.Model) is { } pending)
            {
                return pending;
            }

            string held = $"{request.Model} {request.Device}";
            return Task.FromResult(new ModelLease(request.Model, null, null, 4096, TimeSpan.FromMilliseconds(1), new Release(() => Released.Add(held))));
        }

        private sealed class Release(Action released) : IDisposable
        {
            public void Dispose() => released();
        }
    }
}
