// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Workspaces;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>Autosave copies: written from a dirty document, listed after a "crash", restored, discarded, deleted on save and close.</summary>
[TestGroup("Recovery")]
public static class RecoveryTests
{
    private static string TextOf(TextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    [Test]
    public static void ACopyIsWrittenListedRestoredAndDiscarded()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { RecoveryFolder = folder.File("recovery"), AutosaveSeconds = 0 };
        var path = folder.File("work.cs");
        File.WriteAllBytes(path, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("class A {}\r\n")]);
        var document = TextDocument.OpenAsync(path, options).GetAwaiter().GetResult();
        var store = new RecoveryStore(options);
        store.Track(document);
        store.WriteNowAsync().GetAwaiter().GetResult();
        Check.Equal(0, Directory.GetFiles(store.Folder).Length, "a clean document has no copy");

        document.Edit([TextEdit.Insert(10, " // edited")], EditKind.Paste, SelectionState.At(10), SelectionState.At(20));
        store.WriteNowAsync().GetAwaiter().GetResult();
        Check.Equal(2, Directory.GetFiles(store.Folder).Length, "a dirty document has a copy and its sidecar");
        Check.Equal(0, store.ListRecoverable().Count, "the store does not offer the copies of documents it still tracks");
        Check.True(File.ReadAllBytes(path).AsSpan(2).SequenceEqual(Encoding.Unicode.GetBytes("class A {}\r\n")), "autosave never writes the file itself");

        // A crash: the next start's store finds the copy.
        var next = new RecoveryStore(options);
        var entries = next.ListRecoverable();
        Check.Equal(1, entries.Count, "the next start lists the copy");
        var entry = entries[0];
        Check.Equal(path, entry.OriginalPath, "the sidecar names the original file");
        Check.Equal(1200, entry.CodePage, "the sidecar keeps the encoding");
        Check.True(entry.HasBom, "and the byte order mark");
        var restored = next.Restore(entry);
        Check.Equal("class A {} // edited\r\n", TextOf(restored), "restored text");
        Check.Equal(path, restored.FilePath, "restored as the original file");
        Check.True(restored.IsDirty, "a restored document is dirty until saved");
        Check.Equal(1200, restored.Encoding.CodePage, "restored with its encoding");
        next.Discard(entry);
        Check.Equal(0, next.ListRecoverable().Count, "a discarded copy is gone");
        store.Dispose();
        next.Dispose();
    }

    [Test]
    public static void TheCopyGoesOnSaveAndOnClose()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { RecoveryFolder = folder.File("recovery"), AutosaveSeconds = 0 };
        using var store = new RecoveryStore(options);
        var saved = TextDocument.Create("", options);
        store.Track(saved);
        saved.Edit([TextEdit.Insert(0, "new file")], EditKind.Typing, SelectionState.At(0), SelectionState.At(8));
        store.WriteNowAsync().GetAwaiter().GetResult();
        Check.Equal(2, Directory.GetFiles(store.Folder).Length, "a new document with text has a copy");
        saved.SaveAsync(folder.File("saved.txt")).GetAwaiter().GetResult();
        store.WriteNowAsync().GetAwaiter().GetResult();
        Check.Equal(0, Directory.GetFiles(store.Folder).Length, "saving deletes the copy");

        var closed = TextDocument.Create("", options);
        store.Track(closed);
        closed.Edit([TextEdit.Insert(0, "draft")], EditKind.Typing, SelectionState.At(0), SelectionState.At(5));
        store.WriteNowAsync().GetAwaiter().GetResult();
        Check.Equal(2, Directory.GetFiles(store.Folder).Length, "a draft has a copy");
        closed.Dispose();
        Check.Equal(0, Directory.GetFiles(store.Folder).Length, "closing deletes the copy");
    }

    [Test]
    public static void TheTimerWritesTheCopy()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { RecoveryFolder = folder.File("recovery"), AutosaveSeconds = 1 };
        using var store = new RecoveryStore(options);
        using var document = TextDocument.Create("", options);
        store.Track(document);
        document.Edit([TextEdit.Insert(0, "autosaved")], EditKind.Typing, SelectionState.At(0), SelectionState.At(9));
        var until = DateTime.UtcNow.AddSeconds(10);
        while (Directory.GetFiles(store.Folder, "*.txt").Length == 0 && DateTime.UtcNow < until)
        {
            Thread.Sleep(100);
        }

        var copies = Directory.GetFiles(store.Folder, "*.txt");
        Check.Equal(1, copies.Length, "the timer wrote a copy within a few seconds");
        Check.Equal("autosaved", File.ReadAllText(copies[0]), "the copy holds the text");
    }
}

/// <summary>A workspace's files, listed lazily without the ignored folders, and its open documents.</summary>
[TestGroup("Workspace")]
public static class WorkspaceTests
{
    [Test]
    public static void FilesSkipTheIgnoredFolders()
    {
        using var folder = new TempFolder();
        foreach (var file in new[] { "a.cs", "src/b.cs", "src/deep/c.cs", "bin/x.dll", "obj/y.json", ".git/HEAD", ".vs/s", "node_modules/m.js", "test-output/t.txt", "src/obj/z.cs", "Docs/BIN/k.txt" })
        {
            var full = folder.File(file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, file);
        }

        using var workspace = new Workspace(folder.Path);
        var files = workspace.EnumerateFiles().Select(f => Path.GetRelativePath(folder.Path, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Check.SequenceEqual(["a.cs", "src/b.cs", "src/deep/c.cs"], files, "the default ignore list skips bin, obj, .git, .vs, node_modules and test-output, at any depth, ignoring case");
        Check.True(workspace.Contains(folder.File("src/b.cs")) && !workspace.Contains(folder.File("src/obj/z.cs")), "Contains follows the ignore list");

        var options = CraftOptions.FromVariables(n => n == "CRAFT_WORKSPACE_IGNORE" ? "src; docs" : null);
        using var narrower = new Workspace(folder.Path, options);
        var others = narrower.EnumerateFiles().Select(f => Path.GetRelativePath(folder.Path, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Check.SequenceEqual([".git/HEAD", ".vs/s", "a.cs", "bin/x.dll", "node_modules/m.js", "obj/y.json", "test-output/t.txt"], others, "CRAFT_WORKSPACE_IGNORE replaces the list");

        using (var lazy = workspace.EnumerateFiles().GetEnumerator())
        {
            Check.True(lazy.MoveNext(), "the listing yields as it goes");
        }
    }

    [Test]
    public static void OneDocumentPerFile()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("a.cs"), "class A {}");
        using var workspace = new Workspace(folder.Path);
        var first = workspace.OpenAsync("a.cs").GetAwaiter().GetResult();
        var second = workspace.OpenAsync(folder.File("a.cs")).GetAwaiter().GetResult();
        Check.True(ReferenceEquals(first, second), "the same file opens the same document, by a relative or a full path");
        Check.True(ReferenceEquals(first, workspace.Find("a.cs")), "Find returns the open document");
        Check.Equal(1, workspace.Documents.Count, "one open document");
        Check.True(workspace.Close("a.cs"), "Close closes it");
        Check.True(workspace.Find("a.cs") is null && workspace.Documents.Count == 0, "and it is no longer open");
        Check.Throws<DirectoryNotFoundException>(() => _ = new Workspace(folder.File("missing")), "a folder that does not exist");
    }
}
