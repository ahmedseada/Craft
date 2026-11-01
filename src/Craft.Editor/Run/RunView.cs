// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;

namespace Craft.Editor.Run;

/// <summary>How a run ended.</summary>
public enum RunEnd
{
    /// <summary>It exited with 0.</summary>
    Succeeded,

    /// <summary>It exited with another code, or stopped at its time limit.</summary>
    Failed,

    /// <summary>The user stopped it (Stop, Cancel), with its process tree.</summary>
    Stopped,

    /// <summary>It did not start: the user said no, the sandbox refused it, or it cannot run here (said why).</summary>
    NotStarted,
}

/// <summary>A run's outcome: how it ended, its exit code, how long it took, the problems its output reported, and why it did not start.</summary>
/// <param name="End">How it ended.</param>
/// <param name="ExitCode">Its exit code, or null.</param>
/// <param name="Elapsed">How long it ran.</param>
/// <param name="Problems">The problems its output reported (each once).</param>
/// <param name="Reason">Why it did not start, or null.</param>
public sealed record RunOutcome(RunEnd End, int? ExitCode, TimeSpan Elapsed, IReadOnlyList<RunProblem> Problems, string? Reason = null);

/// <summary>A file's problems from the last run, with the file's text they are placed in (for the Problems list).</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Snapshot">Its text (the open document's, else the file's as the run ended).</param>
/// <param name="Diagnostics">Its problems in <paramref name="Snapshot"/>'s positions.</param>
public sealed record RunProblemFile(string Path, ITextSnapshot Snapshot, IReadOnlyList<EditorDiagnostic> Diagnostics);

/// <summary>
/// The Run view (X part 6), the rail's Run: what this workspace can run and build, found by every registered
/// <see cref="IRunProvider"/> off the UI thread and found again when the folder watcher sees a file a provider watches (a
/// project, a launch settings file, a script) or an editor saves one; each with Run, Build, Test and Stop; the
/// configurations of <c>.craft/run.json</c>, edited in the view; a default for F5 and Ctrl+F5 (run) and Ctrl+Shift+B
/// (build), Shift+F5 stopping. Every command is approved by the user (<c>CRAFT_RUN_APPROVAL</c>) and runs through the
/// registered command sandbox <c>CRAFT_RUN_SANDBOX</c> (Idrak's <see cref="CommandSandboxes"/>), streamed to its own
/// terminal tab, shown as an activity whose Cancel (and the view's Stop) ends its process tree (X part 6b: a run whose
/// target reads input, or as <c>CRAFT_RUN_INTERACTIVE</c> and the configuration say, is started through the sandbox's
/// interactive start in a program tab that takes the keyboard, Ctrl+C there ending it); MSBuild's and the
/// compilers' <c>file(line,col): error CODE:</c> lines go to Problems. No debugging. Headless; every member is called on
/// the UI thread.
/// </summary>
public sealed partial class RunView : IEditorViewPanel, IDisposable
{
    /// <summary>Its panel id (and its rail item's).</summary>
    public const string PanelId = "craft.run";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly EditorHost _host;
    private readonly Func<string?> _root;
    private readonly Func<CraftOptions> _options;
    private readonly Dictionary<string, RunningItem> _running = new(StringComparer.Ordinal);
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    private readonly List<string> _providerProblems = [];
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private List<RunTarget> _targets = [];
    private RunFile _file = RunFile.Empty;
    private IReadOnlyList<RunProblemFile> _problemFiles = [];
    private IReadOnlyList<RunProblem> _problems = [];
    private IDisposable? _watch;
    private string? _watched;
    private CancellationTokenSource? _finding;
    private TaskCompletionSource _found = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _refreshPending;
    private bool _finding2;
    private Draft? _draft;
    private string? _note;
    private int _version;
    private int _focus;
    private bool _disposed;

    /// <summary>The view over <paramref name="host"/> for the folder <paramref name="root"/> gives, with <paramref name="options"/>.</summary>
    public RunView(EditorHost host, Func<string?> root, Func<CraftOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host.DocumentSaved += OnDocumentSaved;
        RunProviders.Changed += OnProvidersChanged;
        BuildFixProviders.Changed += OnFixProvidersChanged;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => "Run";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Raised (on the UI thread) when <see cref="ProblemFiles"/> changed.</summary>
    public event EventHandler? ProblemsChanged;

    /// <summary>What the providers found, providers in their registry's order.</summary>
    public IReadOnlyList<RunTarget> Targets => _targets;

    /// <summary>The configurations and the default (<c>.craft/run.json</c>).</summary>
    public RunFile File => _file;

    /// <summary>The problems the last run's output reported.</summary>
    public IReadOnlyList<RunProblem> Problems => _problems;

    /// <summary>The last run's problems by file, placed in each file's text (what the Problems list shows).</summary>
    public IReadOnlyList<RunProblemFile> ProblemFiles => _problemFiles;

    /// <summary>Completes when the find asked last has its targets in.</summary>
    public Task Found => _found.Task;

    /// <summary>The names of what runs now (a configuration's, or a target's title and action).</summary>
    public IReadOnlyCollection<string> Running => _running.Keys;

    /// <summary>The workspace changed (a folder opened, or none): what it runs is found again, and its folder watched.</summary>
    public void WorkspaceChanged()
    {
        Watch();
        _ = RefreshAsync();
    }

    /// <summary>Finds again what the workspace runs (off the UI thread) and reads its <c>.craft/run.json</c>; completes when they are in.</summary>
    public Task RefreshAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var previous = _finding;
        var cancel = _finding = new CancellationTokenSource();
        if (previous is not null)
        {
            _ = previous.CancelAsync().ContinueWith(_ => previous.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        if (_found.Task.IsCompleted)
        {
            _found = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var done = _found;
        if (_root() is not { } root)
        {
            _targets = [];
            _file = RunFile.Empty;
            _providerProblems.Clear();
            _finding2 = false;
            Changed2();
            done.TrySetResult();
            return done.Task;
        }

        _finding2 = true;
        Publish();
        var ignore = _options().WorkspaceIgnore;
        var providers = RunProviders.All;
        _ = Task.Run(() =>
        {
            var problems = new List<string>();
            var targets = new List<RunTarget>();
            RunFile file;
            try
            {
                var files = WorkspaceFiles.Enumerate(root, ignore, useIgnoreFiles: true, null, null, cancel.Token).Select(path => WorkspaceFiles.Relative(root, path)).ToList();
                var workspace = new RunWorkspace(root, files);
                foreach (var provider in providers)
                {
                    try
                    {
                        targets.AddRange(provider.Find(workspace, cancel.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
#pragma warning disable CA1031 // A provider that fails is named in the view; the others' targets still show.
                    catch (Exception error)
#pragma warning restore CA1031
                    {
                        problems.Add($"{provider.Title} ({provider.Name}) could not look: {error.Message}");
                    }
                }

                file = RunFile.Load(root);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _host.Dispatch("run: what the workspace runs", () =>
            {
                if (cancel.IsCancellationRequested || _disposed)
                {
                    return;
                }

                _targets = targets;
                _file = file;
                _providerProblems.Clear();
                _providerProblems.AddRange(problems);
                _providerProblems.AddRange(file.Problems);
                _finding2 = false;
                Changed2();
                done.TrySetResult();
            });
        }, CancellationToken.None);
        return done.Task;
    }

    /// <summary>The target a configuration or a default names ("provider:id", or a configuration's target), or null.</summary>
    public RunTarget? TargetOf(string provider, string id) =>
        _targets.FirstOrDefault(t => t.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && t.Id == id);

    /// <summary>A target's key as a default names it: "provider:id".</summary>
    public static string KeyOf(RunTarget target) => (target ?? throw new ArgumentNullException(nameof(target))).Provider + ":" + target.Id;

    /// <summary>
    /// The configuration F5 and Ctrl+Shift+B use: the default (a configuration, or a target as a configuration of its own),
    /// else the first configuration, else the first target that runs (or, for a build, builds); null when there is none.
    /// </summary>
    public RunConfiguration? DefaultConfiguration(RunActions action)
    {
        if (_file.Default is { } chosen)
        {
            if (_file.Configurations.FirstOrDefault(c => c.Name == chosen) is { } configuration)
            {
                return configuration;
            }

            int colon = chosen.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && TargetOf(chosen[..colon], chosen[(colon + 1)..]) is { } target)
            {
                return AdHoc(target, action);
            }
        }

        if (_file.Configurations.Count > 0)
        {
            return _file.Configurations[0];
        }

        var first = _targets.FirstOrDefault(t => (t.Actions & action) != 0);
        return first is null ? null : AdHoc(first, action);
    }

    /// <summary>F5 and Ctrl+F5: runs the default (<see cref="DefaultConfiguration"/>); Ctrl+Shift+B builds it (<paramref name="action"/>).</summary>
    public async Task<RunOutcome> RunDefaultAsync(RunActions action, CancellationToken cancellationToken = default)
    {
        if (_found.Task is { IsCompleted: false } finding)
        {
            await finding.WaitAsync(cancellationToken).ConfigureAwait(true);
        }

        if (DefaultConfiguration(action) is not { } configuration)
        {
            string why = _root() is null ? "Open a folder to run what it holds." : $"This workspace has nothing to {RunFile.ActionName(action)}: no project, solution or script was found.";
            Say(why, NotificationSeverity.Warning);
            return NotStarted(why);
        }

        if (action == RunActions.Build && configuration.Action != RunActions.Build)
        {
            // Ctrl+Shift+B builds the default's target (a configuration that runs builds its target).
            configuration = configuration with { Action = RunActions.Build, BuildFirst = false };
        }

        return await RunAsync(configuration, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs <paramref name="configuration"/>: its target's build first when it asks, then its command, approved by the user
    /// and run through the registered sandbox, its output in its own terminal tab, an activity whose Cancel stops it.
    /// </summary>
    public async Task<RunOutcome> RunAsync(RunConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (_root() is not { } root)
        {
            return NotStarted("Open a folder to run what it holds.");
        }

        if (RunProviders.Find(configuration.Provider) is not { } provider)
        {
            string why = RunProviders.Find(configuration.Provider) is null ? $"'{configuration.Name}': run provider '{configuration.Provider}' is not registered (registered: {string.Join(", ", RunProviders.Names)}); the plug-in that brings it may be removed or disabled." : "";
            Say(why, NotificationSeverity.Error);
            return NotStarted(why);
        }

        if (TargetOf(configuration.Provider, configuration.Target) is not { } target)
        {
            string why = $"'{configuration.Name}': {configuration.Target} is not in this workspace any more (Refresh, or edit the configuration).";
            Say(why, NotificationSeverity.Warning);
            return NotStarted(why);
        }

        string name = configuration.Name;
        if (_running.ContainsKey(name))
        {
            string why = $"'{name}' runs already: stop it first (Shift+F5).";
            Say(why, NotificationSeverity.Information);
            return NotStarted(why);
        }

        var item = new RunningItem(name);
        _running[name] = item;
        Changed2();
        try
        {
            if (configuration.BuildFirst && configuration.Action == RunActions.Run && (target.Actions & RunActions.Build) != 0)
            {
                var built = await StepAsync(item, provider, target, RunActions.Build, configuration, root, cancellationToken).ConfigureAwait(true);
                if (built.End != RunEnd.Succeeded)
                {
                    NoteOutcome(configuration, RunActions.Build, built);
                    return built;
                }
            }

            var outcome = await StepAsync(item, provider, target, configuration.Action, configuration, root, cancellationToken).ConfigureAwait(true);
            NoteOutcome(configuration, configuration.Action, outcome);
            return outcome;
        }
        finally
        {
            _running.Remove(name);
            item.Dispose();
            Changed2();
        }
    }

    /// <summary>
    /// Runs <paramref name="command"/> (another view's: the Test explorer's <c>dotnet test</c>) as a run named
    /// <paramref name="name"/>: approved by the user, through the registered sandbox, its output in its own terminal tab
    /// ("Test: NAME"), an activity whose Cancel (and Stop) ends its tree, its compilers' problems in Problems; each output
    /// line also handed to <paramref name="tap"/> (on the sandbox's thread). It never takes the keyboard.
    /// </summary>
    public async Task<RunOutcome> RunCommandAsync(string name, RunCommand command, RunActions action, Action<string>? tap = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(command);
        if (_root() is not { } root)
        {
            return NotStarted("Open a folder to run what it holds.");
        }

        if (_running.ContainsKey(name))
        {
            string why = $"'{name}' runs already: stop it first (Shift+F5).";
            Say(why, NotificationSeverity.Information);
            return NotStarted(why);
        }

        var item = new RunningItem(name);
        _running[name] = item;
        Changed2();
        try
        {
            var target = new RunTarget(CommandOnly.ProviderName, name, name, "", action);
            var configuration = new RunConfiguration(name, CommandOnly.ProviderName, name) { Action = action, Interactive = RunInteractive.Off };
            return await StepAsync(item, CommandOnly.Instance, target, action, configuration, root, cancellationToken, command, tap).ConfigureAwait(true);
        }
        finally
        {
            _running.Remove(name);
            item.Dispose();
            Changed2();
        }
    }

    /// <summary>Stops the run named <paramref name="name"/> (every run when null), with its process tree; false when none ran.</summary>
    public bool Stop(string? name = null)
    {
        bool any = false;
        foreach (var (key, item) in _running.ToArray())
        {
            if (name is null || key == name)
            {
                item.Cancel();
                any = true;
            }
        }

        return any;
    }

    /// <summary>Saves <paramref name="configuration"/> (in place of <paramref name="replacing"/>'s, else added) to <c>.craft/run.json</c>.</summary>
    public async Task SaveConfigurationAsync(RunConfiguration configuration, string? replacing = null, bool makeDefault = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (_root() is not { } root)
        {
            return;
        }

        var list = _file.Configurations.ToList();
        int at = list.FindIndex(c => c.Name == (replacing ?? configuration.Name));
        if (at >= 0)
        {
            list[at] = configuration;
        }
        else
        {
            list.Add(configuration);
        }

        string? chosen = makeDefault ? configuration.Name : _file.Default == replacing && replacing is not null ? configuration.Name : _file.Default;
        await WriteAsync(root, new RunFile(list, chosen)).ConfigureAwait(true);
    }

    /// <summary>Removes the configuration <paramref name="name"/> from <c>.craft/run.json</c>.</summary>
    public async Task DeleteConfigurationAsync(string name)
    {
        if (_root() is not { } root)
        {
            return;
        }

        var list = _file.Configurations.Where(c => c.Name != name).ToList();
        await WriteAsync(root, new RunFile(list, _file.Default == name ? null : _file.Default)).ConfigureAwait(true);
    }

    /// <summary>Makes <paramref name="key"/> (a configuration's name, or a target's "provider:id") the default for F5 and Ctrl+Shift+B.</summary>
    public async Task SetDefaultAsync(string key)
    {
        if (_root() is { } root)
        {
            await WriteAsync(root, _file with { Default = key }).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        switch (actionId)
        {
            case "run.default":
                await RunDefaultAsync(RunActions.Run, cancellationToken).ConfigureAwait(true);
                break;
            case "run.buildDefault":
                await RunDefaultAsync(RunActions.Build, cancellationToken).ConfigureAwait(true);
                break;
            case "run.stopAll":
                Stop();
                break;
            case "run.stop" when value is not null:
                Stop(value);
                break;
            case "run.refresh":
                await RefreshAsync().ConfigureAwait(true);
                break;
            case "run.configuration" when value is not null && _file.Configurations.FirstOrDefault(c => c.Name == value) is { } configuration:
                await RunAsync(configuration, cancellationToken).ConfigureAwait(true);
                break;
            case "run.buildConfiguration" when value is not null && _file.Configurations.FirstOrDefault(c => c.Name == value) is { } configuration:
                await RunAsync(configuration with { Action = RunActions.Build, BuildFirst = false }, cancellationToken).ConfigureAwait(true);
                break;
            case "run.target" or "run.build" or "run.test" or "run.publish" when value is not null && FindKey(value) is { } target:
                var action = actionId switch
                {
                    "run.build" => RunActions.Build,
                    "run.test" => RunActions.Test,
                    "run.publish" => RunActions.Publish,
                    _ => RunActions.Run,
                };
                await RunAsync(AdHoc(target, action), cancellationToken).ConfigureAwait(true);
                break;
            case "run.makeDefault" when value is not null:
                await SetDefaultAsync(value).ConfigureAwait(true);
                break;
            case "run.add":
                var from = value is not null ? FindKey(value) : _targets.FirstOrDefault(t => (t.Actions & RunActions.Run) != 0) ?? (_targets.Count > 0 ? _targets[0] : null);
                if (from is null)
                {
                    _note = "There is nothing in this workspace to configure yet.";
                    Publish();
                    break;
                }

                // The target's own arguments and variables still apply; the configuration adds to them.
                _draft = new Draft(null, UniqueName(from.Title), KeyOf(from), RunFile.ActionName(from.DefaultAction), "", "", "", false, "auto");
                _focus++;
                Publish();
                break;
            case "run.edit" when value is not null && _file.Configurations.FirstOrDefault(c => c.Name == value) is { } edited:
                _draft = new Draft(edited.Name, edited.Name, edited.Provider + ":" + edited.Target, RunFile.ActionName(edited.Action), edited.Arguments,
                    RunCommandLine.FormatEnvironment(edited.Environment), edited.Folder ?? "", edited.BuildFirst, RunFile.InteractiveName(edited.Interactive));
                _focus++;
                Publish();
                break;
            case "run.draft.name" when _draft is not null:
                _draft = _draft with { Name = value ?? "" };
                break;
            case "run.draft.target" when _draft is not null && value is not null:
                _draft = _draft with { Target = value, Action = FindKey(value) is { } picked && (picked.Actions & RunFile.ActionOf(_draft.Action)) == 0 ? RunFile.ActionName(picked.DefaultAction) : _draft.Action };
                Publish();
                break;
            case "run.draft.action" when _draft is not null && value is not null:
                _draft = _draft with { Action = value };
                Publish();
                break;
            case "run.draft.arguments" when _draft is not null:
                _draft = _draft with { Arguments = value ?? "" };
                break;
            case "run.draft.environment" when _draft is not null:
                _draft = _draft with { Environment = value ?? "" };
                break;
            case "run.draft.folder" when _draft is not null:
                _draft = _draft with { Folder = value ?? "" };
                break;
            case "run.draft.interactive" when _draft is not null && RunFile.InteractiveOf(value) is { } interactive:
                _draft = _draft with { Interactive = RunFile.InteractiveName(interactive) };
                Publish();
                break;
            case "run.draft.buildFirst" when _draft is not null:
                _draft = _draft with { BuildFirst = value == "on" };
                Publish();
                break;
            case "run.draft.save" when _draft is not null:
                await SaveDraftAsync().ConfigureAwait(true);
                break;
            case "run.draft.delete" when _draft is { Original: { } original }:
                _draft = null;
                await DeleteConfigurationAsync(original).ConfigureAwait(true);
                break;
            case "run.draft.cancel":
                _draft = null;
                Publish();
                break;
            case FixWithAiAction:
                await FixWithAiAsync(null, cancellationToken).ConfigureAwait(true);
                break;
            case StopFixAction:
                if (_fixing is { } fixing)
                {
                    await fixing.CancelAsync().ConfigureAwait(true);
                }

                break;
            case RunAgainAction:
                await RunAgainAsync(cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.DocumentSaved -= OnDocumentSaved;
        RunProviders.Changed -= OnProvidersChanged;
        BuildFixProviders.Changed -= OnFixProvidersChanged;
        _fixing?.Cancel();
        _watch?.Dispose();
        _finding?.Cancel();
        _finding?.Dispose();
        Stop();
        _found.TrySetResult();
    }

    // ---- one step of a run: approve, sandbox, terminal, activity, problems ----

    private async Task<RunOutcome> StepAsync(RunningItem item, IRunProvider provider, RunTarget target, RunActions action, RunConfiguration configuration, string root, CancellationToken cancellationToken,
        RunCommand? given = null, Action<string>? tap = null)
    {
        var options = _options();
        var command = given ?? provider.Command(target, action, configuration, root);
        if (command.Problem is { } cannot || command.Command.Count == 0)
        {
            string why = command.Problem ?? "The provider gave no command.";
            Say($"'{configuration.Name}': {why}", NotificationSeverity.Warning);
            return NotStarted(why);
        }

        // No silent run: the user approves the command line (once a session, or each run: CRAFT_RUN_APPROVAL).
        var environment = new Dictionary<string, string>(target.Environment, StringComparer.Ordinal);
        foreach (var (key, value) in command.Environment)
        {
            environment[key] = value;
        }

        foreach (var (key, value) in configuration.Environment)
        {
            environment[key] = value;
        }

        string line = command.Line;
        string approval = line + "\u0001" + command.Folder + "\u0001" + RunCommandLine.FormatEnvironment(environment);
        if (options.RunApproval != "once" || !_approved.Contains(approval))
        {
            var question = new EditorQuestion($"{Verb(action)} {configuration.Name}?", line, ["Run", "Cancel"])
            {
                Details =
                [
                    "Folder: " + Relative(root, command.Folder),
                    environment.Count == 0 ? "Variables: inherited only" : "Variables: " + RunCommandLine.FormatEnvironment(environment),
                    $"Runs in: the {options.RunSandbox} sandbox (CRAFT_RUN_SANDBOX)",
                    options.RunTimeLimitMinutes == 0 ? "Time limit: none (Stop ends it)" : string.Create(Culture, $"Time limit: {options.RunTimeLimitMinutes} min"),
                    action == RunActions.Run && TakesKeyboard(configuration, target, options) ? "Input: its terminal tab takes the keyboard (CRAFT_RUN_INTERACTIVE)" : "Input: none (its output only)",
                    options.RunApproval == "once" ? "Asked once a session for this command (CRAFT_RUN_APPROVAL)" : "Asked for every run (CRAFT_RUN_APPROVAL)",
                ],
                Dismissed = 1,
            };
            int answer = await _host.AskAsync(question, cancellationToken).ConfigureAwait(true);
            if (answer != 0)
            {
                string why = answer < 0 ? "This editor cannot ask before a run, so nothing ran." : "You said no; nothing ran.";
                _note = $"{configuration.Name}: {why}";
                Publish();
                return NotStarted(why);
            }

            _approved.Add(approval);
        }

        ICommandSandbox sandbox;
        try
        {
            sandbox = CommandSandboxes.Get(options.RunSandbox);
        }
        catch (NotRegisteredException e)
        {
            Say($"'{configuration.Name}' did not run: {e.Message} (CRAFT_RUN_SANDBOX)", NotificationSeverity.Error);
            return NotStarted(e.Message);
        }

        var output = new RunOutput(_host, $"{(action == RunActions.Run ? "Run" : Verb(action))}: {configuration.Name}");
        var problems = new List<RunProblem>();
        var seen = new HashSet<RunProblem>();
        var limits = new CommandLimits
        {
            Time = options.RunTimeLimitMinutes == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(options.RunTimeLimitMinutes),
            Environment = environment,
            Root = root,
            MaxOutputCharacters = 4_000,
            Output = text =>
            {
                bool error = false;
                if (provider.TryParseProblem(text, command.Folder, out var problem))
                {
                    lock (problems)
                    {
                        if (seen.Add(problem))
                        {
                            problems.Add(problem);
                        }
                    }

                    error = problem.Severity == DiagnosticSeverity.Error;
                }

                output.Line(text, error);
                tap?.Invoke(text);
                item.Activity?.Report(null, text.Length <= 80 ? text.Trim() : string.Concat(text.AsSpan(0, 79), "…").Trim());
            },
        };
        if (sandbox.Check(limits) is { } refused)
        {
            Say($"'{configuration.Name}' did not run: the {sandbox.Name} sandbox refuses it: {refused}", NotificationSeverity.Error);
            return NotStarted(refused);
        }

        // X part 6b: a run that reads input takes the keyboard in its terminal tab (CRAFT_RUN_INTERACTIVE, the configuration's own).
        if (action == RunActions.Run && TakesKeyboard(configuration, target, options) && _host.Terminals is not null)
        {
            if (sandbox.CheckStart(limits) is { } noStart)
            {
                _host.Log(EditorLogLevel.Information, $"Run: {configuration.Name} shows its output only: {noStart}");
            }
            else
            {
                return await InteractiveAsync(item, provider, configuration, command, line, root, sandbox, limits with { Output = null }, options).ConfigureAwait(true);
            }
        }

        output.Start($"$ {line}", Relative(root, command.Folder));
        var activity = _host.Activities.Start($"{Verb(action)}: {configuration.Name}", action == RunActions.Build ? ActivityKinds.Build : ActivityKinds.Command,
            cancellable: true, item.Token);
        item.Activity = activity;
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => sandbox.RunAsync(command.Command, command.Folder, limits, activity.CancellationToken).AsTask(), CancellationToken.None).ConfigureAwait(true);
            var found = Snapshot(problems);
            await ShowProblemsAsync(found).ConfigureAwait(true);
            int errors = found.Count(p => p.Severity == DiagnosticSeverity.Error);
            if (result.Refused)
            {
                output.End($"[did not start: {result.Output}]", failed: true);
                activity.Fail(result.Output);
                return new RunOutcome(RunEnd.NotStarted, null, clock.Elapsed, found, result.Output);
            }

            string elapsed = string.Create(Culture, $"{clock.Elapsed.TotalSeconds:0.0} s");
            if (result.TimedOut || result.OutOfMemory)
            {
                string why = result.TimedOut ? $"stopped at its time limit, {options.RunTimeLimitMinutes} min (CRAFT_RUN_TIME_LIMIT_MINUTES)" : "stopped at its memory limit";
                output.End($"[{why}]", failed: true);
                activity.Fail(why);
                return new RunOutcome(RunEnd.Failed, null, clock.Elapsed, found, why);
            }

            bool ok = result.ExitCode == 0;
            string said = string.Create(Culture, $"exit code {result.ExitCode} · {elapsed}{(errors > 0 ? $" · {errors} {(errors == 1 ? "error" : "errors")} in Problems" : "")}");
            output.End($"[{said}]", failed: !ok);
            if (ok)
            {
                activity.Complete(said);
            }
            else
            {
                activity.Fail(said);
            }

            return new RunOutcome(ok ? RunEnd.Succeeded : RunEnd.Failed, result.ExitCode, clock.Elapsed, found);
        }
        catch (OperationCanceledException)
        {
            var found = Snapshot(problems);
            await ShowProblemsAsync(found).ConfigureAwait(true);
            output.End("[stopped, with its process tree]", failed: true);
            activity.Complete("stopped");
            return new RunOutcome(RunEnd.Stopped, null, clock.Elapsed, found);
        }
        catch (NotSupportedException e)
        {
            output.End($"[the sandbox refused it: {e.Message}]", failed: true);
            activity.Fail(e.Message, e);
            return new RunOutcome(RunEnd.NotStarted, null, clock.Elapsed, [], e.Message);
        }
        finally
        {
            activity.Dispose();
            item.Activity = null;
        }
    }

    /// <summary>
    /// Whether a run takes the keyboard (X part 6b): the configuration's own "interactive" when it says on or off, else
    /// <c>CRAFT_RUN_INTERACTIVE</c>'s on or off, else (auto) when its target reads input (a console program, a script).
    /// </summary>
    public static bool TakesKeyboard(RunConfiguration configuration, RunTarget target, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        return configuration.Interactive switch
        {
            RunInteractive.On => true,
            RunInteractive.Off => false,
            _ => RunFile.InteractiveOf(options.RunInteractive) switch
            {
                RunInteractive.On => true,
                RunInteractive.Off => false,
                _ => target.ReadsInput,
            },
        };
    }

    // A run that takes the keyboard: started through the sandbox's interactive start, its streams in a program tab (what is
    // typed goes to its input, Ctrl+C and Stop end its tree), its output's lines read on the way for Problems and the
    // activity, its end said in the tab.
    private async Task<RunOutcome> InteractiveAsync(RunningItem item, IRunProvider provider, RunConfiguration configuration, RunCommand command, string line, string root,
        ICommandSandbox sandbox, CommandLimits limits, CraftOptions options)
    {
        var problems = new List<RunProblem>();
        var seen = new HashSet<RunProblem>();
        var activity = _host.Activities.Start($"Run: {configuration.Name}", ActivityKinds.Command, cancellable: true, item.Token);
        item.Activity = activity;
        var clock = Stopwatch.StartNew();
        CommandStart start;
        try
        {
            start = await Task.Run(() => sandbox.StartAsync(command.Command, command.Folder, limits, activity.CancellationToken).AsTask(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            activity.Complete("stopped");
            activity.Dispose();
            item.Activity = null;
            return new RunOutcome(RunEnd.Stopped, null, clock.Elapsed, []);
        }
        catch (NotSupportedException e)
        {
            Say($"'{configuration.Name}' did not run: the {sandbox.Name} sandbox refuses it: {e.Message}", NotificationSeverity.Error);
            activity.Fail(e.Message, e);
            activity.Dispose();
            item.Activity = null;
            return NotStarted(e.Message);
        }

        if (!start.Started)
        {
            Say($"'{configuration.Name}' did not start: {start.Refusal}", NotificationSeverity.Error);
            activity.Fail(start.Refusal);
            activity.Dispose();
            item.Activity = null;
            return new RunOutcome(RunEnd.NotStarted, null, clock.Elapsed, [], start.Refusal);
        }

        var session = start.Session;
        void OnLine(string text)
        {
            if (provider.TryParseProblem(text, command.Folder, out var problem))
            {
                lock (problems)
                {
                    if (seen.Add(problem))
                    {
                        problems.Add(problem);
                    }
                }
            }

            activity.Report(null, text.Length <= 80 ? text.Trim() : string.Concat(text.AsSpan(0, 79), "…").Trim());
        }

        string name = $"Run: {configuration.Name}";
        var output = new LineTap(session.Output, OnLine);
        var error = new LineTap(session.Error, OnLine);
        var registration = activity.CancellationToken.Register(session.Stop);
        IEditorTerminalProgram? tab = null;
        RunOutput? lines = null;
        try
        {
            tab = _host.Terminals?.OpenProgram(new EditorProgramRequest(name, session.Input, output)
            {
                Error = error,
                Stop = item.Cancel,
                Folder = command.Folder,
                Header = $"\e[2m{Relative(root, command.Folder)}\e[0m\n\e[1m$ {line}\e[0m\n",
            });
            Task outputEnded;
            if (tab is not null)
            {
                tab.Show();
                outputEnded = tab.OutputEnded;
            }
            else
            {
                // A host without program tabs: the input is closed and the output shown as lines, as a run that does not take the keyboard.
                await session.Input.DisposeAsync().ConfigureAwait(true);
                lines = new RunOutput(_host, name);
                lines.Start($"$ {line}", Relative(root, command.Folder));
                outputEnded = Task.WhenAll(output.DrainAsync(text => lines.Line(text, false)), error.DrainAsync(text => lines.Line(text, true)));
            }

            var result = await session.Completion.ConfigureAwait(true);
            try
            {
                // A grandchild that outlived the program may hold its output open: its end is not waited for long.
                await outputEnded.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
            }

            var found = Snapshot(problems);
            await ShowProblemsAsync(found).ConfigureAwait(true);
            int errors = found.Count(p => p.Severity == DiagnosticSeverity.Error);
            string elapsed = string.Create(Culture, $"{clock.Elapsed.TotalSeconds:0.0} s");
            void End(string text, bool failed, int code)
            {
                string coloured = (failed ? "\e[33m" : "\e[32m") + text + "\e[0m\n";
                if (tab is not null)
                {
                    tab.End("\n" + coloured, code);
                }
                else
                {
                    lines!.End(text, failed);
                }
            }

            if (result.Stopped || item.Token.IsCancellationRequested)
            {
                End("[stopped, with its process tree]", failed: true, -1);
                activity.Complete("stopped");
                return new RunOutcome(RunEnd.Stopped, null, clock.Elapsed, found);
            }

            if (result.TimedOut || result.OutOfMemory)
            {
                string why = result.TimedOut ? $"stopped at its time limit, {options.RunTimeLimitMinutes} min (CRAFT_RUN_TIME_LIMIT_MINUTES)" : "stopped at its memory limit";
                End($"[{why}]", failed: true, -1);
                activity.Fail(why);
                return new RunOutcome(RunEnd.Failed, null, clock.Elapsed, found, why);
            }

            bool ok = result.ExitCode == 0;
            string said = string.Create(Culture, $"exit code {result.ExitCode} · {elapsed}{(errors > 0 ? $" · {errors} {(errors == 1 ? "error" : "errors")} in Problems" : "")}");
            End($"[{said}]", failed: !ok, result.ExitCode ?? -1);
            if (ok)
            {
                activity.Complete(said);
            }
            else
            {
                activity.Fail(said);
            }

            return new RunOutcome(ok ? RunEnd.Succeeded : RunEnd.Failed, result.ExitCode, clock.Elapsed, found);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(true);
            activity.Dispose();
            item.Activity = null;
            await session.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static List<RunProblem> Snapshot(List<RunProblem> problems)
    {
        lock (problems)
        {
            return [.. problems];
        }
    }

    // The problems go to Problems placed in each file's text: the open document's, else the file read off the UI thread.
    private async Task ShowProblemsAsync(IReadOnlyList<RunProblem> problems)
    {
        var open = new Dictionary<string, ITextSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } path)
            {
                open[Path.GetFullPath(path)] = document.Document.Buffer.Current;
            }
        }

        var files = await Task.Run(() =>
        {
            var made = new List<RunProblemFile>();
            foreach (var group in problems.GroupBy(p => p.File, StringComparer.OrdinalIgnoreCase))
            {
                ITextSnapshot? snapshot = open.GetValueOrDefault(group.Key);
                if (snapshot is null)
                {
                    try
                    {
                        snapshot = System.IO.File.Exists(group.Key) ? new PieceTableBuffer(System.IO.File.ReadAllText(group.Key)).Current : new PieceTableBuffer("").Current;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        snapshot = new PieceTableBuffer("").Current;
                    }
                }

                var diagnostics = new List<EditorDiagnostic>();
                foreach (var problem in group)
                {
                    int line = Math.Clamp(problem.Line - 1, 0, snapshot.LineCount - 1);
                    var text = snapshot.GetLine(line);
                    int start = text.Start + Math.Clamp(problem.Column - 1, 0, text.Length);
                    diagnostics.Add(new EditorDiagnostic(start, 0, problem.Severity, problem.Code, problem.Message));
                }

                made.Add(new RunProblemFile(group.Key, snapshot, diagnostics));
            }

            return made;
        }).ConfigureAwait(true);
        _host.Dispatch("run: problems", () =>
        {
            _problems = problems;
            _problemFiles = files;
            ProblemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    // ---- the workspace changing ----

    private void Watch()
    {
        string? root = _root();
        if (root == _watched)
        {
            return;
        }

        _watch?.Dispose();
        _watch = null;
        _watched = root;
        if (root is null)
        {
            return;
        }

        try
        {
            if (FolderWatchers.Find(_options().WorkspaceWatcher) is { } watcher)
            {
                _watch = watcher.Watch(root, OnFolderChanged);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            _host.Log(EditorLogLevel.Warning, $"The Run view does not follow the disk ({e.Message}); Refresh finds again.", e);
        }
    }

    // The watcher's threads: a change to a file a provider watches (or run.json, or a folder that held a target) finds again, once per burst.
    private void OnFolderChanged(FolderChange change)
    {
        if (change.Kind == FolderChangeKind.Rescan || Relevant(change.Path) || (change.OldPath is { } old && Relevant(old)))
        {
            ScheduleRefresh();
        }
    }

    private bool Relevant(string path)
    {
        var name = Path.GetFileName(path.AsSpan());
        if (name.Equals(RunFile.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var provider in RunProviders.All)
        {
            if (provider.Watches(name))
            {
                return true;
            }
        }

        // A folder that held a target, removed or renamed.
        if (_watched is { } root)
        {
            var relative = WorkspaceFiles.Relative(root, path);
            foreach (var target in Volatile.Read(ref _targets))
            {
                if (target.File.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        int wait = Math.Max(0, _options().WorkspaceWatchMilliseconds);
        _ = Task.Run(async () =>
        {
            await Task.Delay(wait).ConfigureAwait(false);
            _host.Dispatch("run: the workspace changed", () =>
            {
                Volatile.Write(ref _refreshPending, 0);
                _ = RefreshAsync();
            });
        });
    }

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is { } path && Relevant(path))
        {
            ScheduleRefresh();
        }
    }

    private void OnProvidersChanged(object? sender, EventArgs e) => _host.Dispatch("run: the providers changed", () => _ = RefreshAsync());

    // ---- configurations ----

    private async Task SaveDraftAsync()
    {
        var draft = _draft!;
        string name = draft.Name.Trim();
        int colon = draft.Target.IndexOf(':', StringComparison.Ordinal);
        if (name.Length == 0 || colon <= 0)
        {
            _note = "A configuration needs a name and a target.";
            Publish();
            return;
        }

        if (_file.Configurations.Any(c => c.Name == name && c.Name != draft.Original))
        {
            _note = $"A configuration named '{name}' exists already: pick another name.";
            Publish();
            return;
        }

        var configuration = new RunConfiguration(name, draft.Target[..colon], draft.Target[(colon + 1)..])
        {
            Action = RunFile.ActionOf(draft.Action) is var action && action != RunActions.None ? action : RunActions.Run,
            Arguments = draft.Arguments.Trim(),
            Environment = RunCommandLine.ParseEnvironment(draft.Environment),
            Folder = draft.Folder.Trim() is { Length: > 0 } folder ? folder : null,
            BuildFirst = draft.BuildFirst,
            Interactive = RunFile.InteractiveOf(draft.Interactive) ?? RunInteractive.Auto,
        };
        _draft = null;
        await SaveConfigurationAsync(configuration, draft.Original, makeDefault: _file.Configurations.Count == 0 && _file.Default is null).ConfigureAwait(true);
        _note = $"Saved '{name}' to .craft/{RunFile.FileName}.";
        Publish();
    }

    private async Task WriteAsync(string root, RunFile file)
    {
        _file = file;
        Changed2();
        try
        {
            await file.SaveAsync(root).ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($".craft/{RunFile.FileName} could not be written: {e.Message}", NotificationSeverity.Error);
        }
    }

    private string UniqueName(string title)
    {
        string name = title;
        for (int i = 2; _file.Configurations.Any(c => c.Name == name); i++)
        {
            name = string.Create(Culture, $"{title} ({i})");
        }

        return name;
    }

    private RunTarget? FindKey(string key)
    {
        int colon = key.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 ? null : TargetOf(key[..colon], key[(colon + 1)..]);
    }

    private static RunConfiguration AdHoc(RunTarget target, RunActions action) =>
        new(action == RunActions.Run ? target.Title : $"{target.Title} ({RunFile.ActionName(action)})", target.Provider, target.Id)
        {
            Action = (target.Actions & action) != 0 ? action : target.DefaultAction,
        };

    // ---- blocks ----

    private void Say(string text, NotificationSeverity severity)
    {
        _note = text;
        _host.Notify("Run", text, severity);
        Publish();
    }

    private static RunOutcome NotStarted(string why) => new(RunEnd.NotStarted, null, TimeSpan.Zero, [], why);

    private static string Verb(RunActions action) => action switch
    {
        RunActions.Build => "Build",
        RunActions.Test => "Test",
        RunActions.Publish => "Publish",
        _ => "Run",
    };

    private static string Relative(string root, string folder)
    {
        var relative = WorkspaceFiles.Relative(root, folder);
        return relative is "." or "" ? "the workspace" : relative;
    }

    private void Changed2()
    {
        _version++;
        Publish();
    }

    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        _blocks = Build();
        _items = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<PanelBlock> Build()
    {
        var options = _options();
        var blocks = new List<PanelBlock>();
        string? keys(string command) => _host.KeysOf(command);
        string with(string text, string command) => keys(command) is { } k ? $"{text} ({k})" : text;
        var chosen = _root() is null ? null : DefaultConfiguration(RunActions.Run);
        string status = _root() is null ? "Open a folder to run and build what it holds."
            : _finding2 && _targets.Count == 0 ? "Finding what this workspace runs…"
            : chosen is null ? "Nothing to run here: no project, solution or script was found."
            : $"F5 runs {chosen.Name}" + (_file.Default is null ? " (the first found: pick a default with ★)" : "");
        blocks.Add(new PanelText(status, PanelTextStyle.Secondary) { Tone = _finding2 ? PanelTone.Accent : PanelTone.Normal });
        blocks.Add(new PanelButtons(
        [
            new PanelButton("Run", "run.default", PanelButtonKind.Primary) { Icon = RailIcons.Run, Enabled = chosen is not null, Tooltip = with("Run the default", RunCommands.Run) },
            new PanelButton("Build", "run.buildDefault", PanelButtonKind.Secondary) { Icon = RailIcons.Build, Enabled = chosen is not null, Tooltip = with("Build the default", RunCommands.Build) },
            new PanelButton("Stop", "run.stopAll", PanelButtonKind.Danger) { Icon = RailIcons.Stop, Enabled = _running.Count > 0, Tooltip = with("Stop every run", RunCommands.Stop) },
            new PanelButton("Add configuration", "run.add", PanelButtonKind.Flat) { Icon = RailIcons.Add, Enabled = _targets.Count > 0, Tooltip = "A configuration: arguments, variables, folder, build first; saved in .craft/run.json" },
            new PanelButton("Refresh", "run.refresh", PanelButtonKind.Flat) { Icon = RailIcons.Refresh, Tooltip = "Find again what the workspace runs" },
        ]));
        AddFailure(blocks);
        foreach (var problem in _providerProblems)
        {
            blocks.Add(new PanelText(problem, PanelTextStyle.Secondary) { Tone = PanelTone.Warning });
        }

        if (_note is not null)
        {
            blocks.Add(new PanelText(_note, PanelTextStyle.Caption));
        }

        if (_draft is { } draft)
        {
            AddEditor(blocks, draft);
        }

        blocks.Add(new PanelText($"Every run asks first and goes through the '{options.RunSandbox}' sandbox; its output in a terminal tab of its own.", PanelTextStyle.Caption)
        {
            Variable = "CRAFT_RUN_SANDBOX",
        });
        blocks.Add(new PanelResults("run.targets", Groups(chosen))
        {
            Version = _version,
            Empty = _root() is null ? "No folder is open." : _finding2 ? null : "No project, solution or script was found in this workspace.",
        });
        return blocks;
    }

    // The configuration's fields as top-level blocks (each field keeps the keyboard while the view redraws; no box inside a box).
    private void AddEditor(List<PanelBlock> blocks, Draft draft)
    {
        blocks.Add(new PanelText(draft.Original is null ? "New configuration" : $"Edit '{draft.Original}'", PanelTextStyle.Heading));
        blocks.Add(new PanelText("Name", PanelTextStyle.Caption));
        blocks.Add(new PanelQuery("run.draft.name", "Name", draft.Name) { Focus = _focus });
        var targets = _targets.Select(t => new PanelOption(KeyOf(t), $"{t.Title} · {t.File}") { Detail = t.Detail }).ToList();
        if (targets.Count > 0)
        {
            blocks.Add(new PanelPicker("run.draft.target", "Target", targets, draft.Target));
        }

        var picked = FindKey(draft.Target);
        var actions = new List<PanelOption>();
        foreach (var action in (ReadOnlySpan<RunActions>)[RunActions.Run, RunActions.Build, RunActions.Test, RunActions.Publish])
        {
            if (picked is null || (picked.Actions & action) != 0)
            {
                actions.Add(new PanelOption(RunFile.ActionName(action), Verb(action)));
            }
        }

        blocks.Add(new PanelPicker("run.draft.action", "Action", actions, draft.Action));
        blocks.Add(new PanelText("Arguments", PanelTextStyle.Caption));
        blocks.Add(new PanelQuery("run.draft.arguments", "Arguments (--port 5000)", draft.Arguments) { Code = true });
        blocks.Add(new PanelText("Variables", PanelTextStyle.Caption));
        blocks.Add(new PanelQuery("run.draft.environment", "NAME=value|OTHER=value", draft.Environment) { Code = true });
        blocks.Add(new PanelText("Folder, relative to the workspace", PanelTextStyle.Caption));
        blocks.Add(new PanelQuery("run.draft.folder", "The project's folder", draft.Folder) { Code = true });
        if (picked is null || (picked.Actions & RunActions.Build) != 0)
        {
            blocks.Add(new PanelSwitch("run.draft.buildFirst", "Build before it runs", draft.BuildFirst) { Detail = "A failed build stops the run there." });
        }

        if (RunFile.ActionOf(draft.Action) == RunActions.Run)
        {
            string auto = $"As CRAFT_RUN_INTERACTIVE says ({_options().RunInteractive}){(picked is { ReadsInput: true } ? ": this target reads input" : "")}";
            blocks.Add(new PanelPicker("run.draft.interactive", "Takes the keyboard",
            [
                new PanelOption("auto", "Auto") { Detail = auto },
                new PanelOption("on", "On") { Detail = "Its terminal tab takes the keyboard: what you type goes to the program." },
                new PanelOption("off", "Off") { Detail = "Its output only; its input is closed." },
            ], draft.Interactive));
        }

        var buttons = new List<PanelButton> { new("Save", "run.draft.save", PanelButtonKind.Primary), new("Cancel", "run.draft.cancel", PanelButtonKind.Flat) };
        if (draft.Original is not null)
        {
            buttons.Add(new PanelButton("Delete", "run.draft.delete", PanelButtonKind.Danger));
        }

        blocks.Add(new PanelButtons(buttons));
    }

    private List<PanelResultGroup> Groups(RunConfiguration? chosen)
    {
        var groups = new List<PanelResultGroup>();
        if (_file.Configurations.Count > 0)
        {
            var rows = new List<PanelResultRow>();
            foreach (var configuration in _file.Configurations)
            {
                bool running = _running.ContainsKey(configuration.Name);
                var target = TargetOf(configuration.Provider, configuration.Target);
                var buttons = new List<PanelButton>();
                buttons.Add(running
                    ? new PanelButton("Stop", "run.stop", PanelButtonKind.Flat) { Icon = RailIcons.Stop, Value = configuration.Name }
                    : new PanelButton(Verb(configuration.Action), "run.configuration", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = configuration.Name, Enabled = target is not null });
                if (target is not null && (target.Actions & RunActions.Build) != 0 && configuration.Action != RunActions.Build)
                {
                    buttons.Add(new PanelButton("Build", "run.buildConfiguration", PanelButtonKind.Flat) { Icon = RailIcons.Build, Value = configuration.Name });
                }

                buttons.Add(new PanelButton("Edit", "run.edit", PanelButtonKind.Flat) { Icon = RailIcons.Edit, Value = configuration.Name });
                rows.Add(new PanelResultRow("configuration:" + configuration.Name, configuration.Name)
                {
                    Detail = target is null ? $"{configuration.Target} (not found)" : $"{Verb(configuration.Action)} · {target.Title}",
                    Icon = RailIcons.Run,
                    Tone = running ? PanelTone.Accent : target is null ? PanelTone.Warning : PanelTone.Normal,
                    Badge = running ? "running" : chosen is not null && _file.Default == configuration.Name ? "default" : null,
                    ActionId = "run.makeDefault",
                    Value = configuration.Name,
                    Buttons = buttons,
                });
            }

            groups.Add(new PanelResultGroup("configurations", "Configurations", rows) { Icon = RailIcons.Run, Badge = rows.Count.ToString(Culture), Tooltip = ".craft/run.json" });
        }

        foreach (var provider in RunProviders.All)
        {
            var rows = new List<PanelResultRow>();
            foreach (var target in _targets.Where(t => t.Provider.Equals(provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                string key = KeyOf(target);
                string runName = AdHoc(target, RunActions.Run).Name;
                bool running = _running.Keys.Any(k => k == runName || k.StartsWith(target.Title + " (", StringComparison.Ordinal));
                var buttons = new List<PanelButton>();
                if (running)
                {
                    buttons.Add(new PanelButton("Stop", "run.stop", PanelButtonKind.Flat) { Icon = RailIcons.Stop, Value = _running.Keys.First(k => k == runName || k.StartsWith(target.Title + " (", StringComparison.Ordinal)) });
                }
                else
                {
                    if ((target.Actions & RunActions.Run) != 0)
                    {
                        buttons.Add(new PanelButton("Run", "run.target", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = key });
                    }

                    if ((target.Actions & RunActions.Build) != 0)
                    {
                        buttons.Add(new PanelButton("Build", "run.build", PanelButtonKind.Flat) { Icon = RailIcons.Build, Value = key });
                    }

                    if ((target.Actions & RunActions.Test) != 0)
                    {
                        buttons.Add(new PanelButton("Test", "run.test", PanelButtonKind.Flat) { Icon = RailIcons.Test, Value = key });
                    }
                }

                buttons.Add(new PanelButton("Add a configuration", "run.add", PanelButtonKind.Flat) { Icon = RailIcons.Add, Value = key });
                rows.Add(new PanelResultRow("target:" + key, target.Title)
                {
                    Detail = target.Detail is { } detail ? $"{detail} · {target.File}" : target.File,
                    Icon = (target.Actions & RunActions.Run) != 0 ? RailIcons.Run : (target.Actions & RunActions.Test) != 0 ? RailIcons.Test : RailIcons.Build,
                    Tone = running ? PanelTone.Accent : PanelTone.Normal,
                    Badge = running ? "running" : _file.Default == key ? "default" : null,
                    ActionId = "run.makeDefault",
                    Value = key,
                    Buttons = buttons,
                });
            }

            if (rows.Count > 0)
            {
                var icon = FileIcons.For(_targets.First(t => t.Provider.Equals(provider.Name, StringComparison.OrdinalIgnoreCase)).File);
                groups.Add(new PanelResultGroup("provider:" + provider.Name, provider.Title, rows)
                {
                    Badge = rows.Count.ToString(Culture),
                    Glyph = icon.Badge,
                    GlyphArgb = icon.Argb,
                    FileIcon = icon,
                    Tooltip = provider.Summary,
                });
            }
        }

        return groups;
    }

    // A configuration being edited in the view (Original: the name it had, null for a new one).
    private sealed record Draft(string? Original, string Name, string Target, string Action, string Arguments, string Environment, string Folder, bool BuildFirst, string Interactive);

    // The provider of a command another view hands over (RunCommandAsync): its problems read as MSBuild's, its command given.
    private sealed class CommandOnly : IRunProvider
    {
        public const string ProviderName = "command";

        public static readonly CommandOnly Instance = new();

        public string Name => ProviderName;

        public string Title => "Command";

        public string Summary => "a command another view hands to the Run view";

        public bool Watches(ReadOnlySpan<char> fileName) => false;

        public IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default) => [];

        public RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root) =>
            RunCommand.Unavailable("A handed command has no command of its own.", root);
    }

    // A run under way: its stop and its activity.
    private sealed class RunningItem(string name) : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();

        public string Name { get; } = name;

        public CancellationToken Token => _stop.Token;

        public IActivity? Activity { get; set; }

        public void Cancel()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => _stop.Dispose();
    }

    // A program's output or error stream as the program tab reads it, its lines (UTF-8, up to a line break) handed to a
    // watcher on the way (Problems, the activity's last line); a line longer than LineCap is handed in parts.
    private sealed class LineTap(Stream inner, Action<string> line) : Stream
    {
        private const int LineCap = 8 * 1024;
        private readonly byte[] _line = new byte[LineCap];
        private int _length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
            Watch(buffer[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Watch(buffer.Span[..read]);
            return read;
        }

        /// <summary>Reads the stream to its end, each line to <paramref name="each"/> (a host without program tabs).</summary>
        public async Task DrainAsync(Action<string> each)
        {
            using var reader = new StreamReader(this, System.Text.Encoding.UTF8);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } text)
            {
                each(text);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Watch(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                Emit();                                           // the end: the last line without its break
                return;
            }

            while (!bytes.IsEmpty)
            {
                int at = bytes.IndexOf((byte)'\n');
                var part = at < 0 ? bytes : bytes[..at];
                int take = Math.Min(part.Length, LineCap - _length);
                part[..take].CopyTo(_line.AsSpan(_length));
                _length += take;
                if (take < part.Length)
                {
                    Emit();                                       // the cap reached inside a line: this part now, the rest next
                    bytes = bytes[take..];
                }
                else if (at >= 0)
                {
                    Emit();
                    bytes = bytes[(at + 1)..];
                }
                else
                {
                    bytes = default;
                }
            }
        }

        private void Emit()
        {
            if (_length == 0)
            {
                return;
            }

            int length = _line[_length - 1] == (byte)'\r' ? _length - 1 : _length;
            _length = 0;
            try
            {
                line(System.Text.Encoding.UTF8.GetString(_line, 0, length));
            }
#pragma warning disable CA1031 // A watcher's failure never stops the program's output.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    // A run's terminal tab: the lines gathered from the sandbox's reader thread, written once per UI turn.
    private sealed class RunOutput(EditorHost host, string name)
    {
        private readonly Lock _gate = new();
        private readonly List<string> _pending = [];
        private IEditorTerminalOutput? _tab;
        private bool _scheduled;

        public void Start(string command, string folder)
        {
            host.Dispatch("run: open its terminal tab", () =>
            {
                _tab = host.Terminals?.OpenOutput(name);
                _tab?.Append($"\e[2m{folder}\e[0m\n\e[1m{command}\e[0m\n");
                _tab?.Show();
            });
        }

        public void Line(string text, bool error) => Add(error ? "\e[31m" + text + "\e[0m" : text);

        public void End(string text, bool failed) => Add((failed ? "\e[33m" : "\e[32m") + text + "\e[0m");

        private void Add(string text)
        {
            bool schedule;
            lock (_gate)
            {
                _pending.Add(text);
                schedule = !_scheduled;
                _scheduled = true;
            }

            if (schedule)
            {
                host.Dispatch("run: show its output", Flush);
            }
        }

        private void Flush()
        {
            string text;
            lock (_gate)
            {
                _scheduled = false;
                text = string.Join('\n', _pending) + "\n";
                _pending.Clear();
            }

            if (_tab is not { IsOpen: true })
            {
                _tab = host.Terminals?.OpenOutput(name);
            }

            _tab?.Append(text);
        }
    }
}

/// <summary>The Run view's commands (X part 6), bound to the same keys as Visual Studio and VS Code in both keymaps.</summary>
public static class RunCommands
{
    /// <summary>"Run: Start" (F5): runs the default.</summary>
    public const string Run = "craft.run.start";

    /// <summary>"Run: Start without debugging" (Ctrl+F5): runs the default (there is no debugging yet, so as F5).</summary>
    public const string RunWithoutDebugging = "craft.run.startWithoutDebugging";

    /// <summary>"Run: Build" (Ctrl+Shift+B): builds the default.</summary>
    public const string Build = "craft.run.build";

    /// <summary>"Run: Stop" (Shift+F5): stops every run.</summary>
    public const string Stop = "craft.run.stop";
}
