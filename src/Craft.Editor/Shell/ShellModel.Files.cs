// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Documents;
using Craft.Core.Workspaces;
using Craft.Editor.Run;

namespace Craft.Editor.Shell;

/// <summary>What is named in place in the explorer (X part 7).</summary>
public enum TreeEditKind
{
    /// <summary>A new file: a row at the top of its folder.</summary>
    NewFile,

    /// <summary>A new folder: a row at the top of its folder.</summary>
    NewFolder,

    /// <summary>A file's or folder's new name, on its own row.</summary>
    Rename,
}

/// <summary>A name being typed in place in the explorer (<see cref="ShellModel.TreeEdit"/>).</summary>
/// <param name="Kind">What it names.</param>
/// <param name="Folder">The folder it is in (a full path).</param>
/// <param name="Path">The renamed file or folder (a full path), or null for a new one.</param>
/// <param name="Name">The name it starts with (the old name; empty for a new one).</param>
public sealed record TreeEdit(TreeEditKind Kind, string Folder, string? Path, string Name)
{
    /// <summary>Whether it names a folder.</summary>
    public bool IsFolder { get; init; }

    /// <summary>The path of the tree's row it shows on: the renamed entry's, or the new row's (its folder's with a separator at the end).</summary>
    public string RowPath => Path ?? Folder + System.IO.Path.DirectorySeparatorChar;
}

/// <summary>
/// X part 7: the explorer's and the tabs' rich menus. Open, open to the side, open with a language; find in the folder; run
/// and build its run targets; format; cut, copy, paste and duplicate files off the UI thread with Undo; compare two files
/// or a tab with its saved text in the diff view; revert with Undo; names typed in place in the tree; pinned tabs; close
/// saved and close all; the targets described for the menus' "when" (the language, the project kinds, the workspace's
/// own folder). Every disk access runs off the UI thread; every change that can be taken back says so in the Undo bar.
/// </summary>
public sealed partial class ShellModel
{
    private readonly List<string> _menuSources = [];
    private (string[] Paths, bool Cut)? _fileClipboard;
    private string? _compareFirst;

    /// <summary>
    /// Whether New file, New folder and Rename name in place in the explorer (X part 7) when it can show the row (the wide
    /// layout); else Craft's dialog asks. The app sets it.
    /// </summary>
    public bool InlineTreeEdits { get; set; }

    /// <summary>The name being typed in place in the explorer, or null.</summary>
    public TreeEdit? TreeEdit { get; private set; }

    /// <summary>Raised when <see cref="TreeEdit"/> began or ended (the explorer shows or hides its field).</summary>
    public event EventHandler? TreeEditChanged;

    /// <summary>The files and folders cut or copied in the explorer (empty when none), and whether they were cut.</summary>
    public (IReadOnlyList<string> Paths, bool Cut) FileClipboard => _fileClipboard is { } held ? (held.Paths, held.Cut) : ([], false);

    /// <summary>The file selected for compare, or null.</summary>
    public string? CompareSelection => _compareFirst;

    /// <summary>
    /// What a context menu of the tree or a tab was opened on, filled in for its rows' <see cref="MenuWhen"/>: the
    /// workspace's own folder (also for the tree's empty space), the file's language, and the project kinds it belongs to
    /// (the run providers with a target of its own, else of its nearest folder above that has any; a folder's within it).
    /// Other menus' targets are given back as they are. In memory only (no disk).
    /// </summary>
    public ContextMenuTarget DescribeMenuTarget(ContextMenuTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Menu is not (EditorContextMenus.Tree or EditorContextMenus.Tab))
        {
            return target;
        }

        string? path = target.Path;
        bool isFolder = target.IsFolder;
        bool isRoot = target.IsRoot;
        if (target.Menu == EditorContextMenus.Tree && path is null && Workspace is { } open)
        {
            path = open.Root;
            isFolder = true;
            isRoot = true;
        }
        else if (path is not null && Workspace is { } workspace && PathKeys.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), workspace.Root))
        {
            isRoot = true;
            isFolder = true;
        }

        string? language = target.Language ?? (target.Item is DocumentTab tab ? tab.Language?.Name : null);
        if (language is null && !isFolder && path is not null)
        {
            language = EditorLanguages.ForFile(path)?.Name;
        }

        IReadOnlyList<string> kinds = [];
        if (path is not null)
        {
            var targets = isFolder ? RunTargetsOf(path, true) : RunTargetsOf(path, false) is { Count: > 0 } own ? own : NearestRunTargets(path);
            kinds = [.. targets.Select(t => t.Provider).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        return target with { Path = path, IsFolder = isFolder, IsRoot = isRoot, Language = language, ProjectKinds = kinds };
    }

    /// <summary>
    /// The Run view's targets of <paramref name="path"/>: a file's own (a project, a solution, a script), or every target
    /// within a folder (<paramref name="isFolder"/>), in the view's order.
    /// </summary>
    public IReadOnlyList<RunTarget> RunTargetsOf(string path, bool isFolder)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Workspace is not { } workspace || Run.Targets is not { Count: > 0 } all)
        {
            return [];
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var found = new List<RunTarget>();
        foreach (var target in all)
        {
            var file = Path.GetFullPath(Path.Combine(workspace.Root, target.File));
            if (isFolder ? PathKeys.Comparer.Equals(full, workspace.Root) || IsUnder(file, full) : PathKeys.Comparer.Equals(file, full))
            {
                found.Add(target);
            }
        }

        return found;
    }

    // The targets of the nearest folder above `path` that has any (the project a source file belongs to).
    private List<RunTarget> NearestRunTargets(string path)
    {
        if (Workspace is not { } workspace || Run.Targets.Count == 0)
        {
            return [];
        }

        for (var folder = Path.GetDirectoryName(Path.GetFullPath(path)); folder is not null && (workspace.Contains(folder) || PathKeys.Comparer.Equals(folder, workspace.Root)); folder = Path.GetDirectoryName(folder))
        {
            var here = Run.Targets.Where(t => PathKeys.Comparer.Equals(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(workspace.Root, t.File))) ?? "", folder)).ToList();
            if (here.Count > 0)
            {
                return here;
            }

            if (PathKeys.Comparer.Equals(folder, workspace.Root))
            {
                break;
            }
        }

        return [];
    }

    private void RegisterFileCommands()
    {
        Command(ShellCommandIds.Open, "Explorer: Open", (_, token) => TargetFile() is { } file ? OpenFileAsync(file, fromTree: true, token) : Task.CompletedTask,
            _ => TargetFile() is not null, null);
        Command(ShellCommandIds.OpenToSide, "Explorer: Open to the side", (_, token) => OpenToSideAsync(TargetFile(), token), _ => TargetFile() is not null && Groups.Max > 1, null,
            _ => Groups.Max <= 1 ? "CRAFT_EDITOR_GROUPS_MAX is 1: the editor does not split." : null);
        Command(ShellCommandIds.OpenWithLanguage, "Explorer: Open with a language", (_, token) => OpenWithAsync(token), _ => TargetFile() is not null, null);
        Command(ShellCommandIds.FindInFolder, "Search: Find in folder", (_, _) => FindInFolder(TargetFolder()), _ => Workspace is not null && TargetFolder() is not null, null, NoFolder);
        Command(ShellCommandIds.RunTarget, "Run: Run, build or test the chosen target", (_, token) => RunChosenAsync(token), _ => Workspace is not null, null, NoFolder);
        Command(ShellCommandIds.FormatFile, "File: Format", (_, token) => FormatFileAsync(token), _ => CanFormat(), null, _ => "No language formats this file.");
        Command(ShellCommandIds.Cut, "Explorer: Cut", (_, _) => HoldFiles(cut: true), _ => CanChange(TargetEntry().Path), null, _ => WhyNotChange(TargetEntry().Path));
        Command(ShellCommandIds.Copy, "Explorer: Copy", (_, _) => HoldFiles(cut: false), _ => CanChange(TargetEntry().Path), null, _ => WhyNotChange(TargetEntry().Path));
        Command(ShellCommandIds.Paste, "Explorer: Paste", (_, token) => PasteAsync(TargetFolder(), token), _ => _fileClipboard is not null && Workspace is not null && TargetFolder() is not null, null,
            _ => Workspace is null ? "Open a folder first." : "Cut or copy a file or folder in the explorer first.");
        Command(ShellCommandIds.Duplicate, "Explorer: Duplicate", (_, token) => DuplicateAsync(TargetEntry(), token), _ => CanChange(TargetEntry().Path), null, _ => WhyNotChange(TargetEntry().Path));
        Command(ShellCommandIds.SelectForCompare, "File: Select for compare", (_, _) => SelectForCompare(TargetFile()), _ => TargetFile() is not null, null, _ => "A new file is not on the disk yet.");
        Command(ShellCommandIds.CompareWithSelected, "File: Compare with the selected file", (_, token) => CompareWithSelectedAsync(TargetFile(), token),
            _ => _compareFirst is { } first && TargetFile() is { } file && !PathKeys.Comparer.Equals(first, file), null,
            _ => _compareFirst is null ? "Pick Select for compare on another file first." : "This is the file selected for compare: pick another.");
        Command(ShellCommandIds.CopyName, "File: Copy name", (_, _) => CopyAsync(TargetName()), _ => _clipboard is not null && TargetName() is not null, null, NotOnDisk);
        Command(ShellCommandIds.CollapseFolder, "Explorer: Collapse all inside", (_, _) => Collapse(TargetFolder()), _ => Tree is not null && TargetEntry() is { IsFolder: true, Path: { } folder } && Tree.IsExpanded(folder), null,
            _ => Tree is null ? "Open a folder first." : "Nothing in it is open.");
        Command(ShellCommandIds.CollapseAll, "Explorer: Collapse all", (_, _) => Collapse(null), _ => Tree is not null, null, NoFolder);
        Command(ShellCommandIds.CloseSavedTabs, "Tab: Close saved", (_, token) => CloseSavedAsync(token),
            _ => TargetGroup()?.Tabs.Tabs.Any(t => !t.IsDirty && !t.IsPinned) == true, null, _ => "Every tab has unsaved changes or is pinned.");
        Command(ShellCommandIds.CloseAllTabs, "Tab: Close all", (_, token) => CloseAllAsync(token), _ => TargetGroup()?.Tabs.Tabs.Count > 0, null);
        Command(ShellCommandIds.PinTab, "Tab: Pin", (_, _) => Pin(TargetTab(), true), _ => TargetTab() is { IsPinned: false }, null);
        Command(ShellCommandIds.UnpinTab, "Tab: Unpin", (_, _) => Pin(TargetTab(), false), _ => TargetTab() is { IsPinned: true }, null);
        Command(ShellCommandIds.CompareWithSaved, "File: Compare with saved", (_, token) => CompareWithSavedAsync(TargetTab(), token), _ => TargetTab() is { FilePath: not null, IsDirty: true }, null, WhyNotUnsaved);
        Command(ShellCommandIds.RevertFile, "File: Revert", (_, token) => RevertAsync(TargetTab(), token), _ => TargetTab() is { FilePath: not null, IsDirty: true }, null, WhyNotUnsaved);
    }

    private string? WhyNotUnsaved(IEditorHost host) => TargetTab() switch
    {
        null => "No file is open.",
        { FilePath: null } => "A new file has no saved text.",
        var tab => $"{tab.Title} has no unsaved changes.",
    };

    // The menu's file (a folder: none), else the active document's.
    private string? TargetFile() => TargetEntry() is { Path: { } path, IsFolder: false } ? path : null;

    // The menu's file's or folder's name, else the target tab's title (an untitled one).
    private string? TargetName() => TargetEntry().Path is { } path ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : TargetTab()?.Title;

    // The group of the menu's tab, else the focused one.
    private EditorGroup? TargetGroup() => TargetTab() is { } tab ? Groups.GroupOf(tab) : Groups.Focused;

    private void Source(MenuItemSource source)
    {
        EditorMenuSources.Register(source);
        _menuSources.Add(source.Name);
    }

    private void DisposeContext()
    {
        foreach (var name in _menuSources)
        {
            EditorMenuSources.Unregister(name);
        }

        _menuSources.Clear();
        CancelTreeEdit();
    }

    // "Open with": one row per registered language (its file icon, its name), then plain text.
    private static List<MenuItem> OpenWithRows()
    {
        var rows = new List<MenuItem>();
        int order = 0;
        foreach (var language in EditorLanguages.All.OrderBy(l => LanguageTitle(l), StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new MenuItem(EditorContextMenus.OpenWith, ShellCommandIds.OpenWithLanguage, LanguageTitle(language), order++, 0)
            {
                Icon = language.Extensions.Count > 0 ? MenuIcons.File("file" + language.Extensions[0]) : MenuIcons.Open,
                Argument = language.Name,
            });
        }

        rows.Add(new MenuItem(EditorContextMenus.OpenWith, ShellCommandIds.OpenWithLanguage, "Plain text", order, 1) { Icon = MenuIcons.Open, Argument = "" });
        return rows;
    }

    // A language as a row names it: its summary up to the first comma ("C#, on Roslyn's parser": "C#"), else its name.
    private static string LanguageTitle(ILanguage language)
    {
        var summary = language.Summary;
        int comma = summary.IndexOf(',', StringComparison.Ordinal);
        return comma > 0 ? summary[..comma] : summary.Length is > 0 and <= 40 ? summary : language.Name;
    }

    // "Run": each of the target's run targets, a group each, with its actions (run, build, test).
    private List<MenuItem> RunRows(ContextMenuTarget? target)
    {
        var rows = new List<MenuItem>();
        if (target?.Path is not { } path)
        {
            return rows;
        }

        int group = 0;
        foreach (var run in RunTargetsOf(path, target.Kind != MenuTargetKinds.File))
        {
            int order = 0;
            foreach (var (action, id, verb, icon) in RunVerbs)
            {
                if ((run.Actions & action) != 0)
                {
                    rows.Add(new MenuItem(EditorContextMenus.RunTargets, ShellCommandIds.RunTarget, $"{verb} {run.Title}", order++, group)
                    {
                        Icon = icon,
                        Argument = id + "|" + RunView.KeyOf(run),
                    });
                }
            }

            group++;
        }

        return rows;
    }

    private static readonly (RunActions Action, string Id, string Verb, string Icon)[] RunVerbs =
    [
        (RunActions.Run, "run.target", "Run", RailIcons.Run),
        (RunActions.Build, "run.build", "Build", RailIcons.Build),
        (RunActions.Test, "run.test", "Test", RailIcons.Test),
    ];

    // A row of "Run": its action on its target, through the Run view (approved, sandboxed, its own output tab).
    private Task RunChosenAsync(CancellationToken cancellationToken)
    {
        if (EditorContextMenus.Target?.Argument is not { } argument || argument.IndexOf('|', StringComparison.Ordinal) is not (> 0 and var bar))
        {
            return Task.CompletedTask;
        }

        return Run.ActAsync(argument[..bar], argument[(bar + 1)..], cancellationToken);
    }

    private async Task OpenToSideAsync(string? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return;
        }

        var focused = Groups.Focused;
        int at = Groups.NumberOf(focused) - 1;
        var side = at + 1 < Groups.Groups.Count ? Groups.Groups[at + 1] : Groups.Split(focused, SplitDirection.Right);
        if (side is null)
        {
            Host.ShowStatus("Open to the side", string.Create(CultureInfo.InvariantCulture,
                $"{Groups.Groups.Count} editor groups are open, the most CRAFT_EDITOR_GROUPS_MAX allows."), NotificationSeverity.Warning);
            return;
        }

        if (await OpenInGroupAsync(side, file, activate: true, cancellationToken) is null && side.Tabs.Tabs.Count == 0)
        {
            Groups.Close(side);
        }
    }

    private async Task OpenWithAsync(CancellationToken cancellationToken)
    {
        var target = EditorContextMenus.Target;
        if (TargetFile() is not { } file || target?.Argument is not { } name)
        {
            return;
        }

        ILanguage? language = name.Length == 0 ? null : EditorLanguages.Find(name);
        if (name.Length > 0 && language is null)
        {
            Host.ShowStatus("Open with", $"The language '{name}' is not registered (any more).", NotificationSeverity.Warning);
            return;
        }

        var tab = target.Item as DocumentTab ?? await OpenFileAsync(file, fromTree: target.Menu == EditorContextMenus.Tree, cancellationToken);
        if (tab is not null && Groups.GroupOf(tab) is not null)
        {
            Groups.SetLanguage(tab, language, language?.Name);
        }
    }

    // Find in folder: the Search view, its files to include set to the folder (everything under it).
    private Task FindInFolder(string? folder)
    {
        if (folder is null || Workspace is not { } workspace)
        {
            return Task.CompletedTask;
        }

        var relative = Path.GetRelativePath(workspace.Root, folder).Replace('\\', '/');
        Search.SetFilters(relative is "." or "" ? "" : relative + "/**", Search.Exclude);
        return ShowSearch();
    }

    private bool CanFormat()
    {
        if (EditorContextMenus.Target is { Item: DocumentTab tab } && EditorOf?.Invoke(tab) is { } editor)
        {
            return editor.FormattingSession is not null;
        }

        var file = TargetFile() ?? (TargetTab() is { } active && EditorContextMenus.Target is null ? active.FilePath : null);
        if (file is null)
        {
            return TargetTab() is { } shown && EditorOf?.Invoke(shown)?.FormattingSession is not null;
        }

        return EditorLanguages.ForFile(file) is not null;
    }

    // Format file: the file opened first when it is not (in the focused group), then formatted by its editor off the UI
    // thread as one undo step; it stays unsaved for the user to see and save.
    private async Task FormatFileAsync(CancellationToken cancellationToken)
    {
        var tab = EditorContextMenus.Target is { Item: DocumentTab target } ? target : null;
        var file = TargetFile();
        if (tab is null && file is not null)
        {
            tab = await OpenFileAsync(file, cancellationToken: cancellationToken);
        }

        tab ??= Tabs.Active;
        if (tab is null)
        {
            return;
        }

        if (EditorOf?.Invoke(tab) is not { } editor)
        {
            Host.ShowStatus("Format file", $"{tab.Title}: no editor shows it yet.", NotificationSeverity.Warning);
            return;
        }

        if (editor.FormattingSession is null)
        {
            Host.ShowStatus("Format file", $"{tab.Title}: its language has no formatter.", NotificationSeverity.Warning);
            return;
        }

        if (!await editor.FormatDocumentAsync(cancellationToken) && editor.LastFormatMessage is { } why)
        {
            Host.ShowStatus("Format file", $"{tab.Title}: {why}", NotificationSeverity.Warning);
        }
    }

    private Task HoldFiles(bool cut)
    {
        if (TargetEntry().Path is not { } path || !CanChange(path))
        {
            return Task.CompletedTask;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        _fileClipboard = ([full], cut);
        Host.ShowStatus(cut ? "Cut" : "Copied", $"{Path.GetFileName(full)}: Paste on a folder of the explorer {(cut ? "moves" : "copies")} it there.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pastes what was cut or copied in the explorer into <paramref name="folder"/> (X part 7): copies get a free name
    /// ("Name copy.cs") when the folder has the name, cut entries move (their clean tabs follow; refused as a drag's move
    /// is), all off the UI thread as an activity; the Undo bar takes it back. Null when done, else why not.
    /// </summary>
    public async Task<string?> PasteAsync(string? folder, CancellationToken cancellationToken = default)
    {
        if (folder is null || _fileClipboard is not { } held || Workspace is not { } workspace)
        {
            return "Nothing to paste.";
        }

        var into = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!PathKeys.Comparer.Equals(into, workspace.Root) && !workspace.Contains(into))
        {
            return "The folder is not in the open folder.";
        }

        string where = WhereOf(into);
        if (held.Cut)
        {
            var moved = new List<(string Moved, string From)>();
            string? problem = null;
            foreach (var source in held.Paths)
            {
                if (PathKeys.Comparer.Equals(Path.GetDirectoryName(source) ?? "", into))
                {
                    continue;                                    // pasted where it is: nothing to do
                }

                if (await MoveEntryCoreAsync(source, into, copy: false, offerUndo: false, cancellationToken) is { } why)
                {
                    problem ??= why;
                    continue;
                }

                moved.Add((Path.Combine(into, Path.GetFileName(source)), Path.GetDirectoryName(source)!));
            }

            _fileClipboard = null;                               // a cut is pasted once
            if (moved.Count > 0)
            {
                UndoBar.Show(moved.Count == 1 ? $"Moved {Path.GetFileName(moved[0].Moved)} to {where}" : string.Create(CultureInfo.InvariantCulture, $"Moved {moved.Count} items to {where}"),
                    () => _ = MoveAllBackAsync(moved));
            }

            return problem;
        }

        string what = held.Paths.Length == 1 ? Path.GetFileName(held.Paths[0]) : string.Create(CultureInfo.InvariantCulture, $"{held.Paths.Length} items");
        List<(string Path, bool IsFolder)> copies;
        List<string> problems;
        using (var activity = Host.Activities.Start($"Copying {what}"))
        {
            (copies, problems) = await Task.Run(() => CopyInto(held.Paths, into), cancellationToken);
            if (copies.Count == 0 && problems.Count > 0)
            {
                activity.Fail(problems[0]);
            }
            else
            {
                activity.Complete("copied");
            }
        }

        foreach (var (copy, _) in copies)
        {
            _treeWatch?.Touch(copy);
        }

        _treeWatch?.Touch(into);
        if (copies.Count > 0)
        {
            Tree?.Reveal(copies[^1].Path);
            UndoBar.Show(copies.Count == 1 ? $"Pasted {Path.GetFileName(copies[0].Path)} in {where}" : string.Create(CultureInfo.InvariantCulture, $"Pasted {copies.Count} items in {where}"),
                () =>
                {
                    foreach (var (copy, isFolder) in copies)
                    {
                        _ = UndoCopyAsync(copy, isFolder);
                    }
                });
        }

        if (problems.Count > 0)
        {
            Host.ShowStatus("Not pasted", string.Join(" ", problems), NotificationSeverity.Warning);
            return problems[0];
        }

        return null;
    }

    // The copies of a paste, off the UI thread: each under its own name, else a free "copy" name; a folder never into itself.
    private static (List<(string Path, bool IsFolder)> Copies, List<string> Problems) CopyInto(string[] sources, string into)
    {
        var copies = new List<(string, bool)>();
        var problems = new List<string>();
        foreach (var source in sources)
        {
            string name = Path.GetFileName(source);
            try
            {
                bool isFolder = Directory.Exists(source);
                if (!isFolder && !File.Exists(source))
                {
                    problems.Add($"{name} is no longer there.");
                    continue;
                }

                if (isFolder && (PathKeys.Comparer.Equals(source, into) || IsUnder(into, source)))
                {
                    problems.Add($"{name} cannot go into itself.");
                    continue;
                }

                string target = Path.Combine(into, name);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    target = FreeCopyName(into, name, isFolder);
                }

                if (isFolder)
                {
                    CopyFolder(source, target);
                }
                else
                {
                    File.Copy(source, target);
                }

                copies.Add((target, isFolder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {e.Message}");
            }
        }

        return (copies, problems);
    }

    // Undo of a cut's paste: each entry back to the folder it came from.
    private async Task MoveAllBackAsync(List<(string Moved, string From)> moved)
    {
        foreach (var (entry, from) in moved)
        {
            await MoveEntryBackAsync(entry, from);
        }
    }

    // Duplicate: a copy beside the entry ("Name copy.cs"), with Undo (the copy goes).
    private async Task DuplicateAsync((string? Path, bool IsFolder) entry, CancellationToken cancellationToken)
    {
        if (entry.Path is not { } path || !CanChange(path))
        {
            return;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        await MoveEntryAsync(full, Path.GetDirectoryName(full)!, copy: true, cancellationToken);
    }

    private Task SelectForCompare(string? file)
    {
        if (file is null)
        {
            return Task.CompletedTask;
        }

        _compareFirst = Path.GetFullPath(file);
        Host.ShowStatus("Compare", $"{Path.GetFileName(file)} is selected: pick Compare with selected on another file.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares the file selected for compare with <paramref name="file"/> in the diff view (X part 7: the review across
    /// files): the selected file's lines against this file's, each read off the UI thread (an open document's current
    /// text); accepting a hunk takes this file's lines into the selected one (one undo step, or the file saved), with Undo.
    /// </summary>
    public async Task CompareWithSelectedAsync(string? file, CancellationToken cancellationToken = default)
    {
        if (file is null || _compareFirst is not { } first)
        {
            return;
        }

        string firstName = Path.GetFileName(first), name = Path.GetFileName(file);
        string? text = await CurrentTextAsync(file, cancellationToken);
        if (text is null)
        {
            return;
        }

        int lineLimit = Options.DiffLineLimit;
        var result = await Host.ProposeFilesAsync([new FileChange(first, snapshot => LineEdits.Between(snapshot, text, lineLimit))],
            $"Compare {firstName} with {name}: a hunk accepted takes {name}'s lines into {firstName}", cancellationToken);
        if (result is null)
        {
            Host.ShowStatus("Compare", string.Equals(await CurrentTextAsync(first, cancellationToken), text, StringComparison.Ordinal)
                ? $"{firstName} and {name} are the same." : $"{firstName} was left as it is.");
            return;
        }

        UndoBar.Show($"Took {name}'s lines into {firstName}", () => _ = result.UndoAsync(), result.Keep);
    }

    // The text of `path` as the editor has it: an open document's current text, else the file read off the UI thread;
    // null (said) when it cannot be read.
    private async Task<string?> CurrentTextAsync(string path, CancellationToken cancellationToken)
    {
        if (Groups.FindOpen(path) is { } open)
        {
            var snapshot = open.Document.Buffer.Current;
            return await Task.Run(() => snapshot.GetText(0, snapshot.Length), cancellationToken);
        }

        return await SavedTextAsync(path, cancellationToken);
    }

    // The file's text on the disk (read and decoded off the UI thread as a document is), or null (said).
    private async Task<string?> SavedTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await _openDocument(path, Options, cancellationToken);
            var snapshot = document.Buffer.Current;
            return await Task.Run(() => snapshot.GetText(0, snapshot.Length), cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Text.DecoderFallbackException)
        {
            Host.ShowStatus("Could not read", $"{Path.GetFileName(path)}: {e.Message}", NotificationSeverity.Warning);
            return null;
        }
    }

    /// <summary>
    /// Compares <paramref name="tab"/>'s unsaved text with its file in the diff view (X part 7): each hunk is a change since
    /// the save, and accepting one takes it back to the saved text (one undo step); taken all the way back, the tab is
    /// clean again.
    /// </summary>
    public async Task CompareWithSavedAsync(DocumentTab? tab, CancellationToken cancellationToken = default)
    {
        if (tab is not { FilePath: { } path, IsDirty: true })
        {
            return;
        }

        if (await SavedTextAsync(path, cancellationToken) is not { } saved)
        {
            return;
        }

        var snapshot = tab.Document.Buffer.Current;
        int lineLimit = Options.DiffLineLimit;
        var edits = await Task.Run(() => LineEdits.Between(snapshot, saved, lineLimit), cancellationToken);
        if (edits.Length == 0)
        {
            (tab.Document as TextDocument)?.MarkUnchanged();
            Host.ShowStatus("Compare with saved", $"{tab.Title} is the same as its file.");
            return;
        }

        if (await Host.ProposeEditsAsync(tab.HostDocument, edits, $"{tab.Title} against its saved file: a hunk accepted goes back to the saved text", cancellationToken)
            && Groups.GroupOf(tab) is not null)
        {
            var now = tab.Document.Buffer.Current;
            if (now.Length == saved.Length && await Task.Run(() => now.GetText(0, now.Length) == saved, cancellationToken))
            {
                (tab.Document as TextDocument)?.MarkUnchanged();
            }
        }
    }

    /// <summary>
    /// Takes <paramref name="tab"/>'s text back to its file's (read off the UI thread) as one undo step, clean again
    /// (X part 7); the Undo bar (or Ctrl+Z) brings the changes back.
    /// </summary>
    public async Task RevertAsync(DocumentTab? tab, CancellationToken cancellationToken = default)
    {
        if (tab is not { FilePath: { } path, IsDirty: true })
        {
            return;
        }

        using var activity = Host.Activities.Start($"Reverting {tab.Title}");
        if (await SavedTextAsync(path, cancellationToken) is not { } saved)
        {
            activity.Fail("the file could not be read");
            return;
        }

        var snapshot = tab.Document.Buffer.Current;
        int lineLimit = Options.DiffLineLimit;
        var edits = await Task.Run(() => LineEdits.Between(snapshot, saved, lineLimit), cancellationToken);
        if (Groups.GroupOf(tab) is null || tab.Document.Buffer.Current.Version != snapshot.Version)
        {
            activity.Complete("the text changed meanwhile: not reverted");
            return;
        }

        if (edits.Length > 0)
        {
            var before = tab.HostDocument.Selection;
            var after = SelectionState.At(Math.Min(before.Caret, saved.Length));
            tab.Document.Edit(edits, EditKind.Command, before, after);
            Host.Select(tab.HostDocument, after);
        }

        (tab.Document as TextDocument)?.MarkUnchanged();
        int version = tab.Document.Buffer.Current.Version;
        activity.Complete("reverted");
        if (edits.Length > 0)
        {
            UndoBar.Show($"Reverted {tab.Title}", () => UndoRevert(tab, version));
        }
    }

    private void UndoRevert(DocumentTab tab, int version)
    {
        if (Groups.GroupOf(tab) is null || tab.Document.Buffer.Current.Version != version || !tab.Document.CanUndo)
        {
            Host.ShowStatus("Undo revert", $"{tab.Title} changed since: Ctrl+Z in its editor takes the steps back one by one.", NotificationSeverity.Warning);
            return;
        }

        if (tab.Document.Undo() is { } selection)
        {
            Host.Select(tab.HostDocument, selection);
        }
    }

    private async Task CloseSavedAsync(CancellationToken cancellationToken)
    {
        if (TargetGroup() is not { } group)
        {
            return;
        }

        foreach (var tab in group.Tabs.Tabs.Where(t => !t.IsDirty && !t.IsPinned).ToArray())
        {
            await CloseTabAsync(tab, cancellationToken);
        }
    }

    // Close all: every tab of the group, pinned ones too; stops when an unsaved one is kept open.
    private async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        if (TargetGroup() is not { } group)
        {
            return;
        }

        foreach (var tab in group.Tabs.Tabs.ToArray())
        {
            if (!await CloseTabAsync(tab, cancellationToken))
            {
                break;
            }
        }
    }

    private Task Pin(DocumentTab? tab, bool pinned)
    {
        if (tab is not null && Groups.GroupOf(tab) is { } group)
        {
            group.Tabs.SetPinned(tab, pinned);
        }

        return Task.CompletedTask;
    }

    private Task Collapse(string? folder)
    {
        Tree?.Collapse(folder);
        return Task.CompletedTask;
    }

    /// <summary>Reads the tree's shown folders again off the UI thread (Refresh), then shows what changed.</summary>
    public async Task RefreshTreeAsync(CancellationToken cancellationToken = default)
    {
        if (Tree is not { } tree)
        {
            return;
        }

        var folders = tree.ShownFolders();
        var workspace = tree.Workspace;
        var listings = await Task.Run(() =>
        {
            var read = new List<FolderListing>(folders.Count);
            foreach (var folder in folders)
            {
                bool exists = Directory.Exists(folder);
                read.Add(new FolderListing(folder, exists, exists ? workspace.ListFolder(PathKeys.Comparer.Equals(folder, workspace.Root) ? null : folder) : []));
            }

            return read;
        }, cancellationToken);
        if (ReferenceEquals(Tree, tree))
        {
            tree.Apply(listings);
        }
    }

    // Begins naming in place in the tree, when it can show the row: false (the dialog asks instead) otherwise.
    private bool BeginTreeEdit(TreeEditKind kind, string folder, string? path, bool isFolder = false)
    {
        if (!InlineTreeEdits || Layout.IsNarrow || Tree is not { } tree)
        {
            return false;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!PathKeys.Comparer.Equals(full, tree.Workspace.Root) && !tree.Workspace.Contains(full))
        {
            return false;
        }

        CancelTreeEdit();
        if (kind == TreeEditKind.Rename ? path is null || !tree.Reveal(path) : !tree.BeginNew(full, kind == TreeEditKind.NewFolder))
        {
            return false;
        }

        _ = Panels.ShowSide(PanelLayout.ExplorerView);
        TreeEdit = new TreeEdit(kind, full, path, path is null ? "" : Path.GetFileName(path)) { IsFolder = kind == TreeEditKind.NewFolder || (kind == TreeEditKind.Rename && isFolder) };
        TreeEditChanged?.Invoke(this, EventArgs.Empty);
        RevealRequested?.Invoke(this, TreeEdit.RowPath);
        return true;
    }

    /// <summary>
    /// Why <paramref name="name"/> cannot be the name typed in place now (shown as it is typed, from what the tree holds:
    /// no disk), or null when it can.
    /// </summary>
    public string? TreeEditProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (TreeEdit is not { } edit)
        {
            return null;
        }

        name = name.Trim();
        if (edit.Kind == TreeEditKind.Rename && name == edit.Name)
        {
            return null;
        }

        if (NameProblem(name) is { } problem)
        {
            return problem;
        }

        bool caseOnly = edit.Kind == TreeEditKind.Rename && string.Equals(name, edit.Name, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Tree?.EntriesOf(edit.Folder) is { } entries && entries.Any(e => PathKeys.Comparer.Equals(e.Name, name)))
        {
            return $"{name} is in {WhereOf(edit.Folder)} already.";
        }

        return null;
    }

    /// <summary>
    /// Takes the name typed in place (Enter, or the field losing the keyboard): makes the new file (opened) or folder, or
    /// renames (with Undo), off the UI thread. A name that cannot be is said and keeps the field; an empty new name, or the
    /// same name, ends it. True when something was made or renamed.
    /// </summary>
    public async Task<bool> CommitTreeEditAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (TreeEdit is not { } edit)
        {
            return false;
        }

        name = name.Trim();
        if ((edit.Kind != TreeEditKind.Rename && name.Length == 0) || (edit.Kind == TreeEditKind.Rename && name == edit.Name))
        {
            CancelTreeEdit();
            return false;
        }

        string title = edit.Kind switch
        {
            TreeEditKind.NewFile => "The file was not made",
            TreeEditKind.NewFolder => "The folder was not made",
            _ => "Not renamed",
        };
        if (TreeEditProblem(name) is { } problem)
        {
            Host.ShowStatus(title, problem, NotificationSeverity.Warning);
            return false;
        }

        EndTreeEdit();
        string? error = edit.Kind == TreeEditKind.Rename
            ? await RenameEntryAsync(edit.Path!, edit.IsFolder, name, offerUndo: true, cancellationToken)
            : await CreateEntryAsync(edit.Folder, name, edit.Kind == TreeEditKind.NewFolder, cancellationToken);
        if (error is not null)
        {
            Host.ShowStatus(title, error, NotificationSeverity.Warning);
            return false;
        }

        return true;
    }

    /// <summary>Ends naming in place without a change (Escape).</summary>
    public void CancelTreeEdit()
    {
        if (TreeEdit is not null)
        {
            EndTreeEdit();
        }
    }

    private void EndTreeEdit()
    {
        TreeEdit = null;
        Tree?.EndNew();
        TreeEditChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The edits that make a text another (X part 7: compare, revert): one edit a changed run of lines, found by the line diff
/// (<see cref="TextDiff"/>), so a review shows each run as its own hunk and a revert keeps the carets of unchanged lines;
/// proven on the result (a diff whose lines do not map back gives one edit of the whole text). Off the UI thread.
/// </summary>
public static class LineEdits
{
    /// <summary>The edits from <paramref name="snapshot"/>'s text to <paramref name="text"/>, in order; none when they are the same.</summary>
    public static TextEdit[] Between(ITextSnapshot snapshot, string text, int lineLimit)
    {
        string old = snapshot.GetText(0, snapshot.Length);
        if (string.Equals(old, text, StringComparison.Ordinal))
        {
            return [];
        }

        var diff = TextDiff.OfTexts(old, text, 0, Math.Max(1, lineLimit));
        var oldStarts = LineStarts(old);
        var newStarts = LineStarts(text);
        var edits = new List<TextEdit>(diff.Hunks.Count);
        foreach (var hunk in diff.Hunks)
        {
            int oldFirst = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
            int newFirst = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;
            if (oldFirst < 0 || oldFirst + hunk.OldCount >= oldStarts.Count || newFirst < 0 || newFirst + hunk.NewCount >= newStarts.Count)
            {
                return Whole(old, text);
            }

            int start = oldStarts[oldFirst], end = oldStarts[oldFirst + hunk.OldCount];
            int from = newStarts[newFirst], to = newStarts[newFirst + hunk.NewCount];
            edits.Add(new TextEdit(start, end - start, text[from..to]));
        }

        // The edits must give the text exactly (a last line without a break, a lone '\r', a coarse run): else one edit.
        var made = new System.Text.StringBuilder(text.Length);
        int at = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < at)
            {
                return Whole(old, text);
            }

            made.Append(old, at, edit.Start - at).Append(edit.Text);
            at = edit.Start + edit.Length;
        }

        made.Append(old, at, old.Length - at);
        return made.Length == text.Length && made.ToString() == text ? [.. edits] : Whole(old, text);
    }

    private static TextEdit[] Whole(string old, string text) => [new TextEdit(0, old.Length, text)];

    // Where each line starts as the diff splits lines (after "\r\n", "\r" or "\n"), then the text's end.
    private static List<int> LineStarts(string text)
    {
        var starts = new List<int>(64) { 0 };
        var span = text.AsSpan();
        int at = 0;
        while (at < span.Length)
        {
            int index = span[at..].IndexOfAny('\r', '\n');
            if (index < 0)
            {
                break;
            }

            at += index + (span[at + index] == '\r' && at + index + 1 < span.Length && span[at + index + 1] == '\n' ? 2 : 1);
            if (at < span.Length)
            {
                starts.Add(at);
            }
        }

        starts.Add(text.Length);
        return starts;
    }
}
