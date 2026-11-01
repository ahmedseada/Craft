// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

// X part 11 item 4: "Review changes". The staged diff (else every change, said so), each line numbered, trimmed to the
// budget by file, reviewed by the first registered code review provider (the AI plug-in: a model already loaded; the
// plug-in never names one); the findings stream into the view, onto the open files as notes in the gutter (a click shows
// the finding's card: Apply fix, Dismiss, Copy all) and under their lines in the diff view. Apply fix runs the registered
// code actions' Fix on the finding's line: a diff the user accepts. Stop keeps what was found.
public sealed partial class GitSession
{
    /// <summary>The source name of the review's notes in the gutter.</summary>
    public const string ReviewMarkSource = "review";

    private readonly HashSet<IEditorDocument> _reviewMarked = [];
    private CancellationTokenSource? _reviewing;
    private int _findingIds;
    private int _reviewGeneration;

    /// <summary>The findings of the last review now (dismissed and fixed ones left out).</summary>
    public IReadOnlyList<ReviewItem> Findings => _state.Findings;

    /// <summary>The notes the gutter shows for <paramref name="document"/> from the review (for the tests).</summary>
    public IReadOnlyList<LineMark> ReviewMarksOf(IEditorDocument document) => document.FilePath is { } path
        ? [.. _state.Findings.Where(f => f.Finding.Line > 0 && SamePath(f.FullPath, path)).Select(f => f.Finding.Line).Distinct().Order().Select(line => new LineMark(line - 1, 1, LineMarkKind.Note))]
        : [];

    /// <summary>Reviews the changes (the view's Review button, the palette's "Source Control: Review changes").</summary>
    public async Task ReviewAsync()
    {
        if (_repository is not { } repository || _state.Reviewing)
        {
            return;
        }

        if (CodeReviewProviders.First is not { } provider)
        {
            Update(_state with { ReviewStatus = "Reviewing needs the AI plug-in (Craft.Ai), which is not loaded.", ReviewTone = PanelTone.Warning, ReviewLoadCommand = null });
            return;
        }

        var plan = provider.Plan();
        if (plan.Model is null)
        {
            Update(_state with { ReviewStatus = plan.Why ?? "No model is loaded.", ReviewTone = PanelTone.Warning, ReviewLoadCommand = plan.LoadCommand });
            return;
        }

        ClearFindingMarks();
        var settings = Settings;
        var reviewing = _reviewing = new CancellationTokenSource();
        int generation = ++_reviewGeneration;
        var activity = _host.Activities.Start($"Reviewing the changes with {plan.Model}", ActivityKinds.Generation, cancellable: true, reviewing.Token);
        var token = activity.CancellationToken;
        Update(_state with
        {
            Reviewing = true, Reviewed = true, Findings = [], ReviewLoadCommand = null, ReviewTone = PanelTone.Normal,
            ReviewStatus = $"Reviewing with {plan.Model} ({plan.Role})…",
        });
        var pending = new List<ReviewFinding>();
        var pendingGate = new Lock();
        bool scheduled = false;
        string outcome = "";
        var tone = PanelTone.Normal;
        bool allChanges = false;
        IReadOnlyList<string> summarised = [];
        try
        {
            (byte[] diffBytes, allChanges) = await repository.CommitDiffAsync(token).ConfigureAwait(true);
            if (diffBytes.Length == 0)
            {
                outcome = "There are no changes to review.";
                tone = PanelTone.Warning;
                activity.Complete(outcome);
                return;
            }

            int budget = settings.ReviewDiffTokens;
            if (plan.ContextTokens > 0)
            {
                // The model's context holds the instructions, the changes and the reply.
                budget = Math.Max(256, Math.Min(budget, plan.ContextTokens - Math.Max(256, plan.ReplyTokens) - 1024));
            }

            var (trimmed, names) = await Task.Run(() => CodeReviews.Prepare(diffBytes, budget), token).ConfigureAwait(true);
            summarised = names;
            var request = new CodeReviewRequest(trimmed.Text) { AllChanges = allChanges, Summarised = names };
            await Task.Run(async () =>
            {
                await foreach (var finding in provider.ReviewAsync(request, token).ConfigureAwait(false))
                {
                    bool schedule;
                    lock (pendingGate)
                    {
                        pending.Add(finding);
                        schedule = !scheduled;
                        scheduled = true;
                    }

                    if (schedule)
                    {
                        // The findings since the last UI turn go in one dispatch (never one redraw per finding).
                        _host.Dispatch("git: the review's findings", () =>
                        {
                            List<ReviewFinding> batch;
                            lock (pendingGate)
                            {
                                batch = [.. pending];
                                pending.Clear();
                                scheduled = false;
                            }

                            AddFindings(generation, batch);
                        });
                    }
                }
            }, token).ConfigureAwait(true);
            FlushFindings(generation, pending, pendingGate);
            outcome = Outcome(plan, allChanges, summarised, stopped: false);
            activity.Complete(outcome);
        }
        catch (OperationCanceledException)
        {
            FlushFindings(generation, pending, pendingGate);
            outcome = Outcome(plan, allChanges, summarised, stopped: true);
            activity.Dispose();
        }
        catch (Exception error) when (error is InvalidOperationException or GitException or IOException or NotSupportedException)
        {
            FlushFindings(generation, pending, pendingGate);
            outcome = error.Message;
            tone = PanelTone.Error;
            activity.Fail(error.Message, error);
        }
        finally
        {
            if (ReferenceEquals(_reviewing, reviewing))
            {
                _reviewing = null;
            }

            reviewing.Dispose();
            if (!_disposed)
            {
                Update(_state with { Reviewing = false, ReviewStatus = outcome, ReviewTone = tone });
            }
        }
    }

    // What the line under the findings says once the review ended.
    private string Outcome(CodeReviewPlan plan, bool allChanges, IReadOnlyList<string> summarised, bool stopped)
    {
        int count = _state.Findings.Count;
        string found = count == 0 ? "no findings" : count == 1 ? "1 finding" : string.Create(CultureInfo.InvariantCulture, $"{count} findings");
        string skipped = summarised.Count == 0 ? ""
            : string.Create(CultureInfo.InvariantCulture, $"; {summarised.Count} large {(summarised.Count == 1 ? "file was" : "files were")} not reviewed (reviewDiffTokens)");
        return stopped
            ? $"Stopped: {found} so far, kept{skipped}."
            : $"Reviewed by {plan.Model} ({plan.Role}){(allChanges ? " from every change (nothing is staged)" : " from the staged changes")}: {found}{skipped}.";
    }

    private void FlushFindings(int generation, List<ReviewFinding> pending, Lock gate)
    {
        List<ReviewFinding> rest;
        lock (gate)
        {
            rest = [.. pending];
            pending.Clear();
        }

        AddFindings(generation, rest);
    }

    private void AddFindings(int generation, List<ReviewFinding> batch)
    {
        if (_disposed || generation != _reviewGeneration || batch.Count == 0 || _repository is not { } repository)
        {
            return;
        }

        var added = new List<ReviewItem>(batch.Count);
        foreach (var finding in batch)
        {
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(repository.Root, finding.Path));
            }
            catch (ArgumentException)
            {
                full = finding.Path;
            }

            added.Add(new ReviewItem("r" + (++_findingIds).ToString(CultureInfo.InvariantCulture), finding, full));
        }

        Update(_state with { Findings = [.. _state.Findings, .. added] });
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } path && added.Any(a => SamePath(a.FullPath, path)))
            {
                MarkFindings(document);
            }
        }
    }

    // The findings of an open document as notes in its gutter (none: the notes go).
    private void MarkFindings(IEditorDocument document)
    {
        var marks = ReviewMarksOf(document);
        if (marks.Count == 0 && !_reviewMarked.Contains(document))
        {
            return;
        }

        if (_host.SetLineMarks(document, ReviewMarkSource, marks))
        {
            if (marks.Count > 0)
            {
                _reviewMarked.Add(document);
            }
            else
            {
                _reviewMarked.Remove(document);
            }
        }
    }

    private void ClearFindingMarks()
    {
        foreach (var document in _reviewMarked.ToList())
        {
            _host.SetLineMarks(document, ReviewMarkSource, []);
        }

        _reviewMarked.Clear();
    }

    // The card of the findings on a line (0-based) of a document: what they say, Apply fix, Dismiss, Copy all.
    private void ShowFindingCard(IEditorDocument document, int line)
    {
        if (document.FilePath is not { } path)
        {
            return;
        }

        var items = _state.Findings.Where(f => f.Finding.Line == line + 1 && SamePath(f.FullPath, path)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        string first = items[0].Id;
        _host.ShowInlineCard(document, new InlineCard(line, CodeReviews.CardText(items),
        [
            new InlineCardAction("Apply fix", () => _ = ApplyFixAsync(first, CancellationToken.None)),
            new InlineCardAction("Dismiss", () =>
            {
                foreach (var item in items)
                {
                    DismissFinding(item.Id);
                }
            }),
            new InlineCardAction("Copy all", CopyFindings),
        ]));
    }

    private async Task OpenFindingAsync(string id)
    {
        if (_state.Findings.FirstOrDefault(f => f.Id == id) is not { } item)
        {
            return;
        }

        if (await _host.OpenDocumentAsync(item.FullPath).ConfigureAwait(true) is not { } document)
        {
            Update(_state with { ReviewStatus = $"{item.Finding.Path} could not be opened.", ReviewTone = PanelTone.Warning });
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        int line = Math.Clamp(item.Finding.Line - 1, 0, Math.Max(0, snapshot.LineCount - 1));
        _host.Select(document, SelectionState.At(snapshot.GetLine(line).Start));
        MarkFindings(document);
        ShowFindingCard(document, line);
    }

    /// <summary>
    /// Apply fix: the registered code actions' Fix on the finding's line (the AI plug-in's: the lines rewritten by its
    /// model, a diff the user accepts); accepted, the finding goes. Nothing changes without the user.
    /// </summary>
    private async Task ApplyFixAsync(string id, CancellationToken cancellationToken)
    {
        if (_state.Findings.FirstOrDefault(f => f.Id == id) is not { } item)
        {
            return;
        }

        if (await _host.OpenDocumentAsync(item.FullPath, cancellationToken).ConfigureAwait(true) is not { } document)
        {
            Update(_state with { ReviewStatus = $"{item.Finding.Path} could not be opened to fix it.", ReviewTone = PanelTone.Warning });
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        var line = snapshot.GetLine(Math.Clamp(item.Finding.Line - 1, 0, Math.Max(0, snapshot.LineCount - 1)));
        var finding = item.Finding;
        string message = finding.Reason.Length > 0 ? $"{finding.Message} ({finding.Reason})" : finding.Message;
        var diagnostic = new EditorDiagnostic(line.Start, line.Length, finding.Severity == DiagnosticSeverity.Info ? DiagnosticSeverity.Warning : finding.Severity, "review", message);
        var context = new CodeActionContext(document, line.Start, line.End, [diagnostic]);
        var fix = CodeActionProviders.ActionsFor(context, (name, error) => _host.Log(EditorLogLevel.Warning, $"The code actions of {name} failed.", error))
            .FirstOrDefault(a => a.Kind == CodeActionKinds.Fix);
        if (fix is null)
        {
            Update(_state with { ReviewStatus = "No plug-in offers a fix for it (the AI plug-in's code actions).", ReviewTone = PanelTone.Warning });
            return;
        }

        var before = document.Document.Buffer.Current;
        try
        {
            await fix.RunAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _host.Log(EditorLogLevel.Error, $"The fix of '{finding.Message}' failed.", error);
            Update(_state with { ReviewStatus = $"The fix failed: {error.Message}", ReviewTone = PanelTone.Error });
            return;
        }

        if (!ReferenceEquals(before, document.Document.Buffer.Current))
        {
            DismissFinding(id);
            Update(_state with { ReviewStatus = $"Fixed: {finding.Message}.", ReviewTone = PanelTone.Normal });
        }
    }

    private void DismissFinding(string id)
    {
        if (_state.Findings.FirstOrDefault(f => f.Id == id) is not { } item)
        {
            return;
        }

        Update(_state with { Findings = [.. _state.Findings.Where(f => f.Id != id)] });
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } path && SamePath(item.FullPath, path))
            {
                MarkFindings(document);
            }
        }
    }

    private void CopyFindings()
    {
        if (_state.Findings.Count == 0)
        {
            return;
        }

        string text = CodeReviews.CopyText(_state.Findings);
        Update(_host.CopyText(text)
            ? _state with { ReviewStatus = string.Create(CultureInfo.InvariantCulture, $"Copied {_state.Findings.Count} {(_state.Findings.Count == 1 ? "finding" : "findings")}."), ReviewTone = PanelTone.Normal }
            : _state with { ReviewStatus = "This editor reaches no clipboard here.", ReviewTone = PanelTone.Warning });
    }

    private void ClearFindings()
    {
        _reviewing?.Cancel();
        _reviewGeneration++;
        ClearFindingMarks();
        Update(_state with { Findings = [], Reviewed = false, ReviewStatus = null, ReviewLoadCommand = null });
    }

    // The findings of a file as the diff view's notes under their lines.
    private List<DiffNote> NotesOf(string path) =>
        [.. _state.Findings.Where(f => SamePath(f.FullPath, path)).OrderBy(f => f.Finding.Line).Select(f => new DiffNote(f.Finding.Line, f.Finding.Severity, f.Finding.Message, f.Finding.Reason))];
}
