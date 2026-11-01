// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Tools;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>Shared pieces of the X restart's piece 1b tests: a provider whose lease gives a chat template, a folder, the host's serial "UI thread".</summary>
internal static class Piece1b
{
    public const string ProviderName = "x1b-fake";

    public static string Folder(string name)
    {
        string root = TestRunner.RepositoryPath("test-output", "tmp", name + "-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        return root;
    }

    public static Action<Action> Serial()
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

    public static async Task Until(Func<bool> done, string what)
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

    public static string Text(ITextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    /// <summary>A provider registered for a test: its lease is the given chat model, with the given template (or none).</summary>
    public sealed class Provider : IModelProvider, IDisposable
    {
        private readonly IChatModel _chat;
        private readonly IChatTemplate? _template;

        public Provider(IChatModel chat, IChatTemplate? template = null)
        {
            _chat = chat;
            _template = template;
            ModelProviders.Register(this);
            EditorToolSets.Register(EditorToolSet.Instance);
        }

        public string Name => ProviderName;

        public string Summary => "piece 1b's test models";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelLease(request.Model, _chat, null, 4096, TimeSpan.Zero) { ChatTemplate = _template });

        public void Dispose()
        {
            ModelProviders.Unregister(ProviderName);
            EditorToolSets.Unregister(EditorToolSets.Editor);
        }
    }

    public sealed class AcceptingPresenter : IEditorPresenter
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

    public static AiSettings Settings(string copies, int nudges = 2, bool callsInText = true) => new()
    {
        ModelProvider = ProviderName, Model = "fake", AgentSandbox = "x1b-none", AgentFolder = copies, AgentMaxTurns = 8, AgentNudges = nudges, AgentCallsInText = callsInText,
    };

    public static void Remove(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        if (Directory.Exists(root + "-copies"))
        {
            Directory.Delete(root + "-copies", recursive: true);
        }
    }

    /// <summary>The owner's House reply (note 27), the call in a ```json fence, as the model wrote it.</summary>
    public static string Fenced(string name, JsonObject arguments) =>
        "I will do it.\n```json\n" + new JsonObject { ["name"] = name, ["arguments"] = arguments }.ToJsonString() + "\n```\nYou created it.";
}

/// <summary>
/// Note 27 in Craft (X restart piece 1b): the agent's settings agentNudges and agentCallsInText reach Idrak's coding agent,
/// the lease's chat template reaches its correction, calls written as text end in a diff of one undo step, and a run that
/// called no tool says so, whatever the model claimed.
/// </summary>
[TestGroup("AiPlugins")]
public static class AgentCallsInTextTests
{
    [Test]
    public static async Task FencedCallsEndInADiffOfOneUndoStep()
    {
        string root = Piece1b.Folder("x1b-agent");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "alpha\nkeep\n");
            var presenter = new Piece1b.AcceptingPresenter();
            using var host = new EditorHost(dispatch: Piece1b.Serial(), presenter: presenter) { WorkspaceFolder = root };
            host.OpenFile = async (path, token) => host.Open(await TextDocument.OpenAsync(path, cancellationToken: token), null);
            var chat = FakeChatModel.Script(
                FakeChatModel.Answer(Piece1b.Fenced("edit_file", new JsonObject { ["path"] = "a.txt", ["old_text"] = "alpha", ["new_text"] = "beta" })),
                FakeChatModel.Answer("Done: alpha is beta now."));
            using var provider = new Piece1b.Provider(chat);
            var settings = Piece1b.Settings(root + "-copies");
            using var models = new AiModels(() => settings);
            using var panel = new AiAgentPanel(host, () => settings, models);
            await panel.SubmitAsync("Change alpha to beta");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(panel.Changes is [{ Relative: "a.txt", Added: 1, Removed: 1 }], $"the call the model wrote in a ```json fence was read and run: a.txt changed in the copy ({panel.Changes.Count} changes)");
            Check.Equal("alpha\nkeep\n", await File.ReadAllTextAsync(Path.Combine(root, "a.txt")), "the user's file is untouched until the diff is accepted");
            var end = panel.Entries.Last(e => e.Role == ConversationRole.Note);
            Check.True(end.Text.StartsWith("Done in 2 turns, 1 tool calls.", StringComparison.Ordinal), $"the end note counts the call read from the text ({end.Text})");
            Check.True(await panel.ReviewAsync(), "Review diff: the change is proposed and accepted");
            Check.True(presenter.Proposals.Single().Diff is { Added: 1, Removed: 1 }, "one diff in the diff view");
            var document = host.Documents.Single();
            Check.Equal("beta\nkeep\n", Piece1b.Text(document.Document), "accepted into the editor");
            document.Document.Undo();
            Check.Equal("alpha\nkeep\n", Piece1b.Text(document.Document), "one undo step takes it back");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task ARunWithNoToolCallsSaysNothingChangedAndCallsNotReadCountTheNudges()
    {
        string root = Piece1b.Folder("x1b-agent");
        try
        {
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            // The owner's run: the model claims a file it never wrote.
            var chat = FakeChatModel.Script(FakeChatModel.Answer("You created a new House.cs with three float properties and a price."));
            using (var provider = new Piece1b.Provider(chat))
            {
                var settings = Piece1b.Settings(root + "-copies");
                using var models = new AiModels(() => settings);
                using var panel = new AiAgentPanel(host, () => settings, models);
                await panel.SubmitAsync("Create C# class For House with float 3 props and + float price");
                await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
                int claim = panel.Entries.ToList().FindIndex(e => e.Role == ConversationRole.Assistant && e.Text.Contains("You created", StringComparison.Ordinal));
                var last = panel.Entries[^1];
                Check.True(claim >= 0 && last is { Role: ConversationRole.Note, Text: "The model wrote no tool calls; nothing was changed." },
                    $"the claim never stands alone: the end note says nothing was done ({last.Role}: {last.Text})");
                Check.True(panel.Changes.Count == 0, "no change listed");
            }

            // Calls the agent cannot read (calls in text off): one correction, then the run stops and says how many were given.
            var stubborn = FakeChatModel.Script(
                FakeChatModel.Answer(Piece1b.Fenced("write_file", new JsonObject { ["path"] = "House.cs", ["content"] = "class House {}" })),
                FakeChatModel.Answer(Piece1b.Fenced("write_file", new JsonObject { ["path"] = "House.cs", ["content"] = "class House {}" })));
            using (var provider = new Piece1b.Provider(stubborn))
            {
                var settings = Piece1b.Settings(root + "-copies", nudges: 1, callsInText: false);
                using var models = new AiModels(() => settings);
                using var panel = new AiAgentPanel(host, () => settings, models);
                await panel.SubmitAsync("Create House");
                await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
                var last = panel.Entries[^1];
                Check.True(last.Role == ConversationRole.Note && last.Text.Contains("could not be read, after 1 correction (agentNudges 1)", StringComparison.Ordinal)
                           && last.Text.EndsWith("Nothing was changed.", StringComparison.Ordinal),
                    $"CallsNotRead: the note names the corrections given and that nothing changed ({last.Text})");
                Check.Equal(2, stubborn.Requests.Count, "one correction, then the run ended");
            }
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task TheSettingsReachTheAgent()
    {
        var defaults = AiSettings.Read(_ => null);
        Check.True(defaults is { AgentNudges: 2, AgentCallsInText: true, ChatImageMaxMegabytes: 20 }, "the defaults: two corrections, calls in text read, 20 MB images");
        var read = AiSettings.Read(key => key switch { "agentNudges" => "0", "agentCallsInText" => "false", _ => null });
        Check.True(read is { AgentNudges: 0, AgentCallsInText: false }, "agentNudges and agentCallsInText are read");
        bool threw = false;
        try
        {
            _ = AiSettings.Read(key => key == "agentNudges" ? "21" : null);
        }
        catch (ArgumentException error)
        {
            threw = error.Message.Contains("CRAFT_PLUGIN_AI_AGENTNUDGES", StringComparison.Ordinal) && error.Message.Contains("0 to 20", StringComparison.Ordinal);
        }

        Check.True(threw, "21 nudges is refused, naming the variable and the range");
        foreach (var (key, variable) in new[] { ("agentNudges", "CRAFT_PLUGIN_AI_AGENTNUDGES"), ("agentCallsInText", "CRAFT_PLUGIN_AI_AGENTCALLSINTEXT"), ("chatImageMaxMb", "CRAFT_PLUGIN_AI_CHATIMAGEMAXMB") })
        {
            var declared = AiSettingShapes.Declared.SingleOrDefault(s => s.Key == key);
            Check.True(declared is { Shape: not null } && PluginSetting.VariableName(AiSettings.PluginName, key) == variable, $"{key} is declared with a shape ({variable})");
        }

        Check.Equal(SettingSections.Agent, AiSettingShapes.SectionOf(AiSettingShapes.Declared.Single(s => s.Key == "agentNudges")), "agentNudges sits in the Agent section");
        Check.Equal(SettingSections.Chat, AiSettingShapes.SectionOf(AiSettingShapes.Declared.Single(s => s.Key == "chatImageMaxMb")), "chatImageMaxMb sits in the Chat section");

        // Both off: a call in a fence is neither read nor corrected; the run ends on the first reply.
        string root = Piece1b.Folder("x1b-agent");
        try
        {
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            var chat = FakeChatModel.Script(FakeChatModel.Answer(Piece1b.Fenced("write_file", new JsonObject { ["path"] = "a.cs", ["content"] = "x" })), FakeChatModel.Answer("Done."));
            using var provider = new Piece1b.Provider(chat);
            var settings = Piece1b.Settings(root + "-copies", nudges: 0, callsInText: false);
            using var models = new AiModels(() => settings);
            using var panel = new AiAgentPanel(host, () => settings, models);
            await panel.SubmitAsync("Write a.cs");
            await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
            Check.True(chat.Requests.Count == 1 && panel.Changes.Count == 0, $"agentCallsInText off and agentNudges 0 reach the agent: no call read, no correction ({chat.Requests.Count} requests)");
            Check.True(panel.Entries[^1].Text.Contains("after 0 corrections (agentNudges 0)", StringComparison.Ordinal), $"said ({panel.Entries[^1].Text})");
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task TheLeasesTemplateReachesTheCorrection()
    {
        string root = Piece1b.Folder("x1b-agent");
        try
        {
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            foreach (var template in (IChatTemplate?[])[new ChatMLTemplate(), null])
            {
                var chat = FakeChatModel.Script(FakeChatModel.Answer(Piece1b.Fenced("write_file", new JsonObject { ["path"] = "a.cs", ["content"] = "x" })), FakeChatModel.Answer("Done."));
                using var provider = new Piece1b.Provider(chat, template);
                var settings = Piece1b.Settings(root + "-copies", nudges: 1, callsInText: false);
                using var models = new AiModels(() => settings);
                using var panel = new AiAgentPanel(host, () => settings, models);
                await panel.SubmitAsync("Write a.cs");
                await panel.Run.WaitAsync(TimeSpan.FromSeconds(20));
                Check.Equal(2, chat.Requests.Count, "the fenced call got one correction, then the model answered");
                string correction = chat.Requests[1].Messages.Last(m => m.Role == "user").Content;
                if (template is not null)
                {
                    Check.True(correction.Contains("between <tool_call> and </tool_call>", StringComparison.Ordinal),
                        $"the lease's template reached the correction through the panel's wrapper: its own markers ({correction})");
                }
                else
                {
                    Check.True(correction.Contains("your chat template's own tool-call format", StringComparison.Ordinal),
                        $"with no template the correction is the generic one ({correction})");
                }

                await AiAgentPanel.DiscardsDone();
            }
        }
        finally
        {
            await AiAgentPanel.DiscardsDone();
            Piece1b.Remove(root);
        }
    }
}

/// <summary>
/// X part 5's drop onto the chat: text files become path: chips and images image: chips (read off the UI thread, up to
/// chatImageMaxMb), an image for a model that takes none is greyed with the reason and nothing is sent until it is
/// removed, the agent takes dropped files as @path, and a panel that takes no files is said so by the host.
/// </summary>
[TestGroup("ChatDrop")]
public static class ChatDropTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];

    private static FakeChatModel Seeing(params ChatMessage[] replies)
    {
        var chat = FakeChatModel.Script(replies);
        chat.PartKinds = FrozenSet.Create(StringComparer.Ordinal, ChatParts.Text, ChatParts.Image);
        return chat;
    }

    [Test]
    public static async Task FilesBecomePathChipsAndImagesImageChipsAndTheAgentTakesThemAsMentions()
    {
        string root = Piece1b.Folder("x1b-drop");
        try
        {
            string notes = Path.Combine(root, "notes.txt");
            string shot = Path.Combine(root, "shot.png");
            string big = Path.Combine(root, "big.png");
            string binary = Path.Combine(root, "data.bin");
            string folder = Path.Combine(root, "sub");
            await File.WriteAllTextAsync(notes, "first line\nsecond line\n");
            await File.WriteAllBytesAsync(shot, Png);
            await File.WriteAllBytesAsync(big, new byte[(int)(1.5 * 1024 * 1024)]);
            await File.WriteAllBytesAsync(binary, [1, 2, 0, 3]);
            Directory.CreateDirectory(folder);
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            var chat = Seeing(FakeChatModel.Answer("A picture and notes."));
            using var provider = new Piece1b.Provider(chat);
            var settings = new AiSettings { ModelProvider = Piece1b.ProviderName, Model = "fake", ChatKeep = false, ChatImageMaxMegabytes = 1 };
            using var models = new AiModels(() => settings);
            using var panel = new AiChatPanel(host, () => settings, models);
            int taken = await panel.AcceptDropAsync([notes, shot, big, binary, folder]);
            Check.Equal(2, taken, "the text file and the small image are taken");
            Check.True(panel.Chips.Any(c => c.Id == "path:" + notes && c.Text == "notes.txt" && c.Problem is null), "a path: chip for the text file");
            Check.True(panel.Chips.Any(c => c.Id == "image:" + shot && c.Problem is null), "an image: chip for the image (the model is not known yet: not greyed)");
            var note = panel.Entries.Last(e => e.Role == ConversationRole.Note).Text;
            Check.True(note.Contains("big.png is 1.5 MB, over chatImageMaxMb (1 MB)", StringComparison.Ordinal) && note.Contains("data.bin is neither a text file nor an image", StringComparison.Ordinal)
                       && note.Contains("sub is a folder", StringComparison.Ordinal), $"what was not taken is said, with why ({note})");
            Check.Equal("Chat took 2 of the 5 dropped files (the panel says why not the rest).", ConversationDrops.Said(panel.Title, 2, 5), "the host's line for a partial drop");

            await panel.SubmitAsync("What is in them?");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(10));
            var user = chat.Requests.Single().Messages.Last(m => m.Role == "user");
            Check.True(user.Parts.OfType<ChatImage>().Single().Data.Span.SequenceEqual(Png), "the image's bytes go with the message as an image part");
            Check.True(user.Content.Contains("The file notes.txt:\n```\nfirst line\nsecond line", StringComparison.Ordinal), $"the text file's lines go as context ({user.Content})");
            Check.True(panel.Chips.All(c => c.Id is "file" or "selection" or "problems"), "the dropped chips went with the message");

            // The agent: files of the open folder as @path; one outside it is not taken.
            var agentChat = FakeChatModel.Script(FakeChatModel.Answer("Read them."));
            string outside = Piece1b.Folder("x1b-outside");
            try
            {
                using var agentProvider = new Piece1b.Provider(agentChat);
                var agentSettings = Piece1b.Settings(root + "-copies");
                using var agentModels = new AiModels(() => agentSettings);
                using var agent = new AiAgentPanel(host, () => agentSettings, agentModels);
                string away = Path.Combine(outside, "x.txt");
                await File.WriteAllTextAsync(away, "x");
                Check.Equal(2, await agent.AcceptDropAsync([notes, folder, away]), "the folder's file and sub-folder are taken, the outside file not");
                Check.True(agent.Chips.Select(c => c.Text).SequenceEqual(["@notes.txt", "@sub"]), $"@path chips ({string.Join(", ", agent.Chips.Select(c => c.Text))})");
                Check.True(agent.Entries[^1].Text.Contains("x.txt is outside the open folder", StringComparison.Ordinal), "the outside file is said");
                await agent.SubmitAsync("Summarize these");
                await agent.Run.WaitAsync(TimeSpan.FromSeconds(20));
                string task = agentChat.Requests[0].Messages.Last(m => m.Role == "user").Content;
                Check.True(task.Contains("@notes.txt, @sub", StringComparison.Ordinal), $"the task carries the files as @path ({task})");
                Check.True(agent.Chips.Count == 0, "the chips went with the task");
            }
            finally
            {
                await AiAgentPanel.DiscardsDone();
                Piece1b.Remove(outside);
            }

            // A panel that takes no files: the host says so.
            var plain = new NoFilesPanel(host);
            Check.Equal("Notes takes no files: nothing was added.", await ConversationDrops.DropAsync(plain, [notes]), "the default AcceptDropAsync takes nothing, and the host says so");
            Check.True(await ConversationDrops.DropAsync(panel, [notes]) is null, "a panel that took every file: nothing to say");
        }
        finally
        {
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task AnImageForAModelWithoutImagesIsGreyedAndNothingIsSentUntilRemoved()
    {
        string root = Piece1b.Folder("x1b-drop");
        try
        {
            string shot = Path.Combine(root, "shot.png");
            await File.WriteAllBytesAsync(shot, Png);
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            var chat = FakeChatModel.Script(FakeChatModel.Answer("Text only here."));          // text only, as Idrak's fake is by default
            using var provider = new Piece1b.Provider(chat);
            var settings = new AiSettings { ModelProvider = Piece1b.ProviderName, Model = "fake", ChatKeep = false };
            using var models = new AiModels(() => settings);
            using var panel = new AiChatPanel(host, () => settings, models);
            Check.Equal(1, await panel.AcceptDropAsync([shot]), "the image is taken");
            await panel.SubmitAsync("What is this?");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(10));
            Check.True(chat.Requests.Count == 0, "nothing reached the model");
            var chip = panel.Chips.Single(c => c.Id == "image:" + shot);
            Check.True(chip.Problem is { } why && why.Contains("fake takes no images", StringComparison.Ordinal), $"the chip came back greyed with the reason ({chip.Problem})");
            Check.True(panel.Entries.Any(e => e.Role == ConversationRole.Error && e.Text.Contains("Not sent: fake takes no images", StringComparison.Ordinal)), "the panel says it was not sent");

            await panel.SubmitAsync("And now?");
            Check.True(chat.Requests.Count == 0 && panel.Entries[^1].Text.StartsWith("Not sent: image: shot.png: fake takes no images", StringComparison.Ordinal),
                $"while the greyed chip is there, nothing is sent ({panel.Entries[^1].Text})");

            // Dropped again now that the model is known: greyed at once.
            panel.RemoveChip(chip.Id);
            Check.Equal(1, await panel.AcceptDropAsync([shot]), "taken again");
            Check.True(panel.Chips.Single(c => c.Id == "image:" + shot).Problem is not null, "greyed at the drop: the model's part kinds are known from its last load");
            panel.RemoveChip("image:" + shot);
            await panel.SubmitAsync("Plain question");
            await panel.LastReply.WaitAsync(TimeSpan.FromSeconds(10));
            Check.Equal(1, chat.Requests.Count, "removed, the message goes");
        }
        finally
        {
            Piece1b.Remove(root);
        }
    }

    [Test]
    public static async Task DroppedFilesAndAChipsFileAreReadOffTheUiThread()
    {
        string root = Piece1b.Folder("x1b-drop");
        var release = new ManualResetEventSlim(false);
        int readOn = 0;
        string? blockOn = null;
        AiChatPanel.Reading = path =>
        {
            if (path == blockOn)
            {
                readOn = Environment.CurrentManagedThreadId;
                release.Wait(TimeSpan.FromSeconds(5));
            }
        };
        try
        {
            string shot = Path.Combine(root, "shot.png");
            string notes = Path.Combine(root, "notes.txt");
            await File.WriteAllBytesAsync(shot, Png);
            await File.WriteAllTextAsync(notes, "from the disk\n");
            using var host = new EditorHost(dispatch: Piece1b.Serial()) { WorkspaceFolder = root };
            var chat = Seeing(FakeChatModel.Answer("Seen."), FakeChatModel.Answer("Seen again."));
            using var provider = new Piece1b.Provider(chat);
            var settings = new AiSettings { ModelProvider = Piece1b.ProviderName, Model = "fake", ChatKeep = false };
            using var models = new AiModels(() => settings);
            using var panel = new AiChatPanel(host, () => settings, models);

            // The drop: the image's bytes are read on the thread pool; the call returns before the read ends.
            blockOn = shot;
            var watch = Stopwatch.StartNew();
            var dropping = panel.AcceptDropAsync([shot, notes]);
            var returned = watch.Elapsed;
            await Piece1b.Until(() => readOn != 0, "the image is being read");
            // The read is held for up to 5 s: had it run in the call, the call would have taken that long.
            Check.True(returned < TimeSpan.FromSeconds(1) && !dropping.IsCompleted,
                $"the drop's call returned at once ({returned.TotalMilliseconds:0} ms) while the image was still being read, off the caller's thread");
            release.Set();
            Check.Equal(2, await dropping.WaitAsync(TimeSpan.FromSeconds(10)), "both taken once read");

            // The send: the chip's file (not open) is read on the thread pool, never in AskAsync's call (the fix found).
            release.Reset();
            readOn = 0;
            blockOn = notes;
            watch.Restart();
            var asking = panel.AskAsync("Use them");
            returned = watch.Elapsed;
            await Piece1b.Until(() => readOn != 0, "the chip's file is being read");
            Check.True(returned < TimeSpan.FromSeconds(1) && !asking.IsCompleted,
                $"AskAsync returned at once ({returned.TotalMilliseconds:0} ms) while the chip's file was still being read, off the caller's thread");
            release.Set();
            await asking.WaitAsync(TimeSpan.FromSeconds(10));
            var user = chat.Requests.Single().Messages.Last(m => m.Role == "user");
            Check.True(user.Content.Contains("from the disk", StringComparison.Ordinal) && user.Parts.OfType<ChatImage>().Count() == 1, "the file's text and the image went with the message");

            // An open document's text is its snapshot (unsaved edits included), not the disk's.
            using var document = await TextDocument.OpenAsync(notes);
            document.Edit([new TextEdit(0, document.Buffer.Current.Length, "unsaved text\n")], EditKind.Command, SelectionState.At(0), SelectionState.At(0));
            host.Open(document, null);
            Check.Equal(1, await panel.AcceptDropAsync([notes]), "dropped again");
            blockOn = null;
            await panel.AskAsync("Again").WaitAsync(TimeSpan.FromSeconds(10));
            var again = chat.Requests[1].Messages.Last(m => m.Role == "user").Content;
            Check.True(again.Contains("unsaved text", StringComparison.Ordinal) && !again.Contains("from the disk\n```", StringComparison.Ordinal),
                $"an open file's chip sends the editor's text, unsaved edits included ({again})");
        }
        finally
        {
            AiChatPanel.Reading = null;
            release.Dispose();
            Piece1b.Remove(root);
        }
    }

    // A conversation panel that takes no files (the contract's default).
    private sealed class NoFilesPanel(IEditorHost host) : ConversationPanel(host, "x1b.notes", "Notes")
    {
        public override string Placeholder => "";

        public override IReadOnlyList<PanelAction> HeaderActions => [];

        public override Task SubmitAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

/// <summary>X part 5's Models page per role: each card writes its own role's settings (on, model, device, "Same as chat") and the agent's inherits.</summary>
[TestGroup("ModelsPage")]
public static class ModelsPageTests
{
    [Test]
    public static void EachCardWritesItsOwnRolesSettingsAndTheAgentInherits()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["CRAFT_PLUGIN_AI_MODEL"] = "big", ["CRAFT_PLUGIN_AI_DEVICE"] = "cuda:0" };
        AiSettings Current() => AiSettings.Read(key => values.GetValueOrDefault(PluginSetting.VariableName(AiSettings.PluginName, key)));
        using var provider = new LocalModelProvider { SettingsSource = Current };
        string? Apply(string variable, string? value)
        {
            if (value is null)
            {
                values.Remove(variable);
            }
            else
            {
                values[variable] = value;
            }

            return null;
        }

        ModelRoleCard Card(string key) => ModelRoleCards.Of(provider.Status).Single(c => c.Role.Key == key);

        var cards = ModelRoleCards.Of(provider.Status);
        Check.True(cards.Select(c => c.Title).SequenceEqual(["Chat", "Agent", "Inline completion"]) && cards.All(c => c.CanToggle && c.Enabled), "a card per role, each with its switch, on");
        Check.True(cards.Select(c => c.Role.EnabledSetting!.Variable).SequenceEqual(["CRAFT_PLUGIN_AI_CHATENABLED", "CRAFT_PLUGIN_AI_AGENTENABLED", "CRAFT_PLUGIN_AI_COMPLETION"]),
            "ModelRole.EnabledSetting names each role's own switch");
        Check.True(cards.Select(c => c.Role.SettingsCommand).SequenceEqual([SettingSections.CommandOf(SettingSections.Chat), SettingSections.CommandOf(SettingSections.Agent), SettingSections.CommandOf(SettingSections.Completion)]),
            "each card's settings link opens its own section");
        var agent = Card(ModelRoleKeys.Agent);
        Check.True(agent is { CanInherit: true, SameAsOther: true, ModelText: "same as chat: big", SameAsLabel: "Same as chat" } && agent.DeviceText == "chat's: cuda:0",
            $"the agent takes the chat's model and device by default ({agent.ModelText}, {agent.DeviceText})");
        Check.True(!Card(ModelRoleKeys.Chat).CanInherit && ModelRoleCards.SameAsOther(Card(ModelRoleKeys.Chat).Role).Count == 0, "the chat inherits nothing");

        // The switches: each its own role.
        Check.True(ModelRoleCards.Apply([ModelRoleCards.Enable(agent.Role, false)], Apply) is null && values["CRAFT_PLUGIN_AI_AGENTENABLED"] == "false", "the agent's switch writes agentEnabled");
        Check.True(Current() is { AgentEnabled: false, ChatEnabled: true, Completion: true }, "only the agent is off");
        Check.True(!Card(ModelRoleKeys.Agent).Enabled && Card(ModelRoleKeys.Agent).Role.State.StartsWith("off", StringComparison.Ordinal), "its card says off");
        ModelRoleCards.Apply([ModelRoleCards.Enable(Card(ModelRoleKeys.Completion).Role, false), ModelRoleCards.Enable(Card(ModelRoleKeys.Agent).Role, true)], Apply);
        Check.True(Current() is { AgentEnabled: true, Completion: false, ChatEnabled: true }, "completion's switch writes completion; the agent back on");

        // The model fields: each its own setting and its own load command.
        var agentModel = ModelRoleCards.Model(Card(ModelRoleKeys.Agent).Role, "  small-agent ");
        Check.True(agentModel is { Variable: "CRAFT_PLUGIN_AI_AGENTMODEL", Value: "small-agent", LoadCommand: AiPlugin.LoadAgentModelCommand }, "the agent's model field writes agentModel and loads the agent's");
        Check.True(ModelRoleCards.Model(Card(ModelRoleKeys.Chat).Role, "bigger") is { Variable: "CRAFT_PLUGIN_AI_MODEL", LoadCommand: AiPlugin.LoadModelCommand }, "the chat's writes model (its old variable)");
        Check.True(ModelRoleCards.Model(Card(ModelRoleKeys.Completion).Role, "tiny") is { Variable: "CRAFT_PLUGIN_AI_COMPLETIONMODEL", LoadCommand: null },
            "completion's writes completionModel; turned off, it loads nothing");
        ModelRoleCards.Apply([agentModel, ModelRoleCards.Device(Card(ModelRoleKeys.Agent).Role, "cpu")], Apply);
        var own = Card(ModelRoleKeys.Agent);
        Check.True(own is { SameAsOther: false, ModelText: "small-agent", DeviceText: "cpu" } && Current().AgentRole is { Model: "small-agent", Device: "cpu", InheritsModel: false },
            $"the agent's own model and device ({own.ModelText}, {own.DeviceText})");
        Check.True(Current().ChatRole is { Model: "big", Device: "cuda:0" }, "the chat's settings are untouched");

        // The device choice: auto is a role's own default (cleared), but written for a role that inherits.
        Check.True(ModelRoleCards.Device(Card(ModelRoleKeys.Chat).Role, "auto") is { Variable: "CRAFT_PLUGIN_AI_DEVICE", Value: null }, "the chat's auto clears its device (the default)");
        Check.True(ModelRoleCards.Device(own.Role, "auto") is { Variable: "CRAFT_PLUGIN_AI_AGENTDEVICE", Value: "auto" }, "the agent's auto is written (empty is the chat's)");
        Check.True(ModelRoleCards.Device(Card(ModelRoleKeys.Completion).Role, "cpu") is { Variable: "CRAFT_PLUGIN_AI_COMPLETIONDEVICE", Value: "cpu" }, "completion's device is its own");

        // Same as chat: the agent's model and device cleared, so it takes the chat's again (one copy).
        Check.True(ModelRoleCards.Apply(ModelRoleCards.SameAsOther(own.Role), Apply) is null && !values.ContainsKey("CRAFT_PLUGIN_AI_AGENTMODEL") && !values.ContainsKey("CRAFT_PLUGIN_AI_AGENTDEVICE"),
            "'Same as chat' clears agentModel and agentDevice");
        Check.True(Card(ModelRoleKeys.Agent) is { SameAsOther: true, ModelText: "same as chat: big" } && Current().AgentRole is { Model: "big", Device: "cuda:0", InheritsModel: true, InheritsDevice: true },
            "the agent inherits the chat's again");

        // A write the shell refuses stops the rest and is said.
        int asked = 0;
        string? problem = ModelRoleCards.Apply([ModelRoleCards.Enable(own.Role, false), ModelRoleCards.Enable(Card(ModelRoleKeys.Chat).Role, false)], (_, _) => ++asked == 1 ? "the workspace file may not set it" : null);
        Check.True(problem == "the workspace file may not set it" && asked == 1, "the first problem is returned and the writes after it are not made");
        Check.True(ModelRoleCards.Of(null).Count == 0, "no provider, no cards");
    }
}

/// <summary>X part 5's more folding: Razor, .sln and .editorconfig ranges, random edits equal a fresh fold, a provider registered later applies without reopening.</summary>
[TestGroup("MoreFolding")]
public static class MoreFoldingTests
{
    private const string Razor = """
        @page "/counter"
        @using System.Linq

        <div class="counter">
            <h1>Counter</h1>
            <p>
                Current: @count
            </p>
            <br>
            <input type="text"
                   value="@name" />
        </div>
        @* a comment
           over lines *@
        <!-- markup
             comment -->
        @if (count > 0)
        {
            <span>positive</span>
        }
        <style>
            .counter {
                color: red;
            }
        </style>
        @code {
            private int count;
            private string name = "}{";
            private void Up()
            {
                if (count < 10) { count++; }
                var items = new List<string> { "a" };
            }
            /* block
               comment */
        }

        """;

    private const string Solution = """

        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        # comment two
        Project("{FAE}") = "App", "App\App.csproj", "{1}"
        	ProjectSection(SolutionItems) = preProject
        		a = a
        	EndProjectSection
        EndProject
        Project("{FAE}") = "Lib", "Lib\Lib.csproj", "{2}"
        EndProject
        Global
        	GlobalSection(SolutionConfigurationPlatforms) = preSolution
        		Debug|Any CPU = Debug|Any CPU
        	EndGlobalSection
        	GlobalSection(ProjectConfigurationPlatforms) = postSolution
        	EndGlobalSection
        EndGlobal

        """;

    private const string EditorConfig = """
        root = true

        # All files
        [*]
        indent_style = space
        indent_size = 4

        # C# files
        # more about C#
        [*.cs]
        csharp_style_var = true:suggestion
        ; a comment
        ; another
        dotnet_diagnostic.CA1000.severity = none

        """;

    private static FoldingRange[] Sorted(IEnumerable<FoldingRange> ranges) =>
        [.. ranges.OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine)];

    [Test]
    public static void RazorSolutionAndEditorConfigFoldTheirBlocks()
    {
        var razor = Sorted(RazorFolding.Ranges(new StringBuffer(Razor.ReplaceLineEndings("\n")).Current));
        FoldingRange[] razorExpected =
        [
            new(3, 11), new(5, 7), new(9, 10), new(12, 13, FoldingRangeKind.Comment), new(14, 15, FoldingRangeKind.Comment), new(17, 19), new(20, 24), new(21, 23),
            new(25, 35), new(29, 32), new(33, 34, FoldingRangeKind.Comment),
        ];
        Check.SequenceEqual(razorExpected, razor,
            "Razor: the elements (a void <br> waits for no end tag), the long start tag, @* *@ and <!-- --> comments, @if's block, the style and its rule, @code with its method, its /* */ comment; a '}' in a string and generics skipped");

        var solution = Sorted(SolutionFolding.Ranges(new StringBuffer(Solution.ReplaceLineEndings("\n")).Current));
        Check.SequenceEqual([new FoldingRange(2, 3, FoldingRangeKind.Comment), new(4, 8), new(5, 7), new(9, 10), new(11, 17), new(12, 14), new(15, 16)], solution,
            ".sln: the comment run, each Project, its ProjectSection, Global and its GlobalSections");

        var config = Sorted(EditorConfigFolding.Ranges(new StringBuffer(EditorConfig.ReplaceLineEndings("\n")).Current));
        Check.SequenceEqual([new FoldingRange(3, 5), new(7, 8, FoldingRangeKind.Comment), new(9, 13), new(11, 12, FoldingRangeKind.Comment)], config,
            ".editorconfig: each section (the comments right above the next header are that one's), and the comment runs");
    }

    [Test]
    public static async Task RandomEditsEqualAFreshFold()
    {
        ProjectLanguages.Register();
        try
        {
            var random = new Random(271828);
            foreach (var (language, text) in new[] { (ProjectLanguages.Razor, Razor), (ProjectLanguages.Solution, Solution), (ProjectLanguages.EditorConfig, EditorConfig) })
            {
                string source = text.ReplaceLineEndings("\n");
                var lines = source.Split('\n');
                var (model, document) = RealEditors.Make(source, language: EditorLanguages.Get(language));
                using var _ = model;
                await model.Folding.RefreshAsync();
                for (int step = 0; step < 60; step++)
                {
                    var now = document.Buffer.Current;
                    int at = random.Next(0, now.Length + 1);
                    var edit = random.Next(3) == 0 && now.Length > 0
                        ? new TextEdit(Math.Min(at, now.Length - 1), 0, "")
                        : new TextEdit(at, 0, lines[random.Next(lines.Length)] + (random.Next(2) == 0 ? "\n" : ""));
                    if (edit.Text.Length == 0)
                    {
                        edit = edit with { Length = Math.Min(random.Next(1, 30), now.Length - edit.Start) };
                    }

                    document.Edit([edit], EditKind.Command, SelectionState.At(edit.Start), SelectionState.At(edit.Start + edit.Text.Length));

                    if (step % 7 == 0)
                    {
                        await model.Folding.RefreshAsync();
                    }
                }

                await model.Folding.RefreshAsync();
                string final = Piece1b.Text(document);
                var (fresh, _) = RealEditors.Make(final, language: EditorLanguages.Get(language));
                using var _f = fresh;
                await fresh.Folding.RefreshAsync();
                Check.True(model.Folding.IsAvailable && fresh.Folding.IsAvailable, $"{language} folds");
                Check.SequenceEqual(fresh.Folding.Ranges.ToArray(), model.Folding.Ranges.ToArray(), $"{language}: after 60 random edits the ranges equal a fresh fold of the same text");
                var provider = FoldingRangeProviders.For(language).Single();
                var direct = await provider.GetRangesAsync(new FoldingRequest(document.Buffer.Current, language, null, 4), CancellationToken.None);
                Check.True(direct.Count >= fresh.Folding.Ranges.Length, $"{language}: the provider's own answer holds every range the editor keeps");
            }
        }
        finally
        {
            ProjectLanguages.Unregister();
        }
    }

    [Test]
    public static async Task AProviderRegisteredAfterADocumentOpenedFoldsItWithoutReopening()
    {
        ProjectLanguages.Register();
        FoldingRangeProviders.Unregister(EditorConfigFolding.ProviderName);
        try
        {
            var (model, _) = RealEditors.Make(EditorConfig.ReplaceLineEndings("\n"), language: EditorLanguages.Get(ProjectLanguages.EditorConfig));
            using var _m = model;
            Check.True(!model.Folding.IsAvailable && model.Folding.Ranges.Length == 0, "no provider when the document opened: nothing folds");
            int changed = 0;
            model.Folding.ProvidersChanged += (_, _) => changed++;
            FoldingRangeProviders.Register(new EditorConfigFolding());
            await Piece1b.Until(() => model.Folding.Ranges.Length == 4, "the provider registered later folds the open document");
            Check.True(model.Folding.IsAvailable && changed == 1, "asked again at once, not on the next edit");
            Check.True(model.Folding.Fold(3) && model.Folding.Map.Folded.Count == 1, "its ranges fold");
            FoldingRangeProviders.Unregister(EditorConfigFolding.ProviderName);
            Check.True(!model.Folding.IsAvailable && model.Folding.Ranges.Length == 0 && model.Folding.Map.Folded.Count == 0 && changed == 2,
                "removed, its ranges and its folds go (every hidden line shown again)");
        }
        finally
        {
            ProjectLanguages.Unregister();
        }
    }
}
