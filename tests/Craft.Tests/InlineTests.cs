// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// Phase X slice 3, headless: the one overlay for quick open and the palette (fuzzy ranks and highlights, the prefixes,
/// recent first, the keys read from the bindings, a 10,000-file list matched off the UI thread), find and replace (the
/// current match, regular expressions, replace all as one undo step, the background search by snapshot version), the
/// welcome page (recent folders removed with Undo, the shortcuts, the switch), tab reorder, drops, and the inline Undo bar.
/// </summary>
[TestGroup("Inline")]
public static class InlineTests
{
    [Test]
    public static void QuickOpenRanksHighlightsAndPutsRecentFirst()
    {
        Span<int> positions = stackalloc int[6];
        Check.True(FuzzyMatch.Score("edtool", "EditorTools.cs", positions) != FuzzyMatch.None, "a match");
        Check.SequenceEqual([(0, 2), (6, 4)], FuzzyMatch.Runs(positions), "the highlight runs: 'Ed' and 'Tool' (the word start, not the t in Editor)");
        Check.Equal(FuzzyMatch.Score("edtool", "EditorTools.cs"), FuzzyMatch.Score("edtool", "EditorTools.cs", positions), "the same score with or without the positions");

        using var host = new EditorHost();
        var quick = new QuickOpen(host, 20);
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "craft-quick-open"));
        quick.SetFiles(root, [Rel("src", "Craft.Ai", "EditorTools.cs"), Rel("src", "Craft.Ai", "ToolText.cs"), Rel("src", "Craft.Editor", "EditorHost.cs"), "README.md", "build.ps1"]);
        var found = quick.Query("edtool");
        Check.True(found is [{ Text: "EditorTools.cs" } first, ..] && first.TextHighlights.SequenceEqual([(0, 2), (6, 4)]) && first.Icon?.Badge == "C#",
            $"the best first, its matched characters and its language's icon ({Describe(found)})");
        var byFolder = quick.Query("aitext");
        Check.True(byFolder is [{ Text: "ToolText.cs" } folderMatch, ..] && folderMatch.DetailHighlights.Count > 0 && folderMatch.TextHighlights.Count > 0
            && folderMatch.DetailHighlights.All(r => r.Start >= 0 && r.Start + r.Length <= folderMatch.Detail!.Length)
            && folderMatch.TextHighlights.All(r => r.Start >= 0 && r.Start + r.Length <= folderMatch.Text.Length),
            $"a match across the folder and the name highlights both, within each ({Describe(byFolder)})");
        Check.Equal("M↓", quick.Query("readme")[0].Icon?.Badge, "Markdown's icon");
        Check.Equal(FileIcons.Plain, FileIcons.For("notes.no-such-extension"), "an extension with no icon shows the plain one");

        // Recent first: with no query, the recent files lead in the order they were opened; with one, they rank ahead.
        quick.NoteFile(Path.Combine(root, "README.md"));
        quick.NoteFile(Path.Combine(root, Rel("src", "Craft.Ai", "ToolText.cs")));
        var empty = quick.Query("");
        Check.True(empty is [{ Text: "ToolText.cs", IsRecent: true }, { Text: "README.md", IsRecent: true }, { IsRecent: false }, ..], $"the recent files first, newest first ({Describe(empty)})");
        Check.True(quick.Query("t") is [{ IsRecent: true }, ..], "a recent match ranks ahead of the others");

        // The prefixes.
        Check.Equal(QuickOpenMode.Commands, QuickOpen.ModeOf(" >fmt"), "'>' asks for the commands");
        Check.Equal(QuickOpenMode.Line, QuickOpen.ModeOf(":42:7"), "':' a line");
        Check.Equal(QuickOpenMode.Files, QuickOpen.ModeOf("@Main"), "'@' is a file name: no language gives document symbols yet");
        Check.True(QuickOpen.PrefixOf(QuickOpenMode.Commands) == ">" && QuickOpen.PrefixOf(QuickOpenMode.Line) == ":" && QuickOpen.PrefixOf(QuickOpenMode.Files).Length == 0,
            "each mode's prefix");
        Check.True(quick.Query(":12:3") is [{ Line: 12, Column: 3 }], "a line and a column");

        // The commands' keys come from the bindings in effect: a remap shows at once.
        var binding = new KeyBinding(KeyChord.Parse("Ctrl+Alt+F9"), "test.inline.format");
        EditorCommands.Register(new EditorCommand("test.inline.format", "Edit: Format the inline test", (_, _) => Task.CompletedTask));
        EditorCommands.Register(new EditorCommand("test.inline.other", "Edit: Other inline test", (_, _) => Task.CompletedTask));
        EditorKeyBindings.Register(binding);
        try
        {
            var formats = quick.Query(">format inline");
            Check.True(formats is [{ CommandId: "test.inline.format", Shortcut: "Ctrl+Alt+F9" } format, ..] && format.TextHighlights.Count > 0,
                $"a command with its keys and its highlights ({Describe(formats)})");
            host.SetKeyBindings([new KeyBinding(KeyChord.Parse("Ctrl+Shift+F9"), "test.inline.format")], null);
            Check.Equal("Ctrl+Shift+F9", quick.Query(">format inline")[0].Shortcut, "the user's remap shows in the palette at once");
            host.SetKeyBindings([new KeyBinding(KeyChord.Parse("Ctrl+Alt+F9"), "")], null);
            Check.Equal(null, quick.Query(">format inline")[0].Shortcut, "unbound by the user: no keys shown");
            host.SetKeyBindings([], null);
            quick.NoteChosen(quick.Query(">other inline")[0]);
            Check.True(quick.Query(">") is [{ CommandId: "test.inline.other", IsRecent: true }, ..], "the palette lists the recent command first");
        }
        finally
        {
            EditorKeyBindings.Unregister(binding);
            EditorCommands.Unregister("test.inline.format");
            EditorCommands.Unregister("test.inline.other");
        }

        // The list's keys: the arrows wrap, a page stops at the ends.
        var selection = new OverlaySelection { PageRows = 10 };
        selection.Reset(25);
        Check.Equal(24, selection.Move(-1), "Up from the first wraps to the last");
        Check.Equal(0, selection.Move(1), "Down from the last wraps to the first");
        Check.Equal(9, selection.Page(1), "Page Down moves a page less one row");
        selection.Page(1);
        Check.Equal(24, selection.Page(1), "and stops at the last");
        Check.True(selection.HandleKey(EditorKey.Home, KeyModifiers.Control) && selection.Selected == 0, "Ctrl+Home the first");
        Check.True(!selection.HandleKey(EditorKey.Left, KeyModifiers.None), "Left is the text box's");
        selection.Reset(0);
        Check.Equal(-1, selection.Move(1), "an empty list selects nothing");
    }

    [Test]
    public static async Task QuickOpenMatchesTenThousandFilesOffTheUiThread()
    {
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 120) { BackgroundFiles = 2000 };
        var files = new List<string>(10_000);
        string[] folders = ["src", "tests", "benches", "samples", "plugins"];
        string[] parts = ["Editor", "Shell", "Model", "Tool", "Find", "Quick", "Open", "View", "Page", "Host", "Text", "Line", "Key", "Map", "Theme"];
        for (int i = 0; i < 10_000; i++)
        {
            files.Add(string.Create(CultureInfo.InvariantCulture,
                $"{folders[i % folders.Length]}/{parts[i % parts.Length]}{parts[(i / 7) % parts.Length]}/{parts[(i / 3) % parts.Length]}{parts[(i / 11) % parts.Length]}{i}.cs"));
        }

        quick.SetFiles(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "craft-10k")), files);
        Check.Equal(10_000, quick.Files.Count, "the list");

        string[] queries = ["edtool", "qopv", "shellmodel", "fndln", "theme12", "tkmap", "pgvw", "hosttext99"];
        var clock = Stopwatch.StartNew();
        var cold = await quick.QueryInBackgroundAsync(queries[0]);
        double coldMs = clock.Elapsed.TotalMilliseconds;
        Check.True(quick.LastQueryInBackground, "a list longer than CRAFT_QUICK_OPEN_BACKGROUND_FILES is matched off the calling thread");

        var times = new List<double>();
        for (int round = 0; round < 5; round++)
        {
            foreach (var query in queries)
            {
                clock.Restart();
                var rows = await quick.QueryInBackgroundAsync(query);
                times.Add(clock.Elapsed.TotalMilliseconds);
                var same = quick.Query(query);
                Check.True(rows.Select(r => (r.Path, r.Score)).SequenceEqual(same.Select(r => (r.Path, r.Score))), $"'{query}': the background rows are the calling thread's, the same order ({rows.Count} rows)");
            }
        }

        times.Sort();
        double median = times[times.Count / 2];
        double worst = times[^1];
        var line = string.Create(CultureInfo.InvariantCulture, $"Quick open over 10,000 files: cold {coldMs:F1} ms, warm median {median:F2} ms, warm worst {worst:F2} ms ({times.Count} queries, {cold.Count} rows kept of 120)");
        Console.WriteLine(line);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "quick-open-10k.txt"), line + Environment.NewLine);
        Check.True(median < 100, line + ": the warm median is within a hundred milliseconds (it runs off the UI thread)");

        // A query cancelled while it matches never lands.
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        bool cancelled = false;
        try
        {
            await quick.QueryInBackgroundAsync("editor", cancel.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Check.True(cancelled, "a cancelled query throws, its rows never shown");
    }

    [Test]
    public static async Task FindCountsTheCurrentMatchSearchesRegexAndReplacesAllAsOneUndo()
    {
        var (model, document, _) = Editors.Make("foo bar foo foofoo");
        var find = model.Find;
        find.Query = "foo";
        Check.Equal("4 matches", find.CountMatches().Text, "four matches, the selection none of them");
        find.FindNext();
        find.FindNext();
        Check.Equal(new FindCount(4, false, 2), find.CountMatches(), "the selection is the second");
        Check.Equal("2 of 4", find.CountMatches().Text, "said as '2 of 4'");
        find.WholeWord = true;
        Check.Equal(2, find.CountMatches().Count, "whole word: foofoo is not one");
        find.WholeWord = false;

        find.UseRegex = true;
        find.Query = "f(o+)";
        Check.Equal(4, find.CountMatches().Count, "a regular expression");
        find.Query = "f(o";
        Check.True(find.Problem is { } problem && problem.Contains("does not parse", StringComparison.Ordinal) && find.CountMatches().Count == 0, "one that does not parse says why and finds nothing");
        find.Query = "o*";
        Check.Equal(4, find.CountMatches().Count, "a match of no characters is never one ('o*' finds the runs of o)");
        find.Query = "f(o+)";
        int before = document.Kinds.Count;
        Check.Equal(4, find.ReplaceAll("$1x"), "four replaced");
        Check.Equal("oox bar oox ooxoox", document.Text, "the groups go into the replacement");
        Check.Equal(before + 1, document.Kinds.Count, "one edit");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("foo bar foo foofoo", document.Text, "one undo takes every replacement back");

        find.UseRegex = false;
        find.MatchCase = true;
        find.Query = "Foo";
        Check.Equal("No results", find.CountMatches().Text, "match case: none");
        find.MatchCase = false;

        // A large document (CRAFT_FIND_BACKGROUND_CHARS 0: every one) is listed, stepped and replaced off the calling thread.
        string line = "alpha beta alpha gamma";
        string text = string.Join("\n", Enumerable.Repeat(line, 500));
        var (large, largeDocument, _) = Editors.Make(text, new CraftOptions { FindBackgroundCharacters = 0 });
        large.Find.Query = "alpha";
        Check.True(!large.Find.IsListed, "not listed yet");
        var count = await large.Find.CountInBackgroundAsync();
        Check.True(count is { Count: 1000, Capped: false, Current: 0 } && large.Find.IsListed, $"listed off the thread ({count})");
        Check.True(await large.Find.FindAsync(forward: true) && large.Selection == new SelectionState(0, 5), "the first match from the list");
        Check.True(await large.Find.FindAsync(forward: false) && large.Selection.Start == text.LastIndexOf("alpha", StringComparison.Ordinal), "previous from the first comes round to the last");
        Check.True(large.Find.LastWrapped, "and says it wrapped");
        Check.Equal("1,000 of 1,000", (await large.Find.CountInBackgroundAsync()).Text, "the last of them");
        large.Find.Query = "gamma";
        Check.True(await large.Find.FindAsync(forward: true, fromSelectionStart: true), "a new query searches from the selection (not listed yet: off the thread)");
        Check.Equal(500, await large.Find.ReplaceAllInBackgroundAsync("delta"), "replace all off the thread");
        Check.Equal(text.Replace("gamma", "delta", StringComparison.Ordinal), largeDocument.Text, "every match replaced");
        large.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal(text, largeDocument.Text, "one undo step");
    }

    [Test]
    public static async Task TheWelcomePageListsRecentFoldersRemovesThemWithUndoAndReadsTheKeys()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var shell = fixture.Shell;
        Directory.CreateDirectory(folder.File("one"));
        Directory.CreateDirectory(folder.File("two"));
        shell.Recent.Add(folder.File("one"), RecentKind.Folder);
        shell.Recent.Add(folder.File("a.txt"), RecentKind.File);
        shell.Recent.Add(folder.File("two"), RecentKind.Folder);
        var welcome = new WelcomePageModel(shell);
        Check.True(welcome.RecentFolders is [{ Name: "two" }, { Name: "one" }], "the recent folders, newest first, the file left out");

        Check.True(welcome.Remove(folder.File("two")) && welcome.RecentFolders is [{ Name: "one" }], "a folder is taken off at once");
        Check.True(shell.UndoBar.Current?.Text.Contains("two", StringComparison.Ordinal) == true, "the bar says so, with Undo");
        Check.True(shell.UndoBar.Undo() && welcome.RecentFolders is [{ Name: "two" }, { Name: "one" }] && shell.Recent.Entries[0].Path == Path.GetFullPath(folder.File("two")),
            "Undo puts it back at its place");
        await shell.Recent.FlushAsync();
        Check.True(RecentList.Load(shell.Recent.FilePath, 10).Entries.Any(e => e.Path == Path.GetFullPath(folder.File("two"))), "and the list on disk has it again");

        var keys = welcome.Shortcuts.First(s => s.CommandId == ShellCommandIds.QuickOpen);
        Check.Equal("Ctrl+P", keys.Keys, "the shortcuts are read from the bindings");
        Check.True(shell.ApplySetting(SettingLayer.User, "CRAFT_KEY_BINDINGS", "Ctrl+Alt+P=craft.quickOpen") is null, "a remap");
        Check.Equal("Ctrl+Alt+P", welcome.Shortcuts.First(s => s.CommandId == ShellCommandIds.QuickOpen).Keys, "the page shows the remap");

        Check.True(welcome.ShowAtStart && shell.WelcomeAtStart, "on by default, with no folder open");
        Check.True(welcome.SetShowAtStart(false) is null && !welcome.ShowAtStart && fixture.UserValue(WelcomePageModel.ShowAtStartVariable) == "false" && !shell.WelcomeAtStart,
            "the switch writes CRAFT_WELCOME_AT_START to the user's settings");
        Check.True(welcome.SetShowAtStart(true) is null && welcome.ShowAtStart && fixture.UserValue(WelcomePageModel.ShowAtStartVariable) is null, "and back to the default");
        Check.True(CraftOptions.Variables.Any(v => v.Name == WelcomePageModel.ShowAtStartVariable && v.Shape.Kind == SettingKind.Toggle), "declared with its shape");

        Check.True(await shell.Host.ExecuteCommandAsync(ShellCommandIds.Welcome) && shell.Pages.Active?.Id == ShellPageIds.Welcome, "Help › Welcome opens its tab");
        Check.True(EditorMenus.ItemsOf(EditorMenus.Help).Any(i => i.CommandId == ShellCommandIds.Welcome), "in the Help menu");

        // Clearing the notification centre is undone in the bar, not asked first.
        shell.Host.Notifications.Post("test", "One", "first");
        shell.Host.Notifications.Post("test", "Two", "second");
        int listed = shell.Host.Notifications.Entries.Count;
        Check.Equal(listed, shell.ClearNotifications(), "every message cleared at once");
        Check.True(shell.Host.Notifications.Entries.Count == 0 && shell.UndoBar.Current?.Text.Contains("Cleared", StringComparison.Ordinal) == true, "the bar says so");
        Check.True(shell.UndoBar.Undo() && shell.Host.Notifications.Entries.Count == listed && shell.Host.Notifications.Entries[0].Title == "Two", "Undo brings them back, newest first");
    }

    [Test]
    public static async Task TabsReorderAndDropsOpenFilesAndAFolder()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var shell = fixture.Shell;
        string[] names = ["a.txt", "b.txt", "c.txt"];
        foreach (var name in names)
        {
            await File.WriteAllTextAsync(folder.File(name), name);
            await shell.OpenFileAsync(folder.File(name));
        }

        var tabs = shell.Tabs;
        var (a, b, c) = (tabs.Tabs[0], tabs.Tabs[1], tabs.Tabs[2]);
        int changed = 0;
        tabs.TabsChanged += (_, _) => changed++;
        Check.True(tabs.Move(c, 0) && tabs.Tabs.SequenceEqual([c, a, b]) && changed == 1, "a tab moved to the front, the others in order, said once");
        Check.True(ReferenceEquals(tabs.Active, c), "the active tab stays active");
        Check.True(!tabs.Move(c, 0), "moved where it is: no change");
        Check.True(tabs.MoveOnto(c, b) && tabs.Tabs.SequenceEqual([a, b, c]), "dragged right onto the last: after it");
        Check.True(tabs.MoveOnto(c, a) && tabs.Tabs.SequenceEqual([c, a, b]), "dragged left onto the first: before it");
        Check.True(tabs.Move(a, 99) && tabs.Tabs[^1] == a, "past the end: the last place");

        Directory.CreateDirectory(folder.File("project"));
        await File.WriteAllTextAsync(folder.File("project/d.txt"), "d");
        await File.WriteAllTextAsync(folder.File("e.txt"), "e");
        var result = await shell.OpenDroppedAsync([folder.File("e.txt"), folder.File("project"), folder.File("project"), folder.File("missing.txt")]);
        Check.True(result.Files is [var e] && e == Path.GetFullPath(folder.File("e.txt")) && tabs.Active?.FilePath == e, "a dropped file opens in a tab");
        Check.True(result.Folder == Path.GetFullPath(folder.File("project")) && shell.Workspace?.Root == result.Folder, "a dropped folder opens as the workspace");
        Check.True(result.Skipped.Count == 2 && result.Skipped.Any(s => s.Contains("one folder", StringComparison.Ordinal)) && result.Skipped.Any(s => s.Contains("no such", StringComparison.Ordinal)),
            $"a second folder and a missing path are said ({string.Join("; ", result.Skipped)})");
    }

    [Test]
    public static void TheUndoBarKeepsTheChangeWhenItsTimeIsUp()
    {
        var time = new ManualTime();
        using var bar = new UndoBar(5000, time: time);
        int undone = 0, changes = 0;
        bar.Changed += (_, _) => changes++;
        var first = bar.Show("Removed one.", () => undone++);
        Check.True(bar.Current == first && changes == 1, "it shows");
        time.AdvanceMilliseconds(4999);
        Check.True(!bar.ExpireIfDue() && bar.Current == first, "before its time it stays");
        time.AdvanceMilliseconds(1);
        Check.True(bar.ExpireIfDue() && bar.Current is null && undone == 0, "at its time it goes and the change stays");
        Check.True(!bar.Undo(), "nothing left to undo");

        bar.Show("Removed one.", () => undone += 10);
        var second = bar.Show("Removed two.", () => undone++);
        Check.True(bar.Current == second, "a newer change takes the bar");
        Check.True(bar.Undo() && undone == 1 && bar.Current is null, "Undo takes back only the newest");
        bar.Show("Removed three.", () => undone++);
        bar.Dismiss();
        Check.True(bar.Current is null && undone == 1, "× keeps the change");

        // B11: a change held for Undo is made for good when the bar goes without one (its time, ×, a newer bar, the end).
        var kept = new List<string>();
        bar.Show("Held one.", () => undone++, () => kept.Add("one"));
        bar.Show("Held two.", () => undone++, () => kept.Add("two"));
        Check.SequenceEqual(["one"], kept, "a newer bar keeps the older change");
        time.AdvanceMilliseconds(5000);
        Check.True(bar.ExpireIfDue() && kept.SequenceEqual(["one", "two"]), "its time up: kept");
        bar.Show("Held three.", () => undone++, () => kept.Add("three"));
        Check.True(bar.Undo() && kept.Count == 2 && undone == 2, "Undo: never kept");
        bar.Show("Held four.", () => undone++, () => kept.Add("four"));
        bar.Dismiss();
        bar.Show("Held five.", () => undone++, () => kept.Add("five"));
        bar.Dispose();
        Check.SequenceEqual(["one", "two", "four", "five"], kept, "× and the bar's end keep it");
    }

    private static string Rel(params string[] parts) => Path.Combine(parts);

    private static string Describe(IReadOnlyList<QuickOpenItem> items) =>
        string.Join(", ", items.Select(i => $"{i.Text} [{string.Join(' ', i.TextHighlights)}] {i.Detail} [{string.Join(' ', i.DetailHighlights)}]{(i.IsRecent ? " recent" : "")}"));
}
