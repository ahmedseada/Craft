// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Plugins.ClaudeCode;

/// <summary>The diagnostic tools' names, as an MCP client calls them (Claude Code: <c>mcp__craft__&lt;name&gt;</c>).</summary>
public static class DiagnosticToolNames
{
    /// <summary>The app's version, the open folder, the open documents, the running activities.</summary>
    public const string AppState = "craft_app_state";

    /// <summary>The crash, callback and stall reports, the newest first.</summary>
    public const string Reports = "craft_reports";

    /// <summary>A report's text, bounded.</summary>
    public const string ReadReport = "craft_read_report";

    /// <summary>The crash trail's last lines.</summary>
    public const string Trail = "craft_trail";

    /// <summary>Output's last lines.</summary>
    public const string Output = "craft_output";

    /// <summary>The telemetry measures, the slowest first.</summary>
    public const string Telemetry = "craft_telemetry";

    /// <summary>The open documents' errors and warnings.</summary>
    public const string Problems = "craft_problems";

    /// <summary>Opens a file at a line in the editor (it edits nothing).</summary>
    public const string OpenFile = "open_file";

    /// <summary>Runs Craft's tests of a CRAFT_FILTER through the command sandbox, the user approving.</summary>
    public const string RunTests = "run_tests";

    /// <summary>Claude Code's permission prompt (<c>--permission-prompt-tool</c>): the user answers in Craft.</summary>
    public const string Approve = "claude_code_approve";
}

/// <summary>
/// The Claude Code plug-in's tools on Craft's MCP server (<see cref="EditorToolSets"/> "craft-diagnostics", beside the
/// editor's own read, search and <c>propose_edit</c>): read-only diagnostics of the running app (its reports, its crash
/// trail, Output, telemetry, problems, open documents, running activities, version), each bounded by
/// <c>toolOutputCharacters</c>; <c>open_file</c>, which shows a file at a line and edits nothing; <c>run_tests</c>, which
/// runs Craft's tests of a CRAFT_FILTER (never the full suite) through the command sandbox, so the user approves it; and
/// <c>claude_code_approve</c>, Claude Code's permission prompt, answered by the user in Craft. No tool edits a file: a
/// change goes through the editor's <c>propose_edit</c>, a diff the user accepts.
/// </summary>
public sealed class DiagnosticToolSet : IEditorToolSet
{
    /// <summary>The set's name.</summary>
    public const string SetName = "craft-diagnostics";

    private static readonly JsonNode NoArguments = Schema("""{"type":"object","properties":{}}""");
    private static readonly JsonNode ReportsArguments = Schema("""
        {"type":"object","properties":{"kind":{"type":"string","enum":["crash","callback","stall"],"description":"Only reports of this kind; none: every kind."},
        "max":{"type":"integer","description":"How many, the newest first (default 20)."}}}
        """);
    private static readonly JsonNode ReadReportArguments = Schema("""
        {"type":"object","properties":{"name":{"type":"string","description":"The report's file name (from craft_reports), or latest (with kind: the latest of that kind)."},
        "kind":{"type":"string","enum":["crash","callback","stall"]},"max_characters":{"type":"integer","description":"The most characters returned (bounded by the plug-in's setting)."}},"required":["name"]}
        """);
    private static readonly JsonNode LinesArguments = Schema("""
        {"type":"object","properties":{"lines":{"type":"integer","description":"How many of the last lines (default 200)."}}}
        """);
    private static readonly JsonNode MaxArguments = Schema("""
        {"type":"object","properties":{"max":{"type":"integer","description":"How many (default 50)."}}}
        """);
    private static readonly JsonNode OpenFileArguments = Schema("""
        {"type":"object","properties":{"path":{"type":"string","description":"The file: a path in the open folder, or a full path."},
        "line":{"type":"integer","description":"The line, from 1."},"column":{"type":"integer","description":"The column, from 1."}},"required":["path"]}
        """);
    private static readonly JsonNode RunTestsArguments = Schema("""
        {"type":"object","properties":{"filter":{"type":"string","description":"CRAFT_FILTER: test groups or names separated by '|', each at least 3 characters (never the full suite)."},
        "build":{"type":"boolean","description":"Build tests/Craft.Tests first (another command the user approves)."}},"required":["filter"]}
        """);
    private static readonly JsonNode ApproveArguments = Schema("""
        {"type":"object","properties":{"tool_name":{"type":"string"},"input":{"type":"object"},"tool_use_id":{"type":"string"}},"required":["tool_name"]}
        """);

    private DiagnosticToolSet()
    {
    }

    /// <summary>The set.</summary>
    public static DiagnosticToolSet Instance { get; } = new();

    /// <inheritdoc />
    public string Name => SetName;

    /// <inheritdoc />
    public string Summary => "Craft's own diagnostics for an outside agent: reports, crash trail, Output, telemetry, problems, open_file, run_tests (approved), Claude Code's approvals";

    /// <inheritdoc />
    public IReadOnlyList<ITool> Live(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var tools = new DiagnosticTools(host);
        return
        [
            Tool.Create(DiagnosticToolNames.AppState, "Craft's version, the open folder, the open documents (path, language, unsaved, active) and the operations running now.", NoArguments, tools.AppStateAsync),
            Tool.Create(DiagnosticToolNames.Reports, "Craft's crash, callback (a caught failure) and stall reports, the newest first: kind, file name, time, size.", ReportsArguments, tools.ReportsAsync),
            Tool.Create(DiagnosticToolNames.ReadReport, "A report's text (by its file name, or latest), bounded.", ReadReportArguments, tools.ReadReportAsync),
            Tool.Create(DiagnosticToolNames.Trail, "The crash trail's last lines: the UI work, commands, key names, first-chance exceptions and Output, oldest first.", LinesArguments, tools.TrailAsync),
            Tool.Create(DiagnosticToolNames.Output, "Output's last lines (the app's and the plug-ins' log), oldest first.", LinesArguments, tools.OutputAsync),
            Tool.Create(DiagnosticToolNames.Telemetry, "The telemetry measures (source, measure, last, mean, p95, count, unit), the slowest first.", MaxArguments, tools.TelemetryAsync),
            Tool.Create(DiagnosticToolNames.Problems, "The open documents' errors and warnings: path:line:column: severity id: message.", MaxArguments, tools.ProblemsAsync),
            Tool.Create(DiagnosticToolNames.OpenFile, "Opens a file in the editor at a line for the user to see; it changes nothing (a change is propose_edit's diff).", OpenFileArguments, tools.OpenFileAsync),
            Tool.Create(DiagnosticToolNames.RunTests, "Runs Craft's tests whose names hold a CRAFT_FILTER (never the full suite) through the command sandbox: the user approves each command. Answers the failures.", RunTestsArguments, tools.RunTestsAsync),
            Tool.Create(DiagnosticToolNames.Approve, "Claude Code's permission prompt: asks the user in Craft whether a tool may run with this input; answers allow or deny as JSON.", ApproveArguments, tools.ApproveAsync),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Folder(string folder) => [];

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;
}

/// <summary>The diagnostic tools over one host: each hops to the UI thread for the window's state and reads files off it.</summary>
internal sealed class DiagnosticTools(IEditorHost host)
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private ClaudeCodeSettings Settings
    {
        get
        {
            try
            {
                return ClaudeCodeSettings.From(host.Settings);
            }
            catch (ArgumentException)
            {
                return new ClaudeCodeSettings();
            }
        }
    }

    private int Limit => Settings.ToolOutputCharacters;

    public async Task<string> AppStateAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var text = await OnUiAsync(() =>
        {
            var state = new StringBuilder();
            state.Append("Craft: ").Append(host.Diagnostics?.AppVersion ?? "version not known").Append('\n');
            state.Append("Open folder: ").Append(host.WorkspaceFolder ?? "none").Append('\n');
            state.Append(Culture, $"Open documents ({host.Documents.Count}):\n");
            foreach (var document in host.Documents)
            {
                state.Append("  ").Append(document.FilePath ?? "untitled").Append(" · ").Append(document.LanguageName)
                    .Append(document.Document.IsDirty ? " · unsaved" : "").Append(ReferenceEquals(document, host.ActiveDocument) ? " · active" : "").Append('\n');
            }

            var running = host.Activities.Running;
            state.Append(Culture, $"Running now ({running.Count}):\n");
            foreach (var activity in running)
            {
                state.Append("  ").Append(activity.Title).Append(" · ").Append(activity.Kind)
                    .Append(activity.Fraction is { } f ? string.Create(Culture, $" · {f * 100:0}%") : "")
                    .Append(activity.Text is { Length: > 0 } now ? " · " + now : "")
                    .Append(string.Create(Culture, $" · {activity.Elapsed.TotalSeconds:0} s")).Append('\n');
            }

            return state.ToString();
        }, cancellationToken).ConfigureAwait(false);
        return ClaudeStream.Cut(text, Limit);
    }

    public async Task<string> ReportsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string? kind = Kind(arguments);
        int max = Whole(arguments, "max", 20, 1, 500);
        var diagnostics = host.Diagnostics;
        if (diagnostics is null)
        {
            return "This host keeps no reports.";
        }

        var reports = await Task.Run(() => diagnostics.Reports(kind, max), cancellationToken).ConfigureAwait(false);
        if (reports.Count == 0)
        {
            return $"No {kind ?? "crash, callback or stall"} report in {diagnostics.ReportFolder ?? "the crash folder"}.";
        }

        var text = new StringBuilder().Append(Culture, $"{reports.Count} reports in {diagnostics.ReportFolder}, the newest first:\n");
        foreach (var report in reports)
        {
            text.Append(report.Line).Append('\n');
        }

        return ClaudeStream.Cut(text.ToString(), Limit);
    }

    public async Task<string> ReadReportAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string name = Text(arguments, "name");
        string? kind = Kind(arguments);
        int max = Math.Min(Limit, Whole(arguments, "max_characters", Limit, 100, int.MaxValue));
        var diagnostics = host.Diagnostics;
        if (diagnostics is null)
        {
            return "This host keeps no reports.";
        }

        return await Task.Run(() =>
        {
            var reports = diagnostics.Reports(kind, 1000);
            var report = name.Length == 0 || name.Equals("latest", StringComparison.OrdinalIgnoreCase)
                ? (reports.Count > 0 ? reports[0] : null)
                : reports.FirstOrDefault(r => r.Name.Equals(Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
            if (report is null)
            {
                return $"No report named {name}{(kind is null ? "" : " of kind " + kind)} in {diagnostics.ReportFolder}: list them with {DiagnosticToolNames.Reports}.";
            }

            string text;
            try
            {
                text = File.ReadAllText(report.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return $"{report.Name} could not be read: {error.Message}";
            }

            return $"{report.Line}\n\n" + ClaudeStream.Cut(text, max);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> TrailAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        int lines = Whole(arguments, "lines", 200, 1, 100_000);
        if (host.Diagnostics is not { } diagnostics)
        {
            return "This host keeps no crash trail.";
        }

        var found = await Task.Run(() => diagnostics.TrailLines(lines), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? "The crash trail is empty, or off (CRAFT_CRASH_TRAIL)." : Tail(found, Limit);
    }

    public async Task<string> OutputAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        int lines = Whole(arguments, "lines", 200, 1, 100_000);
        var found = await OnUiAsync(() => host.Diagnostics?.OutputLines(lines) ?? [], cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? "Output is empty." : Tail(found, Limit);
    }

    public async Task<string> TelemetryAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        int max = Whole(arguments, "max", 50, 1, 10_000);
        var measures = await OnUiAsync(() => host.Diagnostics?.Measures(max) ?? [], cancellationToken).ConfigureAwait(false);
        if (measures.Count == 0)
        {
            return "Nothing was recorded yet.";
        }

        var text = new StringBuilder("source · measure · last · mean · p95 · count · unit\n");
        foreach (var m in measures)
        {
            text.Append(Culture, $"{m.Source} · {m.Name} · {m.Last:0.###} · {m.Mean:0.###} · {m.P95:0.###} · {m.Count} · {m.Unit}\n");
        }

        return ClaudeStream.Cut(text.ToString(), Limit);
    }

    public async Task<string> ProblemsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        int max = Whole(arguments, "max", 200, 1, 100_000);
        var text = await OnUiAsync(() =>
        {
            var lines = new StringBuilder();
            int count = 0;
            foreach (var document in host.Documents)
            {
                var diagnostics = document.Diagnostics;
                var snapshot = diagnostics.Snapshot ?? document.Document.Buffer.Current;
                foreach (var problem in diagnostics)
                {
                    if (problem.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning) || count >= max)
                    {
                        continue;
                    }

                    int start = Math.Clamp(problem.Start, 0, snapshot.Length);
                    int line = snapshot.GetLineIndex(start);
                    int column = start - snapshot.GetLine(line).Start;
                    lines.Append(Culture, $"{document.FilePath ?? "untitled"}:{line + 1}:{column + 1}: {problem.Severity.ToString().ToLowerInvariant()} {problem.Id}: {problem.Message}\n");
                    count++;
                }
            }

            return count == 0 ? "No errors or warnings in the open documents." : lines.ToString();
        }, cancellationToken).ConfigureAwait(false);
        return ClaudeStream.Cut(text, Limit);
    }

    public async Task<string> OpenFileAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string path = Text(arguments, "path");
        if (path.Length == 0)
        {
            return "Not opened: give the file's path.";
        }

        int line = Whole(arguments, "line", 1, 1, int.MaxValue);
        int column = Whole(arguments, "column", 1, 1, int.MaxValue);
        string? root = await OnUiAsync(() => host.WorkspaceFolder, cancellationToken).ConfigureAwait(false);
        string full = Path.IsPathRooted(path) || root is null ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
        if (!await Task.Run(() => File.Exists(full), cancellationToken).ConfigureAwait(false))
        {
            return $"Not opened: {full} does not exist.";
        }

        return await OnUiAsync(async () =>
        {
            if (await host.OpenDocumentAsync(full, cancellationToken).ConfigureAwait(true) is not { } document)
            {
                return $"The editor could not open {full}.";
            }

            var snapshot = document.Document.Buffer.Current;
            int index = Math.Clamp(line - 1, 0, Math.Max(0, snapshot.LineCount - 1));
            var at = snapshot.GetLine(index);
            int position = Math.Min(at.Start + column - 1, at.End);
            host.Select(document, new SelectionState(position, position));
            return string.Create(Culture, $"Opened {full} at line {index + 1} for the user (nothing was changed).");
        }, cancellationToken).Unwrap().ConfigureAwait(false);
    }

    /// <summary>
    /// Why <paramref name="filter"/> is refused, or null when it may run: empty, a wildcard, or a part shorter than three
    /// characters (which matches nearly every test: the full suite is the owner's alone).
    /// </summary>
    public static string? Refusal(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "Refused: run_tests takes a CRAFT_FILTER (test groups or names separated by '|'); the full suite is never run.";
        }

        foreach (var part in parts)
        {
            if (part.Length < 3 || part.Contains('*', StringComparison.Ordinal) || part.Contains('?', StringComparison.Ordinal))
            {
                return $"Refused: '{part}' would match nearly every test (each part at least 3 characters, no wildcards); the full suite is never run.";
            }
        }

        return null;
    }

    public async Task<string> RunTestsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string filter = Text(arguments, "filter");
        if (Refusal(filter) is { } refusal)
        {
            return refusal;
        }

        var settings = Settings;
        if (settings.CraftRepositoryFrom(AppContext.BaseDirectory) is not { } repository)
        {
            return "Craft's repository was not found above the app's folder: set CRAFT_PLUGIN_CLAUDECODE_CRAFTREPOSITORY.";
        }

        if (CommandSandboxes.Find(settings.TestSandbox) is not { } sandbox)
        {
            return $"No command sandbox '{settings.TestSandbox}' is registered (registered: {string.Join(", ", CommandSandboxes.Names)}): set CRAFT_PLUGIN_CLAUDECODE_TESTSANDBOX.";
        }

        var limits = new CommandLimits
        {
            Time = TimeSpan.FromMinutes(settings.TestMinutes),
            MaxOutputCharacters = Limit,
            Environment = new Dictionary<string, string> { ["CRAFT_FILTER"] = filter },
            Root = repository,
            Requester = "Claude Code",
            Purpose = $"Craft's tests matching {filter}",
        };
        var report = new StringBuilder();
        if (arguments["build"]?.GetValueKind() == JsonValueKind.True)
        {
            var built = await sandbox.RunAsync(["dotnet", "build", "-c", "Release", Path.Combine("tests", "Craft.Tests")], repository, limits with { Purpose = "build Craft's tests" }, cancellationToken).ConfigureAwait(false);
            report.Append(Says("build", built));
            if (!built.Succeeded)
            {
                return ClaudeStream.Cut(report.ToString(), Limit);
            }
        }

        var ran = await sandbox.RunAsync(
            ["dotnet", "run", "-c", "Release", "--no-build", "--project", Path.Combine("tests", "Craft.Tests"), "--", "--failures-only", "--failures-file", Path.Combine("test-output", "fails.txt")],
            repository, limits, cancellationToken).ConfigureAwait(false);
        report.Append(Says($"tests (CRAFT_FILTER={filter})", ran));
        return ClaudeStream.Cut(report.ToString(), Limit);
    }

    public async Task<string> ApproveAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string tool = Text(arguments, "tool_name");
        var input = arguments["input"] as JsonObject ?? new JsonObject();
        string summary = Summarize(input);
        var session = ClaudeCodePlugin.Session;
        if (session is not null && session.IsAllowed(tool))
        {
            return Allow(input);
        }

        var question = new EditorQuestion($"Claude Code wants to use {tool}", summary, ["Allow once", $"Allow {tool} for this run", "Deny"])
        {
            Details = [string.Create(Culture, $"Tool: {tool}"), "Where: on this machine, in the agent's copy of the folder"],
            Dismissed = 2,
            Kind = EditorQuestionKinds.Command,
        };
        int answer = await OnUiAsync(() => host.AskAsync(question, cancellationToken), cancellationToken).Unwrap().ConfigureAwait(false);
        if (answer == 1)
        {
            session?.Allow(tool);
        }

        return answer is 0 or 1 ? Allow(input) : Deny("The user denied it in Craft.");
    }

    /// <summary>The permission prompt's answer that lets the tool run with its input unchanged.</summary>
    public static string Allow(JsonObject input) => new JsonObject { ["behavior"] = "allow", ["updatedInput"] = input.DeepClone() }.ToJsonString();

    /// <summary>The permission prompt's answer that refuses it, saying why.</summary>
    public static string Deny(string message) => new JsonObject { ["behavior"] = "deny", ["message"] = message }.ToJsonString();

    private static string Summarize(JsonObject input)
    {
        foreach (var key in (ReadOnlySpan<string>)["command", "file_path", "url", "pattern"])
        {
            if (input[key]?.ToString() is { Length: > 0 } value)
            {
                return ClaudeStream.Cut(value, 400);
            }
        }

        return ClaudeStream.Cut(input.ToJsonString(), 400);
    }

    private static string Says(string what, CommandResult result) =>
        result.Refused ? $"{what}: not run: {result.Output}\n"
        : string.Create(Culture, $"{what}: {(result.TimedOut ? "stopped at its time limit" : $"exit code {result.ExitCode}")} after {result.Duration.TotalSeconds:0} s\n{result.Output}\n");

    // The last lines, as many as the bound takes.
    private static string Tail(IReadOnlyList<string> lines, int max)
    {
        var kept = new List<string>();
        int total = 0;
        for (int i = lines.Count - 1; i >= 0 && total + lines[i].Length + 1 <= max; i--)
        {
            kept.Add(lines[i]);
            total += lines[i].Length + 1;
        }

        kept.Reverse();
        string cut = kept.Count < lines.Count ? string.Create(Culture, $"({lines.Count - kept.Count} earlier lines left out)\n") : "";
        return cut + string.Join('\n', kept);
    }

    private static string? Kind(JsonObject arguments) =>
        Text(arguments, "kind") is { Length: > 0 } kind && DiagnosticReportKinds.All.Contains(kind, StringComparer.OrdinalIgnoreCase) ? kind.ToLowerInvariant() : null;

    private static string Text(JsonObject arguments, string name) => arguments[name]?.GetValueKind() == JsonValueKind.String ? arguments[name]!.GetValue<string>().Trim() : "";

    private static int Whole(JsonObject arguments, string name, int fallback, int min, int max) =>
        arguments[name] is JsonValue value && value.TryGetValue(out int number) ? Math.Clamp(number, min, max) : fallback;

    private Task<T> OnUiAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Dispatch("claude code: a tool reads the window", () =>
        {
            try
            {
                done.TrySetResult(work());
            }
#pragma warning disable CA1031 // The tool's caller gets the failure (the registry reports it to the model).
            catch (Exception error)
#pragma warning restore CA1031
            {
                done.TrySetException(error);
            }
        });
        return done.Task.WaitAsync(cancellationToken);
    }
}
