// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// The models the AI plug-ins use, through the provider of the setting <c>modelProvider</c> (Craft's
/// <see cref="ModelProviders"/>; names resolved by Idrak's model sources, never here): one lease per provider and name, kept
/// while the session lives (the provider shares the loaded model between leases) and taken back when the name changes or
/// the session ends. A failed load is not kept, so the next request tries again. Any thread.
/// </summary>
public sealed class AiModels : IDisposable
{
    private readonly Func<AiSettings> _settings;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<ModelLease>> _leases = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Models read through the settings <paramref name="settings"/> gives at each request.</summary>
    public AiModels(Func<AiSettings> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ModelAdapters.Changed += OnAdaptersChanged;
    }

    /// <summary>Reports a load's stages (set by the session: the status bar), or null.</summary>
    public IProgress<ModelLoadStatus>? Progress { get; set; }

    /// <summary>The chat model's lease (the setting <c>model</c>).</summary>
    /// <exception cref="InvalidOperationException">No model is set.</exception>
    public Task<ModelLease> ChatAsync(CancellationToken cancellationToken) => LeaseAsync(_settings().Model, cancellationToken);

    /// <summary>Inline completion's lease (<c>completionModel</c>, else <c>model</c>).</summary>
    /// <exception cref="InvalidOperationException">No model is set.</exception>
    public Task<ModelLease> CompletionAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        return LeaseAsync(settings.CompletionModel.Length > 0 ? settings.CompletionModel : settings.Model, cancellationToken);
    }

    /// <summary>Whether a model for completion is named.</summary>
    public bool HasCompletionModel
    {
        get
        {
            var settings = _settings();
            return settings.CompletionModel.Length > 0 || settings.Model.Length > 0;
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
        }

        foreach (var lease in held)
        {
            _ = lease.ContinueWith(static t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    t.Result.Dispose();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        ModelAdapters.Changed -= OnAdaptersChanged;
        Release();
    }

    // A promoted or rolled-back adapter (the learning plug-in): the next request leases the model with it.
    private void OnAdaptersChanged(object? sender, EventArgs e) => Release();

    private async Task<ModelLease> LeaseAsync(string model, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (model.Length == 0)
        {
            throw new InvalidOperationException("No model is set: set the model (CRAFT_PLUGIN_AI_MODEL) on the settings page: a folder, a .gguf file, store:NAME or a Hugging Face id.");
        }

        var settings = _settings();
        string? adapter = ModelAdapters.For(model);
        string key = settings.ModelProvider + "\n" + model + "\n" + adapter;
        Task<ModelLease> lease;
        lock (_gate)
        {
            if (!_leases.TryGetValue(key, out lease!))
            {
                // Idrak's built-ins (model sources, formats) register from this plug-in's folder before the first load.
                IdrakLibraries.EnsureIncluded();
                var provider = ModelProviders.Get(settings.ModelProvider);
                // Not tied to one request's cancellation: the lease is shared by the requests that follow.
                lease = provider.AcquireAsync(new ModelRequest(model) { Adapter = adapter }, Progress, CancellationToken.None);
                _leases[key] = lease;
            }
        }

        try
        {
            return await lease.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
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
