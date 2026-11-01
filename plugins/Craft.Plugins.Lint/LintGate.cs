// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Npm;

namespace Craft.Plugins.Lint;

/// <summary>
/// Which sandbox a lint or format run goes through, and the question asked once per workspace and program. A run the user
/// asked for (Format Document, "Lint this file") goes through <c>sandbox</c> (approve-each asks each time); a run nobody
/// asked for just now (on save) needs the workspace to allow the program first: asked once ("Let ESLint run on its own in
/// this workspace?"), the answer kept in the user's settings (<c>CRAFT_PLUGIN_LINT_ALLOWED</c>, this machine's only:
/// never the workspace's file, so a workspace cannot allow itself), taken back on the settings page or with "Lint: Forget
/// this workspace's approvals". Once allowed, every run of that program there goes through <c>autoSandbox</c>. "Not now"
/// stops the automatic runs until the window closes.
/// </summary>
internal sealed class LintGate(IEditorHost host)
{
    /// <summary>Who asks, in a sandbox's question (Idrak's CommandLimits.Requester).</summary>
    public const string Requester = "the lint plug-in";

    // What was answered in this window (the process), shared by the lint and Prettier plug-ins' gates: "Not now", and an
    // approval kept here too (the settings may not be writable, or not yet read back).
    private static readonly HashSet<string> _declined = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _remembered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _gate = new();

    /// <summary>The key of a program in a workspace: "folder|program".</summary>
    public static string Key(string root, string program) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + "|" + program;

    /// <summary>Whether <paramref name="program"/> may run on its own in the workspace <paramref name="root"/> (on the UI thread: reads the settings).</summary>
    public bool IsAllowed(string root, string program)
    {
        string key = Key(root, program);
        lock (_gate)
        {
            if (_remembered.Contains(key))
            {
                return true;
            }
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in LintSettings.Read(host.Settings).Allowed)
        {
            int bar = entry.LastIndexOf('|');
            if (bar > 0 && string.Equals(Key(entry[..bar], entry[(bar + 1)..]), key, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the user said "Not now" to <paramref name="program"/> in <paramref name="root"/> in this window.</summary>
    public static bool IsDeclined(string root, string program)
    {
        lock (_gate)
        {
            return _declined.Contains(Key(root, program));
        }
    }

    /// <summary>
    /// The sandbox a run of <paramref name="program"/> (<paramref name="title"/>, "ESLint") in <paramref name="root"/> goes
    /// through, or why none: asks once for an automatic run of a program the workspace has not allowed. Any thread: the
    /// settings are read and the question asked on the UI thread.
    /// </summary>
    public Task<(ICommandSandbox? Sandbox, string? Why)> SandboxAsync(string root, string program, string title, bool automatic, string what, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<(ICommandSandbox?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Dispatch("lint: which sandbox", async () =>
        {
            try
            {
                done.TrySetResult(await ChooseAsync(root, program, title, automatic, what, cancellationToken).ConfigureAwait(true));
            }
#pragma warning disable CA1031 // The asking failed: the run does not happen, and the caller hears why.
            catch (Exception e)
#pragma warning restore CA1031
            {
                done.TrySetResult((null, e.Message));
            }
        });
        return done.Task.WaitAsync(cancellationToken);
    }

    private async Task<(ICommandSandbox?, string?)> ChooseAsync(string root, string program, string title, bool automatic, string what, CancellationToken cancellationToken)
    {
        var settings = LintSettings.Read(host.Settings);
        bool allowed = IsAllowed(root, program);
        if (automatic && !allowed)
        {
            if (IsDeclined(root, program))
            {
                return (null, $"{title} is not allowed to run on its own in this workspace (\"Not now\" this session).");
            }

            int answer = await host.AskAsync(new EditorQuestion($"Let {title} run on its own in this workspace?", what, ["Allow in this workspace", "Not now"])
            {
                Details =
                [
                    $"Program: {title}, the project's own install, run by Node",
                    $"Folder: {root}",
                    $"Runs in: the {settings.AutoSandbox} sandbox, without asking again",
                    $"Remembered in your settings ({PluginSetting.VariableName(LintSettings.PluginName, "allowed")}), never the workspace's; Settings › Editor › Lint takes it back",
                ],
                Dismissed = 1,
            }, cancellationToken).ConfigureAwait(true);
            if (answer != 0)
            {
                lock (_gate)
                {
                    _declined.Add(Key(root, program));
                }

                return (null, $"{title} was not allowed to run on its own in this workspace.");
            }

            await RememberAsync(root, program).ConfigureAwait(true);
            allowed = true;
        }

        string name = allowed ? settings.AutoSandbox : settings.Sandbox;
        string variable = PluginSetting.VariableName(LintSettings.PluginName, allowed ? "autoSandbox" : "sandbox");
        return CommandSandboxes.Find(name) is { } sandbox
            ? (sandbox, null)
            : (null, $"The command sandbox '{name}' ({variable}) is not registered (registered: {string.Join(", ", CommandSandboxes.Names)}); {title} did not run.");
    }

    /// <summary>Allows <paramref name="program"/> to run on its own in <paramref name="root"/>: written to the user's settings (kept for this window when it cannot be).</summary>
    public async Task RememberAsync(string root, string program)
    {
        string key = Key(root, program);
        lock (_gate)
        {
            _remembered.Add(key);
            _declined.Remove(key);
        }

        var list = LintSettings.Read(host.Settings).Allowed.ToList();
        if (!list.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(key);
        }

        if (await host.Settings.SetAsync(LintSettings.PluginName, "allowed", string.Join(';', list)).ConfigureAwait(true) is { } refused)
        {
            host.Log(EditorLogLevel.Information, $"lint: {program} is allowed in {root} for this window only ({refused})");
        }
    }

    /// <summary>Takes back every program's approval in the workspace <paramref name="root"/>; how many were taken back.</summary>
    public async Task<int> ForgetAsync(string root)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + "|";
        int taken;
        lock (_gate)
        {
            taken = _remembered.RemoveWhere(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            _declined.RemoveWhere(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        var list = LintSettings.Read(host.Settings).Allowed.ToList();
        int before = list.Count;
        list.RemoveAll(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (list.Count != before)
        {
            taken = Math.Max(taken, before - list.Count);
            await host.Settings.SetAsync(LintSettings.PluginName, "allowed", list.Count == 0 ? null : string.Join(';', list)).ConfigureAwait(true);
        }

        return taken;
    }

    /// <summary>A run's limits: who asks (the lint plug-in), what for, the workspace as its root, its time, quiet npm variables.</summary>
    public static CommandLimits Limits(string root, string purpose, TimeSpan time) => new()
    {
        Time = time,
        Root = root,
        Requester = Requester,
        Purpose = purpose,
        MaxOutputCharacters = 8_000,
        Environment = new Dictionary<string, string>(NpmRuns.Environment(new NpmSettings { Color = false }), StringComparer.Ordinal),
    };
}
