// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Plugins.Git;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 11 item 4, Review changes: the model's lines parsed into findings, the staged diff numbered and trimmed to the
/// budget by file, a real repository reviewed through the AI plug-in's "ai" provider with Idrak's scripted fake chat model
/// (findings in the view, as notes in the editor and under their lines in the diff view; Apply fix a diff the user
/// accepts, one undo step; Dismiss; Copy all), Stop keeping what was found, and no model loaded said with the load only on
/// the user's press. The real repository tests need git on the machine; without it they say so and end.
/// </summary>
[TestGroup("CommitReview")]
public static class CommitReviewTests
{
    [Test]
    public static void FindingsParseFromTheModelsLinesAndTheDiffIsNumberedAndTrimmedByFile()
    {
        Check.True(AiCodeReview.Parse("src/A.cs:12: warning: Null is not checked — it throws on empty input") is { Path: "src/A.cs", Line: 12, Severity: DiagnosticSeverity.Warning, Message: "Null is not checked", Reason: "it throws on empty input" },
            "the asked form");
        Check.True(AiCodeReview.Parse("- `src/B.cs:3:5` [error] Off by one | the last item is skipped.") is { Path: "src/B.cs", Line: 3, Severity: DiagnosticSeverity.Error, Message: "Off by one", Reason: "the last item is skipped." },
            "a list mark, quotes, a column, brackets and another separator");
        Check.True(AiCodeReview.Parse("1. src\\C.cs:7 - suggestion - Rename it") is { Path: "src/C.cs", Line: 7, Severity: DiagnosticSeverity.Info, Reason: "" }, "a numbered line, a suggestion, no reason");
        Check.True(AiCodeReview.Parse("No findings.") is null && AiCodeReview.Parse("The change looks fine: it reads well.") is null && AiCodeReview.Parse("") is null, "prose is no finding");

        string diff = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1,3 +1,4 @@\n one\n-two\n+two changed\n+new\n three\n";
        string numbered = CodeReviews.Number(Encoding.UTF8.GetBytes(diff));
        Check.True(numbered.Contains("     1| one\n", StringComparison.Ordinal) && numbered.Contains("-     | two\n", StringComparison.Ordinal)
                   && numbered.Contains("+    2| two changed\n", StringComparison.Ordinal) && numbered.Contains("+    3| new\n", StringComparison.Ordinal) && numbered.Contains("     4| three\n", StringComparison.Ordinal)
                   && numbered.Contains("+++ b/a.txt\n", StringComparison.Ordinal), $"each kept and added line numbered in the new text, the removed ones not ({numbered})");
        string big = "diff --git a/big.txt b/big.txt\n@@ -0,0 +1,400 @@\n" + string.Concat(Enumerable.Repeat("+a long added line of text\n", 400));
        var (trimmed, summarised) = CodeReviews.Prepare(Encoding.UTF8.GetBytes(diff + big), 300);
        Check.True(summarised is ["big.txt"] && trimmed.Text.Contains("+    2| two changed", StringComparison.Ordinal) && trimmed.Text.Contains("big.txt: +400", StringComparison.Ordinal),
            $"over the budget, the largest file is summarised by name and counts and named as not reviewed ({string.Join(",", summarised)})");
        var item = new ReviewItem("r1", new ReviewFinding("a.txt", 2, DiagnosticSeverity.Warning, "Changed", "callers expect two"), "/r/a.txt");
        Check.Equal("a.txt:2: warning: Changed — callers expect two\n", CodeReviews.CopyText([item]), "Copy all: a line each");
        Check.Equal("⚠ warning · Changed\n  callers expect two", CodeReviews.CardText([item]), "the card: severity, one line, the reason");
    }

    [Test]
    public static void ARealRepositorysStagedChangesAreReviewedIntoNotesAndApplyFixIsADiff()
    {
        if (SourceControlTests.Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        using var models = new LoadedChat(FakeChatModel.Script(FakeChatModel.Answer("a.txt:2: warning: The value changed — callers expect two\nNo more findings.\n")));
        var review = new AiCodeReview(() => models.Settings, models.Models);
        CodeReviewProviders.Register(review);
        var fixer = new FixingActions();
        CodeActionProviders.Register(fixer);
        try
        {
            models.Load();
            using var repo = new SourceControlTests.Repo();
            var session = repo.Session;
            fixer.Host = repo.Host;
            string copied = "";
            repo.Host.ClipboardWriter = text =>
            {
                copied = text;
                return true;
            };
            repo.Until(() => session.Repository?.Status.Changes.Count == 2, "the repository opens");
            repo.Act(SourceControlView.Stage, repo.File("a.txt"));
            repo.Until(() => session.State.Status.Staged.Count == 1, "a.txt staged");
            using var document = repo.Wait(TextDocument.OpenAsync(repo.File("a.txt")));
            var open = repo.Host.Open(document, null);
            var box = session.Panel.Blocks.OfType<PanelMessageBox>().Single();
            Check.True(box.Actions.Any(a => a is { ActionId: SourceControlView.Review, Enabled: true } && a.Tooltip.Contains("fake-7b", StringComparison.Ordinal)),
                "Review changes beside Generate, its tooltip naming the loaded model");

            repo.Act(SourceControlView.Review, null);
            repo.Until(() => !session.State.Reviewing && session.State.ReviewStatus?.StartsWith("Reviewed", StringComparison.Ordinal) == true, "the review ends");
            Check.True(session.Findings is [{ Finding: { Path: "a.txt", Line: 2, Severity: DiagnosticSeverity.Warning, Message: "The value changed", Reason: "callers expect two" } }],
                $"the model's line is one finding ({session.Findings.Count})");
            Check.True(session.State.ReviewStatus!.Contains("fake-7b (chat) from the staged changes: 1 finding", StringComparison.Ordinal), $"the line says who reviewed what ({session.State.ReviewStatus})");
            string asked = models.Chat.Requests.Single().Messages[^1].Content;
            Check.True(asked.Contains("+    2| two changed", StringComparison.Ordinal) && !asked.Contains("new file.txt", StringComparison.Ordinal), "the staged diff only, its lines numbered");
            var results = session.Panel.Blocks.OfType<PanelResults>().Single(r => r.Id == "review");
            Check.True(results.Groups is [{ Rows: [{ Text: "The value changed", Prefix: "L2", Badge: "warning" } row] }] && row.Buttons.Select(b => b.Title).SequenceEqual(["Apply fix", "Dismiss"]),
                "the view lists it by file with Apply fix and Dismiss");
            Check.True(session.Panel.Blocks.OfType<PanelButtons>().Any(b => b.Buttons.Any(x => x.Title == "Copy all")), "and Copy all");
            Check.SequenceEqual([new LineMark(1, 1, LineMarkKind.Note)], [.. session.ReviewMarksOf(open)], "a note on line 2 of the open file");
            Check.True(DocumentLineMarks.Of(document).Marks.ToArray().Any(m => m.Kind == LineMarkKind.Note), "drawn in the editor's gutter");

            repo.Act(SourceControlView.OpenFinding, session.Findings[0].Id);
            repo.Until(() => DocumentLineMarks.Of(document).Card is not null, "the finding's card shows");
            var card = DocumentLineMarks.Of(document).Card!;
            Check.True(card.Text.Contains("warning · The value changed", StringComparison.Ordinal) && card.Text.Contains("callers expect two", StringComparison.Ordinal)
                       && card.Actions.Select(a => a.Title).SequenceEqual(["Apply fix", "Dismiss", "Copy all"]), $"inline: severity, one line, the reason, and its actions ({card.Text})");
            card.Actions[2].Run();
            Check.Equal("a.txt:2: warning: The value changed — callers expect two\n", copied, "Copy all puts every finding on the clipboard");

            repo.Act(SourceControlView.Open, repo.File("a.txt"));
            repo.Until(() => !repo.Presenter.Diffs.IsEmpty, "the diff view opens");
            Check.True(repo.Presenter.Diffs.Single().Notes is [{ Line: 2, Severity: DiagnosticSeverity.Warning, Text: "The value changed" }], "the finding is a note under its line in the diff view");

            repo.Act(SourceControlView.ApplyFix, session.Findings[0].Id);
            repo.Until(() => session.Findings.Count == 0, "the fix accepted, the finding goes");
            Check.True(repo.Presenter.Proposals.Any(p => p.Contains("Fix the review finding", StringComparison.Ordinal)), "the fix arrived as a diff the user accepted");
            Check.Equal("one\ntwo fixed\nthree\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "applied to the line");
            Check.True(session.ReviewMarksOf(open).Count == 0 && !DocumentLineMarks.Of(document).Marks.ToArray().Any(m => m.Kind == LineMarkKind.Note), "its note is gone");
            document.Undo();
            Check.Equal("one\ntwo changed\nthree\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "one undo step");
            repo.Host.Close(open);
        }
        finally
        {
            CodeReviewProviders.Unregister(review.Name);
            CodeActionProviders.Unregister(fixer.Name);
        }
    }

    [Test]
    public static void StopKeepsWhatWasFoundAndWithNoModelLoadedItSaysSoAndOffersTheLoad()
    {
        if (SourceControlTests.Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        var provider = new HoldingReviewer();
        CodeReviewProviders.Register(provider);
        int loads = 0;
        EditorCommands.Register(new EditorCommand("test.loadReview", "Test: load", (_, _) =>
        {
            loads++;
            return Task.CompletedTask;
        }));
        try
        {
            using var repo = new SourceControlTests.Repo();
            var session = repo.Session;
            repo.Until(() => session.Repository?.Status.Changes.Count == 2, "the repository opens");
            repo.Act(SourceControlView.Review, null);
            repo.Until(() => session.State.Reviewing && session.Findings.Count == 1, "the first finding is in while it reviews");
            Check.True(session.Panel.Blocks.OfType<PanelMessageBox>().Single().Actions.Any(a => a.ActionId == SourceControlView.StopReview), "Stop takes the Review button's place");
            Check.True(repo.Activities.Any(t => t.Contains("Reviewing", StringComparison.Ordinal)), "the review is an activity (with Cancel)");
            repo.Act(SourceControlView.StopReview, null);
            repo.Until(() => !session.State.Reviewing, "Stop ends it");
            Check.True(session.Findings.Count == 1 && session.State.ReviewStatus!.StartsWith("Stopped: 1 finding", StringComparison.Ordinal), $"what was found stays ({session.State.ReviewStatus})");
            Check.True(provider.Requests.Single().AllChanges, "nothing staged: every change is reviewed, said to the model");

            provider.Model = null;
            repo.Act(SourceControlView.Review, null);
            repo.Pump();
            Check.True(session.State.ReviewStatus!.Contains("No model is loaded", StringComparison.Ordinal) && session.State.ReviewTone == PanelTone.Warning, "no model: it says so");
            Check.Equal(0, loads, "and loads nothing by itself (note 23)");
            Check.True(session.Panel.Blocks.OfType<PanelButtons>().Any(b => b.Buttons.Any(x => x is { ActionId: SourceControlView.LoadReviewModel })), "it offers the load");
            repo.Act(SourceControlView.LoadReviewModel, null);
            repo.Pump();
            Check.True(loads == 1 && provider.Requests.Count == 1, "the load runs only when pressed; the model was asked once");
            repo.Act(SourceControlView.ClearFindings, null);
            Check.True(session.Findings.Count == 0 && !session.Panel.Blocks.OfType<PanelResults>().Any(r => r.Id == "review"), "Clear drops the findings");
        }
        finally
        {
            CodeReviewProviders.Unregister(provider.Name);
            EditorCommands.Unregister("test.loadReview");
        }
    }

    [Test]
    public static async Task TheAiReviewerUsesALoadedRolesLeaseAndItsSettingsAndNeverLoads()
    {
        using var models = new LoadedChat(FakeChatModel.Script(FakeChatModel.Answer("src/A.cs:3: error: Off by one — skips the last\nsrc/B.cs:9: info: Rename it\nNo more.")), reviewMaxTokens: 99);
        var review = new AiCodeReview(() => models.Settings, models.Models);
        var plan = review.Plan();
        Check.True(plan is { Model: null, LoadCommand: AiPlugin.LoadModelCommand } && plan.Why!.Contains("not loaded", StringComparison.Ordinal), $"nothing loaded: said, with the load ({plan.Why})");
        bool refused = false;
        try
        {
            await foreach (var _ in review.ReviewAsync(new CodeReviewRequest("diff"), CancellationToken.None))
            {
            }
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Check.True(refused && models.Provider.Acquired == 0, "reviewing with nothing loaded is refused and loads nothing");
        models.Load();
        plan = review.Plan();
        Check.True(plan is { Model: "fake-7b", Role: "chat", ReplyTokens: 99 }, $"loaded: the chat role's ({plan.Model}, {plan.Role})");
        var found = new List<ReviewFinding>();
        await foreach (var finding in review.ReviewAsync(new CodeReviewRequest("+    3| x") { Summarised = ["big.cs"] }, CancellationToken.None))
        {
            found.Add(finding);
        }

        Check.True(found is [{ Path: "src/A.cs", Line: 3, Severity: DiagnosticSeverity.Error }, { Path: "src/B.cs", Line: 9, Severity: DiagnosticSeverity.Info }], $"each whole line read as it streams ({found.Count})");
        var request = models.Chat.Requests.Single();
        Check.True(request.Options is { Temperature: 0.25f, NumPredict: 99 } && request.Messages[0].Content == AiCodeReview.DefaultPrompt
                   && request.Messages[1].Content.Contains("staged changes", StringComparison.Ordinal) && request.Messages[1].Content.Contains("big.cs", StringComparison.Ordinal),
            "the role's temperature, reviewMaxTokens, the plug-in's instructions, the changes and the files not reviewed");
        Check.Equal(1, models.Provider.Acquired, "one load in all: the user's");
    }

    /// <summary>The chat model loaded by the user through a counting provider (the inline chat tests' provider).</summary>
    private sealed class LoadedChat : IDisposable
    {
        public LoadedChat(FakeChatModel chat, int reviewMaxTokens = 0)
        {
            Chat = chat;
            Provider = new InlineChatTests.CountingProvider(chat);
            ModelProviders.Register(Provider);
            Settings = new AiSettings { ModelProvider = Provider.Name, Model = "fake-7b", CompletionModel = "", Completion = false, ChatTemperature = 0.25f, ReviewMaxTokens = reviewMaxTokens };
            Models = new AiModels(() => Settings);
        }

        public FakeChatModel Chat { get; }

        public InlineChatTests.CountingProvider Provider { get; }

        public AiSettings Settings { get; }

        public AiModels Models { get; }

        public void Load() => Task.Run(() => Models.ChatAsync(CancellationToken.None)).GetAwaiter().GetResult();

        public void Dispose()
        {
            Models.Dispose();
            ModelProviders.Unregister(Provider.Name);
        }
    }

    /// <summary>A code action provider whose Fix proposes the line "two fixed" for a review's finding (as the AI plug-in's would, through the diff).</summary>
    private sealed class FixingActions : ICodeActionProvider
    {
        public IEditorHost? Host { get; set; }

        public string Name => "review-fix-test";

        public string Summary => "the review tests' fix";

        public IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context) => context.Diagnostics is [{ Id: "review" } diagnostic]
            ? [new CodeAction("test.fix", "Fix", CodeActionKinds.Fix, token => Host!.ProposeAsync(context.Document, new ProposedEdits([new TextEdit(diagnostic.Start, diagnostic.Length, "two fixed")], "Fix the review finding"), token))]
            : [];
    }

    /// <summary>A reviewer that streams one finding, then holds until stopped.</summary>
    private sealed class HoldingReviewer : ICodeReviewProvider
    {
        public string Name => "holding-review";

        public string Summary => "the review tests' reviewer";

        public string? Model { get; set; } = "fake-7b";

        public List<CodeReviewRequest> Requests { get; } = [];

        public CodeReviewPlan Plan() => Model is null
            ? new CodeReviewPlan(null, null, "No model is loaded: load the chat model.") { LoadCommand = "test.loadReview" }
            : new CodeReviewPlan(Model, "chat", null);

        public async IAsyncEnumerable<ReviewFinding> ReviewAsync(CodeReviewRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            yield return new ReviewFinding("a.txt", 2, DiagnosticSeverity.Warning, "First", "");
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }
}
