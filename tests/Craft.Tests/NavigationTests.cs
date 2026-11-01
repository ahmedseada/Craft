// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 9, item 13: bookmarks toggled at the caret (both keymaps' keys), drawn in the gutter through the line marks,
/// moved with edits (and merged), gone through across files, listed in their view, cleared with Undo, kept in the
/// workspace's file and read back.
/// </summary>
[TestGroup("Bookmarks")]
public static class BookmarkTests
{
    [Test]
    public static async Task ToggleDrawsAGutterMarkMovesWithEditsAndMergesWhenLinesMeet()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var tab = (await shell.OpenFileAsync(fixture.Write("a.txt", "zero\none\ntwo\nthree\nfour\n")))!;
        var editor = fixture.EditorOf(tab);
        var (first, then) = KeyBinding.ParseKeys("Ctrl+K Ctrl+K");
        Check.True(EditorKeyBindings.Find(first, then!.Value) is { CommandId: BookmarkCommands.Toggle }, "Ctrl+K Ctrl+K toggles a bookmark");
        Check.Equal("Ctrl+K Ctrl+N", fixture.Host.KeysOf(BookmarkCommands.Next), "next bookmark's keys");

        editor.Select(10, 10);                                          // line 2 ("two")
        Check.True(await fixture.Host.ExecuteCommandAsync(BookmarkCommands.Toggle), "the toggle runs");
        Check.True(shell.Bookmarks.Has(tab.FilePath!, 2), "line 2 is bookmarked");
        editor.Select(19, 19);                                          // line 4 ("four")
        Check.True(shell.ToggleBookmark(), "a second bookmark");
        var marks = editor.LineMarks.ToArray();
        Check.True(marks is [{ Line: 2, Kind: LineMarkKind.Bookmark }, { Line: 4, Kind: LineMarkKind.Bookmark }], "the gutter's marks: " + string.Join(",", marks));
        using (var renderer = new EditorRenderer())
        {
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, editor, 0, 0, 800, 400, caretVisible: false);
            Check.Equal(2, renderer.LineMarksDrawn, "both bookmarks drawn in the gutter");
            Check.True(canvas.Calls.Count(c => c.Kind == "fill" && c.Argb == editor.Theme.Colors.Accent && c.X == 1) == 2, "as the accent's tag at the gutter's left edge");
        }

        // Two lines typed above the first: both move down with their text.
        editor.Select(0, 0);
        editor.OnText("new\nnew\n");
        Check.SequenceEqual([4, 6], shell.Bookmarks.Of(tab.FilePath!).Select(b => b.Line).ToArray(), "inserted lines above move them down");
        Check.True(editor.LineMarks.ToArray() is [{ Line: 4 }, { Line: 6 }], "and their marks");
        Check.Equal("two", shell.Bookmarks.Of(tab.FilePath!)[0].Text, "a bookmark keeps its line's text");

        // Lines 4 to 6 deleted at once: the bookmarks inside go to where the deletion began and become one.
        var snapshot = tab.Document.Buffer.Current;
        int from = snapshot.GetLine(4).Start;
        int to = snapshot.GetLine(6).Start;
        tab.Document.Edit([new TextEdit(from, to - from, "")], EditKind.Command, SelectionState.At(from), SelectionState.At(from));
        var left = shell.Bookmarks.Of(tab.FilePath!);
        Check.True(left is [{ Line: 4 }], "two bookmarks that met are one: " + string.Join(",", left.Select(b => b.Line)));

        // A toggle on a bookmarked line takes it away.
        editor.Select(tab.Document.Buffer.Current.GetLine(4).Start, tab.Document.Buffer.Current.GetLine(4).Start);
        Check.True(!shell.ToggleBookmark() && shell.Bookmarks.Count == 0 && editor.LineMarks.Length == 0, "toggled off: gone from the list and the gutter");
    }

    [Test]
    public static async Task NextAndPreviousGoAcrossFilesAndWrap()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var a = fixture.Write("a.txt", "a0\na1\na2\na3\n");
        var b = fixture.Write("b.txt", "b0\nb1\nb2\n");
        shell.Bookmarks.Toggle(a, 1);
        shell.Bookmarks.Toggle(a, 3);
        shell.Bookmarks.Toggle(b, 2);
        var tab = (await shell.OpenFileAsync(a))!;
        var editor = fixture.EditorOf(tab);
        editor.Select(0, 0);
        fixture.Host.UpdateSelection(tab.HostDocument, editor.Selection);

        var at = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            Check.True(await shell.GoToBookmarkAsync(forward: true), "next runs");
            var active = shell.Tabs.Active!;
            fixture.EditorOf(active).Select(active.HostDocument.Selection.Anchor, active.HostDocument.Selection.Caret);
            at.Add($"{Path.GetFileName(active.FilePath)}:{active.Document.Buffer.Current.GetLineIndex(active.HostDocument.Selection.Caret)}");
        }

        Check.SequenceEqual(["a.txt:1", "a.txt:3", "b.txt:2", "a.txt:1"], at, "next goes through a's then b's and wraps to the first");
        Check.True(await shell.GoToBookmarkAsync(forward: false), "previous runs");
        var back = shell.Tabs.Active!;
        Check.Equal("b.txt", Path.GetFileName(back.FilePath), "previous from the first wraps to the last");
    }

    [Test]
    public static async Task TheViewListsLabelsOpensAndClearsWithUndoAndTheFileKeepsThem()
    {
        using var folder = new TempFolder();
        string bookmarksFile;
        {
            using var fixture = new SplitFixture(folder);
            var shell = await fixture.OpenWorkspaceAsync();
            var path = fixture.Write("src/a.txt", "one\ntwo\nthree\n");
            var tab = (await shell.OpenFileAsync(path))!;
            fixture.EditorOf(tab).Select(4, 4);
            shell.ToggleBookmark();
            Check.True(shell.Bookmarks.SetLabel(path, 1, "  the second  "), "labelled");
            var results = shell.BookmarksView.Blocks.OfType<PanelResults>().Single();
            Check.True(results.Groups is [{ Title: "a.txt", Detail: "src", Rows: [{ Prefix: "2", Text: "two", Detail: "the second" }] }],
                "the view: a group per file (its folder), a row per bookmark (line, text, label)");
            var row = results.Groups[0].Rows[0];
            Check.Equal("1", shell.BookmarksView.Badge, "the rail's count");
            Check.True(fixture.Host.Panels.Any(p => p.Id == ShellModel.BookmarksPanelId), "the view is a host panel (a side view of the rail)");

            await shell.BookmarksSaved;
            bookmarksFile = Path.Combine(fixture.Workspace, ".craft", BookmarksFile.FileName);
            var text = await File.ReadAllTextAsync(bookmarksFile);
            Check.True(text.Contains("\"file\": \"src/a.txt\"", StringComparison.Ordinal) && text.Contains("\"line\": 2", StringComparison.Ordinal)
                && text.Contains("\"label\": \"the second\"", StringComparison.Ordinal), "kept in .craft/bookmarks.json, relative, lines from 1: " + text);

            // The view's row opens the place; Clear all has Undo.
            fixture.EditorOf(tab).Select(0, 0);
            await shell.BookmarksView.ActAsync("bookmarks.open", row.Value);
            Check.Equal(4, tab.HostDocument.Selection.Caret, "the row's click puts the caret on its line");
            Check.Equal(1, shell.ClearBookmarks(), "clear all takes one");
            Check.True(shell.Bookmarks.Count == 0 && shell.UndoBar.Current is not null, "cleared, with Undo");
            Check.True(shell.UndoBar.Undo() && shell.Bookmarks.Has(path, 1), "Undo brings it back");
            await shell.BookmarksSaved;
        }

        // A new shell on the same folder reads them back.
        using var again = new SplitFixture(folder);
        var reopened = await again.OpenWorkspaceAsync();
        await reopened.BookmarksLoaded;
        Check.True(reopened.Bookmarks.All() is [{ Line: 1, Label: "the second", Text: "two" }], "read back from the file: " + string.Join(",", reopened.Bookmarks.All()));
        var opened = (await reopened.OpenFileAsync(Path.Combine(again.Workspace, "src", "a.txt")))!;
        Check.True(again.EditorOf(opened).LineMarks.ToArray() is [{ Line: 1, Kind: LineMarkKind.Bookmark }], "and drawn when its file opens");
    }

    [Test]
    public static async Task TheVisualStudioKeymapTogglesWithCtrlF2()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_KEYMAP" ? "visualstudio" : null);
        Check.Equal("visualstudio", fixture.Shell.Options.KeyMap, "the keymap is chosen");
        var keyMap = StandardShortcuts.VisualStudioKeyMap.Bindings;
        Check.True(keyMap.Any(b => b.DisplayText == "Ctrl+F2" && b.CommandId == BookmarkCommands.Toggle), "Visual Studio's keymap: Ctrl+F2 toggles");
        Check.True(keyMap.Any(b => b.DisplayText == "Ctrl+K Ctrl+W" && b.CommandId == BookmarkCommands.Show), "the bookmarks window: Ctrl+K Ctrl+W");
        Check.True(keyMap.Any(b => b.DisplayText == "Ctrl+K Ctrl+L" && b.CommandId == BookmarkCommands.Clear), "clear: Ctrl+K Ctrl+L");
        await Task.CompletedTask;
    }
}

/// <summary>
/// X part 9, item 14: a single click in the explorer opens an italic preview tab the next preview replaces; an edit, a
/// double click, a pin or Keep open keeps it; quick open never previews; off with <c>CRAFT_PREVIEW_TABS</c>.
/// </summary>
[TestGroup("PreviewTabs")]
public static class PreviewTabTests
{
    [Test]
    public static async Task ThePreviewIsReplacedUntilAnEditADoubleClickOrAPinKeepsIt()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var kept = (await shell.OpenFileAsync(fixture.Write("kept.txt", "k")))!;
        Check.True(!kept.IsPreview, "quick open, Ctrl+O and the other ways open a kept tab");

        var a = (await shell.OpenPreviewAsync(fixture.Write("a.txt", "a")))!;
        Check.True(a.IsPreview && ReferenceEquals(shell.Tabs.Active, a) && shell.Tabs.Preview == a, "a single click: a preview, active");
        var b = (await shell.OpenPreviewAsync(fixture.Write("b.txt", "b")))!;
        Check.True(b.IsPreview && !shell.Tabs.Contains(a) && shell.Tabs.Tabs.Count == 2 && shell.Tabs.IndexOf(b) == 1,
            "the next preview takes the preview's place: " + string.Join(",", shell.Tabs.Tabs.Select(t => t.Title)));
        Check.True(fixture.Host.Documents.Count == 2, "the replaced preview's document is closed");

        // An edit keeps it: the next preview opens beside it.
        fixture.EditorOf(b).OnText("x");
        Check.True(!b.IsPreview, "an edit keeps the preview");
        var c = (await shell.OpenPreviewAsync(fixture.Write("c.txt", "c")))!;
        Check.True(c.IsPreview && shell.Tabs.Contains(b) && shell.Tabs.Tabs.Count == 3, "a kept tab is not replaced");

        // A double click (on the tab, or on the file in the explorer) keeps it.
        Check.True(shell.KeepTab(c) && !c.IsPreview, "a double click on the tab keeps it");
        var d = (await shell.OpenPreviewAsync(fixture.Write("d.txt", "d")))!;
        var e = await shell.OpenKeptAsync(d.FilePath!);
        Check.True(ReferenceEquals(d, e) && !d.IsPreview && shell.Tabs.Tabs.Count == 4, "a double click on the previewed file keeps its tab (not a second one)");

        // A pin keeps it; Keep open by its command.
        var f = (await shell.OpenPreviewAsync(fixture.Write("f.txt", "f")))!;
        shell.Tabs.SetPinned(f, true);
        Check.True(!f.IsPreview, "pinning keeps it");
        var g = (await shell.OpenPreviewAsync(fixture.Write("g.txt", "g")))!;
        Check.True(await fixture.Host.ExecuteCommandAsync(NavigationCommands.KeepTab) && !g.IsPreview, "Tab: Keep open (Ctrl+K Enter) keeps the active preview");
        Check.Equal("Ctrl+K Enter", fixture.Host.KeysOf(NavigationCommands.KeepTab), "its keys");

        // A file already open is only shown, never turned into a preview.
        var again = await shell.OpenPreviewAsync(kept.FilePath!);
        Check.True(ReferenceEquals(again, kept) && !kept.IsPreview && ReferenceEquals(shell.Tabs.Active, kept), "an open file is shown as it is");
    }

    [Test]
    public static async Task OffEveryClickKeepsItsTab()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_PREVIEW_TABS" ? "false" : null);
        var shell = await fixture.OpenWorkspaceAsync();
        var a = (await shell.OpenPreviewAsync(fixture.Write("a.txt", "a")))!;
        var b = (await shell.OpenPreviewAsync(fixture.Write("b.txt", "b")))!;
        Check.True(!a.IsPreview && !b.IsPreview && shell.Tabs.Tabs.Count == 2, "CRAFT_PREVIEW_TABS off: a click opens a kept tab");
    }

    [Test]
    public static async Task APreviewInAnotherGroupSharesItsDocument()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var a = (await shell.OpenFileAsync(fixture.Write("a.txt", "a")))!;
        await shell.OpenFileAsync(fixture.Write("x.txt", "x"));
        shell.Split(SplitDirection.Right);                       // the right group shows x
        var other = (await shell.OpenPreviewAsync(fixture.Write("b.txt", "b")))!;
        var shared = (await shell.OpenPreviewAsync(a.FilePath!))!;
        Check.True(shared.IsPreview && ReferenceEquals(shared.Document, a.Document) && !shell.Tabs.Contains(other),
            "a file open in another group previews as a shared tab, replacing this group's preview");
        Check.Equal(2, fixture.Host.Documents.Count, "b's document was closed with its preview; a and x are one document each");
    }
}

/// <summary>
/// X part 9, item 15: the last session written per workspace in the data folder (groups, tabs, active tabs, pins, carets,
/// scroll, folds) and reopened: gone files skipped and said once, a file with a recovery copy left to its recovery, off with
/// <c>CRAFT_RESTORE_SESSION</c>, the workspace's piece 3a layout read when no session is kept.
/// </summary>
[TestGroup("SessionRestore")]
public static class SessionRestoreTests
{
    [Test]
    public static async Task GroupsTabsPinsCaretsScrollAndFoldsComeBackAndGoneFilesAreSaidOnce()
    {
        using var folder = new TempFolder();
        string a, b, c, gone;
        {
            using var fixture = new SplitFixture(folder);
            var shell = await fixture.OpenWorkspaceAsync();
            a = fixture.Write("a.txt", string.Concat(Enumerable.Range(0, 200).Select(i => $"line {i}\n")));
            b = fixture.Write("b.txt", "b\n");
            c = fixture.Write("c.txt", "c\n");
            gone = fixture.Write("gone.txt", "gone\n");
            var tabA = (await shell.OpenFileAsync(a))!;
            var tabB = (await shell.OpenFileAsync(b))!;
            await shell.OpenFileAsync(gone);
            shell.Tabs.SetPinned(tabB, true);
            shell.Tabs.Activate(tabA);
            var editor = fixture.EditorOf(tabA);
            editor.Folding.SetRanges([new FoldingRange(10, 20), new FoldingRange(30, 40)]);
            Check.True(editor.Folding.Toggle(30), "a fold");
            int caret = tabA.Document.Buffer.Current.GetLine(120).Start + 3;
            editor.Select(caret, caret);
            editor.UpdateViewport(0, 900, 800, 400);
            var right = shell.Split(SplitDirection.Right)!;
            await shell.OpenInGroupAsync(right, c);
            Check.True(shell.SessionToWrite, "a folder is open: the close writes the session");
            await shell.SaveSessionAsync();
            var file = ShellModel.SessionPathOf(shell.Options, fixture.Workspace);
            Check.True(File.Exists(file) && file.StartsWith(folder.File("obj"), StringComparison.Ordinal) && !File.Exists(Path.Combine(fixture.Workspace, ".craft", "session.json")),
                "the session is in the data folder, never the workspace: " + file);
        }

        File.Delete(gone);
        using var again = new SplitFixture(folder);
        var restored = await again.OpenWorkspaceAsync();
        var result = await restored.RestoreSessionAsync();
        Check.True(result is { Opened: 4, Missing: [var missing], Recovered: [] } && missing.EndsWith("gone.txt", StringComparison.Ordinal),
            $"four tabs back (c in both counts once per group), the gone file skipped: {result}");
        var groups = restored.Groups.Groups;
        Check.Equal(2, groups.Count, "two groups");
        Check.SequenceEqual(["b.txt", "a.txt", "a.txt"], groups[0].Tabs.Tabs.Select(t => t.Title).Concat(groups[1].Tabs.Tabs.Take(1).Select(t => t.Title)).ToArray(),
            "the first group's tabs in order (the pinned b first), the right group showing a (its split) then c");
        Check.True(groups[0].Tabs.Tabs[0].IsPinned && !groups[0].Tabs.Tabs[1].IsPinned, "the pin came back");
        Check.True(ReferenceEquals(restored.Groups.Focused, groups[1]) && groups[1].Tabs.Active!.Title == "c.txt", "the focus and the right group's active tab as they were");
        var tab = groups[0].Tabs.Active!;
        Check.Equal("a.txt", tab.Title, "the first group's active tab");
        Check.Equal(1, again.Host.Notifications.Entries.Count(n => n.Text.Contains("gone.txt", StringComparison.Ordinal)), "the gone file is said once");

        // Its editor made: the caret now, the scroll once it has a viewport, the folds once its ranges arrive.
        var editorAgain = again.EditorOf(tab);
        float requested = -1;
        editorAgain.ScrollRequested += (_, e) => requested = e.Y;
        editorAgain.UpdateViewport(0, 0, 0, 0);
        restored.ApplyViewState(tab, editorAgain);
        Check.Equal(120, editorAgain.CaretLine, "the caret's line");
        editorAgain.UpdateViewport(0, 0, 800, 400);
        Check.True(Math.Abs(requested - 900) < 0.5f, $"the scroll asked once the viewport is known ({requested})");
        Check.True(tab.PendingView is not null, "the folds wait for the ranges");
        editorAgain.Folding.SetRanges([new FoldingRange(10, 20), new FoldingRange(30, 40)]);
        Check.True(editorAgain.Folding.IsFolded(30) && !editorAgain.Folding.IsFolded(10) && tab.PendingView is null, "the fold came back with its ranges");
        Check.True(restored.TabStateOf(tab) is { Folds: [30], Selection.Caret: var kept } && kept == editorAgain.Selection.Caret, "the state captured again from the editor");
    }

    [Test]
    public static async Task AFileWithARecoveryCopyIsLeftToItsRecovery()
    {
        using var folder = new TempFolder();
        var recovery = folder.File("recovery");
        Func<string, string?> variables = name => name == "CRAFT_RECOVERY_FOLDER" ? recovery : null;
        string edited;
        {
            using var fixture = new SplitFixture(folder, variables);
            var shell = await fixture.OpenWorkspaceAsync();
            edited = fixture.Write("edited.txt", "saved\n");
            await shell.OpenFileAsync(edited);
            await shell.OpenFileAsync(fixture.Write("plain.txt", "plain\n"));
            await shell.SaveSessionAsync();
        }

        // A crash left unsaved changes of edited.txt (a copy in the recovery folder).
        using var store = new RecoveryStore(new CraftOptions { RecoveryFolder = recovery, AutosaveSeconds = 0 });
        using var document = await TextDocument.OpenAsync(edited, new CraftOptions());
        using var tracked = store.Track(document);
        document.Edit([new TextEdit(0, 0, "unsaved ")], EditKind.Typing, default, default);
        await store.WriteNowAsync();
        Check.True(RecoveryStore.RecoveredFiles(recovery) is [var copy] && copy == Path.GetFullPath(edited), "the copy names its file");

        using var again = new SplitFixture(folder, variables);
        var shell2 = await again.OpenWorkspaceAsync();
        var result = await shell2.RestoreSessionAsync();
        Check.True(result is { Opened: 1, Recovered: [_] } && shell2.Groups.FindOpen(edited) is null && shell2.Tabs.Tabs is [{ Title: "plain.txt" }],
            $"the file with a recovery copy is not reopened past its recovery: {result}");
        Check.True(again.Host.Notifications.Entries.Any(n => n.Text.Contains("recovery copy", StringComparison.Ordinal)), "and said");
    }

    [Test]
    public static async Task OffNothingIsWrittenOrRestoredAndTheWorkspaceLayoutStandsInForAMissingSession()
    {
        using var folder = new TempFolder();
        {
            using var off = new SplitFixture(folder, name => name == "CRAFT_RESTORE_SESSION" ? "false" : null);
            var shell = await off.OpenWorkspaceAsync();
            await shell.OpenFileAsync(off.Write("a.txt", "a"));
            Check.True(!shell.SessionToWrite, "off: the close writes nothing");
            await shell.SaveSessionAsync();
            Check.True(!File.Exists(ShellModel.SessionPathOf(shell.Options, off.Workspace)), "no session file");
            Check.True(await shell.RestoreSessionAsync() is null, "and nothing restored");

            // The split editor's layout (piece 3a) is kept in the workspace once split.
            shell.Split(SplitDirection.Right);
            await shell.LayoutSaved;
        }

        using var on = new SplitFixture(folder);
        var reopened = await on.OpenWorkspaceAsync();
        var result = await reopened.RestoreSessionAsync();
        Check.True(result is { Opened: 2 } && reopened.Groups.Groups.Count == 2, $"no session kept: the workspace's layout reopens the groups ({result})");
        Check.True(await reopened.RestoreSessionAsync() is null, "a second restore into open tabs does nothing");
    }
}

/// <summary>
/// X part 9, item 16: the local history's snapshots (one per save, deduplicated by content hash, bounded by size, age and
/// file size, memory flat over many saves) and the shell's Timeline (listed per file, restored as one undo step).
/// </summary>
[TestGroup("LocalHistory")]
public static class LocalHistoryTests
{
    [Test]
    public static async Task ASnapshotPerSaveDeduplicatedByContent()
    {
        using var folder = new TempFolder();
        var clock = new SteppingClock();
        var history = new LocalHistory(folder.File("history"), new CraftOptions(), clock);
        var file = folder.File("a.cs");
        var first = await history.AddAsync(file, Snapshot("one"));
        clock.Step(TimeSpan.FromSeconds(1));
        Check.True(first is { Bytes: 3 } && await history.AddAsync(file, Snapshot("one")) is null, "the same text again keeps nothing new");
        clock.Step(TimeSpan.FromSeconds(1));
        var second = await history.AddAsync(file, Snapshot("two"));
        clock.Step(TimeSpan.FromSeconds(1));
        var third = await history.AddAsync(file, Snapshot("one"));
        var entries = history.Entries(file);
        Check.True(entries.Count == 2 && entries[0].Hash == first!.Hash && entries[0].Time == third!.Time && entries[1].Hash == second!.Hash,
            "a save back to an older text moves that snapshot to now (two files, not three): " + string.Join(",", entries.Select(e => e.Hash)));
        Check.Equal("one", LocalHistory.Read(entries[0]), "a snapshot's text");
        Check.True((await File.ReadAllTextAsync(Path.Combine(history.FolderOf(file), LocalHistory.SourceFileName))) == Path.GetFullPath(file), "each file's folder names its file");
        Check.Equal(6L, history.KeptBytes, "the bytes kept in all");
        var unicode = await history.AddAsync(file, Snapshot("é😀"));
        Check.True(unicode is { Bytes: 6 } && LocalHistory.Read(unicode) == "é😀", "UTF-8 bytes, read back the same");
    }

    [Test]
    public static async Task TheBoundsDropTheOldestTheExpiredAndTheTooLarge()
    {
        using var folder = new TempFolder();
        var clock = new SteppingClock();
        var options = new CraftOptions { LocalHistoryMaxMegabytes = 1, LocalHistoryDays = 30, LocalHistoryFileMegabytes = 1 };
        var history = new LocalHistory(folder.File("history"), options, clock);
        var text = new string('x', 300 * 1024);
        for (int i = 0; i < 5; i++)
        {
            clock.Step(TimeSpan.FromMinutes(1));
            Check.True(await history.AddAsync(folder.File($"f{i}.txt"), Snapshot(text + i)) is not null, $"snapshot {i}");
        }

        Check.True(history.KeptBytes <= 1024 * 1024 && history.Entries(folder.File("f0.txt")).Count == 0 && history.Entries(folder.File("f4.txt")).Count == 1,
            $"past CRAFT_LOCAL_HISTORY_MAX_MB the oldest go ({history.KeptBytes:N0} bytes kept)");
        Check.True(await history.AddAsync(folder.File("big.txt"), Snapshot(new string('y', (1024 * 1024) + 1))) is null, "a file past CRAFT_LOCAL_HISTORY_FILE_MB is not kept");

        clock.Step(TimeSpan.FromDays(31));
        await history.AddAsync(folder.File("new.txt"), Snapshot("fresh"));
        Check.True(history.Entries(folder.File("f4.txt")).Count == 0 && history.Entries(folder.File("new.txt")).Count == 1 && history.KeptBytes == 5,
            "past CRAFT_LOCAL_HISTORY_DAYS the old ones go");
        history.Options = options with { LocalHistoryMaxMegabytes = 0 };
        Check.True(!history.IsOn && await history.AddAsync(folder.File("new.txt"), Snapshot("off")) is null, "0 MB turns it off");
    }

    [Test]
    public static async Task MemoryStaysFlatOverManySaves()
    {
        using var folder = new TempFolder();
        var history = new LocalHistory(folder.File("history"), new CraftOptions { LocalHistoryMaxMegabytes = 1 }, new SteppingClock());
        var file = folder.File("a.txt");
        var body = new string('z', 4096);
        for (int i = 0; i < 20; i++)
        {
            await history.AddAsync(file, Snapshot(body + i));
        }

        long before = GC.GetTotalMemory(forceFullCollection: true);
        for (int i = 20; i < 520; i++)
        {
            await history.AddAsync(file, Snapshot(body + i));
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        Check.True(after - before < 256 * 1024, $"500 more saves keep the heap flat ({after - before:N0} bytes)");
        Check.True(history.KeptBytes <= 1024 * 1024, $"and the disk within its bound ({history.KeptBytes:N0} bytes)");
    }

    [Test]
    public static async Task ASaveKeepsASnapshotTheTimelineListsItAndRestoreIsOneUndoStep()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var tab = (await shell.OpenFileAsync(fixture.Write("a.txt", "first\n")))!;
        var editor = fixture.EditorOf(tab);
        editor.Select(0, 0);
        editor.OnText("one ");
        Check.True(await shell.SaveAsync(tab), "saved");
        var kept = await shell.HistorySaved;
        Check.True(kept is not null && kept.SnapshotPath.StartsWith(folder.File("obj"), StringComparison.Ordinal), "the save kept a snapshot in the data folder: " + kept?.SnapshotPath);
        editor.Select(tab.Document.Buffer.Current.Length, tab.Document.Buffer.Current.Length);
        editor.OnText("two\n");
        Check.True(await shell.SaveAsync(tab), "saved again");
        await shell.HistorySaved;
        await shell.TimelineRead;                                       // the Timeline reads the active file's snapshots again after the write
        Check.Equal(2, shell.Timeline.Count, "the Timeline lists the active file's two snapshots");
        var rows = shell.TimelineView.Blocks.OfType<PanelResults>().Single().Groups.Single().Rows;
        Check.True(rows.Count == 2 && rows[0].Detail is "14 B" && rows[1].Detail is "10 B" && rows[0].Buttons.Any(b => b.Title == "Restore"),
            "a row per snapshot: its time, its size, Compare and Restore: " + string.Join(",", rows.Select(r => r.Text + " " + r.Detail)));

        editor.OnText("unsaved");
        Check.True(await shell.RestoreSnapshotAsync(shell.Timeline[1]), "restored the first snapshot");
        Check.Equal("one first\n", tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length), "the snapshot's text");
        Check.True(tab.IsDirty && shell.UndoBar.Current is not null, "not saved, with Undo in the inline bar");
        Check.True(shell.UndoBar.Undo(), "Undo");
        Check.Equal("one first\ntwo\nunsaved", tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length), "one undo step brings the text back");
    }

    private static ITextSnapshot Snapshot(string text) => TextDocument.Create(text).Buffer.Current;

    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Step(TimeSpan by) => _now += by;
    }
}

/// <summary>
/// X part 9, item 17: file(line,col), file:line:col and file:line links found per line (a span scan, nothing allocated),
/// Windows paths, the negatives, a plug-in's detector, the paths resolved against the run's folder and the workspace, a
/// click in Output and a Ctrl+click in the terminal, and the underlines drawn with 0 bytes a warm frame.
/// </summary>
[TestGroup("OutputLinks")]
public static class OutputLinkTests
{
    private static (string Path, int Line, int Column)[] Links(string line)
    {
        var found = new OutputLink[16];
        int count = OutputLinkDetectors.FindAll(line, found);
        return [.. found.Take(count).Select(l => (line.Substring(l.PathStart, l.PathLength), l.Line, l.Column))];
    }

    [Test]
    public static void TheFormatsAndWindowsPathsAreFoundAndTheLookalikesAreNot()
    {
        Check.SequenceEqual([("src/A.cs", 12, 5)], Links("src/A.cs(12,5): error CS1002: ; expected [src/app.csproj]"), "MSBuild's path(line,column)");
        Check.SequenceEqual([(@"C:\Projects\App\Program.cs", 10, 3)], Links(@"C:\Projects\App\Program.cs(10,3,10,9): warning CS0168"), "a drive letter, MSBuild's range");
        Check.SequenceEqual([("lib/x.py", 7, 2)], Links("lib/x.py:7:2: E302 expected 2 blank lines"), "path:line:column");
        Check.SequenceEqual([("main.go", 9, 0)], Links("main.go:9 undefined: y"), "path:line");
        Check.SequenceEqual([("C:/x/y.ts", 4, 0)], Links("at C:/x/y.ts:4"), "a drive with forward slashes");
        Check.SequenceEqual([(@"\\server\share\a.cs", 2, 0)], Links(@"\\server\share\a.cs:2"), "a UNC path");
        Check.SequenceEqual([(@"D:\a\b.cs", 42, 0)], Links(@"   at App.Main() in D:\a\b.cs:line 42"), ".NET's stack frame");
        Check.SequenceEqual([("a.ts", 3, 0)], Links("a.ts(3)"), "path(line)");
        Check.SequenceEqual([("a.cs", 3, 0)], Links("error:a.cs:3"), "a colon before the path starts it");
        Check.SequenceEqual([("one.cs", 1, 0), ("two.cs", 2, 3)], Links("one.cs:1 and \"two.cs(2,3)\""), "two links in a line");
        foreach (var plain in (string[])["at 10:30:45", "Main(1)", "http://host:80/a.cs", "version 1.2:3", "file.cs: nothing", "a.cs(x)", "(12,5)"])
        {
            Check.True(Links(plain).Length == 0, $"no link in \"{plain}\": " + string.Join(",", Links(plain)));
        }
    }

    [Test]
    public static void AScanAllocatesNothingAndAPluginAddsAFormat()
    {
        var line = "src/A.cs(12,5): error CS1002 and lib/x.py:7:2 and main.go:9".AsSpan();
        Span<OutputLink> found = stackalloc OutputLink[8];
        for (int i = 0; i < 100; i++)
        {
            OutputLinkDetectors.FindAll(line, found);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int i = 0; i < 1000; i++)
        {
            count = OutputLinkDetectors.FindAll(line, found);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(count == 3 && bytes == 0, $"1,000 scans of a line with three links allocate nothing ({bytes:N0} bytes)");

        OutputLinkDetectors.Register(new PythonTraceLinks());
        try
        {
            var links = Links("  File \"app.py\", line 3, in main; also a.cs:2");
            Check.True(links is [("app.py", 3, 0), ("a.cs", 2, 0)], "a plug-in's detector joins the library's, in order: " + string.Join(",", links));
        }
        finally
        {
            OutputLinkDetectors.Unregister("python-trace");
        }

        Check.Throws<NotRegisteredException>(() => OutputLinkDetectors.Get("no-such-detector"), "an unknown detector fails loudly");
    }

    [Test]
    public static async Task PathsResolveAgainstTheRunsFolderThenTheWorkspaceAndOpenAtThePlace()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var inWorkspace = fixture.Write("src/A.cs", "zero\none\ntwo\n");
        var run = Path.Combine(fixture.Workspace, "run");
        Directory.CreateDirectory(Path.Combine(run, "src"));
        var inRun = Path.Combine(run, "src", "A.cs");
        await File.WriteAllTextAsync(inRun, "run\n");
        Check.Equal(inRun, ShellModel.ResolveLink("src/A.cs", run, fixture.Workspace), "the run's folder first");
        Check.Equal(inWorkspace, ShellModel.ResolveLink(@"src\A.cs", null, fixture.Workspace), "then the workspace (back slashes too)");
        Check.Equal(inWorkspace, ShellModel.ResolveLink(inWorkspace, run, null), "an absolute path as it is");
        Check.True(ShellModel.ResolveLink("src/none.cs", run, fixture.Workspace) is null, "a file that is not there");

        Check.True(await shell.OpenLinkAsync("src/A.cs", 2, 3), "the link opens");
        var tab = shell.Tabs.Active!;
        Check.True(tab.FilePath == inWorkspace && tab.HostDocument.Selection.Caret == 7, $"at line 2, column 3 ({tab.HostDocument.Selection.Caret})");
        Check.True(!await shell.OpenLinkAsync("src/none.cs", 1, 0), "a missing file opens nothing");
        Check.True(fixture.Host.Notifications.Entries.Any(n => n.Text.Contains("src/none.cs", StringComparison.Ordinal)), "and says so");
    }

    [Test]
    public static void OutputsClickOpensAHoveredLinkUnderlinesAndADragDoesNot()
    {
        var document = TextDocument.Create("Build started\nsrc/A.cs(12,5): error CS1002\ndone\n");
        using var model = new EditorModel(document, null, new CraftOptions(), EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard())
        {
            DetectsLinks = true,
            IsReadOnly = true,
            ShowLineNumbers = false,
        };
        model.UpdateViewport(0, 0, 800, 400);
        float cell = model.Metrics.CellWidth;
        float lineHeight = model.Metrics.LineHeight;
        float x = model.TextLeft + (4 * cell) + 1;
        float y = lineHeight + 2;
        Check.True(model.LinkAt(x, y) is { Line: 1, Link: { Line: 12, Column: 5 } }, "the link under the pointer");
        Check.True(model.LinkAt(model.TextLeft + (20 * cell) + 1, y) is null, "past it, none");

        var opened = new List<(string, int, int)>();
        model.LinkActivated += (_, e) => opened.Add((e.Path, e.Line, e.Column));
        model.PointerHovered(x, y, KeyModifiers.None);
        Check.True(model.HoveredLink is { Line: 1 }, "hovered");
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(canvas.Calls.Any(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Info && Math.Abs(c.W - (14 * cell)) < 0.5f),
            "the hovered link underlined across its 14 cells");

        model.PointerPressed(x, y, KeyModifiers.None);
        model.PointerReleased();
        Check.SequenceEqual([("src/A.cs", 12, 5)], opened, "a click opens it");
        model.PointerPressed(x, y, KeyModifiers.None);
        model.PointerMoved(x + (10 * cell), y);
        model.PointerReleased();
        Check.Equal(1, opened.Count, "a drag from it selects and opens nothing");

        model.LinksNeedControl = true;
        model.PointerPressed(x, y, KeyModifiers.None);
        model.PointerReleased();
        model.PointerPressed(x, y, KeyModifiers.Control);
        model.PointerReleased();
        Check.Equal(2, opened.Count, "where a link needs Ctrl, only Ctrl+click opens it");
    }

    [Test]
    public static void AWarmFrameUnderliningEveryLinkAllocatesNothing()
    {
        var text = string.Concat(Enumerable.Range(0, 300).Select(i => $"src/F{i}.cs({i + 1},2): error CS{i:0000} and lib/m{i}.py:{i}:1\n"));
        var document = TextDocument.Create(text);
        using var model = new EditorModel(document, null, new CraftOptions { LinksUnderline = "always" }, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard())
        {
            DetectsLinks = true,
            IsReadOnly = true,
            ShowLineNumbers = false,
        };
        model.UpdateViewport(0, 0, 1600, 400);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 1600, 400, caretVisible: false);
        int lines = renderer.LinesDrawn;
        Check.True(canvas.Calls.Count(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Info) == 2 * lines, $"two links underlined on each of the {lines} visible lines");

        var counting = new CountingCanvas();
        for (int i = 0; i < 100; i++)
        {
            renderer.Draw(counting, model, 0, (i & 1) * 20, 1600, 400, caretVisible: false);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            renderer.Draw(counting, model, 0, (i & 1) * 20, 1600, 400, caretVisible: false);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(bytes < 1024, $"200 warm frames with every link underlined allocate nothing ({bytes:N0} bytes)");
    }

    [Test]
    public static void TheTerminalFindsTheLinkUnderACellAndUnderlinesWithNoWarmAllocation()
    {
        var screen = new TerminalScreen(80, 6, 100);
        screen.Feed(Encoding.UTF8.GetBytes("Build failed:\r\n  \e[31msrc/A.cs(3,4)\e[0m: error CS1002\r\n中 lib/x.py:7\r\n"));
        long top = screen.ScreenTop;
        Check.True(TerminalLinks.At(screen, top + 1, 5) is { Path: "src/A.cs", TargetLine: 3, TargetColumn: 4, StartColumn: 2, EndColumn: 15 }, "the link under a cell, its colours aside");
        Check.True(TerminalLinks.At(screen, top + 2, 6) is { Path: "lib/x.py", TargetLine: 7, StartColumn: 3 }, "after a wide character its cells still line up");
        Check.True(TerminalLinks.At(screen, top, 3) is null && TerminalLinks.At(screen, top + 1, 30) is null, "no link elsewhere");

        var palette = new TerminalPalette(EditorThemes.Get(EditorThemes.Dark));
        var metrics = new FontMetrics(10, 20, 15);
        var renderer = new TerminalRenderer { HoveredLink = (top + 1, 2, 15) };
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, screen, palette, metrics, 800, 120, 0, null, true, false);
        Check.True(renderer.LinksDrawn == 1 && canvas.Calls.Any(c => c.Kind == "line" && c.X == 20 && Math.Abs(c.W - 130) < 0.5f), "the hovered link underlined (Ctrl held)");
        renderer.LinksUnderline = "off";
        renderer.Draw(canvas, screen, palette, metrics, 800, 120, 0, null, true, false);
        Check.Equal(0, renderer.LinksDrawn, "off: none");
        renderer.LinksUnderline = "always";
        renderer.HoveredLink = null;
        renderer.Draw(canvas, screen, palette, metrics, 800, 120, 0, null, true, false);
        Check.Equal(2, renderer.LinksDrawn, "always: every link on screen");
        var counting = new CountingCanvas();
        for (int i = 0; i < 100; i++)
        {
            renderer.Draw(counting, screen, palette, metrics, 800, 120, 0, null, true, (i & 1) == 0);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            renderer.Draw(counting, screen, palette, metrics, 800, 120, 0, null, true, (i & 1) == 0);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(bytes < 1024, $"200 warm terminal frames underlining every link allocate nothing ({bytes:N0} bytes)");
    }

    // A plug-in's format: Python's traceback lines (File "app.py", line 3).
    private sealed class PythonTraceLinks : IOutputLinkDetector
    {
        public string Name => "python-trace";

        public string Summary => "Test: File \"x.py\", line N";

        public int Find(ReadOnlySpan<char> line, Span<OutputLink> into)
        {
            int at = line.IndexOf("File \"", StringComparison.Ordinal);
            if (at < 0 || into.Length == 0)
            {
                return 0;
            }

            int start = at + 6;
            int quote = line[start..].IndexOf('"');
            int number = line[(start + quote)..].IndexOf("line ", StringComparison.Ordinal);
            if (quote < 0 || number < 0)
            {
                return 0;
            }

            int digits = start + quote + number + 5;
            int end = digits;
            int value = 0;
            while (end < line.Length && char.IsAsciiDigit(line[end]))
            {
                value = (value * 10) + (line[end++] - '0');
            }

            into[0] = new OutputLink(at, end - at, start, quote, value, 0);
            return 1;
        }
    }
}
