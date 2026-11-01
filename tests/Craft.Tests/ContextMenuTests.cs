// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 7, headless: the rich context menus of the explorer (a file's, a folder's, the workspace's) and of the tabs:
/// each target's rows and whether they can run (and why not), icons, keys, submenus opened by Right and closed by Left,
/// typing to jump, a plug-in's rich item shown by its "when", paste, duplicate and delete with Undo, names typed in place,
/// pinned tabs and close others, reopen closed with its caret, compare with saved and revert. The look is the owner's on
/// Windows.
/// </summary>
[TestGroup("ContextMenus")]
public static class ContextMenuTests
{
    private static readonly string[] FileRows =
    [
        "Open", "Open to the side", "Open with", "Reveal in Test Files", "Open in terminal", "Find in folder…", "Format file", "Cut", "Copy", "Paste", "Duplicate",
        "Select for compare", "Compare with selected", "Copy name", "Copy path", "Copy relative path", "Rename…", "Delete",
    ];

    private static readonly string[] RootRows =
    [
        "New file…", "New folder…", "Reveal in Test Files", "Open in terminal", "Find in folder…", "Collapse all", "Refresh", "Paste", "Copy path", "Open folder…",
    ];

    [Test]
    public static async Task EachTargetsRowsTheirStateIconsKeysAndSubmenus()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        await File.WriteAllTextAsync(folder.File("src/A.cs"), "class A {}");
        await File.WriteAllTextAsync(folder.File("build.sh"), "echo build");
        await File.WriteAllTextAsync(folder.File("notes.txt"), "notes");
        using var fixture = new Fixture(folder);
        var shell = fixture.Shell;
        Check.True(await shell.OpenFolderAsync(folder.Path), "the folder opens");
        await shell.Run.RefreshAsync();
        var menu = fixture.Menu();

        // A file's row: its own rows, in groups, each with an icon; the folder's and the workspace's rows hidden.
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("src/A.cs")));
        Check.SequenceEqual(FileRows, Titles(menu), "a file's rows (X part 7)");
        Check.True(menu.Rows.All(r => r.Icon is not null), "every row has its icon: " + string.Join(", ", menu.Rows.Where(r => r.Icon is null).Select(r => r.Title)));
        Check.True(Row(menu, "Open").StartsGroup == false && Row(menu, "Reveal in Test Files").StartsGroup && Row(menu, "Cut").StartsGroup, "groups with a line between them");
        Check.Equal("Shift+Alt+C", Row(menu, "Copy path").Chord, "a row shows the keys in effect");
        Check.Equal("Shift+Alt+R", Row(menu, "Reveal in Test Files").Chord, "Reveal's keys");
        var compare = Row(menu, "Compare with selected");
        Check.True(!compare.Enabled && compare.Reason == "Pick Select for compare on another file first.", $"a row that cannot run says why ({compare.Reason})");
        var paste = Row(menu, "Paste");
        Check.True(!paste.Enabled && paste.Reason == "Cut or copy a file or folder in the explorer first.", $"Paste waits for a cut or a copy ({paste.Reason})");
        Check.True(!Row(menu, "Open in terminal").Enabled && Row(menu, "Open in terminal").Reason == "No terminal host is running.", "Open in terminal waits for a terminal host, said");
        Check.True(Row(menu, "Open with").Submenu == EditorContextMenus.OpenWith && Row(menu, "Open with").Enabled, "Open with opens a submenu");
        Check.True(menu.Target is { Kind: MenuTargetKinds.File, IsRoot: false } && menu.Target.ProjectKinds.Contains(RunProviders.Scripts),
            $"the target is described: a file, of the root's scripts project ({string.Join(",", menu.Target?.ProjectKinds ?? [])})");

        // Submenus: Right opens one on its first row, Left closes it, Escape closes it first, then the menu.
        menu.SetFocus(Index(menu, "Open with"));
        Check.True(menu.HandleKey(EditorKey.Right, KeyModifiers.None, null) && menu.Submenu is { IsOpen: true } open && open.Focused == 0, "Right opens the submenu on its first row");
        Check.True(menu.Submenu!.Rows[^1].Title == "Plain text" && menu.Submenu.Rows.All(r => r.CommandId == ShellCommandIds.OpenWithLanguage),
            $"its rows: the languages, then plain text ({string.Join(", ", menu.Submenu.Rows.Select(r => r.Title))})");
        Check.True(menu.HandleKey(EditorKey.Down, KeyModifiers.None, null) && menu.Submenu.Focused != 0 || menu.Submenu.Rows.Count == 1, "Down moves inside the submenu");
        Check.True(menu.HandleKey(EditorKey.Left, KeyModifiers.None, null) && menu.Submenu is null && menu.IsOpen && menu.Focused == Index(menu, "Open with"), "Left closes it, the keyboard back on its row");
        menu.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
        Check.True(menu.Submenu is not null, "Enter on a submenu's row opens it");
        Check.True(menu.HandleKey(EditorKey.Escape, KeyModifiers.None, null) && menu.Submenu is null && menu.IsOpen, "Escape closes the submenu first");
        Check.True(menu.HandleKey(EditorKey.Escape, KeyModifiers.None, null) && !menu.IsOpen, "then the menu");

        // A runnable file: its "Run" submenu lists its own target's actions; a source file without a runner has none.
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("build.sh")));
        Check.True(Titles(menu).Contains("Run") && Row(menu, "Run").Submenu == EditorContextMenus.RunTargets, "a script's menu has Run");
        Check.True(menu.OpenSubmenu(Index(menu, "Run")) && menu.Submenu!.Rows.Any(r => r.Title == "Run build.sh" && r.Argument == "run.target|" + RunProviders.Scripts + ":build.sh"),
            $"its rows run the script through the Run view ({string.Join(", ", menu.Submenu!.Rows.Select(r => r.Title + "=" + r.Argument))})");
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("notes.txt")));
        Check.True(!Titles(menu).Contains("Run"), "a file without a runner has no Run");

        // A folder, and the workspace (its header, the empty space).
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree));
        Check.SequenceEqual(RootRows, Titles(menu), "the workspace's rows (the tree's empty space)");
        Check.True(menu.Target is { IsRoot: true, IsFolder: true } && menu.Target.Path == shell.Workspace!.Root, "the empty space is the workspace's folder");
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.Path, IsFolder: true));
        Check.SequenceEqual(RootRows, Titles(menu), "the workspace's own row is described as the root");
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("src"), IsFolder: true));
        Check.True(Titles(menu).Contains("Collapse all inside") && !Titles(menu).Contains("Open") && !Titles(menu).Contains("Collapse all"), "a folder's rows");
        Check.True(!Row(menu, "Collapse all inside").Enabled && Row(menu, "Collapse all inside").Reason == "Nothing in it is open.", "Collapse all inside waits for an open folder, said");

        // Typing jumps: letters typed together form a prefix; one letter again goes to its next row.
        var clock = new StepClock();
        menu.Time = clock;
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("src/A.cs")));
        menu.HandleKey(EditorKey.C, KeyModifiers.None, null);
        Check.Equal("Cut", menu.Rows[menu.Focused].Title, "c: the first row starting with it");
        clock.Now += 100;
        menu.HandleKey(EditorKey.O, KeyModifiers.None, null);
        Check.Equal("Copy", menu.Rows[menu.Focused].Title, "co, typed together");
        clock.Now += 5000;
        menu.HandleKey(EditorKey.C, KeyModifiers.None, null);
        Check.Equal("Copy name", menu.Rows[menu.Focused].Title, "c later: a new search after the row (the disabled Compare with selected passed over)");
        clock.Now += 100;
        menu.HandleKey(EditorKey.C, KeyModifiers.None, null);
        Check.Equal("Copy path", menu.Rows[menu.Focused].Title, "c again at once: its next row");
        menu.Close();

        // The tab's menu: rows by its state (pinned or not, dirty or not).
        var tab = (await shell.OpenFileAsync(folder.File("notes.txt")))!;
        var target = new ContextMenuTarget(EditorContextMenus.Tab, tab.FilePath, false, tab);
        menu.Open(EditorContextMenus.Tab, target);
        Check.True(Titles(menu).Contains("Pin") && !Titles(menu).Contains("Unpin") && !Row(menu, "Revert file").Enabled && Row(menu, "Revert file").Reason == "notes.txt has no unsaved changes.",
            "a clean, unpinned tab: Pin; Revert says why it cannot");
        Check.True(menu.Target?.Language is null && menu.Target?.Kind == MenuTargetKinds.File, "a tab is a file");
    }

    [Test]
    public static async Task APluginsRichItemShowsByItsWhenWithItsIconAndSubmenu()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        await File.WriteAllTextAsync(folder.File("src/App.csproj"), "<Project />");
        await File.WriteAllTextAsync(folder.File("src/Readme.md"), "# app");
        Directory.CreateDirectory(folder.File("scripts"));
        await File.WriteAllTextAsync(folder.File("scripts/deploy.sh"), "echo deploy");
        using var fixture = new Fixture(folder);
        var shell = fixture.Shell;
        await shell.OpenFolderAsync(folder.Path);
        await shell.Run.RefreshAsync();
        const string Command = "test.plugin.projectAction";
        const string Pick = "test.plugin.pick";
        const string Sub = "context:test.plugin.sub";
        const string Broken = "test.plugin.broken";
        var ran = new List<ContextMenuTarget?>();
        EditorCommands.Register(new EditorCommand(Command, "Plug-in: Project action", (_, _) => { ran.Add(EditorContextMenus.Target); return Task.CompletedTask; }));
        EditorCommands.Register(new EditorCommand(Pick, "Plug-in: Pick", (_, _) => { ran.Add(EditorContextMenus.Target); return Task.CompletedTask; }));
        var byEnding = new MenuItem(EditorContextMenus.Tree, Command, "Project action", 0, 30) { Icon = MenuIcons.File("App.csproj"), When = new MenuWhen { Targets = MenuTargetKinds.File, Endings = [".csproj"] } };
        var byLanguage = new MenuItem(EditorContextMenus.Tab, Command, "C# only", 0, 30) { Icon = MenuIcons.Format, When = new MenuWhen { Languages = ["csharp"] } };
        var byKind = new MenuItem(EditorContextMenus.Tree, Pick, "Scripts", 0, 31) { Icon = RailIcons.Run, Submenu = Sub, When = new MenuWhen { ProjectKinds = [RunProviders.Scripts] } };
        EditorMenus.Register(byEnding);
        EditorMenus.Register(byLanguage);
        EditorMenus.Register(byKind);
        EditorMenuSources.Register(new MenuItemSource("test.plugin.rows", Sub, target =>
            [new MenuItem(Sub, Pick, "First", 0, 0) { Argument = "one" }, new MenuItem(Sub, Pick, "Second for " + Path.GetFileName(target?.Path ?? ""), 1, 0) { Argument = "two" }]));
        try
        {
            var menu = fixture.Menu();
            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("src/App.csproj")));
            var row = Row(menu, "Project action");
            Check.True(row.Icon == "file:App.csproj" && MenuIcons.FileOf(row.Icon) == "App.csproj" && row.Enabled && row.StartsGroup, "a .csproj's row shows with its file icon, in its own group");
            Check.True(!Titles(menu).Contains("Scripts"), "a project kind it does not belong to hides the other row");
            Check.True(await menu.RunAsync(Index(menu, "Project action")) && ran is [{ Path: var ranOn }] && ranOn == folder.File("src/App.csproj"), "it runs on its target");

            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("src/Readme.md")));
            Check.True(!Titles(menu).Contains("Project action"), "an ending it does not have hides it");
            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("scripts/deploy.sh")));
            Check.True(Titles(menu).Contains("Scripts") && Row(menu, "Scripts").Submenu == Sub, "the scripts project's file shows the plug-in's submenu");
            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("scripts"), IsFolder: true));
            Check.True(Titles(menu).Contains("Scripts"), "the folder holding the scripts belongs to the kind too");

            // The submenu's rows come from the plug-in's source, made for the target; a picked one runs with its argument.
            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("scripts/deploy.sh")));
            menu.SetFocus(Index(menu, "Scripts"));
            menu.HandleKey(EditorKey.Right, KeyModifiers.None, null);
            Check.SequenceEqual(["First", "Second for deploy.sh"], menu.Submenu!.Rows.Select(r => r.Title).ToList(), "the source's rows, made for the target");
            menu.HandleKey(EditorKey.Down, KeyModifiers.None, null);
            menu.HandleKey(EditorKey.Enter, KeyModifiers.None, null);
            Check.True(await Eventually(() => ran.Count == 2) && ran[1] is { Argument: "two" } picked && picked.Path == folder.File("scripts/deploy.sh") && !menu.IsOpen,
                "the picked row runs on the target with its argument, and both menus close");
            Check.True(EditorContextMenus.Target is null, "the target is taken back");

            // A language's condition, on a tab whose target names its language.
            menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, folder.File("src/A.cs")) { Language = "csharp" });
            Check.True(Titles(menu).Contains("C# only"), "a C# tab shows the C# row");
            menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, folder.File("src/Readme.md")));
            Check.True(!Titles(menu).Contains("C# only"), "another language's tab does not");
            Check.True(!new MenuWhen { Languages = ["csharp"] }.Matches(null) && MenuWhen.Files.Matches(new ContextMenuTarget(EditorContextMenus.Tab, null)), "a when on no target, and an untitled tab as a file");

            // A test that throws hides its row; a source that throws gives no rows; neither breaks the menu.
            EditorCommands.Register(new EditorCommand(Broken, "Plug-in: Broken", (_, _) => Task.CompletedTask));
            var throwing = new MenuItem(EditorContextMenus.Tree, Broken, "Broken", 5, 32) { When = new MenuWhen { Test = _ => throw new InvalidOperationException("no") } };
            EditorMenus.Register(throwing);
            EditorMenuSources.Register(new MenuItemSource("test.plugin.broken", Sub, _ => throw new InvalidOperationException("no")));
            menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("scripts/deploy.sh")));
            Check.True(!Titles(menu).Contains("Broken") && Titles(menu).Contains("Scripts"), "a throwing test hides its row only");
            Check.True(menu.OpenSubmenu(Index(menu, "Scripts")) && menu.Submenu!.Rows.Count == 2, "a throwing source gives no rows; the others still do");
            menu.Close();
            Check.Throws<NotRegisteredException>(() => EditorMenuSources.Get("test.plugin.none"), "an unknown source is not registered");
        }
        finally
        {
            EditorMenus.Unregister(EditorContextMenus.Tree, Command);
            EditorMenus.Unregister(EditorContextMenus.Tab, Command);
            EditorMenus.Unregister(EditorContextMenus.Tree, Pick);
            EditorMenus.Unregister(EditorContextMenus.Tree, Broken);
            EditorMenuSources.Unregister("test.plugin.rows");
            EditorMenuSources.Unregister("test.plugin.broken");
            EditorCommands.Unregister(Command);
            EditorCommands.Unregister(Pick);
            EditorCommands.Unregister(Broken);
        }
    }

    [Test]
    public static async Task PasteDuplicateAndDeleteAreUndoable()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        Directory.CreateDirectory(folder.File("lib"));
        await File.WriteAllTextAsync(folder.File("src/A.cs"), "class A {}");
        using var fixture = new Fixture(folder);
        var shell = fixture.Shell;
        await shell.OpenFolderAsync(folder.Path);
        var menu = fixture.Menu();
        string a = folder.File("src/A.cs"), lib = folder.File("lib"), src = folder.File("src");

        // Copy, then Paste twice into lib: the second takes a free name; Undo takes the copy away.
        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Copy);
        Check.True(shell.FileClipboard is { Cut: false, Paths: [var held] } && held == a, "Copy holds the file");
        await Run(menu, EditorContextMenus.Tree, lib, true, ShellCommandIds.Paste);
        Check.True(File.Exists(Path.Combine(lib, "A.cs")) && File.Exists(a) && shell.UndoBar.Current?.Text == "Pasted A.cs in lib", $"pasted in lib, said with Undo ({shell.UndoBar.Current?.Text})");
        await Run(menu, EditorContextMenus.Tree, lib, true, ShellCommandIds.Paste);
        var copy = Path.Combine(lib, "A copy.cs");
        Check.True(File.Exists(copy) && await File.ReadAllTextAsync(copy) == "class A {}", "a second paste takes a free name, the same text");
        Check.True(shell.UndoBar.Undo() && await Eventually(() => !File.Exists(copy)) && File.Exists(Path.Combine(lib, "A.cs")), "Undo takes the last paste's copy away");

        // A file of a folder pasted into itself is refused for a folder; a cut moves once, and Undo moves it back.
        await Run(menu, EditorContextMenus.Tree, src, true, ShellCommandIds.Copy);
        Check.True(await shell.PasteAsync(folder.File("src")) is { } why && why.Contains("into itself", StringComparison.Ordinal) && !Directory.Exists(folder.File("src/src")),
            "a folder pasted into itself: refused, said");
        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Cut);
        await Run(menu, EditorContextMenus.Tree, null, true, ShellCommandIds.Paste);
        var moved = folder.File("A.cs");
        Check.True(File.Exists(moved) && !File.Exists(a) && shell.FileClipboard.Paths.Count == 0, "a cut pasted at the root moves the file, and is pasted once");
        Check.Equal("Moved A.cs to " + Path.GetFileName(folder.Path), shell.UndoBar.Current?.Text, "said with Undo");
        Check.True(shell.UndoBar.Undo() && await Eventually(() => File.Exists(a) && !File.Exists(moved)), "Undo moves it back");

        // Duplicate beside itself, with Undo.
        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Duplicate);
        var duplicate = folder.File("src/A copy.cs");
        Check.True(File.Exists(duplicate) && shell.UndoBar.Current?.Text == "Copied A copy.cs to src", $"Duplicate: a copy beside it ({shell.UndoBar.Current?.Text})");
        Check.True(shell.UndoBar.Undo() && await Eventually(() => !File.Exists(duplicate)), "Undo takes the duplicate away");

        // Delete with Undo: no question, the clean tab closes, Undo puts the file back with its text and opens its tab again.
        await shell.OpenFileAsync(a);
        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Delete);
        Check.True(!File.Exists(a) && shell.Tabs.Find(a) is null && fixture.Ui.Confirms.Count == 0 && shell.UndoBar.Current?.Text == "Deleted A.cs", "deleted at once, said with Undo");
        Check.True(shell.UndoBar.Undo() && await Eventually(() => File.Exists(a) && shell.Tabs.Find(a) is not null), "Undo puts it back and opens its tab again");
        Check.Equal("class A {}", await File.ReadAllTextAsync(a), "with its text");

        // A folder deleted and brought back with everything in it.
        await File.WriteAllTextAsync(folder.File("lib/B.cs"), "class B {}");
        await Run(menu, EditorContextMenus.Tree, lib, true, ShellCommandIds.Delete);
        Check.True(!Directory.Exists(lib) && shell.UndoBar.Undo() && await Eventually(() => File.Exists(folder.File("lib/B.cs"))), "a folder comes back whole");

        // Larger than CRAFT_DELETE_UNDO_MAX_MB (0: never kept): asked first, and No keeps it.
        Check.True(shell.ApplySetting(SettingLayer.User, "CRAFT_DELETE_UNDO_MAX_MB", "0") is null, "the limit is set");
        fixture.Ui.Confirm = false;
        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Delete);
        Check.True(File.Exists(a) && fixture.Ui.Confirms.Count == 1 && fixture.Ui.Confirms[0].Contains("for good", StringComparison.Ordinal), "too large to keep: asked first (said for good), No keeps it");
    }

    [Test]
    public static async Task NamesAreTypedInPlaceInTheTree()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        await File.WriteAllTextAsync(folder.File("src/A.cs"), "class A {}");
        await File.WriteAllTextAsync(folder.File("src/C.cs"), "class C {}");
        using var fixture = new Fixture(folder);
        var shell = fixture.Shell;
        await shell.OpenFolderAsync(folder.Path);
        shell.InlineTreeEdits = true;
        int changes = 0;
        shell.TreeEditChanged += (_, _) => changes++;
        var menu = fixture.Menu();
        string a = folder.File("src/A.cs");

        await Run(menu, EditorContextMenus.Tree, a, false, ShellCommandIds.Rename);
        Check.True(shell.TreeEdit is { Kind: TreeEditKind.Rename, Name: "A.cs" } edit && edit.RowPath == a && fixture.Ui.PromptCount == 0 && changes == 1,
            "Rename names in place on the row (no dialog)");
        Check.True(shell.Tree!.IndexOf(a) >= 0, "its folder is open so the row shows");
        Check.True(shell.TreeEditProblem("bad/name") is { } bad && bad.Contains("cannot be", StringComparison.Ordinal), "a name that cannot be is said as it is typed");
        Check.Equal("C.cs is in src already.", shell.TreeEditProblem("C.cs"), "a name the folder has is said, from what the tree holds");
        Check.True(shell.TreeEditProblem("a.cs") is null && shell.TreeEditProblem("B.cs") is null, "a case change and a free name are fine");
        Check.True(!await shell.CommitTreeEditAsync("C.cs") && shell.TreeEdit is not null && File.Exists(a), "a name in use keeps the field, nothing renamed");
        Check.True(await shell.CommitTreeEditAsync("B.cs") && shell.TreeEdit is null && File.Exists(folder.File("src/B.cs")) && !File.Exists(a), "Enter renames");
        Check.Equal("Renamed A.cs to B.cs", shell.UndoBar.Current?.Text, "with Undo");
        Check.True(shell.UndoBar.Undo() && await Eventually(() => File.Exists(a) && !File.Exists(folder.File("src/B.cs"))), "Undo gives the old name back");

        // New file and new folder: a row at the top of the folder, named in place.
        await Run(menu, EditorContextMenus.Tree, folder.File("src"), true, ShellCommandIds.NewFileInFolder);
        Check.True(shell.TreeEdit is { Kind: TreeEditKind.NewFile, Name: "" } && shell.Tree.Rows.Any(r => r.IsNew && r.Entry.FullPath == folder.File("src") + Path.DirectorySeparatorChar),
            "New file: an empty row in the folder");
        Check.True(!await shell.CommitTreeEditAsync("  ") && shell.TreeEdit is null && !shell.Tree.Rows.Any(r => r.IsNew), "an empty name ends it, nothing made");
        await Run(menu, EditorContextMenus.Tree, folder.File("src"), true, ShellCommandIds.NewFileInFolder);
        Check.True(await shell.CommitTreeEditAsync("D.cs") && File.Exists(folder.File("src/D.cs")) && shell.Tabs.Find(folder.File("src/D.cs")) is not null, "a name makes the file and opens it");
        await Run(menu, EditorContextMenus.Tree, null, true, ShellCommandIds.NewFolder);
        Check.True(shell.TreeEdit is { Kind: TreeEditKind.NewFolder, IsFolder: true } && shell.Tree.Rows[0].IsNew, "New folder at the workspace: the first row");
        shell.CancelTreeEdit();
        Check.True(shell.TreeEdit is null && !shell.Tree.Rows.Any(r => r.IsNew), "Escape takes the row away");
        await Run(menu, EditorContextMenus.Tree, null, true, ShellCommandIds.NewFolder);
        Check.True(await shell.CommitTreeEditAsync("lib") && Directory.Exists(folder.File("lib")), "a folder is made");

        // Collapse all inside, and Collapse all.
        Check.True(shell.Tree.IsExpanded(folder.File("src")), "src is open");
        await Run(menu, EditorContextMenus.Tree, folder.File("src"), true, ShellCommandIds.CollapseFolder);
        Check.True(!shell.Tree.IsExpanded(folder.File("src")), "Collapse all inside closes it");
        shell.Tree.Reveal(a);
        await Run(menu, EditorContextMenus.Tree, null, true, ShellCommandIds.CollapseAll);
        Check.True(shell.Tree.Rows.All(r => !r.IsExpanded), "Collapse all closes every folder");

        // Without the inline field (the narrow layout, a test without the tree), the dialog asks as before.
        shell.InlineTreeEdits = false;
        fixture.Ui.Prompts.Enqueue("E.cs");
        await Run(menu, EditorContextMenus.Tree, folder.File("src"), true, ShellCommandIds.NewFileInFolder);
        Check.True(File.Exists(folder.File("src/E.cs")) && shell.TreeEdit is null, "the dialog's name makes the file");
    }

    [Test]
    public static async Task PinnedTabsCloseOthersCloseSavedAndReopenClosed()
    {
        using var folder = new TempFolder();
        using var fixture = new Fixture(folder);
        var shell = fixture.Shell;
        foreach (var name in (string[])["one.txt", "two.txt", "three.txt", "four.txt"])
        {
            await File.WriteAllTextAsync(folder.File(name), name + " text");
            await shell.OpenFileAsync(folder.File(name));
        }

        var menu = fixture.Menu();
        DocumentTab TabOf(string name) => shell.Tabs.Find(folder.File(name))!;
        string Order() => string.Join(" ", shell.Tabs.Tabs.Select(t => (t.IsPinned ? "*" : "") + t.Title));

        await RunOnTab(menu, TabOf("three.txt"), ShellCommandIds.PinTab);
        Check.Equal("*three.txt one.txt two.txt four.txt", Order(), "a pinned tab goes left");
        menu.Open(EditorContextMenus.Tab, Target(TabOf("three.txt")));
        Check.True(Titles(menu).Contains("Unpin") && !Titles(menu).Contains("Pin"), "a pinned tab's menu has Unpin");
        Check.True(!shell.Tabs.Move(TabOf("three.txt"), 3) && !shell.Tabs.Move(TabOf("four.txt"), 0) || Order() == "*three.txt one.txt two.txt four.txt" || Order() == "*three.txt four.txt one.txt two.txt",
            "a pinned tab stays among the pinned, another after them");
        Check.True(shell.Tabs.Tabs[0].IsPinned, "the pinned tab is still first: " + Order());

        // Close others spares the pinned one; Close saved spares unsaved and pinned ones.
        await RunOnTab(menu, TabOf("one.txt"), ShellCommandIds.CloseOtherTabs);
        Check.Equal("*three.txt one.txt", Order(), "Close others keeps the pinned tab");
        await shell.OpenFileAsync(folder.File("two.txt"));
        var two = TabOf("two.txt");
        two.Document.Edit([new TextEdit(0, 0, "x")], EditKind.Typing, default, SelectionState.At(1));
        await shell.OpenFileAsync(folder.File("four.txt"));
        await RunOnTab(menu, TabOf("four.txt"), ShellCommandIds.CloseSavedTabs);
        Check.Equal("*three.txt two.txt", Order(), "Close saved closes the clean unpinned tabs");

        // Reopen closed: the tab back at its place with its caret; pinned again when it was.
        var one = (await shell.OpenFileAsync(folder.File("one.txt")))!;
        shell.Tabs.Move(one, 1);
        Check.True(shell.Host.Select(one.HostDocument, new SelectionState(2, 5)), "a selection in one.txt");
        await RunOnTab(menu, one, ShellCommandIds.CloseTab);
        Check.True(shell.Tabs.Find(folder.File("one.txt")) is null && shell.ClosedFiles[^1] == folder.File("one.txt"), "closed, kept to reopen");
        await RunOnTab(menu, TabOf("three.txt"), ShellCommandIds.CloseTab);
        Check.True(await shell.Host.ExecuteCommandAsync(ShellCommandIds.ReopenClosedTab) && TabOf("three.txt") is { IsPinned: true } && shell.Tabs.Tabs[0] == TabOf("three.txt"),
            "Ctrl+Shift+T: the pinned tab back, pinned, first");
        Check.True(await shell.ReopenClosedTabAsync() && TabOf("one.txt") is { } back && shell.Tabs.IndexOf(back) == 1, "then one.txt at its place: " + Order());
        Check.True(TabOf("one.txt").HostDocument.Selection == new SelectionState(2, 5), $"with its selection ({TabOf("one.txt").HostDocument.Selection})");

        // The limit: CRAFT_TABS_REOPEN_LIMIT closed tabs are kept.
        Check.True(shell.ApplySetting(SettingLayer.User, "CRAFT_TABS_REOPEN_LIMIT", "1") is null, "the limit is set");
        await RunOnTab(menu, TabOf("one.txt"), ShellCommandIds.CloseTab);
        await RunOnTab(menu, TabOf("three.txt"), ShellCommandIds.CloseTab);
        Check.SequenceEqual([folder.File("three.txt")], shell.ClosedFiles, "only the last one is kept");

        // Unpin, Close all (which asks about the unsaved one; Discard).
        await shell.ReopenClosedTabAsync();
        await RunOnTab(menu, TabOf("three.txt"), ShellCommandIds.UnpinTab);
        Check.True(!TabOf("three.txt").IsPinned, "Unpin");
        await RunOnTab(menu, TabOf("three.txt"), ShellCommandIds.CloseAllTabs);
        Check.Equal(0, shell.Tabs.Tabs.Count, "Close all closes every tab (asking about the unsaved one)");
    }

    [Test]
    public static async Task CompareWithSavedRevertAndCompareWithSelected()
    {
        using var folder = new TempFolder();
        var lines = Enumerable.Range(1, 20).Select(i => "line " + i).ToArray();
        string saved = string.Join("\n", lines) + "\n";
        await File.WriteAllTextAsync(folder.File("a.txt"), saved);
        await File.WriteAllTextAsync(folder.File("b.txt"), saved.Replace("line 10", "line ten", StringComparison.Ordinal));
        var presenter = new Presenter();
        using var fixture = new Fixture(folder, presenter);
        var shell = fixture.Shell;
        await shell.OpenFolderAsync(folder.Path);
        var tab = (await shell.OpenFileAsync(folder.File("a.txt")))!;
        var menu = fixture.Menu();

        // Two changes far apart: two hunks; the first accepted takes it back, the tab stays unsaved.
        void Change(string from, string to)
        {
            var text = tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length);
            int at = text.IndexOf(from, StringComparison.Ordinal);
            tab.Document.Edit([new TextEdit(at, from.Length, to)], EditKind.Typing, default, SelectionState.At(at));
        }

        Change("line 2\n", "line two\n");
        Change("line 18\n", "line eighteen\nmore\n");
        menu.Open(EditorContextMenus.Tab, Target(tab));
        Check.True(Row(menu, "Compare with saved").Enabled && Row(menu, "Revert file").Enabled, "an unsaved tab can be compared and reverted");
        presenter.Answers = [true, false];
        await RunOnTab(menu, tab, ShellCommandIds.CompareWithSaved);
        Check.True(presenter.Proposals is [{ Diff.Hunks.Count: 2 }], $"the diff view shows the changes since the save as two hunks ({presenter.Proposals.FirstOrDefault()?.Diff.Hunks.Count})");
        string Text() => tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length);
        Check.True(Text().Contains("line 2\n", StringComparison.Ordinal) && Text().Contains("line eighteen", StringComparison.Ordinal) && tab.IsDirty, "the accepted hunk went back to the saved text; the tab is still unsaved");

        // Revert: the saved text back as one undo step, clean; the Undo bar brings the change back.
        await RunOnTab(menu, tab, ShellCommandIds.RevertFile);
        Check.True(Text() == saved && !tab.IsDirty && shell.UndoBar.Current?.Text == "Reverted a.txt", "Revert: the saved text, clean, said with Undo");
        Check.True(shell.UndoBar.Undo() && Text().Contains("line eighteen", StringComparison.Ordinal) && tab.IsDirty, "Undo brings the change back (unsaved again)");

        // Compare with saved accepted whole: clean again.
        presenter.Answers = null;
        await RunOnTab(menu, tab, ShellCommandIds.CompareWithSaved);
        Check.True(Text() == saved && !tab.IsDirty, "every hunk taken back: the tab is clean");

        // Compare with selected: a.txt selected, then b.txt: the review on a.txt with b.txt's line.
        presenter.Proposals.Clear();
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("a.txt")));
        Check.True(!Row(menu, "Compare with selected").Enabled, "nothing selected for compare yet");
        await Run(menu, EditorContextMenus.Tree, folder.File("a.txt"), false, ShellCommandIds.SelectForCompare);
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.File("a.txt")));
        Check.True(!Row(menu, "Compare with selected").Enabled && Row(menu, "Compare with selected").Reason!.Contains("selected for compare", StringComparison.Ordinal), "not with itself, said");
        presenter.Answers = [false];
        await Run(menu, EditorContextMenus.Tree, folder.File("b.txt"), false, ShellCommandIds.CompareWithSelected);
        Check.True(presenter.Proposals is [{ Diff.Hunks.Count: 1 } proposal] && proposal.Diff.Hunks[0].Lines.Any(l => l.Kind == DiffLineKind.Added && l.Text == "line ten"),
            "the review shows a.txt against b.txt's line");
        Check.Equal(saved, Text(), "rejected: a.txt is left as it was");

        // The line edits behind them: CRLF, no last line break, a whole replacement when lines do not map.
        foreach (var (from, to) in (ReadOnlySpan<(string, string)>)[("a\r\nb\r\nc", "a\r\nB\r\nc\r\nd"), ("x\ny", "x\ny\nz"), ("", "new"), ("one\rtwo\n", "one\rTWO\n"), ("same", "same")])
        {
            var snapshot = Craft.Core.Documents.TextDocument.Create(from).Buffer.Current;
            var edits = LineEdits.Between(snapshot, to, 2000);
            var made = from;
            for (int i = edits.Length - 1; i >= 0; i--)
            {
                made = made[..edits[i].Start] + edits[i].Text + made[(edits[i].Start + edits[i].Length)..];
            }

            Check.Equal(to, made, $"the edits from '{from}' make '{to}' ({edits.Length} edits)");
        }
    }

    private static List<string> Titles(MenuModel menu) => [.. menu.Rows.Where(r => !r.CommandId.StartsWith("ai.", StringComparison.Ordinal) && !r.CommandId.StartsWith("git.", StringComparison.Ordinal)).Select(r => r.Title)];

    private static MenuRow Row(MenuModel menu, string title) => menu.Rows.FirstOrDefault(r => r.Title == title)
        ?? throw new InvalidOperationException($"No row '{title}' ({string.Join(", ", menu.Rows.Select(r => r.Title))}).");

    private static int Index(MenuModel menu, string title)
    {
        for (int i = 0; i < menu.Rows.Count; i++)
        {
            if (menu.Rows[i].Title == title)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"No row '{title}' ({string.Join(", ", menu.Rows.Select(r => r.Title))}).");
    }

    private static ContextMenuTarget Target(DocumentTab tab) => new(EditorContextMenus.Tab, tab.FilePath, false, tab);

    // Opens the tree's menu on `path` (null: the empty space) and runs the row of `command`.
    private static async Task Run(MenuModel menu, string which, string? path, bool isFolder, string command)
    {
        menu.Open(which, path is null ? new ContextMenuTarget(which) : new ContextMenuTarget(which, path, isFolder));
        int index = -1;
        for (int i = 0; i < menu.Rows.Count; i++)
        {
            if (menu.Rows[i].CommandId == command)
            {
                index = i;
                break;
            }
        }

        Check.True(index >= 0 && menu.Rows[index].Enabled, $"the row of {command} shows and can run on {path ?? "the empty space"} ({string.Join(", ", menu.Rows.Select(r => r.Title + (r.Enabled ? "" : "(off: " + r.Reason + ")")))})");
        await menu.RunAsync(index);
    }

    private static async Task RunOnTab(MenuModel menu, DocumentTab tab, string command)
    {
        menu.Open(EditorContextMenus.Tab, Target(tab));
        int index = menu.Rows.ToList().FindIndex(r => r.CommandId == command);
        Check.True(index >= 0 && menu.Rows[index].Enabled, $"the tab's row of {command} can run ({string.Join(", ", menu.Rows.Select(r => r.Title + (r.Enabled ? "" : "(off)")))})");
        await menu.RunAsync(index);
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    private sealed class StepClock : TimeProvider
    {
        public long Now { get; set; }

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(TempFolder folder, IEditorPresenter? presenter = null)
        {
            var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
            Host = new EditorHost(null, null, presenter);
            Shell = new ShellModel(Host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), Ui, null, Files, Clipboard);
        }

        public EditorHost Host { get; }

        public ShellModel Shell { get; }

        public FakeUi Ui { get; } = new();

        public FakeFiles Files { get; } = new();

        public FakeClipboard Clipboard { get; } = new();

        public MenuModel Menu() => new(Host) { Describe = Shell.DescribeMenuTarget };

        public void Dispose()
        {
            Shell.Dispose();
            Host.Dispose();
        }
    }

    private sealed class Presenter : IEditorPresenter
    {
        public bool[]? Answers { get; set; }

        public List<EditProposal> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => throw new InvalidOperationException("the host asks per hunk");

        public Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal);
            return Task.FromResult<IReadOnlyList<bool>>(Answers ?? Enumerable.Repeat(true, Math.Max(1, proposal.Diff.Hunks.Count)).ToArray());
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeUi : IShellUi
    {
        public Queue<string?> Prompts { get; } = new();

        public int PromptCount { get; private set; }

        public bool Confirm { get; set; }

        public List<string> Confirms { get; } = [];

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default)
        {
            PromptCount++;
            return Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
        }

        public Task<bool> ConfirmAsync(string title, string text, string action, bool danger = false, CancellationToken cancellationToken = default)
        {
            Confirms.Add(title + " " + text);
            return Task.FromResult(Confirm);
        }
    }

    private sealed class FakeFiles : IShellFiles
    {
        public string RevealTitle => "Reveal in Test Files";

        public string? TrashName => null;

        public Task RevealAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrashAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
