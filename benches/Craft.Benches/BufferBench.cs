// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Documents;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// E1: open a generated C# file, apply random edits through the document (with its undo history), read lines; cold (the
/// first call of a fresh process) and warm apart, and the memory held against the text's size.
/// </summary>
internal static class BufferBench
{
    private const int Edits = 10_000;
    private const int LineReads = 200_000;

    public static async Task RunAsync(int lines)
    {
        var results = new StringBuilder();
        var text = GenerateCSharp(lines);
        var input = Path.Combine(OutputFolder(), "input");
        Directory.CreateDirectory(input);
        var path = Path.Combine(input, "generated.cs");
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        long textBytes = text.Length * 2L;
        long fileBytes = new FileInfo(path).Length;
        results.Append(CultureInfo.InvariantCulture, $"Craft buffer bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores, {RuntimeInformationText()}\n");
        text = null;

        // Open: cold (first in the process) and warm.
        long start = Stopwatch.GetTimestamp();
        OpenInto(path);
        double openCold = Microseconds(start);
        long heapOpen = HeldByTheSlot();
        TextDocument? document = await TextDocument.OpenAsync(path).ConfigureAwait(false);
        ITextSnapshot? snapshot = document.Buffer.Current;
        var openWarm = new double[5];
        for (int i = 0; i < openWarm.Length; i++)
        {
            start = Stopwatch.GetTimestamp();
            using var again = await TextDocument.OpenAsync(path).ConfigureAwait(false);
            openWarm[i] = Microseconds(start);
        }

        results.Append(CultureInfo.InvariantCulture, $"file: {snapshot.LineCount:N0} lines, {snapshot.Length:N0} chars ({fileBytes:N0} bytes UTF-8, {textBytes:N0} bytes as UTF-16)\n");
        results.Append(CultureInfo.InvariantCulture, $"open: cold {F(openCold / 1000)} ms, warm median {F(Median(openWarm) / 1000)} ms\n");
        results.Append(CultureInfo.InvariantCulture, $"memory after open: {heapOpen:N0} bytes = {F((double)heapOpen / textBytes, 3)} x the text (UTF-16) [text + its line index]\n");

        // GetLine: cold (first call) and warm.
        var random = new Random(1);
        start = Stopwatch.GetTimestamp();
        var line = snapshot.GetLine(snapshot.LineCount / 2);
        double getLineCold = Microseconds(start);
        long sink = line.Length;
        for (int i = 0; i < 50_000; i++)
        {
            sink += snapshot.GetLine(random.Next(snapshot.LineCount)).Length;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < LineReads; i++)
        {
            sink += snapshot.GetLine(random.Next(snapshot.LineCount)).Length;
        }

        double getLineWarm = Microseconds(start) / LineReads;
        long getLineBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        results.Append(CultureInfo.InvariantCulture, $"GetLine (one piece): cold {F(getLineCold)} us, warm {F(getLineWarm, 3)} us, {getLineBytes / LineReads} B/call\n");

        // Random edits through the document: typing runs, deletions, newlines, pastes.
        int caret = snapshot.Length / 3;
        var editTimes = new double[Edits];
        long editBytes = GC.GetAllocatedBytesForCurrentThread();
        long editsStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < Edits; i++)
        {
            var current = document.Buffer.Current;
            if (random.Next(20) == 0)
            {
                caret = random.Next(current.Length + 1);
            }

            int roll = random.Next(10);
            start = Stopwatch.GetTimestamp();
            if (roll < 6)
            {
                var typed = Typed[random.Next(Typed.Length)];
                document.Edit([TextEdit.Insert(caret, typed)], EditKind.Typing, SelectionState.At(caret), SelectionState.At(caret + typed.Length));
                caret += typed.Length;
            }
            else if (roll < 8 && caret > 0)
            {
                document.Edit([TextEdit.Delete(caret - 1, 1)], EditKind.Deleting, SelectionState.At(caret), SelectionState.At(caret - 1));
                caret--;
            }
            else if (roll < 9)
            {
                document.Edit([TextEdit.Insert(caret, "\r\n        ")], EditKind.Typing, SelectionState.At(caret), SelectionState.At(caret + 10));
                caret += 10;
            }
            else
            {
                int length = Math.Min(40, current.Length - caret);
                document.Edit([new TextEdit(caret, length, "var pasted = Compute(value, item) + 1;")], EditKind.Paste, SelectionState.At(caret), SelectionState.At(caret + 38));
                caret += 38;
            }

            editTimes[i] = Microseconds(start);
        }

        double editsTotal = Microseconds(editsStart);
        editBytes = GC.GetAllocatedBytesForCurrentThread() - editBytes;
        double firstEdit = editTimes[0];
        double first100 = editTimes.Take(100).Average();
        double warmMean = editTimes.Skip(1000).Average();
        var warm = editTimes.Skip(1000).ToArray();
        double warmMedian = Median(warm.ToArray());
        double warmP99 = P99(warm);
        results.Append(CultureInfo.InvariantCulture, $"edits ({Edits:N0}, through the document with undo): cold first {F(firstEdit)} us, first 100 mean {F(first100)} us; warm (after 1,000) mean {F(warmMean, 3)} us, median {F(warmMedian, 3)} us, p99 {F(warmP99, 3)} us; total {F(editsTotal / 1000)} ms, {editBytes / Edits:N0} B allocated/edit\n");

        // Lines after the edits (many pieces now): cold-ish first call on the new tree, then warm.
        ITextSnapshot? edited = document.Buffer.Current;
        start = Stopwatch.GetTimestamp();
        sink += edited.GetLine(edited.LineCount / 2).Length;
        double editedLineCold = Microseconds(start);
        allocated = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < LineReads; i++)
        {
            sink += edited.GetLine(random.Next(edited.LineCount)).Length;
        }

        double editedLineWarm = Microseconds(start) / LineReads;
        getLineBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;

        // A visible screen of 60 lines read into a span, as the paint does.
        var lineBuffer = new char[4096];
        start = Stopwatch.GetTimestamp();
        const int Screens = 2_000;
        for (int s = 0; s < Screens; s++)
        {
            int top = random.Next(Math.Max(1, edited.LineCount - 60));
            for (int l = top; l < top + 60 && l < edited.LineCount; l++)
            {
                var visible = edited.GetLine(l);
                int take = Math.Min(visible.Length, lineBuffer.Length);
                edited.CopyTo(visible.Start, lineBuffer.AsSpan(0, take));
                sink += lineBuffer[0];
            }
        }

        double screen = Microseconds(start) / Screens;
        results.Append(CultureInfo.InvariantCulture, $"after the edits: {edited.LineCount:N0} lines; GetLine cold {F(editedLineCold)} us, warm {F(editedLineWarm, 3)} us, {getLineBytes / LineReads} B/call; 60 visible lines read (GetLine + CopyTo) {F(screen)} us\n");

        int editedLength = edited.Length;
        Slot[0] = document;
        edited = null;
        snapshot = null;
        document = null;
        long heapEdited = HeldByTheSlot();
        results.Append(CultureInfo.InvariantCulture, $"memory after {Edits:N0} edits (the current text, its pieces, the add buffer and the undo history): {heapEdited:N0} bytes = {F((double)heapEdited / (editedLength * 2L), 3)} x the text (UTF-16)\n");
        results.Append(CultureInfo.InvariantCulture, $"(sink {sink % 10})\n");
        Report("buffer.txt", results);
    }

    private static readonly string[] Typed = ["a", "b", "e", "x", " ", ";", "(", ")", "_", "i"];

    private static string RuntimeInformationText() => System.Runtime.InteropServices.RuntimeInformation.OSDescription + " " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

    private static readonly TextDocument?[] Slot = new TextDocument?[1];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OpenInto(string path) => Slot[0] = TextDocument.OpenAsync(path).GetAwaiter().GetResult();

    /// <summary>The heap bytes the document in <see cref="Slot"/> holds: the heap with it, less the heap once it is dropped.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long HeldByTheSlot()
    {
        long with = GC.GetTotalMemory(forceFullCollection: true);
        Slot[0] = null;
        long without = GC.GetTotalMemory(forceFullCollection: true);
        return with - without;
    }
}
