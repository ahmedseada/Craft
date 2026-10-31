// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>A file the agent changed in its working copy: its path in the workspace, whether it is new, and the lines added and removed.</summary>
/// <param name="Path">The file's full path in the user's workspace.</param>
/// <param name="Relative">Its path relative to the workspace ('/' separated).</param>
/// <param name="IsNew">Whether the workspace has no such file (the agent created it).</param>
/// <param name="Added">Lines added.</param>
/// <param name="Removed">Lines removed.</param>
public sealed record AgentChange(string Path, string Relative, bool IsNew, int Added, int Removed)
{
    /// <summary>"+14 −0", or "new +31".</summary>
    public string Counts => IsNew
        ? string.Create(CultureInfo.InvariantCulture, $"new +{Added}")
        : string.Create(CultureInfo.InvariantCulture, $"+{Added} −{Removed}");
}

/// <summary>
/// The agent's working copy (plan 1 phase A2, "every edit held as a diff"): the workspace copied once into a folder of its
/// own (the folders of <see cref="AiSettings.AgentCopyIgnore"/> left out; obj kept, so a build needs no restore), with the
/// open documents' unsaved text written over their files, so the agent sees what the user sees. Idrak's coding tools edit
/// and build there; the user's files change only when the user accepts a file's diff in the editor
/// (the agent panel's Review diff), and <see cref="Discard"/> drops it all.
/// </summary>
public sealed class AgentWorkspace : IDisposable
{
    private readonly Dictionary<string, string> _base = new(StringComparer.Ordinal);
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);

    private AgentWorkspace(string workspace, string copy, int files)
    {
        Workspace = workspace;
        Copy = copy;
        Files = files;
    }

    /// <summary>The user's workspace.</summary>
    public string Workspace { get; }

    /// <summary>The working copy's folder.</summary>
    public string Copy { get; }

    /// <summary>The files copied.</summary>
    public int Files { get; }

    /// <summary>
    /// Copies <paramref name="workspace"/> into a new folder under <paramref name="folder"/> (the system's temporary folder
    /// when empty), leaving out <paramref name="ignore"/>, then writes <paramref name="unsaved"/> (full path to text) over their copies.
    /// </summary>
    public static AgentWorkspace Create(string workspace, string folder, IReadOnlyList<string> ignore, IReadOnlyDictionary<string, string> unsaved,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(ignore);
        ArgumentNullException.ThrowIfNull(unsaved);
        workspace = System.IO.Path.GetFullPath(workspace);
        string root = folder.Length > 0 ? folder : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "craft-agent");
        string name = System.IO.Path.GetFileName(workspace.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        string copy = System.IO.Path.Combine(root, string.Concat(name, "-", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), "-",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8]));
        var skip = new HashSet<string>(ignore, StringComparer.OrdinalIgnoreCase);
        int files = 0;
        var pending = new Stack<string>();
        pending.Push(workspace);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string from = pending.Pop();
            string to = System.IO.Path.Combine(copy, System.IO.Path.GetRelativePath(workspace, from));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)));
                files++;
            }

            foreach (var directory in Directory.EnumerateDirectories(from))
            {
                // The copies' own folder, when it is inside the workspace, is never copied into itself.
                if (!skip.Contains(System.IO.Path.GetFileName(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0
                    && !string.Equals(System.IO.Path.GetFullPath(directory), System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                {
                    pending.Push(directory);
                }
            }
        }

        var created = new AgentWorkspace(workspace, copy, files);
        foreach (var (path, text) in unsaved)
        {
            if (created.CopyOf(path) is { } target)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
                created._base[System.IO.Path.GetFullPath(path)] = text;
            }
        }

        return created;
    }

    /// <summary>The copy of the workspace file <paramref name="path"/>, or null when it is outside the workspace.</summary>
    public string? CopyOf(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string relative = System.IO.Path.GetRelativePath(Workspace, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? null : System.IO.Path.Combine(Copy, relative);
    }

    /// <summary>The workspace file of the copy's <paramref name="relative"/> path (as the agent's tools name it).</summary>
    public string WorkspacePath(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Workspace, relative));

    /// <summary>
    /// The change of the file the agent's tool named <paramref name="relative"/> (relative to the copy), against the
    /// workspace's text when the copy was made (or what the user accepted since); null when it is the same.
    /// </summary>
    public AgentChange? ChangeOf(string relative)
    {
        string target = WorkspacePath(relative);
        string copy = System.IO.Path.Combine(Copy, System.IO.Path.GetRelativePath(Workspace, target));
        if (!File.Exists(copy))
        {
            return null;
        }

        string? before = BaseText(target);
        string after = File.ReadAllText(copy);
        if (before == after)
        {
            return null;
        }

        var diff = TextDiff.OfTexts(before ?? "", after, contextLines: 0);
        return new AgentChange(target, System.IO.Path.GetRelativePath(Workspace, target).Replace('\\', '/'), before is null, diff.Added, diff.Removed);
    }

    /// <summary>The copy's text of <paramref name="path"/> (a workspace path).</summary>
    public string NewText(string path) => File.ReadAllText(CopyOf(path) ?? throw new ArgumentException($"{path} is outside the workspace.", nameof(path)));

    /// <summary>The workspace's text of <paramref name="path"/> the change is measured against: as it was copied (unsaved text included), or null for a new file.</summary>
    public string? BaseText(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (_base.TryGetValue(full, out var text))
        {
            return text;
        }

        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    /// <summary>Marks <paramref name="path"/>'s change settled (accepted into the editor): later changes are measured from <paramref name="text"/>.</summary>
    public void Settle(string path, string text)
    {
        string full = System.IO.Path.GetFullPath(path);
        _base[full] = text;
        _settled.Add(full);
    }

    /// <summary>
    /// The edits that turn <paramref name="current"/> into <paramref name="target"/>, one per changed run of lines (so the
    /// diff view accepts them hunk by hunk); one edit for the whole changed middle when the lines do not rebuild it exactly.
    /// </summary>
    public static IReadOnlyList<TextEdit> Edits(string current, string target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        if (current == target)
        {
            return [];
        }

        var diff = TextDiff.OfTexts(current, target, contextLines: 0);
        var oldStarts = LineStarts(current);
        var newStarts = LineStarts(target);
        var edits = new List<TextEdit>(diff.Hunks.Count);
        foreach (var hunk in diff.Hunks)
        {
            int oldIndex = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
            int newIndex = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;
            int start = oldStarts[Math.Min(oldIndex, oldStarts.Count - 1)];
            int end = oldStarts[Math.Min(oldIndex + hunk.OldCount, oldStarts.Count - 1)];
            int from = newStarts[Math.Min(newIndex, newStarts.Count - 1)];
            int to = newStarts[Math.Min(newIndex + hunk.NewCount, newStarts.Count - 1)];
            edits.Add(new TextEdit(start, end - start, target[from..to]));
        }

        if (Apply(current, edits) == target)
        {
            return edits;
        }

        // The line diff did not rebuild the text (a last line without its break): one edit over the common start and end.
        int prefix = 0;
        int max = Math.Min(current.Length, target.Length);
        while (prefix < max && current[prefix] == target[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < max - prefix && current[^(suffix + 1)] == target[^(suffix + 1)])
        {
            suffix++;
        }

        return [new TextEdit(prefix, current.Length - prefix - suffix, target[prefix..^suffix])];
    }

    /// <summary>Removes the working copy.</summary>
    public void Discard()
    {
        try
        {
            if (Directory.Exists(Copy))
            {
                Directory.Delete(Copy, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A file held open (a build's) stays until the next start; nothing of the user's is touched.
        }
    }

    /// <inheritdoc />
    public void Dispose() => Discard();

    // Each line's start, then the text's end (the end of the last line, whether or not it ends with a break).
    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        if (starts[^1] != text.Length)
        {
            starts.Add(text.Length);
        }

        return starts;
    }

    private static string Apply(string text, List<TextEdit> edits)
    {
        var result = new System.Text.StringBuilder(text.Length);
        int at = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < at || edit.Start + edit.Length > text.Length)
            {
                return "\0";
            }

            result.Append(text, at, edit.Start - at).Append(edit.Text);
            at = edit.Start + edit.Length;
        }

        return result.Append(text, at, text.Length - at).ToString();
    }
}
