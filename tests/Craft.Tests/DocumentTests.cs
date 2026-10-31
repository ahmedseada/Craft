// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>A folder under test-output/tmp for one test, deleted after it.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = TestRunner.RepositoryPath("test-output", "tmp", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Documents: undo and redo with grouping, dirty tracking, encodings, line endings and the atomic save.</summary>
[TestGroup("Document")]
public static class DocumentTests
{
    private sealed class FakeClock
    {
        public long Now { get; set; } = 1_000;

        public long Read() => Now;
    }

    private static readonly CraftOptions Grouping = new() { UndoGroupMilliseconds = 1000, NewLine = "\n" };

    private static void Type(TextDocument document, FakeClock clock, int at, string text, long pause = 100)
    {
        clock.Now += pause;
        document.Edit([TextEdit.Insert(at, text)], EditKind.Typing, SelectionState.At(at), SelectionState.At(at + text.Length));
    }

    private static string TextOf(TextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    [Test]
    public static void TypingGroupsWhileThePausesStayShort()
    {
        var clock = new FakeClock();
        var document = TextDocument.Create("", Grouping, clock.Read);
        Type(document, clock, 0, "a");
        Type(document, clock, 1, "b");
        Type(document, clock, 2, "c");
        Type(document, clock, 3, "d", pause: 1500);
        Type(document, clock, 4, "e");
        Check.Equal("abcde", TextOf(document), "typed text");
        Check.Equal(SelectionState.At(3), document.Undo(), "undo returns the selection before the second group");
        Check.Equal("abc", TextOf(document), "the pause split the typing: 'de' is one step");
        Check.Equal(SelectionState.At(0), document.Undo(), "undo returns the selection before the first group");
        Check.Equal("", TextOf(document), "'abc' is one step");
        Check.True(!document.CanUndo && document.CanRedo, "nothing left to undo, two steps to redo");
        Check.Equal(SelectionState.At(3), document.Redo(), "redo returns the selection after the step");
        Check.Equal("abc", TextOf(document), "redo of the first group");
        Check.Equal(SelectionState.At(5), document.Redo(), "redo of the second group");
        Check.Equal("abcde", TextOf(document), "redone");
    }

    [Test]
    public static void ACaretJumpOrANewlineBreaksTheGroup()
    {
        var clock = new FakeClock();
        var document = TextDocument.Create("", Grouping, clock.Read);
        Type(document, clock, 0, "ab");
        Type(document, clock, 0, "X");
        Check.Equal("Xab", TextOf(document), "typed at the start");
        document.Undo();
        Check.Equal("ab", TextOf(document), "typing elsewhere than the caret left off is a step of its own");

        Type(document, clock, 2, "\n");
        Type(document, clock, 3, "cd");
        document.Undo();
        Check.Equal("ab\n", TextOf(document), "a newline closes its group: what follows is a new step");
        document.Undo();
        Check.Equal("ab", TextOf(document), "the newline started a step of its own: the 'ab' step was closed by the step after it");

        var clock2 = new FakeClock();
        var joined = TextDocument.Create("", Grouping, clock2.Read);
        Type(joined, clock2, 0, "ab");
        Type(joined, clock2, 2, "\n");
        Type(joined, clock2, 3, "cd");
        joined.Undo();
        Check.Equal("ab\n", TextOf(joined), "a newline closes its group");
        joined.Undo();
        Check.Equal("", TextOf(joined), "the newline joined the typing before it");
    }

    [Test]
    public static void DeletingGroupsBackspacesAndDeletes()
    {
        var clock = new FakeClock();
        var document = TextDocument.Create("hello world", Grouping, clock.Read);
        int caret = 5;
        for (int i = 0; i < 3; i++)
        {
            clock.Now += 50;
            document.Edit([TextEdit.Delete(caret - 1, 1)], EditKind.Deleting, SelectionState.At(caret), SelectionState.At(caret - 1));
            caret--;
        }

        clock.Now += 50;
        document.Edit([TextEdit.Delete(caret, 1)], EditKind.Deleting, SelectionState.At(caret), SelectionState.At(caret));
        Check.Equal("heworld", TextOf(document), "three backspaces and a delete");
        Check.Equal(SelectionState.At(5), document.Undo(), "one step back to the first caret");
        Check.Equal("hello world", TextOf(document), "all four deletions were one step");
        document.Redo();
        Check.Equal("heworld", TextOf(document), "redo of the group");
    }

    [Test]
    public static void RandomStepsUndoAndRedoExactly()
    {
        var random = new Random(2026);
        var clock = new FakeClock();
        var document = TextDocument.Create("start\r\ntext\n", new CraftOptions { BufferChunkCharacters = 32 }, clock.Read);
        var states = new List<string> { TextOf(document) };
        for (int i = 0; i < 400; i++)
        {
            var text = TextOf(document);
            var edits = new List<TextEdit>();
            int at = 0;
            for (int e = 0, count = random.Next(1, 4); e < count && at <= text.Length; e++)
            {
                int start = random.Next(at, Math.Min(text.Length, at + 20) + 1);
                int length = random.Next(0, Math.Min(text.Length - start, 5) + 1);
                edits.Add(new TextEdit(start, length, random.Next(3) == 0 ? "" : "x\r\ny"[..random.Next(1, 5)]));
                at = start + length;
            }

            clock.Now += 10;
            var before = document.Buffer.Current;
            document.Edit(edits.ToArray(), EditKind.Command, SelectionState.At(0), SelectionState.At(0));
            if (!ReferenceEquals(document.Buffer.Current, before))
            {
                states.Add(TextOf(document));
            }
        }

        for (int i = states.Count - 2; i >= 0; i--)
        {
            document.Undo();
            Check.Equal(states[i], TextOf(document), $"undo back to state {i}");
        }

        Check.True(document.Undo() is null, "nothing more to undo");
        for (int i = 1; i < states.Count; i++)
        {
            document.Redo();
            Check.Equal(states[i], TextOf(document), $"redo to state {i}");
        }
    }

    [Test]
    public static void RandomTypingGroupsUndoToTheStart()
    {
        var random = new Random(77);
        var clock = new FakeClock();
        var document = TextDocument.Create("seed text", Grouping, clock.Read);
        int caret = 4;
        for (int i = 0; i < 2000; i++)
        {
            clock.Now += random.Next(10) == 0 ? 2000 : 30;
            int length = document.Buffer.Current.Length;
            if (random.Next(8) == 0)
            {
                caret = random.Next(0, length + 1);
            }

            if (random.Next(3) == 0 && caret > 0)
            {
                document.Edit([TextEdit.Delete(caret - 1, 1)], EditKind.Deleting, SelectionState.At(caret), SelectionState.At(caret - 1));
                caret--;
            }
            else if (random.Next(4) == 0 && caret < length)
            {
                document.Edit([TextEdit.Delete(caret, 1)], EditKind.Deleting, SelectionState.At(caret), SelectionState.At(caret));
            }
            else
            {
                var text = random.Next(15) == 0 ? "\r\n" : ((char)('a' + random.Next(26))).ToString();
                document.Edit([TextEdit.Insert(caret, text)], EditKind.Typing, SelectionState.At(caret), SelectionState.At(caret + text.Length));
                caret += text.Length;
            }
        }

        var final = TextOf(document);
        while (document.Undo() is not null)
        {
        }

        Check.Equal("seed text", TextOf(document), "undoing every group returns the first text");
        while (document.Redo() is not null)
        {
        }

        Check.Equal(final, TextOf(document), "redoing every group returns the last text");
    }

    [Test]
    public static void DirtyFollowsTheSavedState()
    {
        using var folder = new TempFolder();
        var path = folder.File("a.txt");
        File.WriteAllText(path, "abc");
        var clock = new FakeClock();
        var document = TextDocument.OpenAsync(path, Grouping).GetAwaiter().GetResult();
        int changes = 0;
        document.StateChanged += (_, _) => changes++;
        Check.True(!document.IsDirty, "a file just read is clean");
        document.Edit([TextEdit.Insert(3, "d")], EditKind.Typing, SelectionState.At(3), SelectionState.At(4));
        Check.True(document.IsDirty && changes == 1, "an edit makes it dirty and raises StateChanged");
        document.Undo();
        Check.True(!document.IsDirty, "undoing back to the saved text is clean");
        document.Redo();
        Check.True(document.IsDirty, "redo makes it dirty again");
        document.SaveAsync().GetAwaiter().GetResult();
        Check.True(!document.IsDirty, "saved: clean");
        Check.Equal("abcd", File.ReadAllText(path), "the file holds the text");
        document.Edit([TextEdit.Insert(4, "e")], EditKind.Typing, SelectionState.At(4), SelectionState.At(5));
        document.Undo();
        Check.True(!document.IsDirty, "typing after a save is a new group: undoing it is the saved state");
        document.Undo();
        Check.True(document.IsDirty, "undoing past the save is dirty");

        var fresh = TextDocument.Create("", Grouping, clock.Read);
        Check.True(!fresh.IsDirty, "a new empty document is clean");
        Type(fresh, clock, 0, "x");
        Check.True(fresh.IsDirty, "a new document with text is dirty");
        fresh.Undo();
        Check.True(!fresh.IsDirty, "a new document undone to empty is clean");
        Check.Throws<InvalidOperationException>(() => fresh.SaveAsync().GetAwaiter().GetResult(), "saving a document with no file and no path");
    }

    [Test]
    public static void EncodingsRoundTripByteForByte()
    {
        using var folder = new TempFolder();
        const string Text = "first line\r\nsecond é中 line\nthird\r\nالعربية";
        var cases = new (string Name, byte[] Bytes, int CodePage, bool Bom)[]
        {
            ("utf8", new UTF8Encoding(false).GetBytes(Text), 65001, false),
            ("utf8-bom", [0xEF, 0xBB, 0xBF, .. new UTF8Encoding(false).GetBytes(Text)], 65001, true),
            ("utf16le", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(Text)], 1200, true),
            ("utf16be", [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(Text)], 1201, true),
        };
        foreach (var (name, bytes, codePage, bom) in cases)
        {
            var path = folder.File(name + ".txt");
            File.WriteAllBytes(path, bytes);
            var document = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
            Check.Equal(Text, TextOf(document), $"{name}: the text read");
            Check.Equal(codePage, document.Encoding.CodePage, $"{name}: the encoding detected");
            Check.Equal(bom, document.HasBom, $"{name}: the byte order mark detected");
            Check.Equal("\r\n", document.LineEnding, $"{name}: the dominant line ending");
            Check.True(!document.DecodedWithFallback, $"{name}: no fallback");
            document.SaveAsync().GetAwaiter().GetResult();
            Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), $"{name}: saved byte for byte, mixed line endings kept");

            document.Edit([TextEdit.Insert(0, "ü")], EditKind.Command, SelectionState.At(0), SelectionState.At(1));
            document.SaveAsync().GetAwaiter().GetResult();
            var again = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
            Check.Equal("ü" + Text, TextOf(again), $"{name}: an edited save reads back");
            Check.Equal(codePage, again.Encoding.CodePage, $"{name}: an edited save keeps the encoding");
            Check.Equal(bom, again.HasBom, $"{name}: an edited save keeps the mark");
        }

        var lf = folder.File("lf.txt");
        File.WriteAllBytes(lf, "a\nb\nc\r\nd"u8.ToArray());
        Check.Equal("\n", TextDocument.OpenAsync(lf).GetAwaiter().GetResult().LineEnding, "mostly \\n: the line ending is \\n");
    }

    [Test]
    public static void InvalidUtf8FallsBackToLatin1()
    {
        using var folder = new TempFolder();
        var path = folder.File("latin1.txt");
        byte[] bytes = [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n', 0xFF, 0x80];
        File.WriteAllBytes(path, bytes);
        var document = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
        Check.True(document.DecodedWithFallback, "invalid UTF-8 with no mark says it fell back");
        Check.Equal("café\nÿ\u0080", TextOf(document), "each byte is one Latin-1 character");
        document.SaveAsync().GetAwaiter().GetResult();
        Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "saved back byte for byte");

        document.Edit([TextEdit.Insert(0, "中")], EditKind.Command, SelectionState.At(0), SelectionState.At(1));
        var error = Check.Throws<InvalidOperationException>(() => document.SaveAsync().GetAwaiter().GetResult(), "a character Latin-1 cannot hold");
        Check.True(error.Message.Contains("cannot hold", StringComparison.Ordinal), "the error says the encoding cannot hold it");
        Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "a failed save leaves the file as it was");
        Check.Equal(1, Directory.GetFiles(folder.Path).Length, "a failed save leaves no temporary file");
        document.SetEncoding(new UTF8Encoding(false), byteOrderMark: false);
        document.SaveAsync().GetAwaiter().GetResult();
        Check.Equal("中café\nÿ\u0080", File.ReadAllText(path, Encoding.UTF8), "saved as UTF-8 once the encoding is changed");
    }

    [Test]
    public static void SaveIsAtomicAndLeavesNoTemporaryFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("atomic.cs");
        File.WriteAllText(path, "old");
        var document = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
        var big = new string('x', 200_000) + "\n";
        document.Edit([new TextEdit(0, 3, big)], EditKind.Paste, SelectionState.At(0), SelectionState.At(big.Length));
        document.SaveAsync().GetAwaiter().GetResult();
        Check.Equal(big, File.ReadAllText(path), "the saved file holds the whole text (several blocks)");
        Check.SequenceEqual(["atomic.cs"], Directory.GetFiles(folder.Path).Select(Path.GetFileName).ToList(), "only the file is left in the folder");

        var other = folder.File("copy.cs");
        document.SaveAsync(other).GetAwaiter().GetResult();
        Check.Equal(other, document.FilePath, "saving to a path makes it the document's file");
        Check.True(!document.IsDirty, "and clean");
    }
}
