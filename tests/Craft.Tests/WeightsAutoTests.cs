// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Ai;
using Craft.Editor.Shell;
using Craft.Tests.Runner;
using Idrak.Abstraction;

namespace Craft.Tests;

/// <summary>
/// The owner, 2026-11-01 ("1a"): weights "auto" picks the largest of float32, bfloat16, int8 and int4 whose weights and the
/// role's working memory fit the device's free memory measured before the load (a fake device here); a named format wins;
/// nothing fitting is refused before the load with offload named; the choice is said on the chip and the Models page.
/// </summary>
[TestGroup("WeightsAuto")]
public static class WeightsAutoTests
{
    // A 1.5B coder's sizes (its config's: tied embeddings, 28 layers, 2 key and value heads of 128).
    private static readonly ModelShape Coder = new(1_543_714_304, 151_936, 1536, 28, 2, 128, 32768, true);

    [Test]
    public static void AutoPicksTheLargestFormatThatFitsTheMeasuredFreeMemoryAndANamedOneWins()
    {
        long Need(string format) => WeightsAuto.Choose("coder", "cuda:0", Coder, format, 8192, null, false, 0.1).NeededBytes;
        Check.True(Need("float32") > Need("bfloat16") && Need("bfloat16") > Need("int8") && Need("int8") > Need("int4"), "each smaller format needs less");
        Check.True(WeightsAuto.WeightBytes(Coder, "float32") is > 6_000_000_000L and < 6_300_000_000L, $"float32 of a 1.5B is about 6.2 GB, as the owner measured ({WeightsAuto.WeightBytes(Coder, "float32")})");
        foreach (var (free, expected) in (ReadOnlySpan<(long, string)>)[(Need("float32") + 1, "float32"), (Need("float32") - 1, "bfloat16"), (Need("int8"), "int8"), (Need("int8") - 1, "int4")])
        {
            var chosen = WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, free, false, 0.1);
            Check.True(chosen is { Auto: true, Refusal: null, Fits: true } && chosen.Weights == expected, $"{free} bytes free: {expected} ({chosen.Weights})");
        }

        var said = WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, 6_600_000_000, false, 0.1);
        Check.True(said.Text.StartsWith("bfloat16 (auto): ", StringComparison.Ordinal) && said.Text.EndsWith(" of 6.6 GB free", StringComparison.Ordinal), $"said as the chip says it ({said.Text})");

        var none = WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, Need("int4") - 1, false, 0.1);
        Check.True(none is { Weights: "int4", Fits: false } && none.Refusal!.Contains("even int4", StringComparison.Ordinal) && none.Refusal.Contains("offload", StringComparison.Ordinal)
                   && none.Refusal.Contains("CRAFT_PLUGIN_AI_OFFLOAD", StringComparison.Ordinal), $"nothing fits: refused before the load, offload named ({none.Refusal})");
        var offload = WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, Need("int4") - 1, true, 0.1);
        Check.True(offload is { Weights: "int4", Offload: true, Refusal: null } && offload.Text.EndsWith("· offload", StringComparison.Ordinal), "with offload on, int4 loads with offload, said");
        var named = WeightsAuto.Choose("coder", "cuda:0", Coder, "int8", 8192, 1, false, 0.1);
        Check.True(named is { Weights: "int8", Auto: false, Refusal: null }, "a named format always wins");
        Check.True(WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, null, false, 0.1) is { Weights: "float32", Auto: true }, "a device that reports no free memory: float32");
        Check.True(WeightsAuto.BudgetContext(Coder, 0, 8192) == 8192 && WeightsAuto.BudgetContext(Coder, 4096, 8192) == 4096 && WeightsAuto.BudgetContext(Coder, 100_000, 8192) == 32768,
            "the room kept: the role's context, else the model's own up to weightsAutoContext");
        var settings = AiSettings.Read(_ => null);
        Check.True(settings is { Weights: "auto", Offload: false } && settings.ChatRole.Weights == "auto" && settings.AgentRole.Weights == "auto" && settings.CompletionRole.Weights == "auto",
            "auto is the default, and the agent's and completion's empty weights are the chat's");
        Check.True(AiSettings.Read(k => k == "weights" ? "f32" : k == "agentWeights" ? "int4" : null) is { ChatRole.Weights: "float32", AgentRole.Weights: "int4", CompletionRole.Weights: "float32" },
            "a named one is kept per role");
    }

    [Test]
    public static async Task TheProviderChoosesByAFakeDevicesFreeMemoryBeforeTheLoadAndSaysSo()
    {
        using var temp = new TempFolder();
        string folder = LearningTests.TinyChatModel(temp);
        var shape = WeightsAuto.ShapeOf(folder) ?? throw new CheckFailedException("the tiny model's size is not read");
        Check.True(shape is { Layers: 2, KvHeads: 1, HeadDim: 16, Vocabulary: 260 } && shape.Parameters > 0, $"the size is read from the config before any weight ({shape})");
        var before = WeightsAuto.FreeMemoryProbe;
        try
        {
            long free = long.MaxValue / 4;
            WeightsAuto.FreeMemoryProbe = device => device == "cpu" ? free : null;
            var settings = new AiSettings { Model = folder, Device = "cpu", CompletionModel = "", Completion = false, IdrakPlugins = [] };
            using (var provider = new LocalModelProvider { Settings = settings })
            {
                var estimate = await provider.EstimateAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto" });
                Check.True(estimate is { Weights: "float32", Auto: true, Fits: true }, $"plenty free: float32 ({estimate?.Text})");
                using var lease = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto", Context = 0 });
                Check.True(lease.Memory is { Weights: "float32", Auto: true }, $"the lease says what it loaded with ({lease.Memory?.Text})");
                var chat = provider.Status.Roles.Single(r => r.Key == ModelRoleKeys.Chat);
                Check.True(chat.Weights!.StartsWith("float32 (auto): ", StringComparison.Ordinal) && ShellText.ModelChip(provider.Status, ModelRoleKeys.Chat).Contains("float32 (auto)", StringComparison.Ordinal)
                           && ShellText.ModelRolesTooltip(provider.Status).Contains("weights float32 (auto)", StringComparison.Ordinal),
                    $"the chip, its tooltip and the Models page say it ({chat.Weights})");
            }

            // Less free: the measure moves the choice down (int8 fits, bfloat16 does not), the cache named float32 (as before cacheFormat).
            free = WeightsAuto.Choose("tiny", "cpu", shape, "int8", WeightsAuto.BudgetContext(shape, 0, settings.WeightsAutoContext), null, false, settings.WeightsAutoHeadroom).NeededBytes;
            using (var provider = new LocalModelProvider { Settings = settings })
            {
                using var lease = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto", CacheFormat = "float32" });
                Check.True(lease.Memory is { Weights: "int8", Auto: true, Fits: true, CacheFormat: "float32" }, $"{free} bytes free: int8 ({lease.Memory?.Text})");
            }

            // The cache's format loads as chosen, and the copy's caches grow to no more than the estimate counted.
            free = long.MaxValue / 4;
            using (var provider = new LocalModelProvider { Settings = settings })
            {
                using var auto = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto", CacheFormat = "auto", Context = 0 });
                Check.True(auto.Memory is { Weights: "float32", CacheFormat: "float32" } memory && auto.ContextLength == memory.Context && memory.Context == WeightsAuto.BudgetContext(shape, 0, settings.WeightsAutoContext),
                    $"plenty free with cacheFormat auto: float32 and a float32 cache, as before; the context the estimate counted ({auto.Memory?.Text}, context {auto.ContextLength} of {auto.Memory?.Context})");
                using var named = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "float32", CacheFormat = "bfloat16", Context = 128 });
                Check.True(named.Memory is { CacheFormat: "bfloat16", Context: 128 } && named.ContextLength == 128 && named.Memory.Text.Contains(", bfloat16 cache: ", StringComparison.Ordinal),
                    $"a named cache format loads, said beside the weights; a named context is the role's ({named.Memory?.Text}, context {named.ContextLength})");
                Exception? unknown = null;
                try
                {
                    using var never = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "float32", CacheFormat = "no-such-layout" });
                }
#pragma warning disable CA1031 // The failure is the expected outcome, its message checked.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    unknown = error;
                }

                Check.True(unknown is not null && unknown.ToString().Contains("no-such-layout", StringComparison.Ordinal) && unknown.ToString().Contains("KeyValueLayouts", StringComparison.Ordinal),
                    $"an unknown cache format fails loudly, named ({unknown?.Message})");
            }

            free = WeightsAuto.Choose("tiny", "cpu", shape, "int8", WeightsAuto.BudgetContext(shape, 0, settings.WeightsAutoContext), null, false, settings.WeightsAutoHeadroom).NeededBytes;

            // A named format wins over the measure; nothing fitting is refused before the load.
            free = 1;
            using (var provider = new LocalModelProvider { Settings = settings })
            {
                using var lease = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "bfloat16" });
                Check.True(lease.Memory is { Weights: "bfloat16", Auto: false }, $"the named format loads whatever is free ({lease.Memory?.Text})");
            }

            using (var provider = new LocalModelProvider { Settings = settings })
            {
                InvalidOperationException? refused = null;
                try
                {
                    using var never = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto" });
                }
                catch (InvalidOperationException error)
                {
                    refused = error;
                }

                Check.True(refused is not null && refused.Message.Contains("even int4", StringComparison.Ordinal) && refused.Message.Contains("offload", StringComparison.Ordinal) && provider.Loaded.Count == 0,
                    $"said before the load, offload named, nothing loaded ({refused?.Message})");
            }
        }
        finally
        {
            WeightsAuto.FreeMemoryProbe = before;
        }
    }

    // DeepSeek-Coder 6.7B's sizes (32 layers, 32 key and value heads of 128: no grouped-query attention, so a float32 cache
    // takes 1 MiB a token), and Qwen2.5-Coder 7B's (28 layers, 4 key and value heads of 128: grouped-query).
    private static readonly ModelShape DeepSeek = new(6_740_512_768, 32_256, 4096, 32, 32, 128, 16_384, false);
    private static readonly ModelShape Qwen7B = new(7_615_616_512, 152_064, 3584, 28, 4, 128, 32_768, false);

    [Test]
    public static void CacheAutoTakesTheFirstWeightsAndCachePairThatFitsTheFakeDevice()
    {
        Check.Equal(1L << 20, WeightsAuto.CacheBytes(DeepSeek, 1, "float32"), "a float32 cache of DeepSeek 6.7B takes 1 MiB a token");
        Check.Equal(1L << 19, WeightsAuto.CacheBytes(DeepSeek, 1, "bfloat16"), "bfloat16: 2 bytes a value");
        Check.Equal(2L * 32 * 32 * (128 + 4), WeightsAuto.CacheBytes(DeepSeek, 1, "int8"), "int8: a byte a value and a four-byte scale a row");
        Check.Equal(WeightsAuto.WorkingBytes(DeepSeek, 8192, 1000, 0.1), WeightsAuto.WorkingBytes(DeepSeek, 8192, 1000, 0.1, "float32"), "the working memory counts a float32 cache unless told");

        // The owner's run: a 16 GB card with 14.4 GB free. Before cacheFormat (a float32 cache) "auto" took int4; with the pair
        // chosen, int8 weights with a bfloat16 cache (int8 with float32 does not fit, bfloat16 weights do with no cache).
        var fake = WeightsAuto.FreeMemoryProbe;
        try
        {
            WeightsAuto.FreeMemoryProbe = device => device == "cuda:0" ? 14_400_000_000 : null;
            long? free = WeightsAuto.FreeMemory("cuda:0");
            var before = WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "auto", 8192, free, false, 0.1, "float32");
            Check.True(before is { Weights: "int4", CacheFormat: "float32", Fits: true }, $"a float32 cache named: int4, as the run took ({before.Text})");
            var pair = WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "auto", 8192, free, false, 0.1, "auto");
            Check.True(pair is { Weights: "int8", CacheFormat: "bfloat16", Auto: true, Fits: true, Refusal: null, Context: 8192 }, $"cacheFormat auto: int8 weights with a bfloat16 cache ({pair.Text})");
            Check.True(pair.Text.StartsWith("int8 (auto), bfloat16 cache: ", StringComparison.Ordinal) && pair.Text.EndsWith(" of 14.4 GB free", StringComparison.Ordinal), $"the pair is said where the weights are ({pair.Text})");
            Check.True(WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "int8", 8192, free, false, 0.1, "auto") is { Weights: "int8", Auto: false, CacheFormat: "bfloat16" },
                "named weights take the first cache that fits beside them");
            Check.True(WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "auto", 8192, free, false, 0.1, "int8") is { CacheFormat: "int8" }, "a named cache format always wins");
            var none = WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "auto", 8192, 1_000_000_000, false, 0.1, "auto");
            Check.True(none is { Weights: "int4", CacheFormat: "int8", Fits: false } && none.Refusal!.Contains("even int4 with an int8 cache", StringComparison.Ordinal),
                $"nothing fits: the smallest pair is refused, said ({none.Refusal})");
            Check.Throws<NotRegisteredException>(() => WeightsAuto.Choose("deepseek", "cuda:0", DeepSeek, "auto", 8192, free, false, 0.1, "no-such-layout"), "an unknown cache format fails loudly");

            // A grouped-query model that fits with a float32 cache keeps it: the choice is the one it was.
            var qwen = WeightsAuto.Choose("qwen", "cuda:0", Qwen7B, "auto", 8192, 16_000_000_000, false, 0.1, "auto");
            Check.True(qwen is { Weights: "int8", CacheFormat: "float32" } && qwen.Text.StartsWith("int8 (auto): ", StringComparison.Ordinal)
                       && qwen.NeededBytes == WeightsAuto.Choose("qwen", "cuda:0", Qwen7B, "auto", 8192, 16_000_000_000, false, 0.1).NeededBytes,
                $"a GQA 7B on 16 GB: int8 with a float32 cache, as before ({qwen.Text})");
            long all = WeightsAuto.Choose("coder", "cuda:0", Coder, "float32", 8192, null, false, 0.1).NeededBytes;
            Check.True(WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, all, false, 0.1, "auto") is { Weights: "float32", CacheFormat: "float32" },
                "a 1.5B that fits whole stays float32 with a float32 cache");
            Check.True(WeightsAuto.Choose("coder", "cuda:0", Coder, "auto", 8192, null, false, 0.1, "auto") is { Weights: "float32", CacheFormat: "float32" },
                "a device that reports no free memory: float32 and a float32 cache");
        }
        finally
        {
            WeightsAuto.FreeMemoryProbe = fake;
        }

        var settings = AiSettings.Read(_ => null);
        Check.True(settings is { CacheFormat: "auto", AgentCacheFormat: "", CompletionCacheFormat: "" } && settings.ChatRole.CacheFormat == "auto" && settings.AgentRole.CacheFormat == "auto"
                   && settings.CompletionRole.CacheFormat == "auto", "cacheFormat auto is the default; the agent's and completion's empty ones are the chat's");
        Check.True(AiSettings.Read(k => k switch { "cacheFormat" => "BF16", "agentCacheFormat" => "int8", _ => null }) is { ChatRole.CacheFormat: "bfloat16", AgentRole.CacheFormat: "int8", CompletionRole.CacheFormat: "bfloat16" },
            "a named one is kept per role, under one name");
    }

    [Test]
    public static void AnOutOfMemoryFailureNamesTheFailedRolesModelAndTheNextSmallerOfWhatItLoaded()
    {
        var oom = new ResourceLimitExceededException("The device ran out of memory: 117,440,512 more bytes needed, in use 13.40 GiB");
        var settings = new AiSettings { Model = "Qwen/Qwen2.5-Coder-7B-Instruct", AgentModel = "deepseek-ai/deepseek-coder-6.7b-instruct" };
        ModelMemoryEstimate Loaded(string weights, string cache = "float32", int context = 16_384) =>
            new("deepseek-ai/deepseek-coder-6.7b-instruct", "cuda:0", weights, true, 4_000_000_000, 9_000_000_000, 14_400_000_000) { CacheFormat = cache, Context = context };

        string atInt4 = AiErrors.Describe(oom, settings, ModelRoleKeys.Agent, Loaded("int4"));
        Check.True(atInt4.Contains("for deepseek-ai/deepseek-coder-6.7b-instruct (int4 weights, float32 cache, context 16384)", StringComparison.Ordinal)
                   && atInt4.Contains("agentContext 8192", StringComparison.Ordinal) && atInt4.Contains("agentCacheFormat bfloat16", StringComparison.Ordinal)
                   && !atInt4.Contains("Weights", StringComparison.Ordinal) && !atInt4.Contains("int8)", StringComparison.Ordinal),
            $"at int4: the agent's model named, a shorter context and the smaller cache by the agent's variables, no larger weights ({atInt4})");
        string atInt8 = AiErrors.Describe(oom, settings, ModelRoleKeys.Agent, Loaded("int8", "bfloat16"));
        Check.True(atInt8.Contains("agentWeights int4", StringComparison.Ordinal) && atInt8.Contains("(int8 weights, bfloat16 cache", StringComparison.Ordinal), $"int8 loaded: int4 next ({atInt8})");
        Check.True(AiErrors.Describe(oom, settings, ModelRoleKeys.Agent, Loaded("bfloat16")).Contains("agentWeights int8", StringComparison.Ordinal), "bfloat16 loaded: int8 next");
        string lastCache = AiErrors.Describe(oom, settings, ModelRoleKeys.Agent, Loaded("int4", "int8"));
        Check.True(lastCache.Contains("agentContext 8192", StringComparison.Ordinal) && !lastCache.Contains("agentCacheFormat", StringComparison.Ordinal), $"int4 with an int8 cache: only a shorter context ({lastCache})");
        string unmeasured = AiErrors.Describe(oom, settings, ModelRoleKeys.Agent);
        Check.True(unmeasured.Contains("for deepseek-ai/deepseek-coder-6.7b-instruct (float32 weights", StringComparison.Ordinal) && unmeasured.Contains("agentWeights bfloat16", StringComparison.Ordinal),
            $"nothing measured, weights auto: what the load takes then (float32), bfloat16 next ({unmeasured})");
        string chat = AiErrors.Describe(oom, settings);
        Check.True(chat.Contains("for Qwen/Qwen2.5-Coder-7B-Instruct", StringComparison.Ordinal) && chat.Contains("(weights bfloat16)", StringComparison.Ordinal), $"the chat's by default, its own variable ({chat})");
        string completion = AiErrors.Describe(oom, new AiSettings { CompletionModel = "small", CompletionWeights = "int8" }, ModelRoleKeys.Completion);
        Check.True(completion.Contains("set a smaller completionModel", StringComparison.Ordinal) && completion.Contains("completionWeights int4", StringComparison.Ordinal), $"completion's own ({completion})");

        // A load's failure carries what it was loading with (the provider notes the estimate on it in an exception filter).
        Check.True(!AiErrors.Remember(oom, Loaded("int8")), "the note is an exception filter's: it never catches");
        Check.True(AiErrors.Describe(oom, settings, ModelRoleKeys.Agent).Contains("agentWeights int4", StringComparison.Ordinal),
            "an out-of-memory error noted by the load is described by what the load took");
    }
}
