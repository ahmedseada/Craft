// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

// X part 9, item 17: a file(line,col) or file:line link clicked in the terminal or Output opens the file at its place. The
// path is resolved off the UI thread: as it is when it is absolute (a drive letter, a UNC path, a root), else against the
// run's folder (the terminal's) and then the workspace's.
public sealed partial class ShellModel
{
    /// <summary>
    /// Opens the file a link names at its <paramref name="line"/> and <paramref name="column"/> (both from 1; a column of 0
    /// is the line's start), the path resolved off the UI thread against <paramref name="runFolder"/> and then the
    /// workspace; false, said in the status bar, when no such file exists.
    /// </summary>
    public async Task<bool> OpenLinkAsync(string path, int line, int column, string? runFolder = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = Workspace?.Root;
        var found = await Task.Run(() => ResolveLink(path, runFolder, root), cancellationToken);
        if (found is null)
        {
            Host.ShowStatus("Open link", $"{path}: no such file{(runFolder is null ? "" : " in the run's folder")}{(root is null ? "" : " or the workspace")}.", NotificationSeverity.Warning);
            return false;
        }

        return await OpenAtLineAsync(found, Math.Max(1, line) - 1, Math.Max(1, column) - 1, cancellationToken);
    }

    /// <summary>
    /// The file a link's <paramref name="path"/> names: itself when absolute, else the first that exists of it under
    /// <paramref name="runFolder"/> and under <paramref name="workspace"/>; null when none does. Reads the disk: off the UI
    /// thread.
    /// </summary>
    public static string? ResolveLink(string path, string? runFolder, string? workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = path.Trim().Trim('"', '\'');
        if (!OperatingSystem.IsWindows())
        {
            text = text.Replace('\\', '/');                     // a Windows tool's relative path on another system
        }

        bool absolute = Path.IsPathFullyQualified(text) || (text.Length > 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/');
        if (absolute)
        {
            return Exists(text);
        }

        foreach (var folder in (ReadOnlySpan<string?>)[runFolder, workspace])
        {
            if (!string.IsNullOrWhiteSpace(folder) && Exists(Path.Combine(folder, text)) is { } found)
            {
                return found;
            }
        }

        return null;

        static string? Exists(string candidate)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                return File.Exists(full) ? full : null;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
