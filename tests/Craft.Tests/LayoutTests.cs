// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// Phase U2, panels and layout, headless: the pages as tabs of the editor area with the shell's commands in reach, the
/// tree following the disk (the editor's saves, another process's files, ignored folders, renames and deletes; only the
/// rows that changed), every panel's open state and size round-tripping through the workspace's settings, the side
/// column gone (a plug-in panel is a side view of the rail), and the sandbox's read-only terminal tab.
/// </summary>
[TestGroup("Layout")]
public static class LayoutTests
{
    [Test]
    public static async Task SettingsAndModelsOpenAsTabsAndTheShellsCommandsStayInReach()
    {
        using var folder = new TempFolder();
        using var fixture = new ShellFixture(folder);
        var shell = fixture.Shell;
        var host = fixture.Host;
        var document = shell.NewFile();
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Settings) && shell.Pages.Active is { Id: ShellPageIds.Settings, Title: "Settings" },
            "Settings (Ctrl+Comma) opens a tab of the editor area, not a page over the window");
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Models) && shell.Pages.Active?.Id == ShellPageIds.Models && shell.Pages.Tabs.Count == 2,
            "Models and devices opens its own tab beside it");
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.Settings) && shell.Pages.Tabs.Count == 2 && shell.Pages.Active?.Id == ShellPageIds.Settings,
            "one tab each: opening the settings again selects its tab");
        Check.True(shell.Tabs.Tabs.Count == 1 && ReferenceEquals(shell.Tabs.Active, document), "the document's tab stays open behind the pages");

        // The menus and their commands answer while a page shows: every File and View item can run.
        foreach (var menu in (string[])[EditorMenus.File, EditorMenus.View])
        {
            var rows = ShellMenus.Rows(menu, host);
            Check.True(rows.Count > 0 && rows.Any(r => r.Enabled), $"the {menu} menu has its items while a page shows");
        }

        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.ToggleTheme), "a View command runs with a page open");
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.TogglePanel) && !shell.Panels.IsOpen(ShellPanel.Bottom), "Ctrl+J runs with a page open");
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.NewFile) && shell.Pages.Active is null && shell.Pages.Tabs.Count == 2,
            "a new file shows in the editor area; the page tabs stay");
        shell.Pages.Activate(shell.Pages.Find(ShellPageIds.Models)!);
        Check.True(await host.ExecuteCommandAsync(ShellCommandIds.CloseTab) && shell.Pages.Find(ShellPageIds.Models) is null && shell.Tabs.Tabs.Count == 2,
            "Ctrl+W on a page closes the page's tab, not a document");
        Check.True(shell.Pages.Active is null, "the document shows again");
        shell.Pages.Activate(shell.Pages.Find(ShellPageIds.Settings)!);
        shell.Tabs.Activate(shell.Tabs.Tabs[0]);
        Check.True(shell.Pages.Active is null && shell.Pages.Tabs.Count == 1, "a document's tab chosen shows the document; the settings tab stays");
    }

    [Test]
    public static async Task TheTreeFollowsTheDiskWithinASecondAndChangesOnlyTheRowsThatChanged()
    {
        using var folder = new TempFolder();
        string root = folder.File("workspace");                           // the fixture's data folder stays outside it
        Directory.CreateDirectory(root);
        string F(string name) => Path.Combine(root, name);
        await File.WriteAllTextAsync(F("a.txt"), "a");
        Directory.CreateDirectory(F("src"));
        await File.WriteAllTextAsync(Path.Combine(F("src"), "One.cs"), "class One { }");
        using var fixture = new ShellFixture(folder, watchMilliseconds: 40);
        var shell = fixture.Shell;
        Check.True(await shell.OpenFolderAsync(root) && shell.Tree is not null && shell.TreeWatch is not null, "the folder is open and its tree watched");
        var tree = shell.Tree!;
        tree.Toggle(tree.IndexOf(F("src")));
        var before = tree.Rows.ToArray();

        // Another process writes a file (as the terminal's dotnet new would): it shows within a second.
        var clock = Stopwatch.StartNew();
        await File.WriteAllTextAsync(Path.Combine(F("src"), "Two.cs"), "class Two { }");
        await fixture.PumpUntil(() => tree.IndexOf(Path.Combine(F("src"), "Two.cs")) >= 0, "a file another process wrote shows in the tree");
        Check.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"within a second ({clock.Elapsed.TotalMilliseconds:F0} ms)");
        var change = RowChange.Between(before, tree.Rows.ToArray());
        Check.True(change is { Removed: 0, Inserted: 1 }, $"only the new row is inserted, the others stay as they were ({change}; before: {Names(before)}; after: {Names(tree.Rows)})");
        Check.True(tree.Rows[tree.IndexOf(F("src"))].IsExpanded, "the open folder stays open");

        // The editor saves a new file: it shows at once (no batch wait).
        var untitled = shell.NewFile();
        ((TextDocument)untitled.Document).Edit([TextEdit.Insert(0, "hello")], EditKind.Paste, SelectionState.At(0), SelectionState.At(5));
        fixture.Dialogs.SavePath = F("Saved.txt");
        clock.Restart();
        Check.True(await shell.SaveAsync(untitled), "saved where the user picked");
        await fixture.PumpUntil(() => tree.IndexOf(F("Saved.txt")) >= 0, "a file the editor saved shows in the tree");
        Check.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"at once ({clock.Elapsed.TotalMilliseconds:F0} ms)");

        // An ignored folder's churn (a build's bin and obj) never reaches the tree; a rename and a delete do.
        await Task.Delay(200);                                            // the save's own watcher event settles first
        fixture.Pump();
        int batches = shell.TreeWatch!.Batches;
        Directory.CreateDirectory(Path.Combine(F("src"), "bin"));
        int binBatches = 0;
        await fixture.PumpUntil(() => (binBatches = shell.TreeWatch.Batches) > batches, "the bin folder itself is a change of src");
        for (int i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(F("src"), "bin", $"out{i}.dll"), "x");
        }

        await Task.Delay(200);
        fixture.Pump();
        Check.True(shell.TreeWatch.Batches == binBatches && tree.IndexOf(Path.Combine(F("src"), "bin")) < 0,
            $"twenty files written inside bin (CRAFT_WORKSPACE_IGNORE): no batch, no row ({shell.TreeWatch.Batches - binBatches} more)");
        File.Move(F("a.txt"), F("b.txt"));
        await fixture.PumpUntil(() => tree.IndexOf(F("b.txt")) >= 0 && tree.IndexOf(F("a.txt")) < 0, "a rename shows");
        Directory.Delete(F("src"), recursive: true);
        await fixture.PumpUntil(() => tree.IndexOf(F("src")) < 0 && tree.IndexOf(Path.Combine(F("src"), "One.cs")) < 0,
            "a deleted folder leaves with its rows");
    }

    [Test]
    public static void RowChangeKeepsTheRowsBeforeAndAfterTheOneThatDiffers()
    {
        string[] rows = ["a", "b", "c", "d"];
        Check.Equal(new RowChange(2, 0, 3), RowChange.Between(rows, ["a", "b", "c1", "c2", "c3", "c", "d"]), "a folder opened: only its children are inserted");
        Check.Equal(new RowChange(1, 2, 0), RowChange.Between(rows, ["a", "d"]), "a folder closed: only its children go");
        Check.Equal(new RowChange(3, 1, 1), RowChange.Between(rows, ["a", "b", "c", "D"]), "one row's fill changed: that row alone");
        Check.True(RowChange.Between(rows, rows).IsEmpty, "nothing changed: nothing replaced");
    }

    [Test]
    public static async Task EveryPanelsStateAndSizeRoundTripsThroughTheWorkspacesSettings()
    {
        using var folder = new TempFolder();
        using (var fixture = new ShellFixture(folder))
        {
            var shell = fixture.Shell;
            var panels = shell.Panels;
            Check.True(panels.IsOpen(ShellPanel.Explorer) && panels.IsOpen(ShellPanel.Chat) && panels.IsOpen(ShellPanel.Bottom), "every panel open by default");
            Check.True(panels.SizeOf(ShellPanel.Explorer) == 260 && panels.SizeOf(ShellPanel.Chat) == 360 && panels.SizeOf(ShellPanel.Bottom) == 132,
                "the CRAFT_PANEL_* defaults");
            Check.True(await shell.OpenFolderAsync(folder.Path), "a folder opens");
            Check.Equal(120d, panels.Clamp(50, 1000), "a drag never leaves less than CRAFT_PANEL_MIN_SIZE (120)");
            Check.Equal(880d, panels.Clamp(5000, 1000), "nor less than the minimum for the editor");

            panels.Resize(ShellPanel.Explorer, 333.4);
            await panels.CommitAsync(ShellPanel.Explorer);
            panels.Resize(ShellPanel.Chat, 410);
            await panels.CommitAsync(ShellPanel.Chat);
            panels.Resize(ShellPanel.Bottom, 250);
            await panels.CommitAsync(ShellPanel.Bottom);
            Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.TogglePanel), "Ctrl+J");
            await panels.LastSave;
            await panels.SetOpenAsync(ShellPanel.Chat, false);                // the chat column's close button
            Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.ToggleTree), "Ctrl+B (the rail's Explorer item runs it too)");
            await panels.LastSave;
            Check.True(!panels.IsOpen(ShellPanel.Explorer) && !shell.Layout.TreeVisible, "the explorer folds away (the owner's note 7: the rail's button toggles it)");
            Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.ToggleTree) && shell.Layout.TreeVisible, "and back");
            await panels.SetOpenAsync(ShellPanel.Explorer, false);
            await panels.LastSave;
        }

        var file = SettingsFile.Load(CraftSettings.WorkspaceFilePath(folder.Path));
        Check.True(file.Get("CRAFT_PANEL_EXPLORER_WIDTH") == "333" && file.Get("CRAFT_PANEL_CHAT_WIDTH") == "410" && file.Get("CRAFT_PANEL_BOTTOM_HEIGHT") == "250",
            $"the sizes are in the workspace's .craft/settings.json ({string.Join(", ", file.Values.Select(v => v.Key + "=" + v.Value))})");
        Check.True(file.Get("CRAFT_PANEL_EXPLORER_OPEN") == "false" && file.Get("CRAFT_PANEL_CHAT_OPEN") == "false" && file.Get("CRAFT_PANEL_BOTTOM_OPEN") == "false",
            "and the open states");

        // After a restart: the same folder opens with the same panels.
        using (var again = new ShellFixture(folder))
        {
            Check.True(await again.Shell.OpenFolderAsync(folder.Path), "the folder opens again");
            var panels = again.Shell.Panels;
            Check.True(panels.SizeOf(ShellPanel.Explorer) == 333 && panels.SizeOf(ShellPanel.Chat) == 410 && panels.SizeOf(ShellPanel.Bottom) == 250,
                "the sizes come back");
            Check.True(!panels.IsOpen(ShellPanel.Explorer) && !panels.IsOpen(ShellPanel.Chat) && !panels.IsOpen(ShellPanel.Bottom) && !again.Shell.Layout.TreeVisible,
                "the open states come back");
        }

        Check.True(CraftOptions.Variables.All(v => v.Name != "CRAFT_PANEL_WIDTH" && v.Name != "CRAFT_TREE_WIDTH"), "no width for a right-hand panel column, no old name");
    }

    [Test]
    public static async Task TheSideColumnIsGoneAndAPluginPanelIsASideViewOfTheRail()
    {
        using var folder = new TempFolder();
        using var fixture = new ShellFixture(folder);
        var shell = fixture.Shell;
        var host = fixture.Host;
        Check.True(typeof(ShellLayout).GetProperty("PanelsVisible") is null, "the layout has no side panels' column any more");
        var panel = new ListPanel("u2.outline", "Outline");
        panel.SetItems([new PanelItem("Main", "line 1")]);
        var conversation = new FakeConversation();
        using (host.AddPanel(panel))
        using (host.AddPanel(conversation))
        {
            Check.True(EditorRailItems.Find("u2.outline") is { Title: "Outline" } item && item.CommandId == ShellModel.SideViewCommand("u2.outline")
                       && EditorCommands.Find(item.CommandId) is not null, "the panel gets a rail item and a command (View: Outline)");
            Check.True(EditorRailItems.Find("u2.chat") is null && EditorCommands.Find(ShellModel.SideViewCommand("u2.chat")) is null,
                "a conversation panel gets none: it is a tab of the chat column");
            Check.True(await host.ExecuteCommandAsync(ShellModel.SideViewCommand("u2.outline")) && shell.Panels.SideView == "u2.outline" && shell.Panels.IsOpen(ShellPanel.Explorer),
                "its rail item shows it as the side view, in the explorer's place");
            Check.True(await host.ExecuteCommandAsync(ShellModel.SideViewCommand("u2.outline")) && !shell.Panels.IsOpen(ShellPanel.Explorer), "a second click folds the side away");
            Check.True(await host.ExecuteCommandAsync(ShellCommandIds.ToggleTree) && shell.Panels.SideView == PanelLayout.ExplorerView && shell.Panels.IsOpen(ShellPanel.Explorer),
                "the Explorer item brings the tree back");
            ((IEditorHost)host).ShowPanel("u2.outline");
            Check.Equal("u2.outline", shell.Panels.SideView, "IEditorHost.ShowPanel shows it the same way");
        }

        Check.True(EditorRailItems.Find("u2.outline") is null && EditorCommands.Find(ShellModel.SideViewCommand("u2.outline")) is null, "the panel removed: its item and command go");
        Check.Equal(PanelLayout.ExplorerView, shell.Panels.SideView, "and the explorer takes its place");
    }

    [Test]
    public static void ThePluginOutputTabIsReadOnlyAndOnePerName()
    {
        var options = new CraftOptions();
        using var host = new EditorHost(options);
        using var manager = new TerminalManager(host, () => options, new FakeClipboard());
        host.Terminals = manager;
        IEditorHost plugin = host;
        var requests = new List<TerminalViewRequest>();
        manager.ViewRequested += (_, request) => requests.Add(request);
        var output = plugin.Terminals!.OpenOutput("Agent sandbox")!;
        Check.True(manager.Terminals is [{ IsReadOnly: true, Title: "Agent sandbox" }] && output.IsOpen, "a read-only terminal tab");
        Check.True(ReferenceEquals(manager.OpenOutput("Agent sandbox"), manager.OpenOutput("Agent sandbox")) && manager.Terminals.Count == 1, "one tab per name");
        Check.True(output.Append("$ dotnet build\n  exit code 0\n"), "a plug-in appends");
        var terminal = manager.Terminals[0];
        Check.True(!terminal.HandleKey(EditorKey.A, KeyModifiers.None), "a key goes nowhere");
        terminal.SendText("rm -rf /");
        output.Show();
        Check.True(requests is [TerminalViewRequest.Reveal], "Show reveals the tab without taking the keyboard");
        manager.Close(terminal);
        Check.True(!output.IsOpen && !output.Append("more"), "closed by the user: the handle takes no more");
    }

    private static string Names(IEnumerable<FolderTreeRow> rows) => string.Join(", ", rows.Select(r => new string(' ', r.Depth) + r.Entry.Name + (r.IsExpanded ? "/" : "")));

    /// <summary>A shell over a host whose dispatch is a queue the test pumps (the UI thread), with a user settings file.</summary>
    private sealed class ShellFixture : IDisposable
    {
        private readonly ConcurrentQueue<Action> _ui = new();

        public ShellFixture(TempFolder folder, int watchMilliseconds = 150)
        {
            var user = SettingsFile.Load(folder.File("data/settings.json"));
            var settings = new CraftSettings(folder.Isolated(name => name == "CRAFT_WORKSPACE_WATCH_MS" ? watchMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : null), user);
            var recent = RecentList.Load(folder.File("data/recent.json"), 10);
            Host = new EditorHost(dispatch: _ui.Enqueue);
            Shell = new ShellModel(Host, settings, recent, Dialogs, new QuietUi());
        }

        public EditorHost Host { get; }

        public ShellModel Shell { get; }

        public Dialogs Dialogs { get; } = new();

        public void Pump()
        {
            while (_ui.TryDequeue(out var action))
            {
                action();
            }
        }

        public async Task PumpUntil(Func<bool> done, string what)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Pump();
                if (done())
                {
                    return;
                }

                if (watch.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new CheckFailedException($"{what}: not within 10 s");
                }

                await Task.Delay(5);
            }
        }

        public void Dispose()
        {
            Shell.Dispose();
            Host.Dispose();
        }
    }

    private sealed class Dialogs : IFileDialogs
    {
        public string SavePath { get; set; } = "";

        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(SavePath);
    }

    private sealed class QuietUi : IShellUi
    {
        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }

    private sealed class FakeConversation : IConversationPanel
    {
        public string Id => "u2.chat";

        public string Title => "Chat";

        public IReadOnlyList<PanelItem> Items => [];

        public string? Subtitle => null;

        public IReadOnlyList<ConversationEntry> Entries => [];

        public IReadOnlyList<ContextChip> Chips => [];

        public IReadOnlyList<PanelAction> HeaderActions => [];

        public string? Status => null;

        public string Placeholder => "";

        public bool IsBusy => false;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler<ConversationChangedEventArgs>? ConversationChanged
        {
            add { }
            remove { }
        }

        public Task SubmitAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RemoveChip(string id)
        {
        }
    }
}
