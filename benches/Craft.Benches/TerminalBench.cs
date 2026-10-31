// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Editor.Terminal;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// T: the terminal's parser and renderer. A million lines of build-like output (plain text, SGR colours, a progress line
/// rewritten with CR, a CJK name now and then) fed in 64 KB reads into a 120x30 screen with the default scrollback: bytes
/// per second and lines per second, the first read (cold) apart; the managed heap after 100,000 lines and after the
/// million (flat with the scrollback cap); the bytes allocated per line. Then the frame of the visible rows on a canvas
/// that only counts: a still frame, a frame after one new line (one row's runs made), a full repaint (a theme change).
/// CRAFT_BENCH_TERMINAL_LINES sets the lines (1,000,000 by default), CRAFT_TERMINAL_SCROLLBACK the cap (5,000).
/// </summary>
internal static class TerminalBench
{
    private const int Columns = 120;
    private const int Rows = 30;
    private const int ReadBytes = 64 * 1024;
    private const int Frames = 2000;

    public static void Run()
    {
        int lines = int.TryParse(Environment.GetEnvironmentVariable("CRAFT_BENCH_TERMINAL_LINES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1_000_000;
        int scrollback = int.TryParse(Environment.GetEnvironmentVariable("CRAFT_TERMINAL_SCROLLBACK"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : 5000;
        var results = new StringBuilder();
        results.Append(CultureInfo.InvariantCulture, $"Craft terminal bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores\n");
        var output = Generate(lines);
        results.Append(CultureInfo.InvariantCulture, $"output: {lines:N0} lines, {output.Length / 1048576.0:F1} MB; screen {Columns}x{Rows}, scrollback {scrollback:N0}; reads of {ReadBytes / 1024} KB\n");

        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        var screen = new TerminalScreen(Columns, Rows, scrollback);
        long start = Stopwatch.GetTimestamp();
        screen.Feed(output.AsSpan(0, Math.Min(ReadBytes, output.Length)));
        double cold = Microseconds(start);
        results.Append(CultureInfo.InvariantCulture, $"first read (cold): {cold:F0} us for {Math.Min(ReadBytes, output.Length):N0} bytes\n");

        int tenth = Offset(output, Math.Min(100_000, lines));
        long heapAtTenth = 0;
        long allocatedAtTenth = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        bool measuredTenth = false;
        for (int at = ReadBytes; at < output.Length; at += ReadBytes)
        {
            if (!measuredTenth && at >= tenth)
            {
                double paused = Microseconds(start);
                heapAtTenth = GC.GetTotalMemory(forceFullCollection: true);
                allocatedAtTenth = GC.GetAllocatedBytesForCurrentThread();
                measuredTenth = true;
                start = Stopwatch.GetTimestamp() - (long)(paused * Stopwatch.Frequency / 1_000_000);
            }

            screen.Feed(output.AsSpan(at, Math.Min(ReadBytes, output.Length - at)));
        }

        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        long allocatedAfterTenth = GC.GetAllocatedBytesForCurrentThread() - allocatedAtTenth;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        long heapAtEnd = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(output);
        results.Append(CultureInfo.InvariantCulture,
            $"parse (warm): {output.Length / seconds / 1048576.0:F1} MB/s, {lines / seconds / 1e6:F2} M lines/s ({seconds * 1000:F0} ms); {allocated:N0} bytes allocated in all (the scrollback's rows, made once as it fills), {allocatedAfterTenth:N0} after the first {Math.Min(100_000, lines):N0} lines\n");
        results.Append(CultureInfo.InvariantCulture,
            $"the screen's managed memory: {(heapAtTenth - baseline) / 1048576.0:F2} MB after {Math.Min(100_000, lines):N0} lines, {(heapAtEnd - baseline) / 1048576.0:F2} MB after {lines:N0} (scrollback holds {screen.ScrollbackLines:N0} lines)\n");

        var palette = new TerminalPalette(EditorThemes.Get(EditorThemes.Dark));
        var renderer = new TerminalRenderer();
        var canvas = new CountingCanvas();
        var metrics = new FontMetrics(8.4f, 17, 13);
        float width = Columns * metrics.CellWidth, height = Rows * metrics.LineHeight;
        renderer.Draw(canvas, screen, palette, metrics, width, height, 0, null, true, true);
        results.Append(Frame("still frame", renderer, canvas, screen, () => palette, metrics, width, height, null));
        var line = Encoding.UTF8.GetBytes("  \e[32m✓\e[0m Craft.Editor -> bin/Release/net10.0/Craft.Editor.dll \e[2m(0.4 s)\e[0m\r\n");
        results.Append(Frame("frame after one new line", renderer, canvas, screen, () => palette, metrics, width, height, () => screen.Feed(line)));
        var dark = EditorThemes.Get(EditorThemes.Dark);
        results.Append(Frame("full repaint (theme change)", renderer, canvas, screen, () => palette = new TerminalPalette(dark), metrics, width, height, null));
        results.Append(CultureInfo.InvariantCulture, $"rows drawn per frame: {renderer.RowsDrawn}; canvas calls in the last frame: {canvas.LastFrameCalls}\n");

        var path = Path.Combine(OutputFolder(), "terminal.txt");
        File.WriteAllText(path, results.ToString());
        Console.Write(results);
        Console.WriteLine($"written to {path}");
    }

    private static string Frame(string what, TerminalRenderer renderer, CountingCanvas canvas, TerminalScreen screen, Func<TerminalPalette> palette, FontMetrics metrics,
        float width, float height, Action? before)
    {
        var times = new double[Frames];
        long runs = renderer.RunStringsBuilt;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++)
        {
            before?.Invoke();
            var p = palette();
            canvas.LastFrameCalls = 0;
            long start = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, screen, p, metrics, width, height, 0, null, true, (i & 1) == 0);
            times[i] = Microseconds(start);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(times);
        return string.Create(CultureInfo.InvariantCulture,
            $"{what}, warm ({Frames:N0}): median {times[Frames / 2]:F1} us, p99 {times[Frames * 99 / 100]:F1} us; {(renderer.RunStringsBuilt - runs) / (double)Frames:F1} run strings and {allocated / Frames:N0} bytes per frame (with what happens before it)\n");
    }

    /// <summary>The output: compiler-like lines, colours, a rewritten progress line every 50, a CJK file name every 97.</summary>
    private static byte[] Generate(int lines)
    {
        var text = new StringBuilder(lines * 72);
        for (int i = 0; i < lines; i++)
        {
            if (i % 50 == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\e[36mRestore\e[0m {i % 100}%\r\e[K");
            }

            if (i % 97 == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"  src/资料/文件{i}.cs(12,5): \e[1;33mwarning\e[0m CS0168: The variable 'e' is declared but never used\r\n");
            }
            else if (i % 7 == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"  \e[32mPASS\e[0m Craft.Tests.Terminal.Case{i} \e[2m({i % 1000} ms)\e[0m\r\n");
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"  Compiling src/Craft.Editor/Terminal/File{i}.cs -> obj/Release/net10.0/File{i}.o\r\n");
            }
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>The byte offset after <paramref name="lines"/> line feeds.</summary>
    private static int Offset(byte[] output, int lines)
    {
        int at = 0;
        for (int i = 0; i < lines && at < output.Length; i++)
        {
            int next = Array.IndexOf(output, (byte)'\n', at);
            if (next < 0)
            {
                return output.Length;
            }

            at = next + 1;
        }

        return at;
    }

    private sealed class CountingCanvas : IEditorCanvas
    {
        public long LastFrameCalls { get; set; }

        public void FillRect(float x, float y, float width, float height, uint argb) => LastFrameCalls++;

        public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness) => LastFrameCalls++;

        public void DrawText(string text, float x, float top, in TextStyle style) => LastFrameCalls++;
    }
}
