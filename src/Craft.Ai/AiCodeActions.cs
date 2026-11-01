// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// The AI plug-in's code actions ("ai", plan 1 phase A2): for a diagnostic, Fix (the model rewrites the lines it covers)
/// and Explain; for a selection, Explain, Document (doc comments), Refactor and Write tests. An explanation is a chat turn
/// in the chat panel; Fix, Document and Refactor ask the chat model once, without tools, for the replacement of the
/// lines in one fenced block, and propose it through <see cref="IEditorHost.ProposeAsync"/> (the diff view; the language's
/// errors after the change shown before the user accepts; one undo step); Write tests answers in the chat, its code with
/// "Apply as diff". Nothing changes without the user.
/// </summary>
public sealed class AiCodeActions : ICodeActionProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    private const string RewritePrompt = "You rewrite code inside a code editor. Reply with the complete replacement of the lines you are given in one fenced code block, "
                                         + "keeping their indentation, and nothing else after the block.";

    private readonly IEditorHost _host;
    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly AiChatPanel _chat;

    /// <summary>Code actions in <paramref name="host"/>, answering in <paramref name="chat"/>.</summary>
    public AiCodeActions(IEditorHost host, Func<AiSettings> settings, AiModels models, AiChatPanel chat)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "Fix and explain a problem; explain, document, refactor or write tests for a selection, with the AI plug-in's model";

    /// <summary>The last rewrite's outcome (the tests' hook): whether the user accepted it.</summary>
    public bool? LastAccepted { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actions = new List<CodeAction>(6);
        if (context.Diagnostics.Count > 0)
        {
            var diagnostic = context.Diagnostics[0];
            actions.Add(new CodeAction("ai.fix", "Fix with the model", CodeActionKinds.Fix, token => FixAsync(context.Document, diagnostic, token)));
            actions.Add(new CodeAction("ai.explainProblem", "Explain", CodeActionKinds.Explain, token => ExplainProblemAsync(context.Document, diagnostic, token)));
        }

        if (context.End > context.Start)
        {
            int start = context.Start, end = context.End;
            actions.Add(new CodeAction("ai.explain", "Explain the selection", CodeActionKinds.Explain, token => ExplainAsync(context.Document, start, end, token)));
            actions.Add(new CodeAction("ai.document", "Add doc comments", CodeActionKinds.Document,
                token => RewriteAsync(context.Document, start, end, "Add documentation comments, in the language's own style, to every declaration in these lines; change nothing else.", "Add doc comments", token)));
            actions.Add(new CodeAction("ai.refactor", "Refactor the selection", CodeActionKinds.Refactor,
                token => RewriteAsync(context.Document, start, end, "Refactor these lines for clarity, keeping what they do exactly the same.", "Refactor", token)));
            actions.Add(new CodeAction("ai.tests", "Write tests", CodeActionKinds.Tests, token => TestsAsync(context.Document, start, end, token)));
        }

        return actions;
    }

    /// <summary>The context at the active document's selection: its range, or the caret's line, and the diagnostics there.</summary>
    public static CodeActionContext? AtSelection(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.ActiveDocument is not { } document)
        {
            return null;
        }

        var snapshot = document.Document.Buffer.Current;
        var selection = document.Selection;
        int start = Math.Clamp(selection.Start, 0, snapshot.Length);
        int end = Math.Clamp(selection.End, start, snapshot.Length);
        var line = snapshot.GetLine(snapshot.GetLineIndex(start));
        int from = selection.IsEmpty ? line.Start : start;
        int to = selection.IsEmpty ? line.End : end;
        var set = document.Diagnostics;
        var found = new List<EditorDiagnostic>();
        if (ReferenceEquals(set.Snapshot, snapshot) || set.Snapshot is null)
        {
            foreach (var d in set.Items)
            {
                if (d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && d.Start <= to && d.End >= from)
                {
                    found.Add(d);
                }
            }
        }

        return new CodeActionContext(document, start, end, found);
    }

    /// <summary>Runs the action of <paramref name="kind"/> at the active selection; false when there is none.</summary>
    public async Task<bool> RunAsync(string kind, CancellationToken cancellationToken)
    {
        if (AtSelection(_host) is not { } context)
        {
            return false;
        }

        var action = ActionsFor(context).FirstOrDefault(a => a.Kind == kind && (kind != CodeActionKinds.Explain || context.End > context.Start || a.Id == "ai.explainProblem"));
        if (action is null)
        {
            return false;
        }

        await action.RunAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    private Task ExplainProblemAsync(IEditorDocument document, EditorDiagnostic diagnostic, CancellationToken token)
    {
        var (start, end) = Lines(document.Document.Buffer.Current, diagnostic.Start, diagnostic.End);
        string code = document.Document.Buffer.Current.GetText(start, end - start);
        return _chat.AskAsync($"Explain the {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.Message}", token,
            $"It is on these lines of {DocumentName(document)}:\n```\n{code}\n```");
    }

    private Task ExplainAsync(IEditorDocument document, int start, int end, CancellationToken token) =>
        _chat.AskAsync("Explain what the selected code does.", token, $"The code, from {DocumentName(document)}:\n```\n{document.Document.Buffer.Current.GetText(start, end - start)}\n```");

    private Task TestsAsync(IEditorDocument document, int start, int end, CancellationToken token) =>
        _chat.AskAsync("Write unit tests for the selected code, in the project's test style, in one fenced block.", token,
            $"The code, from {DocumentName(document)}:\n```\n{document.Document.Buffer.Current.GetText(start, end - start)}\n```");

    /// <summary>
    /// Fix for <paramref name="diagnostic"/> of <paramref name="document"/> (X part 11: a failed build's one error): its
    /// lines rewritten by the chat's model and proposed as a diff; true when the user accepted it.
    /// </summary>
    public async Task<bool> FixProblemAsync(IEditorDocument document, EditorDiagnostic diagnostic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await FixAsync(document, diagnostic, cancellationToken).ConfigureAwait(true);
        return LastAccepted == true;
    }

    private Task FixAsync(IEditorDocument document, EditorDiagnostic diagnostic, CancellationToken token)
    {
        var (start, end) = Lines(document.Document.Buffer.Current, diagnostic.Start, diagnostic.End);
        return RewriteAsync(document, start, end, $"Fix the {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.Message}", $"Fix {diagnostic.Id}", token);
    }

    // The lines of [start, end) rewritten by the chat model, proposed as a diff the user accepts.
    private async Task RewriteAsync(IEditorDocument document, int start, int end, string instruction, string title, CancellationToken token)
    {
        LastAccepted = null;
        var snapshot = document.Document.Buffer.Current;
        (start, end) = Lines(snapshot, start, end);
        string original = snapshot.GetText(start, end - start);
        var lineStart = snapshot.GetLineIndex(start);
        int first = Math.Max(0, lineStart - 15);
        int last = Math.Min(snapshot.LineCount - 1, snapshot.GetLineIndex(end) + 15);
        string around = snapshot.GetText(snapshot.GetLine(first).Start, snapshot.GetLine(last).EndIncludingBreak - snapshot.GetLine(first).Start);
        var settings = _settings();
        using var progress = _host.StartProgress($"AI: {title}");
        string reply;
        string model;
        try
        {
            (reply, model) = await Task.Run(async () =>
            {
                var lease = await _models.ChatAsync(token).ConfigureAwait(false);
                var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template.");
                var request = new ChatRequest(
                [
                    new ChatMessage("system", RewritePrompt),
                    new ChatMessage("user", string.Create(CultureInfo.InvariantCulture,
                        $"{instruction}\n\nThe file {DocumentName(document)} ({document.LanguageName}), lines {first + 1}-{last + 1} for context:\n```\n{around}\n```\n\nThe lines to rewrite ({lineStart + 1}-{snapshot.GetLineIndex(end) + 1}):\n```\n{original}\n```")),
                ], Options: new GenerationOptions { NumPredict = settings.ChatMaxTokens, NumCtx = lease.ContextLength, Temperature = settings.ChatTemperature });
                var final = await chat.ChatAsync(request, token).ConfigureAwait(false);
                return (final.Message?.Content ?? "", lease.Model);
            }, token).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A failure (a load that did not fit, an out-of-memory one saying what to do) is a notification, never a modal stop.
            _host.Log(EditorLogLevel.Error, $"AI: {title} failed.", error);
            _host.Notify($"AI: {title} failed", AiErrors.Describe(error, settings), NotificationSeverity.Error);
            return;
        }

        string replacement = FirstBlock(reply);
        if (original.EndsWith('\n') && !replacement.EndsWith('\n'))
        {
            replacement += "\n";
        }

        if (!ReferenceEquals(document.Document.Buffer.Current, snapshot))
        {
            await _host.ShowMessageAsync($"AI: {title}", "The document changed while the model answered; run it again.").ConfigureAwait(true);
            return;
        }

        var proposal = new ProposedEdits([new TextEdit(start, end - start, replacement)], title)
        {
            Placement = string.Create(CultureInfo.InvariantCulture, $"lines {lineStart + 1}-{snapshot.GetLineIndex(end) + 1}, rewritten whole"),
            Origin = $"Code action · {model}",
        };
        var edit = proposal.Edits[0];
        var answer = AiEvents.Listening ? AiChatPanel.AnswerOf(AiAnswerKinds.CodeAction, instruction + "\n\n" + around, model, document, snapshot, edit, _host.WorkspaceFolder) : null;
        if (answer is not null)
        {
            AiEvents.Answered(answer);
        }

        LastAccepted = await _host.ProposeAsync(document, proposal, token).ConfigureAwait(true);
        if (answer is not null)
        {
            AiEvents.Judged(AiChatPanel.VerdictOf(answer, LastAccepted == true, document, edit));
        }
    }

    /// <summary>The first fenced block's code in <paramref name="reply"/>, or the whole reply when it has none.</summary>
    public static string FirstBlock(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var lines = reply.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int open = Array.FindIndex(lines, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        if (open < 0)
        {
            return reply.Trim('\n');
        }

        var code = new StringBuilder();
        for (int i = open + 1; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            code.Append(lines[i]).Append('\n');
        }

        return code.ToString().TrimEnd('\n');
    }

    // Whole lines from start's line to end's (end at a line start keeps that line out), with the last line's break.
    private static (int Start, int End) Lines(ITextSnapshot snapshot, int start, int end)
    {
        start = Math.Clamp(start, 0, snapshot.Length);
        end = Math.Clamp(end, start, snapshot.Length);
        var first = snapshot.GetLine(snapshot.GetLineIndex(start));
        int lastIndex = snapshot.GetLineIndex(end);
        if (end > start && end == snapshot.GetLine(lastIndex).Start && lastIndex > 0)
        {
            lastIndex--;
        }

        return (first.Start, snapshot.GetLine(lastIndex).EndIncludingBreak);
    }

    private string DocumentName(IEditorDocument document) => document.FilePath is { } path
        ? _host.WorkspaceFolder is { } root ? Path.GetRelativePath(root, path) : Path.GetFileName(path)
        : "an untitled document";
}
