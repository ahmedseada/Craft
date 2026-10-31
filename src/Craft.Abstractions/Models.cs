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
