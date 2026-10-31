// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Abstractions;
using Craft.Core.Diagnostics;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's note 21: the crash reporter that needs no hook. The run marker found at the next start, the breadcrumb
/// trail (its lines, its ring, nothing allocated per key), Windows' event XML read into records, the problem report and
/// the guard of the platform's callbacks.
/// </summary>
[TestGroup("CrashWatch")]
public static class CrashWatchTests
{
    [Test]
    public static async Task TheTrailRecordsKeysCommandsLabelsOutputAndExceptionsNeverTheText()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "trail-1.txt");
        using var trail = new CrashTrail(path, 256, 20);
        trail.Key(EditorKey.C, KeyModifiers.Control | KeyModifiers.Shift);
        trail.Command("terminal.copy");
        const string Paint = "shell: paint";
        for (int i = 0; i < 5; i++)
        {
            trail.Label(Paint);
        }

        trail.Output("Error", "Terminal", "the selection could not be copied");
        trail.Exception(MakeException());
        trail.Exception(new OperationCanceledException("a cancellation is not a fault"));
        trail.Note("a note");
        await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var lines = CrashTrail.Read(path);
        var text = string.Join("\n", lines);
        foreach (var expected in (string[])["key  Ctrl+Shift+C", "cmd  terminal.copy", "ui   shell: paint", "(again ×4)", "out  Terminal Error: the selection could not be copied",
            "exc  System.InvalidOperationException: boom | at", "note a note"])
        {
            Check.True(text.Contains(expected, StringComparison.Ordinal), $"the trail holds '{expected}':\n{text}");
        }

        Check.True(!text.Contains("cancellation", StringComparison.Ordinal), "a cancellation is left out");
        Check.Equal(1, lines.Count(l => l.Contains("ui   shell: paint", StringComparison.Ordinal)), "repeats of a label are written once with their count");
    }

    [Test]
    public static async Task TheTrailIsARingOfTwoFilesBoundedByItsSize()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "trail-2.txt");
        using var trail = new CrashTrail(path, 16, 0);
        for (int i = 0; i < 3000; i++)
        {
            trail.Note("line number " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of a trail that outgrows its ring");
            if (i % 500 == 0)
            {
                await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        long size = new FileInfo(path).Length + new FileInfo(trail.OlderPath).Length;
        Check.True(File.Exists(trail.OlderPath), "the older half exists once the first passed half the size");
        Check.True(size <= (16 * 1024) + 8192, $"both files stay near the ring's 16 KiB: {size} bytes");
        var lines = CrashTrail.Read(path);
        Check.True(lines[^1].Contains("line number 2999", StringComparison.Ordinal) && !lines[0].Contains("line number 0 ", StringComparison.Ordinal), "the newest lines are kept, the oldest dropped");
        Check.Equal(10, CrashTrail.Read(path, 10).Count, "the last lines asked for");
    }

    [Test]
    public static async Task AKeyOnTheTrailAllocatesNothingOnTheCallersThread()
    {
        using var folder = new TempFolder();
        using var trail = new CrashTrail(Path.Combine(folder.Path, "trail-3.txt"), 1024, 0);
        for (int i = 0; i < 5000; i++)
        {
            trail.Key(EditorKey.A + (i % 20), KeyModifiers.None);     // the channel's queue and the writer warm up
        }

        await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        const int Keys = 20_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Keys; i++)
        {
            trail.Key(EditorKey.A + (i % 20), KeyModifiers.Control);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(allocated <= Keys / 4, $"{Keys} keys allocated {allocated} bytes on the caller's thread (at most a few bytes a hundred keys, the queue's growth)");
    }

    [Test]
    public static void ARunThatDidNotEndCleanlyIsFoundAtTheNextStartOnce()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { CrashFolder = folder.Path };
        var t0 = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);
        var first = CrashWatch.Begin(options, "1.0", static _ => false, 111, "Craft.App", t0, watchExceptions: false);
        Check.True(first.UncleanRuns.Count == 0 && File.Exists(first.Current.Path) && first.Trail is not null, "the first run writes its marker and starts its trail");
        first.Trail!.Command("craft.save");
        first.Trail.FlushAsync().Wait(TimeSpan.FromSeconds(10));
        first.Dispose();                                              // killed: no clean exit, the marker stays

        var second = CrashWatch.Begin(options, "1.0", static marker => marker.ProcessId == 999, 222, "Craft.App", t0.AddMinutes(5), watchExceptions: false);
        Check.True(second.UncleanRuns is [{ ProcessId: 111, FoundDead: not null }], "the next start finds the run that died");
        var died = second.UncleanRuns[0];
        Check.True(died.Path is { } diedPath && diedPath.EndsWith(CrashWatch.DiedSuffix, StringComparison.Ordinal) && File.Exists(diedPath) && !File.Exists(first.Current.Path),
            "its marker is kept as died");
        Check.True(died.TrailPath is { } kept && CrashTrail.Read(kept).Any(l => l.Contains("craft.save", StringComparison.Ordinal)), "its trail is kept, renamed with its start, and readable");

        var third = CrashWatch.Begin(options, "1.0", static marker => marker.ProcessId == 222, 333, "Craft.App", t0.AddMinutes(10), watchExceptions: false);
        Check.True(third.UncleanRuns.Count == 0, "a run found once is not offered again, and a run still alive is not a crash");
        Check.True(third.DiedRuns() is [{ ProcessId: 111 }], "it is still listed for Help › Report a problem");
        second.EndCleanly();
        Check.True(!File.Exists(second.Current.Path) && !File.Exists(second.Current.TrailPath!), "a clean exit removes its marker and trail");
        third.EndCleanly();

        var off = CrashWatch.Begin(options with { CrashTrail = false }, "1.0", static _ => true, 444, "Craft.App", t0, watchExceptions: false);
        Check.True(off.Trail is null && off.Current.TrailPath is null && File.Exists(off.Current.Path), "CRAFT_CRASH_TRAIL=false: the marker without a trail");
        off.EndCleanly();
    }

    [Test]
    public static void WindowsEventXmlIsReadIntoTheRunsRecords()
    {
        var query = new CrashRecordQuery(0x1f2c, "Craft.App", new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 31, 10, 30, 0, TimeSpan.Zero));
        var fault = FaultXml("0x1f2c", named: true);
        var record = EventLogCrashXml.Parse(fault, query);
        Check.True(record is { Source: "Application log, Application Error 1000" }, "Application Error 1000 of the run's process");
        Check.True(record!.Summary.Contains("Microsoft.ui.xaml.dll", StringComparison.Ordinal) && record.Summary.Contains("0xc000027b", StringComparison.Ordinal)
            && record.Summary.Contains("stowed exception", StringComparison.Ordinal) && record.Summary.Contains("0xab12c", StringComparison.Ordinal), "its module, code, meaning and offset: " + record.Summary);
        Check.True(record.Text.Contains("AppPath: D:\\Projects\\Craft\\Craft.App.exe", StringComparison.Ordinal), "the record's fields");
        Check.True(EventLogCrashXml.Parse(FaultXml("0x10", named: true), query) is null, "another process's fault is not the run's");
        Check.True(EventLogCrashXml.Parse(FaultXml("0x1f2c", named: false), query) is { } unnamed && unnamed.Summary.Contains("Microsoft.ui.xaml.dll", StringComparison.Ordinal),
            "fields rendered without names are read in their order");
        var runtime = EventLogCrashXml.Parse(RuntimeXml("Craft.App.exe", "2026-10-31T10:12:00.0000000Z"), query);
        Check.True(runtime is { Summary: ".NET: System.InvalidOperationException: the managed failure" } && runtime.Text.Contains("at Craft.Maui.TerminalView", StringComparison.Ordinal),
            ".NET Runtime 1026 matched by the application's name, with its stack");
        Check.True(EventLogCrashXml.Parse(RuntimeXml("Other.exe", "2026-10-31T10:12:00.0000000Z"), query) is null, "another application's");
        Check.True(EventLogCrashXml.Parse(RuntimeXml("Craft.App.exe", "2026-10-30T10:12:00.0000000Z"), query) is null, "outside the run's time");
        Check.True(EventLogCrashXml.Parse("<not xml", query) is null, "XML that does not parse is no record");
        var xpath = EventLogCrashXml.XPath(query);
        Check.True(xpath.Contains("@SystemTime>='2026-10-31T09:59:00.000Z'", StringComparison.Ordinal) && xpath.Contains("EventID=1026", StringComparison.Ordinal), "the query's window and events: " + xpath);
    }

    [Test]
    public static async Task AProblemReportBundlesTheRunAndScrubsTheUser()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { CrashFolder = folder.Path };
        var start = DateTimeOffset.Now.AddMinutes(-2);
        var dead = CrashWatch.Begin(options, "1.0", static _ => false, 501, "Craft.App", start, watchExceptions: false);
        dead.Trail!.Note("the user mailed someone@example.com");
        dead.Trail.Key(EditorKey.C, KeyModifiers.Control);
        await dead.Trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "crash-20261031-100000-000.txt"), "Craft crash report\nCaught by: the window\n");
        dead.Dispose();
        var next = CrashWatch.Begin(options, "1.0", static _ => false, 502, "Craft.App", DateTimeOffset.Now, watchExceptions: false);
        var run = next.UncleanRuns.Single();
        var source = new FakeRecords();
        var content = CrashTicket.Gather(folder.Path, run, died: true, source, [new TicketPlugin("Craft.CSharp", "0.1.0", "loaded")], 100, next.Current);
        var text = CrashTicket.Format(content);
        foreach (var expected in (string[])["== The run ==", "Process 501 (Craft.App), Craft 1.0", "did not close cleanly", "Application log, Application Error 1000",
            "Faulting module KERNELBASE.dll", "--- crash-20261031-100000-000.txt ---", "Caught by: the window", "key  Ctrl+C", "Craft.CSharp 0.1.0: loaded", "== App and system =="])
        {
            Check.True(text.Contains(expected, StringComparison.Ordinal), $"the report holds '{expected}':\n{text}");
        }

        Check.True(source.Asked is { ProcessId: 501, ProcessName: "Craft.App" }, "the platform's records were asked for the run's process");
        Check.True(!text.Contains("someone@example.com", StringComparison.Ordinal) && text.Contains("<e-mail>", StringComparison.Ordinal), "no e-mail address is written");
        Check.Equal("<profile>\\src at <user>'s <machine>, not jdoes", CrashTicket.Scrub("C:\\Users\\JDoe\\src at jdoe's DESK-7, not jdoes", "jdoe", "C:\\Users\\JDoe", "DESK-7"),
            "the profile folder, the user name as a word and the machine name are replaced");
        var path = CrashTicket.Write(folder.Path, text);
        Check.True(Path.GetFileName(path).StartsWith(CrashTicket.Prefix, StringComparison.Ordinal) && await File.ReadAllTextAsync(path) == text, "written as ticket-<time>.txt in the crash folder");
        next.EndCleanly();
    }

    [Test]
    public static async Task ReportAProblemIsOfferedAfterACrashWrittenReviewedAndCopied()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { CrashFolder = folder.Path, CrashRecords = CrashRecordSources.None };
        CrashWatch.Begin(options, "1.0", static _ => false, 601, "Craft.App", DateTimeOffset.Now.AddMinutes(-1), watchExceptions: false).Dispose();
        var watch = CrashWatch.Begin(options, "1.0", static _ => false, 602, "Craft.App", DateTimeOffset.Now, watchExceptions: false);
        using var host = new EditorHost(options);
        var presenter = new ScriptedPresenter(0, 0, 1, 3);            // write it; Copy; Open folder; Close
        host.Presenter = presenter;
        var clipboard = new FakeClipboard();
        var opened = new List<string>();
        using (var report = new ProblemReport(host, watch, [], clipboard, f =>
        {
            opened.Add(f);
            return Task.CompletedTask;
        }))
        {
            Check.True(EditorCommands.Find(ProblemReport.CommandId) is not null && EditorMenus.All.Any(i => i.Menu == EditorMenus.Help && i.CommandId == ProblemReport.CommandId),
                "Help › Report a problem is registered");
            await report.OfferAsync();
            Check.True(presenter.Asked is ["Craft did not close cleanly last time", "Problem report written", "Problem report written", "Problem report written"], "offered, then reviewed: " + string.Join(", ", presenter.Asked));
            Check.True(report.LastPath is { } path && File.Exists(path) && clipboard.Text == await File.ReadAllTextAsync(path), "the report is written and Copy put it on the clipboard");
            Check.True(clipboard.Text!.Contains("Process 601", StringComparison.Ordinal) && clipboard.Text.Contains("No crash record source", StringComparison.Ordinal), "it is the run that died");
            Check.True(opened is [var opened0] && opened0 == Path.GetFullPath(folder.Path), "Open folder opens the crash folder");
        }

        Check.True(EditorCommands.Find(ProblemReport.CommandId) is null, "disposed, the command is taken back");
        watch.EndCleanly();
    }

    [Test]
    public static void TheCrashRecordSourcesHaveNoneByDefaultAndRefuseAnUnknownName()
    {
        Check.True(CrashRecordSources.Resolve("auto").Name == CrashRecordSources.None && CrashRecordSources.Resolve(null).Name == CrashRecordSources.None, "auto with no platform source: none");
        Check.Throws<NotRegisteredException>(() => CrashRecordSources.Resolve("no-such-source"), "an unknown source");
        var fake = new FakeRecords();
        CrashRecordSources.Register(fake);
        try
        {
            Check.True(ReferenceEquals(CrashRecordSources.Resolve("auto"), fake) && ReferenceEquals(CrashRecordSources.Resolve("FAKE-RECORDS"), fake), "auto takes the platform's; a name ignores case");
        }
        finally
        {
            CrashRecordSources.Unregister(fake.Name);
        }
    }

    [Test]
    public static void ACaughtCallbackFailureIsReportedOnceAndTheAppGoesOn()
    {
        using var folder = new TempFolder();
        CallbackGuard.Reset();
        var previous = CallbackGuard.Options;
        CallbackGuard.Options = new CraftOptions { CrashFolder = folder.Path };
        var failures = new List<CallbackFailure>();
        void OnFailed(object? sender, CallbackFailure failure) => failures.Add(failure);
        CallbackGuard.Failed += OnFailed;
        try
        {
            Check.True(!CallbackGuard.Run("a test callback", static () => throw new InvalidOperationException("thrown in a callback")), "the guard catches and says so");
            Check.True(!CallbackGuard.Run("a test callback", static () => throw new InvalidOperationException("again")), "and again");
            Check.True(CallbackGuard.Run("a test callback", static () => { }), "a callback that does not throw");
            Check.True(failures is [{ ReportPath: { } report }, { ReportPath: null }] && File.ReadAllText(report).Contains("thrown in a callback", StringComparison.Ordinal),
                "both raised, the report written once for the place and type");
            Check.True(Path.GetFileName(failures[0].ReportPath!).StartsWith("callback-", StringComparison.Ordinal), "a callback-<time>.txt the problem report folds in");
        }
        finally
        {
            CallbackGuard.Failed -= OnFailed;
            CallbackGuard.Options = previous;
            CallbackGuard.Reset();
        }
    }

    private static InvalidOperationException MakeException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }

    private static string FaultXml(string pid, bool named)
    {
        string[] names = ["AppName", "AppVersion", "AppTimeStamp", "ModuleName", "ModuleVersion", "ModuleTimeStamp", "ExceptionCode", "FaultingOffset", "ProcessId", "ProcessCreationTime", "AppPath", "ModulePath", "IntegratorReportId"];
        string[] values = ["Craft.App.exe", "0.1.0.0", "6720a1b2", "Microsoft.ui.xaml.dll", "3.1.5.0", "671f0000", "c000027b", "00000000000ab12c", pid, "0x1db2b6f0e2c4a10", "D:\\Projects\\Craft\\Craft.App.exe", "C:\\x\\Microsoft.ui.xaml.dll", "a-b-c"];
        var data = string.Concat(names.Select((n, i) => named ? $"<Data Name='{n}'>{values[i]}</Data>" : $"<Data>{values[i]}</Data>"));
        return "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error'/><EventID Qualifiers='0'>1000</EventID>"
            + "<TimeCreated SystemTime='2026-10-31T10:12:13.4567890Z'/><Channel>Application</Channel></System><EventData>" + data + "</EventData></Event>";
    }

    private static string RuntimeXml(string application, string time) =>
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='.NET Runtime'/><EventID Qualifiers='0'>1026</EventID>"
        + $"<TimeCreated SystemTime='{time}'/></System><EventData><Data>Application: {application}&#xD;&#xA;CoreCLR Version: 10.0.0&#xD;&#xA;Description: The process was terminated due to an unhandled exception.&#xD;&#xA;"
        + "Exception Info: System.InvalidOperationException: the managed failure&#xD;&#xA;   at Craft.Maui.TerminalView.OnDrag()</Data></EventData></Event>";

    private sealed class FakeRecords : ICrashRecordSource
    {
        public CrashRecordQuery? Asked { get; private set; }

        public string Name => "fake-records";

        public string Summary => "a test's records";

        public string? Unavailable => null;

        public IReadOnlyList<CrashRecord> Read(CrashRecordQuery query, CancellationToken cancellationToken = default)
        {
            Asked = query;
            return [new CrashRecord(query.To, "Application log, Application Error 1000", "Faulting module KERNELBASE.dll, exception code 0xe0434352", "ModuleName: KERNELBASE.dll\n")];
        }
    }

    /// <summary>Answers each question with the next answer given, and keeps the titles asked.</summary>
    private sealed class ScriptedPresenter(params int[] answers) : IEditorPresenter
    {
        private int _next;

        public List<string> Asked { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message)
        {
            Asked.Add(title);
            return Task.CompletedTask;
        }

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question.Title);
            return Task.FromResult(_next < answers.Length ? answers[_next++] : -1);
        }
    }
}

/// <summary>
/// The owner's notes 19 and 20: copying from the bottom panel's lists and from the terminal, and the clipboard reached
/// through the dispatcher so it is never called inside an input callback and never throws into one.
/// </summary>
[TestGroup("Copy")]
public static class CopyTests
{
    [Test]
    public static async Task TheDispatchedClipboardWaitsForTheDispatcherAndTurnsEveryFailureIntoTheTasks()
    {
        var queue = new Queue<Action>();
        var platform = new FakeClipboard();
        var failures = new List<Exception>();
        var clipboard = new DispatchedClipboard(platform, queue.Enqueue, failures.Add);
        var set = clipboard.SetTextAsync("copied");
        Check.True(!set.IsCompleted && platform.Text is null && queue.Count == 1, "nothing reaches the platform inside the caller (a key's handler): it is queued");
        queue.Dequeue()();
        await set.WaitAsync(TimeSpan.FromSeconds(5));
        Check.Equal("copied", platform.Text, "the text reached the platform from the dispatcher's queue");
        var get = clipboard.GetTextAsync();
        queue.Dequeue()();
        Check.Equal("copied", await get.WaitAsync(TimeSpan.FromSeconds(5)), "a paste reads through the queue too");

        var throwing = new DispatchedClipboard(new ThrowingClipboard(), queue.Enqueue, failures.Add);
        var failed = throwing.SetTextAsync("x");
        queue.Dequeue()();                                            // the platform throws at once: the callback must not
        var error = await Captured(failed);
        Check.True(error is System.Runtime.InteropServices.COMException && failures is [System.Runtime.InteropServices.COMException], "a platform's throw is the task's failure and reported, never the dispatcher's");
        var refused = new DispatchedClipboard(platform, static _ => throw new InvalidOperationException("the window is closing"), failures.Add);
        Check.True(await Captured(refused.SetTextAsync("y")) is InvalidOperationException && failures.Count == 2, "a dispatcher that refuses fails the task, not the caller");
    }

    [Test]
    public static void ThePanelsCopyRowsAsTheyShowAndProblemsAsABuildToolWritesThem()
    {
        Check.Equal("Program.cs(12,5): error CS1002: ; expected", PanelCopy.Problem("Program.cs", 12, 5, DiagnosticSeverity.Error, "CS1002", "; expected"), "an error");
        Check.Equal("(untitled)(1,1): warning IDE0005: Using directive is unnecessary. Two lines.", PanelCopy.Problem(null, 1, 1, DiagnosticSeverity.Warning, "IDE0005", "Using directive is unnecessary.\nTwo lines."),
            "a warning of an untitled file, its message on one line");
        var rows = new[] { new Row("a"), new Row("b"), new Row("c") };
        Check.SequenceEqual([rows[0], rows[2]], PanelCopy.InListOrder(rows, [rows[2], rows[0], new Row("gone")]), "the chosen rows in the list's order; a row no longer listed is left out");
        Check.Equal($"a{Environment.NewLine}c", PanelCopy.Lines(["a", "c"]), "the lines joined by the system's line break");
    }

    [Test]
    public static async Task CtrlCInATerminalCopiesTheSelectionElseGoesToTheShell()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions();
        using var host = new EditorHost(options);
        var clipboard = new FakeClipboard();
        using var manager = new Craft.Editor.Terminal.TerminalManager(host, () => options, clipboard) { HomeFolder = folder.Path };
        var output = manager.OpenOutput("copy test");
        Check.True(output.Append("copy-me please\r\n"), "the output tab takes text");
        var terminal = manager.ActiveTerminal!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !ScreenText(terminal).Contains("copy-me", StringComparison.Ordinal))
        {
            await Task.Delay(20);
        }

        Check.True(ScreenText(terminal).Contains("copy-me", StringComparison.Ordinal), "the text is on the screen");
        long line = terminal.Screen.FirstLine;
        terminal.Selection = new Craft.Editor.Terminal.TerminalSelection(line, 0, line, 7);
        Check.True(manager.Input.OnKey(EditorKey.C, KeyModifiers.Control), "Ctrl+C with a selection is taken");
        await Task.Delay(50);
        Check.Equal("copy-me", clipboard.Text, "the selection is on the clipboard");
        Check.True(terminal.Selection is null, "and the selection ends, as a terminal's copy does");
        clipboard.Text = null;
        _ = manager.Input.OnKey(EditorKey.C, KeyModifiers.Control);
        Check.True(clipboard.Text is null, "with no selection Ctrl+C is the shell's (nothing copied)");
        options = options with { TerminalCtrlCCopies = false };
        terminal.Selection = new Craft.Editor.Terminal.TerminalSelection(line, 0, line, 7);
        Check.True(!manager.TryCopyOnCtrlC() && clipboard.Text is null, "CRAFT_TERMINAL_CTRL_C_COPIES=false: Ctrl+C stays the shell's");
    }

    [Test]
    public static async Task ACopyThatFailsInTheTerminalIsLoggedNotThrown()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions();
        using var host = new EditorHost(options);
        var logged = new List<string>();
        host.Logged += (_, entry) => logged.Add(entry.Message);
        using var manager = new Craft.Editor.Terminal.TerminalManager(host, () => options, new ThrowingClipboard()) { HomeFolder = folder.Path };
        var output = manager.OpenOutput("copy test");
        output.Append("fails to copy\r\n");
        var terminal = manager.ActiveTerminal!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !ScreenText(terminal).Contains("fails", StringComparison.Ordinal))
        {
            await Task.Delay(20);
        }

        terminal.Selection = new Craft.Editor.Terminal.TerminalSelection(terminal.Screen.FirstLine, 0, terminal.Screen.FirstLine, 5);
        Check.True(await host.ExecuteCommandAsync(Craft.Editor.Terminal.TerminalCommandIds.Copy), "Ctrl+Shift+C's command ends");
        Check.True(logged.Any(m => m.Contains("could not be copied", StringComparison.Ordinal)), "the clipboard's failure is said in Output: " + string.Join(" | ", logged));
        terminal.Selection = new Craft.Editor.Terminal.TerminalSelection(terminal.Screen.FirstLine, 0, terminal.Screen.FirstLine, 5);
        Check.True(manager.Input.OnKey(EditorKey.C, KeyModifiers.Control), "Ctrl+C's copy does not throw into the key's handler");
    }

    private static string ScreenText(Craft.Editor.Terminal.TerminalInstance terminal)
    {
        lock (terminal.Screen.Sync)
        {
            return terminal.Screen.TextOf(terminal.Screen.FirstLine, 0, terminal.Screen.ScreenTop + terminal.Screen.Rows - 1, terminal.Screen.Columns);
        }
    }

    private static async Task<Exception?> Captured(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
#pragma warning disable CA1031 // The test looks at what the task failed with.
        catch (Exception error)
#pragma warning restore CA1031
        {
            return error;
        }
    }

    private sealed record Row(string Text);

    /// <summary>A clipboard that throws as WinUI's can (a COM error at once, inside the call).</summary>
    private sealed class ThrowingClipboard : IEditorClipboard
    {
        public Task<string?> GetTextAsync() => throw new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0));

        public Task SetTextAsync(string text) => throw new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0));
    }
}
