// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>
/// Keeps a <see cref="FolderTree"/> following the disk: a registered <see cref="IFolderWatcher"/> (<c>CRAFT_WORKSPACE_WATCHER</c>)
/// reports what was made, removed or renamed, on its own threads; a change inside an ignored folder
/// (<c>CRAFT_WORKSPACE_IGNORE</c>) or outside the workspace is dropped there; the folders the rest touched are gathered for
/// <c>CRAFT_WORKSPACE_WATCH_MS</c>, read once each on the thread pool, and handed to the tree through the host's dispatch
/// in one <see cref="FolderTree.Apply"/> (one rebuild of the rows per batch, nothing read on the UI thread). A file the
/// editor saves, creates or renames (<see cref="Touch"/>) is read at once, without the wait. A lost track (the watcher's
/// buffer overflowed) reads again every folder the tree holds.
/// </summary>
public sealed class FolderTreeWatch : IDisposable
{
    private readonly FolderTree _tree;
    private readonly Workspace _workspace;
    private readonly Action<Action> _dispatch;
    private readonly int _delay;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _pending = new(PathKeys.Comparer);
    private readonly IDisposable _watch;
    private bool _rescan;
    private bool _scheduled;
    private bool _disposed;
    private int _batches;

    /// <summary>Watches <paramref name="tree"/>'s folder with <paramref name="watcher"/>; batches reach the tree through <paramref name="dispatch"/> (the UI thread).</summary>
    /// <exception cref="IOException">The folder cannot be watched (the watcher says why).</exception>
    public FolderTreeWatch(FolderTree tree, IFolderWatcher watcher, int delayMilliseconds, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(dispatch);
        _tree = tree;
        _workspace = tree.Workspace;
        _dispatch = dispatch;
        _delay = Math.Max(0, delayMilliseconds);
        _watch = watcher.Watch(_workspace.Root, OnChanged);
    }

    /// <summary>
    /// Raised on the watcher's threads (never the UI thread) with each entry made, removed or renamed (both paths of a
    /// rename): the shell reads a file's settings again when an <c>.editorconfig</c> changed (X part 9).
    /// </summary>
    public event Action<string>? EntryChanged;

    /// <summary>The batches applied to the tree so far.</summary>
    public int Batches => Volatile.Read(ref _batches);

    /// <summary>The editor wrote <paramref name="path"/> (a save, a save as, a new file): its folder is read now, not after the wait.</summary>
    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Add(path))
        {
            Schedule(now: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        _watch.Dispose();
    }

    // The watcher's threads: the folders the change touched are kept; one flush is scheduled per batch.
    private void OnChanged(FolderChange change)
    {
        bool added;
        if (change.Kind == FolderChangeKind.Rescan)
        {
            lock (_gate)
            {
                _rescan = true;
            }

            added = true;
        }
        else
        {
            added = Add(change.Path);
            EntryChanged?.Invoke(change.Path);
            if (change.OldPath is { } old)
            {
                added |= Add(old);
                EntryChanged?.Invoke(old);
            }
        }

        if (added)
        {
            Schedule(now: false);
        }
    }

    // The entry's folder, when the entry is the workspace's and in no ignored folder.
    private bool Add(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
            if (!_workspace.Contains(full))
            {
                return false;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }

        if (Path.GetDirectoryName(full) is not { } folder)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _pending.Add(Path.TrimEndingDirectorySeparator(folder));
            return true;
        }
    }

    private void Schedule(bool now)
    {
        lock (_gate)
        {
            if (_disposed || (_scheduled && !now))
            {
                return;                      // the batch already waiting takes this change
            }

            _scheduled = true;
        }

        if (now || _delay == 0)
        {
            _ = Task.Run(Flush);
        }
        else
        {
            _ = Task.Delay(_delay).ContinueWith(_ => Flush(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    // The thread pool: the gathered folders are read once each, then applied on the UI thread in one go.
    private void Flush()
    {
        string[] folders;
        bool rescan;
        lock (_gate)
        {
            _scheduled = false;
            if (_disposed)
            {
                return;
            }

            folders = [.. _pending];
            _pending.Clear();
            rescan = _rescan;
            _rescan = false;
        }

        if (rescan)
        {
            // What the tree holds is the UI thread's to read; the reading itself comes back here.
            _dispatch(() =>
            {
                var shown = _tree.ShownFolders();
                _ = Task.Run(() => Read([.. shown, .. folders]));
            });
            return;
        }

        if (folders.Length > 0)
        {
            Read(folders);
        }
    }

    private void Read(string[] folders)
    {
        var listings = new List<FolderListing>(folders.Length);
        foreach (var folder in folders.Distinct(PathKeys.Comparer))
        {
            bool exists = Directory.Exists(folder);
            listings.Add(new FolderListing(folder, exists, exists ? _workspace.ListFolder(folder) : []));
        }

        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            _tree.Apply(listings);
            Interlocked.Increment(ref _batches);
        });
    }
}
