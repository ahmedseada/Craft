// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>
/// Where a tool's configuration and install are, found by file checks from a file's folder up to the workspace's root
/// (never above it), read once per folder and kept until the workspace changes or a configuration file is saved (rule 75).
/// A package.json is read as bytes only for the keys asked ("eslintConfig", "prettier", "version").
/// </summary>
public static class LintProjects
{
    /// <summary>ESLint's flat configuration files, in the order ESLint looks.</summary>
    public static IReadOnlyList<string> EslintFlat { get; } = ["eslint.config.js", "eslint.config.mjs", "eslint.config.cjs", "eslint.config.ts", "eslint.config.mts", "eslint.config.cts"];

    /// <summary>ESLint's legacy configuration files (read only with <c>ESLINT_USE_FLAT_CONFIG=false</c> from ESLint 9; never from 10).</summary>
    public static IReadOnlyList<string> EslintLegacy { get; } = [".eslintrc.js", ".eslintrc.cjs", ".eslintrc.yaml", ".eslintrc.yml", ".eslintrc.json", ".eslintrc"];

    /// <summary>Prettier's configuration files.</summary>
    public static IReadOnlyList<string> PrettierConfigs { get; } =
    [
        ".prettierrc", ".prettierrc.json", ".prettierrc.yaml", ".prettierrc.yml", ".prettierrc.json5", ".prettierrc.js", ".prettierrc.cjs", ".prettierrc.mjs",
        ".prettierrc.ts", ".prettierrc.cts", ".prettierrc.mts", ".prettierrc.toml", "prettier.config.js", "prettier.config.cjs", "prettier.config.mjs",
        "prettier.config.ts", "prettier.config.cts", "prettier.config.mts",
    ];

    private static readonly ConcurrentDictionary<string, string?> Versions = new(StringComparer.Ordinal);

    /// <summary>Forgets every version read (a workspace changed, a package installed).</summary>
    public static void Forget() => Versions.Clear();

    /// <summary>The folders from <paramref name="path"/>'s folder (or itself, a folder) up to <paramref name="root"/>, nearest first (only the first when it is outside the root).</summary>
    public static IEnumerable<string> Upward(string path, string root)
    {
        string folder = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool inside = folder.StartsWith(top, comparison) && (folder.Length == top.Length || folder[top.Length] is '/' or '\\');
        for (string? at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            yield return at;
            if (!inside || string.Equals(Path.TrimEndingDirectorySeparator(at), top, comparison))
            {
                yield break;
            }
        }
    }

    /// <summary>The first of <paramref name="names"/> in the nearest folder that holds one (from <paramref name="path"/> up to <paramref name="root"/>), with its folder; or null.</summary>
    public static string? Nearest(string path, string root, IReadOnlyList<string> names, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        foreach (var folder in Upward(path, root))
        {
            foreach (var name in names)
            {
                string candidate = Path.Combine(folder, name);
                if (exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>The nearest package.json (up to the root) holding the top-level key <paramref name="key"/> ("eslintConfig", "prettier"), or null.</summary>
    public static string? NearestPackageWith(string path, string root, ReadOnlySpan<byte> key)
    {
        foreach (var folder in Upward(path, root))
        {
            string package = Path.Combine(folder, "package.json");
            if (File.Exists(package) && LintJson.HasTopLevel(ReadBytes(package), key))
            {
                return package;
            }
        }

        return null;
    }

    /// <summary>
    /// The tool's script in the nearest <c>node_modules/&lt;package&gt;</c> from <paramref name="folder"/> up to the root
    /// (a monorepo hoists it), e.g. <c>node_modules/eslint/bin/eslint.js</c>; or null.
    /// </summary>
    public static string? LocalInstall(string folder, string root, string package, string script)
    {
        foreach (var at in Upward(folder, root))
        {
            string candidate = Path.Combine(at, "node_modules", package, script);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The tool's script installed globally beside <paramref name="node"/> (Windows: node_modules beside node.exe; Unix: ../lib/node_modules), or null.</summary>
    public static string? GlobalInstall(string? node, string package, string script)
    {
        if (node is null || Path.GetDirectoryName(node) is not { } folder)
        {
            return null;
        }

        foreach (var candidate in (string[])[Path.Combine(folder, "node_modules", package, script), Path.Combine(folder, "..", "lib", "node_modules", package, script)])
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>The version in the package.json of the package whose script <paramref name="script"/> is (<c>…/eslint/bin/eslint.js</c> → <c>…/eslint/package.json</c>), read once.</summary>
    public static string? VersionOf(string script, int levels = 2)
    {
        string? folder = Path.GetDirectoryName(script);
        for (int i = 1; i < levels && folder is not null; i++)
        {
            folder = Path.GetDirectoryName(folder);
        }

        if (folder is null)
        {
            return null;
        }

        string package = Path.Combine(folder, "package.json");
        return Versions.GetOrAdd(package, static p => File.Exists(p) ? LintJson.TopLevelString(ReadBytes(p), "version"u8) : null);
    }

    /// <summary>The major number of <paramref name="version"/> ("9.39.1" → 9), or 0.</summary>
    public static int Major(string? version)
    {
        if (version is null)
        {
            return 0;
        }

        var span = version.AsSpan();
        int dot = span.IndexOf('.');
        return int.TryParse(dot < 0 ? span : span[..dot], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major) ? major : 0;
    }

    /// <summary>
    /// The folders under <paramref name="root"/> (itself first) holding one of <paramref name="names"/>, in path order, the
    /// ignored folders (and every folder named in a .gitignore line ending in '/') never entered; at most 2,000 folders.
    /// </summary>
    public static IReadOnlyList<string> FoldersWith(string root, IReadOnlyList<string> names, IReadOnlyList<string> ignore)
    {
        var found = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));
        int visited = 0;
        var skip = new HashSet<string>(ignore, StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && visited++ < 2_000)
        {
            string folder = queue.Dequeue();
            if (names.Any(n => File.Exists(Path.Combine(folder, n))))
            {
                found.Add(folder);
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                string name = Path.GetFileName(child);
                if (!skip.Contains(name) && !name.StartsWith('.'))
                {
                    queue.Enqueue(child);
                }
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>A file's bytes (empty when it cannot be read).</summary>
    public static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>The Node the linters and Prettier run with (found as the npm plug-in finds it, its CRAFT_PLUGIN_NPM_NODE honoured).</summary>
public static class LintNode
{
    private static volatile Craft.Plugins.Npm.NodeInstall? _install;

    /// <summary>The Node found (with npm's npm-cli.js, for "Set up ESLint"), or null before it was looked for.</summary>
    public static Craft.Plugins.Npm.NodeInstall? Install => _install;

    /// <summary>
    /// Looks for Node (the npm plug-in's settings when it is registered, else Node on PATH), off the UI thread; the
    /// settings are read on the calling (UI) thread. Completes with what was found.
    /// </summary>
    public static async Task<Craft.Plugins.Npm.NodeInstall> FindAsync(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = EditorPlugins.Find(Craft.Plugins.Npm.NpmSettings.PluginName) is not null
            ? Craft.Plugins.Npm.NpmSettings.Read(host.Settings)
            : new Craft.Plugins.Npm.NpmSettings();
        var install = await Task.Run(() => Craft.Plugins.Npm.NodeInstall.Find(settings, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows())).ConfigureAwait(false);
        _install = install;
        Set(install.Node, install.Node is null ? install.Problem : null);
        return install;
    }

    private static volatile string? _program;
    private static volatile string? _problem = "Node.js has not been looked for yet.";

    /// <summary>Node's program (a full path), or null.</summary>
    public static string? Program => _program;

    /// <summary>Why there is no Node, or null.</summary>
    public static string? Problem => _program is null ? _problem : null;

    /// <summary>Records what was found.</summary>
    public static void Set(string? program, string? problem)
    {
        _problem = problem ?? (program is null ? "Node.js was not found." : null);
        _program = program;
    }

    /// <summary>Why nothing that needs Node runs on this device (Android), or null.</summary>
    public static string? DeviceProblem => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? "Node.js does not run on this device, so the lint plug-in lints nothing here." : null;
}
