// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Idrak.Abstraction.Generation;

namespace Craft.Abstractions;

/// <summary>A model a plug-in asks for: its name (resolved by Idrak's <c>ModelSources</c>, never by Craft) and how to load it.</summary>
/// <param name="Model">The name: a folder, a .gguf file, "store:NAME", a Hugging Face id, or any kind a registered model source reads.</param>
public sealed record ModelRequest(string Model)
{
    /// <summary>The device ("auto", "cpu", "vulkan:0"); null for the provider's setting.</summary>
    public string? Device { get; init; }

    /// <summary>The context window in tokens; null for the provider's setting (at most the model's own).</summary>
    public int? Context { get; init; }

    /// <summary>The projections' format once loaded ("int8", "int4", "bfloat16"); null for the provider's setting.</summary>
    public string? Weights { get; init; }
}

/// <summary>How far a model's load is: what it does now, and how far (0 to 1) when that is known.</summary>
/// <param name="Stage">What it does ("resolving", "loading", "ready").</param>
/// <param name="Fraction">How far, from 0 to 1, or null when not known.</param>
/// <param name="Message">A line for the user ("loading Qwen/Qwen3-0.6B on cpu, 4.2 s").</param>
public sealed record ModelLoadStatus(string Stage, double? Fraction, string Message);

/// <summary>
/// A model in use: its chat and text contracts (Idrak's <see cref="IChatModel"/> and <see cref="ITextModel"/>), its
/// context window, and how long this lease spent loading it. Dispose it when done: the provider keeps the model loaded
/// for its keep-alive and shares it between leases.
/// </summary>
public sealed class ModelLease : IDisposable
{
    private IDisposable? _release;

    /// <summary>A lease of <paramref name="model"/>.</summary>
    /// <param name="model">The name it was asked by.</param>
    /// <param name="chat">The model as a chat model, or null when it has no chat template.</param>
    /// <param name="text">The model as a text model (raw continuation, for fill-in-the-middle), or null.</param>
    /// <param name="contextLength">Its context window in tokens (the budget a plug-in fills, never a fixed number).</param>
    /// <param name="loadDuration">The time this lease spent loading it (zero when it was loaded already).</param>
    /// <param name="release">What gives it back to the provider, or null.</param>
    public ModelLease(string model, IChatModel? chat, ITextModel? text, int contextLength, TimeSpan loadDuration, IDisposable? release = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextLength);
        Model = model;
        Chat = chat;
        Text = text;
        ContextLength = contextLength;
        LoadDuration = loadDuration;
        _release = release;
    }

    /// <summary>The name it was asked by.</summary>
    public string Model { get; }

    /// <summary>The model as a chat model, or null when it has no chat template.</summary>
    public IChatModel? Chat { get; }

    /// <summary>The model as a text model, or null.</summary>
    public ITextModel? Text { get; }

    /// <summary>
    /// The model's tokenizer, or null when the provider does not give it: a plug-in detects the model's fill-in-the-middle
    /// format from it (Idrak's <c>FillInMiddleFormats.Detect</c>) and lays its prompts out with it.
    /// </summary>
    public IPretrainedTokenizer? Tokenizer { get; init; }

    /// <summary>Its context window in tokens.</summary>
    public int ContextLength { get; }

    /// <summary>The time this lease spent loading it: zero when it was loaded already.</summary>
    public TimeSpan LoadDuration { get; }

    /// <summary>Whether this lease loaded it.</summary>
    public bool Loaded => LoadDuration > TimeSpan.Zero;

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Dispose();
}

/// <summary>
/// How the plug-ins reach a model, registered in <see cref="ModelProviders"/>: "local" (Craft.Ai: a name resolved by
/// Idrak's model sources, loaded once on this machine and kept for its keep-alive) or, later, "remote". A provider never
/// resolves names itself.
/// </summary>
public interface IModelProvider : IKind
{
    /// <summary>
    /// The model of <paramref name="request"/>, loaded (or shared when loaded already), reporting its stages to
    /// <paramref name="progress"/>. Runs its work off the calling thread.
    /// </summary>
    /// <exception cref="NotRegisteredException">The name's model source, or the model's family, is not registered.</exception>
    Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the provider holds now, for the shell (the title bar's model chip, the status bar's device, memory and speed,
    /// the Models and devices page): <see cref="ModelProviderStatus.None"/> by default.
    /// </summary>
    ModelProviderStatus Status => ModelProviderStatus.None;

    /// <summary>Raised when <see cref="Status"/> changed, on any thread (the shell marshals to its UI thread). Never by default.</summary>
    event EventHandler? StatusChanged
    {
        add { }
        remove { }
    }
}

/// <summary>A model by its role ("Chat and agent", "Inline completion"): the name set for it and its state.</summary>
/// <param name="Role">The role.</param>
/// <param name="Model">The model's name, or null when none is set.</param>
/// <param name="State">Its state ("Loaded · chat · text", "not loaded").</param>
/// <param name="IsProblem">Whether the state is a problem to show as one (a family not registered, a load that failed).</param>
public sealed record ModelRole(string Role, string? Model, string State, bool IsProblem = false);

/// <summary>A setting the Models page edits for the provider: its variable, its label, and a hint for an empty value.</summary>
/// <param name="Variable">The setting's variable ("CRAFT_PLUGIN_AI_MODEL").</param>
/// <param name="Label">The label ("Model name").</param>
/// <param name="Placeholder">What an empty field shows ("all cores").</param>
public sealed record ModelSettingField(string Variable, string Label, string? Placeholder = null);

/// <summary>
/// What a model provider holds now (<see cref="IModelProvider.Status"/>): where it runs, the models by role, the device in
/// use and what it measured, and the settings the Models page edits. Every value the provider has not measured is null,
/// so the shell shows nothing rather than a guess.
/// </summary>
public sealed record ModelProviderStatus
{
    /// <summary>Nothing set or loaded.</summary>
    public static ModelProviderStatus None { get; } = new();

    /// <summary>Where it runs, as the user reads it ("local").</summary>
    public string Where { get; init; } = "";

    /// <summary>The models by role.</summary>
    public IReadOnlyList<ModelRole> Roles { get; init; } = [];

    /// <summary>The device in use ("vulkan:0"), or null before a model is loaded.</summary>
    public string? Device { get; init; }

    /// <summary>The device memory the loaded models use, or null when not measured.</summary>
    public long? DeviceMemoryBytes { get; init; }

    /// <summary>The last generation's speed, or null when nothing was generated.</summary>
    public double? TokensPerSecond { get; init; }

    /// <summary>The last generation's time to its first token, or null.</summary>
    public TimeSpan? FirstToken { get; init; }

    /// <summary>The command that loads the chat model, or null.</summary>
    public string? LoadCommand { get; init; }

    /// <summary>The setting that names the chat model (the page's "Add a model" sets it), or null.</summary>
    public ModelSettingField? ModelSetting { get; init; }

    /// <summary>The setting of the device to run on, or null.</summary>
    public ModelSettingField? DeviceSetting { get; init; }

    /// <summary>The limits the user may set (threads, memory caps), each a setting.</summary>
    public IReadOnlyList<ModelSettingField> Limits { get; init; } = [];

    /// <summary>The chat model's name, or null.</summary>
    public string? ChatModel => Roles.Count > 0 ? Roles[0].Model : null;
}

/// <summary>
/// The model providers, by name (ignoring case). Craft's library registers none; the AI plug-in (Craft.Ai) registers
/// "local". A name that is not registered fails with "not registered", naming those that are.
/// </summary>
public static class ModelProviders
{
    /// <summary>The provider on this machine.</summary>
    public const string Local = "local";

    private static readonly KindTable<IModelProvider> Table = new(nameof(ModelProviders), "Model provider", "add it with ModelProviders.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a provider holds the models it loaded and shares them between leases");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(IModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IModelProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IModelProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IModelProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}
