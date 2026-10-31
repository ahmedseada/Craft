// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Idrak.Abstraction;
using Idrak.Abstraction.Diagnostics;

namespace Craft.Plugins.Telemetry;

/// <summary>
/// An Idrak telemetry hook at <see cref="TelemetryLevel.Stages"/> (the stages of reading an answer alone, which add no
/// wait for the device): each prompt pass gives the time to the first token, each chunk of decoding the tokens and their
/// time, so the last generation's tokens per second. On the generator's thread it only adds to counters; <see cref="Take"/>
/// (on a timer) turns them into <see cref="ModelMeasures"/>, with the device memory the library holds on that device
/// (Idrak's <see cref="ComputeResources.GetMemoryUsage"/>), only when something new was measured.
/// </summary>
internal sealed class ModelMeasuresHook : ITelemetryHook
{
    private readonly Lock _gate = new();
    private long _tokens;            // decoded since the last prompt pass
    private TimeSpan _decoding;
    private long _total;
    private TimeSpan? _firstToken;
    private string? _device;
    private bool _changed;

    public TelemetryLevel Levels => TelemetryLevel.Stages;

    public void OnInferenceStage(in InferenceStage e)
    {
        lock (_gate)
        {
            switch (e.Stage)
            {
                case InferenceStageKind.Prefill:
                    _firstToken = e.Duration;
                    _tokens = 0;
                    _decoding = TimeSpan.Zero;
                    _device = e.Device;
                    _changed = true;
                    break;
                case InferenceStageKind.DecodeChunk when e.Count > 0:
                    _tokens += e.Count;
                    _total += e.Count;
                    _decoding += e.Duration;
                    _device = e.Device;
                    _changed = true;
                    break;
            }
        }
    }

    /// <summary>The measures since they were last taken, or null when nothing new was measured.</summary>
    public ModelMeasures? Take()
    {
        long tokens, total;
        TimeSpan decoding;
        TimeSpan? first;
        string? device;
        lock (_gate)
        {
            if (!_changed)
            {
                return null;
            }

            _changed = false;
            (tokens, total, decoding, first, device) = (_tokens, _total, _decoding, _firstToken, _device);
        }

        long? inUse = null, peak = null;
        if (device is not null)
        {
            try
            {
                var usage = ComputeResources.GetMemoryUsage(Device.Parse(device));
                (inUse, peak) = (usage.InUse, usage.Peak);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                // A device kind this process does not know: its memory is not shown rather than guessed.
            }
        }

        return new ModelMeasures
        {
            TokensPerSecond = decoding > TimeSpan.Zero && tokens > 0 ? tokens / decoding.TotalSeconds : null,
            FirstToken = first,
            Device = device,
            DeviceMemoryBytes = inUse,
            DeviceMemoryPeakBytes = peak,
            TokensGenerated = total,
        };
    }

    /// <summary>The status bar's text: "38 tok/s · 1.2 GiB cuda".</summary>
    public static string Describe(ModelMeasures measures)
    {
        ArgumentNullException.ThrowIfNull(measures);
        var parts = new List<string>(2);
        if (measures.TokensPerSecond is { } rate)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{rate:F0} tok/s"));
        }

        if (measures.DeviceMemoryBytes is { } bytes)
        {
            parts.Add(Bytes(bytes) + (measures.Device is { } device ? " " + device : ""));
        }

        return string.Join(" · ", parts);
    }

    private static string Bytes(long bytes) => bytes >= 1L << 30
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F1} GiB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):F0} MiB");
}
