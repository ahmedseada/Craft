// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Ai;
using Idrak.Abstraction.Formats;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(AiPackage))]

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
/// the server's address in the status bar, and holds the "local" model provider whose status the Models page shows. The AI
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

    /// <summary>Explains the selection in the chat.</summary>
    public const string ExplainCommand = "ai.explainSelection";

    /// <summary>Proposes doc comments for the selection.</summary>
    public const string DocumentCommand = "ai.documentSelection";

    /// <summary>Proposes a refactoring of the selection.</summary>
    public const string RefactorCommand = "ai.refactorSelection";

    /// <summary>Writes tests for the selection in the chat.</summary>
    public const string TestsCommand = "ai.writeTests";

    /// <summary>Proposes a fix of the problem at the caret.</summary>
    public const string FixCommand = "ai.fixProblem";

    /// <summary>Explains the problem at the caret in the chat.</summary>
    public const string ExplainProblemCommand = "ai.explainProblem";

    /// <summary>Starts a new chat.</summary>
    public const string NewChatCommand = "ai.newChat";

    /// <summary>Reviews the agent's changed files in the diff view.</summary>
    public const string AgentReviewCommand = "ai.agentReview";

    /// <summary>Discards the agent's changes.</summary>
    public const string AgentDiscardCommand = "ai.agentDiscard";

    /// <summary>Stops the agent's run.</summary>
    public const string AgentStopCommand = "ai.agentStop";

    /// <summary>Shows the chat panel.</summary>
    public const string ShowChatCommand = "ai.showChat";

    /// <summary>Shows the agent's panel: the id the shell's rail item runs (Craft.Editor's ShellCommandIds.Agent), so the item shows while this plug-in is active.</summary>
    public const string ShowAgentCommand = "craft.agent";

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
        new EditorCommand(ExplainCommand, "AI: Explain the selection", (_, token) => Current!.RunActionAsync(CodeActionKinds.Explain, token),
            _ => Current?.HasSelection == true, "Explains the selected code in the chat"),
        new EditorCommand(DocumentCommand, "AI: Add doc comments to the selection", (_, token) => Current!.RunActionAsync(CodeActionKinds.Document, token),
            _ => Current?.HasSelection == true, "Proposes documentation comments as a diff you accept"),
        new EditorCommand(RefactorCommand, "AI: Refactor the selection", (_, token) => Current!.RunActionAsync(CodeActionKinds.Refactor, token),
            _ => Current?.HasSelection == true, "Proposes a clearer version of the selected code as a diff you accept"),
        new EditorCommand(TestsCommand, "AI: Write tests for the selection", (_, token) => Current!.RunActionAsync(CodeActionKinds.Tests, token),
            _ => Current?.HasSelection == true, "Writes tests in the chat; Apply as diff puts them in a document"),
        new EditorCommand(FixCommand, "AI: Fix the problem at the caret", (_, token) => Current!.RunActionAsync(CodeActionKinds.Fix, token),
            _ => Current?.HasProblemAtCaret == true, "Proposes a fix of the error or warning at the caret as a diff you accept"),
        new EditorCommand(ExplainProblemCommand, "AI: Explain the problem at the caret", (_, token) => Current!.RunActionAsync(CodeActionKinds.Explain, token),
            _ => Current?.HasProblemAtCaret == true, "Explains the error or warning at the caret in the chat"),
        new EditorCommand(NewChatCommand, "AI: New chat", (_, _) => { Current!.Chat.NewChat(); return Task.CompletedTask; },
            _ => Current is not null, "Clears the chat (and the one kept for the workspace)"),
        new EditorCommand(AgentReviewCommand, "AI: Review the agent's changes", (_, token) => Current!.Agent.ReviewAsync(token),
            _ => Current?.Agent.Changes.Count > 0, "Shows each file the agent changed as a diff you accept"),
        new EditorCommand(AgentDiscardCommand, "AI: Discard the agent's changes", (_, _) => { Current!.Agent.DiscardAll(); return Task.CompletedTask; },
            _ => Current?.Agent.Changes.Count > 0, "Drops the agent's working copy; your files were never changed"),
        new EditorCommand(AgentStopCommand, "AI: Stop the agent", (_, _) => { Current!.Agent.Stop(); return Task.CompletedTask; },
            _ => Current?.Agent.IsBusy == true, "Stops the agent's run; its changes so far stay to review"),
        new EditorCommand(ShowChatCommand, "AI: Show the chat", (_, _) => { Current!.ShowPanel(AiChatPanel.PanelId); return Task.CompletedTask; },
            _ => Current is not null, "Shows the chat column (a sheet in the narrow layout)"),
        new EditorCommand(ShowAgentCommand, "AI: Show the agent", (_, _) => { Current!.ShowPanel(AiAgentPanel.PanelId); return Task.CompletedTask; },
            _ => Current is not null, "Shows the agent run: a task on a copy of the folder, every change a diff, every command approved"),
    ];

    /// <inheritdoc />
    public string Name => AiSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "The AI plug-ins: inline completion, the chat, code actions and the agent, on the editor's tools (served over MCP when asked), the diff for every model edit and the local model provider";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => AiSettingShapes.Declared;

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

/// <summary>The AI host in one editor host: its MCP server, its status items and the model status the Models page reads. Used on the UI thread.</summary>
public sealed class AiSession : IDisposable
{
    private readonly IEditorHost _host;
    private readonly Action _closed;
    private readonly StatusItem _mcpStatus = new("ai.mcp", "", StatusAlignment.Right, 10, AiPlugin.McpShowCommand);
    private IDisposable? _mcpShown;
    private readonly StatusItem _completionStatus = new("ai.completion", "", StatusAlignment.Right, 9);
    private IDisposable? _completionShown;
    private readonly IEditorNotification _modelStatus;
    private string _modelState = "not loaded";
    private bool _disposed;

    internal AiSession(IEditorHost host, Action closed)
    {
        _host = host;
        _closed = closed;
        var settings = Settings;
        AiPlugin.Local.Settings = settings;
        Models = new AiModels(() => Settings)
        {
            Progress = new Progress<ModelLoadStatus>(status => _host.Dispatch("ai: a model load's stage", () => OnLoad(status))),
            Activities = host.Activities,
        };
        // Phase U: the model's state as a status line of the notification centre (U2 moves the side panel's content there).
        _modelStatus = host.ShowStatus("Chat and agent model", _modelState);
        Chat = new AiChatPanel(host, () => Settings, Models);
        Agent = new AiAgentPanel(host, () => Settings, Models);
        Completion = new AiInlineCompletion(() => Settings, Models, () => !_disposed) { Activities = host.Activities };
        Actions = new AiCodeActions(host, () => Settings, Models, Chat);
        Chat.RunAction = (name, token) => RunActionAsync(name switch
        {
            "doc" or "document" => CodeActionKinds.Document,
            "test" or "tests" => CodeActionKinds.Tests,
            var other => other,
        }, token);
        Completion.ModeChanged += (_, mode) => _host.Dispatch(() => ShowMode(mode));
        Completion.Completed += (_, stats) => _host.Dispatch(() =>
        {
            _host.Record("ai.completion.promptTokens", stats.PromptTokens, "tokens");
            _host.Record("ai.completion.cachedTokens", stats.CachedTokens, "tokens");
            _host.Record("ai.completion.tokensPerSecond", stats.TokensPerSecond, "tok/s");
        });
        InlineCompletionProviders.Register(Completion);
        CodeActionProviders.Register(Actions);
        _host.AddPanel(Chat);
        _host.AddPanel(Agent);
        Completion.Unavailable += (_, reason) => _host.Dispatch("ai: completion's state", () => ShowUnavailable(reason));
        RefreshModels();
        if (settings.LoadOnStart && settings.Model.Length > 0)
        {
            // Only when the user asked for it (loadOnStart); off by default: the model loads when it is picked or used.
            _host.Dispatch("ai: load the model on start (loadOnStart)", () => _ = LoadModelAsync(CancellationToken.None));
        }

        if (settings.McpOnStart)
        {
            _ = StartMcpAsync(CancellationToken.None);
        }
    }

    /// <summary>The MCP server while it runs, else null.</summary>
    public EditorMcpServer? Mcp { get; private set; }

    /// <summary>The models the plug-ins use (one lease per name, kept while the session lives).</summary>
    public AiModels Models { get; }

    /// <summary>The chat panel.</summary>
    public AiChatPanel Chat { get; }

    /// <summary>The agent run's panel.</summary>
    public AiAgentPanel Agent { get; }

    /// <summary>The inline completion provider ("ai").</summary>
    public AiInlineCompletion Completion { get; }

    /// <summary>The code actions ("ai").</summary>
    public AiCodeActions Actions { get; }

    /// <summary>Brings the panel <paramref name="id"/> into view.</summary>
    public void ShowPanel(string id) => _host.ShowPanel(id);

    /// <summary>Whether the active document has a selection.</summary>
    public bool HasSelection => _host.ActiveDocument is { Selection.IsEmpty: false };

    /// <summary>Whether there is an error or warning at the active caret's line.</summary>
    public bool HasProblemAtCaret => AiCodeActions.AtSelection(_host) is { Diagnostics.Count: > 0 };

    /// <summary>Runs the code action of <paramref name="kind"/> at the active selection; false (said so) when it does not apply.</summary>
    public async Task<bool> RunActionAsync(string kind, CancellationToken cancellationToken)
    {
        if (await Actions.RunAsync(kind, cancellationToken).ConfigureAwait(true))
        {
            return true;
        }

        await _host.ShowMessageAsync("AI: nothing to do here", kind is CodeActionKinds.Fix
            ? "Put the caret on a line with an error or a warning."
            : "Select the code first (explain, document, refactor and tests work on a selection; fix and explain also on a problem at the caret).").ConfigureAwait(true);
        return false;
    }

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

    /// <summary>
    /// Loads the chat model of the settings (the load command, the composer's model chip), through the same leases the
    /// chat, the agent and the code actions use (one copy, under one key): its progress and Cancel in the status bar (the
    /// load's activity), its state on the Models page; a failure (an out-of-memory one says what to do) in the
    /// notifications, never a modal message. The UI thread never waits on it.
    /// </summary>
    public async Task LoadModelAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        AiPlugin.Local.Settings = settings;
        if (settings.Model.Length == 0)
        {
            await _host.ShowMessageAsync("AI: no model", "Pick a model at the composer's model chip, or set the model (CRAFT_PLUGIN_AI_MODEL) on the settings page: a folder, a .gguf file, store:NAME or a Hugging Face id.");
            return;
        }

        Models.ForgetFailures();
        try
        {
            var lease = await Task.Run(() => Models.ChatAsync(cancellationToken), cancellationToken).ConfigureAwait(true);
            _modelState = string.Create(CultureInfo.InvariantCulture,
                $"loaded{(lease.Loaded ? $" in {lease.LoadDuration.TotalSeconds:F1} s" : "")} · context {lease.ContextLength} · {(lease.Chat is null ? "no chat template" : "chat")} · text");
            if (lease.Loaded)
            {
                _host.Record("ai.model.load", lease.LoadDuration.TotalMilliseconds, "ms");
            }

            AiPlugin.Local.SetState(_modelState);
        }
        catch (OperationCanceledException)
        {
            _modelState = "not loaded (cancelled)";
            AiPlugin.Local.SetState(_modelState);
        }
        catch (Exception error)
        {
            _modelState = "failed: " + AiErrors.Describe(error, settings);
            AiPlugin.Local.SetState(_modelState, isProblem: true);
            _host.Log(EditorLogLevel.Error, $"The model {settings.Model} did not load.", error);
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
        InlineCompletionProviders.Unregister(Completion.Name);
        CodeActionProviders.Unregister(Actions.Name);
        Chat.Dispose();
        Agent.Dispose();
        Models.Dispose();
        _modelStatus.Dispose();
        _completionShown?.Dispose();
        _mcpShown?.Dispose();
        if (Mcp is { } server)
        {
            Mcp = null;
            _ = server.DisposeAsync().AsTask();
        }

        _closed();
    }

    // A load's stages in the status bar while the AI plug-ins wait for their model.
    private void OnLoad(ModelLoadStatus status)
    {
        if (_disposed)
        {
            return;
        }

        // The status bar shows the load as AiModels' activity; here the panel's and the centre's state line.
        _modelState = status.Message;
        AiPlugin.Local.SetState(status.Message);
        RefreshModels();
    }

    // Completion has no model to use now (none loaded, its load failed): said once, quietly, in the status bar.
    private void ShowUnavailable(string? reason)
    {
        if (_disposed)
        {
            return;
        }

        if (reason is null)
        {
            if (_completionStatus.Text.StartsWith("Completion: off", StringComparison.Ordinal))
            {
                _completionStatus.Text = "Completion: ready";
                _completionStatus.Tooltip = null;
            }

            return;
        }

        _completionStatus.Text = "Completion: off — no model loaded";
        _completionStatus.Tooltip = reason;
        _completionShown ??= _host.AddStatusItem(_completionStatus);
    }

    // How completion lays its prompt out, said in the status bar (prefix-only is never silent).
    private void ShowMode(CompletionMode mode)
    {
        if (_disposed)
        {
            return;
        }

        _completionStatus.Text = mode.Format is null ? "Completion: prefix-only" : $"Completion: {mode.Format}";
        _completionStatus.Tooltip = mode.Text;
        _completionShown ??= _host.AddStatusItem(_completionStatus);
        if (mode.Format is null)
        {
            _host.Log(EditorLogLevel.Information, mode.Text);
        }
    }

    // Phase U2: the right-hand "Models and devices" panel is gone. What it said (where models run, the model by role and
    // its state, the device, the limits, how a name is resolved, what is loaded) is the provider's status, which the
    // Models page (a tab of the editor area) and the model chip read, and the model's state line in phase U's
    // notification centre (IEditorHost.ShowStatus); a load's progress is an activity in the status bar.
    private void RefreshModels()
    {
        var settings = Settings;
        AiPlugin.Local.Changed();
        _modelStatus.Update($"{(settings.Model.Length == 0 ? "(none set)" : settings.Model)} · {_modelState} · device {settings.Device}");
    }
}
