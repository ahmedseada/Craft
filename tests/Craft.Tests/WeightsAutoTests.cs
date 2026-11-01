// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Ai;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

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

            // Less free: the measure moves the choice down (int8 fits, bfloat16 does not).
            free = WeightsAuto.Choose("tiny", "cpu", shape, "int8", WeightsAuto.BudgetContext(shape, 0, settings.WeightsAutoContext), null, false, settings.WeightsAutoHeadroom).NeededBytes;
            using (var provider = new LocalModelProvider { Settings = settings })
            {
                using var lease = await provider.AcquireAsync(new ModelRequest(folder) { Device = "cpu", Weights = "auto" });
                Check.True(lease.Memory is { Weights: "int8", Auto: true, Fits: true }, $"{free} bytes free: int8 ({lease.Memory?.Text})");
            }

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
}
