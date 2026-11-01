// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>A lint run's end, for the tests and the log: which linter, how many files, problems, why it did not run.</summary>
/// <param name="Linter">The linter's name.</param>
/// <param name="Files">The files linted (requested; none: a whole project).</param>
/// <param name="Problems">The diagnostics published.</param>
/// <param name="Why">Why it did not run or failed, or null.</param>
/// <param name="Cancelled">Whether a newer run (or Cancel) stopped it.</param>
public sealed record LintRunReport(string Linter, IReadOnlyList<string> Files, int Problems, string? Why, bool Cancelled);

/// <summary>
/// The lint plug-in in one host: the linters and the code actions registered, saves of linted files joined for
/// <c>delayMs</c> into one run per project (a newer run cancels the older and takes its files), each run through the
/// sandbox the gate chooses (asked once per workspace and program for a run on save), what it found published per file
/// (open or closed), the fixes kept per file for the text they were found in and offered as one diff, "Lint workspace",
/// "Set up ESLint" where a project has none, the notes said once per workspace. Its members run on the UI thread, the
/// linters' work off it; <see cref="ActionsFor"/> may be called off it.
/// </summary>
internal sealed class LintSession : IDisposable, ICodeActionProvider
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly IEditorHost _host;
    private readonly List<Action> _undo = [];
    private readonly Dictionary<string, ITextSnapshot?> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEditorNotification> _statuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _publishedBy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LintedFile> _linted = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private Dictionary<string, ITextSnapshot?> _inFlight = new(StringComparer.Ordinal);
    private CancellationTokenSource? _running;
    private int _batch;
    private bool _disposed;

    public LintSession(IEditorHost host)
    {
        _host = host;
        Settings = LintSettings.Read(host.Settings);
        Gate = new LintGate(host);
        ScratchFolder = Path.Combine(host.DataFolder ?? Path.GetTempPath(), "lint", "tmp");
        Part(() => Linters.Register(EslintLinter.Instance), () => Linters.Unregister(EslintLinter.LinterName));
        Part(() => Linters.Register(TslintLinter.Instance), () => Linters.Unregister(TslintLinter.LinterName));
        Part(() => CodeActionProviders.Register(this), () => CodeActionProviders.Unregister(LintPlugin.ProviderName));
        foreach (var command in Commands())
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        host.DocumentSaved += OnDocumentSaved;
        host.DocumentOpened += OnDocumentOpened;
        host.WorkspaceChanged += OnWorkspaceChanged;
        _undo.Add(() =>
        {
            host.DocumentSaved -= OnDocumentSaved;
            host.DocumentOpened -= OnDocumentOpened;
            host.WorkspaceChanged -= OnWorkspaceChanged;
        });
        Ready = LintNode.FindAsync(host);
    }

    /// <summary>The settings, as read on the UI thread last.</summary>
    public LintSettings Settings { get; private set; }

    /// <summary>The sandbox gate and the approvals.</summary>
    public LintGate Gate { get; }

    /// <summary>Where the linters write their reports (the user's data folder, never the workspace).</summary>
    public string ScratchFolder { get; }

    /// <summary>Completes once Node was looked for.</summary>
    public Task Ready { get; }

    /// <summary>The runs ended so far (each project's run, cancelled or not), newest last.</summary>
    public List<LintRunReport> Reports { get; } = [];

    /// <summary>Raised on the UI thread after each run ended.</summary>
    public event EventHandler<LintRunReport>? RunEnded;

    /// <inheritdoc />
    public string Name => LintPlugin.ProviderName;

    /// <inheritdoc />
    public string Summary => "a linter's fixes and suggestions for its problems, and every fix of a file at once, each a diff the user accepts";

    // ---- on save ----

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        Settings = LintSettings.Read(_host.Settings);
        if (e.Document.FilePath is not { } file)
        {
            return;
        }

        string name = Path.GetFileName(file);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("eslint.config.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".eslintrc", StringComparison.OrdinalIgnoreCase) || name.StartsWith("tslint.", StringComparison.OrdinalIgnoreCase))
        {
            LintProjects.Forget();
        }

        if (Settings.Linter == "off" || Settings.RunOn != "save" || _host.WorkspaceFolder is not { } root || !Inside(root, file) || !Linted(file))
        {
            return;
        }

        _pending[Path.GetFullPath(file)] = e.Document.Document.Buffer.Current;
        int batch = ++_batch;
        _ = Task.Delay(Settings.DelayMilliseconds).ContinueWith(_ => _host.Dispatch("lint: saves joined", () =>
        {
            if (batch == _batch && !_disposed)
            {
                StartSaved();
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Lints the saved files waiting now (the batch's end; the tests call it to skip the pause).</summary>
    public Task StartSaved()
    {
        if (_pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        var files = new Dictionary<string, ITextSnapshot?>(_pending, StringComparer.Ordinal);
        _pending.Clear();
        if (_running is { } older)
        {
            // A newer run cancels the older; the older's files that this batch does not hold join it.
            _ = older.CancelAsync();
            foreach (var (path, snapshot) in _inFlight)
            {
                files.TryAdd(path, snapshot);
            }
        }

        var cancel = new CancellationTokenSource();
        _running = cancel;
        _inFlight = files;
        return RunFilesAsync(files, automatic: true, cancel);
    }

    private async Task RunFilesAsync(Dictionary<string, ITextSnapshot?> files, bool automatic, CancellationTokenSource cancel)
    {
        try
        {
            if (_host.WorkspaceFolder is not { } root)
            {
                return;
            }

            await Ready.ConfigureAwait(true);
            var settings = Settings;
            EslintLinter.Instance.AllowGlobal = TslintLinter.Instance.AllowGlobal = settings.AllowGlobal;
            List<(ILinter Linter, LinterProject Project, List<string> Files)> groups;
            List<string> unconfigured;
            try
            {
                (groups, unconfigured) = await Task.Run(() => GroupAsync(files.Keys, root, settings, cancel.Token), cancel.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (unconfigured.Count > 0 && settings.Linter is "auto" or "eslint")
            {
                OfferSetup(root);
            }

            foreach (var (linter, project, paths) in groups)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                await RunProjectAsync(linter, project, paths, files, automatic, root, cancel.Token).ConfigureAwait(true);
            }
        }
        finally
        {
            if (ReferenceEquals(_running, cancel))
            {
                _running = null;
                _inFlight = new Dictionary<string, ITextSnapshot?>(StringComparer.Ordinal);
            }

            cancel.Dispose();
        }
    }

    // Off the UI thread: each file's linter (the first registered one the setting picks whose configuration is there:
    // ESLint before TSLint) and project; the files no linter is configured for.
    private static async Task<(List<(ILinter, LinterProject, List<string>)>, List<string>)> GroupAsync(IEnumerable<string> files, string root, LintSettings settings, CancellationToken cancellationToken)
    {
        var groups = new List<(ILinter Linter, LinterProject Project, List<string> Files)>();
        var unconfigured = new List<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            LinterProject? found = null;
            ILinter? by = null;
            foreach (var linter in Linters.ForFile(file))
            {
                if (!settings.Picks(linter.Name) || linter.Unavailable is not null)
                {
                    continue;
                }

                if (await linter.DetectAsync(file, root, cancellationToken).ConfigureAwait(false) is { } project)
                {
                    (found, by) = (project, linter);
                    break;
                }
            }

            if (found is null || by is null)
            {
                unconfigured.Add(file);
                continue;
            }

            int at = groups.FindIndex(g => ReferenceEquals(g.Linter, by) && string.Equals(g.Project.Folder, found.Folder, StringComparison.Ordinal));
            if (at < 0)
            {
                groups.Add((by, found, [file]));
            }
            else
            {
                groups[at].Files.Add(file);
            }
        }

        return (groups, unconfigured);
    }

    // One project's run: its note said once, the gate's sandbox, an activity with Cancel, then what it found published.
    private async Task RunProjectAsync(ILinter linter, LinterProject project, List<string> paths, IReadOnlyDictionary<string, ITextSnapshot?> snapshots, bool automatic,
        string root, CancellationToken cancellationToken)
    {
        if (project.Note is { } note && _said.Add("note|" + project.Folder + "|" + linter.Name))
        {
            _host.Notify($"{linter.Title}: {Relative(root, project.Folder)}", note);
        }

        if (!project.Ready)
        {
            Status(linter, project.Problem!);
            End(new LintRunReport(linter.Name, paths, 0, project.Problem, false));
            return;
        }

        int count = paths.Count;
        string what = count == 0 ? $"every file of {Relative(root, project.Folder)}" : count == 1 ? Path.GetFileName(paths[0]) : string.Create(Culture, $"{count} files");
        var (sandbox, why) = await Gate.SandboxAsync(root, linter.Name, linter.Title, automatic,
            $"The lint plug-in runs the project's {linter.Title}{(project.Version is { } v ? " " + v : "")} on each JavaScript or TypeScript file you save here, and shows what it finds in the editor and in Problems.",
            cancellationToken).ConfigureAwait(true);
        if (sandbox is null)
        {
            if (!automatic)
            {
                _host.Notify($"{linter.Title}: not run", why ?? "not run", NotificationSeverity.Warning);
            }

            End(new LintRunReport(linter.Name, paths, 0, why, false));
            return;
        }

        using var activity = _host.Activities.Start($"{linter.Title}: {what}", ActivityKinds.Command, cancellable: true, cancellationToken);
        var clock = Stopwatch.StartNew();
        var settings = Settings;
        var request = new LintRequest(project, paths, sandbox,
            LintGate.Limits(root, $"{linter.Title} lints {what} ({Path.GetFileName(project.Config ?? project.Folder)})", settings.TimeLimit), ScratchFolder);
        LintResult result;
        try
        {
            result = await Task.Run(() => linter.LintAsync(request, activity.CancellationToken), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            activity.Complete("cancelled: a newer run took its files");
            End(new LintRunReport(linter.Name, paths, 0, null, true));
            return;
        }

        _host.Record("lint.run", clock.Elapsed.TotalMilliseconds, "ms");
        if (activity.CancellationToken.IsCancellationRequested || _disposed)
        {
            activity.Complete("cancelled");
            End(new LintRunReport(linter.Name, paths, 0, null, true));
            return;
        }

        if (result.Problem is { } problem)
        {
            activity.Fail(problem);
            Status(linter, problem);
            End(new LintRunReport(linter.Name, paths, 0, problem, false));
            return;
        }

        ClearStatus(linter);
        int problems = Publish(linter, project, result, paths, snapshots);
        activity.Complete(string.Create(Culture, $"{problems} problem{(problems == 1 ? "" : "s")} in {clock.Elapsed.TotalSeconds:0.0} s"));
        End(new LintRunReport(linter.Name, paths, problems, null, false));
    }

    // What a run found goes to the files: each linted file's list (a clean file's empty one takes the old away), and for a
    // whole project, the files it listed before and no longer does. The fixes are kept for the text they were found in.
    private int Publish(ILinter linter, LinterProject project, LintResult result, List<string> requested, IReadOnlyDictionary<string, ITextSnapshot?> snapshots)
    {
        if (!_publishedBy.TryGetValue(linter.Name, out var published))
        {
            published = new HashSet<string>(StringComparer.Ordinal);
            _publishedBy[linter.Name] = published;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int problems = 0;
        foreach (var file in result.Files)
        {
            string path = Path.GetFullPath(file.Path);
            seen.Add(path);
            var snapshot = snapshots.TryGetValue(path, out var saved) ? saved : CleanSnapshotOf(path);
            _host.PublishDiagnostics(path, linter.Name, snapshot, file.Diagnostics);
            problems += file.Diagnostics.Count;
            if (file.Diagnostics.Count > 0)
            {
                published.Add(path);
            }
            else
            {
                published.Remove(path);
            }

            lock (_gate)
            {
                _linted[path] = new LintedFile(linter, snapshot, file.Diagnostics, file.Fixes);
            }
        }

        // A requested file the report did not list (ignored by the configuration), or a whole project's file that is gone.
        var stale = requested.Count > 0
            ? requested.Select(Path.GetFullPath).Where(p => !seen.Contains(p)).ToList()
            : published.Where(p => !seen.Contains(p) && Inside(project.Folder, p)).ToList();
        foreach (var path in stale)
        {
            _host.PublishDiagnostics(path, linter.Name, null, []);
            published.Remove(path);
            lock (_gate)
            {
                _linted.Remove(path);
            }
        }

        return problems;
    }

    private void End(LintRunReport report)
    {
        Reports.Add(report);
        if (report.Why is { } why && !report.Cancelled)
        {
            _host.Log(EditorLogLevel.Information, $"lint: {report.Linter}: {why}");
        }

        RunEnded?.Invoke(this, report);
    }

    // ---- the commands ----

    private IEnumerable<IEditorCommand> Commands() =>
    [
        new EditorCommand(LintPlugin.LintFileCommand, "Lint: Lint this file", (_, _) => LintActiveAsync(), _ => ActiveLinted() is not null)
            { Reason = _ => "Open a saved JavaScript or TypeScript file of the workspace." },
        new EditorCommand(LintPlugin.LintWorkspaceCommand, "Lint: Lint workspace", (_, token) => LintWorkspaceAsync(token), _ => _host.WorkspaceFolder is not null)
            { Reason = _ => "Open a folder first." },
        new EditorCommand(LintPlugin.FixFileCommand, "Lint: Fix ESLint problems in file", (_, _) => FixFileAsync(), _ => FixAll(_host.ActiveDocument) is not null)
            { Reason = _ => "No fixable problem in this file's last lint (save it to lint it again)." },
        new EditorCommand(LintPlugin.SetupCommand, "Lint: Set up ESLint", (_, token) => SetupAsync(token), _ => _host.WorkspaceFolder is not null)
            { Reason = _ => "Open a folder first." },
        new EditorCommand(LintPlugin.ForgetCommand, "Lint: Forget this workspace's approvals", (_, _) => ForgetAsync(), _ => _host.WorkspaceFolder is not null)
            { Reason = _ => "Open a folder first." },
    ];

    private string? ActiveLinted() =>
        _host.ActiveDocument?.FilePath is { } path && _host.WorkspaceFolder is { } root && Inside(root, path) && Linted(path) ? Path.GetFullPath(path) : null;

    /// <summary>"Lint: Lint this file": the active file as saved (asked through the sandbox; no approval needed).</summary>
    public Task LintActiveAsync()
    {
        Settings = LintSettings.Read(_host.Settings);
        if (ActiveLinted() is not { } path)
        {
            return Task.CompletedTask;
        }

        var document = _host.ActiveDocument!;
        var files = new Dictionary<string, ITextSnapshot?>(StringComparer.Ordinal) { [path] = document.Document.IsDirty ? null : document.Document.Buffer.Current };
        var cancel = new CancellationTokenSource();
        return RunFilesAsync(files, automatic: false, cancel);
    }

    /// <summary>
    /// "Lint: Lint workspace": every project of the workspace (each folder with a configuration, the ignored folders
    /// never walked) linted whole, so Problems lists the closed files too.
    /// </summary>
    public async Task LintWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        Settings = LintSettings.Read(_host.Settings);
        if (_host.WorkspaceFolder is not { } root)
        {
            return;
        }

        await Ready.ConfigureAwait(true);
        var settings = Settings;
        EslintLinter.Instance.AllowGlobal = TslintLinter.Instance.AllowGlobal = settings.AllowGlobal;
        var projects = await Task.Run(async () =>
        {
            var found = new List<(ILinter Linter, LinterProject Project)>();
            var configs = LintProjects.EslintFlat.Concat(LintProjects.EslintLegacy).Append("tslint.json").Append("tslint.yaml").ToList();
            foreach (var folder in LintProjects.FoldersWith(root, configs, settings.Ignore))
            {
                foreach (var linter in Linters.All)
                {
                    if (!settings.Picks(linter.Name) || linter.Unavailable is not null || found.Any(f => f.Project.Folder == folder))
                    {
                        continue;
                    }

                    if (await linter.DetectAsync(folder, root, cancellationToken).ConfigureAwait(false) is { } project && project.Folder == folder)
                    {
                        found.Add((linter, project));
                    }
                }
            }

            return found;
        }, cancellationToken).ConfigureAwait(true);
        if (projects.Count == 0)
        {
            if (settings.Linter is "auto" or "eslint")
            {
                OfferSetup(root, force: true);
            }

            return;
        }

        var empty = new Dictionary<string, ITextSnapshot?>(StringComparer.Ordinal);
        foreach (var (linter, project) in projects)
        {
            await RunProjectAsync(linter, project, [], empty, automatic: false, root, cancellationToken).ConfigureAwait(true);
        }
    }

    // ---- fixes ----

    /// <inheritdoc />
    public IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Current(context.Document) is not { } linted)
        {
            return [];
        }

        var actions = new List<CodeAction>();
        var document = context.Document;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Source != linted.Linter.Name)
            {
                continue;
            }

            int index = linted.IndexOf(diagnostic);
            if (index < 0)
            {
                continue;
            }

            var own = linted.Fixes.Where(f => f.Diagnostic == index && !f.Suggestion).ToList();
            if (own.Count > 0)
            {
                actions.Add(new CodeAction($"lint.fix:{index}", own[0].Title, CodeActionKinds.Fix, _ => ProposeAsync(document, linted, own, own[0].Title)));
            }

            foreach (var suggestion in linted.Fixes.Where(f => f.Diagnostic == index && f.Suggestion))
            {
                actions.Add(new CodeAction($"lint.suggest:{index}:{suggestion.Start}", suggestion.Title, CodeActionKinds.Fix,
                    _ => ProposeAsync(document, linted, [suggestion], suggestion.Title)));
            }
        }

        if (FixAll(document) is { } all)
        {
            string title = FixAllTitle(linted.Linter);
            actions.Add(new CodeAction("lint.fixAll", title, CodeActionKinds.Fix, _ => ProposeAsync(document, linted, all, title)));
        }

        return actions;
    }

    /// <summary>"Fix ESLint problems in file".</summary>
    public static string FixAllTitle(ILinter linter) => $"Fix {(linter ?? throw new ArgumentNullException(nameof(linter))).Title} problems in file";

    /// <summary>"Lint: Fix ESLint problems in file": every automatic fix of the active file, as one diff.</summary>
    public Task<bool> FixFileAsync()
    {
        var document = _host.ActiveDocument;
        if (document is null || Current(document) is not { } linted || FixAll(document) is not { } all)
        {
            return Task.FromResult(false);
        }

        return ProposeAsync(document, linted, all, FixAllTitle(linted.Linter));
    }

    // The fixes of the file's last lint while its text is still the one linted: every automatic fix in order, those that
    // overlap one before them left out (ESLint's own rule for one pass); null when there is none.
    private List<LintFix>? FixAll(IEditorDocument? document)
    {
        if (document is null || Current(document) is not { } linted)
        {
            return null;
        }

        var chosen = new List<LintFix>();
        int end = -1, lastStart = -1;
        foreach (var fix in linted.Fixes.Where(f => !f.Suggestion).OrderBy(f => f.Start).ThenBy(f => f.Length))
        {
            if (fix.Start < end || fix.Start == lastStart)
            {
                continue;
            }

            chosen.Add(fix);
            end = fix.End;
            lastStart = fix.Start;
        }

        return chosen.Count > 0 ? chosen : null;
    }

    private LintedFile? Current(IEditorDocument document)
    {
        if (document.FilePath is not { } path)
        {
            return null;
        }

        LintedFile? linted;
        lock (_gate)
        {
            _linted.TryGetValue(Path.GetFullPath(path), out linted);
        }

        var current = document.Document.Buffer.Current;
        return linted is { Snapshot: { } of } && ReferenceEquals(of.Buffer, document.Document.Buffer) && of.Version == current.Version ? linted : null;
    }

    // The fixes as one diff the user accepts (one undo step); a text changed since the lint takes none.
    private Task<bool> ProposeAsync(IEditorDocument document, LintedFile linted, IReadOnlyList<LintFix> fixes, string title)
    {
        if (Current(document) is null)
        {
            _host.Notify(title, "The file changed since it was linted: save it to lint it again, then fix.", NotificationSeverity.Warning);
            return Task.FromResult(false);
        }

        int length = document.Document.Buffer.Current.Length;
        var edits = fixes.OrderBy(f => f.Start).Where(f => f.Start >= 0 && f.End <= length).Select(f => new TextEdit(f.Start, f.Length, f.Text)).ToList();
        return _host.ProposeAsync(document, new ProposedEdits(edits, title) { Origin = linted.Linter.Title });
    }

    // ---- set up ESLint ----

    private void OfferSetup(string root, bool force = false)
    {
        if (!force && (!_said.Add("setup|" + root) || !File.Exists(Path.Combine(root, "package.json"))))
        {
            return;
        }

        _statuses.TryGetValue("setup", out var old);
        old?.Dispose();
        _statuses["setup"] = _host.ShowStatus("Lint", "No ESLint configuration in this project. Set up ESLint runs npm init @eslint/config@latest in a terminal: it asks how to lint, writes eslint.config.* and installs ESLint.",
            NotificationSeverity.Information, [new NotificationAction("Set up ESLint", token => SetupAsync(token), Primary: true)]);
    }

    /// <summary>
    /// "Lint: Set up ESLint": <c>npm init @eslint/config@latest</c> in the workspace, in a terminal tab that takes the
    /// keyboard, through the sandbox (asked first); its end said, the projects read again after it.
    /// </summary>
    public async Task SetupAsync(CancellationToken cancellationToken = default)
    {
        Settings = LintSettings.Read(_host.Settings);
        if (_host.WorkspaceFolder is not { } root)
        {
            return;
        }

        await Ready.ConfigureAwait(true);
        if (LintNode.Install is not { Ready: true } install)
        {
            _host.Notify("Set up ESLint: not run", LintNode.Install?.Problem ?? LintNode.Problem ?? "Node.js and npm were not found.", NotificationSeverity.Warning);
            return;
        }

        var limits = LintGate.Limits(root, "Set up ESLint: npm init @eslint/config@latest asks how to lint this project, writes eslint.config.* and installs ESLint", Timeout.InfiniteTimeSpan);
        var (sandbox, why) = await Gate.SandboxAsync(root, "npm", "npm", automatic: false, "", cancellationToken).ConfigureAwait(true);
        string? refusal = sandbox is null ? why : sandbox.CheckStart(limits);
        if (sandbox is null || refusal is not null || _host.Terminals is null)
        {
            _host.Notify("Set up ESLint: not run", refusal ?? "This window has no terminal to take the questions' answers.", NotificationSeverity.Warning);
            return;
        }

        using var activity = _host.Activities.Start("Set up ESLint", ActivityKinds.Command, cancellable: true, cancellationToken);
        CommandStart start;
        try
        {
            start = await Task.Run(() => sandbox.StartAsync([install.Node!, install.NpmCli!, "init", "@eslint/config@latest"], root, limits, activity.CancellationToken).AsTask(),
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            activity.Complete("cancelled");
            return;
        }

        if (start.Refusal is { } refused)
        {
            activity.Complete("not run");
            _host.Notify("Set up ESLint: not run", refused);
            return;
        }

        await using var session = start.Session!;
        using var stop = activity.CancellationToken.Register(session.Stop);
        var tab = _host.Terminals.OpenProgram(new EditorProgramRequest("Lint: set up ESLint", session.Input, session.Output)
        {
            Error = session.Error,
            Stop = session.Stop,
            Folder = root,
            Header = "\e[1m$ npm init @eslint/config@latest\e[0m\n",
        });
        if (tab is null)
        {
            session.Stop();
            activity.Complete("not run");
            return;
        }

        tab.Show();
        var result = await session.Completion.ConfigureAwait(true);
        tab.End(result.Succeeded ? "\n\e[32mESLint is set up: saved files are linted now.\e[0m\n" : $"\n\e[33mnpm init ended (exit code {result.ExitCode}).\e[0m\n", result.ExitCode ?? -1);
        LintProjects.Forget();
        if (result.Succeeded)
        {
            activity.Complete("ESLint is set up");
            if (_statuses.Remove("setup", out var offer))
            {
                offer.Dispose();
            }
        }
        else
        {
            activity.Fail(string.Create(Culture, $"exit code {result.ExitCode}"));
        }
    }

    private async Task ForgetAsync()
    {
        if (_host.WorkspaceFolder is not { } root)
        {
            return;
        }

        int taken = await Gate.ForgetAsync(root).ConfigureAwait(true);
        _host.Notify("Lint", taken == 0 ? "Nothing was allowed to run on its own in this workspace." : "ESLint, TSLint and Prettier will ask again before they run on their own here.");
    }

    // ---- the workspace, the documents ----

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
    {
        LintProjects.Forget();
        if (e.Kind == WorkspaceChangeKind.Refreshed)
        {
            return;
        }

        _running?.Cancel();
        _pending.Clear();
        _said.Clear();
        foreach (var name in _publishedBy.Keys)
        {
            _host.ClearDiagnostics(name);
        }

        _publishedBy.Clear();
        lock (_gate)
        {
            _linted.Clear();
        }

        foreach (var status in _statuses.Values)
        {
            status.Dispose();
        }

        _statuses.Clear();
    }

    // A file linted while it was closed: its fixes belong to the text it opens with (read from the disk that was linted).
    private void OnDocumentOpened(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is not { } path || e.Document.Document.IsDirty)
        {
            return;
        }

        lock (_gate)
        {
            string full = Path.GetFullPath(path);
            if (_linted.TryGetValue(full, out var linted) && linted.Snapshot is null)
            {
                _linted[full] = linted with { Snapshot = e.Document.Document.Buffer.Current };
            }
        }
    }

    private ITextSnapshot? CleanSnapshotOf(string path)
    {
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } file && string.Equals(Path.GetFullPath(file), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return document.Document.IsDirty ? null : document.Document.Buffer.Current;
            }
        }

        return null;
    }

    private void Status(ILinter linter, string text)
    {
        if (_statuses.TryGetValue(linter.Name, out var status))
        {
            status.Update(text, NotificationSeverity.Warning);
            return;
        }

        _statuses[linter.Name] = _host.ShowStatus(linter.Title, text, NotificationSeverity.Warning,
            [new NotificationAction("Lint workspace", token => LintWorkspaceAsync(token), Primary: true, Dismisses: false)]);
    }

    private void ClearStatus(ILinter linter)
    {
        if (_statuses.Remove(linter.Name, out var status))
        {
            status.Dispose();
        }
    }

    private bool Linted(string path) => Linters.ForFile(path).Any(l => Settings.Picks(l.Name));

    private static bool Inside(string folder, string path)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(top, comparison) && (full.Length == top.Length || full[top.Length] is '/' or '\\');
    }

    private static string Relative(string root, string folder)
    {
        string relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
        return relative == "." ? "the workspace" : relative;
    }

    private void Part(Action add, Action remove)
    {
        add();
        _undo.Add(remove);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running?.Cancel();
        foreach (var name in _publishedBy.Keys)
        {
            _host.ClearDiagnostics(name);
        }

        foreach (var status in _statuses.Values)
        {
            status.Dispose();
        }

        _statuses.Clear();
        for (int i = _undo.Count - 1; i >= 0; i--)
        {
            _undo[i]();
        }

        _undo.Clear();
    }

    /// <summary>A file's last lint: the linter, the text it was found in (null until known), its diagnostics and fixes.</summary>
    internal sealed record LintedFile(ILinter Linter, ITextSnapshot? Snapshot, IReadOnlyList<FileDiagnostic> Diagnostics, IReadOnlyList<LintFix> Fixes)
    {
        /// <summary>The index of <paramref name="diagnostic"/> (as the editor holds it, in the same text) among <see cref="Diagnostics"/>, or -1.</summary>
        public int IndexOf(EditorDiagnostic diagnostic)
        {
            if (Snapshot is not { } snapshot)
            {
                return -1;
            }

            for (int i = 0; i < Diagnostics.Count; i++)
            {
                var placed = Diagnostics[i].In(snapshot, Linter.Name);
                if (placed.Start == diagnostic.Start && placed.Length == diagnostic.Length && string.Equals(placed.Message, diagnostic.Message, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
