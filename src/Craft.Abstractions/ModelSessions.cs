// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// One session's own model settings (a chat conversation's, an agent session's; the owner, 2026-11-01, "2a"): each null
/// field takes the shared setting. The sampling fields apply to the session's next request with no reload; the load-time
/// fields (<see cref="Context"/>, <see cref="Weights"/>, <see cref="Device"/>, <see cref="Offload"/>) give the session a
/// copy of its own, loaded only after the user confirms its measured cost.
/// </summary>
public sealed record ModelSessionSettings
{
    /// <summary>No field of its own: every one the shared setting.</summary>
    public static ModelSessionSettings None { get; } = new();

    /// <summary>The sampling temperature (0: the likeliest token).</summary>
    public float? Temperature { get; init; }

    /// <summary>Sample among the k likeliest tokens (0: all).</summary>
    public int? TopK { get; init; }

    /// <summary>Sample among the likeliest tokens holding this share of the probability (1: all).</summary>
    public float? TopP { get; init; }

    /// <summary>Leave out tokens less likely than this share of the likeliest one's probability (0: none left out).</summary>
    public float? MinP { get; init; }

    /// <summary>How much a token seen recently is held back (1: not at all).</summary>
    public float? RepeatPenalty { get; init; }

    /// <summary>The random seed (the same seed and prompt, the same reply); null: the shared one (random when unset).</summary>
    public int? Seed { get; init; }

    /// <summary>The most tokens a reply (an agent's turn) samples.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>The context window in tokens (0: the model's own). A load-time setting.</summary>
    public int? Context { get; init; }

    /// <summary>The weights once loaded ("auto", "float32", "bfloat16", "int8", "int4"). A load-time setting.</summary>
    public string? Weights { get; init; }

    /// <summary>The device ("auto", "cpu", "cuda:0"). A load-time setting.</summary>
    public string? Device { get; init; }

    /// <summary>Whether part of the model may live in system memory when the device is full. A load-time setting.</summary>
    public bool? Offload { get; init; }

    /// <summary>The draft model (speculative decoding): a model's name, "off", or null for the shared one.</summary>
    public string? Draft { get; init; }

    /// <summary>Whether every field is the shared setting.</summary>
    public bool IsEmpty => Equals(None);

    /// <summary>Whether a load-time field is set (the session then has a copy of its own).</summary>
    public bool ChangesLoad => Context is not null || Weights is not null || Device is not null || Offload is not null;

    /// <summary>The load-time fields of this, every other field the shared setting.</summary>
    public ModelSessionSettings LoadOnly => new() { Context = Context, Weights = Weights, Device = Device, Offload = Offload };
}

/// <summary>The shared settings a session's own ones replace, every field filled (what the panel shows beside each field, and Reset returns to).</summary>
/// <param name="Model">The role's model (empty for none).</param>
/// <param name="Temperature">The shared temperature.</param>
/// <param name="TopK">The shared top-k.</param>
/// <param name="TopP">The shared top-p.</param>
/// <param name="MinP">The shared min-p.</param>
/// <param name="RepeatPenalty">The shared repeat penalty.</param>
/// <param name="Seed">The shared seed, or null for random.</param>
/// <param name="MaxTokens">The shared reply limit.</param>
/// <param name="Context">The shared context window (0: the model's own).</param>
/// <param name="Weights">The shared weights setting ("auto", "int8"…).</param>
/// <param name="Device">The shared device setting ("auto", "cuda:0"…).</param>
/// <param name="Offload">The shared offload setting.</param>
/// <param name="Draft">The shared draft setting (empty: the completion model when it matches; "off").</param>
public sealed record ModelSessionShared(string Model, float Temperature, int TopK, float TopP, float MinP, float RepeatPenalty, int? Seed, int MaxTokens,
    int Context, string Weights, string Device, bool Offload, string Draft)
{
    /// <summary>The devices to pick from (the provider's, "auto" first).</summary>
    public IReadOnlyList<string> Devices { get; init; } = ["auto"];

    /// <summary>The weights to pick from.</summary>
    public IReadOnlyList<string> WeightChoices { get; init; } = ["auto", "float32", "bfloat16", "int8", "int4"];
}

/// <summary>Where a session's settings are written when the user applies them to the environment.</summary>
public enum ModelSettingsScope
{
    /// <summary>The user's settings (the store the settings page uses).</summary>
    User,

    /// <summary>The open workspace's <c>.craft/settings.json</c>.</summary>
    Workspace,
}

/// <summary>
/// A conversation panel whose session can have model settings of its own (the chat's conversation, the agent's session),
/// edited from its model chip: <see cref="SaveModelSessionAsync"/> keeps them with the session only (a load-time change
/// loads a separate copy after the user's confirm, showing its measured cost); <see cref="ApplyModelSessionToEnvironmentAsync"/>
/// writes them as the shared settings. A plug-in's panel implements it beside <see cref="IConversationPanel"/>.
/// </summary>
public interface IModelSessionHost
{
    /// <summary>What the session is called in the panel ("this chat", "this agent session").</summary>
    string ModelSessionTitle { get; }

    /// <summary>The session's own settings now (<see cref="ModelSessionSettings.None"/> when it takes the shared ones).</summary>
    ModelSessionSettings ModelSession { get; }

    /// <summary>The shared settings now, every field filled.</summary>
    ModelSessionShared ModelShared { get; }

    /// <summary>What a separate copy for <paramref name="proposed"/> would take, measured now; null when it has no load-time field or the provider cannot tell.</summary>
    Task<ModelMemoryEstimate?> EstimateModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps <paramref name="proposed"/> as the session's own settings (the shared settings unchanged). Sampling applies to
    /// the next request; a load-time change is asked first (its measured cost, whether it fits beside the copies loaded,
    /// offload or a smaller format offered when it does not) and its copy loaded only when the user says yes. False when
    /// the user said no (nothing changed).
    /// </summary>
    Task<bool> SaveModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="proposed"/>'s fields as the shared settings in <paramref name="scope"/> (and the session takes
    /// the shared ones again); what it says, or the first refusal.
    /// </summary>
    Task<string> ApplyModelSessionToEnvironmentAsync(ModelSessionSettings proposed, ModelSettingsScope scope, CancellationToken cancellationToken = default);

    /// <summary>Raised when <see cref="ModelSession"/> or <see cref="ModelShared"/> changed.</summary>
    event EventHandler? ModelSessionChanged;
}
