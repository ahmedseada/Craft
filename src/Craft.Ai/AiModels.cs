// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Idrak.Abstraction;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// The models the AI plug-ins use, through the provider of the setting <c>modelProvider</c> (Craft's
/// <see cref="ModelProviders"/>; names resolved by Idrak's model sources, never here). Three roles, each with its own
/// settings (note 25, <see cref="AiSettings.ChatRole"/>, <see cref="AiSettings.AgentRole"/>,
/// <see cref="AiSettings.CompletionRole"/>): the chat's (the chat, the code actions, the load command), the agent's (by
/// default the chat's model and device) and inline completion's (a small one). Nothing loads by itself: the chat's and
/// the agent's model load when the user asks for them (a message sent, an action picked, the composer's model chip, the
/// load command); completion's on its first request when <c>completionLoad</c> allows it. A role turned off loads
/// nothing. Each role's lease is kept under everything its load depends on (the provider, the name, the adapter, the
/// device, the context, the weights, the keep-alive), so a role whose settings change lets its old copy go before the
/// next is loaded (the owner's Windows run: a second copy of a 7B model beside the first filled a 16 GB card), and two
/// roles that name the same share one lease, one copy. A failed load is not kept: the user's next request tries again,
/// but completion does not (a failure on every pause in typing), until its settings change or the model is loaded
/// on purpose. Any thread.
/// </summary>
public sealed class AiModels : IDisposable
{
    private readonly Func<AiSettings> _settings;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<ModelLease>> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _roles = new(StringComparer.Ordinal);   // role key → its lease's key
    private readonly Dictionary<string, DraftPair> _drafts = new(StringComparer.Ordinal);   // a target's key and its draft's plan → the pair
    private bool _disposed;

    /// <summary>Models read through the settings <paramref name="settings"/> gives at each request.</summary>
    public AiModels(Func<AiSettings> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ModelAdapters.Changed += OnAdaptersChanged;
    }

    /// <summary>Reports a load's stages (set by the session: the models panel), or null.</summary>
    public IProgress<ModelLoadStatus>? Progress { get; set; }

    /// <summary>
    /// Where a load shows (phase U: the host's activities, so the status bar shows the model's download and load with its
    /// stage, time and Cancel, and the notification centre how it ended, a failure with Retry); none by default.
    /// </summary>
    public IEditorActivities Activities { get; set; } = NoActivities.Instance;

    /// <summary>
    /// The chat model's lease (the chat's settings: <c>model</c>, <c>device</c>…), loading it when it is not loaded: only
    /// for what the user asked for (a message, an action, the chip, the load command). A failure of an earlier load of it
    /// is forgotten (this is the try again).
    /// </summary>
    /// <exception cref="InvalidOperationException">No model is set, or the chat is turned off (<c>chatEnabled</c>).</exception>
    public Task<ModelLease> ChatAsync(CancellationToken cancellationToken) => RoleAsync(ModelRoleKeys.Chat, cancellationToken);

    /// <summary>
    /// The agent's lease (its settings: <c>agentModel</c>, <c>agentDevice</c>…, each empty one the chat's), loading it
    /// when it is not loaded; the chat's lease itself when they name the same.
    /// </summary>
    /// <exception cref="InvalidOperationException">No model is set, or the agent is turned off (<c>agentEnabled</c>).</exception>
    public Task<ModelLease> AgentAsync(CancellationToken cancellationToken) => RoleAsync(ModelRoleKeys.Agent, cancellationToken);

    /// <summary>
    /// The lease of the role <paramref name="role"/> (<see cref="ModelRoleKeys.Chat"/> or <see cref="ModelRoleKeys.Agent"/>),
    /// loading it when it is not loaded; its earlier failure forgotten.
    /// </summary>
    /// <exception cref="InvalidOperationException">No model is set for it, or it is turned off.</exception>
    public Task<ModelLease> RoleAsync(string role, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var resolved = settings.RoleOf(role);
        if (!resolved.Enabled)
        {
            LetGo(role);
            return Task.FromException<ModelLease>(new InvalidOperationException(Off(role)));
        }

        var (key, request) = KeyOf(settings, resolved, resolved.Model);
        lock (_gate)
        {
            _failed.Remove(key);
            Retarget(role, key);
        }

        return LeaseWithDraftAsync(settings, role, resolved, key, request, cancellationToken);
    }

    /// <summary>
    /// The lease of the session <paramref name="session"/> of the role <paramref name="role"/> with its own load-time and
    /// draft settings <paramref name="overrides"/> (the owner, 2026-11-01, "2a"): a copy of its own when they make another
    /// lease key, the role's (or any other holder's) copy when they make the same; held under the session until
    /// <see cref="EndSession"/>. Loads only when the session's user confirmed it (the panel asks first).
    /// </summary>
    /// <exception cref="InvalidOperationException">No model is set for the role, or it is turned off.</exception>
    public Task<ModelLease> SessionAsync(string role, string session, ModelSessionSettings overrides, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var settings = _settings();
        var resolved = settings.RoleOf(role);
        if (!resolved.Enabled)
        {
            return Task.FromException<ModelLease>(new InvalidOperationException(Off(role)));
        }

        var own = resolved.With(overrides);
        var (key, request) = KeyOf(settings, own, own.Model);
        string holder = SessionHolder(role, session);
        lock (_gate)
        {
            _failed.Remove(key);
            Retarget(holder, key);
        }

        return LeaseWithDraftAsync(settings, holder, own, key, request, cancellationToken);
    }

    /// <summary>Lets the session's own copy go (the session ended, or went back to the shared settings), unless another holder holds it.</summary>
    public void EndSession(string role, string session) => LetGo(SessionHolder(role, session));

    /// <summary>Whether the session's own copy (its load-time settings) is loaded.</summary>
    public bool IsSessionLoaded(string role, ModelSessionSettings overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var settings = _settings();
        var own = settings.RoleOf(role).With(overrides);
        if (own.Model.Length == 0)
        {
            return false;
        }

        var (key, _) = KeyOf(settings, own, own.Model);
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var lease) && lease.IsCompletedSuccessfully;
        }
    }

    /// <summary>
    /// What a copy of the role <paramref name="role"/> with <paramref name="overrides"/> would take on its device, measured
    /// now by the provider (nothing loaded); null when the provider cannot tell or no model is set.
    /// </summary>
    public Task<ModelMemoryEstimate?> EstimateAsync(string role, ModelSessionSettings overrides, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var settings = _settings();
        var own = settings.RoleOf(role).With(overrides);
        if (own.Model.Length == 0)
        {
            return Task.FromResult<ModelMemoryEstimate?>(null);
        }

        var (_, request) = KeyOf(settings, own, own.Model);
        return ModelProviders.Get(settings.ModelProvider).EstimateAsync(request, cancellationToken);
    }

    /// <summary>The leases held now (one a distinct model, weights, device, context and offload; for the tests and the status).</summary>
    public int LeaseCount
    {
        get
        {
            lock (_gate)
            {
                return _leases.Count;
            }
        }
    }

    // Who holds a session's lease: the role and the session.
    private static string SessionHolder(string role, string session) => role + "@" + session;

    /// <summary>
    /// Raised when a role's draft changed (speculative decoding: it loaded, was refused, or a reply measured how many of its
    /// tokens were kept), on any thread: the chip and the status bar read <see cref="DraftOf"/> again.
    /// </summary>
    public event EventHandler? DraftChanged;

    /// <summary>
    /// The draft of the role <paramref name="role"/> under the settings now (speculative decoding, <c>draftModel</c>): the
    /// model drafting and the share of its tokens kept so far, or why none drafts (turned off, none to draft with, another
    /// vocabulary, loading); null when the role has no model or is turned off.
    /// </summary>
    public DraftStatus? DraftOf(string role)
    {
        var settings = _settings();
        if (role is not (ModelRoleKeys.Chat or ModelRoleKeys.Agent))
        {
            return null;
        }

        var resolved = settings.RoleOf(role);
        if (!resolved.Enabled || resolved.Model.Length == 0)
        {
            return null;
        }

        var plan = DraftPlan(settings, resolved);
        if (plan.Key is null)
        {
            return new DraftStatus(null, plan.Off, null);
        }

        var (key, _) = KeyOf(settings, resolved, resolved.Model);
        lock (_gate)
        {
            if (_drafts.TryGetValue(PairKey(key, plan), out var pair))
            {
                return pair.Off is { } off ? new DraftStatus(null, off, null)
                    : SamplerRefusal() is { } sampler ? new DraftStatus(null, sampler, null)
                    : pair.Ready is null ? new DraftStatus(null, $"{plan.Name} is loading to draft", null)
                    : new DraftStatus(plan.Name, null, pair.Acceptance);
            }
        }

        return new DraftStatus(null, $"{plan.Name} drafts once the {role} model loads", null);
    }

    // The role's entry its draft's lease is held under (beside the role's own, so a copy shared with completion stays while either holds it).
    private static string DraftRole(string role) => role + ":draft";

    private static string PairKey(string target, DraftPlanned plan) =>
        string.Join('\n', target, ">", plan.Key, plan.Tokens.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // What drafts for a role (speculative decoding): its draftModel, else the completion model (its own lease key: one copy
    // shared with completion) while inline completion is on; none when turned off or when it would be the role's own model.
    private static DraftPlanned DraftPlan(AiSettings settings, AiRoleSettings role)
    {
        string variable = role.Key == ModelRoleKeys.Agent ? "agentDraftModel" : "draftModel";
        if (role.DraftOff)
        {
            return DraftPlanned.None($"off ({variable})");
        }

        var completion = settings.CompletionRole;
        string name = role.DraftModel;
        if (name.Length == 0)
        {
            if (!completion.Enabled)
            {
                return DraftPlanned.None($"none: inline completion is off, so no completion model drafts (set {variable})");
            }

            if (completion.Model.Length == 0)
            {
                return DraftPlanned.None($"none: inline completion has no model of its own (set completionModel or {variable})");
            }

            name = completion.Model;
        }

        // Never the target itself: the same copy (one lease key), or the same model with the same weights (a second copy
        // drafting exactly what the target computes, its cache beside the target's, for no gain).
        var on = name.Equals(completion.Model, StringComparison.OrdinalIgnoreCase) ? completion : role with { Model = name };
        var (key, request) = KeyOf(settings, on, name);
        if (name.Equals(role.Model, StringComparison.OrdinalIgnoreCase) && string.Equals(on.Weights, role.Weights, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, KeyOf(settings, role, role.Model).Key, StringComparison.Ordinal))
        {
            return DraftPlanned.None(Itself(role.Key));
        }

        return new DraftPlanned(name, key, request, role.DraftTokens, null);
    }

    // Why a draft that is the role's model itself is not used, as the chip's and the status bar's tooltip say it.
    private static string Itself(string role) => $"the draft would be the {role} model itself: no gain";

    // The role's lease with its draft: the lease itself when the provider lends no draft or none is planned; else one lease
    // per target and draft whose chat model passes the draft on with each request once the draft is ready (loaded after the
    // role's model, off this request: nothing waits for it), shared by the chat and the agent when they plan the same.
    private async Task<ModelLease> LeaseWithDraftAsync(AiSettings settings, string role, AiRoleSettings resolved, string key, ModelRequest request,
        CancellationToken cancellationToken)
    {
        var lease = await LeaseAsync(settings, key, request, cancellationToken).ConfigureAwait(false);
        var plan = DraftPlan(settings, resolved);
        if (lease.Draft is null || lease.Chat is null || plan.Key is null)
        {
            LetGo(DraftRole(role));
            return lease;
        }

        DraftPair pair;
        bool start = false;
        lock (_gate)
        {
            foreach (var stale in _drafts.Where(p => !_leases.ContainsKey(p.Value.TargetKey)).Select(p => p.Key).ToList())
            {
                _drafts.Remove(stale);                                    // a target let go: its pairs go with it
            }

            string pairKey = PairKey(key, plan);
            if (!_drafts.TryGetValue(pairKey, out pair!) || !ReferenceEquals(pair.Target, lease))
            {
                pair = new DraftPair(this, key, plan.Name, lease, plan.Tokens);
                _drafts[pairKey] = pair;
                start = true;
            }

            if (pair.Off is null)
            {
                Retarget(DraftRole(role), plan.Key);
            }
        }

        if (start)
        {
            _ = LoadDraftAsync(settings, role, plan, pair);
        }

        return pair.Lease;
    }

    // The draft's lease (the completion model's own when it is that one), checked against the target's vocabulary; a draft
    // that fails to load or is refused leaves the role generating alone, with the reason for the chip's tooltip.
    private async Task LoadDraftAsync(AiSettings settings, string role, DraftPlanned plan, DraftPair pair)
    {
        try
        {
            var draft = await LeaseAsync(settings, plan.Key!, plan.Request!, CancellationToken.None).ConfigureAwait(false);
            var target = pair.Target;
            if (ReferenceEquals(draft, target) || draft.Draft is { } same && ReferenceEquals(same, target.Draft)
                || draft.Underlying is { } copy && ReferenceEquals(copy, target.Underlying))
            {
                pair.Off = Itself(role);                                     // the provider lent the target's own copy
            }
            else if (draft.Draft is not { } model)
            {
                pair.Off = $"{plan.Name} drafts nothing (its provider lends no draft)";
            }
            else if (SpeculativeDecoding.Refusal(pair.Target.Draft!.Tokenizer, model) is { } refusal)
            {
                pair.Off = $"{plan.Name} cannot draft for {pair.Target.Model}: {refusal}";
            }
            else
            {
                pair.Ready = model;
            }
        }
#pragma warning disable CA1031 // A draft that does not load leaves the role generating alone; the reason shows on the chip's tooltip.
        catch (Exception error)
#pragma warning restore CA1031
        {
            pair.Off = $"{plan.Name} did not load to draft: {(error is OperationCanceledException ? "cancelled" : AiErrors.Describe(error, settings))}";
        }

        if (pair.Off is not null)
        {
            LetGo(DraftRole(role));
        }

        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The draft a role's replies use, as the shell shows it.</summary>
    /// <param name="Model">The model drafting now, or null when none does.</param>
    /// <param name="State">Why none drafts, or that it is loading; null when it drafts.</param>
    /// <param name="Acceptance">The share of its tokens kept over the replies so far (0 to 1), or null before one was measured.</param>
    public sealed record DraftStatus(string? Model, string? State, double? Acceptance);

    // A role's planned draft: its name and lease (key and request) and the tokens a round, or why there is none.
    private sealed record DraftPlanned(string Name, string? Key, ModelRequest? Request, int Tokens, string? Off)
    {
        public static DraftPlanned None(string why) => new("", null, null, 0, why);
    }

    // A target's lease and its draft: the lease the roles get (its chat model passing the draft on), the draft once it is
    // ready or why it is not, and the tokens the replies proposed and kept (the library's statistics of each reply).
    private sealed class DraftPair(AiModels owner, string targetKey, string name, ModelLease target, int tokens)
    {
        private long _proposed, _accepted;
        private ModelLease? _lease;

        public string TargetKey { get; } = targetKey;

        public string Name { get; } = name;

        public ModelLease Target { get; } = target;

        public int Tokens { get; } = tokens;

        public volatile IDraftModel? Ready;

        public volatile string? Off;

        public double? Acceptance
        {
            get
            {
                long proposed = Interlocked.Read(ref _proposed);
                return proposed > 0 ? (double)Interlocked.Read(ref _accepted) / proposed : null;
            }
        }

        public ModelLease Lease => _lease ??= new ModelLease(Target.Model, new DraftingChat(Target.Chat!, this), Target.Text, Target.ContextLength, Target.LoadDuration)
        {
            Tokenizer = Target.Tokenizer,
            ChatTemplate = Target.ChatTemplate,
            Underlying = Target.Underlying,
            Exclusive = Target.Exclusive,
            Draft = Target.Draft,
        };

        public void Record(GenerationStats stats)
        {
            Interlocked.Add(ref _proposed, stats.DraftTokens);
            Interlocked.Add(ref _accepted, stats.DraftAccepted);
            owner.DraftChanged?.Invoke(owner, EventArgs.Empty);
        }
    }

    // Why no draft can run while a sampler other than the library's samples (Idrak's TextGenerator refuses that pair), or null.
    private static string? SamplerRefusal() =>
        string.Equals(TokenSamplers.Origin(TokenSamplers.DefaultName), Overrides.Library, StringComparison.Ordinal)
            ? null
            : $"no draft: a sampler plug-in ({TokenSamplers.Origin(TokenSamplers.DefaultName)}) samples, and speculative decoding checks the draft by the library's own sampling rule";

    // The target's chat model with the draft passed on in each request's options once it is ready (GenerationOptions.Draft,
    // the role's draftTokens), each reply's statistics recorded for the chip.
    private sealed class DraftingChat(IChatModel inner, DraftPair pair) : IChatModel
    {
        public IReadOnlySet<string> PartKinds => inner.PartKinds;

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // A sampler plug-in that took the library's "default" sampler leaves the draft out (Idrak's check of a round
            // draws by the library's rule, and refuses another sampler: the owner's run 2026-11-01 ended every agent turn
            // on that refusal). Asked per request, so turning the plug-in off brings the draft back.
            if (pair.Ready is { } draft && SamplerRefusal() is null)
            {
                request = request with { Options = (request.Options ?? new GenerationOptions()) with { Draft = draft, DraftTokens = pair.Tokens } };
            }

            await foreach (var chunk in inner.StreamAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (chunk is { Done: true, Stats: { DraftTokens: > 0 } stats })
                {
                    pair.Record(stats);
                }

                yield return chunk;
            }
        }
    }

    /// <summary>What a role turned off says, with the setting that turns it on.</summary>
    public static string Off(string role) => role switch
    {
        ModelRoleKeys.Chat => "The chat is turned off (chatEnabled, CRAFT_PLUGIN_AI_CHATENABLED): turn it on in Settings › Chat.",
        ModelRoleKeys.Agent => "The agent is turned off (agentEnabled, CRAFT_PLUGIN_AI_AGENTENABLED): turn it on in Settings › Agent and sandbox.",
        _ => "Inline completion is turned off (completion, CRAFT_PLUGIN_AI_COMPLETION): turn it on in Settings › Completion.",
    };

    /// <summary>Whether the chat model of the settings now is loaded (its lease is ready).</summary>
    public bool IsChatLoaded => IsLoaded(ModelRoleKeys.Chat);

    /// <summary>Whether the agent's model of the settings now is loaded (its lease, or the chat's when the same, is ready).</summary>
    public bool IsAgentLoaded => IsLoaded(ModelRoleKeys.Agent);

    /// <summary>Whether the chat model of the settings now is loading.</summary>
    public bool IsChatLoading => IsLoading(ModelRoleKeys.Chat);

    /// <summary>Whether the model of the role <paramref name="role"/> (its settings now) is loaded.</summary>
    public bool IsLoaded(string role)
    {
        var settings = _settings();
        var resolved = settings.RoleOf(role);
        if (resolved.Model.Length == 0)
        {
            return false;
        }

        var (key, _) = KeyOf(settings, resolved, resolved.Model);
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var lease) && lease.IsCompletedSuccessfully;
        }
    }

    /// <summary>Whether the model of the role <paramref name="role"/> (its settings now) is loading.</summary>
    public bool IsLoading(string role)
    {
        var settings = _settings();
        var resolved = settings.RoleOf(role);
        if (resolved.Model.Length == 0)
        {
            return false;
        }

        var (key, _) = KeyOf(settings, resolved, resolved.Model);
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var lease) && !lease.IsCompleted;
        }
    }

    /// <summary>
    /// Inline completion's lease, or null when it has none to use now (<see cref="CompletionUnavailable"/> says why;
    /// turned off, it leases nothing and gives back what it held): <c>completionModel</c> on its own settings, loaded on
    /// this request when <c>completionLoad</c> allows it and its last load did not fail; else the chat's (or the agent's)
    /// model, only when it is loaded already (completion never loads it).
    /// </summary>
    public async Task<ModelLease?> CompletionAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        if (!settings.Completion)
        {
            LetGo(ModelRoleKeys.Completion);
            return null;
        }

        if (CompletionPlan(settings) is not { } plan)
        {
            return null;
        }

        lock (_gate)
        {
            Retarget(ModelRoleKeys.Completion, plan.Key);
        }

        try
        {
            return await LeaseAsync(settings, plan.Key, plan.Request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            lock (_gate)
            {
                // Not again on the next pause in typing: until the settings change, or the model is loaded on purpose.
                _failed[plan.Key] = AiErrors.Describe(error, settings, forCompletion: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Why inline completion cannot answer now (turned off, no model set, its model not loaded and not to be loaded by
    /// it, its last load failed), in words for the status bar; null when it can.
    /// </summary>
    public string? CompletionUnavailable
    {
        get
        {
            var settings = _settings();
            if (!settings.Completion)
            {
                return Off(ModelRoleKeys.Completion);
            }

            if (CompletionPlan(settings) is not null)
            {
                return null;
            }

            var own = settings.CompletionRole;
            if (own.Model.Length > 0)
            {
                var (key, _) = KeyOf(settings, own, own.Model);
                lock (_gate)
                {
                    if (_failed.TryGetValue(key, out var failure))
                    {
                        return failure;
                    }
                }

                return $"{own.Model} is not loaded, and completion does not load it (completionLoad is off): run 'AI: Load the local model' or turn completionLoad on.";
            }

            return settings.Model.Length == 0
                ? "No model is set: set completionModel (a small one) or pick a model at the composer."
                : $"Completion has no model of its own (completionModel is empty) and {settings.Model} is not loaded: it loads when you send a message or pick it at the composer.";
        }
    }

    /// <summary>Whether a model for completion is named (the provider answers only when <see cref="CompletionUnavailable"/> is null).</summary>
    public bool HasCompletionModel
    {
        get
        {
            var settings = _settings();
            return settings.Completion && (settings.CompletionModel.Length > 0 || settings.Model.Length > 0);
        }
    }

    /// <summary>
    /// Gives back the lease the role <paramref name="role"/> holds (a role turned off), unless another role holds the same;
    /// the provider then keeps the model for its keep-alive and unloads it.
    /// </summary>
    public void LetGo(string role)
    {
        Task<ModelLease>? dropped = null;
        lock (_gate)
        {
            if (_roles.Remove(role, out var old) && !_roles.ContainsValue(old) && _leases.Remove(old, out var lease))
            {
                dropped = lease;
            }
        }

        if (dropped is not null)
        {
            DisposeWhenLoaded(dropped);
        }

        if (!role.EndsWith(":draft", StringComparison.Ordinal))
        {
            LetGo(DraftRole(role));                                      // the role's draft goes with it (unless completion holds that copy)
        }
    }

    /// <summary>Forgets every failed load (the load command: the user asks again on purpose).</summary>
    public void ForgetFailures()
    {
        lock (_gate)
        {
            _failed.Clear();
        }
    }

    /// <summary>Gives back every lease.</summary>
    public void Release()
    {
        Task<ModelLease>[] held;
        lock (_gate)
        {
            held = [.. _leases.Values];
            _leases.Clear();
            _roles.Clear();
            _drafts.Clear();
        }

        foreach (var lease in held)
        {
            DisposeWhenLoaded(lease);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        ModelAdapters.Changed -= OnAdaptersChanged;
        Release();
    }

    // What completion would lease now: its own model (when it may load it, or it is loaded), else the chat's or the
    // agent's lease when that is ready; null when none.
    private (string Key, ModelRequest Request)? CompletionPlan(AiSettings settings)
    {
        var role = settings.CompletionRole;
        if (role.Model.Length > 0)
        {
            var own = KeyOf(settings, role, role.Model);
            lock (_gate)
            {
                bool ready = _leases.TryGetValue(own.Key, out var lease) && lease.IsCompletedSuccessfully;
                if (ready || (settings.CompletionLoad && !_failed.ContainsKey(own.Key)))
                {
                    return own;
                }
            }
        }

        foreach (var other in (ReadOnlySpan<AiRoleSettings>)[settings.ChatRole, settings.AgentRole])
        {
            if (other.Model.Length == 0)
            {
                continue;
            }

            var loaded = KeyOf(settings, other, other.Model);
            lock (_gate)
            {
                if (_leases.TryGetValue(loaded.Key, out var lease) && lease.IsCompletedSuccessfully)
                {
                    return loaded;
                }
            }
        }

        return null;
    }

    // The lease's key and request: everything its load depends on (the role's own settings), so changed settings mean
    // another lease, and two roles equal in them one.
    private static (string Key, ModelRequest Request) KeyOf(AiSettings settings, AiRoleSettings role, string model)
    {
        string name = model.Trim();
        string? adapter = name.Length == 0 ? null : ModelAdapters.For(name);
        var request = new ModelRequest(name)
        {
            Adapter = adapter,
            Device = role.Device,
            Context = role.Context,
            Weights = role.Weights,
            KeepAlive = TimeSpan.FromMinutes(role.KeepAliveMinutes),
            Offload = role.Offload,
        };
        string key = string.Join('\n', settings.ModelProvider, name, adapter ?? "", role.Device, role.Context.ToString(System.Globalization.CultureInfo.InvariantCulture), role.Weights,
            role.KeepAliveMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), role.Offload ? "offload" : "");
        return (key, request);
    }

    // Called holding the gate: a role moves to `key`; the lease it held goes when no role holds it any more.
    private void Retarget(string role, string key)
    {
        if (_roles.TryGetValue(role, out var old) && string.Equals(old, key, StringComparison.Ordinal))
        {
            return;
        }

        _roles[role] = key;
        if (old is not null && !_roles.ContainsValue(old) && _leases.Remove(old, out var dropped))
        {
            DisposeWhenLoaded(dropped);
        }
    }

    private static void DisposeWhenLoaded(Task<ModelLease> lease) => _ = lease.ContinueWith(static t =>
    {
        if (t.IsCompletedSuccessfully)
        {
            t.Result.Dispose();
        }
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    // A promoted or rolled-back adapter (the learning plug-in): the next request leases the model with it.
    private void OnAdaptersChanged(object? sender, EventArgs e) => Release();

    /// <summary>A load's stages into its activity (the stage's message, its fraction) and on to the session's progress.</summary>
    private sealed class LoadProgress(IActivity activity, IProgress<ModelLoadStatus>? next) : IProgress<ModelLoadStatus>
    {
        public void Report(ModelLoadStatus value)
        {
            activity.Report(value.Fraction is { } f && f < 1 ? f : null, value.Message);
            next?.Report(value);
        }
    }

    // The lease's load, off the calling thread (the UI's, for a chat or a completion): Idrak's built-ins (model sources,
    // formats) register from this plug-in's folder first, then the provider loads; an activity shows it all, with Cancel
    // (the load is let go: its waiters end, and the model is given back when the load returns).
    private Task<ModelLease> Load(IModelProvider provider, string key, ModelRequest request, AiSettings settings)
    {
        var activity = Activities.Start($"Loading {request.Model}", ActivityKinds.ModelLoad, cancellable: true);
        var progress = new LoadProgress(activity, Progress);
        var token = activity.CancellationToken;
        // Not tied to one request's cancellation: the lease is shared by the requests that follow.
        var load = Task.Run(() =>
        {
            activity.Report(null, "registering Idrak's libraries");
            IdrakLibraries.EnsureIncluded();
            return provider.AcquireAsync(request, progress, token);
        }, token);
        var lease = load.WaitAsync(token);
        _ = token.Register(() =>
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(key, out var kept) && ReferenceEquals(kept, lease))
                {
                    _leases.Remove(key);
                }
            }

            DisposeWhenLoaded(load);
        });
        _ = lease.ContinueWith(done =>
        {
            if (done.IsCompletedSuccessfully)
            {
                var held = done.Result;
                activity.Complete(held.Loaded
                    ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"loaded in {held.LoadDuration.TotalSeconds:F1} s, context {held.ContextLength}")
                    : "loaded already");
                return;
            }

            lock (_gate)
            {
                if (_leases.TryGetValue(key, out var kept) && ReferenceEquals(kept, done))
                {
                    _leases.Remove(key);
                }
            }

            if (done.IsCanceled)
            {
                activity.Complete("cancelled");
                return;
            }

            var error = done.Exception?.GetBaseException();
            activity.Fail($"{request.Model} did not load: {(error is null ? "cancelled" : AiErrors.Describe(error, settings))}", error, () => _ = RetryAsync(key, request));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return lease;
    }

    // Retry of a failed load (the user's, from the notification): that model again, its failure forgotten.
    private async Task RetryAsync(string key, ModelRequest request)
    {
        lock (_gate)
        {
            _failed.Remove(key);
            if (!_roles.ContainsValue(key))
            {
                return;                                          // the settings moved on: the next request loads what they name
            }
        }

        try
        {
            await LeaseAsync(_settings(), key, request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A retry's failure shows as its own activity's.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task<ModelLease> LeaseAsync(AiSettings settings, string key, ModelRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Model.Length == 0)
        {
            throw new InvalidOperationException("No model is set: pick one at the composer's model chip, or set the model (CRAFT_PLUGIN_AI_MODEL) on the settings page: a folder, a .gguf file, store:NAME or a Hugging Face id.");
        }

        Task<ModelLease> lease;
        lock (_gate)
        {
            if (!_leases.TryGetValue(key, out lease!))
            {
                var provider = ModelProviders.Get(settings.ModelProvider);
                lease = Load(provider, key, request, settings);
                _leases[key] = lease;
            }
        }

        try
        {
            return await lease.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException || lease.IsCanceled)
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(key, out var held) && ReferenceEquals(held, lease))
                {
                    _leases.Remove(key);
                }
            }

            throw;
        }
    }
}

/// <summary>A model's failure in the user's words: an out-of-memory one says what to do (never a setting changed behind them).</summary>
public static class AiErrors
{
    /// <summary>Whether <paramref name="error"/> is a device or system running out of memory.</summary>
    public static bool IsOutOfMemory(Exception? error) => ModelErrors.IsOutOfMemory(error);

    /// <summary>
    /// <paramref name="error"/>'s message, and for an out-of-memory one what the user can do: pick a smaller model at the
    /// composer (or a smaller <c>completionModel</c>), smaller weights, or close other programs on the device. Offloading
    /// to system memory is named as a choice, never set.
    /// </summary>
    public static string Describe(Exception error, AiSettings settings, bool forCompletion = false)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsOutOfMemory(error))
        {
            return error.Message;
        }

        var role = forCompletion && settings.CompletionModel.Length > 0 ? settings.CompletionRole : settings.ChatRole;
        string model = role.Model;
        string smaller = forCompletion
            ? "set a smaller completionModel on the settings page"
            : "pick a smaller model at the composer's model chip";
        string variable = role.Key == ModelRoleKeys.Completion ? "completionWeights" : "weights";
        string weights = role.Weights is "int4" ? "" : $", load it with smaller weights ({variable} {(role.Weights is "int8" ? "int4" : "int8")})";
        return $"The device ran out of memory for {model}: {smaller}{weights}, or close other programs that use the GPU, then try again. "
               + "(To keep what does not fit in system memory, slower, set IDRAK_OFFLOAD=1 yourself.) " + error.Message;
    }
}
