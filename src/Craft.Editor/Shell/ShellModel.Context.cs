// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Workspaces;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The shell's commands behind the context menus (phase X): every item of the tree's, the tabs' and the editor's menu
/// runs one of these (or another registered command), so the palette, a key binding and a plug-in run the same thing. Each
/// acts on what the menu was opened on (<see cref="EditorContextMenus.Target"/>), else on the active document. The disk's
/// work (create, rename, delete, the trash, the file manager) runs off the UI thread; a delete is confirmed first.
/// </summary>
public sealed partial class ShellModel
{
    private readonly IShellFiles? _files;
    private readonly IEditorClipboard? _clipboard;

    /// <summary>Raised with a path when a command asks the explorer to show its row (its folders are opened already).</summary>
    public event EventHandler<string>? RevealRequested;

    /// <summary>The system's file manager and trash, or null.</summary>
    public IShellFiles? Files => _files;

    private void RegisterContextCommands()
    {
        Command(ShellCommandIds.NewFileInFolder, "Explorer: New file…", (_, token) => NewEntryAsync(TargetFolder(), false, token), _ => TargetFolder() is not null, null, NoFolder);
        Command(ShellCommandIds.NewFolder, "Explorer: New folder…", (_, token) => NewEntryAsync(TargetFolder(), true, token), _ => TargetFolder() is not null, null, NoFolder);
        Command(ShellCommandIds.Rename, "Explorer: Rename…", (_, token) => RenameAsync(TargetEntry(), token), _ => CanChange(TargetEntry().Path), null, _ => WhyNotChange(TargetEntry().Path));
        Command(ShellCommandIds.Delete, "Explorer: Delete", (_, token) => DeleteAsync(TargetEntry(), token), _ => CanChange(TargetEntry().Path), null, _ => WhyNotChange(TargetEntry().Path));
        Command(ShellCommandIds.RevealInFileManager, "File: " + (_files?.RevealTitle ?? "Reveal in the file manager"), (_, token) => RevealInFileManagerAsync(TargetEntry().Path, token),
            _ => _files is not null && TargetEntry().Path is not null, "Shift+Alt+R", _ => _files is null ? "This system has no file manager Craft can show it in." : "A new file is not on the disk yet.");
        Command(ShellCommandIds.OpenInTerminal, "Terminal: Open in this folder", (_, _) => OpenInTerminal(TargetFolder()), _ => Host.Terminals is not null && TargetFolder() is not null, null,
            _ => Host.Terminals is null ? "No terminal host is running." : "Open a folder first.");
        Command(ShellCommandIds.CopyPath, "File: Copy path", (_, _) => CopyAsync(TargetEntry().Path), _ => _clipboard is not null && TargetEntry().Path is not null, "Shift+Alt+C", NotOnDisk);
        Command(ShellCommandIds.CopyRelativePath, "File: Copy relative path", (_, _) => CopyAsync(RelativeOf(TargetEntry().Path)), _ => _clipboard is not null && RelativeOf(TargetEntry().Path) is not null, null,
            _ => "It is not in the open folder.");
        Command(ShellCommandIds.CloseOtherTabs, "Tab: Close others", (_, token) => CloseOthersAsync(TargetTab(), false, token),
            _ => TargetTab() is { } tab && Groups.GroupOf(tab)?.Tabs.Tabs.Any(t => !ReferenceEquals(t, tab) && !t.IsPinned) == true, null, _ => "No other tab to close (pinned tabs stay).");
        Command(ShellCommandIds.CloseTabsToTheRight, "Tab: Close to the right", (_, token) => CloseOthersAsync(TargetTab(), true, token),
            _ => TargetTab() is { } tab && Groups.GroupOf(tab) is { } group && group.Tabs.IndexOf(tab) is var at && at >= 0 && group.Tabs.Tabs.Skip(at + 1).Any(t => !t.IsPinned), null,
            _ => "No tab to its right (pinned tabs stay).");
        Command(ShellCommandIds.RevealInTree, "File: Reveal in the explorer", (_, _) => RevealInTree(TargetEntry().Path), _ => Tree is not null && TargetEntry().Path is { } path && Tree.Workspace.Contains(Path.GetFullPath(path)), null,
            _ => Tree is null ? "Open a folder first." : "It is not in the open folder.");
        Command(ShellCommandIds.FindReferences, "Go: Find references", (_, token) => FindReferencesAsync(token), _ => Tabs.Active is { } tab && EditorOf?.Invoke(tab)?.SemanticSession is not null, "Shift+F12");
        RegisterFileCommands();

        // The tree's menu (X part 7): a file's, a folder's and the workspace's (its own row, or the empty space) rows, each
        // with its icon, shown where its When says.
        string tree = EditorContextMenus.Tree;
        Menu(new MenuItem(tree, ShellCommandIds.Open, "Open", 0, 0) { Icon = MenuIcons.Open, When = MenuWhen.Files });
        Menu(new MenuItem(tree, ShellCommandIds.OpenToSide, "Open to the side", 1, 0) { Icon = MenuIcons.SplitRight, When = MenuWhen.Files });
        Menu(new MenuItem(tree, ShellCommandIds.OpenWith, "Open with", 2, 0) { Icon = MenuIcons.OpenWith, When = MenuWhen.Files, Submenu = EditorContextMenus.OpenWith });
        Menu(new MenuItem(tree, ShellCommandIds.NewFileInFolder, "New file…", 3, 0) { Icon = MenuIcons.NewFile, When = MenuWhen.AnyFolder });
        Menu(new MenuItem(tree, ShellCommandIds.NewFolder, "New folder…", 4, 0) { Icon = MenuIcons.NewFolder, When = MenuWhen.AnyFolder });
        Menu(new MenuItem(tree, ShellCommandIds.RevealInFileManager, _files?.RevealTitle, 0, 1) { Icon = MenuIcons.Reveal });
        Menu(new MenuItem(tree, ShellCommandIds.OpenInTerminal, "Open in terminal", 1, 1) { Icon = MenuIcons.Terminal });
        Menu(new MenuItem(tree, ShellCommandIds.FindInFolder, "Find in folder…", 2, 1) { Icon = MenuIcons.Find });
        Menu(new MenuItem(tree, ShellCommandIds.RunMenu, "Run", 0, 2) { Icon = RailIcons.Run, Submenu = EditorContextMenus.RunTargets, When = HasRunners(MenuTargetKinds.File | MenuTargetKinds.Folder) });
        Menu(new MenuItem(tree, ShellCommandIds.FormatFile, "Format file", 0, 3) { Icon = MenuIcons.Format, When = MenuWhen.Files });
        Menu(new MenuItem(tree, ShellCommandIds.CollapseFolder, "Collapse all inside", 1, 3) { Icon = RailIcons.Collapse, When = MenuWhen.Folders });
        Menu(new MenuItem(tree, ShellCommandIds.CollapseAll, "Collapse all", 2, 3) { Icon = RailIcons.Collapse, When = MenuWhen.Root });
        Menu(new MenuItem(tree, ShellCommandIds.RefreshTree, "Refresh", 3, 3) { Icon = RailIcons.Refresh, When = MenuWhen.Root });
        Menu(new MenuItem(tree, ShellCommandIds.Cut, "Cut", 0, 4) { Icon = MenuIcons.Cut, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.Copy, "Copy", 1, 4) { Icon = MenuIcons.Copy, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.Paste, "Paste", 2, 4) { Icon = MenuIcons.Paste });
        Menu(new MenuItem(tree, ShellCommandIds.Duplicate, "Duplicate", 3, 4) { Icon = MenuIcons.Duplicate, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.SelectForCompare, "Select for compare", 0, 5) { Icon = MenuIcons.SelectForCompare, When = MenuWhen.Files });
        Menu(new MenuItem(tree, ShellCommandIds.CompareWithSelected, "Compare with selected", 1, 5) { Icon = MenuIcons.Compare, When = MenuWhen.Files });
        Menu(new MenuItem(tree, ShellCommandIds.CopyName, "Copy name", 0, 6) { Icon = MenuIcons.Name, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.CopyPath, "Copy path", 1, 6) { Icon = MenuIcons.Path });
        Menu(new MenuItem(tree, ShellCommandIds.CopyRelativePath, "Copy relative path", 2, 6) { Icon = MenuIcons.Path, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.Rename, "Rename…", 0, 7) { Icon = MenuIcons.Rename, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.Delete, "Delete", 1, 7) { Icon = MenuIcons.Delete, When = MenuWhen.Entries });
        Menu(new MenuItem(tree, ShellCommandIds.OpenFolder, "Open folder…", 0, 8) { Icon = MenuIcons.Folder, When = MenuWhen.Root });

        // A tab's menu.
        string tab = EditorContextMenus.Tab;
        Menu(new MenuItem(tab, ShellCommandIds.CloseTab, "Close", 0, 0) { Icon = MenuIcons.Close });
        Menu(new MenuItem(tab, ShellCommandIds.CloseOtherTabs, "Close others", 1, 0) { Icon = MenuIcons.CloseMany });
        Menu(new MenuItem(tab, ShellCommandIds.CloseTabsToTheRight, "Close to the right", 2, 0) { Icon = MenuIcons.CloseMany });
        Menu(new MenuItem(tab, ShellCommandIds.CloseSavedTabs, "Close saved", 3, 0) { Icon = MenuIcons.CloseSaved });
        Menu(new MenuItem(tab, ShellCommandIds.CloseAllTabs, "Close all", 4, 0) { Icon = MenuIcons.CloseMany });
        Menu(new MenuItem(tab, ShellCommandIds.ReopenClosedTab, "Reopen closed tab", 0, 1) { Icon = MenuIcons.Reopen });
        Menu(new MenuItem(tab, ShellCommandIds.PinTab, "Pin", 1, 1) { Icon = MenuIcons.Pin, When = new MenuWhen { Test = static t => t.Item is DocumentTab { IsPinned: false } } });
        Menu(new MenuItem(tab, ShellCommandIds.UnpinTab, "Unpin", 2, 1) { Icon = MenuIcons.Unpin, When = new MenuWhen { Test = static t => t.Item is DocumentTab { IsPinned: true } } });
        Menu(new MenuItem(tab, ShellCommandIds.SplitRight, "Split right", 0, 2) { Icon = MenuIcons.SplitRight });
        Menu(new MenuItem(tab, ShellCommandIds.SplitDown, "Split down", 1, 2) { Icon = MenuIcons.SplitDown });
        Menu(new MenuItem(tab, ShellCommandIds.MoveToNextGroup, "Move to the other group", 2, 2) { Icon = MenuIcons.MoveToGroup });
        Menu(new MenuItem(tab, ShellCommandIds.CopyName, "Copy name", 0, 3) { Icon = MenuIcons.Name });
        Menu(new MenuItem(tab, ShellCommandIds.CopyPath, "Copy path", 1, 3) { Icon = MenuIcons.Path });
        Menu(new MenuItem(tab, ShellCommandIds.CopyRelativePath, "Copy relative path", 2, 3) { Icon = MenuIcons.Path });
        Menu(new MenuItem(tab, ShellCommandIds.RevealInTree, "Reveal in the explorer", 0, 4) { Icon = MenuIcons.RevealInTree });
        Menu(new MenuItem(tab, ShellCommandIds.RevealInFileManager, _files?.RevealTitle, 1, 4) { Icon = MenuIcons.Reveal });
        Menu(new MenuItem(tab, ShellCommandIds.OpenInTerminal, "Open in terminal", 2, 4) { Icon = MenuIcons.Terminal });
        Menu(new MenuItem(tab, ShellCommandIds.FormatFile, "Format file", 0, 5) { Icon = MenuIcons.Format });
        Menu(new MenuItem(tab, ShellCommandIds.CompareWithSaved, "Compare with saved", 1, 5) { Icon = MenuIcons.Compare });
        Menu(new MenuItem(tab, ShellCommandIds.RevertFile, "Revert file", 2, 5) { Icon = MenuIcons.Revert });
        Menu(new MenuItem(tab, ShellCommandIds.RunMenu, "Run", 0, 6) { Icon = RailIcons.Run, Submenu = EditorContextMenus.RunTargets, When = HasRunners(MenuTargetKinds.File) });

        // The editor's menu: the shell's part (Go to definition and the edits come from ShellEditCommands; the AI actions from its plug-in).
        Menu(EditorContextMenus.Editor, ShellCommandIds.FindReferences, 1, 1);

        // The submenus' rows, made as the menu opens.
        Source(new MenuItemSource("craft.openWith", EditorContextMenus.OpenWith, _ => OpenWithRows()));
        Source(new MenuItemSource("craft.run", EditorContextMenus.RunTargets, RunRows));
    }

    private static string? NoFolder(IEditorHost host) => "Open a folder first.";

    private string? NotOnDisk(IEditorHost host) => _clipboard is null ? "This system has no clipboard Craft can reach." : "A new file is not on the disk yet.";

    // Why a file or folder cannot be renamed, deleted, cut or duplicated.
    private string? WhyNotChange(string? path) =>
        path is null ? "A new file is not on the disk yet."
        : Workspace is not { } workspace ? "Open its folder first: Craft changes files of the open folder."
        : PathKeys.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(workspace.Root)) ? "The open folder itself cannot be."
        : !workspace.Contains(Path.GetFullPath(path)) ? "It is not in the open folder."
        : null;

    // A row shown on the kinds given when the target has run targets of its own (the Run view's providers).
    private MenuWhen HasRunners(MenuTargetKinds kinds) => new()
    {
        Targets = kinds,
        Test = target => target.Path is { } path && RunTargetsOf(path, target.Kind != MenuTargetKinds.File).Count > 0,
    };

    // The file or folder the menu was opened on (a row's, a tab's), else the active document's file.
    private (string? Path, bool IsFolder) TargetEntry() => EditorContextMenus.Target is { Path: { } path } target
        ? (path, target.IsFolder)
        : (Tabs.Active?.FilePath, false);

    // The folder a new file goes in: the row's folder (a file's: its folder), else the workspace's; null with no workspace.
    private string? TargetFolder()
    {
        if (EditorContextMenus.Target is { Menu: EditorContextMenus.Tree or EditorContextMenus.Tab, Path: { } path } target)
        {
            return target.IsFolder ? path : Path.GetDirectoryName(path);
        }

        return Workspace?.Root;
    }

    // The tab the menu was opened on (in any group), else the focused group's active one.
    private DocumentTab? TargetTab() => EditorContextMenus.Target is { Item: DocumentTab tab } && Groups.GroupOf(tab) is not null ? tab : Tabs.Active;

    // Whether a file or folder of the workspace (not its root) may be renamed or deleted.
    private bool CanChange(string? path) =>
        path is not null && Workspace is { } workspace && workspace.Contains(Path.GetFullPath(path)) && !PathKeys.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(workspace.Root));

    // The path from the workspace's folder ('/' separated), or null outside it.
    private string? RelativeOf(string? path)
    {
        if (path is null || Workspace is not { } workspace || !workspace.Contains(Path.GetFullPath(path)))
        {
            return null;
        }

        return Path.GetRelativePath(workspace.Root, path);
    }

    // Why a name typed for a new or renamed entry cannot be one, or null.
    internal static string? NameProblem(string name)
    {
        if (name.Length == 0)
        {
            return "A name is needed.";
        }

        if (name is "." or ".." || name.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            return $"'{name}' cannot be a file's or folder's name (no / \\ : * ? \" < > | and not . or ..).";
        }

        return name.EndsWith(' ') || name.EndsWith('.') ? $"'{name}' ends with a space or a dot, which Windows drops." : null;
    }

    // New file, New folder: named in place in the tree when it shows (X part 7), else asked in Craft's dialog.
    private async Task NewEntryAsync(string? folder, bool isFolder, CancellationToken cancellationToken)
    {
        if (folder is null)
        {
            return;
        }

        if (BeginTreeEdit(isFolder ? TreeEditKind.NewFolder : TreeEditKind.NewFile, folder, null))
        {
            return;
        }

        string where = WhereOf(folder);
        string? typed = await _ui.PromptAsync(isFolder ? "New folder" : "New file", $"In {where}", isFolder ? "The folder's name" : "The file's name (Program.cs)", "", "Create", cancellationToken);
        if (typed is null)
        {
            return;
        }

        if (await CreateEntryAsync(folder, typed.Trim(), isFolder, cancellationToken) is { } problem)
        {
            await _ui.ShowMessageAsync(isFolder ? "Craft: the folder was not made" : "Craft: the file was not made", problem);
        }
    }

    // The folder as the user reads it: its path from the workspace's folder, else its name.
    private string WhereOf(string folder) =>
        RelativeOf(folder) is { Length: > 0 } relative && relative != "." ? relative.Replace('\\', '/') : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

    // Makes the file or folder `name` in `folder` off the UI thread (a file opens): null when made, else why not.
    private async Task<string?> CreateEntryAsync(string folder, string name, bool isFolder, CancellationToken cancellationToken)
    {
        if (NameProblem(name) is { } problem)
        {
            return problem;
        }

        string where = WhereOf(folder);
        string path = Path.Combine(folder, name);
        string? error = await Task.Run(() =>
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return $"{name} is in {where} already.";
                }

                if (isFolder)
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    Directory.CreateDirectory(folder);
                    using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                }

                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        }, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _treeWatch?.Touch(path);
        Tree?.Reveal(path);
        if (!isFolder)
        {
            await OpenFileAsync(path, fromTree: true, cancellationToken);
        }

        return null;
    }

    // The tabs of `path` (a folder's: every tab under it).
    private List<DocumentTab> TabsOf(string path, bool isFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var tabs = new List<DocumentTab>();
        foreach (var tab in Groups.AllTabs)
        {
            if (tab.FilePath is { } file && (PathKeys.Comparer.Equals(file, full)
                || (isFolder && file.Length > full.Length + 1 && file.StartsWith(full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && (file[full.Length] == Path.DirectorySeparatorChar || file[full.Length] == Path.AltDirectorySeparatorChar))))
            {
                tabs.Add(tab);
            }
        }

        return tabs;
    }

    // Rename: in place in the tree when it shows (X part 7), else asked in Craft's dialog; then RenameEntryAsync.
    private async Task RenameAsync((string? Path, bool IsFolder) entry, CancellationToken cancellationToken)
    {
        if (entry.Path is not { } path || !CanChange(path))
        {
            return;
        }

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (TabsOf(path, entry.IsFolder).Find(t => t.IsDirty) is { } dirty)
        {
            await _ui.ShowMessageAsync("Craft: not renamed", $"{dirty.Title} has unsaved changes: save it or close it, then rename.");
            return;
        }

        if (BeginTreeEdit(TreeEditKind.Rename, Path.GetDirectoryName(path)!, path, entry.IsFolder))
        {
            return;
        }

        string oldName = Path.GetFileName(path);
        string? typed = await _ui.PromptAsync("Rename", RelativeOf(path) ?? path, "The new name", oldName, "Rename", cancellationToken);
        if (typed is null)
        {
            return;
        }

        if (await RenameEntryAsync(path, entry.IsFolder, typed.Trim(), offerUndo: true, cancellationToken) is { } problem)
        {
            await _ui.ShowMessageAsync("Craft: not renamed", problem);
        }
    }

    /// <summary>
    /// Renames the file or folder <paramref name="path"/> to <paramref name="name"/> in its folder, off the UI thread: its
    /// clean tabs follow it, and with <paramref name="offerUndo"/> the inline bar says so with Undo (the name back). Null
    /// when renamed (or the name is the same), else why not.
    /// </summary>
    private async Task<string?> RenameEntryAsync(string path, bool isFolder, string name, bool offerUndo, CancellationToken cancellationToken)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string oldName = Path.GetFileName(path);
        if (name == oldName)
        {
            return null;
        }

        if (NameProblem(name) is { } problem)
        {
            return problem;
        }

        var open = TabsOf(path, isFolder);
        if (open.Find(t => t.IsDirty) is { } dirty)
        {
            return $"{dirty.Title} has unsaved changes: save it or close it, then rename.";
        }

        string target = Path.Combine(Path.GetDirectoryName(path)!, name);
        bool caseOnly = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);
        string? error = await Task.Run(() =>
        {
            try
            {
                if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
                {
                    return $"{name} is there already.";
                }

                if (isFolder)
                {
                    Directory.Move(path, target);
                }
                else
                {
                    File.Move(path, target);
                }

                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        }, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        // The renamed file's (or folder's) clean tabs open again under the new name, in their groups and places, the
        // active ones staying active.
        await FollowMovedAsync(open, path, target, cancellationToken);
        _treeWatch?.Touch(path);
        _treeWatch?.Touch(target);
        Tree?.Reveal(target);
        if (_compareFirst is { } first && PathKeys.Comparer.Equals(first, path))
        {
            _compareFirst = target;
        }

        if (offerUndo)
        {
            UndoBar.Show($"Renamed {oldName} to {name}", () => _ = RenameBackAsync(target, isFolder, oldName));
        }

        return null;
    }

    // Undo of a rename: the old name back (not itself offered for undo), a refusal said.
    private async Task RenameBackAsync(string renamed, bool isFolder, string oldName)
    {
        if (await RenameEntryAsync(renamed, isFolder, oldName, offerUndo: false, CancellationToken.None) is { } problem)
        {
            Host.ShowStatus("Undo rename", problem, NotificationSeverity.Warning);
        }
    }

    // Delete (X part 7): the entry is kept aside first (up to CRAFT_DELETE_UNDO_MAX_MB), then goes to the trash (or for
    // good where there is none), its clean tabs close, and the inline bar's Undo brings it back; an entry too large to keep
    // is asked about first. The disk's work runs off the UI thread as an activity.
    private async Task DeleteAsync((string? Path, bool IsFolder) entry, CancellationToken cancellationToken)
    {
        if (entry.Path is not { } path || !CanChange(path))
        {
            return;
        }

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        bool isFolder = entry.IsFolder;
        var open = TabsOf(path, isFolder);
        int dirty = open.Count(t => t.IsDirty);
        string name = Path.GetFileName(path);
        string? trashName = _files?.TrashName;
        long limit = Options.DeleteUndoMaxMegabytes * 1024L * 1024L;
        string? held = null;
        if (limit > 0)
        {
            using var keeping = Host.Activities.Start($"Keeping {name} for Undo");
            held = await Task.Run(() => HoldForUndo(path, isFolder, limit), cancellationToken);
            keeping.Complete(held is null ? "too large to keep: asking first" : "kept");
        }

        if (held is null)
        {
            string where = trashName is { } trash ? $"It goes to {trash}." : "It is deleted for good: Craft has no trash to move it to here.";
            string kept = dirty switch
            {
                0 => "",
                1 => " Its tab with unsaved changes stays open.",
                _ => string.Create(CultureInfo.InvariantCulture, $" {dirty} tabs with unsaved changes stay open."),
            };
            string what = isFolder ? $"The folder {RelativeOf(path)} and everything in it." : $"{RelativeOf(path)}.";
            string why = limit > 0 ? string.Create(CultureInfo.InvariantCulture, $" It is larger than {Options.DeleteUndoMaxMegabytes} MB, so it is not kept for Undo (CRAFT_DELETE_UNDO_MAX_MB).") : "";
            if (!await _ui.ConfirmAsync($"Delete {name}?", $"{what} {where}{kept}{why}", "Delete", danger: true, cancellationToken))
            {
                return;
            }
        }

        using var activity = Host.Activities.Start($"Deleting {name}");
        var files = _files;
        string? error = await Task.Run(async () =>
        {
            try
            {
                if (files is not null && await files.TrashAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                if (isFolder)
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }

                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        }, cancellationToken);
        if (error is not null)
        {
            activity.Fail(error);
            DropHeld(held);
            await _ui.ShowMessageAsync("Craft: not deleted", $"{name}: {error}");
            return;
        }

        activity.Complete(trashName is { } bin ? "moved to " + bin : "deleted");
        var reopen = new List<string>();
        foreach (var tab in open)
        {
            if (!tab.IsDirty && tab.FilePath is { } file && await CloseTabAsync(tab, cancellationToken))
            {
                reopen.Add(file);
            }
        }

        _treeWatch?.Touch(path);
        if (held is not null)
        {
            string said = trashName is { } trashed ? $"Deleted {name} (to {trashed})" : $"Deleted {name}";
            UndoBar.Show(said, () => _ = RestoreHeldAsync(held, path, isFolder, reopen), () => DropHeld(held));
        }
    }

    // The entry copied aside for Undo (a folder of Craft's own under the system's temporary folder): its copy's path, or
    // null when it is larger than `limit` bytes or could not be copied. Off the UI thread.
    private static string? HoldForUndo(string path, bool isFolder, long limit)
    {
        try
        {
            long size = 0;
            if (isFolder)
            {
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    size += file.Length;
                    if (size > limit)
                    {
                        return null;
                    }
                }
            }
            else if (new FileInfo(path).Length > limit)
            {
                return null;
            }

            string holder = Path.Combine(Path.GetTempPath(), "craft-undo", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(holder);
            string copy = Path.Combine(holder, Path.GetFileName(path));
            if (isFolder)
            {
                CopyFolder(path, copy);
            }
            else
            {
                File.Copy(path, copy);
            }

            return copy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // What was held for Undo goes (the bar's time is up, or it was not needed), off the UI thread.
    private static void DropHeld(string? held)
    {
        if (held is null || Path.GetDirectoryName(held) is not { } holder)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Directory.Delete(holder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left in the temporary folder, which the system empties.
            }
        });
    }

    // Undo of a delete: the held copy back at its place (unless something took the name meanwhile, said), the tabs that
    // closed opened again.
    private async Task RestoreHeldAsync(string held, string path, bool isFolder, List<string> reopen)
    {
        string name = Path.GetFileName(path);
        using var activity = Host.Activities.Start($"Restoring {name}");
        string? error = await Task.Run(() =>
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return $"{name} is there again: the deleted one was not put back.";
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (isFolder)
                {
                    CopyFolder(held, path);
                }
                else
                {
                    File.Copy(held, path);
                }

                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        });
        DropHeld(held);
        if (error is not null)
        {
            activity.Fail(error);
            Host.ShowStatus("Undo delete", error, NotificationSeverity.Warning);
            return;
        }

        activity.Complete("restored");
        _treeWatch?.Touch(path);
        Tree?.Reveal(path);
        foreach (var file in reopen)
        {
            await OpenFileAsync(file);
        }
    }

    private async Task RevealInFileManagerAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null || _files is not { } files)
        {
            return;
        }

        try
        {
            await files.RevealAsync(path, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await _ui.ShowMessageAsync("Craft: could not show it", $"{path}: {e.Message}");
        }
    }

    private Task OpenInTerminal(string? folder)
    {
        if (folder is null || Host.Terminals is not { } terminals)
        {
            return Task.CompletedTask;
        }

        try
        {
            terminals.Open(new EditorTerminalRequest { Folder = folder }).Show();
            return Task.CompletedTask;
        }
        catch (InvalidOperationException e)
        {
            return _ui.ShowMessageAsync("Craft: the terminal did not start", e.Message);
        }
    }

    private async Task CopyAsync(string? text)
    {
        if (text is not null && _clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            Host.ShowStatus("Copied", text);
        }
    }

    // Close others (every tab but `keep`) or close to the right (the tabs after it); stops when an unsaved one is kept open.
    private async Task CloseOthersAsync(DocumentTab? keep, bool toTheRight, CancellationToken cancellationToken)
    {
        if (keep is null)
        {
            return;
        }

        if (Groups.GroupOf(keep) is not { } group)
        {
            return;
        }

        int at = group.Tabs.IndexOf(keep);
        var closing = group.Tabs.Tabs.Where((tab, index) => !ReferenceEquals(tab, keep) && !tab.IsPinned && (!toTheRight || index > at)).ToArray();
        foreach (var tab in closing)
        {
            if (!await CloseTabAsync(tab, cancellationToken))
            {
                break;
            }
        }

        if (group.Tabs.Contains(keep))
        {
            group.Tabs.Activate(keep);
        }
    }

    /// <summary>
    /// The clean tabs of a moved or renamed file or folder (<paramref name="open"/>, under <paramref name="from"/>) open
    /// again under <paramref name="to"/>, each in its group at its place, the active ones active again; the focus stays.
    /// </summary>
    private async Task FollowMovedAsync(List<DocumentTab> open, string from, string to, CancellationToken cancellationToken)
    {
        if (open.Count == 0)
        {
            return;
        }

        var focused = Groups.Focused;
        var places = new List<(EditorGroup Group, string Path, int Index, bool Active)>(open.Count);
        foreach (var tab in open)
        {
            if (Groups.GroupOf(tab) is { } group && tab.FilePath is { } file)
            {
                places.Add((group, to + file[from.Length..], group.Tabs.IndexOf(tab), ReferenceEquals(group.Tabs.Active, tab)));
            }
        }

        // A group whose every tab follows stays open meanwhile (it gets them back).
        bool closeEmpty = Groups.CloseEmpty;
        Groups.CloseEmpty = false;
        try
        {
            foreach (var tab in open)
            {
                Groups.Remove(tab);
            }

            foreach (var (group, moved, index, active) in places)
            {
                if (!Groups.Groups.Contains(group) || await OpenInGroupAsync(group, moved, activate: false, cancellationToken) is not { } reopened)
                {
                    continue;
                }

                group.Tabs.Move(reopened, index);
                if (active)
                {
                    group.Tabs.Select(reopened);
                }
            }
        }
        finally
        {
            Groups.CloseEmpty = closeEmpty;
            foreach (var group in Groups.Groups.ToArray())
            {
                Groups.CloseIfEmpty(group);
            }
        }

        if (Groups.Groups.Contains(focused))
        {
            Groups.Focus(focused);
            focused.Tabs.TookFocus();
        }
    }

    private Task RevealInTree(string? path)
    {
        if (path is null || Tree is not { } tree || !tree.Reveal(path))
        {
            return Task.CompletedTask;
        }

        if (!Layout.IsNarrow)
        {
            _ = Panels.ShowSide(PanelLayout.ExplorerView);
        }

        RevealRequested?.Invoke(this, Path.GetFullPath(path));
        return Task.CompletedTask;
    }

    // Shift+F12: the language's references to the symbol at the caret, as an activity off the UI thread, then a list to
    // pick from; the one picked opens at its place.
    private async Task FindReferencesAsync(CancellationToken cancellationToken)
    {
        if (Tabs.Active is not { } tab || EditorOf?.Invoke(tab) is not { SemanticSession: { } session } model)
        {
            return;
        }

        var snapshot = model.Snapshot;
        int caret = model.Selection.Caret;
        IReadOnlyList<SymbolLocation> found;
        using (var activity = Host.Activities.Start("Finding references", ActivityKinds.General, cancellable: true, cancellationToken))
        {
            try
            {
                found = await session.FindReferencesAsync(snapshot, caret, Options.ReferencesLimit, activity.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            activity.Complete(string.Create(CultureInfo.InvariantCulture, $"{found.Count} found"));
        }

        if (found.Count == 0)
        {
            Host.ShowStatus("Find references", "Nothing refers to a symbol at the caret.");
            return;
        }

        var items = new string[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            var place = found[i];
            string file = place.FilePath is { } path ? RelativeOf(path) ?? path : tab.Title;
            items[i] = string.Create(CultureInfo.InvariantCulture, $"{file}:{place.Line}  {place.Description}");
        }

        string title = string.Create(CultureInfo.InvariantCulture, $"{found.Count} {(found.Count == 1 ? "reference" : "references")} to {found[0].Name}");
        int picked = await _ui.PickAsync(title, items, cancellationToken);
        if (picked < 0 || picked >= found.Count)
        {
            return;
        }

        var chosen = found[picked];
        var target = chosen.FilePath is { } chosenFile && !PathKeys.Comparer.Equals(Path.GetFullPath(chosenFile), tab.FilePath ?? "")
            ? await OpenFileAsync(chosenFile, cancellationToken: cancellationToken)
            : tab;
        if (target is not null)
        {
            Tabs.Activate(target);
            EditorOf?.Invoke(target)?.GoToLine(chosen.Line, chosen.Column);
        }
    }
}
