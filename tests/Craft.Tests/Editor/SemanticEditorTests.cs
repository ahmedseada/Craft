// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>A language whose semantic layer the test drives: its answers wait until the test completes them.</summary>
internal sealed class FakeSemanticLanguage : ILanguage
{
    public Session? Last { get; private set; }

    public string Name => "fake-semantic";

    public string Summary => "Test: a semantic layer whose answers the test gives";

    public IReadOnlyList<string> Extensions { get; } = [".fake"];

    public LanguageRules Rules => LanguageRules.Plain;

    public ILanguageSession Open(ITextBuffer buffer) => Last = new Session(buffer);

    internal sealed class Session(ITextBuffer buffer) : ISemanticLanguageSession
    {
        public ITextBuffer Buffer { get; } = buffer;

        public List<(int Position, CompletionTrigger Trigger, TaskCompletionSource<CompletionList?> Answer)> Completions { get; } = [];

        public TaskCompletionSource<DefinitionLocation?> Definition { get; set; } = new();

        public IReadOnlyList<TextEdit> FormatEdits { get; set; } = [];

        public DiagnosticSet Diagnostics { get; set; } = DiagnosticSet.Empty;

        public event EventHandler? DiagnosticsChanged;

        public bool IsSemanticDeferred => false;

        public SemanticTelemetry Telemetry => SemanticTelemetry.None;

        public int Releases { get; private set; }

        public void Raise() => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);

        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
        }

        public void EnableSemantics()
        {
        }

        public Task<CompletionList?> GetCompletionsAsync(ITextSnapshot snapshot, int position, CompletionTrigger trigger, CancellationToken cancellationToken)
        {
            var answer = new TaskCompletionSource<CompletionList?>();
            Completions.Add((position, trigger, answer));
            return answer.Task;
        }

        public Task<CompletionItem> ResolveCompletionAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken) =>
            Task.FromResult(item.NeedsResolve ? item with { Edit = item.Edit with { Text = item.Edit.Text + "()" }, CaretAfter = item.Edit.Start + item.Edit.Text.Length + 1, NeedsResolve = false } : item);

        public Task<SignatureHelp?> GetSignatureHelpAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) =>
            Task.FromResult<SignatureHelp?>(new SignatureHelp([new SignatureInfo("void M(int a, int b)", "Does M.", [new("a", 7, 5, null), new("b", 14, 5, "The second.")])], 0, 1, position));

        public string HoverText { get; set; } = "(local) int x";

        public List<int> DefinitionPositions { get; } = [];

        public Task<HoverInfo?> GetHoverAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) =>
            Task.FromResult<HoverInfo?>(new HoverInfo(HoverText, position, 1));

        public Task<DefinitionLocation?> GetDefinitionAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
        {
            DefinitionPositions.Add(position);
            return Definition.Task;
        }

        public Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken) => Task.FromResult(FormatEdits);

        public void Release() => Releases++;

        public void Dispose()
        {
        }
    }
}

/// <summary>The editor's semantic layer, driven headless with a fake language: completion, diagnostics, squiggles, popups.</summary>
[TestGroup("Editor")]
public static class SemanticEditorTests
{
    private static readonly CraftOptions Immediate = new() { CompletionDelayMilliseconds = 0, HoverDelayMilliseconds = 0, HoverHideMilliseconds = 0, CompletionRows = 3 };

    private static (EditorModel Model, FakeDocument Document, FakeSemanticLanguage.Session Session, Queue<Action> Ui) Make(string text, int caret)
    {
        var language = new FakeSemanticLanguage();
        var (model, document, _) = Editors.Make(text, Immediate, language);
        var ui = new Queue<Action>();
        model.Dispatch = ui.Enqueue;
        model.Select(caret, caret);
        return (model, document, language.Last!, ui);
    }

    private static void Pump(Queue<Action> ui)
    {
        while (ui.TryDequeue(out var action))
        {
            action();
        }
    }

    private static CompletionList List(ITextSnapshot snapshot, int position, int spanStart, params string[] labels) =>
        new(snapshot, position, spanStart, [.. labels.Select(l => new CompletionItem(l, CompletionItemKind.Class, null, new TextEdit(spanStart, position - spanStart, l), l, l))]);

    [Test]
    public static void CompletionFiltersSelectsAndCommitsAsOneUndoStep()
    {
        var (model, document, session, ui) = Make("Con", 3);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        Check.Equal(1, session.Completions.Count, "Ctrl+Space asks at once");
        Check.Equal(CompletionTriggerKind.Invoke, session.Completions[0].Trigger.Kind, "as an invocation");
        session.Completions[0].Answer.SetResult(List(model.Snapshot, 3, 0, "ConsoleColor", "Console", "Convert", "Math"));
        Check.True(!model.Completion.IsOpen, "the answer waits for the UI thread");
        Pump(ui);
        Check.True(model.Completion.IsOpen, "the list opens on the UI thread");
        Check.SequenceEqual(["ConsoleColor", "Console", "Convert"], model.Completion.Items.Select(i => i.Label).ToList(), "filtered by the typed \"Con\", in the list's order");

        model.OnText("s");
        Check.Equal(1, session.Completions.Count, "typing in the word filters the open list, no new request");
        Check.SequenceEqual(["ConsoleColor", "Console"], model.Completion.Items.Select(i => i.Label).ToList(), "\"Cons\" filters again");
        Check.Equal("ConsoleColor", model.Completion.Selected?.Label, "the first best match is selected");
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.Equal("Console", model.Completion.Selected?.Label, "Down selects the next");
        Check.Equal(4, model.Selection.Caret, "and the caret stays");
        model.OnKey(EditorKey.Up, KeyModifiers.None);
        model.OnKey(EditorKey.PageDown, KeyModifiers.None);
        Check.Equal("Console", model.Completion.Selected?.Label, "Page Down stops at the last");

        int steps = document.Kinds.Count;
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.Equal("Console|", Editors.Marked(model, document), "Enter commits the item over what was typed since the list came");
        Check.True(!model.Completion.IsOpen, "and closes the list");
        Check.Equal(steps + 1, document.Kinds.Count, "the commit is one edit");
        Check.Equal(EditKind.Command, document.Kinds[^1], "its own undo step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("Cons|", Editors.Marked(model, document), "one undo takes the commit back, the typing stays");

        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        session.Completions[^1].Answer.SetResult(List(model.Snapshot, 4, 0, "Console"));
        Pump(ui);
        model.OnKey(EditorKey.Escape, KeyModifiers.None);
        Check.True(!model.Completion.IsOpen, "Escape closes the list");
        Check.Equal("Cons|", Editors.Marked(model, document), "and changes nothing");

        // An item whose exact edit is resolved on commit: applied when the answer comes, as one step.
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        var complex = new CompletionItem("Console", CompletionItemKind.Method, null, new TextEdit(0, 4, "Console"), "Console", "Console", NeedsResolve: true);
        session.Completions[^1].Answer.SetResult(new CompletionList(model.Snapshot, 4, 0, [complex]));
        Pump(ui);
        steps = document.Kinds.Count;
        model.OnKey(EditorKey.Tab, KeyModifiers.None);
        Check.Equal("Cons|", Editors.Marked(model, document), "Tab commits a resolved item only when its answer arrives");
        Pump(ui);
        Check.Equal("Console(|)", Editors.Marked(model, document), "the resolved edit and caret");
        Check.Equal(steps + 1, document.Kinds.Count, "in one step");
    }

    [Test]
    public static void TypingAnIdentifierOrADotAsksAndOtherCharactersClose()
    {
        var (model, _, session, ui) = Make("", 0);
        model.OnText("x");
        Check.Equal(1, session.Completions.Count, "an identifier character asks (the delay is 0 here)");
        Check.Equal(new CompletionTrigger(CompletionTriggerKind.Typing, 'x'), session.Completions[0].Trigger, "as typing 'x'");
        model.OnText("y");
        Check.Equal(1, session.Completions.Count, "while that request is in flight, typing in the word asks nothing more");
        session.Completions[0].Answer.SetResult(List(model.Snapshot, 1, 0, "xyz", "xa"));
        Pump(ui);
        Check.SequenceEqual(["xyz"], model.Completion.Items.Select(i => i.Label).ToList(), "the late answer is filtered by \"xy\", typed meanwhile");
        model.OnText(" ");
        Check.True(!model.Completion.IsOpen, "a space closes it");
        model.OnText(".");
        Check.Equal(2, session.Completions.Count, "'.' asks");
        Check.Equal('.', session.Completions[1].Trigger.Character, "as typing '.'");
    }

    [Test]
    public static void LateResultsAreDropped()
    {
        var (model, _, session, ui) = Make("Con foo", 3);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        model.OnKey(EditorKey.End, KeyModifiers.None);
        session.Completions[0].Answer.SetResult(List(model.Snapshot, 3, 0, "Console"));
        Pump(ui);
        Check.True(!model.Completion.IsOpen, "an answer for a word the caret left is not shown");
        Check.Equal(1L, model.Completion.ResultsDropped, "and counted as dropped");

        model.Select(3, 3);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        session.Completions[1].Answer.SetResult(List(model.Snapshot, 3, 0, "Old"));
        session.Completions[2].Answer.SetResult(List(model.Snapshot, 3, 0, "Console"));
        Pump(ui);
        Check.SequenceEqual(["Console"], model.Completion.Items.Select(i => i.Label).ToList(), "only the newest request's answer is shown");
        Check.Equal(2L, model.Completion.ResultsDropped, "the older one is dropped");
    }

    [Test]
    public static void CamelHumpsAndPrefixesScore()
    {
        Check.Equal(4, CompletionController.Score("WriteLine", "Wr"), "a prefix of the same case");
        Check.Equal(3, CompletionController.Score("WriteLine", "wri"), "a prefix ignoring case");
        Check.Equal(2, CompletionController.Score("WriteLine", "WL"), "camel humps");
        Check.Equal(2, CompletionController.Score("WriteLine", "wrli"), "hump prefixes");
        Check.Equal(2, CompletionController.Score("ConsoleColor", "CoC"), "a hump's prefix then the next hump");
        Check.Equal(2, CompletionController.Score("_itemCount", "iC"), "after an underscore");
        Check.Equal(0, CompletionController.Score("WriteLine", "x"), "no match");
        Check.Equal(0, CompletionController.Score("WriteLine", "Lw"), "humps keep their order");
        Check.Equal(1, CompletionController.Score("WriteLine", ""), "an empty filter matches all");
    }

    [Test]
    public static void APopupRowClickSelectsAndADoubleClickCommits()
    {
        var (model, document, session, ui) = Make("C", 1);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        session.Completions[0].Answer.SetResult(List(model.Snapshot, 1, 0, "Ca", "Cb", "Cc", "Cd"));
        Pump(ui);
        var box = model.CompletionBox();
        Check.True(box is { Rows: 3 }, "the list shows CRAFT_COMPLETION_ROWS rows");
        var metrics = model.Metrics;
        float x = box!.Value.X + 4;
        float rowY = box.Value.Y + metrics.LineHeight * 1.5f;
        Check.Equal(1, model.PopupRowAt(x, rowY), "the point is on the second row");
        model.PointerPressed(x, rowY, KeyModifiers.None);
        Check.Equal("Cb", model.Completion.Selected?.Label, "a click selects the row");
        Check.Equal(1, model.Selection.Caret, "and does not move the caret");
        model.PointerPressed(x, rowY, KeyModifiers.None);
        Check.Equal("Cb|", Editors.Marked(model, document), "a second click commits it");

        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        session.Completions[1].Answer.SetResult(List(model.Snapshot, 2, 2, "Ca", "Cb", "Cc", "Cd", "Ce"));
        Pump(ui);
        model.OnKey(EditorKey.PageDown, KeyModifiers.None);
        model.OnKey(EditorKey.PageDown, KeyModifiers.None);
        Check.Equal(4, model.Completion.SelectedIndex, "Page Down moves by the rows");
        Check.Equal(2, model.Completion.FirstVisible, "and the rows scroll to show the selection");
    }

    [Test]
    public static void DiagnosticsOfAnOlderSnapshotMapOntoTheCurrentOne()
    {
        //                         0         1         2
        //                         0123456789012345678901234
        var (model, _, session, ui) = Make("aaa bbb ccc ddd", 0);
        var older = model.Snapshot;
        session.Diagnostics = new DiagnosticSet(older,
        [
            new(0, 3, DiagnosticSeverity.Error, "E1", "before"),
            new(4, 3, DiagnosticSeverity.Warning, "W1", "touched"),
            new(12, 3, DiagnosticSeverity.Error, "E2", "after"),
            new(8, 0, DiagnosticSeverity.Info, "I1", "a point after"),
        ]);

        // Two edits land before the set arrives: "XX" typed inside "bbb", then "Y" typed at the start of "ddd" removed again.
        model.Select(5, 5);
        model.OnText("XX");
        model.Select(0, 0);
        session.Raise();
        Pump(ui);
        var mapped = model.Diagnostics.ToArray();
        Check.SequenceEqual(["E1", "I1", "E2"], mapped.Select(d => d.Id).ToList(), "the one the edit touched is dropped, the others kept in order");
        Check.Equal(0, mapped[0].Start, "one before the edit stays");
        Check.Equal(10, mapped[1].Start, "one after it shifts by the edit");
        Check.Equal(14, mapped[2].Start, "so does the last");
        Check.Equal(2, model.ErrorCount, "the counts follow");

        int changed = 0;
        model.DiagnosticsChanged += (_, _) => changed++;
        model.Select(0, 0);
        model.OnText("Z");
        Check.Equal(1, changed, "an edit moves the current diagnostics at once");
        Check.SequenceEqual(["I1", "E2"], model.Diagnostics.ToArray().Select(d => d.Id).ToList(), "typing at E1's start drops it");
        Check.Equal(11, model.Diagnostics[0].Start, "and shifts the rest");
        Check.Equal(1, model.DiagnosticsAt(16).Count, "the problem at a position is found");
    }

    [Test]
    public static void SquigglesAreDrawnOnlyOnVisibleLines()
    {
        var text = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"line {i} text"));
        var (model, _, session, ui) = Make(text, 0);
        var snapshot = model.Snapshot;
        int visible = snapshot.GetLine(5).Start;
        int hidden = snapshot.GetLine(900).Start;
        session.Diagnostics = new DiagnosticSet(snapshot,
        [
            new(visible, 4, DiagnosticSeverity.Error, "E", "seen"),
            new(hidden, 4, DiagnosticSeverity.Error, "E", "not seen"),
            new(visible + 5, 1, DiagnosticSeverity.Hidden, "H", "never drawn"),
        ]);
        session.Raise();
        Pump(ui);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        var lines = canvas.Calls.Where(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Error).ToList();
        Check.True(lines.Count > 0 && lines.Count == renderer.SquiggleSegmentsDrawn, "the visible error is drawn as squiggle segments");
        float top = 5 * FixedMeasurer.Metrics.LineHeight;
        Check.True(lines.All(l => l.Y >= top && l.Y <= top + FixedMeasurer.Metrics.LineHeight), "on its own line only");
        float left = model.TextLeft;
        Check.True(lines.Min(l => l.X) >= left - 0.01 && lines.Max(l => l.X + l.W) <= left + 4 * FixedMeasurer.Metrics.CellWidth + 0.01, "under its four columns");

        canvas.Clear();
        renderer.Draw(canvas, model, 0, 885 * FixedMeasurer.Metrics.LineHeight, 800, 400, caretVisible: false);
        Check.True(renderer.SquiggleSegmentsDrawn > 0, "scrolled to line 900, its squiggle is drawn");
        Check.True(canvas.Calls.Where(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Error).All(l => l.Y >= 0 && l.Y <= 400), "inside the viewport");
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 400 * FixedMeasurer.Metrics.LineHeight, 800, 400, caretVisible: false);
        Check.Equal(0, renderer.SquiggleSegmentsDrawn, "lines with no diagnostic draw none");
    }

    [Test]
    public static void ThePopupsAreDrawnThroughTheCanvas()
    {
        var (model, _, session, ui) = Make("M(1, ", 5);
        model.OnText("2");
        session.Completions[0].Answer.SetResult(null);
        model.OnText(",");
        Pump(ui);
        Check.True(model.SignatureHelp is { ActiveParameter: 1 }, "',' asks for signature help");
        Check.True(model.Tooltip is { } tip && tip.Text.StartsWith("void M(int a, int b)", StringComparison.Ordinal) && tip.Text.Contains("The second.", StringComparison.Ordinal),
            "its tooltip shows the signature and the active parameter's documentation");
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        var colors = model.Theme.Colors;
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.PopupBackground), "the tooltip's box is filled");
        Check.True(canvas.Texts.Any(t => t.Text == "int b"), "the active parameter is drawn as a run of its own (bold)");
        model.OnKey(EditorKey.Escape, KeyModifiers.None);
        Check.True(model.SignatureHelp is null && model.Tooltip is null, "Escape closes it");

        model.PointerHovered(model.TextLeft + 5, 5);
        Pump(ui);
        Check.True(model.Tooltip is { Text: "(local) int x" }, "a resting pointer shows the hover");
        Check.True(model.Tooltip is { Kind: EditorTooltipKind.Hover }, "a hover card");
        model.PointerExited();
        Check.True(model.Tooltip is null, "leaving closes it after CRAFT_HOVER_HIDE_MS (0 here)");
    }

    [Test]
    public static void DefinitionAndFormatGoThroughTheModel()
    {
        var (model, document, session, ui) = Make("int x;\nx = 1;", 7);
        DefinitionLocation? requested = null;
        model.DefinitionRequested += (_, e) => requested = e.Location;
        model.OnKey(EditorKey.F12, KeyModifiers.None);
        session.Definition.SetResult(new DefinitionLocation(null, 4, 1, true, "(local) int x"));
        Pump(ui);
        Check.Equal(new SelectionState(5, 4), model.Selection, "the same document: the definition's name is selected");
        Check.True(requested is null, "and nothing is raised");

        session.Definition = new TaskCompletionSource<DefinitionLocation?>();
        model.OnKey(EditorKey.F12, KeyModifiers.None);
        session.Definition.SetResult(new DefinitionLocation("/other.cs", 10, 3, false, "(class) Other"));
        Pump(ui);
        Check.Equal("/other.cs", requested?.FilePath, "another file is raised for the host to open");

        session.Definition = new TaskCompletionSource<DefinitionLocation?>();
        model.OnKey(EditorKey.F12, KeyModifiers.None);
        session.Definition.SetResult(new DefinitionLocation(null, 0, 0, false, "(class) System.Console — in System.Console"));
        Pump(ui);
        Check.True(model.Tooltip is { Text: "(class) System.Console — in System.Console" }, "a metadata symbol shows its description");

        session.FormatEdits = [new TextEdit(3, 1, " "), new TextEdit(9, 0, " ")];
        int steps = document.Kinds.Count;
        model.Select(10, 10);
        model.OnKey(EditorKey.F, KeyModifiers.Shift | KeyModifiers.Alt);
        Pump(ui);
        Check.Equal("int x;\nx  = 1;", document.Text, "Shift+Alt+F applies the edits");
        Check.Equal(steps + 1, document.Kinds.Count, "as one step");
        model.OnKey(EditorKey.K, KeyModifiers.Control);
        model.OnKey(EditorKey.D, KeyModifiers.Control);
        Pump(ui);
        Check.Equal(steps + 2, document.Kinds.Count, "Ctrl+K Ctrl+D formats too");
    }
}
