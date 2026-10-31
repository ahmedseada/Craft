// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Voice;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(VoicePackage))]

namespace Craft.Voice;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Voice\.</summary>
public sealed class VoicePackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Voice";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new VoiceRegistration()];
}

/// <summary>Registers the voice plug-in's parts, each with how to take it back: the editor plug-in, its commands, its spoken commands and the every-language spoken symbols.</summary>
public sealed class VoiceRegistration() : IdrakPlugin(VoiceSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(VoicePlugin.Instance), () => EditorPlugins.Unregister(VoiceSettings.PluginName));
        foreach (var command in VoicePlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        foreach (var spoken in VoicePlugin.SpokenAliases)
        {
            Part(() => SpokenCommands.Register(spoken), () => SpokenCommands.Unregister(spoken.Phrase));
        }

        foreach (var symbol in VoicePlugin.Symbols)
        {
            Part(() => SpokenSymbols.Register(symbol), () => SpokenSymbols.Unregister(symbol.Name));
        }
    }
}

/// <summary>
/// Coding by voice as an editor plug-in (plan 1, phase V, "voice"): the microphone opened by a key (toggled, or for one
/// phrase), the live transcription in the status bar with the microphone's level, committed phrases dictated at the
/// caret (one undo step each) or run as voice commands, spoken code sent to the chat model and answered as a diff, and
/// read back of the selection, a problem, a hover or the last chat reply (any key stops it).
/// </summary>
public sealed class VoicePlugin : IEditorPlugin
{
    /// <summary>Opens or closes the microphone for dictation and commands.</summary>
    public const string ListenCommand = "voice.listen";

    /// <summary>Opens or closes it for spoken code.</summary>
    public const string CodeCommand = "voice.code";

    /// <summary>Closes it.</summary>
    public const string StopCommand = "voice.stop";

    /// <summary>Loads the recognizer of the settings.</summary>
    public const string LoadCommand = "voice.loadRecognizer";

    /// <summary>Reads the selection (or the caret's line) aloud.</summary>
    public const string ReadSelectionCommand = "voice.readSelection";

    /// <summary>Reads the problem at the caret aloud.</summary>
    public const string ReadProblemCommand = "voice.readProblem";

    /// <summary>Reads the hover at the caret aloud.</summary>
    public const string ReadHoverCommand = "voice.readHover";

    /// <summary>Reads the last chat reply aloud.</summary>
    public const string ReadReplyCommand = "voice.readReply";

    /// <summary>Stops reading aloud.</summary>
    public const string StopReadingCommand = "voice.stopReading";

    /// <summary>Moves the caret to the next problem.</summary>
    public const string NextProblemCommand = "voice.nextProblem";

    /// <summary>Moves the caret to the previous problem.</summary>
    public const string PreviousProblemCommand = "voice.previousProblem";

    private VoicePlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static VoicePlugin Instance { get; } = new();

    /// <summary>The session of the host it is active in, or null.</summary>
    public static VoiceSession? Current { get; private set; }

    /// <summary>Its commands (registered by its package); each also runs by voice through its title.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(ListenCommand, "Voice: Start or stop listening", (_, token) => Current!.ToggleAsync(VoiceMode.Dictate, token),
            _ => Current is not null, "Opens the microphone (dictation and voice commands) or closes it"),
        new EditorCommand(CodeCommand, "Voice: Speak code", (_, token) => Current!.ToggleAsync(VoiceMode.Code, token),
            _ => Current is not null, "Opens the microphone for spoken code: each phrase goes to the chat model, its answer a diff you accept"),
        new EditorCommand(StopCommand, "Voice: Stop listening", (_, _) => { Current!.Stop(); return Task.CompletedTask; },
            _ => Current is { IsListening: true }, "Closes the microphone (the phrase being said is finished first)"),
        new EditorCommand(LoadCommand, "Voice: Load the speech recognizer", (_, token) => Current!.LoadRecognizerAsync(token),
            _ => Current is not null, "Loads the recognizer of CRAFT_PLUGIN_VOICE_RECOGNIZER, with its progress"),
        new EditorCommand(ReadSelectionCommand, "Voice: Read the selection aloud", (_, token) => Current!.ReadSelectionAsync(token),
            _ => Current?.Host.ActiveDocument is not null, "Speaks the selection (or the caret's line); any key stops it"),
        new EditorCommand(ReadProblemCommand, "Voice: Read the problem aloud", (_, token) => Current!.ReadProblemAsync(token),
            _ => Current?.Host.ActiveDocument is { Diagnostics.Count: > 0 }, "Speaks the error or warning at the caret (or the next one)"),
        new EditorCommand(ReadHoverCommand, "Voice: Read the hover aloud", (_, token) => Current!.ReadHoverAsync(token),
            _ => Current?.Host.ActiveDocument is { SemanticSession: not null }, "Speaks what the language says of the symbol at the caret"),
        new EditorCommand(ReadReplyCommand, "Voice: Read the last chat reply aloud", (_, token) => Current!.ReadReplyAsync(token),
            _ => Current is not null, "Speaks the chat's last answer"),
        new EditorCommand(StopReadingCommand, "Voice: Stop reading aloud", (_, _) => { Current!.StopReading(); return Task.CompletedTask; },
            _ => Current is { IsSpeaking: true }, "Stops the speech (any key does too)"),
        new EditorCommand(NextProblemCommand, "Go: Next problem", (host, _) => { VoiceSession.GoToProblem(host, forward: true); return Task.CompletedTask; },
            host => host.ActiveDocument is { Diagnostics.Count: > 0 }, "Moves the caret to the next error or warning"),
        new EditorCommand(PreviousProblemCommand, "Go: Previous problem", (host, _) => { VoiceSession.GoToProblem(host, forward: false); return Task.CompletedTask; },
            host => host.ActiveDocument is { Diagnostics.Count: > 0 }, "Moves the caret to the previous error or warning"),
    ];

    /// <summary>The phrases a command's title does not say, and the commands that take a number ("go to line #").</summary>
    public static IReadOnlyList<SpokenCommand> SpokenAliases { get; } =
    [
        new("go to line #", VoiceSession.GoToLineAsync, "goes to the line of that number"),
        new("line #", VoiceSession.GoToLineAsync, "goes to the line of that number"),
        new("next error", NextProblemCommand),
        new("previous error", PreviousProblemCommand),
        new("read selection", ReadSelectionCommand),
        new("read that", ReadSelectionCommand),
        new("read error", ReadProblemCommand),
        new("read problem", ReadProblemCommand),
        new("read hover", ReadHoverCommand),
        new("read reply", ReadReplyCommand),
        new("read answer", ReadReplyCommand),
        new("stop listening", StopCommand),
        new("stop reading", StopReadingCommand),
    ];

    /// <summary>The every-language spoken symbols (a language plug-in registers its own, which win for its documents).</summary>
    public static IReadOnlyList<SpokenSymbol> Symbols { get; } =
    [
        new("open paren", "("), new("close paren", ")", SpaceAfter: true),
        new("open bracket", "["), new("close bracket", "]", SpaceAfter: true),
        new("open brace", "{", SpaceBefore: true, SpaceAfter: true), new("close brace", "}", SpaceBefore: true, SpaceAfter: true),
        new("open angle", "<"), new("close angle", ">", SpaceAfter: true),
        new("semicolon", ";", SpaceAfter: true), new("colon", ":", SpaceAfter: true), new("comma", ",", SpaceAfter: true),
        new("dot", "."), new("period", "."), new("ellipsis", "..."),
        new("equals", "=", SpaceBefore: true, SpaceAfter: true), new("double equals", "==", SpaceBefore: true, SpaceAfter: true),
        new("not equals", "!=", SpaceBefore: true, SpaceAfter: true), new("plus equals", "+=", SpaceBefore: true, SpaceAfter: true),
        new("minus equals", "-=", SpaceBefore: true, SpaceAfter: true),
        new("plus", "+", SpaceBefore: true, SpaceAfter: true), new("minus", "-", SpaceBefore: true, SpaceAfter: true),
        new("times", "*", SpaceBefore: true, SpaceAfter: true), new("slash", "/", SpaceBefore: true, SpaceAfter: true),
        new("percent", "%", SpaceBefore: true, SpaceAfter: true),
        new("less than", "<", SpaceBefore: true, SpaceAfter: true), new("greater than", ">", SpaceBefore: true, SpaceAfter: true),
        new("less or equal", "<=", SpaceBefore: true, SpaceAfter: true), new("greater or equal", ">=", SpaceBefore: true, SpaceAfter: true),
        new("and and", "&&", SpaceBefore: true, SpaceAfter: true), new("or or", "||", SpaceBefore: true, SpaceAfter: true),
        new("arrow", "->"), new("fat arrow", "=>", SpaceBefore: true, SpaceAfter: true),
        new("plus plus", "++"), new("minus minus", "--"),
        new("bang", "!", SpaceBefore: true), new("question mark", "?", SpaceAfter: true),
        new("ampersand", "&", SpaceBefore: true, SpaceAfter: true), new("pipe", "|", SpaceBefore: true, SpaceAfter: true),
        new("caret", "^", SpaceBefore: true, SpaceAfter: true), new("tilde", "~", SpaceBefore: true),
        new("hash", "#", SpaceBefore: true), new("at sign", "@", SpaceBefore: true), new("dollar", "$", SpaceBefore: true),
        new("underscore", "_"), new("backslash", "\\"), new("backtick", "`"),
        new("quote", "\""), new("single quote", "'"),
        new("new line", "\n"), new("tab", "\t"), new("space", " "),
    ];

    /// <inheritdoc />
    public string Name => VoiceSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Coding by voice: dictation at the caret, voice commands, spoken code as a diff, and read back, on Idrak.Audio";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } = PluginSettingShapes.Apply(VoiceSettings.Declared, static _ => SettingSections.Voice);

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // Idrak.Audio, Idrak.Gpu, Idrak.Data and the rest sit in this plug-in's folder and load context: their built-ins
        // register only through this (the "audio" chat part, the GPU devices, the Hugging Face source), then the families.
        var own = typeof(VoicePlugin).Assembly;
        IdrakInPlugins.IncludeLibraries(own);
        var settings = VoiceSettings.From(host.Settings);
        // The families' packages load on the thread pool (assemblies and their registrations took the UI thread at
        // start-up); what needs them (a recognizer's or a synthesizer's load, the registries' description) waits for them.
        lock (PackagesGate)
        {
            s_packages = settings.IdrakPlugins;
        }

        _ = Task.Run(EnsurePackages);
        var session = new VoiceSession(host, () => Current = null);
        Current = session;
        return session;
    }

    /// <summary>
    /// What this plug-in sees in Idrak.Audio's registries, from its own load context (the tests check the plug-in loaded
    /// from a folder): "AudioCaptures: …", "AudioPlaybacks: …", "SpeechFamilies: …", "SpeechSynthesisFamilies: …",
    /// "LiveCommitPolicies: …", and the load context Idrak.Audio lives in.
    /// </summary>
    public static IReadOnlyList<string> DescribeRegistries()
    {
        EnsurePackages();
        return Registries();
    }

    /// <summary>
    /// Registers the Idrak plug-in packages (the speech families) of the settings the plug-in was activated with, from its
    /// own load context, once (a second caller waits for the first); any thread, never the UI's at start-up.
    /// </summary>
    internal static void EnsurePackages()
    {
        IReadOnlyList<string> packages;
        lock (PackagesGate)
        {
            packages = s_packages;
        }

        IdrakInPlugins.IncludePackages(typeof(VoicePlugin).Assembly, packages);
    }

    private static readonly Lock PackagesGate = new();
    private static IReadOnlyList<string> s_packages = [];

    private static IReadOnlyList<string> Registries() =>
    [
        "AudioCaptures: " + string.Join(", ", Idrak.Audio.Abstractions.AudioCaptures.Names.Order(StringComparer.Ordinal)),
        "AudioPlaybacks: " + string.Join(", ", Idrak.Audio.Abstractions.AudioPlaybacks.Names.Order(StringComparer.Ordinal)),
        "SpeechFamilies: " + string.Join(", ", Idrak.Audio.Abstractions.SpeechFamilies.Names.Order(StringComparer.Ordinal)),
        "SpeechSynthesisFamilies: " + string.Join(", ", Idrak.Audio.Abstractions.SpeechSynthesisFamilies.Names.Order(StringComparer.Ordinal)),
        "LiveCommitPolicies: " + string.Join(", ", Idrak.Audio.Abstractions.LiveCommitPolicies.Names.Order(StringComparer.Ordinal)),
        "Idrak.Audio context: " + (System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(Idrak.Audio.Abstractions.AudioCaptures).Assembly)?.Name ?? ""),
    ];
}
