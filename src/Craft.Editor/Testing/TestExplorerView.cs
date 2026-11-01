// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Editor.Run;

namespace Craft.Editor.Testing;

/// <summary>
/// The Test explorer (X part 9, item 18), the rail's Testing: the workspace's tests as every registered
/// <see cref="ITestProvider"/> finds them off the UI thread (the C# plug-in's "dotnet": xUnit, NUnit and MSTest in the
/// syntax, nothing built or run to list them), joined with the test projects the Run view's providers find, as a tree of
/// project › class › method with Run all, Run failed, a project's, a class's and a test's Run; each test's last result
/// (passed, failed, skipped, its time) in the tree and in the gutter of its file (▶ to run it, ✓ or ✗ after), a failure's
/// message and stack with its <c>file:line</c> frames clickable, and the failures in Problems. A run is the provider's
/// command (<c>dotnet test --filter</c>) through the Run view (<see cref="RunView.RunCommandAsync(string, RunCommand, RunActions, Action{string}?, Func{double?}?, CancellationToken)"/>: the user's approval,
/// the registered sandbox, a terminal tab, an activity with Cancel), its results read from the results file (a TRX) or
/// the console's lines off the UI thread. Headless; every member is called on the UI thread.
/// </summary>
public sealed class TestExplorerView : IEditorViewPanel, IDisposable
{
    /// <summary>Its panel id (and its rail item's).</summary>
    public const string PanelId = "craft.tests";

    /// <summary>The source of its gutter marks (<see cref="IEditorHost.SetLineMarks"/>).</summary>
    public const string MarkSource = "tests";

    /// <summary>A check (passed).</summary>
    public const string PassedIcon = "M5 12.5l4.5 4.5L19 7.5";

    /// <summary>A cross (failed).</summary>
    public const string FailedIcon = "M7 7l10 10M17 7L7 17";

    /// <summary>A ring (skipped).</summary>
    public const string SkippedIcon = "M12 5a7 7 0 1 0 0.01 0z";

    /// <summary>A small dot (not run).</summary>
    public const string NotRunIcon = "M12 10.5a1.5 1.5 0 1 0 0.01 0z";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly EditorHost _host;
    private readonly RunView _run;
    private readonly Func<string?> _root;
    private readonly Func<CraftOptions> _options;
    private readonly Dictionary<string, TestResult> _results = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];
    private List<TestItem> _tests = [];
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private IReadOnlyList<RunProblemFile> _problemFiles = [];
    private CancellationTokenSource? _finding;
    private TaskCompletionSource _found = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _filter = "";
    private string? _selected;
    private string? _note;
    private bool _discovering;
    private int _version;
    private bool _disposed;

    /// <summary>The explorer over <paramref name="host"/>, running through <paramref name="run"/>, for the folder <paramref name="root"/> gives.</summary>
    public TestExplorerView(EditorHost host, RunView run, Func<string?> root, Func<CraftOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host.DocumentSaved += OnDocumentSaved;
        _host.DocumentOpened += OnDocumentOpened;
        _host.LineMarkClicked += OnLineMarkClicked;
        _run.Changed += OnRunChanged;
        TestProviders.Changed += OnProvidersChanged;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => "Testing";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public string? Badge => FailedCount > 0 ? FailedCount.ToString(Culture) : null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Raised (on the UI thread) when <see cref="ProblemFiles"/> changed.</summary>
    public event EventHandler? ProblemsChanged;

    /// <summary>The tests found, providers in their registry's order, each provider's in its files' order.</summary>
    public IReadOnlyList<TestItem> Tests => _tests;

    /// <summary>The last result of each test run (by <see cref="KeyOf"/>).</summary>
    public IReadOnlyDictionary<string, TestResult> Results => _results;

    /// <summary>The last run's failures by file, placed in each file's text (what Problems shows, as "File.cs (tests)").</summary>
    public IReadOnlyList<RunProblemFile> ProblemFiles => _problemFiles;

    /// <summary>Completes when the discovery asked last has its tests in.</summary>
    public Task Found => _found.Task;

    /// <summary>The tests whose last run failed.</summary>
    public int FailedCount => _results.Values.Count(r => r.Outcome == TestOutcome.Failed);

    /// <summary>A test's key: its provider and its id.</summary>
    public static string KeyOf(TestItem test) => (test ?? throw new ArgumentNullException(nameof(test))).Provider + ":" + test.Id;

    /// <summary>How a test stands now: running, its last result, or not run.</summary>
    public TestOutcome OutcomeOf(TestItem test)
    {
        string key = KeyOf(test);
        return _running.Contains(key) ? TestOutcome.Running : _results.TryGetValue(key, out var result) ? result.Outcome : TestOutcome.NotRun;
    }

    /// <summary>The workspace changed: its tests are found again.</summary>
    public void WorkspaceChanged()
    {
        _results.Clear();
        _problemFiles = [];
        ProblemsChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshAsync();
    }

    /// <summary>Finds the workspace's tests again (off the UI thread); completes when they are in.</summary>
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
            _tests = [];
            _discovering = false;
            Changed2();
            done.TrySetResult();
            return done.Task;
        }

        _discovering = true;
        Publish();
        var ignore = _options().WorkspaceIgnore;
        var providers = TestProviders.All;
        // The owner, 2026-11-01: finding the tests shows in the status bar: listing the files (no share known yet), then each
        // provider's discovery, the providers done of all.
        var activity = _host.Activities.Start("Tests: finding the tests", ActivityKinds.Search, cancellable: true, cancel.Token, keep: false);
        _ = Task.Run(() =>
        {
            var problems = new List<string>();
            var tests = new List<TestItem>();
            try
            {
                activity.Report(null, "listing the workspace's files");
                var files = WorkspaceFiles.Enumerate(root, ignore, useIgnoreFiles: true, null, null, activity.CancellationToken).Select(path => WorkspaceFiles.Relative(root, path)).ToList();
                var workspace = new RunWorkspace(root, files);
                for (int i = 0; i < providers.Count; i++)
                {
                    var provider = providers[i];
                    activity.Report((double)i / providers.Count, string.Create(Culture, $"{provider.Title}: {files.Count:N0} files"));
                    try
                    {
                        tests.AddRange(provider.Discover(workspace, activity.CancellationToken));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
#pragma warning disable CA1031 // A provider that fails is named in the view; the others' tests still show.
                    catch (Exception error)
#pragma warning restore CA1031
                    {
                        problems.Add($"{provider.Title} ({provider.Name}) could not look for tests: {error.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                activity.Complete("cancelled");
                return;
            }

            activity.Complete(string.Create(Culture, $"{tests.Count} tests"));
            _host.Dispatch("tests: what the workspace tests", () =>
            {
                if (cancel.IsCancellationRequested || _disposed)
                {
                    return;
                }

                _tests = tests;
                _problems.Clear();
                _problems.AddRange(problems);
                var keys = new HashSet<string>(tests.Select(KeyOf), StringComparer.Ordinal);
                foreach (var gone in _results.Keys.Where(k => !keys.Contains(k)).ToList())
                {
                    _results.Remove(gone);
                }

                _discovering = false;
                Changed2();
                UpdateMarks();
                done.TrySetResult();
            });
        }, CancellationToken.None).ContinueWith(_ => activity.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return done.Task;
    }

    /// <summary>Runs every test found (each project's, one run a project).</summary>
    public Task RunAllAsync(CancellationToken cancellationToken = default) => RunAsync(_tests, all: true, cancellationToken);

    /// <summary>Runs the tests whose last run failed.</summary>
    public Task RunFailedAsync(CancellationToken cancellationToken = default) =>
        RunAsync([.. _tests.Where(t => OutcomeOf(t) == TestOutcome.Failed)], all: false, cancellationToken);

    /// <summary>
    /// Runs <paramref name="tests"/>: one run per provider and project (the project's every test when <paramref name="all"/>),
    /// each through the Run view, the tests marked running, their results read when it ends.
    /// </summary>
    public async Task RunAsync(IReadOnlyList<TestItem> tests, bool all = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tests);
        if (_root() is not { } root || tests.Count == 0)
        {
            return;
        }

        var failures = new List<(TestItem Test, TestResult Result)>();
        bool ran = false;
        foreach (var group in tests.GroupBy(t => (t.Provider, t.Project)).ToList())
        {
            if (TestProviders.Find(group.Key.Provider) is not { } provider)
            {
                _note = $"Test provider '{group.Key.Provider}' is not registered (registered: {string.Join(", ", TestProviders.Names)}).";
                Publish();
                continue;
            }

            var chosen = group.ToList();
            bool whole = all || chosen.Count == _tests.Count(t => t.Provider == group.Key.Provider && t.Project == group.Key.Project);
            string folder = Path.Combine(root, ".craft", "test-results");
            string results = Path.Combine(folder, "run-" + Guid.NewGuid().ToString("N")[..12] + ".trx");
            var request = new TestRunRequest(root, group.Key.Project, whole ? [] : chosen, results);
            var command = provider.Command(request);
            string name = ProjectTitle(group.Key.Project) + (whole ? "" : chosen.Count == 1 ? " · " + chosen[0].Method : string.Create(Culture, $" · {chosen.Count} tests"));
            if (command.Problem is { } cannot)
            {
                _note = $"{name}: {cannot}";
                Publish();
                continue;
            }

            foreach (var test in chosen)
            {
                _running.Add(KeyOf(test));
            }

            _note = null;
            Changed2();
            UpdateMarks();
            var output = new System.Text.StringBuilder();
            RunOutcome outcome;
            // The owner, 2026-11-01: the run's share is the results seen of the tests it runs, when the provider can tell a
            // result line; else none (the run's bar moves without a share).
            int total = whole ? _tests.Count(t => t.Provider == group.Key.Provider && t.Project == group.Key.Project) : chosen.Count;
            int seen = 0;
            Func<double?>? share = total > 0 ? () => Math.Min(1, (double)Volatile.Read(ref seen) / total) : null;
            try
            {
                Directory.CreateDirectory(folder);
                outcome = await _run.RunCommandAsync("Tests: " + name, command, RunActions.Test, line =>
                {
                    if (provider.IsResultLine(line))
                    {
                        Interlocked.Increment(ref seen);
                    }

                    lock (output)
                    {
                        output.Append(line).Append('\n');
                    }
                }, share, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                outcome = new RunOutcome(RunEnd.NotStarted, null, TimeSpan.Zero, [], e.Message);
            }
            finally
            {
                foreach (var test in chosen)
                {
                    _running.Remove(KeyOf(test));
                }
            }

            string text;
            lock (output)
            {
                text = output.ToString();
            }

            var read = outcome.End == RunEnd.NotStarted ? [] : await Task.Run(() =>
            {
                try
                {
                    return provider.ReadResults(request, text, CancellationToken.None);
                }
                finally
                {
                    try
                    {
                        File.Delete(results);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }, CancellationToken.None).ConfigureAwait(true);
            ran |= outcome.End != RunEnd.NotStarted;
            var byId = new Dictionary<string, TestResult>(StringComparer.Ordinal);
            foreach (var result in read)
            {
                byId[result.Id] = result;
            }

            foreach (var test in whole ? _tests.Where(t => t.Provider == group.Key.Provider && t.Project == group.Key.Project).ToList() : chosen)
            {
                string key = KeyOf(test);
                if (byId.TryGetValue(test.Id, out var result))
                {
                    _results[key] = result;
                    if (result.Outcome == TestOutcome.Failed)
                    {
                        failures.Add((test, result));
                    }
                }
                else if (outcome.End != RunEnd.NotStarted)
                {
                    _results.Remove(key);                     // the run ended without it (stopped, or the build failed)
                }
            }

            if (outcome.End == RunEnd.NotStarted)
            {
                _note = $"{name}: {outcome.Reason ?? "it did not run"}";
            }
            else if (read.Count == 0)
            {
                _note = outcome.End == RunEnd.Stopped ? $"{name}: stopped before any result." : $"{name}: no result came (exit code {outcome.ExitCode?.ToString(Culture) ?? "none"}; see its terminal tab).";
            }

            Changed2();
            UpdateMarks();
        }

        if (ran)
        {
            await ShowFailuresAsync(failures, root).ConfigureAwait(true);
        }
    }

    /// <summary>Runs the test of key <paramref name="key"/>, of a class, or of a project (<see cref="ActAsync"/>'s values).</summary>
    public Task RunKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tests = _tests.Where(t => KeyOf(t) == key).ToList();
        return RunAsync(tests, all: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        switch (actionId)
        {
            case "tests.runAll":
                await RunAllAsync(cancellationToken).ConfigureAwait(true);
                break;
            case "tests.runFailed":
                await RunFailedAsync(cancellationToken).ConfigureAwait(true);
                break;
            case "tests.stop":
                foreach (var name in _run.Running.Where(n => n.StartsWith("Tests: ", StringComparison.Ordinal)).ToList())
                {
                    _run.Stop(name);
                }

                break;
            case "tests.refresh":
                await RefreshAsync().ConfigureAwait(true);
                break;
            case "tests.filter":
                _filter = value ?? "";
                Changed2();
                break;
            case "tests.collapseAll":
                foreach (var item in _tests)
                {
                    _collapsed.Add(item.Provider + ":" + item.Project + ":" + item.Class);
                }

                Changed2();
                break;
            case "tests.toggle" when value is not null:
                if (!_collapsed.Remove(value))
                {
                    _collapsed.Add(value);
                }

                Changed2();
                break;
            case "tests.run" when value is not null:
                await RunKeyAsync(value, cancellationToken).ConfigureAwait(true);
                break;
            case "tests.runClass" when value is not null:
                await RunAsync([.. _tests.Where(t => t.Provider + ":" + t.Project + ":" + t.Class == value)], all: false, cancellationToken).ConfigureAwait(true);
                break;
            case "tests.runProject" when value is not null:
                await RunAsync([.. _tests.Where(t => t.Provider + ":" + t.Project == value)], all: true, cancellationToken).ConfigureAwait(true);
                break;
            case "tests.runTarget" when value is not null:
                await _run.ActAsync("run.test", value, cancellationToken).ConfigureAwait(true);
                break;
            case "tests.select" when value is not null:
                _selected = value;
                Changed2();
                if (_tests.FirstOrDefault(t => KeyOf(t) == value) is { File: { } file, Line: >= 0 } test && _root() is { } root)
                {
                    await OpenAsync(Path.GetFullPath(file, root), test.Line + 1).ConfigureAwait(true);
                }

                break;
            case "tests.closeDetails":
                _selected = null;
                Changed2();
                break;
            case "tests.open" when value is not null:
                int bar = value.LastIndexOf('|');
                if (bar > 0 && int.TryParse(value.AsSpan(bar + 1), NumberStyles.None, Culture, out int line))
                {
                    await OpenAsync(value[..bar], line).ConfigureAwait(true);
                }

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
        _host.DocumentOpened -= OnDocumentOpened;
        _host.LineMarkClicked -= OnLineMarkClicked;
        _run.Changed -= OnRunChanged;
        TestProviders.Changed -= OnProvidersChanged;
        _finding?.Cancel();
        _finding?.Dispose();
        _found.TrySetResult();
    }

    /// <summary>The gutter marks of the file <paramref name="relative"/> (relative to the root): each test's line and how it stands.</summary>
    public IReadOnlyList<LineMark> MarksOf(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        var marks = new List<LineMark>();
        foreach (var test in _tests)
        {
            if (test.Line >= 0 && string.Equals(test.File, relative, StringComparison.OrdinalIgnoreCase))
            {
                marks.Add(new LineMark(test.Line, 1, OutcomeOf(test) switch
                {
                    TestOutcome.Passed => LineMarkKind.TestPassed,
                    TestOutcome.Failed => LineMarkKind.TestFailed,
                    TestOutcome.Skipped => LineMarkKind.TestSkipped,
                    _ => LineMarkKind.TestReady,
                }));
            }
        }

        return marks;
    }

    // ---- the gutter ----

    private void UpdateMarks()
    {
        if (_root() is not { } root)
        {
            return;
        }

        bool on = _options().TestGutter;
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } path)
            {
                _host.SetLineMarks(document, MarkSource, on ? MarksOf(WorkspaceFiles.Relative(root, Path.GetFullPath(path))) : []);
            }
        }
    }

    private void OnDocumentOpened(object? sender, EditorDocumentEventArgs e)
    {
        if (_root() is { } root && e.Document.FilePath is { } path && _options().TestGutter && _tests.Count > 0)
        {
            _host.SetLineMarks(e.Document, MarkSource, MarksOf(WorkspaceFiles.Relative(root, Path.GetFullPath(path))));
        }
    }

    // A click on a test's mark runs it (its ✓ or ✗ too: run it again).
    private void OnLineMarkClicked(object? sender, LineMarkEventArgs e)
    {
        if (e.Source != MarkSource || _root() is not { } root || e.Document.FilePath is not { } path)
        {
            return;
        }

        string relative = WorkspaceFiles.Relative(root, Path.GetFullPath(path));
        var tests = _tests.Where(t => t.Line == e.Mark.Line && string.Equals(t.File, relative, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tests.Count > 0)
        {
            _selected = KeyOf(tests[0]);
            _ = RunAsync(tests);
        }
    }

    // ---- what changes ----

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is { } path && TestProviders.All.Any(p => p.Watches(Path.GetFileName(path.AsSpan()))))
        {
            _ = RefreshAsync();
        }
    }

    private void OnRunChanged(object? sender, EventArgs e) => Publish();

    private void OnProvidersChanged(object? sender, EventArgs e) => _host.Dispatch("tests: a provider came or went", () => _ = RefreshAsync());

    // ---- failures to Problems ----

    private async Task ShowFailuresAsync(List<(TestItem Test, TestResult Result)> failures, string root)
    {
        if (!_options().TestProblems)
        {
            failures.Clear();
        }

        var problems = new List<RunProblem>();
        foreach (var (test, result) in failures)
        {
            // The failure's place: its stack's first frame in the workspace, else the test's own declaration.
            string? file = null;
            int line = 0;
            foreach (var frame in TestStackFrame.Parse(result.StackTrace))
            {
                if (frame.File is { } framed && IsUnder(root, framed))
                {
                    (file, line) = (framed, frame.Line);
                    break;
                }
            }

            if (file is null && test.File is { } declared)
            {
                (file, line) = (Path.GetFullPath(declared, root), test.Line + 1);
            }

            if (file is not null)
            {
                string message = (result.Message ?? "failed").ReplaceLineEndings(" ");
                problems.Add(new RunProblem(file, Math.Max(1, line), 1, DiagnosticSeverity.Error, "test", $"{test.ClassName}.{test.Method} failed: {message}"));
            }
        }

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
            foreach (var group in problems.GroupBy(p => Path.GetFullPath(p.File), StringComparer.OrdinalIgnoreCase))
            {
                var snapshot = open.GetValueOrDefault(group.Key);
                if (snapshot is null)
                {
                    try
                    {
                        snapshot = new PieceTableBuffer(File.Exists(group.Key) ? File.ReadAllText(group.Key) : "").Current;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        snapshot = new PieceTableBuffer("").Current;
                    }
                }

                var diagnostics = new List<EditorDiagnostic>();
                foreach (var problem in group)
                {
                    var text = snapshot.GetLine(Math.Clamp(problem.Line - 1, 0, snapshot.LineCount - 1));
                    string content = snapshot.GetText(text.Start, text.Length);
                    int indent = content.Length - content.TrimStart().Length;
                    diagnostics.Add(new EditorDiagnostic(text.Start + indent, Math.Max(0, text.Length - indent), problem.Severity, problem.Code, problem.Message));
                }

                made.Add(new RunProblemFile(group.Key, snapshot, diagnostics));
            }

            return made;
        }).ConfigureAwait(true);
        _host.Dispatch("tests: the failures in Problems", () =>
        {
            _problemFiles = files;
            ProblemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private static bool IsUnder(string root, string file)
    {
        try
        {
            var full = Path.GetFullPath(file);
            var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task OpenAsync(string path, int line)
    {
        if (await _host.OpenDocumentAsync(path).ConfigureAwait(true) is not { } document)
        {
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        var at = snapshot.GetLine(Math.Clamp(line - 1, 0, snapshot.LineCount - 1));
        _host.Select(document, SelectionState.At(at.Start));
    }

    // ---- the blocks ----

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

    private string ProjectTitle(string project)
    {
        var target = _run.Targets.FirstOrDefault(t => (t.Actions & RunActions.Test) != 0 && string.Equals(t.File, project, StringComparison.OrdinalIgnoreCase));
        return target?.Title ?? Path.GetFileNameWithoutExtension(project);
    }

    private List<PanelBlock> Build()
    {
        var blocks = new List<PanelBlock>();
        int passed = 0, failed = 0, skipped = 0, notRun = 0;
        foreach (var test in _tests)
        {
            switch (OutcomeOf(test))
            {
                case TestOutcome.Passed:
                    passed++;
                    break;
                case TestOutcome.Failed:
                    failed++;
                    break;
                case TestOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    notRun++;
                    break;
            }
        }

        bool running = _running.Count > 0;
        string status = _root() is null ? "Open a folder to find its tests."
            : _discovering && _tests.Count == 0 ? "Finding the workspace's tests…"
            : _tests.Count == 0 ? "No tests found: a test project (xUnit, NUnit, MSTest) shows its tests here."
            : running ? string.Create(Culture, $"Running {_running.Count} {(_running.Count == 1 ? "test" : "tests")}…")
            : string.Create(Culture, $"{_tests.Count} tests · {passed} passed · {failed} failed · {skipped} skipped · {notRun} not run");
        blocks.Add(new PanelText(status, PanelTextStyle.Secondary) { Tone = failed > 0 && !running ? PanelTone.Error : running || _discovering ? PanelTone.Accent : passed > 0 && failed == 0 ? PanelTone.Success : PanelTone.Normal });
        blocks.Add(new PanelButtons(
        [
            new PanelButton("Run all", "tests.runAll", PanelButtonKind.Primary) { Icon = RailIcons.Run, Enabled = _tests.Count > 0 && !running, Tooltip = "Run every test (one run a project)" },
            new PanelButton("Run failed", "tests.runFailed", PanelButtonKind.Secondary) { Icon = RailIcons.Refresh, Enabled = failed > 0 && !running, Tooltip = "Run the tests whose last run failed" },
            new PanelButton("Stop", "tests.stop", PanelButtonKind.Danger) { Icon = RailIcons.Stop, Enabled = running, Tooltip = "Stop the test runs, with their process trees" },
            new PanelButton("Refresh", "tests.refresh", PanelButtonKind.Flat) { Icon = RailIcons.Refresh, Tooltip = "Find the tests again" },
            new PanelButton("Collapse", "tests.collapseAll", PanelButtonKind.Flat) { Icon = RailIcons.Collapse, Enabled = _tests.Count > 0, Tooltip = "Fold every class" },
        ]));
        blocks.Add(new PanelQuery("tests.filter", "Filter tests by name", _filter));
        foreach (var problem in _problems)
        {
            blocks.Add(new PanelText(problem, PanelTextStyle.Secondary) { Tone = PanelTone.Warning });
        }

        if (_note is not null)
        {
            blocks.Add(new PanelText(_note, PanelTextStyle.Caption) { Tone = PanelTone.Warning });
        }

        if (_selected is not null && _tests.FirstOrDefault(t => KeyOf(t) == _selected) is { } chosen && _results.TryGetValue(_selected, out var result) && result.Outcome == TestOutcome.Failed)
        {
            blocks.Add(Details(chosen, result));
        }

        blocks.Add(new PanelText($"Each run asks first and goes through the '{_options().RunSandbox}' sandbox, its output in a terminal tab.", PanelTextStyle.Caption) { Variable = "CRAFT_RUN_SANDBOX" });
        blocks.Add(new PanelResults("tests.tree", Groups())
        {
            Version = _version,
            Empty = _root() is null ? "No folder is open." : _discovering ? null : _filter.Length > 0 ? "No test matches the filter." : "No tests in this workspace.",
        });
        return blocks;
    }

    // A failure's card: its message and its stack, each frame that names a file:line a row that opens it.
    private static PanelCard Details(TestItem test, TestResult result)
    {
        var parts = new List<PanelBlock>
        {
            new PanelText(result.Message ?? "It failed without a message.", PanelTextStyle.Code) { Tone = PanelTone.Error },
        };
        var frames = TestStackFrame.Parse(result.StackTrace);
        if (frames.Count > 0)
        {
            var rows = frames.Select((f, i) => new PanelResultRow("frame" + i.ToString(Culture), f.Text)
            {
                Code = true,
                ActionId = f.File is null ? null : "tests.open",
                Value = f.File is null ? null : f.File + "|" + f.Line.ToString(Culture),
                Tone = f.File is null ? PanelTone.Normal : PanelTone.Accent,
                Detail = f.File is null ? null : Path.GetFileName(f.File) + ":" + f.Line.ToString(Culture),
            }).ToList();
            parts.Add(new PanelText("Stack trace (a frame with a file opens it):", PanelTextStyle.Caption));
            foreach (var row in rows)
            {
                parts.Add(new PanelButtons([new PanelButton(row.Text, row.ActionId ?? "tests.none", PanelButtonKind.Flat) { Enabled = row.ActionId is not null, Value = row.Value, Tooltip = row.Detail }]));
            }
        }

        parts.Add(new PanelButtons(
        [
            new PanelButton("Run again", "tests.run", PanelButtonKind.Secondary) { Value = KeyOf(test), Icon = RailIcons.Run },
            new PanelButton("Close", "tests.closeDetails", PanelButtonKind.Flat) { Icon = RailIcons.Close },
        ]));
        return new PanelCard($"{test.ClassName}.{test.Method} failed", parts)
        {
            Tone = PanelTone.Error,
            Icon = FailedIcon,
            Badge = Time(result.Duration),
        };
    }

    // Project › class › method: a project's header (Run project), then its classes, each a group of its methods.
    private List<PanelResultGroup> Groups()
    {
        var groups = new List<PanelResultGroup>();
        string filter = _filter.Trim();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in _tests.GroupBy(t => (t.Provider, t.Project)))
        {
            var shown = project.Where(t => filter.Length == 0 || t.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            listed.Add(project.Key.Project);
            string projectKey = project.Key.Provider + ":" + project.Key.Project;
            int failed = project.Count(t => OutcomeOf(t) == TestOutcome.Failed);
            int passed = project.Count(t => OutcomeOf(t) == TestOutcome.Passed);
            groups.Add(new PanelResultGroup("project:" + projectKey, ProjectTitle(project.Key.Project), [])
            {
                Detail = project.Key.Project,
                Icon = RailIcons.Test,
                Badge = Counts(project.Count(), passed, failed),
                Buttons = [new PanelButton("Run the project's tests", "tests.runProject", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = projectKey, Enabled = _running.Count == 0 }],
                Tooltip = project.Key.Project,
            });
            foreach (var type in shown.GroupBy(t => t.Class))
            {
                string classKey = projectKey + ":" + type.Key;
                var first = type.First();
                var rows = type.Select(Row).ToList();
                int classFailed = type.Count(t => OutcomeOf(t) == TestOutcome.Failed);
                int classPassed = type.Count(t => OutcomeOf(t) == TestOutcome.Passed);
                groups.Add(new PanelResultGroup(classKey, first.ClassName, rows)
                {
                    Detail = type.Key.Length > first.ClassName.Length ? type.Key[..(type.Key.Length - first.ClassName.Length - 1)] : null,
                    Icon = classFailed > 0 ? FailedIcon : classPassed == type.Count() ? PassedIcon : null,
                    Badge = Counts(type.Count(), classPassed, classFailed),
                    Collapsed = _collapsed.Contains(classKey),
                    ToggleActionId = "tests.toggle",
                    Buttons = [new PanelButton("Run the class's tests", "tests.runClass", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = classKey, Enabled = _running.Count == 0 }],
                    Tooltip = type.Key,
                });
            }
        }

        // A test project the Run view found without a test listed (another framework, a build-generated test): its Run.
        foreach (var target in _run.Targets.Where(t => (t.Actions & RunActions.Test) != 0 && !t.File.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !t.File.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) && !listed.Contains(t.File)))
        {
            if (filter.Length > 0 && !target.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            groups.Add(new PanelResultGroup("target:" + RunView.KeyOf(target), target.Title, [])
            {
                Detail = target.File + " · no test found in its files: Run runs them all",
                Icon = RailIcons.Test,
                Buttons = [new PanelButton("Run its tests", "tests.runTarget", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = RunView.KeyOf(target) }],
            });
        }

        return groups;
    }

    private PanelResultRow Row(TestItem test)
    {
        string key = KeyOf(test);
        var outcome = OutcomeOf(test);
        _results.TryGetValue(key, out var result);
        return new PanelResultRow(key, test.Method)
        {
            Icon = outcome switch
            {
                TestOutcome.Passed => PassedIcon,
                TestOutcome.Failed => FailedIcon,
                TestOutcome.Skipped => SkippedIcon,
                TestOutcome.Running => RailIcons.Refresh,
                _ => NotRunIcon,
            },
            Tone = outcome switch
            {
                TestOutcome.Passed => PanelTone.Success,
                TestOutcome.Failed => PanelTone.Error,
                TestOutcome.Skipped => PanelTone.Warning,
                TestOutcome.Running => PanelTone.Accent,
                _ => PanelTone.Normal,
            },
            Detail = outcome == TestOutcome.Failed && result?.Message is { } message ? FirstLine(message) : outcome == TestOutcome.Skipped && result?.Message is { } why ? FirstLine(why) : test.Framework,
            Badge = outcome == TestOutcome.Running ? "running" : result is not null ? Time(result.Duration) : null,
            ActionId = "tests.select",
            Value = key,
            Buttons = [new PanelButton("Run", "tests.run", PanelButtonKind.Flat) { Icon = RailIcons.Run, Value = key, Tooltip = "Run this test", Enabled = _running.Count == 0 }],
        };
    }

    private static string Counts(int total, int passed, int failed) =>
        failed > 0 ? string.Create(Culture, $"{failed} ✗ · {passed}/{total}") : passed > 0 ? string.Create(Culture, $"{passed}/{total} ✓") : total.ToString(Culture);

    private static string FirstLine(string text)
    {
        int end = text.AsSpan().IndexOfAny('\r', '\n');
        string line = end < 0 ? text : text[..end];
        return line.Length <= 120 ? line : string.Concat(line.AsSpan(0, 119), "…");
    }

    private static string Time(TimeSpan duration) => duration.TotalMilliseconds < 1 ? "< 1 ms"
        : duration.TotalSeconds < 1 ? string.Create(Culture, $"{duration.TotalMilliseconds:0} ms")
        : string.Create(Culture, $"{duration.TotalSeconds:0.0} s");
}
