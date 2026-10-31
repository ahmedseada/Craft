// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Plugins.Sampler;
using Craft.Plugins.StarCoder2;
using Idrak.Abstraction.Generation;
using Idrak.Generation;
using Idrak.Layers;
using Idrak.Models;
using Idrak.Models.Abstractions;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// Plan phase P3: what the bracket-balanced sampler costs on the completion's hot path, in tokens per second against the
/// library's sampler, cold (the first generation: the vocabulary's bracket table made, methods compiled) and warm (the
/// median of the next runs) apart, on a decoder with a code model's vocabulary width (CRAFT_BENCH_VOCABULARY, 49,152 by
/// default: StarCoder2's) and the StarCoder2 FIM layout: a prompt where nothing is masked (every bracket kind opened deep)
/// and one where tokens are masked every step (no bracket open). The tokens where nothing is masked are checked equal.
/// </summary>
internal static class SamplerBench
{
    public static void Run()
    {
        int vocabulary = int.TryParse(Environment.GetEnvironmentVariable("CRAFT_BENCH_VOCABULARY"), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 300 ? v : 49_152;
        const int Tokens = 128, Warm = 9;
        FillInMiddleFormats.Register(StarCoder2Plugin.FillInMiddle);
        try
        {
            var tokenizer = Tokenizer(vocabulary);
            var spec = new DecoderSpec { Vocabulary = vocabulary, Dim = 64, Layers = 2, Heads = 4, KvHeads = 2, HeadDim = 16, FfDim = 128, MaxPositions = 1024, Rope = new RopeSettings(10000f) };
            var network = spec.Build(options: new DecoderBuildOptions { Device = Idrak.Abstraction.Device.Cpu });
            var options = new GenerationOptions { NumPredict = Tokens, Seed = 5, Temperature = 1.5f, TopK = 0, TopP = 1f, RepeatPenalty = 1f };
            string deep = "<fim_prefix>" + new string('(', 150) + new string('[', 150) + new string('{', 150) + "<fim_suffix><fim_middle>";
            string shallow = "<fim_prefix>int Main() <fim_suffix>\n<fim_middle>";
            var results = new StringBuilder($"Bracket-balanced sampler (P3): {Tokens} tokens a generation, vocabulary {vocabulary}, a {spec.Layers}-layer decoder of width {spec.Dim} on the CPU ({Environment.ProcessorCount} threads)\n");

            // The bracket table of a fresh tokenizer of the same vocabulary: what the first completion of a model pays once.
            long tableStart = Stopwatch.GetTimestamp();
            var table = BracketTable.For(Tokenizer(vocabulary), vocabulary);
            double tableMs = Stopwatch.GetElapsedTime(tableStart).TotalMilliseconds;

            var made = new List<BracketBalancedSampler>();
            var library = new TextGenerator(network, tokenizer, 1024) { KeepCache = false };
            var balanced = new TextGenerator(network, tokenizer, 1024)
            {
                KeepCache = false,
                CreateSampler = request =>
                {
                    var sampler = (BracketBalancedSampler)BracketBalancedSamplerKind.Instance.Create(request);
                    made.Add(sampler);
                    return sampler;
                },
            };

            foreach (var (name, prompt) in new[] { ("nothing masked (every kind open deep)", deep), ("masked every step (nothing open)", shallow) })
            {
                // Cold: each one's first generation of this prompt; warm: the two interleaved, the median of each.
                made.Clear();
                double libraryCold = Rate(library, prompt, options, out var libraryText);
                double cold = Rate(balanced, prompt, options, out var text);
                var libraryRates = new double[Warm];
                var rates = new double[Warm];
                for (int i = 0; i < Warm; i++)
                {
                    libraryRates[i] = Rate(library, prompt, options, out _);
                    rates[i] = Rate(balanced, prompt, options, out _);
                }

                double libraryWarm = Median(libraryRates), warm = Median(rates);
                var last = made[^1];
                results.Append(name).Append('\n')
                    .Append("  library sampler:          cold ").Append(F(libraryCold, 1)).Append(" tok/s, warm ").Append(F(libraryWarm, 1)).Append(" tok/s\n")
                    .Append("  bracket-balanced sampler: cold ").Append(F(cold, 1)).Append(" tok/s, warm ").Append(F(warm, 1)).Append(" tok/s (warm ")
                    .Append(F(100 * (warm - libraryWarm) / libraryWarm, 1)).Append("% against the library's), masked steps ").Append(last.MaskedSteps)
                    .Append(", masked tokens ").Append(last.MaskedTokens).Append('\n');
                if (prompt == deep)
                {
                    results.Append("  tokens equal to the library's: ").Append(text == libraryText ? "yes" : "NO").Append('\n');
                    if (text != libraryText || last.MaskedSteps != 0)
                    {
                        throw new InvalidOperationException("The bracket-balanced sampler changed tokens where it masked nothing.");
                    }
                }
            }

            results.Append("the vocabulary's bracket table, made once per tokenizer (cold): ").Append(F(tableMs, 0)).Append(" ms, ")
                .Append(F(table.Bytes / 1024.0, 0)).Append(" KiB, ").Append(table.Profiles).Append(" distinct token behaviours (")
                .Append("tokens closing a parenthesis in code: ").Append(table.Closers(LexState.Code, 0).Length).Append(")\n");
            Report("sampler.txt", results);
        }
        finally
        {
            FillInMiddleFormats.Unregister(StarCoder2Plugin.FillInMiddleFormat);
        }
    }

    private static double Rate(TextGenerator generator, string prompt, GenerationOptions options, out string text)
    {
        var (answer, _, stats) = generator.Generate(prompt, options);
        text = answer;
        return stats.GeneratedTokens / Math.Max(1e-9, stats.GenerationDuration.TotalSeconds);
    }

    // A byte-level tokenizer (one token per byte) with StarCoder2's tokens, padded to the bench's vocabulary width.
    private static BpeTokenizer Tokenizer(int vocabulary)
    {
        string[] special = ["<|endoftext|>", "<fim_prefix>", "<fim_middle>", "<fim_suffix>", "<fim_pad>", "<repo_name>", "<file_sep>"];
        var printable = Enumerable.Range('!', '~' - '!' + 1).Concat(Enumerable.Range('¡', '¬' - '¡' + 1)).Concat(Enumerable.Range('®', 'ÿ' - '®' + 1)).ToHashSet();
        var vocab = new JsonObject();
        for (int b = 0, n = 0; b < 256; b++)
        {
            vocab[((char)(printable.Contains(b) ? b : 256 + n++)).ToString()] = b;
        }

        var added = new JsonArray();
        for (int i = 0; i < special.Length; i++)
        {
            added.Add((JsonNode)new JsonObject { ["id"] = 256 + i, ["content"] = special[i], ["special"] = true });
        }

        var tokenizer = BpeTokenizer.FromJson(new JsonObject
        {
            ["added_tokens"] = added,
            ["model"] = new JsonObject { ["type"] = "BPE", ["vocab"] = vocab, ["merges"] = new JsonArray() },
            ["pre_tokenizer"] = new JsonObject { ["type"] = "ByteLevel", ["add_prefix_space"] = false, ["use_regex"] = true },
            ["decoder"] = new JsonObject { ["type"] = "ByteLevel" },
        });
        tokenizer.PadVocabulary(vocabulary);
        return tokenizer;
    }
}
