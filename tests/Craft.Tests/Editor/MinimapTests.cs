// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.CSharp;
using Craft.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>
/// The X restart, piece 2b: the minimap (a rect per coloured run from a per-line cache keyed by version and theme, the
/// syntactic colours only, the slider, click and drag, no redraw on the blink, nothing allocated by a warm frame, one per view).
/// </summary>
[TestGroup("Minimap")]
public static class MinimapTests
{
    private static readonly CraftOptions On = new() { Minimap = true };

    private static (EditorModel Model, TextDocument Document) Make(int lines, CraftOptions? options = null, ILanguage? language = null, Func<int, string>? line = null)
    {
        var (model, document) = RealEditors.Make(LayerUi.Lines(lines, line ?? (i => $"say \"hello {i}\" now // a note on it")), options ?? On, language ?? new QuoteLanguage());
        model.MinimapShown = true;
        return (model, document);
    }

    // Draws until the minimap has checked every kept line's colours again (a budget a frame); the frames it took.
    private static int Settle(MinimapRenderer minimap, IEditorCanvas canvas, EditorModel model, float width, float height)
    {
        int frames = 0;
        while (minimap.NeedsRedraw(model, width, height) && frames < 100)
        {
            (canvas as RecordingCanvas)?.Clear();
            minimap.Draw(canvas, model, width, height);
            frames++;
        }

        return frames;
    }

    // Rule 71: the incremental cache draws exactly what a renderer building every line afresh draws.
    private static void SameAsFresh(MinimapRenderer minimap, EditorModel model, float width, float height, string when)
    {
        var kept = new RecordingCanvas();
        minimap.Draw(kept, model, width, height);
        Check.True(!minimap.Pending, $"{when}: settled");
        using var fresh = new MinimapRenderer();
        var built = new RecordingCanvas();
        fresh.Draw(built, model, width, height);
        Check.SequenceEqual(built.Calls, kept.Calls, $"{when}: the kept lines draw what a fresh build draws");
    }

    private static List<(string Kind, float X, float Y, float W, float H, uint Argb, string? Text)> Rects(RecordingCanvas canvas, float y, float rowHeight) =>
        canvas.Calls.Where(c => c.Kind == "fill" && Math.Abs(c.Y - y) < 0.01 && Math.Abs(c.H - (rowHeight * 0.75f)) < 0.01).ToList();

    [Test]
    public static void EachLineIsARectPerColouredRunFromACacheKeyedByVersionAndTheme()
    {
        var (model, _) = Make(1000);
        using var _m = model;
        float width = MinimapRenderer.WidthOf(model.Options);
        Check.Equal(138f, width, "the default minimap: a 4-unit margin, 120 columns of 1 unit, and the 14-unit ruler at its right edge");
        Check.Equal(0f, model.OverviewRulerWidth, "while the minimap shows, the text's viewport keeps no room for the ruler");
        using var minimap = new MinimapRenderer();
        var canvas = new RecordingCanvas();
        minimap.Draw(canvas, model, width, 400);
        var theme = model.Theme;
        static uint Soft(uint argb) => (argb & 0x00FFFFFFu) | 0xB0000000u;
        uint plain = Soft(theme.Colors.Foreground);
        uint text = Soft(theme.StyleOf(TokenClasses.String).Argb);
        uint comment = Soft(theme.StyleOf(TokenClasses.Comment).Argb);
        // say "hello 0" now // a note on it: "say", the string (its space inside joins), "now", the comment's words joined.
        var first = Rects(canvas, 0, 2);
        Check.SequenceEqual([(4f, 3f, plain), (8f, 9f, text), (18f, 3f, plain), (22f, 15f, comment)], first.Select(r => (r.X, r.W, r.Argb)).ToList(), "line 0: one rect per coloured run, cut at a gap of more than one space");
        Check.Equal(201, minimap.LastWindow.Count, "the window: the rows 400 units show (a row is 2 units at scale 2), and the one cut at the bottom");
        Check.Equal(201L, minimap.LinesBuilt, "each line of the window built once");
        Check.Equal(1L, minimap.ClassifyCalls, "the window classified in one call");

        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        Check.Equal(201L, minimap.LinesBuilt, "a frame that changed nothing builds nothing (the cache)");
        Check.Equal(1L, minimap.ClassifyCalls, "and classifies nothing");
        Check.True(minimap.RectsDrawn >= 4 * 201, $"every line of the window is drawn ({minimap.RectsDrawn} rects)");

        model.Select(model.Snapshot.GetLine(5).End, model.Snapshot.GetLine(5).End);
        model.OnText(" x");
        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        Check.Equal(201L + 1 + 64, minimap.LinesBuilt, "an edit builds the line it touched at once, and checks again 64 kept lines' colours (CRAFT_MINIMAP_LINES_PER_FRAME)");
        Check.True(minimap.Pending && minimap.NeedsRedraw(model, width, 400), "the rest wait for the next frames");
        int frames = Settle(minimap, canvas, model, width, 400);
        Check.True(frames == 3 && minimap.LinesBuilt == 402, $"three more frames check the rest: the whole window once ({frames} frames, {minimap.LinesBuilt} built)");
        SameAsFresh(minimap, model, width, 400, "after a typed text");
        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        var edited = Rects(canvas, 10, 2);
        Check.Equal((22f, 17f), (edited[^1].X, edited[^1].W), "the typed \" x\" joins the comment's run across its single space");

        // Enter: the lines after it move down one; only the two lines it made are built at once.
        model.Select(model.Snapshot.GetLine(10).End, model.Snapshot.GetLine(10).End);
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        long before = minimap.LinesBuilt;
        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        Check.Equal(2L + 64, minimap.LinesBuilt - before, "a line break builds its two lines; the kept lines below moved down one");
        Settle(minimap, canvas, model, width, 400);
        SameAsFresh(minimap, model, width, 400, "after a line break");

        // A quote typed at line 3 colours the lines below differently: the kept lines are checked again, so they follow.
        model.Select(model.Snapshot.GetLine(3).Start, model.Snapshot.GetLine(3).Start);
        model.OnText("\"");
        Settle(minimap, canvas, model, width, 400);
        SameAsFresh(minimap, model, width, 400, "after a quote");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Settle(minimap, canvas, model, width, 400);
        SameAsFresh(minimap, model, width, 400, "after two undos");

        before = minimap.LinesBuilt;
        model.Theme = EditorThemes.Get(EditorThemes.Light);
        Settle(minimap, canvas, model, width, 400);
        Check.Equal(201L, minimap.LinesBuilt - before, "another theme colours the window again, once");
        SameAsFresh(minimap, model, width, 400, "after a theme");
        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        Check.Equal(Soft(model.Theme.StyleOf(TokenClasses.String).Argb), Rects(canvas, 0, 2)[1].Argb, "in the new theme's colours");

        // The syntactic layer only: a session whose Classify gives semantic colours is never asked for them.
        var semantic = new SyntaxOrMeaningLanguage();
        var (other, _) = Make(50, language: semantic);
        using var _o = other;
        using var second = new MinimapRenderer();
        second.Draw(new RecordingCanvas(), other, width, 400);
        Check.True(semantic.Syntactic > 0 && semantic.Semantic == 0, $"the minimap classifies with the syntactic layer only ({semantic.Syntactic} syntactic, {semantic.Semantic} semantic calls)");
    }

    [Test]
    public static void TheSliderCoversTheViewportAndScrollsWithTheText()
    {
        var (model, _) = Make(1000);
        using var _m = model;
        float width = MinimapRenderer.WidthOf(model.Options);
        using var minimap = new MinimapRenderer();
        var canvas = new RecordingCanvas();
        var colors = model.Theme.Colors;
        float most = model.ContentHeight - 400;

        minimap.Draw(canvas, model, width, 400);
        var slider = canvas.Calls.Single(c => c.Kind == "fill" && c.Argb == colors.MinimapSlider);
        Check.True(slider.Y == 0 && Math.Abs(slider.H - 40) < 0.01 && Math.Abs(slider.W - 124) < 0.01, $"at the top: the slider covers the 20 lines shown, 2 units a line ({slider.Y}, {slider.H}, {slider.W})");

        model.UpdateViewport(0, most, 800, 400);
        canvas.Clear();
        minimap.Draw(canvas, model, width, 400);
        slider = canvas.Calls.Single(c => c.Kind == "fill" && c.Argb == colors.MinimapSlider);
        Check.True(Math.Abs(slider.Y - 360) < 0.01, $"at the end: the slider at the bottom ({slider.Y})");
        Check.True(minimap.LastWindow.Last >= 999, $"the window scrolled to the file's end ({minimap.LastWindow.First}-{minimap.LastWindow.Last})");

        var (small, _) = Make(100);
        using var _s = small;
        small.UpdateViewport(0, 300, 800, 400);
        canvas.Clear();
        minimap.Draw(canvas, small, width, 400);
        slider = canvas.Calls.Single(c => c.Kind == "fill" && c.Argb == colors.MinimapSlider);
        Check.True(Math.Abs(slider.Y - 30) < 0.01 && minimap.LastWindow.First == 0, $"a file that fits does not scroll its rows; the slider moves a tenth of the text ({slider.Y})");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.Gutter && Math.Abs(c.X - 124) < 0.01), "the overview ruler sits at the minimap's right edge");
    }

    [Test]
    public static void AClickCentresTheLineThereAndADragMovesTheSlider()
    {
        var (model, _) = Make(1000);
        using var _m = model;
        float width = MinimapRenderer.WidthOf(model.Options);
        var requests = new List<float>();
        model.ScrollRequested += (_, e) => requests.Add(e.Y);
        using var minimap = new MinimapRenderer();
        minimap.Draw(new RecordingCanvas(), model, width, 400);

        // Below the slider: row 100 is under the pointer; the view centres on it.
        minimap.PointerPressed(model, 30, 200, width, 400);
        Check.Equal(1, requests.Count, "a press away from the slider scrolls at once");
        Check.True(Math.Abs(requests[0] - ((100 * 20) + 10 - 200)) < 0.01, $"to row 100, centred ({requests[0]})");
        Check.True(minimap.IsDragging, "and a drag goes on from there");
        model.UpdateViewport(0, requests[0], 800, 400);
        var g = MinimapRenderer.Geometry(model, width, 400);
        minimap.PointerMoved(model, 30, 250, width, 400);
        Check.True(Math.Abs(requests[1] - (requests[0] + (50 * g.ScrollPerSlider))) < 0.5, $"a drag of 50 units moves the slider 50 units ({requests[1]} from {requests[0]}, {g.ScrollPerSlider:F2} a unit)");
        minimap.PointerReleased();
        Check.True(!minimap.IsDragging, "released");

        // On the slider: a press scrolls nothing; the drag carries the text.
        model.UpdateViewport(0, 0, 800, 400);
        requests.Clear();
        minimap.PointerPressed(model, 30, 20, width, 400);
        Check.Equal(0, requests.Count, "a press on the slider does not jump");
        minimap.PointerMoved(model, 30, 56, width, 400);
        g = MinimapRenderer.Geometry(model, width, 400);
        Check.True(requests.Count == 1 && Math.Abs(requests[0] - (36 * g.ScrollPerSlider)) < 0.5, $"the slider follows the pointer ({string.Join(", ", requests)})");
        minimap.PointerReleased();

        // The ruler at its right edge keeps its own press.
        requests.Clear();
        minimap.PointerPressed(model, width - 5, 200, width, 400);
        float scale = 400 / model.ContentHeight;
        Check.True(requests.Count == 1 && Math.Abs(requests[0] - Math.Clamp((200 / scale) - 200, 0, model.ContentHeight - 400)) < 1, $"a press on the ruler scrolls as the ruler does ({string.Join(", ", requests)})");
        minimap.PointerReleased();
        Check.Equal(0, model.Selection.Caret, "the caret stays");

        var narrow = new CraftOptions { Minimap = true };
        Check.True(MinimapRenderer.Shows(narrow, 1000) && !MinimapRenderer.Shows(narrow, 500) && !MinimapRenderer.Shows(new CraftOptions(), 1000),
            "it shows on a wide editor with CRAFT_MINIMAP on; not on a narrow one (CRAFT_MINIMAP_MIN_WIDTH), not with it off (the default)");
    }

    [Test]
    public static void ABlinkRedrawsNothingAScrollAnEditOrATheme()
    {
        var (model, _) = Make(1000, On with { OverviewRuler = false });
        using var _m = model;
        float width = MinimapRenderer.WidthOf(model.Options);
        using var minimap = new MinimapRenderer();
        using var text = new EditorRenderer();
        var canvas = new CountingCanvas();
        minimap.Draw(canvas, model, width, 400);
        Check.True(!minimap.NeedsRedraw(model, width, 400), "drawn: nothing to redraw");
        for (int i = 0; i < 4; i++)
        {
            text.DrawCarets(canvas, model, 0, 0, 800, 400);       // the blink: the caret layer only
            text.Draw(canvas, model, 0, 0, 800, 400, caretVisible: (i & 1) == 0);
        }

        Check.True(!minimap.NeedsRedraw(model, width, 400), "the caret's blink (and the text's own frames) redraw no minimap");
        model.OnKey(EditorKey.Right, KeyModifiers.None);
        Check.True(!minimap.NeedsRedraw(model, width, 400), "nor does a caret moving on screen (the ruler off)");

        model.UpdateViewport(0, 60, 800, 400);
        Check.True(minimap.NeedsRedraw(model, width, 400), "a scroll does");
        Settle(minimap, canvas, model, width, 400);
        model.OnText("x");
        Check.True(minimap.NeedsRedraw(model, width, 400), "an edit does");
        Settle(minimap, canvas, model, width, 400);
        model.ApplyReclassification();
        Check.True(minimap.NeedsRedraw(model, width, 400), "a landed parse does (new colours)");
        Settle(minimap, canvas, model, width, 400);
        model.Theme = EditorThemes.Get(EditorThemes.Light);
        Check.True(minimap.NeedsRedraw(model, width, 400), "a theme does");
        Settle(minimap, canvas, model, width, 400);
        Check.True(!minimap.NeedsRedraw(model, width, 400) && minimap.NeedsRedraw(model, width, 300), "a new size does");

        // With the ruler at its edge, a caret that moved changes the ruler's caret mark: that redraws it (not the blink).
        var (ruled, _) = Make(1000);
        using var _r = ruled;
        minimap.Draw(canvas, ruled, width, 400);
        text.DrawCarets(canvas, ruled, 0, 0, 800, 400);
        Check.True(!minimap.NeedsRedraw(ruled, width, 400), "the blink still redraws nothing");
        ruled.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.True(minimap.NeedsRedraw(ruled, width, 400), "a caret that moved redraws the ruler's mark");
    }

    [Test]
    public static void AWarmMinimapFrameAllocatesNothing()
    {
        var (model, _) = Make(3000);
        using var _m = model;
        var snapshot = model.Snapshot;
        model.Find.Query = "hello 1";
        model.Find.IsHighlighting = true;
        _ = model.Find.CountMatches();
        float width = MinimapRenderer.WidthOf(model.Options);
        using var minimap = new MinimapRenderer();
        var canvas = new CountingCanvas();
        for (int i = 0; i < 100; i++)
        {
            model.UpdateViewport(0, (i & 1) * 20, 800, 400);
            minimap.Draw(canvas, model, width, 400);
        }

        long built = minimap.LinesBuilt;
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 200;
        for (int i = 0; i < frames; i++)
        {
            model.UpdateViewport(0, (i & 1) * 20, 800, 400);
            minimap.Draw(canvas, model, width, 400);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.Equal(built, minimap.LinesBuilt, "warm frames (a line's scroll back and forth) build nothing");
        Check.True(minimap.MarksDrawn > 0, $"the find hits are marked ({minimap.MarksDrawn})");
        Check.Equal(0L, bytes / frames, $"{frames} warm frames allocate nothing ({bytes:N0} bytes in all; rule 63)");

        // Scrolling through the whole file keeps the cache bounded and reuses its lines.
        for (int y = 0; y < model.ContentHeight; y += 400)
        {
            model.UpdateViewport(0, y, 800, 400);
            minimap.Draw(canvas, model, width, 400);
            Check.True(minimap.LinesHeld <= (3 * 2 * minimap.LastWindow.Count) + 16 + minimap.LastWindow.Count, $"the cache stays near its window ({minimap.LinesHeld} lines for a window of {minimap.LastWindow.Count})");
        }

        _ = snapshot;
    }

    [Test]
    public static void EachViewHasItsOwnMinimap()
    {
        var (first, document) = Make(5000);
        using var _f = first;
        using var second = new EditorModel(document, new QuoteLanguage(), On with { NewLine = "\n", FoldingDelayMilliseconds = 60_000 }, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        second.UpdateViewport(0, 60_000, 800, 400);
        float width = MinimapRenderer.WidthOf(first.Options);
        using var one = new MinimapRenderer();
        using var two = new MinimapRenderer();
        var canvas = new CountingCanvas();
        one.Draw(canvas, first, width, 400);
        two.Draw(canvas, second, width, 400);
        Check.True(one.LastWindow.First == 0 && two.LastWindow.First > 2000, $"each view's minimap shows its own window ({one.LastWindow.First}, {two.LastWindow.First})");
        long built = one.LinesBuilt;
        two.Draw(canvas, second, width, 400);
        one.Draw(canvas, first, width, 400);
        Check.Equal(built, one.LinesBuilt, "a view's minimap keeps its own cache (the other view's frames do not touch it)");
        Check.True(first.OverviewRulerWidth == 0 && second.OverviewRulerWidth == 14, "the ruler moves to the minimap only in the view that shows it");

        first.OnText("typed ");
        Check.True(one.NeedsRedraw(first, width, 400) && two.NeedsRedraw(second, width, 400), "an edit in one view redraws both minimaps (one text)");
    }

    [Test]
    public static void APaintOfA100000LineFileWithTheMinimapOnAndOffIsMeasured()
    {
        // A C# file of 100,000 lines (the paint tests' fixed sample, repeated), coloured by the C# plug-in's language.
        string sample = File.ReadAllText(TestRunner.RepositoryPath("tests", "Craft.Tests", "Editor", "Samples", "EditorModel.cs.txt")).ReplaceLineEndings("\n");
        int sampleLines = sample.Count(c => c == '\n');
        var text = new StringBuilder();
        int lines = 0;
        while (lines < 100_000)
        {
            text.Append(sample);
            lines += sampleLines;
        }

        var options = new CraftOptions { NewLine = "\n", ParseWaitMilliseconds = 30_000, FoldingDelayMilliseconds = 600_000, Minimap = true, SemanticColorsDelayMilliseconds = -1 };
        using var document = TextDocument.Create(text.ToString(), options);
        using var model = new EditorModel(document, new CSharpLanguage(), options, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        float y = 50_000 * model.Metrics.LineHeight;
        model.UpdateViewport(0, y, 1200, 800);
        float width = MinimapRenderer.WidthOf(options);
        using var off = new EditorRenderer();
        using var on = new EditorRenderer();
        using var minimap = new MinimapRenderer();
        var canvas = new CountingCanvas();

        long t = Stopwatch.GetTimestamp();
        off.Draw(canvas, model, 0, y, 1200, 800, caretVisible: true);
        double offCold = Stopwatch.GetElapsedTime(t).TotalMilliseconds;           // the first parse waited for
        var offWarm = Frames(() => off.Draw(canvas, model, 0, y, 1200, 800, caretVisible: true), out long offBytes);

        model.MinimapShown = true;
        t = Stopwatch.GetTimestamp();
        on.Draw(canvas, model, 0, y, 1200 - width, 800, caretVisible: true);
        double textCold = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        t = Stopwatch.GetTimestamp();
        minimap.Draw(canvas, model, width, 800);
        double minimapCold = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        int window = minimap.LastWindow.Count;
        var onWarm = Frames(() =>
        {
            on.Draw(canvas, model, 0, y, 1200 - width, 800, caretVisible: true);
            minimap.Draw(canvas, model, width, 800);
        }, out long onBytes);
        var minimapWarm = Frames(() => minimap.Draw(canvas, model, width, 800), out long minimapBytes);

        // A scroll of three lines (the window moves a little: the lines it brings in are built), then an edit (a new version).
        float line = model.Metrics.LineHeight;
        var scroll = new double[50];
        for (int i = 0; i < scroll.Length; i++)
        {
            model.UpdateViewport(0, y + ((i + 1) * 3 * line), 1200 - width, 800);
            t = Stopwatch.GetTimestamp();
            minimap.Draw(canvas, model, width, 800);
            scroll[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
        }

        Array.Sort(scroll);
        model.Select(document.Buffer.Current.GetLine(50_010).End, document.Buffer.Current.GetLine(50_010).End);
        var edit = new double[20];
        var settle = new double[edit.Length];
        var settleFrames = new int[edit.Length];
        for (int i = 0; i < edit.Length; i++)
        {
            model.OnText(" ");
            t = Stopwatch.GetTimestamp();
            minimap.Draw(canvas, model, width, 800);
            edit[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
            while (minimap.Pending)
            {
                t = Stopwatch.GetTimestamp();
                minimap.Draw(canvas, model, width, 800);
                settle[i] = Math.Max(settle[i], Stopwatch.GetElapsedTime(t).TotalMicroseconds);
                settleFrames[i]++;
            }
        }

        Array.Sort(edit);
        Array.Sort(settle);
        Array.Sort(settleFrames);
        var whole = new double[10];
        for (int i = 0; i < whole.Length; i++)
        {
            using var fresh = new MinimapRenderer();
            t = Stopwatch.GetTimestamp();
            fresh.Draw(canvas, model, width, 800);
            whole[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
        }

        Array.Sort(whole);
        string result = string.Create(CultureInfo.InvariantCulture,
            $"Paint of a 100,000-line C# file at line 50,000 (1200 x 800, fixed metrics; the minimap {width} wide, a window of {window} lines): minimap off: cold {offCold:F1} ms (the first parse waited for), warm median {offWarm[offWarm.Length / 2]:F1} us, p99 {offWarm[offWarm.Length * 99 / 100]:F1} us, {offBytes} B a frame; " +
            $"minimap on: the text's cold frame {textCold:F2} ms and the minimap's {minimapCold:F2} ms; the text and the minimap warm median {onWarm[onWarm.Length / 2]:F1} us, p99 {onWarm[onWarm.Length * 99 / 100]:F1} us, {onBytes} B a frame; " +
            $"the minimap's own warm frame median {minimapWarm[minimapWarm.Length / 2]:F1} us, {minimapBytes} B a frame; after a 3-line scroll median {scroll[scroll.Length / 2]:F1} us; after a keystroke median {edit[edit.Length / 2]:F1} us (the touched line and {options.MinimapLinesPerFrame} lines' colours checked), then {settleFrames[settleFrames.Length / 2]} frames of at most {settle[settle.Length / 2]:F1} us (median) to check the rest; the whole window built afresh median {whole[whole.Length / 2]:F1} us");
        Console.WriteLine("    measured: " + result);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "minimap.txt"), result + Environment.NewLine);
        Check.Equal(0L, minimapBytes, "the minimap's warm frame allocates nothing");
        Check.True(minimapWarm[minimapWarm.Length / 2] < 5_000, $"a warm minimap frame stays well under a frame ({minimapWarm[minimapWarm.Length / 2]:F1} us)");
    }

    // 500 frames after 50 to warm: their times in microseconds, sorted, and the bytes a frame.
    private static double[] Frames(Action frame, out long bytesPerFrame)
    {
        for (int i = 0; i < 50; i++)
        {
            frame();
        }

        const int count = 500;
        var times = new double[count];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            frame();
            times[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
        }

        bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
        Array.Sort(times);
        return times;
    }

    /// <summary>A language whose Classify gives a "semantic" span and whose ClassifySyntactic the syntactic ones; it counts both.</summary>
    private sealed class SyntaxOrMeaningLanguage : ILanguage
    {
        public int Syntactic { get; set; }

        public int Semantic { get; set; }

        public string Name => "syntax-or-meaning";

        public string Summary => "Test: two layers of colours";

        public IReadOnlyList<string> Extensions { get; } = [".som"];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer) => new Session(this);

        private sealed class Session(SyntaxOrMeaningLanguage language) : ILanguageSession
        {
            public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
            {
                language.Semantic++;
                into.Add(start, Math.Min(1, length), TokenClasses.Keyword);
            }

            public void ClassifySyntactic(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => language.Syntactic++;

            public void Dispose()
            {
            }
        }
    }
}

/// <summary>
/// The X restart, piece 2b: completion and ghost text at several carets (one request at the primary; accepted at every
/// caret whose text matches, the rest left and said; one undo step; ghost text's "+N" and Tab).
/// </summary>
[TestGroup("CaretCompletion")]
public static class CaretCompletionTests
{
    private static readonly CraftOptions Immediate = new() { CompletionDelayMilliseconds = 0, HoverDelayMilliseconds = 0, CompletionRows = 5 };

    private static void Pump(ConcurrentQueue<Action> ui)
    {
        while (ui.TryDequeue(out var action))
        {
            action();
        }
    }

    private static int[] LineEnds(EditorModel model, params int[] lines) => [.. lines.Select(l => model.Snapshot.GetLine(l).End)];

    private static void Carets(EditorModel model, int primary, params int[] positions) =>
        model.SetSelections(positions.Select(SelectionState.At).ToArray(), primary);

    [Test]
    public static void AnItemCommittedAtSeveralCaretsGoesToTheMatchingOnesAsOneUndoStep()
    {
        var language = new FakeSemanticLanguage();
        var (model, document) = RealEditors.Make("Con\nCon\nxCon\nCo\n", Immediate, language);
        using var _m = model;
        using var _d = document;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var session = language.Last!;
        Carets(model, 0, LineEnds(model, 0, 1, 2, 3));
        Check.Equal(4, model.SelectionCount, "four carets");

        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        Check.Equal(1, session.Completions.Count, "one request, at Ctrl+Space");
        Check.Equal(3, session.Completions[0].Position, "at the primary caret");
        session.Completions[0].Answer.SetResult(new CompletionList(model.Snapshot, 3, 0,
            [new CompletionItem("Console", CompletionItemKind.Class, null, new TextEdit(0, 3, "Console"), "Console", "Console")]));
        Pump(ui);
        Check.True(model.Completion.IsOpen && model.SelectionCount == 4, "the list opens and the carets stay");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.Equal("Console|\nConsole|\nxCon|\nCo|\n", RealEditors.Marked(model),
            "applied at the carets whose text before them is the primary's \"Con\" (a word start); \"xCon\" and \"Co\" are left");
        Check.Equal("Completion applied at 2 of 4 carets: the text before the others differs.", model.LastStatusMessage, "the status says how many");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("Con|\nCon|\nxCon|\nCo|\n", RealEditors.Marked(model), "one undo step takes it back at every caret, the carets restored");

        // Typing at several carets asks again at the primary (no request with the option off).
        model.OnText("s");
        Check.Equal(2, session.Completions.Count, "typing an identifier character at four carets asks once");
        Check.Equal(model.Selection.Caret, session.Completions[1].Position, "at the primary caret");

        var offLanguage = new FakeSemanticLanguage();
        var (off, offDocument) = RealEditors.Make("Con\nCon\n", Immediate with { CompletionAllCarets = false }, offLanguage);
        using var _o = off;
        using var _od = offDocument;
        off.Dispatch = ui.Enqueue;
        Carets(off, 0, LineEnds(off, 0, 1));
        off.OnKey(EditorKey.Space, KeyModifiers.Control);
        off.OnText("s");
        Check.Equal(0, offLanguage.Last!.Completions.Count, "CRAFT_COMPLETION_ALL_CARETS off: no list while there are several carets");
    }

    [Test]
    public static void AnItemReplacingTextAfterTheCaretGoesWhereBothSidesMatch()
    {
        var language = new FakeSemanticLanguage();
        var (model, document) = RealEditors.Make("WriLine();\nWriLine();\nWriText();\n", Immediate, language);
        using var _m = model;
        using var _d = document;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var session = language.Last!;
        var snapshot = model.Snapshot;
        Carets(model, 1, snapshot.GetLine(0).Start + 3, snapshot.GetLine(1).Start + 3, snapshot.GetLine(2).Start + 3);
        model.OnKey(EditorKey.Space, KeyModifiers.Control);
        int primary = snapshot.GetLine(1).Start + 3;
        Check.Equal(primary, session.Completions[0].Position, "asked at the primary (the second caret)");
        // The item replaces the whole word "WriLine" (3 characters before the caret, 4 after) with "WriteLine".
        session.Completions[0].Answer.SetResult(new CompletionList(model.Snapshot, primary, primary - 3,
            [new CompletionItem("WriteLine", CompletionItemKind.Method, null, new TextEdit(primary - 3, 7, "WriteLine"), "WriteLine", "WriteLine")]));
        Pump(ui);
        model.OnKey(EditorKey.Tab, KeyModifiers.None);
        Check.Equal("WriteLine|();\nWriteLine|();\nWri|Text();\n", RealEditors.Marked(model), "both sides of the caret match on the first two lines; \"WriText\" is left");
        Check.True(model.LastStatusMessage?.Contains("2 of 3", StringComparison.Ordinal) == true, $"said ({model.LastStatusMessage})");
    }

    [Test]
    public static void GhostTextAtSeveralCaretsShowsPlusNAndTabInsertsWhereTheLineMatches()
    {
        var provider = new FixedInline("Line(x);\n    Done();");
        InlineCompletionProviders.Register(provider);
        try
        {
            var options = new CraftOptions { InlineCompletionProvider = provider.Name };
            var (model, document) = RealEditors.Make("    Console.Write\n    Console.Write\n  Console.Write\nConsole.Wri\n", options);
            using var _m = model;
            using var _d = document;
            var ui = new ConcurrentQueue<Action>();
            model.Dispatch = ui.Enqueue;
            Carets(model, 0, LineEnds(model, 0, 1, 2, 3));
            Check.True(model.InlineCompletion.Invoke(), "asked at the primary");
            var watch = Stopwatch.StartNew();
            while ((model.InlineCompletion.IsRequesting || !model.InlineCompletion.IsShown) && watch.ElapsedMilliseconds < 5000)
            {
                Pump(ui);
                Thread.Sleep(2);
            }

            Check.True(model.InlineCompletion.IsShown, "the ghost text shows with four carets");
            Check.Equal(model.Snapshot.GetLine(0).End, provider.Positions.Single(), "one request, at the primary caret");
            Check.Equal(2, model.InlineCompletion.OtherCarets, "two other carets' lines read \"Console.Write\" before the caret (white space aside); \"Console.Wri\" does not");
            using var renderer = new EditorRenderer();
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            Check.Equal("+2", renderer.GhostPlusDrawn, "drawn at the primary with a \"+2\" mark");
            Check.True(canvas.Texts.Any(t => t.Text == "+2" && t.Top == 0), "on the primary's line");

            model.OnKey(EditorKey.Tab, KeyModifiers.None);
            Check.Equal("    Console.WriteLine(x);\n    Done();|\n    Console.WriteLine(x);\n    Done();|\n  Console.WriteLine(x);\n  Done();|\nConsole.Wri|\n", RealEditors.Marked(model),
                "Tab inserts it at the matching carets, its later line in each caret's indent; the last caret is left");
            Check.Equal("Ghost text applied at 3 of 4 carets: the text before the others differs.", model.LastStatusMessage, "the status says how many");
            model.OnKey(EditorKey.Z, KeyModifiers.Control);
            Check.Equal("    Console.Write|\n    Console.Write|\n  Console.Write|\nConsole.Wri|\n", RealEditors.Marked(model), "one undo step");

            // With the option off, several carets show no ghost text (as before).
            var (off, offDocument) = RealEditors.Make("a\na\n", options with { CompletionAllCarets = false });
            using var _o = off;
            using var _od = offDocument;
            Carets(off, 0, LineEnds(off, 0, 1));
            Check.True(!off.InlineCompletion.Invoke(), "CRAFT_COMPLETION_ALL_CARETS off: no request at several carets");
        }
        finally
        {
            InlineCompletionProviders.Unregister(provider.Name);
        }
    }

    /// <summary>An inline completion provider that answers its text at once, in two pieces, and records where it was asked.</summary>
    private sealed class FixedInline(string text) : IInlineCompletionProvider
    {
        public List<int> Positions { get; } = [];

        public string Name => "caret-completion-test";

        public string Summary => "Test: a fixed completion";

        public int DelayMilliseconds => 0;

        public bool Accepts(InlineCompletionRequest request) => true;

        public async IAsyncEnumerable<InlineCompletionPiece> CompleteAsync(InlineCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lock (Positions)
            {
                Positions.Add(request.Position);
            }

            await Task.Yield();
            yield return new InlineCompletionPiece(text[..4]);
            yield return new InlineCompletionPiece(text[4..]);
        }
    }
}
