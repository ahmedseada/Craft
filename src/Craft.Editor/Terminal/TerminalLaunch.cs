// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Terminal;

/// <summary>Where a terminal starts and why (the rule that chose it), and what was passed over.</summary>
/// <param name="Folder">The folder (a full path that exists).</param>
/// <param name="Reason">"the folder chosen", "CRAFT_TERMINAL_CWD", "the open folder" or "the home folder".</param>
/// <param name="Note">A folder that was asked for but does not exist (the next rule was taken), or null.</param>
public sealed record TerminalStartFolder(string Folder, string Reason, string? Note);

/// <summary>
/// What a terminal starts with, from the settings: the folder (the owner's rule), the shell and its arguments, the
/// environment. Pure functions over their inputs, so the rule is tested headless.
/// </summary>
public static class TerminalLaunch
{
    /// <summary>
    /// The folder a terminal starts in: <paramref name="chosen"/> (the explorer's "Open in terminal", or a plug-in's
    /// request) first; then <paramref name="overrideFolder"/> (<c>CRAFT_TERMINAL_CWD</c>); then the open folder's root
    /// (<paramref name="workspaceFolder"/>); else, with only files open or nothing, <paramref name="home"/> (the shell's own
    /// default start folder, the user's home). A folder that does not exist is passed over, said so in the note.
    /// </summary>
    public static TerminalStartFolder StartFolder(string? chosen, string? overrideFolder, string? workspaceFolder, string home, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        exists ??= Directory.Exists;
        string? note = null;
        foreach (var (folder, reason) in (ReadOnlySpan<(string?, string)>)[(chosen, "the folder chosen"), (overrideFolder, "CRAFT_TERMINAL_CWD"), (workspaceFolder, "the open folder")])
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var full = Path.GetFullPath(folder.Trim());
            if (exists(full))
            {
                return new TerminalStartFolder(full, reason, note);
            }

            note ??= $"{reason} '{full}' does not exist";
        }

        return new TerminalStartFolder(Path.GetFullPath(home), "the home folder", note);
    }

    /// <summary>The user's home folder (the shell's own start folder when nothing else is chosen).</summary>
    public static string HomeFolder()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? System.Environment.GetEnvironmentVariable("HOME") ?? Path.GetPathRoot(AppContext.BaseDirectory) ?? "/" : home;
    }

    /// <summary>The shell: <c>CRAFT_TERMINAL_SHELL</c> with <c>CRAFT_TERMINAL_ARGS</c>, else <paramref name="host"/>'s default (its arguments replaced when <c>CRAFT_TERMINAL_ARGS</c> is set).</summary>
    public static TerminalShell Shell(CraftOptions options, ITerminalHost host)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        if (options.TerminalShell is { Length: > 0 } program)
        {
            return new TerminalShell(program, options.TerminalArguments ?? "");
        }

        var shell = host.DefaultShell();
        return options.TerminalArguments is { } arguments ? shell with { Arguments = arguments } : shell;
    }

    /// <summary>
    /// The terminal's environment: <paramref name="parent"/> (the app's), then <paramref name="extra"/>
    /// (<c>CRAFT_TERMINAL_ENV</c>: <c>${NAME}</c> in a value is the parent's NAME, an empty value removes the variable), then
    /// <paramref name="request"/> (a plug-in's; null removes), with <c>TERM_PROGRAM=Craft</c> and <c>COLORTERM=truecolor</c>.
    /// Names compare as the platform's do (ignoring case on Windows).
    /// </summary>
    public static Dictionary<string, string> Environment(IDictionary parent, IReadOnlyList<KeyValuePair<string, string>> extra,
        IReadOnlyDictionary<string, string?>? request = null, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(extra);
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in parent)
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                result[name] = value;
            }
        }

        result["TERM_PROGRAM"] = "Craft";
        result["COLORTERM"] = "truecolor";
        var original = new Dictionary<string, string>(result, comparer);
        foreach (var (name, value) in extra)
        {
            if (value.Length == 0)
            {
                result.Remove(name);
            }
            else
            {
                result[name] = Expand(value, original);
            }
        }

        if (request is not null)
        {
            foreach (var (name, value) in request)
            {
                if (value is null)
                {
                    result.Remove(name);
                }
                else
                {
                    result[name] = value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="program"/>'s full path on <paramref name="path"/> (folders separated by <paramref name="separator"/>),
    /// trying each of <paramref name="extensions"/> (";"-separated, Windows' PATHEXT) when it has none; null when it is not
    /// there. A rooted program is returned when it exists.
    /// </summary>
    public static string? FindOnPath(string program, string? path, char separator, string? extensions = null, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        exists ??= File.Exists;
        var suffixes = Path.HasExtension(program) || string.IsNullOrEmpty(extensions)
            ? [""]
            : extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Path.IsPathRooted(program))
        {
            foreach (var suffix in suffixes)
            {
                if (exists(program + suffix))
                {
                    return program + suffix;
                }
            }

            return null;
        }

        foreach (var folder in (path ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in suffixes)
            {
                var candidate = Path.Combine(folder.Trim('"'), program + suffix);
                if (exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>A tab's title: the shell's file name without its extension, and the folder's name ("pwsh — Craft").</summary>
    public static string TitleOf(TerminalShell shell, string folder)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var name = Path.GetFileNameWithoutExtension(shell.Program.Replace('\\', '/').Split('/')[^1]);
        var place = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrEmpty(place) ? name : $"{name} — {place}";
    }

    private static string Expand(string value, Dictionary<string, string> variables)
    {
        int start = value.IndexOf("${", StringComparison.Ordinal);
        if (start < 0)
        {
            return value;
        }

        var text = new System.Text.StringBuilder(value.Length + 64);
        int at = 0;
        while (start >= 0)
        {
            int end = value.IndexOf('}', start + 2);
            if (end < 0)
            {
                break;
            }

            text.Append(value, at, start - at);
            var name = value[(start + 2)..end];
            text.Append(variables.GetValueOrDefault(name) ?? "");
            at = end + 1;
            start = value.IndexOf("${", at, StringComparison.Ordinal);
        }

        text.Append(value, at, value.Length - at);
        return text.ToString();
    }
}
