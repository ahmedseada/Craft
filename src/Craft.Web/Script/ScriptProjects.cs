// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text.Json;

namespace Craft.Web.Script;

/// <summary>
/// What a script project's files say (W-script's versions): the ECMAScript <c>target</c> of its <c>jsconfig.json</c> (else
/// its <c>tsconfig.json</c>), its <c>extends</c> followed; the TypeScript it has (<c>node_modules/typescript/package.json</c>,
/// else <c>devDependencies</c> or <c>dependencies</c>); whether it uses React or Preact (JSX in .js files).
/// </summary>
/// <param name="Folder">The project's folder (the nearest one up from a file holding a tsconfig.json, jsconfig.json or package.json), or the file's own when none does.</param>
/// <param name="EcmaScript">The target's ECMAScript level, or null when no config names one.</param>
/// <param name="TypeScript">The TypeScript level installed or asked for, or null when none is.</param>
/// <param name="UsesJsx">Whether its package.json depends on react or preact.</param>
public sealed record ScriptProject(string Folder, int? EcmaScript, int? TypeScript, bool UsesJsx);

/// <summary>
/// The script projects read so far, by folder: read once per folder, off the UI thread (<see cref="ReadAsync"/>), as UTF-8
/// bytes with <see cref="Utf8JsonReader"/> (comments and trailing commas allowed, as TypeScript reads its configs; rules 49,
/// 74); <see cref="Find"/> answers from memory only (no I/O, for the UI thread). The cache is bounded (rule 77): past
/// <see cref="Capacity"/> folders it starts again.
/// </summary>
public static class ScriptProjects
{
    /// <summary>The folders held at most before the cache starts again.</summary>
    public const int Capacity = 4096;

    private const int MaxLevels = 32;
    private const int MaxExtends = 8;
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, ScriptProject> ByFolder = new(StringComparer.Ordinal);
    private static long _reads;

    /// <summary>The project folders read from disk since the start (or <see cref="Forget"/>): one per folder.</summary>
    public static long Reads => Interlocked.Read(ref _reads);

    /// <summary>The project of the file at <paramref name="filePath"/> when its folder was read, else null. No I/O.</summary>
    public static ScriptProject? Find(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || Path.GetDirectoryName(Path.GetFullPath(filePath)) is not { } folder)
        {
            return null;
        }

        return ByFolder.TryGetValue(folder, out var project) ? project : null;
    }

    /// <summary>The project of the file at <paramref name="filePath"/> (within <paramref name="workspaceRoot"/>, when given), read off the calling thread once per folder.</summary>
    public static Task<ScriptProject> ReadAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => ReadFolder(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Path.GetFullPath(filePath), workspaceRoot), cancellationToken);
    }

    /// <summary>Reads the workspace's own folder (a language selector's preparation when a folder opens), off the calling thread.</summary>
    public static Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Task.Run(() => ReadFolder(Path.GetFullPath(workspaceRoot), workspaceRoot), cancellationToken);
    }

    /// <summary>Forgets every folder read (a project's files changed; the next open reads them again).</summary>
    public static void Forget()
    {
        ByFolder.Clear();
        Interlocked.Exchange(ref _reads, 0);
    }

    /// <summary>The project of <paramref name="folder"/> (a file's folder), reading it when it was not: blocking I/O, never on the UI thread.</summary>
    public static ScriptProject ReadFolder(string folder, string? workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        folder = Path.GetFullPath(folder);
        if (ByFolder.TryGetValue(folder, out var known))
        {
            return known;
        }

        string? root = string.IsNullOrEmpty(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? projectFolder = null;
        string? at = folder;
        for (int level = 0; level < MaxLevels && at is not null; level++)
        {
            if (File.Exists(Path.Combine(at, "tsconfig.json")) || File.Exists(Path.Combine(at, "jsconfig.json")) || File.Exists(Path.Combine(at, "package.json")))
            {
                projectFolder = at;
                break;
            }

            if (root is not null && string.Equals(at.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.Ordinal))
            {
                break;
            }

            at = Path.GetDirectoryName(at);
        }

        ScriptProject project;
        if (projectFolder is null)
        {
            project = new ScriptProject(folder, null, null, false);
        }
        else if (ByFolder.TryGetValue(projectFolder, out var shared))
        {
            project = shared;
        }
        else
        {
            Interlocked.Increment(ref _reads);
            project = Read(projectFolder, root);
            Remember(projectFolder, project);
        }

        Remember(folder, project);
        return project;
    }

    private static void Remember(string folder, ScriptProject project)
    {
        if (ByFolder.Count >= Capacity)
        {
            ByFolder.Clear();
        }

        ByFolder[folder] = project;
    }

    private static ScriptProject Read(string folder, string? root)
    {
        int? target = Target(Path.Combine(folder, "jsconfig.json"), root, 0, []) ?? Target(Path.Combine(folder, "tsconfig.json"), root, 0, []);
        var package = Package(Path.Combine(folder, "package.json"));
        int? typeScript = null;
        if (FindUp(folder, root, Path.Combine("node_modules", "typescript", "package.json")) is { } installed && Package(installed).Version is { } version)
        {
            typeScript = ScriptVersion.ParseTypeScript(version);
        }

        typeScript ??= package.TypeScript is { } wanted ? ScriptVersion.ParseTypeScript(wanted) : null;
        return new ScriptProject(folder, target, typeScript, package.Jsx);
    }

    // A config's compilerOptions.target, else that of what it extends (the last of an array that names one), bounded.
    private static int? Target(string path, string? root, int depth, HashSet<string> seen)
    {
        if (depth > MaxExtends || !seen.Add(path) || Bytes(path) is not { } bytes)
        {
            return null;
        }

        string? own = null;
        var extends = new List<string>();
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                if (reader.ValueTextEquals("extends"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        extends.Add(reader.GetString()!);
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                extends.Add(reader.GetString()!);
                            }
                        }
                    }
                }
                else if (reader.ValueTextEquals("compilerOptions"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("target"u8))
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                own = reader.GetString();
                            }
                        }
                        else if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            return null;   // a config that does not read: no target from it
        }

        if (own is not null && ScriptVersion.ParseEcmaScript(own) is { } level)
        {
            return level;
        }

        string folder = Path.GetDirectoryName(path)!;
        for (int i = extends.Count - 1; i >= 0; i--)
        {
            if (Extended(extends[i], folder, root) is { } parent && Target(parent, root, depth + 1, seen) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    // An extends: a path relative to the config, or a package's config in a node_modules up from it.
    private static string? Extended(string name, string folder, string? root)
    {
        if (name.StartsWith('.') || Path.IsPathRooted(name))
        {
            string path = Path.GetFullPath(Path.Combine(folder, name));
            return File.Exists(path) ? path : File.Exists(path + ".json") ? path + ".json" : null;
        }

        if (FindUp(folder, root, Path.Combine("node_modules", name)) is { } file)
        {
            return file;
        }

        if (FindUp(folder, root, Path.Combine("node_modules", name + ".json")) is { } json)
        {
            return json;
        }

        return FindUp(folder, root, Path.Combine("node_modules", name, "tsconfig.json"));
    }

    // The first existing file at `relative` in `folder` or a folder above it (to the workspace's root, bounded).
    private static string? FindUp(string folder, string? root, string relative)
    {
        string? at = folder;
        for (int level = 0; level < MaxLevels && at is not null; level++)
        {
            string path = Path.Combine(at, relative);
            if (File.Exists(path))
            {
                return path;
            }

            if (root is not null && string.Equals(at.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.Ordinal))
            {
                return null;
            }

            at = Path.GetDirectoryName(at);
        }

        return null;
    }

    private readonly record struct PackageFacts(string? Version, string? TypeScript, bool Jsx);

    // A package.json's version, the typescript it asks for, and whether it depends on react or preact.
    private static PackageFacts Package(string path)
    {
        if (Bytes(path) is not { } bytes)
        {
            return default;
        }

        string? version = null;
        string? typeScript = null;
        bool jsx = false;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                if (reader.ValueTextEquals("version"u8))
                {
                    reader.Read();
                    version = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else if (reader.ValueTextEquals("dependencies"u8) || reader.ValueTextEquals("devDependencies"u8) || reader.ValueTextEquals("peerDependencies"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                        {
                            continue;
                        }

                        if (reader.ValueTextEquals("react"u8) || reader.ValueTextEquals("preact"u8))
                        {
                            jsx = true;
                        }

                        bool isTypeScript = reader.ValueTextEquals("typescript"u8);
                        reader.Read();
                        if (isTypeScript && reader.TokenType == JsonTokenType.String)
                        {
                            typeScript ??= reader.GetString();
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            return new PackageFacts(version, typeScript, jsx);
        }

        return new PackageFacts(version, typeScript, jsx);
    }

    // A file's bytes (a UTF-8 BOM taken off), or null when it is missing, too large or unreadable.
    private static byte[]? Bytes(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            return bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
