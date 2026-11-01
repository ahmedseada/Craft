// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text.Json;
using Craft.Web.Script;
using Craft.Web.Style;

namespace Craft.Web.Markup;

/// <summary>A project of an Angular workspace: its folder and the language of its components' inline styles.</summary>
/// <param name="Name">Its name in angular.json.</param>
/// <param name="Root">Its folder (full path).</param>
/// <param name="InlineStyles">The language of <c>styles:</c> in its components (<c>inlineStyleLanguage</c>, else the component schematic's <c>style</c>, else CSS).</param>
public sealed record AngularProject(string Name, string Root, StyleDialect InlineStyles);

/// <summary>What a folder's Angular files say (W-markup): its angular.json's projects, and the Angular it has (<c>@angular/core</c>).</summary>
/// <param name="Folder">The folder read.</param>
/// <param name="Projects">angular.json's projects (none when there is no angular.json).</param>
/// <param name="Version">Angular's major × 100 + minor (installed, else asked for by package.json), or null.</param>
public sealed record AngularWorkspace(string Folder, IReadOnlyList<AngularProject> Projects, int? Version)
{
    /// <summary>The project <paramref name="path"/> is in (the one with the longest root holding it), or null.</summary>
    public AngularProject? ProjectOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        AngularProject? best = null;
        foreach (var project in Projects)
        {
            string root = project.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool inside = full.StartsWith(root, StringComparison.Ordinal) && (full.Length == root.Length || full[root.Length] is '/' or '\\');
            if (inside && (best is null || root.Length > best.Root.TrimEnd('/', '\\').Length))
            {
                best = project;
            }
        }

        return best;
    }
}

/// <summary>
/// The Angular workspaces read so far, by folder (W-markup): read off the UI thread, once per folder, as UTF-8 bytes with
/// <see cref="Utf8JsonReader"/> (rules 49, 74); <see cref="Find"/> answers from memory only. Bounded (rule 77): past
/// <see cref="Capacity"/> folders the cache starts again. <see cref="Forget(string?)"/> drops a folder (the host's
/// <c>WorkspaceChanged</c>: angular.json may have changed).
/// </summary>
public static class AngularWorkspaces
{
    /// <summary>The folders held at most.</summary>
    public const int Capacity = 1024;

    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxLevels = 32;
    private static readonly ConcurrentDictionary<string, AngularWorkspace> ByFolder = new(StringComparer.Ordinal);
    private static long _reads;

    /// <summary>The folders read from disk since the start (or the last <see cref="Forget(string?)"/> of all).</summary>
    public static long Reads => Interlocked.Read(ref _reads);

    /// <summary>The workspace of <paramref name="folder"/> when it was read, else null. No I/O.</summary>
    public static AngularWorkspace? Find(string? folder) =>
        string.IsNullOrEmpty(folder) ? null : ByFolder.TryGetValue(Path.GetFullPath(folder), out var workspace) ? workspace : null;

    /// <summary>Reads <paramref name="folder"/> off the calling thread (once; again after <see cref="Forget(string?)"/>).</summary>
    public static Task<AngularWorkspace> ReadAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Task.Run(() => Read(folder), cancellationToken);
    }

    /// <summary>Forgets <paramref name="folder"/> (every folder when null).</summary>
    public static void Forget(string? folder = null)
    {
        if (folder is null)
        {
            ByFolder.Clear();
            Interlocked.Exchange(ref _reads, 0);
            return;
        }

        ByFolder.TryRemove(Path.GetFullPath(folder), out _);
    }

    /// <summary>The workspace of <paramref name="folder"/>, reading it when it was not: blocking I/O, never on the UI thread.</summary>
    public static AngularWorkspace Read(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        folder = Path.GetFullPath(folder);
        if (ByFolder.TryGetValue(folder, out var known))
        {
            return known;
        }

        Interlocked.Increment(ref _reads);
        var projects = Projects(folder);
        int? version = null;
        if (FindUp(folder, Path.Combine("node_modules", "@angular", "core", "package.json")) is { } installed && Field(installed, "version"u8) is { } text)
        {
            version = ScriptVersion.ParseTypeScript(text);
        }

        if (version is null && FindUp(folder, "package.json") is { } package && Dependency(package, "@angular/core"u8) is { } wanted)
        {
            version = ScriptVersion.ParseTypeScript(wanted);
        }

        var workspace = new AngularWorkspace(folder, projects, version);
        if (ByFolder.Count >= Capacity)
        {
            ByFolder.Clear();
        }

        ByFolder[folder] = workspace;
        return workspace;
    }

    // angular.json's projects: their root (relative to the folder), inlineStyleLanguage or the component schematic's style.
    private static List<AngularProject> Projects(string folder)
    {
        var projects = new List<AngularProject>();
        if (Bytes(Path.Combine(folder, "angular.json")) is not { } bytes)
        {
            return projects;
        }

        var path = new List<string>();
        string? pending = null;
        string? name = null;
        string root = "";
        string? style = null;
        string? schematic = null;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pending = reader.GetString();
                        break;
                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        path.Add(pending ?? "");
                        pending = null;
                        if (path.Count == 3 && path[1] == "projects")
                        {
                            name = path[2];
                            root = "";
                            style = schematic = null;
                        }

                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        if (path.Count == 3 && path[1] == "projects" && name is not null)
                        {
                            string full = Path.GetFullPath(Path.Combine(folder, root));
                            projects.Add(new AngularProject(name, full, Dialect(style ?? schematic)));
                            name = null;
                        }

                        if (path.Count > 0)
                        {
                            path.RemoveAt(path.Count - 1);
                        }

                        break;
                    case JsonTokenType.String:
                        string? value = reader.GetString();
                        if (path.Count == 3 && path[1] == "projects" && pending == "root")
                        {
                            root = value ?? "";
                        }
                        else if (path.Count == 6 && path[1] == "projects" && path[3] == "architect" && path[4] == "build" && path[5] == "options" && pending == "inlineStyleLanguage")
                        {
                            style = value;
                        }
                        else if (path.Count == 5 && path[1] == "projects" && path[3] == "schematics" && path[4] == "@schematics/angular:component" && pending == "style")
                        {
                            schematic = value;
                        }

                        pending = null;
                        break;
                    default:
                        pending = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return projects;   // an angular.json that does not read: the projects read so far
        }

        return projects;
    }

    private static StyleDialect Dialect(string? style) => style?.ToLowerInvariant() switch
    {
        "scss" => StyleDialect.Scss,
        "sass" => StyleDialect.Sass,
        "less" => StyleDialect.Less,
        _ => StyleDialect.Css,
    };

    // A top-level string field of a JSON file.
    private static string? Field(string path, ReadOnlySpan<byte> name)
    {
        if (Bytes(path) is not { } bytes)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    bool wanted = reader.ValueTextEquals(name);
                    reader.Read();
                    if (wanted && reader.TokenType == JsonTokenType.String)
                    {
                        return reader.GetString();
                    }

                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    // A package.json's dependency (dependencies, devDependencies, peerDependencies) version.
    private static string? Dependency(string path, ReadOnlySpan<byte> name)
    {
        if (Bytes(path) is not { } bytes)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 2)
                {
                    continue;
                }

                bool wanted = reader.ValueTextEquals(name);
                reader.Read();
                if (wanted && reader.TokenType == JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? FindUp(string folder, string relative)
    {
        string? at = folder;
        for (int level = 0; level < MaxLevels && at is not null; level++)
        {
            string path = Path.Combine(at, relative);
            if (File.Exists(path))
            {
                return path;
            }

            at = Path.GetDirectoryName(at);
        }

        return null;
    }

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
