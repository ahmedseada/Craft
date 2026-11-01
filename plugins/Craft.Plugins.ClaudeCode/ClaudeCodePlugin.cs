// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Craft.Plugins.ClaudeCode;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(ClaudeCodePackage))]

namespace Craft.Plugins.ClaudeCode;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.ClaudeCode\.</summary>
public sealed class ClaudeCodePackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.ClaudeCode";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new ClaudeCodeRegistration()];
}

/// <summary>
/// Registers the editor plug-in "claudeCode", the agent engine "claude-code", the MCP tool set "craft-diagnostics", the
/// report action "Ask Claude Code", the commands and their Run menu items, each with how to take it back.
/// </summary>
public sealed class ClaudeCodeRegistration() : IdrakPlugin(ClaudeCodeSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(ClaudeCodePlugin.Instance), () => EditorPlugins.Unregister(ClaudeCodeSettings.PluginName));
        Part(() => AgentEngines.Register(ClaudeEngine.Instance), () => AgentEngines.Unregister(ClaudeEngine.EngineName));
        Part(() => EditorToolSets.Register(DiagnosticToolSet.Instance), () => EditorToolSets.Unregister(DiagnosticToolSet.SetName));
        Part(() => EditorReportActions.Register(ClaudeReportAction.Instance), () => EditorReportActions.Unregister(ClaudeReportAction.ActionName));
        int order = 0;
        foreach (var command in ClaudeCodePlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
            var item = new MenuItem(EditorMenus.Run, command.Name, Order: order++, Group: 80);
            Part(() => EditorMenus.Register(item), () => EditorMenus.Unregister(EditorMenus.Run, item.CommandId));
        }
    }
}

/// <summary>
/// Claude Code as an editor plug-in ("claudeCode"): while active it finds the user's Claude Code (off the UI thread: its
/// program, version and flags, and the accounts), shows its version in the status bar, and runs it for the agent engine
/// and the commands. Its settings are on the settings page's Agent section (<c>CRAFT_PLUGIN_CLAUDECODE_*</c>).
/// </summary>
public sealed class ClaudeCodePlugin : IEditorPlugin
{
    /// <summary>Shows the agent panel answered by Claude Code.</summary>
    public const string OpenCommand = "claudeCode.open";

    /// <summary>Asks Claude Code about the latest crash or caught failure.</summary>
    public const string CrashCommand = "claudeCode.diagnoseCrash";

    /// <summary>Asks Claude Code about the latest stall.</summary>
    public const string StallCommand = "claudeCode.diagnoseStall";

    /// <summary>Asks Claude Code to test Craft's MCP server.</summary>
    public const string McpCommand = "claudeCode.testMcp";

    /// <summary>Asks Claude Code to build and test Craft and Idrak on this machine.</summary>
    public const string BuildCommand = "claudeCode.buildAndTest";

    /// <summary>Finds the program again and says what was found, with the accounts.</summary>
    public const string FindCommand = "claudeCode.find";

    /// <summary>Adds an account (its own configuration folder in Craft's data folder) and starts its login.</summary>
    public const string AddAccountCommand = "claudeCode.addAccount";

    /// <summary>Runs an account's login again.</summary>
    public const string LoginCommand = "claudeCode.login";

    /// <summary>Removes an account (its folder, after a confirm).</summary>
    public const string RemoveAccountCommand = "claudeCode.removeAccount";

    private ClaudeCodePlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static ClaudeCodePlugin Instance { get; } = new();

    /// <summary>The session of the host it is active in, or null.</summary>
    public static ClaudeCodeSession? Session { get; private set; }

    /// <summary>Its commands (in the palette and the Run menu).</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(OpenCommand, "Claude Code: open in the agent panel", (_, _) => Session!.AskAsync(""), _ => Session is not null,
            "The agent panel answers with Claude Code (your account, your machine); every change is a diff you accept"),
        new EditorCommand(CrashCommand, "Claude Code: diagnose the last crash", (_, token) => Session!.DiagnoseAsync(DiagnosticReportKinds.Crash, token), _ => Session is not null,
            "Asks Claude Code about the latest crash or caught failure, naming its report and Craft's diagnostic MCP tools"),
        new EditorCommand(StallCommand, "Claude Code: diagnose the last stall", (_, token) => Session!.DiagnoseAsync(DiagnosticReportKinds.Stall, token), _ => Session is not null,
            "Asks Claude Code about the latest stall of the window, naming its report"),
        new EditorCommand(McpCommand, "Claude Code: test Craft's MCP server", (_, _) => Session!.PromptAsync(s => s.PromptMcp), _ => Session is not null,
            "Asks Claude Code to call each of Craft's read-only MCP tools and report what failed"),
        new EditorCommand(BuildCommand, "Claude Code: build and test on this machine", (_, _) => Session!.PromptAsync(s => s.PromptBuild), _ => Session is not null,
            "Asks Claude Code to build and test Craft and Idrak here, with this machine's CPU and GPU"),
        new EditorCommand(FindCommand, "Claude Code: find the program again", (_, token) => Session!.FindAgainAsync(token), _ => Session is not null,
            "Looks for claude on PATH (or its setting) again, and says what was found and which accounts Craft has"),
        new EditorCommand(AddAccountCommand, "Claude Code: add an account…", (_, token) => Session!.AddAccountAsync(token), _ => Session is not null,
            "A named account with its own Claude Code configuration in Craft's data folder; you log in with Claude Code's own login"),
        new EditorCommand(LoginCommand, "Claude Code: log in again…", (_, token) => Session!.LoginAgainAsync(token), _ => Session is not null,
            "Runs Claude Code's own login for an account, in a terminal tab"),
        new EditorCommand(RemoveAccountCommand, "Claude Code: remove an account…", (_, token) => Session!.RemoveAccountAsync(token), _ => Session is not null,
            "Deletes an account's folder and its sign-in, after you confirm"),
    ];

    /// <inheritdoc />
    public string Name => ClaudeCodeSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Claude Code as the agent: your own CLI, headless, one model per account at the agent's chip; Craft's MCP diagnostics; Ask Claude Code on a failure or a stall";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } = PluginSettingShapes.Apply(ClaudeCodeSettings.Declared, static _ => SettingSections.Agent);

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new ClaudeCodeSession(host);
        Session = session;
        session.Start();
        return new Deactivation(session);
    }

    private sealed class Deactivation(ClaudeCodeSession session) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(Session, session))
            {
                Session = null;
            }

            session.Dispose();
            AgentEngines.RaiseChanged();
        }
    }
}

/// <summary>"Ask Claude Code" on the failure dialog and the stall notice: the agent panel answers with Claude Code, the report named.</summary>
public sealed class ClaudeReportAction : IEditorReportAction
{
    /// <summary>The action's name.</summary>
    public const string ActionName = "claude-code";

    private ClaudeReportAction()
    {
    }

    /// <summary>The action.</summary>
    public static ClaudeReportAction Instance { get; } = new();

    /// <inheritdoc />
    public string Name => ActionName;

    /// <inheritdoc />
    public string Summary => "Opens the agent panel answered by Claude Code, with the report named";

    /// <inheritdoc />
    public string Label => "Ask Claude Code";

    /// <inheritdoc />
    public bool IsAvailable => ClaudeCodePlugin.Session is { Program: not null };

    /// <inheritdoc />
    public Task RunAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ClaudeCodePlugin.Session is { } session ? session.AskAboutAsync(report) : Task.CompletedTask;
    }
}

/// <summary>
/// The agent engine "claude-code": the user's Claude Code CLI, headless, one model per account ("Claude Code (default)", "Claude
/// Code (work)"), run by the active session. None where Claude Code cannot run (a phone) or the plug-in is not active.
/// </summary>
public sealed class ClaudeEngine : IAgentEngine
{
    /// <summary>The engine's name.</summary>
    public const string EngineName = "claude-code";

    private ClaudeEngine()
    {
    }

    /// <summary>The engine.</summary>
    public static ClaudeEngine Instance { get; } = new();

    /// <inheritdoc />
    public string Name => EngineName;

    /// <inheritdoc />
    public string Summary => "Claude Code (your own CLI, headless, on this machine): its own tools on the agent's copy of the folder, Craft's tools over MCP";

    /// <inheritdoc />
    public IReadOnlyList<string> Models => ClaudeProgram.Unavailable is null && ClaudeCodePlugin.Session is { } session ? session.Models : [];

    /// <inheritdoc />
    public string? Check(string model) =>
        ClaudeProgram.Unavailable ?? (ClaudeCodePlugin.Session is { } session ? session.Check(model) : "The Claude Code plug-in is not active (Plug-ins page).");

    /// <inheritdoc />
    public Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken = default) =>
        ClaudeCodePlugin.Session is { } session
            ? session.RunAsync(request, report, cancellationToken)
            : Task.FromResult(new AgentEngineResult("The Claude Code plug-in is not active (Plug-ins page).", null) { Failed = true });
}

/// <summary>
/// The plug-in in one host: the program, its version and flags, the accounts (found off the UI thread at its start and
/// on "find again"), the status item, the engine's runs (a process each, its stream read as steps), the approvals given
/// for a run, and the commands' prompts sent to the agent panel.
/// </summary>
public sealed class ClaudeCodeSession : IDisposable
{
    private readonly IEditorHost _host;
    private readonly StatusItem _status = new("claudeCode.version", "Claude Code", StatusAlignment.Right, 9, ClaudeCodePlugin.OpenCommand);
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private IDisposable? _shown;
    private Task _found = Task.CompletedTask;
    private IReadOnlyList<ClaudeAccount> _accounts = [ClaudeAccounts.Default];
    private bool _disposed;

    /// <summary>The session in <paramref name="host"/>.</summary>
    public ClaudeCodeSession(IEditorHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>The program, once found (null: not found, said in <see cref="Problem"/>).</summary>
    public ClaudeProgram? Program { get; private set; }

    /// <summary>Its version and flags, once read.</summary>
    public ClaudeFlags? Flags { get; private set; }

    /// <summary>Why it cannot run (not found, no headless flags), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>The accounts: "default" (the CLI's own configuration), then Craft's own, by name.</summary>
    public IReadOnlyList<ClaudeAccount> Accounts => Volatile.Read(ref _accounts);

    /// <summary>The models the agent's chip lists: one per account ("Claude Code (default)", "Claude Code (work)").</summary>
    public IReadOnlyList<string> Models => [.. Accounts.Select(a => a.Model)];

    /// <summary>Craft's data folder (the accounts' and the MCP config's place): the host's, else the local application data's "Craft".</summary>
    public string DataFolder => _host.DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Craft");

    /// <summary>Completes when the program and the accounts were found.</summary>
    public Task Found => _found;

    /// <summary>The settings now (the defaults when one does not parse; the problem is said in Output).</summary>
    public ClaudeCodeSettings Settings
    {
        get
        {
            try
            {
                return ClaudeCodeSettings.From(_host.Settings);
            }
            catch (ArgumentException error)
            {
                _host.Log(EditorLogLevel.Warning, "Claude Code: a setting was not taken: " + error.Message);
                return new ClaudeCodeSettings();
            }
        }
    }

    /// <summary>Starts finding the program and the accounts, and shows the status item.</summary>
    public void Start()
    {
        _shown = _host.AddStatusItem(_status);
        _status.Text = "Claude Code…";
        _status.Tooltip = "Looking for Claude Code and your accounts";
        _found = FindAsync(CancellationToken.None);
    }

    /// <summary>Finds again and says what was found.</summary>
    public async Task FindAgainAsync(CancellationToken cancellationToken)
    {
        _found = FindAsync(cancellationToken);
        await _found.ConfigureAwait(true);
        string accounts = string.Join("\n", Accounts.Select(a => $"{a.Name} · {(a.Folder ?? "the CLI's own configuration")} · {LoginText(a.IsLoggedIn)}"));
        await _host.ShowMessageAsync("Claude Code", (Problem ?? $"Claude Code {Flags?.Version} at {Program?.Path}") + "\n\nAccounts:\n" + accounts).ConfigureAwait(true);
    }

    // Off the UI thread: the program on PATH, its --version and --help, the accounts (setting, then found as text).
    private async Task FindAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        string data = DataFolder;
        var found = await Task.Run(async () =>
        {
            var program = ClaudeProgram.Find(settings.Path, out var why);
            ClaudeFlags? flags = null;
            if (program is not null)
            {
                try
                {
                    flags = await program.FlagsAsync(cancellationToken).ConfigureAwait(false);
                    var missing = flags.MissingForHeadless();
                    if (missing.Count > 0)
                    {
                        why = $"This Claude Code ({flags.Version}) does not support {string.Join(" and ", missing)}: not supported by this version. Update it (claude update).";
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
                {
                    why = $"Claude Code at {program.Path} did not answer --version: {error.Message}";
                }
            }

            return (program, flags, why, ClaudeAccounts.List(data));
        }, cancellationToken).ConfigureAwait(false);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Dispatch("claude code: found", () =>
        {
            if (!_disposed)
            {
                Program = found.program;
                Flags = found.flags;
                Problem = found.why;
                Volatile.Write(ref _accounts, found.Item4);
                _status.Text = found.program is null ? "Claude Code: not found" : found.why is null ? $"Claude Code {found.flags?.Version}" : "Claude Code: needs an update";
                _status.Tooltip = (found.why ?? $"Claude Code {found.flags?.Version} at {found.program?.Path}: pick it at the agent's model chip.")
                    + "\nAccounts: " + string.Join(", ", found.Item4.Select(a => a.Name)) + " (Claude Code: add an account…)";
                _host.Log(found.why is null ? EditorLogLevel.Information : EditorLogLevel.Warning, "Claude Code: " + _status.Tooltip.Replace('\n', ' '));
                AgentEngines.RaiseChanged();
            }

            done.TrySetResult();
        });
        await done.Task.ConfigureAwait(false);
    }

    /// <summary>Why <paramref name="model"/> cannot run now, or null.</summary>
    public string? Check(string model)
    {
        if (!_found.IsCompleted)
        {
            return null;                                              // the run waits for the search
        }

        if (Program is null || (Problem is not null && Flags is null))
        {
            return Problem ?? "Claude Code was not found. " + ClaudeProgram.InstallHint;
        }

        if (Flags is { } flags && flags.MissingForHeadless() is { Count: > 0 } missing)
        {
            return $"This Claude Code ({flags.Version}) does not support {string.Join(" and ", missing)}: not supported by this version. Update it (claude update).";
        }

        return ClaudeAccounts.AccountOf(model) is { } name && AccountFor(model) is null
            ? $"Craft has no Claude Code account {name}: add it with Claude Code: add an account…, or pick another at the agent's model chip."
            : null;
    }

    private ClaudeAccount? AccountFor(string model) =>
        ClaudeAccounts.AccountOf(model) is { } name ? Accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;

    private static string LoginText(bool? loggedIn) => loggedIn switch
    {
        true => "logged in",
        false => "not logged in",
        _ => "its sign-in is the CLI's own",
    };

    /// <summary>Whether <paramref name="tool"/> was allowed for this run (the approval's "Allow for this run").</summary>
    public bool IsAllowed(string tool)
    {
        lock (_gate)
        {
            return _allowed.Contains(tool);
        }
    }

    /// <summary>Allows <paramref name="tool"/> for the rest of this run.</summary>
    public void Allow(string tool)
    {
        lock (_gate)
        {
            _allowed.Add(tool);
        }
    }

    /// <summary>
    /// The arguments of a headless run: the print mode and the stream, the permission mode, Craft's MCP config and its
    /// approvals when the server runs, the session resumed, the user's own; each flag only when this version has it (what
    /// it lacks is said in <paramref name="notes"/>). The task goes on standard input.
    /// </summary>
    public static IReadOnlyList<string> Arguments(ClaudeFlags flags, ClaudeCodeSettings settings, string? mcpConfig, string? resume, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notes);
        var arguments = new List<string> { flags.Has("--print") ? "--print" : "-p", "--output-format", "stream-json" };
        if (flags.Has("--verbose"))
        {
            arguments.Add("--verbose");                              // stream-json in print mode needs it
        }

        if (settings.PermissionMode.Length > 0)
        {
            if (flags.Has("--permission-mode"))
            {
                arguments.AddRange(["--permission-mode", settings.PermissionMode]);
            }
            else
            {
                notes.Add("--permission-mode is not supported by this version: Claude Code keeps its own mode.");
            }
        }

        if (mcpConfig is not null)
        {
            if (flags.Has("--mcp-config"))
            {
                arguments.AddRange(["--mcp-config", mcpConfig]);
                if (settings.Approvals)
                {
                    if (flags.Has("--permission-prompt-tool"))
                    {
                        arguments.AddRange(["--permission-prompt-tool", $"mcp__{McpConfig.ServerName}__{DiagnosticToolNames.Approve}"]);
                    }
                    else
                    {
                        notes.Add("--permission-prompt-tool is not supported by this version: Craft cannot ask you before a command; Claude Code refuses what needs permission.");
                    }
                }
            }
            else
            {
                notes.Add("--mcp-config is not supported by this version: Claude Code runs without Craft's tools.");
            }
        }

        if (resume is not null)
        {
            if (flags.Has("--resume"))
            {
                arguments.AddRange(["--resume", resume]);
            }
            else
            {
                notes.Add("--resume is not supported by this version: the follow-up starts a new Claude Code session.");
            }
        }

        arguments.AddRange(SplitArguments(settings.Arguments));
        return arguments;
    }

    /// <summary>A command line's words (double quotes keep spaces).</summary>
    public static IReadOnlyList<string> SplitArguments(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    any = false;
                }
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }

        if (any)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>
    /// Runs <paramref name="request"/>: Claude Code headless in the agent's copy of the folder, as the model's account,
    /// connected to Craft's MCP server when it runs (or started, or without it, as <c>mcpWhenOff</c> says), its stream
    /// reported as steps; cancelled, its process tree ends. Off the UI thread.
    /// </summary>
    public async Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        await _found.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Check(request.Model) is { } problem)
        {
            return new AgentEngineResult(problem, null) { Failed = true };
        }

        lock (_gate)
        {
            _allowed.Clear();                                         // "for this run" starts again
        }

        // An account Craft knows is not logged in: ask to log in rather than fail.
        var account = AccountFor(request.Model) ?? ClaudeAccounts.Default;
        if (account.IsLoggedIn == false)
        {
            return await AskLoginAsync(account, cancellationToken).ConfigureAwait(false);
        }

        var settings = Settings;
        var notes = new List<string>();
        string? config = await McpConfigAsync(settings, request.Workspace, notes, cancellationToken).ConfigureAwait(false);
        var arguments = Arguments(Flags!, settings, config, request.Resume, notes);
        foreach (var note in notes)
        {
            report(new AgentEngineEvent(AgentEngineStep.Note, note));
        }

        var info = Program!.StartInfo(arguments, request.Folder);
        foreach (var (name, value) in account.Environment)
        {
            info.Environment[name] = value;
        }

        report(new AgentEngineEvent(AgentEngineStep.Note, account.Folder is null
            ? "As the account default (the CLI's own sign-in)."
            : $"As the account {account.Name} (its own configuration, {account.Folder})."));
        var result = await RunProcessAsync(info, request.Task, report, cancellationToken).ConfigureAwait(false);
        return result.Failed && NeedsLogin(result.End) ? await AskLoginAsync(account, cancellationToken).ConfigureAwait(false) : result;
    }

    /// <summary>Whether a run's end says Claude Code is not signed in (its own words: log in, /login, an API key, authentication).</summary>
    public static bool NeedsLogin(string end)
    {
        ArgumentNullException.ThrowIfNull(end);
        foreach (var words in (ReadOnlySpan<string>)["/login", "log in", "login", "not logged", "invalid api key", "api key", "authenticat", "oauth"])
        {
            if (end.Contains(words, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Off the UI thread: asks to log in to the account (Log in opens the CLI's own login in a terminal tab); the run's end.
    private async Task<AgentEngineResult> AskLoginAsync(ClaudeAccount account, CancellationToken cancellationToken)
    {
        var question = new EditorQuestion($"Log in to Claude Code ({account.Name})",
            $"The account {account.Name} is not logged in. Claude Code's own login opens in a terminal tab; Craft never sees your password or token.",
            ["Log in", "Not now"]) { Dismissed = 1 };
        bool login = await OnUiAsync(() => _host.AskAsync(question, cancellationToken)).Unwrap().ConfigureAwait(false) == 0;
        if (login)
        {
            await OnUiAsync(() => LoginAsync(account)).Unwrap().ConfigureAwait(false);
        }

        return new AgentEngineResult(login
            ? $"Log in to {account.Name} in the terminal tab that opened (Claude Code's own steps), then send the task again."
            : $"Not run: the account {account.Name} is not logged in (Claude Code: log in again…).", null);
    }

    /// <summary>
    /// Opens Claude Code's own login for <paramref name="account"/> in a terminal tab (its folder as CLAUDE_CONFIG_DIR):
    /// the CLI's <c>login</c> command when its help lists one, else Claude Code itself, whose first run with a new folder
    /// walks through its login (<c>/login</c> inside it logs in again). On the UI thread; Craft reads nothing it types.
    /// </summary>
    public async Task LoginAsync(ClaudeAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (Program is not { } program || ClaudeProgram.Unavailable is not null)
        {
            await _host.ShowMessageAsync("Claude Code", Problem ?? ClaudeProgram.Unavailable ?? "Claude Code was not found. " + ClaudeProgram.InstallHint).ConfigureAwait(true);
            return;
        }

        if (_host.Terminals is not { } terminals)
        {
            await _host.ShowMessageAsync("Claude Code: log in", LoginHelp(account, Flags, program.Path)).ConfigureAwait(true);
            return;
        }

        var arguments = LoginArguments(Flags);
        var environment = new Dictionary<string, string?>();
        foreach (var (name, value) in account.Environment)
        {
            environment[name] = value;
        }

        try
        {
            terminals.Open(new EditorTerminalRequest
            {
                Name = $"Claude Code login: {account.Name}",
                Folder = account.Folder,
                Shell = program.TerminalShellFor(arguments),
                Environment = environment,
                Show = true,
            });
            _host.Notify($"Log in to Claude Code ({account.Name})", LoginHelp(account, Flags, program.Path));
        }
        catch (InvalidOperationException error)
        {
            await _host.ShowMessageAsync("Claude Code: the login did not start", error.Message).ConfigureAwait(true);
        }
    }

    /// <summary>The login's arguments: the CLI's <c>login</c> command when its help lists one, else none (Claude Code itself).</summary>
    public static IReadOnlyList<string> LoginArguments(ClaudeFlags? flags) => flags is not null && flags.HasCommand("login") ? ["login"] : [];

    /// <summary>What the user does to log in, in words.</summary>
    public static string LoginHelp(ClaudeAccount account, ClaudeFlags? flags, string program)
    {
        ArgumentNullException.ThrowIfNull(account);
        string where = account.Folder is null ? "with its own default configuration" : $"with {ClaudeAccounts.ConfigVariable}={account.Folder}";
        return LoginArguments(flags).Count > 0
            ? $"Claude Code's login runs in the terminal tab ({program} login, {where}): follow its steps in the browser it opens."
            : $"Claude Code runs in the terminal tab ({where}): follow its first-run steps, or type /login and press Enter, then follow its steps in the browser it opens. Close the tab when it says you are logged in.";
    }

    /// <summary>Adds an account: asks its name, makes its folder, then opens its login.</summary>
    public async Task AddAccountAsync(CancellationToken cancellationToken)
    {
        string? name = await _host.PromptAsync("Add a Claude Code account",
            "A name for the account (\"work\", \"personal\"). Craft makes its own Claude Code configuration folder in your data folder (never the project), then opens Claude Code's login for it.",
            "work", "", cancellationToken).ConfigureAwait(true);
        if (name is null)
        {
            return;
        }

        if (ClaudeAccounts.Problem(name) is { } problem)
        {
            await _host.ShowMessageAsync("Claude Code: the account was not added", problem).ConfigureAwait(true);
            return;
        }

        ClaudeAccount account;
        try
        {
            string data = DataFolder;
            string? workspace = _host.WorkspaceFolder;
            account = await Task.Run(() => ClaudeAccounts.Add(data, workspace, name), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            await _host.ShowMessageAsync("Claude Code: the account was not added", error.Message).ConfigureAwait(true);
            return;
        }

        await RefreshAccountsAsync(cancellationToken).ConfigureAwait(true);
        _host.Log(EditorLogLevel.Information, $"Claude Code: the account {account.Name} was added ({account.Folder}).");
        await LoginAsync(account).ConfigureAwait(true);
    }

    /// <summary>Picks an account and runs its login again.</summary>
    public async Task LoginAgainAsync(CancellationToken cancellationToken)
    {
        if (await PickAccountAsync("Log in again: which account?", Accounts, cancellationToken).ConfigureAwait(true) is { } account)
        {
            await LoginAsync(account).ConfigureAwait(true);
        }
    }

    /// <summary>Picks one of Craft's own accounts and, confirmed, deletes its folder.</summary>
    public async Task RemoveAccountAsync(CancellationToken cancellationToken)
    {
        var own = Accounts.Where(a => a.Folder is not null).ToArray();
        if (own.Length == 0)
        {
            await _host.ShowMessageAsync("Claude Code", "Craft has no account of its own to remove (\"default\" is the CLI's own configuration).").ConfigureAwait(true);
            return;
        }

        if (await PickAccountAsync("Remove which account?", own, cancellationToken).ConfigureAwait(true) is not { } account)
        {
            return;
        }

        var confirm = new EditorQuestion($"Remove the account {account.Name}?", $"Its folder and its sign-in are deleted: {account.Folder}. Claude Code itself and your other accounts stay.",
            ["Remove", "Cancel"]) { Dismissed = 1 };
        if (await _host.AskAsync(confirm, cancellationToken).ConfigureAwait(true) != 0)
        {
            return;
        }

        try
        {
            await Task.Run(() => ClaudeAccounts.Remove(account), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _host.ShowMessageAsync("Claude Code: the account was not removed", error.Message).ConfigureAwait(true);
            return;
        }

        await RefreshAccountsAsync(cancellationToken).ConfigureAwait(true);
        _host.Log(EditorLogLevel.Information, $"Claude Code: the account {account.Name} was removed.");
    }

    private async Task<ClaudeAccount?> PickAccountAsync(string title, IReadOnlyList<ClaudeAccount> accounts, CancellationToken cancellationToken)
    {
        var question = new EditorQuestion(title, "Craft's Claude Code accounts.", [.. accounts.Select(a => $"{a.Name} · {LoginText(a.IsLoggedIn)}"), "Cancel"]) { Dismissed = accounts.Count };
        int picked = await _host.AskAsync(question, cancellationToken).ConfigureAwait(true);
        return picked >= 0 && picked < accounts.Count ? accounts[picked] : null;
    }

    // The accounts listed again off the UI thread; the chip lists them.
    private async Task RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        string data = DataFolder;
        var accounts = await Task.Run(() => ClaudeAccounts.List(data), cancellationToken).ConfigureAwait(true);
        Volatile.Write(ref _accounts, accounts);
        AgentEngines.RaiseChanged();
    }

    /// <summary>Runs Claude Code as <paramref name="info"/> says with <paramref name="task"/> on its input, its stream read as steps; the run's end.</summary>
    public static async Task<AgentEngineResult> RunProcessAsync(ProcessStartInfo info, string task, Action<AgentEngineEvent> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(report);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{info.FileName} did not start.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            return new AgentEngineResult($"Claude Code did not start ({info.FileName}): {error.Message}", null) { Failed = true };
        }

        using (process)
        {
            using var stop = cancellationToken.Register(() => ClaudeProgram.Kill(process));
            var errors = new Queue<string>();
            var errorReader = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                {
                    lock (errors)
                    {
                        errors.Enqueue(line);
                        if (errors.Count > 40)
                        {
                            errors.Dequeue();
                        }
                    }
                }
            }, CancellationToken.None);
            try
            {
                await process.StandardInput.WriteAsync(task.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // It ended before it read the task: its error output says why.
            }

            var stream = new ClaudeStream();
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                foreach (var step in stream.Read(line))
                {
                    report(step);
                }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await errorReader.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Ended)
            {
                return new AgentEngineResult(stream.End, stream.Session) { Failed = stream.Failed };
            }

            string tail;
            lock (errors)
            {
                tail = string.Join("\n", errors).Trim();
            }

            return new AgentEngineResult(
                $"Claude Code ended with exit code {process.ExitCode} before its result{(tail.Length > 0 ? ": " + ClaudeStream.Cut(tail, 2000) : ".")} "
                + "If it is not signed in for this account, run claude once in a terminal with the account's variables, then try again.", stream.Session)
            { Failed = true };
        }
    }

    // Craft's MCP server for the run: its address and token in a private config file, after starting it or asking.
    private async Task<string?> McpConfigAsync(ClaudeCodeSettings settings, string? workspace, List<string> notes, CancellationToken cancellationToken)
    {
        if (EditorMcpServers.Find(EditorMcpServers.Editor) is not { } server)
        {
            notes.Add("Craft's MCP server is not installed (the AI plug-in brings it): Claude Code runs without Craft's tools.");
            return null;
        }

        var (address, token) = await OnUiAsync(() => (server.Address, server.Token)).ConfigureAwait(false);
        if (address is null)
        {
            var when = settings.McpWhenOff;
            if (when == McpWhenOff.Ask)
            {
                var question = new EditorQuestion("Craft's MCP server is off",
                    "Claude Code can use Craft's tools (its diagnostics, open_file, propose_edit, run_tests) and ask you before its commands through it.",
                    ["Start it and run", "Run without it"]) { Dismissed = 1 };
                when = await OnUiAsync(() => _host.AskAsync(question, cancellationToken)).Unwrap().ConfigureAwait(false) == 0 ? McpWhenOff.Start : McpWhenOff.Without;
            }

            if (when == McpWhenOff.Start && server.StartCommand.Length > 0)
            {
                await OnUiAsync(() => _host.ExecuteCommandAsync(server.StartCommand, cancellationToken)).Unwrap().ConfigureAwait(false);
                (address, token) = await OnUiAsync(() => (server.Address, server.Token)).ConfigureAwait(false);
                notes.Add(address is null ? "Craft's MCP server did not start: Claude Code runs without Craft's tools." : $"Started Craft's MCP server at {address}.");
            }
            else
            {
                notes.Add("Running without Craft's MCP server (mcpWhenOff): no Craft tools, and no approvals: Claude Code refuses what needs permission.");
            }

            if (address is null)
            {
                return null;
            }
        }

        string data = _host.DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Craft");
        try
        {
            string path = McpConfig.Write(data, workspace, address, token);
            notes.Add($"Connected to Craft's MCP server ({address}) through {path}.");
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            notes.Add($"Craft's MCP config could not be written ({error.Message}): Claude Code runs without Craft's tools.");
            return null;
        }
    }

    /// <summary>The model the commands use: the agent's when it is a Claude Code one, else the <c>account</c> setting's, else "default"'s.</summary>
    public string CommandModel
    {
        get
        {
            var agent = _host.Settings.Get("ai", "agentModel").Trim();
            if (Models.Contains(agent, StringComparer.OrdinalIgnoreCase))
            {
                return agent;
            }

            var account = Settings.Account;
            return Accounts.Any(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase)) ? ClaudeAccounts.ModelOf(account) : ClaudeAccounts.DefaultModel;
        }
    }

    /// <summary>Asks the agent panel to run <paramref name="task"/> with Claude Code (empty: shows it, Claude Code answering next).</summary>
    public async Task AskAsync(string task)
    {
        if (Check(CommandModel) is { } problem)
        {
            await _host.ShowMessageAsync("Claude Code", problem).ConfigureAwait(true);
            return;
        }

        if (!AgentEngines.Ask(CommandModel, task))
        {
            await _host.ShowMessageAsync("Claude Code: no agent panel", "Claude Code answers in the AI plug-in's agent panel, which is not active: turn the AI plug-in on (Plug-ins page).").ConfigureAwait(true);
        }
    }

    /// <summary>Sends the prompt <paramref name="template"/> picks, its places filled.</summary>
    public async Task PromptAsync(Func<ClaudeCodeSettings, string> template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var settings = Settings;
        string? craft = await Task.Run(() => CraftFolder(settings)).ConfigureAwait(true);   // the repositories are looked up off the UI thread
        await AskAsync(ClaudeCodeSettings.Fill(template(settings), null, null, craft, settings.IdrakRepositoryFrom(craft), _host.WorkspaceFolder)).ConfigureAwait(true);
    }

    /// <summary>Asks Claude Code about the latest report of <paramref name="kind"/> (a crash: the latest crash or caught failure).</summary>
    public async Task DiagnoseAsync(string kind, CancellationToken cancellationToken)
    {
        if (_host.Diagnostics is not { } diagnostics)
        {
            await _host.ShowMessageAsync("Claude Code", "This host keeps no reports.").ConfigureAwait(true);
            return;
        }

        var latest = await Task.Run(() => Latest(diagnostics, kind), cancellationToken).ConfigureAwait(true);
        if (latest is null)
        {
            await _host.ShowMessageAsync("Claude Code", $"No {(kind == DiagnosticReportKinds.Stall ? "stall" : "crash or failure")} report in {diagnostics.ReportFolder}.").ConfigureAwait(true);
            return;
        }

        await AskAboutAsync(latest).ConfigureAwait(true);
    }

    /// <summary>The latest report of <paramref name="kind"/>: for a crash, the latest crash or caught failure.</summary>
    public static DiagnosticReport? Latest(IEditorDiagnostics diagnostics, string kind)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (kind == DiagnosticReportKinds.Stall)
        {
            var stalls = diagnostics.Reports(DiagnosticReportKinds.Stall, 1);
            return stalls.Count > 0 ? stalls[0] : null;
        }

        foreach (var report in diagnostics.Reports(null, 50))
        {
            if (report.Kind != DiagnosticReportKinds.Stall)
            {
                return report;
            }
        }

        return null;
    }

    /// <summary>Asks Claude Code about <paramref name="report"/> (its prompt by its kind, the report named).</summary>
    public async Task AskAboutAsync(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var settings = Settings;
        string? craft = await Task.Run(() => CraftFolder(settings)).ConfigureAwait(true);
        await AskAsync(PromptFor(settings, report, craft, _host.WorkspaceFolder)).ConfigureAwait(true);
    }

    /// <summary>The prompt about <paramref name="report"/>: the stall's for a stall, else the crash's, its places filled.</summary>
    public static string PromptFor(ClaudeCodeSettings settings, DiagnosticReport report, string? craft, string? workspace)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(report);
        string template = report.Kind == DiagnosticReportKinds.Stall ? settings.PromptStall : settings.PromptCrash;
        string kind = report.Kind switch
        {
            DiagnosticReportKinds.Stall => "a stall of the window",
            DiagnosticReportKinds.Callback => "a failure it caught and went on from",
            _ => "a crash",
        };
        return ClaudeCodeSettings.Fill(template, $"{report.Path} (craft_read_report name {report.Name})", kind, craft, settings.IdrakRepositoryFrom(craft), workspace);
    }

    private static string? CraftFolder(ClaudeCodeSettings settings) => settings.CraftRepositoryFrom(AppContext.BaseDirectory);

    private Task<T> OnUiAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Dispatch("claude code: the window's state", () =>
        {
            try
            {
                done.TrySetResult(work());
            }
#pragma warning disable CA1031 // The run's caller gets the failure.
            catch (Exception error)
#pragma warning restore CA1031
            {
                done.TrySetException(error);
            }
        });
        return done.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _shown?.Dispose();
    }
}
