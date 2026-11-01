// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 10 (the chat and the agent redesigned): one selectable transcript across messages (Ctrl+A, Ctrl+C as Markdown,
/// Ctrl+Shift+C plain), the message menu's actions, retry and edit-and-resend, streaming that follows unless the user
/// scrolled up, the agent's timeline from a scripted run with its approval inline, the composer's chips, history, slash
/// commands and mentions, Expand and restore keeping the rail, the side view and the widths, the past conversations with
/// Undo, and only the visible lines laid out with nothing allocated in a warm frame.
/// </summary>
[TestGroup("Conversation")]
public static class ConversationTests
{
    /// <summary>A conversation panel the test fills by hand.</summary>
    private sealed class ScriptedPanel(IEditorHost host, bool timeline = false) : ConversationPanel(host, "test.chat", "Test chat")
    {
        public List<int> Retried { get; } = [];

        public List<(int Index, string Text)> Resent { get; } = [];

        public List<int> Deleted { get; } = [];

        public List<string> Sent { get; } = [];

        public override string Placeholder => "Ask";

        public override IReadOnlyList<PanelAction> HeaderActions => [];

        public override bool ShowsTimeline => timeline;

        public override ConversationCapabilities Capabilities => ConversationCapabilities.Retry | ConversationCapabilities.EditAndResend | ConversationCapabilities.DeleteFrom;

        public override IReadOnlyList<ConversationCommand> SlashCommands { get; } = [new("explain", "Explain it"), new("fix", "Fix it"), new("tests", "Write tests"), new("clear", "Start again")];

        public int Put(ConversationEntry entry) => Add(entry);

        public void Change(int index, ConversationEntry entry) => Update(index, entry);

        public override Task SubmitAsync(string text, CancellationToken cancellationToken = default)
        {
            Sent.Add(text);
            return Task.CompletedTask;
        }

        public override Task RetryFromAsync(int index, CancellationToken cancellationToken = default)
        {
            Retried.Add(index);
            return Task.CompletedTask;
        }

        public override Task EditAndResendAsync(int index, string text, CancellationToken cancellationToken = default)
        {
            Resent.Add((index, text));
            return Task.CompletedTask;
        }

        public override void DeleteFrom(int index)
        {
            Deleted.Add(index);
            ReplaceFrom(index, []);
        }
    }

    private static (EditorHost Host, ScriptedPanel Panel, ConversationController Controller, EditorModel Model, FakeClipboard Clipboard) Fixture(bool timeline = false, CraftOptions? options = null)
    {
        var host = new EditorHost(dispatch: Piece1b.Serial());
        var panel = new ScriptedPanel(host, timeline);
        var clipboard = new FakeClipboard();
        var controller = new ConversationController(host, options ?? new CraftOptions(), clipboard, new UndoBar(5000));
        controller.Panel = panel;
        var model = new EditorModel(controller.Transcript.Document, controller.Transcript.Language, ConversationTranscript.EditorOptions(new CraftOptions()), null, new FixedMeasurer(), clipboard) { IsReadOnly = true, ShowLineNumbers = false };
        controller.Transcript.Attach(model);
        model.UpdateViewport(0, 0, 800, 400);                  // 80 columns of 10, 20 lines of 20
        return (host, panel, controller, model, clipboard);
    }

    private static void Conversation(ScriptedPanel panel)
    {
        panel.Put(new ConversationEntry(ConversationRole.User, "Explain this class"));
        panel.Put(new ConversationEntry(ConversationRole.Assistant, "It is **small** and uses `int`:\n- one field\n- one method") { Header = "coder · cpu" });
        panel.Put(new ConversationEntry(ConversationRole.Code, "int x = 1;\n") { Language = "csharp" });
        panel.Put(new ConversationEntry(ConversationRole.Assistant, "That is all.") { Header = "coder · cpu", Metrics = "12 tokens · 40 tok/s · 0.3 s" });
    }

    private static string All(EditorModel model) => model.Snapshot.GetText(0, model.Snapshot.Length);

    // The view's point of a text position (the scroll taken away).
    private static (float X, float Y) At(EditorModel model, int position)
    {
        var point = model.PointFromPosition(position);
        return (point.X - model.ScrollX + 1, point.Y - model.ScrollY + 1);
    }

    [Test]
    public static async Task SelectionAcrossMessagesAndCtrlACopyAsMarkdownAndPlain()
    {
        var (host, panel, controller, model, clipboard) = Fixture();
        using var _ = host;
        using var __ = controller;
        using var ___ = model;
        Conversation(panel);
        string text = All(model);
        Check.True(text.Contains("Explain this class", StringComparison.Ordinal) && text.Contains("int x = 1;", StringComparison.Ordinal) && text.Contains("• one field", StringComparison.Ordinal),
            $"the four entries are one text: the user's, the model's Markdown rendered, the code block ({text.Length} characters)");
        Check.True(!text.Contains("**small**", StringComparison.Ordinal) && text.Contains("It is small and uses int:", StringComparison.Ordinal), "the inline marks are rendered, not shown");
        Check.Equal(4, controller.Transcript.MessageCount, "a message per entry");
        var (userStart, _) = controller.Transcript.RangeOf(0);
        int bubbleLine = model.Snapshot.GetLineIndex(text.IndexOf("Explain this class", StringComparison.Ordinal));
        Check.Equal(TranscriptLineKind.Bubble, controller.Transcript.KindOfLine(bubbleLine), "the user's message is a bubble");
        Check.True(model.Snapshot.GetLine(bubbleLine).Length > 60, "at the right (padded to the right edge of 80 columns)");

        // Ctrl+A selects the whole conversation; Ctrl+C copies it as Markdown, the speakers named, the code fenced, no chrome.
        model.OnKey(EditorKey.A, KeyModifiers.Control);
        Check.True(model.Selection.Start == 0 && model.Selection.End == model.Snapshot.Length, "Ctrl+A: the whole conversation");
        Check.True(controller.HandleTranscriptKey(model, EditorKey.C, KeyModifiers.Control), "Ctrl+C is the conversation's");
        await controller.LastAction;
        string markdown = clipboard.Text!.ReplaceLineEndings("\n");
        Check.True(markdown.Contains("**You:**\n\nExplain this class", StringComparison.Ordinal) && markdown.Contains("**Assistant (coder · cpu):**", StringComparison.Ordinal),
            $"the speakers named ({markdown})");
        Check.True(markdown.Contains("It is **small** and uses `int`:\n- one field", StringComparison.Ordinal) && markdown.Contains("```csharp\nint x = 1;\n```", StringComparison.Ordinal),
            "each whole message as it was written, the code fenced with its language");
        Check.True(!markdown.Contains("Insert at caret", StringComparison.Ordinal) && !markdown.Contains("tok/s", StringComparison.Ordinal) && !markdown.Contains('⋯'),
            "the chrome (actions, metrics) is never copied");

        // Ctrl+Shift+C: plain text, as shown.
        controller.HandleTranscriptKey(model, EditorKey.C, KeyModifiers.Control | KeyModifiers.Shift);
        await controller.LastAction;
        string plain = clipboard.Text!.ReplaceLineEndings("\n");
        Check.True(plain.Contains("Explain this class", StringComparison.Ordinal) && plain.Contains("It is small and uses int:", StringComparison.Ordinal) && plain.Contains("int x = 1;", StringComparison.Ordinal),
            $"plain: the text shown ({plain})");
        Check.True(!plain.Contains("```", StringComparison.Ordinal) && !plain.Contains("**", StringComparison.Ordinal) && !plain.Contains("Copy", StringComparison.Ordinal), "plain: no fences, no marks, no chrome");

        // A drag from the user's message into the last reply selects across them; Shift+click extends.
        int from = text.IndexOf("this class", StringComparison.Ordinal);
        int to = text.IndexOf("That is", StringComparison.Ordinal) + 4;
        var (x1, y1) = At(model, from);
        var (x2, y2) = At(model, to);
        model.PointerPressed(x1, y1, KeyModifiers.None);
        model.PointerMoved(x2, y2);
        model.PointerReleased();
        Check.True(model.Selection.Start == from && model.Selection.End == to, $"the drag selects across four messages ({model.Selection.Start}-{model.Selection.End}, wanted {from}-{to})");
        await controller.CopySelectionAsync(model, markdown: true);
        string part = clipboard.Text!.ReplaceLineEndings("\n");
        Check.True(part.StartsWith("**You:**\n\nthis class", StringComparison.Ordinal) && part.EndsWith("That", StringComparison.Ordinal) && part.Contains("```csharp\nint x = 1;\n```", StringComparison.Ordinal),
            $"a part of a message copies what was selected of it ({part})");
        int further = text.IndexOf("all.", StringComparison.Ordinal) + 4;
        var (x3, y3) = At(model, further);
        model.PointerPressed(x3, y3, KeyModifiers.Shift);
        model.PointerReleased();
        Check.True(model.Selection.Start == from && model.Selection.End == further, "Shift+click extends the selection");
        Check.True(userStart == 0, "the first message starts the text");
    }

    [Test]
    public static async Task TheMessageMenusActionsAndAClickOnCopy()
    {
        var (host, panel, controller, model, clipboard) = Fixture();
        using var _ = host;
        using var __ = controller;
        using var ___ = model;
        using var commands = new ConversationCommands();
        Conversation(panel);
        var menu = new MenuModel(host);
        Check.True(menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 1))), "the message menu opens");
        string titles = string.Join(", ", menu.Rows.Select(r => r.Title + (r.Enabled ? "" : " (off)")));
        Check.Equal("Copy the message, Copy the message as Markdown, Quote in reply, Retry from here, Edit and resend (off), Delete from here", titles,
            "Copy, Copy as Markdown, Quote in reply, Retry from here, Edit and resend (the user's only), Delete from here; no checkboxes");
        await menu.RunAsync(0);
        Check.Equal("It is small and uses int:\n• one field\n• one method", clipboard.Text!.ReplaceLineEndings("\n"), "Copy: the message as shown");
        menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 1)));
        await menu.RunAsync(1);
        Check.Equal("It is **small** and uses `int`:\n- one field\n- one method", clipboard.Text!.ReplaceLineEndings("\n"), "Copy as Markdown: as written");
        menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 3)));
        await menu.RunAsync(2);
        Check.Equal("> That is all.\n\n", controller.Composer.Text, "Quote in reply: the message quoted at the composer's start");
        menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 3)));
        await menu.RunAsync(3);
        Check.True(panel.Retried.SequenceEqual([3]), "Retry from here reaches the panel");
        menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 0)));
        Check.True(menu.Rows[4].Enabled, "Edit and resend on the user's message");
        await menu.RunAsync(4);
        Check.True(controller.Composer.EditingEntry == 0 && controller.Composer.Text == "Explain this class", "Edit and resend: the message back in the composer");
        controller.Composer.Typed("Explain this record", 19);
        await controller.Composer.SendAsync();
        Check.True(panel.Resent.SequenceEqual([(0, "Explain this record")]) && panel.Sent.Count == 0 && controller.Composer.EditingEntry == -1, "Send replaces the edited message (EditAndResendAsync)");
        menu.Open(EditorContextMenus.Conversation, new ContextMenuTarget(EditorContextMenus.Conversation, null, false, new ConversationMessage(controller, 2)));
        await menu.RunAsync(5);
        Check.True(panel.Deleted.SequenceEqual([2]) && panel.Entries.Count == 2 && controller.Transcript.MessageCount == 2, "Delete from here drops it and what came after, and the transcript follows");

        // A click on a message's own "Copy" (shown on hover) copies it.
        string text = All(model);
        int copyAt = text.IndexOf("Copy", StringComparison.Ordinal);          // the user's message's meta line
        Check.True(controller.Transcript.ActionAt(copyAt + 1) is { Kind: TranscriptActionKind.CopyMessage, Entry: 0 }, "the meta line holds the message's Copy");
        clipboard.Text = null;
        var (x, y) = At(model, copyAt + 1);
        model.PointerHovered(x, y);
        Check.Equal(0, controller.Transcript.Hovered, "hovering a message shows its actions");
        model.PointerPressed(x, y, KeyModifiers.None);
        model.PointerReleased();
        await controller.LastAction;
        Check.Equal("Explain this class", clipboard.Text, "a click on Copy copies the message");
        commands.Dispose();
        Check.True(EditorCommands.Find(ConversationCommandIds.Retry) is null, "the commands go with their registration");
    }

    [Test]
    public static async Task RetryAndEditAndResendAndDeleteFromHereInTheChat()
    {
        using var host = new EditorHost(dispatch: Piece1b.Serial());
        var chat = FakeChatModel.Script(FakeChatModel.Answer("first"), FakeChatModel.Answer("second"), FakeChatModel.Answer("third"), FakeChatModel.Answer("fourth"));
        using var provider = new Piece1b.Provider(chat);
        var settings = Piece1b.Settings(Path.GetTempPath());
        using var models = new AiModels(() => settings);
        using var panel = new AiChatPanel(host, () => settings, models);
        await panel.AskAsync("a");
        await panel.AskAsync("b");
        Check.True(panel.History.Select(m => m.Content).SequenceEqual(["a", "first", "b", "second"]), "two exchanges");
        var reply = panel.Entries.Last(e => e.Role == ConversationRole.Assistant);
        Check.True(reply.Metrics is { } metrics && metrics.Contains("tokens", StringComparison.Ordinal) && metrics.EndsWith(" s", StringComparison.Ordinal) && !reply.IsStreaming,
            $"the rate and the time under a finished reply ({reply.Metrics})");
        Check.True(panel.Entries.Where(e => e.Role == ConversationRole.Assistant).All(e => e.Header is { Length: > 0 }), "the model's name small above");
        int b = panel.Entries.ToList().FindIndex(e => e is { Role: ConversationRole.User, Text: "b" });
        await panel.RetryFromAsync(b + 1);
        Check.True(panel.History.Select(m => m.Content).SequenceEqual(["a", "first", "b", "third"]), $"Retry from the reply asks b again ({string.Join(", ", panel.History.Select(m => m.Content))})");
        Check.True(!chat.Requests[2].Messages.Any(m => m.Content == "second"), "the retried reply is not sent back");
        await panel.EditAndResendAsync(0, "a2");
        Check.True(panel.History.Select(m => m.Content).SequenceEqual(["a2", "fourth"]) && panel.Entries[0].Text == "a2", "Edit and resend replaces a and drops what came after it");
        panel.DeleteFrom(1);
        Check.True(panel.Entries.Count == 1 && panel.Entries[0].Text == "a2" && panel.History.Count == 0, "Delete from the reply: the message stays unanswered, nothing goes back to the model");
        panel.DeleteFrom(0);
        Check.True(panel.Entries.Count == 0, "Delete from the message: all of it");
        Check.True(panel.SlashCommands.Select(c => c.Name).SequenceEqual(["explain", "fix", "doc", "refactor", "tests", "clear"]), "the slash commands listed as typed");
    }

    [Test]
    public static void StreamingFollowsUnlessScrolledUpAndJumpToLatest()
    {
        var (host, panel, controller, model, _) = Fixture();
        using var _h = host;
        using var _c = controller;
        using var _m = model;
        var scrolls = new List<float>();
        model.ScrollRequested += (_, e) => scrolls.Add(e.Y);
        for (int i = 0; i < 20; i++)
        {
            panel.Put(new ConversationEntry(ConversationRole.User, $"question {i}"));
            panel.Put(new ConversationEntry(ConversationRole.Assistant, $"answer {i}"));
        }

        int streaming = panel.Put(new ConversationEntry(ConversationRole.Assistant, "It") { IsStreaming = true });
        float end = model.ContentHeight - model.ViewportHeight;
        Check.True(controller.Transcript.Following && scrolls[^1] == end, $"following the newest text ({scrolls[^1]}, the end {end})");
        model.UpdateViewport(0, end, 800, 400);
        int written = controller.Transcript.MessagesWritten;
        for (int i = 0; i < 10; i++)
        {
            panel.Change(streaming, new ConversationEntry(ConversationRole.Assistant, "It grows " + new string('x', i * 30) + "\nmore " + i) { IsStreaming = true });
            end = model.ContentHeight - model.ViewportHeight;
            model.UpdateViewport(0, scrolls[^1], 800, 400);
        }

        Check.Equal(written + 10, controller.Transcript.MessagesWritten, "a streamed piece writes only its own message again");
        Check.True(scrolls[^1] == end, "each piece is followed");
        model.UpdateViewport(0, end - 200, 800, 400);         // the user scrolls up to read
        Check.True(!controller.Transcript.Following, "scrolled up: not following");
        int before = scrolls.Count;
        panel.Change(streaming, new ConversationEntry(ConversationRole.Assistant, "It grows still\nand more\nand more") { IsStreaming = true });
        Check.True(scrolls.Count == before && controller.Transcript.ShowsJump, "the view stays; \"Jump to latest\" shows");
        controller.Transcript.JumpToLatest();
        Check.True(controller.Transcript.Following && !controller.Transcript.ShowsJump && scrolls[^1] == model.ContentHeight - model.ViewportHeight, "Jump to latest: at the end, following");
        string text = All(model);
        Check.True(text.TrimEnd().EndsWith("and more", StringComparison.Ordinal), "the newest text at the end");
    }

    /// <summary>A sandbox that asks the user (through the host, as the approve-each sandbox does) and runs nothing.</summary>
    private sealed class AskingSandbox(IEditorHost host) : ICommandSandbox
    {
        public const string SandboxName = "x10-ask";

        public List<string> Ran { get; } = [];

        public string Name => SandboxName;

        public string Summary => "asks, then pretends";

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            string line = string.Join(' ', command);
            var asked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Dispatch(() => _ = host.AskAsync(new EditorQuestion("The agent wants to run a command", line, ["Allow once", "Deny and tell the agent"])
            {
                Details = ["Program: git"], Dismissed = 1, Kind = EditorQuestionKinds.Command,
            }, cancellationToken).ContinueWith(t => asked.TrySetResult(t.IsCompletedSuccessfully ? t.Result : 1), TaskScheduler.Default));
            int answer = await asked.Task.ConfigureAwait(false);
            if (answer != 0)
            {
                return new CommandResult(null, false, "denied", TimeSpan.Zero, true);
            }

            Ran.Add(line);
            return new CommandResult(0, false, "On branch main\nnothing to commit", TimeSpan.FromMilliseconds(5), false);
        }
    }

    [Test]
    public static async Task TheAgentsTimelineFromAScriptedRunWithTheApprovalInline()
    {
        string root = Piece1b.Folder("x10-agent");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\n");
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            var sandbox = new AskingSandbox(host);
            CommandSandboxes.Register(sandbox);
            var chat = FakeChatModel.Script(
                FakeChatModel.ToolCall("read_file", new JsonObject { ["path"] = "a.txt" }),
                FakeChatModel.ToolCall("run_command", new JsonObject { ["command"] = "git status" }),
                FakeChatModel.ToolCall("write_file", new JsonObject { ["path"] = "b.txt", ["content"] = "beta\n" }),
                FakeChatModel.Answer("Wrote b.txt."));
            using var provider = new Piece1b.Provider(chat);
            var settings = Piece1b.Settings(root + "-copies") with { AgentSandbox = AskingSandbox.SandboxName };
            using var models = new AiModels(() => settings);
            using var panel = new AiAgentPanel(host, () => settings, models);
            host.AddPanel(panel);
            using var controller = new ConversationController(host, new CraftOptions(), new FakeClipboard());
            controller.Panel = panel;
            Check.True(panel.ShowsTimeline, "the agent is drawn as a timeline");
            await panel.SubmitAsync("Add b.txt");
            await Piece1b.Until(() => panel.Entries.Any(e => e.Role == ConversationRole.Approval && e.Actions.Count == 2), "the command waits with Approve and Deny inline");
            var waiting = panel.Entries.First(e => e.Role == ConversationRole.Approval);
            Check.True(waiting.Text == "git status" && waiting.Actions[0] is { Label: "Allow once", Primary: true } && waiting.Details == "Program: git",
                "the step shows the command, its facts and the sandbox's answers (no dialog)");
            string Shown() => controller.Transcript.Document.Buffer.Current.GetText(0, controller.Transcript.Document.Buffer.Current.Length);
            await Piece1b.Until(() => Shown() is var shown && shown.Contains("● Waiting for you: git status", StringComparison.Ordinal) && shown.Contains("Allow once", StringComparison.Ordinal),
                "the transcript shows the command waiting with its answers");
            string text;
            await waiting.Actions[0].RunAsync(CancellationToken.None);
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(sandbox.Ran.SequenceEqual(["git status"]), "approved inline, the command ran");
            var entries = panel.Entries;
            Check.True(entries.Any(e => e is { Role: ConversationRole.Tool, Text: "read_file", Succeeded: true } && e.Details!.Contains("Arguments: {\"path\":\"a.txt\"}", StringComparison.Ordinal) && e.Details.Contains("alpha", StringComparison.Ordinal)),
                "each tool call a step with its arguments and output to open");
            Check.True(entries.Any(e => e.Role == ConversationRole.Tool && e.Text == "run_command git status" && e.Details!.Contains("nothing to commit", StringComparison.Ordinal)), "the command's step with its output");
            Check.True(entries.Any(e => e is { Role: ConversationRole.File, Text: "b.txt" }) && entries.Any(e => e is { Role: ConversationRole.Section, Text: "CHANGED FILES" }), "the files changed with Review");
            Check.True(entries.Any(e => e.Role == ConversationRole.Note && e.Text.StartsWith("Done", StringComparison.Ordinal)), "the end in plain words");
            Check.True(entries.Where(e => e.Role == ConversationRole.Assistant).All(e => !e.IsStreaming), "no caret left once the run ended");

            // The timeline: a tool's step opens to show its details, and closes again.
            text = controller.Transcript.Document.Buffer.Current.GetText(0, controller.Transcript.Document.Buffer.Current.Length);
            int step = text.IndexOf("▸ ✓ read_file", StringComparison.Ordinal);
            Check.True(step >= 0 && !text.Contains("Arguments: {\"path\":\"a.txt\"}", StringComparison.Ordinal), $"the step shows collapsed to its line ({text})");
            var toggle = controller.Transcript.ActionAt(step);
            Check.True(toggle is { Kind: TranscriptActionKind.Toggle }, "its toggle");
            await controller.RunAsync(toggle!);
            text = controller.Transcript.Document.Buffer.Current.GetText(0, controller.Transcript.Document.Buffer.Current.Length);
            Check.True(text.Contains("▾ ✓ read_file", StringComparison.Ordinal) && text.Contains("Arguments: {\"path\":\"a.txt\"}", StringComparison.Ordinal), "opened: its arguments and output");
            await controller.RunAsync(toggle!);
            text = controller.Transcript.Document.Buffer.Current.GetText(0, controller.Transcript.Document.Buffer.Current.Length);
            Check.True(!text.Contains("Arguments: {\"path\":\"a.txt\"}", StringComparison.Ordinal), "closed again");
            CommandSandboxes.Unregister(AskingSandbox.SandboxName);
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task TheComposersChipsHistorySlashCommandsAndMentions()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        Directory.CreateDirectory(folder.File("obj"));
        await File.WriteAllTextAsync(folder.File("src/HouseBuilder.cs"), "class HouseBuilder {}\n");
        await File.WriteAllTextAsync(folder.File("obj/HouseBuilder.g.cs"), "generated\n");
        await File.WriteAllTextAsync(folder.File("notes.txt"), "text\n");
        var (host, panel, controller, model, _) = Fixture(options: new CraftOptions { ComposerMaxLines = 3 });
        using var _h = host;
        using var _c = controller;
        using var _m = model;
        var composer = controller.Composer;
        composer.WorkspaceFolder = () => folder.Path;
        composer.Symbols = () => [new ComposerSuggestion(ComposerSuggestionKind.Symbol, "House", "@House ", "class in House.cs")];

        composer.Typed("/te", 3);
        Check.True(composer.Suggestions.Select(s => s.Label).SequenceEqual(["/tests"]), $"\"/te\" lists /tests ({string.Join(", ", composer.Suggestions.Select(s => s.Label))})");
        composer.Typed("/", 1);
        Check.True(composer.Suggestions.Select(s => s.Label).SequenceEqual(["/explain", "/fix", "/tests", "/clear"]), "\"/\" lists every slash command");
        Check.True(composer.HandleKey(EditorKey.Down, KeyModifiers.None) && composer.Selected == 1, "Down moves in the picker");
        Check.True(composer.HandleKey(EditorKey.Tab, KeyModifiers.None) && composer.Text == "/fix " && composer.Suggestions.Count == 0, "Tab takes it");

        composer.Typed("look at @House", 14);
        await composer.Listing;
        Check.True(composer.Suggestions.Any(s => s is { Kind: ComposerSuggestionKind.Symbol, Label: "House" }) && composer.Suggestions.Any(s => s is { Kind: ComposerSuggestionKind.File, Label: "HouseBuilder.cs", Insert: "@src/HouseBuilder.cs " }),
            $"\"@\" lists the symbols and the workspace's files ({string.Join(", ", composer.Suggestions.Select(s => s.Label))})");
        Check.True(!composer.Suggestions.Any(s => s.Label.Contains(".g.", StringComparison.Ordinal)), "the ignored folders' files are not listed");
        composer.Accept(composer.Suggestions.ToList().FindIndex(s => s.Kind == ComposerSuggestionKind.File));
        Check.Equal("look at @src/HouseBuilder.cs ", composer.Text, "the picked file replaces the typed word");
        Check.True(composer.HandleKey(EditorKey.Escape, KeyModifiers.None) is false, "Escape with the picker closed is the composer's binding's (leave)");

        await composer.SendAsync();
        composer.Typed("second", 6);
        await composer.SendAsync();
        Check.True(panel.Sent.SequenceEqual(["look at @src/HouseBuilder.cs", "second"]) && composer.Text.Length == 0, "Send gives the panel the message and empties the box");
        Check.True(composer.HandleKey(EditorKey.Up, KeyModifiers.None) && composer.Text == "second", "Up in the empty box: the last sent");
        Check.True(composer.HandleKey(EditorKey.Up, KeyModifiers.None) && composer.Text == "look at @src/HouseBuilder.cs", "Up again: the one before");
        Check.True(composer.HandleKey(EditorKey.Down, KeyModifiers.None) && composer.Text == "second", "Down: back");
        Check.True(composer.HandleKey(EditorKey.Down, KeyModifiers.None) && composer.Text.Length == 0, "Down past the newest: the draft (empty)");
        composer.Typed("typed", 5);
        Check.True(!composer.HandleKey(EditorKey.Up, KeyModifiers.None), "with text, Up is the box's");
        composer.Typed("1\n2\n3\n4\n5", 9);
        Check.True(composer.VisibleLines == 3 && composer.Scrolls, "the box grows to CRAFT_COMPOSER_MAX_LINES, then scrolls");

        // The chips: a dropped file becomes a chip; a tap removes one.
        using var chatHost = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = folder.Path };
        var chat = FakeChatModel.Script(FakeChatModel.Answer("ok"));
        using var provider = new Piece1b.Provider(chat);
        var settings = Piece1b.Settings(Path.GetTempPath()) with { ChatKeep = false };
        using var models = new AiModels(() => settings);
        using var chatPanel = new AiChatPanel(chatHost, () => settings, models);
        int taken = await chatPanel.AcceptDropAsync([folder.File("notes.txt")]);
        Check.True(taken == 1 && chatPanel.Chips.Any(c => c.Id.StartsWith("path:", StringComparison.Ordinal) && c.Text == "notes.txt"), "a dropped file is a context chip");
        chatPanel.RemoveChip(chatPanel.Chips.Single().Id);
        Check.True(chatPanel.Chips.Count == 0, "a tap removes it");
        Check.True(ComposerOptions.From(new CraftOptions()).MaxLines == 12 && CraftOptions.FromVariables(n => n == "CRAFT_COMPOSER_MAX_LINES" ? "5" : null).ComposerMaxLines == 5,
            "CRAFT_COMPOSER_MAX_LINES: 12 by default");
    }

    [Test]
    public static async Task ExpandAndRestoreKeepTheRailTheSideViewAndTheWidths()
    {
        using var folder = new TempFolder();
        var file = SettingsFile.Load(folder.File("data/settings.json"));
        var settings = new CraftSettings(_ => null, file);
        var layout = new PanelLayout(settings, new CraftOptions());
        double side = layout.SizeOf(ShellPanel.Explorer);
        double chat = layout.SizeOf(ShellPanel.Chat);
        bool explorer = layout.IsOpen(ShellPanel.Explorer);
        Check.True(!layout.IsChatExpanded(narrow: false, editorRoom: 900), "a wide window shows the column by default");
        Check.True(layout.IsChatExpanded(narrow: true, editorRoom: 0), "a narrow window shows it expanded by default");
        await layout.ToggleChatExpandedAsync(narrow: false, editorRoom: 900);
        Check.True(layout.ChatExpanded && layout.IsChatExpanded(false, 900), "Expand");
        Check.True(layout.SizeOf(ShellPanel.Explorer) == side && layout.SizeOf(ShellPanel.Chat) == chat && layout.IsOpen(ShellPanel.Explorer) == explorer,
            "the rail's side view and the column keep their state and widths");
        await layout.LastSave;
        Check.True((await File.ReadAllTextAsync(folder.File("data/settings.json"))).Contains("CRAFT_CHAT_EXPANDED", StringComparison.Ordinal), "kept in the settings (per workspace when a folder is open)");
        Check.Equal((308.0, 1092.0), PanelLayout.ExpandedArea(1400, 48, sideOpen: true, sideWidth: 260), "between the rail and side view and the window's right edge");
        Check.Equal((48.0, 1352.0), PanelLayout.ExpandedArea(1400, 48, sideOpen: false, sideWidth: 260), "the side view closed: from the rail");
        Check.Equal((348.0, 1052.0), PanelLayout.ExpandedArea(1400, 48, sideOpen: true, sideWidth: 300), "it follows the side view's width");
        await layout.ToggleChatExpandedAsync(narrow: false, editorRoom: 900);
        Check.True(!layout.ChatExpanded && layout.SizeOf(ShellPanel.Chat) == chat, "restored: the column at its kept width");
        await layout.ToggleChatExpandedAsync(narrow: true, editorRoom: 0);
        Check.True(!layout.IsChatExpanded(true, 0), "the narrow window's expanded view restores too");
        Check.True(layout.IsChatExpanded(false, layout.MinimumSize - 1), "no room for the editor beside the column: expanded");
        await layout.ToggleChatExpandedAsync(narrow: false, editorRoom: layout.MinimumSize - 1);
        Check.True(!layout.IsChatExpanded(false, layout.MinimumSize - 1) && !layout.ChatExpanded, "restored for this run, nothing written");
        layout.Load(new CraftOptions { ChatExpanded = true });
        Check.True(layout.ChatExpanded, "CRAFT_CHAT_EXPANDED read back");
        using (var commands = new ConversationCommands(expand: () => Task.CompletedTask))
        {
            var binding = EditorKeyBindings.Find(ConversationCommands.ExpandBinding.Chord);
            Check.True(binding is { CommandId: ConversationCommandIds.Expand, When: KeyContext.Anywhere } && binding.Chord.ToString() == "Ctrl+Shift+Alt+M", "Chat: Expand on Ctrl+Shift+Alt+M, anywhere, registered with its command");
            Check.True(!StandardShortcuts.VisualStudioKeyMap.Bindings.Any(b => b.Chord.ToString() == "Ctrl+Shift+Alt+M"), "the Visual Studio keymap keeps it (both keymaps)");
        }

        Check.True(EditorKeyBindings.Find(ConversationCommands.ExpandBinding.Chord) is null, "it goes with its command");
        var read = CraftOptions.FromVariables(n => n switch
        {
            "CRAFT_CHAT_EXPANDED" => "true", "CRAFT_CHAT_COPY_FORMAT" => "plain", "CRAFT_CHAT_FOLD_CODE_LINES" => "5", "CRAFT_CHAT_SHOW_METRICS" => "false", _ => null,
        });
        Check.True(read is { ChatExpanded: true, ChatCopyFormat: "plain", ChatFoldCodeLines: 5, ChatShowMetrics: false }, "the options read their variables");
    }

    [Test]
    public static async Task ThePastConversationsWithDeleteAndUndo()
    {
        using var folder = new TempFolder();
        using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = folder.Path };
        var chat = FakeChatModel.Script(FakeChatModel.Answer("about houses"), FakeChatModel.Answer("about boats"));
        using var provider = new Piece1b.Provider(chat);
        var settings = Piece1b.Settings(Path.GetTempPath());
        using var models = new AiModels(() => settings);
        var panel = new AiChatPanel(host, () => settings, models);
        using var undo = new UndoBar(60_000);
        var list = new ConversationList(undo) { History = panel };
        await panel.AskAsync("Tell me about houses");
        list.New();
        Check.True(panel.Entries.Count == 0 && list.Rows.Count == 1, "New: an empty conversation; the first stays in the list");
        await panel.AskAsync("Tell me about boats");
        Check.True(list.Rows.Select(r => r.Title).SequenceEqual(["Tell me about boats", "Tell me about houses"]), "two, the most recent first, titled by their first message");
        list.Search = "HOUSE";
        Check.True(list.Rows.Single().Title == "Tell me about houses", "the search ignores case");
        list.Search = "";
        string houses = list.Rows[1].Id;
        list.Rename(houses, "Houses");
        Check.True(list.Rows.Any(r => r.Title == "Houses"), "Rename");
        Check.True(list.Open(houses) && panel.Entries[0].Text == "Tell me about houses" && panel.History.Count == 2, "Open shows it, its exchanges back");
        Check.True(list.Delete(houses) && list.Rows.Count == 1 && undo.Current?.Text == "Deleted \"Houses\"", "Delete, with Undo on the bar");
        Check.True(panel.Entries[0].Text == "Tell me about boats", "the next one shows");
        Check.True(undo.Undo() && list.Rows.Count == 2, "Undo brings it back");
        await panel.Kept.WaitAsync(TimeSpan.FromSeconds(10));
        Check.True(ConversationList.When(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now) == "5 min ago", "the time as said");
        panel.Dispose();
        using var again = new AiChatPanel(host, () => settings, models);
        await again.KeptRead.WaitAsync(TimeSpan.FromSeconds(10));     // read on the thread pool, shown through the dispatcher
        Check.True(again.Conversations.Count == 2 && again.Conversations.Any(c => c.Title == "Houses") && again.Entries.Count > 0,
            $"kept in .craft/chat.json and read back ({again.Conversations.Count})");
        string kept = await File.ReadAllTextAsync(Path.Combine(folder.Path, ".craft", "chat.json"));
        Check.True(kept.Contains("\"version\":2", StringComparison.Ordinal) && kept.Contains("\"conversations\"", StringComparison.Ordinal), "the file holds every conversation");
    }

    [Test]
    public static void OnlyVisibleLinesAreLaidOutAndAWarmFrameAllocatesNothing()
    {
        var (host, panel, controller, model, _) = Fixture();
        using var _h = host;
        using var _c = controller;
        using var _m = model;
        for (int i = 0; i < 300; i++)
        {
            panel.Put(new ConversationEntry(ConversationRole.User, $"Question {i}: what does the method do?"));
            panel.Put(new ConversationEntry(ConversationRole.Assistant, $"## Answer {i}\nIt **adds** `one` to the count.\n- first\n- second\n\n| a | b |\n|---|---|\n| 1 | 2 |") { Header = "coder · cpu" });
            panel.Put(new ConversationEntry(ConversationRole.Code, $"int Add{i}(int x) => x + 1;\n") { Language = "plaintext", Metrics = "10 tokens · 40 tok/s · 0.2 s" });
        }

        int lines = model.Snapshot.LineCount;
        using var renderer = new EditorRenderer();
        using var canvas = new LayoutCanvas { Record = false };
        float y = model.ContentHeight / 2;
        model.UpdateViewport(0, y, 800, 400);
        renderer.Draw(canvas, model, 0, y, 800, 400, caretVisible: true);
        Check.True(renderer.LinesDrawn <= 22 && canvas.Surface.LinesLaid <= renderer.LinesDrawn, $"only the shown lines drawn and laid out ({renderer.LinesDrawn} drawn, {canvas.Surface.LinesLaid} laid out, of {lines})");
        for (int i = 0; i < 300; i++)
        {
            renderer.Draw(canvas, model, 0, y, 800, 400, (i & 1) == 0);
        }

        long laid = canvas.Surface.LinesLaid + canvas.Surface.RunsLaid;
        const int frames = 1000;
        long steady = 0;
        int gcs = GC.CollectionCount(0);
        var times = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            int before = GC.CollectionCount(0);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, y, 800, 400, (i & 1) == 0);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            long used = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (GC.CollectionCount(0) == before && before == gcs)
            {
                steady += used;
            }

            gcs = GC.CollectionCount(0);
        }

        Array.Sort(times);
        Console.WriteLine($"    conversation of {panel.Entries.Count} entries, {lines} lines: warm frame median {times[frames / 2]:F1} µs, p99 {times[frames * 99 / 100]:F1} µs");
        Check.Equal(laid, canvas.Surface.LinesLaid + canvas.Surface.RunsLaid, "a warm frame lays nothing out");
        Check.Equal(0L, steady, "a warm frame allocates nothing (rule 63)");
        controller.Transcript.SetHovered(1);
        renderer.Draw(canvas, model, 0, y, 800, 400, caretVisible: true);
        Check.True(canvas.Surface.LinesLaid + canvas.Surface.RunsLaid - laid <= 2, "a hover lays out at most its message's action line again");
    }
}
