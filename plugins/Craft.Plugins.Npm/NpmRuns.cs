// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>How a script or an install ended (<see cref="NpmRunOutcome"/>).</summary>
public enum NpmRunEnd
{
    /// <summary>It did not start: denied, refused by the sandbox, no sandbox, no Node, already running.</summary>
    NotRun,

    /// <summary>It exited with 0.</summary>
    Succeeded,

    /// <summary>It exited with another code.</summary>
    Failed,

    /// <summary>Cancel (or Stop) ended it with its process tree.</summary>
    Stopped,

    /// <summary>Its time limit ended it.</summary>
    TimedOut,
}

/// <summary>A run's end: how, its exit code, and why it did not run or end well.</summary>
/// <param name="End">How it ended.</param>
/// <param name="ExitCode">Its exit code, or null.</param>
/// <param name="Reason">Why it did not run, or what stopped it, or null.</param>
public sealed record NpmRunOutcome(NpmRunEnd End, int? ExitCode = null, string? Reason = null);

/// <summary>The commands and limits every npm run shares: npm as node with npm-cli.js, its variables, who asks.</summary>
public static class NpmRuns
{
    /// <summary>Who asks, in a sandbox's question (Idrak's <see cref="CommandLimits.Requester"/>): "the npm plug-in".</summary>
    public const string Requester = "the npm plug-in";

    /// <summary>
    /// The variables every run gets: npm's funding and audit messages, its update notice and Angular's analytics question
    /// off (nothing waits on an answer nobody can give), and colours kept when <see cref="NpmSettings.Color"/> is on.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Environment(NpmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["npm_config_fund"] = "false",
            ["npm_config_audit"] = "false",
            ["npm_config_update_notifier"] = "false",
            ["NG_CLI_ANALYTICS"] = "false",
        };
        if (settings.Color)
        {
            variables["FORCE_COLOR"] = "1";
            variables["npm_config_color"] = "always";
        }

        return variables;
    }

    /// <summary><c>node npm-cli.js run SCRIPT</c>, with <paramref name="arguments"/> (a command line) after <c>--</c> when given.</summary>
    public static IReadOnlyList<string> Script(NodeInstall install, string script, string? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentException.ThrowIfNullOrEmpty(script);
        var words = RunCommandLine.Split(arguments);
        return words.Count == 0 ? [install.Node!, install.NpmCli!, "run", script] : [install.Node!, install.NpmCli!, "run", script, "--", .. words];
    }

    /// <summary><c>node npm-cli.js install</c> (or <c>ci</c>).</summary>
    public static IReadOnlyList<string> Install(NodeInstall install, string verb)
    {
        ArgumentNullException.ThrowIfNull(install);
        return [install.Node!, install.NpmCli!, verb];
    }

    /// <summary>The limits of a run: its time, the variables, the workspace as its root, the npm plug-in as who asks and what it runs.</summary>
    public static CommandLimits Limits(NpmSettings settings, string root, string purpose, TimeSpan time) => new()
    {
        Time = time,
        Environment = Environment(settings),
        Root = root,
        Requester = Requester,
        Purpose = purpose,
        MaxOutputCharacters = 4_000,
    };

    /// <summary>A run's tab: "npm: build" for the root's package, "npm: build (src/app)" for another's.</summary>
    public static string TabName(NpmPackage package, string what)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package.Folder.Length == 0 ? "npm: " + what : $"npm: {what} ({package.Folder})";
    }
}
