// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Tests.Runner;
using Craft.Voice;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Audio;
using Idrak.Audio.Abstractions;

namespace Craft.Tests;

/// <summary>
/// Phase V, headless: a generated WAV played through Idrak's <c>PcmStreamInput</c> (behind a test capture registered in
/// <c>AudioCaptures</c>) with a fake recognizer dictates the expected text as one undo step a phrase, runs a spoken command,
/// asks for spoken code that arrives as a diff from a fake chat model (the sound itself to a model that hears), read back
/// stops on a key (a fake playback), the spoken symbols and numbers; the time from a phrase's end to text on screen and the
/// memory over many phrases measured; and the plug-in loaded from a copy of its folder, in a fresh process, sees
/// Idrak.Audio's registries populated from its own load context.
/// </summary>
[TestGroup("Voice")]
public static class VoiceTests
{
    private const int Rate = 16000;
    private const string Capture = "voice-test";
    private const string Provider = "voice-test";

    [Test]
    public static void AWavDictatesTheExpectedTextAsOneUndoStepAPhrase()
    {
        string[] phrases = ["Var total equals zero semicolon.", "New line.", "Print open paren total close paren semicolon."];
        using var fixture = new VoiceFixture(phrases, speed: 8);
        using var document = TextDocument.Create("");
        var open = fixture.Open(document);
        var session = fixture.Session;
        var heard = new List<string>();
        session.PhraseHandled += (_, p) => heard.Add(p.Heard);
        bool sawOpenWords = false, sawLevel = false;
        fixture.Ui.Run(() => session.StartAsync(VoiceMode.Dictate, CancellationToken.None));
        fixture.Ui.PumpUntil(() =>
        {
            sawOpenWords |= session.HeardText.Contains('[', StringComparison.Ordinal);
            sawLevel |= session.MicText.StartsWith("● Mic", StringComparison.Ordinal) && fixture.Host.StatusItems.Any(i => i.Id == "voice.mic");
            return session.Phrases == 3 && !session.IsListening;
        }, "the three phrases are heard and the sound ends");
        Check.Equal("Var total = 0;\nPrint(total);", Text(document), "each phrase typed at the caret, its spoken symbols and numbers as text");
        Check.True(sawLevel, "the status bar shows the microphone open, with its level");
        Check.True(sawOpenWords, "the open words show in brackets before they are final");
        Check.True(!fixture.Host.StatusItems.Any(i => i.Id == "voice.mic"), "the microphone's mark goes when it closes");
        Check.True(fixture.Recognizer.Calls > 3, $"the open phrase is transcribed again as it grows ({fixture.Recognizer.Calls} calls)");
        document.Undo();
        Check.Equal("Var total = 0;\n", Text(document), "one undo step takes back the last phrase only");
        document.Undo();
        Check.Equal("Var total = 0;", Text(document), "the next undo step the phrase before it");
        document.Undo();
        Check.True(Text(document).Length == 0 && !document.CanUndo, "three phrases, three undo steps");
        _ = open;
    }

    [Test]
    public static void ASpokenCommandRunsTheMatchingCommand()
    {
        int saved = 0;
        var save = new EditorCommand("test.voiceSave", "File: Save", (_, _) => { saved++; return Task.CompletedTask; });
        EditorCommands.Register(save);
        try
        {
            using var fixture = new VoiceFixture(["Save.", "Go to line two.", "Format the document."], speed: 50);
            int formatted = 0;
            var format = new EditorCommand("test.voiceFormat", "Edit: Format document", (_, _) => { formatted++; return Task.CompletedTask; });
            EditorCommands.Register(format);
            using var document = TextDocument.Create("first\nsecond\nthird\n");
            var open = fixture.Open(document);
            var session = fixture.Session;
            var selections = new List<SelectionState>();
            fixture.Host.SelectionRequested += (_, e) => selections.Add(e.Selection);
            fixture.Ui.Run(() => session.StartAsync(VoiceMode.Dictate, CancellationToken.None));
            fixture.Ui.PumpUntil(() => session.Phrases == 3 && !session.IsListening, "the three commands are heard");
            Check.Equal(1, saved, "\"Save\" runs the command titled \"File: Save\"");
            Check.Equal(1, formatted, "\"Format the document\" runs \"Edit: Format document\" (articles left out)");
            Check.True(open.Selection == SelectionState.At(6) && selections.Contains(SelectionState.At(6)), "\"go to line two\" moves the caret to line 2 (the number from its word), and the editor is told");
            Check.Equal("first\nsecond\nthird\n", Text(document), "commands type nothing");

            var typed = fixture.Ui.Run(() => session.HandlePhraseAsync("Type save."));
            fixture.Ui.PumpUntil(() => typed.IsCompleted, "the dictate prefix");
            Check.True(typed.Result.Kind == VoicePhraseKind.Dictated && saved == 1 && Text(document) == "first\nsavesecond\nthird\n",
                $"\"type save\" types the word at the caret instead of saving ({Text(document).Replace("\n", "\\n", StringComparison.Ordinal)})");
            var next = fixture.Ui.Run(() => session.HandlePhraseAsync("next error"));
            fixture.Ui.PumpUntil(() => next.IsCompleted, "next error");
            Check.True(next.Result.Kind == VoicePhraseKind.Ignored, "\"next error\" with no problems: its command cannot run, said so");
            EditorCommands.Unregister(format.Name);
        }
        finally
        {
            EditorCommands.Unregister(save.Name);
        }
    }

    [Test]
    public static void SpokenCodeArrivesAsADiffAndAModelThatHearsGetsTheSound()
    {
        foreach (bool hears in (ReadOnlySpan<bool>)[false, true])
        {
            var chat = new FakeChat(hears, "Here it is:\n```csharp\nint One() => 1;\n```");
            using var fixture = new VoiceFixture(["Code make a method that returns one."], speed: 50, chat: chat);
            using var document = TextDocument.Create("class A\n{\n    \n}\n");
            var open = fixture.Open(document);
            fixture.Host.UpdateSelection(open, SelectionState.At(14));
            var session = fixture.Session;
            fixture.Ui.Run(() => session.StartAsync(VoiceMode.Dictate, CancellationToken.None));
            fixture.Ui.PumpUntil(() => session.Phrases == 1 && !session.IsListening && fixture.Presenter.Proposals.Count == 1, "the spoken-code phrase reaches the diff view");
            var proposal = fixture.Presenter.Proposals.Single();
            Check.True(proposal.Title.EndsWith("Spoken code", StringComparison.Ordinal) && proposal.Origin!.StartsWith("Voice · fake", StringComparison.Ordinal),
                $"the model's answer arrives as a diff ({proposal.Title}, {proposal.Origin})");
            Check.Equal("class A\n{\n    int One() => 1;\n}\n", Text(document), "accepted, its code block is at the caret");
            document.Undo();
            Check.Equal("class A\n{\n    \n}\n", Text(document), "one undo step");
            var user = chat.Requests.Single().Messages[^1];
            var sound = user.Parts.OfType<ChatAudio>().SingleOrDefault();
            Check.True(user.Parts[0] is ChatText context && context.Text.Contains("class A", StringComparison.Ordinal) && context.Text.Contains("at the caret", StringComparison.Ordinal),
                "the file's lines around the caret go with it");
            if (hears)
            {
                Check.True(sound is not null && sound.MediaType == "audio/wav" && proposal.Origin!.Contains("heard the sound", StringComparison.Ordinal), "a model whose part kinds hold \"audio\" gets the phrase's sound itself");
                var decoded = sound!.Decode();
                Check.True(decoded.Duration.TotalSeconds is > 0.7 and < 2.5 && FakeRecognizer.Frequency(decoded) is > 300 and < 340, $"the sound is the phrase's ({decoded.Duration.TotalSeconds:F2} s)");
                Check.True(!((ChatText)user.Parts[0]).Text.Contains("make a method", StringComparison.Ordinal), "the sound stands for the transcript");
            }
            else
            {
                Check.True(sound is null && ((ChatText)user.Parts[0]).Text.Contains("The request: make a method that returns one.", StringComparison.Ordinal),
                    "a text-only model gets the transcript, the prefix taken off");
            }
        }
    }

    [Test]
    public static void ReadBackSpeaksAndStopsOnAnyKey()
    {
        var playback = new FakePlayback();
        AudioPlaybacks.Register(playback);
        var synthesizer = new FakeSynthesizer();
        VoiceModels.SynthesizerLoader = (_, _) => synthesizer;
        try
        {
            using var fixture = new VoiceFixture([], speed: 1, extra: new() { ["CRAFT_PLUGIN_VOICE_PLAYBACK"] = playback.Name, ["CRAFT_PLUGIN_VOICE_SYNTHESIZER"] = "fake" });
            using var document = TextDocument.Create("int x = 1;\nint y = 2;\n");
            var open = fixture.Open(document);
            fixture.Host.UpdateSelection(open, new SelectionState(0, 10));
            var session = fixture.Session;
            var reading = fixture.Ui.Run(() => session.ReadSelectionAsync(CancellationToken.None));
            fixture.Ui.PumpUntil(() => playback.Written >= 3, "the speech plays block by block");
            Check.True(session.IsSpeaking && fixture.Host.StatusItems.Any(i => i.Id == "voice.speaking") && synthesizer.Texts.Single() == "int x = 1;",
                "the selection is spoken, the status bar says so");
            fixture.Host.HandleKey(EditorKey.A, KeyModifiers.None, null);
            fixture.Ui.PumpUntil(() => reading.IsCompleted && playback.Disposed, "a key: the reading ends and the output closes");
            int written = playback.Written;
            Check.True(!session.IsSpeaking && playback.Disposed && written < FakeSynthesizer.Blocks && !fixture.Host.StatusItems.Any(i => i.Id == "voice.speaking"),
                $"any key stops it: {written} of {FakeSynthesizer.Blocks} blocks played, the output closed");
            Thread.Sleep(30);
            Check.Equal(written, playback.Written, "nothing plays after the stop");
        }
        finally
        {
            VoiceModels.SynthesizerLoader = null;
            AudioPlaybacks.Unregister(playback.Name);
        }
    }

    [Test]
    public static void SpokenSymbolsNumbersAndALanguagesOwnTable()
    {
        var csharp = new SpokenSymbol("arrow", "=>", "csharp", SpaceBefore: true, SpaceAfter: true);
        IdrakPlugins.RegisterAll(new VoicePackage());
        SpokenSymbols.Register(csharp);
        try
        {
            static string Say(string phrase, string? language, char? before = null) =>
                SpokenText.Dictate(SpokenText.Split(phrase), new SymbolTable(SpokenSymbols.For(language)), digits: true, keepPunctuation: false, before);
            Check.Equal("x->y", Say("x arrow y", null), "the every-language arrow");
            Check.Equal("x => y", Say("x arrow y", "csharp"), "a language plug-in's own symbol wins for its documents");
            Check.Equal("If(a == 42 && !b) {", Say("If open paren a double equals forty two and and bang b close paren open brace.", null),
                "multi-word symbols (the longest first), spaces by symbol, number words as digits");
            Check.Equal(" items, 105", Say("items comma one hundred and five", null, before: 'x'), "a space after a word before the caret; \"one hundred and five\"");
            Check.Equal("f(", Say("f open paren", null, before: ' '), "no space after a space");
            var numbers = new[] { "twenty", "one", "four", "two" };
            Check.True(SpokenText.TryNumber(numbers, 0, out long n, out int used) && n == 21 && used == 2, "\"twenty one\" is 21");
            Check.True(SpokenText.TryNumber(numbers, 2, out n, out used) && n == 4 && used == 1, "a unit after a unit ends the number");
            Check.True(SpokenText.TryNumber(["40"], 0, out n, out _) && n == 40, "digits as written");
            Check.True(SpokenText.FindCommand(SpokenText.Split("Go to line forty."), []) is { Spoken.Phrase: "go to line #", Number: 40 }, "\"go to line forty\": the alias and its number");
            Check.True(SpokenText.FindCommand(SpokenText.Split("something else"), []) is null, "no command: dictation");
        }
        finally
        {
            SpokenSymbols.Unregister(csharp.Name);
            IdrakPlugins.Unregister(VoiceSettings.PluginName);
        }

        Check.True(!VoicePlugin.Symbols.Any(s => SpokenSymbols.Find(s.Name) is not null) && SpokenCommands.Names.Count == 0, "unregistered, the plug-in leaves no symbol or spoken command behind");
    }

    [Test]
    public static void ThePipelinesOwnTimeAndFlatMemoryOverManyPhrases()
    {
        const int Count = 160;
        var phrases = Enumerable.Range(0, Count).Select(i => i % 4 == 3 ? "New line." : $"Value {i % 10} equals {i % 7} semicolon.").ToArray();
        using var fixture = new VoiceFixture(phrases, speed: 40, synchronous: true);
        using var document = TextDocument.Create("");
        fixture.Open(document);
        var session = fixture.Session;
        var times = new List<double>();
        long early = 0, late = 0;
        session.PhraseHandled += (_, p) =>
        {
            times.Add(p.Milliseconds);
            if (session.Phrases == 40)
            {
                early = Memory();
            }
            else if (session.Phrases == Count)
            {
                late = Memory();
            }
        };
        var watch = Stopwatch.StartNew();
        fixture.Ui.Run(() => session.StartAsync(VoiceMode.Dictate, CancellationToken.None));
        fixture.Ui.PumpUntil(() => session.Phrases == Count && !session.IsListening, $"the {Count} phrases are heard", seconds: 120);
        watch.Stop();
        Check.True(times.Count == Count && session.LastPhrase!.Kind == VoicePhraseKind.Dictated, "every phrase is dictated");
        var warm = times.Skip(10).Where(t => t > 0).Order().ToArray();
        double median = warm[warm.Length / 2], p99 = warm[(int)(warm.Length * 0.99)], first = times[0];
        // Between the 40th and the last phrase the document grows by its text and its undo steps (about 120 phrases of ~25
        // characters, twice: the text and the step); the pipeline itself (the transcriber's segment, the ring, the tables) is fixed.
        double perPhrase = (late - early) / (double)(Count - 40);
        string line = string.Create(CultureInfo.InvariantCulture,
            $"voice pipeline, fake recognizer: {Count} phrases ({watch.Elapsed.TotalSeconds:F1} s for {Count * 1.8:F0} s of sound); phrase end to text on screen: first {first:F3} ms, median {median:F3} ms, p99 {p99:F3} ms; managed memory after 40 phrases {early / 1024.0:F0} KiB, after {Count} {late / 1024.0:F0} KiB ({perPhrase:F0} bytes a phrase, the document's growth included); recognizer calls {fixture.Recognizer.Calls}");
        var output = TestRunner.RepositoryPath("test-output", "voice-latency.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, line + "\n");
        Check.True(median < 5, $"the pipeline's own time from a phrase's end to text is small ({line})");
        Check.True(perPhrase < 4096, $"memory stays flat over the phrases ({line})");
    }

    [Test]
    public static void LoadedFromACopiedFolderTheVoicePluginSeesIdrakAudiosRegistries()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Path, "plugins", "Craft.Voice");
        CopyVoicePlugin(folder);
        // A fresh process: nothing of Idrak.Audio is loaded in its default load context, as in the app (whose folder has none),
        // so what the plug-in sees is what its own load context registered (the owner's Windows run: a plug-in's Idrak
        // libraries register their built-ins only through IdrakInPlugins).
        var lines = PluginProbe.RunInFreshProcess(folder);
        string Line(string key) => lines.FirstOrDefault(l => l.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..] ?? "(missing)";
        Check.True(Line("loaded").StartsWith("True", StringComparison.Ordinal), $"the plug-in loads from its folder ({string.Join(" | ", lines)})");
        Check.True(Line("activation voice").StartsWith("True", StringComparison.Ordinal), $"the host activates it ({Line("activation voice")})");
        Check.Equal("Craft.Voice", Line("Idrak.Audio context"), "Idrak.Audio is the plug-in's own, in its load context");
        // (The test program's own folder holds Idrak.Audio too, which the app's does not, so Idrak's LibraryDefaults may load
        // a copy of it in the default context here; what is checked is what the plug-in's own copy registered.)
        Check.Equal("Craft.Voice", Line("audio chat part from"), "the \"audio\" chat part was registered by the plug-in's own Idrak.Audio (IdrakInPlugins)");
        Check.True(Line("AudioCaptures").Split(", ").Order().SequenceEqual(["aaudio", "alsa", "winmm"]), $"AudioCaptures holds winmm, alsa and aaudio ({Line("AudioCaptures")})");
        Check.True(Line("AudioPlaybacks").Contains("winmm", StringComparison.Ordinal), $"AudioPlaybacks is populated ({Line("AudioPlaybacks")})");
        Check.True(Line("SpeechFamilies").Contains("WhisperForConditionalGeneration", StringComparison.Ordinal),
            $"SpeechFamilies holds Idrak.Audio.Plugins' families, registered by the setting idrakPlugins ({Line("SpeechFamilies")})");
        Check.True(Line("SpeechSynthesisFamilies").Length > 0 && Line("SpeechSynthesisFamilies") != "(missing)", $"SpeechSynthesisFamilies is populated ({Line("SpeechSynthesisFamilies")})");
        Check.True(Line("LiveCommitPolicies").Contains("agreement", StringComparison.Ordinal), "the live commit policy is there");
        Check.True(Line("chat parts").Split(", ").Contains("audio"), $"the \"audio\" chat part Idrak.Audio adds to Idrak.Abstraction is registered ({Line("chat parts")})");
        Check.True(Line("device providers").Length > 0, $"Idrak.Gpu's device providers registered too ({Line("device providers")})");
        Check.True(Line("commands").Contains(VoicePlugin.ListenCommand, StringComparison.Ordinal) && Line("key bindings").Contains("Ctrl+Shift+M=voice.listen", StringComparison.Ordinal),
            $"its commands and its key ({Line("commands")}; {Line("key bindings")})");
    }

    private static long Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static string Text(TextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    private static void CopyVoicePlugin(string folder)
    {
        // tests/Craft.Tests/bin/<Configuration>/net10.0/ -> plugins/Craft.Voice/bin/<Configuration>/net10.0/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TestRunner.RepositoryPath("plugins", "Craft.Voice", "bin", testOutput.Parent!.Name, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, "Craft.Voice.deps.json")), $"Craft.Voice's build output is at {output}");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(output))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("Craft.Abstractions.", StringComparison.Ordinal) && !name.StartsWith("Idrak.Abstraction.", StringComparison.Ordinal)
                && !name.StartsWith("Craft.Core.", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(folder, name));
            }
        }

        Check.True(File.Exists(Path.Combine(folder, "plugin.json")) && File.Exists(Path.Combine(folder, "Idrak.Audio.dll")) && File.Exists(Path.Combine(folder, "Idrak.Gpu.dll"))
                   && !File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")), "the folder carries its plugin.json, Idrak.Audio and Idrak.Gpu, and no shared contract");
    }

    /// <summary>
    /// A test UI thread: a queue the test pumps, and the synchronization context the session's awaits come back to, so
    /// everything the host and the session do runs on the pumping thread, as on MAUI's.
    /// </summary>
    private sealed class UiLoop : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("The test UI loop only posts.");

        public void Dispatch(Action action) => Post(static a => ((Action)a!)(), action);

        public T Run<T>(Func<T> work)
        {
            var old = Current;
            SetSynchronizationContext(this);
            try
            {
                return work();
            }
            finally
            {
                SetSynchronizationContext(old);
            }
        }

        public void Pump()
        {
            var old = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_queue.TryDequeue(out var item))
                {
                    item.Callback(item.State);
                }
            }
            finally
            {
                SetSynchronizationContext(old);
            }
        }

        public void PumpUntil(Func<bool> done, string what, int seconds = 20)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Pump();
                if (done())
                {
                    return;
                }

                if (watch.Elapsed > TimeSpan.FromSeconds(seconds))
                {
                    throw new CheckFailedException($"{what}: not within {seconds} s");
                }

                Thread.Sleep(1);
            }
        }
    }

    /// <summary>
    /// The voice plug-in registered and activated in a host whose UI thread is a <see cref="UiLoop"/>, over a test capture
    /// that plays a generated WAV (one tone a phrase, a pause after each) through Idrak's PcmStreamInput, a fake recognizer
    /// that writes each tone's phrase, and (when given) a fake chat model behind a test provider.
    /// </summary>
    private sealed class VoiceFixture : IDisposable
    {
        private readonly TestCapture _capture;
        private readonly bool _synchronous;

        public VoiceFixture(string[] phrases, double speed, FakeChat? chat = null, Dictionary<string, string>? extra = null, bool synchronous = false)
        {
            _synchronous = synchronous;
            Recognizer = new FakeRecognizer(phrases);
            _capture = new TestCapture(FakeRecognizer.Wav(phrases.Length), speed);
            AudioCaptures.Register(_capture);
            VoiceModels.RecognizerLoader = (_, _) => Recognizer;
            if (chat is not null)
            {
                ModelProviders.Register(new FakeProvider(chat));
            }

            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CRAFT_PLUGIN_VOICE_CAPTURE"] = Capture,
                ["CRAFT_PLUGIN_VOICE_RECOGNIZER"] = "fake",
                ["CRAFT_PLUGIN_VOICE_IDRAKPLUGINS"] = "none-here",
                ["CRAFT_PLUGIN_VOICE_STEPSECONDS"] = "0.25",
                ["CRAFT_PLUGIN_VOICE_MODELPROVIDER"] = Provider,
                ["CRAFT_PLUGIN_VOICE_CODEMODEL"] = "fake",
                ["CRAFT_PLUGIN_VOICE_LEVELMS"] = "10",
            };
            foreach (var (key, value) in extra ?? [])
            {
                variables[key] = value;
            }

            variables["CRAFT_PLUGIN_VOICE_IDRAKPLUGINS"] = "";
            Presenter = new AcceptingPresenter();
            Host = new EditorHost(dispatch: synchronous ? static action => action() : Ui.Dispatch, presenter: Presenter, readVariable: name => variables.GetValueOrDefault(name) is { Length: > 0 } v ? v : null);
            IdrakPlugins.RegisterAll(new VoicePackage());
            var activation = Host.RefreshPlugins().Single(a => a.Name == VoiceSettings.PluginName);
            Check.True(activation.Active, $"the voice plug-in activates ({activation.Error})");
            Session = VoicePlugin.Current!;
        }

        public UiLoop Ui { get; } = new();

        public EditorHost Host { get; }

        public AcceptingPresenter Presenter { get; }

        public FakeRecognizer Recognizer { get; }

        public VoiceSession Session { get; }

        public IEditorDocument Open(TextDocument document)
        {
            var open = Host.Open(document, null);
            Host.SetActive(open);
            return open;
        }

        public void Dispose()
        {
            _ = _synchronous;
            Host.Dispose();
            IdrakPlugins.Unregister(VoiceSettings.PluginName);
            VoiceModels.RecognizerLoader = null;
            AudioCaptures.Unregister(Capture);
            ModelProviders.Unregister(Provider);
            Check.True(EditorPlugins.Find(VoiceSettings.PluginName) is null && SpokenCommands.Names.Count == 0 && !VoicePlugin.Symbols.Any(s => SpokenSymbols.Find(s.Name) is not null)
                       && !EditorKeyBindings.Names.Any(n => n.Contains("Shift+M", StringComparison.Ordinal)), "the plug-in leaves nothing behind");
        }
    }

    /// <summary>A capture that "records" a WAV through Idrak's PcmStreamInput, paced at <c>speed</c> times real time (0: at once).</summary>
    private sealed class TestCapture(byte[] wav, double speed) : IAudioCapture
    {
        public string Name => Capture;

        public bool IsAvailable(out string reason)
        {
            reason = "";
            return true;
        }

        public IReadOnlyList<AudioInputDevice> Devices() => [new AudioInputDevice("wav", "the test's WAV", true)];

        public IAudioInput Open(AudioCaptureOptions options) =>
            new PcmStreamInput(speed > 0 ? new PacedStream(wav, Rate * 2 * speed) : new MemoryStream(wav), "the test's WAV", PcmSampleFormat.S16LE, Rate, 1);
    }

    /// <summary>A stream of bytes that arrive at a rate, as a microphone's do.</summary>
    private sealed class PacedStream(byte[] data, double bytesPerSecond) : Stream
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position { get => _at; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                int due = (int)Math.Min(data.Length, Math.Max(44, _clock.Elapsed.TotalSeconds * bytesPerSecond));
                if (_at >= data.Length)
                {
                    return 0;
                }

                if (due > _at)
                {
                    int n = Math.Min(buffer.Length, due - _at);
                    data.AsMemory(_at, n).CopyTo(buffer);
                    _at += n;
                    return n;
                }

                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A recognizer that knows its phrases by their tone: phrase k is a tone of 320 + 30k Hz. A tone still sounding at the
    /// end of the sound it is given (an open phrase) gives the share of its words heard so far; a whole one, all of them.
    /// </summary>
    private sealed class FakeRecognizer(string[] phrases) : ISpeechRecognizer
    {
        private const double ToneSeconds = 0.8;
        private const double PauseSeconds = 1.0;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Device Device => Device.Cpu;

        public IReadOnlySet<string> Languages { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The 16-bit mono WAV of <paramref name="count"/> phrases: 0.3 s of quiet, then each tone and its pause.</summary>
        public static byte[] Wav(int count)
        {
            int toneSamples = (int)(ToneSeconds * Rate), pauseSamples = (int)(PauseSeconds * Rate), lead = (int)(0.3 * Rate);
            var samples = new float[lead + (count * (toneSamples + pauseSamples))];
            for (int k = 0; k < count; k++)
            {
                double frequency = 320 + (30 * k);
                int start = lead + (k * (toneSamples + pauseSamples));
                for (int i = 0; i < toneSamples; i++)
                {
                    samples[start + i] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / Rate));
                }
            }

            using var stream = new MemoryStream();
            AudioEncoders.Get("wav").Encode(new AudioData(samples, 1, Rate), stream);
            return stream.ToArray();
        }

        /// <summary>The tone's frequency: its zero crossings over the time it sounds.</summary>
        public static double Frequency(AudioData audio)
        {
            var x = audio.Samples;
            int first = Array.FindIndex(x, v => Math.Abs(v) > 0.05f), last = Array.FindLastIndex(x, v => Math.Abs(v) > 0.05f);
            if (first < 0 || last <= first)
            {
                return 0;
            }

            int crossings = 0;
            for (int i = first + 1; i <= last; i++)
            {
                if ((x[i - 1] < 0) != (x[i] < 0))
                {
                    crossings++;
                }
            }

            return crossings / (2.0 * (last - first) / audio.SampleRate);
        }

        public Transcription Transcribe(AudioData audio, TranscriptionOptions? options = null, IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var x = audio.Samples;
            int first = Array.FindIndex(x, v => Math.Abs(v) > 0.05f), last = Array.FindLastIndex(x, v => Math.Abs(v) > 0.05f);
            string text = "";
            if (first >= 0 && last > first && phrases.Length > 0)
            {
                int k = Math.Clamp((int)Math.Round((Frequency(audio) - 320) / 30), 0, phrases.Length - 1);
                var words = phrases[k].Split(' ');
                bool open = x.Length - last < Rate / 50;                     // the tone sounds to the end: the phrase goes on
                int heard = open ? Math.Clamp((int)Math.Ceiling(words.Length * (last - first) / (ToneSeconds * Rate)), 1, words.Length) : words.Length;
                text = string.Join(' ', words[..heard]);
            }

            var stats = new TranscriptionStats(audio.Duration, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 1, 1);
            return new Transcription(text, null, [], stats);
        }

        public IAsyncEnumerable<TranscriptSegment> TranscribeStream(IAsyncEnumerable<AudioData> chunks, TranscriptionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The live transcriber transcribes the open phrase whole.");

        public void Dispose()
        {
        }
    }

    /// <summary>A chat model that answers one reply and records its requests; it hears sound when told to.</summary>
    private sealed class FakeChat(bool hears, string reply) : IChatModel
    {
        public List<ChatRequest> Requests { get; } = [];

        public IReadOnlySet<string> PartKinds { get; } = hears
            ? new HashSet<string>(StringComparer.Ordinal) { ChatParts.Text, ChatParts.Audio }
            : ChatParts.TextOnly;

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            await Task.Yield();
            yield return new ChatChunk(new ChatDelta(reply, "", []));
            yield return new ChatChunk(new ChatDelta("", "", []), true, "stop", new ChatMessage("assistant", reply),
                new GenerationStats(10, TimeSpan.Zero, 5, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), 0));
        }
    }

    private sealed class FakeProvider(IChatModel chat) : IModelProvider
    {
        public string Name => Provider;

        public string Summary => "the voice tests' chat model";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelLease(request.Model, chat, null, 4096, TimeSpan.Zero));
    }

    /// <summary>A synthesizer that speaks any text as <see cref="Blocks"/> blocks of 20 ms.</summary>
    private sealed class FakeSynthesizer : ISpeechSynthesizer
    {
        public const int Blocks = 200;

        public List<string> Texts { get; } = [];

        public Device Device => Device.Cpu;

        public int SampleRate => Rate;

        public IReadOnlyList<string> Voices => [];

        public (AudioData Audio, SynthesisStats Stats) Speak(string text, SpeechSynthesisOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Read back streams.");

        public async IAsyncEnumerable<AudioData> SpeakStream(string text, SpeechSynthesisOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (Texts)
            {
                Texts.Add(text);
            }

            for (int i = 0; i < Blocks; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new AudioData(new float[Rate / 50], 1, Rate);
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A playback whose output takes 5 ms a block (a device's pace) and counts what it played.</summary>
    private sealed class FakePlayback : IAudioPlayback
    {
        private int _written;

        public string Name => "voice-test-out";

        public int Written => Volatile.Read(ref _written);

        public bool Disposed { get; private set; }

        public bool IsAvailable(out string reason)
        {
            reason = "";
            return true;
        }

        public IReadOnlyList<AudioOutputDevice> Devices() => [];

        public IAudioOutput Open(AudioPlaybackOptions options) => new Output(this, options.SampleRate ?? Rate);

        private sealed class Output(FakePlayback owner, int rate) : IAudioOutput
        {
            public string Description => "the voice tests' output";

            public int SampleRate => rate;

            public int Channels => 1;

            public async ValueTask WriteAsync(AudioData block, CancellationToken cancellationToken = default)
            {
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref owner._written);
            }

            public Task DrainAsync(CancellationToken cancellationToken = default) => Task.Delay(5, cancellationToken);

            public void Dispose() => owner.Disposed = true;
        }
    }

    private sealed class AcceptingPresenter : IEditorPresenter
    {
        public List<EditProposal> Proposals { get; } = [];

        public List<string> Messages { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal);
            return Task.FromResult(true);
        }

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add($"{title}: {message}");
            return Task.CompletedTask;
        }
    }
}
