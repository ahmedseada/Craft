// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>The owner's note 26: a read-only editor refuses every edit and still selects, copies and finds.</summary>
[TestGroup("ReadOnly")]
public static class ReadOnlyTests
{
    [Test]
    public static void EveryEditPathIsRefusedAndReadingStillWorks()
    {
        string text = $"first line{Environment.NewLine}second line{Environment.NewLine}third";
        var document = new FakeDocument(text);
        var clipboard = new FakeClipboard { Text = "pasted" };
        using var model = new EditorModel(document, null, new CraftOptions(), null, new FixedMeasurer(), clipboard) { IsReadOnly = true };
        model.Select(2, 2);
        model.OnText("x");
        foreach (var (key, modifiers) in (ReadOnlySpan<(EditorKey, KeyModifiers)>)[
            (EditorKey.Backspace, KeyModifiers.None), (EditorKey.Delete, KeyModifiers.None), (EditorKey.Backspace, KeyModifiers.Control),
            (EditorKey.Delete, KeyModifiers.Control), (EditorKey.Enter, KeyModifiers.None), (EditorKey.Tab, KeyModifiers.None),
            (EditorKey.Tab, KeyModifiers.Shift), (EditorKey.X, KeyModifiers.Control), (EditorKey.V, KeyModifiers.Control),
            (EditorKey.Z, KeyModifiers.Control), (EditorKey.Y, KeyModifiers.Control), (EditorKey.Z, KeyModifiers.Control | KeyModifiers.Shift)])
        {
            model.OnKey(key, modifiers);
        }

        model.Select(0, 5);
        model.OnText("typed over the selection");
        model.OnKey(EditorKey.Backspace, KeyModifiers.None);
        foreach (var (id, _, run, _) in StandardShortcuts.LineCommands)
        {
            model.Select(0, 3);
            run(model);
        }

        model.Find.Query = "line";
        model.Find.ReplaceAll("LINE");
        model.Find.ReplaceOne("LINE");
        Check.Equal(text, document.Text, "the text after typing, paste, cut, delete, enter, tab, undo, redo, the line commands and find's replace");
        Check.True(document.Kinds.Count == 0, "no edit reached the document");
        Check.True(model.RefusedEdits > 10, $"the refusals were counted ({model.RefusedEdits})");
        Check.Equal("pasted", clipboard.Text, "a cut copies nothing from a read-only text");

        model.OnKey(EditorKey.A, KeyModifiers.Control);
        Check.Equal(new SelectionState(0, text.Length), model.Selection, "Ctrl+A selects the whole text");
        model.OnKey(EditorKey.C, KeyModifiers.Control);
        Check.Equal(text, clipboard.Text, "Ctrl+C copies across the lines");
        model.PointerPressed(model.TextLeft + 1, 1, KeyModifiers.None);
        model.PointerMoved(model.TextLeft + 1 + (3 * FixedMeasurer.Metrics.CellWidth), 1 + (2 * FixedMeasurer.Metrics.LineHeight));
        model.PointerReleased();
        Check.True(model.Selection is { Start: 0 } && model.Snapshot.GetLineIndex(model.Selection.End) == 2, $"a drag selects across lines ({model.Selection})");
        model.PointerPressed(model.TextLeft + 1 + (2 * FixedMeasurer.Metrics.CellWidth), 1 + FixedMeasurer.Metrics.LineHeight, KeyModifiers.Shift);
        Check.True(model.Selection.Anchor == 0 && model.Selection.Caret > 0, "Shift+click extends from the anchor");
        bool found = false;
        model.FindRequested += (_, _) => found = true;
        model.OnKey(EditorKey.F, KeyModifiers.Control);
        Check.True(found, "Ctrl+F asks for the find");
        Check.True(!model.OnKey(EditorKey.S, KeyModifiers.Control), "Ctrl+S is not taken (the window's Save gets it)");
    }

    [Test]
    public static void TheLogsDocumentRefusesEditsAndTheReadOnlyMenuNeverEdits()
    {
        using var log = new LogSurface(() => 100);
        log.Post("a line");
        log.Flush();
        Check.Throws<InvalidOperationException>(() => log.Document.Edit([TextEdit.Insert(0, "x")], EditKind.Typing, default, default), "the log's document refuses an edit loudly");
        Check.True(log.Document.Undo() is null && !log.Document.CanUndo && !log.Document.IsDirty, "nothing to undo, never dirty");

        using var host = new EditorHost();
        using var model = new EditorModel(log.Shown, LogSurface.Language, new CraftOptions(), null, new FixedMeasurer(), new FakeClipboard()) { IsReadOnly = true };
        using var editor = new EditorModel(new FakeDocument("editable"), null, new CraftOptions(), null, new FixedMeasurer(), new FakeClipboard());
        using var commands = new ShellEditCommands(() => editor);
        var menu = new MenuModel(host);
        menu.Open(EditorContextMenus.ReadOnlyText, new ContextMenuTarget(EditorContextMenus.ReadOnlyText, null, false, model));
        Check.SequenceEqual(["craft.copy", "craft.selectAll", "craft.find"], menu.Rows.Select(r => r.CommandId).ToArray(), "the read-only text's menu: copy, select all, find");
        Check.True(menu.Rows.All(r => r.Enabled), "each can run on the read-only text");
        menu.Close();
        menu.Open(EditorContextMenus.Editor, new ContextMenuTarget(EditorContextMenus.Editor, null, false, model));
        Check.True(menu.Rows.First(r => r.CommandId == "craft.cut") is { Enabled: false } && menu.Rows.First(r => r.CommandId == "craft.paste") is { Enabled: false },
            "on a read-only text the editing items cannot run");
        Check.True(menu.Rows.First(r => r.CommandId == "craft.copy") is { Enabled: true }, "copy still can");
    }
}

/// <summary>The owner's note 26: Output as one read-only text that appends, follows, trims in one step and filters.</summary>
[TestGroup("OutputLog")]
public static class OutputLogTests
{
    private static readonly string NewLine = Environment.NewLine;

    private static (EditorModel Model, List<float> Scrolls) Show(LogSurface log)
    {
        var model = new EditorModel(log.Shown, LogSurface.Language, new CraftOptions { BottomMarginLines = 0 }, null, new FixedMeasurer(), new FakeClipboard()) { IsReadOnly = true };
        var scrolls = new List<float>();
        model.ScrollRequested += (_, e) => scrolls.Add(e.Y);
        model.UpdateViewport(0, 0, 800, 100);                  // five lines of 20
        log.Attach(model);
        return (model, scrolls);
    }

    private static string Text(ITextSnapshot snapshot) => snapshot.GetText(0, snapshot.Length);

    [Test]
    public static void AppendKeepsTheSelectionAndFollowsUntilTheUserScrollsUp()
    {
        using var log = new LogSurface(() => 1000);
        var (model, scrolls) = Show(log);
        using var _ = model;
        bool first = log.Post("line 0");
        bool second = log.Post("line 1");
        Check.True(first && !second, "the first post asks for a flush, the next joins it");
        for (int i = 2; i < 20; i++)
        {
            log.Post($"line {i}");
        }

        int changes = 0;
        log.Shown.Buffer.Changed += (_, _) => changes++;
        Check.Equal(20, log.Flush(), "the batch appended");
        Check.Equal(1, changes, "one change for the whole batch");
        Check.Equal(20, log.LineCount, "twenty lines");
        Check.Equal("line 0" + NewLine + "line 1", model.Snapshot.GetText(0, ("line 0" + NewLine + "line 1").Length), "the lines joined by the system's line break");
        Check.True(log.Following && scrolls[^1] == 300, $"following: the end is scrolled to ({scrolls.LastOrDefault()})");
        model.UpdateViewport(0, 300, 800, 100);               // the view lands there
        model.Select(5, 15);
        log.Post("line 20");
        log.Post("line 21");
        log.Flush();
        Check.Equal(new SelectionState(5, 15), model.Selection, "the selection stays where it was");
        Check.True(scrolls[^1] == 340, $"the end followed ({scrolls[^1]})");
        model.UpdateViewport(0, 340, 800, 100);
        model.UpdateViewport(0, 200, 800, 100);               // the user scrolls up
        Check.True(!log.Following, "scrolling up stops following");
        int before = scrolls.Count;
        log.Post("line 22");
        log.Flush();
        Check.True(scrolls.Count == before, "not following: the view stays where the user put it");
        model.UpdateViewport(0, model.ContentHeight - 100, 800, 100);
        Check.True(log.Following, "back at the end: following again");
        log.Following = false;
        log.Following = true;
        Check.True(scrolls[^1] == model.ContentHeight - 100, "the toggle on scrolls to the end");
        Check.True(!log.Post("a") || log.Flush() == 1, "flushed");
    }

    [Test]
    public static void TheOldestLinesGoInOneStepAndTheViewKeepsItsPlace()
    {
        using var log = new LogSurface(() => 10);
        var (model, scrolls) = Show(log);
        using var _ = model;
        for (int i = 0; i < 10; i++)
        {
            log.Post($"line {i}");
        }

        log.Flush();
        log.Following = false;
        model.UpdateViewport(0, 100, 800, 60);               // line 5 at the top, the end not in view
        var five = model.Snapshot.GetLine(5);
        model.Select(five.Start, five.End);
        var changes = new List<TextChangedEventArgs>();
        log.Shown.Buffer.Changed += (_, e) => changes.Add(e);
        log.Post("line 10");
        log.Post("line 11");
        log.Post($"line 12{NewLine}line 12, its second line");
        log.Flush();
        Check.Equal(10, log.LineCount, "at most ten lines");
        Check.True(changes.Count == 1 && changes[0].Edits.Count == 2, "the trim and the append are one change");
        Check.Equal("line 4", model.Snapshot.GetText(0, 6), "the four oldest lines went (a message's second line counts)");
        Check.Equal("line 5", model.Snapshot.GetText(model.Selection.Start, model.Selection.Length), "the selection kept its text");
        Check.True(scrolls[^1] == 20, $"the view moved up with the text: line 5 stays at its top ({scrolls[^1]})");
        log.Post(string.Join(NewLine, Enumerable.Range(100, 15).Select(i => $"new {i}")));
        log.Flush();
        Check.Equal("new 105", model.Snapshot.GetText(0, 7), "a batch longer than the limit keeps its last lines");
        Check.Equal(10, model.Snapshot.LineCount, "ten lines");
    }

    [Test]
    public static void TheFilterIsAViewOfTheSameText()
    {
        using var log = new LogSurface(() => 6);
        log.Post("error one");
        log.Post("info two");
        log.Post("ERROR three");
        log.Flush();
        log.Filter = "error";
        var shown = log.Shown;
        Check.True(!ReferenceEquals(shown, log.Document), "the filter shows a view");
        Check.Equal("error one" + NewLine + "ERROR three", Text(shown.Buffer.Current), "only the lines holding the text, ignoring case");
        var view = shown.Buffer.Current;
        Check.True(view.LineCount == 2 && view.GetLine(0) is { Start: 0, Length: 9 } && view.GetLine(1).Start == 9 + NewLine.Length, "its lines");
        Check.Equal(0, view.GetLineIndex(9), "a position on a line break belongs to the line it ends");
        Check.Equal('E', view[9 + NewLine.Length], "a character read through to the log");
        Check.Throws<InvalidOperationException>(() => shown.Buffer.Apply([TextEdit.Insert(0, "x")]), "the view refuses an edit");
        using var model = new EditorModel(shown, LogSurface.Language, new CraftOptions { BottomMarginLines = 0 }, null, new FixedMeasurer(), new FakeClipboard()) { IsReadOnly = true };
        log.Attach(model);
        model.Select(0, 5);
        int changes = 0;
        shown.Buffer.Changed += (_, _) => changes++;
        log.Post("info four");
        log.Flush();
        Check.Equal(0, changes, "a line that does not match changes nothing shown");
        log.Post("error five");
        log.Post("info six");
        log.Post("error seven");
        log.Flush();
        Check.Equal("ERROR three" + NewLine + "error five" + NewLine + "error seven", Text(shown.Buffer.Current), "the oldest gone from the view too, the new matches added");
        Check.Equal(1, changes, "one change");
        Check.Equal(new SelectionState(0, 0), model.Selection, "a selection in a trimmed line goes to the start");
        Check.Equal(6, log.LineCount, "the log keeps its limit");
        log.Filter = "";
        Check.True(ReferenceEquals(log.Shown, log.Document), "no filter: the log itself");
    }

    [Test]
    public static void ErrorAndWarningLinesAreColoured()
    {
        var time = new DateTimeOffset(2026, 11, 1, 9, 30, 15, TimeSpan.Zero);
        string error = LogText.Line(new EditorLogEntry(time, EditorLogLevel.Error, "host", "It broke", null));
        string warning = LogText.Line(new EditorLogEntry(time, EditorLogLevel.Warning, "ai", "Slow", null));
        string info = LogText.Line(new EditorLogEntry(time, EditorLogLevel.Information, "host", "Fine", null));
        Check.True(LogText.LevelOf(error) == EditorLogLevel.Error && LogText.LevelOf(warning) == EditorLogLevel.Warning && LogText.LevelOf(info) == EditorLogLevel.Information,
            "the level read back from the line");
        Check.True(LogText.LevelOf("a continuation line") is null, "another line has none");
        var buffer = new StringBuffer(string.Join(NewLine, error, warning, info));
        using var session = LogSurface.Language.Open(buffer);
        using var spans = new TokenSpanBuffer();
        var snapshot = buffer.Current;
        session.Classify(snapshot, 0, snapshot.Length, spans);
        var classes = spans.Spans.ToArray().Select(s => s.Class).ToArray();
        Check.SequenceEqual([LogClasses.Error, LogClasses.Warning, LogClasses.Quiet], classes, "an error's line, a warning's, an information's time");
        var theme = LogClasses.Theme(EditorThemes.Get(EditorThemes.Dark));
        Check.True(theme.StyleOf(LogClasses.Error).Argb == theme.Colors.Error && theme.StyleOf(LogClasses.Warning).Argb == theme.Colors.Warning, "in the theme's error and warning colours");
        Check.True(theme.Colors.Background == theme.Colors.Surface, "on the panel's surface");
    }
}

/// <summary>The owner's note 26: Idrak telemetry as a live metrics table.</summary>
[TestGroup("Metrics")]
public static class MetricsTests
{
    [Test]
    public static void PercentilesMeanAndTheRecentValues()
    {
        using var table = new MetricsTable(null, new CraftOptions { TelemetrySamples = 20 });
        bool first = table.Add("host", "paint", 1, "ms");
        bool second = table.Add("host", "paint", 2, "ms");
        Check.True(first && !second, "the first value asks for a refresh, the next joins it");
        for (int i = 3; i <= 100; i++)
        {
            table.Add("host", "paint", i, "ms");
        }

        Check.True(table.Refresh(), "the refresh took the values");
        var row = table.Rows[0];
        Check.True(row is { Last: 100, Count: 100, Mean: 50.5, SampleCount: 20 }, "last, count, mean, the recent values kept");
        Check.Equal(99d, row.P95, "p95 of the last twenty (81..100), nearest rank");
        Check.Equal(81d, row.Samples[0], "the recent values oldest first");
        Check.Equal("99", row.Text(MetricColumn.P95), "its cell");
        Check.True(!table.Refresh(), "nothing new: nothing changes");
    }

    [Test]
    public static void SortFilterStallsAndSlowValues()
    {
        using var table = new MetricsTable(null, new CraftOptions { TelemetrySlowMilliseconds = 100 });
        table.Add("ai", "a.load", 10, "ms");
        table.Add("host", "b.load", 300, "ms");
        table.Add("ai", "c.tokens", 20, "tok/s");
        table.Add("host", "ui.stall", 5, "ms");
        table.Refresh();
        table.SortBy(MetricColumn.Last);
        Check.SequenceEqual(["b.load", "c.tokens", "a.load", "ui.stall"], table.Rows.Select(r => r.Measure).ToArray(), "Last: the largest first");
        table.SortBy(MetricColumn.Last);
        Check.SequenceEqual(["ui.stall", "a.load", "c.tokens", "b.load"], table.Rows.Select(r => r.Measure).ToArray(), "again: the other way");
        table.SortBy(MetricColumn.Measure);
        Check.SequenceEqual(["a.load", "b.load", "c.tokens", "ui.stall"], table.Rows.Select(r => r.Measure).ToArray(), "Measure: by name");
        Check.True(table.Rows.Single(r => r.Measure == "b.load").IsSlow && table.Rows.Single(r => r.Measure == "ui.stall").IsSlow, "a slow value and a stall are marked");
        Check.True(!table.Rows.Single(r => r.Measure == "a.load").IsSlow && !table.Rows.Single(r => r.Measure == "c.tokens").IsSlow, "a fast one and another unit are not");
        table.Filter = "AI";
        Check.SequenceEqual(["a.load", "c.tokens"], table.Rows.Select(r => r.Measure).ToArray(), "the filter on the source, ignoring case");
        table.Filter = "load";
        Check.Equal(2, table.Rows.Count, "and on the measure");
    }

    [Test]
    public static void PicksMoveAndCopyAsATable()
    {
        using var table = new MetricsTable(null);
        table.Add("host", "one", 1, "ms");
        table.Add("host", "two", 2.5, "ms");
        table.Add("host", "three", 3, "items");
        table.Refresh();
        var metrics = FixedMeasurer.Metrics;
        float row = PanelList.RowHeight(metrics);
        float header = table.HeaderHeight(metrics);
        table.Press(metrics, 50, header + 1, 0, 1000, KeyModifiers.None);
        table.Press(metrics, 50, header + (2 * row) + 1, 0, 1000, KeyModifiers.Shift);
        Check.Equal(3, table.Selection.Count, "click, then Shift+click: the range");
        table.Press(metrics, 50, header + row + 1, 0, 1000, KeyModifiers.Control);
        Check.Equal(2, table.Selection.Count, "Ctrl+click takes one out");
        string? copied = null;
        table.CopyRequested += (_, text) => copied = text;
        Check.True(table.OnKey(EditorKey.C, KeyModifiers.Control), "Ctrl+C taken");
        string nl = Environment.NewLine;
        Check.Equal($"Source\tMeasure\tLast\tMean\tp95\tCount\tUnit{nl}host\tone\t1\t1\t1\t1\tms{nl}host\tthree\t3\t3\t3\t1\titems", copied, "the picked rows tab-separated under the header");
        table.Press(metrics, 50, header + 1, 0, 1000, KeyModifiers.None);
        table.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.True(table.Selection is { Count: 1, Focus.Measure: "two" }, "Down moves the pick");
        table.OnKey(EditorKey.Down, KeyModifiers.Shift);
        Check.Equal(2, table.Selection.Count, "Shift+Down extends it");
        Check.True(table.CopyAll().Split(nl).Length == 4, "Copy all: the header and every row");
        table.Press(metrics, (1 + 16) * metrics.CellWidth + 2, 1, 0, 1000, KeyModifiers.None);
        Check.True(table.SortColumn == MetricColumn.Measure, "a header's click sorts by its column");
    }

    [Test]
    public static void ItFollowsTheHostAndDrawsWithoutAllocating()
    {
        using var host = new EditorHost();
        using var table = new MetricsTable(host);
        int asked = 0;
        table.Recorded += (_, _) => asked++;
        host.Record("ai.model.load", 4000, "ms");
        host.Record("ai.model.load", 2000, "ms");
        host.Record("ai.tokens", 41, "tok/s");
        Check.Equal(1, asked, "one refresh asked for the three");
        table.Refresh();
        Check.True(table.AllRows is [{ Measure: "ai.model.load", Last: 2000, Count: 2, Mean: 3000 }, { Measure: "ai.tokens", Count: 1 }], "one row per metric: last, count, mean");
        for (int i = 0; i < 30; i++)
        {
            table.Add("host", "paint", i % 7, "ms");
        }

        table.Refresh();
        var canvas = new CountingCanvas();
        var colors = EditorThemes.Get(EditorThemes.Dark).Colors;
        for (int i = 0; i < 50; i++)
        {
            table.Draw(canvas, FixedMeasurer.Metrics, colors, 0, 1200, 300);
        }

        canvas.Lines = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        table.Draw(canvas, FixedMeasurer.Metrics, colors, 0, 1200, 300);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(canvas.Lines > 25, $"the sparklines were drawn ({canvas.Lines} segments)");
        Check.Equal(0L, allocated, "a frame of the table allocates nothing");
        Check.True(canvas.Texts.Contains("Source") && canvas.Texts.Contains("ai.model.load"), "the header and the rows drawn");
    }
}

/// <summary>The owner's note 26: Problems as plain rows grouped by file.</summary>
[TestGroup("ProblemsPanel")]
public static class ProblemsPanelTests
{
    private static ProblemSource Source(string name, string text, params EditorDiagnostic[] diagnostics) =>
        new(name, name, new StringBuffer(text).Current, diagnostics, name);

    [Test]
    public static void GroupedByFileFoldedAndKeptWhenNothingChanged()
    {
        var list = new ProblemsList();
        string text = $"class A{Environment.NewLine}{{ int x }}";
        var a = Source("A.cs", text, new EditorDiagnostic(4, 1, DiagnosticSeverity.Warning, "CS0168", "unused"), new EditorDiagnostic(text.Length - 2, 1, DiagnosticSeverity.Error, "CS1002", "; expected"));
        var b = Source("B.cs", "x", new EditorDiagnostic(0, 1, DiagnosticSeverity.Info, "IDE1", "info"), new EditorDiagnostic(0, 1, DiagnosticSeverity.Hidden, "H", "hidden"));
        Check.True(list.Update([a, b], 100, false), "the first update shows rows");
        Check.True(list.Count == 3 && list.Errors == 1, "three problems (the hidden left out), one error");
        Check.True(list.Rows is [ProblemGroup { Header: "A.cs  2" }, ProblemItem { Diagnostic.Id: "CS1002", Line: 2 }, ProblemItem { Diagnostic.Id: "CS0168", Line: 1, Column: 5 }, ProblemGroup { Header: "B.cs  1" }, ProblemItem],
            "a header per file with its count, errors first");
        var rows = list.Rows.ToArray();
        int redraws = 0;
        list.Changed += (_, _) => redraws++;
        Check.True(!list.Update([a, b], 100, false), "the same problems: nothing replaced");
        Check.True(redraws == 0 && list.Rows.SequenceEqual(rows), "the same rows, no redraw");
        var metrics = FixedMeasurer.Metrics;
        list.Press(metrics, 10, 1, 0, 800, KeyModifiers.None);
        Check.True(list.Rows.Count == 3 && list.Groups[0].Collapsed, "a header's click folds its file");
        Check.True(!list.Update([a, b], 100, false) && list.Groups[0].Collapsed, "and it stays folded");
        list.Toggle(list.Groups[0]);
        Check.Equal(5, list.Rows.Count, "unfolded");
        Check.True(list.Update([a], 1, false) && list.Rows.Count == 2 && list.Count == 2, "at most the limit's rows, the count still all");
    }

    [Test]
    public static void ClickJumpsPicksSeveralAndCopies()
    {
        var list = new ProblemsList();
        var a = Source("Program.cs", "abc;def", new EditorDiagnostic(3, 1, DiagnosticSeverity.Error, "CS1002", "; expected"), new EditorDiagnostic(5, 1, DiagnosticSeverity.Warning, "CS0168", "unused"));
        var b = Source("Other.cs", "x", new EditorDiagnostic(0, 1, DiagnosticSeverity.Error, "CS0103", "no x"));
        list.Update([a, b], 100, true);
        ProblemItem? chosen = null;
        list.Chosen += (_, item) => chosen = item;
        var metrics = FixedMeasurer.Metrics;
        float row = PanelList.RowHeight(metrics);
        list.Press(metrics, 50, row + 1, 0, 800, KeyModifiers.None);
        Check.True(chosen is { Diagnostic.Id: "CS1002", Position: 3, Group.Owner: "Program.cs" }, "a click jumps to the problem, its file named");
        chosen = null;
        list.Press(metrics, 50, (4 * row) + 1, 0, 800, KeyModifiers.Control);
        Check.True(chosen is null && list.Selection.Count == 2, "Ctrl+click picks another without jumping");
        list.Press(metrics, 50, (2 * row) + 1, 0, 800, KeyModifiers.Shift | KeyModifiers.Control);
        Check.Equal(3, list.Selection.Count, "Ctrl+Shift+click adds the range");
        string nl = Environment.NewLine;
        Check.Equal($"Program.cs(1,4): error CS1002: ; expected{nl}Program.cs(1,6): warning CS0168: unused{nl}Other.cs(1,1): error CS0103: no x", list.CopySelected(), "the picked rows as a build tool writes them");
        list.Update([a, Source("Other.cs", "x", new EditorDiagnostic(0, 1, DiagnosticSeverity.Error, "CS0103", "no x"), new EditorDiagnostic(0, 1, DiagnosticSeverity.Warning, "CS8019", "new"))], 100, true);
        Check.Equal(3, list.Selection.Count, "the picks follow their problems to the new rows");
        Check.Equal(4, list.CopyAll().Split(nl).Length, "Copy all: every problem");
        Check.True(list.OnKey(EditorKey.Escape, KeyModifiers.None) && list.Selection.Count == 0, "Escape picks none");
        (ProblemItem Item, string Kind)? action = null;
        list.ActionRequested += (_, request) => action = request;
        list.Hover = 1;
        float fixX = 800 - ((3 + 7 + 6) * metrics.CellWidth) + 2;
        list.Press(metrics, fixX, row + 1, 0, 800, KeyModifiers.None);
        Check.True(action is { Kind: CodeActionKinds.Fix, Item.Diagnostic.Id: "CS1002" }, "the hovered row's Fix");
    }
}

/// <summary>A canvas that counts what it is asked to draw (it keeps each text once, by reference, without allocating per call after the first).</summary>
internal sealed class CountingCanvas : IEditorCanvas
{
    public int Lines { get; set; }

    public HashSet<string> Texts { get; } = new(StringComparer.Ordinal);

    public void FillRect(float x, float y, float width, float height, uint argb)
    {
    }

    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness) => Lines++;

    public void DrawText(string text, float x, float top, in TextStyle style) => Texts.Add(text);
}
