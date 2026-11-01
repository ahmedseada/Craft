// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>
/// Moving files and folders by dragging them in the tree (the X restart, piece 3a): a row dropped on a folder moves there
/// (Ctrl copies), the disk's work off the UI thread as an activity, the moved file's clean tabs following it in their
/// groups, and the inline bar saying "Moved X to Y" with Undo. A folder is not moved to another drive (said); a name the
/// folder has already, a folder into itself and a file with unsaved changes are refused, said.
/// </summary>
public sealed partial class ShellModel
{
    /// <summary>
    /// Moves (or with <paramref name="copy"/>, copies) the file or folder <paramref name="source"/> into
    /// <paramref name="targetFolder"/>, both in the workspace: null when it was done (or there was nothing to do: the same
    /// folder), else why not, said in the status bar too. Undo in the inline bar moves it back (or deletes the copy).
    /// </summary>
    public Task<string?> MoveEntryAsync(string source, string targetFolder, bool copy, CancellationToken cancellationToken = default) =>
        MoveEntryCoreAsync(source, targetFolder, copy, offerUndo: true, cancellationToken);

    private async Task<string?> MoveEntryCoreAsync(string source, string targetFolder, bool copy, bool offerUndo, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string into = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder));
        string name = Path.GetFileName(from);
        // The workspace's own folder is a target too (Workspace.Contains says no for the root itself).
        if (Workspace is not { } workspace || !CanChange(from) || !(workspace.Contains(into) || PathKeys.Comparer.Equals(into, Path.TrimEndingDirectorySeparator(workspace.Root))))
        {
            return Refuse(copy, $"{name} and {Path.GetFileName(into)} must both be in the open folder (and the folder itself does not move).");
        }

        // What is there, asked off the UI thread once.
        var (isFile, isFolder, intoExists) = await Task.Run(() => (File.Exists(from), Directory.Exists(from), Directory.Exists(into)), cancellationToken);
        if (!isFile && !isFolder)
        {
            return Refuse(copy, $"{name} is no longer there.");
        }

        if (!intoExists)
        {
            return Refuse(copy, $"{Path.GetFileName(into)} is not a folder.");
        }

        bool sameFolder = PathKeys.Comparer.Equals(Path.GetDirectoryName(from) ?? "", into);
        if (sameFolder && !copy)
        {
            return null;                                     // dropped where it is: nothing to do
        }

        if (isFolder && (PathKeys.Comparer.Equals(from, into) || IsUnder(into, from)))
        {
            return Refuse(copy, $"{name} cannot go into itself.");
        }

        if (isFolder && !copy && !SameVolumeOf(from, into))
        {
            return Refuse(copy, $"{name} is a folder on another drive than {Path.GetFileName(into)}: Craft does not move a folder between drives. Ctrl+drag copies it.");
        }

        var open = TabsOf(from, isFolder);
        if (!copy && open.Find(t => t.IsDirty) is { } dirty)
        {
            return Refuse(copy, $"{dirty.Title} has unsaved changes: save it or close it, then move it.");
        }

        string destination = Path.Combine(into, name);
        string? error;
        string verb = copy ? "Copying" : "Moving";
        using (var activity = Host.Activities.Start($"{verb} {name}", ActivityKinds.General))
        {
            (destination, error) = await Task.Run(() =>
            {
                try
                {
                    string target = copy && sameFolder ? FreeCopyName(from, isFolder) : destination;
                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        return (target, $"{Path.GetFileName(into)} has a {Path.GetFileName(target)} already.");
                    }

                    if (copy)
                    {
                        if (isFolder)
                        {
                            CopyFolder(from, target);
                        }
                        else
                        {
                            File.Copy(from, target);
                        }
                    }
                    else if (isFolder)
                    {
                        Directory.Move(from, target);
                    }
                    else
                    {
                        File.Move(from, target);
                    }

                    return (target, (string?)null);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return (destination, e.Message);
                }
            }, cancellationToken);
            if (error is not null)
            {
                activity.Fail(error);
            }
            else
            {
                activity.Complete(copy ? "copied" : "moved");
            }
        }

        if (error is not null)
        {
            return Refuse(copy, error);
        }

        if (!copy)
        {
            await FollowMovedAsync(open, from, destination, cancellationToken);
        }

        _treeWatch?.Touch(from);
        _treeWatch?.Touch(destination);
        Tree?.Reveal(destination);
        string where = RelativeOf(into) is { Length: > 0 } relative && relative != "." ? relative.Replace('\\', '/') : Path.GetFileName(into);
        string moved = Path.GetFileName(destination);
        string originalFolder = Path.GetDirectoryName(from)!;
        if (!offerUndo)
        {
            return null;
        }

        if (copy)
        {
            UndoBar.Show($"Copied {moved} to {where}", () => _ = UndoCopyAsync(destination, isFolder));
        }
        else
        {
            UndoBar.Show($"Moved {moved} to {where}", () => _ = MoveEntryBackAsync(destination, originalFolder));
        }

        return null;
    }

    // Undo of a move: back to the folder it came from, under the same rules (its tabs follow again); not itself offered
    // for undo.
    private Task<string?> MoveEntryBackAsync(string moved, string originalFolder) =>
        MoveEntryCoreAsync(moved, originalFolder, copy: false, offerUndo: false, CancellationToken.None);

    // Undo of a copy: the copy goes (its clean tabs close); one the user changed since is kept, said.
    private async Task UndoCopyAsync(string copied, bool isFolder)
    {
        var open = TabsOf(copied, isFolder);
        if (open.Find(t => t.IsDirty) is { } dirty)
        {
            Host.ShowStatus("Undo copy", $"{dirty.Title} has unsaved changes: the copy is kept.", NotificationSeverity.Warning);
            return;
        }

        foreach (var tab in open)
        {
            Groups.Remove(tab);
        }

        string? error = await Task.Run(() =>
        {
            try
            {
                if (isFolder)
                {
                    Directory.Delete(copied, recursive: true);
                }
                else
                {
                    File.Delete(copied);
                }

                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        });
        _treeWatch?.Touch(copied);
        if (error is not null)
        {
            Host.ShowStatus("Undo copy", $"{Path.GetFileName(copied)} was not deleted: {error}", NotificationSeverity.Warning);
        }
    }

    private string Refuse(bool copy, string why)
    {
        Host.ShowStatus(copy ? "Not copied" : "Not moved", why, NotificationSeverity.Warning);
        return why;
    }

    // Whether `path` is inside `folder` (not the folder itself).
    private static bool IsUnder(string path, string folder) =>
        path.Length > folder.Length + 1 && path.StartsWith(folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        && (path[folder.Length] == Path.DirectorySeparatorChar || path[folder.Length] == Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Whether two paths are on one volume, for the refusal of a folder's move to another drive: <see cref="SameVolume"/> by
    /// default (the paths' roots); a platform that knows its mounts, or a test, sets its own.
    /// </summary>
    public Func<string, string, bool> SameVolumeOf { get; set; } = SameVolume;

    /// <summary>Whether two paths are on the same volume as their roots say (a drive letter or a share on Windows; one root elsewhere).</summary>
    public static bool SameVolume(string first, string second) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(first)), Path.GetPathRoot(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    // "Name copy.cs", "Name copy 2.cs", ... beside the original (a copy dropped on its own folder).
    private static string FreeCopyName(string path, bool isFolder) => FreeCopyName(Path.GetDirectoryName(path)!, Path.GetFileName(path), isFolder);

    // "Name copy.cs", "Name copy 2.cs", ... in `folder` for an entry named `name` (a paste onto a name the folder has).
    private static string FreeCopyName(string folder, string name, bool isFolder)
    {
        string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isFolder ? "" : Path.GetExtension(name);
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(folder, n == 1 ? $"{stem} copy{extension}" : string.Create(CultureInfo.InvariantCulture, $"{stem} copy {n}{extension}"));
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // A folder copied with everything in it (its folders first, then its files).
    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }
}
