// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Core.Documents;
using Craft.CSharp;
using Craft.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>
/// The owner's third Windows run, B1 and B2: each shown line drawn from one kept layout (its runs' colours on ranges), a
/// warm frame laying nothing out and allocating nothing, the cache bounded to the screen and a margin, the carets on a
/// layer of their own, a frame that throws reported with its step and skipped, the trail's inner exceptions.
/// </summary>
[TestGroup("Paint")]
public static class PaintTests
{
    // The fixed C# file: a source file of this repository, classified by the C# plug-in's language (its parse waited for).
    private static (EditorModel Model, TextDocument Document) CSharpEditor(CraftOptions? options = null)
    {
        // A fixed copy of EditorModel.cs as B1 measured it (2026-11-01): the paint's numbers and its 0 B a frame are about the
        // renderer, so the text on screen must not move when the source file is edited (the X restart's constructor put more
        // documentation comments on screen, whose structured trivia Roslyn keeps only weakly and rebuilds after a collection).
        string text = File.ReadAllText(TestRunner.RepositoryPath("tests", "Craft.Tests", "Editor", "Samples", "EditorModel.cs.txt")).ReplaceLineEndings("\n");
        options = (options ?? new CraftOptions()) with { NewLine = "\n", ParseWaitMilliseconds = 10_000, FoldingDelayMilliseconds = 60_000 };
        var document = TextDocument.Create(text, options);
        var model = new EditorModel(document, new CSharpLanguage(), options, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        model.UpdateViewport(0, 0, 1200, 800);
        return (model, document);
    }

    // The views each comparison draws: the top, a scroll of a few lines, a page further, scrolled right, a line on, far down.
    private static readonly (float X, float Y)[] Views = [(0, 0), (0, 140), (0, 2000), (35, 2000), (0, 2020), (0, 9000)];

    [Test]
    public static void ALineDrawnWholeHoldsTheSameRunsPlacesAndColoursAsRunByRun()
    {
        var (model, _) = CSharpEditor();
        using var _ = model;
        int at = model.Snapshot.GetText(0, model.Snapshot.Length).IndexOf("public void ", StringComparison.Ordinal);
        model.Select(at, at + 300);
        using var byRuns = new EditorRenderer();
        using var whole = new EditorRenderer();
        var runs = new RecordingCanvas();
        using var lines = new LayoutCanvas();
        int compared = 0;
        foreach (var (x, y) in Views)
        {
            runs.Clear();
            lines.Clear();
            byRuns.Draw(runs, model, x, y, 1200, 800, caretVisible: true);
            whole.Draw(lines, model, x, y, 1200, 800, caretVisible: true);
            Check.True(byRuns.LastFrameClassified && whole.LastFrameClassified, "the C# parse landed (the spans are final)");
            Check.Equal(runs.Calls.Count, lines.Calls.Count, $"as many draws at ({x}, {y})");
            for (int i = 0; i < runs.Calls.Count; i++)
            {
                Check.Equal(runs.Calls[i], lines.Calls[i], $"draw {i} at ({x}, {y}): the same kind, place, colour and text");
            }

            compared += runs.Calls.Count(c => c.Kind == "text");
        }

        Check.True(compared > 500, $"the comparison covered the C# file's coloured runs ({compared} texts)");
        Check.True(lines.Surface.LinesLaid > 0 && lines.Surface.LinesLaid <= whole.LinesHeld + lines.Surface.LinesDisposed, "lines were laid out whole, one layout a line");
        Check.True(lines.Calls.Select(c => c.Argb).Distinct().Count() > 5, "the runs keep their colours (several token colours)");
    }

    [Test]
    public static void AWarmFrameLaysNothingOutAndAllocatesNothingAndAScrollLaysOutOnlyTheLinesItBringsIn()
    {
        var (model, _) = CSharpEditor();
        using var _ = model;
        using var renderer = new EditorRenderer();
        using var canvas = new LayoutCanvas { Record = false };
        long start = Stopwatch.GetTimestamp();
        renderer.Draw(canvas, model, 0, 2000, 1200, 800, caretVisible: true);
        double coldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        int shown = renderer.LinesDrawn;
        var plain = new RecordingCanvas();
        using (var reference = new EditorRenderer())
        {
            reference.Draw(plain, model, 0, 2000, 1200, 800, caretVisible: true);
        }

        int withText = plain.Texts.Where(t => t.X >= model.TextLeft).Select(t => t.Top).Distinct().Count();
        Check.Equal((long)withText, canvas.Surface.LinesLaid, $"the first frame lays out each shown line with text once ({shown} shown, {withText} with text; a blank line has nothing to lay out)");
        // Warm: the caret's blink and the same place again.
        for (int i = 0; i < 300; i++)
        {
            renderer.Draw(canvas, model, 0, 2000, 1200, 800, (i & 1) == 0);
        }

        long laid = canvas.Surface.LinesLaid + canvas.Surface.RunsLaid;
        const int frames = 1000;
        var times = new double[frames];
        var frameBytes = new long[frames];
        var frameGcs = new int[frames];
        long bytes = 0;
        for (int i = 0; i < frames; i++)
        {
            int gcs = GC.CollectionCount(0);
            long before0 = GC.GetAllocatedBytesForCurrentThread();
            long t = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, 2000, 1200, 800, (i & 1) == 0);
            times[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
            frameBytes[i] = GC.GetAllocatedBytesForCurrentThread() - before0;
            frameGcs[i] = GC.CollectionCount(0) - gcs;
            bytes += frameBytes[i];
        }

        Array.Sort(times);
        Check.Equal(laid, canvas.Surface.LinesLaid + canvas.Surface.RunsLaid, "a warm frame lays nothing out");
        // A frame right after a garbage collection may rebuild what Roslyn keeps only weakly (a documentation comment's
        // structured trivia, GetStructure's cache); every other frame allocates nothing. Shown here: a forced collection,
        // then the frames after it.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var afterGc = new long[3];
        for (int i = 0; i < afterGc.Length; i++)
        {
            long before0 = GC.GetAllocatedBytesForCurrentThread();
            renderer.Draw(canvas, model, 0, 2000, 1200, 800, caretVisible: true);
            afterGc[i] = GC.GetAllocatedBytesForCurrentThread() - before0;
        }

        Console.WriteLine($"    after a forced collection, the next frames allocated {string.Join(", ", afterGc)} B");
        long steady = Enumerable.Range(0, frames).Where(i => frameGcs[i] == 0 && (i == 0 || frameGcs[i - 1] == 0)).Sum(i => frameBytes[i]);
        var allocating = Enumerable.Range(0, frames).Where(i => frameBytes[i] != 0).Select(i => $"frame {i}: {frameBytes[i]} B, a collection in it or just before: {frameGcs[i] + (i > 0 ? frameGcs[i - 1] : 0) > 0}").ToList();
        // Rule 90, step 1: a failure names what was allocated (the allocation ticks over more frames).
        string types = steady == 0 ? "" : "; " + AllocatedTypes.Of(() => renderer.Draw(canvas, model, 0, 2000, 1200, 800, caretVisible: true));
        Check.Equal(0L, steady, $"a warm frame with no collection in or just before it allocates nothing ({bytes} bytes over {frames} frames in all; rule 63): {string.Join("; ", allocating)}{types}");
        Check.True(afterGc[1] == 0 && afterGc[2] == 0, $"the frames after the first one past a collection allocate nothing again ({string.Join(", ", afterGc)} B)");
        Check.True(afterGc[0] < 4096, $"what the first frame after a collection rebuilds is small ({afterGc[0]} B)");

        // A scroll of three lines lays out the three lines it brings in (and their numbers), nothing else.
        float line = model.Metrics.LineHeight;
        long before = canvas.Surface.LinesLaid;
        start = Stopwatch.GetTimestamp();
        renderer.Draw(canvas, model, 0, 2000 + (3 * line), 1200, 800, caretVisible: true);
        double scrollMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var after = new RecordingCanvas();
        using (var reference = new EditorRenderer())
        {
            reference.Draw(after, model, 0, 2000 + (3 * line), 1200, 800, caretVisible: true);
        }

        var wasShown = plain.Texts.Where(t => t.X >= model.TextLeft).Select(t => (int)MathF.Round((t.Top + 2000) / line)).ToHashSet();
        int broughtIn = after.Texts.Where(t => t.X >= model.TextLeft).Select(t => (int)MathF.Round((t.Top + 2000 + (3 * line)) / line)).Distinct().Count(l => !wasShown.Contains(l));
        Check.True(broughtIn is > 0 and <= 3, $"the scroll brings in up to three lines with text ({broughtIn})");
        Check.Equal((long)broughtIn, canvas.Surface.LinesLaid - before, "a scroll lays out only the lines it brings in");
        before = canvas.Surface.LinesLaid;
        renderer.Draw(canvas, model, 0, 2000, 1200, 800, caretVisible: true);
        Check.Equal(0L, canvas.Surface.LinesLaid - before, "scrolling back finds the lines kept in the margin");

        string result = string.Create(CultureInfo.InvariantCulture,
            $"Paint of a C# file ({shown} lines shown, 1200 x 800, fixed metrics): cold frame {coldMs:F2} ms (the run strings and {withText} layouts); warm frame median {times[frames / 2]:F1} us, p99 {times[frames * 99 / 100]:F1} us, {bytes / frames} B a frame; a 3-line scroll {scrollMs * 1000:F1} us ({broughtIn} layouts)");
        Console.WriteLine("    measured: " + result);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "paint.txt"), result + Environment.NewLine);
        Check.True(times[frames / 2] < 5_000, $"a warm frame stays well under a frame ({times[frames / 2]:F1} us)");
    }

    [Test]
    public static void TheKeptLayoutsStayWithinTheScreenAndItsMarginAndGoWithTheThemeAndOnRelease()
    {
        var options = new CraftOptions { PaintCacheLines = 10 };
        var (model, _) = CSharpEditor(options);
        using var _ = model;
        using var renderer = new EditorRenderer();
        using var canvas = new LayoutCanvas { Record = false };
        float line = model.Metrics.LineHeight;
        for (int top = 0; top < 600; top += 7)
        {
            renderer.Draw(canvas, model, 0, top * line, 1200, 800, caretVisible: true);
            int shown = renderer.LastVisible.Count;
            int bound = shown + (2 * 10);
            // Trimmed at a frame's start once over half as many again as the screen and its margin; the frame then adds what it shows.
            Check.True(renderer.LinesHeld <= bound + (bound / 2) + 8 + shown, $"the kept lines stay near the screen and its margin ({renderer.LinesHeld} kept, {shown} shown)");
            Check.Equal(renderer.LayoutsHeld, canvas.Surface.LinesLive, "the live layouts are the kept lines' own: each dropped line's layout was disposed");
        }

        Check.True(canvas.Surface.LinesDisposed > 0, "lines scrolled far away gave their layouts back");
        model.Theme = EditorThemes.Get(EditorThemes.Light);
        renderer.Draw(canvas, model, 0, 0, 1200, 800, caretVisible: true);
        Check.Equal(renderer.LayoutsHeld, canvas.Surface.LinesLive, "another theme drops the old layouts (only the new ones live)");
        renderer.ReleaseLayouts();
        Check.Equal(0, canvas.Surface.LinesLive, "a release (memory pressure) disposes every kept layout");
        long laid = canvas.Surface.LinesLaid;
        canvas.Layouts.Invalidate();
        renderer.Draw(canvas, model, 0, 0, 1200, 800, caretVisible: true);
        Check.Equal((long)renderer.LayoutsHeld, canvas.Surface.LinesLaid - laid, "after a release, a frame lays out the lines it shows (and only those)");
        laid = canvas.Surface.LinesLaid;
        canvas.Layouts.Invalidate();
        renderer.Draw(canvas, model, 0, 0, 1200, 800, caretVisible: true);
        Check.Equal((long)renderer.LayoutsHeld, canvas.Surface.LinesLaid - laid, "a new font (a new key) lays each shown line out again");
    }

    [Test]
    public static void LoneRunsAreKeptWhileDrawnByTheirTextAndDroppedAfterAFrameWithoutThem()
    {
        using var cache = new TextLayoutCache();
        var surface = new FakeSurface();
        var style = new TextStyle(0xFF112233);
        cache.BeginFrame();
        cache.DrawText(surface, "42", style, 0, 0);
        cache.DrawText(surface, "43", style, 0, 20);
        cache.EndFrame();
        Check.Equal(2L, surface.RunsLaid, "each text laid out once");
        cache.BeginFrame();
        cache.DrawText(surface, new string('4', 1) + "2", style, 0, 0);      // the same characters in another string
        cache.DrawText(surface, "43", new TextStyle(0xFF112233, Bold: true), 0, 20);
        cache.EndFrame();
        Check.Equal(3L, surface.RunsLaid, "the same text finds its layout; another style is another layout");
        Check.Equal(2, cache.RunsHeld, "the run not drawn in that frame was dropped");
        Check.Equal(1L, surface.RunsDisposed, "its layout was disposed");
        cache.BeginFrame();
        cache.EndFrame();
        Check.Equal(2, cache.RunsHeld, "a frame that drew no text (the caret layer's) drops nothing");
        cache.ReleaseRuns();
        Check.Equal(0, cache.RunsHeld, "released");
        Check.Equal(3L, surface.RunsDisposed, "every lone run's layout disposed");
    }

    [Test]
    public static void ALineWhoseLayoutWouldNotKeepItsColumnsIsDrawnRunByRunAtTheSamePlaces()
    {
        var (model, _, _) = Editors.Make("int a = \"x\"; // note\nplain line\n", language: new QuoteLanguage());
        using var _ = model;
        using var byRuns = new EditorRenderer();
        using var whole = new EditorRenderer();
        var runs = new RecordingCanvas();
        using var lines = new LayoutCanvas();
        lines.Surface.RefuseWholeLines = true;
        byRuns.Draw(runs, model, 0, 0, 800, 400, caretVisible: true);
        whole.Draw(lines, model, 0, 0, 800, 400, caretVisible: true);
        Check.True(runs.Calls.SequenceEqual(lines.Calls), "the same draws when every line falls back to its runs");
        Check.Equal(0L, lines.Surface.LinesLaid, "no whole line was kept");
        Check.True(lines.Layouts.LinesByRuns >= 2, "the lines were drawn run by run");
        long asked = lines.Surface.WholeLinesAsked;
        whole.Draw(lines, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(asked, lines.Surface.WholeLinesAsked, "a line drawn by runs is not laid out whole again until it changes");
    }

    [Test]
    public static void StyledLineTextPlacesEachRunAtItsColumn()
    {
        var line = new StyledLine();
        line.Add(new StyledRun("int", 4, new TextStyle(1)));
        line.Add(new StyledRun("😀x", 8, new TextStyle(2)));        // a surrogate pair: two characters, one column
        line.Add(new StyledRun(";", 10, new TextStyle(3)));
        Check.Equal("    int 😀x;", line.Text, "the runs at their columns, spaces between");
        Check.Equal(4, line.TextIndexOf(0), "run 0 at its column");
        Check.Equal(8, line.TextIndexOf(1), "run 1 after one space");
        Check.Equal(11, line.TextIndexOf(2), "run 2 after the pair's two characters");
        Check.Equal(11, line.TextColumns, "the pair counts one column");
        var layout = new FakeLayout();
        line.KeepLayout(layout, 7);
        Check.Equal(7, line.LayoutKey, "kept with its key");
        line.Add(new StyledRun("!", 11, new TextStyle(4)));
        Check.True(layout.Disposed && line.Layout is null, "a changed line lets its layout go");
    }

    [Test]
    public static void TheCaretLayerDrawsTheCaretsTheTextFrameDrewAndATextFrameCanLeaveThemOut()
    {
        var (model, _) = CSharpEditor();
        using var _ = model;
        var text = model.Snapshot.GetText(0, model.Snapshot.Length);
        int first = text.IndexOf("public ", 3000, StringComparison.Ordinal);
        model.SetSelections([SelectionState.At(first), SelectionState.At(first + 7), SelectionState.At(text.IndexOf('\n', first) + 3), SelectionState.At(text.Length - 5)], 0);
        using var renderer = new EditorRenderer();
        float line = model.Metrics.LineHeight;
        float scrollY = (model.Snapshot.GetLineIndex(first) - 5) * line;
        foreach (bool overwrite in (bool[])[false, true])
        {
            if (model.Overwrite != overwrite)
            {
                model.OnKey(EditorKey.Insert, KeyModifiers.None);
            }

            var with = new RecordingCanvas();
            var without = new RecordingCanvas();
            var alone = new RecordingCanvas();
            renderer.Draw(with, model, 12, scrollY, 1200, 800, caretVisible: true);
            renderer.Draw(without, model, 12, scrollY, 1200, 800, caretVisible: false);
            renderer.DrawCarets(alone, model, 12, scrollY, 1200, 800);
            Check.True(!EditorRenderer.CaretsOnTextLayer(model), "no popup or ghost text: the carets go on their own layer");
            Check.Equal(3, renderer.CaretsDrawnAlone, "the three carets on screen (the fourth is far below)");
            var caretFills = with.Calls.Skip(without.Calls.Count).ToList();
            Check.True(with.Calls.Take(without.Calls.Count).SequenceEqual(without.Calls), "the text frame without the carets is the frame with them, less the caret fills");
            Check.Equal(caretFills.Count, alone.Calls.Count, $"the caret layer draws as many fills (overwrite {overwrite})");
            Check.True(caretFills.SequenceEqual(alone.Calls), $"the caret layer's fills are the text frame's, same places and colours (overwrite {overwrite})");
        }
    }

    [Test]
    public static void AFrameThatThrowsIsReportedWithItsStepAndSkippedAndALostDeviceIsLetThrough()
    {
        var (model, _, _) = Editors.Make("a\nb\n");
        using var _ = model;
        using var renderer = new EditorRenderer();
        var error = Check.Throws<InvalidOperationException>(() => renderer.Draw(new ThrowingCanvas(), model, 0, 0, 800, 400, caretVisible: true), "a canvas that fails in a run");
        Check.Equal("draw the lines", renderer.Step, "the renderer names the step the frame was at");
        renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal("done", renderer.Step, "a frame that ends says so");

        CallbackGuard.Reset();
        var failures = new List<CallbackFailure>();
        EventHandler<CallbackFailure> onFailed = (_, f) => failures.Add(f);
        CallbackGuard.Failed += onFailed;
        try
        {
            long before = PaintGuard.Failures;
            bool rethrow = PaintGuard.Handle("editor: paint", "render: draw the lines", error);
            Check.True(!rethrow, "an ordinary failure is swallowed: the frame is skipped");
            Check.Equal(before + 1, PaintGuard.Failures, "counted");
            Check.True(CallbackGuard.WhenReported().Wait(TimeSpan.FromSeconds(10)), "the failure is delivered off the painting thread");
            Check.True(failures.Count == 1 && failures[0].Where.Contains("editor: paint", StringComparison.Ordinal) && failures[0].Where.Contains("render: draw the lines", StringComparison.Ordinal),
                $"reported with the surface and its step: {string.Join(" / ", failures.Select(f => f.Where))}");
            Check.True(ReferenceEquals(error, failures[0].Error), "the exception itself goes to Output (its type: rule 90)");
            var lost = new COMException("DXGI_ERROR_DEVICE_REMOVED", unchecked((int)0x887A0005));
            Check.True(PaintGuard.Handle("editor: paint", "render", lost), "a lost device is let through to Win2D's control, which makes it again");
            Check.True(PaintGuard.IsDeviceLost(new InvalidOperationException("wrapped", new COMException("reset", unchecked((int)0x8899000C)))), "also inside another exception");
            Check.True(!PaintGuard.IsDeviceLost(new COMException("other", unchecked((int)0x80004005))), "another COM failure is not a lost device");
            Check.True(CallbackGuard.WhenReported().Wait(TimeSpan.FromSeconds(10)), "nothing more pending");
            Check.Equal(1, failures.Count, "a lost device is noted in the trail, not reported as a failure");
        }
        finally
        {
            CallbackGuard.Failed -= onFailed;
            CallbackGuard.Reset();
        }
    }

    [Test]
    public static async Task TheTrailWritesInnerExceptions()
    {
        var wrapped = new TargetInvocationException(new TypeInitializationException("Sample.Registration", new MissingMethodException("KeyBinding..ctor(KeyChord, string, bool)")));
        string line = CrashTrail.AppendWithInner(new StringBuilder(), wrapped).ToString();
        Check.True(line.StartsWith("System.Reflection.TargetInvocationException: ", StringComparison.Ordinal), line);
        Check.True(line.Contains(" | inner: System.TypeInitializationException: ", StringComparison.Ordinal), $"the first inner exception: {line}");
        Check.True(line.Contains(" | inner: System.MissingMethodException: KeyBinding..ctor(KeyChord, string, bool)", StringComparison.Ordinal), $"and its own inner one: {line}");
        string aggregate = CrashTrail.AppendWithInner(new StringBuilder(), new AggregateException(new IOException("disk"))).ToString();
        Check.True(aggregate.Contains(" | inner: System.IO.IOException: disk", StringComparison.Ordinal), $"an aggregate's first inner exception: {aggregate}");

        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "trail-paint.txt");
        using var trail = new CrashTrail(path, 64, 20);
        try
        {
            throw wrapped;
        }
#pragma warning disable CA1031 // The test catches what it threw, to record it as the first-chance handler would.
        catch (Exception thrown)
#pragma warning restore CA1031
        {
            trail.Exception(thrown);
        }

        await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        string text = string.Join("\n", CrashTrail.Read(path));
        Check.True(text.Contains("inner: System.MissingMethodException", StringComparison.Ordinal), $"the trail's exception line names the inner cause:\n{text}");
    }
}

/// <summary>A canvas that lays lines out through a real <see cref="TextLayoutCache"/> on a <see cref="FakeSurface"/>, recording what each layout draws as a <see cref="RecordingCanvas"/> would.</summary>
internal sealed class LayoutCanvas : IEditorCanvas, IStyledLineCanvas, IDisposable
{
    private float _clipLeft;
    private float _clipRight;
    private float _cell;

    public LayoutCanvas()
    {
        Surface = new FakeSurface { Owner = this };
        Layouts.BeginFrame();
    }

    public FakeSurface Surface { get; }

    public TextLayoutCache Layouts { get; } = new();

    public bool Record { get; set; } = true;

    public List<(string Kind, float X, float Y, float W, float H, uint Argb, string? Text)> Calls { get; } = [];

    public void Clear() => Calls.Clear();

    public void FillRect(float x, float y, float width, float height, uint argb)
    {
        if (Record)
        {
            Calls.Add(("fill", x, y, width, height, argb, null));
        }
    }

    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness)
    {
        if (Record)
        {
            Calls.Add(("line", x1, y1, x2 - x1, y2 - y1, argb, null));
        }
    }

    public void DrawText(string text, float x, float top, in TextStyle style)
    {
        _cell = 0;
        Layouts.DrawText(Surface, text, style, x, top);
    }

    public void DrawStyledLine(StyledLine line, float x, float top, float cell, float clipLeft, float clipRight)
    {
        (_cell, _clipLeft, _clipRight) = (cell, clipLeft, clipRight);
        Layouts.DrawLine(Surface, line, x, top, cell, clipLeft, clipRight);
        _cell = 0;
    }

    // A layout drawn: its runs as the run-by-run path would draw them (a whole line clipped as that path clips).
    public void Emit(FakeLayout layout, float x, float top)
    {
        if (!Record)
        {
            return;
        }

        foreach (var (text, column, style) in layout.Runs)
        {
            float at = x + (column * _cell);
            if (layout.Whole && (at > _clipRight || at + (text.Length * _cell) < _clipLeft))
            {
                continue;
            }

            Calls.Add(("text", at, top, 0, 0, style.Argb, text));
        }
    }

    public void Dispose() => Layouts.Dispose();
}

/// <summary>A fake platform surface: counts what it lays out and disposes; a layout remembers its runs.</summary>
internal sealed class FakeSurface : ITextLayoutSurface
{
    public LayoutCanvas? Owner { get; set; }

    public bool RefuseWholeLines { get; set; }

    public long LinesLaid { get; private set; }

    public long RunsLaid { get; private set; }

    public long WholeLinesAsked { get; private set; }

    public long LinesDisposed { get; private set; }

    public long RunsDisposed { get; private set; }

    public int LinesLive { get; private set; }

    public IDisposable? Lay(StyledLine line, bool wholeLine)
    {
        if (wholeLine)
        {
            WholeLinesAsked++;
            if (RefuseWholeLines)
            {
                return null;
            }

            LinesLaid++;
            LinesLive++;
        }
        else
        {
            RunsLaid++;
        }

        var runs = new List<(string, int, TextStyle)>();
        foreach (var run in line.Runs)
        {
            runs.Add((run.Text, run.Column, run.Style));
        }

        return new FakeLayout { Runs = runs, Whole = wholeLine, Surface = this };
    }

    public void Draw(IDisposable layout, float x, float top) => Owner?.Emit((FakeLayout)layout, x, top);

    public void Disposed(FakeLayout layout)
    {
        if (layout.Whole)
        {
            LinesDisposed++;
            LinesLive--;
        }
        else
        {
            RunsDisposed++;
        }
    }
}

internal sealed class FakeLayout : IDisposable
{
    public List<(string Text, int Column, TextStyle Style)> Runs { get; init; } = [];

    public bool Whole { get; init; }

    public FakeSurface? Surface { get; init; }

    public bool Disposed { get; private set; }

    public void Dispose()
    {
        if (!Disposed)
        {
            Disposed = true;
            Surface?.Disposed(this);
        }
    }
}

/// <summary>A canvas whose text drawing fails (a frame that throws in its lines).</summary>
internal sealed class ThrowingCanvas : IEditorCanvas
{
    public void FillRect(float x, float y, float width, float height, uint argb)
    {
    }

    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness)
    {
    }

    public void DrawText(string text, float x, float top, in TextStyle style) => throw new InvalidOperationException("the canvas failed to draw a run");
}
