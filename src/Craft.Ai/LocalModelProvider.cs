// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
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

    /// <summary>
    /// Where <see cref="Status"/> reads the settings now (the session sets the host's, so the chip and the status bar
    /// follow a setting changed on the settings page); null: <see cref="Settings"/>. A value that does not parse keeps
    /// <see cref="Settings"/>.
    /// </summary>
    public Func<AiSettings>? SettingsSource { get; set; }

    /// <summary>
    /// Where <see cref="Status"/> reads each role's draft (speculative decoding: the session sets its models'
    /// <see cref="AiModels.DraftOf"/>, so the chip and the status bar show "draft: 1.5B, 72% accepted"); null: none shown.
    /// </summary>
    public Func<string, AiModels.DraftStatus?>? DraftSource { get; set; }

    /// <summary>Raises <see cref="StatusChanged"/> (a role's draft changed: loaded, refused, a reply measured).</summary>
    internal void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    // A role's status with its draft (DraftSource), as the chip, the status bar and the tooltip read it.
    private ModelRole WithDraft(ModelRole role)
    {
        if (role.Key is not { } key || DraftSource?.Invoke(key) is not { } draft)
        {
            return role;
        }

        return role with { Draft = draft.Model, DraftAcceptance = draft.Acceptance, DraftState = draft.State };
    }

    /// <summary>The models loaded now.</summary>
    public IReadOnlyList<LoadedModel> Loaded => _models?.Loaded ?? [];

    private string _state = "not loaded";
    private bool _stateIsProblem;
    private string? _device;
    private volatile bool _loading;
    private readonly List<string> _picked = [];
    private IReadOnlyList<string> _devices = [];
    private string? _autoDevice;
    private int _devicesListed;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _deviceOf = new(StringComparer.Ordinal);   // load key → device in use
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ModelMemoryEstimate> _memoryOf = new(StringComparer.Ordinal);   // load key → its weights' choice

    /// <summary>
    /// What the provider holds (phase D's Models page and the model chip): the chat model of the settings and its state,
    /// the device a load used, and the settings the page edits. Nothing unmeasured is guessed.
    /// </summary>
    public ModelProviderStatus Status
    {
        get
        {
            var settings = Current();
            string? model = settings.Model.Length == 0 ? null : settings.Model;
            ListDevicesOnce();
            var loaded = Loaded;
            return new ModelProviderStatus
            {
                Models = Pickable(model),
                Devices = _devices,
                AutoDevice = _autoDevice,
                DeviceChoice = settings.Device,
                IsLoading = _loading,
                Where = "local",
                Roles =
                [
                    WithDraft(ChatRole(settings, loaded)),
                    WithDraft(AgentRole(settings, loaded)),
                    CompletionRole(settings, loaded),
                ],
                Device = _device,
                Loaded = [.. loaded.Select(m => $"{m.Name.Split('\n')[0]} · {(m.ActiveUsers > 0 ? "in use" : "idle")}")],
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

    private AiSettings Current()
    {
        if (SettingsSource is { } source)
        {
            try
            {
                return source();
            }
            catch (ArgumentException)
            {
                // A value that does not parse: the settings page says so; the status keeps the last good settings.
            }
        }

        return Settings;
    }

    // The device the role's model runs on now: the loaded copy under the role's settings (any adapter), or null.
    private string? RunningOn(AiRoleSettings role, IReadOnlyList<LoadedModel> loaded)
    {
        if (role.Model.Length == 0)
        {
            return null;
        }

        foreach (var model in loaded)
        {
            if (LoadKey.Parse(model.Name).Matches(role))
            {
                return _deviceOf.TryGetValue(model.Name, out var device) ? device : role.Device;
            }
        }

        return null;
    }

    // The role's weights as its loaded copy took them ("int4 (auto): 4.1 GB of 6.6 GB free"), else its setting ("auto").
    private string WeightsOf(AiRoleSettings role, IReadOnlyList<LoadedModel> loaded)
    {
        foreach (var model in loaded)
        {
            if (LoadKey.Parse(model.Name).Matches(role) && _memoryOf.TryGetValue(model.Name, out var memory))
            {
                return memory.Text;
            }
        }

        string weights = role.CacheFormat.Equals(WeightsAuto.Auto, StringComparison.OrdinalIgnoreCase) ? role.Weights : $"{role.Weights}, {role.CacheFormat} cache";
        return role.Offload ? weights + " · offload" : weights;
    }

    private ModelRole ChatRole(AiSettings settings, IReadOnlyList<LoadedModel> loaded)
    {
        var role = settings.ChatRole;
        string? model = role.Model.Length == 0 ? null : role.Model;
        string? device = RunningOn(role, loaded);
        string state = !role.Enabled ? "off (chatEnabled)" : model is null ? "none set" : device is not null ? "loaded" : _state;
        return new ModelRole("Chat", model, state, role.Enabled && model is not null && device is null && _stateIsProblem)
        {
            Key = ModelRoleKeys.Chat,
            Enabled = role.Enabled,
            Device = device,
            Weights = model is null ? null : WeightsOf(role, loaded),
            DeviceChoice = role.Device,
            ModelSetting = new ModelSettingField(Variable("model"), "Chat model"),
            DeviceSetting = new ModelSettingField(Variable("device"), "Chat runs on", "auto (the fastest found)"),
            EnabledSetting = new ModelSettingField(Variable("chatEnabled"), "Chat on", "true"),
            LoadCommand = AiPlugin.LoadModelCommand,
            SettingsCommand = SettingSections.CommandOf(SettingSections.Chat),
            IsLoading = _loading && device is null && model is not null,
        };
    }

    private ModelRole AgentRole(AiSettings settings, IReadOnlyList<LoadedModel> loaded)
    {
        var role = settings.AgentRole;
        string? model = role.Model.Length == 0 ? null : role.Model;
        // An agent engine's model (Claude Code) is no local model: nothing loads, it runs on this machine as its own program.
        bool engine = AgentEngines.FindModel(model) is not null;
        string? device = engine ? null : RunningOn(role, loaded);
        string state = !role.Enabled ? "off (agentEnabled)" : model is null ? "none set" : engine ? "an outside agent: runs on this machine, nothing to load"
            : device is not null ? "loaded" : "loads on the agent's first task";
        return new ModelRole("Agent", model, state)
        {
            Key = ModelRoleKeys.Agent,
            Enabled = role.Enabled,
            Device = device,
            Weights = model is null || engine ? null : WeightsOf(role, loaded),
            DeviceChoice = role.Device,
            ModelSetting = new ModelSettingField(Variable("agentModel"), "Agent model (empty: the chat's)"),
            DeviceSetting = new ModelSettingField(Variable("agentDevice"), "Agent runs on", "the chat's device"),
            EnabledSetting = new ModelSettingField(Variable("agentEnabled"), "Agent on", "true"),
            LoadCommand = engine ? null : AiPlugin.LoadAgentModelCommand,   // the engine runs its model: no Load offered
            SettingsCommand = SettingSections.CommandOf(SettingSections.Agent),
            Inherits = "Chat",
            InheritsModel = role.InheritsModel,
            InheritsDevice = role.InheritsDevice,
            IsLoading = _loading && device is null && model is not null && !engine,
        };
    }

    // Inline completion's own model (a small one beside the chat model), else the chat model while it is loaded.
    private ModelRole CompletionRole(AiSettings settings, IReadOnlyList<LoadedModel> loaded)
    {
        var role = settings.CompletionRole;
        var shared = new ModelRole("Inline completion", null, "")
        {
            Key = ModelRoleKeys.Completion,
            Enabled = role.Enabled,
            DeviceChoice = role.Device,
            ModelSetting = new ModelSettingField(Variable("completionModel"), "Completion model"),
            DeviceSetting = new ModelSettingField(Variable("completionDevice"), "Completion runs on", "auto (the fastest found)"),
            EnabledSetting = new ModelSettingField(Variable("completion"), "Inline completion on", "true"),
            SettingsCommand = SettingSections.CommandOf(SettingSections.Completion),
        };
        if (!role.Enabled)
        {
            return shared with { Model = role.Model.Length == 0 ? null : role.Model, State = "off (completion)" };
        }

        if (role.Model.Length > 0)
        {
            string? device = RunningOn(role, loaded);
            return shared with
            {
                Model = role.Model,
                State = device is not null ? "loaded" : settings.CompletionLoad ? "loads on the first completion" : "not loaded (completionLoad is off)",
                Device = device,
                Weights = WeightsOf(role, loaded),
            };
        }

        var chat = settings.ChatRole;
        string? chatModel = chat.Model.Length == 0 ? null : chat.Model;
        return shared with
        {
            Model = chatModel,
            State = chatModel is null ? "none set" : "the chat model, while it is loaded",
            Device = RunningOn(chat, loaded),
            DeviceChoice = chat.Device,
        };
    }

    // The composer's picker (the owner's note 11): the model set first, then those loaded and those used before, once each.
    private string[] Pickable(string? model)
    {
        var names = new List<string>(4);
        void Add(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name.Trim());
            }
        }

        Add(model);
        foreach (var loaded in Loaded)
        {
            Add(LoadKey.Parse(loaded.Name).Model);
        }

        lock (_picked)
        {
            foreach (var picked in _picked)
            {
                Add(picked);
            }
        }

        return [.. names];
    }

    // The devices of this machine and the one "auto" takes (the owner's note 6: the GPU shows before a load), found once
    // on the thread pool: listing them starts the GPU drivers (CUDA's, Vulkan's, HIP's), never on the UI thread.
    private void ListDevicesOnce()
    {
        if (Interlocked.Exchange(ref _devicesListed, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                IdrakLibraries.EnsureIncluded();
                var devices = new List<string> { "auto" };
                foreach (var device in Device.Available)
                {
                    devices.Add(device.ToString());
                }

                _devices = devices;
                _autoDevice = Device.Default.ToString();
            }
#pragma warning disable CA1031 // A driver that fails to answer leaves the list empty; the device setting still takes a name.
            catch (Exception)
#pragma warning restore CA1031
            {
                _devices = [];
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        });
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

    /// <summary>Raises <see cref="StatusChanged"/> (the models loaded or the settings changed).</summary>
    internal void Changed() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static string Variable(string key) => PluginSetting.VariableName(AiSettings.PluginName, key);

    /// <inheritdoc />
    public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        if (AiModels.EngineRuns(request.Model) is { } engine)
        {
            // An agent engine's model (Claude Code) is no name Idrak's model sources resolve: refused, never a load.
            return Task.FromException<ModelLease>(new InvalidOperationException(engine));
        }

        var settings = Settings;
        var key = new LoadKey(request.Model.Trim(), request.Device ?? settings.Device, request.Context ?? settings.Context, request.Weights ?? settings.Weights,
            request.Adapter is { Length: > 0 } adapter ? Path.GetFullPath(adapter) : "", request.Offload ?? settings.Offload,
            AiSettings.CacheFormatOf(request.CacheFormat ?? settings.CacheFormat));
        return Task.Run(() => Acquire(key, request.KeepAlive, settings, progress, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The model's folder resolved (a model not cached is downloaded first, as its load would), its size read from its config,
    /// the device's free memory measured now: the weights "auto" picks (or the named ones) and whether they fit. Null when
    /// the model's size is not known here.
    /// </remarks>
    public Task<ModelMemoryEstimate?> EstimateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        if (AgentEngines.FindModel(request.Model) is not null)
        {
            return Task.FromResult<ModelMemoryEstimate?>(null);         // an agent engine's model takes no memory of this provider
        }

        var settings = Settings;
        var key = new LoadKey(request.Model.Trim(), request.Device ?? settings.Device, request.Context ?? settings.Context, request.Weights ?? settings.Weights,
            "", request.Offload ?? settings.Offload, AiSettings.CacheFormatOf(request.CacheFormat ?? settings.CacheFormat));
        return Task.Run(() =>
        {
            IdrakLibraries.EnsureIncluded();
            RegisterIdrakPlugins(settings);
            string folder = ModelSource.Resolve(key.Model, ModelSource.Options());
            return Estimate(key, folder, settings);
        }, cancellationToken);
    }

    /// <summary>
    /// The weights and the decoding cache the role's settings load with, measured before the load ("int8 (auto), bfloat16
    /// cache: 12.3 GB of 14.4 GB free"); null when it has none.
    /// </summary>
    private static ModelMemoryEstimate? Estimate(LoadKey key, string folder, AiSettings settings)
    {
        if (WeightsAuto.ShapeOf(folder) is not { } shape)
        {
            return null;
        }

        int context = WeightsAuto.BudgetContext(shape, key.Context, settings.WeightsAutoContext);
        string device = key.Device;
        return WeightsAuto.Choose(key.Model, device, shape, key.Weights, context, WeightsAuto.FreeMemory(device), key.Offload, settings.WeightsAutoHeadroom, key.CacheFormat);
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

    private ModelLease Acquire(LoadKey key, TimeSpan? keepAlive, AiSettings settings, IProgress<ModelLoadStatus>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = Host(settings);
        var watch = Stopwatch.StartNew();
        // Phase U: every stage also becomes the provider's state (the status bar's model text and the chip follow
        // StatusChanged), whoever asked for the lease: the load command, the chat, the agent or completion.
        // The owner, 2026-11-01: the download's bytes and the load's tensors give the stage its share; the ticker keeps the
        // last share known (it never invents one) and says the time so far.
        var last = new ModelLoadStatus("loading", null, $"loading {key.Model} on {key.Device}");
        void Report(ModelLoadStatus status)
        {
            Volatile.Write(ref last, status);
            progress?.Report(status);
            SetState(status.Message, isProblem: false);
        }

        // A timer's callback that throws would end the process: guarded (reported, the load goes on).
        using var ticker = new Timer(_ =>
        {
            try
            {
                var now = Volatile.Read(ref last);
                progress?.Report(now with { Message = string.Create(CultureInfo.InvariantCulture, $"{now.Message}, {watch.Elapsed.TotalSeconds:F1} s") });
            }
#pragma warning disable CA1031 // Nothing may leave a timer's callback (the owner, 2026-11-01: never end the app).
            catch (Exception error)
#pragma warning restore CA1031
            {
                System.Diagnostics.Trace.TraceError($"AI: the load's progress failed: {error}");
            }
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        lock (_picked)
        {
            if (!_picked.Contains(key.Model, StringComparer.OrdinalIgnoreCase))
            {
                _picked.Add(key.Model);
            }
        }

        _loading = true;
        Report(new ModelLoadStatus("resolving", null, $"resolving {key.Model} (Idrak's model sources)"));
        ModelHost<LocalModel>.Lease lease;
        try
        {
            LetIdleGo(host, key.Text);
            Reporting.Value = Report;                                       // the load (on this flow) tells its download and its tensors
            try
            {
                lease = host.Acquire(key.Text, keepAlive ?? _keepAlive);    // the role's keep-alive (note 25), else the chat's
            }
            finally
            {
                Reporting.Value = null;
            }
        }
        catch (Exception error)
        {
            ticker.Dispose();
            _loading = false;
            SetState("failed: " + error.Message, isProblem: true);
            throw;
        }

        ticker.Dispose();
        _loading = false;
        var model = lease.Model;
        _device = model.Device;                    // the device in use, shown in the status bar and the chip
        _deviceOf[key.Text] = model.Device;        // and per copy, so each role shows its own (note 25)
        if (model.Memory is { } memory)
        {
            _memoryOf[key.Text] = memory;          // the weights it loaded with, said on the chip and the Models page
        }

        Report(new ModelLoadStatus("ready", 1, lease.LoadDuration > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"{key.Model} loaded on {model.Device} in {lease.LoadDuration.TotalSeconds:F1} s, context {model.ContextLength}")
            : $"{key.Model} on {model.Device} (loaded already)"));
        return new ModelLease(key.Model, model.Chat, model.Text, model.ContextLength, lease.LoadDuration, lease)
        {
            Tokenizer = model.Tokenizer,
            ChatTemplate = model.Pretrained.ChatTemplate,                   // the agent reads calls written as text and corrects in its format (note 27)
            Draft = model.Draft,                                            // this copy drafting for another of its vocabulary, through its gate
            Underlying = model.Pretrained,                                  // the one copy, for a plug-in that trains it (B7)
            Memory = model.Memory,
            Exclusive = model.Gate.ExclusiveAsync,
        };
    }

    /// <summary>
    /// Before a model is loaded (not one loaded already): the models no lease holds are unloaded first, so a model of other
    /// settings (another device, weights, context or adapter: a key of its own), or one the user moved away from, is not
    /// kept for its keep-alive beside the new one (the owner's Windows run: two copies of a 7B model on a 16 GB card).
    /// One in use is never unloaded.
    /// </summary>
    private static void LetIdleGo(ModelHost<LocalModel> host, string key)
    {
        var loaded = host.Loaded;
        if (loaded.Any(m => m.Name == key))
        {
            return;
        }

        foreach (var model in loaded)
        {
            if (model.ActiveUsers == 0)
            {
                host.Unload(model.Name);
            }
        }
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
        var report = Reporting.Value;
        // The download's bytes of all its files (Idrak's ModelSourceOptions.Progress; a cached model reports nothing).
        string folder = ModelSource.Resolve(key.Model, ModelSource.Options() with { Progress = report is null ? null : new PullReport(key.Model, report) });
        var device = key.Device.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Device.Default : Device.Parse(key.Device);
        // The owner, 2026-11-01 ("1a"): weights "auto" chosen by the device's free memory measured now (after the idle copies
        // were let go), before any weight is read; a named format wins; nothing fitting is refused here, offload named.
        var memory = Estimate(key, folder, settings);
        if (memory?.Refusal is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        if (key.Offload)
        {
            Idrak.Abstraction.ComputeResources.OffloadToHostMemory = true;   // Idrak's offload is the process's: on once a copy asks for it
        }

        try
        {
            return LoadCopy(key, settings, folder, device, memory, report);
        }
        catch (Exception error) when (AiErrors.Remember(error, memory))
        {
            throw;                                                        // never reached: the filter only notes what the load took on the failure
        }
    }

    // The copy's weights read and its generators made, with the weights and the cache the estimate chose (float32 of each
    // when there is none: the model's size is not known here) and the caches' capacity the estimate counted.
    private static LocalModel LoadCopy(LoadKey key, AiSettings settings, string folder, Device device, ModelMemoryEstimate? memory, Action<ModelLoadStatus>? report)
    {
        string weights = (memory?.Weights ?? (key.Weights.Equals(WeightsAuto.Auto, StringComparison.OrdinalIgnoreCase) ? WeightsAuto.Float32 : key.Weights)).ToLowerInvariant();
        string cacheFormat = memory?.CacheFormat ?? (key.CacheFormat.Equals(WeightsAuto.Auto, StringComparison.OrdinalIgnoreCase) ? WeightsAuto.Float32 : key.CacheFormat);
        var layout = KeyValueLayouts.Get(cacheFormat);                    // a name no layout is registered under fails here, named
        var model = PretrainedModel.Load(folder, new PretrainedOptions
        {
            // The checkpoint's values read of all of them (Idrak's PretrainedOptions.Progress).
            Progress = report is null ? null : new LoadReport(key.Model, device.ToString(), report),
            Device = device,
            Int8 = weights == "int8",
            Int4 = weights == "int4",
            BFloat16 = weights is "bf16" or "bfloat16",
            PackedFormatName = weights is "" or "int8" or "int4" or "bf16" or "bfloat16" or "float32" or "f32" or "auto" ? null : weights,
            MaxPositions = key.Context > 0 ? key.Context : null,
        });
        try
        {
            if (key.Adapter.Length > 0)
            {
                model.LoadAdapter(key.Adapter);                           // the promoted adapter (ModelRequest.Adapter), never the base weights changed
            }

            // The caches grow on demand up to what the estimate counted (the role's context, else the model's own up to
            // weightsAutoContext), never past it: a 16k model whose estimate kept room for 8k tokens does not run out at 9k.
            int context = WeightsAuto.BudgetContext(model.MaxPositions, key.Context, settings.WeightsAutoContext);
            IChatModel? chat = model.ChatTemplate is null ? null : model.CreateChat(layout, context);
            var generator = model.CreateGenerator(layout, context);
            // Every generation passes the copy's gate, so a training round on the copy (ModelLease.Exclusive) has it alone;
            // so does every drafting session for another model (speculative decoding), which never waits for it.
            var gate = new CopyGate();
            return new LocalModel(model, chat is null ? null : new GatedChatModel(chat, gate), new GatedTextModel(generator, gate), context, device.ToString(), gate)
            {
                Tokenizer = model.Tokenizer as IPretrainedTokenizer,
                Draft = new GatedDraftModel(generator, gate),
                Memory = memory,
            };
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    // The stage reporter of the load running on this flow (Acquire sets it around the host's load; the host's factory reads it).
    private static readonly AsyncLocal<Action<ModelLoadStatus>?> Reporting = new();

    // A download's bytes of all its files as the load's stage: "downloading NAME: 1.2 of 15.2 GB, file 2 of 4".
    private sealed class PullReport(string model, Action<ModelLoadStatus> report) : IProgress<Idrak.Abstraction.Formats.ModelPullProgress>
    {
        public void Report(Idrak.Abstraction.Formats.ModelPullProgress value) =>
            report(new ModelLoadStatus("downloading", value.Fraction < 1 ? value.Fraction : null, string.Create(CultureInfo.InvariantCulture,
                $"downloading {model}: {Gigabytes(value.Received)} of {Gigabytes(value.Total)}, file {Math.Min(value.Files, value.FilesDone + 1)} of {value.Files}")));

        private static string Gigabytes(long bytes) => bytes >= 1L << 30
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0} MB");
    }

    // A load's tensors read of all of them as the load's stage: "loading NAME on cuda:0: 120 of 339 tensors".
    private sealed class LoadReport(string model, string device, Action<ModelLoadStatus> report) : IProgress<PretrainedLoadProgress>
    {
        public void Report(PretrainedLoadProgress value) =>
            report(new ModelLoadStatus("loading", value.Fraction < 1 ? value.Fraction : null, string.Create(CultureInfo.InvariantCulture,
                $"loading {model} on {device}: {value.Tensors} of {value.TotalTensors} tensors")));
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

    // The Idrak plug-in packages named in the settings (the model families), loaded in this plug-in's own load context
    // beside its Idrak, once: through IdrakInPlugins, the path the learning plug-in takes with the same setting (B4), so the
    // families a process has do not depend on which plug-in loaded a model first.
    private static void RegisterIdrakPlugins(AiSettings settings)
    {
        lock (PluginsGate)
        {
            if (s_pluginsRegistered)
            {
                return;
            }

            IdrakInPlugins.IncludePackages(typeof(LocalModelProvider).Assembly, settings.IdrakPlugins);
            s_pluginsRegistered = true;
        }
    }

    /// <summary>What one loaded model is kept under: its name, device, context, weights, adapter ("" for none), offload and cache format.</summary>
    private readonly record struct LoadKey(string Model, string Device, int Context, string Weights, string Adapter, bool Offload = false, string CacheFormat = WeightsAuto.Auto)
    {
        public string Text => string.Join('\n', Model, Device, Context.ToString(CultureInfo.InvariantCulture), Weights, Adapter, Offload ? "offload" : "", CacheFormat);

        public static LoadKey Parse(string text)
        {
            var parts = text.Split('\n');
            return new LoadKey(parts[0], parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture), parts[3], parts.Length > 4 ? parts[4] : "", parts.Length > 5 && parts[5] == "offload",
                parts.Length > 6 ? parts[6] : WeightsAuto.Auto);
        }

        // Whether this copy is the one the role's settings load (any adapter).
        public bool Matches(AiRoleSettings role) =>
            Model.Equals(role.Model, StringComparison.OrdinalIgnoreCase) && Device.Equals(role.Device, StringComparison.OrdinalIgnoreCase) && Context == role.Context
            && string.Equals(Weights, role.Weights, StringComparison.OrdinalIgnoreCase) && Offload == role.Offload
            && string.Equals(CacheFormat, role.CacheFormat, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A loaded model: the weights and its chat and text generators.</summary>
    private sealed class LocalModel(PretrainedModel model, IChatModel? chat, ITextModel text, int contextLength, string device, CopyGate gate) : IDisposable
    {
        public PretrainedModel Pretrained { get; } = model;

        public CopyGate Gate { get; } = gate;

        public IChatModel? Chat { get; } = chat;

        public ITextModel Text { get; } = text;

        public int ContextLength { get; } = contextLength;

        public string Device { get; } = device;

        public IPretrainedTokenizer? Tokenizer { get; init; }

        public IDraftModel? Draft { get; init; }

        public ModelMemoryEstimate? Memory { get; init; }

        public void Dispose() => Pretrained.Dispose();
    }
}
