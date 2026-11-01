// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 9 item 23, the inline chat (Ctrl+I): a box in the editor at the caret's line or over the selection, its request
/// with the lines, the file around them and their problems to the chat role's loaded model (Idrak's scripted fake chat
/// model here, through the AI plug-in's "ai" provider), the answer as a ghost diff in place, Accept one undo step,
/// Discard leaving the text, Retry and a follow-up refining the same edit, the box's history, and no model loaded said
/// with the load only on the user's press.
/// </summary>
[TestGroup("InlineChat")]
public static class InlineChatTests
{
    private const string ProviderName = "inline-chat-fake";
    private const string Source = "class A\n{\n    int x;\n    int z;\n}\n";

    [Test]
    public static async Task TheAnswerStreamsAsAGhostDiffAndAcceptIsOneUndoStep()
    {
        using var fixture = new Fixture(FakeChatModel.Script(FakeChatModel.Answer("Here it is:\n```csharp\n    int y;\n```\nDone.")));
        await fixture.LoadAsync();
        var (model, document, ui) = fixture.Editor(Source);
        model.Select(Source.IndexOf("int x", StringComparison.Ordinal), Source.IndexOf("int x", StringComparison.Ordinal));
        Check.True(model.OpenInlineChat() && model.InlineChat is { IsOpen: true, HasFocus: true, State: InlineChatState.Input, OverSelection: false }, "Ctrl+I opens the box at the caret's line, with the keyboard");
        Check.Equal("    int x;\n", model.InlineChat.Original, "the caret's whole line is what the answer replaces");
        model.OnText("rename x to y");
        Check.Equal(Source, Text(document), "typing goes into the box, not the document");
        Check.True(model.OnKey(EditorKey.Enter, KeyModifiers.None), "Enter sends");
        await PumpUntil(ui, () => model.InlineChat.State == InlineChatState.Answered, "the answer comes back");

        var request = fixture.Chat.Requests.Single();
        string asked = request.Messages[^1].Content;
        Check.True(request.Messages[0].Role == "system" && asked.Contains("    int x;", StringComparison.Ordinal) && asked.Contains("rename x to y", StringComparison.Ordinal)
                   && asked.Contains("class A", StringComparison.Ordinal) && asked.Contains("int z;", StringComparison.Ordinal),
            $"the lines, the file around them and the instruction go to the model ({asked})");
        Check.True(request.Options is { Temperature: 0.25f, NumPredict: 333 }, "with the chat role's temperature and reply limit");
        Check.Equal("    int y;\n", model.InlineChat.Answer, "the block's code is the answer (the prose and fences left out)");
        Check.True(model.InlineChat.Rows is [{ Kind: InlineChatRowKind.Removed, Text: "    int x;" }, { Kind: InlineChatRowKind.Added, Text: "    int y;" }]
                   && model.InlineChat is { AddedLines: 1, RemovedLines: 1 }, "the ghost diff: the old line removed, the new one added");
        Check.Equal(Source, Text(document), "the text is unchanged until Accept");

        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var colors = EditorThemes.Get(EditorThemes.Dark).Colors;
        Check.True(renderer is { InlineChatRowsDrawn: 2, InlineChatBoxDrawn: true }, "the ghost rows and the box are drawn");
        Check.True(canvas.Calls.Any(c => c is { Kind: "fill", Argb: var added } && added == colors.DiffAdded) && canvas.Calls.Any(c => c is { Kind: "fill", Argb: var removed } && removed == colors.DiffRemoved)
                   && canvas.Calls.Any(c => c is { Kind: "line", Argb: var strike } && strike == colors.Error), "added on the added fill, removed on the removed fill and struck through");
        Check.True(canvas.Texts.Any(t => t.Text == "Accept") && canvas.Texts.Any(t => t.Text == "Discard") && canvas.Texts.Any(t => t.Text == "Retry"), "Accept, Discard and Retry in the box");

        Check.True(model.OnKey(EditorKey.Enter, KeyModifiers.Control), "Ctrl+Enter accepts");
        Check.Equal("class A\n{\n    int y;\n    int z;\n}\n", Text(document), "applied in place");
        Check.True(!model.InlineChat.IsOpen && model.InlineChat.Accepted == 1, "the box closes");
        document.Undo();
        Check.True(Text(document) == Source && !document.CanUndo, "one undo step takes it back, and only it");
    }

    [Test]
    public static async Task DiscardLeavesTheTextRetryAndAFollowUpRefineTheSameEdit()
    {
        using var fixture = new Fixture(FakeChatModel.Script(
            FakeChatModel.Answer("```\n    int y;\n    int z;\n```"),
            FakeChatModel.Answer("```\n    readonly int y;\n    int z;\n```"),
            FakeChatModel.Answer("```\n    readonly int y = 1;\n    int z;\n```")));
        await fixture.LoadAsync();
        var (model, document, ui) = fixture.Editor(Source);
        int start = Source.IndexOf("    int x", StringComparison.Ordinal);
        model.Select(start, Source.IndexOf('}', StringComparison.Ordinal));
        Check.True(model.OpenInlineChat() && model.InlineChat.OverSelection && model.InlineChat.Original == "    int x;\n    int z;\n", "on a selection: its whole lines, the box over them");
        model.OnText("rename x");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        await PumpUntil(ui, () => model.InlineChat.State == InlineChatState.Answered, "the first answer");
        model.OnText("and make it readonly");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        await PumpUntil(ui, () => model.InlineChat is { State: InlineChatState.Answered, Turns.Count: 2 }, "the follow-up's answer");
        var second = fixture.Chat.Requests[1].Messages;
        Check.True(second[^2] is { Role: "assistant" } previous && previous.Content.Contains("    int y;", StringComparison.Ordinal)
                   && second[^1].Content.Contains("and make it readonly", StringComparison.Ordinal) && second[1].Content.Contains("rename x", StringComparison.Ordinal),
            "the follow-up carries the first instruction, its answer and the new instruction: it refines the same edit");
        Check.True(model.InlineChat.Rows.Any(r => r is { Kind: InlineChatRowKind.Added, Text: "    readonly int y;" }) && model.InlineChat.Rows.Any(r => r is { Kind: InlineChatRowKind.Removed, Text: "    int x;" }),
            "the ghost diff is still against the original lines");

        model.InlineChat.Press(InlineChatButton.Retry);
        await PumpUntil(ui, () => model.InlineChat is { State: InlineChatState.Answered } && fixture.Chat.Requests.Count == 3, "Retry asks again");
        Check.True(fixture.Chat.Requests[2].Messages.Count(m => m.Role == "assistant") == 1 && model.InlineChat.Answer.Contains("= 1", StringComparison.Ordinal),
            "Retry asks the last instruction again without the answer it replaces");

        Check.True(model.OnKey(EditorKey.Up, KeyModifiers.None) && model.InlineChat.Input == "and make it readonly", "Up recalls the last instruction");
        model.OnKey(EditorKey.Up, KeyModifiers.None);
        Check.Equal("rename x", model.InlineChat.Input, "Up again, the one before");
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        model.OnKey(EditorKey.Down, KeyModifiers.None);
        Check.Equal("", model.InlineChat.Input, "Down past the newest: what was typed comes back");

        Check.True(model.OnKey(EditorKey.Escape, KeyModifiers.None) && !model.InlineChat.IsOpen, "Escape discards and closes");
        Check.True(Text(document) == Source && !document.CanUndo, "the text is as it was, nothing to undo");
    }

    [Test]
    public static async Task WithNoModelLoadedItSaysSoAndLoadsOnlyWhenTheUserPresses()
    {
        using var fixture = new Fixture(FakeChatModel.Script(FakeChatModel.Answer("```\n    int y;\n```")));
        var (model, document, ui) = fixture.Editor(Source);
        model.Select(Source.IndexOf("int x", StringComparison.Ordinal), Source.IndexOf("int x", StringComparison.Ordinal));
        model.OpenInlineChat();
        model.OnText("rename x to y");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.True(model.InlineChat.State == InlineChatState.NeedsModel && model.InlineChat.Message!.Contains("not loaded", StringComparison.Ordinal), $"no model: the box says so ({model.InlineChat.Message})");
        Check.Equal(0, fixture.Provider.Acquired, "and loads nothing by itself (note 23)");
        var layout = model.InlineChat.Layout()!;
        int load = Enumerable.Range(0, layout.ButtonCount).Single(i => layout.ButtonAt(i) == InlineChatButton.Load);
        Check.Equal("Load the chat model", model.InlineChat.TitleOf(InlineChatButton.Load), "its button offers the load");
        model.PointerPressed(layout.ButtonLeft(load) + 5, layout.StatusTop + 5, KeyModifiers.None);
        await PumpUntil(ui, () => model.InlineChat.State == InlineChatState.Answered, "pressed: the model loads and the request waiting goes");
        Check.True(fixture.Provider.Acquired == 1 && fixture.Chat.Requests.Count == 1, "one load, at the user's press, then one request");
        Check.Equal(Source, Text(document), "still nothing applied before Accept");

        // No provider registered: said; a read-only text opens no box; Ctrl+I is the editor's key in both keymaps.
        InlineChatProviders.Unregister(fixture.InlineChat.Name);
        model.InlineChat.Discard();
        model.OpenInlineChat();
        model.OnText("x");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.True(model.InlineChat.State == InlineChatState.Failed && model.InlineChat.Message!.Contains("AI plug-in", StringComparison.Ordinal), "no provider: it says which plug-in answers");
        model.InlineChat.Discard();
        model.IsReadOnly = true;
        Check.True(!model.OpenInlineChat(), "a read-only text opens no box");
        Check.True(StandardShortcuts.Bindings.Any(b => b.CommandId == StandardShortcuts.InlineChat && b.Chord == KeyChord.Parse("Ctrl+I"))
                   && StandardShortcuts.LineCommands.Any(c => c.Id == StandardShortcuts.InlineChat) && ShellEditCommands.EditorContextItems.Any(i => i.Id == StandardShortcuts.InlineChat),
            "Ctrl+I runs Edit: Inline chat, also in the editor's context menu");
    }

    [Test]
    public static void TheGhostDiffMarksWhatTheStreamHasNotReachedAndTheReplacementKeepsTheLineBreaks()
    {
        var rows = InlineChatController.DiffRows("a\nb\nc\n", "a\nB\n", streaming: true, 4, out int added, out int removed);
        Check.True(rows is [{ Kind: InlineChatRowKind.Same, Text: "a" }, { Kind: InlineChatRowKind.Removed, Text: "b" }, { Kind: InlineChatRowKind.Added, Text: "B" }, { Kind: InlineChatRowKind.Pending, Text: "c" }]
                   && added == 1 && removed == 1, $"while streaming, the lines after the last one reached are pending, not removed ({string.Join(",", rows)})");
        rows = InlineChatController.DiffRows("a\nb\nc\n", "a\nB\n", streaming: false, 4, out _, out removed);
        Check.True(rows.Any(r => r is { Kind: InlineChatRowKind.Removed, Text: "c" }) && !rows.Any(r => r.Kind == InlineChatRowKind.Pending) && removed == 2, "once ended, they are removed");
        Check.Equal("x\r\ny\r\n", InlineChatController.Replacement("a\r\nb\r\n", "x\ny"), "the original's CRLF and its last break");
        Check.Equal("int y;\n", InlineChatController.Clean("Sure!\n```cs\nint y;\n```\nMore."), "the first block's code");
        Check.Equal("int y;\n", InlineChatController.Clean("\n\nint y;\n\n"), "no block: the text without blank ends");

        var snapshot = TextDocument.Create("l1\nl2\nl3\nl4\nl5\n").Buffer.Current;
        var request = new InlineChatRequest(snapshot, snapshot.GetLine(2).Start, snapshot.GetLine(3).Start, "fix it", "/w/a.cs", "csharp")
        {
            Diagnostics = [new EditorDiagnostic(snapshot.GetLine(2).Start, 2, DiagnosticSeverity.Error, "CS1", "broken")],
            WorkspaceFolder = "/w",
        };
        var messages = AiInlineChat.Messages(request, 1);
        string user = messages[1].Content;
        Check.True(user.Contains("a.cs (csharp)", StringComparison.Ordinal) && user.Contains("Lines 2-2 before", StringComparison.Ordinal) && user.Contains("l2", StringComparison.Ordinal)
                   && user.Contains("Lines 4-4 after", StringComparison.Ordinal) && !user.Contains("l5", StringComparison.Ordinal) && user.Contains("line 3: error CS1: broken", StringComparison.Ordinal),
            $"the file around the lines (inlineChatContextLines) and their problems ({user})");
    }

    private static string Text(TextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    internal static async Task PumpUntil(ConcurrentQueue<Action> ui, Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            while (ui.TryDequeue(out var action))
            {
                action();
            }

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

    /// <summary>The AI plug-in's inline chat provider over a fake model provider whose chat model is Idrak's scripted fake.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        public Fixture(FakeChatModel chat)
        {
            Chat = chat;
            Provider = new CountingProvider(chat);
            ModelProviders.Register(Provider);
            Settings = new AiSettings { ModelProvider = ProviderName, Model = "fake-7b", CompletionModel = "", Completion = false, ChatTemperature = 0.25f, ChatMaxTokens = 333 };
            Models = new AiModels(() => Settings);
            InlineChat = new AiInlineChat(() => Settings, Models, token => Models.ChatAsync(token));
            InlineChatProviders.Register(InlineChat);
        }

        public FakeChatModel Chat { get; }

        public CountingProvider Provider { get; }

        public AiSettings Settings { get; }

        public AiModels Models { get; }

        public AiInlineChat InlineChat { get; }

        public Task<ModelLease> LoadAsync() => Models.ChatAsync(CancellationToken.None);

        public (EditorModel Model, TextDocument Document, ConcurrentQueue<Action> Ui) Editor(string text)
        {
            var document = TextDocument.Create(text, new CraftOptions { NewLine = "\n" });
            var model = new EditorModel(document, null, new CraftOptions { NewLine = "\n" }, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
            var ui = new ConcurrentQueue<Action>();
            model.Dispatch = ui.Enqueue;
            model.UpdateViewport(0, 0, 800, 400);
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

            InlineChatProviders.Unregister(InlineChat.Name);
            Models.Dispose();
            ModelProviders.Unregister(ProviderName);
        }
    }

    internal sealed class CountingProvider(IChatModel chat) : IModelProvider
    {
        public int Acquired { get; private set; }

        public string Name => ProviderName;

        public string Summary => "the inline chat tests' models";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            Acquired++;
            return Task.FromResult(new ModelLease(request.Model, chat, null, 4096, TimeSpan.FromMilliseconds(1)));
        }
    }
}
