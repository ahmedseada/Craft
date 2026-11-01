// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Plugins.Npm;

/// <summary>
/// The Run view's provider "npm" (<see cref="RunProviders"/>): every script of every package.json the Run view's walk
/// finds (node_modules is never walked), so F5 runs a script; the Run view asks and runs it through its own sandbox
/// (<c>CRAFT_RUN_SANDBOX</c>). Registered while the npm plug-in has found Node; with none it finds nothing.
/// </summary>
public sealed class NpmRunProvider : IRunProvider
{
    /// <summary>The provider's name: "npm".</summary>
    public const string ProviderName = "npm";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "npm scripts: every package.json's scripts, run as node with npm's npm-cli.js";

    /// <inheritdoc />
    public string Title => "npm scripts";

    /// <inheritdoc />
    public bool Watches(ReadOnlySpan<char> fileName) => NpmPackages.Matters(fileName);

    /// <inheritdoc />
    public IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (NpmPlugin.Session is not { Install.Ready: true } session)
        {
            return [];
        }

        var environment = NpmRuns.Environment(session.Settings);
        var targets = new List<RunTarget>();
        foreach (var file in workspace.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NpmPackages.IsPackageFile(Path.GetFileName(file.AsSpan())) || file.Split('/').Contains("node_modules", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var package = NpmPackages.Read(workspace.Root, file);
            foreach (var script in package.Scripts)
            {
                var actions = RunActions.Run;
                if (script.Name == "build")
                {
                    actions |= RunActions.Build;
                }
                else if (script.Name == "test")
                {
                    actions |= RunActions.Test;
                }

                targets.Add(new RunTarget(ProviderName, file + "#" + script.Name, script.Name, file, actions)
                {
                    Detail = $"npm run · {(package.Folder.Length == 0 ? "" : package.Folder + " · ")}{script.Text}",
                    Environment = environment,
                });
            }
        }

        return targets;
    }

    /// <inheritdoc />
    public RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(root);
        int hash = target.Id.LastIndexOf('#');
        string file = hash > 0 ? target.Id[..hash] : target.File;
        string script = hash > 0 ? target.Id[(hash + 1)..] : target.Title;
        string folder = configuration?.Folder is { Length: > 0 } chosen
            ? Path.GetFullPath(Path.Combine(root, chosen))
            : Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(file) ?? ""));
        if (NpmPlugin.Session?.Install is not { Ready: true } install)
        {
            return RunCommand.Unavailable(NpmPlugin.Session?.Install.Problem ?? "The npm plug-in is not active, so npm's scripts do not run.", folder);
        }

        var environment = new Dictionary<string, string>(target.Environment, StringComparer.Ordinal);
        foreach (var (name, value) in configuration?.Environment ?? RunConfiguration.NoEnvironment)
        {
            environment[name] = value;
        }

        return new RunCommand(NpmRuns.Script(install, script, configuration?.Arguments), folder) { Environment = environment };
    }
}
