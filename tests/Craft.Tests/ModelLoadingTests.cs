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

    private static InlineCompletionRequest Request() => new(new PieceTableBuffer("int x = ").Current, 8, null, "csharp", null);

    /// <summary>A provider that counts its loads (by name), the requests' settings, and the leases given back.</summary>
    private sealed class CountingProvider : IModelProvider
    {
        public ConcurrentQueue<string> Acquired { get; } = new();

        public ConcurrentQueue<ModelRequest> Requests { get; } = new();

        public ConcurrentBag<string> Released { get; } = [];

        public Func<string, Exception?> Fails { get; init; } = _ => null;

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

            string held = $"{request.Model} {request.Device}";
            return Task.FromResult(new ModelLease(request.Model, null, null, 4096, TimeSpan.FromMilliseconds(1), new Release(() => Released.Add(held))));
        }

        private sealed class Release(Action released) : IDisposable
        {
            public void Dispose() => released();
        }
    }
}
