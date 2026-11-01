// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>What the two linters share: the languages, how a run goes through its sandbox and reads its report.</summary>
public abstract class NodeLinter : ILinter
{
    /// <summary>The languages a JavaScript or TypeScript linter lints (matched by extension: a removed language plug-in still gets linted files).</summary>
    public IReadOnlyList<string> Languages { get; } = ["javascript", "typescript", "javascriptreact", "typescriptreact"];

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Title { get; }

    /// <inheritdoc />
    public string Summary => $"{Title}: the project's own install, run by Node through a command sandbox; diagnostics and fixes from its JSON report";

    /// <inheritdoc />
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>Whether a global install may run where a project has none (the lint plug-in's <c>allowGlobal</c>).</summary>
    public bool AllowGlobal { get; set; }

    /// <inheritdoc />
    public string? Unavailable => LintNode.DeviceProblem ?? LintNode.Problem;

    /// <inheritdoc />
    public abstract Task<LinterProject?> DetectAsync(string path, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<LintResult> LintAsync(LintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = request.Project;
        if (!project.Ready || project.Program is null)
        {
            return LintResult.Failed(project.Problem ?? $"{Title} is not installed in {project.Folder}.");
        }

        if (LintNode.Program is not { } node)
        {
            return LintResult.Failed(Unavailable ?? "Node.js was not found.");
        }

        Directory.CreateDirectory(request.ScratchFolder);
        string output = Path.Combine(request.ScratchFolder, $"{Name}-{Guid.NewGuid():N}.json");
        var environment = new Dictionary<string, string>(request.Limits.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var (name, value) in project.Environment)
        {
            environment[name] = value;
        }

        var limits = request.Limits with { Environment = environment };
        try
        {
            var command = Command(node, project, request.Files, output);
            var result = await request.Sandbox.RunAsync(command, project.Folder, limits, cancellationToken).ConfigureAwait(false);
            if (result.Refused)
            {
                return LintResult.Failed($"not run: {result.Output}");
            }

            if (result.TimedOut)
            {
                return LintResult.Failed($"{Title} stopped at its time limit ({limits.Time.TotalSeconds:0} s)");
            }

            byte[] report = File.Exists(output) ? await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false) : [];
            if (!Succeeded(result.ExitCode) || (report.Length == 0 && RequiresReport))
            {
                string said = result.Output.Trim();
                return LintResult.Failed($"{Title} failed (exit code {result.ExitCode}){(said.Length > 0 ? ": " + FirstLines(said) : "")}");
            }

            try
            {
                var files = Read(report);
                return new LintResult([.. files.Select(f => f with { Path = Path.GetFullPath(Path.IsPathRooted(f.Path) ? f.Path : Path.Combine(project.Folder, f.Path)) })]);
            }
            catch (FormatException e)
            {
                return LintResult.Failed(e.Message);
            }
        }
        catch (NotSupportedException e)
        {
            return LintResult.Failed(e.Message);
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The command line of a run: node, the tool's script, its report written to <paramref name="output"/>, the files (or the whole project).</summary>
    public abstract IReadOnlyList<string> Command(string node, LinterProject project, IReadOnlyList<string> files, string output);

    /// <summary>The report's files.</summary>
    protected abstract IReadOnlyList<LintFileResult> Read(ReadOnlySpan<byte> report);

    /// <summary>Whether an exit code is a finished run (problems found or not).</summary>
    protected abstract bool Succeeded(int? exitCode);

    /// <summary>Whether a finished run always writes a report.</summary>
    protected virtual bool RequiresReport => true;

    private static string FirstLines(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" · ", lines.Take(4));
    }

    /// <summary>A file's path relative to <paramref name="folder"/> with '/' (what a tool is given on its command line).</summary>
    protected static string Relative(string folder, string file)
    {
        string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? Path.GetFullPath(file) : relative;
    }
}

/// <summary>
/// "eslint": the project's own ESLint (<c>node_modules/eslint/bin/eslint.js</c>, found up to the workspace's root; a
/// global one only with <c>allowGlobal</c>), its flat configuration first (<c>eslint.config.*</c>, nearest up to the
/// root); an <c>.eslintrc*</c> only when there is none, run with <c>ESLINT_USE_FLAT_CONFIG=false</c> on ESLint 9 (said),
/// refused on ESLint 10 (which no longer reads it, said with the way out). Run as
/// <c>node eslint.js --format json --output-file REPORT FILES</c> in the configuration's folder.
/// </summary>
public sealed class EslintLinter : NodeLinter
{
    /// <summary>The linter's name.</summary>
    public const string LinterName = "eslint";

    private EslintLinter()
    {
    }

    /// <summary>The linter.</summary>
    public static EslintLinter Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => LinterName;

    /// <inheritdoc />
    public override string Title => "ESLint";

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".js", ".mjs", ".cjs", ".jsx", ".ts", ".mts", ".cts", ".tsx"];

    /// <inheritdoc />
    public override Task<LinterProject?> DetectAsync(string path, string workspaceRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(path, workspaceRoot), cancellationToken);

    /// <summary>The project of <paramref name="path"/> (reads files).</summary>
    public LinterProject? Detect(string path, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        bool legacy = false;
        string? config = LintProjects.Nearest(path, workspaceRoot, LintProjects.EslintFlat);
        if (config is null)
        {
            config = LintProjects.Nearest(path, workspaceRoot, LintProjects.EslintLegacy) ?? LintProjects.NearestPackageWith(path, workspaceRoot, "eslintConfig"u8);
            legacy = config is not null;
        }

        if (config is null)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(config)!;
        string script = Path.Combine("bin", "eslint.js");
        string? program = LintProjects.LocalInstall(folder, workspaceRoot, "eslint", script);
        bool global = false;
        if (program is null && AllowGlobal && LintProjects.GlobalInstall(LintNode.Program, "eslint", script) is { } found)
        {
            program = found;
            global = true;
        }

        if (program is null)
        {
            return new LinterProject(Name, folder, config, null,
                $"{Path.GetFileName(config)} is here but ESLint is not installed in the project: run npm install (or allow a global ESLint, {PluginSetting.VariableName(LintSettings.PluginName, "allowGlobal")}).");
        }

        string? version = LintProjects.VersionOf(program);
        int major = LintProjects.Major(version);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        string? note = global ? $"ESLint {version} runs from the global install ({program}): the project has none." : null;
        if (legacy)
        {
            if (major >= 10)
            {
                return new LinterProject(Name, folder, config, version,
                    $"ESLint {version} reads only eslint.config.* and this project has {Path.GetFileName(config)}: migrate it (npx @eslint/migrate-config {Path.GetFileName(config)}) to lint here.") { Program = program };
            }

            if (major == 9)
            {
                environment["ESLINT_USE_FLAT_CONFIG"] = "false";
                note = $"ESLint {version} runs this project's {Path.GetFileName(config)} with ESLINT_USE_FLAT_CONFIG=false (its legacy mode, gone in ESLint 10): move it to eslint.config.js (npx @eslint/migrate-config {Path.GetFileName(config)}).";
            }
        }
        else if (major is > 0 and < 9)
        {
            environment["ESLINT_USE_FLAT_CONFIG"] = "true";       // ESLint 8 reads eslint.config.* only when asked to
        }

        return new LinterProject(Name, folder, config, version, null) { Program = program, Environment = environment, Note = note };
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Command(string node, LinterProject project, IReadOnlyList<string> files, string output)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(files);
        var command = new List<string> { node, project.Program!, "--format", "json", "--output-file", output };
        if (LintProjects.Major(project.Version) >= 9)
        {
            command.Add("--no-warn-ignored");                   // a saved file the configuration ignores is not a warning of its own
        }

        if (files.Count == 0)
        {
            command.Add(".");
        }
        else
        {
            command.AddRange(files.Select(f => Relative(project.Folder, f)));
        }

        return command;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<LintFileResult> Read(ReadOnlySpan<byte> report) => LintJson.ReadEslint(report);

    /// <inheritdoc />
    protected override bool Succeeded(int? exitCode) => exitCode is 0 or 1;  // 1: problems found; 2: ESLint itself failed
}

/// <summary>
/// "tslint": only where a project has <c>tslint.json</c> and no ESLint configuration (the lint plug-in asks ESLint first),
/// its own install (<c>node_modules/tslint/bin/tslint</c>), run as <c>node tslint --format json --out REPORT --project
/// tsconfig.json FILES</c>; TSLint has been deprecated since 2019, said once.
/// </summary>
public sealed class TslintLinter : NodeLinter
{
    /// <summary>The linter's name.</summary>
    public const string LinterName = "tslint";

    private TslintLinter()
    {
    }

    /// <summary>The linter.</summary>
    public static TslintLinter Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => LinterName;

    /// <inheritdoc />
    public override string Title => "TSLint";

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx"];

    /// <inheritdoc />
    public override Task<LinterProject?> DetectAsync(string path, string workspaceRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(path, workspaceRoot), cancellationToken);

    /// <summary>The project of <paramref name="path"/> (reads files).</summary>
    public LinterProject? Detect(string path, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        if (LintProjects.Nearest(path, workspaceRoot, ["tslint.json", "tslint.yaml"]) is not { } config)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(config)!;
        string script = Path.Combine("bin", "tslint");
        string? program = LintProjects.LocalInstall(folder, workspaceRoot, "tslint", script)
                          ?? (AllowGlobal ? LintProjects.GlobalInstall(LintNode.Program, "tslint", script) : null);
        const string Deprecated = "TSLint has been deprecated since 2019: move to ESLint with typescript-eslint (npx tslint-to-eslint-config), which Craft runs the same way.";
        if (program is null)
        {
            return new LinterProject(Name, folder, config, null, $"tslint.json is here but TSLint is not installed in the project: run npm install. {Deprecated}") { Note = Deprecated };
        }

        return new LinterProject(Name, folder, config, LintProjects.VersionOf(program), null) { Program = program, Note = Deprecated };
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Command(string node, LinterProject project, IReadOnlyList<string> files, string output)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(files);
        var command = new List<string> { node, project.Program!, "--format", "json", "--out", output };
        if (File.Exists(Path.Combine(project.Folder, "tsconfig.json")))
        {
            command.Add("--project");
            command.Add("tsconfig.json");
        }

        command.AddRange(files.Select(f => Relative(project.Folder, f)));
        if (files.Count == 0 && !command.Contains("--project"))
        {
            command.Add("**/*.ts");
        }

        return command;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<LintFileResult> Read(ReadOnlySpan<byte> report) => LintJson.ReadTslint(report);

    /// <inheritdoc />
    protected override bool Succeeded(int? exitCode) => exitCode is 0 or 2;  // 2: errors found; 1: bad arguments or configuration

    /// <inheritdoc />
    protected override bool RequiresReport => false;
}
