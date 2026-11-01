// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The user's own snippets (X part 9), registered by the shell as the "user" provider: every <c>*.json</c> and
/// <c>*.code-snippets</c> file (VS Code's form: a name to <c>prefix</c>, <c>body</c>, <c>description</c> and
/// <c>scope</c>) of the user folder (<c>CRAFT_SNIPPETS_FOLDER</c>) and of the workspace's <c>.craft/snippets</c>. A file
/// named after a language ("csharp.json") is that language's; another one's snippets are for the languages of their
/// <c>scope</c> (separated by commas), or for every language. The files are read off the UI thread, again when a folder
/// watcher sees one made, removed or renamed or the editor saves one; what completion asks for is the last read, never
/// waiting. A file that does not parse is skipped and said (<see cref="Problems"/>).
/// </summary>
public sealed class UserSnippets : ISnippetProvider, IDisposable
{
    /// <summary>Its name in <see cref="SnippetProviders"/>.</summary>
    public const string ProviderName = "user";

    private static readonly JsonDocumentOptions Json = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _watches = [];
    private IReadOnlyList<Snippet> _snippets = [];
    private IReadOnlyList<string> _problems = [];
    private IReadOnlyList<string> _folders = [];
    private Task _loaded = Task.CompletedTask;
    private int _generation;
    private bool _disposed;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "your snippets: the user folder's and the workspace's .craft/snippets files";

    /// <summary>The folders read, in order (the user's, then the workspace's).</summary>
    public IReadOnlyList<string> Folders => Volatile.Read(ref _folders);

    /// <summary>The files that did not parse, each with why, from the last read.</summary>
    public IReadOnlyList<string> Problems => Volatile.Read(ref _problems);

    /// <summary>Every snippet of the last read.</summary>
    public IReadOnlyList<Snippet> All => Volatile.Read(ref _snippets);

    /// <summary>Completes when the last read asked for is in.</summary>
    public Task Loaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    /// <summary>How many times the files were read.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Snippet> SnippetsFor(string? language)
    {
        var all = Volatile.Read(ref _snippets);
        if (all.Count == 0)
        {
            return [];
        }

        List<Snippet>? found = null;
        foreach (var snippet in all)
        {
            if (snippet.IsFor(language))
            {
                (found ??= []).Add(snippet);
            }
        }

        return found is null ? [] : found;
    }

    /// <summary>
    /// Reads from <paramref name="userFolder"/> and <paramref name="workspaceRoot"/>'s <c>.craft/snippets</c> (either may be
    /// null) and watches both with <paramref name="watcher"/> (a <see cref="FolderWatchers"/> name, or null for none); the
    /// read runs off the calling thread.
    /// </summary>
    public Task SetFolders(string? userFolder, string? workspaceRoot, string? watcher)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(userFolder))
        {
            folders.Add(Path.GetFullPath(userFolder));
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            folders.Add(Path.Combine(Path.GetFullPath(workspaceRoot), ".craft", "snippets"));
        }

        lock (_gate)
        {
            foreach (var watch in _watches)
            {
                watch.Dispose();
            }

            _watches.Clear();
            Volatile.Write(ref _folders, folders);
            if (watcher is not null && FolderWatchers.Find(watcher) is { } found)
            {
                foreach (var folder in folders)
                {
                    // The watcher needs the folder: its parent is watched when it does not exist yet (a .craft made later).
                    string watched = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder) ?? folder;
                    if (!Directory.Exists(watched))
                    {
                        continue;
                    }

                    try
                    {
                        _watches.Add(found.Watch(watched, change =>
                        {
                            if (IsSnippetPath(change.Path) || (change.OldPath is { } old && IsSnippetPath(old)) || change.Kind == FolderChangeKind.Rescan)
                            {
                                _ = Reload();
                            }
                        }));
                    }
#pragma warning disable CA1031 // A folder that cannot be watched is read on saves and on the next open; the snippets stay.
                    catch (Exception)
#pragma warning restore CA1031
                    {
                    }
                }
            }
        }

        return Reload();
    }

    /// <summary>The editor saved <paramref name="path"/>: a snippets file of the folders is read again (off the calling thread).</summary>
    public void NoteSaved(string? path)
    {
        if (path is not null && IsSnippetPath(path))
        {
            _ = Reload();
        }
    }

    /// <summary>Reads every file of the folders again, off the calling thread.</summary>
    public Task Reload()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            int generation = ++_generation;
            var folders = Volatile.Read(ref _folders);
            var previous = _loaded;
            _loaded = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);               // reads in order: the newest lands last
                var (snippets, problems) = Read(folders);
                lock (_gate)
                {
                    if (generation == _generation || _snippets.Count == 0)
                    {
                        Volatile.Write(ref _snippets, snippets);
                        Volatile.Write(ref _problems, problems);
                        Reads++;
                    }
                }
            });
            return _loaded;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var watch in _watches)
            {
                watch.Dispose();
            }

            _watches.Clear();
        }
    }

    /// <summary>The snippets of every file in <paramref name="folders"/>, and the files that did not parse.</summary>
    public static (IReadOnlyList<Snippet> Snippets, IReadOnlyList<string> Problems) Read(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var snippets = new List<Snippet>();
        var problems = new List<string>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            string[] files;
            try
            {
                files = [.. Directory.EnumerateFiles(folder).Where(IsSnippetFile).Order(StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{folder}: {e.Message}");
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    snippets.AddRange(Parse(File.ReadAllBytes(file), file));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException)
                {
                    problems.Add($"{file}: {e.Message}");
                }
            }
        }

        return (snippets, problems);
    }

    /// <summary>
    /// The snippets of one file's UTF-8 JSON (VS Code's form). A file named after a language ("csharp.json") gives that
    /// language's snippets; a <c>scope</c> names others.
    /// </summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="FormatException">An entry has no body or no prefix.</exception>
    public static IReadOnlyList<Snippet> Parse(ReadOnlySpan<byte> utf8, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        string stem = Path.GetFileNameWithoutExtension(path);
        bool languageFile = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) && EditorLanguages.Find(stem) is not null;
        using var document = JsonDocument.Parse(utf8.ToArray(), Json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A snippets file is an object of named snippets.");
        }

        var found = new List<Snippet>();
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = entry.Value;
            var prefixes = Strings(value, "prefix");
            var body = Strings(value, "body");
            if (body.Count == 0)
            {
                throw new FormatException($"'{entry.Name}' has no body.");
            }

            if (prefixes.Count == 0)
            {
                continue;                                         // VS Code: a snippet without a prefix is only inserted by name (not offered)
            }

            string? description = value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            IReadOnlyList<string> languages = languageFile ? [stem]
                : value.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String
                    ? [.. scope.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                    : [];
            found.Add(new Snippet(entry.Name, prefixes, string.Join('\n', body), description) { Languages = languages, Source = path });
        }

        return found;
    }

    private static List<string> Strings(JsonElement value, string name)
    {
        var list = new List<string>();
        if (!value.TryGetProperty(name, out var property))
        {
            return list;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            list.Add(property.GetString()!);
        }
        else if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    list.Add(item.GetString()!);
                }
            }
        }

        return list;
    }

    private bool IsSnippetPath(string path)
    {
        if (!IsSnippetFile(path))
        {
            return false;
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        foreach (var known in Volatile.Read(ref _folders))
        {
            if (Core.Workspaces.PathKeys.Comparer.Equals(Path.TrimEndingDirectorySeparator(known), Path.TrimEndingDirectorySeparator(folder)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSnippetFile(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".code-snippets", StringComparison.OrdinalIgnoreCase);
}
