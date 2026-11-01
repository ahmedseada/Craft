// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction;
using Idrak.Abstraction.Formats;
using Idrak.Models.Abstractions;

namespace Craft.Ai;

/// <summary>A decoder model's sizes, read from its folder before any weight is (Idrak's config and its architecture's spec).</summary>
/// <param name="Parameters">Its parameters (weights and biases).</param>
/// <param name="Vocabulary">Its vocabulary.</param>
/// <param name="Dim">Its width.</param>
/// <param name="Layers">Its layers.</param>
/// <param name="KvHeads">Its key and value heads.</param>
/// <param name="HeadDim">Its heads' width.</param>
/// <param name="MaxPositions">Its longest context.</param>
/// <param name="TiedEmbeddings">Whether its output head is its token table.</param>
public sealed record ModelShape(long Parameters, int Vocabulary, int Dim, int Layers, int KvHeads, int HeadDim, int MaxPositions, bool TiedEmbeddings);

/// <summary>
/// The weights "auto" choice (the owner, 2026-11-01, "1a": float32 of a 1.5B took 5.9 GB of an 8 GB laptop card and a 7B
/// ran out of memory): before a load, the largest of float32, bfloat16, int8 and int4 whose weights and the role's
/// working memory fit the device's free memory, measured at that moment through Idrak's device memory
/// (<c>Backend.AvailableMemory</c>: the driver's free memory and the pool's reusable blocks, less the device's reserve, so
/// the copies already loaded and the driver's share are counted). The model's size comes from its parameter count and
/// the format's bytes per weight. A named format always wins; nothing fitting is refused before the load with offload
/// named, never a silent fallback. Card-agnostic: no size is assumed, every figure is measured or read from the model.
/// </summary>
public static class WeightsAuto
{
    /// <summary>The setting's value that chooses ("auto").</summary>
    public const string Auto = "auto";

    /// <summary>Full precision ("float32").</summary>
    public const string Float32 = "float32";

    /// <summary>The formats "auto" tries, the largest first.</summary>
    public static IReadOnlyList<string> Formats { get; } = [Float32, "bfloat16", "int8", "int4"];

    /// <summary>
    /// What measures a device's free memory (its name, "auto" for the default device) in place of Idrak's device memory: a
    /// test's fake device. Null (the default): Idrak's.
    /// </summary>
    public static Func<string, long?>? FreeMemoryProbe { get; set; }

    /// <summary>The bytes of memory <paramref name="device"/> ("auto", "cpu", "cuda:0") can give new tensors now, or null when it reports none.</summary>
    public static long? FreeMemory(string device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (FreeMemoryProbe is { } probe)
        {
            return probe(device);
        }

        var found = device.Equals(Auto, StringComparison.OrdinalIgnoreCase) ? Device.Default : Device.Parse(device);
        return found.Backend.AvailableMemory();
    }

    /// <summary>
    /// The sizes of the model in <paramref name="folder"/> (a resolved model's folder or file) from its config through its
    /// architecture's spec, reading no weight; null when its format or architecture is not known here.
    /// </summary>
    public static ModelShape? ShapeOf(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        try
        {
            string prepared = CheckpointFormats.For(folder).Prepare(folder);
            string config = Path.Combine(prepared, "config.json");
            if (!File.Exists(config) || JsonNode.Parse(File.ReadAllText(config)) is not JsonObject json || (string?)json["architectures"]?[0] is not { } name)
            {
                return null;
            }

            var spec = PretrainedArchitectures.Get(name).Spec(json, []);
            return new ModelShape(spec.ParameterCount, spec.Vocabulary, spec.Dim, spec.Layers, spec.KvHeads, spec.HeadDim, spec.MaxPositions, spec.TieEmbeddings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotRegisteredException or System.Text.Json.JsonException
            or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The bytes <paramref name="shape"/>'s weights take in <paramref name="format"/>: float32 4 a weight; bfloat16 2; int8
    /// 1 and its row scales (1/16 more); int4 about 5 bits (Idrak's figure for its 4-bit weights). With int8 and int4 the
    /// projections and the head are packed and the token table stays beside them in bfloat16. A packed format of another
    /// name is counted as int8.
    /// </summary>
    public static long WeightBytes(ModelShape shape, string format)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(format);
        long table = (long)shape.Vocabulary * shape.Dim;
        long head = table;
        long projections = Math.Max(0, shape.Parameters - (shape.TiedEmbeddings ? table : 2 * table));
        return format.ToLowerInvariant() switch
        {
            Float32 or "f32" => shape.Parameters * 4,
            "bfloat16" or "bf16" => shape.Parameters * 2,
            "int4" => (long)((projections + head) * 0.625) + (table * 2),
            _ => (long)((projections + head) * 1.0625) + (table * 2),
        };
    }

    /// <summary>
    /// The room a copy's work needs beside its weights: the decoding caches of <paramref name="context"/> tokens (keys and
    /// values of every layer, float32), a step's scores over the vocabulary, and <paramref name="headroom"/> of the weights' bytes.
    /// </summary>
    public static long WorkingBytes(ModelShape shape, int context, long weightBytes, double headroom)
    {
        ArgumentNullException.ThrowIfNull(shape);
        long caches = 2L * shape.Layers * shape.KvHeads * shape.HeadDim * Math.Max(1, context) * sizeof(float);
        long scores = 2L * shape.Vocabulary * sizeof(float);
        return caches + scores + (long)(Math.Max(0, headroom) * weightBytes);
    }

    /// <summary>
    /// The weights to load <paramref name="model"/> with on <paramref name="device"/>: <paramref name="setting"/> when it
    /// names a format (it always wins), else the largest of <see cref="Formats"/> whose weights and working memory for
    /// <paramref name="context"/> tokens fit <paramref name="free"/> bytes (float32 when the device reports none). When even
    /// int4 does not fit: int4 with offload when <paramref name="offload"/> is on, else a refusal that names offload.
    /// </summary>
    public static ModelMemoryEstimate Choose(string model, string device, ModelShape shape, string setting, int context, long? free, bool offload, double headroom)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(setting);
        ModelMemoryEstimate Of(string format, bool auto)
        {
            long weights = WeightBytes(shape, format);
            return new ModelMemoryEstimate(model, device, format, auto, weights, WorkingBytes(shape, context, weights, headroom), free) { Offload = offload };
        }

        if (!setting.Equals(Auto, StringComparison.OrdinalIgnoreCase) && setting.Length > 0)
        {
            return Of(setting.ToLowerInvariant(), auto: false);
        }

        if (free is null)
        {
            return Of(Float32, auto: true);
        }

        foreach (var format in Formats)
        {
            var estimate = Of(format, auto: true);
            if (estimate.Fits)
            {
                return estimate;
            }
        }

        var smallest = Of(Formats[^1], auto: true);
        return offload ? smallest : smallest with
        {
            Refusal = $"{model} does not fit {device}: even int4 needs {ModelMemoryEstimate.Gigabytes(smallest.NeededBytes)} "
                + $"({ModelMemoryEstimate.Gigabytes(smallest.WeightBytes)} of weights and {ModelMemoryEstimate.Gigabytes(smallest.WorkingBytes)} of working memory for {context} tokens) "
                + $"and {ModelMemoryEstimate.Gigabytes(free.Value)} is free (the copies already loaded and the driver's share counted). Turn on offload "
                + "(CRAFT_PLUGIN_AI_OFFLOAD, or the session's Offload) to keep part of it in system memory, pick a smaller model or context, or free the device.",
        };
    }

    /// <summary>The context "auto" keeps room for: the role's context when set, else the model's own up to <paramref name="autoContext"/>.</summary>
    public static int BudgetContext(ModelShape shape, int roleContext, int autoContext)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return roleContext > 0 ? Math.Min(roleContext, shape.MaxPositions) : Math.Min(Math.Max(256, autoContext), shape.MaxPositions);
    }
}
