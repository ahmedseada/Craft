// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json.Nodes;
using Craft.Plugins.StarCoder2;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Models;
using Idrak.Models.Abstractions;

[assembly: IdrakPluginPackage(typeof(StarCoder2Package))]

namespace Craft.Plugins.StarCoder2;

/// <summary>The assembly's plug-in package: what Craft (or the idrak tool's -P) registers when it loads the assembly.</summary>
public sealed class StarCoder2Package : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.StarCoder2";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [StarCoder2Plugin.Plugin];
}

/// <summary>
/// The StarCoder2 family (BigCode; plan phase P2): Hugging Face's <c>Starcoder2ForCausalLM</c> (config.json's
/// "architectures") in <see cref="PretrainedArchitectures"/> (<see cref="Architecture"/>), and its fill-in-the-middle format
/// "starcoder2" in <see cref="FillInMiddleFormats"/> (<see cref="FillInMiddle"/>). GGUF files of the family (llama.cpp's
/// "starcoder2") are not mapped yet.
/// </summary>
public static class StarCoder2Plugin
{
    /// <summary>The architecture name: "Starcoder2ForCausalLM".</summary>
    public const string Starcoder2ForCausalLM = "Starcoder2ForCausalLM";

    /// <summary>Its fill-in-the-middle format's name: "starcoder2".</summary>
    public const string FillInMiddleFormat = "starcoder2";

    /// <summary>
    /// StarCoder2's fill-in-the-middle format, prefix first: <c>&lt;fim_prefix&gt;prefix&lt;fim_suffix&gt;suffix&lt;fim_middle&gt;</c>
    /// (BigCode's own evaluation harness, its StarCoder FIM task), ending on <c>&lt;|endoftext|&gt;</c> or
    /// <c>&lt;file_sep&gt;</c> (the harness's stop words). Repository files are written before it as
    /// <c>&lt;file_sep&gt;path\ntext</c> (StarCoder2's repository format); its <c>&lt;file_sep&gt;</c> token also tells
    /// StarCoder2's tokenizer from StarCoder's (which has <c>&lt;filename&gt;</c>). The repository's name is not written.
    /// </summary>
    public static FillInMiddleLayout FillInMiddle { get; } = FillInMiddleLayout.PrefixSuffixMiddle(FillInMiddleFormat,
        "StarCoder2's <fim_prefix> prefix <fim_suffix> suffix <fim_middle>, repository files after <file_sep>",
        new FillInMiddleTokens("<fim_prefix>", "<fim_suffix>", "<fim_middle>") { FileSeparator = "<file_sep>", Stop = ["<|endoftext|>", "<file_sep>"] });

    /// <summary>How the architecture is read and written.</summary>
    public static IConfigWritingArchitecture Architecture { get; } = new StarCoder2Architecture();

    /// <summary>The plug-in, as <see cref="IdrakPlugins"/> records it ("starcoder2").</summary>
    public static IIdrakPlugin Plugin { get; } = new StarCoder2Registration();

    // The architecture and the fill-in-the-middle format, each with how to take it back.
    private sealed class StarCoder2Registration() : IdrakPlugin("starcoder2")
    {
        protected override void RegisterParts()
        {
            Part(() => PretrainedArchitectures.Register(Starcoder2ForCausalLM, Architecture), () => PretrainedArchitectures.Unregister(Starcoder2ForCausalLM));
            Part(() => FillInMiddleFormats.Register(FillInMiddle), () => FillInMiddleFormats.Unregister(FillInMiddleFormat));
        }
    }
}

/// <summary>
/// <c>Starcoder2ForCausalLM</c>, as transformers' modular_starcoder2.py builds it: layer norms with a bias
/// (<c>norm_epsilon</c>), a feed-forward of <c>c_fc</c> then <c>c_proj</c> without a gate (<c>hidden_act</c>, GELU's tanh
/// form), every projection with a bias when <c>use_bias</c> (the default), grouped-query attention with rotary positions
/// (<c>rope_theta</c>, or <c>rope_parameters.rope_theta</c>), the sliding window in every layer (<c>sliding_window</c>),
/// tied embeddings unless <c>tie_word_embeddings</c> is false. The dropouts are training's and are not applied.
/// </summary>
internal sealed class StarCoder2Architecture : PretrainedArchitecture, IConfigWritingArchitecture
{
    public override DecoderSpec Spec(JsonObject config, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(notes);
        if (config["rope_theta"] is null && config["rope_parameters"] is JsonObject rope && rope["rope_theta"] is { } theta)
        {
            config = (JsonObject)config.DeepClone();
            config["rope_theta"] = theta.DeepClone();
            if (rope["rope_type"] is { } type && (string?)type != "default")
            {
                config["rope_scaling"] = rope.DeepClone();
            }
        }

        var common = PretrainedFamilies.CommonSpec(config, notes);
        bool bias = (bool?)config["use_bias"] ?? true;
        foreach (string dropout in (string[])["embedding_dropout", "residual_dropout", "attention_dropout"])
        {
            if ((double?)config[dropout] is > 0)
            {
                notes.Add($"{dropout} (training only) is not applied.");
            }
        }

        return common with
        {
            Norm = DecoderNorm.Layer,
            NormEpsilon = (float?)config["norm_epsilon"] ?? 1e-5f,
            Gated = false,
            QkvBias = bias,
            OutputBias = bias,
            FeedForwardBias = bias,
            TieEmbeddings = (bool?)config["tie_word_embeddings"] ?? true,
        };
    }

    // Idrak's weight names → transformers' Starcoder2 names.
    public override string? TensorName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        switch (name)
        {
            case "embed.weight":
                return "model.embed_tokens.weight";
            case "norm.weight" or "norm.bias":
                return "model." + name;
        }

        if (name.StartsWith("head.", StringComparison.Ordinal))
        {
            return "lm_head." + name[5..];
        }

        // layers.{i}.attn_norm.{kind}, layers.{i}.mlp_norm.{kind}, layers.{i}.attn.{q,k,v,o}.{kind}, layers.{i}.mlp.{up,down}.{kind}
        var parts = name.Split('.');
        if (parts.Length < 4 || parts[0] != "layers" || parts[^1] is not ("weight" or "bias"))
        {
            return null;
        }

        string layer = $"model.layers.{parts[1]}", kind = parts[^1];
        return parts.Length switch
        {
            4 => parts[2] switch
            {
                "attn_norm" => $"{layer}.input_layernorm.{kind}",
                "mlp_norm" => $"{layer}.post_attention_layernorm.{kind}",
                _ => null,
            },
            5 => (parts[2], parts[3]) switch
            {
                ("attn", "q" or "k" or "v" or "o") => $"{layer}.self_attn.{parts[3]}_proj.{kind}",
                ("mlp", "up") => $"{layer}.mlp.c_fc.{kind}",
                ("mlp", "down") => $"{layer}.mlp.c_proj.{kind}",
                _ => null,
            },
            _ => null,
        };
    }

    public JsonObject Config(DecoderSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Norm != DecoderNorm.Layer || spec.Gated || spec.QkvBias != spec.OutputBias || spec.QkvBias != spec.FeedForwardBias || spec.QkNorm || spec.PostNorms
            || spec.ParallelBlocks || spec.Experts > 0 || spec.LearnedPositions || spec.HeadBias || spec.Rope is null || spec.Rope.Scaling is not null
            || spec.Rope.RotaryDim is not null || spec.SlidingWindowLayers is not null || spec.EmbeddingScale is not null || spec.Activation != FeedForwardActivation.Gelu)
        {
            throw new NotSupportedException("StarCoder2's keys say a layer-normed, ungated GELU decoder with one bias setting for every projection, plain rotary "
                + "positions and an optional window in every layer; this spec says more.");
        }

        var config = new JsonObject
        {
            ["architectures"] = new JsonArray(StarCoder2Plugin.Starcoder2ForCausalLM),
            ["model_type"] = "starcoder2",
            ["vocab_size"] = spec.Vocabulary,
            ["hidden_size"] = spec.Dim,
            ["intermediate_size"] = spec.FfDim,
            ["num_hidden_layers"] = spec.Layers,
            ["num_attention_heads"] = spec.Heads,
            ["num_key_value_heads"] = spec.KvHeads,
            ["head_dim"] = spec.HeadDim,
            ["max_position_embeddings"] = spec.MaxPositions,
            ["norm_epsilon"] = spec.NormEpsilon,
            ["rope_theta"] = spec.Rope.Theta,
            ["hidden_act"] = "gelu_pytorch_tanh",
            ["use_bias"] = spec.QkvBias,
            ["tie_word_embeddings"] = spec.TieEmbeddings,
            ["sliding_window"] = spec.SlidingWindow,
        };
        var again = Spec(config, []);
        return again == spec ? config
            : throw new NotSupportedException($"StarCoder2's keys cannot hold this spec: read back it is {again}, not {spec}.");
    }
}
