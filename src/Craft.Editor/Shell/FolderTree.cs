// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>A visible row of the folder tree.</summary>
/// <param name="Entry">The file or folder.</param>
/// <param name="Depth">Its depth below the root (0 for the root's own entries).</param>
/// <param name="IsExpanded">Whether it is a folder shown open.</param>
public sealed record FolderTreeRow(WorkspaceEntry Entry, int Depth, bool IsExpanded);

/// <summary>
/// The folder tree of a <see cref="Workspace"/> as the rows the view shows: the root's entries, and below each expanded
/// folder its own. A folder is read when it is first expanded and kept (<see cref="Refresh"/> reads the open ones again),
/// so nothing below a closed folder is ever listed. Used on the UI thread.
/// </summary>
public sealed class FolderTree
{
    private readonly Dictionary<string, IReadOnlyList<WorkspaceEntry>> _listed;
    private readonly HashSet<string> _expanded;
    private IReadOnlyList<WorkspaceEntry> _root;
    private List<FolderTreeRow> _rows = [];

    /// <summary>The tree of <paramref name="workspace"/>, its root listed.</summary>
    public FolderTree(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Workspace = workspace;
        var comparer = PathKeys.Comparer;
        _listed = new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(comparer);
        _expanded = new HashSet<string>(comparer);
        _root = workspace.ListFolder();
        Rebuild();
    }

    /// <summary>The workspace.</summary>
    public Workspace Workspace { get; }

    /// <summary>The visible rows, top to bottom.</summary>
    public IReadOnlyList<FolderTreeRow> Rows => _rows;

    /// <summary>Raised when <see cref="Rows"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The row of <paramref name="path"/> (a full path), or -1 when it is not visible.</summary>
    public int IndexOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var comparer = PathKeys.Comparer;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (comparer.Equals(_rows[i].Entry.FullPath, path))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Opens the folder of row <paramref name="index"/> when closed, closes it when open; false for a file's row.</summary>
    public bool Toggle(int index)
    {
        if ((uint)index >= (uint)_rows.Count || !_rows[index].Entry.IsFolder)
        {
            return false;
        }

        var path = _rows[index].Entry.FullPath;
        if (!_expanded.Remove(path))
        {
            _expanded.Add(path);
        }

        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Opens every folder above <paramref name="path"/> (a file or folder of the workspace) so its row shows; false when it is outside the workspace.</summary>
    public bool Reveal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!Workspace.Contains(full))
        {
            return false;
        }

        bool changed = false;
        for (var folder = Path.GetDirectoryName(full); folder is not null && !PathKeys.Comparer.Equals(folder, Workspace.Root); folder = Path.GetDirectoryName(folder))
        {
            changed |= _expanded.Add(folder);
        }

        if (changed)
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Reads the root and every open folder again (files added or removed outside the editor).</summary>
    public void Refresh()
    {
        _listed.Clear();
        _root = Workspace.ListFolder();
        _expanded.RemoveWhere(folder => !Directory.Exists(folder));
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var rows = new List<FolderTreeRow>(_rows.Count + 16);
        Add(rows, _root, 0);
        _rows = rows;
    }

    private void Add(List<FolderTreeRow> rows, IReadOnlyList<WorkspaceEntry> entries, int depth)
    {
        foreach (var entry in entries)
        {
            bool expanded = entry.IsFolder && _expanded.Contains(entry.FullPath);
            rows.Add(new FolderTreeRow(entry, depth, expanded));
            if (expanded)
            {
                if (!_listed.TryGetValue(entry.FullPath, out var children))
                {
                    children = Workspace.ListFolder(entry.FullPath);
                    _listed[entry.FullPath] = children;
                }

                Add(rows, children, depth + 1);
            }
        }
    }
}
