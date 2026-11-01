// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;

namespace Craft.Plugins.ClaudeCode;

/// <summary>What the installed Claude Code CLI says it takes (its <c>--help</c>, read once at run time).</summary>
/// <param name="Version">Its version ("2.0.31"), or "".</param>
/// <param name="Help">Its help text.</param>
public sealed record ClaudeFlags(string Version, string Help)
{
    /// <summary>Whether its help names <paramref name="flag"/> ("--output-format", "-p").</summary>
    public bool Has(string flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        int at = 0;
        while ((at = Help.IndexOf(flag, at, StringComparison.Ordinal)) >= 0)
        {
            int end = at + flag.Length;
            bool startsWord = at == 0 || Help[at - 1] is ' ' or ',' or '\t' or '\n' or '(' or '[';
            bool endsWord = end >= Help.Length || Help[end] is ' ' or ',' or '\t' or '\r' or '\n' or '=' or '<' or '[' or ')';
            if (startsWord && endsWord)
            {
                return true;
            }

            at = end;
        }

        return false;
    }

    /// <summary>Whether its help's "Commands:" list has <paramref name="name"/> ("login"): a line whose first word it is.</summary>
    public bool HasCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        bool inCommands = false;
        foreach (var raw in Help.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("Commands:", StringComparison.OrdinalIgnoreCase))
            {
                inCommands = true;
                continue;
            }

            if (!inCommands || line.Length == 0)
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                inCommands = false;                               // the next section
                continue;
            }

            var first = line.TrimStart().Split([' ', '|', '\t'], 2)[0];
            if (first.Equals(name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The headless flags a run needs and this version lacks, each said (empty: it can run headless).</summary>
    public IReadOnlyList<string> MissingForHeadless()
    {
        var missing = new List<string>(2);
        if (!Has("--print") && !Has("-p"))
        {
            missing.Add("--print (-p)");
        }

        if (!Has("--output-format"))
        {
            missing.Add("--output-format");
        }

        return missing;
    }
}

/// <summary>
/// The user's own Claude Code CLI: found on PATH (with PATHEXT on Windows: <c>claude.exe</c>, or the <c>claude.cmd</c> npm
/// installs) or at the machine's <c>path</c> setting; run with its arguments as a list (a <c>.cmd</c> through
/// <c>%ComSpec%</c>, its arguments quoted by the Windows rules). Every run is off the calling thread; nothing is installed.
/// </summary>
public sealed class ClaudeProgram
{
    private ClaudeProgram(string path) => Path = path;

    /// <summary>The program's full path.</summary>
    public string Path { get; }

    /// <summary>Why Claude Code cannot run on this platform (a phone, a browser), or null on a desktop.</summary>
    public static string? Unavailable =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsBrowser() || OperatingSystem.IsWasi()
            ? "Claude Code runs only on a desktop (Windows, macOS or Linux): it is not available on this device."
            : null;

    /// <summary>What to do when it is not found.</summary>
    public const string InstallHint =
        "Install Claude Code (claude.ai/code: its installer, or npm install -g @anthropic-ai/claude-code), sign in by running claude once in a terminal, "
        + "or set its program in Settings › Agent (CRAFT_PLUGIN_CLAUDECODE_PATH). Craft installs nothing itself.";

    /// <summary>
    /// The program: <paramref name="configured"/> when set (it must exist), else the first <c>claude</c> on
    /// <paramref name="searchPath"/> (the process's PATH when null), trying <paramref name="extensions"/> (PATHEXT on
    /// Windows); null with <paramref name="why"/> when none.
    /// </summary>
    public static ClaudeProgram? Find(string configured, out string? why, string? searchPath = null, string? extensions = null, Func<string, bool>? exists = null)
    {
        why = Unavailable;
        if (why is not null)
        {
            return null;
        }

        exists ??= File.Exists;
        if (configured.Length > 0)
        {
            if (exists(configured))
            {
                return new ClaudeProgram(System.IO.Path.GetFullPath(configured));
            }

            why = $"The Claude Code program set in CRAFT_PLUGIN_CLAUDECODE_PATH ({configured}) was not found: correct the setting, or clear it to use claude on PATH.";
            return null;
        }

        var path = searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        var suffixes = (extensions ?? (OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD" : ""))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        char separator = searchPath is not null && searchPath.Contains(';', StringComparison.Ordinal) ? ';' : System.IO.Path.PathSeparator;
        foreach (var folder in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in (ReadOnlySpan<string>)[.. suffixes, ""])
            {
                string candidate;
                try
                {
                    candidate = System.IO.Path.Combine(folder.Trim('"'), "claude" + suffix.ToLowerInvariant());
                }
                catch (ArgumentException)
                {
                    continue;                                       // a PATH entry with characters a path cannot hold
                }

                if (exists(candidate))
                {
                    return new ClaudeProgram(candidate);
                }
            }
        }

        why = "Claude Code was not found on PATH. " + InstallHint;
        return null;
    }

    /// <summary>A program at <paramref name="path"/> (the tests' fake).</summary>
    internal static ClaudeProgram At(string path) => new(path);

    /// <summary>Whether it is a batch script (npm's <c>claude.cmd</c>), started through <c>%ComSpec%</c>.</summary>
    public bool IsScript => Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || Path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>How to start it with <paramref name="arguments"/> in <paramref name="folder"/>: its output and errors read, its input written.</summary>
    public ProcessStartInfo StartInfo(IReadOnlyList<string> arguments, string folder)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo info;
        if (IsScript)
        {
            // cmd /d /s /c ""C:\…\claude.cmd" arg …": /s takes the outer quotes off, each argument quoted by the Windows rules.
            var line = new StringBuilder("/d /s /c \"").Append(Quote(Path));
            foreach (var argument in arguments)
            {
                line.Append(' ').Append(Quote(argument));
            }

            info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comspec ? comspec : "cmd.exe", line.Append('"').ToString());
        }
        else
        {
            info = new ProcessStartInfo(Path);
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        info.WorkingDirectory = folder;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        return info;
    }

    /// <summary>The shell a terminal tab starts to run it with <paramref name="arguments"/> (a <c>.cmd</c> through <c>%ComSpec%</c>).</summary>
    public Craft.Abstractions.TerminalShell TerminalShellFor(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string line = string.Join(' ', arguments.Select(Quote));
        if (IsScript)
        {
            string comspec = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } found ? found : "cmd.exe";
            return new Craft.Abstractions.TerminalShell(comspec, $"/d /s /c \"{Quote(Path)}{(line.Length > 0 ? " " + line : "")}\"");
        }

        return new Craft.Abstractions.TerminalShell(Path, line);
    }

    /// <summary>An argument as the Windows command line writes it: quoted when it holds a space or a quote, its quotes and the backslashes before them escaped.</summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\"") < 0)
        {
            return argument;
        }

        var text = new StringBuilder(argument.Length + 2).Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            text.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return text.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>
    /// Runs it with <paramref name="arguments"/> to its end (at most <paramref name="timeout"/>, then its process tree is
    /// ended), off the calling thread: its exit code and its output (standard output then error).
    /// </summary>
    public async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var info = StartInfo(arguments, Environment.CurrentDirectory);
        info.RedirectStandardInput = false;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{Path} did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return (process.ExitCode, (await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false)));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    /// <summary>Its version and its help, read once (<c>--version</c>, <c>--help</c>).</summary>
    public async Task<ClaudeFlags> FlagsAsync(CancellationToken cancellationToken = default)
    {
        var (_, version) = await RunAsync(["--version"], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        var (_, help) = await RunAsync(["--help"], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        return new ClaudeFlags(VersionOf(version), help);
    }

    /// <summary>The version in <c>--version</c>'s output ("2.0.31 (Claude Code)" gives "2.0.31"), or "".</summary>
    public static string VersionOf(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var word in output.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = word.TrimStart('v');
            if (trimmed.Length > 0 && char.IsAsciiDigit(trimmed[0]) && trimmed.Contains('.', StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return "";
    }

    /// <summary>Ends <paramref name="process"/> with its process tree; nothing when it already ended.</summary>
    public static void Kill(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It ended meanwhile, or a child outlived it: nothing more to end from here.
        }
    }
}
