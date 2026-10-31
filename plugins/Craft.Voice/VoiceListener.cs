// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Idrak.Audio;
using Idrak.Audio.Abstractions;

namespace Craft.Voice;

/// <summary>What the transcriber heard: its update, when it arrived (a timestamp), and, at a phrase's end, the phrase's sound when kept.</summary>
/// <param name="Transcript">The live transcription's update (Idrak's <c>LiveTranscript</c>).</param>
/// <param name="Arrived">When it arrived (<see cref="Stopwatch.GetTimestamp"/>), for the time to text on screen.</param>
/// <param name="Sound">The phrase's sound (mono, at the input's rate) when it ended and sound is kept; else null.</param>
internal sealed record VoiceHeard(LiveTranscript Transcript, long Arrived, AudioData? Sound);

/// <summary>
/// One opening of the microphone: Idrak's <c>LiveTranscriber</c> over the input as it comes, on the thread pool. Each block
/// passes once through here on its way: its level (the loudest since the last report, every
/// <see cref="VoiceSettings.LevelMilliseconds"/>) and, when sound is kept for spoken code, into a ring of the last window's
/// samples (fixed, so memory does not grow with the phrases). <see cref="Stop"/> ends the input so the last phrase is
/// finished; <see cref="DisposeAsync"/> stops at once. Its callbacks run on the transcriber's task: the session posts them
/// to the UI thread.
/// </summary>
internal sealed class VoiceListener : IAsyncDisposable
{
    private readonly IAudioInput _input;
    private readonly ISpeechRecognizer _recognizer;
    private readonly LiveTranscriptionOptions _options;
    private readonly Action<double> _onLevel;
    private readonly Action<VoiceHeard> _onHeard;
    private readonly Action<Exception?> _onEnded;
    private readonly long _levelTicks;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _abort = new();
    private readonly Lock _ringGate = new();
    private readonly float[]? _ring;
    private float[] _mono = [];
    private long _written;
    private int _rate;
    private double _loudest = double.NegativeInfinity;
    private long _lastLevel;
    private Task _run = Task.CompletedTask;

    /// <param name="input">The sound (a microphone, or a stream in the tests); disposed with the listener.</param>
    /// <param name="recognizer">The recognizer (the session's, kept by <see cref="VoiceModels"/>).</param>
    /// <param name="options">The live transcription's settings.</param>
    /// <param name="keepSeconds">Seconds of sound kept for spoken code (0: none).</param>
    /// <param name="levelMilliseconds">How often the level is reported.</param>
    /// <param name="onLevel">The loudest level since the last report, in dB of full scale.</param>
    /// <param name="onHeard">Each update of the transcription.</param>
    /// <param name="onEnded">The end: null when the sound ended or was stopped, else what failed.</param>
    public VoiceListener(IAudioInput input, ISpeechRecognizer recognizer, LiveTranscriptionOptions options, double keepSeconds, int levelMilliseconds,
        Action<double> onLevel, Action<VoiceHeard> onHeard, Action<Exception?> onEnded)
    {
        _input = input;
        _recognizer = recognizer;
        _options = options;
        _onLevel = onLevel;
        _onHeard = onHeard;
        _onEnded = onEnded;
        _levelTicks = (long)(Stopwatch.Frequency * (levelMilliseconds / 1000.0));
        if (keepSeconds > 0)
        {
            _ring = new float[(int)Math.Ceiling(keepSeconds * input.SampleRate)];
        }
    }

    /// <summary>Where the sound comes from.</summary>
    public string Description => _input.Description;

    /// <summary>The blocks heard so far.</summary>
    public long Blocks { get; private set; }

    /// <summary>Starts listening on the thread pool.</summary>
    public void Start() => _run = Task.Run(RunAsync);

    /// <summary>Ends the input: the transcriber finishes the phrase it holds, then the listener ends.</summary>
    public void Stop() => _ = _stop.CancelAsync();                         // the input's callbacks run off the caller's (UI) thread

    /// <summary>Waits for the end (after <see cref="Stop"/>).</summary>
    public Task Completion => _run;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _abort.CancelAsync().ConfigureAwait(false);
        try
        {
            await _run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopped at once
        }

        _input.Dispose();
        _stop.Dispose();
        _abort.Dispose();
    }

    private async Task RunAsync()
    {
        Exception? failure = null;
        try
        {
            await foreach (var update in LiveTranscriber.TranscribeAsync(_recognizer, Read(), _options, _abort.Token).ConfigureAwait(false))
            {
                long arrived = Stopwatch.GetTimestamp();
                var sound = update.SegmentEnded ? Kept(update.Start, update.End) : null;
                _onHeard(new VoiceHeard(update, arrived, sound));
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // stopped at once
        }
#pragma warning disable CA1031 // The microphone's or the recognizer's failure, whatever it is, is reported to the user.
        catch (Exception error)
#pragma warning restore CA1031
        {
            failure = error;
        }

        _onEnded(failure);
    }

    // The input's blocks until it ends or Stop ends them (the transcriber then finishes its last phrase).
    private async IAsyncEnumerable<AudioData> Read([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var blocks = _input.ReadAsync(_stop.Token).GetAsyncEnumerator(_stop.Token);
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await blocks.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    break;
                }

                if (!more || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var block = blocks.Current;
                Observe(block);
                yield return block;
            }
        }
        finally
        {
            await blocks.DisposeAsync().ConfigureAwait(false);
        }
    }

    // A block's level (one pass over its mono samples) and, when kept, its samples into the ring.
    private void Observe(AudioData block)
    {
        Blocks++;
        int length = block.Length;
        if (length == 0)
        {
            return;
        }

        if (_mono.Length < length)
        {
            _mono = new float[length];
        }

        var mono = _mono.AsSpan(0, length);
        block.ToMono(mono);
        double energy = 0;
        foreach (float x in mono)
        {
            energy += (double)x * x;
        }

        _loudest = Math.Max(_loudest, 10 * Math.Log10(Math.Max(energy / length, 1e-12)));
        long now = Stopwatch.GetTimestamp();
        if (now - _lastLevel >= _levelTicks)
        {
            _lastLevel = now;
            _onLevel(_loudest);
            _loudest = double.NegativeInfinity;
        }

        if (_ring is { } ring)
        {
            lock (_ringGate)
            {
                _rate = block.SampleRate;
                var source = length > ring.Length ? mono[^ring.Length..] : mono;
                int at = (int)((_written + length - source.Length) % ring.Length);
                int first = Math.Min(source.Length, ring.Length - at);
                source[..first].CopyTo(ring.AsSpan(at));
                source[first..].CopyTo(ring);
                _written += length;
            }
        }
    }

    // The kept sound between start and end seconds of the input (what of it the ring still holds), or null.
    private AudioData? Kept(double start, double end)
    {
        if (_ring is not { } ring)
        {
            return null;
        }

        lock (_ringGate)
        {
            if (_rate == 0)
            {
                return null;
            }

            long from = Math.Max((long)(start * _rate), _written - ring.Length);
            long to = Math.Min((long)Math.Ceiling(end * _rate), _written);
            if (to <= from)
            {
                return null;
            }

            var samples = new float[to - from];
            int at = (int)(from % ring.Length);
            int first = Math.Min(samples.Length, ring.Length - at);
            ring.AsSpan(at, first).CopyTo(samples);
            ring.AsSpan(0, samples.Length - first).CopyTo(samples.AsSpan(first));

            return new AudioData(samples, 1, _rate);
        }
    }
}
