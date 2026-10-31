// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Abstraction.Serving;
using Idrak.Models;
using Idrak.Text;

namespace Craft.Ai;

/// <summary>
/// The "local" model provider (plan phase A1): a name resolved by Idrak's model sources (<c>ModelSource.Resolve</c>,
/// which asks <c>ModelSources</c>: a folder, a .gguf file, <c>store:NAME</c>, a Hugging Face id, any source a plug-in
/// registers), loaded once on this machine with Idrak's <c>PretrainedModel</c> and kept by a <see cref="ModelHost{TModel}"/>
/// for the keep-alive, shared between leases; its chat model is the model's own chat template over the generator, its
/// text model the generator itself (for fill-in-the-middle). The user's limits (threads, CPU and GPU memory) are set on
/// Idrak's <c>ComputeResources</c> before a load; none set, the library uses what the device has. The model families
/// are Idrak's plug-in packages (<see cref="AiSettings.IdrakPlugins"/>), registered before the first load: a model whose
/// family none registers fails with the library's "not registered".
/// </summary>
public sealed class LocalModelProvider : IModelProvider, IDisposable
{
    private static readonly Lock PluginsGate = new();
    private static bool s_pluginsRegistered;
    private readonly Lock _gate = new();
    private ModelHost<LocalModel>? _models;
    private TimeSpan? _keepAlive;

    /// <inheritdoc />
    public string Name => ModelProviders.Local;

    /// <inheritdoc />
    public string Summary => "Models on this machine: names resolved by Idrak's model sources, loaded once and kept for the keep-alive";

    /// <summary>The settings a request does not override (the plug-in sets the host's when it activates); the environment's until then.</summary>
    public AiSettings Settings { get; set; } = AiSettings.FromEnvironment();

    /// <summary>The models loaded now.</summary>
    public IReadOnlyList<LoadedModel> Loaded => _models?.Loaded ?? [];

    private string _state = "not loaded";
    private bool _stateIsProblem;
    private string? _device;

    /// <summary>
    /// What the provider holds (phase D's Models page and the model chip): the chat model of the settings and its state,
    /// the device a load used, and the settings the page edits. Nothing unmeasured is guessed.
    /// </summary>
    public ModelProviderStatus Status
    {
        get
        {
            var settings = Settings;
            string? model = settings.Model.Length == 0 ? null : settings.Model;
            return new ModelProviderStatus
            {
                Where = "local",
                Roles =
                [
                    new ModelRole("Chat and agent", model, model is null ? "none set" : _state, model is not null && _stateIsProblem),
                    new ModelRole("Inline completion", model, model is null ? "none set" : "the chat model (fill-in-the-middle when its family registers a format)"),
                ],
                Device = _device,
                LoadCommand = AiPlugin.LoadModelCommand,
                ModelSetting = new ModelSettingField(Variable("model"), "Model name: a folder, store:name, a .gguf file, a Hugging Face id"),
                DeviceSetting = new ModelSettingField(Variable("device"), "Run on", "auto (the fastest found)"),
                Limits =
                [
                    new ModelSettingField(Variable("gpuMemoryMb"), "Device memory cap, MiB (0: none)", "0"),
                    new ModelSettingField(Variable("cpuMemoryMb"), "CPU memory cap, MiB (0: none)", "0"),
                    new ModelSettingField(Variable("threads"), "CPU threads (0: all cores)", "0"),
                ],
            };
        }
    }

    /// <summary>Raised when <see cref="Status"/> changed (a load's stage, its end), on the reporting thread.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Sets the chat model's state line (the plug-in's load command reports through it) and raises <see cref="StatusChanged"/>.</summary>
    internal void SetState(string state, bool isProblem = false)
    {
        _state = state;
        _stateIsProblem = isProblem;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Variable(string key) => PluginSetting.VariableName(AiSettings.PluginName, key);

    /// <inheritdoc />
    public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        var settings = Settings;
        var key = new LoadKey(request.Model.Trim(), request.Device ?? settings.Device, request.Context ?? settings.Context, request.Weights ?? settings.Weights);
        return Task.Run(() => Acquire(key, settings, progress, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _models?.Dispose();
            _models = null;
        }
    }

    private ModelLease Acquire(LoadKey key, AiSettings settings, IProgress<ModelLoadStatus>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = Host(settings);
        var watch = Stopwatch.StartNew();
        using var ticker = new Timer(_ => progress?.Report(new ModelLoadStatus("loading", null,
            string.Create(CultureInfo.InvariantCulture, $"loading {key.Model} on {key.Device}, {watch.Elapsed.TotalSeconds:F1} s"))), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        progress?.Report(new ModelLoadStatus("resolving", null, $"resolving {key.Model} (Idrak's model sources)"));
        var lease = host.Acquire(key.Text, _keepAlive);
        var model = lease.Model;
        _device = model.Device;
        progress?.Report(new ModelLoadStatus("ready", 1, lease.LoadDuration > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"{key.Model} loaded on {model.Device} in {lease.LoadDuration.TotalSeconds:F1} s, context {model.ContextLength}")
            : $"{key.Model} on {model.Device} (loaded already)"));
        return new ModelLease(key.Model, model.Chat, model.Text, model.ContextLength, lease.LoadDuration, lease);
    }

    private ModelHost<LocalModel> Host(AiSettings settings)
    {
        lock (_gate)
        {
            if (_models is null)
            {
                _keepAlive = TimeSpan.FromMinutes(settings.KeepAliveMinutes);
                _models = new ModelHost<LocalModel>(text => Load(LoadKey.Parse(text), Settings), _keepAlive);
            }

            return _models;
        }
    }

    private static LocalModel Load(LoadKey key, AiSettings settings)
    {
        IdrakLibraries.EnsureIncluded();
        RegisterIdrakPlugins(settings);
        ApplyLimits(settings);
        string folder = ModelSource.Resolve(key.Model);
        var device = key.Device.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Device.Default : Device.Parse(key.Device);
        string weights = key.Weights.ToLowerInvariant();
        var model = PretrainedModel.Load(folder, new PretrainedOptions
        {
            Device = device,
            Int8 = weights == "int8",
            Int4 = weights == "int4",
            BFloat16 = weights is "bf16" or "bfloat16",
            PackedFormatName = weights is "" or "int8" or "int4" or "bf16" or "bfloat16" or "float32" or "f32" ? null : key.Weights,
            MaxPositions = key.Context > 0 ? key.Context : null,
        });
        try
        {
            int context = key.Context > 0 ? Math.Min(key.Context, model.MaxPositions) : model.MaxPositions;
            var layout = KeyValueLayouts.Get("float32");
            IChatModel? chat = model.ChatTemplate is null ? null : model.CreateChat(layout, context);
            ITextModel text = model.CreateGenerator(layout, context);
            return new LocalModel(model, chat, text, context, device.ToString());
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    // The user's limits on Idrak's ComputeResources (process-wide, as the library has them); 0 leaves the library's own.
    private static void ApplyLimits(AiSettings settings)
    {
        if (settings.Threads > 0)
        {
            ComputeResources.MaxCpuThreads = settings.Threads;
        }

        if (settings.CpuMemoryMegabytes > 0)
        {
            ComputeResources.CpuMemoryLimit = settings.CpuMemoryMegabytes * 1024 * 1024;
        }

        if (settings.GpuMemoryMegabytes > 0)
        {
            ComputeResources.GpuMemoryLimit = settings.GpuMemoryMegabytes * 1024 * 1024;
        }
    }

    // The Idrak plug-in packages named in the settings, loaded in this plug-in's own load context (beside its Idrak), once.
    private static void RegisterIdrakPlugins(AiSettings settings)
    {
        lock (PluginsGate)
        {
            if (s_pluginsRegistered)
            {
                return;
            }

            var context = AssemblyLoadContext.GetLoadContext(typeof(LocalModelProvider).Assembly) ?? AssemblyLoadContext.Default;
            foreach (var name in settings.IdrakPlugins)
            {
                // Once per assembly (IdrakPlugins.Load registers nothing twice).
                IdrakPlugins.Load(context.LoadFromAssemblyName(new AssemblyName(name)));
            }

            s_pluginsRegistered = true;
        }
    }

    /// <summary>What one loaded model is kept under: its name, device, context and weights.</summary>
    private readonly record struct LoadKey(string Model, string Device, int Context, string Weights)
    {
        public string Text => string.Join('\n', Model, Device, Context.ToString(CultureInfo.InvariantCulture), Weights);

        public static LoadKey Parse(string text)
        {
            var parts = text.Split('\n');
            return new LoadKey(parts[0], parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture), parts[3]);
        }
    }

    /// <summary>A loaded model: the weights and its chat and text generators.</summary>
    private sealed class LocalModel(PretrainedModel model, IChatModel? chat, ITextModel text, int contextLength, string device) : IDisposable
    {
        public IChatModel? Chat { get; } = chat;

        public ITextModel Text { get; } = text;

        public int ContextLength { get; } = contextLength;

        public string Device { get; } = device;

        public void Dispose() => model.Dispose();
    }
}
