// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Lint;

/// <summary>
/// The document formatter "prettier" (W, contract 8): the project's own Prettier (<c>node_modules/prettier</c>, found up
/// to the workspace's root; a global one only with <c>allowGlobal</c>) where the project has a Prettier configuration
/// (<c>.prettierrc*</c>, <c>prettier.config.*</c> or "prettier" in package.json), for JS, TS, JSX, TSX, CSS, SCSS, LESS,
/// HTML, JSON, Markdown and YAML; Craft's built-in formatters otherwise. What applies is found once per folder (rule 75)
/// and said on the status bar's language tooltip. A format writes the text to the user's data folder and runs Craft's
/// small script with Node through the lint plug-in's sandbox: Prettier's own <c>resolveConfig</c>, <c>getFileInfo</c>
/// (.prettierignore) and <c>format</c> for the file's path, the result read back whole.
/// </summary>
public sealed class PrettierFormatter : IDocumentFormatter
{
    /// <summary>The formatter's name.</summary>
    public const string FormatterName = "prettier";

    /// <summary>The script Craft runs with Node: Prettier's API on a file's text, for its path.</summary>
    public const string Script = """
        'use strict';
        // Craft's Prettier runner: node craft-prettier.cjs PRETTIER_FOLDER INPUT FILE_PATH OUTPUT
        const fs = require('fs');
        const [, , folder, input, filepath, output] = process.argv;
        (async () => {
          const prettier = require(folder);
          const text = fs.readFileSync(input, 'utf8');
          const info = prettier.getFileInfo ? await prettier.getFileInfo(filepath, { ignorePath: undefined, resolveConfig: false }) : { ignored: false };
          if (info && info.ignored) { fs.writeFileSync(output, text); return; }
          const options = (await prettier.resolveConfig(filepath, { editorconfig: true })) || {};
          const formatted = await prettier.format(text, { ...options, filepath });
          fs.writeFileSync(output, formatted);
        })().catch(e => { process.stderr.write(String((e && e.message) || e)); process.exit(2); });
        """;

    private readonly ConcurrentDictionary<string, DocumentFormatterFit> _byFolder = new(StringComparer.Ordinal);

    private PrettierFormatter()
    {
    }

    /// <summary>The formatter.</summary>
    public static PrettierFormatter Instance { get; } = new();

    /// <summary>The session that runs it (its host's settings, sandbox and data folder), or null while the plug-in is not active.</summary>
    internal static PrettierSession? Session { get; set; }

    /// <inheritdoc />
    public string Name => FormatterName;

    /// <inheritdoc />
    public string Title => "Prettier";

    /// <inheritdoc />
    public string Summary => "the project's own Prettier, where it has a Prettier configuration, run by Node through the lint plug-in's sandbox";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = ["javascript", "typescript", "javascriptreact", "typescriptreact", "css", "scss", "less", "html", "json", "markdown", "yaml"];

    /// <inheritdoc />
    public bool Formats(string filePath, string languageName) =>
        Session is { Settings: { Enabled: true } settings } && LintNode.DeviceProblem is null && settings.Formats(filePath ?? "");

    /// <inheritdoc />
    public DocumentFormatterFit? Cached(string filePath) =>
        Path.GetDirectoryName(Path.GetFullPath(filePath)) is { } folder && _byFolder.TryGetValue(folder, out var fit) ? fit : null;

    /// <summary>Forgets what was found (the workspace changed, a configuration or package saved).</summary>
    public void Forget() => _byFolder.Clear();

    /// <inheritdoc />
    public Task<DocumentFormatterFit> FitAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        string folder = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? filePath;
        if (_byFolder.TryGetValue(folder, out var known))
        {
            return Task.FromResult(known);
        }

        var settings = Session?.Settings ?? new PrettierSettings();
        return Task.Run(() => _byFolder.GetOrAdd(folder, f => Fit(f, workspaceRoot ?? f, settings)), cancellationToken);
    }

    /// <summary>Whether Prettier formats files of <paramref name="folder"/> (reads files): its install and its configuration.</summary>
    public static DocumentFormatterFit Fit(string folder, string root, PrettierSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string script = Path.Combine("bin", "prettier.cjs");
        string? program = LintProjects.LocalInstall(folder, root, "prettier", script);
        string whose = "the project's";
        if (program is null && settings.AllowGlobal && LintProjects.GlobalInstall(LintNode.Program, "prettier", script) is { } global)
        {
            program = global;
            whose = "the global";
        }

        string? config = LintProjects.Nearest(folder, root, LintProjects.PrettierConfigs) ?? LintProjects.NearestPackageWith(folder, root, "prettier"u8);
        if (program is null)
        {
            return new DocumentFormatterFit(false, config is null
                ? "Prettier is not installed in this project"
                : $"{Path.GetFileName(config)} is here but Prettier is not installed in the project (npm install)");
        }

        string version = LintProjects.VersionOf(program) is { } v ? " " + v : "";
        if (config is null && settings.RequireConfig)
        {
            return new DocumentFormatterFit(false, $"Prettier{version} is installed but the project has no Prettier configuration (.prettierrc): Craft's own formatter");
        }

        return new DocumentFormatterFit(true, config is null ? $"Prettier{version}, {whose}" : $"Prettier{version}, {whose} ({Path.GetFileName(config)})");
    }

    /// <inheritdoc />
    public async Task<string> FormatAsync(DocumentFormatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot;
        string text = snapshot.GetText(0, snapshot.Length);
        if (Session is not { } session)
        {
            return text;
        }

        if (LintNode.Program is not { } node)
        {
            throw new FormatException(LintNode.DeviceProblem ?? LintNode.Problem ?? "Node.js was not found");
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(request.FilePath))!;
        string root = request.WorkspaceRoot ?? folder;
        string? program = LintProjects.LocalInstall(folder, root, "prettier", Path.Combine("bin", "prettier.cjs"))
                          ?? (session.Settings.AllowGlobal ? LintProjects.GlobalInstall(node, "prettier", Path.Combine("bin", "prettier.cjs")) : null);
        if (program is null)
        {
            throw new FormatException("Prettier is not installed in this project");
        }

        string package = Path.GetDirectoryName(Path.GetDirectoryName(program)!)!;
        var (sandbox, why) = await session.Gate.SandboxAsync(root, FormatterName, Title, request.Automatic,
            $"Prettier formats {Path.GetFileName(request.FilePath)} each time you save it (CRAFT_FORMAT_ON_SAVE), with the project's Prettier configuration.", cancellationToken).ConfigureAwait(false);
        if (sandbox is null)
        {
            if (request.Automatic)
            {
                return text;                                     // not allowed on its own: the file is saved as it is
            }

            throw new FormatException(why ?? "Prettier did not run");
        }

        string scratch = session.ScratchFolder;
        Directory.CreateDirectory(scratch);
        string runner = Path.Combine(scratch, "craft-prettier.cjs");
        if (!File.Exists(runner) || await File.ReadAllTextAsync(runner, cancellationToken).ConfigureAwait(false) != Script)
        {
            await File.WriteAllTextAsync(runner, Script, cancellationToken).ConfigureAwait(false);
        }

        string id = Guid.NewGuid().ToString("N");
        string input = Path.Combine(scratch, id + ".in");
        string output = Path.Combine(scratch, id + ".out");
        try
        {
            await File.WriteAllTextAsync(input, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var time = session.Settings.TimeLimitSeconds <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(session.Settings.TimeLimitSeconds);
            var limits = LintGate.Limits(root, $"Prettier formats {Path.GetFileName(request.FilePath)} (the project's Prettier, {package})", time);
            var result = await sandbox.RunAsync([node, runner, package, input, Path.GetFullPath(request.FilePath), output], folder, limits, cancellationToken).ConfigureAwait(false);
            if (result.Refused)
            {
                throw new FormatException($"Prettier did not run: {result.Output}");
            }

            if (result.TimedOut)
            {
                throw new FormatException($"Prettier stopped at its time limit ({PluginSetting.VariableName(PrettierSettings.PluginName, "timeLimitSeconds")})");
            }

            if (!result.Succeeded || !File.Exists(output))
            {
                string said = result.Output.Trim();
                throw new FormatException(said.Length > 0 ? "Prettier: " + said.ReplaceLineEndings(" ") : $"Prettier failed (exit code {result.ExitCode})");
            }

            return await File.ReadAllTextAsync(output, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var file in (string[])[input, output])
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
