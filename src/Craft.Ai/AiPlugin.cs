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
        // The editor's MCP server as plug-ins reach it (the Claude Code plug-in connects an outside agent to it).
        Part(() => EditorMcpServers.Register(AiMcpServer.Instance), () => EditorMcpServers.Unregister(EditorMcpServers.Editor));

        // The editor's context menu (phase X): the actions on the selection and the problem at the caret, each run only
        // when picked (opening the menu asks only whether each can run, which loads nothing).
        int order = 0;
        foreach (var id in (ReadOnlySpan<string>)[AiPlugin.ExplainCommand, AiPlugin.DocumentCommand, AiPlugin.RefactorCommand, AiPlugin.TestsCommand, AiPlugin.FixCommand, AiPlugin.ExplainProblemCommand])
        {
            var item = new MenuItem(EditorContextMenus.Editor, id, Order: order++, Group: 50);
            Part(() => EditorMenus.Register(item), () => EditorMenus.Unregister(EditorContextMenus.Editor, item.CommandId));
        }

        // X part 7: the explorer's and the tabs' menus, on a file only (a "when" over the target), with the design's icons.
        foreach (var item in (ReadOnlySpan<MenuItem>)
        [
            new(EditorContextMenus.Tree, AiPlugin.AddToChatCommand, "Add to chat context", 10, 3) { Icon = MenuIcons.Chat, When = MenuWhen.Files },
            new(EditorContextMenus.Tree, AiPlugin.AskAgentCommand, "Ask the agent about it", 11, 3) { Icon = RailIcons.Agent, When = MenuWhen.Files },
            new(EditorContextMenus.Tab, AiPlugin.AddToChatCommand, "Add to chat context", 0, 7) { Icon = MenuIcons.Chat, When = MenuWhen.Files },
        ])
        {
            var added = item;
            Part(() => EditorMenus.Register(added), () => EditorMenus.Unregister(added.Menu, added.CommandId));
        }
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

    /// <summary>Loads the chat's local model (the setting <c>model</c>).</summary>
    public const string LoadModelCommand = "ai.loadModel";

    /// <summary>Loads the agent's local model (the setting <c>agentModel</c>; empty: the chat's, shared).</summary>
    public const string LoadAgentModelCommand = "ai.loadAgentModel";

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

    /// <summary>Adds the menu's file (the explorer's row, the tab's) to the chat's context (X part 7).</summary>
    public const string AddToChatCommand = "ai.addFileToChat";

    /// <summary>Puts the menu's file in the agent's input, to ask about it (X part 7).</summary>
    public const string AskAgentCommand = "ai.askAgentAboutFile";

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
            _ => Current is not null, "Loads the chat's model (CRAFT_PLUGIN_AI_MODEL), with its progress"),
        new EditorCommand(LoadAgentModelCommand, "AI: Load the agent's model", (_, token) => Current!.LoadModelAsync(ModelRoleKeys.Agent, token),
            _ => Current is not null, "Loads the agent's model (CRAFT_PLUGIN_AI_AGENTMODEL; empty: the chat's, one copy shared), with its progress"),
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
        new EditorCommand(AddToChatCommand, "AI: Add the file to the chat's context", (host, token) => Current!.AddFileAsync(FileOf(host), agent: false, token),
            host => Current is not null && FileOf(host) is not null, "Adds the file as a chip of the chat's context")
        {
            Reason = _ => Current is null ? "The AI plug-in is not active." : "A new file is not on the disk yet.",
        },
        new EditorCommand(AskAgentCommand, "AI: Ask the agent about the file", (host, token) => Current!.AddFileAsync(FileOf(host), agent: true, token),
            host => Current is not null && FileOf(host) is not null, "Puts the file in the agent's input")
        {
            Reason = _ => Current is null ? "The AI plug-in is not active." : "A new file is not on the disk yet.",
        },
        new EditorCommand(ShowChatCommand, "AI: Show the chat", (_, _) => { Current!.ShowPanel(AiChatPanel.PanelId); return Task.CompletedTask; },
            _ => Current is not null, "Shows the chat column (a sheet in the narrow layout)"),
        new EditorCommand(ShowAgentCommand, "AI: Show the agent", (_, _) => { Current!.ShowPanel(AiAgentPanel.PanelId); return Task.CompletedTask; },
            _ => Current is not null, "Shows the agent run: a task on a copy of the folder, every change a diff, every command approved"),
    ];

    // The file a context menu was opened on (a file's row, a tab), else the active document's; read before any await.
    private static string? FileOf(IEditorHost host) =>
        EditorContextMenus.Target is { Path: { } path, IsFolder: false } ? path : EditorContextMenus.Target is null ? host.ActiveDocument?.FilePath : null;

    /// <inheritdoc />
    public string Name => AiSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "The AI plug-ins: inline completion, the inline chat (Ctrl+I), the chat, code actions, the agent, Review changes and Fix with AI, on the editor's tools (served over MCP when asked), the diff for every model edit and the local model provider";

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
        AiPlugin.Local.SettingsSource = () => Settings;        // the chip and the status bar follow the settings page (note 25)
        Models = new AiModels(() => Settings)
        {
            Progress = new Progress<ModelLoadStatus>(status => _host.Dispatch("ai: a model load's stage", () => OnLoad(status))),
            Activities = host.Activities,
        };
        // Each role's draft (speculative decoding) on the chip and the status bar: "draft: 1.5B, 72% accepted".
        AiPlugin.Local.DraftSource = Models.DraftOf;
        Models.DraftChanged += (_, _) => AiPlugin.Local.RaiseStatusChanged();
        // Phase U: the model's state as a status line of the notification centre (U2 moves the side panel's content there).
        _modelStatus = host.ShowStatus("Models", _modelState);
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
        // X part 8: the git plug-in's Generate commit message writes with a model already loaded, through these leases.
        CommitMessages = new AiCommitMessages(() => Settings, Models);
        CommitMessageProviders.Register(CommitMessages);
        // X part 9 item 23 and X part 11 items 4 and 5: the inline chat (Ctrl+I), Review changes, Fix with AI.
        InlineChat = new AiInlineChat(() => Settings, Models, token => LoadModelAsync(ModelRoleKeys.Chat, token));
        InlineChatProviders.Register(InlineChat);
        Review = new AiCodeReview(() => Settings, Models);
        CodeReviewProviders.Register(Review);
        BuildFix = new AiBuildFix(host, () => Settings, Actions, Agent);
        BuildFixProviders.Register(BuildFix);
        _host.AddPanel(Chat);
        _host.AddPanel(Agent);
        Completion.Unavailable += (_, reason) => _host.Dispatch("ai: completion's state", () => ShowUnavailable(reason));
        RefreshModels();
        if (settings.LoadOnStart && settings.ChatEnabled && settings.Model.Length > 0)
        {
            // Only when the user asked for it (loadOnStart); off by default: the model loads when it is picked or used.
            _host.Dispatch("ai: load the model on start (loadOnStart)", () => Forget(LoadModelAsync(CancellationToken.None), "The model's load on start failed."));
        }

        if (settings.McpOnStart)
        {
            Forget(StartMcpAsync(CancellationToken.None), "The MCP server's start failed.");
        }
    }

    // A task nobody awaits (started on its own): its failure goes to the log, never to a "background task nobody awaited" report.
    private void Forget(Task task, string what) => _ = task.ContinueWith(done =>
    {
        var error = done.Exception?.GetBaseException();
        try
        {
            _host.Log(EditorLogLevel.Error, what, error);
        }
#pragma warning disable CA1031 // A closed host's log: nothing more to say, and nothing may leave this continuation.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>The MCP server while it runs, else null.</summary>
    public EditorMcpServer? Mcp { get; private set; }

    /// <summary>The models the plug-ins use (one lease per name, kept while the session lives).</summary>
    public AiModels Models { get; }

    /// <summary>"ai" in <see cref="CommitMessageProviders"/>: a commit message written with a model already loaded.</summary>
    public AiCommitMessages CommitMessages { get; }

    /// <summary>"ai" in <see cref="InlineChatProviders"/>: the inline chat's answers (Ctrl+I).</summary>
    public AiInlineChat InlineChat { get; }

    /// <summary>"ai" in <see cref="CodeReviewProviders"/>: Source Control's Review changes.</summary>
    public AiCodeReview Review { get; }

    /// <summary>"ai" in <see cref="BuildFixProviders"/>: a failed build's Fix with AI.</summary>
    public AiBuildFix BuildFix { get; }

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

    /// <summary>
    /// Shows the chat (or the agent, <paramref name="agent"/>) and gives it <paramref name="path"/> as a dropped file: a
    /// chip of the chat's context, or "@path" in the agent's input (X part 7); what it could not take is said.
    /// </summary>
    public async Task AddFileAsync(string? path, bool agent, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return;
        }

        IConversationPanel panel = agent ? Agent : Chat;
        _host.ShowPanel(panel.Id);
        if (await panel.AcceptDropAsync([path], cancellationToken).ConfigureAwait(true) == 0)
        {
            _host.ShowStatus(agent ? "Ask the agent" : "Add to chat", $"{Path.GetFileName(path)} could not be added (a folder, or a file it does not read).", NotificationSeverity.Warning);
        }
    }

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
            AiMcpServer.Instance.RaiseChanged();
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
        AiMcpServer.Instance.RaiseChanged();
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
    public Task LoadModelAsync(CancellationToken cancellationToken) => LoadModelAsync(ModelRoleKeys.Chat, cancellationToken);

    /// <summary>
    /// Loads the model of the role <paramref name="role"/> (<see cref="ModelRoleKeys.Chat"/>, <see cref="ModelRoleKeys.Agent"/>)
    /// as <see cref="LoadModelAsync(CancellationToken)"/> does the chat's: the agent's chip runs it for the agent's model
    /// (the chat's lease itself when they name the same). A role turned off says so and loads nothing.
    /// </summary>
    public async Task LoadModelAsync(string role, CancellationToken cancellationToken)
    {
        var settings = Settings;
        AiPlugin.Local.Settings = settings;
        var resolved = settings.RoleOf(role);
        if (!resolved.Enabled)
        {
            await _host.ShowMessageAsync("AI: turned off", AiModels.Off(role));
            return;
        }

        if (resolved.Model.Length == 0)
        {
            await _host.ShowMessageAsync("AI: no model", "Pick a model at the composer's model chip, or set the model (CRAFT_PLUGIN_AI_MODEL) on the settings page: a folder, a .gguf file, store:NAME or a Hugging Face id.");
            return;
        }

        if (AiModels.EngineRuns(resolved.Model) is { } engine)
        {
            // An agent engine's model (Claude Code) never goes to the model provider: said, nothing loaded.
            _host.Notify("AI: nothing to load", engine);
            return;
        }

        Models.ForgetFailures();
        try
        {
            var lease = await Task.Run(() => Models.RoleAsync(role, cancellationToken), cancellationToken).ConfigureAwait(true);
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
            _modelState = "failed: " + AiErrors.Describe(error, settings, role);
            AiPlugin.Local.SetState(_modelState, isProblem: true);
            _host.Log(EditorLogLevel.Error, $"The model {resolved.Model} did not load.", error);
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
        CommitMessageProviders.Unregister(CommitMessages.Name);
        InlineChatProviders.Unregister(InlineChat.Name);
        CodeReviewProviders.Unregister(Review.Name);
        BuildFixProviders.Unregister(BuildFix.Name);
        Chat.Dispose();
        Agent.Dispose();
        if (ReferenceEquals(AiPlugin.Local.DraftSource?.Target, Models))
        {
            AiPlugin.Local.DraftSource = null;                           // a closed session's drafts are not shown
        }

        Models.Dispose();
        _modelStatus.Dispose();
        _completionShown?.Dispose();
        _mcpShown?.Dispose();
        if (Mcp is { } server)
        {
            Mcp = null;
            AiMcpServer.Instance.RaiseChanged();
            Forget(server.DisposeAsync().AsTask(), "The MCP server did not stop cleanly.");
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
        // Each role's model and device (note 25); the agent's "same as chat" when it takes the chat's.
        var agent = settings.AgentRole;
        string Name(AiRoleSettings role) => !role.Enabled ? "off" : role.Model.Length == 0 ? "(none set)" : $"{role.Model} · {role.Device}";
        _modelStatus.Update($"Chat: {Name(settings.ChatRole)} · {_modelState}; agent: {(agent.Enabled && agent.InheritsModel && agent.InheritsDevice ? "same as chat" : Name(agent))}; completion: {Name(settings.CompletionRole)}");
    }
}

/// <summary>
/// The editor's MCP server as plug-ins reach it (<see cref="EditorMcpServers.Editor"/>): the active AI session's server,
/// its address and token while it runs, and <see cref="AiPlugin.McpStartCommand"/> to start it.
/// </summary>
public sealed class AiMcpServer : IEditorMcpServer
{
    private AiMcpServer()
    {
    }

    /// <summary>The one instance.</summary>
    public static AiMcpServer Instance { get; } = new();

    /// <inheritdoc />
    public string Name => EditorMcpServers.Editor;

    /// <inheritdoc />
    public string Summary => "The editor's live tools over MCP (the AI plug-in's server; loopback, a bearer token)";

    /// <inheritdoc />
    public string? Address => AiPlugin.Current?.Mcp?.Address;

    /// <inheritdoc />
    public string? Token => AiPlugin.Current?.Mcp?.Token;

    /// <inheritdoc />
    public string StartCommand => AiPlugin.McpStartCommand;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Says the server started or stopped.</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
