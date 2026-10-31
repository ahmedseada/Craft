// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Sampler;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(SamplerPackage))]

namespace Craft.Plugins.Sampler;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Sampler\.</summary>
public sealed class SamplerPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Sampler";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new SamplerRegistration()];
}

/// <summary>Registers the bracket-balanced kind over Idrak's "default" sampler (its Unregister brings the built-in back) and the editor plug-in "sampler".</summary>
public sealed class SamplerRegistration() : IdrakPlugin(BracketBalancedSamplerKind.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => TokenSamplers.Register(BracketBalancedSamplerKind.Instance), () => TokenSamplers.Unregister(TokenSamplers.DefaultName));
        Part(() => EditorPlugins.Register(SamplerPlugin.Instance), () => EditorPlugins.Unregister(BracketBalancedSamplerKind.PluginName));
    }
}

/// <summary>
/// The bracket-balanced sampler kind (plan phase P3), registered as Idrak's <see cref="TokenSamplers.DefaultName"/>: each
/// generation's sampler is the library's default (<see cref="TokenSamplers.Default(string)"/>) wrapped in a
/// <see cref="BracketBalancedSampler"/> when the request carries its tokenizer and <see cref="Mode"/> is not off;
/// otherwise the library's sampler itself.
/// </summary>
public sealed class BracketBalancedSamplerKind : ITokenSamplerKind
{
    /// <summary>The editor plug-in's name, the prefix of its settings ("sampler").</summary>
    public const string PluginName = "sampler";

    private static BracketMode s_mode = ModeFromEnvironment();

    private BracketBalancedSamplerKind()
    {
    }

    /// <summary>The kind.</summary>
    public static BracketBalancedSamplerKind Instance { get; } = new();

    /// <summary>
    /// When it masks: the editor's setting <c>mode</c> once the plug-in is active, else <c>CRAFT_PLUGIN_SAMPLER_MODE</c>
    /// (fim, always, off); fim when neither says.
    /// </summary>
    public static BracketMode Mode
    {
        get => s_mode;
        set => s_mode = value;
    }

    /// <inheritdoc />
    public string Name => TokenSamplers.DefaultName;

    /// <inheritdoc />
    public string Summary => "bracket-balanced (Craft.Plugins.Sampler): the library's sampler, with the tokens that would close a bracket the prefix never opened masked on completions";

    /// <inheritdoc />
    public IReadOnlySet<string> Keys => OptionKeys.None;

    /// <inheritdoc />
    public ITokenSampler Create(SamplerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var library = TokenSamplers.Default(TokenSamplers.DefaultName)
            ?? throw new InvalidOperationException("The library's default token sampler is missing (TokenSamplers.Default(\"default\")): the bracket-balanced sampler wraps it.");
        var inner = library.Create(request);
        var mode = Mode;
        if (mode == BracketMode.Off || request.Tokenizer is not { } tokenizer)
        {
            return inner;
        }

        return new BracketBalancedSampler(inner, BracketTable.For(tokenizer, request.Vocabulary), request.Rows, mode, tokenizer);
    }

    /// <summary>The mode <paramref name="text"/> names ("fim", "always", "off", ignoring case; empty: fim).</summary>
    /// <exception cref="ArgumentException">Another word, naming the variable and the words it takes.</exception>
    public static BracketMode ParseMode(string? text) => (text ?? "").Trim().ToUpperInvariant() switch
    {
        "" or "FIM" => BracketMode.Fim,
        "ALWAYS" => BracketMode.Always,
        "OFF" => BracketMode.Off,
        _ => throw new ArgumentException($"{PluginSetting.VariableName(PluginName, "mode")} is '{text}': it takes fim, always or off.", nameof(text)),
    };

    private static BracketMode ModeFromEnvironment()
    {
        try
        {
            return ParseMode(Environment.GetEnvironmentVariable(PluginSetting.VariableName(PluginName, "mode")));
        }
        catch (ArgumentException)
        {
            return BracketMode.Fim;                          // the editor's activation reports it, naming the variable
        }
    }
}

/// <summary>The sampler as the editor lists it ("sampler"): its setting <c>mode</c>, read when it activates.</summary>
public sealed class SamplerPlugin : IEditorPlugin
{
    private SamplerPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static SamplerPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => BracketBalancedSamplerKind.PluginName;

    /// <inheritdoc />
    public string Summary => "Bracket-balanced completion: tokens that would close a brace, parenthesis or bracket the prefix never opened are masked";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } =
    [
        new("mode", "fim", "When tokens that would close an unopened bracket are masked: fim (inline completion only), always (chat replies too), off."),
    ];

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        BracketBalancedSamplerKind.Mode = BracketBalancedSamplerKind.ParseMode(host.Settings.Get(Name, "mode"));
        return Nothing.Instance;
    }

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
