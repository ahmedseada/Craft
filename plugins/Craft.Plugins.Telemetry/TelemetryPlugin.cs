// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Plugins.Telemetry;
using Idrak.Abstraction.Diagnostics;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(TelemetryPackage))]

namespace Craft.Plugins.Telemetry;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Telemetry\.</summary>
public sealed class TelemetryPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Telemetry";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new TelemetryRegistration()];
}

/// <summary>Registers "editor" in Idrak's <see cref="ProgressReporters"/> and the editor plug-in "telemetry", each with how to take it back.</summary>
public sealed class TelemetryRegistration() : IdrakPlugin(TelemetrySettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => ProgressReporters.Register(EditorProgressReporterKind.Instance), () => ProgressReporters.Unregister(EditorProgressReporterKind.ReporterName));
        Part(() => EditorPlugins.Register(TelemetryPlugin.Instance), () => EditorPlugins.Unregister(TelemetrySettings.PluginName));
    }
}

/// <summary>The plug-in's settings (plug-in "telemetry", variables <c>CRAFT_PLUGIN_TELEMETRY_&lt;KEY&gt;</c>).</summary>
public sealed record TelemetrySettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "telemetry";

    /// <summary>The least time between two showings of a progress line, ms (<c>progressMs</c>).</summary>
    public int ProgressMilliseconds { get; init; } = 250;

    /// <summary>Whether Idrak's telemetry is read (<c>telemetry</c>): tokens per second, first token, device memory.</summary>
    public bool Telemetry { get; init; } = true;

    /// <summary>How often the measures reach the status bar, the model chip and the telemetry tab, ms (<c>refreshMs</c>).</summary>
    public int RefreshMilliseconds { get; init; } = 1000;

    /// <summary>Whether the status bar shows the last generation's speed and the device memory (<c>statusItem</c>).</summary>
    public bool StatusItem { get; init; } = true;

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("progressMs", "250", "The least time between two showings of a model's pull or load progress in the status bar, ms."),
        new("telemetry", "true", "Whether Idrak's telemetry (its inference stages) is read for tokens per second, the first token and the device memory."),
        new("refreshMs", "1000", "How often the measured speed and memory reach the status bar, the model chip and the Idrak telemetry tab, ms."),
        new("statusItem", "true", "Whether the status bar shows the last generation's tokens per second and the device memory in use."),
    ];

    /// <summary>The settings from <paramref name="settings"/>; a value that does not parse throws, naming its variable.</summary>
    public static TelemetrySettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Read(key => settings.Get(PluginName, key));
    }

    /// <summary>The settings from <paramref name="value"/> (a key's text, or null for its default).</summary>
    public static TelemetrySettings Read(Func<string, string?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var defaults = new TelemetrySettings();
        string? Text(string key) => value(key) is { } text && text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
        int Whole(string key, int fallback, int min, int max) => Text(key) is not { } text ? fallback
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max ? number
            : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a whole number from {min} to {max}.", key);
        bool Flag(string key, bool fallback) => Text(key) switch
        {
            null => fallback,
            var t when t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1" => true,
            var t when t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0" => false,
            var t => throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{t}': it takes true or false.", key),
        };

        return new TelemetrySettings
        {
            ProgressMilliseconds = Whole("progressMs", defaults.ProgressMilliseconds, 10, 60_000),
            Telemetry = Flag("telemetry", defaults.Telemetry),
            RefreshMilliseconds = Whole("refreshMs", defaults.RefreshMilliseconds, 100, 600_000),
            StatusItem = Flag("statusItem", defaults.StatusItem),
        };
    }
}

/// <summary>
/// The editor plug-in "telemetry": while it is active, the "editor" progress reporters show in its host's status bar
/// (<see cref="EditorProgressReporterKind"/>), and Idrak's telemetry is read into the model chip, the status bar and the
/// telemetry tab (<see cref="ModelMeasuresHook"/>).
/// </summary>
public sealed class TelemetryPlugin : IEditorPlugin
{
    private TelemetryPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static TelemetryPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => TelemetrySettings.PluginName;

    /// <inheritdoc />
    public string Summary => "A model's pull and load in the status bar (stage, share, speed, time left); tokens per second and device memory from Idrak's telemetry";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => TelemetrySettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new TelemetrySession(host, TelemetrySettings.From(host.Settings));
    }
}

/// <summary>The plug-in in one host: the progress bridge, the telemetry hook and the status item. Made and disposed on the UI thread.</summary>
internal sealed class TelemetrySession : IDisposable
{
    private readonly IEditorHost _host;
    private readonly IDisposable _bridge;
    private readonly ModelMeasuresHook? _hook;
    private readonly IDisposable? _subscription;
    private readonly Timer? _timer;
    private readonly StatusItem _status = new("telemetry.model", "", StatusAlignment.Right, 8, "craft.showTelemetry");
    private readonly bool _showStatus;
    private IDisposable? _statusShown;
    private int _disposed;

    public TelemetrySession(IEditorHost host, TelemetrySettings settings)
    {
        _host = host;
        _bridge = EditorProgressReporterKind.Attach(host, TimeSpan.FromMilliseconds(settings.ProgressMilliseconds));
        _showStatus = settings.StatusItem;
        if (settings.Telemetry)
        {
            _hook = new ModelMeasuresHook();
            _subscription = Idrak.Abstraction.Diagnostics.Telemetry.Subscribe(_hook);
            var every = TimeSpan.FromMilliseconds(settings.RefreshMilliseconds);
            _timer = new Timer(_ => Publish(), null, every, every);
        }
    }

    // On the timer's thread: the hook's figures, once they changed, to the shell's board, then on the UI thread to the
    // status item and the telemetry tab.
    private void Publish()
    {
        if (Volatile.Read(ref _disposed) != 0 || _hook?.Take() is not { } measures)
        {
            return;
        }

        ModelTelemetry.Report(measures);
        _host.Dispatch(() =>
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (measures.TokensPerSecond is { } rate)
            {
                _host.Record("idrak.tokensPerSecond", rate, "tok/s");
            }

            if (measures.FirstToken is { } first)
            {
                _host.Record("idrak.firstToken", first.TotalMilliseconds, "ms");
            }

            if (measures.DeviceMemoryBytes is { } memory)
            {
                _host.Record("idrak.deviceMemory", memory / 1048576.0, "MiB");
            }

            if (_showStatus)
            {
                _status.Text = ModelMeasuresHook.Describe(measures);
                _status.Tooltip = "The last generation, from Idrak's telemetry (its inference stages); the Idrak telemetry tab has every measure.";
                _statusShown ??= _host.AddStatusItem(_status);
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer?.Dispose();
        _subscription?.Dispose();
        _statusShown?.Dispose();
        _bridge.Dispose();
        if (_hook is not null)
        {
            ModelTelemetry.Clear();
        }
    }
}
