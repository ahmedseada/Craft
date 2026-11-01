// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>Helpers of the X restart's layer tests: a UI queue the timers post to, pumped or waited on.</summary>
internal static class LayerUi
{
    public static void Pump(ConcurrentQueue<Action> ui)
    {
        while (ui.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>Waits until something is posted (at most two seconds), then runs it all.</summary>
    public static void WaitAndPump(ConcurrentQueue<Action> ui)
    {
        var watch = Stopwatch.StartNew();
        while (ui.IsEmpty && watch.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(5);
        }

        Pump(ui);
    }

    public static string Lines(int count, Func<int, string> line)
    {
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            text.Append(line(i)).Append('\n');
        }

        return text.ToString();
    }
}

/// <summary>The X restart, piece 2a: hover cards that stay (select, copy, wheel, "Go to definition") and a fold's preview.</summary>
[TestGroup("HoverCards")]
public static class HoverCardTests
{
    private static (EditorModel Model, FakeClipboard Clipboard, FakeSemanticLanguage.Session Session, ConcurrentQueue<Action> Ui) Make(string text, int hideMs)
    {
        var language = new FakeSemanticLanguage();
        var (model, _, clipboard) = Editors.Make(text, new CraftOptions { HoverDelayMilliseconds = 0, HoverHideMilliseconds = hideMs }, language);
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        return (model, clipboard, language.Last!, ui);
    }

    [Test]
    public static void AHoverCardStaysWhileThePointerIsInItAndLeavesAfterTheHideDelay()
    {
        var (model, _, _, ui) = Make("int x = 1;\nx = 2;\n", hideMs: 150);
        using var _m = model;
        model.PointerHovered(model.TextLeft + 2, 25);
        LayerUi.Pump(ui);
        Check.True(model.Tooltip is { Kind: EditorTooltipKind.Hover, Text: "(local) int x" }, "a resting pointer shows a hover card");
        var box = model.HoverCard.Layout()!.Value;
        model.PointerHovered(box.X + 4, box.Y + 4);
        LayerUi.Pump(ui);
        Check.True(model.Tooltip is not null, "the pointer inside the card keeps it");

        model.PointerHovered(2, 25);                       // the gutter: the card's hide starts
        Check.True(model.Tooltip is not null, "leaving does not close it at once");
        model.PointerHovered(box.X + 4, box.Y + 4);        // back inside before CRAFT_HOVER_HIDE_MS
        Thread.Sleep(400);
        LayerUi.Pump(ui);
        Check.True(model.Tooltip is not null, "coming back inside within the hide delay keeps it");

        model.PointerExited();
        Check.True(model.Tooltip is not null, "leaving the editor keeps it for the hide delay");
        LayerUi.WaitAndPump(ui);
        Check.True(model.Tooltip is null, "then it closes");
    }

    [Test]
    public static void TheCardSelectsCopiesScrollsAndGoesToTheDefinition()
    {
        var (model, clipboard, session, ui) = Make("int x = 1;\nx = 2;\n", hideMs: 60_000);
        using var _m = model;
        session.HoverText = string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line {i:D2} of the description"));
        int hovered = model.Snapshot.GetLine(1).Start;
        model.PointerHovered(model.TextLeft + 2, 25);
        LayerUi.Pump(ui);
        var card = model.HoverCard;
        var box = card.Layout()!.Value;
        Check.Equal(40, card.Rows, "one row a line of the description");
        Check.Equal(10, card.ShownRows, "at most half the editor's height shows (400 / 2 / 20 rows)");
        Check.True(card.HasDefinitionRow, "a hover on a symbol ends with \"Go to definition\"");

        model.PointerHovered(box.X + 4, box.Y + 4);
        Check.True(model.WheelOverPopup(60), "the wheel over the card is the card's");
        Check.Equal(3, card.FirstRow, "three rows down (60 units of 20)");
        model.PointerHovered(2, 25);
        Check.True(!model.WheelOverPopup(60), "the wheel outside the card scrolls the text");
        model.PointerHovered(box.X + 4, box.Y + 4);

        float cell = FixedMeasurer.Metrics.CellWidth;
        float left = box.X + (0.5f * cell);
        model.PointerPressed(left, box.Y + 4, KeyModifiers.None);
        model.PointerMoved(left + (7 * cell), box.Y + 4);
        model.PointerReleased();
        Check.Equal("line 03", card.SelectedText, "a drag selects the card's text (row 3 shown first)");
        Check.True(model.Tooltip is not null && model.Selection.IsEmpty && model.Selection.Caret == 0, "the card stays and the editor's caret did not move");
        model.OnKey(EditorKey.C, KeyModifiers.Control);
        Check.Equal("line 03", clipboard.Text, "Ctrl+C copies the card's selection");

        using (var renderer = new EditorRenderer())
        {
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
            var colors = model.Theme.Colors;
            Check.True(canvas.Texts.Any(t => t.Text == HoverCard.DefinitionLabel && t.Argb == colors.Info), "the definition row is drawn in the link colour");
            Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.Selection && c.X >= box.X && c.Y >= box.Y), "the card's selection is drawn");
            Check.True(canvas.Texts.Any(t => t.Text == "line 03 of the description") && !canvas.Texts.Any(t => t.Text == "line 00 of the description"), "the shown rows follow the card's scroll");
        }

        model.PointerPressed(left, box.Y + (card.ShownRows * FixedMeasurer.Metrics.LineHeight) + 4, KeyModifiers.None);
        model.PointerReleased();
        Check.True(session.DefinitionPositions.Count == 1 && session.DefinitionPositions[0] == hovered, $"the row asks for the hovered symbol's definition ({string.Join(", ", session.DefinitionPositions)})");
        Check.True(model.Tooltip is null && model.Selection.Caret == hovered, "the card closes and the caret goes to the symbol");
    }

    [Test]
    public static void AFoldedBlocksPreviewShowsItsHiddenLinesInTheirColoursAndAClickUnfolds()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            var (model, _) = RealEditors.Make("a {\n  \"s\" one\n  two\n}\nb\n", new CraftOptions { HoverDelayMilliseconds = 0, HoverHideMilliseconds = 0 }, new QuoteLanguage());
            using var _m = model;
            model.Folding.SetRanges(BraceFolding.Braces(model.Snapshot));
            Check.True(model.Folding.Toggle(0), "the block folds");
            float cell = FixedMeasurer.Metrics.CellWidth;
            model.PointerHovered(model.TextLeft + (5 * cell), 5);               // on the "..." after "a {"
            Check.True(model.Tooltip is { Kind: EditorTooltipKind.FoldPreview } preview && preview.Text == "  \"s\" one\n  two\n}", $"the hidden lines show ({model.Tooltip?.Text})");
            var box = model.HoverCard.Layout()!.Value;
            Check.Equal(3, model.HoverCard.ShownRows, "three hidden lines");
            Check.True(box.Y >= FixedMeasurer.Metrics.LineHeight, "under its header");

            using (var renderer = new EditorRenderer())
            {
                var canvas = new RecordingCanvas();
                renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
                uint stringColour = model.Theme.StyleOf(TokenClasses.String).Argb;
                Check.True(canvas.Texts.Any(t => t.Text == "\"s\"" && t.Argb == stringColour && t.X >= box.X && t.Top >= box.Y), "a hidden line's string is drawn in the string colour inside the card");
                Check.True(canvas.Texts.Any(t => t.Text.Contains("two", StringComparison.Ordinal) && t.X >= box.X && t.X < box.X + (2 * cell)), "the common indent is taken off");
            }

            model.PointerPressed(box.X + 4, box.Y + 4, KeyModifiers.None);
            model.PointerReleased();
            Check.True(!model.Folding.IsFolded(0) && model.Tooltip is null, "a click in the preview unfolds the block");

            model.Folding.Toggle(0);
            model.PointerHovered(model.GutterWidth - 10, 5);                    // the folded chevron
            Check.True(model.Tooltip is { Kind: EditorTooltipKind.FoldPreview }, "the chevron of a folded block shows the preview too");
            model.PointerHovered(model.TextLeft + (40 * cell), 85);
            Check.True(model.Tooltip is null, "moving away closes it (CRAFT_HOVER_HIDE_MS 0 here)");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }
}

/// <summary>The X restart, piece 2a: the overview ruler (its marks, bucketed once per change; click and drag to scroll).</summary>
[TestGroup("OverviewRuler")]
public static class OverviewRulerTests
{
    private static (EditorModel Model, FakeSemanticLanguage.Session Session, ConcurrentQueue<Action> Ui) Make(CraftOptions? options = null)
    {
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make(LayerUi.Lines(1000, i => $"line {i} text"), options, language);
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        return (model, language.Last!, ui);
    }

    [Test]
    public static void TheRulerMarksErrorsWarningsHitsAndCaretsAndRebuildsOnlyWhenTheyChange()
    {
        var (model, session, ui) = Make();
        using var _m = model;
        var snapshot = model.Snapshot;
        session.Diagnostics = new DiagnosticSet(snapshot,
        [
            new(snapshot.GetLine(100).Start, 4, DiagnosticSeverity.Error, "E", "an error"),
            new(snapshot.GetLine(500).Start, 4, DiagnosticSeverity.Warning, "W", "a warning"),
            new(snapshot.GetLine(700).Start, 4, DiagnosticSeverity.Info, "I", "not on the ruler"),
        ]);
        session.Raise();
        LayerUi.Pump(ui);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var colors = model.Theme.Colors;
        float left = 800 - model.OverviewRulerWidth;
        float scale = 400 / model.ContentHeight;
        Check.Equal(14f, model.OverviewRulerWidth, "CRAFT_OVERVIEW_RULER_WIDTH's default");
        var errors = canvas.Calls.Where(c => c.Kind == "fill" && c.Argb == colors.RulerError && c.X >= left).ToList();
        var warnings = canvas.Calls.Where(c => c.Kind == "fill" && c.Argb == colors.RulerWarning && c.X >= left).ToList();
        var carets = canvas.Calls.Where(c => c.Kind == "fill" && c.Argb == colors.RulerCaret && c.X >= left).ToList();
        Check.True(errors.Count == 1 && Math.Abs(errors[0].Y - (int)(100 * 20 * scale)) <= 1, $"one error mark at line 100's place ({string.Join(", ", errors.Select(e => e.Y))}, expected {(int)(100 * 20 * scale)})");
        Check.True(warnings.Count == 1 && Math.Abs(warnings[0].Y - (int)(500 * 20 * scale)) <= 1, "one warning mark at line 500's place");
        Check.True(carets.Count == 1 && carets[0].Y == 0, "the caret's mark at the top");
        Check.True(errors[0].X > warnings[0].X - 0.01 && errors[0].X > left + 2, "errors and warnings in the right half");
        Check.Equal(1L, renderer.OverviewRuler.Rebuilds, "bucketed once");

        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        renderer.Draw(canvas, model, 0, 20, 800, 400, caretVisible: true);
        Check.Equal(1L, renderer.OverviewRuler.Rebuilds, "a blink or a scroll keeps the marks");

        model.Select(snapshot.GetLine(300).Start, snapshot.GetLine(300).Start);
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 20, 800, 400, caretVisible: true);
        Check.Equal(2L, renderer.OverviewRuler.Rebuilds, "a caret that moved rebuilds them");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.RulerCaret && Math.Abs(c.Y - (int)(300 * 20 * scale)) <= 1), "the caret's mark moved to line 300's place");

        model.Find.Query = "line 9";
        model.Find.IsHighlighting = true;
        _ = model.Find.CountMatches();
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 20, 800, 400, caretVisible: true);
        var hits = canvas.Calls.Where(c => c.Kind == "fill" && c.Argb == colors.RulerFind && c.X >= left).ToList();
        Check.True(hits.Count > 1 && hits.All(h => h.X < left + 2), $"the find overlay's hits are marked, in the left half ({hits.Count} marks)");
        Check.Equal(3L, renderer.OverviewRuler.Rebuilds, "listed hits rebuild them once");
    }

    [Test]
    public static void AClickOrADragOnTheRulerScrollsAndItsOptionTurnsItOff()
    {
        var (model, _, _) = Make();
        using var _m = model;
        var requests = new List<float>();
        model.ScrollRequested += (_, e) => requests.Add(e.Y);
        float scale = 400 / model.ContentHeight;
        float most = model.ContentHeight - 400;
        model.PointerPressed(795, 200, KeyModifiers.None);
        model.PointerMoved(795, 300);
        model.PointerReleased();
        Check.True(requests.Count == 2, $"a press and a drag each scroll ({requests.Count})");
        Check.True(Math.Abs(requests[0] - Math.Clamp((200 / scale) - 200, 0, most)) < 1 && Math.Abs(requests[1] - Math.Clamp((300 / scale) - 200, 0, most)) < 1,
            $"to the place under the pointer, centred ({string.Join(", ", requests)})");
        Check.Equal(0, model.Selection.Caret, "the caret stays");

        var (off, _, _) = Make(new CraftOptions { OverviewRuler = false });
        using var _o = off;
        Check.Equal(0f, off.OverviewRulerWidth, "CRAFT_OVERVIEW_RULER off: no ruler");
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, off, 0, 0, 800, 400, caretVisible: true);
        Check.True(!canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == off.Theme.Colors.RulerCaret) && renderer.OverviewRuler.Rebuilds == 0, "nothing drawn");
        off.PointerPressed(795, 30, KeyModifiers.None);
        off.PointerReleased();
        Check.Equal(1, off.CaretLine, "a press at the right edge places the caret");
    }

    [Test]
    public static void AWarmFrameWithTheRulerAllocatesNothing()
    {
        var (model, session, ui) = Make();
        using var _m = model;
        var snapshot = model.Snapshot;
        session.Diagnostics = new DiagnosticSet(snapshot, Enumerable.Range(0, 50).Select(i => new EditorDiagnostic(snapshot.GetLine(i * 20).Start, 3, i % 2 == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, "E", "m")));
        session.Raise();
        LayerUi.Pump(ui);
        model.SetSelections(Enumerable.Range(0, 30).Select(i => SelectionState.At(snapshot.GetLine(i * 33).Start)).ToArray(), 0);
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        for (int i = 0; i < 100; i++)
        {
            renderer.Draw(canvas, model, 0, (i & 1) * 20, 800, 400, caretVisible: (i & 2) == 0);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 200;
        for (int i = 0; i < frames; i++)
        {
            renderer.Draw(canvas, model, 0, (i & 1) * 20, 800, 400, caretVisible: (i & 2) == 0);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(renderer.OverviewRuler.MarksDrawn > 50, $"the marks are drawn ({renderer.OverviewRuler.MarksDrawn})");
        Check.True(bytes < 1024, $"{frames} warm frames with the ruler allocate nothing ({bytes:N0} bytes)");
    }
}

/// <summary>The X restart, piece 2a: sticky scroll over the folding ranges (a binary search a frame, nothing allocated).</summary>
[TestGroup("StickyScroll")]
public static class StickyScrollTests
{
    // namespace (0) { 9 lines; class (10) { 4 lines; method (15) { 40 lines } (56) } (57) } (58), then 100 lines.
    private static string Nested()
    {
        var text = new StringBuilder("namespace N {\n");
        for (int i = 1; i < 10; i++)
        {
            text.Append("  y;\n");
        }

        text.Append("  class A {\n");
        for (int i = 11; i < 15; i++)
        {
            text.Append("    z;\n");
        }

        text.Append("    void M() {\n");
        for (int i = 16; i < 56; i++)
        {
            text.Append("      x;\n");
        }

        text.Append("    }\n  }\n}\n");
        for (int i = 0; i < 100; i++)
        {
            text.Append("tail;\n");
        }

        return text.ToString();
    }

    private static EditorModel Make(string text, CraftOptions? options = null)
    {
        var (model, _) = RealEditors.Make(text, options, new QuoteLanguage());
        model.Folding.SetRanges(BraceFolding.Braces(model.Snapshot));
        return model;
    }

    [Test]
    public static void TheHeadersOfTheBlocksAroundTheTopLineStayAndLeaveAtTheirEnd()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            using var model = Make(Nested());
            Check.SequenceEqual([new FoldingRange(0, 58), new FoldingRange(10, 57), new FoldingRange(15, 56)], model.Folding.Ranges.ToArray(), "the blocks");
            model.UpdateViewport(0, 0, 800, 400);
            Check.Equal(0, model.StickyScroll.Count, "at the top nothing sticks");
            model.UpdateViewport(0, 30 * 20, 800, 400);
            Check.SequenceEqual([0, 10, 15], model.StickyScroll.Lines.ToArray(), "inside the method: the namespace, the class and the method, outermost first");
            model.UpdateViewport(0, 54 * 20, 800, 400);
            Check.SequenceEqual([0, 10], model.StickyScroll.Lines.ToArray(), "the method's end reaches its sticky row: it leaves");
            model.UpdateViewport(0, 12 * 20, 800, 400);
            Check.SequenceEqual([0, 10], model.StickyScroll.Lines.ToArray(), "in the class above the method");
            model.UpdateViewport(0, 80 * 20, 800, 400);
            Check.Equal(0, model.StickyScroll.Count, "past every block");

            model.UpdateViewport(0, 30 * 20, 800, 400);
            using var renderer = new EditorRenderer();
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, model, 0, 30 * 20, 800, 400, caretVisible: true);
            Check.True(canvas.Texts.Any(t => t.Text == "namespace N {" && t.Top == 0) && canvas.Texts.Any(t => t.Text == "  class A {" && t.Top == 20)
                       && canvas.Texts.Any(t => t.Text == "    void M() {" && t.Top == 40), "the headers are drawn on the top rows");
            Check.True(canvas.Texts.Any(t => t.Text == "11" && t.Top == 20 && t.X < model.GutterWidth), "with their own line numbers");

            using var few = Make(Nested(), new CraftOptions { StickyScrollLines = 2 });
            few.UpdateViewport(0, 30 * 20, 800, 400);
            Check.SequenceEqual([0, 10], few.StickyScroll.Lines.ToArray(), "CRAFT_STICKY_SCROLL_LINES keeps the outermost");
            using var off = Make(Nested(), new CraftOptions { StickyScroll = false });
            off.UpdateViewport(0, 30 * 20, 800, 400);
            Check.Equal(0, off.StickyScroll.Count, "CRAFT_STICKY_SCROLL off");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void TheSearchEqualsABruteForceWalkOfTheRanges()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            var random = new Random(31);
            for (int round = 0; round < 20; round++)
            {
                // Random nested blocks: "{" opens one, "}" closes the innermost, other lines are text.
                var text = new StringBuilder();
                int depth = 0;
                for (int line = 0; line < 400; line++)
                {
                    int roll = random.Next(10);
                    if (roll < 2 && depth < 12)
                    {
                        text.Append('h').Append(line.ToString(CultureInfo.InvariantCulture)).Append(" {\n");
                        depth++;
                    }
                    else if (roll < 4 && depth > 0)
                    {
                        text.Append("}\n");
                        depth--;
                    }
                    else
                    {
                        text.Append("t;\n");
                    }
                }

                using var model = Make(text.ToString(), new CraftOptions { StickyScrollLines = 1 + random.Next(6) });
                var ranges = model.Folding.Ranges.ToArray();
                for (int top = 0; top < 380; top += 1 + random.Next(4))
                {
                    model.UpdateViewport(0, top * 20, 800, 400);
                    Check.SequenceEqual(Brute(ranges, top, model.Options.StickyScrollLines), model.StickyScroll.Lines.ToArray(), $"round {round}, top line {top}");
                }
            }
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }

        // Every range holding the top line, outermost first, kept while it is above the row it covers and goes on below it.
        static int[] Brute(FoldingRange[] ranges, int top, int max)
        {
            var holding = ranges.Where(r => r.StartLine < top && r.EndLine >= top).OrderBy(r => r.StartLine).ToList();
            var kept = new List<int>();
            foreach (var range in holding)
            {
                int row = top + kept.Count;
                if (kept.Count == max || range.StartLine >= row || range.EndLine <= row)
                {
                    break;
                }

                kept.Add(range.StartLine);
            }

            return [.. kept];
        }
    }

    [Test]
    public static void AClickGoesToTheHeaderAndTheCaretStaysOutFromUnderTheHeaders()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            using var model = Make(Nested());
            var requests = new List<float>();
            model.ScrollRequested += (_, e) => requests.Add(e.Y);
            model.UpdateViewport(0, 30 * 20, 800, 400);
            model.PointerPressed(model.TextLeft + 30, 25, KeyModifiers.None);      // the class's sticky row
            model.PointerReleased();
            var snapshot = model.Snapshot;
            Check.Equal(snapshot.GetLine(10).Start + 2, model.Selection.Caret, "the caret goes to the class's header");
            Check.True(requests.Count == 1 && requests[0] == (10 - 1) * 20, $"the view scrolls so the header sits where its sticky row was ({string.Join(", ", requests)})");

            requests.Clear();
            model.UpdateViewport(0, 30 * 20, 800, 400);
            model.Select(snapshot.GetLine(34).Start, snapshot.GetLine(34).Start);
            for (int i = 0; i < 2; i++)
            {
                model.OnKey(EditorKey.Up, KeyModifiers.None);
            }

            Check.True(requests.Count > 0 && requests[^1] <= (32 - 3) * 20, $"a caret moved up under the three headers scrolls it out from under them ({string.Join(", ", requests)})");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void AFrameWithStickyHeadersAllocatesNothing()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            using var model = Make(Nested());
            using var renderer = new EditorRenderer();
            var canvas = new CountingCanvas();
            for (int i = 0; i < 100; i++)
            {
                renderer.Draw(canvas, model, 0, (30 + (i & 1)) * 20, 800, 400, caretVisible: (i & 2) == 0);
            }

            long searches = model.StickyScroll.Searches;
            long before = GC.GetAllocatedBytesForCurrentThread();
            const int frames = 200;
            for (int i = 0; i < frames; i++)
            {
                renderer.Draw(canvas, model, 0, (30 + (i & 1)) * 20, 800, 400, caretVisible: (i & 2) == 0);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(3, model.StickyScroll.Count, "three headers shown");
            Check.True(model.StickyScroll.Searches - searches <= frames, "one search a frame at most");
            Check.True(bytes < 1024, $"{frames} warm frames with sticky headers allocate nothing ({bytes:N0} bytes)");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }
}
