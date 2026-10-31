// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>A folder's entries as read again off the UI thread (<see cref="FolderTree.Apply"/>).</summary>
/// <param name="Folder">The folder's full path.</param>
/// <param name="Exists">Whether it was there when read.</param>
/// <param name="Entries">Its entries (folders first, as <see cref="Workspace.ListFolder"/> gives them); none when it is gone.</param>
public sealed record FolderListing(string Folder, bool Exists, IReadOnlyList<WorkspaceEntry> Entries);

/// <summary>A visible row of the folder tree.</summary>
/// <param name="Entry">The file or folder.</param>
/// <param name="Depth">Its depth below the root (0 for the root's own entries).</param>
/// <param name="IsExpanded">Whether it is a folder shown open.</param>
public sealed record FolderTreeRow(WorkspaceEntry Entry, int Depth, bool IsExpanded);

/// <summary>
/// The folder tree of a <see cref="Workspace"/> as the rows the view shows: the root's entries, and below each expanded
/// folder its own. A folder is read when it is first expanded and kept (<see cref="Refresh"/> reads the open ones again),
/// so nothing below a closed folder is ever listed. It follows the disk through <see cref="FolderTreeWatch"/>, which reads
/// the folders a change touched off the UI thread and hands them to <see cref="Apply"/>. Used on the UI thread.
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
        // Normalized as Reveal does: on Windows "a/b" and "a\b" are the same file (a caller's path may mix them).
        var full = path.Length == 0 ? path : Path.GetFullPath(path);
        var comparer = PathKeys.Comparer;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (comparer.Equals(_rows[i].Entry.FullPath, full))
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

    /// <summary>The folders whose entries the tree holds now: the root and every folder listed (the watch reads these again after a rescan).</summary>
    public IReadOnlyList<string> ShownFolders()
    {
        var folders = new List<string>(_listed.Count + 1) { Workspace.Root };
        folders.AddRange(_listed.Keys);
        return folders;
    }

    /// <summary>
    /// Takes the entries of folders read again off the UI thread (<see cref="FolderTreeWatch"/>): each folder the tree
    /// holds gets its new entries (one missing on the disk loses them, and its open state), a subfolder that went away is
    /// forgotten with everything below it, a folder the tree does not hold is passed over. The rows are rebuilt and
    /// <see cref="Changed"/> raised once, only when something the tree holds changed.
    /// </summary>
    public bool Apply(IReadOnlyList<FolderListing> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        var comparer = PathKeys.Comparer;
        bool changed = false;
        foreach (var listing in listings)
        {
            IReadOnlyList<WorkspaceEntry> old;
            bool isRoot = comparer.Equals(listing.Folder, Workspace.Root);
            if (isRoot)
            {
                old = _root;
            }
            else if (!_listed.TryGetValue(listing.Folder, out old!))
            {
                continue;
            }

            if (Same(old, listing.Entries))
            {
                continue;
            }

            changed = true;
            if (isRoot)
            {
                _root = listing.Entries;
            }
            else if (listing.Exists)
            {
                _listed[listing.Folder] = listing.Entries;
            }
            else
            {
                Forget(listing.Folder);
            }

            // A subfolder that is gone: what was listed or open below it goes too.
            foreach (var entry in old)
            {
                if (entry.IsFolder && !Contains(listing.Entries, entry.FullPath))
                {
                    Forget(entry.FullPath);
                }
            }
        }

        if (changed)
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    private static bool Same(IReadOnlyList<WorkspaceEntry> a, IReadOnlyList<WorkspaceEntry> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].IsFolder != b[i].IsFolder || !string.Equals(a[i].FullPath, b[i].FullPath, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<WorkspaceEntry> entries, string path)
    {
        var comparer = PathKeys.Comparer;
        foreach (var entry in entries)
        {
            if (entry.IsFolder && comparer.Equals(entry.FullPath, path))
            {
                return true;
            }
        }

        return false;
    }

    // The folder and everything below it leave the listed and the open sets.
    private void Forget(string folder)
    {
        var below = folder + Path.DirectorySeparatorChar;
        var comparison = ReferenceEquals(PathKeys.Comparer, StringComparer.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var key in _listed.Keys.Where(k => PathKeys.Comparer.Equals(k, folder) || k.StartsWith(below, comparison)).ToArray())
        {
            _listed.Remove(key);
        }

        _expanded.RemoveWhere(k => PathKeys.Comparer.Equals(k, folder) || k.StartsWith(below, comparison));
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

/// <summary>The one run of rows that differs between two lists of rows (what a view replaces; the rows around it stay as they are).</summary>
/// <param name="Start">The index of the first row that differs.</param>
/// <param name="Removed">The old rows from <see cref="Start"/> that go.</param>
/// <param name="Inserted">The new rows from <see cref="Start"/> that come in their place.</param>
public readonly record struct RowChange(int Start, int Removed, int Inserted)
{
    /// <summary>Whether nothing differs.</summary>
    public bool IsEmpty => Removed == 0 && Inserted == 0;

    /// <summary>
    /// The run of <paramref name="next"/> that differs from <paramref name="old"/>: the rows the two share at the start
    /// and at the end are kept (a folder opened inserts its children only; the active file's row changes alone), walked
    /// once from each end.
    /// </summary>
    public static RowChange Between<T>(IReadOnlyList<T> old, IReadOnlyList<T> next, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(next);
        comparer ??= EqualityComparer<T>.Default;
        int start = 0;
        int shortest = Math.Min(old.Count, next.Count);
        while (start < shortest && comparer.Equals(old[start], next[start]))
        {
            start++;
        }

        int oldEnd = old.Count;
        int newEnd = next.Count;
        while (oldEnd > start && newEnd > start && comparer.Equals(old[oldEnd - 1], next[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }

        return new RowChange(start, oldEnd - start, newEnd - start);
    }
}
