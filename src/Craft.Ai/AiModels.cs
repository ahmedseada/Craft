// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Idrak.Abstraction;

namespace Craft.Ai;

/// <summary>
/// The models the AI plug-ins use, through the provider of the setting <c>modelProvider</c> (Craft's
/// <see cref="ModelProviders"/>; names resolved by Idrak's model sources, never here). Two roles, each its own setting:
/// the chat model (<c>model</c>: the chat, the agent, the code actions, the load command) and inline completion's
/// (<c>completionModel</c>, a small one). Nothing loads by itself: the chat model loads when the user asks for it (a
/// message sent, an action picked, the composer's model chip, the load command); completion's on its first request when
/// <c>completionLoad</c> allows it. A lease is kept under everything its load depends on (the provider, the name, the
/// adapter, the device, the context, the weights), so a role whose settings change lets its old copy go before the
/// next is loaded (the owner's Windows run: a second copy of a 7B model beside the first filled a 16 GB card); the two
/// roles share one lease when they name the same. A failed load is not kept: the user's next request tries again,
/// but completion does not (a failure on every pause in typing), until its settings change or the model is loaded
/// on purpose. Any thread.
/// </summary>
public sealed class AiModels : IDisposable
{
    private readonly Func<AiSettings> _settings;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<ModelLease>> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private string? _chatKey;
    private string? _completionKey;
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
    /// The chat model's lease (the setting <c>model</c>), loading it when it is not loaded: only for what the user asked
    /// for (a message, an action, the chip, the load command). A failure of an earlier load of it is forgotten (this is
    /// the try again).
    /// </summary>
    /// <exception cref="InvalidOperationException">No model is set.</exception>
    public Task<ModelLease> ChatAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        var (key, request) = KeyOf(settings, settings.Model);
        lock (_gate)
        {
            _failed.Remove(key);
            Retarget(ref _chatKey, key);
        }

        return LeaseAsync(settings, key, request, cancellationToken);
    }

    /// <summary>Whether the chat model of the settings now is loaded (its lease is ready).</summary>
    public bool IsChatLoaded => IsReady(_settings().Model);

    /// <summary>Whether the chat model of the settings now is loading.</summary>
    public bool IsChatLoading
    {
        get
        {
            var settings = _settings();
            if (settings.Model.Length == 0)
            {
                return false;
            }

            var (key, _) = KeyOf(settings, settings.Model);
            lock (_gate)
            {
                return _leases.TryGetValue(key, out var lease) && !lease.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Inline completion's lease, or null when it has none to use now (<see cref="CompletionUnavailable"/> says why):
    /// <c>completionModel</c>, loaded on this request when <c>completionLoad</c> allows it and its last load did not fail;
    /// else the chat model, only when it is loaded already (completion never loads the chat model).
    /// </summary>
    public async Task<ModelLease?> CompletionAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        if (CompletionPlan(settings) is not { } plan)
        {
            return null;
        }

        lock (_gate)
        {
            Retarget(ref _completionKey, plan.Key);
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
    /// Why inline completion cannot answer now (no model set, its model not loaded and not to be loaded by it, its last
    /// load failed), in words for the status bar; null when it can.
    /// </summary>
    public string? CompletionUnavailable
    {
        get
        {
            var settings = _settings();
            if (CompletionPlan(settings) is not null)
            {
                return null;
            }

            if (settings.CompletionModel.Length > 0)
            {
                var (key, _) = KeyOf(settings, settings.CompletionModel);
                lock (_gate)
                {
                    if (_failed.TryGetValue(key, out var failure))
                    {
                        return failure;
                    }
                }

                return $"{settings.CompletionModel} is not loaded, and completion does not load it (completionLoad is off): run 'AI: Load the local model' or turn completionLoad on.";
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
            return settings.CompletionModel.Length > 0 || settings.Model.Length > 0;
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
            _chatKey = null;
            _completionKey = null;
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

    // What completion would lease now: its own model (when it may load it, or it is loaded), else the chat model's lease
    // when that is ready; null when none.
    private (string Key, ModelRequest Request)? CompletionPlan(AiSettings settings)
    {
        if (settings.CompletionModel.Length > 0)
        {
            var own = KeyOf(settings, settings.CompletionModel);
            lock (_gate)
            {
                bool ready = _leases.TryGetValue(own.Key, out var lease) && lease.IsCompletedSuccessfully;
                if (ready || (settings.CompletionLoad && !_failed.ContainsKey(own.Key)))
                {
                    return own;
                }
            }
        }

        if (settings.Model.Length > 0)
        {
            var chat = KeyOf(settings, settings.Model);
            lock (_gate)
            {
                if (_leases.TryGetValue(chat.Key, out var lease) && lease.IsCompletedSuccessfully)
                {
                    return chat;
                }
            }
        }

        return null;
    }

    private bool IsReady(string model)
    {
        if (model.Length == 0)
        {
            return false;
        }

        var (key, _) = KeyOf(_settings(), model);
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var lease) && lease.IsCompletedSuccessfully;
        }
    }

    // The lease's key and request: everything its load depends on, so changed settings mean another lease.
    private static (string Key, ModelRequest Request) KeyOf(AiSettings settings, string model)
    {
        string name = model.Trim();
        string? adapter = name.Length == 0 ? null : ModelAdapters.For(name);
        var request = new ModelRequest(name) { Adapter = adapter, Device = settings.Device, Context = settings.Context, Weights = settings.Weights };
        string key = string.Join('\n', settings.ModelProvider, name, adapter ?? "", settings.Device, settings.Context.ToString(System.Globalization.CultureInfo.InvariantCulture), settings.Weights);
        return (key, request);
    }

    // Called holding the gate: a role moves to `key`; the lease it held goes when no role holds it any more.
    private void Retarget(ref string? role, string key)
    {
        if (string.Equals(role, key, StringComparison.Ordinal))
        {
            return;
        }

        string? old = role;
        role = key;
        if (old is not null && old != _chatKey && old != _completionKey && _leases.Remove(old, out var dropped))
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
            if (key != _chatKey && key != _completionKey)
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
    public static bool IsOutOfMemory(Exception? error) => error switch
    {
        null => false,
        ResourceLimitExceededException or OutOfMemoryException or InsufficientMemoryException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsOutOfMemory),
        _ => error.Message.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || IsOutOfMemory(error.InnerException),
    };

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

        string model = forCompletion && settings.CompletionModel.Length > 0 ? settings.CompletionModel : settings.Model;
        string smaller = forCompletion
            ? "set a smaller completionModel on the settings page"
            : "pick a smaller model at the composer's model chip";
        string weights = settings.Weights is "int4" ? "" : $", load it with smaller weights (weights {(settings.Weights is "int8" ? "int4" : "int8")})";
        return $"The device ran out of memory for {model}: {smaller}{weights}, or close other programs that use the GPU, then try again. "
               + "(To keep what does not fit in system memory, slower, set IDRAK_OFFLOAD=1 yourself.) " + error.Message;
    }
}
