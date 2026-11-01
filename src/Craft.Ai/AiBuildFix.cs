// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// "ai" in <see cref="BuildFixProviders"/> (X part 11 item 5, the Run view's and the Problems rows' "Fix with AI"): one
/// error goes to the chat's model as the code action Fix does (its lines rewritten, a diff the user accepts); several go
/// to the agent as one task with every error and <c>buildFixContextLines</c> of code around each, run on its working copy,
/// its changes then proposed file by file as diffs. Pressing Fix with AI is the user's asking, so the model loads then
/// if it is not loaded (note 23), with its progress in the status bar. Nothing changes without the user's accept.
/// </summary>
public sealed class AiBuildFix(IEditorHost host, Func<AiSettings> settings, AiCodeActions actions, AiAgentPanel agent) : IBuildFixProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "the AI plug-in: one error to the chat's model, several to the agent; the fix a diff you accept";

    /// <inheritdoc />
    public bool Offers(BuildFixRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = settings();
        if (!now.BuildFix || request.Problems.Count == 0)
        {
            return false;
        }

        return Errors(request).Count == 1 ? now.ChatEnabled && now.ChatRole.Model.Length > 0 : now.AgentEnabled && now.AgentRole.Model.Length > 0;
    }

    /// <inheritdoc />
    public async Task<BuildFixResult> FixAsync(BuildFixRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = Errors(request);
        if (errors.Count == 0)
        {
            return new BuildFixResult(false, [], "There is no problem to fix.");
        }

        return errors.Count == 1
            ? await FixOneAsync(errors[0], cancellationToken).ConfigureAwait(true)
            : await FixManyAsync(request, errors, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// The agent's task for <paramref name="errors"/> of <paramref name="request"/>: the errors, each with the code around it
    /// (numbered lines, the open document's text when it is open), and what to change.
    /// </summary>
    public static string Task(BuildFixRequest request, IReadOnlyList<RunProblem> errors, Func<string, string?> textOf, int contextLines)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(textOf);
        var task = new StringBuilder();
        task.Append(CultureInfo.InvariantCulture, $"{request.Title} failed with {errors.Count} errors. Fix them in the project's files, changing only what they need, then stop.\n");
        foreach (var group in errors.GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Relative(request.WorkspaceFolder, group.Key);
            var lines = textOf(group.Key)?.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            foreach (var error in group)
            {
                task.Append(CultureInfo.InvariantCulture, $"\n{relative}({error.Line},{error.Column}): {SeverityWord(error.Severity)} {error.Code}: {error.Message}\n");
                if (lines is not null && lines.Length > 0)
                {
                    int first = Math.Max(1, error.Line - contextLines);
                    int last = Math.Min(lines.Length, error.Line + contextLines);
                    task.Append("```\n");
                    for (int line = first; line <= last; line++)
                    {
                        task.Append(CultureInfo.InvariantCulture, $"{line,5}| {lines[line - 1]}\n");
                    }

                    task.Append("```\n");
                }
            }
        }

        return task.ToString();
    }

    // The errors (the warnings only when there is no error), each place once.
    private static List<RunProblem> Errors(BuildFixRequest request)
    {
        var errors = request.Problems.Where(p => p.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count == 0)
        {
            errors = [.. request.Problems];
        }

        return [.. errors.DistinctBy(p => (p.File.ToUpperInvariant(), p.Line, p.Column, p.Code))];
    }

    private async Task<BuildFixResult> FixOneAsync(RunProblem error, CancellationToken cancellationToken)
    {
        if (await host.OpenDocumentAsync(error.File, cancellationToken).ConfigureAwait(true) is not { } document)
        {
            return new BuildFixResult(false, [], $"{Path.GetFileName(error.File)} could not be opened to fix it.");
        }

        var snapshot = document.Document.Buffer.Current;
        var line = snapshot.GetLine(Math.Clamp(error.Line - 1, 0, Math.Max(0, snapshot.LineCount - 1)));
        int start = line.Start + Math.Clamp(error.Column - 1, 0, line.Length);
        var diagnostic = new EditorDiagnostic(start, Math.Max(0, line.End - start), error.Severity, error.Code, error.Message);
        bool accepted = await actions.FixProblemAsync(document, diagnostic, cancellationToken).ConfigureAwait(true);
        return accepted
            ? new BuildFixResult(true, [document], $"The fix of {error.Code} in {Path.GetFileName(error.File)} was accepted.")
            : new BuildFixResult(false, [], "The fix was not accepted.");
    }

    private async Task<BuildFixResult> FixManyAsync(BuildFixRequest request, IReadOnlyList<RunProblem> errors, CancellationToken cancellationToken)
    {
        if (agent.IsBusy)
        {
            return new BuildFixResult(false, [], "The agent is running: stop it or wait for it, then press Fix with AI again.");
        }

        // The code around each error: the open document's text, else the file read off the UI thread.
        var open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in host.Documents)
        {
            if (document.FilePath is { } path)
            {
                var snapshot = document.Document.Buffer.Current;
                open[Path.GetFullPath(path)] = snapshot.GetText(0, snapshot.Length);
            }
        }

        int context = settings().BuildFixContextLines;
        string task = await System.Threading.Tasks.Task.Run(() => Task(request, errors, path =>
        {
            string full = Path.GetFullPath(path);
            if (open.TryGetValue(full, out var text))
            {
                return text;
            }

            try
            {
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }, context), cancellationToken).ConfigureAwait(true);
        host.ShowPanel(AiAgentPanel.PanelId);
        await agent.SubmitAsync(task, cancellationToken).ConfigureAwait(true);
        await agent.Run.WaitAsync(cancellationToken).ConfigureAwait(true);
        var changed = new List<IEditorDocument>();
        int offered = 0;
        foreach (var change in agent.Changes)
        {
            offered++;
            if (await agent.ReviewAsync(change, cancellationToken).ConfigureAwait(true)
                && host.Documents.FirstOrDefault(d => d.FilePath is { } path && SamePath(path, change.Path)) is { } document)
            {
                changed.Add(document);
            }
        }

        return offered == 0
            ? new BuildFixResult(false, [], "The agent changed no file.")
            : changed.Count == 0
                ? new BuildFixResult(false, [], "No change of the agent's was accepted.")
                : new BuildFixResult(true, changed, string.Create(CultureInfo.InvariantCulture, $"{changed.Count} of {offered} {(offered == 1 ? "file" : "files")} the agent changed accepted."));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string SeverityWord(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "info",
    };
}
