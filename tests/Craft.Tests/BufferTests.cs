// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>The piece table against a plain string model: text, lines, line indexes, ranges, and old snapshots.</summary>
[TestGroup("Buffer")]
public static class BufferTests
{
    [Test]
    public static void RandomEditsMatchAStringModel() => RandomEquivalence(seed: 1234, steps: 4000, chunk: 64 * 1024);

    [Test]
    public static void RandomEditsMatchAStringModelWithTinyChunks() => RandomEquivalence(seed: 99, steps: 4000, chunk: 16);

    [Test]
    public static void RandomMultiEditChangesMatchAStringModel()
    {
        var random = new Random(4321);
        var model = new StringBuilder(RandomText(random, 300));
        var buffer = new PieceTableBuffer(model.ToString(), new CraftOptions { BufferChunkCharacters = 32 });
        for (int step = 0; step < 1500; step++)
        {
            int count = random.Next(1, 5);
            var edits = new List<TextEdit>();
            int at = 0;
            for (int i = 0; i < count && at <= model.Length; i++)
            {
                int start = random.Next(at, Math.Min(model.Length, at + 40) + 1);
                int length = random.Next(0, Math.Min(model.Length - start, 6) + 1);
                edits.Add(new TextEdit(start, length, RandomText(random, random.Next(0, 5))));
                at = start + length;
            }

            for (int i = edits.Count - 1; i >= 0; i--)
            {
                model.Remove(edits[i].Start, edits[i].Length).Insert(edits[i].Start, edits[i].Text);
            }

            buffer.Apply(edits.ToArray());
            CheckSnapshot(buffer.Current, model.ToString(), random, $"multi-edit step {step}");
        }
    }

    [Test]
    public static void KeystrokesAtACaretMatchAStringModel()
    {
        // Typing runs that extend the piece before the caret, backspaces and deletes that trim a piece's edge.
        foreach (int chunk in new[] { 16, 64 * 1024 })
        {
            var random = new Random(chunk);
            var model = new StringBuilder(RandomText(random, 400));
            var buffer = new PieceTableBuffer(model.ToString(), new CraftOptions { BufferChunkCharacters = chunk });
            int caret = 200;
            for (int step = 0; step < 4000; step++)
            {
                if (random.Next(30) == 0)
                {
                    caret = random.Next(model.Length + 1);
                }

                int roll = random.Next(10);
                TextEdit edit;
                if (roll < 6)
                {
                    edit = TextEdit.Insert(caret, RandomText(random, random.Next(1, 3)));
                    caret += edit.Text.Length;
                }
                else if (roll < 8 && caret > 0)
                {
                    edit = TextEdit.Delete(caret - 1, 1);
                    caret--;
                }
                else if (caret < model.Length)
                {
                    edit = TextEdit.Delete(caret, Math.Min(model.Length - caret, random.Next(1, 3)));
                }
                else
                {
                    continue;
                }

                model.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
                buffer.Apply([edit]);
                CheckSnapshot(buffer.Current, model.ToString(), random, $"chunk {chunk}, keystroke {step}");
            }
        }
    }

    [Test]
    public static void CrLfSplitAcrossEditsIsOneBreak()
    {
        var buffer = new PieceTableBuffer("a\rb\nc");
        Check.Equal(3, buffer.Current.LineCount, "'a\\rb\\nc' has three lines");
        buffer.Apply([TextEdit.Delete(2, 1)]);
        CheckSnapshot(buffer.Current, "a\r\nc", new Random(1), "after deleting the b between \\r and \\n");
        Check.Equal(new TextLine(0, 0, 1, 2), buffer.Current.GetLine(0), "the \\r\\n made of two pieces is one break of length 2");
        buffer.Apply([TextEdit.Insert(2, "x")]);
        CheckSnapshot(buffer.Current, "a\rx\nc", new Random(2), "after inserting between \\r and \\n");

        var typed = new PieceTableBuffer("");
        typed.Apply([TextEdit.Insert(0, "line\r")]);
        typed.Apply([TextEdit.Insert(5, "\n")]);
        typed.Apply([TextEdit.Insert(6, "next")]);
        CheckSnapshot(typed.Current, "line\r\nnext", new Random(3), "\\r and \\n typed apart");
        Check.Equal(0, typed.Current.GetLineIndex(5), "the position of the \\n of a \\r\\n belongs to the line it ends");
        Check.Equal(1, typed.Current.GetLineIndex(6), "the position after the \\r\\n is the next line");
    }

    [Test]
    public static void OldSnapshotsKeepTheirTextAndAreReadOnOtherThreads()
    {
        var random = new Random(7);
        var model = new StringBuilder(RandomText(random, 2000));
        var buffer = new PieceTableBuffer(model.ToString());
        var kept = new List<(ITextSnapshot Snapshot, string Text)>();
        for (int i = 0; i < 300; i++)
        {
            RandomEdit(random, model, buffer);
            if (i % 10 == 0)
            {
                kept.Add((buffer.Current, model.ToString()));
            }
        }

        // Readers on other threads while the buffer keeps changing on this one.
        var readers = kept.Select(k => Task.Run(() =>
        {
            for (int r = 0; r < 20; r++)
            {
                if (k.Snapshot.GetText(0, k.Snapshot.Length) != k.Text)
                {
                    return false;
                }
            }

            return true;
        })).ToArray();
        for (int i = 0; i < 300; i++)
        {
            RandomEdit(random, model, buffer);
        }

        Check.True(Task.WhenAll(readers).GetAwaiter().GetResult().All(ok => ok), "every old snapshot read its own text on another thread");
        foreach (var (snapshot, text) in kept)
        {
            CheckSnapshot(snapshot, text, random, $"old snapshot of version {snapshot.Version}");
        }
    }

    [Test]
    public static void ChangesAreValidatedAndReported()
    {
        var buffer = new PieceTableBuffer("hello world");
        TextChangedEventArgs? seen = null;
        buffer.Changed += (_, e) => seen = e;
        var before = buffer.Current;
        var after = buffer.Apply([new TextEdit(0, 5, "HELLO"), TextEdit.Insert(11, "!")]);
        Check.Equal("HELLO world!", after.GetText(0, after.Length), "both edits applied, in the positions of the text before");
        Check.Equal(1, after.Version, "the version grows by one per change");
        Check.True(seen is not null && ReferenceEquals(seen.Before, before) && ReferenceEquals(seen.After, after), "Changed carries the snapshots before and after");
        Check.SequenceEqual([new TextEdit(0, 5, "HELLO"), TextEdit.Insert(11, "!")], seen!.Edits, "Changed carries the edits");
        Check.Equal(TextChangeReason.Edit, seen.Reason, "the reason");

        seen = null;
        Check.True(ReferenceEquals(after, buffer.Apply([])), "no edits change nothing and return the current snapshot");
        Check.True(ReferenceEquals(after, buffer.Apply([TextEdit.Insert(3, "")])), "an edit that does nothing changes nothing");
        Check.True(seen is null, "no change raises no event");

        var outside = Check.Throws<ArgumentException>(() => buffer.Apply([new TextEdit(10, 5, "x")]), "an edit past the end");
        Check.True(outside.Message.Contains("outside", StringComparison.Ordinal), "the message says the edit runs outside the text");
        var overlap = Check.Throws<ArgumentException>(() => buffer.Apply([new TextEdit(0, 4, "x"), new TextEdit(2, 1, "y")]), "overlapping edits");
        Check.True(overlap.Message.Contains("overlap", StringComparison.Ordinal), "the message says the edits overlap");
        Check.Throws<ArgumentException>(() => buffer.Apply([TextEdit.Insert(5, "x"), TextEdit.Insert(1, "y")]), "edits out of order");
        Check.Throws<ArgumentException>(() => buffer.Apply([new TextEdit(-1, 0, "x")]), "a negative start");
        Check.Equal(1, buffer.Current.Version, "a refused change leaves the buffer as it was");
        Check.Throws<ArgumentOutOfRangeException>(() => _ = buffer.Current[12], "a character past the end");
        Check.Throws<ArgumentOutOfRangeException>(() => buffer.Current.GetLine(1), "a line past the last");
        Check.Throws<ArgumentOutOfRangeException>(() => buffer.Current.CopyTo(10, new char[3]), "a range past the end");
    }

    [Test]
    public static void EmptyTextIsOneEmptyLine()
    {
        var buffer = new PieceTableBuffer(ReadOnlySpan<char>.Empty);
        Check.Equal(1, buffer.Current.LineCount, "an empty text has one line");
        Check.Equal(new TextLine(0, 0, 0, 0), buffer.Current.GetLine(0), "its line is empty");
        Check.Equal(0, buffer.Current.GetLineIndex(0), "position 0 is on line 0");
        buffer.Apply([TextEdit.Insert(0, "\n")]);
        CheckSnapshot(buffer.Current, "\n", new Random(5), "a lone line break");
        buffer.Apply([TextEdit.Delete(0, 1)]);
        CheckSnapshot(buffer.Current, "", new Random(5), "everything deleted");
    }

    /// <summary>
    /// The X restart (piece 2a): changes of many edits built in one batch (forced from one edit, and from the default eight)
    /// equal a plain string after every step (rule 71), with tiny and large add-buffer blocks, with and without coalescing,
    /// line breaks among the edits ("\r\n" split across them too); the tree stays an AVL tree whose heights, lengths and
    /// line breaks follow from its nodes, and old snapshots keep their text.
    /// </summary>
    [Test]
    public static void BatchedMultiEditChangesMatchAStringModelAndKeepTheTreeSound()
    {
        foreach (var (seed, chunk, minEdits, coalesce) in new[] { (11, 16, 1, 64), (12, 64 * 1024, 1, 64), (13, 32, 8, 64), (14, 64 * 1024, 1, 0), (15, 16, 2, 7) })
        {
            var random = new Random(seed);
            var model = new StringBuilder(RandomText(random, 400));
            var buffer = new PieceTableBuffer(model.ToString(), new CraftOptions { BufferChunkCharacters = chunk, PieceBatchMinEdits = minEdits, PieceCoalesceCharacters = coalesce });
            var kept = new List<(ITextSnapshot Snapshot, string Text)>();
            for (int step = 0; step < 600; step++)
            {
                int count = random.Next(1, 40);
                var edits = new List<TextEdit>();
                int at = 0;
                for (int i = 0; i < count && at <= model.Length; i++)
                {
                    int start = random.Next(at, Math.Min(model.Length, at + 30) + 1);
                    int length = random.Next(4) == 0 ? random.Next(0, Math.Min(model.Length - start, 5) + 1) : 0;
                    string text = random.Next(5) == 0 ? "" : RandomText(random, random.Next(1, random.Next(3) == 0 ? 20 : 3));
                    edits.Add(new TextEdit(start, length, text));
                    at = start + length;
                }

                for (int i = edits.Count - 1; i >= 0; i--)
                {
                    model.Remove(edits[i].Start, edits[i].Length).Insert(edits[i].Start, edits[i].Text);
                }

                buffer.Apply(edits.ToArray());
                string what = $"seed {seed}, step {step} ({edits.Count} edits, batch from {minEdits}, coalesce {coalesce})";
                CheckSnapshot(buffer.Current, model.ToString(), random, what);
                Check.True(PieceTableBuffer.TreeProblem(buffer.Current) is null, $"{what}: the tree is sound ({PieceTableBuffer.TreeProblem(buffer.Current)})");
                int pieces = PieceTableBuffer.PieceCount(buffer.Current);
                Check.True(PieceTableBuffer.TreeHeight(buffer.Current) <= (1.45 * Math.Log2(pieces + 2)) + 1, $"{what}: an AVL tree's height ({PieceTableBuffer.TreeHeight(buffer.Current)} for {pieces} pieces)");
                if (step % 53 == 0)
                {
                    kept.Add((buffer.Current, model.ToString()));
                }
            }

            Check.True(buffer.BatchedChanges > 0, $"seed {seed}: changes went through the batch ({buffer.BatchedChanges})");
            foreach (var (snapshot, text) in kept)
            {
                CheckSnapshot(snapshot, text, random, $"seed {seed}: old snapshot of version {snapshot.Version}");
            }
        }
    }

    /// <summary>
    /// Typing at a hundred carets: the batch keeps a piece a caret (joined while it stays within the coalescing limit),
    /// where the edit-by-edit path keeps one a caret a keystroke; both give the same text, and Backspace takes it back.
    /// </summary>
    [Test]
    public static void TypingAtAHundredCaretsKeepsAPieceACaretInsteadOfOneAKeystroke()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            text.Append("    int value").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" = 1;\n");
        }

        string original = text.ToString();
        var batched = new PieceTableBuffer(original, new CraftOptions());
        var single = new PieceTableBuffer(original, new CraftOptions { PieceBatchMinEdits = int.MaxValue });
        const int keys = 400;
        var edits = new TextEdit[100];
        for (int key = 0; key < keys; key++)
        {
            Carets(batched.Current, key, edits, typed: true);
            batched.Apply(edits);
            single.Apply(edits);
        }

        Check.Equal(single.Current.ToString(), batched.Current.ToString(), "the batch's text equals the edit-by-edit path's");
        int batchedPieces = PieceTableBuffer.PieceCount(batched.Current);
        int singlePieces = PieceTableBuffer.PieceCount(single.Current);
        int bound = (100 * (((keys + 63) / 64) + 1)) + 101;
        Check.True(batchedPieces <= bound, $"a piece a caret per 64 typed characters, plus the original's cuts ({batchedPieces} pieces, at most {bound})");
        Check.True(singlePieces >= 100 * keys, $"the edit-by-edit path keeps a piece a caret a keystroke ({singlePieces})");
        Check.Equal(keys, (int)batched.BatchedChanges, "every keystroke went through the batch");
        Check.True(PieceTableBuffer.TreeProblem(batched.Current) is null, "the batched tree is sound");
        for (int key = keys - 1; key >= 0; key--)
        {
            Carets(batched.Current, key, edits, typed: false);
            batched.Apply(edits);
        }

        Check.Equal(original, batched.Current.ToString(), "400 Backspaces at every caret take the typing back");

        // Each caret sits before the ';' of its line; `key` characters were typed there before this one.
        static void Carets(ITextSnapshot snapshot, int key, TextEdit[] edits, bool typed)
        {
            for (int i = 0; i < 100; i++)
            {
                int caret = snapshot.GetLine(i).End - 1;
                edits[i] = typed ? TextEdit.Insert(caret, "x") : TextEdit.Delete(caret - 1, 1);
            }
        }
    }

    /// <summary>A change whose first and last edits hold a much-edited middle between them takes the edit-by-edit path (its cost does not grow with the middle).</summary>
    [Test]
    public static void AMiddleOfManyPiecesTakesTheEditByEditPath()
    {
        var random = new Random(77);
        var model = new StringBuilder(RandomText(random, 4000));
        var buffer = new PieceTableBuffer(model.ToString(), new CraftOptions { PieceCoalesceCharacters = 0 });
        for (int i = 0; i < 2000; i++)
        {
            RandomEdit(random, model, buffer);       // single edits: a fragmented text
        }

        long before = buffer.BatchedChanges;
        var edits = new List<TextEdit>();
        for (int i = 0; i < 4; i++)
        {
            edits.Add(TextEdit.Insert(i, "a"));
        }

        for (int i = 0; i < 4; i++)
        {
            edits.Add(TextEdit.Insert(model.Length - 4 + i, "b"));
        }

        for (int i = edits.Count - 1; i >= 0; i--)
        {
            model.Insert(edits[i].Start, edits[i].Text);
        }

        buffer.Apply(edits.ToArray());
        Check.Equal(before, buffer.BatchedChanges, $"eight edits around a middle of {PieceTableBuffer.PieceCount(buffer.Current)} pieces are not batched");
        CheckSnapshot(buffer.Current, model.ToString(), random, "the edit-by-edit path's text");
        Check.True(PieceTableBuffer.TreeProblem(buffer.Current) is null, "the tree is sound");
    }

    private static void RandomEquivalence(int seed, int steps, int chunk)
    {
        var random = new Random(seed);
        var model = new StringBuilder(RandomText(random, 500));
        var buffer = new PieceTableBuffer(model.ToString(), new CraftOptions { BufferChunkCharacters = chunk });
        var kept = new List<(ITextSnapshot Snapshot, string Text)>();
        for (int step = 0; step < steps; step++)
        {
            RandomEdit(random, model, buffer);
            CheckSnapshot(buffer.Current, model.ToString(), random, $"step {step}");
            if (step % 97 == 0)
            {
                kept.Add((buffer.Current, model.ToString()));
            }
        }

        foreach (var (snapshot, text) in kept)
        {
            CheckSnapshot(snapshot, text, random, $"old snapshot of version {snapshot.Version}");
        }
    }

    private static void RandomEdit(Random random, StringBuilder model, PieceTableBuffer buffer)
    {
        int start = random.Next(0, model.Length + 1);
        int kind = random.Next(3);
        int length = kind == 0 ? 0 : random.Next(0, Math.Min(model.Length - start, 12) + 1);
        string text = kind == 1 ? "" : RandomText(random, random.Next(1, random.Next(2) == 0 ? 4 : 30));
        model.Remove(start, length).Insert(start, text);
        buffer.Apply([new TextEdit(start, length, text)]);
    }

    private static string RandomText(Random random, int length)
    {
        const string Alphabet = "abcdefgh ;{}\r\n\n\r\t";
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Checks a snapshot against its expected text: the text, length, line count, every line, line indexes, characters and ranges.</summary>
    internal static void CheckSnapshot(ITextSnapshot snapshot, string text, Random random, string what)
    {
        Check.Equal(text.Length, snapshot.Length, $"{what}: Length");
        Check.Equal(text, snapshot.GetText(0, snapshot.Length), $"{what}: the text");
        var lines = ModelLines(text);
        Check.Equal(lines.Count, snapshot.LineCount, $"{what}: LineCount");
        for (int i = 0; i < lines.Count; i++)
        {
            Check.Equal(lines[i], snapshot.GetLine(i), $"{what}: line {i}");
        }

        for (int i = 0; i < 20; i++)
        {
            int position = random.Next(0, text.Length + 1);
            Check.Equal(ModelLineIndex(lines, position), snapshot.GetLineIndex(position), $"{what}: GetLineIndex({position})");
            if (position < text.Length)
            {
                Check.Equal(text[position], snapshot[position], $"{what}: this[{position}]");
            }

            int length = random.Next(0, text.Length - position + 1);
            var copy = new char[length];
            snapshot.CopyTo(position, copy);
            Check.True(copy.AsSpan().SequenceEqual(text.AsSpan(position, length)), $"{what}: CopyTo({position}, {length})");
        }

        Check.Equal(lines.Count - 1, snapshot.GetLineIndex(text.Length), $"{what}: GetLineIndex(Length)");
    }

    private static List<TextLine> ModelLines(string text)
    {
        var lines = new List<TextLine>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                lines.Add(new TextLine(lines.Count, start, i - start, 2));
                start = i + 2;
                i++;
            }
            else if (text[i] is '\r' or '\n')
            {
                lines.Add(new TextLine(lines.Count, start, i - start, 1));
                start = i + 1;
            }
        }

        lines.Add(new TextLine(lines.Count, start, text.Length - start, 0));
        return lines;
    }

    private static int ModelLineIndex(List<TextLine> lines, int position)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (position < lines[i].EndIncludingBreak || i == lines.Count - 1)
            {
                return i;
            }
        }

        return lines.Count - 1;
    }
}
