// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// One chat conversation's or agent session's own model settings (the owner, 2026-11-01, "2a"), shared by the chat and
/// the agent panels: the sampling its requests take (no reload), its own lease when it sets load-time fields (a separate
/// copy, loaded only after the user confirms its measured cost; one copy per distinct model, weights, device, context and
/// offload, note 25), and Apply to the environment (the shared settings written, the session back on them). Used on the UI thread.
/// </summary>
public sealed class SessionModel
{
    private readonly IEditorHost _host;
    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;

    /// <summary>The session of <paramref name="role"/> (<see cref="ModelRoleKeys.Chat"/> or <see cref="ModelRoleKeys.Agent"/>), called <paramref name="title"/>.</summary>
    public SessionModel(IEditorHost host, Func<AiSettings> settings, AiModels models, string role, string title)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        Role = role;
        Title = title;
    }

    /// <summary>The role ("chat", "agent").</summary>
    public string Role { get; }

    /// <summary>What the session is called ("this chat").</summary>
    public string Title { get; }

    /// <summary>The session's id (its own lease's holder).</summary>
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Its own settings now.</summary>
    public ModelSessionSettings Settings { get; private set; } = ModelSessionSettings.None;

    /// <summary>Raised when <see cref="Settings"/> changed (the panel keeps them, the chip shows them).</summary>
    public event EventHandler? Changed;

    /// <summary>Whether its requests take a lease of their own (a load-time or draft field set).</summary>
    public bool OwnLease => Settings.ChangesLoad || Settings.Draft is not null;

    /// <summary>The shared settings now, every field filled.</summary>
    public ModelSessionShared Shared
    {
        get
        {
            var settings = _settings();
            var role = settings.RoleOf(Role);
            bool agent = Role == ModelRoleKeys.Agent;
            var devices = ModelProviders.Find(settings.ModelProvider) is { } provider ? provider.Status.Devices : [];
            return new ModelSessionShared(role.Model, role.Temperature, role.TopK, role.TopP, role.MinP, role.RepeatPenalty, role.Seed, role.MaxTokens, role.Context,
                role.Weights, role.Device, role.Offload, agent ? (settings.AgentDraftModel.Length > 0 ? settings.AgentDraftModel : settings.DraftModel) : settings.DraftModel)
            {
                Devices = ["auto", .. devices.Where(d => !d.Equals("auto", StringComparison.OrdinalIgnoreCase))],
            };
        }
    }

    /// <summary>The lease its next request takes: its own when it sets load-time or draft fields, else the role's.</summary>
    public Task<ModelLease> LeaseAsync(CancellationToken cancellationToken) => OwnLease
        ? _models.SessionAsync(Role, Id, Settings, cancellationToken)
        : _models.RoleAsync(Role, cancellationToken);

    /// <summary>Its sampling for a request: the role's settings with its own fields over them.</summary>
    public GenerationOptions Sampling(int context, IReadOnlyList<string> stop) => _settings().RoleOf(Role).Sampling(Settings, context, stop);

    /// <summary>Takes <paramref name="kept"/> as the session's own (a chat conversation opened again): the copy of the session left goes.</summary>
    public void Restore(ModelSessionSettings kept)
    {
        ArgumentNullException.ThrowIfNull(kept);
        _models.EndSession(Role, Id);
        Id = Guid.NewGuid().ToString("N");
        Settings = kept;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The session ended (a new conversation, the panel closed): its own copy goes, its settings with it.</summary>
    public void End()
    {
        _models.EndSession(Role, Id);
        Id = Guid.NewGuid().ToString("N");
        if (!Settings.IsEmpty)
        {
            Settings = ModelSessionSettings.None;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What a copy of its own with <paramref name="proposed"/>'s load-time fields would take, measured now; null when none is needed or it cannot be told.</summary>
    public async Task<ModelMemoryEstimate?> EstimateAsync(ModelSessionSettings proposed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        if (!proposed.ChangesLoad)
        {
            return null;
        }

        try
        {
            return await _models.EstimateAsync(Role, proposed, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _host.Log(EditorLogLevel.Warning, $"The memory of {Title}'s copy could not be measured: {error.Message}", error);
            return null;
        }
    }

    /// <summary>
    /// Keeps <paramref name="proposed"/> as the session's own (the shared settings unchanged). A load-time change is asked
    /// first: the copy's measured cost and whether it fits beside the copies loaded; not fitting, offload and int4 are
    /// offered. The copy then loads (an activity), the request waiting for it. False when the user said no.
    /// </summary>
    public async Task<bool> SaveAsync(ModelSessionSettings proposed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        bool loadChanged = proposed.ChangesLoad && !Equals(proposed.LoadOnly, Settings.LoadOnly);
        if (loadChanged)
        {
            var estimate = await EstimateAsync(proposed, cancellationToken).ConfigureAwait(true);
            var chosen = await ConfirmAsync(proposed, estimate, cancellationToken).ConfigureAwait(true);
            if (chosen is null)
            {
                return false;
            }

            proposed = chosen;
        }

        Settings = proposed;
        if (!OwnLease)
        {
            _models.EndSession(Role, Id);                              // back on the role's copy: its own goes
        }
        else if (loadChanged)
        {
            _ = LoadAsync();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Writes <paramref name="proposed"/>'s fields as the shared settings of the role into <paramref name="scope"/>, then the
    /// session takes the shared settings again (its own copy goes); what it says, or the first refusal.
    /// </summary>
    public async Task<string> ApplyToEnvironmentAsync(ModelSessionSettings proposed, ModelSettingsScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var written = new List<string>();
        foreach (var (key, value) in Fields(proposed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _host.Settings.SetAsync(AiSettings.PluginName, key, value, scope).ConfigureAwait(true) is { } refused)
            {
                return written.Count == 0 ? refused : $"{refused} (written before it: {string.Join(", ", written)})";
            }

            written.Add(PluginSetting.VariableName(AiSettings.PluginName, key));
        }

        if (written.Count == 0)
        {
            return $"{Title} has no setting of its own to apply.";
        }

        _models.EndSession(Role, Id);
        Settings = ModelSessionSettings.None;
        Changed?.Invoke(this, EventArgs.Empty);
        string where = scope == ModelSettingsScope.Workspace ? "this workspace's .craft/settings.json" : "your settings";
        return $"Written to {where} as the shared settings: {string.Join(", ", written)}. {char.ToUpperInvariant(Title[0])}{Title[1..]} takes the shared settings again.";
    }

    /// <summary>The role's setting keys and values <paramref name="proposed"/>'s set fields are written as (the chat's: chatTopK…; the agent's: agentTopK…).</summary>
    public IReadOnlyList<(string Key, string Value)> Fields(ModelSessionSettings proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        bool agent = Role == ModelRoleKeys.Agent;
        var culture = CultureInfo.InvariantCulture;
        var fields = new List<(string, string)>();
        void Add(string chat, string agentKey, string? value)
        {
            if (value is not null)
            {
                fields.Add((agent ? agentKey : chat, value));
            }
        }

        Add("chatTemperature", "agentTemperature", proposed.Temperature?.ToString(culture));
        Add("chatTopK", "agentTopK", proposed.TopK?.ToString(culture));
        Add("chatTopP", "agentTopP", proposed.TopP?.ToString(culture));
        Add("chatMinP", "agentMinP", proposed.MinP?.ToString(culture));
        Add("chatRepeatPenalty", "agentRepeatPenalty", proposed.RepeatPenalty?.ToString(culture));
        Add("chatSeed", "agentSeed", proposed.Seed?.ToString(culture));
        Add("chatMaxTokens", "agentMaxTokens", proposed.MaxTokens?.ToString(culture));
        Add("context", "agentContext", proposed.Context?.ToString(culture));
        Add("weights", "agentWeights", proposed.Weights);
        Add("device", "agentDevice", proposed.Device);
        Add("offload", "agentOffload", proposed.Offload is { } offload ? (offload ? "true" : "false") : null);
        Add("draftModel", "agentDraftModel", proposed.Draft);
        return fields;
    }

    /// <summary>The session's settings as kept with it (a chat conversation's "model"); null when it has none of its own.</summary>
    public static JsonObject? Write(ModelSessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsEmpty)
        {
            return null;
        }

        return new JsonObject
        {
            ["temperature"] = settings.Temperature, ["topK"] = settings.TopK, ["topP"] = settings.TopP, ["minP"] = settings.MinP,
            ["repeatPenalty"] = settings.RepeatPenalty, ["seed"] = settings.Seed, ["maxTokens"] = settings.MaxTokens, ["context"] = settings.Context,
            ["weights"] = settings.Weights, ["device"] = settings.Device, ["offload"] = settings.Offload, ["draft"] = settings.Draft,
        };
    }

    /// <summary>The settings <see cref="Write"/> kept; none when <paramref name="node"/> is none.</summary>
    public static ModelSessionSettings Read(JsonNode? node)
    {
        if (node is not JsonObject json)
        {
            return ModelSessionSettings.None;
        }

        return new ModelSessionSettings
        {
            Temperature = json["temperature"]?.GetValue<float>(), TopK = json["topK"]?.GetValue<int>(), TopP = json["topP"]?.GetValue<float>(),
            MinP = json["minP"]?.GetValue<float>(), RepeatPenalty = json["repeatPenalty"]?.GetValue<float>(), Seed = json["seed"]?.GetValue<int>(),
            MaxTokens = json["maxTokens"]?.GetValue<int>(), Context = json["context"]?.GetValue<int>(), Weights = json["weights"]?.GetValue<string>(),
            Device = json["device"]?.GetValue<string>(), Offload = json["offload"]?.GetValue<bool>(), Draft = json["draft"]?.GetValue<string>(),
        };
    }

    /// <summary>The confirm's words for a copy of its own: what loads, its measured cost, and whether it fits beside the copies loaded.</summary>
    public string Describe(ModelSessionSettings proposed, ModelMemoryEstimate? estimate)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var role = _settings().RoleOf(Role).With(proposed);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(Title[0])}{Title[1..]} gets a copy of its own: {role.Model}, {role.Weights} weights on {role.Device}");
        text.Append(role.Context > 0 ? string.Create(CultureInfo.InvariantCulture, $", context {role.Context}") : "").Append(role.Offload ? ", offload on" : "").Append(". The other sessions keep theirs.\n");
        if (estimate is null)
        {
            text.Append("Its memory could not be measured before the load (the model's size is not known here).");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture,
                $"It takes {ModelMemoryEstimate.Gigabytes(estimate.NeededBytes)} ({estimate.Weights}{(estimate.Auto ? " (auto)" : "")}: {ModelMemoryEstimate.Gigabytes(estimate.WeightBytes)} of weights, {ModelMemoryEstimate.Gigabytes(estimate.WorkingBytes)} of working memory)");
            text.Append(estimate.FreeBytes is { } free
                ? $"; {ModelMemoryEstimate.Gigabytes(free)} is free on {estimate.Device} beside the copies loaded: {(estimate.Fits ? "it fits." : "it does NOT fit.")}"
                : $"; {estimate.Device} reports no free memory.");
            if (estimate.Refusal is not null || !estimate.Fits)
            {
                text.Append("\nLoad it with offload (part of it in system memory, slower) or a smaller format, or keep the shared copy.");
            }
        }

        return text.ToString();
    }

    // The confirm: yes, offload or int4 when it does not fit, or no (null).
    private async Task<ModelSessionSettings?> ConfirmAsync(ModelSessionSettings proposed, ModelMemoryEstimate? estimate, CancellationToken cancellationToken)
    {
        bool fits = estimate is null || (estimate.Fits && estimate.Refusal is null);
        var answers = new List<string>();
        var outcomes = new List<ModelSessionSettings?>();
        if (fits)
        {
            answers.Add("Load it");
            outcomes.Add(proposed);
        }
        else
        {
            answers.Add("Load with offload");
            outcomes.Add(proposed with { Offload = true });
            if (!string.Equals(estimate!.Weights, "int4", StringComparison.OrdinalIgnoreCase))
            {
                answers.Add("Use int4");
                outcomes.Add(proposed with { Weights = "int4" });
            }
        }

        answers.Add("Cancel");
        outcomes.Add(null);
        var question = new EditorQuestion(fits ? $"A separate copy for {Title}?" : $"It does not fit beside the copies loaded", Describe(proposed, estimate), answers)
        {
            Dismissed = answers.Count - 1,
        };
        int answer = await _host.AskAsync(question, cancellationToken).ConfigureAwait(true);
        return answer >= 0 && answer < outcomes.Count ? outcomes[answer] : null;
    }

    // The copy loads now (the user confirmed it), as an activity; a failure is a notification, the session keeps its settings.
    private async Task LoadAsync()
    {
        try
        {
            await Task.Run(() => _models.SessionAsync(Role, Id, Settings, CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _host.Notify($"{char.ToUpperInvariant(Title[0])}{Title[1..]}: its own copy did not load", AiErrors.Describe(error, _settings()), NotificationSeverity.Error);
        }
    }
}
