// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Voice;

/// <summary>
/// The voice plug-in's settings ("voice": each <c>CRAFT_PLUGIN_VOICE_*</c>, on the settings page with the other plug-ins').
/// Nothing in it is fixed: the models, the devices, the keys, the live transcription's step, window, pause and commit
/// policy, the prefixes, and how spoken code reaches the model.
/// </summary>
public sealed record VoiceSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "voice";

    /// <summary>The speech recognizer: a model name Idrak's model sources resolve (a folder, store:NAME, a Hugging Face id).</summary>
    public string Recognizer { get; init; } = "";

    /// <summary>The speech synthesizer for read back: a model name, as <see cref="Recognizer"/>.</summary>
    public string Synthesizer { get; init; } = "";

    /// <summary>The synthesizer's voice (a speaker of the checkpoint, or an installed voice); empty: the model's first.</summary>
    public string Voice { get; init; } = "";

    /// <summary>Where the speech models run: auto, cpu, vulkan:0, cuda:0.</summary>
    public string ComputeDevice { get; init; } = "auto";

    /// <summary>The capture (Idrak's <c>AudioCaptures</c>: winmm, alsa, aaudio); auto: the first that can record here.</summary>
    public string Capture { get; init; } = "auto";

    /// <summary>The input device's id (the capture's <c>Devices</c>); empty: the system's default microphone.</summary>
    public string Device { get; init; } = "";

    /// <summary>The playback (Idrak's <c>AudioPlaybacks</c>); auto: the first that can play here.</summary>
    public string Playback { get; init; } = "auto";

    /// <summary>The output device's id; empty: the system's default.</summary>
    public string OutputDevice { get; init; } = "";

    /// <summary>The microphone's sample rate in Hz.</summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>"toggle" (the key opens the microphone, the key again closes it) or "push" (the key opens it for one phrase).</summary>
    public string Mode { get; init; } = "toggle";

    /// <summary>The key that opens and closes the microphone for dictation and commands.</summary>
    public string Key { get; init; } = "Ctrl+Shift+M";

    /// <summary>The key that opens it for spoken code: every phrase goes to the chat model.</summary>
    public string CodeKey { get; init; } = "Ctrl+Shift+Alt+M";

    /// <summary>The spoken language ("en"); empty: the recognizer detects it.</summary>
    public string Language { get; init; } = "";

    /// <summary>How often the open phrase is transcribed again, in seconds.</summary>
    public double StepSeconds { get; init; } = 1.0;

    /// <summary>The longest phrase, in seconds (a phrase that long is ended there).</summary>
    public double WindowSeconds { get; init; } = 20;

    /// <summary>The pause that ends a phrase, in seconds.</summary>
    public double SilenceSeconds { get; init; } = 0.6;

    /// <summary>The commit policy that makes the open words final (Idrak's <c>LiveCommitPolicies</c>).</summary>
    public string CommitPolicy { get; init; } = "agreement";

    /// <summary>How many transcriptions in a row must agree on a word ("agreement").</summary>
    public int Agreement { get; init; } = 2;

    /// <summary>The word a command starts with; empty: any phrase that matches a command runs it.</summary>
    public string CommandPrefix { get; init; } = "";

    /// <summary>The word that makes the rest of a phrase dictation even when it matches a command ("type save").</summary>
    public string DictatePrefix { get; init; } = "type";

    /// <summary>The word that sends the rest of a phrase to the chat model as spoken code ("code: a method that ...").</summary>
    public string CodePrefix { get; init; } = "code";

    /// <summary>How spoken code reaches the model: auto (the sound itself to a model that hears, else the transcript), sound, or text.</summary>
    public string CodeAs { get; init; } = "auto";

    /// <summary>The model provider spoken code asks (Craft's <c>ModelProviders</c>).</summary>
    public string ModelProvider { get; init; } = ModelProviders.Local;

    /// <summary>The chat model for spoken code; empty: the AI plug-in's model (its setting "model").</summary>
    public string CodeModel { get; init; } = "";

    /// <summary>The most tokens a spoken-code answer may take.</summary>
    public int CodeMaxTokens { get; init; } = 1024;

    /// <summary>The lines around the caret sent with spoken code, before and after.</summary>
    public int CodeContextLines { get; init; } = 40;

    /// <summary>"spoken" (only spoken symbols type marks: the recognizer's own punctuation is dropped) or "heard" (kept).</summary>
    public string Punctuation { get; init; } = "spoken";

    /// <summary>Whether number words are typed as digits ("forty two": 42).</summary>
    public bool Digits { get; init; } = true;

    /// <summary>The Idrak plug-in packages (the speech families) registered at activation; empty for none.</summary>
    public IReadOnlyList<string> IdrakPlugins { get; init; } = ["Idrak.Audio.Plugins"];

    /// <summary>Minutes the speech models stay loaded after their last use (0: while the plug-in is active).</summary>
    public double KeepAliveMinutes { get; init; } = 10;

    /// <summary>How often the microphone's level in the status bar is updated, in milliseconds.</summary>
    public int LevelMilliseconds { get; init; } = 100;

    /// <summary>The settings the plug-in declares, with their defaults.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("recognizer", "", "The speech recognizer: a model name Idrak resolves (a folder, store:NAME, a Hugging Face id) of a family Idrak.Audio.Plugins registers."),
        new("synthesizer", "", "The speech synthesizer for read back: a model name, as the recognizer."),
        new("voice", "", "The synthesizer's voice (a speaker or an installed voice); empty: the model's first."),
        new("computeDevice", "auto", "Where the speech models run: auto, cpu, vulkan:0, cuda:0."),
        new("capture", "auto", "The microphone capture (winmm, alsa, aaudio); auto: the first that records here."),
        new("device", "", "The microphone's device id; empty: the system's default."),
        new("playback", "auto", "The sound output for read back (winmm, alsa, aaudio); auto: the first that plays here."),
        new("outputDevice", "", "The output device's id; empty: the system's default."),
        new("sampleRate", "16000", "The microphone's sample rate in Hz."),
        new("mode", "toggle", "toggle: the key opens the microphone and closes it; push: the key opens it for one phrase."),
        new("key", "Ctrl+Shift+M", "The key that opens and closes the microphone (dictation and commands)."),
        new("codeKey", "Ctrl+Shift+Alt+M", "The key that opens it for spoken code (each phrase goes to the chat model, its answer a diff)."),
        new("language", "", "The spoken language (en, de, ar); empty: the recognizer detects it."),
        new("stepSeconds", "1", "How often the open phrase is transcribed again, in seconds."),
        new("windowSeconds", "20", "The longest phrase, in seconds."),
        new("silenceSeconds", "0.6", "The pause that ends a phrase, in seconds."),
        new("commitPolicy", "agreement", "The commit policy that makes open words final (Idrak's LiveCommitPolicies)."),
        new("agreement", "2", "How many transcriptions in a row must agree on a word."),
        new("commandPrefix", "", "The word a voice command starts with; empty: any phrase that matches a command runs it."),
        new("dictatePrefix", "type", "The word that makes the rest of a phrase dictation even when it names a command."),
        new("codePrefix", "code", "The word that sends the rest of a phrase to the chat model as spoken code."),
        new("codeAs", "auto", "Spoken code as auto (the sound to a model that hears, else the transcript), sound or text."),
        new("modelProvider", ModelProviders.Local, "The model provider spoken code asks (local comes with the AI plug-in)."),
        new("codeModel", "", "The chat model for spoken code; empty: the AI plug-in's model."),
        new("codeMaxTokens", "1024", "The most tokens a spoken-code answer may take."),
        new("codeContextLines", "40", "The lines around the caret sent with spoken code, before and after."),
        new("punctuation", "spoken", "spoken: only spoken symbols type marks; heard: the recognizer's punctuation is kept."),
        new("digits", "true", "Number words typed as digits (forty two: 42)."),
        new("idrakPlugins", "Idrak.Audio.Plugins", "Idrak plug-in packages (the speech families) registered at activation; empty: none."),
        new("keepAliveMinutes", "10", "Minutes the speech models stay loaded after their last use; 0: while the plug-in is active."),
        new("levelMs", "100", "How often the microphone's level in the status bar is updated, in milliseconds."),
    ];

    /// <summary>The settings as <paramref name="settings"/> reads them now.</summary>
    /// <exception cref="ArgumentException">A value does not parse (the message names the variable).</exception>
    public static VoiceSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Text(string key) => settings.Get(PluginName, key);
        double Number(string key, double min)
        {
            var text = Text(key);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= min
                ? value
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': a number of at least {min.ToString(CultureInfo.InvariantCulture)}.");
        }

        int Whole(string key, int min)
        {
            var text = Text(key);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min
                ? value
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': a whole number of at least {min.ToString(CultureInfo.InvariantCulture)}.");
        }

        bool Flag(string key)
        {
            var text = Text(key);
            return bool.TryParse(text, out bool value)
                ? value
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': true or false.");
        }

        string OneOf(string key, params string[] allowed)
        {
            var text = Text(key).ToLowerInvariant();
            return Array.IndexOf(allowed, text) >= 0
                ? text
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': one of {string.Join(", ", allowed)}.");
        }

        return new VoiceSettings
        {
            Recognizer = Text("recognizer"),
            Synthesizer = Text("synthesizer"),
            Voice = Text("voice"),
            ComputeDevice = Text("computeDevice"),
            Capture = Text("capture"),
            Device = Text("device"),
            Playback = Text("playback"),
            OutputDevice = Text("outputDevice"),
            SampleRate = Whole("sampleRate", 8000),
            Mode = OneOf("mode", "toggle", "push"),
            Key = Text("key"),
            CodeKey = Text("codeKey"),
            Language = Text("language"),
            StepSeconds = Number("stepSeconds", 0.05),
            WindowSeconds = Number("windowSeconds", 0.1),
            SilenceSeconds = Number("silenceSeconds", 0.02),
            CommitPolicy = Text("commitPolicy"),
            Agreement = Whole("agreement", 1),
            CommandPrefix = Text("commandPrefix").ToLowerInvariant(),
            DictatePrefix = Text("dictatePrefix").ToLowerInvariant(),
            CodePrefix = Text("codePrefix").ToLowerInvariant(),
            CodeAs = OneOf("codeAs", "auto", "sound", "text"),
            ModelProvider = Text("modelProvider"),
            CodeModel = Text("codeModel"),
            CodeMaxTokens = Whole("codeMaxTokens", 1),
            CodeContextLines = Whole("codeContextLines", 0),
            Punctuation = OneOf("punctuation", "spoken", "heard"),
            Digits = Flag("digits"),
            IdrakPlugins = Text("idrakPlugins").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            KeepAliveMinutes = Number("keepAliveMinutes", 0),
            LevelMilliseconds = Whole("levelMs", 10),
        };
    }
}
