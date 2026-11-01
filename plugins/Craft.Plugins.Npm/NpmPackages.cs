// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>One script of a package.json: its name ("build", "dev:server", any name npm takes) and its text ("ng build").</summary>
/// <param name="Name">The script's name, as written (escapes read).</param>
/// <param name="Text">What it runs.</param>
public sealed record NpmScript(string Name, string Text);

/// <summary>
/// One package.json of the workspace as read: where it is, its name, its scripts in their order, the package manager it
/// names or its lock files show, and what was wrong with it.
/// </summary>
/// <param name="File">The file, relative to the workspace's root ('/' between folders: "src/app/package.json").</param>
/// <param name="Scripts">Its scripts, in the file's order (a name written twice keeps its last text in its first place, as JSON.parse does).</param>
public sealed record NpmPackage(string File, IReadOnlyList<NpmScript> Scripts)
{
    /// <summary>Its "name", or null.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The package manager it is meant for, when it is not npm: "yarn", "pnpm" or "bun" (its "packageManager" field, else a
    /// lock file in its folder or one above it within the workspace); null for npm.
    /// </summary>
    public string? Manager { get; init; }

    /// <summary>What told <see cref="Manager"/> ("pnpm-lock.yaml", "packageManager: pnpm@9.1.0"), or null.</summary>
    public string? ManagerEvidence { get; init; }

    /// <summary>Whether its folder has npm's lock file (package-lock.json or npm-shrinkwrap.json): what "npm ci" needs.</summary>
    public bool HasNpmLock { get; init; }

    /// <summary>Why it could not be read (not JSON, not an object), or null.</summary>
    public string? Problem { get; init; }

    /// <summary>Its folder relative to the root ("" for the root's own).</summary>
    public string Folder => File.Contains('/', StringComparison.Ordinal) ? File[..File.LastIndexOf('/')] : "";

    /// <summary>The script <paramref name="name"/>, or null.</summary>
    public NpmScript? Script(string name)
    {
        foreach (var script in Scripts)
        {
            if (script.Name == name)
            {
                return script;
            }
        }

        return null;
    }
}

/// <summary>
/// Reads package.json files as UTF-8 bytes with <see cref="Utf8JsonReader"/> (rule 74: no string of the file, the names
/// compared as bytes): a byte order mark is passed over, comments and trailing commas are taken (as editors write them),
/// every other value is skipped unread. And finds the workspace's package.json files without walking node_modules.
/// </summary>
public static class NpmPackages
{
    /// <summary>The file every npm package has.</summary>
    public const string FileName = "package.json";

    /// <summary>The lock files of the other package managers, in the order they are looked for, with the manager each names.</summary>
    public static IReadOnlyList<(string File, string Manager)> OtherLocks { get; } =
        [("pnpm-lock.yaml", "pnpm"), ("yarn.lock", "yarn"), ("bun.lock", "bun"), ("bun.lockb", "bun")];

    /// <summary>npm's own lock files.</summary>
    public static IReadOnlyList<string> NpmLocks { get; } = ["package-lock.json", "npm-shrinkwrap.json"];

    private static readonly JsonReaderOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 256 };

    /// <summary>Whether a file of this name is a package.json.</summary>
    public static bool IsPackageFile(ReadOnlySpan<char> fileName) => fileName.Equals(FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a file of this name changes what the plug-in found (a package.json or a lock file).</summary>
    public static bool Matters(ReadOnlySpan<char> fileName)
    {
        if (IsPackageFile(fileName))
        {
            return true;
        }

        foreach (var (file, _) in OtherLocks)
        {
            if (fileName.Equals(file, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var file in NpmLocks)
        {
            if (fileName.Equals(file, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The package.json in <paramref name="json"/> (its bytes) at <paramref name="file"/> (relative to the root): its name,
    /// its "packageManager" (yarn, pnpm or bun when it names one) and its scripts. A file that is not a JSON object gives no
    /// scripts and a <see cref="NpmPackage.Problem"/>.
    /// </summary>
    public static NpmPackage Parse(ReadOnlySpan<byte> json, string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (json is [0xEF, 0xBB, 0xBF, ..])
        {
            json = json[3..];
        }

        var scripts = new List<NpmScript>();
        string? name = null;
        string? manager = null;
        var reader = new Utf8JsonReader(json, Options);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return new NpmPackage(file, []) { Problem = "it is not a JSON object" };
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("scripts"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadScripts(ref reader, scripts);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else if (reader.ValueTextEquals("name"u8))
                {
                    reader.Read();
                    name = reader.TokenType == JsonTokenType.String ? reader.GetString() : Skipped(ref reader);
                }
                else if (reader.ValueTextEquals("packageManager"u8))
                {
                    reader.Read();
                    manager = reader.TokenType == JsonTokenType.String ? reader.GetString() : Skipped(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException e)
        {
            return new NpmPackage(file, scripts) { Name = name, Problem = $"it is not valid JSON (line {e.LineNumber + 1}: {e.Message})" };
        }

        string? named = manager is null ? null : ManagerOf(manager);
        return new NpmPackage(file, scripts)
        {
            Name = name,
            Manager = named,
            ManagerEvidence = named is null ? null : "packageManager: " + manager,
        };
    }

    /// <summary>
    /// <paramref name="package"/> with its lock files looked at: npm's in its folder, another manager's in its folder or a
    /// folder above it up to <paramref name="root"/> (a workspace of packages keeps one lock at its top). A manager the
    /// package names itself wins. Reads names only (no file's text).
    /// </summary>
    public static NpmPackage WithLocks(NpmPackage package, string root)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(root);
        string folder = Path.GetFullPath(Path.Combine(root, package.Folder));
        bool npmLock = NpmLocks.Any(f => File.Exists(Path.Combine(folder, f)));
        if (package.Manager is not null)
        {
            return package with { HasNpmLock = npmLock };
        }

        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (string? at = folder; at is not null && at.StartsWith(top, StringComparison.Ordinal); at = Path.GetDirectoryName(at))
        {
            foreach (var (file, manager) in OtherLocks)
            {
                if (File.Exists(Path.Combine(at, file)))
                {
                    return package with { HasNpmLock = npmLock, Manager = manager, ManagerEvidence = WorkspaceFiles.Relative(top, Path.Combine(at, file)) };
                }
            }

            if (NpmLocks.Any(f => File.Exists(Path.Combine(at, f))) || string.Equals(Path.TrimEndingDirectorySeparator(at), top, StringComparison.Ordinal))
            {
                break;                                     // npm's lock here (this package or an npm workspace above), or the root
            }
        }

        return package with { HasNpmLock = npmLock };
    }

    /// <summary>
    /// Every package.json under <paramref name="root"/>, relative to it, in path order: the folders of
    /// <paramref name="ignore"/> (node_modules first of all) are never entered and each folder's .gitignore applies.
    /// </summary>
    public static IReadOnlyList<string> Find(string root, IReadOnlyList<string> ignore, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(ignore);
        IReadOnlyList<string> folders = ignore.Contains("node_modules", StringComparer.OrdinalIgnoreCase) ? ignore : [.. ignore, "node_modules"];
        var found = new List<string>();
        foreach (var path in WorkspaceFiles.Enumerate(root, folders, useIgnoreFiles: true, WorkspaceGlob.Parse(FileName), null, cancellationToken))
        {
            if (IsPackageFile(Path.GetFileName(path.AsSpan())))
            {
                found.Add(WorkspaceFiles.Relative(root, path));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Reads the package.json <paramref name="file"/> (relative to <paramref name="root"/>) as bytes, with its lock files; a file that cannot be read gives its problem.</summary>
    public static NpmPackage Read(string root, string file)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(file);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(root, file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new NpmPackage(file, []) { Problem = $"it cannot be read ({e.Message})" };
        }

        return WithLocks(Parse(bytes, file), root);
    }

    // "pnpm@9.1.0+sha…" → "pnpm"; npm's own, or a name not known, is npm's.
    private static string? ManagerOf(string field)
    {
        int at = field.IndexOf('@', StringComparison.Ordinal);
        var name = (at > 0 ? field.AsSpan(0, at) : field.AsSpan()).Trim();
        foreach (var known in (string[])["yarn", "pnpm", "bun"])
        {
            if (name.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    // The scripts object: each string value a script, any other value passed over unread.
    private static void ReadScripts(ref Utf8JsonReader reader, List<NpmScript> scripts)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                continue;
            }

            string text = reader.GetString()!;
            if (name.Length == 0)
            {
                continue;                                  // npm cannot run a script without a name
            }

            int existing = scripts.FindIndex(s => s.Name == name);
            if (existing >= 0)
            {
                scripts[existing] = new NpmScript(name, text);   // JSON.parse keeps the last value, in the first place
            }
            else
            {
                scripts.Add(new NpmScript(name, text));
            }
        }
    }

    private static string? Skipped(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}
