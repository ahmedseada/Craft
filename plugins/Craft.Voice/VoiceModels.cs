// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Idrak.Audio;
using Idrak.Audio.Abstractions;
using Idrak.Models;

namespace Craft.Voice;

/// <summary>
/// The speech models of one voice session: the recognizer and the synthesizer, each named by a setting, resolved through
/// Idrak's model sources (<c>ModelSource.Resolve</c>: a folder, store:NAME, a Hugging Face id) and read by the family
/// Idrak.Audio.Plugins registers (<c>SpeechModels.Load</c>, <c>SpeechSynthesisModels.Load</c>) on the device setting, off
/// the UI thread, once; kept while in use and for <see cref="VoiceSettings.KeepAliveMinutes"/> after, then let go.
/// </summary>
internal sealed class VoiceModels : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Lock _loading = new();
    private readonly Timer _idle;
    private ISpeechRecognizer? _recognizer;
    private string? _recognizerKey;
    private ISpeechSynthesizer? _synthesizer;
    private string? _synthesizerKey;
    private int _inUse;
    private bool _disposed;

    public VoiceModels() => _idle = new Timer(_ => Guarded(Unload), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>How a recognizer is made from its name (the tests' fake); null: Idrak's model sources and speech families.</summary>
    internal static Func<string, VoiceSettings, ISpeechRecognizer>? RecognizerLoader { get; set; }

    /// <summary>How a synthesizer is made from its name (the tests' fake); null: Idrak's model sources and synthesis families.</summary>
    internal static Func<string, VoiceSettings, ISpeechSynthesizer>? SynthesizerLoader { get; set; }

    /// <summary>How many times a model was loaded (the tests: once, then shared).</summary>
    public int Loads { get; private set; }

    /// <summary>Whether a recognizer is loaded now.</summary>
    public bool HasRecognizer => Volatile.Read(ref _recognizer) is not null;

    /// <summary>The recognizer of <paramref name="settings"/>, loaded on the thread pool (the load's stage and time to <paramref name="progress"/>); held until <see cref="Release"/>.</summary>
    public Task<ISpeechRecognizer> AcquireRecognizerAsync(VoiceSettings settings, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Acquire(settings.Recognizer, settings, progress, ref _recognizer, ref _recognizerKey,
            static (name, s) => RecognizerLoader is { } load ? load(name, s) : SpeechModels.Load(ModelSource.Resolve(name), new SpeechModelOptions
            {
                Device = DeviceOf(s.ComputeDevice),
                Language = s.Language.Length == 0 ? null : s.Language,
            }), cancellationToken), cancellationToken);

    /// <summary>The synthesizer of <paramref name="settings"/> (installed voices too, through Idrak's <c>VoicedSynthesizer</c>), as <see cref="AcquireRecognizerAsync"/>.</summary>
    public Task<ISpeechSynthesizer> AcquireSynthesizerAsync(VoiceSettings settings, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Acquire(settings.Synthesizer, settings, progress, ref _synthesizer, ref _synthesizerKey,
            static (name, s) => SynthesizerLoader is { } load ? load(name, s)
                : new VoicedSynthesizer(SpeechSynthesisModels.Load(ModelSource.Resolve(name), new SpeechSynthesisLoadOptions { Device = DeviceOf(s.ComputeDevice) })),
            cancellationToken), cancellationToken);

    /// <summary>A use of a model ended: when none is left, the models are let go after the keep-alive (never while 0 minutes).</summary>
    public void Release(VoiceSettings settings)
    {
        lock (_gate)
        {
            if (_inUse > 0 && --_inUse == 0 && settings.KeepAliveMinutes > 0 && !_disposed)
            {
                _idle.Change(TimeSpan.FromMinutes(settings.KeepAliveMinutes), Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _inUse = 0;
        }

        _idle.Dispose();
        _ = Task.Run(Unload, CancellationToken.None);                       // a model's memory is freed off the UI thread
    }

    /// <summary>The device a setting names: "auto" the fastest the library found (the GPU when Idrak.Gpu registered one).</summary>
    internal static Device DeviceOf(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Device.Default : Device.Parse(name.Trim());

    private T Acquire<T>(string name, VoiceSettings settings, IProgress<string>? progress, ref T? held, ref string? heldKey, Func<string, VoiceSettings, T> load,
        CancellationToken cancellationToken) where T : class, IDisposable
    {
        cancellationToken.ThrowIfCancellationRequested();
        VoicePlugin.EnsurePackages();                    // the speech families, registered off the UI thread at activation
        var key = string.Join('\n', name, settings.ComputeDevice, settings.Language);
        // One load at a time (a second request waits for it, then shares it); the counters take their own lock, so the UI
        // thread's Release never waits for a load.
        lock (_loading)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _idle.Change(Timeout.Infinite, Timeout.Infinite);
                _inUse++;
                if (held is not null && heldKey == key)
                {
                    return held;
                }
            }

            try
            {
                var watch = Stopwatch.StartNew();
                using var ticker = progress is null ? null : new Timer(_ => Guarded(() => progress.Report(string.Create(CultureInfo.InvariantCulture,
                    $"loading {name} on {settings.ComputeDevice}, {watch.Elapsed.TotalSeconds:F1} s"))), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
                var model = load(name, settings);
                T? old;
                lock (_gate)
                {
                    old = held;
                    held = model;
                    heldKey = key;
                    Loads++;
                }

                old?.Dispose();
                progress?.Report(string.Create(CultureInfo.InvariantCulture, $"{name} loaded in {watch.Elapsed.TotalSeconds:F1} s"));
                return model;
            }
            catch
            {
                lock (_gate)
                {
                    _inUse--;
                }

                throw;
            }
        }
    }

    // A timer's callback runs on a pool thread, where a throw ends the process (the owner, 2026-11-01: never end the app):
    // what it throws is traced instead (the next load says what is wrong).
    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Nothing may leave a timer's callback.
        catch (Exception error)
#pragma warning restore CA1031
        {
            System.Diagnostics.Trace.TraceError($"Voice: a timer's callback failed: {error}");
        }
    }

    private void Unload()
    {
        ISpeechRecognizer? recognizer;
        ISpeechSynthesizer? synthesizer;
        lock (_gate)
        {
            if (_inUse > 0)
            {
                return;
            }

            (recognizer, _recognizer, _recognizerKey) = (_recognizer, null, null);
            (synthesizer, _synthesizer, _synthesizerKey) = (_synthesizer, null, null);
        }

        recognizer?.Dispose();
        synthesizer?.Dispose();
    }
}
