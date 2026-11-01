// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>
/// The git plug-in in one host (X part 8): it opens the open folder's repository, publishes it (the shell's branch and tree
/// colours), watches the folder and the saves to read the status again after a pause, draws the Source Control view and
/// its count, runs the user's actions (each an activity; their own, so no sandbox question), marks the open files' changed
/// lines in the gutter and shows a change inline with Revert and Stage, resolves conflicts, and writes a commit message
/// with a model already loaded. Called on the host's UI thread; git, the files and the diffs run on the thread pool and
/// come back through <see cref="IEditorHost.Dispatch(string, Action)"/>.
/// </summary>
public sealed partial class GitSession : IDisposable
{
    /// <summary>The source name of its gutter marks.</summary>
    public const string MarkSource = "git";

    private readonly IEditorHost _host;
    private readonly ViewPanel _panel;
    private readonly Dictionary<IEditorDocument, DocumentGutter> _gutters = [];
    private readonly List<IDisposable> _held = [];
    private GitRepository? _repository;
    private IDisposable? _published;
    private IDisposable? _watch;
    private Timer? _autofetch;
    private SourceControlViewState _state;
    private IReadOnlyList<GitBranchInfo> _branches = [];
    private string _draft = "";
    private CancellationTokenSource? _writing;
    private CancellationTokenSource? _opening;
    private bool _disposed;

    /// <summary>The session in <paramref name="host"/>: its view added, the open folder's repository opened.</summary>
    public GitSession(IEditorHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _state = new SourceControlViewState { NoFolder = host.WorkspaceFolder is null };
        _panel = new ViewPanel(SourceControlView.PanelId, "Source Control", ActAsync);
        _held.Add(host.AddPanel(_panel));
        host.WorkspaceFolderChanged += OnWorkspaceChanged;
        host.DocumentSaved += OnDocumentSaved;
        host.DocumentOpened += OnDocumentOpened;
        host.DocumentChanged += OnDocumentChanged;
        host.DocumentClosed += OnDocumentClosed;
        host.LineMarkClicked += OnLineMarkClicked;
        CommitMessageProviders.Changed += OnProvidersChanged;
        CodeReviewProviders.Changed += OnProvidersChanged;
        Render();
        _ = OpenAsync();
    }

    /// <summary>The settings now.</summary>
    public GitSettings Settings => GitSettings.Read(_host.Settings);

    /// <summary>The open repository, or null.</summary>
    public GitRepository? Repository => _repository;

    /// <summary>The view's panel.</summary>
    public ViewPanel Panel => _panel;

    /// <summary>What the view shows now.</summary>
    public SourceControlViewState State => _state;

    /// <summary>The commit message as the user has it now (typed, or a draft written into the box).</summary>
    public string Draft => _draft;

    /// <summary>A task that completes when the repository's opening (and its first read) ended; for the tests.</summary>
    public Task Opened { get; private set; } = Task.CompletedTask;

    /// <summary>The marks the gutter shows for <paramref name="document"/> now (the last worked out), for the tests.</summary>
    public IReadOnlyList<LineMark> MarksOf(IEditorDocument document) => _gutters.TryGetValue(document, out var gutter) ? gutter.Marks : [];

    /// <summary>A task that completes when <paramref name="document"/>'s gutter marks being worked out are done; for the tests.</summary>
    public Task GutterOf(IEditorDocument document) => _gutters.TryGetValue(document, out var gutter) ? gutter.Work : Task.CompletedTask;

    /// <summary>Runs a block's action (on the UI thread; the work runs off it).</summary>
    public Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        switch (actionId)
        {
            case SourceControlView.Message:
                _draft = value ?? "";                          // told as the user types: nothing is redrawn
                return Task.CompletedTask;
            case SourceControlView.Amend:
                Update(_state with { Amend = value == "on" });
                return Task.CompletedTask;
            case SourceControlView.Dismiss:
                Update(_state with { Error = null });
                return Task.CompletedTask;
            case SourceControlView.Retry:
                return OpenAsync();
            case SourceControlView.Settings:
                return _host.ExecuteCommandAsync(SettingSections.CommandOf(SettingSections.SourceControl), cancellationToken);
            case SourceControlView.OpenFolder:
                return _host.ExecuteCommandAsync("craft.openFolder", cancellationToken);
            case SourceControlView.Stop:
                return _writing?.CancelAsync() ?? Task.CompletedTask;
            case SourceControlView.Generate:
                return GenerateAsync(null);
            case SourceControlView.PickModel:
                return GenerateAsync(value);
            case SourceControlView.LoadModel when _state.LoadCommand is { } load:
                Update(_state with { LoadCommand = null, MessageStatus = "Loading the chat model… press Generate once it is loaded (its progress is in the status bar).", MessageTone = PanelTone.Normal });
                return _host.ExecuteCommandAsync(load, cancellationToken);
            case SourceControlView.Commit:
                if (value is not null)
                {
                    _draft = value;
                }

                return CommitAsync();
            case SourceControlView.Open when value is not null:
                return OpenChangesAsync(value);
            case SourceControlView.OpenFile when value is not null:
                return _host.OpenDocumentAsync(value, cancellationToken);
            case SourceControlView.Review:
                return ReviewAsync();
            case SourceControlView.StopReview:
                return _reviewing?.CancelAsync() ?? Task.CompletedTask;
            case SourceControlView.LoadReviewModel when _state.ReviewLoadCommand is { } loadReview:
                Update(_state with { ReviewLoadCommand = null, ReviewStatus = "Loading the model… press Review once it is loaded (its progress is in the status bar).", ReviewTone = PanelTone.Normal });
                return _host.ExecuteCommandAsync(loadReview, cancellationToken);
            case SourceControlView.OpenFinding when value is not null:
                return OpenFindingAsync(value);
            case SourceControlView.ApplyFix when value is not null:
                return ApplyFixAsync(value, cancellationToken);
            case SourceControlView.DismissFinding when value is not null:
                DismissFinding(value);
                return Task.CompletedTask;
            case SourceControlView.CopyFindings:
                CopyFindings();
                return Task.CompletedTask;
            case SourceControlView.ClearFindings:
                ClearFindings();
                return Task.CompletedTask;
        }

        if (_repository is not { } repository)
        {
            return actionId == SourceControlView.Init ? InitAsync() : Task.CompletedTask;
        }

        var status = repository.Status;
        return actionId switch
        {
            SourceControlView.Stage when value is not null => Run("Staging", r => r.StageAsync([value])),
            SourceControlView.Unstage when value is not null => Run("Unstaging", r => r.UnstageAsync([value])),
            SourceControlView.Resolved when value is not null => Run("Marking resolved", r => r.StageAsync([value])),
            SourceControlView.StageAll => Run("Staging every change", r => r.StageAsync([.. status.Changes.Select(f => f.Path)])),
            SourceControlView.UnstageAll => Run("Unstaging every change", r => r.UnstageAsync([.. status.Staged.Select(f => f.Path)])),
            SourceControlView.Discard when value is not null => DiscardAsync([.. status.Changes.Where(f => string.Equals(f.Path, value, StringComparison.OrdinalIgnoreCase))]),
            SourceControlView.DiscardAll => DiscardAsync(status.Changes),
            SourceControlView.Pull => Run("Pulling", (r, t) => r.RunAsync(["pull"], t), network: true),
            SourceControlView.Push => PushAsync(),
            SourceControlView.Fetch => Run("Fetching", (r, t) => r.RunAsync(["fetch", "--all", "--prune"], t), network: true),
            SourceControlView.Sync => SyncAsync(),
            SourceControlView.Stash => Run("Stashing", r => r.RunAsync(["stash", "push", "--include-untracked"])),
            SourceControlView.Pop => Run("Taking the stash back", r => r.RunAsync(["stash", "pop"])),
            SourceControlView.Branch when value is { Length: > 0 } && value != status.Branch => Run($"Switching to {value}", r => r.RunAsync(["switch", value])),
            SourceControlView.NewBranch when value is { Length: > 0 } => Run($"Creating {value.Trim()}", r => r.RunAsync(["switch", "-c", value.Trim()])),
            SourceControlView.AcceptCurrent when value is not null => AcceptFileAsync(value, ConflictChoice.Current),
            SourceControlView.AcceptIncoming when value is not null => AcceptFileAsync(value, ConflictChoice.Incoming),
            SourceControlView.AcceptBoth when value is not null => AcceptFileAsync(value, ConflictChoice.Both),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>The status bar's branch: the branches to switch to (the current one marked), or the view to make a new one.</summary>
    public async Task PickBranchAsync()
    {
        if (_repository is not { } repository)
        {
            _host.ShowPanel(SourceControlView.PanelId);
            return;
        }

        var branches = await repository.BranchesAsync().ConfigureAwait(true);
        string? current = repository.Status.Branch;
        var choices = branches.Select(b => b.Name == current ? $"{b.Name} (current)" : b.Name).ToList();
        choices.Add("Create a branch…");
        int answer = await _host.AskAsync(new EditorQuestion("Switch branch", current is null ? "Pick a branch." : $"On {repository.Status.BranchText}. Pick a branch.", choices)).ConfigureAwait(true);
        if (answer < 0)
        {
            return;
        }

        if (answer == branches.Count)
        {
            _host.ShowPanel(SourceControlView.PanelId);          // the view's New branch field
            return;
        }

        var name = branches[answer].Name;
        if (name != current)
        {
            await Run($"Switching to {name}", r => r.RunAsync(["switch", name])).ConfigureAwait(true);
        }
    }

    /// <summary>The changed file of <paramref name="path"/> in the status (the working copy's change first), or null.</summary>
    public SourceControlFile? FileOf(string path) => _repository?.Status.FileState(path);

    /// <summary>Stages the file <paramref name="path"/> (the tree's and the tab's menus).</summary>
    public Task StagePathAsync(string path) => _repository is null ? Task.CompletedTask : Run("Staging", r => r.StageAsync([path]));

    /// <summary>Unstages the file <paramref name="path"/>.</summary>
    public Task UnstagePathAsync(string path) => _repository is null ? Task.CompletedTask : Run("Unstaging", r => r.UnstageAsync([path]));

    /// <summary>Discards the working copy's change of <paramref name="path"/>, with Undo.</summary>
    public Task DiscardPathAsync(string path) => _repository is not { } repository ? Task.CompletedTask
        : DiscardAsync([.. repository.Status.Changes.Where(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))]);

    /// <summary>
    /// Shows the change of <paramref name="path"/> in the diff view, read-only: a staged file against the last commit, else the
    /// working copy against the index.
    /// </summary>
    public async Task OpenChangesAsync(string path)
    {
        if (_repository is not { } repository)
        {
            return;
        }

        var status = repository.Status;
        bool staged = status.Staged.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
            && !status.Changes.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        var file = status.FileState(path);
        try
        {
            string before = (staged ? await repository.ReadCommittedAsync(file?.OldPath ?? path).ConfigureAwait(true) : await repository.ReadStagedAsync(path).ConfigureAwait(true)) ?? "";
            string after = staged
                ? await repository.ReadStagedAsync(path).ConfigureAwait(true) ?? ""
                : _host.Documents.FirstOrDefault(d => SamePath(d.FilePath, path)) is { } open ? open.Document.Buffer.Current.GetText(0, open.Document.Buffer.Current.Length)
                : File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(true) : "";
            string title = $"{Path.GetFileName(path)} ({(staged ? "staged, against the last commit" : "working copy, against the index")})";
            var notes = NotesOf(path);
            bool shown = notes.Count > 0
                ? await _host.ShowDiffAsync(title, path, before, after, notes).ConfigureAwait(true)
                : await _host.ShowDiffAsync(title, path, before, after).ConfigureAwait(true);
            if (!shown)
            {
                await _host.OpenDocumentAsync(path).ConfigureAwait(true);   // a host without the diff view opens the file
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or GitException)
        {
            Update(_state with { Error = error.Message });
        }
    }

    /// <summary>Reveals the Source Control view.</summary>
    public void Show() => _host.ShowPanel(SourceControlView.PanelId);

    // A timer's callback runs on a pool thread, where a throw ends the process: what it throws goes to the log instead.
    private void Guarded(string where, Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Nothing may leave a timer's callback (the owner, 2026-11-01: never end the app).
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Error, $"{where} failed.", error);
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
        _host.WorkspaceFolderChanged -= OnWorkspaceChanged;
        _host.DocumentSaved -= OnDocumentSaved;
        _host.DocumentOpened -= OnDocumentOpened;
        _host.DocumentChanged -= OnDocumentChanged;
        _host.DocumentClosed -= OnDocumentClosed;
        _host.LineMarkClicked -= OnLineMarkClicked;
        CommitMessageProviders.Changed -= OnProvidersChanged;
        CodeReviewProviders.Changed -= OnProvidersChanged;
        _writing?.Cancel();
        _reviewing?.Cancel();
        _opening?.Cancel();
        Close();
        foreach (var held in _held)
        {
            held.Dispose();
        }

        _held.Clear();
    }

    // ---- the repository ----

    private Task OpenAsync()
    {
        _ = _opening?.CancelAsync();
        var opening = _opening = new CancellationTokenSource();
        Opened = OpenCoreAsync(opening.Token);
        return Opened;
    }

    private async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        Close();
        var folder = _host.WorkspaceFolder;
        if (folder is null)
        {
            Update(new SourceControlViewState { NoFolder = true, Message = _draft, MessageVersion = _state.MessageVersion });
            return;
        }

        var settings = Settings;
        if (GitProgram.Find(settings.Path, out var why) is not { } git)
        {
            Update(new SourceControlViewState { Unavailable = why });
            return;
        }

        Update(_state with { Opening = true, Unavailable = null, NoFolder = false, NotRepository = null });
        string? root;
        try
        {
            root = await GitRepository.FindRootAsync(git, folder, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Update(new SourceControlViewState { Unavailable = $"Git could not be run ({git.Path}): {error.Message}" });
            return;
        }

        if (cancellationToken.IsCancellationRequested || _disposed)
        {
            return;
        }

        if (root is null)
        {
            Update(new SourceControlViewState { NotRepository = folder });
            return;
        }

        var repository = new GitRepository(git, root, action => _host.Dispatch("git: the status changed", action), () => Settings.RefreshMilliseconds);
        _repository = repository;
        repository.Changed += OnRepositoryChanged;
        _published = _host.AddRepository(repository);
        try
        {
            _watch = FolderWatchers.Get(FolderWatchers.System).Watch(root, OnFolderChange);
        }
        catch (IOException error)
        {
            _host.Log(EditorLogLevel.Warning, $"The folder {root} cannot be watched ({error.Message}): the status is read on saves and actions only.");
        }

        if (settings.AutofetchMinutes > 0)
        {
            var every = TimeSpan.FromMinutes(settings.AutofetchMinutes);
            _autofetch = new Timer(_ => Guarded("git: the background fetch's timer", () => _host.Dispatch("git: fetch in the background", () => _ = Run("Fetching", (r, t) => r.RunAsync(["fetch", "--all", "--prune"], t), network: true, quiet: true))), null, every, every);
        }

        Update(_state with { Opening = false, Status = repository.Status });
        await repository.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await ReadBranchesAsync().ConfigureAwait(true);
        Update(_state with { Status = repository.Status });
        foreach (var document in _host.Documents)
        {
            ScheduleGutter(document, 0);
        }
    }

    private void Close()
    {
        _autofetch?.Dispose();
        _autofetch = null;
        _watch?.Dispose();
        _watch = null;
        _published?.Dispose();
        _published = null;
        if (_repository is { } repository)
        {
            repository.Changed -= OnRepositoryChanged;
            repository.Dispose();
            _repository = null;
        }

        foreach (var (document, gutter) in _gutters)
        {
            gutter.Cancel();
            _host.SetLineMarks(document, MarkSource, []);
        }

        _gutters.Clear();
    }

    private async Task InitAsync()
    {
        if (_host.WorkspaceFolder is not { } folder || GitProgram.Find(Settings.Path, out _) is not { } git)
        {
            return;
        }

        var activity = _host.Activities.Start($"Initializing a repository in {Path.GetFileName(folder)}", ActivityKinds.Command);
        var result = await git.RunAsync(folder, ["init"]).ConfigureAwait(true);
        if (result.Ok)
        {
            activity.Complete("initialized");
            await OpenAsync().ConfigureAwait(true);
        }
        else
        {
            activity.Fail(result.Problem);
            Update(_state with { Error = "git init: " + result.Problem });
        }
    }

    private async Task ReadBranchesAsync()
    {
        if (_repository is { } repository)
        {
            _branches = await repository.BranchesAsync().ConfigureAwait(true);
        }
    }

    // Any change under the folder (a file made, removed or renamed; git's index or HEAD rewritten): the status is read
    // again after the pause. git's object store and its locks are its own churn, not a change.
    private void OnFolderChange(FolderChange change)
    {
        var path = change.Path;
        if (path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}objects", StringComparison.Ordinal)
            || path.Contains($"{Path.AltDirectorySeparatorChar}.git{Path.AltDirectorySeparatorChar}objects", StringComparison.Ordinal)
            || path.EndsWith(".lock", StringComparison.Ordinal))
        {
            return;
        }

        _repository?.ScheduleRefresh();
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _repository) || _repository is not { } repository)
        {
            return;
        }

        bool branchMoved = _state.Status.Branch != repository.Status.Branch || _state.Status.Ahead != repository.Status.Ahead || _state.Status.Behind != repository.Status.Behind;
        Update(_state with { Status = repository.Status });
        if (branchMoved)
        {
            _ = ReadBranchesThenRender();
        }

        foreach (var document in _host.Documents)
        {
            ScheduleGutter(document, 0);                          // the index may have moved under the open files
        }
    }

    private async Task ReadBranchesThenRender()
    {
        await ReadBranchesAsync().ConfigureAwait(true);
        Render();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => _ = OpenAsync();

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is { } path && _repository is { } repository && repository.Contains(path))
        {
            repository.ScheduleRefresh();
        }
    }

    private void OnProvidersChanged(object? sender, EventArgs e) => _host.Dispatch("git: the commit message providers changed", Render);

    // ---- the view ----

    private void Update(SourceControlViewState state)
    {
        _state = state;
        Render();
    }

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        var provider = FirstProvider();
        var settings = Settings;
        string tooltip;
        IReadOnlyList<CommitModelChoice> models = [];
        if (provider is null)
        {
            tooltip = "Generate commit message: needs the AI plug-in (Craft.Ai), which is not loaded";
        }
        else
        {
            models = provider.Models();
            var plan = provider.Plan(settings.CommitModel);
            tooltip = plan.Model is { } model
                ? $"Generate commit message with {model} ({plan.Role}){Keys()}"
                : $"Generate commit message{Keys()}: {plan.Why}";
        }

        var reviewer = CodeReviewProviders.First;
        string reviewTooltip = "Review changes: needs the AI plug-in (Craft.Ai), which is not loaded";
        if (reviewer is not null)
        {
            var reviewPlan = reviewer.Plan();
            reviewTooltip = reviewPlan.Model is { } reviewModel
                ? $"Review changes with {reviewModel} ({reviewPlan.Role}): the staged changes, else every change"
                : $"Review changes: {reviewPlan.Why}";
        }

        _state = _state with
        {
            Branches = _branches, Models = models, GenerateTooltip = tooltip, CanGenerate = provider is not null,
            ReviewTooltip = reviewTooltip, CanReview = reviewer is not null,
        };
        _panel.SetBlocks(SourceControlView.Blocks(_state));
        _panel.Badge = SourceControlView.Badge(_state);
    }

    private static ICommitMessageProvider? FirstProvider() => CommitMessageProviders.All is { Count: > 0 } all ? all[0] : null;

    private static string Keys()
    {
        foreach (var binding in EditorKeyBindings.All)
        {
            if (binding.CommandId == GitPlugin.GenerateCommand)
            {
                return $" ({binding.DisplayText})";
            }
        }

        return "";
    }

    // ---- the user's actions ----

    // One action of the user's on the repository: an activity in the status bar (a network one cancellable), its failure
    // in the view in git's words, the view drawn again when it ends.
    private Task Run(string title, Func<GitRepository, Task> action, bool network = false, bool quiet = false) =>
        Run(title, (r, _) => action(r), network, quiet);

    // The same with the activity's token (a network action's Cancel ends git's process).
    private async Task Run(string title, Func<GitRepository, CancellationToken, Task> action, bool network = false, bool quiet = false)
    {
        if (_repository is not { } repository)
        {
            return;
        }

        var activity = _host.Activities.Start(title + "…", ActivityKinds.Command, cancellable: network, keep: !quiet);
        if (!quiet)
        {
            Update(_state with { Working = title + "…", Error = null });
        }

        try
        {
            await action(repository, activity.CancellationToken).ConfigureAwait(true);
            activity.Complete();
            if (!quiet)
            {
                Update(_state with { Working = null });
            }
        }
        catch (OperationCanceledException)
        {
            activity.Dispose();
            Update(_state with { Working = null });
        }
        catch (Exception error) when (error is GitException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            activity.Fail(error.Message, error);
            Update(_state with { Working = null, Error = quiet ? _state.Error : error.Message });
        }

        await repository.RefreshAsync().ConfigureAwait(true);
        await ReadBranchesAsync().ConfigureAwait(true);
        Update(_state with { Status = repository.Status });
    }

    private Task PushAsync()
    {
        var status = _repository?.Status;
        if (status is { Upstream: null, Branch: { } branch })
        {
            // No upstream yet: publish the branch to the remote (the first remote, "origin" when there is one).
            return Run($"Publishing {branch}", async (r, t) =>
            {
                var remotes = (await r.RunAsync(["remote"], t).ConfigureAwait(false)).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (remotes.Length == 0)
                {
                    throw new GitException("git push: the repository has no remote; add one (git remote add origin URL) to publish the branch.");
                }

                string remote = remotes.Contains("origin") ? "origin" : remotes[0];
                await r.RunAsync(["push", "--set-upstream", remote, branch], t).ConfigureAwait(false);
            }, network: true);
        }

        return Run("Pushing", (r, t) => r.RunAsync(["push"], t), network: true);
    }

    private async Task SyncAsync()
    {
        if (_repository is not { } repository)
        {
            return;
        }

        var status = repository.Status;
        if (Settings.ConfirmSync)
        {
            int answer = await _host.AskAsync(new EditorQuestion("Sync",
                string.Create(CultureInfo.InvariantCulture, $"Pull {status.Behind} and push {status.Ahead} commits between {status.Branch} and {status.Upstream}?"),
                ["Sync", "Cancel"]) { Details = ["Turn this question off with CRAFT_PLUGIN_GIT_CONFIRMSYNC=false (Settings › Source Control)."], Dismissed = 1 }).ConfigureAwait(true);
            if (answer != 0)
            {
                return;
            }
        }

        await Run("Syncing", async (r, t) =>
        {
            await r.RunAsync(["pull"], t).ConfigureAwait(false);
            await r.RunAsync(["push"], t).ConfigureAwait(false);
        }, network: true).ConfigureAwait(true);
    }

    private async Task CommitAsync()
    {
        if (_repository is not { } repository)
        {
            return;
        }

        string message = _draft.Trim();
        if (message.Length == 0)
        {
            Update(_state with { Error = "Write a commit message first (or press the sparkle to generate one from a loaded model)." });
            return;
        }

        var status = repository.Status;
        bool stageAll = false;
        if (status.Staged.Count == 0 && !_state.Amend)
        {
            if (status.Changes.Count == 0)
            {
                Update(_state with { Error = "There is nothing to commit." });
                return;
            }

            int answer = await _host.AskAsync(new EditorQuestion("Commit", $"Nothing is staged. Stage all {status.Changes.Count} changes and commit them?", ["Stage all and commit", "Cancel"]) { Dismissed = 1 }).ConfigureAwait(true);
            if (answer != 0)
            {
                return;
            }

            stageAll = true;
        }

        bool amend = _state.Amend;
        bool committed = false;
        await Run(amend ? "Amending the last commit" : "Committing", async r =>
        {
            if (stageAll)
            {
                await r.StageAsync([.. status.Changes.Select(f => f.Path)]).ConfigureAwait(false);
            }

            await r.CommitAsync(message, amend).ConfigureAwait(false);
            committed = true;
        }).ConfigureAwait(true);
        if (committed)
        {
            _draft = "";
            Update(_state with { Message = "", MessageVersion = _state.MessageVersion + 1, Amend = false, MessageStatus = null });
        }
    }

    // Discard with Undo (B11's rule: the change made at once, the shell's bar takes it back); a discard larger than the
    // Undo's room asks first and keeps nothing.
    private async Task DiscardAsync(IReadOnlyList<SourceControlFile> files)
    {
        if (_repository is not { } repository || files.Count == 0)
        {
            return;
        }

        long bytes = 0;
        foreach (var file in files)
        {
            bytes += File.Exists(file.Path) ? new FileInfo(file.Path).Length : 0;
        }

        long room = (long)Settings.DiscardUndoMegabytes * 1024 * 1024;
        bool undoable = bytes <= room;
        if (!undoable)
        {
            int answer = await _host.AskAsync(new EditorQuestion("Discard changes",
                $"Discard the changes of {Describe(files)}? They are too large to keep for Undo ({bytes / (1024 * 1024)} MiB; CRAFT_PLUGIN_GIT_DISCARDUNDOMB {Settings.DiscardUndoMegabytes}).",
                ["Discard", "Cancel"]) { Dismissed = 1 }).ConfigureAwait(true);
            if (answer != 0)
            {
                return;
            }
        }

        IReadOnlyList<(string Path, byte[]? Bytes)> kept = [];
        await Run("Discarding", async r => kept = await r.DiscardAsync(files).ConfigureAwait(false)).ConfigureAwait(true);
        if (!undoable || kept.Count == 0)
        {
            return;
        }

        bool shown = _host.ShowUndo($"Discarded the changes of {Describe(files)}.", () => _ = RestoreAsync(repository, kept));
        if (!shown)
        {
            _host.Log(EditorLogLevel.Information, $"Discarded the changes of {Describe(files)} (this host has no Undo bar).");
        }
    }

    // Undo of a discard: each file's bytes written back (a deleted untracked file made again), off the UI thread.
    private async Task RestoreAsync(GitRepository repository, IReadOnlyList<(string Path, byte[]? Bytes)> kept)
    {
        await Task.Run(() =>
        {
            foreach (var (path, bytes) in kept)
            {
                if (bytes is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }).ConfigureAwait(true);
        await repository.RefreshAsync().ConfigureAwait(true);
        foreach (var document in _host.Documents)
        {
            ScheduleGutter(document, 0);
        }
    }

    private static string Describe(IReadOnlyList<SourceControlFile> files) =>
        files.Count == 1 ? Path.GetFileName(files[0].Path) : string.Create(CultureInfo.InvariantCulture, $"{files.Count} files");

    // ---- conflicts ----

    /// <summary>How a conflict is resolved.</summary>
    internal enum ConflictChoice
    {
        Current,
        Incoming,
        Both,
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="blocks"/> resolved by <paramref name="choice"/> (all of them when
    /// <paramref name="only"/> is null): the current lines, the incoming ones, or both, the markers gone.
    /// </summary>
    internal static string Resolve(string text, IReadOnlyList<ConflictBlock> blocks, ConflictChoice choice, ConflictBlock? only = null)
    {
        var lines = GitParsers.LinesWithBreaks(text);
        var result = new StringBuilder(text.Length);
        int next = 0;
        foreach (var block in blocks)
        {
            if (only is { } single && single != block)
            {
                continue;
            }

            for (; next < block.Start; next++)
            {
                result.Append(lines[next]);
            }

            int currentEnd = block.Base >= 0 ? block.Base : block.Middle;
            if (choice is ConflictChoice.Current or ConflictChoice.Both)
            {
                for (int i = block.Start + 1; i < currentEnd; i++)
                {
                    result.Append(lines[i]);
                }
            }

            if (choice is ConflictChoice.Incoming or ConflictChoice.Both)
            {
                for (int i = block.Middle + 1; i < block.End; i++)
                {
                    result.Append(lines[i]);
                }
            }

            next = block.End + 1;
        }

        for (; next < lines.Count; next++)
        {
            result.Append(lines[next]);
        }

        return result.ToString();
    }

    // A file's every conflict accepted one way: an open document through the diff (one undo step, the user accepts), a
    // closed file rewritten on disk with Undo.
    private async Task AcceptFileAsync(string path, ConflictChoice choice)
    {
        if (_host.Documents.FirstOrDefault(d => SamePath(d.FilePath, path)) is { } open)
        {
            await ProposeResolutionAsync(open, choice, null).ConfigureAwait(true);
            return;
        }

        try
        {
            byte[] before = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            string text = Encoding.UTF8.GetString(before);
            var blocks = GitParsers.Conflicts(text);
            if (blocks.Count == 0)
            {
                Update(_state with { Error = $"{Path.GetFileName(path)} has no conflict markers left: mark it resolved with ✓." });
                return;
            }

            await File.WriteAllTextAsync(path, Resolve(text, blocks, choice), new UTF8Encoding(false)).ConfigureAwait(true);
            _repository?.ScheduleRefresh();
            _host.ShowUndo($"Resolved {blocks.Count} {(blocks.Count == 1 ? "conflict" : "conflicts")} in {Path.GetFileName(path)}.",
                () => _ = Task.Run(() => File.WriteAllBytes(path, before)).ContinueWith(_ => _repository?.ScheduleRefresh(), TaskScheduler.Default));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Update(_state with { Error = error.Message });
        }
    }

    private async Task ProposeResolutionAsync(IEditorDocument document, ConflictChoice choice, ConflictBlock? only)
    {
        var snapshot = document.Document.Buffer.Current;
        string text = snapshot.GetText(0, snapshot.Length);
        var blocks = GitParsers.Conflicts(text);
        if (blocks.Count == 0)
        {
            return;
        }

        var edits = new List<TextEdit>();
        foreach (var block in blocks)
        {
            if (only is { } single && single != block)
            {
                continue;
            }

            int start = snapshot.GetLine(block.Start).Start;
            int end = snapshot.GetLine(block.End).EndIncludingBreak;
            string resolved = Resolve(snapshot.GetText(start, end - start), [new ConflictBlock(0, block.Base < 0 ? -1 : block.Base - block.Start, block.Middle - block.Start, block.End - block.Start)], choice);
            edits.Add(new TextEdit(start, end - start, resolved));
        }

        string what = choice switch
        {
            ConflictChoice.Current => "Accept current",
            ConflictChoice.Incoming => "Accept incoming",
            _ => "Accept both",
        };
        await _host.ProposeAsync(document, new ProposedEdits(edits, what) { Origin = "Source Control: your choice" }).ConfigureAwait(true);
    }

    // ---- the gutter ----

    private void OnDocumentOpened(object? sender, EditorDocumentEventArgs e)
    {
        ScheduleGutter(e.Document, 0);
        MarkFindings(e.Document);
    }

    private void OnDocumentChanged(object? sender, EditorDocumentChangedEventArgs e)
    {
        if (_repository is not null)
        {
            ScheduleGutter(e.Document, Settings.GutterDelayMilliseconds);
        }
    }

    private void OnDocumentClosed(object? sender, EditorDocumentEventArgs e)
    {
        if (_gutters.Remove(e.Document, out var gutter))
        {
            gutter.Cancel();
        }
    }

    // A document's marks worked out again after the pause: its text now (an immutable snapshot, read off the UI thread)
    // against the index's, no context lines; a conflicted file's blocks marked too.
    private void ScheduleGutter(IEditorDocument document, int delay)
    {
        if (_repository is not { } repository || document.FilePath is not { } path || !repository.Contains(path))
        {
            return;
        }

        if (!Settings.Gutter)
        {
            if (_gutters.Remove(document, out var off))
            {
                off.Cancel();
                _host.SetLineMarks(document, MarkSource, []);
            }

            return;
        }

        if (!_gutters.TryGetValue(document, out var gutter))
        {
            _gutters[document] = gutter = new DocumentGutter();
        }

        gutter.Cancel();
        var cancel = gutter.Start();
        var snapshot = document.Document.Buffer.Current;
        var status = repository.Status;
        bool untracked = status.Changes.Any(f => f.State == SourceControlState.Untracked && SamePath(f.Path, path));
        bool conflicted = status.Conflicts.Any(f => SamePath(f.Path, path));
        int lineLimit = 2000;
        gutter.Work = Task.Run(async () =>
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancel).ConfigureAwait(false);
            }

            string text = snapshot.GetText(0, snapshot.Length);
            string? staged = untracked ? null : await repository.ReadStagedAsync(path, cancel).ConfigureAwait(false);
            var diff = staged is null || conflicted ? TextDiff.Empty : TextDiff.OfTexts(staged, text, 0, lineLimit);
            var marks = new List<LineMark>(GitParsers.Marks(diff));
            IReadOnlyList<ConflictBlock> blocks = conflicted ? GitParsers.Conflicts(text) : [];
            foreach (var block in blocks)
            {
                marks.Add(new LineMark(block.Start, block.End - block.Start + 1, LineMarkKind.Conflict));
            }

            cancel.ThrowIfCancellationRequested();
            var done = new TaskCompletionSource();
            _host.Dispatch("git: the gutter's marks", () =>
            {
                if (!cancel.IsCancellationRequested && !_disposed && _gutters.TryGetValue(document, out var current) && ReferenceEquals(current, gutter))
                {
                    gutter.Staged = staged;
                    gutter.Diff = diff;
                    gutter.Conflicts = blocks;
                    gutter.Marks = marks;
                    _host.SetLineMarks(document, MarkSource, marks);
                }

                done.TrySetResult();
            });
            await done.Task.ConfigureAwait(false);
        }, cancel).ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
    }

    // A click on a mark: the change inline (its old lines, its new ones) with Revert and Stage; a conflict's choices.
    private void OnLineMarkClicked(object? sender, LineMarkEventArgs e)
    {
        if (e.Source == ReviewMarkSource)
        {
            ShowFindingCard(e.Document, e.Mark.Line);
            return;
        }

        if (e.Source != MarkSource || !_gutters.TryGetValue(e.Document, out var gutter))
        {
            return;
        }

        var document = e.Document;
        var mark = e.Mark;
        if (mark.Kind == LineMarkKind.Conflict)
        {
            var block = gutter.Conflicts.FirstOrDefault(b => b.Start == mark.Line);
            _host.ShowInlineCard(document, new InlineCard(mark.Line, "A merge conflict: keep your lines, the incoming ones, or both.",
            [
                new InlineCardAction("Accept current", () => _ = ProposeResolutionAsync(document, ConflictChoice.Current, block)),
                new InlineCardAction("Accept incoming", () => _ = ProposeResolutionAsync(document, ConflictChoice.Incoming, block)),
                new InlineCardAction("Accept both", () => _ = ProposeResolutionAsync(document, ConflictChoice.Both, block)),
            ]));
            return;
        }

        if (HunkOf(gutter.Diff, mark) is not { } hunk)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var line in hunk.Lines)
        {
            if (line.Kind != DiffLineKind.Context)
            {
                text.Append(line.Kind == DiffLineKind.Removed ? "- " : "+ ").Append(line.Text).Append('\n');
            }
        }

        int at = mark.Kind == LineMarkKind.Deleted ? Math.Max(0, mark.Line - 1) : mark.LastLine;
        _host.ShowInlineCard(document, new InlineCard(at, text.ToString().TrimEnd('\n'),
        [
            new InlineCardAction("Revert", () => _ = RevertHunkAsync(document, gutter, hunk)),
            new InlineCardAction("Stage", () => _ = StageHunkAsync(document, gutter, hunk)),
        ]));
    }

    /// <summary>The hunk of <paramref name="diff"/> a mark was made from (its new lines' start), or null.</summary>
    internal static DiffHunk? HunkOf(TextDiff diff, LineMark mark)
    {
        foreach (var hunk in diff.Hunks)
        {
            var made = GitParsers.Marks([(hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount)]);
            if (made.Count == 1 && made[0].Line == mark.Line && made[0].Kind == mark.Kind)
            {
                return hunk;
            }
        }

        return null;
    }

    // Revert: the hunk's new lines replaced by the index's, proposed (one undo step, the user accepts).
    private async Task RevertHunkAsync(IEditorDocument document, DocumentGutter gutter, DiffHunk hunk)
    {
        var snapshot = document.Document.Buffer.Current;
        int newAt = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;
        int start = newAt >= snapshot.LineCount ? snapshot.Length : snapshot.GetLine(newAt).Start;
        int end = hunk.NewCount == 0 ? start : snapshot.GetLine(Math.Min(snapshot.LineCount - 1, newAt + hunk.NewCount - 1)).EndIncludingBreak;
        var old = new StringBuilder();
        if (gutter.Staged is { } staged)
        {
            var lines = GitParsers.LinesWithBreaks(staged);
            int oldAt = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
            for (int i = oldAt; i < oldAt + hunk.OldCount && i < lines.Count; i++)
            {
                old.Append(lines[i]);
            }

            // The last line of the document with no break gets the index line without one.
            if (end == snapshot.Length && old.Length > 0 && start < end && snapshot[end - 1] != '\n' && old[^1] == '\n')
            {
                old.Length -= old.Length > 1 && old[^2] == '\r' ? 2 : 1;
            }
        }

        await _host.ProposeAsync(document, new ProposedEdits([new TextEdit(start, end - start, old.ToString())], "Revert this change") { Origin = "Source Control: your choice" }).ConfigureAwait(true);
    }

    // Stage: the index's text with this hunk taken from the document (its new lines), written to the index as the file's.
    private async Task StageHunkAsync(IEditorDocument document, DocumentGutter gutter, DiffHunk hunk)
    {
        if (_repository is not { } repository || document.FilePath is not { } path || gutter.Staged is not { } staged)
        {
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        var indexLines = GitParsers.LinesWithBreaks(staged);
        var newLines = new List<string>(hunk.NewCount);
        int newAt = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;
        for (int i = newAt; i < newAt + hunk.NewCount && i < snapshot.LineCount; i++)
        {
            var line = snapshot.GetLine(i);
            newLines.Add(snapshot.GetText(line.Start, line.EndIncludingBreak - line.Start));
        }

        int oldAt = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
        oldAt = Math.Clamp(oldAt, 0, indexLines.Count);
        if (oldAt == indexLines.Count && oldAt > 0 && !indexLines[^1].EndsWith('\n') && newLines.Count > 0)
        {
            indexLines[^1] += "\n";                               // lines added after a last line that had no break
        }

        indexLines.RemoveRange(oldAt, Math.Min(hunk.OldCount, indexLines.Count - oldAt));
        indexLines.InsertRange(oldAt, newLines);
        await Run("Staging the change", r => r.StageTextAsync(path, string.Concat(indexLines))).ConfigureAwait(true);
        ScheduleGutter(document, 0);
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---- generate the commit message ----

    // The staged diff (else every change, said so), trimmed to the budget by file, with the repository's recent subjects,
    // written by the first commit message provider (the AI plug-in) with a model already loaded, streamed into the box as an
    // editable draft; Stop ends it; nothing is committed. Nothing loaded: says so and offers to load the chat model.
    private async Task GenerateAsync(string? pick)
    {
        if (_repository is not { } repository || _state.Generating)
        {
            return;
        }

        var settings = Settings;
        string choice = pick ?? settings.CommitModel;
        if (FirstProvider() is not { } provider)
        {
            Update(_state with { MessageStatus = "Generating a message needs the AI plug-in (Craft.Ai), which is not loaded.", MessageTone = PanelTone.Warning });
            return;
        }

        var plan = provider.Plan(choice);
        if (plan.Model is null)
        {
            Update(_state with { MessageStatus = plan.Why ?? "No model is loaded.", MessageTone = PanelTone.Warning, LoadCommand = plan.LoadCommand });
            return;
        }

        var writing = _writing = new CancellationTokenSource();
        var activity = _host.Activities.Start($"Writing the commit message with {plan.Model}", ActivityKinds.Generation, cancellable: true, writing.Token);
        var token = activity.CancellationToken;
        string start = _draft;
        Update(_state with
        {
            Generating = true, LoadCommand = null, Error = null,
            MessageStatus = $"Writing with {plan.Model} ({plan.Role})…", MessageTone = PanelTone.Normal,
        });
        var written = new StringBuilder();
        var pending = new StringBuilder();
        var pendingGate = new Lock();
        bool scheduled = false;
        string? outcome = null;
        PanelTone tone = PanelTone.Normal;
        try
        {
            var (diffBytes, allChanges) = await repository.CommitDiffAsync(token).ConfigureAwait(true);
            if (diffBytes.Length == 0)
            {
                outcome = "There are no changes to describe.";
                tone = PanelTone.Warning;
                return;
            }

            int budget = settings.CommitDiffTokens;
            if (plan.ContextTokens > 0)
            {
                // The model's context holds the instructions, the examples, the changes and the reply.
                int reply = settings.CommitMaxTokens > 0 ? settings.CommitMaxTokens : 512;
                budget = Math.Max(256, Math.Min(budget, plan.ContextTokens - reply - 512));
            }

            var trimmed = await Task.Run(() => CommitMessages.Trim(diffBytes, budget), token).ConfigureAwait(true);
            IReadOnlyList<string> examples = settings.CommitStyle == "plain" ? [] : await repository.SubjectsAsync(settings.CommitExamples, token).ConfigureAwait(true);
            var request = new CommitMessageRequest(CommitMessages.Instructions(settings, examples.Count > 0, allChanges, trimmed.Summarised), trimmed.Text)
            {
                Examples = settings.CommitStyle == "conventional" ? [] : examples,
                Model = choice,
                MaxTokens = settings.CommitMaxTokens,
            };
            await Task.Run(async () =>
            {
                await foreach (var piece in provider.WriteAsync(request, token).ConfigureAwait(false))
                {
                    bool schedule;
                    lock (pendingGate)
                    {
                        pending.Append(piece);
                        schedule = !scheduled;
                        scheduled = true;
                    }

                    if (schedule)
                    {
                        // The pieces since the last UI turn go in one dispatch (never one redraw per token).
                        _host.Dispatch("git: the commit message's draft", () =>
                        {
                            string add;
                            lock (pendingGate)
                            {
                                add = pending.ToString();
                                pending.Clear();
                                scheduled = false;
                            }

                            written.Append(add);
                            ShowDraft(written.ToString(), streaming: true);
                        });
                    }
                }
            }, token).ConfigureAwait(true);
            lock (pendingGate)
            {
                written.Append(pending);
                pending.Clear();
            }

            string message = CommitMessages.Clean(written.ToString());
            ShowDraft(message, streaming: false);
            outcome = string.Create(CultureInfo.InvariantCulture,
                $"Written by {plan.Model} ({plan.Role}){(allChanges ? " from every change (nothing is staged)" : " from the staged changes")}{(trimmed.Summarised > 0 ? $", {trimmed.Summarised} large {(trimmed.Summarised == 1 ? "file" : "files")} summarised" : "")}. Edit it, then commit.");
            activity.Complete(outcome);
        }
        catch (OperationCanceledException)
        {
            string kept = CommitMessages.Clean(written.ToString());
            ShowDraft(kept.Length > 0 ? kept : start, streaming: false);
            outcome = "Stopped: what was written is kept as the draft.";
            activity.Dispose();
        }
        catch (Exception error) when (error is InvalidOperationException or GitException or IOException or NotSupportedException)
        {
            ShowDraft(start, streaming: false);
            outcome = error.Message;
            tone = PanelTone.Error;
            activity.Fail(error.Message, error);
        }
        finally
        {
            if (ReferenceEquals(_writing, writing))
            {
                _writing = null;
            }

            writing.Dispose();
            if (!_disposed)
            {
                Update(_state with { Generating = false, MessageStatus = outcome, MessageTone = tone });
            }
        }
    }

    // The draft into the box: a new version, so the box takes it (the user edits it once it is done).
    private void ShowDraft(string text, bool streaming)
    {
        if (_disposed)
        {
            return;
        }

        _draft = text;
        Update(_state with { Message = text, MessageVersion = _state.MessageVersion + 1, Generating = streaming || _state.Generating });
    }

    /// <summary>One open document's gutter: the work in flight, and what its marks were made from (for a click on one).</summary>
    private sealed class DocumentGutter
    {
        private CancellationTokenSource? _cancel;

        public Task Work { get; set; } = Task.CompletedTask;

        public string? Staged { get; set; }

        public TextDiff Diff { get; set; } = TextDiff.Empty;

        public IReadOnlyList<ConflictBlock> Conflicts { get; set; } = [];

        public IReadOnlyList<LineMark> Marks { get; set; } = [];

        public CancellationToken Start()
        {
            _cancel = new CancellationTokenSource();
            return _cancel.Token;
        }

        public void Cancel()
        {
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
        }
    }
}
