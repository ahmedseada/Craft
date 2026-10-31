// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The app's shell without a UI (phase E4): the settings files and their layers, the recent list, the layout chosen by
/// width, the folder tree, the tabs, and the shell's commands and key bindings on K's registries.
/// </summary>
[TestGroup("Shell")]
public static class ShellTests
{
    [Test]
    public static void ASettingsFileReadsWhatEditorsWriteAndWritesItBack()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var text = "\uFEFF{\n  // the tab\n  \"CRAFT_TAB_SIZE\": 2,\n  \"craft_theme\": \"light\",\n  \"CRAFT_INSERT_SPACES\": false,\n  \"TAB\": \"8\",\n  \"CRAFT_FONT\": null,\n  \"CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS\": true,\n}";
        File.WriteAllText(path, text, new UTF8Encoding(false));
        var file = SettingsFile.Load(path);
        Check.Equal("2", file.Get("CRAFT_TAB_SIZE"), "a number is taken as its text");
        Check.Equal("light", file.Get("CRAFT_THEME"), "a key is upper-cased");
        Check.Equal("false", file.Get("craft_insert_spaces"), "false as its text, read by any case");
        Check.Equal("true", file.Get("CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS"), "a plug-in's variable is kept");
        Check.Equal(4, file.Values.Count, "the byte-order mark, the comment and the trailing comma are skipped");
        Check.Equal(2, file.Problems.Count, "a key that is not a variable and a null value are reported");
        Check.True(file.Problems[0].Contains("'TAB'", StringComparison.Ordinal), "the problem names the key: " + file.Problems[0]);
        Check.True(file.Problems[1].Contains("CRAFT_FONT", StringComparison.Ordinal), "the problem names the variable: " + file.Problems[1]);

        Check.True(file.Set("CRAFT_TAB_SIZE", "3"), "a changed value");
        Check.True(!file.Set("CRAFT_TAB_SIZE", " 3 "), "the same value (trimmed) is no change");
        Check.True(file.Set("CRAFT_THEME", null), "null removes");
        Check.Throws<ArgumentException>(() => file.Set("TAB", "1"), "a name that is not a variable");
        file.Save();
        var again = SettingsFile.Load(path);
        Check.Equal(0, again.Problems.Count, "the written file reads back clean");
        Check.SequenceEqual(["CRAFT_INSERT_SPACES", "CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS", "CRAFT_TAB_SIZE"], [.. again.Values.Keys], "written sorted, the removed one gone");
        Check.Equal("3", again.Get("CRAFT_TAB_SIZE"), "the written value");
        Check.True(!File.Exists(path + ".tmp"), "the temporary file was moved over the file");

        var broken = SettingsFile.Parse(path, "{ \"CRAFT_TAB_SIZE\": \"2\" "u8);
        Check.Equal(0, broken.Values.Count, "a file that does not parse gives no values");
        Check.True(broken.Problems is [var reason] && reason.Contains("does not parse", StringComparison.Ordinal), "and says why");
        Check.Equal(0, SettingsFile.Load(folder.File("none.json")).Values.Count, "a missing file is empty");
        Check.True(SettingsFile.Parse(path, "[1]"u8).Problems.Count == 1, "an array is not a settings object");
    }

    [Test]
    public static void SettingsLayersTheEnvironmentOverTheWorkspaceOverTheUser()
    {
        using var folder = new TempFolder();
        var user = SettingsFile.Parse(folder.File("user.json"), """{ "CRAFT_TAB_SIZE": "2", "CRAFT_THEME": "light", "CRAFT_FONT_SIZE": "20", "CRAFT_DATA_FOLDER": "x", "CRAFT_PLUGIN_FOLDERS": "u" }"""u8);
        var workspace = SettingsFile.Parse(folder.File("ws.json"), """{ "CRAFT_TAB_SIZE": "8", "CRAFT_FONT_SIZE": "huge", "CRAFT_PLUGIN_FOLDERS": "w", "CRAFT_PLUGINS_DISABLED": "Craft.CSharp" }"""u8);
        var environment = new Dictionary<string, string> { ["CRAFT_THEME"] = "dark" };
        var settings = new CraftSettings(name => environment.GetValueOrDefault(name), user, workspace);

        Check.Equal("8", settings.Read("CRAFT_TAB_SIZE"), "the workspace over the user");
        Check.Equal(SettingSource.Workspace, settings.SourceOf("CRAFT_TAB_SIZE"), "and says so");
        Check.Equal("dark", settings.Read("CRAFT_THEME"), "the environment over the user");
        Check.Equal(SettingSource.Environment, settings.SourceOf("CRAFT_THEME"), "and says so");
        Check.Equal(null, settings.Read("CRAFT_DATA_FOLDER"), "the data folder is from the environment only");
        Check.Equal("u", settings.Read("CRAFT_PLUGIN_FOLDERS"), "a workspace never names plug-in folders (code to load); the user's does");
        Check.Equal(null, settings.Read("CRAFT_PLUGINS_DISABLED"), "nor disables plug-ins");
        Check.Equal(SettingSource.Default, settings.SourceOf("CRAFT_CARET_WIDTH"), "an unset one is its default");

        var options = settings.BuildOptions(out var problems);
        Check.Equal(8, options.TabSize, "the workspace's tab size");
        Check.Equal("dark", options.Theme, "the environment's theme");
        Check.Equal(new CraftOptions().FontSize, options.FontSize, "a value that does not parse keeps its own default (the user's 20 is under it, not taken)");
        Check.True(problems is [var problem] && problem.Contains("CRAFT_FONT_SIZE", StringComparison.Ordinal) && problem.Contains("workspace", StringComparison.Ordinal),
            "the problem names the variable and the file: " + string.Join(" | ", problems));
        Check.SequenceEqual(["u"], options.PluginFolders, "every other setting is still taken");

        Check.Throws<ArgumentException>(() => settings.Set(SettingLayer.Workspace, "CRAFT_TAB_SIZE", "wide"), "a value that does not parse is refused");
        Check.Throws<ArgumentException>(() => settings.Set(SettingLayer.Workspace, "CRAFT_PLUGIN_FOLDERS", "C:\\evil"), "a workspace may not name plug-in folders");
        Check.Throws<ArgumentException>(() => settings.Set(SettingLayer.User, "CRAFT_DATA_FOLDER", "y"), "the user's file may not move the data folder");
        Check.True(settings.Set(SettingLayer.Workspace, "craft_tab_size", "3"), "a good value is written");
        Check.Equal("3", SettingsFile.Load(workspace.FilePath).Get("CRAFT_TAB_SIZE"), "into the workspace's file on disk");
        Check.True(settings.Set(SettingLayer.Workspace, "CRAFT_TAB_SIZE", null), "cleared");
        Check.Equal("2", settings.Read("CRAFT_TAB_SIZE"), "so the user's value applies again");
        Check.Throws<InvalidOperationException>(() => new CraftSettings(_ => null, user).Set(SettingLayer.Workspace, "CRAFT_TAB_SIZE", "2"), "no folder, no workspace file");
        Check.Equal(null, CraftSettings.Validate("CRAFT_PLUGIN_SAMPLE_X", "anything"), "a plug-in's setting is taken as it is");

        var rows = settings.Rows([new CraftVariable("CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS", "false", "sample: count characters")]);
        Check.Equal(CraftOptions.Variables.Count + 1, rows.Count, "every option and the plug-in's setting");
        var theme = rows.First(r => r.Variable.Name == "CRAFT_THEME");
        Check.True(theme is { Value: "dark", Source: SettingSource.Environment, UserValue: "light" }, "a row shows the value in effect, its source and the file's own");
        var size = rows.First(r => r.Variable.Name == "CRAFT_FONT_SIZE");
        Check.True(size.Note?.Contains("CRAFT_FONT_SIZE", StringComparison.Ordinal) == true, "a row whose value does not parse says so");
        Check.True(rows.First(r => r.Variable.Name == "CRAFT_DATA_FOLDER").Note is { Length: > 0 }, "the data folder's row says it is from the environment only");
        Check.Equal("CRAFT_PLUGIN_SAMPLE_SHOWCHARACTERS", rows[^1].Variable.Name, "the plug-in's setting comes last");
    }

    [Test]
    public static void TheRecentListKeepsTheLatestFirstWithinItsLimit()
    {
        using var folder = new TempFolder();
        var path = folder.File("data/recent.json");
        var list = RecentList.Load(path, 3);
        int changes = 0;
        list.Changed += (_, _) => changes++;
        var a = folder.File("a.cs");
        var b = folder.File("b.cs");
        var c = folder.File("c.cs");
        var d = folder.File("d.cs");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        list.Add(a, RecentKind.File);
        list.Add(b, RecentKind.File);
        list.Add(folder.Path, RecentKind.Folder);
        list.Add(a, RecentKind.File);
        Check.SequenceEqual([a, folder.Path, b], [.. list.Entries.Select(e => e.Path)], "opened again moves first");
        list.Add(c, RecentKind.File);
        list.Add(d, RecentKind.File);
        Check.SequenceEqual([d, c, a], [.. list.Entries.Select(e => e.Path)], "the oldest fall off past the limit");
        Check.Equal(6, changes, "each change is raised");

        list.FlushAsync().GetAwaiter().GetResult();             // a change is written off the UI thread
        var read = RecentList.Load(path, 3);
        Check.Equal(0, read.Problems.Count, "the file reads back clean");
        Check.SequenceEqual([d, c, a], [.. read.Entries.Select(e => e.Path)], "in the same order");
        Check.Equal(2, read.RemoveMissing(), "c and d do not exist");
        Check.SequenceEqual([a], [.. read.Entries.Select(e => e.Path)], "the one that exists stays");
        read.Add(folder.Path, RecentKind.Folder);
        read.FlushAsync().GetAwaiter().GetResult();
        Check.Equal(RecentKind.Folder, RecentList.Load(path, 3).Entries[0].Kind, "a folder is stored as one");
        read.Limit = 1;
        read.FlushAsync().GetAwaiter().GetResult();
        Check.Equal(1, RecentList.Load(path, 10).Entries.Count, "lowering the limit drops the oldest and saves");
        Check.True(read.Remove(folder.Path) && read.Entries.Count == 0, "removed");

        var off = RecentList.Load(folder.File("off.json"), 0);
        off.Add(a, RecentKind.File);
        Check.Equal(0, off.Entries.Count, "a limit of 0 keeps none");
        File.WriteAllText(folder.File("bad.json"), "{ not json");
        var bad = RecentList.Load(folder.File("bad.json"), 5);
        Check.True(bad.Entries.Count == 0 && bad.Problems.Count == 1, "a file that does not parse starts empty and says why");
    }

    [Test]
    public static void TheLayoutFollowsTheMeasuredWidth()
    {
        Check.Equal(ShellLayoutKind.Wide, ShellLayout.Choose(0, 720), "wide until a width is measured");
        Check.Equal(ShellLayoutKind.Narrow, ShellLayout.Choose(400, 720), "narrow below the threshold");
        Check.Equal(ShellLayoutKind.Wide, ShellLayout.Choose(720, 720), "wide at it");
        Check.Equal(ShellLayoutKind.Wide, ShellLayout.Choose(400, 0), "a threshold of 0 never narrows");

        var layout = new ShellLayout(new CraftOptions());
        int changes = 0;
        layout.Changed += (_, _) => changes++;
        Check.True(!layout.TreeVisible && layout.EditorVisible, "wide, no folder: no tree");
        layout.HasFolder = true;
        Check.True(layout.TreeVisible && layout.EditorVisible, "wide with a folder: the tree beside the editor");
        Check.True(layout.UpdateWidth(500) && layout.IsNarrow, "a narrow window: the narrow layout");
        Check.True(!layout.UpdateWidth(510), "still narrow: no change");
        Check.True(layout.TreeVisible && !layout.EditorVisible && layout.IsNarrow, "narrow, a folder just opened: the tree in the editor's place");
        layout.FileOpenedFromTree();
        Check.True(!layout.TreeVisible && layout.EditorVisible, "a file opened from the tree: back to the editor");
        layout.ToggleTree();
        Check.True(layout.TreeVisible && !layout.EditorVisible, "toggled: the tree again");
        layout.UpdateWidth(1200);
        Check.True(layout.TreeVisible && layout.EditorVisible && !layout.IsNarrow, "wide again: the wide layout's own choice (shown)");
        layout.ToggleTree();
        layout.UpdateWidth(500);
        Check.True(layout.TreeVisible, "hiding it in the wide layout leaves the narrow one's choice");
        layout.UpdateWidth(1200);
        Check.True(!layout.TreeVisible, "and the wide one keeps its own");
        layout.Options = new CraftOptions { NarrowWidth = 1500 };
        Check.True(layout.IsNarrow, "a higher threshold narrows the same window");
        Check.True(changes >= 8, "each change is raised");
    }

    [Test]
    public static void TheFolderTreeListsAFolderWhenItOpens()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src/Inner"));
        Directory.CreateDirectory(folder.File("bin"));
        Directory.CreateDirectory(folder.File("Docs"));
        File.WriteAllText(folder.File("b.cs"), "");
        File.WriteAllText(folder.File("A.txt"), "");
        File.WriteAllText(folder.File("src/Program.cs"), "");
        File.WriteAllText(folder.File("src/Inner/Deep.cs"), "");
        File.WriteAllText(folder.File("bin/out.dll"), "");
        var workspace = new Workspace(folder.Path);
        var tree = new FolderTree(workspace);
        Check.SequenceEqual(["Docs", "src", "A.txt", "b.cs"], [.. tree.Rows.Select(r => r.Entry.Name)], "folders first, each sorted ignoring case; bin ignored");
        int changes = 0;
        tree.Changed += (_, _) => changes++;
        Check.True(!tree.Toggle(3), "a file's row does not toggle");
        Check.True(tree.Toggle(1), "src opens");
        Check.SequenceEqual(["Docs", "src", "Inner", "Program.cs", "A.txt", "b.cs"], [.. tree.Rows.Select(r => r.Entry.Name)], "src's entries below it");
        Check.True(tree.Rows[2] is { Depth: 1, IsExpanded: false } && tree.Rows[1].IsExpanded, "one level deeper; src shown open");
        Check.True(tree.Toggle(1), "src closes");
        Check.Equal(4, tree.Rows.Count, "its entries hidden");
        Check.True(tree.Reveal(folder.File("src/Inner/Deep.cs")), "revealed");
        Check.True(tree.IndexOf(folder.File("src/Inner/Deep.cs")) is var at && at > 0 && tree.Rows[at].Depth == 2, "the file's row shows, two levels down");
        Check.True(!tree.Reveal(folder.File("bin/out.dll")), "an ignored folder's file is not in the workspace");
        File.WriteAllText(folder.File("src/New.cs"), "");
        tree.Refresh();
        Check.True(tree.IndexOf(folder.File("src/New.cs")) > 0, "a file added outside the editor shows after a refresh");
        Check.Equal(4, changes, "each change is raised");
        Check.Equal(0, new Workspace(folder.Path).ListFolder("no-such").Count, "a folder that cannot be read lists nothing");
    }

    [Test]
    public static void TabsFollowTheHostAndRedrawOnlyWhenTheirMarkerChanges()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("one"));
        Directory.CreateDirectory(folder.File("two"));
        using var host = new EditorHost();
        var tabs = new DocumentTabs(host);
        var a = tabs.Add(TextDocument.Create("a"), null);
        Check.True(ReferenceEquals(host.ActiveDocument, a.HostDocument) && ReferenceEquals(tabs.Active, a), "the first tab is the host's active document");
        Check.Equal("untitled-1", a.Title, "a new document's title");
        File.WriteAllText(folder.File("one/Program.cs"), "x");
        File.WriteAllText(folder.File("two/Program.cs"), "y");
        var b = tabs.Add(TextDocument.OpenAsync(folder.File("one/Program.cs")).GetAwaiter().GetResult(), null);
        Check.Equal("Program.cs", b.Title, "a file's title is its name");
        var c = tabs.Add(TextDocument.OpenAsync(folder.File("two/Program.cs")).GetAwaiter().GetResult(), null, activate: false);
        Check.True(b.Title == "Program.cs — one" && c.Title == "Program.cs — two", "two files of one name show their folders");
        Check.True(ReferenceEquals(tabs.Active, b) && ReferenceEquals(host.ActiveDocument, b.HostDocument), "added without activating: the active one stays");
        Check.True(ReferenceEquals(tabs.Find(folder.File("two/./Program.cs")), c), "found by its path");
        Check.Equal(3, host.Documents.Count, "every tab is open in the host");

        Check.True(tabs.Next() && ReferenceEquals(tabs.Active, c), "next");
        Check.True(tabs.Next() && ReferenceEquals(tabs.Active, a), "next wraps to the first");
        Check.True(tabs.Previous() && ReferenceEquals(tabs.Active, c), "previous wraps to the last");
        Check.True(ReferenceEquals(host.ActiveDocument, c.HostDocument), "the host follows");

        int redraws = 0;
        tabs.TabChanged += (_, _) => redraws++;
        var document = (TextDocument)c.Document;
        for (int i = 0; i < 20; i++)
        {
            document.Edit([TextEdit.Insert(0, "z")], EditKind.Paste, SelectionState.At(0), SelectionState.At(1));
        }

        Check.Equal(1, redraws, "twenty edits change the dirty marker once");
        Check.True(tabs.HasUnsaved, "a tab has unsaved changes");

        tabs.Remove(c);
        Check.True(ReferenceEquals(tabs.Active, b), "the active tab closed: the one before it (it was last)");
        Check.Equal("Program.cs", b.Title, "its name alone again");
        Check.Equal(2, host.Documents.Count, "closed in the host");
        tabs.Activate(a);
        tabs.Remove(a);
        Check.True(ReferenceEquals(tabs.Active, b), "the first closed: the one after it");
        Check.True(!tabs.Next(), "one tab: no next");
        tabs.Remove(b);
        Check.True(tabs.Active is null && host.ActiveDocument is null && host.Documents.Count == 0, "the last closed: nothing active");
    }

    [Test]
    public static async Task TheShellsActionsAreCommandsWithKeyBindings()
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File("Program.cs"), "class P {}");
        Directory.CreateDirectory(folder.File(".craft"));
        await File.WriteAllTextAsync(folder.File(".craft/settings.json"), """{ "CRAFT_TAB_SIZE": "2" }""");
        var user = SettingsFile.Load(folder.File("data/settings.json"));
        var settings = new CraftSettings(_ => null, user);
        var recent = RecentList.Load(folder.File("data/recent.json"), 10);
        var dialogs = new FakeDialogs { Folder = folder.Path, File = folder.File("Program.cs"), SavePath = folder.File("Saved.txt") };
        var ui = new FakeShellUi();
        using var host = new EditorHost();
        var commandsBefore = EditorCommands.Names.Count;
        var bindingsBefore = EditorKeyBindings.Names.Count;
        using (var shell = new ShellModel(host, settings, recent, dialogs, ui))
        {
            foreach (var id in (string[])[ShellCommandIds.OpenFolder, ShellCommandIds.OpenFile, ShellCommandIds.Save, ShellCommandIds.SaveAs, ShellCommandIds.CloseTab,
                ShellCommandIds.NextTab, ShellCommandIds.PreviousTab, ShellCommandIds.Settings, ShellCommandIds.ToggleTree, ShellCommandIds.OpenRecent])
            {
                Check.True(EditorCommands.Find(id) is not null, $"{id} is a registered command");
            }

            foreach (var (chord, id) in (ReadOnlySpan<(string, string)>)[("Ctrl+O", ShellCommandIds.OpenFile), ("Ctrl+Shift+O", ShellCommandIds.OpenFolder), ("Ctrl+W", ShellCommandIds.CloseTab),
                ("Ctrl+Tab", ShellCommandIds.NextTab), ("Ctrl+Shift+Tab", ShellCommandIds.PreviousTab), ("Ctrl+Comma", ShellCommandIds.Settings), ("Ctrl+B", ShellCommandIds.ToggleTree)])
            {
                Check.Equal(id, EditorKeyBindings.Find(KeyChord.Parse(chord))?.CommandId, $"{chord} runs {id}");
            }

            Check.True(!host.Commands.First(c => c.Name == ShellCommandIds.CloseTab).CanExecute(host), "no tab: Close cannot run");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.OpenFolder), "Open folder ran");
            Check.True(shell.Workspace?.Root == folder.Path && shell.Tree is { Rows.Count: > 0 } && shell.Layout.HasFolder, "the folder is the workspace, its tree listed");
            Check.Equal(2, shell.Options.TabSize, "its .craft/settings.json applies");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.OpenFile), "Open file ran");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.OpenFile), "and again");
            Check.Equal(1, shell.Tabs.Tabs.Count, "a file opens in one tab");
            Check.SequenceEqual([folder.File("Program.cs"), folder.Path], [.. recent.Entries.Select(e => e.Path)], "the file and the folder are recent");

            int optionsChanged = 0;
            shell.OptionsChanged += (_, _) => optionsChanged++;
            Check.Equal(null, shell.ApplySetting(SettingLayer.Workspace, "CRAFT_TAB_SIZE", "6"), "a setting written to the workspace");
            Check.True(shell.Options.TabSize == 6 && optionsChanged == 1, "applied at once");
            Check.True(shell.ApplySetting(SettingLayer.Workspace, "CRAFT_TAB_SIZE", "wide") is { } refused && refused.Contains("CRAFT_TAB_SIZE", StringComparison.Ordinal), "a bad value refused with the reason");
            Check.Equal(null, shell.ApplySetting(SettingLayer.User, "CRAFT_THEME", "light"), "the user's theme");
            Check.True(!shell.Theme.IsDark, "the theme follows the setting");
            shell.ToggleTheme();
            Check.True(shell.Theme.IsDark, "and toggles for the run");
            Check.True(shell.SettingRows().Any(r => r.Variable.Name == "CRAFT_TAB_SIZE" && r.Source == SettingSource.Workspace), "the page's rows show the workspace's value");

            var untitled = shell.NewFile();
            ((TextDocument)untitled.Document).Edit([TextEdit.Insert(0, "hello")], EditKind.Paste, SelectionState.At(0), SelectionState.At(5));
            ui.Choice = UnsavedChoice.Cancel;
            Check.True(host.HandleKey(EditorKey.W, KeyModifiers.Control, null), "Ctrl+W reaches the shell through the host");
            Check.True(ui.Asked == 1 && shell.Tabs.Tabs.Count == 2, "an unsaved tab asks; Cancel keeps it");
            ui.Choice = UnsavedChoice.Save;
            Check.True(await shell.CloseTabAsync(), "Save: asked where, saved, closed");
            Check.Equal("hello", await File.ReadAllTextAsync(dialogs.SavePath), "the new document was written where the user picked");
            Check.True(shell.Tabs.Tabs.Count == 1 && recent.Entries[0].Path == dialogs.SavePath, "closed; the saved file is recent");

            var program = (TextDocument)shell.Tabs.Active!.Document;
            program.Edit([TextEdit.Insert(0, "// ")], EditKind.Paste, SelectionState.At(0), SelectionState.At(3));
            ui.Choice = UnsavedChoice.Discard;
            Check.True(await shell.ConfirmCloseAllAsync(), "closing the window: Discard lets it close");
            Check.Equal("class P {}", await File.ReadAllTextAsync(folder.File("Program.cs")), "discarded, never written");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Save), "Save ran");
            Check.Equal("// class P {}", await File.ReadAllTextAsync(folder.File("Program.cs")), "Save writes the file");

            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.ToggleTree) && !shell.Layout.TreeVisible, "Ctrl+B's command hides the tree");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Settings) && shell.Pages.Active?.Id == ShellPageIds.Settings, "the settings page opens as a tab of the editor area");
            ui.Pick = 1;
            await shell.OpenRecentAsync();
            Check.True(ui.Picked.Count == 3 && ui.Picked[1].StartsWith("File: ", StringComparison.Ordinal), "the recent list is offered, kinds named");
            shell.CloseFolder();
            Check.True(shell.Workspace is null && shell.Options.TabSize == 4 && !shell.Layout.HasFolder, "the folder closed: its settings no longer apply");
        }

        Check.Equal(commandsBefore, EditorCommands.Names.Count, "the shell's commands are taken back");
        Check.Equal(bindingsBefore, EditorKeyBindings.Names.Count, "and its bindings");
    }

    [Test]
    public static async Task SaveWritesANewFileWhereItWasMadeForAsksForAnUntitledOneAndSaysWhenItFails()
    {
        // The owner's Windows run: Save did nothing on the agent's new file (a tab with no file, made for a path in a folder
        // that did not exist yet), and a save dialog that failed said nothing.
        using var folder = new TempFolder();
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        var recent = RecentList.Load(folder.File("data/recent.json"), 10);
        var dialogs = new FakeDialogs { Folder = folder.Path, SavePath = folder.File("picked/Untitled.txt") };
        var ui = new FakeShellUi();
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, recent, dialogs, ui);
        await shell.OpenFolderAsync(folder.Path);
        string target = folder.File("Generated/Deep/Example.cs");
        var proposed = await host.OpenDocumentAsync(target);
        Check.True(proposed is not null && proposed.FilePath is null, "a path with no file yet opens a new document (the agent's new file)");
        var tab = shell.Tabs.Active!;
        Check.True(tab.SuggestedPath == Path.GetFullPath(target) && tab.Title == "Example.cs", $"the tab keeps the path it was made for and is titled by it ({tab.Title})");
        Check.True(ReferenceEquals(await host.OpenDocumentAsync(target), proposed), "the same new file again goes to the same tab");
        ((TextDocument)tab.Document).Edit([TextEdit.Insert(0, "class Example { }")], EditKind.Paste, SelectionState.At(0), SelectionState.At(17));
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Save), "Ctrl+S ran");
        Check.Equal("class Example { }", await File.ReadAllTextAsync(target), "Save writes it where it was made for, its folders created");
        Check.True(dialogs.SaveAsked == 0 && tab.FilePath == Path.GetFullPath(target) && tab.SuggestedPath is null && !tab.IsDirty, "without asking; the tab is that file's now");

        var untitled = shell.NewFile();
        ((TextDocument)untitled.Document).Edit([TextEdit.Insert(0, "notes")], EditKind.Paste, SelectionState.At(0), SelectionState.At(5));
        Check.True(await shell.SaveAsync(untitled), "an untitled document is saved");
        Check.True(dialogs.SaveAsked == 1 && dialogs.SaveFolder == folder.Path, $"after asking where, starting in the workspace ({dialogs.SaveFolder})");
        Check.Equal("notes", await File.ReadAllTextAsync(dialogs.SavePath), "written where the user picked, its folder created");

        ((TextDocument)untitled.Document).Edit([TextEdit.Insert(5, "!")], EditKind.Paste, SelectionState.At(5), SelectionState.At(6));
        Check.True(await shell.SaveAsync(untitled) && await File.ReadAllTextAsync(dialogs.SavePath) == "notes!", "a file's own Save writes it again");

        dialogs.Fails = true;
        var another = shell.NewFile();
        Check.True(!await shell.SaveAsync(another), "a save dialog that fails saves nothing");
        Check.True(ui.Messages.Any(m => m.StartsWith("Craft: the save dialog did not open", StringComparison.Ordinal) && m.Contains("no dialog here", StringComparison.Ordinal)),
            $"and says so, with the reason ({string.Join(" | ", ui.Messages)})");
        dialogs.Fails = false;

        await File.WriteAllTextAsync(folder.File("Blocker"), "a file where a folder should be");
        var blocked = shell.NewFile(folder.File("Blocker/Inner.cs"));
        Check.True(!await shell.SaveAsync(blocked) && ui.Messages.Any(m => m.StartsWith("Craft: could not save", StringComparison.Ordinal)), "a write that fails is said, never silent");

        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.OpenFolder) && dialogs.FolderStart == folder.Path, $"the folder dialog opens on the workspace ({dialogs.FolderStart})");
        shell.Tabs.Activate(tab);
        dialogs.File = target;
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.OpenFile) && dialogs.FileStart == Path.GetDirectoryName(Path.GetFullPath(target)), $"the file dialog opens on the active file's folder ({dialogs.FileStart})");
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public string? Folder { get; set; }

        public string? File { get; set; }

        public string SavePath { get; set; } = "";

        public bool Fails { get; set; }

        public int SaveAsked { get; private set; }

        public string? SaveFolder { get; private set; }

        public string? FolderStart { get; private set; }

        public string? FileStart { get; private set; }

        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default)
        {
            FolderStart = startFolder;
            return Task.FromResult(Folder);
        }

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default)
        {
            FileStart = startFolder;
            return Task.FromResult(File);
        }

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new System.Runtime.InteropServices.COMException("no dialog here");
            }

            SaveAsked++;
            SaveFolder = folder;
            return Task.FromResult<string?>(SavePath);
        }
    }

    private sealed class FakeShellUi : IShellUi
    {
        public UnsavedChoice Choice { get; set; } = UnsavedChoice.Cancel;

        public int Asked { get; private set; }

        public int Pick { get; set; } = -1;

        public IReadOnlyList<string> Picked { get; private set; } = [];

        public List<string> Messages { get; } = [];

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(Choice);
        }

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
        {
            Picked = items;
            return Task.FromResult(Pick);
        }

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(title + ": " + message);
            return Task.CompletedTask;
        }
    }
}
