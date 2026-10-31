// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.CSharp;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// E2: the session parses off the UI thread, so each case is measured twice: the UI thread's cost (the buffer's edit and
/// its <c>Changed</c>, the frame's wait for the parse up to <see cref="CraftOptions.ParseWaitMilliseconds"/>, and the
/// classification of the 60 visible lines) and the background parse's own time. Cases: opening the generated file (the
/// first full parse), typing in a method body (cold, the first in the process, and warm; how often a keystroke's parse
/// outran the wait budget), a block comment opened at line 10 and deleted again (the deletion re-lexes the rest of the
/// file: the measured freeze before the parse moved off the UI thread), and a raw string.
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
        var budget = TimeSpan.FromMilliseconds(CraftOptions.FromEnvironment().ParseWaitMilliseconds);
        results.Append(CultureInfo.InvariantCulture, $"Craft C# bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores; parse wait budget {F(budget.TotalMilliseconds)} ms (CRAFT_PARSE_WAIT_MS)\n");
        results.Append("UI = on the UI thread: the edit and its Changed, the frame's wait for the parse (up to the budget), the classification of the 60 visible lines. parse = the worker's parse, off the UI thread.\n");

        var buffer = new PieceTableBuffer(text);
        text = null;
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        // Open: the session starts its first full parse on the worker; the first frame waits up to the budget, then draws plain.
        long start = Stopwatch.GetTimestamp();
        Slot[0] = new CSharpLanguage().Open(buffer);
        var session = Slot[0]!;
        double openUi = Microseconds(start);
        var snapshot = buffer.Current;
        start = Stopwatch.GetTimestamp();
        bool openClassified = session.WaitUntilClassified(snapshot, budget);
        int openSpans = VisibleSpans(session, snapshot, 0);
        double firstFrame = Microseconds(start);
        session.WhenIdle().GetAwaiter().GetResult();
        double openParse = session.LastUpdate.TotalMilliseconds;
        int landedSpans = VisibleSpans(session, snapshot, 0);
        long treeBytes = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;
        results.Append(CultureInfo.InvariantCulture, $"file: {snapshot.LineCount:N0} lines, {snapshot.Length:N0} chars; the syntax tree holds {treeBytes:N0} bytes = {F((double)treeBytes / (snapshot.Length * 2L), 2)} x the text (UTF-16)\n");
        results.Append(CultureInfo.InvariantCulture, $"open (cold): UI {F(openUi / 1000)} ms for Open, then the first frame {F(firstFrame / 1000)} ms ({(openClassified ? "classified" : "drawn plain")}, {openSpans} spans); the first full parse {F(openParse)} ms in the background, then {landedSpans} spans on Reclassified\n");

        // Typing in the middle of a method body, each keystroke followed by its frame, with no pause between them.
        int middleLine = snapshot.LineCount / 2;
        int caret = FindLine(snapshot, middleLine, "_items.Add(item);");
        int top = Math.Max(0, snapshot.GetLineIndex(caret) - VisibleLines / 2);
        var keystroke = new double[Keystrokes];
        var parse = new double[Keystrokes];
        var waited = new bool[Keystrokes];
        const string Typed = "var count = _items.Count + value; ";
        using var spans = new TokenSpanBuffer(1024);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Keystrokes; i++)
        {
            var piece = Typed[i % Typed.Length].ToString();
            start = Stopwatch.GetTimestamp();
            var now = buffer.Apply([TextEdit.Insert(caret, piece)]);
            waited[i] = session.WaitUntilClassified(now, budget);
            Classify(session, now, top, spans);
            keystroke[i] = Microseconds(start);
            caret++;
            session.WhenIdle().GetAwaiter().GetResult();
            parse[i] = session.LastUpdate.TotalMicroseconds;
        }

        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / Keystrokes;
        double cold = keystroke[0], coldParse = parse[0];
        var warm = keystroke.Skip(50).ToArray();
        var warmParse = parse.Skip(50).ToArray();
        int over = waited.Skip(50).Count(w => !w);
        results.Append(CultureInfo.InvariantCulture, $"keystroke (UI: edit + wait + classify 60 lines): cold {F(cold / 1000)} ms ({(waited[0] ? "classified" : "over the budget, drawn plain")}; parse {F(coldParse / 1000)} ms); warm median {F(Median(warm))} us, p99 {F(P99(warm))} us, max {F(warm.Max())} us; {over} of {warm.Length} warm keystrokes over the budget; parse median {F(Median(warmParse))} us, p99 {F(P99(warmParse))} us; {bytes:N0} B allocated/keystroke on the UI thread\n");

        // The same typing with no wait at all (CRAFT_PARSE_WAIT_MS=0): the UI never waits; the frame classifies through the change.
        bytes = GC.GetAllocatedBytesForCurrentThread();
        var noWait = new double[Keystrokes];
        for (int i = 0; i < Keystrokes; i++)
        {
            var piece = Typed[i % Typed.Length].ToString();
            start = Stopwatch.GetTimestamp();
            var now = buffer.Apply([TextEdit.Insert(caret, piece)]);
            session.WaitUntilClassified(now, TimeSpan.Zero);
            Classify(session, now, top, spans);
            noWait[i] = Microseconds(start);
            caret++;
            session.WhenIdle().GetAwaiter().GetResult();
        }

        bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / Keystrokes;
        var noWaitWarm = noWait.Skip(50).ToArray();
        results.Append(CultureInfo.InvariantCulture, $"keystroke with a budget of 0 (UI: edit + classify 60 lines through the pending change): warm median {F(Median(noWaitWarm))} us, p99 {F(P99(noWaitWarm))} us; {bytes:N0} B allocated/keystroke on the UI thread\n");

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
        results.Append(CultureInfo.InvariantCulture, $"reference, Roslyn's own SourceText (WithChanges) and the same parse, synchronously: warm median {F(Median(refWarm))} us, p99 {F(P99(refWarm))} us; {refBytes:N0} B allocated/keystroke\n");

        // The visible lines around the caret, classified from a landed parse: cold (first call) and warm.
        results.Append(CultureInfo.InvariantCulture, $"classify {VisibleLines} visible lines around the caret: {ClassifyCost(session, buffer.Current, top)}\n");

        // A block comment opened at line 10 and deleted again, five times: the deletion re-lexes the rest of the file.
        int commentAt = buffer.Current.GetLine(10).Start;
        var openUiTimes = new double[Rounds];
        var openParseTimes = new double[Rounds];
        var deleteUiTimes = new double[Rounds];
        var deleteParseTimes = new double[Rounds];
        int deletePlain = 0, deleteLanded = 0;
        for (int round = 0; round < Rounds; round++)
        {
            (openUiTimes[round], _, _) = UiCost(session, buffer, TextEdit.Insert(commentAt, "/*"), 0, budget, spans);
            session.WhenIdle().GetAwaiter().GetResult();
            openParseTimes[round] = session.LastUpdate.TotalMicroseconds;
            bool classified;
            (deleteUiTimes[round], classified, deletePlain) = UiCost(session, buffer, TextEdit.Delete(commentAt, 2), 0, budget, spans);
            session.WhenIdle().GetAwaiter().GetResult();
            deleteParseTimes[round] = session.LastUpdate.TotalMicroseconds;
            deleteLanded = VisibleSpans(session, buffer.Current, 0);
            if (classified)
            {
                deletePlain = -1;
            }
        }

        results.Append(CultureInfo.InvariantCulture, $"'/*' typed at line 10: UI cold {F(openUiTimes[0] / 1000)} ms, warm median {F(Median(openUiTimes.Skip(1).ToArray()) / 1000)} ms; parse cold {F(openParseTimes[0] / 1000)} ms, warm median {F(Median(openParseTimes.Skip(1).ToArray()) / 1000)} ms; then classify lines 0-60: {ClassifyCost(session, buffer.Current, 0)}\n");
        results.Append(CultureInfo.InvariantCulture, $"'/*' deleted again (the freeze case): UI cold {F(deleteUiTimes[0] / 1000)} ms, warm median {F(Median(deleteUiTimes.Skip(1).ToArray()) / 1000)} ms, max {F(deleteUiTimes.Max() / 1000)} ms ({(deletePlain < 0 ? "classified in the frame" : $"{deletePlain} spans in the frame, the rest plain")}); parse cold {F(deleteParseTimes[0] / 1000)} ms, warm median {F(Median(deleteParseTimes.Skip(1).ToArray()) / 1000)} ms, max {F(deleteParseTimes.Max() / 1000)} ms in the background; then {deleteLanded} spans on Reclassified\n");

        // Keystrokes inside a raw string in the middle of the file.
        int raw = FindLine(buffer.Current, middleLine, "raw ");
        int rawTop = Math.Max(0, buffer.Current.GetLineIndex(raw) - VisibleLines / 2);
        var rawTimes = new double[50];
        var rawParse = new double[50];
        for (int i = 0; i < rawTimes.Length; i++)
        {
            (rawTimes[i], _, _) = UiCost(session, buffer, TextEdit.Insert(raw + i, "q"), rawTop, budget, spans);
            session.WhenIdle().GetAwaiter().GetResult();
            rawParse[i] = session.LastUpdate.TotalMicroseconds;
        }

        results.Append(CultureInfo.InvariantCulture, $"keystrokes inside a raw string: UI first {F(rawTimes[0])} us, then median {F(Median(rawTimes.Skip(5).ToArray()))} us; parse median {F(Median(rawParse.Skip(5).ToArray()))} us\n");

        // A raw string's opening quotes typed at the top: the rest of the file becomes one string until it is closed.
        var (rawOpenUi, rawOpenClassified, _) = UiCost(session, buffer, TextEdit.Insert(commentAt, "var s = \"\"\"\n"), 0, budget, spans);
        session.WhenIdle().GetAwaiter().GetResult();
        results.Append(CultureInfo.InvariantCulture, $"a raw string opened at line 10: UI {F(rawOpenUi / 1000)} ms ({(rawOpenClassified ? "classified" : "over the budget")}); parse {F(session.LastUpdate.TotalMilliseconds)} ms in the background\n");
        results.Append(CultureInfo.InvariantCulture, $"parses landed: {session.Updates:N0}; parse error: {session.ParseError?.Message ?? "none"}\n");

        GC.KeepAlive(session);
        session.Dispose();
        Slot[0] = null;
        Report("csharp.txt", results);
    }

    private const int Rounds = 5;

    /// <summary>The UI thread's cost of one edit: the edit and its Changed, the wait up to the budget, the classification of the visible lines.</summary>
    private static (double Microseconds, bool Classified, int Spans) UiCost(CSharpLanguageSession session, PieceTableBuffer buffer, TextEdit edit, int top, TimeSpan budget, TokenSpanBuffer spans)
    {
        long start = Stopwatch.GetTimestamp();
        var now = buffer.Apply([edit]);
        bool classified = session.WaitUntilClassified(now, budget);
        Classify(session, now, top, spans);
        return (Microseconds(start), classified, spans.Count);
    }

    private static void Classify(CSharpLanguageSession session, ITextSnapshot snapshot, int top, TokenSpanBuffer spans)
    {
        int last = Math.Min(snapshot.LineCount - 1, top + VisibleLines - 1);
        int from = snapshot.GetLine(top).Start;
        spans.Clear();
        session.Classify(snapshot, from, snapshot.GetLine(last).EndIncludingBreak - from, spans);
    }

    private static int VisibleSpans(CSharpLanguageSession session, ITextSnapshot snapshot, int top)
    {
        using var spans = new TokenSpanBuffer(1024);
        Classify(session, snapshot, top, spans);
        return spans.Count;
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
