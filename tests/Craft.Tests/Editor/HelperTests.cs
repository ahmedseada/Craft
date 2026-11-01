// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>A semantic test language (its diagnostics given by the test) with C#-like rules: // and /* */ comments, "..." and '...' strings.</summary>
internal sealed class RuledLanguage : ILanguage
{
    private readonly FakeSemanticLanguage _inner = new();

    public static LanguageRules CLike { get; } = new("//", "/*", "*/", [new('(', ')'), new('[', ']'), new('{', '}')]) { StringDelimiters = "\"'" };

    public FakeSemanticLanguage.Session? Last => _inner.Last;

    public string Name => "ruled";

    public string Summary => "Test: C-like rules over a semantic fake";

    public IReadOnlyList<string> Extensions { get; } = [".ruled"];

    public LanguageRules Rules => CLike;

    public ILanguageSession Open(ITextBuffer buffer) => _inner.Open(buffer);
}

/// <summary>X part 9's tests' helpers: warm frames measured as the paint test measures them (rule 63).</summary>
internal static class HelperFrames
{
    /// <summary>The bytes of <paramref name="frames"/> warm frames that had no collection in or just before them, and the median and p99 in microseconds.</summary>
    public static (long Steady, long All, double Median, double P99) Warm(EditorRenderer renderer, EditorModel model, IEditorCanvas canvas, float scrollY, float width, float height, int frames = 400)
    {
        for (int i = 0; i < 100; i++)
        {
            renderer.Draw(canvas, model, 0, scrollY, width, height, (i & 1) == 0);
        }

        var times = new double[frames];
        var bytes = new long[frames];
        var gcs = new int[frames];
        long all = 0;
        for (int i = 0; i < frames; i++)
        {
            int before = GC.CollectionCount(0);
            long start = GC.GetAllocatedBytesForCurrentThread();
            long t = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, scrollY, width, height, (i & 1) == 0);
            times[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
            bytes[i] = GC.GetAllocatedBytesForCurrentThread() - start;
            gcs[i] = GC.CollectionCount(0) - before;
            all += bytes[i];
        }

        long steady = Enumerable.Range(0, frames).Where(i => gcs[i] == 0 && (i == 0 || gcs[i - 1] == 0)).Sum(i => bytes[i]);
        Array.Sort(times);
        return (steady, all, times[frames / 2], times[frames * 99 / 100]);
    }

    public static string Text(ITextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);
}

/// <summary>X part 9, item 1: word wrap (rows for shown lines only, kept per line; caret moves, hit tests, folds and sticky scroll by row).</summary>
[TestGroup("WordWrap")]
public static class WordWrapTests
{
    // "    alpha beta gamma delta epsilon zeta" at 20 columns: "    alpha beta gamma " | "delta epsilon " | "zeta", the indent kept.
    private const string Long = "    alpha beta gamma delta epsilon zeta";

    private static (EditorModel Model, TextDocument Document) Make(string text, CraftOptions? options = null, ILanguage? language = null) =>
        RealEditors.Make(text, (options ?? new CraftOptions()) with { WordWrap = "column", WordWrapColumn = 20 }, language);

    [Test]
    public static void LongLinesWrapAtWhiteSpaceKeepTheirIndentAndAreDrawnRowByRow()
    {
        var (model, _) = Make(Long + "\nshort\n");
        using var _m = model;
        Check.True(model.WordWrap.IsActive && model.WordWrap.Width == 20, "column mode wraps at CRAFT_WORD_WRAP_COLUMN");
        var wrapped = model.WordWrap.Line(0)!;
        Check.Equal(3, wrapped.Rows, "three rows");
        Check.SequenceEqual([0, 21, 35], Enumerable.Range(0, 3).Select(wrapped.RowStart).ToArray(), "the rows break after white space");
        Check.Equal(4, wrapped.Indent, "the continuation rows keep the line's indent");
        Check.Equal(5, model.RowCount, "the wrapped line's rows count (3 + 1 + the last empty line)");
        Check.Equal(3, model.RowOfLine(1), "the next line starts after the wrapped rows");
        Check.Equal(0, model.LineAtRow(2), "a continuation row's line");

        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        float left = model.TextLeft;
        var texts = canvas.Texts.Where(t => t.X >= left).ToList();
        Check.True(texts.Any(t => t.Text == "    alpha beta gamma " && t.Top == 0 && t.X == left), "row 0 at the left");
        Check.True(texts.Any(t => t.Text == "delta epsilon " && t.Top == 20 && t.X == left + 40), "row 1 at the indent");
        Check.True(texts.Any(t => t.Text == "zeta" && t.Top == 40 && t.X == left + 40), "row 2 at the indent");
        Check.True(texts.Any(t => t.Text == "short" && t.Top == 60), "the next line under the rows");
        Check.Equal(5, renderer.RowsDrawn, "five rows drawn");
        Check.True(canvas.Texts.Count(t => t.X < left && t.Text == "2") == 1, "the line numbers count lines, not rows");
    }

    [Test]
    public static void UpDownHomeAndEndMoveByVisualRow()
    {
        var (model, _) = Make(Long + "\nshort\n");
        using var _m = model;
        model.Select(6, 6);
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.Equal(23, model.Selection.Caret, "Down goes to the next row of the same line, at the same column on screen");
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.Equal(37, model.Selection.Caret, "and to its last row");
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.Equal(Long.Length + 1 + 5, model.Selection.Caret, "then to the next line (its end: it is shorter)");
        model.OnKey(EditorKey.Up, KeyModifiers.None);
        Check.Equal(37, model.Selection.Caret, "Up comes back to the wrapped row at the column it started from");
        model.Select(23, 23);
        model.OnKey(EditorKey.Home, KeyModifiers.None);
        Check.Equal(21, model.Selection.Caret, "Home on a continuation row goes to the row's start");
        model.OnKey(EditorKey.Home, KeyModifiers.None);
        Check.Equal(4, model.Selection.Caret, "Home again: the line's first non-blank");
        model.Select(6, 6);
        model.OnKey(EditorKey.End, KeyModifiers.None);
        Check.Equal(20, model.Selection.Caret, "End on a row before the last: the row's end");
        model.OnKey(EditorKey.End, KeyModifiers.None);
        Check.Equal(Long.Length, model.Selection.Caret, "End again: the line's end");
        model.Select(6, 6);
        model.AddCaretVertical(LineDirection.Down);
        Check.SequenceEqual([6, 23], model.Selections.ToArray().Select(s => s.Caret).ToArray(), "a caret below goes to the next row");
    }

    [Test]
    public static void HitTestsAndPointsFollowTheRows()
    {
        var (model, _) = Make(Long + "\nshort\n");
        using var _m = model;
        using (var renderer = new EditorRenderer())
        {
            renderer.Draw(new CountingCanvas(), model, 0, 0, 800, 400, caretVisible: false);   // the rows are wrapped when shown
        }

        float left = model.TextLeft;
        Check.Equal(22, model.PositionFromPoint(left + 51, 25), "a click on row 1, column 5 (the continuation's indent is 4)");
        Check.Equal(34, model.PositionFromPoint(left + 195, 25), "a click past a row's end stays on that row");
        Check.Equal(new EditorPoint(left + 60, 20), model.PointFromPosition(23), "a position's point is on its row");
        for (int position = 0; position <= Long.Length; position++)
        {
            var (row, column) = model.VisualOf(position);
            int back = model.PositionAtVisual(row, column, nearest: true);
            Check.Equal(position, back, $"position {position} goes to row {row}, column {column} and back");
        }
    }

    [Test]
    public static void FoldsAndStickyScrollStayRightOverWrappedLines()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            string text = "class A {\n  void M() {\n" + Long + "\n    x;\n  }\n}\n" + LayerUi.Lines(40, i => "tail" + i);
            var (model, _) = Make(text, null, new QuoteLanguage());
            using var _m = model;
            model.Folding.SetRanges(BraceFolding.Braces(model.Snapshot));
            using var renderer = new EditorRenderer();
            renderer.Draw(new CountingCanvas(), model, 0, 0, 800, 400, caretVisible: false);
            Check.Equal(3, model.RowsOfLine(2), "the long line wraps");
            Check.Equal(5, model.RowOfLine(3), "the line after it");
            Check.Equal(2, model.LineAtRow(4), "a continuation row");
            int rows = model.RowCount;
            Check.True(model.Folding.Fold(1), "fold the method");
            Check.Equal(rows - 5, model.RowCount, "the folded lines' rows (three wrapped, two more) are gone");
            Check.Equal(model.RowOfLine(1), model.RowOfLine(3), "a hidden line's row is its header's");
            Check.Equal(5, model.LineAtRow(model.RowOfLine(1) + 1), "the row after the header is the line after the fold");
            Check.True(model.Folding.Unfold(1), "unfold");
            Check.Equal(rows, model.RowCount, "the rows come back");

            // Sticky scroll: the top row is the long line's second row; the class and the method stay above it.
            model.UpdateViewport(0, 3 * 20, 800, 400);
            Check.SequenceEqual([0, 1], model.StickyScroll.Lines.ToArray(), "the headers of the blocks around a wrapped row");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void RandomEditsKeepTheRowsEqualToAFreshWrap()
    {
        var random = new Random(9);
        string[] words = ["a", "bb", "ccc", "dddd", "eeeeeeeee", "\t", " ", "  ", "\n", "fffffffffffffffffffffffff", "(x)", "{", "}"];
        var start = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            int count = random.Next(0, 14);
            for (int w = 0; w < count; w++)
            {
                start.Append(words[random.Next(words.Length - 4)]).Append(' ');
            }

            start.Append('\n');
        }

        var (model, document) = Make(start.ToString(), new CraftOptions { TabSize = 4 });
        using var _m = model;
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        for (int step = 0; step < 300; step++)
        {
            var snapshot = model.Snapshot;
            int at = random.Next(snapshot.Length + 1);
            int length = Math.Min(random.Next(0, 12), snapshot.Length - at);
            var insert = new StringBuilder();
            for (int w = random.Next(0, 4); w > 0; w--)
            {
                insert.Append(words[random.Next(words.Length)]);
            }

            document.Edit([new TextEdit(at, length, insert.ToString())], EditKind.Command, model.Selection, model.Selection);
            float scroll = random.Next(0, Math.Max(1, model.RowCount - 10)) * 20f;
            renderer.Draw(canvas, model, 0, scroll, 800, 400, caretVisible: false);

            // The rows on screen equal a fresh wrap of their text, and the row map adds up.
            snapshot = model.Snapshot;
            var visible = renderer.LastVisible;
            for (int line = visible.First; line <= visible.Last; line++)
            {
                var text = snapshot.GetText(snapshot.GetLine(line).Start, snapshot.GetLine(line).Length);
                var fresh = WrappedLine.Wrap(text, 20, 4);
                Check.Equal(fresh.Rows, model.RowsOfLine(line), $"step {step}: line {line}'s rows equal a fresh wrap");
            }

            if (step % 25 == 24)
            {
                int sum = 0;
                for (int line = 0; line < snapshot.LineCount; line++)
                {
                    Check.Equal(sum, model.RowOfLine(line), $"step {step}: line {line}'s first row is the rows before it");
                    sum += model.WordWrap.Line(line)!.Rows;
                    Check.Equal(line, model.LineAtRow(model.RowOfLine(line)), $"step {step}: line {line}'s row maps back");
                }

                Check.Equal(sum, model.RowCount, $"step {step}: the rows add up");
                var (freshModel, _) = Make(HelperFrames.Text(document), new CraftOptions { TabSize = 4 });
                using (freshModel)
                {
                    for (int line = 0; line < snapshot.LineCount; line++)
                    {
                        freshModel.WordWrap.Line(line);
                    }

                    Check.Equal(freshModel.RowCount, model.RowCount, $"step {step}: the rows equal a fresh editor's");
                }
            }
        }
    }

    [Test]
    public static void AltZSwitchesWrapKeepingTheTopLineAndTheOptionReadsItsVariables()
    {
        var options = CraftOptions.FromVariables(name => name switch { "CRAFT_WORD_WRAP" => "column", "CRAFT_WORD_WRAP_COLUMN" => "30", _ => null });
        Check.True(options is { WordWrap: "column", WordWrapColumn: 30 }, "CRAFT_WORD_WRAP and CRAFT_WORD_WRAP_COLUMN");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(name => name == "CRAFT_WORD_WRAP" ? "sometimes" : null), "an unknown mode");
        var (model, _) = RealEditors.Make(LayerUi.Lines(50, i => Long + Long + i));
        using var _m = model;
        Check.True(!model.WordWrap.IsActive, "off by default");
        var command = StandardShortcuts.LineCommands.First(c => c.Id == StandardShortcuts.ToggleWordWrap);
        var (altZ, _) = KeyBinding.ParseKeys("Alt+Z");
        Check.True(StandardShortcuts.Bindings.Any(b => b.CommandId == StandardShortcuts.ToggleWordWrap && Equals(b.Chord, altZ)), "Alt+Z is bound");
        command.Run(model);
        Check.True(model.WordWrap.IsActive && model.WordWrap.Mode == "on", "Alt+Z wraps at the editor's width");
        Check.True(model.ContentWidth <= model.ViewportWidth, "no sideways scrolling while wrapped at the width");
        command.Run(model);
        Check.True(!model.WordWrap.IsActive, "Alt+Z again unwraps");
    }

    [Test]
    public static void AWarmFrameOfAWrapped100000LineFileWithBracketColoursAndInlineDiagnosticsAllocatesNothing()
    {
        var text = new StringBuilder(100_000 * 120);
        for (int i = 0; i < 100_000; i++)
        {
            text.Append("    if (value").Append(i).Append(" > limit[").Append(i % 7).Append("]) { total += Compute(value").Append(i)
                .Append(", \"a (string) with [brackets]\", other); } // note (x)\n");
        }

        var language = new RuledLanguage();
        var options = new CraftOptions { WordWrap = "on", BracketColors = true, InlineDiagnostics = "all", Rulers = "40,60", PaintCacheLines = 64 };
        var (model, _) = RealEditors.Make(text.ToString(), options, language);
        using var _m = model;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var snapshot = model.Snapshot;
        var session = language.Last!;
        session.Diagnostics = new DiagnosticSet(snapshot, Enumerable.Range(0, 10_000).Select(i =>
            new EditorDiagnostic(snapshot.GetLine(i * 10).Start + 8, 5, i % 3 == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, "E1", "The name 'value' does not exist in the current context, and this message is long enough to be cut")));
        session.Raise();
        LayerUi.Pump(ui);

        using var renderer = new EditorRenderer();
        using var canvas = new LayoutCanvas { Record = false };
        const float width = 800, height = 600;
        model.UpdateViewport(0, 0, width, height);
        float scrollY = model.RowOfLine(50_000) * 20f;
        long start = Stopwatch.GetTimestamp();
        renderer.Draw(canvas, model, 0, scrollY, width, height, caretVisible: true);
        double coldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        scrollY = model.ScrollY;
        int scannedFirst = (int)model.BracketColors.LinesScanned;
        Check.True(scannedFirst <= options.BracketColorsScanLines + 1, $"a frame reads at most CRAFT_BRACKET_COLORS_SCAN_LINES bracket states ({scannedFirst})");
        int frames = 1;
        start = Stopwatch.GetTimestamp();
        while (!model.BracketColors.IsComplete && frames < 20)
        {
            LayerUi.Pump(ui);
            renderer.Draw(canvas, model, 0, scrollY, width, height, caretVisible: true);
            frames++;
        }

        double completeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LayerUi.Pump(ui);
        Check.True(model.BracketColors.IsComplete, $"the bracket states reach the file's end over a few frames ({frames})");
        renderer.Draw(canvas, model, 0, scrollY, width, height, caretVisible: true);
        LayerUi.Pump(ui);
        long wrappedBefore = model.WordWrap.LinesWrapped;
        long scannedBefore = model.BracketColors.LinesScanned;
        long laid = canvas.Surface.LinesLaid + canvas.Surface.RunsLaid;
        var (steady, all, median, p99) = HelperFrames.Warm(renderer, model, canvas, scrollY, width, height, 1000);
        Check.Equal(0L, steady, $"a warm frame allocates nothing ({all} bytes over 1,000 frames in all, collections included)");
        Check.Equal(wrappedBefore, model.WordWrap.LinesWrapped, "a warm frame wraps nothing");
        Check.Equal(scannedBefore, model.BracketColors.LinesScanned, "a warm frame reads no bracket state");
        Check.Equal(laid, canvas.Surface.LinesLaid + canvas.Surface.RunsLaid, "a warm frame lays nothing out");
        Check.True(renderer.RowsDrawn > renderer.LinesDrawn, $"the lines wrap ({renderer.LinesDrawn} lines in {renderer.RowsDrawn} rows)");
        Check.True(model.WordWrap.LinesHeld <= renderer.LinesDrawn + (2 * options.PaintCacheLines) + 2, $"only the lines on screen and near it are wrapped ({model.WordWrap.LinesHeld} kept of 100,000)");
        Check.True(renderer.BracketsColored > 0 && renderer.InlineDiagnosticsDrawn > 0 && renderer.RulersDrawn > 0, "brackets coloured, messages and rulers drawn");
        string result = string.Create(CultureInfo.InvariantCulture,
            $"Warm frame of a wrapped 100,000-line file (bracket colours, inline diagnostics, rulers; {renderer.LinesDrawn} lines in {renderer.RowsDrawn} rows, 800 x 600, fixed metrics): cold frame {coldMs:F1} ms ({scannedFirst:N0} bracket states read), {frames} frames to read the rest ({completeMs:F1} ms); warm median {median:F1} us, p99 {p99:F1} us, {steady} B a frame; {model.WordWrap.LinesHeld} lines wrapped and kept");
        Console.WriteLine("    measured: " + result);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "editing-helpers.txt"), result + Environment.NewLine);
    }
}

/// <summary>X part 9, item 2: bracket pair colours by depth, strings and comments skipped, unmatched in the error colour, kept per line.</summary>
[TestGroup("BracketColors")]
public static class BracketColorTests
{
    private static (EditorModel Model, TextDocument Document) Make(string text, CraftOptions? options = null) =>
        RealEditors.Make(text, options ?? new CraftOptions(), new RuledLanguage());

    private static List<(string Text, uint Argb)> Draw(EditorModel model, int frames = 2)
    {
        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        for (int i = 0; i < frames; i++)
        {
            canvas.Clear();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        }

        return [.. canvas.Texts.Where(t => t.X >= model.TextLeft).Select(t => (t.Text, t.Argb))];
    }

    [Test]
    public static void BracketsTakeTheColourOfTheirDepth()
    {
        var (model, _) = Make("f(a[b{c}])\n");
        using var _m = model;
        var colors = model.Theme.Colors;
        var texts = Draw(model);
        Check.True(texts.Contains(("(", colors.BracketDepth1)) && texts.Contains((")", colors.BracketDepth1)), "depth 1");
        Check.True(texts.Contains(("[", colors.BracketDepth2)) && texts.Contains(("]", colors.BracketDepth2)), "depth 2");
        Check.True(texts.Contains(("{", colors.BracketDepth3)) && texts.Contains(("}", colors.BracketDepth3)), "depth 3");
        Check.True(colors.BracketDepth(7) == colors.BracketDepth1 && colors.BracketDepth(4) != colors.BracketDepth1, "six colours in turn");
        var off = Make("f(a)\n", new CraftOptions { BracketColors = false }).Model;
        using (off)
        {
            Check.True(Draw(off).Any(t => t.Text == "f(a)"), "CRAFT_BRACKET_COLORS off draws the line as before");
        }
    }

    [Test]
    public static void BracketsInStringsAndCommentsAreSkippedAndAnUnmatchedOneIsAnError()
    {
        var (model, _) = Make("x(\"(\", '[', // {\n/* ( */ y)\nz)\n(w\n");
        using var _m = model;
        var colors = model.Theme.Colors;
        var texts = Draw(model);
        Check.Equal(1, model.BracketColors.DepthAt(1), "the string's, the char's and the comment's brackets are skipped");
        Check.True(texts.Count(t => t.Text == ")" && t.Argb == colors.BracketDepth1) == 1, "the first ')' closes the first '('");
        Check.True(texts.Count(t => t.Text == ")" && t.Argb == colors.BracketUnmatched) == 1, "a ')' with nothing open is unmatched");
        Check.True(texts.Count(t => t.Text == "(" && t.Argb == colors.BracketUnmatched) == 1, "a '(' never closed is unmatched");
        Check.Equal(colors.Error, colors.BracketUnmatched, "unmatched takes the error colour");
    }

    [Test]
    public static void DepthsFollowEditsIncrementallyAndEqualAFreshRead()
    {
        var random = new Random(4);
        var text = new StringBuilder();
        string[] pieces = ["{", "}", "(", ")", "[", "]", "\"{(\"", "'('", "// (\n", "/* { */", "x", " ", "\n"];
        for (int i = 0; i < 6000; i++)
        {
            text.Append(pieces[random.Next(pieces.Length)]);
        }

        var (model, document) = Make(text.ToString());
        using var _m = model;
        int budget = int.MaxValue;
        model.BracketColors.Ensure(model.Snapshot.LineCount - 1, ref budget);
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        for (int step = 0; step < 200; step++)
        {
            var snapshot = model.Snapshot;
            int at = random.Next(snapshot.Length + 1);
            int length = Math.Min(random.Next(0, 4), snapshot.Length - at);
            string insert = string.Concat(Enumerable.Range(0, random.Next(0, 3)).Select(_ => pieces[random.Next(pieces.Length)]));
            document.Edit([new TextEdit(at, length, insert)], EditKind.Command, model.Selection, model.Selection);
            renderer.Draw(canvas, model, 0, random.Next(0, Math.Max(1, model.RowCount - 20)) * 20f, 800, 400, caretVisible: false);
            if (step % 20 == 19)
            {
                budget = int.MaxValue;
                model.BracketColors.Ensure(model.Snapshot.LineCount - 1, ref budget);
                var (fresh, _) = Make(HelperFrames.Text(document));
                using (fresh)
                {
                    budget = int.MaxValue;
                    fresh.BracketColors.Ensure(fresh.Snapshot.LineCount - 1, ref budget);
                    for (int line = 0; line < fresh.Snapshot.LineCount; line++)
                    {
                        Check.Equal(fresh.BracketColors.DepthAt(line), model.BracketColors.DepthAt(line), $"step {step}: line {line}'s depth equals a fresh read");
                    }
                }
            }
        }

        // Typing a letter reads its own line again, and the next one finds its old state: nothing more.
        budget = int.MaxValue;
        model.BracketColors.Ensure(model.Snapshot.LineCount - 1, ref budget);
        long before = model.BracketColors.LinesScanned;
        int middle = model.Snapshot.GetLine(model.Snapshot.LineCount / 2).Start;
        document.Edit([TextEdit.Insert(middle, "q")], EditKind.Typing, model.Selection, model.Selection);
        budget = int.MaxValue;
        model.BracketColors.Ensure(model.Snapshot.LineCount - 1, ref budget);
        Check.True(model.BracketColors.LinesScanned - before <= 2, $"a keystroke reads its own line only ({model.BracketColors.LinesScanned - before})");
    }

    [Test]
    public static void AJumpDeepIntoALongFileReadsAtMostTheBudgetAFrameAndAWarmFrameAllocatesNothing()
    {
        var (model, _) = Make(LayerUi.Lines(100_000, i => "    call(a[" + i + "], { b }); // (c"), new CraftOptions { BracketColorsScanLines = 20_000 });
        using var _m = model;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        float y = 90_000 * 20f;
        renderer.Draw(canvas, model, 0, y, 800, 400, caretVisible: false);
        Check.Equal(20_000L, model.BracketColors.LinesScanned, "the first frame reads its budget, not the file");
        Check.Equal(0, renderer.BracketsColored, "the lines on screen wait for their depth (drawn plain)");
        Check.True(!ui.IsEmpty, "the frame asked for another");
        int frames = 1;
        while (renderer.BracketsColored == 0 && frames < 10)
        {
            LayerUi.Pump(ui);
            long before = model.BracketColors.LinesScanned;
            renderer.Draw(canvas, model, 0, y, 800, 400, caretVisible: false);
            Check.True(model.BracketColors.LinesScanned - before <= 20_001, "never more than the budget a frame");
            frames++;
        }

        Check.True(renderer.BracketsColored > 0, $"coloured after {frames} frames");
        LayerUi.Pump(ui);
        renderer.Draw(canvas, model, 0, y, 800, 400, caretVisible: false);
        LayerUi.Pump(ui);
        var (steady, all, _, _) = HelperFrames.Warm(renderer, model, canvas, y, 800, 400);
        Check.Equal(0L, steady, $"a warm frame with bracket colours allocates nothing ({all} bytes in all)");
    }
}

/// <summary>X part 9, item 6: the line's most severe diagnostic's first message, faded, after its text.</summary>
[TestGroup("InlineDiagnostics")]
public static class InlineDiagnosticTests
{
    private static (EditorModel Model, FakeSemanticLanguage.Session Session, ConcurrentQueue<Action> Ui) Make(string text, string mode)
    {
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make(text, new CraftOptions { InlineDiagnostics = mode }, language);
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var session = language.Last!;
        var snapshot = model.Snapshot;
        session.Diagnostics = new DiagnosticSet(snapshot,
        [
            new EditorDiagnostic(0, 3, DiagnosticSeverity.Warning, "W0", "first warning"),
            new EditorDiagnostic(4, 1, DiagnosticSeverity.Error, "E1", "the error\nsecond line"),
            new EditorDiagnostic(snapshot.GetLine(1).Start, 3, DiagnosticSeverity.Warning, "W1", "a warning"),
            new EditorDiagnostic(snapshot.GetLine(2).Start, 3, DiagnosticSeverity.Info, "I1", "a note"),
        ]);
        session.Raise();
        LayerUi.Pump(ui);
        return (model, session, ui);
    }

    private static List<(string Text, float X, float Top, uint Argb)> Draw(EditorModel model, float width = 800)
    {
        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        renderer.Draw(canvas, model, 0, 0, width, 400, caretVisible: false);
        return [.. canvas.Texts];
    }

    [Test]
    public static void TheMostSevereMessageOfALineIsDrawnFadedAfterItsTextByMode()
    {
        const string text = "int x = 1;\nint y = 2;\nint z = 3;\n";
        var (errors, _, _) = Make(text, "errors");
        using (errors)
        {
            var colors = errors.Theme.Colors;
            var texts = Draw(errors);
            Check.True(texts.Any(t => t.Text == "the error" && t.Argb == colors.InlineError && t.Top == 0 && t.X == errors.TextLeft + (12 * 10)),
                "the error's first line, two cells after the text, in the faded error colour (it wins over the line's warning)");
            Check.True(!texts.Any(t => t.Text is "a warning" or "a note" or "first warning"), "errors only");
        }

        var (all, _, _) = Make(text, "all");
        using (all)
        {
            var colors = all.Theme.Colors;
            var texts = Draw(all);
            Check.True(texts.Any(t => t.Text == "a warning" && t.Argb == colors.InlineWarning && t.Top == 20), "a warning with all");
            Check.True(texts.Any(t => t.Text == "a note" && t.Argb == colors.InlineInfo && t.Top == 40), "a note with all");
        }

        var (off, _, _) = Make(text, "off");
        using (off)
        {
            Check.True(!Draw(off).Any(t => t.Text is "the error" or "a warning"), "off draws none");
        }

        Check.True(new CraftOptions().InlineDiagnostics == "errors", "errors by default");
    }

    [Test]
    public static void AMessageIsCutToTheViewportAndAWarmFrameAllocatesNothing()
    {
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make(LayerUi.Lines(30, i => "line " + i), new CraftOptions { InlineDiagnostics = "all", OverviewRuler = false }, language);
        using var _m = model;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var snapshot = model.Snapshot;
        string message = new('m', 300);
        language.Last!.Diagnostics = new DiagnosticSet(snapshot, Enumerable.Range(0, 30).Select(i => new EditorDiagnostic(snapshot.GetLine(i).Start, 2, DiagnosticSeverity.Error, "E", message)));
        language.Last.Raise();
        LayerUi.Pump(ui);
        var texts = Draw(model, 400);
        var cut = texts.Where(t => t.Text.StartsWith('m')).ToList();
        Check.Equal(21, cut.Count, "one message on each line drawn (the screen's twenty and the one under it)");
        Check.True(cut.All(t => t.Text.EndsWith('…') && t.X + (t.Text.Length * 10) <= 400), "cut with an ellipsis inside the viewport");

        using var renderer = new EditorRenderer();
        var (steady, all, _, _) = HelperFrames.Warm(renderer, model, new CountingCanvas(), 0, 400, 400);
        Check.Equal(0L, steady, $"a warm frame with inline messages allocates nothing ({all} bytes in all)");
        Check.Equal(21, renderer.InlineDiagnosticsDrawn, "drawn in each frame");
    }
}

/// <summary>X part 9, item 7: the lightbulb in the gutter where code actions exist (asked off the UI thread, kept per version).</summary>
[TestGroup("Lightbulb")]
public static class LightbulbTests
{
    [Test]
    public static void TheLightbulbShowsWhereCodeActionsExistAndAsksOncePerVersionAndRange()
    {
        var language = new FakeSemanticLanguage();
        var (model, _, _) = Editors.Make("bad code;\nfine;\nother;\n", new CraftOptions { LightbulbDelayMilliseconds = 0 }, language);
        using var _m = model;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var snapshot = model.Snapshot;
        language.Last!.Diagnostics = new DiagnosticSet(snapshot, [new EditorDiagnostic(0, 3, DiagnosticSeverity.Error, "E1", "bad")]);
        language.Last.Raise();
        LayerUi.Pump(ui);
        int threads = 0;
        int uiThread = Environment.CurrentManagedThreadId;
        model.Lightbulb.Probe = query =>
        {
            if (Environment.CurrentManagedThreadId != uiThread)
            {
                Interlocked.Increment(ref threads);
            }

            return query.Diagnostics.Count > 0;
        };
        model.Select(5, 5);                                           // on the line with the error, not on it
        LayerUi.WaitAndPump(ui);
        Check.Equal(0, model.LightbulbLine, "the bulb on the line of a fixable problem");
        Check.Equal(1, model.Lightbulb.Asks, "asked once");
        Check.Equal(1, threads, "off the UI thread");
        model.Select(snapshot.GetLine(1).Start, snapshot.GetLine(1).Start);
        LayerUi.WaitAndPump(ui);
        Check.Equal(-1, model.LightbulbLine, "none where nothing is offered");
        model.Select(5, 5);
        LayerUi.Pump(ui);
        Check.Equal(0, model.LightbulbLine, "back: the answer kept for this version shows at once");
        Check.Equal(2, model.Lightbulb.Asks, "no new question for a range already asked in this version");

        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(renderer.LightbulbDrawn && canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == model.Theme.Colors.Lightbulb && c.X < model.Metrics.CellWidth), "drawn at the gutter's left");
        var (steady, all, _, _) = HelperFrames.Warm(renderer, model, new CountingCanvas(), 0, 800, 400);
        Check.Equal(0L, steady, $"a warm frame with the bulb allocates nothing ({all} bytes in all)");

        int fixes = 0;
        model.QuickFixRequested += (_, _) => fixes++;
        model.PointerPressed(model.Metrics.CellWidth * 0.5f, 10, KeyModifiers.None);
        model.PointerReleased();
        Check.Equal(1, fixes, "a click on the bulb asks for Quick fix");
        Check.Equal(0, model.Selection.Caret, "the caret goes to the problem first");
    }

    [Test]
    public static async Task TheShellsProbeAsksTheCodeActionProviders()
    {
        var provider = new BulbProvider();
        CodeActionProviders.Register(provider);
        try
        {
            using var folder = new TempFolder();
            string file = folder.File("a.txt");
            await File.WriteAllTextAsync(file, "text");
            var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
            using var host = new EditorHost();
            using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), new SilentUi());
            var tab = (await shell.OpenFileAsync(file))!;
            var probe = ShellModel.LightbulbProbe(tab);
            Check.True(probe(new LightbulbQuery(0, 1, [new EditorDiagnostic(0, 1, DiagnosticSeverity.Error, "E", "m")])), "a provider offers a fix for a problem");
            Check.True(!probe(new LightbulbQuery(0, 0, [])), "and nothing for a caret alone");
            Check.True(new CraftOptions().Lightbulb, "on by default");
        }
        finally
        {
            CodeActionProviders.Unregister(provider.Name);
        }
    }

    private sealed class BulbProvider : ICodeActionProvider
    {
        public string Name => "test-bulb";

        public string Summary => "Test: a fix for any problem";

        public IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context) =>
            context.Diagnostics.Count > 0 ? [new CodeAction("fix", "Fix", CodeActionKinds.Fix, _ => Task.CompletedTask)] : [];
    }

    internal sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    internal sealed class SilentUi : IShellUi
    {
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);
    }
}

/// <summary>X part 9, item 8: rendered white space, vertical rulers, and trimming and the final newline on save (each one undo step).</summary>
[TestGroup("Whitespace")]
public static class WhitespaceTests
{
    private static int Marks(string mode, SelectionState? selection = null)
    {
        var (model, _, _) = Editors.Make("  a b  c d \n", new CraftOptions { RenderWhitespace = mode });
        using var _m = model;
        if (selection is { } s)
        {
            model.Select(s.Anchor, s.Caret);
        }

        using var renderer = new EditorRenderer();
        renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 400, caretVisible: false);
        return renderer.WhitespaceMarksDrawn;
    }

    [Test]
    public static void WhiteSpaceIsDrawnByModeAsDotsAndArrows()
    {
        Check.Equal(0, Marks("none"), "none");
        Check.Equal(7, Marks("all"), "all: every space");
        Check.Equal(5, Marks("boundary"), "boundary: leading, trailing and the run of two");
        Check.Equal(0, Marks("selection"), "selection: none without one");
        Check.Equal(3, Marks("selection", new SelectionState(2, 7)), "selection: the spaces inside it");
        var (model, _, _) = Editors.Make("\tx\n", new CraftOptions { RenderWhitespace = "all" });
        using var _m = model;
        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(3, canvas.Calls.Count(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Whitespace), "a tab is an arrow (a shaft and two strokes) in the faded white space colour");
        Check.Equal("selection", new CraftOptions().RenderWhitespace, "the selection's white space by default");
    }

    [Test]
    public static void RulersAreDrawnAtTheirColumnsAndAFilesLineLengthAddsOne()
    {
        Check.Equal("4,8,120", CraftOptions.RulerColumns(" 8, 4 ;120"), "CRAFT_RULERS read in order");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_RULERS" ? "80,wide" : null), "a ruler that is not a column");
        var (model, _, _) = Editors.Make("text\n", new CraftOptions { Rulers = "4,8", RenderWhitespace = "all" });
        using var _m = model;
        var canvas = new RecordingCanvas();
        using var renderer = new EditorRenderer();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(2, renderer.RulersDrawn, "two rulers");
        Check.True(canvas.Calls.Any(c => c.Kind == "line" && c.Argb == model.Theme.Colors.VerticalRuler && Math.Abs(c.X - (model.TextLeft + 40)) <= 1 && c.H >= 399), "at column 4, the viewport's height");
        model.ApplyFileSettings(new FileSettings { MaxLineLength = 12 });
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(3, renderer.RulersDrawn, "max_line_length adds a ruler");
        var (steady, all, _, _) = HelperFrames.Warm(renderer, model, new CountingCanvas(), 0, 800, 400);
        Check.Equal(0L, steady, $"a warm frame with rulers and white space allocates nothing ({all} bytes in all)");
    }

    [Test]
    public static void TrimAndTheFinalNewlineAreEachOneUndoStep()
    {
        var (model, document) = RealEditors.Make("a  \nb\t\nc");
        using var _m = model;
        model.Select(2, 2);
        Check.True(model.TrimTrailingWhitespace(), "trimmed");
        Check.Equal("a\nb\nc", HelperFrames.Text(document), "the ends of the lines");
        Check.Equal(1, model.Selection.Caret, "the caret in the trimmed space moves to the line's end");
        Check.True(model.InsertFinalNewline(), "a final newline");
        Check.Equal("a\nb\nc\n", HelperFrames.Text(document), "ends with a break");
        Check.True(!model.InsertFinalNewline() && !model.TrimTrailingWhitespace(), "nothing more to do");
        document.Undo();
        Check.Equal("a\nb\nc", HelperFrames.Text(document), "one undo takes the final newline back");
        document.Undo();
        Check.Equal("a  \nb\t\nc", HelperFrames.Text(document), "the next one the trimming");
    }

    [Test]
    public static async Task SaveTrimsAndEndsWithANewlineWhenTheOptionsSaySoAsOneUndoStepEach()
    {
        foreach (bool on in new[] { false, true })
        {
            using var folder = new TempFolder();
            await File.WriteAllTextAsync(folder.File(".editorconfig"), "root = true\n");   // no .editorconfig above the test's folder applies
            string file = folder.File("notes.txt");
            await File.WriteAllTextAsync(file, "x  \ny");
            string value = on ? "true" : "false";
            var settings = new CraftSettings(name => name is "CRAFT_TRIM_TRAILING_WHITESPACE" or "CRAFT_INSERT_FINAL_NEWLINE" ? value : null, SettingsFile.Load(folder.File("data/settings.json")));
            using var host = new EditorHost();
            using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi());
            var tab = (await shell.OpenFileAsync(file))!;
            var document = (TextDocument)tab.Document;
            document.Edit([TextEdit.Insert(0, "w")], EditKind.Typing, SelectionState.At(0), SelectionState.At(1));
            Check.True(await shell.SaveAsync(tab), "saved");
            string saved = await File.ReadAllTextAsync(file);
            Check.Equal(on ? "wx\ny\n" : "wx  \ny", saved.Replace("\r\n", "\n", StringComparison.Ordinal), on ? "trimmed and ended with a break before it was written" : "as it was with the options off");
            if (on)
            {
                document.Undo();
                document.Undo();
                Check.Equal("wx  \ny", HelperFrames.Text(document), "two undo steps: the final newline, then the trimming");
            }
        }
    }
}

/// <summary>X part 9, item 9: .editorconfig applied per file (globs per file, cached per folder, read off the UI thread, refreshed).</summary>
[TestGroup("EditorConfigApplied")]
public static class EditorConfigTests
{
    private const string Root = """
        root = true

        [*]
        indent_style = space
        indent_size = 4
        end_of_line = lf
        insert_final_newline = true

        [*.{json,xml}]
        indent_size = 2

        [Makefile]
        indent_style = tab
        tab_width = 8

        [lib/**.js]
        max_line_length = 100

        [file{1..3}.txt]
        charset = utf-8-bom

        # a comment
        [*.md]
        trim_trailing_whitespace = false
        insert_final_newline = unset
        """;

    private const string Sub = """
        [*.cs]
        indent_size = 2
        trim_trailing_whitespace = true
        end_of_line = crlf
        """;

    private static async Task<FileSettings> Of(string path) => await FileSettingsProviders.ForFileAsync(path);

    [Test]
    public static async Task SectionsAreMatchedPerFileAndTheNearerFileWins()
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File(".editorconfig"), Root);
        Directory.CreateDirectory(folder.File("sub"));
        await File.WriteAllTextAsync(folder.File("sub/.editorconfig"), Sub);

        var cs = await Of(folder.File("a.cs"));
        Check.True(cs is { TabSize: 4, InsertSpaces: true, NewLine: "\n", InsertFinalNewline: true, TrimTrailingWhitespace: null }, $"[*] for a.cs ({cs})");
        var sub = await Of(folder.File("sub/b.cs"));
        Check.True(sub is { TabSize: 2, NewLine: "\r\n", TrimTrailingWhitespace: true, InsertFinalNewline: true }, $"the nearer file wins for sub/b.cs ({sub})");
        Check.Equal(2, sub.Sources.Count, "both files said something");
        Check.Equal(2, (await Of(folder.File("x.json"))).TabSize, "{json,xml}");
        Check.Equal(2, (await Of(folder.File("deep/er/y.xml"))).TabSize, "a glob without a slash matches at any depth");
        var make = await Of(folder.File("Makefile"));
        Check.True(make is { InsertSpaces: false, TabSize: 8 }, $"tabs of tab_width ({make})");
        Check.Equal(100, (await Of(folder.File("lib/a/b.js"))).MaxLineLength, "lib/**.js");
        Check.Equal((int?)null, (await Of(folder.File("other/b.js"))).MaxLineLength, "a glob with a slash is anchored to its folder");
        Check.Equal("utf-8-bom", (await Of(folder.File("file2.txt"))).Charset, "{1..3}");
        Check.Equal((string?)null, (await Of(folder.File("file4.txt"))).Charset, "outside the range");
        var md = await Of(folder.File("readme.md"));
        Check.True(md is { TrimTrailingWhitespace: false, InsertFinalNewline: null }, $"unset takes a value back ({md})");
        Check.Equal(sub with { Sources = [] }, FileSettingsProviders.Cached(folder.File("sub/b.cs")) is { } kept ? kept with { Sources = [] } : null, "kept per folder: known without reading again");
        Check.True(FileSettingsProviders.Cached(folder.File("never/read/c.cs")) is null, "a folder never read is not known");
    }

    [Test]
    public static async Task TheShellReadsThemOffTheUiThreadAndAgainWhenAnEditorConfigChanges()
    {
        using var folder = new TempFolder();
        string config = folder.File(".editorconfig");
        await File.WriteAllTextAsync(config, "root = true\n[*.txt]\nindent_size = 3\n");
        string file = folder.File("a.txt");
        await File.WriteAllTextAsync(file, "x");
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi());
        int thread = Environment.CurrentManagedThreadId;
        var read = await shell.FileSettingsAsync(file);
        Check.Equal(3, read.TabSize, "read");
        Check.Equal(3, shell.CachedFileSettings(file)?.TabSize, "then known at once");
        var changed = new List<string?>();
        shell.FileSettingsChanged += (_, e) => changed.Add(e.Path);
        await File.WriteAllTextAsync(config, "root = true\n[*.txt]\nindent_size = 5\n");
        shell.OnConfigurationFileChanged(config);
        Check.True(changed is [null], "every file's settings are said to change");
        Check.True(shell.CachedFileSettings(file) is null, "forgotten");
        Check.Equal(5, (await shell.FileSettingsAsync(file)).TabSize, "read again");
        await File.WriteAllTextAsync(config, "root = true\n[*.txt]\nindent_size = 6\n");
        File.SetLastWriteTimeUtc(config, DateTime.UtcNow.AddMinutes(1));
        Check.Equal(6, (await shell.FileSettingsAsync(file)).TabSize, "a changed write time is read again when asked");
        _ = thread;

        // CRAFT_EDITORCONFIG off: the options alone.
        var off = new CraftSettings(name => name == "CRAFT_EDITORCONFIG" ? "false" : null, SettingsFile.Load(folder.File("data/off.json")));
        using var host2 = new EditorHost();
        using var shell2 = new ShellModel(host2, off, RecentList.Load(folder.File("data/recent2.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi());
        Check.True((await shell2.FileSettingsAsync(file)).IsEmpty, "CRAFT_EDITORCONFIG=false reads none");
    }

    [Test]
    public static void TheSettingsApplyToTheEditorOverItsOptionsAndTheStatusSaysSo()
    {
        var (model, document) = RealEditors.Make("x\n", new CraftOptions { TabSize = 4, InsertSpaces = true });
        using var _m = model;
        var settings = new FileSettings { TabSize = 2, InsertSpaces = false, NewLine = "\r\n", MaxLineLength = 40, Sources = ["/w/.editorconfig"] };
        model.ApplyFileSettings(settings);
        Check.True(model.Options is { TabSize: 2, InsertSpaces: false } && model.BaseOptions.TabSize == 4, "the file's settings over the options");
        model.Select(1, 1);
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.Equal("x\r\n\n", HelperFrames.Text(document), "Enter inserts end_of_line's break");
        model.OnKey(EditorKey.Tab, KeyModifiers.None);
        Check.Equal("x\r\n\t\n", HelperFrames.Text(document), "Tab inserts a tab with indent_style = tab");
        Check.True(model.RulerColumns.ToArray().Contains(40), "max_line_length is a ruler");
        Check.True(ShellText.IndentationTooltip(model).Contains(".editorconfig", StringComparison.Ordinal), "the indentation item says where it comes from");
        Check.True(ShellText.LineEndingTooltip(model).Contains("CRLF", StringComparison.Ordinal) && ShellText.LineEndingTooltip(model).Contains(".editorconfig", StringComparison.Ordinal), "and the line ending item");
        model.ApplyFileSettings(FileSettings.None);
        Check.True(model.Options.TabSize == 4 && !ShellText.IndentationTooltip(model).Contains(".editorconfig", StringComparison.Ordinal), "none: the options again");
    }

    [Test]
    public static async Task SaveFollowsTheFilesTrimFinalNewlineAndANewFilesCharset()
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File(".editorconfig"), "root = true\n[*.cs]\ntrim_trailing_whitespace = true\ninsert_final_newline = true\n[*.txt]\ncharset = utf-8-bom\n");
        string file = folder.File("a.cs");
        await File.WriteAllTextAsync(file, "x  \ny");
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi());
        Check.True(!shell.Options.TrimTrailingWhitespace && !shell.Options.InsertFinalNewline, "the options say no");
        var tab = (await shell.OpenFileAsync(file))!;
        ((TextDocument)tab.Document).Edit([TextEdit.Insert(0, "w")], EditKind.Typing, SelectionState.At(0), SelectionState.At(1));
        Check.True(await shell.SaveAsync(tab), "saved");
        Check.Equal("wx\ny\n", (await File.ReadAllTextAsync(file)).Replace("\r\n", "\n", StringComparison.Ordinal), "the .editorconfig wins: trimmed, a final newline");

        var fresh = shell.NewFile(folder.File("new.txt"));
        ((TextDocument)fresh.Document).Edit([TextEdit.Insert(0, "hi")], EditKind.Typing, SelectionState.At(0), SelectionState.At(2));
        Check.True(await shell.SaveAsync(fresh), "a new file saved");
        var bytes = await File.ReadAllBytesAsync(folder.File("new.txt"));
        Check.True(bytes is [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'], "charset = utf-8-bom for a new file");
    }
}
