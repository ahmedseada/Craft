// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Text;
using Craft.CSharp;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// E2: the session's incremental parse per keystroke (cold, the first in the process, and warm), and the classification
/// of 60 visible lines after edits that change the rest of the file's meaning (a block comment opened at the top, a
/// keystroke inside a raw string), on the generated C# file.
/// </summary>
internal static class CSharpBench
{
    private const int VisibleLines = 60;
    private const int Keystrokes = 400;
    private const int Classifications = 200;

    public static void Run(int lines)
    {
        var results = new StringBuilder();
        var text = GenerateCSharp(lines);
        results.Append(CultureInfo.InvariantCulture, $"Craft C# bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores\n");

        var buffer = new PieceTableBuffer(text);
        text = null;
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        long start = Stopwatch.GetTimestamp();
        Slot[0] = new CSharpLanguage().Open(buffer);
        double openMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long treeBytes = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;
        var session = Slot[0]!;
        var snapshot = buffer.Current;
        results.Append(CultureInfo.InvariantCulture, $"file: {snapshot.LineCount:N0} lines, {snapshot.Length:N0} chars; first full parse (cold) {F(openMs)} ms; the syntax tree holds {treeBytes:N0} bytes = {F((double)treeBytes / (snapshot.Length * 2L), 2)} x the text (UTF-16)\n");

        // Keystrokes in the middle of a method body: each one is the buffer's edit plus the incremental parse.
        int middleLine = snapshot.LineCount / 2;
        int caret = FindLine(snapshot, middleLine, "_items.Add(item);");
        var keystroke = new double[Keystrokes];
        var parse = new double[Keystrokes];
        const string Typed = "var count = _items.Count + value; ";
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Keystrokes; i++)
        {
            var piece = Typed[i % Typed.Length].ToString();
            start = Stopwatch.GetTimestamp();
            buffer.Apply([TextEdit.Insert(caret, piece)]);
            keystroke[i] = Microseconds(start);
            parse[i] = session.LastUpdate.TotalMicroseconds;
            caret++;
        }

        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / Keystrokes;
        double cold = keystroke[0], coldParse = parse[0];
        var warm = keystroke.Skip(50).ToArray();
        var warmParse = parse.Skip(50).ToArray();
        results.Append(CultureInfo.InvariantCulture, $"keystroke (edit + incremental parse): cold {F(cold)} us (parse {F(coldParse)} us); warm mean {F(warm.Average())} us, median {F(Median(warm))} us, p99 {F(P99(warm))} us (parse median {F(Median(warmParse))} us); {bytes:N0} B allocated/keystroke\n");

        // The reference: the same keystrokes on Roslyn's own text (SourceText.From, then WithChanges), parsed the same way.
        var reference = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(buffer.Current.GetText(0, buffer.Current.Length), new CSharpLanguage().ParseOptions);
        int refCaret = caret;
        var refTimes = new double[Keystrokes];
        long refBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Keystrokes; i++)
        {
            var piece = Typed[i % Typed.Length].ToString();
            start = Stopwatch.GetTimestamp();
            var changed = reference.GetText().WithChanges(new Microsoft.CodeAnalysis.Text.TextChange(new Microsoft.CodeAnalysis.Text.TextSpan(refCaret, 0), piece));
            reference = reference.WithChangedText(changed);
            _ = reference.GetRoot();
            refTimes[i] = Microseconds(start);
            refCaret++;
        }

        refBytes = (GC.GetAllocatedBytesForCurrentThread() - refBytes) / Keystrokes;
        var refWarm = refTimes.Skip(50).ToArray();
        results.Append(CultureInfo.InvariantCulture, $"reference, Roslyn's own SourceText (WithChanges) and the same parse: warm median {F(Median(refWarm))} us, p99 {F(P99(refWarm))} us; {refBytes:N0} B allocated/keystroke\n");

        // The visible lines around the caret, classified: cold (first call) and warm.
        int top = Math.Max(0, buffer.Current.GetLineIndex(caret) - VisibleLines / 2);
        results.Append(CultureInfo.InvariantCulture, $"classify {VisibleLines} visible lines around the caret: {ClassifyCost(session, buffer.Current, top)}\n");

        // A block comment opened at the top of the file: every line below becomes comment.
        int commentAt = buffer.Current.GetLine(10).Start;
        start = Stopwatch.GetTimestamp();
        buffer.Apply([TextEdit.Insert(commentAt, "/*")]);
        double openComment = Microseconds(start);
        results.Append(CultureInfo.InvariantCulture, $"'/*' typed at line 10: edit + parse {F(openComment / 1000)} ms (parse {F(session.LastUpdate.TotalMilliseconds)} ms); classify lines 0-60: {ClassifyCost(session, buffer.Current, 0)}; lines at the middle: {ClassifyCost(session, buffer.Current, middleLine)}\n");
        start = Stopwatch.GetTimestamp();
        buffer.Apply([TextEdit.Delete(commentAt, 2)]);
        results.Append(CultureInfo.InvariantCulture, $"'/*' deleted again: edit + parse {F(Microseconds(start) / 1000)} ms; classify the middle lines: {ClassifyCost(session, buffer.Current, middleLine)}\n");

        // A keystroke inside a raw string in the middle of the file.
        int raw = FindLine(buffer.Current, middleLine, "raw ");
        var rawTimes = new double[50];
        for (int i = 0; i < rawTimes.Length; i++)
        {
            start = Stopwatch.GetTimestamp();
            buffer.Apply([TextEdit.Insert(raw + i, "q")]);
            rawTimes[i] = Microseconds(start);
        }

        int rawTop = Math.Max(0, buffer.Current.GetLineIndex(raw) - VisibleLines / 2);
        results.Append(CultureInfo.InvariantCulture, $"keystrokes inside a raw string: first {F(rawTimes[0])} us, then median {F(Median(rawTimes.Skip(5).ToArray()))} us; classify the {VisibleLines} lines around it: {ClassifyCost(session, buffer.Current, rawTop)}\n");

        // A raw string's opening quotes typed at the top: the rest of the file becomes one string until it is closed.
        start = Stopwatch.GetTimestamp();
        buffer.Apply([TextEdit.Insert(commentAt, "var s = \"\"\"\n")]);
        results.Append(CultureInfo.InvariantCulture, $"a raw string opened at line 10: edit + parse {F(Microseconds(start) / 1000)} ms; classify lines 0-60: {ClassifyCost(session, buffer.Current, 0)}\n");

        GC.KeepAlive(session);
        Slot[0] = null;
        Report("csharp.txt", results);
    }

    private static readonly CSharpLanguageSession?[] Slot = new CSharpLanguageSession?[1];

    /// <summary>The cost of classifying <see cref="VisibleLines"/> lines from <paramref name="top"/>: the first call, then the warm mean, and its allocations.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ClassifyCost(CSharpLanguageSession session, ITextSnapshot snapshot, int top)
    {
        int last = Math.Min(snapshot.LineCount - 1, top + VisibleLines - 1);
        int from = snapshot.GetLine(top).Start;
        int length = snapshot.GetLine(last).EndIncludingBreak - from;
        using var spans = new TokenSpanBuffer(1024);
        long start = Stopwatch.GetTimestamp();
        session.Classify(snapshot, from, length, spans);
        double first = Microseconds(start);
        int count = spans.Count;
        for (int i = 0; i < 20; i++)
        {
            spans.Clear();
            session.Classify(snapshot, from, length, spans);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Classifications; i++)
        {
            spans.Clear();
            session.Classify(snapshot, from, length, spans);
        }

        double warm = Microseconds(start) / Classifications;
        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / Classifications;
        return $"{count} spans, first {F(first)} us, warm {F(warm)} us, {bytes:N0} B/call";
    }

    private static int FindLine(ITextSnapshot snapshot, int fromLine, string what)
    {
        for (int line = fromLine; line < snapshot.LineCount; line++)
        {
            var at = snapshot.GetLine(line);
            int index = snapshot.GetText(at.Start, at.Length).IndexOf(what, StringComparison.Ordinal);
            if (index >= 0)
            {
                return at.Start + index;
            }
        }

        throw new InvalidOperationException($"'{what}' is not in the generated file after line {fromLine}.");
    }
}
