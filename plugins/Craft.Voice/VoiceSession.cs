// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;
using Idrak.Audio.Abstractions;

namespace Craft.Voice;

/// <summary>What the microphone is open for: dictation and voice commands, or spoken code (every phrase to the chat model).</summary>
public enum VoiceMode
{
    /// <summary>Dictation at the caret and voice commands; a phrase may still start with the code prefix.</summary>
    Dictate,

    /// <summary>Every phrase is spoken code.</summary>
    Code,
}

/// <summary>What became of a phrase.</summary>
public enum VoicePhraseKind
{
    /// <summary>Typed at the caret.</summary>
    Dictated,

    /// <summary>Ran a command.</summary>
    Command,

    /// <summary>Went to the chat model as spoken code; its answer was proposed as a diff.</summary>
    Code,

    /// <summary>Nothing came of it (said why in the status bar).</summary>
    Ignored,
}

/// <summary>A phrase handled: what was heard, what became of it, a line about it, and the time from its end to text on screen (dictation).</summary>
/// <param name="Heard">The phrase as the recognizer wrote it.</param>
/// <param name="Kind">What became of it.</param>
/// <param name="Detail">The text typed, the command run, or why nothing was done.</param>
/// <param name="Milliseconds">From the transcriber's end of the phrase to the text typed (dictation); 0 otherwise.</param>
public sealed record VoicePhrase(string Heard, VoicePhraseKind Kind, string Detail, double Milliseconds);

/// <summary>
/// The voice plug-in in one editor host (on its UI thread): the microphone's status item with its level, the live words
/// (the committed ones, then the open ones in brackets), each phrase dictated, run as a command or sent as spoken code,
/// and read back with any key stopping it. Nothing of the sound or the model runs on the UI thread: only the insert.
/// </summary>
public sealed class VoiceSession : IDisposable
{
    private const string LevelBars = "▁▂▃▄▅▆▇█";

    private readonly Action _closed;
    private readonly VoiceModels _models = new();
    private readonly StatusItem _mic = new("voice.mic", "", StatusAlignment.Right, 20, VoicePlugin.StopCommand);
    private readonly StatusItem _heard = new("voice.heard", "", StatusAlignment.Left, 5);
    private readonly StatusItem _speaking = new("voice.speaking", "", StatusAlignment.Right, 19, VoicePlugin.StopReadingCommand);
    private readonly List<KeyBinding> _bindings = [];
    private readonly Queue<VoiceHeard> _phrases = new();
    private readonly StringBuilder _committed = new();
    private IDisposable? _micShown;
    private IDisposable? _heardShown;
    private IDisposable? _speakingShown;
    private VoiceListener? _listener;
    private VoiceSettings? _listening;
    private VoiceMode _mode;
    private bool _starting;
    private bool _handling;
    private int _levelBar = -1;
    private CancellationTokenSource? _reading;
    private SymbolTable? _symbols;
    private bool _disposed;

    internal VoiceSession(IEditorHost host, Action closed)
    {
        Host = host;
        _closed = closed;
        BindKeys();
    }

    /// <summary>The host it is active in.</summary>
    public IEditorHost Host { get; }

    /// <summary>The settings as the host reads them now.</summary>
    public VoiceSettings Settings => VoiceSettings.From(Host.Settings);

    /// <summary>Whether the microphone is open.</summary>
    public bool IsListening => _listener is not null;

    /// <summary>What it is open for.</summary>
    public VoiceMode Mode => _mode;

    /// <summary>Whether it reads aloud now.</summary>
    public bool IsSpeaking => _reading is not null;

    /// <summary>The phrases handled so far.</summary>
    public int Phrases { get; private set; }

    /// <summary>The last phrase handled, or null.</summary>
    public VoicePhrase? LastPhrase { get; private set; }

    /// <summary>The live words as the status bar shows them ("" when none).</summary>
    public string HeardText => _heard.Text;

    /// <summary>The microphone's status text ("" when closed).</summary>
    public string MicText => _mic.Text;

    /// <summary>Raised on the UI thread after each phrase is handled.</summary>
    public event EventHandler<VoicePhrase>? PhraseHandled;

    /// <summary>Raised on the UI thread when the microphone closed (the sound ended, it was stopped, or it failed).</summary>
    public event EventHandler? ListeningEnded;

    /// <summary>Raised on the UI thread when reading aloud ended (finished, stopped by a key, or failed).</summary>
    public event EventHandler? ReadingEnded;

    /// <summary>Opens the microphone for <paramref name="mode"/>, or closes it when it is open (for any mode).</summary>
    public Task ToggleAsync(VoiceMode mode, CancellationToken cancellationToken)
    {
        if (IsListening || _starting)
        {
            Stop();
            return Task.CompletedTask;
        }

        return StartAsync(mode, cancellationToken);
    }

    /// <summary>Loads the recognizer of the settings (its progress in the status bar), so the first phrase does not wait for it.</summary>
    public async Task LoadRecognizerAsync(CancellationToken cancellationToken)
    {
        if (await RecognizerAsync(cancellationToken).ConfigureAwait(true) is { } settings)
        {
            _models.Release(settings);
        }
    }

    /// <summary>
    /// Opens the microphone (the capture of the settings, its device) for <paramref name="mode"/>: the recognizer loaded
    /// first (once), then Idrak's live transcription over the sound; the status bar shows the microphone open with its level.
    /// </summary>
    public async Task StartAsync(VoiceMode mode, CancellationToken cancellationToken)
    {
        if (IsListening || _starting || _disposed)
        {
            return;
        }

        _starting = true;
        try
        {
            var settings = await RecognizerAsync(cancellationToken).ConfigureAwait(true);
            if (settings is null || _disposed)
            {
                return;
            }

            ISpeechRecognizer recognizer;
            IAudioInput input;
            LiveTranscriptionOptions options;
            try
            {
                recognizer = await _models.AcquireRecognizerAsync(settings, null, cancellationToken).ConfigureAwait(true);
                _models.Release(settings);                     // the extra hold of RecognizerAsync's load
                options = new LiveTranscriptionOptions
                {
                    Transcription = new TranscriptionOptions { Language = settings.Language.Length == 0 ? null : settings.Language },
                    StepSeconds = settings.StepSeconds,
                    WindowSeconds = settings.WindowSeconds,
                    SilenceSeconds = settings.SilenceSeconds,
                    Policy = settings.CommitPolicy,
                    Agreement = settings.Agreement,
                };
                options.Check();
                _ = LiveCommitPolicies.Get(options.Policy);
                // The system's audio library is loaded and the device opened off the UI thread (nothing there waits).
                input = await Task.Run(() => OpenInput(settings), cancellationToken).ConfigureAwait(true);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException or IOException)
            {
                _models.Release(settings);
                Host.Log(EditorLogLevel.Error, "Voice: the microphone did not open.", error);
                await Host.ShowMessageAsync("Voice: the microphone did not open", error.Message).ConfigureAwait(true);
                return;
            }

            double keep = settings.CodeAs == "text" ? 0 : settings.WindowSeconds + (2 * settings.StepSeconds) + 1;
            _mode = mode;
            _listening = settings;
            _committed.Clear();
            var listener = new VoiceListener(input, recognizer, options, keep, settings.LevelMilliseconds,
                level => Host.Dispatch(() => ShowLevel(level)),
                heard => Host.Dispatch(() => OnHeard(heard)),
                failure => Host.Dispatch(() => OnEnded(failure)));
            _listener = listener;
            ShowLevel(double.NegativeInfinity);
            Host.Log(EditorLogLevel.Information, $"Voice: listening to {input.Description} ({(mode == VoiceMode.Code ? "spoken code" : "dictation and commands")}).");
            listener.Start();
        }
        finally
        {
            _starting = false;
        }
    }

    /// <summary>Closes the microphone: the phrase being said is finished and handled, then the status item goes.</summary>
    public void Stop() => _listener?.Stop();

    /// <summary>
    /// Handles one phrase as the microphone's would be: the code prefix (or the code mode) sends it as spoken code, the
    /// dictate prefix types the rest, a phrase naming a command runs it, anything else is dictated at the caret.
    /// <paramref name="sound"/> is the phrase's sound (for a model that hears); <paramref name="arrived"/> when it ended.
    /// </summary>
    public async Task<VoicePhrase> HandlePhraseAsync(string phrase, AudioData? sound = null, long arrived = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (arrived == 0)
        {
            arrived = Stopwatch.GetTimestamp();
        }

        VoicePhrase result;
        var words = SpokenText.Split(phrase);
        try
        {
            var settings = Settings;
            if (words.Count == 0)
            {
                result = new VoicePhrase(phrase, VoicePhraseKind.Ignored, "nothing to type", 0);
            }
            else if (_mode == VoiceMode.Code && IsListening)
            {
                result = await CodeAsync(phrase, phrase, sound, settings, cancellationToken).ConfigureAwait(true);
            }
            else if (settings.CodePrefix.Length > 0 && words[0].Normal == settings.CodePrefix)
            {
                result = await CodeAsync(phrase, After(phrase, words[0]), sound, settings, cancellationToken).ConfigureAwait(true);
            }
            else if (settings.DictatePrefix.Length > 0 && words[0].Normal == settings.DictatePrefix)
            {
                result = Dictate(phrase, words.GetRange(1, words.Count - 1), settings, arrived);
            }
            else if (settings.CommandPrefix.Length == 0 || words[0].Normal == settings.CommandPrefix)
            {
                var said = settings.CommandPrefix.Length == 0 ? words : words.GetRange(1, words.Count - 1);
                if (SpokenText.FindCommand(said, Host.Commands) is { } found)
                {
                    result = await RunAsync(phrase, found, cancellationToken).ConfigureAwait(true);
                }
                else if (settings.CommandPrefix.Length > 0)
                {
                    result = new VoicePhrase(phrase, VoicePhraseKind.Ignored, "no command is called that", 0);
                }
                else
                {
                    result = Dictate(phrase, words, settings, arrived);
                }
            }
            else
            {
                result = Dictate(phrase, words, settings, arrived);
            }
        }
        catch (ArgumentException error)
        {
            result = new VoicePhrase(phrase, VoicePhraseKind.Ignored, error.Message, 0);
        }

        Phrases++;
        LastPhrase = result;
        if (result.Kind != VoicePhraseKind.Dictated)
        {
            ShowHeard(result.Kind switch
            {
                VoicePhraseKind.Command => "Ran: " + result.Detail,
                VoicePhraseKind.Code => "Spoken code: " + result.Detail,
                _ => $"\"{phrase}\": {result.Detail}",
            });
        }

        PhraseHandled?.Invoke(this, result);
        return result;
    }

    /// <summary>Reads the selection aloud (the caret's line when nothing is selected).</summary>
    public Task ReadSelectionAsync(CancellationToken cancellationToken)
    {
        if (Host.ActiveDocument is not { } document)
        {
            return Task.CompletedTask;
        }

        var snapshot = document.Document.Buffer.Current;
        var selection = document.Selection;
        int start = Math.Clamp(selection.Start, 0, snapshot.Length);
        int end = Math.Clamp(selection.End, start, snapshot.Length);
        if (start == end)
        {
            var line = snapshot.GetLine(snapshot.GetLineIndex(start));
            (start, end) = (line.Start, line.End);
        }

        return ReadAloudAsync(snapshot.GetText(start, end - start), "the selection", cancellationToken);
    }

    /// <summary>Reads the problem at the caret aloud (else the next one after it, else the first).</summary>
    public Task ReadProblemAsync(CancellationToken cancellationToken)
    {
        if (Host.ActiveDocument is not { Diagnostics: { Count: > 0 } diagnostics } document)
        {
            return ReadAloudAsync("", "the problem", cancellationToken);
        }

        int caret = document.Selection.Caret;
        EditorDiagnostic? chosen = null;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Start <= caret && caret <= diagnostic.End)
            {
                chosen = diagnostic;
                break;
            }

            if (chosen is null && diagnostic.Start > caret)
            {
                chosen = diagnostic;
            }
        }

        var problem = chosen ?? diagnostics[0];
        return ReadAloudAsync($"{problem.Severity} {problem.Id}: {problem.Message}", "the problem", cancellationToken);
    }

    /// <summary>Reads the hover of the symbol at the caret aloud (the language's semantic session).</summary>
    public async Task ReadHoverAsync(CancellationToken cancellationToken)
    {
        string text = "";
        if (Host.ActiveDocument is { SemanticSession: { } session } document)
        {
            var hover = await session.GetHoverAsync(document.Document.Buffer.Current, document.Selection.Caret, cancellationToken).ConfigureAwait(true);
            text = hover?.Text ?? "";
        }

        await ReadAloudAsync(text, "the hover", cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Reads the last chat reply aloud (the last answer of a conversation panel, the AI plug-in's chat).</summary>
    public Task ReadReplyAsync(CancellationToken cancellationToken)
    {
        string text = "";
        foreach (var panel in Host.Panels)
        {
            if (panel is IConversationPanel conversation)
            {
                for (int i = conversation.Entries.Count - 1; i >= 0; i--)
                {
                    if (conversation.Entries[i] is { Role: ConversationRole.Assistant, Text.Length: > 0 } entry)
                    {
                        text = entry.Text;
                        break;
                    }
                }
            }
        }

        return ReadAloudAsync(text, "the last reply", cancellationToken);
    }

    /// <summary>
    /// Speaks <paramref name="text"/> through the synthesizer of the settings and the playback (off the UI thread, block by
    /// block); any key, <see cref="StopReading"/> or another reading stops it.
    /// </summary>
    public async Task ReadAloudAsync(string text, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        StopReading();
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowHeard($"Nothing to read: {what} is empty.");
            return;
        }

        VoiceSettings settings;
        try
        {
            settings = Settings;
        }
        catch (ArgumentException error)
        {
            await Host.ShowMessageAsync("Voice: a setting does not read", error.Message).ConfigureAwait(true);
            return;
        }

        if (settings.Synthesizer.Length == 0 && VoiceModels.SynthesizerLoader is null)
        {
            await Host.ShowMessageAsync("Voice: no synthesizer",
                "Set the synthesizer (CRAFT_PLUGIN_VOICE_SYNTHESIZER) on the settings page: a speech model name (a folder, store:NAME, a Hugging Face id) of a family Idrak.Audio.Plugins registers.").ConfigureAwait(true);
            return;
        }

        var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reading = reading;
        Host.KeyPressed += OnKeyWhileReading;
        _speaking.Text = $"Speaking {what} · any key stops";
        _speakingShown ??= Host.AddStatusItem(_speaking);
        IAudioOutput? output = null;
        bool acquired = false;
        try
        {
            var synthesizer = await WithProgressAsync($"Voice: loading {settings.Synthesizer}", report => _models.AcquireSynthesizerAsync(settings, report, reading.Token)).ConfigureAwait(true);
            acquired = true;
            // The playback is found and its device opened off the UI thread, as the speech is made and played.
            output = await Task.Run(() =>
            {
                var playback = settings.Playback.Equals("auto", StringComparison.OrdinalIgnoreCase) ? AudioPlaybacks.ForThisMachine() : AudioPlaybacks.Get(settings.Playback);
                return playback.Open(new AudioPlaybackOptions { Device = settings.OutputDevice.Length == 0 ? null : settings.OutputDevice, SampleRate = synthesizer.SampleRate, Channels = 1 });
            }, reading.Token).ConfigureAwait(true);
            var speech = new SpeechSynthesisOptions { Voice = settings.Voice.Length == 0 ? null : settings.Voice };
            var playing = output;
            await Task.Run(async () =>
            {
                await foreach (var block in synthesizer.SpeakStream(text, speech, reading.Token).ConfigureAwait(false))
                {
                    await playing.WriteAsync(block, reading.Token).ConfigureAwait(false);
                }

                await playing.DrainAsync(reading.Token).ConfigureAwait(false);
            }, reading.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
            // stopped: a key, Stop reading, or another reading
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException or IOException)
        {
            Host.Log(EditorLogLevel.Error, "Voice: reading aloud failed.", error);
            await Host.ShowMessageAsync("Voice: reading aloud failed", error.Message).ConfigureAwait(true);
        }
        finally
        {
            if (output is { } opened)
            {
                _ = Task.Run(opened.Dispose, CancellationToken.None);          // the device closes off the UI thread
            }

            if (acquired)
            {
                _models.Release(settings);
            }

            if (ReferenceEquals(_reading, reading))
            {
                _reading = null;
                Host.KeyPressed -= OnKeyWhileReading;
                _speakingShown?.Dispose();
                _speakingShown = null;
            }

            reading.Dispose();
            ReadingEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stops reading aloud (the sound stops within a block).</summary>
    public void StopReading()
    {
        if (_reading is { } reading)
        {
            _reading = null;
            Host.KeyPressed -= OnKeyWhileReading;
            _speakingShown?.Dispose();
            _speakingShown = null;
            _ = reading.CancelAsync();                                     // its callbacks run off the UI thread
        }
    }

    /// <summary>"go to line #": the caret to the start of line <paramref name="number"/> (from 1) of the active document.</summary>
    public static Task GoToLineAsync(IEditorHost host, int number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.ActiveDocument is { } document)
        {
            var snapshot = document.Document.Buffer.Current;
            var line = snapshot.GetLine(Math.Clamp(number - 1, 0, snapshot.LineCount - 1));
            host.Select(document, SelectionState.At(line.Start));
        }

        return Task.CompletedTask;
    }

    /// <summary>The caret to the next (or previous) problem of the active document after (before) it, round the end.</summary>
    public static void GoToProblem(IEditorHost host, bool forward)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.ActiveDocument is not { Diagnostics: { Count: > 0 } diagnostics } document)
        {
            return;
        }

        int caret = document.Selection.Caret;
        EditorDiagnostic target = forward ? diagnostics[0] : diagnostics[^1];
        if (forward)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Start > caret)
                {
                    target = diagnostic;
                    break;
                }
            }
        }
        else
        {
            for (int i = diagnostics.Count - 1; i >= 0; i--)
            {
                if (diagnostics[i].Start < caret)
                {
                    target = diagnostics[i];
                    break;
                }
            }
        }

        host.Select(document, SelectionState.At(target.Start));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopReading();
        if (_listener is { } listener)
        {
            _listener = null;
            _ = Task.Run(() => listener.DisposeAsync().AsTask(), CancellationToken.None);   // the device closes off the UI thread
        }

        foreach (var binding in _bindings)
        {
            if (ReferenceEquals(EditorKeyBindings.Find(binding.Chord), binding))
            {
                EditorKeyBindings.Unregister(binding.Chord);
            }
        }

        _bindings.Clear();
        _micShown?.Dispose();
        _heardShown?.Dispose();
        _speakingShown?.Dispose();
        _models.Dispose();
        _closed();
    }

    // The settings, and the recognizer they name loaded (held once; the caller releases it), its progress in the status bar.
    private async Task<VoiceSettings?> RecognizerAsync(CancellationToken cancellationToken)
    {
        VoiceSettings settings;
        try
        {
            settings = Settings;
        }
        catch (ArgumentException error)
        {
            await Host.ShowMessageAsync("Voice: a setting does not read", error.Message).ConfigureAwait(true);
            return null;
        }

        if (settings.Recognizer.Length == 0 && VoiceModels.RecognizerLoader is null)
        {
            await Host.ShowMessageAsync("Voice: no recognizer",
                "Set the recognizer (CRAFT_PLUGIN_VOICE_RECOGNIZER) on the settings page: a speech model name (a folder, store:NAME, a Hugging Face id such as a Whisper checkpoint) of a family Idrak.Audio.Plugins registers.").ConfigureAwait(true);
            return null;
        }

        try
        {
            await WithProgressAsync($"Voice: loading {settings.Recognizer}", report => _models.AcquireRecognizerAsync(settings, report, cancellationToken)).ConfigureAwait(true);
            return settings;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Host.Log(EditorLogLevel.Error, $"Voice: the recognizer {settings.Recognizer} did not load.", error);
            await Host.ShowMessageAsync("Voice: the recognizer did not load", error.Message).ConfigureAwait(true);
            return null;
        }
    }

    // A load shown in the status bar while it runs (its stage and time, reported from the worker, shown on the UI thread).
    private async Task<T> WithProgressAsync<T>(string title, Func<IProgress<string>, Task<T>> load)
    {
        // Phase U: an activity of its own kind; its stages come from the worker straight to it (any thread).
        using var progress = Host.Activities.Start(title, ActivityKinds.Voice);
        var report = new Progress<string>(message => progress.Report(null, message));
        try
        {
            var loaded = await load(report).ConfigureAwait(true);
            progress.Complete();
            return loaded;
        }
        catch (Exception error)
        {
            progress.Fail(error.Message, error);
            throw;
        }
    }

    private static IAudioInput OpenInput(VoiceSettings settings)
    {
        var capture = settings.Capture.Equals("auto", StringComparison.OrdinalIgnoreCase) ? AudioCaptures.ForThisMachine() : AudioCaptures.Get(settings.Capture);
        if (!capture.IsAvailable(out string reason))
        {
            throw new NotSupportedException($"The {capture.Name} capture cannot record here: {reason}.");
        }

        return capture.Open(new AudioCaptureOptions
        {
            Device = settings.Device.Length == 0 ? null : settings.Device,
            SampleRate = settings.SampleRate,
            Channels = 1,
        });
    }

    // The keys of the settings bound to listening and spoken code (taken back on dispose); a key that does not parse is said in the log.
    private void BindKeys()
    {
        VoiceSettings settings;
        try
        {
            settings = Settings;
        }
        catch (ArgumentException error)
        {
            Host.Log(EditorLogLevel.Warning, "Voice: " + error.Message);
            return;
        }

        foreach (var (text, command) in (ReadOnlySpan<(string, string)>)[(settings.Key, VoicePlugin.ListenCommand), (settings.CodeKey, VoicePlugin.CodeCommand)])
        {
            if (text.Length == 0)
            {
                continue;
            }

            if (!KeyChord.TryParse(text, out var chord, out var problem))
            {
                Host.Log(EditorLogLevel.Warning, $"Voice: the key {problem}; {command} has no key.");
                continue;
            }

            var binding = new KeyBinding(chord, command);
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }
    }

    private void OnHeard(VoiceHeard heard)
    {
        if (_disposed)
        {
            return;
        }

        var update = heard.Transcript;
        if (!update.SegmentEnded)
        {
            if (update.Committed.Length > 0)
            {
                if (_committed.Length > 0)
                {
                    _committed.Append(' ');
                }

                _committed.Append(update.Committed);
            }

            // The committed words are final; the open ones, in brackets, may still change.
            ShowHeard(update.Tentative.Length == 0 ? _committed.ToString() : $"{_committed} [{update.Tentative}…]");
            return;
        }

        _committed.Clear();
        ShowHeard("");
        _phrases.Enqueue(heard);
        if (_listening is { Mode: "push" })
        {
            Stop();                                                        // push to talk: one phrase a press
        }

        _ = HandleQueueAsync();
    }

    // The phrases in the order they were said, one at a time (a spoken-code phrase waits for its model before the next).
    private async Task HandleQueueAsync()
    {
        if (_handling)
        {
            return;
        }

        _handling = true;
        try
        {
            while (_phrases.TryDequeue(out var heard) && !_disposed)
            {
                await HandlePhraseAsync(heard.Transcript.Segment!.Trim(), heard.Sound, heard.Arrived).ConfigureAwait(true);
            }
        }
        finally
        {
            _handling = false;
        }
    }

    private void OnEnded(Exception? failure)
    {
        if (_listener is not { } listener)
        {
            return;
        }

        _listener = null;
        _ = Task.Run(() => listener.DisposeAsync().AsTask(), CancellationToken.None);   // the device closes off the UI thread
        if (_listening is { } settings)
        {
            _models.Release(settings);
        }

        _listening = null;
        _levelBar = -1;
        _micShown?.Dispose();
        _micShown = null;
        _mic.Text = "";
        if (failure is not null)
        {
            Host.Log(EditorLogLevel.Error, "Voice: listening failed.", failure);
            _ = Host.ShowMessageAsync("Voice: listening failed", failure.Message);
        }

        ListeningEnded?.Invoke(this, EventArgs.Empty);
    }

    // The microphone open, with the level of the loudest block since the last report (-60 dB to 0 dB in eight bars).
    private void ShowLevel(double decibels)
    {
        if (_listener is null && !_starting)
        {
            return;
        }

        int bar = double.IsNegativeInfinity(decibels) ? 0 : Math.Clamp((int)((decibels + 60) / 60 * LevelBars.Length), 0, LevelBars.Length - 1);
        if (bar == _levelBar && _micShown is not null)
        {
            return;
        }

        _levelBar = bar;
        _mic.Text = string.Create(CultureInfo.InvariantCulture, $"● Mic{(_mode == VoiceMode.Code ? " · code" : "")} {LevelBars[bar]}");
        _mic.Tooltip = $"The microphone is open ({_listener?.Description}); click or say \"stop listening\" to close it";
        _micShown ??= Host.AddStatusItem(_mic);
    }

    private void ShowHeard(string text)
    {
        if (_heard.Text == text)
        {
            return;
        }

        _heard.Text = text;
        _heard.Tooltip = "Heard: the words in brackets are still open and may change";
        if (text.Length == 0)
        {
            _heardShown?.Dispose();
            _heardShown = null;
        }
        else
        {
            _heardShown ??= Host.AddStatusItem(_heard);
        }
    }

    private void OnKeyWhileReading(object? sender, EditorKeyEventArgs e) => StopReading();

    // The words dictated at the active caret as one undo step; the time from the phrase's end to the text recorded.
    private VoicePhrase Dictate(string phrase, List<SpokenWord> words, VoiceSettings settings, long arrived)
    {
        if (Host.ActiveDocument is not { } document)
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, "no document is open to type into", 0);
        }

        string language = document.Language?.Name ?? "";
        int version = SpokenSymbols.Version;
        if (_symbols is not { } table || table.Key != (language, version))
        {
            table = _symbols = new SymbolTable(SpokenSymbols.For(language)) { Key = (language, version) };
        }

        var snapshot = document.Document.Buffer.Current;
        int at = Math.Clamp(document.Selection.Start, 0, snapshot.Length);
        char? before = at > 0 ? snapshot[at - 1] : null;
        string text = SpokenText.Dictate(words, table, settings.Digits, settings.Punctuation == "heard", before);
        if (text.Length == 0 || !Host.TypeText(document, text))
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, "the editor took no text", 0);
        }

        double ms = Stopwatch.GetElapsedTime(arrived).TotalMilliseconds;
        Host.Record("voice.phraseToText", ms, "ms");
        return new VoicePhrase(phrase, VoicePhraseKind.Dictated, text, ms);
    }

    private async Task<VoicePhrase> RunAsync(string phrase, (SpokenCommand? Spoken, IEditorCommand? Command, int Number) found, CancellationToken cancellationToken)
    {
        if (found.Spoken is { } spoken)
        {
            bool ran = await spoken.RunAsync(Host, found.Number, cancellationToken).ConfigureAwait(true);
            string what = spoken.TakesNumber ? spoken.Phrase.Replace(SpokenCommand.NumberWord, found.Number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) : spoken.Phrase;
            return new VoicePhrase(phrase, ran ? VoicePhraseKind.Command : VoicePhraseKind.Ignored, ran ? what : $"{what} cannot run now", 0);
        }

        var command = found.Command!;
        bool done = await Host.ExecuteCommandAsync(command.Name, cancellationToken).ConfigureAwait(true);
        return new VoicePhrase(phrase, done ? VoicePhraseKind.Command : VoicePhraseKind.Ignored, done ? command.Title : $"{command.Title} cannot run now", 0);
    }

    // Spoken code: the request (as text, or the phrase's sound to a model that hears), the selection and the lines around
    // the caret to the chat model; its first code block proposed over the selection (or at the caret) as a diff.
    private async Task<VoicePhrase> CodeAsync(string phrase, string request, AudioData? sound, VoiceSettings settings, CancellationToken cancellationToken)
    {
        if (Host.ActiveDocument is not { } document)
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, "no document is open for the code", 0);
        }

        string model = settings.CodeModel;
        if (model.Length == 0 && EditorPlugins.Find("ai") is not null)
        {
            try
            {
                model = Host.Settings.Get("ai", "model");
            }
            catch (ArgumentException)
            {
                model = "";
            }
        }

        if (model.Length == 0)
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, "no chat model: set CRAFT_PLUGIN_VOICE_CODEMODEL (or the AI plug-in's model)", 0);
        }

        if (ModelProviders.Find(settings.ModelProvider) is not { } provider)
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored,
                $"no model provider '{settings.ModelProvider}' is registered (registered: {(ModelProviders.Names.Count == 0 ? "none; the AI plug-in brings \"local\"" : string.Join(", ", ModelProviders.Names))})", 0);
        }

        var snapshot = document.Document.Buffer.Current;
        var selection = document.Selection;
        int start = Math.Clamp(selection.Start, 0, snapshot.Length);
        int end = Math.Clamp(selection.End, start, snapshot.Length);
        int caretLine = snapshot.GetLineIndex(start);
        int first = Math.Max(0, caretLine - settings.CodeContextLines);
        int last = Math.Min(snapshot.LineCount - 1, snapshot.GetLineIndex(end) + settings.CodeContextLines);
        int aroundStart = snapshot.GetLine(first).Start;
        string around = snapshot.GetText(aroundStart, snapshot.GetLine(last).EndIncludingBreak - aroundStart);
        string selected = snapshot.GetText(start, end - start);
        string name = document.FilePath is { } path ? Path.GetFileName(path) : "the new file";

        using var progress = Host.Activities.Start($"Voice: asking {model}", ActivityKinds.Generation);
        string reply;
        bool heard;
        string served;
        try
        {
            (reply, heard, served) = await Task.Run(async () =>
            {
                using var lease = await provider.AcquireAsync(new ModelRequest(model), null, cancellationToken).ConfigureAwait(false);
                var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template.");
                bool hears = chat.PartKinds.Contains(ChatParts.Audio);
                if (settings.CodeAs == "sound" && !hears)
                {
                    throw new NotSupportedException($"The model {lease.Model} takes no sound (its part kinds: {string.Join(", ", chat.PartKinds)}); set CRAFT_PLUGIN_VOICE_CODEAS to auto or text.");
                }

                bool asSound = sound is not null && hears && settings.CodeAs != "text";
                string context = string.Create(CultureInfo.InvariantCulture,
                    $"The file {name} ({document.LanguageName}), lines {first + 1}-{last + 1}:\n```\n{around}\n```\n\n")
                    + (selected.Length > 0 ? $"The selection, which the code replaces:\n```\n{selected}\n```\n\n" : "Nothing is selected: the code goes at the caret.\n\n")
                    + (asSound ? "The request is the attached sound." : $"The request: {request}");
                var parts = new List<ChatPart> { new ChatText(context) };
                if (asSound)
                {
                    parts.Add(ChatAudio.FromBytes(Wav(sound!), "audio/wav"));
                }

                var answer = await chat.ChatAsync(new ChatRequest(
                [
                    new ChatMessage("system", CodePrompt),
                    new ChatMessage("user", parts),
                ], Options: new GenerationOptions { NumPredict = settings.CodeMaxTokens, NumCtx = lease.ContextLength }), cancellationToken).ConfigureAwait(false);
                return (answer.Message?.Content ?? "", asSound, lease.Model);
            }, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Host.Log(EditorLogLevel.Error, "Voice: spoken code failed.", error);
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, error.Message, 0);
        }

        string code = FirstBlock(reply);
        if (!ReferenceEquals(document.Document.Buffer.Current, snapshot))
        {
            return new VoicePhrase(phrase, VoicePhraseKind.Ignored, "the document changed while the model answered; say it again", 0);
        }

        var proposal = new ProposedEdits([new TextEdit(start, end - start, code)], "Spoken code")
        {
            Placement = selected.Length > 0 ? "over the selection" : "at the caret",
            Origin = $"Voice · {served}{(heard ? " · heard the sound" : " · the transcript")}",
        };
        bool accepted = await Host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
        return new VoicePhrase(phrase, VoicePhraseKind.Code, accepted ? "accepted" : "not accepted", 0);
    }

    private const string CodePrompt = "You write code for a request the user spoke while editing. Answer with one fenced code block holding only the code "
        + "that replaces the selection (or goes at the caret when nothing is selected), in the file's language and style. No explanation outside the block.";

    /// <summary>The first fenced block's code in <paramref name="reply"/>, or the whole reply when it has none.</summary>
    internal static string FirstBlock(string reply)
    {
        var lines = reply.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int open = Array.FindIndex(lines, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        if (open < 0)
        {
            return reply.Trim('\n');
        }

        var code = new StringBuilder();
        for (int i = open + 1; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            code.Append(lines[i]).Append('\n');
        }

        return code.ToString().TrimEnd('\n');
    }

    // The phrase's sound as a 16-bit WAV (Idrak's "wav" encoder), for a model that hears.
    private static byte[] Wav(AudioData sound)
    {
        using var stream = new MemoryStream();
        AudioEncoders.Get("wav").Encode(sound, stream);
        return stream.ToArray();
    }

    // The words of the phrase after its first word (the prefix), as heard.
    private static string After(string phrase, SpokenWord first)
    {
        int at = phrase.IndexOf(first.Heard, StringComparison.Ordinal);
        return at < 0 ? phrase : phrase[(at + first.Heard.Length)..].Trim();
    }
}
