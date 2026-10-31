// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Ai;
using Idrak.Abstraction.Formats;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(AiPackage))]
[assembly: ToolProvider(typeof(FolderToolProvider))]

namespace Craft.Ai;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Ai\.</summary>
public sealed class AiPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Ai";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new AiRegistration()];
}

/// <summary>Registers the AI host's parts, each with how to take it back: the editor plug-in, its commands, the "local" model provider.</summary>
public sealed class AiRegistration() : IdrakPlugin(AiSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(AiPlugin.Instance), () => EditorPlugins.Unregister(AiSettings.PluginName));
        foreach (var command in AiPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        Part(() => ModelProviders.Register(AiPlugin.Local), () => ModelProviders.Unregister(ModelProviders.Local));
    }
}

/// <summary>
/// The AI host as an editor plug-in (plan phase A1, "ai"): it declares the settings (<see cref="AiSettings"/>), serves the
/// editor's live tools over MCP when asked (off by default; the command <c>ai.mcpStart</c>, or <c>mcpOnStart</c>), shows
/// the server's address in the status bar, and holds the "local" model provider with a "Models and devices" panel. The AI
/// plug-ins of phase A2 build on it through the same public contracts.
/// </summary>
public sealed class AiPlugin : IEditorPlugin
{
    /// <summary>Starts the MCP server.</summary>
    public const string McpStartCommand = "ai.mcpStart";

    /// <summary>Stops it.</summary>
    public const string McpStopCommand = "ai.mcpStop";

    /// <summary>Shows its address and token.</summary>
    public const string McpShowCommand = "ai.mcpShow";

    /// <summary>Loads the local model of the setting <c>model</c>.</summary>
    public const string LoadModelCommand = "ai.loadModel";

    private AiPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static AiPlugin Instance { get; } = new();

    /// <summary>The "local" model provider it registers.</summary>
    public static LocalModelProvider Local { get; } = new();

    /// <summary>The session of the host it is active in, or null.</summary>
    public static AiSession? Current { get; private set; }

    /// <summary>Its commands (registered by its package).</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(McpStartCommand, "AI: Serve the editor's tools over MCP", (_, token) => Current!.StartMcpAsync(token),
            _ => Current is { Mcp: null }, "Starts the MCP server of the editor's live tools (loopback, a token)"),
        new EditorCommand(McpStopCommand, "AI: Stop serving the editor's tools over MCP", (_, _) => Current!.StopMcpAsync(),
            _ => Current is { Mcp: not null }, "Stops the MCP server"),
        new EditorCommand(McpShowCommand, "AI: Show the MCP address and token", (_, _) => Current!.ShowMcpAsync(),
            _ => Current is not null, "Shows where MCP clients connect, the token and the idrak command line"),
        new EditorCommand(LoadModelCommand, "AI: Load the local model", (_, token) => Current!.LoadModelAsync(token),
            _ => Current is not null, "Loads the model of the setting CRAFT_PLUGIN_AI_MODEL, with its progress"),
    ];

    /// <inheritdoc />
    public string Name => AiSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "The AI host: the editor's tools (served over MCP when asked), the diff for every model edit, the local model provider";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => AiSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        IdrakLibraries.EnsureIncluded();
        var session = new AiSession(host, () => Current = null);
        Current = session;
        return session;
    }
}

/// <summary>The AI host in one editor host: its MCP server, its status items and its models panel. Used on the UI thread.</summary>
public sealed class AiSession : IDisposable
{
    private readonly IEditorHost _host;
    private readonly Action _closed;
    private readonly ListPanel _models = new("ai.models", "Models and devices");
    private readonly StatusItem _mcpStatus = new("ai.mcp", "", StatusAlignment.Right, 10, AiPlugin.McpShowCommand);
    private IDisposable? _mcpShown;
    private string _modelState = "not loaded";
    private bool _disposed;

    internal AiSession(IEditorHost host, Action closed)
    {
        _host = host;
        _closed = closed;
        var settings = Settings;
        AiPlugin.Local.Settings = settings;
        _host.AddPanel(_models);
        RefreshModels();
        if (settings.McpOnStart)
        {
            _ = StartMcpAsync(CancellationToken.None);
        }
    }

    /// <summary>The MCP server while it runs, else null.</summary>
    public EditorMcpServer? Mcp { get; private set; }

    /// <summary>The settings as the host reads them now.</summary>
    public AiSettings Settings => AiSettings.From(_host.Settings);

    /// <summary>Starts the MCP server of the live tools (once); its address goes to the status bar.</summary>
    public async Task StartMcpAsync(CancellationToken cancellationToken)
    {
        if (Mcp is not null || _disposed)
        {
            return;
        }

        try
        {
            var server = await EditorMcpServer.StartAsync(_host, Settings, cancellationToken);
            if (_disposed)
            {
                await server.DisposeAsync();
                return;
            }

            Mcp = server;
            _mcpStatus.Text = "MCP " + server.Address;
            _mcpStatus.Tooltip = server.Describe();
            _mcpShown = _host.AddStatusItem(_mcpStatus);
            _host.Log(EditorLogLevel.Information, $"Serving {server.ToolCount} tools over MCP at {server.Address}.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _host.Log(EditorLogLevel.Error, "The MCP server did not start.", error);
            await _host.ShowMessageAsync("AI: the MCP server did not start", error.Message);
        }
    }

    /// <summary>Stops the MCP server.</summary>
    public async Task StopMcpAsync()
    {
        if (Mcp is not { } server)
        {
            return;
        }

        Mcp = null;
        _mcpShown?.Dispose();
        _mcpShown = null;
        await server.DisposeAsync();
        _host.Log(EditorLogLevel.Information, "Stopped serving the editor's tools over MCP.");
    }

    /// <summary>Shows the server's address, token and the idrak command line (or that it is off).</summary>
    public Task ShowMcpAsync() => _host.ShowMessageAsync("AI: MCP", Mcp is { } server
        ? server.Describe()
        : $"The MCP server is off. Run 'AI: Serve the editor's tools over MCP'; it listens at {Settings.McpAddress} (CRAFT_PLUGIN_AI_MCPADDRESS).");

    /// <summary>Loads the local model of the settings, its progress in the status bar, its state in the models panel.</summary>
    public async Task LoadModelAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        AiPlugin.Local.Settings = settings;
        if (settings.Model.Length == 0)
        {
            await _host.ShowMessageAsync("AI: no model", "Set the model (CRAFT_PLUGIN_AI_MODEL) on the settings page: a folder, a .gguf file, store:NAME or a Hugging Face id.");
            return;
        }

        using var progress = _host.StartProgress($"Loading {settings.Model}");
        // The provider reports from its worker; the host is reached on its UI thread.
        var report = new Progress<ModelLoadStatus>(status => _host.Dispatch(() =>
        {
            progress.Report(status.Fraction, status.Message);
            _modelState = status.Message;
            RefreshModels();
        }));
        try
        {
            using var lease = await ModelProviders.Get(ModelProviders.Local).AcquireAsync(new ModelRequest(settings.Model), report, cancellationToken);
            _modelState = string.Create(CultureInfo.InvariantCulture,
                $"loaded{(lease.Loaded ? $" in {lease.LoadDuration.TotalSeconds:F1} s" : "")} · context {lease.ContextLength} · {(lease.Chat is null ? "no chat template" : "chat")} · text");
            _host.Record("ai.model.load", lease.LoadDuration.TotalMilliseconds, "ms");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _modelState = "failed: " + error.Message;
            _host.Log(EditorLogLevel.Error, $"The model {settings.Model} did not load.", error);
            await _host.ShowMessageAsync("AI: the model did not load", error.Message);
        }

        RefreshModels();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mcpShown?.Dispose();
        if (Mcp is { } server)
        {
            Mcp = null;
            _ = server.DisposeAsync().AsTask();
        }

        _closed();
    }

    // The design's "Models and devices" (docs/design/Models.dc.html) as a panel: where models run, the model by role, the
    // device and the limits, and how a name is resolved. The full page is phase D's.
    private void RefreshModels()
    {
        var settings = Settings;
        var items = new List<PanelItem>
        {
            new("Where models run: Local (this machine)", "Remote endpoint: later (A3)"),
            new($"Chat and agent: {(settings.Model.Length == 0 ? "(none set)" : settings.Model)}", _modelState, AiPlugin.LoadModelCommand),
            new($"Device: {settings.Device}", settings.Device.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "the fastest found" : null),
            new("Limits: " + Limits(settings), "A limit here sets Idrak's ComputeResources"),
            new("How a name is resolved: " + string.Join(", ", ModelSources.Names), "Idrak's model sources, asked in order; the same names work in the idrak tool"),
        };
        foreach (var loaded in AiPlugin.Local.Loaded)
        {
            items.Add(new PanelItem("Loaded: " + loaded.Name.Split('\n')[0], loaded.ActiveUsers > 0 ? "in use" : "idle"));
        }

        _models.SetItems(items);
    }

    private static string Limits(AiSettings settings)
    {
        var parts = new List<string>(3);
        if (settings.Threads > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{settings.Threads} CPU threads"));
        }

        if (settings.CpuMemoryMegabytes > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"CPU memory {settings.CpuMemoryMegabytes} MiB"));
        }

        if (settings.GpuMemoryMegabytes > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"device memory {settings.GpuMemoryMegabytes} MiB"));
        }

        return parts.Count == 0 ? "none set: Craft uses what the device has" : string.Join(", ", parts);
    }
}
