// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;

namespace Craft.Plugins.Git;

/// <summary>What a git command gave: its exit code, its output as bytes (parsed as bytes, rule 74), and its errors as text.</summary>
/// <param name="ExitCode">The exit code (0: it worked).</param>
/// <param name="Output">What it wrote to its output.</param>
/// <param name="Error">What it wrote to its error output, trimmed.</param>
#pragma warning disable CA1819 // The output is handed over whole to its one reader (a parser of bytes), never shared or kept.
public sealed record GitResult(int ExitCode, byte[] Output, string Error)
#pragma warning restore CA1819
{
    /// <summary>Whether it worked.</summary>
    public bool Ok => ExitCode == 0;

    /// <summary>The output as text (at the edge: a message, a subject list).</summary>
    public string Text => Encoding.UTF8.GetString(Output);

    /// <summary>What went wrong in one line (its error output's first line, else its exit code).</summary>
    public string Problem => Error.Length > 0 ? Error.Split('\n', 2)[0].Trim() : $"git exited with {ExitCode}";
}

/// <summary>
/// The user's own git program (X part 8): found on PATH or at the machine's <c>path</c> setting, run with its arguments
/// as a list (never a shell), never prompting in a terminal (<c>GIT_TERMINAL_PROMPT=0</c>: a credential helper still
/// asks in its own window), its output read as bytes. Every run is off the calling thread; a cancelled run is ended with
/// its process tree.
/// </summary>
public sealed class GitProgram
{
    private GitProgram(string path) => Path = path;

    /// <summary>The program's full path.</summary>
    public string Path { get; }

    /// <summary>
    /// The git program: <paramref name="configured"/> when set (it must exist), else the first <c>git</c> on
    /// <paramref name="searchPath"/> (the process's PATH when null); null with <paramref name="why"/> (and what to do) when none.
    /// </summary>
    public static GitProgram? Find(string configured, out string? why, string? searchPath = null)
    {
        why = null;
        if (configured.Length > 0)
        {
            if (File.Exists(configured))
            {
                return new GitProgram(System.IO.Path.GetFullPath(configured));
            }

            why = $"The git program set in CRAFT_PLUGIN_GIT_PATH ({configured}) was not found: correct the setting, or clear it to use the git on PATH.";
            return null;
        }

        var path = searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in (ReadOnlySpan<string>)["git.exe", "git"])
            {
                string candidate;
                try
                {
                    candidate = System.IO.Path.Combine(folder.Trim('"'), name);
                }
                catch (ArgumentException)
                {
                    continue;                                       // a PATH entry with characters a path cannot hold
                }

                if (File.Exists(candidate))
                {
                    return new GitProgram(candidate);
                }
            }
        }

        why = "Git was not found on PATH. Install git (git-scm.com), or set its program in Settings › Source Control (CRAFT_PLUGIN_GIT_PATH), then press Try again.";
        return null;
    }

    /// <summary>
    /// Runs git with <paramref name="arguments"/> in <paramref name="folder"/>, writing <paramref name="input"/> to it when
    /// given; on the thread pool.
    /// </summary>
    public Task<GitResult> RunAsync(string folder, IReadOnlyList<string> arguments, byte[]? input = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(arguments);
        return Task.Run(() => RunCoreAsync(folder, arguments, input, cancellationToken), cancellationToken);
    }

    private async Task<GitResult> RunCoreAsync(string folder, IReadOnlyList<string> arguments, byte[]? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Paths as they are (no octal quoting), never a pager or an editor, never a prompt in a terminal nobody sees.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("color.ui=false");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GIT_EDITOR"] = "true";
        start.Environment["LC_ALL"] = "C";

        using var process = new Process { StartInfo = start };
        process.Start();
        using var output = new MemoryStream();
        var reading = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();
            await reading.ConfigureAwait(false);
            string error = await errors.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new GitResult(process.ExitCode, output.ToArray(), error.Trim());
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended meanwhile.
            }

            throw;
        }
    }
}
