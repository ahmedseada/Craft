// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// E3: keystroke to render on the generated file: a character typed into the middle of it, then the frame that shows it
/// drawn on a canvas that only counts (fixed metrics, so the editor's own cost is measured, not a platform's). The first
/// keystroke of the process (cold) apart from the steady ones (warm); a caret-blink frame and a one-line scroll frame;
/// the bytes allocated per keystroke and per frame. A registered language classifies (none is in this process unless E2's
/// is referenced; the file is then drawn as plain text, said so in the results).
/// </summary>
internal static class EditorBench
{
    private const int Keystrokes = 2000;
    private const int Frames = 2000;
    private const float Width = 1600;
    private const float Height = 900;

    public static void Run(int lines)
    {
        var results = new StringBuilder();
        results.Append(CultureInfo.InvariantCulture, $"Craft editor bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores\n");
        var options = new CraftOptions();
        using var document = TextDocument.Create(GenerateCSharp(lines), options);
        var language = EditorLanguages.ForFile("generated.cs");
        using var model = new EditorModel(document, language, options, EditorThemes.Get(EditorThemes.Dark), new Measurer(), new Clipboard());
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        results.Append(CultureInfo.InvariantCulture, $"file: {lines:N0} lines, {document.Buffer.Current.Length:N0} characters; language: {model.LanguageName}; viewport {Width}x{Height}, cells 8.4x18.9\n");

        // The caret in the middle of the file, at the end of its line, scrolled into view.
        var snapshot = document.Buffer.Current;
        var middle = snapshot.GetLine(snapshot.LineCount / 2);
        float scrollY = (middle.Index - 10) * model.Metrics.LineHeight;
        model.UpdateViewport(0, scrollY, Width, Height);
        model.Select(middle.End, middle.End);

        // Cold: the first keystroke and its frame in this process.
        long start = Stopwatch.GetTimestamp();
        model.OnText("x");
        renderer.Draw(canvas, model, model.ScrollX, model.ScrollY, Width, Height, caretVisible: true);
        double cold = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        results.Append(CultureInfo.InvariantCulture, $"keystroke to render, cold: {cold:F0} us (model's telemetry: {model.LastInputToPaintMilliseconds:F3} ms)\n");

        // Warm: typing and deleting alternately, each with its frame.
        var times = new double[Keystrokes];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long runs = renderer.RunStringsBuilt;
        for (int i = 0; i < Keystrokes; i++)
        {
            start = Stopwatch.GetTimestamp();
            if ((i & 1) == 0)
            {
                model.OnText("y");
            }
            else
            {
                model.OnKey(EditorKey.Backspace, KeyModifiers.None);
            }

            renderer.Draw(canvas, model, model.ScrollX, model.ScrollY, Width, Height, caretVisible: true);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }

        long perKey = (GC.GetAllocatedBytesForCurrentThread() - allocated) / Keystrokes;
        long runsPerKey = (renderer.RunStringsBuilt - runs) / Keystrokes;
        Array.Sort(times);
        results.Append(CultureInfo.InvariantCulture, $"keystroke to render, warm ({Keystrokes:N0}): median {times[Keystrokes / 2]:F1} us, p99 {times[Keystrokes * 99 / 100]:F1} us, max {times[^1]:F1} us; {perKey:N0} bytes and {runsPerKey} run strings per keystroke; {renderer.LinesDrawn} lines drawn; model max {model.MaxInputToPaintMilliseconds:F3} ms\n");

        // A caret-blink frame: nothing changed.
        results.Append(Frame("blink frame (no change)", renderer, canvas, model, scrollStep: 0));

        // Scrolling: one line further each frame.
        results.Append(Frame("scroll frame (one line each)", renderer, canvas, model, scrollStep: model.Metrics.LineHeight));
        results.Append(CultureInfo.InvariantCulture, $"canvas calls in the last frame: {canvas.LastFrameCalls}\n");

        var path = Path.Combine(OutputFolder(), "editor.txt");
        File.WriteAllText(path, results.ToString());
        Console.Write(results);
        Console.WriteLine($"written to {path}");
    }

    private static string Frame(string what, EditorRenderer renderer, CountingCanvas canvas, EditorModel model, float scrollStep)
    {
        var times = new double[Frames];
        float y = model.ScrollY;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long runs = renderer.RunStringsBuilt;
        for (int i = 0; i < Frames; i++)
        {
            y += scrollStep;
            canvas.LastFrameCalls = 0;
            long start = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, y, Width, Height, caretVisible: (i & 1) == 0);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }

        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / Frames;
        double runsPerFrame = (renderer.RunStringsBuilt - runs) / (double)Frames;
        Array.Sort(times);
        return string.Create(CultureInfo.InvariantCulture,
            $"{what}, warm ({Frames:N0}): median {times[Frames / 2]:F1} us, p99 {times[Frames * 99 / 100]:F1} us; {perFrame:N0} bytes and {runsPerFrame:F1} run strings per frame\n");
    }

    private sealed class Measurer : ITextMeasurer
    {
        public FontMetrics Measure(EditorFont font, float lineSpacing) => new(8.4f, 18.9f, 14);
    }

    private sealed class Clipboard : IEditorClipboard
    {
        public Task<string?> GetTextAsync() => Task.FromResult<string?>(null);

        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class CountingCanvas : IEditorCanvas
    {
        public long LastFrameCalls { get; set; }

        public void FillRect(float x, float y, float width, float height, uint argb) => LastFrameCalls++;

        public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness) => LastFrameCalls++;

        public void DrawText(string text, float x, float top, in TextStyle style) => LastFrameCalls++;
    }
}
