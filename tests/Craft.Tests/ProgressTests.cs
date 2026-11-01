// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Activities;
using Craft.Editor.Run;
using Craft.Editor.Testing;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner, 2026-11-01: "add a progress bar to the status bar and use it in any progress related work … don't use the
/// default one". Craft's bar's model (determinate, indeterminate, reduced motion, nothing allocated a frame), the activity
/// the status bar shows (the newest or the one picked, "+N", percent and time left), and the reporters that now give a
/// share: a model's download and load, the C# workspace's projects, a search's files, the tests found and run.
/// </summary>
[TestGroup("Progress")]
public static class ProgressTests
{
    [Test]
    public static void TheBarIsDeterminateWithAShareAMovingSegmentWithoutAndStillUnderReducedMotion()
    {
        var bar = new ProgressBarModel(1000);
        Check.True(bar.Fraction is null && bar.Animating, "no share: indeterminate, moving");
        bar.Advance(250);
        var segment = bar.Geometry(200, 4);
        Check.True(Math.Abs(bar.Phase - 0.25) < 1e-9 && segment.FillX >= 0 && segment.FillX + segment.FillWidth <= 200 && segment.FillWidth > 0 && segment.Radius == 2,
            $"a quarter through its cycle the segment is inside the bar ({segment})");
        bar.Advance(1000);
        Check.True(Math.Abs(bar.Phase - 0.25) < 1e-9, "a whole cycle later it is where it was");
        var entering = new ProgressBarModel(1000).Geometry(200, 4);
        Check.True(entering.FillX == 0 && entering.FillWidth == 0, "at the cycle's start the segment has not entered yet");

        Check.True(bar.Set(0.5) && bar.Fraction == 0.5 && bar.Shown == 0 && bar.Animating, "a share: the fill grows from the start toward it");
        for (int i = 0; i < 200 && bar.Advance(16); i++)
        {
        }

        Check.True(bar.Shown == 0.5 && !bar.Animating, $"it settles on the share and stops moving ({bar.Shown})");
        Check.True(bar.Geometry(200, 4) is { FillX: 0, FillWidth: 100, Opacity: 1 }, "half the bar filled from the left");
        Check.True(!bar.Set(0.5), "the same share changes nothing");
        bar.Set(0.2);
        Check.True(bar.Shown == 0.2, "work that went back: the fill goes back at once, no easing down");
        bar.Set(7);
        Check.True(bar.Fraction == 1, "a share past the end is the end");
        bar.Set(-1);
        Check.True(bar.Fraction is null, "a share below 0 is not known");
        bar.Set(double.NaN);
        Check.True(bar.Fraction is null, "nor NaN");
        var tiny = new ProgressBarModel { Reduced = true };
        tiny.Set(0.001);
        Check.True(tiny.Geometry(200, 4).FillWidth == 4, "something done shows at least a dot");

        var still = new ProgressBarModel { Reduced = true };
        Check.True(!still.Animating && still.Geometry(120, 4) is { FillX: 0, FillWidth: 120, Opacity: 0.5f }, "reduced motion without a share: the whole bar at half strength, nothing moves");
        still.Set(0.75);
        Check.True(still.Shown == 0.75 && !still.Animating, "reduced motion with a share: the fill jumps there, no timer");
    }

    [Test]
    public static void AWarmFrameOfTheBarAllocatesNothing()
    {
        var moving = new ProgressBarModel();
        var filling = new ProgressBarModel();
        filling.Set(0.9);
        var running = Snapshots(("Loading a model", 0.4), ("Building", null));
        long sum = 0;
        for (int i = 0; i < 2000; i++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Frame();
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.Equal(0L, bytes, $"10,000 warm frames of a moving and a filling bar, the status bar's choice among the running ({sum})");

        void Frame()
        {
            moving.Advance(16);
            filling.Advance(16);
            sum += (long)moving.Geometry(72, 4).FillWidth + (long)filling.Geometry(72, 4).FillWidth + ActivityStatus.Chosen(running, 2);
        }
    }

    [Test]
    public static void TheStatusBarShowsTheNewestOrThePickedWithItsShareTimeLeftAndTheOthersCounted()
    {
        var running = Snapshots(("Loading the coder model", 0.42), ("C#: building the workspace", null), ("Searching", null));
        Check.Equal(0, ActivityStatus.Chosen(running, null), "the newest first");
        Check.Equal(1, ActivityStatus.Chosen(running, 2), "the one the user picked while it runs");
        Check.Equal(0, ActivityStatus.Chosen(running, 99), "a pick that ended: the newest again");
        Check.Equal(-1, ActivityStatus.Chosen([], null), "none runs: nothing");
        string line = ActivityText.StatusLine(running, 0);
        Check.True(line.StartsWith("Loading the coder model · 42% · ", StringComparison.Ordinal) && line.Contains("1:20 left", StringComparison.Ordinal) && line.EndsWith("+2", StringComparison.Ordinal),
            $"its percent and the time left, the others counted: {line}");
        string picked = ActivityText.StatusLine(running, 1);
        Check.True(picked.StartsWith("C#: building the workspace · ", StringComparison.Ordinal) && !picked.Contains('%', StringComparison.Ordinal) && picked.EndsWith("+2", StringComparison.Ordinal),
            $"no share: no percent invented, the time so far: {picked}");

        var center = new ActivityCenter(null, new CraftOptions { ActivityMinimumMilliseconds = 0 });
        using var first = center.Start("first");
        using var second = center.Start("second");
        center.Pin(center.Running[^1].Id);
        Check.True(center.Pinned == center.Running[^1].Id, "the pick is kept (Changed tells the status bar)");
        Check.Equal(1, ActivityStatus.Chosen(center.Running, center.Pinned), "the picked (older) one is the one shown");
        center.Pin(null);
        Check.Equal(0, ActivityStatus.Chosen(center.Running, center.Pinned), "unpicked: the newest");
    }

    [Test]
    public static async Task AModelsDownloadAndLoadReportTheirShareToTheirActivity()
    {
        var provider = new ReportingProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = provider.Name, Model = "big", CompletionModel = "" };
            var activities = new RecordingActivities();
            using var models = new AiModels(() => settings) { Activities = activities };
            await models.ChatAsync(CancellationToken.None);
            var load = activities.Started.Single(a => a.Title == "Loading big");
            for (int i = 0; i < 500 && !load.Ended; i++)
            {
                await Task.Delay(10);                                    // the activity ends on the lease's continuation
            }

            var shares = load.Reports.Select(r => r.Fraction).ToList();
            Check.True(shares.Contains(0.25) && shares.Contains(0.5) && shares.IndexOf(0.25) < shares.IndexOf(0.5), $"the download's and the load's shares reach the activity in order ({string.Join(", ", shares)})");
            Check.True(load.Reports.Any(r => r.Text?.StartsWith("downloading big", StringComparison.Ordinal) == true) && load.Ended, "with the stage's words; then it ends");
            Check.True(!shares.Contains(1), "a stage's 1 (ready) is the end, not a share shown");
        }
        finally
        {
            ModelProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static async Task TheCSharpWorkspacesBuildReportsItsProjectsAddedOfAll()
    {
        using var folder = new TempFolder();
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            Directory.CreateDirectory(folder.File(name));
            await File.WriteAllTextAsync(folder.File(Path.Combine(name, name + ".csproj")), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(folder.File(Path.Combine(name, "A.cs")), "class A { }");
        }

        await File.WriteAllTextAsync(folder.File("All.slnx"), "<Solution><Project Path=\"One/One.csproj\" /><Project Path=\"Two/Two.csproj\" /><Project Path=\"Three/Three.csproj\" /></Solution>");
        var activities = new RecordingActivities();
        // The C# plug-in registers its readers in the app; here, unless another test did.
        bool readers = WorkspaceReaders.Find(Craft.CSharp.Workspaces.CsprojWorkspaceReader.ReaderName) is null;
        if (readers)
        {
            WorkspaceReaders.Register(new Craft.CSharp.Workspaces.CsprojWorkspaceReader());
            WorkspaceReaders.Register(new Craft.CSharp.Workspaces.LooseWorkspaceReader());
        }

        var workspace = CSharpWorkspace.Enter(folder.Path, LanguageSettings.Default, activities);
        try
        {
            var solution = await workspace.BuildAsync(null, CancellationToken.None);
            var build = activities.Started.Single(a => a.Title.StartsWith("C#: building the workspace", StringComparison.Ordinal));
            var shares = build.Reports.Where(r => r.Fraction is not null).Select(r => r.Fraction!.Value).ToList();
            int projects = solution.ProjectIds.Count;
            Check.True(projects >= 3 && shares.Count == projects && shares[0] == 0 && shares.Zip(shares.Skip(1)).All(p => p.Second > p.First) && shares[^1] < 1,
                $"each project added of the {projects} found: {string.Join(", ", shares.Select(f => f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
            Check.True(build.Reports[0].Fraction is null && build.Reports.Any(r => r.Text?.StartsWith("project 1 of", StringComparison.Ordinal) == true), "reading the projects first (no share known), then project k of n");
        }
        finally
        {
            await workspace.LeaveAsync(workspace);
            if (readers)
            {
                WorkspaceReaders.Unregister(Craft.CSharp.Workspaces.CsprojWorkspaceReader.ReaderName);
                WorkspaceReaders.Unregister(Craft.CSharp.Workspaces.LooseWorkspaceReader.ReaderName);
            }
        }
    }

    [Test]
    public static async Task ASearchReportsItsFilesSearchedOfAllOnceTheListingEnded()
    {
        using var folder = new TempFolder();
        for (int i = 0; i < 100; i++)
        {
            await File.WriteAllTextAsync(folder.File($"f{i:000}.txt"), i % 10 == 0 ? "needle\n" : "hay\n");
        }

        var told = new ConcurrentQueue<WorkspaceSearchProgress>();
        var summary = await WorkspaceSearchProviders.Get(WorkspaceSearchProviders.Text).SearchAsync(
            new WorkspaceSearchRequest(folder.Path, new WorkspaceSearchQuery("needle")) { Threads = 2, Progress = told.Enqueue }, _ => { });
        var reports = told.ToList();
        Check.True(summary.FilesSearched == 100 && reports.Count >= 2, $"every file searched, told several times ({reports.Count})");
        Check.True(reports[^1] is { Searched: 100, Total: 100, Fraction: 1 }, $"the end: all of all ({reports[^1]})");
        Check.True(reports.All(r => r.Total is null ? r.Fraction is null : r.Fraction <= 1), "no share before the listing ended: none is invented");
        Check.True(new WorkspaceSearchProgress(10, null).Fraction is null && new WorkspaceSearchProgress(25, 100).Fraction == 0.25, "the share once the total is known");
    }

    [Test]
    public static async Task TheTestsFoundAndRunReportProvidersDoneAndResultsSeenOfTheirTests()
    {
        using var folder = new TempFolder();
        TestExplorerTests.Fixture(folder.Path);
        string source = Path.Combine(folder.Path, "tests", "App.Tests", "MathTests.cs");
        var options = new CraftOptions { RunSandbox = "progress-lines", WorkspaceWatcher = FolderWatchers.None, ActivityMinimumMilliseconds = 0 };
        var ui = new ConcurrentQueue<Action>();
        var presenter = new TestExplorerTests.Presenter();
        using var host = new EditorHost(options, ui.Enqueue, presenter);
        var sandbox = new LinesSandbox(TestExplorerTests.Trx(source), host);
        var provider = new TestExplorerTests.WrappedProvider();
        var watching = new WatchingProvider(host);
        CommandSandboxes.Register(sandbox);
        TestProviders.Register(provider);
        TestProviders.Register(watching);
        try
        {
            using var run = new RunView(host, () => folder.Path, () => options);
            using var view = new TestExplorerView(host, run, () => folder.Path, () => options);
            var found = view.RefreshAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => found.IsCompleted, "the tests are found");
            Check.True(watching.Seen is { } seen && seen.Fraction is >= 0 and < 1 && seen.Text!.Contains("files", StringComparison.Ordinal),
                $"finding the tests: each provider's share of the providers, with the files listed ({watching.Seen})");

            presenter.Answers.Enqueue(0);                               // Run
            var ran = view.RunAllAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => ran.IsCompleted, "the run ends");
            var shares = sandbox.Shares.ToList();
            int total = view.Tests.Count(t => t.Provider == provider.Name);
            Check.True(shares.Count == 4 && shares.Zip(shares.Skip(1)).All(p => p.Second > p.First) && Math.Abs(shares[^1] - (4.0 / total)) < 1e-9,
                $"each result line counts one test of the {total} run: {string.Join(", ", shares)}");
            Check.True(new Craft.CSharp.Run.DotnetTestProvider().IsResultLine("  Passed App.Tests.MathTests.Adds [12 ms]") && !new Craft.CSharp.Run.DotnetTestProvider().IsResultLine("Test run for App.Tests.dll")
                && !new Craft.CSharp.Run.DotnetTestProvider().IsResultLine("Passed!  - Failed: 0, Passed: 4"), "the console logger's result lines, not its header or its summary");
        }
        finally
        {
            CommandSandboxes.Unregister(sandbox.Name);
            TestProviders.Unregister(provider.Name);
            TestProviders.Unregister(watching.Name);
        }
    }

    private static List<ActivitySnapshot> Snapshots(params (string Title, double? Fraction)[] activities)
    {
        var list = new List<ActivitySnapshot>();
        long id = activities.Length;
        foreach (var (title, fraction) in activities)
        {
            list.Add(new ActivitySnapshot(id--, title, ActivityKinds.General, "host", ActivityState.Running, fraction, null, null, DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(58), true)
            {
                Left = fraction is null ? null : TimeSpan.FromSeconds(80),
            });
        }

        return list;
    }

    /// <summary>A provider whose load reports a download's and a load's shares, then the ready stage.</summary>
    private sealed class ReportingProvider : IModelProvider
    {
        public string Name => "progress-test";

        public string Summary => "the progress tests' provider";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new ModelLoadStatus("downloading", 0.25, "downloading big: 1.0 of 4.0 GB, file 1 of 2"));
            progress?.Report(new ModelLoadStatus("loading", 0.5, "loading big on cpu: 100 of 200 tensors"));
            progress?.Report(new ModelLoadStatus("ready", 1, "big loaded"));
            return Task.FromResult(new ModelLease(request.Model, null, null, 4096, TimeSpan.FromMilliseconds(1), new Nothing()));
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>Activities that keep every report, for the reporters' tests.</summary>
    internal sealed class RecordingActivities : IEditorActivities
    {
        public ConcurrentQueue<Recorded> Started { get; } = new();

        public IReadOnlyList<ActivitySnapshot> Running => [];

        public IReadOnlyList<ActivitySnapshot> Recent => [];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IActivity Start(string title, string kind = ActivityKinds.General, bool cancellable = false, CancellationToken cancellationToken = default, bool keep = true)
        {
            var activity = new Recorded(title, kind, cancellationToken);
            Started.Enqueue(activity);
            return activity;
        }

        public bool Cancel(long id) => false;

        public bool Retry(long id) => false;

        internal sealed class Recorded(string title, string kind, CancellationToken token) : IActivity
        {
            public List<(double? Fraction, string? Text)> Reports { get; } = [];

            public bool Ended { get; private set; }

            public long Id => 1;

            public string Title => title;

            public string Kind => kind;

            public CancellationToken CancellationToken => token;

            public bool IsEnded => Ended;

            public void Report(double? fraction, string? text = null, ActivityRate? rate = null)
            {
                lock (Reports)
                {
                    Reports.Add((fraction, text));
                }
            }

            public void Complete(string? outcome = null) => Ended = true;

            public void Fail(string reason, Exception? error = null, Action? retry = null) => Ended = true;

            public void Dispose() => Ended = true;
        }
    }

    /// <summary>A sandbox that writes the TRX fixture and four result lines, noting the run activity's share after each.</summary>
    private sealed class LinesSandbox(string trx, EditorHost host) : ICommandSandbox
    {
        public ConcurrentQueue<double> Shares { get; } = new();

        public string Name => "progress-lines";

        public string Summary => "result lines, then the TRX";

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            int at = command.ToList().IndexOf("--results-directory");
            string name = command.First(c => c.StartsWith("trx;LogFileName=", StringComparison.Ordinal))["trx;LogFileName=".Length..];
            await File.WriteAllTextAsync(Path.Combine(command[at + 1], name), trx, cancellationToken);
            limits.Output?.Invoke("Test run for App.Tests.dll (.NETCoreApp,Version=v10.0)");
            foreach (var line in (ReadOnlySpan<string>)["  Passed App.Tests.MathTests.Adds [1 ms]", "  Failed App.Tests.MathTests.Divides [2 ms]", "  Passed App.Tests.MathTests.Theory [1 ms]", "  Skipped App.Tests.MathTests.Skips"])
            {
                limits.Output?.Invoke(line);
                if (host.ActivityCenter.Running.FirstOrDefault(a => a.Title.Contains("Tests: App.Tests", StringComparison.Ordinal)) is { Fraction: { } share })
                {
                    Shares.Enqueue(share);
                }
            }

            return new CommandResult(1, false, "", TimeSpan.FromMilliseconds(5), false);
        }
    }

    /// <summary>A provider that finds nothing and notes what the status bar shows of the search for tests while it is asked.</summary>
    private sealed class WatchingProvider(EditorHost host) : ITestProvider
    {
        public ActivitySnapshot? Seen { get; private set; }

        public string Name => "progress-watching";

        public string Summary => "notes the finding's share";

        public string Title => "Watching";

        public bool Watches(ReadOnlySpan<char> fileName) => false;

        public IReadOnlyList<TestItem> Discover(RunWorkspace workspace, CancellationToken cancellationToken = default)
        {
            Seen = host.ActivityCenter.Running.FirstOrDefault(a => a.Title == "Tests: finding the tests");
            return [];
        }

        public RunCommand Command(TestRunRequest request) => RunCommand.Unavailable("none", request.Root);

        public IReadOnlyList<TestResult> ReadResults(TestRunRequest request, string output, CancellationToken cancellationToken = default) => [];
    }
}
