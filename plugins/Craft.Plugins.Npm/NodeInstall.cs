// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>
/// The Node this machine runs npm with: Node's program, npm's own <c>npm-cli.js</c> (npm runs as <c>node npm-cli.js</c>,
/// never through npm.cmd or a shell), and Node's version once read; or why there is none.
/// </summary>
/// <param name="Node">Node's program (a full path), or null.</param>
/// <param name="NpmCli">npm's npm-cli.js (a full path), or null.</param>
/// <param name="Problem">Why npm cannot run here (no Node, no npm beside it, Android), or null.</param>
public sealed record NodeInstall(string? Node, string? NpmCli, string? Problem)
{
    /// <summary>Node's version ("22.23.4", without its "v"), or null until read (or when it could not be).</summary>
    public string? Version { get; init; }

    /// <summary>Whether npm can run.</summary>
    public bool Ready => Problem is null && Node is not null && NpmCli is not null;

    /// <summary>The status bar's text: "Node 22.23.4", "Node" before the version is in.</summary>
    public string StatusText => Version is { } version ? "Node " + version : "Node";

    /// <summary>
    /// Finds Node and npm with <paramref name="settings"/>' paths (else PATH, from <paramref name="environment"/>), checking
    /// files with <paramref name="exists"/> (the tests' fake machine) and following links with <paramref name="link"/>.
    /// Reads no file's text and starts no process.
    /// </summary>
    public static NodeInstall Find(NpmSettings settings, Func<string, string?> environment, bool windows, Func<string, bool>? exists = null, Func<string, string?>? link = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        exists ??= File.Exists;
        link ??= FinalTarget;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            return new NodeInstall(null, null, "Node.js does not run on this device, so the npm plug-in adds nothing here.");
        }

        string nodeVariable = PluginSetting.VariableName(NpmSettings.PluginName, "node");
        string? path = environment("PATH");
        string? extensions = windows ? environment("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD" : null;
        char separator = windows ? ';' : ':';
        string? node;
        if (settings.Node.Length > 0)
        {
            node = TerminalProgram.FindOnPath(settings.Node, Path.IsPathRooted(settings.Node) ? null : path, separator, extensions, exists);
            if (node is null)
            {
                return new NodeInstall(null, null, $"Node was not found at '{settings.Node}' ({nodeVariable}).");
            }
        }
        else
        {
            node = TerminalProgram.FindOnPath("node", path, separator, windows ? ".EXE" : null, exists);
            if (node is null)
            {
                return new NodeInstall(null, null, $"Node.js was not found on PATH: install it (nodejs.org), or set {nodeVariable} to its program; the npm plug-in adds nothing until then.");
            }
        }

        string? npm = FindNpm(settings, node, path, separator, windows, exists, link);
        return npm is null
            ? new NodeInstall(node, null, $"npm's npm-cli.js was not found beside Node ({node}): set {PluginSetting.VariableName(NpmSettings.PluginName, "npm")} to it.")
            : new NodeInstall(node, npm, null);
    }

    /// <summary>The version in <c>node --version</c>'s output ("v22.23.4\n" → "22.23.4"), or null when it holds none.</summary>
    public static string? ParseVersion(ReadOnlySpan<char> output)
    {
        var line = output.Trim();
        int end = line.IndexOfAny('\r', '\n');
        if (end >= 0)
        {
            line = line[..end].TrimEnd();
        }

        if (line.StartsWith('v') || line.StartsWith('V'))
        {
            line = line[1..];
        }

        return line.Length > 0 && char.IsAsciiDigit(line[0]) && line.IndexOfAnyExcept(VersionCharacters) < 0 ? line.ToString() : null;
    }

    private static readonly System.Buffers.SearchValues<char> VersionCharacters = System.Buffers.SearchValues.Create("0123456789.-+abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    // npm's npm-cli.js: the setting (the file, or npm's package folder), else beside Node in each install's layout (Windows'
    // installer: node_modules\npm beside node.exe; Unix: ../lib/node_modules/npm; Debian's nodejs: ../share/nodejs/npm),
    // through Node's link when it is one (Homebrew, nvm), else npm on PATH followed to its npm-cli.js.
    private static string? FindNpm(NpmSettings settings, string node, string? path, char separator, bool windows, Func<string, bool> exists, Func<string, string?> link)
    {
        if (settings.Npm.Length > 0)
        {
            string given = settings.Npm;
            foreach (var candidate in (string[])[given, Path.Combine(given, "bin", "npm-cli.js"), Path.Combine(given, "node_modules", "npm", "bin", "npm-cli.js")])
            {
                if (candidate.EndsWith("npm-cli.js", StringComparison.OrdinalIgnoreCase) && exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        foreach (var program in link(node) is { } target && target != node ? (string[])[node, target] : [node])
        {
            if (Beside(program, exists) is { } found)
            {
                return found;
            }
        }

        if (TerminalProgram.FindOnPath("npm", path, separator, windows ? ".CMD;.EXE" : null, exists) is { } npm)
        {
            if (link(npm) is { } script && script.EndsWith("npm-cli.js", StringComparison.OrdinalIgnoreCase) && exists(script))
            {
                return script;
            }

            if (Beside(npm, exists) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? Beside(string program, Func<string, bool> exists)
    {
        if (Path.GetDirectoryName(program) is not { } folder)
        {
            return null;
        }

        foreach (var relative in (string[])[Path.Combine("node_modules", "npm", "bin", "npm-cli.js"), Path.Combine("..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
                     Path.Combine("..", "share", "nodejs", "npm", "bin", "npm-cli.js")])
        {
            string candidate = Path.GetFullPath(Path.Combine(folder, relative));
            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // A link's final target as a full path (Homebrew's /opt/homebrew/bin/node, npm's bin/npm), or null when it is none.
    private static string? FinalTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true) is { } target ? Path.GetFullPath(target.FullName) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
