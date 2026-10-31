// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Sandbox;

/// <summary>What the user answered to one command (<see cref="SandboxSession.RunAsync"/>).</summary>
public enum CommandApproval
{
    /// <summary>Run it this time.</summary>
    AllowOnce,

    /// <summary>Run it, and the program's later commands in the same folder for the rest of this run without asking.</summary>
    AllowProgramForRun,

    /// <summary>Do not run it; the agent is told the user said no.</summary>
    Deny,
}

/// <summary>
/// The approve-each sandbox in one editor host: it asks the user (on the host's UI thread), remembers the programs allowed
/// for this run (until the command "Sandbox: end this run", or a gap between commands longer than the setting
/// <c>runGapMinutes</c>), and runs an allowed command in the inner sandbox with each line of its output in the read-only
/// terminal tab "Agent sandbox" of the bottom panel's Terminal tab (<see cref="IEditorTerminals.OpenOutput"/>, phase U2;
/// opened on the first command, a batch of lines per UI turn, never one dispatch per line); the last <c>panelLines</c> are
/// also kept here (what a host without terminals still has).
/// </summary>
internal sealed class SandboxSession : IDisposable
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly IEditorHost _host;
    /// <summary>The terminal tab's name.</summary>
    public const string OutputName = "Agent sandbox";

    private const string Intro = "Commands an agent runs through the approve-each sandbox show here, each after you allow it.";
    private readonly Lock _gate = new();
    private readonly List<string> _lines = [];
    private readonly List<string> _unsent = [];
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly IEditorNotification _status;
    private IEditorTerminalOutput? _output;
    private SandboxSettings _settings;
    private long _lastCommand;
    private bool _flushPending;
    private bool _disposed;

    public SandboxSession(IEditorHost host)
    {
        _host = host;
        _settings = SandboxSettings.From(host.Settings);
        // Phase U: the last command as a status line of the notification centre; U2: its output in the "Agent sandbox"
        // terminal tab (the side panel is gone).
        _status = host.ShowStatus("Sandbox: the last command", "none yet");
    }

    /// <summary>The settings as the host gave them last (read on its UI thread when the plug-in starts and before each command).</summary>
    public SandboxSettings Settings => Volatile.Read(ref _settings);

    /// <summary>The lines kept (the last <c>panelLines</c>), oldest first.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>The programs allowed for this run, as "folder|program".</summary>
    public IReadOnlyCollection<string> Allowed
    {
        get
        {
            lock (_gate)
            {
                return [.. _allowed];
            }
        }
    }

    /// <summary>The inner sandbox, or null when the setting names one that is not registered or this one.</summary>
    public ICommandSandbox? Inner()
    {
        string name = Settings.Inner;
        return name.Equals(ApproveEachSandbox.SandboxName, StringComparison.OrdinalIgnoreCase) ? null : CommandSandboxes.Find(name);
    }

    /// <summary>Why <see cref="Inner"/> is null.</summary>
    public string InnerProblem()
    {
        string name = Settings.Inner;
        return name.Equals(ApproveEachSandbox.SandboxName, StringComparison.OrdinalIgnoreCase)
            ? $"The {ApproveEachSandbox.SandboxName} sandbox cannot run its commands in itself: set {PluginSetting.VariableName(SandboxSettings.PluginName, "inner")} to another sandbox."
            : $"The {ApproveEachSandbox.SandboxName} sandbox runs allowed commands in '{name}' ({PluginSetting.VariableName(SandboxSettings.PluginName, "inner")}), which is not registered "
              + $"(registered: {string.Join(", ", CommandSandboxes.Names)}).";
    }

    /// <summary>Forgets the programs allowed for this run.</summary>
    public void EndRun()
    {
        lock (_gate)
        {
            _allowed.Clear();
        }

        Append("— the run ended: the next command asks again —");
    }

    /// <summary>Empties the kept lines and the terminal tab (its screen and scrollback).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _unsent.Clear();
        }

        _output?.Append("\e[3J\e[2J\e[H");
    }

    public async Task<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken)
    {
        if (command.Count == 0 || command[0].Length == 0)
        {
            return ApproveEachSandbox.Refused("Error: empty command.");
        }

        // The settings are read on the host's UI thread, where its members are called.
        var read = new TaskCompletionSource<SandboxSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Dispatch(() =>
        {
            try
            {
                read.TrySetResult(SandboxSettings.From(_host.Settings));
            }
#pragma warning disable CA1031 // A setting that does not parse: the command is not run, and the agent hears why.
            catch (Exception error)
#pragma warning restore CA1031
            {
                read.TrySetException(error);
            }
        });
        Volatile.Write(ref _settings, await read.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
        if (Inner() is not { } inner)
        {
            return ApproveEachSandbox.Refused(InnerProblem());
        }

        if (inner.Check(limits) is { } cannot)
        {
            throw new NotSupportedException(cannot);
        }

        var settings = Settings;
        string program = Path.GetFileNameWithoutExtension(command[0]);
        string root = limits.Root ?? directory;
        string key = Path.GetFullPath(root) + "|" + program;
        string line = CommandLine(command);
        bool remembered;
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastCommand != 0 && Stopwatch.GetElapsedTime(_lastCommand, now) > TimeSpan.FromMinutes(settings.RunGapMinutes))
            {
                _allowed.Clear();                              // a long gap: a new run
            }

            _lastCommand = now;
            remembered = _allowed.Contains(key);
        }

        var answer = remembered ? CommandApproval.AllowOnce : await AskAsync(line, program, directory, root, limits, cancellationToken).ConfigureAwait(false);
        if (answer == CommandApproval.Deny)
        {
            Append($"$ {line}");
            Append("  denied by you; the agent was told.");
            return ApproveEachSandbox.Refused($"The user denied this command ({line}). Do not run it again; ask the user, or do the task another way.");
        }

        if (answer == CommandApproval.AllowProgramForRun)
        {
            lock (_gate)
            {
                _allowed.Add(key);
            }
        }

        Append($"$ {line}{(remembered ? "    (allowed for this run)" : "")}");
        var clock = Stopwatch.StartNew();
        // Phase U: the command shows in the status bar with its last line and Cancel (which stops it as the agent's stop does).
        using var activity = _host.Activities.Start("Sandbox: " + (line.Length <= 60 ? line : string.Concat(line.AsSpan(0, 59), "…")), ActivityKinds.Command, cancellable: true, cancellationToken);
        var watched = limits with { Output = WithLive(limits.Output, activity) };
        _status.Update("$ " + line + " · running");
        try
        {
            var result = await inner.RunAsync(command, directory, watched, activity.CancellationToken).ConfigureAwait(false);
            string outcome = result switch
            {
                { Refused: true } => $"  refused by the {inner.Name} sandbox: {result.Output}",
                { TimedOut: true } => $"  stopped at its time limit, {ApproveEachSandbox.Seconds(limits.Time)}",
                { OutOfMemory: true } => "  stopped at its memory limit",
                _ => string.Create(Culture, $"  exit code {result.ExitCode} · elapsed {clock.Elapsed:hh\\:mm\\:ss\\.ff}"),
            };
            Append(outcome);
            _status.Update("$ " + line + " ·" + outcome, result.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Warning);
            activity.Complete(outcome.Trim());      // a non-zero exit is the agent's to read, not a failure that toasts

            return result;
        }
        catch (OperationCanceledException)
        {
            Append("  stopped (cancelled)");
            _status.Update("$ " + line + " · stopped (cancelled)", NotificationSeverity.Warning);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _status.Dispose();
        _output = null;
    }

    // The question, asked on the UI thread; a dismissed one (or a host that cannot ask) is a denial.
    private async Task<CommandApproval> AskAsync(string line, string program, string directory, string root, CommandLimits limits, CancellationToken cancellationToken)
    {
        string folder = string.Equals(Path.GetFullPath(directory), Path.GetFullPath(root), StringComparison.Ordinal)
            ? "the workspace"
            : Path.GetRelativePath(root, directory).Replace('\\', '/');
        string allowedPrograms = limits.Programs is { Count: > 0 } programs ? $" (allowed: {string.Join(", ", programs)})" : "";
        var question = new EditorQuestion("The agent wants to run a command", line,
            ["Allow once", $"Allow {program} for this run", "Deny and tell the agent"])
        {
            Details =
            [
                $"Program: {program}{allowedPrograms}",
                $"Time limit: {ApproveEachSandbox.Seconds(limits.Time)}",
                $"Network: {(limits.Network ? "on" : "off")}",
                $"Folder: {folder}",
                $"Runs in: the {Settings.Inner} sandbox",
            ],
            Dismissed = 2,
        };
        var asked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Dispatch(() =>
        {
            try
            {
                _host.AskAsync(question, cancellationToken).ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        asked.TrySetCanceled(cancellationToken);
                    }
                    else if (t.Exception is { } error)
                    {
                        asked.TrySetException(error.InnerExceptions);
                    }
                    else
                    {
                        asked.TrySetResult(t.Result);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
#pragma warning disable CA1031 // The asking failed: the command is not run, and the agent hears why.
            catch (Exception error)
#pragma warning restore CA1031
            {
                asked.TrySetException(error);
            }
        });
        int answer = await asked.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return answer switch
        {
            0 => CommandApproval.AllowOnce,
            1 => CommandApproval.AllowProgramForRun,
            _ => CommandApproval.Deny,
        };
    }

    private Action<string> WithLive(Action<string>? other, IActivity activity) => text =>
    {
        Append(text);
        activity.Report(null, text.Length <= 80 ? text.Trim() : string.Concat(text.AsSpan(0, 79), "…").Trim());
        other?.Invoke(text);
    };

    // Any thread: the line is kept, and one UI turn shows every line kept since the last.
    private void Append(string text)
    {
        bool schedule;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _lines.Add(text);
            _unsent.Add(text);
            int keep = Settings.PanelLines;
            if (_lines.Count > keep)
            {
                _lines.RemoveRange(0, _lines.Count - keep);
            }

            if (_unsent.Count > keep)
            {
                _unsent.RemoveRange(0, _unsent.Count - keep);
            }

            schedule = !_flushPending;
            _flushPending = true;
        }

        if (schedule)
        {
            _host.Dispatch(Flush);
        }
    }

    // The UI thread: the lines since the last turn go to the "Agent sandbox" tab in one append; the tab opens on the first
    // command (and again after the user closed it), shown without taking the keyboard.
    private void Flush()
    {
        string text;
        lock (_gate)
        {
            _flushPending = false;
            if (_disposed || _unsent.Count == 0)
            {
                return;
            }

            text = string.Join('\n', _unsent) + "\n";
            _unsent.Clear();
        }

        if (_output is not { IsOpen: true })
        {
            _output = _host.Terminals?.OpenOutput(OutputName);
            if (_output is null)
            {
                return;                      // a host without terminals: the lines stay in Lines only
            }

            _output.Append("\e[2m" + Intro + "\e[0m\n");
            _output.Show();
        }

        _output.Append(text);
    }

    // The command as a line the user reads: each word quoted when it holds a space or a quote.
    private static string CommandLine(IReadOnlyList<string> command) =>
        string.Join(' ', command.Select(word => word.Length == 0 || word.AsSpan().IndexOfAny(" \t\"'") >= 0 ? "\"" + word.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : word));
}
