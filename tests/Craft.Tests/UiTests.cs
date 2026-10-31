// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Activities;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>A dispatcher the test pumps: what is dispatched waits until <see cref="Pump"/>.</summary>
internal sealed class QueuedDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public int Pending => _queue.Count;

    public void Dispatch(Action action) => _queue.Enqueue(action);

    /// <summary>Runs what was dispatched, and what that dispatches, until nothing waits.</summary>
    public int Pump()
    {
        int ran = 0;
        while (_queue.TryDequeue(out var action))
        {
            action();
            ran++;
        }

        return ran;
    }
}

/// <summary>A UI thread of the test's own: it runs what is dispatched to it, in order, until disposed.</summary>
internal sealed class FakeUiThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public FakeUiThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _work.GetConsumingEnumerable())
            {
                action();
            }
        })
        { IsBackground = true, Name = "test UI thread" };
        _thread.Start();
    }

    public void Dispatch(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            _work.Add(action);
        }
    }

    /// <summary>Runs <paramref name="action"/> on the thread and waits for it.</summary>
    public void Run(Action action)
    {
        using var done = new ManualResetEventSlim();
        Dispatch(() =>
        {
            action();
            done.Set();
        });
        done.Wait(TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _work.Dispose();
    }
}

/// <summary>Phase U's activities: started, reported and ended on any thread, coalesced, kept, cancelled and retried.</summary>
[TestGroup("Activities")]
public static class ActivitiesTests
{
    private static readonly CraftOptions Quick = new() { ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 1, NotificationsKeep = 10 };

    [Test]
    public static void ReportsFromAnyThreadAreCoalescedAndTheEndIsKept()
    {
        var ui = new QueuedDispatcher();
        var center = new ActivityCenter(ui.Dispatch, Quick);
        int changed = 0;
        var ended = new List<ActivitySnapshot>();
        center.Changed += (_, _) => changed++;
        center.Ended += (_, a) => ended.Add(a);
        var load = center.Start("Loading a model", ActivityKinds.ModelLoad);
        ui.Pump();
        changed = 0;
        Parallel.For(0, 400, i => load.Report(i / 400.0, "reading the weights", new ActivityRate(12.3, "MB/s")));
        Check.True(ui.Pending <= 2, $"400 reports from four threads wait as one update ({ui.Pending} dispatches)");
        ui.Pump();
        Check.True(changed is >= 1 and <= 2, $"the UI hears them once ({changed})");
        var running = center.Running.Single();
        Check.True(running is { Kind: ActivityKinds.ModelLoad, State: ActivityState.Running, Text: "reading the weights", Rate.Value: 12.3 } && running.Fraction is > 0 and < 1,
            "the running activity has its kind, text, rate and share");
        load.Complete("loaded in 4.2 s");
        ui.Pump();
        Check.True(center.Running.Count == 0 && center.Recent.Single() is { State: ActivityState.Completed, Text: "loaded in 4.2 s", Fraction: 1 }, "completed, it is kept with its outcome");
        Check.True(ended.Single().Id == load.Id, "the end is raised once, on the UI thread");
        load.Fail("too late");
        Check.True(center.Recent.Single().State == ActivityState.Completed, "an ended activity takes no second end");
    }

    [Test]
    public static void AFailureCarriesItsRetryAndACancelStopsTheWork()
    {
        var ui = new QueuedDispatcher();
        var center = new ActivityCenter(ui.Dispatch, Quick);
        int retried = 0;
        var failing = center.Start("Downloading", ActivityKinds.Download);
        failing.Fail("the network is off", new IOException("off"), () => retried++);
        var failed = center.Recent.Single();
        Check.True(failed is { State: ActivityState.Failed, Error: "the network is off", CanRetry: true }, "a failure keeps its reason and its retry");
        Check.True(center.Retry(failed.Id) && !center.Retry(failed.Id), "Retry runs once");
        ui.Pump();
        Check.Equal(1, retried, "the retry ran on the UI thread");

        var run = center.Start("Agent: a task", ActivityKinds.Agent, cancellable: true);
        Check.True(center.Running.Single().Cancellable, "a cancellable activity shows Cancel");
        Check.True(center.Cancel(run.Id), "Cancel is taken");
        Check.True(run.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)), "its token is cancelled (the work stops on it)");
        run.Dispose();
        Check.True(center.Recent[0] is { State: ActivityState.Cancelled }, "disposed after the cancel, it ended cancelled");
        var fixedOne = center.Start("Saving", ActivityKinds.Save);
        Check.True(!center.Cancel(fixedOne.Id), "an activity that is not cancellable cannot be cancelled");
        fixedOne.Dispose();

        using var outer = new CancellationTokenSource();
        var linked = center.Start("Chat: a question", ActivityKinds.Generation, cancellable: true, outer.Token);
        outer.Cancel();
        Check.True(linked.CancellationToken.IsCancellationRequested, "the token it was started with cancels it too");
        linked.Fail("stopped", new OperationCanceledException());
        Check.True(center.Recent[0].State == ActivityState.Cancelled, "a failure that is the cancellation counts as cancelled");
    }

    [Test]
    public static void AShortOneNeverShowsAndAFailureAlwaysDoes()
    {
        var center = new ActivityCenter(null, new CraftOptions { ActivityMinimumMilliseconds = 60_000 });
        var quick = center.Start("Saving a small file", ActivityKinds.Save);
        Check.True(center.Running.Count == 1 && center.Visible.Count == 0, "running under the minimum, it is not in the status bar");
        quick.Complete();
        Check.True(center.Recent.Count == 0, "ended under the minimum, it is not kept");
        center.Start("Saving a file", ActivityKinds.Save).Fail("the disk is full");
        Check.True(center.Recent.Single().State == ActivityState.Failed, "a failure is kept however short");
        var unkept = new ActivityCenter(null, Quick).Start("Completion", ActivityKinds.Generation, keep: false);
        unkept.Complete();
    }

    [Test]
    public static void TheWordsTheStatusBarShows()
    {
        var started = DateTimeOffset.Now;
        var load = new ActivitySnapshot(1, "Loading Qwen", ActivityKinds.ModelLoad, "ai", ActivityState.Running, 0.42, "reading", new ActivityRate(12.34, "MB/s"), started, TimeSpan.FromSeconds(20), false)
        {
            Left = TimeSpan.FromSeconds(80),
        };
        var agent = load with { Id = 2, Title = "Agent: fix it", Fraction = null, Rate = new ActivityRate(41, "tok/s"), Left = null, Text = "turn 2 of 30" };
        Check.Equal("42% · 12.3 MB/s · 1:20 left · reading", ActivityText.Progress(load), "share, rate, time left, text");
        Check.Equal("Loading Qwen · 42% · 12.3 MB/s · 1:20 left · reading  +1", ActivityText.StatusLine([load, agent]), "the newest and +N");
        Check.Equal("41 tok/s · 20 s · turn 2 of 30", ActivityText.Progress(agent), "no share: the time so far");
        Check.Equal("", ActivityText.StatusLine([]), "nothing runs: nothing shows");
        Check.Equal("done in 20 s · loaded", ActivityText.Outcome(load with { State = ActivityState.Completed, Text = "loaded" }), "an outcome");
        Check.Equal("failed after 20 s: the file is missing", ActivityText.Outcome(load with { State = ActivityState.Failed, Error = "the file is missing" }), "a failure");
        Check.Equal("The window was blocked 3.2 s by chat: show the reply", ActivityText.Stall(TimeSpan.FromSeconds(3.2), "chat: show the reply"), "a stall");
        Check.True(ActivityText.Duration(TimeSpan.FromMinutes(62)) == "1:02:00" && ActivityText.Duration(TimeSpan.FromSeconds(185)) == "3:05", "durations");
    }

    [Test]
    public static async Task TheHostsProgressAndSavesAreActivities()
    {
        using var folder = new TempFolder();
        using var host = new EditorHost(Quick with { NewLine = "\n" });
        using (var progress = host.StartProgress("Indexing the folder"))
        {
            progress.Report(0.5, "half");
            Check.True(host.ActivityCenter.Running.Single() is { Title: "Indexing the folder", Fraction: 0.5, Text: "half" }, "IEditorHost.StartProgress shows as an activity");
        }

        var document = TextDocument.Create("class A {}", host.Options);
        var open = host.Open(document, null, "plain");
        await host.SaveAsync(open, folder.File("a.cs"));
        Check.True(host.ActivityCenter.Recent.Any(a => a is { Title: "Saving a.cs", Kind: ActivityKinds.Save, State: ActivityState.Completed }), "a save is an activity");
        Check.True(host.ActivityCenter.Recent.Any(a => a is { Title: "Indexing the folder", State: ActivityState.Completed }), "the progress ended when disposed");
    }

    [Test]
    public static void AScriptedSessionListsEveryActivityAndCancelsEach()
    {
        var center = new ActivityCenter(null, Quick);
        string[] kinds = [ActivityKinds.Download, ActivityKinds.ModelLoad, ActivityKinds.Agent, ActivityKinds.Generation, ActivityKinds.Training, ActivityKinds.Indexing,
            ActivityKinds.Build, ActivityKinds.Save, ActivityKinds.Command, ActivityKinds.Plugin, ActivityKinds.Voice];
        var started = kinds.Select(kind => center.Start("work: " + kind, kind, cancellable: true)).ToArray();
        foreach (var activity in started)
        {
            activity.Report(null, "running");
        }

        var visible = center.Visible;
        Check.Equal(kinds.Length, visible.Count, "the status bar's list has every one");
        Check.True(visible[0].Title == "work: " + ActivityKinds.Voice, "the newest first");
        Check.True(ActivityText.StatusLine(visible).EndsWith($"+{kinds.Length - 1}", StringComparison.Ordinal), "the bar shows the newest and +N");
        foreach (var snapshot in visible)
        {
            Check.True(center.Cancel(snapshot.Id), $"{snapshot.Title} cancels from the list");
        }

        foreach (var activity in started)
        {
            Check.True(activity.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)), $"{activity.Title}'s work hears the cancel");
            activity.Dispose();
        }

        Check.True(center.Running.Count == 0 && center.Recent.Count == Math.Min(kinds.Length, Quick.NotificationsKeep) && center.Recent.All(a => a.State == ActivityState.Cancelled),
            "each ended cancelled");
    }
}

/// <summary>Phase U's notification centre: running, ended and failed operations, messages and status lines, grouped by source; the bell; toasts.</summary>
[TestGroup("Notifications")]
public static class NotificationsTests
{
    private static readonly CraftOptions Quick = new() { ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 1, NotificationsKeep = 4 };

    [Test]
    public static async Task TheCentreShowsRunningFinishedFailedAndMessages()
    {
        using var host = new EditorHost(Quick);
        var centre = host.Notifications;
        int logs = 0;
        centre.OpenLog = () => logs++;
        var toasts = new List<NotificationEntry>();
        centre.Toast += (_, e) => toasts.Add(e);
        var running = host.ActivityCenter.Start("Loading a model", ActivityKinds.ModelLoad, true, CancellationToken.None, true, "ai");
        running.Report(0.25, "a quarter");
        host.ActivityCenter.Start("Building the workspace", ActivityKinds.Build, false, CancellationToken.None, true, "csharp").Complete("3 projects");
        int retried = 0;
        host.ActivityCenter.Start("Downloading", ActivityKinds.Download, false, CancellationToken.None, true, "ai").Fail("the network is off", null, () => retried++);
        var message = host.Notify("Hello", "from the host");
        var status = centre.ShowStatus("ai", "Chat model", "not loaded");

        var groups = centre.Groups;
        Check.SequenceEqual(["ai", "host", "csharp"], [.. groups.Select(g => g.Source)], "grouped by source, the newest first");
        var ai = groups[0].Entries;
        Check.True(ai[0] is { IsRunning: true, Title: "Loading a model" } && ai[0].Text.StartsWith("25%", StringComparison.Ordinal), $"running first, live ({ai[0].Text})");
        Check.True(ai[1] is { Kind: NotificationKind.Status, Text: "not loaded" }, "then the status line");
        var failure = ai[2];
        Check.True(failure is { Severity: NotificationSeverity.Error, Kind: NotificationKind.Activity } && failure.Text.Contains("the network is off", StringComparison.Ordinal), "the failure with its reason");
        Check.True(failure.Actions.Select(a => a.Label).SequenceEqual([ActivityText.Retry, ActivityText.OpenLog]), "with Retry and Open log");
        Check.True(groups[2].Entries.Single() is { Severity: NotificationSeverity.Success, Duration: not null } done && done.Text.StartsWith("done in", StringComparison.Ordinal), "the finished one with its time");
        Check.True(toasts.Count == 1 && toasts[0].Title == "Downloading", "only the failure toasts");
        Check.Equal(3, centre.Unseen, "the bell counts the ended ones and the message, never a status line or a running one");

        await centre.RunAsync(failure.Id, failure.Actions[1]);
        Check.True(logs == 1 && centre.Entries.Any(e => e.Id == failure.Id), "Open log shows the Output and keeps the entry");
        await centre.RunAsync(failure.Id, failure.Actions[0]);
        Check.True(retried == 1 && centre.Entries.All(e => e.Id != failure.Id), "Retry runs the retry and takes the entry away");

        status.Update("loaded on cpu", NotificationSeverity.Success);
        Check.True(centre.StatusOf("ai").Single() is { Text: "loaded on cpu", Severity: NotificationSeverity.Success }, "a status line updates in place");
        centre.MarkSeen();
        Check.Equal(0, centre.Unseen, "opening the centre clears the bell");
        var question = host.Notify("A question", "proceed?", NotificationSeverity.Question, [new NotificationAction("Yes", _ => Task.CompletedTask, Primary: true)]);
        Check.True(toasts.Count == 2 && toasts[1].Actions.Single().Label == "Yes", "a question toasts with its answers");
        for (int i = 0; i < 6; i++)
        {
            host.Notify("Note " + i, "");
        }

        Check.True(centre.Entries.Count(e => e is { IsRunning: false, Kind: not NotificationKind.Status }) == Quick.NotificationsKeep, "kept up to CRAFT_NOTIFICATIONS_KEEP");
        Check.True(centre.DismissAll("host") > 0 && centre.Groups.All(g => g.Source != "host"), "a group dismissed at once");
        Check.True(!centre.Dismiss(running.Id), "a running operation is not dismissed (it is cancelled)");
        running.Dispose();
        status.Dispose();
        Check.True(centre.StatusOf("ai").Count == 0, "a status line goes when disposed");
        message.Dispose();
        question.Dispose();
    }

    [Test]
    public static void APlugInPostsThroughTheContractUnderItsName()
    {
        var plugin = new NotifyingPlugin("u-notifier");
        EditorPlugins.Register(plugin);
        try
        {
            using var host = new EditorHost(Quick);
            host.RefreshPlugins();
            var group = host.Notifications.Groups.Single(g => g.Source == "u-notifier");
            Check.True(group.Entries.Any(e => e is { Kind: NotificationKind.Message, Title: "Indexed", Severity: NotificationSeverity.Success }), "its message, under its name");
            Check.True(group.Entries.Any(e => e is { Kind: NotificationKind.Status, Title: "Index" }), "its status line");
            Check.True(group.Entries.Any(e => e is { IsRunning: true, Title: "Indexing" }), "its running operation, under its name");
            EditorPlugins.Unregister(plugin.Name);
            host.RefreshPlugins();
            var left = host.Notifications.Entries.Where(e => e.Source == "u-notifier").ToArray();
            Check.True(left.All(e => e.Kind == NotificationKind.Message || (e.Kind == NotificationKind.Activity && !e.IsRunning)), "deactivated: its status line goes and its operation ends");
            Check.True(plugin.Running!.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)) || plugin.Running.IsEnded, "its operation was cancelled");
        }
        finally
        {
            EditorPlugins.Unregister(plugin.Name);
        }
    }

    private sealed class NotifyingPlugin(string name) : IEditorPlugin
    {
        public string Name => name;

        public string Summary => "test: posts a message, a status line and an operation";

        public IReadOnlyList<PluginSetting> Settings => [];

        public IActivity? Running { get; private set; }

        public IDisposable Activate(IEditorHost host)
        {
            host.Notify("Indexed", "120 files", NotificationSeverity.Success);
            host.ShowStatus("Index", "120 files");
            Running = host.Activities.Start("Indexing", ActivityKinds.Indexing, cancellable: true);
            return new Done();
        }

        private sealed class Done : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

/// <summary>Phase U's UI watchdog and the build-time check against synchronous waits on the UI side.</summary>
[TestGroup("Ui")]
public static class UiTests
{
    // The test machine is shared: a stall here is a tick later than this, far above a frame, far below a real freeze.
    private const int TestStallMilliseconds = 250;

    [Test]
    public static void TheWatchdogReportsADeliberateBlock()
    {
        using var folder = new TempFolder();
        using var ui = new FakeUiThread();
        var labels = new UiWorkLabels();
        var reports = new UiStallReports(new CraftOptions { CrashFolder = folder.File("crashes"), UiStallReports = 5 });
        using var watchdog = new UiWatchdog(ui.Dispatch, labels, 20, 150) { WriteReport = reports.Write };
        var stalls = new ConcurrentQueue<UiStall>();
        watchdog.Stalled += (_, stall) => stalls.Enqueue(stall);
        watchdog.Start();
        ui.Dispatch(() => labels.Run("test: a deliberate block", () => Thread.Sleep(700)));
        var until = Stopwatch.StartNew();
        while (stalls.IsEmpty && until.Elapsed < TimeSpan.FromSeconds(10))
        {
            Thread.Sleep(20);
        }

        Check.True(stalls.TryPeek(out var stall), "the block was seen");
        Check.True(stall!.Label == "test: a deliberate block", $"named by the work that ran ({stall.Label})");
        Check.True(stall.Duration >= TimeSpan.FromMilliseconds(500), $"for as long as it blocked ({stall.Duration.TotalMilliseconds:F0} ms)");
        Check.True(stall.ReportPath is { } path && File.Exists(path) && File.ReadAllText(path).Contains("test: a deliberate block", StringComparison.Ordinal),
            "a stall report under the crash folder names it");
        Check.True(watchdog.StallCount >= 1 && watchdog.Worst >= TimeSpan.FromMilliseconds(500), "counted, and the worst kept");
    }

    [Test]
    public static void TheWatchdogStaysSilentThroughTypingALoadAnAgentRunAndATerminalFlood()
    {
        using var ui = new FakeUiThread();
        var labels = new UiWorkLabels();
        using var watchdog = new UiWatchdog(ui.Dispatch, labels, 20, TestStallMilliseconds);
        watchdog.Start();
        var center = new ActivityCenter(action => ui.Dispatch(() => labels.Run("activities: show", action)), new CraftOptions { ActivityMinimumMilliseconds = 0 });
        var updates = 0;
        center.Changed += (_, _) => updates++;
        EditorModel? model = null;
        ui.Run(() => model = Editors.Make("class A\n{\n}\n").Model);

        // A fake model load and a fake agent run report from the thread pool while the user types.
        var load = Task.Run(async () =>
        {
            using var activity = center.Start("Loading a 7B model", ActivityKinds.ModelLoad);
            for (int i = 0; i < 200; i++)
            {
                activity.Report(i / 200.0, "reading the weights", new ActivityRate(800, "MB/s"));
                await Task.Delay(5);
            }
        });
        var agent = Task.Run(async () =>
        {
            using var activity = center.Start("Agent: a task", ActivityKinds.Agent, cancellable: true);
            for (int turn = 1; turn <= 100; turn++)
            {
                activity.Report(null, $"turn {turn} of 100", new ActivityRate(40, "tok/s"));
                await Task.Delay(8);
            }
        });

        // A terminal flood: 4 MB parsed on the reader's thread in slices under the screen's lock, the UI painting it.
        var screen = new TerminalScreen(120, 30, 5000);
        var flood = Task.Run(() =>
        {
            var line = Encoding.UTF8.GetBytes("\e[32mline of output from a build\e[0m 0123456789 abcdefghijklmnopqrstuvwxyz\r\n");
            var block = new byte[16 * 1024];
            for (int i = 0; i + line.Length <= block.Length; i += line.Length)
            {
                line.CopyTo(block, i);
            }

            for (int sent = 0; sent < 4 * 1024 * 1024; sent += block.Length)
            {
                lock (screen.Sync)
                {
                    screen.Feed(block);
                }
            }
        });

        // Typing: 600 keys, each its own UI turn, with a paint read of the terminal between.
        for (int i = 0; i < 600; i++)
        {
            char c = (char)('a' + (i % 26));
            ui.Dispatch(() => labels.Run("editor: typed text", () => model!.OnText(c.ToString())));
            ui.Dispatch(() => labels.Run("terminal: paint", () =>
            {
                lock (screen.Sync)
                {
                    _ = screen.Rows;
                }
            }));
            if (i % 20 == 0)
            {
                Thread.Sleep(5);
            }
        }

        Task.WaitAll([load, agent, flood], TimeSpan.FromSeconds(60));
        ui.Run(() => { });
        Thread.Sleep(100);
        Check.True(load.IsCompletedSuccessfully && agent.IsCompletedSuccessfully && flood.IsCompletedSuccessfully, "the load, the agent and the flood ran to their ends");
        Check.True(watchdog.Ticks > 10, $"the watchdog ticked ({watchdog.Ticks})");
        Check.Equal(0L, watchdog.StallCount, $"no stall over {TestStallMilliseconds} ms (worst tick {watchdog.Worst.TotalMilliseconds:F0} ms; stalls: {string.Join("; ", watchdog.Stalls.Select(s => $"{s.Label} {s.Duration.TotalMilliseconds:F0} ms"))})");
        Check.True(updates > 0 && updates < 600, $"the activities' reports reached the UI coalesced ({updates} updates for 300 reports)");
        Check.True(model!.Snapshot.Length == "class A\n{\n}\n".Length + 600, "every key was typed");
    }

    /// <summary>
    /// The places a synchronous wait is allowed on the UI side, each with why: a task already completed, or a thread of
    /// its own that is not the UI's. A line matching none fails the test with its file and line.
    /// </summary>
    private static readonly (string File, string Code, string Why)[] Allowed =
    [
        ("src/Craft.Ai/AiModels.cs", "t.Result.Dispose()", "a continuation of the completed lease (IsCompletedSuccessfully checked)"),
        ("src/Craft.Ai/AiModels.cs", "var held = done.Result;", "a continuation of the completed lease (IsCompletedSuccessfully checked)"),
        ("src/Craft.Editor/EditorModel.Semantic.cs", "apply(done.Result);", "a continuation of the completed request (IsCompletedSuccessfully checked)"),
        ("plugins/Craft.Plugins.Sandbox/SandboxSession.cs", "asked.TrySetResult(t.Result);", "a continuation of the completed question (neither cancelled nor faulted)"),
        ("plugins/Craft.Plugins.Learning/LearningSession.cs", "_consents.IsCompletedSuccessfully ? _consents.Result", "the consents' task, read only once it completed"),
        ("plugins/Craft.Plugins.Learning/LearningTrainer.cs", ".AsTask().GetAwaiter().GetResult();", "the training's own thread (the session runs every round in Task.Run), never the UI's"),
        ("plugins/Craft.Plugins.Learning/LearningTrainer.cs", "PlayTasksAsync(policy, heldOutSuite, settings, 2, null, cancellationToken).GetAwaiter().GetResult()", "the training's own thread (the session runs every round in Task.Run), never the UI's"),
        ("src/Craft.Editor/Activities/UiWatchdog.cs", "if (_stop.Wait(_tick))", "the watchdog's own thread sleeps on its stop signal"),
        ("src/Craft.Editor/Activities/UiWatchdog.cs", "_stop.Wait(rest)", "the watchdog's own thread sleeps on its stop signal"),
        ("src/Craft.Maui/Platforms/Windows/ConPtyTerminalHost.cs", "_ = unregistered.WaitOne(TimeSpan.FromSeconds(5));", "Release runs in Task.Run (Dispose hands it off), never on the UI thread: it waits for a running exit callback before the handles close"),
        ("src/Craft.App/Platforms/Windows/CrashDialog.cs", "_ = trail.FlushAsync().Wait(TimeSpan.FromMilliseconds(500));", "the crash path only, the app already closing: the trail's last lines are flushed, bounded to 500 ms, before the message box"),
        ("src/Craft.Editor/DispatchedClipboard.cs", "done.TrySetResult(task.Result);", "a continuation of the completed clipboard call (neither faulted nor cancelled)"),
        ("src/Craft.Editor/EditorFind.cs", "match.Result(replacement)", "Regex's Match.Result (a replacement's groups put in), not a task's: nothing waits"),
    ];

    private static readonly Regex SynchronousWait = new(@"\.Result\b(?!s)|\.Wait\(|\.WaitOne\(|\.WaitAll\(|GetAwaiter\(\)\.GetResult\(\)|Monitor\.Wait\(|Thread\.Sleep\(|SpinWait\.SpinUntil\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public static void TheUiSideHasNoSynchronousWait()
    {
        string root = TestRunner.RepositoryPath();
        string[] folders = ["src/Craft.Maui", "src/Craft.App", "src/Craft.Ai", "src/Craft.Editor", "plugins"];
        var found = new List<string>();
        var used = new HashSet<int>();
        int files = 0;
        foreach (var folder in folders)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                files++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string code = line.TrimStart();
                    if (code.StartsWith("//", StringComparison.Ordinal) || !SynchronousWait.IsMatch(line))
                    {
                        continue;
                    }

                    int allowed = Array.FindIndex(Allowed, a => a.File == relative && line.Contains(a.Code, StringComparison.Ordinal));
                    if (allowed >= 0)
                    {
                        used.Add(allowed);
                        continue;
                    }

                    found.Add($"{relative}:{i + 1}: {code}");
                }
            }
        }

        Check.True(files > 100, $"the UI side's sources were read ({files} files)");
        Check.True(found.Count == 0, "a synchronous wait on the UI side (run it off the UI thread and come back through the dispatcher, or allow it here with why):\n  "
            + string.Join("\n  ", found));
        var stale = Allowed.Where((_, i) => !used.Contains(i)).Select(a => $"{a.File}: {a.Code}").ToArray();
        Check.True(stale.Length == 0, "an allowed wait that is no longer there (take it off the list):\n  " + string.Join("\n  ", stale));
    }

    // The owner's note 26: "the checkbox is so gross". A list in SelectionMode.Multiple draws a checkbox per row on
    // Windows; the bottom panel's lists are drawn rows (PanelListView) and its Output a read-only editor.
    private static readonly Regex CheckboxList = new(@"SelectionMode\s*\.\s*Multiple|SelectionMode\s*=\s*SelectionMode\.Multiple",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public static void TheShellHasNoCheckboxList()
    {
        string root = TestRunner.RepositoryPath();
        var found = new List<string>();
        int files = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src/Craft.Maui"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            files++;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (!code.StartsWith("//", StringComparison.Ordinal) && !code.StartsWith("///", StringComparison.Ordinal) && CheckboxList.IsMatch(code))
                {
                    found.Add($"{relative}:{i + 1}: {code}");
                }
            }
        }

        Check.True(files > 20, $"the shell's sources were read ({files} files)");
        Check.True(found.Count == 0, "a list in SelectionMode.Multiple (a checkbox per row on Windows): draw the rows (PanelListView) or show the text read-only (EditorView.IsReadOnly):\n  "
            + string.Join("\n  ", found));
        string panel = File.ReadAllText(Path.Combine(root, "src/Craft.Maui/ShellChromeViews.cs"));
        string bottom = panel[panel.IndexOf("public sealed class BottomPanelView", StringComparison.Ordinal)..panel.IndexOf("public sealed class StatusBarView", StringComparison.Ordinal)];
        Check.True(!bottom.Contains("CollectionView", StringComparison.Ordinal) && !bottom.Contains("CheckBox", StringComparison.Ordinal) && !bottom.Contains("SelectableLabel", StringComparison.Ordinal),
            "the bottom panel has no list of row controls, no checkbox and no one-line selectable label");
    }
}
