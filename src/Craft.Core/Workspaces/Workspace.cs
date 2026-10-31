// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Documents;

namespace Craft.Core.Workspaces;

/// <summary>
/// An open folder: its files, listed lazily (folders named in <see cref="CraftOptions.WorkspaceIgnore"/> skipped, and
/// links to folders not followed), and its open documents by path (one document per file). Used on the UI thread.
/// </summary>
public sealed class Workspace : IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly CraftOptions _options;
    private readonly HashSet<string> _ignore;
    private readonly Dictionary<string, TextDocument> _documents = new(PathComparer);

    /// <summary>A workspace over the folder <paramref name="root"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
    public Workspace(string root, CraftOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root))
        {
            throw new DirectoryNotFoundException($"The workspace folder '{Root}' does not exist.");
        }

        _options = options ?? new CraftOptions();
        _ignore = new HashSet<string>(_options.WorkspaceIgnore, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The folder, as a full path.</summary>
    public string Root { get; }

    /// <summary>The open documents.</summary>
    public IReadOnlyCollection<TextDocument> Documents => _documents.Values;

    /// <summary>The workspace's files as full paths, read folder by folder as the caller goes (nothing is listed ahead).</summary>
    public IEnumerable<string> EnumerateFiles()
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };
        var folders = new Stack<string>();
        folders.Push(Root);
        while (folders.Count > 0)
        {
            var folder = folders.Pop();
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                yield return file;
            }

            var children = new List<string>();
            foreach (var child in new DirectoryInfo(folder).EnumerateDirectories("*", options))
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0 && !_ignore.Contains(child.Name))
                {
                    children.Add(child.FullName);
                }
            }

            children.Sort(PathComparer);
            for (int i = children.Count - 1; i >= 0; i--)
            {
                folders.Push(children[i]);
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> lies in the workspace and in no ignored folder.</summary>
    public bool Contains(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var relative = Path.GetRelativePath(Root, FullPath(path));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return false;
        }

        var folder = Path.GetDirectoryName(relative.AsSpan());
        var ignored = _ignore.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var part in folder.SplitAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ignored.Contains(folder[part]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The open document of <paramref name="path"/> (relative to the root or full), or null.</summary>
    public TextDocument? Find(string path) => _documents.GetValueOrDefault(FullPath(path));

    /// <summary>The document of <paramref name="path"/> (relative to the root or full): the open one, or read from the file.</summary>
    public async Task<TextDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        if (_documents.TryGetValue(full, out var open))
        {
            return open;
        }

        var document = await TextDocument.OpenAsync(full, _options, cancellationToken).ConfigureAwait(true);
        if (_documents.TryGetValue(full, out open))
        {
            document.Dispose();
            return open;
        }

        _documents.Add(full, document);
        document.Closed += (_, _) => _documents.Remove(full);
        return document;
    }

    /// <summary>Closes (disposes) the open document of <paramref name="path"/>; false when none was open.</summary>
    public bool Close(string path)
    {
        if (!_documents.TryGetValue(FullPath(path), out var document))
        {
            return false;
        }

        document.Dispose();
        return true;
    }

    /// <summary>Closes every open document.</summary>
    public void Dispose()
    {
        foreach (var document in _documents.Values.ToArray())
        {
            document.Dispose();
        }

        _documents.Clear();
    }

    private string FullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, Root);
    }
}
