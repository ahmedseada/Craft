// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>
/// The split editor's commands (the X restart, piece 3a): split right and down, move the editor to the next group, close a
/// group, focus a group by its number; and the layout kept per workspace (<see cref="EditorLayoutFile"/>), written once
/// the editor area was split, read back by <see cref="RestoreEditorLayoutAsync"/>.
/// </summary>
public sealed partial class ShellModel
{
    private EditorLayoutFile? _layoutFile;
    private bool _layoutKept;

    /// <summary>The last write of the editor layout's file (the tests wait on it); completed when none is pending.</summary>
    public Task LayoutSaved { get; private set; } = Task.CompletedTask;

    /// <summary>The open workspace's editor layout file, or null with no folder open.</summary>
    public EditorLayoutFile? LayoutFile => _layoutFile;

    /// <summary>
    /// Splits the focused group <paramref name="direction"/>, the active document shown in the new group too; null, said
    /// in the status bar, when <c>CRAFT_EDITOR_GROUPS_MAX</c> groups are open.
    /// </summary>
    public EditorGroup? Split(SplitDirection direction)
    {
        // X part 7: a tab's menu splits from that tab and its group; the keys and the palette, the focused group's active tab.
        var tab = TargetTab();
        var group = Groups.Split(tab is not null && Groups.GroupOf(tab) is { } own ? own : Groups.Focused, direction, tab);
        if (group is null)
        {
            Host.ShowStatus("Split", string.Create(CultureInfo.InvariantCulture,
                $"{Groups.Groups.Count} editor groups are open, the most CRAFT_EDITOR_GROUPS_MAX allows (Settings › Appearance › Layout)."), NotificationSeverity.Warning);
        }

        return group;
    }

    /// <summary>
    /// Closes <paramref name="group"/> (the focused one when null): each of its tabs with unsaved changes that no other
    /// group shows asks first (a kept one stops it), then the group closes and its room goes to its neighbour; with one
    /// group, its tabs close. True when it closed.
    /// </summary>
    public async Task<bool> CloseGroupAsync(EditorGroup? group = null, CancellationToken cancellationToken = default)
    {
        group ??= Groups.Focused;
        foreach (var tab in group.Tabs.Tabs.ToArray())
        {
            if (tab.IsDirty && !Groups.IsShared(tab))
            {
                group.Tabs.Activate(tab);
                if (!await ResolveUnsavedAsync(tab, cancellationToken))
                {
                    return false;
                }
            }
        }

        if (Groups.Close(group))
        {
            return true;
        }

        foreach (var tab in group.Tabs.Tabs.ToArray())
        {
            Groups.Remove(tab);
        }

        return true;
    }

    /// <summary>
    /// Opens <paramref name="path"/> in <paramref name="group"/>: its tab there, a tab sharing the document another group
    /// shows, or the file read (off the UI thread) into a new tab; activated (and focused) when <paramref name="activate"/>.
    /// Null, said, when it cannot be read.
    /// </summary>
    public async Task<DocumentTab?> OpenInGroupAsync(EditorGroup group, string path, bool activate = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var tab = group.Tabs.Find(full);
        if (tab is null && Groups.FindOpen(full) is { } elsewhere)
        {
            tab = group.Tabs.AddShared(elsewhere, activate: false, takeView: false);
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

            if (!Groups.Groups.Contains(group))
            {
                document.Dispose();                        // the group closed while the file was read
                return null;
            }

            if (group.Tabs.Find(full) is { } opened)
            {
                document.Dispose();
                tab = opened;
            }
            else if (Groups.FindOpen(full) is { } openedElsewhere)
            {
                document.Dispose();
                tab = group.Tabs.AddShared(openedElsewhere, activate: false, takeView: false);
            }
            else
            {
                tab = group.Tabs.Add(document, EditorLanguages.ForFile(full), activate: false);
            }
        }

        if (activate)
        {
            group.Tabs.Activate(tab);
            Recent.Add(full, RecentKind.File);
        }

        return tab;
    }

    /// <summary>
    /// Shapes the editor area as the workspace's kept layout (its groups, their files and active files, the shares, the
    /// focus), read off the UI thread; the files still on disk open in their groups. False when there is no kept layout,
    /// no folder, or the editor area is already split. For a later session's restore.
    /// </summary>
    public async Task<bool> RestoreEditorLayoutAsync(CancellationToken cancellationToken = default)
    {
        if (_layoutFile is not { } file || Groups.Groups.Count != 1)
        {
            return false;
        }

        var state = await Task.Run(file.Load, cancellationToken);
        if (state is null || Groups.Groups.Count != 1 || Groups.Restore(state) is not { } shaped)
        {
            return false;
        }

        // Which kept files are still there, asked off the UI thread once.
        var wanted = shaped.SelectMany(g => g.Files).Distinct(PathKeys.Comparer).ToArray();
        var present = await Task.Run(() => wanted.Where(File.Exists).ToHashSet(PathKeys.Comparer), cancellationToken);
        var focused = Groups.Focused;
        foreach (var (group, files, active) in shaped)
        {
            DocumentTab? activeTab = null;
            foreach (var path in files)
            {
                if (present.Contains(path) && await OpenInGroupAsync(group, path, activate: false, cancellationToken) is { } tab
                    && active is not null && PathKeys.Comparer.Equals(Path.GetFullPath(active), tab.FilePath ?? ""))
                {
                    activeTab = tab;
                }
            }

            if (activeTab is not null)
            {
                group.Tabs.Activate(activeTab);
            }
        }

        if (Groups.Groups.Contains(focused))
        {
            Groups.Focus(focused);
        }

        return true;
    }

    private void RegisterGroupCommands()
    {
        Command(ShellCommandIds.SplitRight, "View: Split the editor right", (_, _) => { Split(SplitDirection.Right); return Task.CompletedTask; }, null, "Ctrl+\\");
        Command(ShellCommandIds.SplitDown, "View: Split the editor down", (_, _) => { Split(SplitDirection.Down); return Task.CompletedTask; }, null, "Ctrl+K Ctrl+\\");
        Command(ShellCommandIds.MoveToNextGroup, "View: Move the editor to the next group", (_, _) =>
        {
            if (TargetTab() is { } tab && Groups.MoveToNext(tab) is null)
            {
                Host.ShowStatus("Move the editor", Groups.Groups.Count >= Groups.Max
                    ? "The last group is focused and no more groups can open (CRAFT_EDITOR_GROUPS_MAX)."
                    : "The group's only tab stays: split it first (Ctrl+\\).");
            }

            return Task.CompletedTask;
        }, _ => Tabs.Active is not null, "Ctrl+Alt+Right");
        Command(ShellCommandIds.CloseGroup, "View: Close the editor group", (_, token) => CloseGroupAsync(null, token), null, null);
        Command(ShellCommandIds.FocusNextGroup, "View: Focus the next editor group", (_, _) => { Groups.FocusNext(); return Task.CompletedTask; }, _ => Groups.Groups.Count > 1, null);
        for (int number = 1; number <= 3; number++)
        {
            int n = number;
            Command(ShellCommandIds.FocusGroupPrefix + n.ToString(CultureInfo.InvariantCulture), string.Create(CultureInfo.InvariantCulture, $"View: Focus editor group {n}"), (_, _) =>
            {
                if (!Groups.FocusNumber(n))
                {
                    Host.ShowStatus("Editor groups", string.Create(CultureInfo.InvariantCulture, $"There is no group {n} and no more can open (CRAFT_EDITOR_GROUPS_MAX)."));
                }

                return Task.CompletedTask;
            }, null, string.Create(CultureInfo.InvariantCulture, $"Ctrl+{n}"));
        }

        Command(ShellCommandIds.PeekDefinition, "Go: Peek definition", (_, _) =>
        {
            if (Tabs.Active is { } tab && EditorOf?.Invoke(tab) is { } editor && !editor.PeekDefinition())
            {
                Host.ShowStatus("Peek definition", "The file's language has no definitions to peek.");
            }

            return Task.CompletedTask;
        }, _ => Tabs.Active is { } active && EditorOf?.Invoke(active)?.SemanticSession is not null, "Alt+F12");
    }

    // The layout is written once the editor area was split (a single group never makes the file), after every change of
    // its groups, tabs, focus or shares, on the thread pool.
    private void KeepLayout()
    {
        if (_disposed || _layoutFile is not { } file || (Groups.Groups.Count < 2 && !_layoutKept))
        {
            return;
        }

        _layoutKept = true;
        var write = file.SaveAsync(Groups.Capture());
        LayoutSaved = write;
        _ = write.ContinueWith(done => Host.Dispatch("shell: the editor layout was not written", () =>
            Host.Log(EditorLogLevel.Warning, $"The editor layout was not written to {file.FilePath}: {done.Exception?.GetBaseException().Message}", done.Exception)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // The workspace opened (or closed): its layout file, and whether one was kept already (asked off the UI thread).
    private void UseLayoutFile(string? root, bool kept)
    {
        _layoutFile = root is null ? null : new EditorLayoutFile(root);
        _layoutKept = kept;
    }
}
