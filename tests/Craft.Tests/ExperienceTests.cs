// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's note 15 and phase X's first slice, headless: every control's states from the tokens, tooltips that follow
/// a remap, the menus (title bar and context) with their keys and commands run on their target only when picked, the
/// tree's and tabs' commands, the empty states, motion off when reduced, the completion list unfolding, and the smooth
/// scroll's offsets. The look itself is the owner's on Windows.
/// </summary>
[TestGroup("Experience")]
public static class ExperienceTests
{
    [Test]
    public static async Task AViewPanelsBlocksRedrawOnlyWhatChangedAndThePluginReachesTheShell()
    {
        // B11: a view panel's blocks as lines for a host that draws lines, and the signatures that redraw only a changed card.
        string? acted = null;
        var panel = new ViewPanel("test.view", "View", (action, value, _) =>
        {
            acted = action + "=" + value;
            return Task.CompletedTask;
        });
        int changes = 0;
        panel.Changed += (_, _) => changes++;
        PanelBlock[] first =
        [
            new PanelCard("One", [new PanelText("hello"), new PanelSwitch("s", "Switch", false) { Variable = "CRAFT_X" }]),
            new PanelCard("Two", [new PanelMeter("Used", 0.25) { Detail = "1 of 4" }]),
        ];
        panel.SetBlocks(first);
        Check.True(changes == 1 && panel.Items.Select(i => i.Text).SequenceEqual(["One", "hello", "Switch: off", "Two", "Used: 25% · 1 of 4"]) && panel.Items[2].Detail == "CRAFT_X",
            $"the blocks as the panel's lines, a control's variable as its detail ({string.Join(" | ", panel.Items.Select(i => i.Text))})");
        await panel.ActAsync("s", "on");
        Check.Equal("s=on", acted, "an action reaches the panel with its value");

        var before = first.Select(PanelBlockLayout.Signature).ToList();
        PanelBlock[] next = [first[0], new PanelCard("Two", [new PanelMeter("Used", 0.5) { Detail = "2 of 4" }]), new PanelText("three")];
        var after = next.Select(PanelBlockLayout.Signature).ToList();
        Check.SequenceEqual([1, 2], PanelBlockLayout.Changed(before, after), "only the changed card and the new block are drawn again");
        PanelBlock tipped = new PanelCard("One", [new PanelText("hello") { Tooltip = "a new tip" }, new PanelSwitch("s", "Switch", false) { Variable = "CRAFT_X" }]);
        Check.True(PanelBlockLayout.Signature(tipped) != before[0], "a changed tooltip inside a card redraws it");

        // A chart's points: evenly across, the lowest at the bottom; one value or equal values at mid-height; NaN left out.
        var points = PanelBlockLayout.ChartPoints([1, 3, 2], 104, 44, 2);
        Check.True(points[0] == (2f, 42f) && points[1] == (52f, 2f) && points[2] == (102f, 22f), $"three points ({string.Join(" ", points)})");
        Check.True(PanelBlockLayout.ChartPoints([5], 100, 40, 0)[0] == (50f, 20f) && PanelBlockLayout.ChartPoints([2, 2], 100, 40, 0)[1].Y == 20f, "one value, equal values: mid-height");
        Check.True(float.IsNaN(PanelBlockLayout.ChartPoints([1, double.NaN, 2], 100, 40, 0)[1].Y), "a value that is not finite is left out");
        Check.True(PanelBlockLayout.MeterWidth(0.5, 200) == 100 && PanelBlockLayout.MeterWidth(3, 200) == 200 && PanelBlockLayout.MeterWidth(-1, 300) == 100, "a meter's fill held to its bar; unknown work a third");

        // The plug-in reaches the shell through its host: the Undo bar, the folder dialog and its settings, or a plain no.
        using var host = new EditorHost();
        Check.True(!((IEditorHost)host).ShowUndo("x", () => { }) && await ((IEditorHost)host).PickFolderAsync() is null, "no shell: no bar, no dialog");
        using var bar = new UndoBar(60_000);
        host.UndoPresenter = (text, undo, kept) =>
        {
            bar.Show(text, undo, kept);
            return true;
        };
        host.FolderPicker = (start, _) => Task.FromResult<string?>(start + "/picked");
        Check.True(((IEditorHost)host).ShowUndo("Deleted 2.", () => { }) && bar.Current?.Text == "Deleted 2.", "the shell's bar shows the plug-in's change");
        Check.Equal("/a/picked", await ((IEditorHost)host).PickFolderAsync("/a"), "the shell's folder dialog");
    }

    private static readonly InteractionKind[] Kinds = Enum.GetValues<InteractionKind>();
    private static readonly string[] TabFiles = ["one.txt", "two.txt", "three.txt"];
    private static readonly string[] Labels = ["Console", "Convert", "ConsoleKey"];

    [Test]
    public static void EveryControlKindHasItsStatesFromTheTokens()
    {
        foreach (var name in (ReadOnlySpan<string>)[EditorThemes.Dark, EditorThemes.Light])
        {
            var colors = EditorThemes.Get(name).Colors;
            foreach (var kind in Kinds)
            {
                foreach (bool selected in (ReadOnlySpan<bool>)[false, true])
                {
                    var normal = InteractionStyle.Of(colors, kind, InteractionState.Normal, selected);
                    var hover = InteractionStyle.Of(colors, kind, InteractionState.Hover, selected);
                    var pressed = InteractionStyle.Of(colors, kind, InteractionState.Pressed, selected);
                    var focused = InteractionStyle.Of(colors, kind, InteractionState.Focused, selected);
                    var disabled = InteractionStyle.Of(colors, kind, InteractionState.Disabled, selected);
                    string what = $"{name} {kind}{(selected ? " (selected)" : "")}";
                    Check.True(hover != normal, $"{what}: the pointer over it shows ({normal} → {hover})");
                    Check.True(pressed != hover, $"{what}: a press shows apart from the hover ({hover} → {pressed})");
                    Check.True(focused.Ring && focused.Stroke == colors.Accent, $"{what}: the keyboard's focus is the accent's ring");
                    Check.True(disabled.Text != normal.Text, $"{what}: disabled fades its text");
                    Check.True(!normal.Ring && !hover.Ring && !pressed.Ring, $"{what}: only the focus shows the ring");
                }
            }

            // The losing action keeps its colour in every state but hover and press, which may brighten it.
            var danger = InteractionStyle.Of(colors, InteractionKind.Secondary, InteractionState.Normal, text: colors.Error);
            Check.Equal(colors.Error, danger.Text, $"{name}: a losing action's text is the error's colour");
        }

        Check.Equal(InteractionState.Disabled, InteractionStyle.StateOf(false, true, true, true), "disabled wins over everything");
        Check.Equal(InteractionState.Pressed, InteractionStyle.StateOf(true, true, true, true), "then pressed");
        Check.Equal(InteractionState.Focused, InteractionStyle.StateOf(true, false, true, true), "then the focus");
        Check.Equal(InteractionState.Hover, InteractionStyle.StateOf(true, false, false, true), "then the hover");
        Check.Equal(InteractionState.Normal, InteractionStyle.StateOf(true, false, false, false), "else at rest");
    }

    [Test]
    public static void TooltipsAndMenuKeysFollowARemap()
    {
        const string Id = "test.experience.tip";
        var defaultKeys = KeyChord.Parse("Ctrl+Alt+Y");
        var binding = new KeyBinding(defaultKeys, Id);
        EditorCommands.Register(new EditorCommand(Id, "Test: Tip", (_, _) => Task.CompletedTask));
        EditorKeyBindings.Register(binding);
        EditorMenus.Register(new MenuItem("context:test", Id));
        EditorKeyMaps.Register(new KeyMap("test-experience", "Test", [new KeyBinding(KeyChord.Parse("Ctrl+Alt+J"), Id)]));
        try
        {
            using var host = new EditorHost();
            int changed = 0;
            host.KeyBindingsChanged += (_, _) => changed++;
            Check.Equal($"Explorer ({defaultKeys.DisplayText})", ShellTooltips.For(host, "Explorer", Id), "the registered binding's keys");
            var menu = new MenuModel(host);
            menu.Open("context:test");
            Check.Equal(defaultKeys.DisplayText, menu.Rows.Single().Chord, "the menu row shows the same keys");

            host.SetKeyBindings([new KeyBinding(KeyChord.Parse("Ctrl+Alt+U"), Id)], null);
            Check.Equal(1, changed, "the host says its keys changed");
            Check.Equal($"Explorer ({KeyChord.Parse("Ctrl+Alt+U").DisplayText})", ShellTooltips.For(host, "Explorer", Id), "the user's remap shows at once");
            menu.Open("context:test");
            Check.Equal(KeyChord.Parse("Ctrl+Alt+U").DisplayText, menu.Rows.Single().Chord, "in the menu too");

            host.SetKeyBindings([], "test-experience");
            Check.Equal(KeyChord.Parse("Ctrl+Alt+J").DisplayText, host.KeysOf(Id), "the keymap's keys over the registered ones");

            host.SetKeyBindings([new KeyBinding(defaultKeys, "")], null);
            Check.Equal("Explorer", ShellTooltips.For(host, "Explorer", Id), "keys the user took away show no shortcut");

            host.SetKeyBindings([], null);
            Check.Equal(KeyChord.Parse("Ctrl+C").DisplayText, host.KeysOf("craft.copy"), "the editor's own key (Copy) is a command's keys too");
            host.SetKeyBindings([new KeyBinding(KeyChord.Parse("Ctrl+C"), Id)], null);
            Check.True(host.KeysOf("craft.copy") is null, "and gone once the user gave that key to another command");
            Check.Equal("Copy", ShellTooltips.For(host, "Copy", null), "an action with no command shows its name");
        }
        finally
        {
            EditorKeyMaps.Unregister("test-experience");
            EditorMenus.Unregister("context:test", Id);
            EditorKeyBindings.Unregister(binding);
            EditorCommands.Unregister(Id);
        }
    }

    [Test]
    public static async Task MenusAreKeyboardNavigableAndRunOnlyWhatIsPickedOnItsTarget()
    {
        using var folder = new TempFolder();
        using var fixture = new ShellFixture(folder);
        const string Id = "test.experience.aiAction";
        int asked = 0, ran = 0;
        ContextMenuTarget? seen = null;
        EditorCommands.Register(new EditorCommand(Id, "AI: Explain the selection", (_, _) =>
        {
            ran++;
            seen = EditorContextMenus.Target;
            return Task.CompletedTask;
        }, _ =>
        {
            asked++;
            return true;
        }));
        var item = new MenuItem(EditorContextMenus.Editor, Id, Group: 50);
        EditorMenus.Register(item);
        try
        {
            using var edits = new ShellEditCommands(() => null, fixture.Shell);
            Check.True(!EditorMenus.Menus().Any(EditorContextMenus.IsContextMenu), "no context menu shows in the title bar");
            var menu = new MenuModel(fixture.Host);
            var target = new ContextMenuTarget(EditorContextMenus.Editor, folder.File("a.cs"));
            Check.True(menu.Open(EditorContextMenus.Editor, target), "the editor's menu has rows");
            var ids = menu.Rows.Select(r => r.CommandId).ToList();
            foreach (var expected in (ReadOnlySpan<string>)["craft.cut", "craft.copy", "craft.paste", "craft.goToDefinition", ShellCommandIds.FindReferences, "craft.formatDocument", StandardShortcuts.QuickFix, Id])
            {
                Check.True(ids.Contains(expected), $"the editor's menu has {expected} ({string.Join(", ", ids)})");
            }

            Check.True(menu.Rows.Any(r => r.StartsGroup), "its groups are separated");
            Check.Equal(0, ran, "opening the menu runs nothing (an AI action runs only when picked)");
            Check.True(asked > 0 && EditorContextMenus.Target is null, "it only asked whether each can run, with the target set while it asked");
            Check.Equal(-1, menu.Focused, "a menu the pointer opened has no keyboard row yet");
            Check.True(menu.HandleKey(EditorKey.Down, KeyModifiers.None, null) && menu.Focused == ids.IndexOf(Id), "Down goes to the first row that can run (no editor: the edits cannot)");
            Check.True(menu.HandleKey(EditorKey.Escape, KeyModifiers.None, null) && !menu.IsOpen && ran == 0, "Escape closes it and runs nothing");

            menu.Open(EditorContextMenus.Editor, target, focusFirst: true);
            Check.Equal(ids.IndexOf(Id), menu.Focused, "a menu the keyboard opened starts on its first row that can run");
            Check.True(menu.HandleKey(EditorKey.Up, KeyModifiers.None, null) && menu.Focused == ids.IndexOf(Id), "Up wraps over the rows that cannot run");
            Check.True(!menu.HandleKey(EditorKey.S, KeyModifiers.Control, null) && !menu.IsOpen, "a chord closes the menu and goes on to the bindings");

            menu.Open(EditorContextMenus.Editor, target);
            Check.True(await menu.RunAsync(ids.IndexOf(Id)), "a picked row runs");
            Check.True(ran == 1 && ReferenceEquals(seen, target), "its command, once, on the menu's target");
            Check.True(!menu.IsOpen && EditorContextMenus.Target is null, "the menu closed and the target is taken back");
            menu.Open(EditorContextMenus.Editor, target);
            Check.True(!await menu.RunAsync(ids.IndexOf("craft.cut")) && ran == 1, "a row that cannot run does not");

            var keys = menu.Before(new NoKeys());
            menu.Open(EditorMenus.File, null, focusFirst: true);
            Check.True(keys.HandleKey(EditorKey.Down, KeyModifiers.None, null) && menu.IsOpen, "the window's keys reach the open menu first");
            menu.Close();
            Check.True(!keys.HandleKey(EditorKey.Down, KeyModifiers.None, null), "and pass by when it is closed");
        }
        finally
        {
            EditorMenus.Unregister(EditorContextMenus.Editor, Id);
            EditorCommands.Unregister(Id);
        }
    }

    [Test]
    public static async Task TheTreesAndTabsMenusCommandsWorkOnTheirTarget()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        using var fixture = new ShellFixture(folder);
        var shell = fixture.Shell;
        var ui = fixture.Ui;
        Check.True(await shell.OpenFolderAsync(folder.Path), "the folder opens");
        var menu = new MenuModel(fixture.Host);
        var src = folder.File("src");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, src, IsFolder: true));
        var titles = menu.Rows.Where(r => !r.CommandId.StartsWith("ai.", StringComparison.Ordinal) && !r.CommandId.StartsWith("git.", StringComparison.Ordinal)).Select(r => r.Title).ToList();
        Check.SequenceEqual(["New file…", "New folder…", "Reveal in Test Files", "Open in terminal", "Find in folder…", "Collapse all inside", "Cut", "Copy", "Paste", "Duplicate",
            "Copy name", "Copy path", "Copy relative path", "Rename…", "Delete"], titles, "the tree's menu on a folder (X part 7: a folder's rows only)");
        Check.True(!menu.Rows[Index(menu, ShellCommandIds.OpenInTerminal)].Enabled, "Open in terminal waits for a terminal host");

        ui.Prompts.Enqueue("A.cs");
        Check.True(await menu.RunAsync(Index(menu, ShellCommandIds.NewFileInFolder)), "New file runs");
        var a = Path.Combine(src, "A.cs");
        Check.True(File.Exists(a) && shell.Tabs.Find(a) is not null, "the file is made in the row's folder and opened");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, src, IsFolder: true));
        ui.Prompts.Enqueue("lib");
        await menu.RunAsync(Index(menu, ShellCommandIds.NewFolder));
        Check.True(Directory.Exists(Path.Combine(src, "lib")), "New folder on a folder's row makes it there");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, src, IsFolder: true));
        ui.Prompts.Enqueue("bad/name");
        await menu.RunAsync(Index(menu, ShellCommandIds.NewFileInFolder));
        Check.True(ui.Messages.Any(m => m.Contains("cannot be a file", StringComparison.Ordinal)) && !File.Exists(Path.Combine(src, "bad")), "a name that cannot be is said, nothing made");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, a));
        await menu.RunAsync(Index(menu, ShellCommandIds.CopyRelativePath));
        Check.Equal(Path.Combine("src", "A.cs"), fixture.Clipboard.Text, "Copy relative path: from the workspace's folder");
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, a));
        await menu.RunAsync(Index(menu, ShellCommandIds.CopyPath));
        Check.Equal(a, fixture.Clipboard.Text, "Copy path: the full path");
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, a));
        await menu.RunAsync(Index(menu, ShellCommandIds.RevealInFileManager));
        Check.Equal(a, fixture.Files.Revealed, "Reveal goes to the system's file manager with the row's path");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, a));
        ui.Prompts.Enqueue("B.cs");
        await menu.RunAsync(Index(menu, ShellCommandIds.Rename));
        var b = Path.Combine(src, "B.cs");
        Check.True(File.Exists(b) && !File.Exists(a), "Rename moves the file");
        Check.True(shell.Tabs.Find(b) is not null && shell.Tabs.Find(a) is null, "and its clean tab follows it");

        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, b));
        await menu.RunAsync(Index(menu, ShellCommandIds.Delete));
        Check.True(!File.Exists(b) && ui.Confirms.Count == 0 && fixture.Files.Trashed == b && shell.Tabs.Find(b) is null,
            "X part 7: Delete asks nothing (Undo instead), asks the trash, then deletes, and closes the clean tab");
        Check.Equal("Deleted B.cs", shell.UndoBar.Current?.Text, "the Undo bar says it");
        shell.UndoBar.Dismiss();
        menu.Open(EditorContextMenus.Tree, new ContextMenuTarget(EditorContextMenus.Tree, folder.Path, IsFolder: true));
        Check.True(!menu.Rows[Index(menu, ShellCommandIds.Delete)].Enabled && !menu.Rows[Index(menu, ShellCommandIds.Rename)].Enabled, "the workspace's own folder cannot be renamed or deleted");
        Check.Equal("The open folder itself cannot be.", menu.Rows[Index(menu, ShellCommandIds.Delete)].Reason, "and the row says why");

        // The tabs' menu.
        foreach (var name in TabFiles)
        {
            await File.WriteAllTextAsync(folder.File(name), name);
            await shell.OpenFileAsync(folder.File(name));
        }

        var one = shell.Tabs.Find(folder.File("one.txt"))!;
        menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, one.FilePath, false, one));
        Check.SequenceEqual(["Close", "Close others", "Close to the right", "Close saved", "Close all", "Reopen closed tab", "Pin", "Split right", "Split down", "Move to the other group",
            "Copy name", "Copy path", "Copy relative path", "Reveal in the explorer", "Reveal in Test Files", "Open in terminal", "Format file", "Compare with saved", "Revert file", "Open timeline"],
            menu.Rows.Where(r => !r.CommandId.StartsWith("ai.", StringComparison.Ordinal) && !r.CommandId.StartsWith("git.", StringComparison.Ordinal)).Select(r => r.Title).ToList(), "the tab's menu (X part 7; X part 9's Open timeline, Keep open only on a preview)");
        string? revealed = null;
        shell.RevealRequested += (_, path) => revealed = path;
        await menu.RunAsync(Index(menu, ShellCommandIds.RevealInTree));
        Check.Equal(one.FilePath, revealed, "Reveal in the explorer asks the explorer for the tab's row");
        menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, one.FilePath, false, one));
        await menu.RunAsync(Index(menu, ShellCommandIds.CloseTabsToTheRight));
        Check.SequenceEqual(["one.txt"], shell.Tabs.Tabs.Select(t => t.Title).ToList(), "Close to the right closes the tabs after the one");
        await shell.OpenFileAsync(folder.File("two.txt"));
        menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, one.FilePath, false, one));
        await menu.RunAsync(Index(menu, ShellCommandIds.CloseOtherTabs));
        Check.True(shell.Tabs.Tabs.Count == 1 && ReferenceEquals(shell.Tabs.Active, one), "Close others keeps the one, active");
        menu.Open(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, one.FilePath, false, one));
        await menu.RunAsync(Index(menu, ShellCommandIds.CloseTab));
        Check.Equal(0, shell.Tabs.Tabs.Count, "Close closes the menu's tab");
    }

    [Test]
    public static async Task EmptyStatesSayWhatToDo()
    {
        using var folder = new TempFolder();
        using var fixture = new ShellFixture(folder);
        var host = fixture.Host;
        fixture.Shell.Recent.Add(folder.File("Work"), RecentKind.Folder);
        fixture.Shell.Recent.Add(folder.File("notes.txt"), RecentKind.File);
        var explorer = ShellEmptyStates.Explorer(host, fixture.Shell.Recent);
        Check.Equal("No folder is open", explorer.Title, "the explorer with no folder");
        Check.True(explorer.Actions[0] is { Primary: true, CommandId: ShellCommandIds.OpenFolder } open && open.Label.Contains(host.KeysOf(ShellCommandIds.OpenFolder)!, StringComparison.Ordinal),
            $"offers Open folder first, with its keys ({explorer.Actions[0].Label})");
        Check.True(explorer.Recent is [{ Label: "Work", Path: { } recent, IsFile: false }] && recent == folder.File("Work") && explorer.ListTitle == "RECENT FOLDERS", "and the recent folders");
        Check.True(explorer.MoreRecent is [{ Label: "notes.txt", Path: { } recentFile, IsFile: true }] && recentFile == folder.File("notes.txt") && explorer.MoreListTitle == "RECENT FILES",
            "and the recent files, each opened as a file (the owner, 2026-11-02: 10 recent folders and 10 recent files)");
        for (int i = 0; i < 12; i++)
        {
            fixture.Shell.Recent.Add(folder.File($"f{i}.txt"), RecentKind.File);
        }

        var full = ShellEmptyStates.Explorer(host, fixture.Shell.Recent, 10);
        Check.True(full.MoreRecent.Count == 10 && full.MoreRecent[0].Label == "f11.txt" && full.Recent.Count == 1, "at most 10 recent files, the latest first; the folders are still listed");

        Check.Equal("No problems to show", ShellEmptyStates.Problems(null, null).Title, "Problems with no file");
        var (plain, _, _) = Editors.Make("text");
        Check.True(ShellEmptyStates.Problems(plain, "notes.txt").Title.Contains("No checks", StringComparison.Ordinal), "Problems of a file no language checks says so");
        var language = new FakeSemanticLanguage();
        var (checkedModel, _, _) = Editors.Make("x", language: language);
        Check.True(ShellEmptyStates.Problems(checkedModel, "a.fake").Title is "Checking…" or "No problems in a.fake", "a checked file: checking, or none found");

        var panel = new Conversation("Chat", "Ask about this file", null);
        var chat = ShellEmptyStates.Conversation(panel, null);
        Check.True(chat.Text.Contains("Ask about this file", StringComparison.Ordinal) && chat.Text.Contains("No model is set", StringComparison.Ordinal), "the chat before its first message: what it does, and that no model is set: " + chat.Text);
        Check.True(chat.Actions is [{ CommandId: ShellCommandIds.Models }], "with Models and devices to add one");
        var agent = ShellEmptyStates.Conversation(new Conversation("Agent", "Describe a task", "The agent works on a copy."), null);
        Check.True(agent.Text.StartsWith("The agent works on a copy.", StringComparison.Ordinal), "a panel's own words come first");

        Check.True(ShellEmptyStates.Notifications().Text.Contains("Cancel", StringComparison.Ordinal), "the notification centre says what shows there");
        Check.Equal("No folder is open", ShellEmptyStates.Search(host, "prog", false, false, false).Title, "a search with no folder");
        Check.True(ShellEmptyStates.Search(host, ">zzz", true, true, false).Title.Contains("zzz", StringComparison.Ordinal), "a command search that found nothing names what was typed");
        Check.Equal("No file is open", ShellEmptyStates.Search(host, ":12", true, false, false).Title, "a line with no file");
        Check.True(ShellEmptyStates.Search(host, "qqq", true, true, true).Title.StartsWith("Listing", StringComparison.Ordinal), "while the folder is listed");
        Check.Equal("No file matches 'qqq'", ShellEmptyStates.Search(host, "qqq", true, true, false).Title, "and none found");
        await Task.CompletedTask;
    }

    [Test]
    public static void MotionIsOffWhenReducedAndDeclaredForTheSettingsPage()
    {
        Check.Equal(160, Motion.Duration(new CraftOptions()), "transitions last 160 ms by default (120-200)");
        Check.Equal(0, Motion.Duration(new CraftOptions { ReducedMotion = true }), "none with reduced motion");
        Check.Equal(0, Motion.ScrollDuration(new CraftOptions { ReducedMotion = true }), "the scroll jumps with reduced motion");
        Check.Equal(1.0, Motion.Progress(TimeSpan.Zero, 0), "a transition of 0 ms is over at once");
        Check.True(Motion.Ease(0.5) > 0.5 && Motion.Ease(0) == 0 && Motion.Ease(1) == 1, "the curve eases out");
        var options = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_REDUCED_MOTION" => "true",
            "CRAFT_MOTION_MS" => "200",
            "CRAFT_SCROLL_MS" => "0",
            "CRAFT_SCROLL_WHEEL_LINES" => "5",
            "CRAFT_REFERENCES_LIMIT" => "50",
            _ => null,
        });
        Check.True(options is { ReducedMotion: true, MotionMilliseconds: 200, ScrollMilliseconds: 0, ScrollWheelLines: 5, ReferencesLimit: 50 }, "the variables are read");
        foreach (var variable in (ReadOnlySpan<string>)["CRAFT_REDUCED_MOTION", "CRAFT_MOTION_MS", "CRAFT_SCROLL_MS", "CRAFT_SCROLL_WHEEL_LINES", "CRAFT_REFERENCES_LIMIT"])
        {
            string name = variable;
            Check.True(CraftOptions.Variables.Any(v => v.Name == name && v.Shape is not null), $"{name} is declared with its shape, so the settings page shows it");
        }

        Check.True(CraftOptions.Variables.Single(v => v.Name == "CRAFT_REDUCED_MOTION").Shape!.Kind == SettingKind.Toggle, "reduced motion is a switch");
    }

    [Test]
    public static void TheCompletionListUnfoldsUnlessMotionIsReduced()
    {
        foreach (bool reduced in (ReadOnlySpan<bool>)[false, true])
        {
            var time = new ManualTime();
            var language = new FakeSemanticLanguage();
            var options = new CraftOptions { CompletionDelayMilliseconds = 0, HoverDelayMilliseconds = 0, CompletionRows = 3, ReducedMotion = reduced };
            var (model, _, _) = Editors.Make("Con", options, language, time);
            var ui = new Queue<Action>();
            model.Dispatch = ui.Enqueue;
            model.Select(3, 3);
            model.OnKey(EditorKey.Space, KeyModifiers.Control);
            var snapshot = model.Snapshot;
            language.Last!.Completions[0].Answer.SetResult(new CompletionList(snapshot, 3, 0,
                [.. Labels.Select(l => new CompletionItem(l, CompletionItemKind.Class, null, new TextEdit(0, 3, l), l, l))]));
            while (ui.TryDequeue(out var action))
            {
                action();
            }

            Check.True(model.Completion.IsOpen, "the list opened");
            float full = 3 * FixedMeasurer.Metrics.LineHeight;
            Check.Equal(full, model.CompletionBox()!.Value.Height, "the hit test always has the whole box");
            float first = model.CompletionShownHeight();
            if (reduced)
            {
                Check.True(first == full && !model.IsAnimating, "with reduced motion it shows whole at once");
                continue;
            }

            Check.True(first < full && model.IsAnimating, $"it starts folded ({first} of {full})");
            time.AdvanceMilliseconds(80);
            float half = model.CompletionShownHeight();
            Check.True(half > first && half < full, $"unfolds over the transition ({half})");
            time.AdvanceMilliseconds(200);
            Check.True(model.CompletionShownHeight() == full && !model.IsAnimating, "and is whole when it ends");
            model.OnText("s");
            Check.True(!model.IsAnimating, "filtering does not fold it again");
        }
    }

    [Test]
    public static void TheSmoothScrollEasesToItsTargetAllocatingNothing()
    {
        var time = new ManualTime();
        var scroll = new SmoothScroll();
        scroll.Jump(0, 0);
        Check.Equal(-60.0, SmoothScroll.WheelPixels(120, 3, 20), "a notch toward the start scrolls three lines up");
        Check.Equal(30.0, SmoothScroll.WheelPixels(-60, 3, 20), "half a notch (a touchpad's) half as far");
        long start = time.GetTimestamp();
        scroll.ScrollTo(0, 1000, 0, 5000, start);
        Check.True(scroll.IsMoving && scroll.Y == 0, "it starts where it was");
        time.AdvanceMilliseconds(60);
        Check.True(scroll.Step(time.GetTimestamp(), 120, time) && scroll.Y > 500 && scroll.Y < 1000, $"halfway in time it is past halfway (eased): {scroll.Y}");
        double mid = scroll.Y;
        scroll.ScrollBy(0, 200, 0, 5000, time.GetTimestamp());
        Check.Equal(1200.0, scroll.TargetY, "a wheel turn while it moves adds to the target");
        Check.Equal(mid, scroll.Y, "from where it is drawn");
        time.AdvanceMilliseconds(120);
        Check.True(!scroll.Step(time.GetTimestamp(), 120, time) && scroll.Y == 1200, "and it settles on the target");
        scroll.ScrollBy(0, 99_999, 0, 5000, time.GetTimestamp());
        Check.Equal(5000.0, scroll.TargetY, "clamped to the content's end");
        scroll.ScrollTo(-50, -50, 100, 100, time.GetTimestamp());
        Check.True(scroll.TargetX == 0 && scroll.TargetY == 0, "and to its start");
        Check.True(!scroll.Step(time.GetTimestamp(), 0, time) && scroll.Y == 0, "a scroll of 0 ms is at its target at once");

        for (int i = 0; i < 100; i++)
        {
            scroll.ScrollBy(0, 10, 0, 5000, time.GetTimestamp());
            time.AdvanceMilliseconds(16);
            scroll.Step(time.GetTimestamp(), 120, time);         // warm: the measured loop runs compiled code
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            scroll.ScrollBy(0, 10, 0, 5000, time.GetTimestamp());
            time.AdvanceMilliseconds(16);
            scroll.Step(time.GetTimestamp(), 120, time);
        }

        Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "a thousand frames allocate nothing");
    }

    [Test]
    public static void ARightClickInsideTheSelectionKeepsIt()
    {
        var (model, _, _) = Editors.Make("abcdef");
        model.Select(1, 4);
        var inside = model.PointFromPosition(2);
        model.PointerPressed(inside.X - model.ScrollX, inside.Y - model.ScrollY + 1, KeyModifiers.None);
        model.PointerReleased();
        Check.True(model.Selection.IsEmpty, "the press itself put the caret down");
        Check.True(model.KeepSelectionForContextMenu() && model.Selection == new SelectionState(1, 4), "the context menu keeps the selection the click was in");
        var outside = model.PointFromPosition(6);
        model.PointerPressed(outside.X - model.ScrollX, outside.Y - model.ScrollY + 1, KeyModifiers.None);
        model.PointerReleased();
        Check.True(!model.KeepSelectionForContextMenu() && model.Selection.Caret == 6, "outside it the caret stays where the click put it");
    }

    private static int Index(MenuModel menu, string commandId)
    {
        for (int i = 0; i < menu.Rows.Count; i++)
        {
            if (menu.Rows[i].CommandId == commandId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"No row runs {commandId} ({string.Join(", ", menu.Rows.Select(r => r.CommandId))}).");
    }

    private sealed class ShellFixture : IDisposable
    {
        public ShellFixture(TempFolder folder)
        {
            var settings = new CraftSettings(folder.Isolated(), SettingsFile.Load(folder.File("data/settings.json")));
            Host = new EditorHost();
            Shell = new ShellModel(Host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), Ui, null, Files, Clipboard);
        }

        public EditorHost Host { get; }

        public ShellModel Shell { get; }

        public FakeUi Ui { get; } = new();

        public FakeFiles Files { get; } = new();

        public FakeClipboard Clipboard { get; } = new();

        public void Dispose()
        {
            Shell.Dispose();
            Host.Dispose();
        }
    }

    private sealed class NoKeys : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) => false;
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

        public bool Confirm { get; set; }

        public List<string> Confirms { get; } = [];

        public List<string> Messages { get; } = [];

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(title + ": " + message);
            return Task.CompletedTask;
        }

        public Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default) =>
            Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);

        public Task<bool> ConfirmAsync(string title, string text, string action, bool danger = false, CancellationToken cancellationToken = default)
        {
            Confirms.Add(title + " " + text);
            return Task.FromResult(Confirm);
        }
    }

    private sealed class FakeFiles : IShellFiles
    {
        public string? Revealed { get; private set; }

        public string? Trashed { get; private set; }

        public string RevealTitle => "Reveal in Test Files";

        public string? TrashName => null;

        public Task RevealAsync(string path, CancellationToken cancellationToken = default)
        {
            Revealed = path;
            return Task.CompletedTask;
        }

        public Task<bool> TrashAsync(string path, CancellationToken cancellationToken = default)
        {
            Trashed = path;
            return Task.FromResult(false);
        }
    }

    private sealed class Conversation(string title, string placeholder, string? empty) : IConversationPanel
    {
        public string Id => "test." + title;

        public string Title => title;

        public IReadOnlyList<PanelItem> Items => [];

        public string? Subtitle => null;

        public IReadOnlyList<ConversationEntry> Entries => [];

        public IReadOnlyList<ContextChip> Chips => [];

        public string? Status => null;

        public string Placeholder => placeholder;

        public string? EmptyText => empty;

        public bool IsBusy => false;

        public IReadOnlyList<PanelAction> HeaderActions => [];

#pragma warning disable CS0067 // The fake never changes.
        public event EventHandler? Changed;

        public event EventHandler<ConversationChangedEventArgs>? ConversationChanged;
#pragma warning restore CS0067

        public void RemoveChip(string id)
        {
        }

        public Task SubmitAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
