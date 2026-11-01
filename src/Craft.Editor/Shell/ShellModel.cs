// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The shell's command ids (each registered in <see cref="EditorCommands"/> by <see cref="ShellModel"/>).</summary>
public static class ShellCommandIds
{
    /// <summary>A new, untitled document (Ctrl+N).</summary>
    public const string NewFile = "craft.newFile";

    /// <summary>A new file on disk in the explorer's folder (the row's, else the workspace's), named by the user, opened.</summary>
    public const string NewFileInFolder = "craft.explorer.newFile";

    /// <summary>A new folder on disk in the explorer's folder, named by the user.</summary>
    public const string NewFolder = "craft.explorer.newFolder";

    /// <summary>Renames the file or folder of the explorer's row (its clean tabs follow it).</summary>
    public const string Rename = "craft.explorer.rename";

    /// <summary>Deletes the file or folder of the explorer's row, confirmed first (to the trash where the system has one).</summary>
    public const string Delete = "craft.explorer.delete";

    /// <summary>Shows the file or folder in the system's file manager (File Explorer on Windows).</summary>
    public const string RevealInFileManager = "craft.revealInFileManager";

    /// <summary>Opens a terminal in the row's folder (a file's: its folder).</summary>
    public const string OpenInTerminal = "craft.openInTerminal";

    /// <summary>Copies the file's full path.</summary>
    public const string CopyPath = "craft.copyPath";

    /// <summary>Copies the file's path from the workspace's folder.</summary>
    public const string CopyRelativePath = "craft.copyRelativePath";

    /// <summary>Closes every tab but the one (asking about unsaved changes).</summary>
    public const string CloseOtherTabs = "craft.closeOtherTabs";

    /// <summary>Closes the tabs to the right of the one.</summary>
    public const string CloseTabsToTheRight = "craft.closeTabsToTheRight";

    /// <summary>Shows the file's row in the explorer (its folders opened).</summary>
    public const string RevealInTree = "craft.revealInTree";

    /// <summary>The places that refer to the symbol at the caret (Shift+F12).</summary>
    public const string FindReferences = "craft.findReferences";

    /// <summary>Opens a file (Ctrl+O).</summary>
    public const string OpenFile = "craft.openFile";

    /// <summary>Opens a folder as the workspace (Ctrl+K Ctrl+O, VS Code's; Ctrl+Shift+O goes to a symbol).</summary>
    public const string OpenFolder = "craft.openFolder";

    /// <summary>Opens a recent file or folder (Ctrl+R).</summary>
    public const string OpenRecent = "craft.openRecent";

    /// <summary>Closes the workspace folder (its tabs stay).</summary>
    public const string CloseFolder = "craft.closeFolder";

    /// <summary>Saves the active document (Ctrl+S; the editor's own Ctrl+S runs it too).</summary>
    public const string Save = "craft.save";

    /// <summary>Saves the active document to a new file (Ctrl+Shift+S).</summary>
    public const string SaveAs = "craft.saveAs";

    /// <summary>Closes the active tab, asking about unsaved changes (Ctrl+W).</summary>
    public const string CloseTab = "craft.closeTab";

    /// <summary>The next tab (Ctrl+Tab).</summary>
    public const string NextTab = "craft.nextTab";

    /// <summary>The previous tab (Ctrl+Shift+Tab).</summary>
    public const string PreviousTab = "craft.previousTab";

    /// <summary>The settings page (Ctrl+Comma).</summary>
    public const string Settings = "craft.settings";

    /// <summary>Shows or hides the folder tree (Ctrl+B).</summary>
    public const string ToggleTree = "craft.toggleTree";

    /// <summary>Reads the folder tree again.</summary>
    public const string RefreshTree = "craft.refreshTree";

    /// <summary>Switches between the light and the dark theme (for this run; CRAFT_THEME sets it for good).</summary>
    public const string ToggleTheme = "craft.toggleTheme";

    // Phase D: the approved design's shell.

    /// <summary>Quick open, the title bar's search box: files by name, '&gt;' commands (Ctrl+P).</summary>
    public const string QuickOpen = "craft.quickOpen";

    /// <summary>Shows or hides the bottom panel (Ctrl+J).</summary>
    public const string TogglePanel = "craft.togglePanel";

    /// <summary>The bottom panel's Problems tab.</summary>
    public const string ShowProblems = "craft.showProblems";

    /// <summary>The bottom panel's Terminal tab (phase T).</summary>
    public const string ShowTerminal = "craft.showTerminal";

    /// <summary>The bottom panel's Output tab (the host's log).</summary>
    public const string ShowOutput = "craft.showOutput";

    /// <summary>The bottom panel's Idrak telemetry tab (the measures recorded through the host).</summary>
    public const string ShowTelemetry = "craft.showTelemetry";

    /// <summary>The Models and devices page (a tab of the editor area).</summary>
    public const string Models = "craft.models";

    /// <summary>The prefix of the command each plug-in panel's rail item runs: shows that panel as the side view ("craft.sideView." + its id).</summary>
    public const string SideViewPrefix = "craft.sideView.";

    /// <summary>The plug-in guide (where docs/plugins.md is).</summary>
    public const string PluginGuide = "craft.pluginGuide";

    /// <summary>About Craft.</summary>
    public const string About = "craft.about";

    /// <summary>The command palette (the app registers it: it is a view).</summary>
    public const string CommandPalette = "craft.commandPalette";

    /// <summary>The Plug-ins page (the app registers it: it is a view).</summary>
    public const string Plugins = "craft.plugins";

    /// <summary>The agent (phase A2's plug-in registers it; its rail item shows from then).</summary>
    public const string Agent = "craft.agent";

    /// <summary>File: Save all (Ctrl+K S).</summary>
    public const string SaveAll = "craft.saveAll";

    /// <summary>Tab: Reopen the closed tab (Ctrl+Shift+T).</summary>
    public const string ReopenClosedTab = "craft.reopenClosedTab";

    /// <summary>View: Zoom in (Ctrl+=): the code font one <c>CRAFT_ZOOM_STEP</c> larger, written to the user's settings.</summary>
    public const string ZoomIn = "craft.zoomIn";

    /// <summary>View: Zoom out (Ctrl+-).</summary>
    public const string ZoomOut = "craft.zoomOut";

    /// <summary>View: Reset the zoom (Ctrl+0): the code font's size back to the files' or the default.</summary>
    public const string ZoomReset = "craft.zoomReset";

    /// <summary>Go: Go to line (Ctrl+G): quick open with ':'.</summary>
    public const string GoToLine = "craft.goToLine";

    /// <summary>Settings: Keyboard shortcuts (Ctrl+K Ctrl+S): the settings page's Keyboard section.</summary>
    public const string KeyboardShortcuts = "craft.keyboardShortcuts";

    /// <summary>Help: Welcome: the welcome page's tab.</summary>
    public const string Welcome = "craft.welcome";

    /// <summary>View: Clear the notifications (with Undo in the inline bar).</summary>
    public const string ClearNotifications = "craft.clearNotifications";

    // The X restart, piece 3a: the split editor and peek definition.

    /// <summary>View: Split the editor right (Ctrl+\): a new group to the right showing the active document too.</summary>
    public const string SplitRight = "craft.splitRight";

    /// <summary>View: Split the editor down (Ctrl+K Ctrl+\): a new group under the focused one.</summary>
    public const string SplitDown = "craft.splitDown";

    /// <summary>View: Move the editor to the next group (Ctrl+Alt+Right), or to a new group to the right.</summary>
    public const string MoveToNextGroup = "craft.moveToNextGroup";

    /// <summary>View: Close the editor group (its tabs, asking about unsaved changes).</summary>
    public const string CloseGroup = "craft.closeGroup";

    /// <summary>View: Focus the next editor group.</summary>
    public const string FocusNextGroup = "craft.focusNextGroup";

    /// <summary>The prefix of View: Focus editor group N (Ctrl+1, Ctrl+2, Ctrl+3): "craft.focusGroup" + N.</summary>
    public const string FocusGroupPrefix = "craft.focusGroup";

    /// <summary>Go: Peek definition (Alt+F12): the definition in an overlay under the caret's line.</summary>
    public const string PeekDefinition = "craft.peekDefinition";

    // X part 7: rich context menus in the explorer and on the tabs.

    /// <summary>Opens the explorer's file (the menu's row) in the focused group.</summary>
    public const string Open = "craft.explorer.open";

    /// <summary>Opens the file in the group to the side (the next group, else a new one to the right).</summary>
    public const string OpenToSide = "craft.explorer.openToSide";

    /// <summary>The submenu "Open with": the file opened in a language picked (its rows run <see cref="OpenWithLanguage"/>).</summary>
    public const string OpenWith = "craft.menu.openWith";

    /// <summary>Opens the file in the language of the row's argument (a language's name; empty: plain text).</summary>
    public const string OpenWithLanguage = "craft.openWithLanguage";

    /// <summary>The Search view scoped to the row's folder (a file's: its folder).</summary>
    public const string FindInFolder = "craft.explorer.findInFolder";

    /// <summary>The submenu "Run": the run targets of the file or folder (its rows run <see cref="RunTarget"/>).</summary>
    public const string RunMenu = "craft.menu.run";

    /// <summary>Runs, builds or tests the run target of the row's argument ("run.target|provider:id").</summary>
    public const string RunTarget = "craft.run.menuTarget";

    /// <summary>Formats the file (opened first when it is not), as one undo step.</summary>
    public const string FormatFile = "craft.formatFile";

    /// <summary>Cuts the explorer's file or folder (pasted, it moves).</summary>
    public const string Cut = "craft.explorer.cut";

    /// <summary>Copies the explorer's file or folder (pasted, it is copied).</summary>
    public const string Copy = "craft.explorer.copy";

    /// <summary>Pastes what was cut or copied into the row's folder, off the UI thread, with Undo.</summary>
    public const string Paste = "craft.explorer.paste";

    /// <summary>Copies the file or folder beside itself ("Name copy.cs"), with Undo.</summary>
    public const string Duplicate = "craft.explorer.duplicate";

    /// <summary>Takes the file as the first of a comparison.</summary>
    public const string SelectForCompare = "craft.explorer.selectForCompare";

    /// <summary>Compares the file with the one selected for compare, in the diff view.</summary>
    public const string CompareWithSelected = "craft.explorer.compareWithSelected";

    /// <summary>Copies the file's or folder's name.</summary>
    public const string CopyName = "craft.copyName";

    /// <summary>Closes the folder and every open folder inside it, in the explorer.</summary>
    public const string CollapseFolder = "craft.explorer.collapseFolder";

    /// <summary>Closes every open folder of the explorer.</summary>
    public const string CollapseAll = "craft.explorer.collapseAll";

    /// <summary>Closes the group's tabs without unsaved changes (the pinned ones stay).</summary>
    public const string CloseSavedTabs = "craft.closeSavedTabs";

    /// <summary>Closes every tab of the group, asking about unsaved changes.</summary>
    public const string CloseAllTabs = "craft.closeAllTabs";

    /// <summary>Pins the tab (kept at the left, spared by Close others).</summary>
    public const string PinTab = "craft.pinTab";

    /// <summary>Unpins the tab.</summary>
    public const string UnpinTab = "craft.unpinTab";

    /// <summary>Compares the tab's unsaved text with its file, in the diff view (a hunk accepted goes back to the file's text).</summary>
    public const string CompareWithSaved = "craft.compareWithSaved";

    /// <summary>Takes the tab's text back to its file's, as one undo step, with Undo.</summary>
    public const string RevertFile = "craft.revertFile";

    /// <summary>Refactor: Rename the symbol at the caret (F2; Visual Studio: Ctrl+R Ctrl+R).</summary>
    public const string RenameSymbol = "craft.renameSymbol";

    /// <summary>Go: a symbol of the focused editor, quick open's '@' (Ctrl+Shift+O).</summary>
    public const string GoToSymbol = "craft.goToSymbol";
}

/// <summary>What a drop of paths onto the window or the editor opened (<see cref="ShellModel.OpenDroppedAsync"/>).</summary>
/// <param name="Files">The files opened in tabs, in order.</param>
/// <param name="Folder">The folder opened as the workspace, or null.</param>
/// <param name="Skipped">The paths not opened (missing, a second folder, or one that could not be read), each with why.</param>
public sealed record DropResult(IReadOnlyList<string> Files, string? Folder, IReadOnlyList<string> Skipped);

/// <summary>The views a shell command asks the app to show (<see cref="ShellModel.ViewRequested"/>).</summary>
public static class ShellViewNames
{
    /// <summary>The search box, focused, for quick open.</summary>
    public const string QuickOpen = "quickOpen";

    /// <summary>Quick open with ':' typed (go to a line of the active editor).</summary>
    public const string GoToLine = "quickOpen.line";

    /// <summary>Quick open with '@': the focused editor's symbols.</summary>
    public const string GoToSymbol = "quickOpen.symbol";

    /// <summary>The bottom panel shown or hidden.</summary>
    public const string TogglePanel = "panel";

    /// <summary>The bottom panel's Problems tab.</summary>
    public const string Problems = "panel.problems";

    /// <summary>The bottom panel's Terminal tab.</summary>
    public const string Terminal = "panel.terminal";

    /// <summary>The bottom panel's Output tab.</summary>
    public const string Output = "panel.output";

    /// <summary>The bottom panel's Idrak telemetry tab.</summary>
    public const string Telemetry = "panel.telemetry";

    /// <summary>The bottom panel's TODO tab (X part 9).</summary>
    public const string Todo = "panel.todo";
}

/// <summary>
/// The app's shell without a UI: the workspace folder and its tree, the tabs, the layout, the recent list, the layered
/// settings and the options built from them, and the theme. Its actions are commands in <see cref="EditorCommands"/> with
/// key bindings in <see cref="EditorKeyBindings"/> (<see cref="ShellCommandIds"/>), registered on the same public
/// contracts a plug-in uses, so they show in the command palette, a user's <c>CRAFT_KEY_BINDINGS</c> can rebind them, and
/// a plug-in can run them. What needs the user goes through <see cref="IFileDialogs"/> (the platform's) and
/// <see cref="IShellUi"/> (the prompts); its pages (settings, Models, Plug-ins) open as tabs of the editor area (<see cref="Pages"/>). Disposing it takes its commands and bindings back and closes the tabs.
/// Used on the UI thread.
/// </summary>
public sealed partial class ShellModel : IDisposable
{
    /// <summary>The settings that take effect at the next start (the plug-ins load, the host reads its bindings, at start).</summary>
    public static IReadOnlySet<string> AppliedAtStart { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CRAFT_PLUGIN_FOLDERS", "CRAFT_PLUGINS_DISABLED", "CRAFT_HOST_LOG_LINES", "CRAFT_PROPOSAL_PREVIEW_CHARS", "CRAFT_DATA_FOLDER",
        "CRAFT_CRASH_TRAIL", "CRAFT_CRASH_TRAIL_KB", "CRAFT_CRASH_TRAIL_EXCEPTIONS", "CRAFT_CRASH_RUNS_KEPT", "CRAFT_CRASH_TICKET_OFFER",
    };

    private readonly IFileDialogs _dialogs;
    private readonly IShellUi _ui;
    private readonly Func<string, CraftOptions, CancellationToken, Task<ITextDocument>> _openDocument;
    private readonly List<string> _commandIds = [];
    private readonly List<KeyBinding> _bindings = [];
    private readonly List<(string Menu, string Command)> _menuItems = [];
    private readonly List<string> _railItems = [];
    private readonly Dictionary<string, (string Command, bool Rail)> _sideViews = new(StringComparer.Ordinal);
    private readonly List<ClosedTab> _closed = [];
    private FolderTreeWatch? _treeWatch;
    private string _optionsSignature;
    private bool _disposed;
    private IDisposable? _memoryWatch;
    private int _memoryReleases;

    /// <summary>The shell over <paramref name="host"/>, its commands and bindings registered.</summary>
    /// <param name="host">The editor host (the tabs open their documents in it).</param>
    /// <param name="settings">The layered settings (the workspace's file is set when a folder opens).</param>
    /// <param name="recent">The recent list.</param>
    /// <param name="dialogs">The platform's file dialogs.</param>
    /// <param name="ui">The prompts and pages.</param>
    /// <param name="openDocument">Reads a file into a document (<see cref="TextDocument.OpenAsync"/> when null).</param>
    /// <param name="files">The system's file manager and trash (Reveal, Delete), or null when the platform has none.</param>
    /// <param name="clipboard">The clipboard (Copy path), or null when there is none.</param>
    public ShellModel(EditorHost host, CraftSettings settings, RecentList recent, IFileDialogs dialogs, IShellUi ui,
        Func<string, CraftOptions, CancellationToken, Task<ITextDocument>>? openDocument = null, IShellFiles? files = null, IEditorClipboard? clipboard = null)
    {
        _files = files;
        _clipboard = clipboard;
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(ui);
        Host = host;
        Settings = settings;
        Recent = recent;
        _dialogs = dialogs;
        _ui = ui;
        _openDocument = openDocument ?? (static async (path, options, token) => await TextDocument.OpenAsync(path, options, token).ConfigureAwait(true));
        Options = settings.BuildOptions(out var problems);
        OptionsProblems = problems;
        _optionsSignature = Signature();
        Recent.Limit = Options.RecentLimit;
        Theme = ThemeOf(Options);
        UseIconTheme(Options.IconTheme);
        Groups = new EditorGroups(host, Options.EditorGroupsMax) { CloseEmpty = Options.EditorGroupsCloseEmpty };
        Layout = new ShellLayout(Options);
        Pages = new PageTabs();
        Panels = new PanelLayout(settings, Options, (message, error) => host.Dispatch(() => host.Log(EditorLogLevel.Warning, message, error)));
        Layout.WideTreeOpen = Panels.IsOpen(ShellPanel.Explorer);
        Panels.Changed += (_, _) => Layout.WideTreeOpen = Panels.IsOpen(ShellPanel.Explorer);
        // A document chosen or opened in the first group (where the pages show) shows there again (a page tab stays open
        // beside it).
        Groups.ActiveChanged += (_, group) =>
        {
            if (ReferenceEquals(group, Groups.Groups[0]))
            {
                Pages.ShowDocument();
            }
        };
        Groups.GroupsChanged += (_, _) => KeepLayout();
        Groups.TabsChanged += (_, _) => KeepLayout();
        Groups.ActiveChanged += (_, _) => KeepLayout();
        Groups.FocusChanged += (_, _) => KeepLayout();
        Groups.LayoutResized += (_, _) => KeepLayout();
        host.DocumentSaved += OnDocumentSaved;
        host.PanelsChanged += OnPanelsChanged;
        host.PanelShowRequested += OnPanelShowRequested;
        // A plug-in's IEditorHost.OpenDocumentAsync (the agent's changed files): the file's tab, or a new document for a path
        // with no file yet (the user saves it there; the plug-in's proposal fills it).
        // The new document keeps the path, so Save writes it there (the owner's Windows run: Save on the agent's new file
        // did nothing); a second proposal for the same new file goes to the same tab.
        host.OpenFile = async (path, token) => File.Exists(path)
            ? (await OpenAsTextAsync(path, token).ConfigureAwait(true))?.HostDocument
            : (Groups.FindSuggested(path) ?? NewFile(path)).HostDocument;
        host.SetKeyBindings(Options.KeyBindings, Options.KeyMap);
        UndoBar = new UndoBar(Options.UndoBarMilliseconds, action => host.Dispatch("shell: the undo bar's time is up", action));
        // B11: what a plug-in reaches through its host: the Undo bar, the folder dialog, its settings written as the page writes them.
        host.UndoPresenter = (text, undo, kept) =>
        {
            UndoBar.Show(text, undo, kept);
            return true;
        };
        host.FolderPicker = (start, token) => _dialogs.PickFolderAsync(start, token);
        // X part 11: a plug-in's Copy all (a review's findings) through the platform's clipboard, written off the caller.
        if (clipboard is not null)
        {
            host.ClipboardWriter = text =>
            {
                _ = clipboard.SetTextAsync(text);
                return true;
            };
        }

        host.SettingWriter = StoreSettingAsync;
        RegisterCommands();
        CreateViews();
        CreateTools();
        // X part 9: bookmarks, the local history's Timeline; a save keeps a snapshot and the bookmarks' lines.
        CreateBookmarks();
        CreateTimeline();
        host.DocumentSaved += OnHistorySaved;
        host.DocumentSaved += OnBookmarksSaved;
        WatchMemory();
        WatchSourceControl();
    }

    /// <summary>The inline bar with Undo (phase X: in place of a confirmation for a change that can be taken back).</summary>
    public UndoBar UndoBar { get; }

    /// <summary>Whether the welcome page opens at start: no folder is open and <c>CRAFT_WELCOME_AT_START</c> is on.</summary>
    public bool WelcomeAtStart => Workspace is null && Options.WelcomeAtStart;

    /// <summary>Opens the welcome page's tab (Help › Welcome).</summary>
    public void ShowWelcome() => Pages.Open(ShellPageIds.Welcome, "Welcome");

    /// <summary>
    /// Clears the notification centre's messages and ended operations at once (running ones and status lines stay), with
    /// Undo in the inline bar; the number taken away.
    /// </summary>
    public int ClearNotifications()
    {
        var notifications = Host.Notifications;
        var taken = notifications.TakeAll();
        if (taken.Count > 0)
        {
            UndoBar.Show(taken.Count == 1 ? "Cleared 1 notification." : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Cleared {taken.Count} notifications."),
                () => notifications.Restore(taken));
        }

        return taken.Count;
    }

    /// <summary>
    /// Takes the recent entry <paramref name="path"/> off the list at once, with Undo in the inline bar (it goes back at
    /// its place); false when it was not listed.
    /// </summary>
    public bool RemoveRecent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        int index = -1;
        for (int i = 0; i < Recent.Entries.Count; i++)
        {
            if (PathKeys.Comparer.Equals(Recent.Entries[i].Path, full))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        var entry = Recent.Entries[index];
        Recent.Remove(full);
        UndoBar.Show($"Removed {Path.GetFileName(full)} from the recent list.", () => Recent.Restore(entry, index));
        return true;
    }

    /// <summary>
    /// Opens what was dropped onto the window or the editor (from the tree, or from the system's file manager): each file
    /// in a tab (the last active), the first folder as the workspace; the rest said in <see cref="DropResult.Skipped"/>.
    /// </summary>
    public async Task<DropResult> OpenDroppedAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = new List<string>();
        var skipped = new List<string>();
        string? folder = null;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // The checks touch the disk: off the UI thread.
            var (isFile, isFolder) = await Task.Run(() => (File.Exists(path), Directory.Exists(path)), cancellationToken);
            if (isFolder)
            {
                if (folder is null && await OpenFolderAsync(path))
                {
                    folder = Workspace?.Root;
                }
                else
                {
                    skipped.Add(folder is null ? $"{path}: the folder could not be opened" : $"{path}: one folder opens at a time ({folder} did)");
                }
            }
            else if (isFile)
            {
                if (await OpenFileAsync(path, cancellationToken: cancellationToken) is not null)
                {
                    files.Add(Path.GetFullPath(path));
                }
                else
                {
                    skipped.Add($"{path}: it could not be read");
                }
            }
            else
            {
                skipped.Add($"{path}: no such file or folder");
            }
        }

        return new DropResult(files, folder, skipped);
    }

    /// <summary>The prompts and pages (the shell's questions, a pick from a list).</summary>
    public IShellUi Ui => _ui;

    /// <summary>The section the settings page shows (a <see cref="SettingSections"/> id); <see cref="ShowSettings"/> sets it.</summary>
    public string SettingsSection { get; private set; } = SettingSections.Editor;

    /// <summary>Raised when <see cref="ShowSettings"/> asks the open settings page for another section.</summary>
    public event EventHandler? SettingsSectionChanged;

    /// <summary>The files of the tabs closed, latest last (<see cref="ReopenClosedTabAsync"/> takes them back; at most <c>CRAFT_TABS_REOPEN_LIMIT</c>).</summary>
    public IReadOnlyList<string> ClosedFiles => [.. _closed.Select(c => c.Path)];

    /// <summary>Opens the settings page's tab at <paramref name="section"/> (the one shown last when null).</summary>
    public void ShowSettings(string? section = null)
    {
        if (section is not null && !string.Equals(section, SettingsSection, StringComparison.OrdinalIgnoreCase))
        {
            SettingsSection = section;
            SettingsSectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Pages.Open(ShellPageIds.Settings, "Settings");
    }

    /// <summary>Saves every tab with unsaved changes (a new document asks where); true when every one was saved.</summary>
    public async Task<bool> SaveAllAsync(CancellationToken cancellationToken = default)
    {
        bool all = true;
        foreach (var tab in Groups.DocumentTabs())
        {
            if (tab.IsDirty && !await SaveAsync(tab, cancellationToken))
            {
                all = false;
            }
        }

        return all;
    }

    /// <summary>
    /// Opens the tab closed last again (X part 7: its file still on disk), in its group when that is still open (else the
    /// focused one), at its place, pinned again when it was, its caret and selection where they were; false when there is
    /// none. The disk is asked off the UI thread.
    /// </summary>
    public async Task<bool> ReopenClosedTabAsync(CancellationToken cancellationToken = default)
    {
        while (_closed.Count > 0)
        {
            var closed = _closed[^1];
            _closed.RemoveAt(_closed.Count - 1);
            var group = Groups.Groups.FirstOrDefault(g => g.Id == closed.GroupId) ?? Groups.Focused;
            if (group.Tabs.Find(closed.Path) is not null || !await Task.Run(() => File.Exists(closed.Path), cancellationToken))
            {
                continue;
            }

            if (await OpenInGroupAsync(group, closed.Path, activate: true, cancellationToken) is not { } tab)
            {
                return false;
            }

            if (closed.Pinned)
            {
                group.Tabs.SetPinned(tab, true);
            }

            group.Tabs.Move(tab, closed.Index);
            int length = tab.Document.Buffer.Current.Length;
            Host.Select(tab.HostDocument, new SelectionState(Math.Clamp(closed.Selection.Anchor, 0, length), Math.Clamp(closed.Selection.Caret, 0, length)));
            return true;
        }

        return false;
    }

    // A closed tab Reopen closed can bring back: its file, group, place, selection and pin.
    private sealed record ClosedTab(string Path, int GroupId, int Index, SelectionState Selection, bool Pinned);

    /// <summary>
    /// Zooms the code font: <paramref name="steps"/> times <see cref="CraftOptions.ZoomStep"/> larger (smaller when
    /// negative), written to the user's settings (the workspace's when there is no user file) and applied at once; 0 takes
    /// the user's size off, so the workspace's or the default applies. Null, or why it could not (the environment sets the size).
    /// </summary>
    public async Task<string?> ZoomAsync(int steps)
    {
        const string Variable = "CRAFT_FONT_SIZE";
        if (Settings.SourceOf(Variable) == SettingSource.Environment)
        {
            return $"The environment sets {Variable}, so the zoom cannot change it.";
        }

        var layer = Settings.User is not null ? SettingLayer.User : SettingLayer.Workspace;
        string? value = steps == 0 ? null
            : Math.Clamp(Options.FontSize + (steps * Options.ZoomStep), 1, 400).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            var write = Settings.StoreAsync(layer, Variable, value);
            ReloadOptions();
            await write.ConfigureAwait(true);
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>How many times low memory made the languages free what they can build again (<see cref="CraftOptions.MemoryMonitor"/>).</summary>
    public int MemoryReleases => Volatile.Read(ref _memoryReleases);

    /// <summary>
    /// What low memory does (the monitor calls it on the thread pool; a test or a command may call it): every language
    /// frees what it can build again (<see cref="ILanguage.ReleaseMemory"/>), the open sessions stay.
    /// </summary>
    public void OnMemoryPressure(MemoryPressureLevel level)
    {
        Interlocked.Increment(ref _memoryReleases);
        MemoryPressureMonitors.ReleaseLanguages();
        MemoryPressure?.Invoke(this, level);
    }

    /// <summary>
    /// Raised by <see cref="OnMemoryPressure"/>, on the monitor's thread (B1: the views drop what they can draw again, the
    /// kept line layouts, on the UI thread).
    /// </summary>
    public event EventHandler<MemoryPressureLevel>? MemoryPressure;

    /// <summary>Starts the options' memory monitor; an unknown name is an options problem (nothing is watched), never a crash.</summary>
    private void WatchMemory()
    {
        _memoryWatch?.Dispose();
        _memoryWatch = null;
        try
        {
            _memoryWatch = MemoryPressureMonitors.Get(Options.MemoryMonitor).Watch(OnMemoryPressure);
        }
        catch (NotRegisteredException e)
        {
            OptionsProblems = [.. OptionsProblems, $"CRAFT_MEMORY_MONITOR: {e.Message}"];
        }
    }

    /// <summary>The editor host.</summary>
    public EditorHost Host { get; }

    /// <summary>The platform's file dialogs (the Plug-ins page picks a plug-in folder with them).</summary>
    public IFileDialogs FileDialogs => _dialogs;

    /// <summary>The layered settings.</summary>
    public CraftSettings Settings { get; }

    /// <summary>The recent files and folders.</summary>
    public RecentList Recent { get; }

    /// <summary>The focused editor group's tabs (<see cref="EditorGroups.Focused"/>).</summary>
    public DocumentTabs Tabs => Groups.Focused.Tabs;

    /// <summary>The editor groups (the split editor): each its own tabs, one focused.</summary>
    public EditorGroups Groups { get; }

    /// <summary>The open documents' language sessions, one per document shared by its views (the split editor).</summary>
    public LanguageSessionPool Sessions { get; } = new();

    /// <summary>The layout and what shows in it.</summary>
    public ShellLayout Layout { get; }

    /// <summary>The pages open as tabs of the editor area (settings, Models and devices, Plug-ins).</summary>
    public PageTabs Pages { get; }

    /// <summary>The panels' open states and sizes (kept per workspace) and the rail's side view.</summary>
    public PanelLayout Panels { get; }

    /// <summary>What keeps the tree following the disk while a folder is open, or null (none, or <c>CRAFT_WORKSPACE_WATCHER</c> is "none" or not registered).</summary>
    public FolderTreeWatch? TreeWatch => _treeWatch;

    /// <summary>The command the rail item of plug-in panel <paramref name="panelId"/> runs (it shows the panel as the side view).</summary>
    public static string SideViewCommand(string panelId) => ShellCommandIds.SideViewPrefix + panelId;

    /// <summary>The open folder, or null.</summary>
    public Workspace? Workspace { get; private set; }

    /// <summary>The open folder's tree, or null.</summary>
    public FolderTree? Tree { get; private set; }

    /// <summary>The options in effect (the settings' layers over the defaults).</summary>
    public CraftOptions Options { get; private set; }

    /// <summary>
    /// The editor showing a tab, or null (the app sets it: its view's model): Save formats through it first when
    /// <see cref="CraftOptions.FormatOnSave"/> is on, so the format is the editor's one undo step.
    /// </summary>
    public Func<DocumentTab, EditorModel?>? EditorOf { get; set; }

    /// <summary>What did not parse in the settings when the options were last built (each names its variable and source).</summary>
    public IReadOnlyList<string> OptionsProblems { get; private set; }

    /// <summary>The theme in use.</summary>
    public ITheme Theme { get; private set; }

    /// <summary>Raised after a folder opened or closed.</summary>
    public event EventHandler? WorkspaceChanged;

    /// <summary>Raised after <see cref="Options"/> changed (a setting written, a workspace's settings read).</summary>
    public event EventHandler? OptionsChanged;

    /// <summary>Raised after <see cref="Theme"/> changed.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Raised when a command asks the app for a view (<see cref="ShellViewNames"/>).</summary>
    public event EventHandler<string>? ViewRequested;

    /// <summary>The branch of the open folder's git repository, or null (read when the folder opens).</summary>
    public string? Branch { get; private set; }

    /// <summary>The settings page's rows: every Craft option, then every plug-in's declared setting.</summary>
    public IReadOnlyList<SettingRow> SettingRows() =>
        Settings.Rows(Host.Settings.All.Select(p => new CraftVariable(p.Variable, p.Setting.Default, $"{p.Plugin}: {p.Setting.Description}") { Shape = p.Setting.Shape ?? PluginSettingShapes.Infer(p.Setting, SettingSections.Plugins) }));

    /// <summary>
    /// Opens the folder <paramref name="path"/> as the workspace (in place of the open one; the tabs stay): its
    /// <c>.craft/settings.json</c> read into the settings, the tree listed, the folder put first in the recent list.
    /// False, with a message, when it cannot be opened.
    /// </summary>
    public async Task<bool> OpenFolderAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Workspace workspace;
        SettingsFile workspaceSettings;
        string? branch;
        bool layoutKept;
        try
        {
            // The folder's checks and files (its .craft/settings.json, its git branch) are read on the thread pool: the
            // UI thread only takes what they say (the owner's start-up stalls).
            var options = Options;
            // B10: opening a folder is workspace work, so the window's loader shows it when it takes a while.
            using var activity = Host.Activities.Start($"Opening {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}", ActivityKinds.Workspace, keep: false);
            (workspace, workspaceSettings, branch, layoutKept) = await Task.Run(() =>
            {
                var opened = new Workspace(path, options);
                return (opened, SettingsFile.Load(CraftSettings.WorkspaceFilePath(opened.Root)), GitBranch.Read(opened.Root),
                    File.Exists(new EditorLayoutFile(opened.Root).FilePath));
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _ui.ShowMessageAsync("Craft: could not open the folder", $"{path}: {e.Message}");
            return false;
        }

        StopWatching();
        Workspace?.Dispose();
        Settings.Workspace = workspaceSettings;
        ReloadOptions();

        // Made again with the options that now include the workspace's own (its CRAFT_WORKSPACE_IGNORE applies to its tree);
        // a workspace lists nothing until asked, so this costs nothing.
        Workspace = new Workspace(workspace.Root, Options);
        Host.WorkspaceFolder = Workspace.Root;
        UseLayoutFile(Workspace.Root, layoutKept);
        Tree = new FolderTree(Workspace);
        Watch();
        Recent.Add(Workspace.Root, RecentKind.Folder);
        Layout.HasFolder = true;
        Branch = branch;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        _ = PrepareLanguageSelectorsAsync(Workspace.Root);
        return true;
    }

    /// <summary>
    /// The language of <paramref name="path"/> in the open workspace (W, contract 4): a language selector's answer for the
    /// workspace first, else by its name (<see cref="EditorLanguages.ForFile(string, string?)"/>).
    /// </summary>
    public ILanguage? LanguageFor(string path) => EditorLanguages.ForFile(path, Workspace?.Root);

    /// <summary>
    /// Gives every open tab the language its file has now (<see cref="LanguageFor"/>): after the language selectors learned
    /// the workspace, or a plug-in registered a language. Only a tab whose language changed is touched.
    /// </summary>
    public void RepickLanguages()
    {
        foreach (var tab in Groups.AllTabs.ToArray())
        {
            if (tab.FilePath is { } path && LanguageFor(path) is var language && !ReferenceEquals(language, tab.Language))
            {
                Groups.SetLanguage(tab, language);
            }
        }
    }

    // The language selectors read what they need of the folder off the UI thread (W, contract 4); the open tabs take
    // their languages again when they did, if the folder is still the one open.
    private async Task PrepareLanguageSelectorsAsync(string root)
    {
        if (LanguageSelectors.All.Count == 0)
        {
            return;
        }

        var problems = await Task.Run(() => LanguageSelectors.PrepareAsync(root)).ConfigureAwait(true);
        foreach (var problem in problems)
        {
            Host.Log(EditorLogLevel.Warning, $"A language selector could not read the folder: {problem}.");
        }

        if (Workspace is { } open && PathKeys.Comparer.Equals(open.Root, root))
        {
            RepickLanguages();
        }
    }

    /// <summary>Closes the workspace folder (its settings no longer apply; the tabs stay open).</summary>
    public void CloseFolder()
    {
        if (Workspace is null)
        {
            return;
        }

        StopWatching();
        Workspace.Dispose();
        Workspace = null;
        Host.WorkspaceFolder = null;
        UseLayoutFile(null, false);
        Tree = null;
        Settings.Workspace = null;
        ReloadOptions();
        Layout.HasFolder = false;
        Branch = null;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the file <paramref name="path"/> in a tab (the open one when it is already open), in the language its name
    /// registers (plain text otherwise), puts it first in the recent list, and makes it active; null, with a message, when
    /// it cannot be read. <paramref name="fromTree"/> tells the narrow layout to go back to the editor.
    /// </summary>
    public async Task<DocumentTab?> OpenFileAsync(string path, bool fromTree = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var tab = Tabs.Find(full);
        if (tab is null && Groups.FindOpen(full) is null && OpensAsImage(full))
        {
            // X part 9: an image opens in its preview tab (Image: Open as text opens an SVG's source).
            await OpenImageAsync(full);
            return null;
        }

        if (tab is null && Groups.FindOpen(full) is { } elsewhere)
        {
            // Open in another group: shown here too, one document (the split editor).
            tab = Groups.Focused.Tabs.AddShared(elsewhere, activate: false, takeView: false);
        }

        if (tab is null)
        {
            ITextDocument document;
            try
            {
                document = await _openDocument(full, Options, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
            {
                await _ui.ShowMessageAsync("Craft: could not open", $"{full}: {e.Message}");
                return null;
            }

            // Opened meanwhile (a second click while the first read): keep the first.
            if (Tabs.Find(full) is { } opened)
            {
                document.Dispose();
                tab = opened;
            }
            else if (Groups.FindOpen(full) is { } openedElsewhere)
            {
                document.Dispose();
                tab = Groups.Focused.Tabs.AddShared(openedElsewhere, activate: false, takeView: false);
            }
            else
            {
                tab = Tabs.Add(document, LanguageFor(full), activate: false);
            }
        }

        Tabs.Activate(tab);
        Recent.Add(full, RecentKind.File);
        if (fromTree)
        {
            Layout.FileOpenedFromTree();
        }

        return tab;
    }

    /// <summary>A new, empty, untitled document (plain text until it is saved under a name) in a new active tab.</summary>
    public DocumentTab NewFile() => Tabs.Add(TextDocument.Create("", Options), null);

    /// <summary>
    /// A new, empty document made for the file <paramref name="path"/>, not written yet (<see cref="DocumentTab.SuggestedPath"/>):
    /// titled with its name, in its language, and saved there by <see cref="SaveAsync"/> without asking.
    /// </summary>
    public DocumentTab NewFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Tabs.AddFor(TextDocument.Create("", Options), path, LanguageFor(path));
    }

    /// <summary>
    /// Saves <paramref name="tab"/> (the active one when null): to its file, or, for a new document, where the user picks.
    /// False when it was not saved (no tab, cancelled, or failed, said so).
    /// </summary>
    public async Task<bool> SaveAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        if (tab.FilePath is null && tab.SuggestedPath is { } suggested)
        {
            return await WriteNewAsync(tab, suggested, cancellationToken);
        }

        if (tab.FilePath is null)
        {
            return await SaveAsAsync(tab, cancellationToken);
        }

        return await WriteAsync(tab, null, cancellationToken);
    }

    /// <summary>Saves <paramref name="tab"/> (the active one when null) to a file the user picks; its language follows the new name.</summary>
    public async Task<bool> SaveAsAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        var target = tab.FilePath ?? tab.SuggestedPath;
        var suggested = target is not null ? Path.GetFileName(target) : tab.Title;
        var folder = target is not null ? ExistingFolder(Path.GetDirectoryName(target)) ?? Workspace?.Root : Workspace?.Root;
        if (await PickAsync("the save dialog", () => _dialogs.PickSavePathAsync(suggested, folder, cancellationToken)) is not { } path)
        {
            return false;
        }

        return await WriteNewAsync(tab, Path.GetFullPath(path), cancellationToken);
    }

    // Writes a tab to a file it may not have yet (its folders made), then gives it that file's language.
    private async Task<bool> WriteNewAsync(DocumentTab tab, string path, CancellationToken cancellationToken)
    {
        if (!await WriteAsync(tab, path, cancellationToken))
        {
            return false;
        }

        var language = LanguageFor(path);
        if (!ReferenceEquals(language, tab.Language))
        {
            Groups.SetLanguage(tab, language);
        }

        return true;
    }

    // The nearest folder of `folder` that exists (a new file's folders are made when it is saved), or null.
    private static string? ExistingFolder(string? folder)
    {
        while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return string.IsNullOrEmpty(folder) ? null : folder;
    }

    // A platform dialog that fails to open is said, never a silent nothing (a command's exception is only logged).
    private async Task<string?> PickAsync(string what, Func<Task<string?>> pick)
    {
        try
        {
            return await pick();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Whatever the platform's dialog threw, the user is told and the editor goes on.
        catch (Exception e)
#pragma warning restore CA1031
        {
            await _ui.ShowMessageAsync("Craft: " + what + " did not open", $"{e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Closes <paramref name="tab"/> (the active one when null), first asking what to do with its unsaved changes: save
    /// (a new document asks where), discard, or keep it open. True when it closed. With no tab given while a page shows,
    /// the page's tab closes.
    /// </summary>
    public async Task<bool> CloseTabAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        if (tab is null && Pages.Active is { } page)
        {
            return Pages.Close(page.Id);
        }

        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        // A document another group still shows keeps its changes: only its last tab asks.
        if (tab.IsDirty && !Groups.IsShared(tab) && !await ResolveUnsavedAsync(tab, cancellationToken))
        {
            return false;
        }

        if (Groups.GroupOf(tab) is null)
        {
            return false;
        }

        if (tab.FilePath is { } closed && !Groups.IsShared(tab) && Groups.GroupOf(tab) is { } closedIn)
        {
            _closed.RemoveAll(c => PathKeys.Comparer.Equals(c.Path, closed));
            if (Options.TabsReopenLimit > 0)
            {
                _closed.Add(new ClosedTab(closed, closedIn.Id, closedIn.Tabs.IndexOf(tab), tab.HostDocument.Selection, tab.IsPinned));
                if (_closed.Count > Options.TabsReopenLimit)
                {
                    _closed.RemoveRange(0, _closed.Count - Options.TabsReopenLimit);
                }
            }
        }

        Groups.Remove(tab);
        return true;
    }

    /// <summary>
    /// Before the window closes: asks about each tab with unsaved changes in turn (each shown first); true when every one
    /// was saved or discarded, false as soon as one is kept open (the window stays).
    /// </summary>
    public async Task<bool> ConfirmCloseAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var tab in Groups.DocumentTabs())
        {
            if (!tab.IsDirty)
            {
                continue;
            }

            Groups.GroupOf(tab)?.Tabs.Activate(tab);
            if (!await ResolveUnsavedAsync(tab, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lists the recent files and folders and opens the one picked; a missing one is taken off the list, said so.</summary>
    public async Task OpenRecentAsync(CancellationToken cancellationToken = default)
    {
        var entries = Recent.Entries.ToArray();
        if (entries.Length == 0)
        {
            await _ui.ShowMessageAsync("Recent", Recent.Limit == 0 ? "The recent list is off (CRAFT_RECENT_LIMIT is 0)." : "Nothing opened yet.");
            return;
        }

        var items = entries.Select(e => (e.Kind == RecentKind.Folder ? "Folder: " : "File: ") + e.Path).ToArray();
        int picked = await _ui.PickAsync("Open a recent file or folder", items, cancellationToken);
        if (picked < 0 || picked >= entries.Length)
        {
            return;
        }

        var entry = entries[picked];
        bool exists = entry.Kind == RecentKind.Folder ? Directory.Exists(entry.Path) : File.Exists(entry.Path);
        if (!exists)
        {
            Recent.Remove(entry.Path);
            await _ui.ShowMessageAsync("Recent", $"{entry.Path} no longer exists; it was taken off the list.");
            return;
        }

        if (entry.Kind == RecentKind.Folder)
        {
            await OpenFolderAsync(entry.Path);
        }
        else
        {
            await OpenFileAsync(entry.Path, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="variable"/> into <paramref name="layer"/>'s settings file (null
    /// or empty removes it) and applies the options; null when done, else why not (a file that may not set it, a value
    /// that does not parse, a file that could not be written).
    /// </summary>
    public string? ApplySetting(SettingLayer layer, string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        try
        {
            Settings.Set(layer, variable, value);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }

        ReloadOptions();
        SettingApplied?.Invoke(this, variable);
        return null;
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="variable"/> as <see cref="ApplySetting"/> does, into the user's
    /// settings (the workspace's when there is no user file), the file written on the thread pool (the value held at once):
    /// a plug-in's <see cref="IPluginSettings.SetAsync"/>. Null when written, else why not.
    /// </summary>
    public async Task<string?> StoreSettingAsync(string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (Settings.SourceOf(variable) == SettingSource.Environment)
        {
            return $"The environment sets {variable}, which overrides the settings: change it there.";
        }

        var layer = Settings.User is not null ? SettingLayer.User : SettingLayer.Workspace;
        try
        {
            var write = Settings.StoreAsync(layer, variable, value);
            ReloadOptions();
            SettingApplied?.Invoke(this, variable);
            await write.ConfigureAwait(true);
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Raised after <see cref="ApplySetting"/> wrote a variable (its name): a plug-in's setting too, which changes no
    /// option (the composer's model chip and the status bar's model item read the AI plug-in's settings again).
    /// </summary>
    public event EventHandler<string>? SettingApplied;

    /// <summary>Builds the options from the settings again; raises <see cref="OptionsChanged"/> (and <see cref="ThemeChanged"/>) when they changed.</summary>
    public void ReloadOptions()
    {
        var options = Settings.BuildOptions(out var problems);
        OptionsProblems = problems;
        var signature = Signature();
        if (signature == _optionsSignature)
        {
            return;
        }

        _optionsSignature = signature;
        bool themeChanged = !string.Equals(options.Theme, Options.Theme, StringComparison.OrdinalIgnoreCase) || options.Accent != Options.Accent;
        bool monitorChanged = !string.Equals(options.MemoryMonitor, Options.MemoryMonitor, StringComparison.OrdinalIgnoreCase);
        bool iconsChanged = UseIconTheme(options.IconTheme);
        Options = options;
        if (monitorChanged)
        {
            WatchMemory();
        }

        Recent.Limit = options.RecentLimit;
        Groups.Max = options.EditorGroupsMax;
        Groups.CloseEmpty = options.EditorGroupsCloseEmpty;
        UndoBar.Milliseconds = options.UndoBarMilliseconds;
        Host.SetKeyBindings(options.KeyBindings, options.KeyMap);
        Layout.Options = options;
        Panels.Load(options);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        if (themeChanged)
        {
            SetTheme(ThemeOf(options));
        }
        else if (iconsChanged)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);      // every view draws its icons again
        }
    }

    // W: CRAFT_ICON_THEME picks the icon theme in use; a name no plug-in registered keeps Craft's own, said in the log.
    // True when the theme in use changed.
    private bool UseIconTheme(string name)
    {
        var before = FileIconThemes.Current;
        if (FileIconThemes.Find(name) is null)
        {
            Host.Log(EditorLogLevel.Warning, $"CRAFT_ICON_THEME: the icon theme '{name}' is not registered (registered: {string.Join(", ", FileIconThemes.Names)}); Craft's own is used.");
            name = FileIconThemes.Craft;
        }

        FileIconThemes.Use(name);
        return !string.Equals(before, FileIconThemes.Current, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Switches between the light and the dark theme for this run.</summary>
    public void ToggleTheme() => SetTheme(Accented(EditorThemes.Get(Theme.IsDark ? EditorThemes.Light : EditorThemes.Dark), Options));

    /// <summary>Takes the shell's commands and bindings back and closes every tab (unsaved changes are lost: ask first).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnwatchSourceControl();
        UndoBar.Dispose();
        foreach (var binding in _bindings)
        {
            // Only while the chord is still the shell's (a later registration, a plug-in's, keeps its own).
            if ((binding.Then is { } then ? EditorKeyBindings.Find(binding.Chord, then) : EditorKeyBindings.Find(binding.Chord)) is { } current && ReferenceEquals(current, binding))
            {
                EditorKeyBindings.Unregister(binding);
            }
        }

        foreach (var id in _commandIds)
        {
            EditorCommands.Unregister(id);
        }

        foreach (var (menu, command) in _menuItems)
        {
            EditorMenus.Unregister(menu, command);
        }

        foreach (var rail in _railItems)
        {
            EditorRailItems.Unregister(rail);
        }

        foreach (var (command, rail) in _sideViews.Values)
        {
            EditorCommands.Unregister(command);
            if (rail)
            {
                EditorRailItems.Unregister(command[ShellCommandIds.SideViewPrefix.Length..]);
            }
        }

        _sideViews.Clear();
        Host.DocumentSaved -= OnDocumentSaved;
        Host.DocumentSaved -= OnHistorySaved;
        Host.DocumentSaved -= OnBookmarksSaved;
        Host.PanelsChanged -= OnPanelsChanged;
        Host.PanelShowRequested -= OnPanelShowRequested;
        DisposeTools();
        DisposeBookmarks();                                 // after the panels' event: their removal registers no side view again
        DisposeTimeline();
        DisposeViews();
        DisposeContext();
        StopWatching();
        _memoryWatch?.Dispose();
        Pages.CloseAll();
        Groups.Dispose();
        Workspace?.Dispose();
    }

    // The tree follows the disk (phase U2): the options' watcher; an unknown one is an options problem, never a crash.
    private void Watch()
    {
        StopWatching();
        if (Tree is not { } tree)
        {
            return;
        }

        try
        {
            _treeWatch = new FolderTreeWatch(tree, FolderWatchers.Get(Options.WorkspaceWatcher), Options.WorkspaceWatchMilliseconds, Host.Dispatch);
            _treeWatch.EntryChanged += OnEntryChanged;
        }
        catch (NotRegisteredException e)
        {
            OptionsProblems = [.. OptionsProblems, $"CRAFT_WORKSPACE_WATCHER: {e.Message}"];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            Host.Log(EditorLogLevel.Warning, $"The folder tree does not follow the disk ({e.Message}); Refresh reads it again.", e);
        }
    }

    private void StopWatching()
    {
        _treeWatch?.Dispose();
        _treeWatch = null;
    }

    // Any save through the host (the editor's, a plug-in's accepted new file): the tree shows the file at once, and the
    // tab takes the language of the file's name.
    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.Document.FilePath is not { } path)
        {
            return;
        }

        _treeWatch?.Touch(path);
        if (IsConfigurationFile(path))
        {
            OnConfigurationFileChanged(path);
        }
        else
        {
            FileSettingsChanged?.Invoke(this, new FileSettingsChangedEventArgs(path));
        }

        if (Groups.AllTabs.FirstOrDefault(t => ReferenceEquals(t.HostDocument, e.Document)) is { } tab && LanguageFor(path) is var language && !ReferenceEquals(language, tab.Language))
        {
            Groups.SetLanguage(tab, language);
        }
    }

    // Each plug-in panel that is not a conversation is a side view of the rail (phase U2: there is no fixed column of
    // panels): a command shows it, and a rail item runs that command unless the plug-in registered a rail item of the
    // panel's id itself.
    private void OnPanelsChanged(object? sender, EventArgs e)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in Host.Panels)
        {
            if (panel is IConversationPanel)
            {
                continue;
            }

            present.Add(panel.Id);
            if (_sideViews.ContainsKey(panel.Id))
            {
                continue;
            }

            string id = panel.Id;
            string command = SideViewCommand(id);
            EditorCommands.Register(new EditorCommand(command, $"View: {panel.Title}", (_, _) => Panels.ToggleSide(id), null, $"The plug-in panel '{panel.Title}' as the side view"));
            bool rail = EditorRailItems.Find(id) is null;
            if (rail)
            {
                EditorRailItems.Register(new RailItem(id, panel.Title, command, RailIcons.Panel, 20));
            }

            _sideViews[id] = (command, rail);
        }

        foreach (var gone in _sideViews.Keys.Where(k => !present.Contains(k)).ToArray())
        {
            var (command, rail) = _sideViews[gone];
            EditorCommands.Unregister(command);
            if (rail)
            {
                EditorRailItems.Unregister(gone);
            }

            _sideViews.Remove(gone);
            Panels.Forget(gone);
        }
    }

    private void OnPanelShowRequested(object? sender, string panelId)
    {
        if (_sideViews.ContainsKey(panelId) && !Layout.IsNarrow)
        {
            _ = Panels.ShowSide(panelId);
        }
    }

    private static ITheme ThemeOf(CraftOptions options) => Accented(EditorThemes.Find(options.Theme) ?? EditorThemes.Get(EditorThemes.Dark), options);

    // CRAFT_ACCENT over the theme's own accent.
    private static ITheme Accented(ITheme theme, CraftOptions options) => options.Accent is { } accent ? EditorThemes.WithAccent(theme, accent) : theme;

    private void SetTheme(ITheme theme)
    {
        if (ReferenceEquals(theme, Theme))
        {
            return;
        }

        Theme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    // The values in effect of every Craft option: equal signatures, equal options (the lists inside compare by reference).
    private string Signature()
    {
        var text = new StringBuilder();
        foreach (var variable in CraftOptions.Variables)
        {
            text.Append(Settings.Read(variable.Name)).Append('\u0001');
        }

        return text.ToString();
    }

    private async Task<bool> WriteAsync(DocumentTab tab, string? path, CancellationToken cancellationToken)
    {
        if (Options.FormatOnSave && EditorOf?.Invoke(tab) is { CanFormat: true } editor)
        {
            // Off the UI thread, applied on it as one undo step; a malformed file is saved as it is (the format says why).
            // W: the project's own formatter (Prettier) first when it applies, told the run is automatic.
            await editor.FormatDocumentAsync(automatic: true, cancellationToken);
        }

        // X part 9: the trailing white space and the final newline (the file's .editorconfig over the options), a new file's charset.
        await ApplySaveActionsAsync(tab, path, cancellationToken);

        try
        {
            if (path is not null && Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);              // a new file's folders (the agent's new file in a new folder)
            }

            await Host.SaveAsync(tab.HostDocument, path, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or EncoderFallbackException or InvalidOperationException)
        {
            await _ui.ShowMessageAsync("Craft: could not save", $"{path ?? tab.FilePath}: {e.Message}");
            return false;
        }

        if (tab.FilePath is { } saved)
        {
            Recent.Add(saved, RecentKind.File);
        }

        return true;
    }

    private async Task<bool> ResolveUnsavedAsync(DocumentTab tab, CancellationToken cancellationToken) =>
        await _ui.AskUnsavedAsync(tab.Title, cancellationToken) switch
        {
            UnsavedChoice.Save => await SaveAsync(tab, cancellationToken),
            UnsavedChoice.Discard => true,
            _ => false,
        };

    private void RegisterCommands()
    {
        Command(ShellCommandIds.NewFile, "File: New file", (_, _) => { NewFile(); return Task.CompletedTask; }, null, "Ctrl+N");
        Command(ShellCommandIds.OpenFile, "File: Open a file", async (_, token) =>
        {
            var start = Tabs.Active?.FilePath is { } active ? Path.GetDirectoryName(active) : Workspace?.Root;
            if (await PickAsync("the open dialog", () => _dialogs.PickFileAsync(start, token)) is { } path)
            {
                await OpenFileAsync(path, cancellationToken: token);
            }
        }, null, "Ctrl+O");
        Command(ShellCommandIds.OpenFolder, "File: Open a folder", async (_, token) =>
        {
            if (await PickAsync("the folder dialog", () => _dialogs.PickFolderAsync(Workspace?.Root, token)) is { } path)
            {
                await OpenFolderAsync(path);
            }
        }, null, "Ctrl+K Ctrl+O");
        Command(ShellCommandIds.OpenRecent, "File: Open a recent file or folder", (_, token) => OpenRecentAsync(token), null, "Ctrl+R");
        Command(ShellCommandIds.CloseFolder, "File: Close the folder", (_, _) => { CloseFolder(); return Task.CompletedTask; }, _ => Workspace is not null, null);
        Command(ShellCommandIds.Save, "File: Save", (_, token) => SaveAsync(null, token), _ => Tabs.Active is not null, "Ctrl+S");
        Command(ShellCommandIds.SaveAs, "File: Save as", (_, token) => SaveAsAsync(null, token), _ => Tabs.Active is not null, "Ctrl+Shift+S");
        Command(ShellCommandIds.CloseTab, "Tab: Close", (_, token) => CloseTabAsync(EditorContextMenus.Target is { Item: DocumentTab target } ? target : null, token), _ => Tabs.Active is not null || Pages.Active is not null, "Ctrl+W");
        Command(ShellCommandIds.NextTab, "Tab: Next", (_, _) => { Tabs.Next(); return Task.CompletedTask; }, _ => Tabs.Tabs.Count > 1, "Ctrl+Tab");
        Command(ShellCommandIds.PreviousTab, "Tab: Previous", (_, _) => { Tabs.Previous(); return Task.CompletedTask; }, _ => Tabs.Tabs.Count > 1, "Ctrl+Shift+Tab");
        Command(ShellCommandIds.Settings, "Settings: Open", (_, _) => { ShowSettings(); return Task.CompletedTask; }, null, "Ctrl+Comma");
        Command(ShellCommandIds.KeyboardShortcuts, "Settings: Keyboard shortcuts", (_, _) => { ShowSettings(SettingSections.Keyboard); return Task.CompletedTask; }, null, "Ctrl+K Ctrl+S");
        // Note 25: a command per section ("craft.settings.chat"), so a plug-in links to its settings ("the chat is off").
        foreach (var (section, sectionTitle) in SettingSections.All)
        {
            if (section != SettingSections.Keyboard)
            {
                string id = section;
                Command(SettingSections.CommandOf(section), "Settings: " + sectionTitle, (_, _) => { ShowSettings(id); return Task.CompletedTask; }, null, null);
            }
        }

        Command(ShellCommandIds.SaveAll, "File: Save all", (_, token) => SaveAllAsync(token), _ => Tabs.HasUnsaved, "Ctrl+K S");
        Command(ShellCommandIds.ReopenClosedTab, "Tab: Reopen the closed tab", (_, token) => ReopenClosedTabAsync(token), _ => _closed.Count > 0, "Ctrl+Shift+T",
            _ => Options.TabsReopenLimit == 0 ? "CRAFT_TABS_REOPEN_LIMIT is 0: closed tabs are not kept." : "No tab was closed yet.");
        Command(ShellCommandIds.ZoomIn, "View: Zoom in", (_, _) => ZoomCommandAsync(1), null, "Ctrl+=");
        Command(ShellCommandIds.ZoomOut, "View: Zoom out", (_, _) => ZoomCommandAsync(-1), null, "Ctrl+-");
        Command(ShellCommandIds.ZoomReset, "View: Reset the zoom", (_, _) => ZoomCommandAsync(0), null, "Ctrl+0");
        Command(ShellCommandIds.GoToLine, "Go: Go to line", (_, _) => View(ShellViewNames.GoToLine), _ => Tabs.Active is not null, "Ctrl+G");
        Command(ShellCommandIds.ToggleTree, "View: Show or hide the folder tree", (_, _) => ToggleTree(), _ => Workspace is not null || !Layout.IsNarrow, "Ctrl+B");
        Command(ShellCommandIds.RefreshTree, "View: Refresh the folder tree", (_, token) => RefreshTreeAsync(token), _ => Tree is not null, null, NoFolder);
        Command(ShellCommandIds.ToggleTheme, "View: Switch light and dark", (_, _) => { ToggleTheme(); return Task.CompletedTask; }, null, null);
        Command(ShellCommandIds.QuickOpen, "Go: Open a file by name ('>' for a command)", (_, _) => View(ShellViewNames.QuickOpen), null, "Ctrl+P");
        Command(ShellCommandIds.TogglePanel, "View: Show or hide the bottom panel", (_, _) => { _ = Panels.ToggleAsync(ShellPanel.Bottom); return View(ShellViewNames.TogglePanel); }, null, "Ctrl+J");
        Command(ShellCommandIds.ShowProblems, "View: Problems", (_, _) => View(ShellViewNames.Problems), null, null);
        Command(ShellCommandIds.ShowTerminal, "View: Terminal", (_, _) => View(ShellViewNames.Terminal), null, null);
        Command(ShellCommandIds.ShowOutput, "View: Output", (_, _) => View(ShellViewNames.Output), null, null);
        Command(ShellCommandIds.ShowTelemetry, "View: Idrak telemetry", (_, _) => View(ShellViewNames.Telemetry), null, null);
        Command(ShellCommandIds.Models, "Settings: Models and devices", (_, _) => { Pages.Open(ShellPageIds.Models, "Models and devices"); return Task.CompletedTask; }, null, null);
        Command(ShellCommandIds.PluginGuide, "Help: Writing a plug-in", (_, _) => _ui.ShowMessageAsync("Writing a Craft plug-in",
            "The guide is docs/plugins.md in Craft's repository; the template is templates/craft-plugin (dotnet new craft-plugin). " +
            "A plug-in adds commands, key bindings, menu items (EditorMenus), rail items (EditorRailItems), status items, panels, settings, languages and themes."), null, null);
        Command(ShellCommandIds.Welcome, "Help: Welcome", (_, _) => { ShowWelcome(); return Task.CompletedTask; }, null, null);
        Command(ShellCommandIds.ClearNotifications, "View: Clear the notifications", (_, _) => { ClearNotifications(); return Task.CompletedTask; }, null, null);
        RegisterGroupCommands();
        RegisterSymbolCommands();
        Command(ShellCommandIds.About, "Help: About Craft", (_, _) => _ui.ShowMessageAsync("Craft",
            $"A code editor on Idrak. Plug-in contracts {CraftContracts.Version}; .NET {Environment.Version}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}."), null, null);

        // The title bar's menus (docs/design/Main.dc.html): every item runs a command, so a plug-in adds its own the same way.
        Menu(EditorMenus.File, ShellCommandIds.NewFile, 0, 0);
        Menu(EditorMenus.File, ShellCommandIds.OpenFile, 0, 1);
        Menu(EditorMenus.File, ShellCommandIds.OpenFolder, 0, 2);
        Menu(EditorMenus.File, ShellCommandIds.OpenRecent, 0, 3);
        Menu(EditorMenus.File, ShellCommandIds.Save, 1, 0);
        Menu(EditorMenus.File, ShellCommandIds.SaveAs, 1, 1);
        Menu(EditorMenus.File, ShellCommandIds.SaveAll, 1, 2);
        Menu(EditorMenus.File, ShellCommandIds.KeyboardShortcuts, 3, 2);
        Menu(EditorMenus.File, ShellCommandIds.CloseTab, 2, 0);
        Menu(EditorMenus.File, ShellCommandIds.CloseFolder, 2, 1);
        Menu(EditorMenus.File, ShellCommandIds.Settings, 3, 0);
        Menu(EditorMenus.File, ShellCommandIds.Models, 3, 1);
        Menu(EditorMenus.View, ShellCommandIds.QuickOpen, 0, 0);
        Menu(EditorMenus.View, ShellCommandIds.CommandPalette, 0, 1);
        Menu(EditorMenus.View, ShellCommandIds.ToggleTree, 1, 0);
        Menu(EditorMenus.View, ShellCommandIds.RefreshTree, 1, 1);
        Menu(EditorMenus.View, ShellCommandIds.TogglePanel, 2, 0);
        Menu(EditorMenus.View, ShellCommandIds.ShowProblems, 2, 1);
        Menu(EditorMenus.View, ShellCommandIds.ShowTerminal, 2, 2);
        Menu(EditorMenus.View, ShellCommandIds.ShowOutput, 2, 3);
        Menu(EditorMenus.View, ShellCommandIds.ShowTelemetry, 2, 4);
        Menu(EditorMenus.View, ShellCommandIds.NextTab, 3, 0);
        Menu(EditorMenus.View, ShellCommandIds.PreviousTab, 3, 1);
        Menu(EditorMenus.View, ShellCommandIds.ToggleTheme, 4, 0);
        Menu(EditorMenus.View, ShellCommandIds.ZoomIn, 4, 1);
        Menu(EditorMenus.View, ShellCommandIds.ZoomOut, 4, 2);
        Menu(EditorMenus.View, ShellCommandIds.ZoomReset, 4, 3);
        Menu(EditorMenus.View, ShellCommandIds.SplitRight, 5, 0);
        Menu(EditorMenus.View, ShellCommandIds.SplitDown, 5, 1);
        Menu(EditorMenus.View, ShellCommandIds.MoveToNextGroup, 5, 2);
        Menu(EditorMenus.View, ShellCommandIds.CloseGroup, 5, 3);
        Menu(EditorMenus.Plugins, ShellCommandIds.Plugins, 0, 0);
        Menu(EditorMenus.Help, ShellCommandIds.Welcome, 0, 0);
        Menu(EditorMenus.Help, ShellCommandIds.CommandPalette, 0, 1, "Every command and its keys");
        Menu(EditorMenus.Help, ShellCommandIds.PluginGuide, 0, 2);
        Menu(EditorMenus.Help, ShellCommandIds.About, 1, 0);

        // The activity rail: each item a command; one whose command is not registered (the agent before A2) does not show.
        Rail(new RailItem("craft.explorer", "Explorer", ShellCommandIds.ToggleTree, RailIcons.Explorer, 0));
        // X part 6: the rail's Search opens the Search view (it ran quick open before).
        Rail(new RailItem(global::Craft.Editor.Search.WorkspaceSearchView.PanelId, "Search", "craft.search.rail", RailIcons.Search, 10));
        Rail(new RailItem("craft.agent", "Agent", ShellCommandIds.Agent, RailIcons.Agent, 30));
        Rail(new RailItem("craft.plugins", "Plug-ins", ShellCommandIds.Plugins, RailIcons.Plugins, 40));
        RegisterViewCommands();
        RegisterToolCommands();
        RegisterBookmarkCommands();
        RegisterTimelineCommands();
        RegisterNavigationCommands();
        Rail(new RailItem("craft.settings", "Settings", ShellCommandIds.Settings, RailIcons.Settings, 0, AtBottom: true));
        RegisterContextCommands();
    }

    // Ctrl+B and the rail's Explorer: the narrow layout's tree in the editor's place; the wide layout's side view (the
    // explorer shown when another view is, else opened or closed).
    private Task ToggleTree()
    {
        if (Layout.IsNarrow)
        {
            Layout.ToggleTree();
            return Task.CompletedTask;
        }

        _ = Panels.ToggleSide(PanelLayout.ExplorerView);
        return Task.CompletedTask;
    }

    private async Task ZoomCommandAsync(int steps)
    {
        if (await ZoomAsync(steps) is { } problem)
        {
            Host.ShowStatus("Zoom", problem, NotificationSeverity.Warning);
        }
    }

    private Task View(string name)
    {
        ViewRequested?.Invoke(this, name);
        return Task.CompletedTask;
    }

    private void Menu(string menu, string command, int group, int order, string? title = null)
    {
        EditorMenus.Register(new MenuItem(menu, command, title, order, group));
        _menuItems.Add((menu, command));
    }

    // A rich menu item (X part 7): its icon, its submenu, when it shows.
    private void Menu(MenuItem item)
    {
        EditorMenus.Register(item);
        _menuItems.Add((item.Menu, item.CommandId));
    }

    private void Rail(RailItem item)
    {
        EditorRailItems.Register(item);
        _railItems.Add(item.Id);
    }

    private void Command(string id, string title, Func<IEditorHost, CancellationToken, Task> execute, Func<IEditorHost, bool>? canExecute, string? chord,
        Func<IEditorHost, string?>? reason = null)
    {
        EditorCommands.Register(new EditorCommand(id, title, execute, canExecute) { Reason = reason });
        _commandIds.Add(id);
        if (chord is not null)
        {
            var (first, then) = KeyBinding.ParseKeys(chord);
            var binding = new KeyBinding(first, id, Then: then);
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }
    }
}
