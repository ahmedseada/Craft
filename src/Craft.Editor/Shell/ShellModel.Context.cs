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
        Command(ShellCommandIds.NewFileInFolder, "Explorer: New file…", (_, token) => CreateAsync(TargetFolder(), false, token), _ => TargetFolder() is not null, null);
        Command(ShellCommandIds.NewFolder, "Explorer: New folder…", (_, token) => CreateAsync(TargetFolder(), true, token), _ => TargetFolder() is not null, null);
        Command(ShellCommandIds.Rename, "Explorer: Rename…", (_, token) => RenameAsync(TargetEntry(), token), _ => CanChange(TargetEntry().Path), null);
        Command(ShellCommandIds.Delete, "Explorer: Delete…", (_, token) => DeleteAsync(TargetEntry(), token), _ => CanChange(TargetEntry().Path), null);
        Command(ShellCommandIds.RevealInFileManager, "File: " + (_files?.RevealTitle ?? "Reveal in the file manager"), (_, token) => RevealInFileManagerAsync(TargetEntry().Path, token),
            _ => _files is not null && TargetEntry().Path is not null, "Shift+Alt+R");
        Command(ShellCommandIds.OpenInTerminal, "Terminal: Open in this folder", (_, _) => OpenInTerminal(TargetFolder()), _ => Host.Terminals is not null && TargetFolder() is not null, null);
        Command(ShellCommandIds.CopyPath, "File: Copy path", (_, _) => CopyAsync(TargetEntry().Path), _ => _clipboard is not null && TargetEntry().Path is not null, "Shift+Alt+C");
        Command(ShellCommandIds.CopyRelativePath, "File: Copy relative path", (_, _) => CopyAsync(RelativeOf(TargetEntry().Path)), _ => _clipboard is not null && RelativeOf(TargetEntry().Path) is not null, null);
        Command(ShellCommandIds.CloseOtherTabs, "Tab: Close others", (_, token) => CloseOthersAsync(TargetTab(), false, token), _ => TargetTab() is not null && Tabs.Tabs.Count > 1, null);
        Command(ShellCommandIds.CloseTabsToTheRight, "Tab: Close to the right", (_, token) => CloseOthersAsync(TargetTab(), true, token),
            _ => TargetTab() is { } tab && IndexOfTab(tab) is var at && at >= 0 && at < Tabs.Tabs.Count - 1, null);
        Command(ShellCommandIds.RevealInTree, "File: Reveal in the explorer", (_, _) => RevealInTree(TargetEntry().Path), _ => Tree is not null && TargetEntry().Path is { } path && Tree.Workspace.Contains(Path.GetFullPath(path)), null);
        Command(ShellCommandIds.FindReferences, "Go: Find references", (_, token) => FindReferencesAsync(token), _ => Tabs.Active is { } tab && EditorOf?.Invoke(tab)?.SemanticSession is not null, "Shift+F12");

        // The tree's menu: a row's (or the empty space's: the workspace's folder).
        Menu(EditorContextMenus.Tree, ShellCommandIds.NewFileInFolder, 0, 0, "New file…");
        Menu(EditorContextMenus.Tree, ShellCommandIds.NewFolder, 0, 1, "New folder…");
        Menu(EditorContextMenus.Tree, ShellCommandIds.RevealInFileManager, 1, 0, _files?.RevealTitle);
        Menu(EditorContextMenus.Tree, ShellCommandIds.OpenInTerminal, 1, 1, "Open in terminal");
        Menu(EditorContextMenus.Tree, ShellCommandIds.CopyPath, 2, 0);
        Menu(EditorContextMenus.Tree, ShellCommandIds.CopyRelativePath, 2, 1);
        Menu(EditorContextMenus.Tree, ShellCommandIds.Rename, 3, 0);
        Menu(EditorContextMenus.Tree, ShellCommandIds.Delete, 3, 1);

        // A tab's menu.
        Menu(EditorContextMenus.Tab, ShellCommandIds.CloseTab, 0, 0);
        Menu(EditorContextMenus.Tab, ShellCommandIds.CloseOtherTabs, 0, 1);
        Menu(EditorContextMenus.Tab, ShellCommandIds.CloseTabsToTheRight, 0, 2);
        Menu(EditorContextMenus.Tab, ShellCommandIds.CopyPath, 1, 0);
        Menu(EditorContextMenus.Tab, ShellCommandIds.CopyRelativePath, 1, 1);
        Menu(EditorContextMenus.Tab, ShellCommandIds.RevealInTree, 2, 0);
        Menu(EditorContextMenus.Tab, ShellCommandIds.RevealInFileManager, 2, 1, _files?.RevealTitle);

        // The editor's menu: the shell's part (Go to definition and the edits come from ShellEditCommands; the AI actions from its plug-in).
        Menu(EditorContextMenus.Editor, ShellCommandIds.FindReferences, 1, 1);
    }

    // The file or folder the menu was opened on (a row's, a tab's), else the active document's file.
    private (string? Path, bool IsFolder) TargetEntry() => EditorContextMenus.Target is { Path: { } path } target
        ? (path, target.IsFolder)
        : (Tabs.Active?.FilePath, false);

    // The folder a new file goes in: the row's folder (a file's: its folder), else the workspace's; null with no workspace.
    private string? TargetFolder()
    {
        if (EditorContextMenus.Target is { Menu: EditorContextMenus.Tree, Path: { } path } target)
        {
            return target.IsFolder ? path : Path.GetDirectoryName(path);
        }

        return Workspace?.Root;
    }

    // The tab the menu was opened on, else the active one.
    private DocumentTab? TargetTab() => EditorContextMenus.Target is { Item: DocumentTab tab } && Tabs.Tabs.Contains(tab) ? tab : Tabs.Active;

    private int IndexOfTab(DocumentTab tab)
    {
        var tabs = Tabs.Tabs;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (ReferenceEquals(tabs[i], tab))
            {
                return i;
            }
        }

        return -1;
    }

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

    private async Task CreateAsync(string? folder, bool isFolder, CancellationToken cancellationToken)
    {
        if (folder is null)
        {
            return;
        }

        string where = RelativeOf(folder) is { Length: > 0 } relative && relative != "." ? relative : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        string? typed = await _ui.PromptAsync(isFolder ? "New folder" : "New file", $"In {where}", isFolder ? "The folder's name" : "The file's name (Program.cs)", "", "Create", cancellationToken);
        if (typed is null)
        {
            return;
        }

        string name = typed.Trim();
        if (NameProblem(name) is { } problem)
        {
            await _ui.ShowMessageAsync(isFolder ? "Craft: the folder was not made" : "Craft: the file was not made", problem);
            return;
        }

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
            await _ui.ShowMessageAsync(isFolder ? "Craft: the folder was not made" : "Craft: the file was not made", error);
            return;
        }

        _treeWatch?.Touch(path);
        Tree?.Reveal(path);
        if (!isFolder)
        {
            await OpenFileAsync(path, fromTree: true, cancellationToken);
        }
    }

    // The tabs of `path` (a folder's: every tab under it).
    private List<DocumentTab> TabsOf(string path, bool isFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var tabs = new List<DocumentTab>();
        foreach (var tab in Tabs.Tabs)
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

    private async Task RenameAsync((string? Path, bool IsFolder) entry, CancellationToken cancellationToken)
    {
        if (entry.Path is not { } path || !CanChange(path))
        {
            return;
        }

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        bool isFolder = entry.IsFolder;
        var open = TabsOf(path, isFolder);
        if (open.Find(t => t.IsDirty) is { } dirty)
        {
            await _ui.ShowMessageAsync("Craft: not renamed", $"{dirty.Title} has unsaved changes: save it or close it, then rename.");
            return;
        }

        string oldName = Path.GetFileName(path);
        string? typed = await _ui.PromptAsync("Rename", RelativeOf(path) ?? path, "The new name", oldName, "Rename", cancellationToken);
        if (typed is null)
        {
            return;
        }

        string name = typed.Trim();
        if (name == oldName)
        {
            return;
        }

        if (NameProblem(name) is { } problem)
        {
            await _ui.ShowMessageAsync("Craft: not renamed", problem);
            return;
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
            await _ui.ShowMessageAsync("Craft: not renamed", error);
            return;
        }

        // The renamed file's (or folder's) clean tabs open again under the new name, the active one staying active.
        var active = Tabs.Active;
        DocumentTab? reopenedActive = null;
        foreach (var tab in open)
        {
            string moved = target + tab.FilePath![path.Length..];
            bool wasActive = ReferenceEquals(tab, active);
            if (await CloseTabAsync(tab, cancellationToken) && await OpenFileAsync(moved, cancellationToken: cancellationToken) is { } reopened && wasActive)
            {
                reopenedActive = reopened;
            }
        }

        if (reopenedActive is not null)
        {
            Tabs.Activate(reopenedActive);
        }
        else if (active is not null && Tabs.Tabs.Contains(active))
        {
            Tabs.Activate(active);
        }

        _treeWatch?.Touch(path);
        _treeWatch?.Touch(target);
        Tree?.Reveal(target);
    }

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
        string where = _files?.TrashName is { } trash ? $"It goes to {trash}." : "It is deleted for good: Craft has no trash to move it to here.";
        string kept = dirty switch
        {
            0 => "",
            1 => " Its tab with unsaved changes stays open.",
            _ => string.Create(CultureInfo.InvariantCulture, $" {dirty} tabs with unsaved changes stay open."),
        };
        string what = isFolder ? $"The folder {RelativeOf(path)} and everything in it." : $"{RelativeOf(path)}.";
        if (!await _ui.ConfirmAsync($"Delete {name}?", $"{what} {where}{kept}", "Delete", danger: true, cancellationToken))
        {
            return;
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
            await _ui.ShowMessageAsync("Craft: not deleted", $"{name}: {error}");
            return;
        }

        activity.Complete(_files?.TrashName is { } bin ? "moved to " + bin : "deleted");
        foreach (var tab in open)
        {
            if (!tab.IsDirty)
            {
                await CloseTabAsync(tab, cancellationToken);
            }
        }

        _treeWatch?.Touch(path);
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

        int at = IndexOfTab(keep);
        var closing = Tabs.Tabs.Where((tab, index) => !ReferenceEquals(tab, keep) && (!toTheRight || index > at)).ToArray();
        foreach (var tab in closing)
        {
            if (!await CloseTabAsync(tab, cancellationToken))
            {
                break;
            }
        }

        if (Tabs.Tabs.Contains(keep))
        {
            Tabs.Activate(keep);
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
