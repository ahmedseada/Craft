// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Sandbox;

/// <summary>
/// The "approve-each" command sandbox (plan phase P5) in Idrak's <see cref="CommandSandboxes"/>: each command is put to
/// the user in the editor (<see cref="IEditorHost.AskAsync"/>: the command line, its program, time limit, network and
/// folder; Allow once, Allow PROGRAM for this run, Deny and tell the agent), and an allowed one runs in the sandbox the
/// plug-in's setting <c>inner</c> names, under the same limits, its output live in the sandbox terminal panel. A denied
/// command, a dismissed question, or no editor to ask gives a refused result whose output tells the agent why; nothing
/// runs without the user. It enforces what its inner sandbox enforces: <see cref="Check"/> is the inner one's (a network
/// turned off needs "container").
/// </summary>
public sealed class ApproveEachSandbox : ICommandSandbox
{
    /// <summary>The sandbox's name in <see cref="CommandSandboxes"/>: "approve-each".</summary>
    public const string SandboxName = "approve-each";

    private ApproveEachSandbox()
    {
    }

    /// <summary>The sandbox.</summary>
    public static ApproveEachSandbox Instance { get; } = new();

    /// <inheritdoc />
    public string Name => SandboxName;

    /// <inheritdoc />
    public string Summary => "each command shown to the user in the editor and run only once allowed, in the inner sandbox (setting 'inner', 'process' by default), its output live in the sandbox terminal";

    /// <summary>The editor session that asks the user, or null when no editor has the plug-in active.</summary>
    internal static SandboxSession? Session { get; set; }

    /// <inheritdoc />
    public string? Check(CommandLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (Session is not { } session)
        {
            return $"The {SandboxName} sandbox asks the user in the Craft editor before each command, and no editor is running it here: run the agent in Craft, "
                   + $"or name another sandbox ({string.Join(", ", CommandSandboxes.Names.Where(n => !n.Equals(SandboxName, StringComparison.OrdinalIgnoreCase)))}).";
        }

        return session.Inner() is { } inner ? inner.Check(limits) : session.InnerProblem();
    }

    /// <inheritdoc />
    public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(limits);
        if (Session is not { } session)
        {
            return Refused(Check(limits)!);
        }

        return await session.RunAsync(command, directory, limits, cancellationToken).ConfigureAwait(false);
    }

    internal static CommandResult Refused(string why) => new(null, false, why, TimeSpan.Zero, true);

    internal static string Seconds(TimeSpan time) => string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.#} s");
}
