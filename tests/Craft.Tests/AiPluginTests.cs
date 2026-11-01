// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Tools;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;

namespace Craft.Tests;

/// <summary>
/// Phase A2, headless: inline completion (ghost text after the pause from a fake text model laid out by a registered
/// fill-in-the-middle format, a keystroke cancelling the request within a token, Tab accepting it as one undo step, the
/// hint bar drawn, prefix-only said when the model has no format), the chat panel (a fake chat model's reply streamed, a
/// tool-call row, a code block's Apply as diff arriving in the diff view and accepted as one undo step, the conversation
/// kept per workspace), a code action's diff accepted as one undo step, and the agent (its edit held as a diff in a working
/// copy, its command waiting for a fake approving sandbox and refused when denied; no sandbox registered, no command).
/// </summary>
[TestGroup("AiPlugins")]
public static class AiPluginTests
{
    private const string ProviderName = "a2-fake";
    private const string FormatName = "a2-psm";

    [Test]
    public static async Task GhostTextAppearsAfterThePauseAndTabAcceptsItAsOneUndoStep()
    {
        var text = new FakeTextModel(["ystem.", "Console", ".", "WriteLine", "();"], stepMilliseconds: 2);
        using var fixture = new CompletionFixture(text, withFormat: true);
        var (model, document, ui) = fixture.Editor("class A\n{\n    void M()\n    {\n        \n    }\n}\n", caretLine: 4);
        model.OnText("S");
        Check.True(model.InlineCompletion.Text.Length == 0 && !model.InlineCompletion.IsRequesting, "nothing is asked before the pause");
        await PumpUntil(ui, () => model.InlineCompletion.Text == "ystem.Console.WriteLine();" && !model.InlineCompletion.IsRequesting, "the ghost text streams in after the pause");
        Check.True(model.InlineCompletion.IsShown, "it shows");
        Check.True(model.InlineCompletion.FirstPieceMilliseconds is >= 0 && model.InlineCompletion.Tokens == 5, $"its first piece's time and its tokens ({model.InlineCompletion.Tokens})");
        Check.Equal(FormatName, fixture.Provider.Mode?.Format, "the format was detected from the model's tokenizer");
        string prompt = text.Prompts.Single();
        Check.True(prompt.StartsWith("<PRE>", StringComparison.Ordinal) && prompt.Contains("<SUF>", StringComparison.Ordinal) && prompt.EndsWith("<MID>", StringComparison.Ordinal),
            $"the prompt is laid out by the format ({prompt})");
        Check.True(prompt.Contains("        S<SUF>\n    }\n}\n<MID>", StringComparison.Ordinal), $"the prefix up to the caret, then the suffix after it ({prompt})");

        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var ghost = EditorThemes.Get(EditorThemes.Dark).Colors.GhostText;
        Check.True(renderer.GhostLinesDrawn == 1 && canvas.Texts.Any(t => t.Text == "ystem.Console.WriteLine();" && t.Argb == ghost && t.X == 9 * 10 + model.TextLeft),
            "the ghost text is drawn after the caret in the theme's ghost colour");
        Check.True(renderer.GhostHintDrawn && canvas.Texts.Any(t => t.Text == "Tab") && canvas.Texts.Any(t => t.Text == " dismiss")
                   && canvas.Texts.Any(t => t.Text.Contains("first token", StringComparison.Ordinal)), "the hint bar: Tab accept, Ctrl+→ word, Esc dismiss, the rate and the first token");

        Check.True(model.OnKey(EditorKey.Tab, KeyModifiers.None), "Tab accepts");
        string accepted = "class A\n{\n    void M()\n    {\n        System.Console.WriteLine();\n    }\n}\n";
        Check.Equal(accepted, Text(document), "the whole ghost text is inserted");
        Check.True(model.Selection.Caret == accepted.IndexOf("();", StringComparison.Ordinal) + 3 && model.InlineCompletion.Text.Length == 0, "the caret after it, the ghost gone");
        document.Undo();
        Check.Equal("class A\n{\n    void M()\n    {\n        S\n    }\n}\n", Text(document), "one undo step takes the accepted text back, and only it");
        Check.True(model.InlineCompletion.Accepted == 1 && model.InlineCompletion.Requests == 1, "one request, one acceptance");
    }

    [Test]
    public static async Task EnterAfterACommentAsksForTheCodeOnTheNewLine()
    {
        var text = new FakeTextModel(["static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));"], stepMilliseconds: 2);
        using var fixture = new CompletionFixture(text, withFormat: true);
        var (model, document, ui) = fixture.Editor("// a sigmoid function\n", caretLine: 0);
        Check.True(model.OnKey(EditorKey.Enter, KeyModifiers.None), "Enter makes the new line");
        await PumpUntil(ui, () => model.InlineCompletion.IsShown && !model.InlineCompletion.IsRequesting,
            $"Enter asks for ghost text on the new line (requests {model.InlineCompletion.Requests}, error {model.InlineCompletion.LastError?.Message})");
        string prompt = text.Prompts.Single();
        Check.True(prompt.Contains("// a sigmoid function\n", StringComparison.Ordinal) && prompt.Contains("<SUF>", StringComparison.Ordinal),
            $"the prefix holds the comment and the new line ({prompt})");
        Check.True(model.OnKey(EditorKey.Tab, KeyModifiers.None), "Tab accepts");
        Check.True(Text(document).Contains("// a sigmoid function\nstatic double Sigmoid(double x)", StringComparison.Ordinal), $"the code under the comment ({Text(document)})");
    }

    [Test]
    public static async Task AKeystrokeCancelsTheRequestWithinAToken()
    {
        var text = new FakeTextModel([.. Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? "x" : "y")], stepMilliseconds: 15);
        using var fixture = new CompletionFixture(text, withFormat: true);
        var (model, document, ui) = fixture.Editor("a\nb\n", caretLine: 0, column: 1);
        model.OnText("=");
        await PumpUntil(ui, () => model.InlineCompletion.Text.Length >= 3, "the ghost text starts streaming");
        Check.True(model.OnKey(EditorKey.Left, KeyModifiers.None), "a key the editor handles");
        Check.True(model.InlineCompletion.Text.Length == 0 && !model.InlineCompletion.IsRequesting && model.InlineCompletion.Cancelled == 1, "the keystroke removes the ghost and cancels its request at once");
        await PumpUntil(ui, () => text.Calls.Count == 1 && text.Calls[0].Ended, "the model's stream ends");
        var call = text.Calls[0];
        Check.True(call.AfterCancel <= 1, $"the model sampled at most one token after the cancellation ({call.AfterCancel})");
        Check.True(call.Produced < 200, $"and stopped well before its end ({call.Produced} of 200)");
        await Task.Delay(60);
        Pump(ui);
        Check.True(model.InlineCompletion.Text.Length == 0 && text.Calls.Count == 1, "no piece of the cancelled request shows, and moving the caret asks nothing new");
        Check.Equal("a=\nb\n", Text(document), "the text holds only what was typed");
    }

    [Test]
    public static async Task EscapeDismissesCtrlRightTakesAWordAndTypingThroughKeepsTheRest()
    {
        var text = new FakeTextModel(["Write", "Line", "(x);"], stepMilliseconds: 1);
        using var fixture = new CompletionFixture(text, withFormat: false);
        var (model, document, ui) = fixture.Editor("\n", caretLine: 0);
        model.OnText("Console.");
        await PumpUntil(ui, () => model.InlineCompletion.Text == "WriteLine(x);" && !model.InlineCompletion.IsRequesting, "the ghost text");
        Check.True(fixture.Provider.Mode is { Format: null } mode && mode.Text.StartsWith("Completion: prefix-only", StringComparison.Ordinal),
            "a model without a registered fill-in-the-middle format completes the prefix alone, and says so");
        Check.Equal("Console.", text.Prompts[0], "prefix-only: the text before the caret is the prompt");
        Check.True(model.OnKey(EditorKey.Right, KeyModifiers.Control), "Ctrl+Right accepts a word");
        Check.True(Text(document) == "Console.WriteLine\n" && model.InlineCompletion.Text == "(x);", "one word went in, the rest stays as ghost text");
        model.OnText("(");
        Check.True(model.InlineCompletion.Text == "x);" && Text(document).StartsWith("Console.WriteLine(", StringComparison.Ordinal), "typing what the ghost starts with types through it");
        Check.True(model.OnKey(EditorKey.Escape, KeyModifiers.None) && model.InlineCompletion.Text.Length == 0, "Escape dismisses");
        Check.Equal(5, InlineCompletionController.NextWordLength("    x"), "a word: the white space, then the word");
        Check.Equal(5, InlineCompletionController.NextWordLength("\n    x"), "a line break takes the next line's indentation");
        Check.Equal(1, InlineCompletionController.NextWordLength(".x"), "one punctuation character");
        Check.Equal(7, InlineCompletionController.NextWordLength("Console.Write"), "letters to the dot");
    }

    [Test]
    public static void ThePrefixStartsAtABlockSoAKeptCacheReusesThePrompt()
    {
        var snapshot = TextDocument.Create(string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i}\n"))).Buffer.Current;
        int moves = 0;
        int last = AiInlineCompletion.PrefixStart(snapshot, 2000, 1000);
        for (int caret = 2001; caret < 2200; caret++)
        {
            int start = AiInlineCompletion.PrefixStart(snapshot, caret, 1000);
            Check.True(start <= caret && caret - start <= 1000 && snapshot.GetLine(snapshot.GetLineIndex(start)).Start == start, $"caret {caret}: a line start within the budget");
            moves += start != last ? 1 : 0;
            last = start;
        }

        Check.True(moves <= 1, $"over 200 characters typed the prefix moved {moves} times (once a block)");
        Check.Equal(0, AiInlineCompletion.PrefixStart(snapshot, 500, 1000), "a caret within the budget takes the text from the start");
    }

    [Test]
    public static async Task TheChatStreamsCallsAToolAndAppliesCodeAsADiff()
    {
        string root = Folder();
        try
        {
            var presenter = new AcceptingPresenter();
            using var host = new EditorHost(dispatch: Serial(), presenter: presenter) { WorkspaceFolder = root };
            using var document = TextDocument.Create("class A\n{\n    int x;\n}\n");
            var open = host.Open(document, null);
            host.SetActive(open);
            host.UpdateSelection(open, SelectionState.At(14));
            var chat = new ScriptedChatModel(
                new ChatMessage("assistant", ToolCalls: [new ToolCall(EditorToolNames.GetDiagnostics, [])]),
                new ChatMessage("assistant", "Rename the field:\n```csharp\n    int y;\n```\nThat is all."));
            using var fixture = new ChatFixture(chat);
            var panel = new AiChatPanel(host, () => fixture.Settings, fixture.Models);
            Check.True(panel.Chips.Any(c => c.Id == "file"), "the active file is a context chip");
            var changes = new List<ConversationChange>();
            panel.ConversationChanged += (_, e) => changes.Add(e.Change);
            await panel.SubmitAsync("Name it better");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(10));

            Check.True(chat.Requests[0].Tools?.Any(t => t.Name == EditorToolNames.ReadDocument) == true, "the editor's live tools are offered to the model");
            Check.True(chat.Requests[0].Messages[^1].Content.Contains("int x;", StringComparison.Ordinal), "the file's context goes with the message");
            Check.True(changes.Count(c => c == ConversationChange.EntryUpdated) >= 3, $"the reply streamed into its entry ({changes.Count(c => c == ConversationChange.EntryUpdated)} updates)");
            var entries = panel.Entries;
            Check.True(entries[0] is { Role: ConversationRole.User, Text: "Name it better" }, "the user's message");
            Check.True(entries.Any(e => e is { Role: ConversationRole.Tool, Text: EditorToolNames.GetDiagnostics, Succeeded: true } && e.Detail!.StartsWith("→ ", StringComparison.Ordinal)),
                "a tool-call row: Called get_diagnostics → its result");
            var code = entries.Single(e => e.Role == ConversationRole.Code);
            Check.True(code.Text == "    int y;\n" && code.Language == "csharp" && code.CopyText == code.Text && code.Actions.Single().Label == "Apply as diff",
                "the code block with Apply as diff and Copy");
            Check.True(entries.Any(e => e is { Role: ConversationRole.Assistant, Text: "Rename the field:" }) && entries.Any(e => e is { Role: ConversationRole.Assistant, Text: "That is all." }),
                "the text around the block");
            Check.True(panel.Status?.Contains("tokens", StringComparison.Ordinal) == true && !panel.IsBusy, $"the tokens and the rate ({panel.Status})");

            await code.Actions[0].RunAsync(CancellationToken.None);
            var proposal = presenter.Proposals.Single();
            Check.True(proposal.Diff is { Hunks.Count: 1, Added: 1, Removed: 1 } && proposal.Placement!.StartsWith("semi-global", StringComparison.Ordinal),
                $"Apply as diff arrives in the diff view, placed by the matcher ({proposal.Placement})");
            Check.Equal("class A\n{\n    int y;\n}\n", Text(document), "accepted, it is applied");
            document.Undo();
            Check.True(Text(document) == "class A\n{\n    int x;\n}\n" && !document.CanUndo, "one undo step");

            string kept = Path.Combine(root, ".craft", "chat.json");
            await panel.Kept.WaitAsync(TimeSpan.FromSeconds(10));                 // written off the UI thread
            Check.True(File.Exists(kept), "the conversation is kept in the workspace");
            var again = new AiChatPanel(host, () => fixture.Settings, fixture.Models);
            await again.KeptRead.WaitAsync(TimeSpan.FromSeconds(10));            // read on the thread pool, shown through the dispatcher
            Check.True(again.History.Count == 2 && again.Entries.Any(e => e.Role == ConversationRole.Code && e.Actions.Count == 1), "a new panel reads it back, its code still applicable");
            again.NewChat();
            await again.Kept.WaitAsync(TimeSpan.FromSeconds(10));
            Check.True(File.Exists(kept) && again.Entries.Count == 0 && again.Conversations.Count == 1,
                "New chat starts an empty conversation; the last one stays in the list of past conversations (X part 10)");
            panel.Dispose();
            again.Dispose();
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public static async Task ACodeActionsDiffIsAcceptedAsOneUndoStep()
    {
        var presenter = new AcceptingPresenter();
        using var host = new EditorHost(dispatch: Serial(), presenter: presenter);
        using var document = TextDocument.Create("class A\n{\n    int x;\n}\n");
        var open = host.Open(document, null);
        host.SetActive(open);
        host.UpdateSelection(open, new SelectionState(10, 20));
        var chat = new ScriptedChatModel(new ChatMessage("assistant", "```csharp\n    /// <summary>The count.</summary>\n    int x;\n```"));
        using var fixture = new ChatFixture(chat);
        using var panel = new AiChatPanel(host, () => fixture.Settings, fixture.Models);
        var actions = new AiCodeActions(host, () => fixture.Settings, fixture.Models, panel);
        CodeActionProviders.Register(actions);
        try
        {
            var context = AiCodeActions.AtSelection(host)!;
            var offered = CodeActionProviders.ActionsFor(context);
            Check.True(offered.Select(a => a.Kind).SequenceEqual([CodeActionKinds.Explain, CodeActionKinds.Document, CodeActionKinds.Refactor, CodeActionKinds.Tests])
                       && offered.All(a => a.Provider == AiCodeActions.ProviderName), "a selection's actions, from the registered provider");
            var withProblem = actions.ActionsFor(context with { Diagnostics = [new EditorDiagnostic(14, 1, DiagnosticSeverity.Warning, "CS0169", "The field is never used")] });
            Check.True(withProblem[0].Kind == CodeActionKinds.Fix && withProblem[1].Kind == CodeActionKinds.Explain, "a problem's Fix and Explain come first");

            await offered.Single(a => a.Kind == CodeActionKinds.Document).RunAsync(CancellationToken.None);
            Check.True(actions.LastAccepted == true && presenter.Proposals.Single().Title == "Add doc comments", "the action's result arrives as a diff the user accepts");
            Check.True(chat.Requests.Single().Tools is null && chat.Requests[0].Messages[^1].Content.Contains("    int x;", StringComparison.Ordinal), "the lines go to the model, no tools");
            Check.Equal("class A\n{\n    /// <summary>The count.</summary>\n    int x;\n}\n", Text(document), "applied");
            document.Undo();
            Check.True(Text(document) == "class A\n{\n    int x;\n}\n" && !document.CanUndo, "one undo step");
        }
        finally
        {
            CodeActionProviders.Unregister(actions.Name);
        }
    }

    [Test]
    public static async Task TheAgentHoldsItsEditAsADiffAndItsCommandWaitsForApprovalAndIsRefusedWhenDenied()
    {
        string root = Folder();
        var sandbox = new ApprovalSandbox("a2-approve");
        CommandSandboxes.Register(sandbox);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\nkeep\n");
            var presenter = new AcceptingPresenter();
            using var host = new EditorHost(dispatch: Serial(), presenter: presenter) { WorkspaceFolder = root };
            host.OpenFile = async (path, token) => host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null);
            var chat = new ScriptedChatModel(
                new ChatMessage("assistant", ToolCalls: [new ToolCall("edit_file", new JsonObject { ["path"] = "a.txt", ["old_text"] = "alpha", ["new_text"] = "beta" })]),
                new ChatMessage("assistant", ToolCalls: [new ToolCall("run_command", new JsonObject { ["command"] = "git status" })]),
                new ChatMessage("assistant", "Done."));
            using var fixture = new ChatFixture(chat, agentSandbox: sandbox.Name, agentFolder: root + "-copies");
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Change alpha");
            await Until(() => sandbox.Waiting, "the command reaches the sandbox, which waits for the user");
            Check.True(panel.IsBusy && panel.Status!.Contains("Waiting for you", StringComparison.Ordinal) && panel.Status.Contains("Sandbox a2-approve", StringComparison.Ordinal),
                $"the run waits, and says so ({panel.Status})");
            Check.True(panel.Entries.Any(e => e is { Role: ConversationRole.Approval, Text: "git status" }), "the command shows, waiting for approval");
            Check.True(panel.Changes is [{ Relative: "a.txt", Added: 1, Removed: 1, IsNew: false }], "the edit is listed as a changed file, +1 −1");
            Check.Equal("alpha\nkeep\n", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")), "the user's file is untouched: the edit is held");
            await panel.SubmitAsync("Do not run anything else.");
            sandbox.Answer(false);
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            var told = chat.Requests[2].Messages.Where(m => m.Role == "tool").Last();
            Check.True(told.Content.Contains("denied", StringComparison.OrdinalIgnoreCase), $"denied, the agent is told so ({told.Content})");
            Check.True(chat.Requests[2].Messages.Any(m => m is { Role: "user", Content: "Note from the user: Do not run anything else." }), "the steering note reached the next turn");
            Check.True(sandbox.Ran == 0 && panel.Entries.Any(e => e.Role == ConversationRole.Tool && e.Text == "run_command git status" && e.Succeeded == false),
                "the command never ran; its row says refused");
            Check.True(panel.Entries.Any(e => e is { Role: ConversationRole.Section, Text: "CHANGED FILES" } && e.Actions.Select(a => a.Label).SequenceEqual(["Review diff", "Discard all"])),
                "the changed files with Review diff and Discard all");

            Check.True(await panel.ReviewAsync(), "Review diff: each file's change is proposed, and accepted");
            var proposal = presenter.Proposals.Single();
            Check.True(proposal.Diff is { Added: 1, Removed: 1 } && proposal.Origin!.StartsWith("Agent", StringComparison.Ordinal), "the file's diff in the diff view");
            var document = host.Documents.Single();
            Check.True(Text(document.Document) == "beta\nkeep\n" && panel.Changes.Count == 0, "accepted into the editor; the list is empty");
            document.Document.Undo();
            Check.Equal("alpha\nkeep\n", Text(document.Document), "one undo step");
            panel.DiscardAll();
            Check.True(panel.WorkingCopy is null, "Discard all drops the working copy");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            CommandSandboxes.Unregister(sandbox.Name);
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-copies"))
            {
                Directory.Delete(root + "-copies", recursive: true);
            }
        }
    }

    [Test]
    public static async Task ALongChatReplyArrivesWholeAndTheNextMessageCarriesTheConversation()
    {
        // The owner's Windows run: replies ended after a few words and the next message seemed to forget them.
        string root = Folder();
        try
        {
            using var host = new EditorHost(dispatch: Serial(), presenter: new AcceptingPresenter()) { WorkspaceFolder = root };
            string reply = string.Join(" ", Enumerable.Range(0, 3000).Select(i => "word" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ".";
            var chat = new ScriptedChatModel(new ChatMessage("assistant", reply), new ChatMessage("assistant", "You asked me to explain."));
            using var fixture = new ChatFixture(chat);
            using var panel = new AiChatPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Explain everything");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(30));
            var options = chat.Requests[0].Options!;
            Check.True(options.NumPredict == 2048 && options.Stop.Count == 0 && options.NumCtx == 4096,
                $"the reply's limit is the chatMaxTokens setting (2048 by default), no extra stops, the model's context ({options.NumPredict}, {options.Stop.Count} stops, {options.NumCtx})");
            string shown = string.Concat(panel.Entries.Where(e => e.Role == ConversationRole.Assistant).Select(e => e.Text));
            Check.Equal(reply, shown, "the 3,001-piece reply arrives whole in the panel");
            Check.True(!panel.Entries.Any(e => e.Role is ConversationRole.Error) && !panel.IsBusy, "it ended at the model's end of turn, nothing said wrong");

            await panel.SubmitAsync("What did I ask?");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(30));
            var sent = chat.Requests[1].Messages;
            Check.True(sent.Any(m => m.Role == "user" && m.Content.EndsWith("Explain everything", StringComparison.Ordinal)) && sent.Any(m => m is { Role: "assistant" } && m.Content == reply)
                && sent[^1].Content.EndsWith("What did I ask?", StringComparison.Ordinal),
                $"the next message carries the conversation: the first question and its whole reply ({sent.Count} messages)");

            var limited = fixture.Settings with { ChatMaxTokens = 16, ChatStops = ["END"] };
            var stopped = AiSettings.Read(k => k switch { "chatMaxTokens" => "16", "chatStops" => "END|<stop;here>", "agentStops" => "", _ => null });
            Check.True(stopped is { ChatMaxTokens: 16, ChatStops: ["END", "<stop;here>"], AgentStops: [] } && new AiSettings().ChatMaxTokens == 2048 && limited.ChatStops.Count == 1,
                "chatMaxTokens and chatStops are settings ('|' between stops, a stop may hold ';')");
            await panel.Kept.WaitAsync(TimeSpan.FromSeconds(10));                 // the kept chat's write ends before the folder goes
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public static async Task AFollowUpContinuesTheAgentsRunOnItsCopyAndClearStartsAfresh()
    {
        // The owner's Windows run: each message to the agent started a new run on a new copy, with no memory of the last.
        string root = Folder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\n");
            using var host = new EditorHost(dispatch: Serial(), presenter: new AcceptingPresenter()) { WorkspaceFolder = root };
            var chat = new ScriptedChatModel(
                new ChatMessage("assistant", ToolCalls: [new ToolCall("edit_file", new JsonObject { ["path"] = "a.txt", ["old_text"] = "alpha", ["new_text"] = "beta" })]),
                new ChatMessage("assistant", "Changed alpha to beta."),
                new ChatMessage("assistant", "You asked me to change alpha; it is beta now."),
                new ChatMessage("assistant", "A new task."));
            using var fixture = new ChatFixture(chat, agentSandbox: "a2-none", agentFolder: root + "-copies");
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Change alpha");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            var copy = panel.WorkingCopy;
            Check.True(copy is not null && panel.Continues && panel.Changes.Count == 1, "after the run, its copy and its change stay, and the next message continues it");

            await panel.SubmitAsync("What did I ask?");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            var sent = chat.Requests[2].Messages;
            Check.True(sent.Any(m => m is { Role: "user", Content: "Change alpha" }) && sent.Any(m => m is { Role: "assistant", Content: "Changed alpha to beta." })
                && sent.Any(m => m.Role == "tool") && sent[^1] is { Role: "user", Content: "What did I ask?" } && sent.Count(m => m.Role == "system") == 1,
                $"the follow-up carries the run's conversation: the task, the tool result, the answer, then the new message ({sent.Count} messages)");
            Check.True(ReferenceEquals(panel.WorkingCopy, copy) && panel.Changes.Count == 1 && panel.Entries.Any(e => e is { Role: ConversationRole.User, Text: "Change alpha" }),
                "on the same copy, its change and its log kept");
            Check.True(panel.Entries.Any(e => e.Role == ConversationRole.Assistant && e.Text.StartsWith("You asked me", StringComparison.Ordinal)), "the answer arrives");

            await panel.HeaderActions.Single(a => a.Label == "Clear").RunAsync(CancellationToken.None);
            Check.True(!panel.Continues, "Clear: the next message starts a new run");
            panel.DiscardAll();
            await panel.SubmitAsync("Something else");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            Check.True(!chat.Requests[3].Messages.Any(m => m.Content == "Change alpha") && !ReferenceEquals(panel.WorkingCopy, copy),
                "a new conversation on a new copy");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-copies"))
            {
                Directory.Delete(root + "-copies", recursive: true);
            }
        }
    }

    [Test]
    public static async Task AnAcceptedNewFileOfTheAgentIsCreatedAndShowsInTheTreeWithinASecond()
    {
        string root = Folder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\n");
            var presenter = new AcceptingPresenter();
            var ui = Serial();                                               // the "UI thread": the host's and the tree's
            using var host = new EditorHost(dispatch: ui, presenter: presenter) { WorkspaceFolder = root };
            // As the shell does: an existing file opens; a path with no file yet opens a new, untitled document.
            host.OpenFile = async (path, token) => File.Exists(path) ? host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null) : host.Open(TextDocument.Create(""), null);
            var tree = new FolderTree(new Workspace(root));
            using var watch = new FolderTreeWatch(tree, FolderWatchers.Get(FolderWatchers.System), 50, ui);
            bool Shows(string path)
            {
                bool shown = false;
                ui(() => shown = tree.IndexOf(path) >= 0);
                return shown;
            }

            var chat = new ScriptedChatModel(
                new ChatMessage("assistant", ToolCalls: [new ToolCall("write_file", new JsonObject { ["path"] = "Example.cs", ["content"] = "class Example { }\n" })]),
                new ChatMessage("assistant", "Done."));
            using var fixture = new ChatFixture(chat, agentFolder: root + "-copies");
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Add Example.cs");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            string created = Path.Combine(root, "Example.cs");
            Check.True(panel.Changes is [{ Relative: "Example.cs", IsNew: true }] && !File.Exists(created), "the new file is held as a change; nothing is written before the user accepts");
            Check.True(!Shows(created), "so the tree does not show it");

            Check.True(await panel.ReviewAsync(), "Review diff: the new file's diff is proposed and accepted");
            Check.True(presenter.Proposals.Single().Placement!.Contains("accepting it creates Example.cs", StringComparison.Ordinal), "the diff says accepting creates the file");
            var clock = Stopwatch.StartNew();
            Check.Equal("class Example { }\n", await File.ReadAllTextAsync(created), "accepted whole, the file is created through the host (IEditorHost.SaveAsync)");
            Check.True(host.Documents.Any(d => d.Document.FilePath == created), "the document is the file now (its tab takes the name)");
            await Until(() => Shows(created), "the accepted file shows in the tree");
            Check.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"within a second of the accept ({clock.Elapsed.TotalMilliseconds:F0} ms; the watch's delay is 50 ms)");
            Check.True(panel.Entries.Any(e => e.Role == ConversationRole.Note && e.Text.Contains("the file is created", StringComparison.Ordinal)), "the agent's column says so");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-copies"))
            {
                Directory.Delete(root + "-copies", recursive: true);
            }
        }
    }

    /// <summary>
    /// B6 (the owner's third Windows run: the agent made the file, but it never reached the workspace): a scripted model
    /// writes a new file by a padded relative path with spaces, edits an existing file, runs a command that writes a file
    /// (and build output), and writes a file by the copy's full path, on a workspace whose path has spaces. Every one is
    /// listed (the build output not), nothing touches the workspace before the review, and accepting each writes it there:
    /// the new files created, the edited one saved.
    /// </summary>
    [Test]
    public static async Task EveryFileTheAgentWritesReachesTheWorkspaceOnAccept()
    {
        string root = Path.Combine(Folder(), "my project");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "class Program\n{\n}\n");
        var sandbox = new WritingSandbox("b6-writes");
        CommandSandboxes.Register(sandbox);
        try
        {
            var presenter = new AcceptingPresenter();
            using var host = new EditorHost(dispatch: Serial(), presenter: presenter) { WorkspaceFolder = root };
            host.OpenFile = async (path, token) => File.Exists(path) ? host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null) : host.Open(TextDocument.Create(""), null);
            static ChatMessage Calls(ToolCall call) => new("assistant", ToolCalls: [call]);
            var chat = new ComputedChatModel(turn => turn switch
            {
                0 => Calls(new ToolCall("write_file", new JsonObject { ["path"] = " ./src/House Model.cs ", ["content"] = "class House\n{\n    public float Price;\n}\n" })),
                1 => Calls(new ToolCall("edit_file", new JsonObject { ["path"] = "src/Program.cs", ["old_text"] = "{\n}", ["new_text"] = "{\n    static void Main() { }\n}" })),
                2 => Calls(new ToolCall("run_command", new JsonObject { ["command"] = "git status" })),
                3 => Calls(new ToolCall("write_file", new JsonObject { ["path"] = Path.Combine(sandbox.Folder!, "Full Path.cs"), ["content"] = "class FullPath { }\n" })),
                _ => new ChatMessage("assistant", "Done."),
            });
            using var fixture = new ChatFixture(chat, agentSandbox: sandbox.Name, agentFolder: root + " copies");
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Make a House class with a price");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            await Until(() => panel.Changes.Count == 4, $"the four files are listed ({string.Join(", ", panel.Changes.Select(c => c.Relative))})");
            var listed = panel.Changes.Select(c => c.Relative).Order(StringComparer.Ordinal).ToList();
            Check.True(listed.SequenceEqual(["Full Path.cs", "Made.cs", "src/House Model.cs", "src/Program.cs"]),
                $"the padded path, the edit, the command's file and the full path are each listed; the build output is not ({string.Join(", ", listed)})");
            Check.True(!File.Exists(Path.Combine(root, "src", "House Model.cs")) && !File.Exists(Path.Combine(root, "Made.cs"))
                       && await File.ReadAllTextAsync(Path.Combine(root, "src", "Program.cs")) == "class Program\n{\n}\n",
                "nothing reaches the workspace before the review");

            Check.True(await panel.ReviewAsync(), "Review diff: every file's change accepted");
            Check.Equal("class House\n{\n    public float Price;\n}\n", await File.ReadAllTextAsync(Path.Combine(root, "src", "House Model.cs")), "the new file by a padded path is created");
            Check.Equal("// made by the command\nclass Made { }\n", await File.ReadAllTextAsync(Path.Combine(root, "Made.cs")), "the command's file is created");
            Check.Equal("class FullPath { }\n", await File.ReadAllTextAsync(Path.Combine(root, "Full Path.cs")), "the file written by the copy's full path is created");
            Check.Equal("class Program\n{\n    static void Main() { }\n}\n", await File.ReadAllTextAsync(Path.Combine(root, "src", "Program.cs")), "the edited file is saved (agentSaveOnAccept)");
            Check.True(!File.Exists(Path.Combine(root, "obj", "project.assets.json")) && panel.Changes.Count == 0, "the build output stays in the copy; the list is empty");
            Check.True(panel.Entries.Any(e => e.Role == ConversationRole.Note && e.Text.Contains("src/Program.cs: accepted into the editor (one undo step); saved to the workspace", StringComparison.Ordinal)),
                "the agent's column says the edited file was saved");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            CommandSandboxes.Unregister(sandbox.Name);
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    [Test]
    public static async Task WithNoSandboxRegisteredTheAgentRunsNoCommandAndSaysSo()
    {
        string root = Folder();
        try
        {
            using var host = new EditorHost(dispatch: Serial(), presenter: new AcceptingPresenter()) { WorkspaceFolder = root };
            var chat = new ScriptedChatModel(
                new ChatMessage("assistant", ToolCalls: [new ToolCall("run_command", new JsonObject { ["command"] = "git status" })]),
                new ChatMessage("assistant", "I could not run it."));
            using var fixture = new ChatFixture(chat, agentSandbox: "a2-none", agentFolder: root + "-copies");
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Check the status");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(10));
            Check.True(panel.Entries.Any(e => e.Role == ConversationRole.Error && e.Text.StartsWith("No command sandbox 'a2-none' is registered", StringComparison.Ordinal)),
                "the panel says no sandbox is registered under the setting's name");
            var told = chat.Requests[1].Messages.Last(m => m.Role == "tool");
            Check.True(told.Content.Contains("runs no command", StringComparison.Ordinal), $"the agent is told the command was refused ({told.Content})");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();                // the panel's copy is deleted on the thread pool: never two deletes at once
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-copies"))
            {
                Directory.Delete(root + "-copies", recursive: true);
            }
        }
    }

    [Test]
    public static void TheSettingsReadAndTheEditorOptionNamesTheProvider()
    {
        var settings = AiSettings.Read(key => key switch
        {
            "completionDelayMs" => "120",
            "completionSuffixChars" => "0",
            "agentSandbox" => "container",
            "placementMaxError" => "0.2",
            _ => null,
        });
        Check.True(settings is { CompletionDelayMilliseconds: 120, CompletionSuffixCharacters: 0, AgentSandbox: "container", PlacementMaxError: 0.2, ChatKeep: true },
            "the A2 settings read");
        Check.Throws<ArgumentException>(() => AiSettings.Read(key => key == "placementMaxError" ? "2" : null), "a share above 1 is refused, naming the variable");
        Check.True(AiSettings.Declared.Any(s => s.Key == "completionSuffixChars") && AiSettings.Declared.Any(s => s.Key == "agentSandbox"), "they are declared for the settings page");
        var options = CraftOptions.FromVariables(name => name switch { "CRAFT_INLINE_COMPLETION_PROVIDER" => "none", "CRAFT_PANEL_CHAT_WIDTH" => "400", _ => null });
        Check.True(options is { InlineCompletionProvider: "none", ChatWidth: 400 }, "the editor's options: the provider and the chat column's width");
        var request = new InlineCompletionRequest(TextDocument.Create("x").Buffer.Current, 1, null, "Plain text", null);
        Check.Throws<NotRegisteredException>(() => InlineCompletionProviders.For(request, "a2-missing"), "an unregistered provider is not registered");
    }

    [Test]
    public static async Task AnAgentEngineRunsOnTheCopyStreamsItsStepsAndItsChangesComeAsDiffs()
    {
        string root = Folder();
        var engine = new ScriptedEngine();
        AgentEngines.Register(engine);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\nkeep\n");
            var presenter = new AcceptingPresenter();
            using var host = new EditorHost(dispatch: Serial(), presenter: presenter) { WorkspaceFolder = root };
            host.OpenFile = async (path, token) => File.Exists(path) ? host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null) : host.Open(TextDocument.Create(""), null);
            using var fixture = new ChatFixture(new ScriptedChatModel(new ChatMessage("assistant", "unused")), agentFolder: root + "-copies", agentModel: ScriptedEngine.Model);
            using var panel = new AiAgentPanel(host, () => fixture.Settings, fixture.Models);
            await panel.SubmitAsync("Change alpha");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            var first = engine.Requests.Single();
            Check.True(first.Model == ScriptedEngine.Model && first.Task == "Change alpha" && first.Resume is null && first.Folder != root && first.Workspace == root,
                "the agent role's engine model runs the task on the working copy, a new session");
            Check.True(panel.Entries.Any(e => e is { Role: ConversationRole.Tool, Text: "Bash", Succeeded: true } && e.Detail!.Contains("dotnet build", StringComparison.Ordinal)),
                "a tool call and its result are one step of the timeline");
            Check.True(panel.Entries.Any(e => e is { Role: ConversationRole.Assistant } && e.Text.Contains("Working on it.", StringComparison.Ordinal)), "its text streams as the agent's message");
            Check.True(panel.Changes.Select(c => (c.Relative, c.IsNew)).SequenceEqual([("a.txt", false), ("b.txt", true)]), "what it wrote in the copy is listed: a.txt changed, b.txt new");
            Check.Equal("alpha\nkeep\n", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")), "the user's file is untouched until a diff is accepted");
            Check.True(!File.Exists(Path.Combine(root, "b.txt")), "no file is made silently");
            Check.True(panel.Subtitle == ScriptedEngine.Model && panel.Continues, "the panel names the engine's model and continues its session");
            await panel.SubmitAsync("And the tests");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(engine.Requests[1].Resume == "session-1" && engine.Requests[1].Folder == first.Folder, "a follow-up resumes the engine's session on the same copy");
            Check.True(await panel.ReviewAsync(), "Review diff: each change is proposed and accepted");
            Check.Equal(2, presenter.Proposals.Count, "one diff per file");
            Check.True(AgentEngines.Ask(ScriptedEngine.Model, "") && panel.EngineModel == ScriptedEngine.Model, "a plug-in's Ask is taken by the agent panel, for its next task");
            panel.DiscardAll();
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            AgentEngines.Unregister(engine.Name);
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-copies"))
            {
                Directory.Delete(root + "-copies", recursive: true);
            }
        }
    }

    /// <summary>An agent engine that writes a.txt and b.txt in the copy and reports a text, a tool call and its result.</summary>
    private sealed class ScriptedEngine : IAgentEngine
    {
        public const string Model = "Scripted Engine (work)";

        public List<AgentEngineRequest> Requests { get; } = [];

        public string Name => "test-scripted-engine";

        public string Summary => "the tests' engine";

        public IReadOnlyList<string> Models { get; } = [Model];

        public async Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            report(new AgentEngineEvent(AgentEngineStep.Text, "Working on it."));
            report(new AgentEngineEvent(AgentEngineStep.ToolCall, "Bash") { Id = "t1", Detail = "dotnet build" });
            await File.WriteAllTextAsync(Path.Combine(request.Folder, "a.txt"), "beta\nkeep\n", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(request.Folder, "b.txt"), "new\n", cancellationToken);
            report(new AgentEngineEvent(AgentEngineStep.ToolResult, "Bash") { Id = "t1", Ok = true, Detail = "Build succeeded." });
            return new AgentEngineResult("Done in 2 turns.", "session-1");
        }
    }

    private static string Text(ITextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    private static string Folder()
    {
        string root = TestRunner.RepositoryPath("test-output", "tmp", "a2-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        return root;
    }

    // The host's "UI thread" in a test: actions run one at a time under a lock.
    private static Action<Action> Serial()
    {
        var gate = new Lock();
        return action =>
        {
            lock (gate)
            {
                action();
            }
        };
    }

    private static void Pump(ConcurrentQueue<Action> ui)
    {
        while (ui.TryDequeue(out var action))
        {
            action();
        }
    }

    private static async Task PumpUntil(ConcurrentQueue<Action> ui, Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Pump(ui);
            if (done())
            {
                return;
            }

            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new CheckFailedException($"{what}: not within 10 s");
            }

            await Task.Delay(2);
        }
    }

    private static async Task Until(Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new CheckFailedException($"{what}: not within 10 s");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>Inline completion over a fake model provider (and, when asked, a fill-in-the-middle format its tokenizer has), registered for the test.</summary>
    private sealed class CompletionFixture : IDisposable
    {
        private readonly bool _withFormat;
        private readonly AiModels _models;
        private readonly List<IDisposable> _owned = [];

        public CompletionFixture(ITextModel text, bool withFormat)
        {
            _withFormat = withFormat;
            var tokenizer = new CharacterTokenizer(withFormat ? ["<PRE>", "<SUF>", "<MID>", "<EOT>"] : []);
            if (withFormat)
            {
                FillInMiddleFormats.Register(FillInMiddleLayout.PrefixSuffixMiddle(FormatName, "the A2 tests' format", new FillInMiddleTokens("<PRE>", "<SUF>", "<MID>") { Stop = ["<EOT>"] }));
            }

            ModelProviders.Register(new FakeProvider(null, text, tokenizer));
            Settings = new AiSettings { ModelProvider = ProviderName, Model = "fake", CompletionModel = "fake", CompletionDelayMilliseconds = 20, CompletionMaxTokens = 32 };
            _models = new AiModels(() => Settings);
            Provider = new AiInlineCompletion(() => Settings, _models);
            InlineCompletionProviders.Register(Provider);
        }

        public AiSettings Settings { get; }

        public AiInlineCompletion Provider { get; }

        public (EditorModel Model, TextDocument Document, ConcurrentQueue<Action> Ui) Editor(string text, int caretLine, int column = -1)
        {
            var document = TextDocument.Create(text, new CraftOptions { NewLine = "\n" });
            var model = new EditorModel(document, null, new CraftOptions { NewLine = "\n", InlineCompletionProvider = AiInlineCompletion.ProviderName },
                EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
            var ui = new ConcurrentQueue<Action>();
            model.Dispatch = ui.Enqueue;
            model.UpdateViewport(0, 0, 800, 400);
            var line = document.Buffer.Current.GetLine(caretLine);
            int at = column < 0 ? line.End : line.Start + column;
            model.Select(at, at);
            _owned.Add(model);
            _owned.Add(document);
            return (model, document, ui);
        }

        public void Dispose()
        {
            foreach (var owned in _owned)
            {
                owned.Dispose();
            }

            InlineCompletionProviders.Unregister(Provider.Name);
            ModelProviders.Unregister(ProviderName);
            _models.Dispose();
            if (_withFormat)
            {
                FillInMiddleFormats.Unregister(FormatName);
            }
        }
    }

    /// <summary>The chat model behind a fake provider, with the settings the panels read.</summary>
    private sealed class ChatFixture : IDisposable
    {
        public ChatFixture(IChatModel chat, string agentSandbox = "a2-approve", string agentFolder = "", string agentModel = "")
        {
            ModelProviders.Register(new FakeProvider(chat, null, null));
            EditorToolSets.Register(EditorToolSet.Instance);                     // the tools plug-in's set, which chat offers the model
            Settings = new AiSettings { ModelProvider = ProviderName, Model = "fake", AgentModel = agentModel, AgentSandbox = agentSandbox, AgentFolder = agentFolder, AgentMaxTurns = 8 };
            Models = new AiModels(() => Settings);
        }

        public AiSettings Settings { get; }

        public AiModels Models { get; }

        public void Dispose()
        {
            Models.Dispose();
            ModelProviders.Unregister(ProviderName);
            EditorToolSets.Unregister(EditorToolSets.Editor);
        }
    }

    private sealed class FakeProvider(IChatModel? chat, ITextModel? text, IPretrainedTokenizer? tokenizer) : IModelProvider
    {
        public string Name => ProviderName;

        public string Summary => "the A2 tests' models";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelLease(request.Model, chat, text, 4096, TimeSpan.Zero) { Tokenizer = tokenizer });
    }

    /// <summary>A text model that streams its tokens one by one, checking the cancellation at each step as Idrak's generator does, and counts what it sampled after it.</summary>
    private sealed class FakeTextModel(IReadOnlyList<string> tokens, int stepMilliseconds) : ITextModel
    {
        public List<string> Prompts { get; } = [];

        public List<Call> Calls { get; } = [];

        public async IAsyncEnumerable<GenerationChunk> StreamAsync(string prompt, GenerationOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (Prompts)
            {
                Prompts.Add(prompt);
            }

            var call = new Call();
            lock (Calls)
            {
                Calls.Add(call);
            }

            try
            {
                for (int i = 0; i < tokens.Count && i < options.NumPredict; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        yield break;                       // the generator's check at each step
                    }

                    await Task.Delay(stepMilliseconds, CancellationToken.None);
                    call.Produced++;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        call.AfterCancel++;
                    }

                    yield return new GenerationChunk(tokens[i]) { Generated = i + 1 };
                }

                yield return new GenerationChunk("", true, "stop", new GenerationStats(prompt.Length, TimeSpan.Zero, call.Produced, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), 0)) { Generated = call.Produced };
            }
            finally
            {
                call.Ended = true;
            }
        }

        public sealed class Call
        {
            public int Produced { get; set; }

            public int AfterCancel { get; set; }

            public volatile bool Ended;
        }
    }

    /// <summary>A chat model that answers its script one reply per request, streaming each reply's text a word at a time.</summary>
    private sealed class ScriptedChatModel(params ChatMessage[] replies) : IChatModel
    {
        private int _next;

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var reply = replies[Math.Min(_next++, replies.Length - 1)];
            int generated = 0;
            foreach (var piece in Pieces(reply.Content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new ChatChunk(new ChatDelta(piece, "", [])) { Generated = ++generated };
            }

            yield return new ChatChunk(new ChatDelta("", "", reply.ToolCalls ?? []), true, "stop", reply,
                new GenerationStats(10, TimeSpan.Zero, generated, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0)) { Generated = generated };
        }

        private static IEnumerable<string> Pieces(string text)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ' || text[i] == '\n')
                {
                    yield return text[start..(i + 1)];
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                yield return text[start..];
            }
        }
    }

    /// <summary>A chat model whose reply to each request is computed when it is asked (so a later reply can use what a tool did).</summary>
    private sealed class ComputedChatModel(Func<int, ChatMessage> reply) : IChatModel
    {
        private int _next;

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var message = reply(_next++);
            if (message.Content.Length > 0)
            {
                yield return new ChatChunk(new ChatDelta(message.Content, "", [])) { Generated = 1 };
            }

            yield return new ChatChunk(new ChatDelta("", "", message.ToolCalls ?? []), true, "stop", message,
                new GenerationStats(10, TimeSpan.Zero, 1, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), 0)) { Generated = 1 };
        }
    }

    /// <summary>A sandbox whose every command writes a file where it runs (as <c>dotnet new class</c> would) and build output under obj.</summary>
    private sealed class WritingSandbox(string name) : ICommandSandbox
    {
        public string Name { get; } = name;

        public string Summary => "the B6 test's sandbox: a command writes Made.cs";

        public string? Folder { get; private set; }

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            Folder = directory;
            await File.WriteAllTextAsync(Path.Combine(directory, "Made.cs"), "// made by the command\nclass Made { }\n", cancellationToken);
            Directory.CreateDirectory(Path.Combine(directory, "obj"));
            await File.WriteAllTextAsync(Path.Combine(directory, "obj", "project.assets.json"), "{}", cancellationToken);
            return new CommandResult(0, false, "The template \"Class\" was created successfully.", TimeSpan.FromMilliseconds(1), Refused: false);
        }
    }

    /// <summary>A sandbox that waits for the test's answer to each command (the approve-each sandbox's part) and runs nothing.</summary>
    private sealed class ApprovalSandbox(string name) : ICommandSandbox
    {
        private TaskCompletionSource<bool>? _answer;

        public string Name { get; } = name;

        public string Summary => "the A2 tests' approve-each sandbox";

        public bool Waiting => Volatile.Read(ref _answer) is { Task.IsCompleted: false };

        public int Ran { get; private set; }

        public void Answer(bool allowed) => _answer!.TrySetResult(allowed);

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _answer, answer);
            bool allowed = await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!allowed)
            {
                return new CommandResult(null, false, "Denied by the user.", TimeSpan.Zero, Refused: true);
            }

            Ran++;
            return new CommandResult(0, false, "ok", TimeSpan.FromMilliseconds(1), Refused: false);
        }
    }

    /// <summary>A presenter that accepts every hunk and keeps what it was shown.</summary>
    private sealed class AcceptingPresenter : IEditorPresenter
    {
        public List<EditProposal> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => throw new InvalidOperationException("the host asks per hunk");

        public Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            lock (Proposals)
            {
                Proposals.Add(proposal);
            }

            return Task.FromResult<IReadOnlyList<bool>>(Enumerable.Repeat(true, Math.Max(1, proposal.Diff.Hunks.Count)).ToArray());
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }

    /// <summary>A tokenizer of one id per character, with special tokens (the fill-in-the-middle format's) above them.</summary>
    private sealed class CharacterTokenizer(IReadOnlyList<string> specials) : IPretrainedTokenizer
    {
        private const int Characters = 65536;

        public int VocabularySize => Characters + specials.Count;

        public string Model => "characters";

        public IReadOnlyList<int> Encode(string text)
        {
            var ids = new List<int>(text.Length);
            EncodeRange(text, 0, text.Length, ids);
            return ids;
        }

        public void EncodeRange(string text, int start, int length, List<int> ids)
        {
            for (int i = start; i < start + length; i++)
            {
                ids.Add(text[i]);
            }
        }

        public string Decode(IEnumerable<int> ids) => string.Concat(ids.Select(id => id < Characters ? ((char)id).ToString() : specials[id - Characters]));

        public int? IdOf(string token)
        {
            for (int i = 0; i < specials.Count; i++)
            {
                if (specials[i] == token)
                {
                    return Characters + i;
                }
            }

            return token.Length == 1 ? token[0] : null;
        }

        public bool IsSpecial(int id) => id >= Characters && id < VocabularySize;

        public void PadVocabulary(int size)
        {
        }

        public JsonObject ToJson() => new() { ["model"] = new JsonObject { ["type"] = Model } };

        public void WriteTo(Stream stream) => throw new NotSupportedException();

        public void Save(string path) => throw new NotSupportedException();

        public TokenizerEncoding Encode(string text, string? pair, TokenizerEncodeOptions options) => throw new NotSupportedException();

        public IReadOnlyList<TokenizerEncoding> EncodeBatch(IReadOnlyList<string> texts, IReadOnlyList<string>? pairs, TokenizerEncodeOptions options) => throw new NotSupportedException();
    }
}
