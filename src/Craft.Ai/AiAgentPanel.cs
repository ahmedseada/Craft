// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Ai;

/// <summary>
/// The agent run (plan 1 phase A2, desktop; docs/design/Agent.dc.html): Idrak's <see cref="CodingAgent"/> with its
/// <see cref="CodingTools"/> over a working copy of the open folder (<see cref="AgentWorkspace"/>), so every edit is held as
/// a diff: the changed files are listed (+added −removed) with Review diff (each file's change proposed in the editor, the
/// diff view, one undo step) and Discard all. Every command runs through the command sandbox the setting
/// <see cref="AiSettings.AgentSandbox"/> names in Idrak's <c>CommandSandboxes</c> (plug-in P5's approve-each sandbox asks
/// the user for each); when none is registered under it the agent runs no command and the panel says so. The header shows
/// the turn of the most, the tokens, the rate, the elapsed time and the sandbox; Stop cancels; while it runs, what the user
/// sends is a steering note the agent reads before its next step (<see cref="CodingAgent.Steer"/>).
/// </summary>
public sealed class AiAgentPanel : ConversationPanel, IModelSessionHost, IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string PanelId = "ai.agent";

    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly List<ConversationEntry> _log = [];
    private readonly Dictionary<string, AgentChange> _changes = new(StringComparer.Ordinal);
    private readonly List<ContextChip> _files = [];   // files dropped on the panel, sent with the next task as @path
    private AiAnswer? _answer;                       // the run's answer for the AI events (raised at its first review), or null
    private bool _judged;
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _running;
    private CodingAgent? _agent;
    private AgentWorkspace? _copy;
    private CodingAgentRun? _conversation;          // the last finished run: a follow-up continues it (same copy); Clear forgets it
    private ITimer? _ticker;
    private int _assistant = -1;
    private readonly StringBuilder _text = new();
    private int _turn;
    private int _tokens;
    private double _modelSeconds;
    private string _sandboxName = "";
    private bool _waiting;
    private string? _loadingModel;
    private IActivity? _activity;
    private bool _disposed;
    private string _header = "";
    private TaskCompletionSource<int>? _approval;     // the sandbox's question shown inline on the command's step, while it waits

    /// <summary>An agent panel in <paramref name="host"/>.</summary>
    public AiAgentPanel(IEditorHost host, Func<AiSettings> settings, AiModels models)
        : base(host, PanelId, "Agent")
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        // The owner, 2026-11-01 ("2a"): this agent session's own model settings (kept until Clear or the panel closes).
        _session = new SessionModel(host, settings, models, ModelRoleKeys.Agent, "this agent session");
        _session.Changed += (_, _) => ModelSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly SessionModel _session;

    /// <inheritdoc />
    public string ModelSessionTitle => _session.Title;

    /// <inheritdoc />
    public ModelSessionSettings ModelSession => _session.Settings;

    /// <inheritdoc />
    public ModelSessionShared ModelShared => _session.Shared;

    /// <summary>The session's own model settings and lease (the tests' view).</summary>
    public SessionModel Session => _session;

    /// <inheritdoc />
    public event EventHandler? ModelSessionChanged;

    /// <inheritdoc />
    public Task<ModelMemoryEstimate?> EstimateModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default) =>
        _session.EstimateAsync(proposed, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SaveModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default) => _session.SaveAsync(proposed, cancellationToken);

    /// <inheritdoc />
    public Task<string> ApplyModelSessionToEnvironmentAsync(ModelSessionSettings proposed, ModelSettingsScope scope, CancellationToken cancellationToken = default) =>
        _session.ApplyToEnvironmentAsync(proposed, scope, cancellationToken);

    /// <summary>The run going on, or the last one (the tests await it).</summary>
    public Task Run { get; private set; } = Task.CompletedTask;

    /// <summary>The files the agent changed and the user has not settled yet.</summary>
    public IReadOnlyList<AgentChange> Changes => [.. _changes.Values.OrderBy(c => c.Relative, StringComparer.Ordinal)];

    /// <summary>The working copy of the run, or null.</summary>
    public AgentWorkspace? WorkingCopy => _copy;

    /// <inheritdoc />
    public override string? EmptyText =>
        "Describe a task (\"add a --verbose option and test it\"): the agent works on a copy of the folder, reads and edits files and runs commands you approve, then shows every change as a diff you accept or discard. Your files change only when you accept.";

    /// <inheritdoc />
    public override string Placeholder => IsBusy
        ? "Add a note for the next turn (it reads it before its next step)"
        : _conversation is not null
            ? "Follow up: the agent keeps this run's conversation and its copy of the folder (Clear starts afresh)"
            : "Describe a task for the agent (it works on a copy of the folder; you review every change)";

    /// <summary>The agent's model role (note 25): the composer's chip shows and writes the agent's model and device.</summary>
    public override string? ModelRoleKey => ModelRoleKeys.Agent;

    /// <summary>The run drawn as a timeline (X part 10): thinking, each tool call with its details, approvals, the files changed.</summary>
    public override bool ShowsTimeline => true;

    /// <inheritdoc />
    public override IReadOnlyList<ConversationCommand> SlashCommands { get; } =
    [
        new("clear", "Start afresh: the next task is a new run on a new copy of the folder"),
    ];

    /// <summary>
    /// The sandbox's question about the command waiting (<see cref="EditorQuestionKinds.Command"/>) shown on that command's
    /// step with its answers as buttons (Approve, Allow for this run, Deny), not in a dialog (X part 10). Any other question,
    /// or none waiting, is left to the host's dialog.
    /// </summary>
    public override Task<int>? AskInline(EditorQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Kind != EditorQuestionKinds.Command || !_waiting || _command < 0 || _command >= _log.Count || _approval is not null)
        {
            return null;
        }

        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approval = answer;
        int index = _command;
        var actions = new List<PanelAction>(question.Choices.Count);
        for (int i = 0; i < question.Choices.Count; i++)
        {
            int choice = i;
            string label = question.Choices[i];
            actions.Add(new PanelAction(label, _ => { Answer(index, choice, label); return Task.CompletedTask; }, Primary: i == 0));
        }

        var waiting = _log[index];
        SetLog(index, waiting with { Actions = actions, Details = string.Join('\n', question.Details) is { Length: > 0 } facts ? facts : waiting.Details });
        cancellationToken.Register(() => Post(() =>
        {
            if (ReferenceEquals(_approval, answer))
            {
                _approval = null;
                answer.TrySetCanceled();
            }
        }));
        return answer.Task;
    }

    // An inline answer: the buttons go, the step says what was chosen, and the sandbox has its answer.
    private void Answer(int index, int choice, string label)
    {
        if (_approval is not { } answer)
        {
            return;
        }

        _approval = null;
        if (index >= 0 && index < _log.Count && _log[index].Role == ConversationRole.Approval)
        {
            SetLog(index, _log[index] with { Actions = [], Detail = "you chose: " + label });
        }

        answer.TrySetResult(choice);
    }

    // A log entry replaced, and shown (in place while no change is listed, else with the list).
    private void SetLog(int index, ConversationEntry entry)
    {
        _log[index] = entry;
        if (_changes.Count == 0)
        {
            Update(index, entry);
        }
        else
        {
            ShowChanges();
        }
    }

    // The model's text stops growing (a tool runs, a turn or the run ends): its caret goes.
    private void EndAssistant()
    {
        if (_assistant >= 0 && _assistant < _log.Count && _log[_assistant].IsStreaming)
        {
            SetLog(_assistant, _log[_assistant] with { IsStreaming = false });
        }
    }

    /// <summary>Whether the next message continues the last run (its conversation and working copy) rather than starting a new one.</summary>
    public bool Continues => _conversation is not null && _copy is { } copy && Host.WorkspaceFolder is { } root
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(copy.Workspace)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.Ordinal)
        && Directory.Exists(copy.Copy);

    /// <inheritdoc />
    public override IReadOnlyList<PanelAction> HeaderActions => IsBusy
        ? [new PanelAction("Stop", _ => { Stop(); return Task.CompletedTask; })]
        : _log.Count > 0 ? [new PanelAction("Clear", _ => { Clear(); return Task.CompletedTask; })] : [];

    /// <summary>
    /// Files and folders dropped on the panel: those of the open folder become <c>@path</c> chips (their path in the
    /// folder), sent with the next task or note so the agent reads them in its copy; one outside the folder is not taken
    /// (the agent works on a copy of the folder only), said in the panel. What exists is checked off the UI thread.
    /// </summary>
    public override async Task<int> AcceptDropAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return 0;
        }

        string? root = Host.WorkspaceFolder;
        var found = await Task.Run(() => paths.Select(p => Path.GetFullPath(p)).Select(p => (Path: p, Exists: File.Exists(p) || Directory.Exists(p))).ToList(), cancellationToken).ConfigureAwait(false);
        var taken = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            int count = 0;
            var refused = new List<string>();
            string? folder = root is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            foreach (var (path, exists) in found)
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                if (!exists)
                {
                    refused.Add($"{name} is not there");
                    continue;
                }

                if (folder is null || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    refused.Add(folder is null ? $"{name}: open a folder first" : $"{name} is outside the open folder (the agent works on a copy of the folder)");
                    continue;
                }

                string id = "path:" + path;
                if (!_files.Any(c => c.Id == id))
                {
                    _files.Add(new ContextChip(id, "@" + Path.GetRelativePath(root!, path).Replace('\\', '/')));
                }

                count++;
            }

            if (refused.Count > 0)
            {
                Log(new ConversationEntry(ConversationRole.Note, "Not added: " + string.Join("; ", refused) + "."));
            }

            SetChips([.. _files]);
            taken.TrySetResult(count);
        });
        return await taken.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void RemoveChip(string id)
    {
        _files.RemoveAll(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        SetChips([.. _files]);
    }

    /// <inheritdoc />
    public override Task SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        string message = text.Trim();
        if (message.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (_files.Count > 0)
        {
            // The dropped files go with the message as @path (their paths in the folder, read by the agent's tools in its copy).
            message += "\n\nThe files this is about (paths in the folder): " + string.Join(", ", _files.Select(c => c.Text)) + ".";
            _files.Clear();
            SetChips([]);
        }

        if (message.Equals("/clear", StringComparison.OrdinalIgnoreCase) && !IsBusy)
        {
            Clear();
            return Task.CompletedTask;
        }

        if (IsBusy && _agent is { } agent)
        {
            agent.Steer(message);
            Log(new ConversationEntry(ConversationRole.User, message) { Detail = "a note for the next turn" });
            return Task.CompletedTask;
        }

        Run = StartAsync(message, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Stops the run (the changes so far stay, to review or discard).</summary>
    public void Stop() => _running?.Cancel();

    /// <summary>
    /// Proposes each changed file's diff in the editor (opening a closed file; a new file opens as a new document), one
    /// proposal per file; the accepted ones leave the list. True when every one was accepted.
    /// </summary>
    public async Task<bool> ReviewAsync(CancellationToken cancellationToken = default)
    {
        bool all = true;
        foreach (var change in Changes)
        {
            all &= await ReviewAsync(change, cancellationToken).ConfigureAwait(true);
        }

        return all;
    }

    /// <summary>Proposes <paramref name="change"/>'s diff in the editor; true when the user accepted it.</summary>
    public async Task<bool> ReviewAsync(AgentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_copy is not { } copy)
        {
            return false;
        }

        var document = await Host.OpenDocumentAsync(change.Path, cancellationToken).ConfigureAwait(true);
        if (document is null)
        {
            Log(new ConversationEntry(ConversationRole.Error, $"{change.Relative}: the editor could not open it to show the diff."));
            return false;
        }

        // The copy's file is read, and the edits made, off the UI thread.
        var snapshot = document.Document.Buffer.Current;
        string current = snapshot.GetText(0, snapshot.Length);
        var edits = await Task.Run(() => AgentWorkspace.Edits(current, copy.NewText(change.Path)), cancellationToken).ConfigureAwait(true);
        if (edits.Count == 0)
        {
            await Task.Run(() => copy.Settle(change.Path, current), CancellationToken.None).ConfigureAwait(true);
            _changes.Remove(change.Relative);
            ShowChanges();
            return true;
        }

        RaiseAnswered();
        bool dirtyBefore = document.Document.IsDirty;
        var proposal = new ProposedEdits(edits, change.IsNew ? $"Agent: new file {change.Relative}" : $"Agent: {change.Relative}")
        {
            Placement = change.IsNew ? $"a new file: accepting it creates {change.Relative}" : "the agent's copy of the file against the editor's text, line by line",
            Origin = "Agent · " + (Subtitle ?? "the model"),
        };
        bool accepted = await Host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
        var now = document.Document.Buffer.Current;
        if (accepted)
        {
            Judge(AiVerdicts.Accepted);
            string settled = now.GetText(0, now.Length);
            var rest = await Task.Run(() =>
            {
                copy.Settle(change.Path, settled);
                return copy.ChangeOf(change.Relative);
            }, CancellationToken.None).ConfigureAwait(true);
            if (rest is not null)
            {
                _changes[change.Relative] = rest;      // some hunks only: the rest stays to review
            }
            else
            {
                _changes.Remove(change.Relative);
            }

            // A new file the user accepted whole is the file the user agreed to create: it is written through the host, so
            // the tab takes its name and language and the folder tree shows it at once (phase U2). An existing file's
            // accepted change stays in the editor, unsaved, as any edit.
            bool created = false;
            if (change.IsNew && rest is null && document.Document.FilePath is null)
            {
                try
                {
                    await Host.SaveAsync(document, change.Path, cancellationToken).ConfigureAwait(true);
                    created = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Log(new ConversationEntry(ConversationRole.Error, $"{change.Relative}: accepted, but the file could not be created ({error.Message}); save it from the editor."));
                }
            }

            // B6: an existing file's accepted change is written to the workspace too (agentSaveOnAccept), unless the editor held
            // unsaved edits of the user's before the diff: those are never saved for them; the change then waits in the editor.
            bool saved = false;
            string? unsaved = null;
            if (!change.IsNew && _settings().AgentSaveOnAccept && document.Document.FilePath is not null)
            {
                if (dirtyBefore)
                {
                    unsaved = "; the file had unsaved edits of yours, so it is not saved: save it to write the change";
                }
                else
                {
                    try
                    {
                        await Host.SaveAsync(document, null, cancellationToken).ConfigureAwait(true);
                        saved = true;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        unsaved = $"; it could not be saved ({error.Message}): save it from the editor";
                    }
                }
            }

            Log(new ConversationEntry(ConversationRole.Note, $"{change.Relative}: accepted into the editor (one undo step)"
                + (created ? "; the file is created" : change.IsNew ? "; save it to create the file" : saved ? "; saved to the workspace" : unsaved ?? "") + "."));
        }

        ShowChanges();
        return accepted;
    }

    /// <summary>Drops every change the agent made (its working copy goes); the user's files were never touched.</summary>
    public void DiscardAll()
    {
        if (IsBusy)
        {
            Stop();
        }

        if (_changes.Count > 0)
        {
            RaiseAnswered();
            Judge(AiVerdicts.Rejected);
        }

        _changes.Clear();
        DiscardLater(_copy);
        _copy = null;
        _conversation = null;                          // its copy is gone: the next message starts a new run
        Log(new ConversationEntry(ConversationRole.Note, "Discarded every change of the run."));
        ShowChanges();
    }

    // The run's answer for the AI events (the task and the files it changed), raised once, before its first review.
    private void RaiseAnswered()
    {
        if (_answer is not null || !AiEvents.Listening)
        {
            return;
        }

        var files = string.Join("\n", _changes.Values.Select(c => c.IsNew ? $"{c.Relative} (new)" : c.Relative));
        _answer = new AiAnswer(AiEvents.NewId(), AiAnswerKinds.Agent, Title, files)
        {
            Plugin = AiSettings.PluginName,
            Model = Subtitle,
            WorkspaceFolder = Host.WorkspaceFolder,
        };
        _judged = false;
        AiEvents.Answered(_answer);
    }

    // What the user did with the run (the first verdict counts: a file accepted, or every change discarded).
    private void Judge(string verdict)
    {
        if (_answer is { } answer && !_judged)
        {
            _judged = true;
            AiEvents.Judged(new AiVerdict(answer.Id, verdict));
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
        Stop();
        _session.End();
        _ticker?.Dispose();
        DiscardLater(_copy);
    }

    // The working copies being deleted now, on the thread pool.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> Discarding = new();

    /// <summary>
    /// Done when every working copy being deleted now (on the thread pool, after a Clear, a Discard all, a new run or a
    /// panel's close) is gone: what removes or reuses the copies' folder waits for it first. Two recursive deletes of one
    /// copy collided (a DirectoryNotFoundException on a file of the copy): the AI tests that failed on Windows only, where
    /// a delete is slower, with "the agent's working copy goes missing".
    /// </summary>
    public static Task DiscardsDone() => Task.WhenAll(Discarding.Keys);

    // The working copy goes on the thread pool: a recursive delete of a folder's copy never runs on the UI thread.
    private static void DiscardLater(AgentWorkspace? copy)
    {
        if (copy is not null)
        {
            var task = Task.Run(copy.Discard);
            Discarding.TryAdd(task, 0);
            _ = task.ContinueWith(static done => Discarding.TryRemove(done, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task StartAsync(string task, CancellationToken cancellationToken)
    {
        var settings = _settings();
        if (!settings.AgentEnabled)
        {
            // Note 25: turned off, the agent loads nothing and says so, with a way to its settings.
            Log(new ConversationEntry(ConversationRole.Error, AiModels.Off(ModelRoleKeys.Agent)) { Actions = [AiChatPanel.SettingsAction(Host, SettingSections.Agent, "Open the agent's settings")] });
            return;
        }

        if (Host.WorkspaceFolder is not { } workspace)
        {
            Log(new ConversationEntry(ConversationRole.Error, "Open a folder first: the agent works on a copy of the open folder."));
            return;
        }

        // A follow-up continues the last run: the same working copy, its changes and log, the conversation so far (the
        // owner's Windows run: each message started a new run on a new copy, remembering nothing). Clear starts afresh.
        var earlier = Continues ? _conversation : null;
        if (earlier is null)
        {
            DiscardLater(_copy);
            _copy = null;
            _changes.Clear();
            _answer = null;
            _log.Clear();
            _conversation = null;
            Title = task;
        }

        _assistant = -1;
        _text.Clear();
        _turn = 0;
        _tokens = 0;
        _modelSeconds = 0;
        _waiting = false;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = source;
        // Phase U: the run shows in the status bar (turn, tokens, tok/s) with Cancel, which stops it as Stop does.
        var activity = Host.Activities.Start("Agent: " + (task.Length <= 48 ? task : string.Concat(task.AsSpan(0, 47), "…")), ActivityKinds.Agent, cancellable: true, source.Token);
        _activity = activity;
        _elapsed.Restart();
        IsBusy = true;
        _ticker = TimeProvider.System.CreateTimer(_ => Post(UpdateStatus), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Log(new ConversationEntry(ConversationRole.User, task));
        // The open documents' text as the user sees it, unsaved changes included (read here, on the UI thread).
        var unsaved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in earlier is null ? Host.Documents : Array.Empty<IEditorDocument>())
        {
            if (document.FilePath is { } path && document.Document.IsDirty)
            {
                unsaved[Path.GetFullPath(path)] = document.Document.Buffer.Current.GetText(0, document.Document.Buffer.Current.Length);
            }
        }

        string end;
        var copy = earlier is null ? null : _copy;
        try
        {
            (end, var run) = await Task.Run(() => RunAgentAsync(task, workspace, unsaved, settings, earlier, copy, activity), CancellationToken.None).ConfigureAwait(true);
            _conversation = run;
            activity.Complete(end);
        }
        catch (OperationCanceledException)
        {
            end = "Stopped.";
            activity.Dispose();
        }
#pragma warning disable CA1031 // The failure is shown in the panel; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Host.Log(EditorLogLevel.Error, "The agent run failed.", error);
            // An out-of-memory failure says what to do (a smaller model at the chip, other GPU programs closed), in words.
            string said = AiErrors.Describe(error, settings);
            Log(new ConversationEntry(ConversationRole.Error, said));
            end = "Failed.";
            activity.Fail("the agent run failed: " + said, error, () => Run = StartAsync(task, CancellationToken.None));
        }

        _activity = null;

        _ticker?.Dispose();
        _ticker = null;
        _elapsed.Stop();
        _running = null;
        source.Dispose();
        _agent = null;
        EndAssistant();
        Log(new ConversationEntry(ConversationRole.Note, end + (_changes.Count > 0 ? " Review the changed files below, or discard them." : "")));
        IsBusy = false;
        UpdateStatus();
    }

    // On the thread pool: the model, the working copy (the run's own when it continues), the sandbox, the tools and the
    // agent's loop (a new conversation, or the earlier run's continued); the end's text and the run.
    private async Task<(string End, CodingAgentRun Run)> RunAgentAsync(string task, string workspace, Dictionary<string, string> unsaved, AiSettings settings,
        CodingAgentRun? earlier, AgentWorkspace? existing, IActivity activity)
    {
        var token = activity.CancellationToken;
        activity.Report(null, "waiting for the model (its download and load show on their own)");
        // The owner's Windows run: the model loads when the agent is first used, never at start-up. The composer says
        // "loading…" until it is ready, and the task goes to the model once it is.
        if (!_models.IsAgentLoaded)
        {
            Post(() =>
            {
                _loadingModel = settings.AgentRole.Model;
                UpdateStatus();
            });
        }

        ModelLease lease;
        try
        {
            lease = await _session.LeaseAsync(token).ConfigureAwait(false);   // the agent's own settings (note 25; empty ones the chat's), the session's over them
        }
        finally
        {
            Post(() =>
            {
                _loadingModel = null;
                UpdateStatus();
            });
        }

        var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot run as an agent.");
        // Idrak's built-ins (the "process" sandbox, the coding languages of the families' plug-in) are registered by now:
        // the lease's load registered the plug-in's Idrak libraries and the idrakPlugins packages.
        IdrakLibraries.EnsureIncluded();
        if (existing is null)
        {
            activity.Report(null, "copying the folder");
        }

        var copy = existing ?? AgentWorkspace.Create(workspace, settings.AgentFolder, settings.AgentCopyIgnore, unsaved, token,
            files => activity.Report(null, string.Create(CultureInfo.InvariantCulture, $"copying the folder: {files:N0} files")));
        string header = HeaderOf(lease.Model, settings.ModelProvider, ModelRoleKeys.Agent);
        Post(() =>
        {
            _copy = copy;
            _header = header;
            Subtitle = lease.Model;
            Log(new ConversationEntry(ConversationRole.Note, existing is null
                ? string.Create(CultureInfo.InvariantCulture, $"Working on a copy of the folder ({copy.Files:N0} files, {copy.Copy}); your files change only when you accept a diff.")
                : string.Create(CultureInfo.InvariantCulture, $"Continuing the run: the same copy of the folder and the conversation so far ({earlier!.Messages.Count} messages); Clear starts afresh.")));
        });

        _sandboxName = settings.AgentSandbox;
        ICommandSandbox inner;
        if (CommandSandboxes.Find(settings.AgentSandbox) is { } registered)
        {
            inner = registered;
        }
        else
        {
            string refusal = $"No command sandbox '{settings.AgentSandbox}' is registered (registered: {(CommandSandboxes.Names.Count == 0 ? "none" : string.Join(", ", CommandSandboxes.Names))}): "
                             + "the agent runs no command. Install the approve-each sandbox plug-in, or set CRAFT_PLUGIN_AI_AGENTSANDBOX to a registered one.";
            inner = new RefusingSandbox(settings.AgentSandbox, refusal);
            Post(() => Log(new ConversationEntry(ConversationRole.Error, refusal)));
        }

        var sandbox = new ObservedSandbox(inner, (command, limits) => Post(() => CommandStarted(command, limits)), result => Post(() => CommandEnded(result)));
        var options = new AgentOptions
        {
            MaxRounds = settings.AgentMaxTurns,
            TimeLimit = TimeSpan.FromMinutes(settings.AgentMinutes),
            Sampling = _session.Sampling(lease.ContextLength, settings.AgentStops),                // the session's own sampling over the agent's
            Tools = new CodingToolOptions { Sandbox = inner.Name },
            // Note 27: calls the model writes as text (fenced JSON) are read, a reply of calls that cannot be read is
            // corrected agentNudges times in a row, and the correction is worded in the model's own template: the lease
            // gives it, since the model is wrapped here (CountingChatModel) and Idrak cannot find it on the wrapper.
            CallsInText = settings.AgentCallsInText,
            Nudges = settings.AgentNudges,
            Template = lease.ChatTemplate ?? (chat as ChatGenerator)?.Template,
        };
        var tools = new CodingTools(copy.Copy, options.Tools, sandbox);
        var observed = new CountingChatModel(chat, () => Post(TurnStarted), (tokens, seconds) => Post(() => Counted(tokens, seconds)));
        var agent = new CodingAgent(observed, options)
        {
            OnDelta = delta => { if (delta.Content.Length > 0) { Post(() => Said(delta.Content)); } },
            // A changed file's diff counts are read here, on the agent's thread, never on the UI's.
            OnToolResult = result => { var change = ChangeAfter(copy, result, settings.AgentChangesIgnore); Post(() => ToolDone(result, change)); },
        };
        Post(() => _agent = agent);
        var run = earlier is null
            ? await agent.RunAsync(task, tools, token).ConfigureAwait(false)
            : await agent.ContinueAsync(earlier, task, tools, token).ConfigureAwait(false);
        // B6: what the run wrote by any way (a tool, a command), listed at its end, so nothing it made stays in the copy unseen.
        var written = Written(copy, settings.AgentChangesIgnore);
        Post(() => Listed(written));
        return (EndNote(run, written.Count, settings, lease.ContextLength), run);
    }

    /// <summary>
    /// What the panel says when a run ends (note 27: the model's own claim never stands alone): a run that called no tool
    /// and changed nothing says so, whatever the model wrote; a run whose calls could not be read says how many corrections
    /// it was given; the corrections sent are counted.
    /// </summary>
    internal static string EndNote(CodingAgentRun run, int written, AiSettings settings, int contextLength)
    {
        bool nothing = run.ToolCalls == 0 && written == 0;
        string nudged = run.Nudges == 0 ? ""
            : string.Create(CultureInfo.InvariantCulture, $" The model was corrected {run.Nudges} {(run.Nudges == 1 ? "time" : "times")} for tool calls it wrote as text.");
        return run.End switch
        {
            CodingAgentEnd.Answered when nothing => "The model wrote no tool calls; nothing was changed." + nudged,
            CodingAgentEnd.Answered => string.Create(CultureInfo.InvariantCulture, $"Done in {run.Rounds} turns, {run.ToolCalls} tool calls.") + nudged,
            CodingAgentEnd.CallsNotRead => string.Create(CultureInfo.InvariantCulture,
                $"Stopped: the model kept writing tool calls that could not be read, after {run.Nudges} {(run.Nudges == 1 ? "correction" : "corrections")} (agentNudges {settings.AgentNudges}); nothing from its last reply was run.")
                + (nothing ? " Nothing was changed." : ""),
            CodingAgentEnd.OutOfBudget when run.Note.Length == 0 => string.Create(CultureInfo.InvariantCulture,
                $"Stopped: the reply reached its limit (agentMaxTokens {settings.AgentMaxTokens}, or the room the model's context of {contextLength} left).") + (nothing ? " Nothing was changed." : ""),
            CodingAgentEnd.OutOfBudget => $"Stopped: {run.Note}." + (nothing ? " Nothing was changed." : ""),
            _ => $"Ended: {run.End} {run.Note}".TrimEnd() + "." + (nothing ? " Nothing was changed." : ""),
        };
    }

    private void TurnStarted()
    {
        _turn++;
        EndAssistant();
        _assistant = -1;
        _text.Clear();
        UpdateStatus();
    }

    private void Counted(int tokens, double seconds)
    {
        _tokens += tokens;
        _modelSeconds += seconds;
        UpdateStatus();
    }

    private void Said(string piece)
    {
        _text.Append(piece);
        var entry = new ConversationEntry(ConversationRole.Assistant, _text.ToString()) { IsStreaming = true, Header = _header };
        if (_assistant < 0)
        {
            _assistant = Log(entry);
        }
        else
        {
            _log[_assistant] = entry;
            if (_changes.Count == 0)
            {
                Update(_assistant, entry);
            }
            else
            {
                ShowChanges();
            }
        }
    }

    // The changes a tool's call left, each with the relative path it touched (its change null: the same as the workspace's):
    // edit_file's and write_file's file, read by the tools' own path rule (B6), and after run_command every file of the
    // copy written since it was made; null for any other tool.
    private static IReadOnlyList<(AgentChange? Change, string Relative)>? ChangeAfter(AgentWorkspace copy, ToolResult result, IReadOnlyList<string> ignore)
    {
        if (result.Call.Name == "run_command")
        {
            return Written(copy, ignore);
        }

        if (result.Call.Name is not ("edit_file" or "write_file") || result.Call.Arguments["path"]?.ToString() is not { } path || copy.ToolRelative(path) is not { } relative)
        {
            return null;
        }

        return [(copy.ChangeOf(relative), Display(copy, relative))];
    }

    // Every file of the copy written since it was made, with its change against the workspace (off the UI thread).
    private static IReadOnlyList<(AgentChange? Change, string Relative)> Written(AgentWorkspace copy, IReadOnlyList<string> ignore)
    {
        try
        {
            return [.. copy.WrittenFiles(ignore).Select(relative => (copy.ChangeOf(relative), Display(copy, relative)))];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];                                                      // a file held open by a command: its tool's change still lists it
        }
    }

    private static string Display(AgentWorkspace copy, string relative) =>
        System.IO.Path.GetRelativePath(copy.Workspace, copy.WorkspacePath(relative)).Replace('\\', '/');

    // The run's written files at its end, listed as the tools' are.
    private void Listed(IReadOnlyList<(AgentChange? Change, string Relative)> written)
    {
        if (_copy is null || written.Count == 0)
        {
            return;
        }

        Apply(written);
        ShowChanges();
    }

    private void Apply(IReadOnlyList<(AgentChange? Change, string Relative)> edited)
    {
        foreach (var (change, relative) in edited)
        {
            if (change is not null)
            {
                _changes[change.Relative] = change;
            }
            else
            {
                _changes.Remove(relative);
            }
        }
    }

    private void ToolDone(ToolResult result, IReadOnlyList<(AgentChange? Change, string Relative)>? edited)
    {
        EndAssistant();
        _assistant = -1;
        _text.Clear();
        string what = result.Call.Arguments["path"]?.ToString() ?? result.Call.Arguments["pattern"]?.ToString() ?? "";
        if (result.Call.Name != "run_command")
        {
            Log(new ConversationEntry(ConversationRole.Tool, result.Call.Name)
            {
                Detail = string.Create(CultureInfo.InvariantCulture, $"{what} · turn {_turn} · {Summarize(result, 70)}"),
                Succeeded = result.Succeeded,
                Details = DetailsOf(result.Call, result),
            });
        }

        if (edited is { Count: > 0 } after && _copy is not null)
        {
            Apply(after);
            ShowChanges();
        }
    }

    private int _command = -1;

    private void CommandStarted(IReadOnlyList<string> command, CommandLimits limits)
    {
        EndAssistant();
        _waiting = true;
        string programs = limits.Programs is { Count: > 0 } allowed ? string.Join(", ", allowed) : "any";
        _command = Log(new ConversationEntry(ConversationRole.Approval, string.Join(' ', command))
        {
            Detail = string.Create(CultureInfo.InvariantCulture,
                $"turn {_turn} · waiting for the '{_sandboxName}' sandbox (it asks you to approve) · programs: {programs} · time limit {limits.Time.TotalSeconds:F0} s · network {(limits.Network ? "on" : "off")} · folder: the working copy"),
        });
        UpdateStatus();
    }

    private void CommandEnded(CommandResult result)
    {
        _waiting = false;
        _approval?.TrySetCanceled();
        _approval = null;
        if (_command >= 0 && _command < _log.Count)
        {
            var entry = _log[_command];
            string outcome = result.Refused
                ? "refused: " + FirstLine(result.Output)
                : result.TimedOut ? "stopped at the time limit" : string.Create(CultureInfo.InvariantCulture, $"exit {result.ExitCode} · {result.Duration.TotalSeconds:F1} s · {FirstLine(result.Output)}");
            _log[_command] = new ConversationEntry(ConversationRole.Tool, "run_command " + entry.Text)
            {
                Detail = outcome, Succeeded = result.Succeeded, Details = "Command: " + entry.Text + "\nOutput:\n" + result.Output.TrimEnd(),
            };
            ShowChanges();
        }

        _command = -1;
        UpdateStatus();
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        int end = trimmed.IndexOf('\n', StringComparison.Ordinal);
        string line = end >= 0 ? trimmed[..end] : trimmed;
        return line.Length <= 100 ? line : string.Concat(line.AsSpan(0, 99), "…");
    }

    private void UpdateStatus()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _settings();
        if (IsBusy && _loadingModel is { } loading)
        {
            Status = $"Loading {loading}… the task goes to the model once it is loaded (its progress and Cancel are in the status bar).";
            return;
        }

        var elapsed = _elapsed.Elapsed;
        string state = IsBusy ? (_waiting ? "Waiting for you · " : "Running · ") : "";
        Status = string.Create(CultureInfo.InvariantCulture,
            $"{state}Turn {_turn} of {settings.AgentMaxTurns} · {_tokens:N0} tokens · {(_modelSeconds > 0 ? _tokens / _modelSeconds : 0):F0} tok/s · Elapsed {(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2} · Sandbox {(_sandboxName.Length > 0 ? _sandboxName : settings.AgentSandbox)}");
        if (IsBusy && _turn > 0)
        {
            _activity?.Report(null, string.Create(CultureInfo.InvariantCulture, $"{(_waiting ? "waiting for you · " : "")}turn {_turn} of {settings.AgentMaxTurns} · {_tokens:N0} tokens"),
                _modelSeconds > 0 ? new ActivityRate(_tokens / _modelSeconds, "tok/s") : null);
        }
    }

    private int Log(ConversationEntry entry)
    {
        _log.Add(entry);
        if (_changes.Count == 0)
        {
            Add(entry);
        }
        else
        {
            ShowChanges();
        }

        return _log.Count - 1;
    }

    // The entries: the run's log, then the changed files with their actions.
    private void ShowChanges()
    {
        var entries = new List<ConversationEntry>(_log);
        if (_changes.Count > 0)
        {
            entries.Add(new ConversationEntry(ConversationRole.Section, "CHANGED FILES")
            {
                Actions =
                [
                    new PanelAction("Review diff", token => ReviewAsync(token), Primary: true),
                    new PanelAction("Discard all", _ => { DiscardAll(); return Task.CompletedTask; }),
                ],
            });
            foreach (var change in Changes)
            {
                var item = change;
                entries.Add(new ConversationEntry(ConversationRole.File, change.Relative)
                {
                    Detail = change.Counts,
                    Actions = [new PanelAction("Review diff", token => ReviewAsync(item, token))],
                });
            }
        }

        ReplaceFrom(0, entries);
    }

    private void Clear()
    {
        _conversation = null;                          // the next message starts a new run and a new conversation
        _log.Clear();
        if (_changes.Count == 0)
        {
            DiscardLater(_copy);
            _copy = null;
        }

        Title = "Agent";
        ShowChanges();
        Status = null;
        _session.End();                               // a new session: the shared settings, its own copy let go
    }

    /// <summary>A sandbox that refuses every command, saying why (no sandbox registered under the setting's name).</summary>
    private sealed class RefusingSandbox(string name, string reason) : ICommandSandbox
    {
        public string Name { get; } = name;

        public string Summary => "refuses every command: " + reason;

        public string? Check(CommandLimits limits) => null;

        public ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CommandResult(null, false, reason, TimeSpan.Zero, Refused: true));
    }

    /// <summary>A sandbox that tells the panel about each command before and after it runs, and otherwise is <paramref name="inner"/>.</summary>
    private sealed class ObservedSandbox(ICommandSandbox inner, Action<IReadOnlyList<string>, CommandLimits> started, Action<CommandResult> ended) : ICommandSandbox
    {
        public string Name => inner.Name;

        public string Summary => inner.Summary;

        public string? Check(CommandLimits limits) => inner.Check(limits);

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            started(command, limits);
            CommandResult result;
            try
            {
                result = await inner.RunAsync(command, directory, limits, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ended(new CommandResult(null, false, "stopped", TimeSpan.Zero, Refused: true));
                throw;
            }

            ended(result);
            return result;
        }
    }

    /// <summary>A chat model that tells the panel when a turn starts and how many tokens it sampled in how long, and otherwise is <paramref name="inner"/>.</summary>
    private sealed class CountingChatModel(IChatModel inner, Action turn, Action<int, double> counted) : IChatModel
    {
        public IReadOnlySet<string> PartKinds => inner.PartKinds;

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            turn();
            var watch = Stopwatch.StartNew();
            int seen = 0;
            await foreach (var chunk in inner.StreamAsync(request, cancellationToken).ConfigureAwait(false))
            {
                int now = chunk.Done && chunk.Stats is { } stats ? stats.GeneratedTokens : chunk.Generated;
                if (now == 0 && !chunk.Done && chunk.Delta.Content.Length > 0)
                {
                    now = seen + 1;    // a model that does not count: a piece a token
                }

                if (now > seen)
                {
                    counted(now - seen, watch.Elapsed.TotalSeconds);
                    watch.Restart();
                    seen = now;
                }

                yield return chunk;
            }
        }
    }
}
