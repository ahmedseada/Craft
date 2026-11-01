// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Plugins.Learning;
using Craft.Plugins.Learning.Abstractions;
using Craft.Tests.Runner;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Abstraction.Reinforcement;
using Idrak.Abstraction.Testing;
using Idrak.Models;
using Idrak.Text.Plugins.Qwen;
using Idrak.Text.Reinforcement;

namespace Craft.Tests;

/// <summary>
/// Phase R: learning in the editor on Idrak's reinforcement learning (plugins/Craft.Plugins.Learning). The two consents
/// (nothing recorded or sent before the first; the second asked apart, only after it; a changed text asks again;
/// withdrawal stops recording and deletes on request), episodes from the AI events and the document (accepted, edited,
/// rejected with the text typed instead, undone), a share that sends exactly the previewed items and nothing ignored, the
/// editor-verdict reward against Idrak's reward suite, recorded episodes replayed into Idrak's preference pairs and a tiny
/// random model's adapter trained with DPO (the loss falling, memory flat), promotion refusing a worse adapter and rolling
/// back, the workspace's failing test as a task the "csharp" environment rewards, and the plug-in loaded from a copied
/// folder seeing Idrak's registries populated.
/// </summary>
[TestGroup("Learning")]
public static class LearningTests
{
    // ---- consent ----

    [Test]
    public static async Task NothingIsRecordedOrSentBeforeConsentAndTheTwoConsentsAreAskedApart()
    {
        using var temp = new TempFolder();
        var vars = new Dictionary<string, string> { ["CRAFT_PLUGIN_LEARNING_SHARETO"] = "fake:target", ["CRAFT_PLUGIN_LEARNING_SHAREWHAT"] = "episodes" };
        using var world = new World(temp, vars);
        var session = world.Session;
        var destination = world.Destination;

        // Before any answer: no file at all.
        Check.True(!File.Exists(Path.Combine(session.Folder, "consent.json")) && !Directory.Exists(Path.Combine(session.Folder, "workspaces")), "nothing is written before an answer");

        // The first answer asks the learning consent; dismissed, nothing is decided, nothing recorded, nothing sent.
        int at = world.Type("int x = 1;\n");
        world.AnswerNow(at, "int y = 2;");
        await world.Idle();
        Check.Equal(1, world.Presenter.Questions.Count, "the first AI answer asks once");
        var first = world.Presenter.Questions[0];
        Check.Equal("Learn on this machine?", first.Title, "the learning consent comes first");
        Check.True(first.Dismissed == -1 && first.Choices.Count == 4 && first.Choices[2] == "No, don't learn", "nothing pre-chosen: a dismissed question decides nothing");
        Check.True(first.Details.Any(d => d.StartsWith("What is recorded", StringComparison.Ordinal)) && first.Details.Any(d => d.Contains(session.Folder, StringComparison.Ordinal))
                   && first.Details.Any(d => d.StartsWith("How much", StringComparison.Ordinal)) && first.Details.Any(d => d.StartsWith("How long", StringComparison.Ordinal))
                   && first.Details.Any(d => d.StartsWith("What training runs", StringComparison.Ordinal)) && first.Details.Any(d => d.Contains("only proposes", StringComparison.Ordinal)),
            "it says what is recorded, where (the folder), how much, how long, what training runs and when, that a learned model only proposes");
        world.Type("int y = 2;", at);
        session.SettleAll();
        await world.Idle();
        Check.True(!File.Exists(session.Store.FilePath) && !File.Exists(Path.Combine(session.Folder, "consent.json")), "dismissed: no episode, no consent written");
        Check.Equal(0, destination.Sent.Count, "no request of any kind went out");

        // A second answer in the same session does not nag.
        world.AnswerNow(world.Type("\n"), "z");
        await world.Idle();
        Check.Equal(1, world.Presenter.Questions.Count, "asked once a session");

        // Yes (all workspaces), then the sharing consent on its own; no to it.
        world.Presenter.Answers.Enqueue(0);
        world.Presenter.Answers.Enqueue(1);
        Check.True(await session.AskLearnAsync(again: true), "yes to learning");
        Check.Equal(3, world.Presenter.Questions.Count, "the sharing consent was a question of its own");
        var share = world.Presenter.Questions[2];
        Check.True(share.Title == "Share what was learned?" && share.Choices[1] == "No, don't share" && share.Dismissed == -1, "the second consent, apart, nothing pre-chosen");
        Check.True(share.Details.Any(d => d.Contains("fake:target", StringComparison.Ordinal)) && share.Details.Any(d => d.Contains(".gitignore", StringComparison.Ordinal))
                   && share.Details.Any(d => d.Contains("preview", StringComparison.Ordinal) || d.Contains("see every item", StringComparison.Ordinal)),
            "it names what would leave, to where, the preview before each share and what is left out");
        var record = session.Consents!.Find(ConsentKinds.Learn, world.Workspace)!;
        Check.True(record.Granted && record.Scope == ConsentStore.AllWorkspaces && record.Version == session.LearnText.Version && record.Time > DateTimeOffset.UtcNow.AddMinutes(-5),
            "stored with its time, its scope and the text's version");
        Check.True(session.Consents.Find(ConsentKinds.Share, world.Workspace) is { Granted: false }, "no to sharing is stored too");
        Check.True(world.Host.StatusItems.Any(i => i.Id == "learning.recording"), "the recording mark shows in the status bar");

        // Now an answer, accepted, edited, rejected for typed text, undone: four episodes with their verdicts.
        at = world.Type("\n");
        world.AnswerNow(at, "Accepted();");
        world.Insert(at, "Accepted();", EditKind.Command);
        session.SettleAll();

        at = world.Type("\n");
        world.AnswerNow(at, "Edited(1);");
        world.Insert(at, "Edited(1);", EditKind.Command);
        world.Replace(at + 7, 1, "42");
        session.SettleAll();

        at = world.Type("\n");
        world.AnswerNow(at, "Rejected();");
        world.Type("Written();", at);
        session.SettleAll();

        at = world.Type("\n");
        world.AnswerNow(at, "Undone();");
        world.Insert(at, "Undone();", EditKind.Command);
        world.Document.Undo();
        session.SettleAll();
        await world.Idle();

        var episodes = session.Store.All();
        Check.SequenceEqual(["accepted", "edited", "rejected", "undone"], [.. episodes.Select(e => e.Verdict)], "the verdicts in order");
        Check.True(episodes[1].Kept == "Edited(42);" && episodes[1].Edited is > 0 and < 1, $"edited: the text after the edit, and how far ({episodes[1].Kept}, {episodes[1].Edited})");
        Check.Equal("Written();", episodes[2].Kept, "rejected: the text written in its place");
        Check.True(episodes.All(e => e.Prompt.Length > 0 && e.File == "src/A.cs"), "each with its context and its file relative to the workspace");
        Check.Equal(0, destination.Sent.Count, "recording sends nothing");

        // No share without the sharing consent: asked again on Share; no, and nothing goes.
        world.Presenter.Answers.Enqueue(1);
        await session.ShareAsync();
        Check.Equal(0, destination.Sent.Count, "without the sharing consent nothing is sent");
    }

    [Test]
    public static async Task AChangedConsentTextAsksAgainAndRecordsNothingUntilAnswered()
    {
        using var temp = new TempFolder();
        var vars = new Dictionary<string, string>();
        using var world = new World(temp, vars);
        var session = world.Session;
        world.Presenter.Answers.Enqueue(0);
        Check.True(await session.AskLearnAsync(again: true), "yes");
        string version = session.LearnText.Version;
        Check.True(session.Recording, "recording");

        vars["CRAFT_PLUGIN_LEARNING_CAPMB"] = "8";                     // what the text says (how much) changed
        Check.True(session.LearnText.Version != version, "the text's version follows its words");
        Check.True(!session.Recording && session.Consents!.State(ConsentKinds.Learn, world.Workspace, session.LearnText) == ConsentState.Changed,
            "a yes to the old text is no yes to the new one");
        int before = world.Presenter.Questions.Count;
        int at = world.Type("\n");
        world.AnswerNow(at, "x();");
        world.Insert(at, "x();", EditKind.Command);
        session.SettleAll();
        await world.Idle();
        Check.Equal(before + 1, world.Presenter.Questions.Count, "the next answer asks again");
        Check.True(!File.Exists(session.Store.FilePath), "nothing recorded until the new text is answered");

        vars["CRAFT_PLUGIN_LEARNING_CONSENT"] = "on";
        var error = Check.Throws<ArgumentException>(() => session.Settings(), "a variable that says yes");
        Check.True(error.Message.Contains("CRAFT_PLUGIN_LEARNING_CONSENT", StringComparison.Ordinal) && error.Message.Contains("only turn it off", StringComparison.Ordinal),
            $"a variable can only turn consent off ({error.Message})");
        vars["CRAFT_PLUGIN_LEARNING_CONSENT"] = "off";
        Check.True(!session.Recording, "consent=off turns it off");
    }

    [Test]
    public static async Task WithdrawalStopsRecordingOnTheNextAnswerAndDeletesOnRequest()
    {
        using var temp = new TempFolder();
        using var world = new World(temp, []);
        var session = world.Session;
        world.Presenter.Answers.Enqueue(1);                             // yes, this workspace only
        Check.True(await session.AskLearnAsync(again: true), "yes for this workspace");
        Check.True(session.Consents!.Find(ConsentKinds.Learn, world.Workspace) is { Scope: var scope } && scope == world.Workspace, "the workspace's own scope");
        int at = world.Type("\n");
        world.AnswerNow(at, "Kept();");
        world.Insert(at, "Kept();", EditKind.Command);
        session.SettleAll();
        await world.Idle();
        long stored = session.Store.Bytes;
        Check.True(stored > 0 && session.Store.All().Count == 1, "one episode stored");

        world.Presenter.Answers.Enqueue(1);                             // delete it
        await session.WithdrawAsync();
        await world.Idle();
        Check.True(!session.Recording && session.Consents.Find(ConsentKinds.Learn, world.Workspace) is { Granted: false }, "withdrawn: one switch");
        Check.True(!Directory.Exists(session.Store.Folder), "deleted on request");
        Check.True(world.Presenter.Messages.Any(m => m.Contains("freed", StringComparison.Ordinal)), $"it says how much was freed ({string.Join(" | ", world.Presenter.Messages)})");
        Check.True(!world.Host.StatusItems.Any(i => i.Id == "learning.recording"), "the recording mark is gone");

        at = world.Type("\n");
        world.AnswerNow(at, "After();");
        world.Insert(at, "After();", EditKind.Command);
        session.SettleAll();
        await world.Idle();
        Check.True(!File.Exists(session.Store.FilePath), "the next answer is not recorded");
    }

    // ---- sharing ----

    [Test]
    public static async Task AShareSendsExactlyThePreviewedItemsAndNothingIgnored()
    {
        using var temp = new TempFolder();
        var vars = new Dictionary<string, string> { ["CRAFT_PLUGIN_LEARNING_SHARETO"] = "fake:somewhere", ["CRAFT_PLUGIN_LEARNING_SHAREWHAT"] = "episodes,scores" };
        using var world = new World(temp, vars);
        await File.WriteAllTextAsync(Path.Combine(world.Workspace, ".gitignore"), "# build\nsecret/\n");
        Directory.CreateDirectory(Path.Combine(world.Workspace, ".craft"));
        await File.WriteAllTextAsync(Path.Combine(world.Workspace, ".craft", "learn-ignore"), "*.gen.cs\n");
        var session = world.Session;
        world.Presenter.Answers.Enqueue(0);
        world.Presenter.Answers.Enqueue(0);                             // yes to sharing too
        Check.True(await session.AskLearnAsync(again: true), "yes");

        var store = session.Store;
        EditorEpisode Episode(string id, string file, string answer) => new(id, DateTimeOffset.UtcNow, AiAnswerKinds.Chat, "Fix it.", answer, AiVerdicts.Accepted) { File = file };
        store.Append(Episode("s1", "src/Fine.cs", "Ok();"));
        store.Append(Episode("s2", "secret/Keys.cs", "Hidden();"));
        store.Append(Episode("s3", "src/Model.gen.cs", "Generated();"));
        store.Append(Episode("s4", "src/Db.cs", "var c = new Conn(password = \"hunter2hunter2\");"));
        store.Append(Episode("s5", "src/Also.cs", "Fine();"));

        world.Presenter.Answers.Enqueue(0);                             // confirm the preview
        await session.ShareAsync();
        await session.Background;
        var plan = session.LastShare!;
        var preview = world.Presenter.Questions[^1];
        Check.Equal("Share these items?", preview.Title, "the preview is asked before anything goes");
        Check.True(preview.Details.SequenceEqual(plan.PreviewLines()), "the preview shows the plan's lines");
        Check.True(preview.Details.Any(d => d.Contains("touches src/Fine.cs", StringComparison.Ordinal)) && preview.Details[0].Contains("3 item(s)", StringComparison.Ordinal),
            $"the count, the size, each item, the paths it touches:\n{string.Join("\n", preview.Details)}");
        Check.SequenceEqual(["episodes/s1.json", "episodes/s5.json", "scores.json"], [.. plan.Items.Select(i => i.Name)], "the items: the fine episodes and the scores");
        Check.True(plan.LeftOut.Count == 3 && plan.LeftOut.Any(l => l.Contains(".gitignore", StringComparison.Ordinal)) && plan.LeftOut.Any(l => l.Contains("learn-ignore", StringComparison.Ordinal))
                   && plan.LeftOut.Any(l => l.Contains("secrets scanner", StringComparison.Ordinal)), $"left out and said so: {string.Join("; ", plan.LeftOut)}");
        Check.Equal(1, world.Destination.Sent.Count, "one share");
        var (target, sent) = world.Destination.Sent[0];
        Check.Equal("somewhere", target, "to the destination named");
        Check.True(sent.Count == plan.Items.Count && sent.Zip(plan.Items).All(p => p.First.Name == p.Second.Name && p.First.Content.Span.SequenceEqual(p.Second.Content.Span)),
            "exactly the previewed items, byte for byte");
        Check.True(!sent.Any(i => i.Content.Span.IndexOf("hunter2"u8) >= 0 || i.Content.Span.IndexOf("Hidden"u8) >= 0 || i.Content.Span.IndexOf("Generated"u8) >= 0),
            "nothing ignored or secret went");

        world.Presenter.Answers.Enqueue(1);                             // cancel the next preview
        await session.ShareAsync();
        Check.Equal(1, world.Destination.Sent.Count, "a cancelled preview sends nothing");
    }

    [Test]
    public static void TheShareFilterReadsGitPatternsAndTheParseOfADestination()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File(".gitignore"), "bin/\n*.log\n/top.txt\ndocs/**/draft.md\n!keep.log\n");
        var filter = new ShareFilter(temp.Path, []);
        Check.Equal(".gitignore", filter.IgnoredBy("src/bin/x.dll"), "a folder pattern ignores its files at any depth");
        Check.Equal(".gitignore", filter.IgnoredBy("a/b.log"), "a name pattern anywhere");
        Check.True(filter.IgnoredBy("keep.log") is null, "a negation brings a file back");
        Check.True(filter.IgnoredBy("top.txt") is not null && filter.IgnoredBy("src/top.txt") is null, "a leading '/' anchors");
        Check.True(filter.IgnoredBy("docs/a/b/draft.md") is not null && filter.IgnoredBy("docs/draft.md") is not null, "'**' spans folders");
        Check.True(filter.Secret("-----BEGIN RSA PRIVATE KEY-----") is not null && filter.Secret("var x = 1;") is null, "the secrets scanner");
        Check.Equal(("https", "https://example.org/in"), ShareDestinations.Parse("https://example.org/in"), "an https address is its own kind");
        Check.Equal(("folder", "D:\\share"), ShareDestinations.Parse("folder:D:\\share"), "kind:target");
        Check.Throws<ArgumentException>(() => ShareDestinations.Parse("D:\\share"), "a path with no kind");
        Check.Throws<NotRegisteredException>(() => ShareDestinations.Get("no-such-destination"), "a destination nobody registered");
    }

    // ---- the reward ----

    [Test]
    public static void TheEditorVerdictRewardPassesIdraksRewardSuite()
    {
        var reward = new EditorVerdictReward();
        RewardInput Input(string? verdict, double edited = 0)
        {
            var info = new EpisodeInfo();
            if (verdict is not null)
            {
                info.Set(PreferenceInfoKeys.Verdict, verdict).Set(EditorVerdictReward.EditedKey, edited);
            }

            return new RewardInput("p", "a", null, info);
        }

        RewardInput[] samples = [Input("accepted"), Input("edited", 0.5), Input("rejected"), Input("undone"), Input(null)];
        Conformance.CheckRewardFunction(reward, samples).ThrowIfFailed();
        var scores = reward.ScoreAsync(samples).AsTask().GetAwaiter().GetResult();
        Check.SequenceEqual([1f, 0.25f, -1f, -1f, 0f], scores, "accepted 1, edited half-way 0.5 × 0.5, rejected −1, undone −1, none 0");
        var weighted = reward.ScoreAsync(samples, new Dictionary<string, string> { ["rejected"] = "-2" }).AsTask().GetAwaiter().GetResult();
        Check.Equal(-2f, weighted[2], "the weights are settings");
    }

    // ---- training ----

    [Test]
    public static async Task RecordedEpisodesReplayIntoPairsAndATinyAdapterTrainsWithTheLossFallingAndMemoryFlat()
    {
        using var temp = new TempFolder();
        string model = TinyChatModel(temp);
        var vars = new Dictionary<string, string>
        {
            ["CRAFT_PLUGIN_LEARNING_MODEL"] = model, ["CRAFT_PLUGIN_LEARNING_DEVICE"] = "cpu", ["CRAFT_PLUGIN_LEARNING_STEPS"] = "12",
            ["CRAFT_PLUGIN_LEARNING_RANK"] = "4", ["CRAFT_PLUGIN_LEARNING_LEARNINGRATE"] = "0.01", ["CRAFT_PLUGIN_LEARNING_BATCHTOKENS"] = "512",
            ["CRAFT_PLUGIN_LEARNING_HELDOUTSHARE"] = "0.25", ["CRAFT_PLUGIN_LEARNING_SCHEDULE"] = "demand",
        };
        using var world = new World(temp, vars);
        var session = world.Session;
        world.Presenter.Answers.Enqueue(0);
        Check.True(await session.AskLearnAsync(again: true), "yes");

        // 24 completions answered "no", each typed over with "yes": rejected, the typed text kept.
        for (int i = 0; i < 24; i++)
        {
            int at = world.Type("\n");
            world.AnswerNow(at, "no", prompt: "Q");
            world.Type("yes", at);
            session.SettleAll();
        }

        await world.Idle();
        var episodes = session.Store.All();
        Check.True(episodes.Count == 24 && episodes.All(e => e.Verdict == "rejected" && e.Kept == "yes"), "24 recorded: rejected, with the text typed instead");

        var settings = session.Settings();
        using (var loaded = LearningTrainer.LoadModel(settings, model))
        {
            var trajectories = LearningTrainer.Trajectories(episodes, loaded, LearningTrainer.Rewards(episodes, settings));
            var pairs = PreferencePairs.Build(trajectories, "verdict", LearningTrainer.Encoder(loaded));
            Check.True(trajectories.Count == 24 && pairs.Count == 24, $"the recorded episodes replay into Idrak's verdict pairs ({trajectories.Count} trajectories, {pairs.Count} pairs)");
            Check.True(trajectories.All(t => t.Return == -1f), "each rewarded by editor-verdict (rejected: −1)");
            var encoder = LearningTrainer.Encoder(loaded);
            int[] kept = [.. encoder.Tokenizer.Encode("yes" + encoder.AssistantEnd)];
            Check.True(pairs[0].Chosen.Tokens.AsSpan(^kept.Length).SequenceEqual(kept), "the chosen answer is the kept text, ending as a reply does");
        }

        await session.TrainAsync(manual: true);
        var result = session.LastTraining!;
        Check.True(result.Version is not null, $"an adapter was written ({result.Message})");
        Check.True(result.Losses.Count == 12 && result.Losses[^1] < result.Losses[0] - 0.05f, $"the DPO loss falls: {string.Join(", ", result.Losses.Select(l => l.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        Check.True(result.DeviceMemory[^1] <= result.DeviceMemory[1] + (1L << 20), $"device memory flat: {result.DeviceMemory[1]} after step 2, {result.DeviceMemory[^1]} after the last");
        Check.True(result.ManagedMemory[^1] <= result.ManagedMemory[1] + (8L << 20), $"managed memory flat: {result.ManagedMemory[1]} after step 2, {result.ManagedMemory[^1]} after the last");
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  measured: {result.Losses.Count} DPO steps, loss {result.Losses[0]:F3} → {result.Losses[^1]:F3}; device memory {result.DeviceMemory[1]:N0} → {result.DeviceMemory[^1]:N0} B; managed {result.ManagedMemory[1]:N0} → {result.ManagedMemory[^1]:N0} B; {result.Message}"));
        Check.True(File.Exists(Path.Combine(result.Version!.Folder, "adapter_model.safetensors")) && File.Exists(Path.Combine(result.Version.Folder, "version.json")),
            "the adapter (PEFT layout) and its record kept");
        Check.True(world.Presenter.Messages.Any(m => m.Contains("cpu", StringComparison.OrdinalIgnoreCase)), "the device shows in what training reports");
        if (result.Promoted)
        {
            Check.Equal(result.Version.Folder, ModelAdapters.For(model), "the promoted adapter is what the AI plug-ins load next");
        }
    }

    [Test]
    public static void PromotionRefusesAWorseAdapterKeepsEveryVersionAndRollsBack()
    {
        using var temp = new TempFolder();
        string model = TinyChatModel(temp);
        RegisterReward();
        try
        {
            var settings = LearningSettings.From(key => key switch
            {
                "device" => "cpu", "steps" => "12", "rank" => "4", "learningRate" => "0.01", "batchTokens" => "512", "heldOutShare" => "0.25", _ => "",
            });

            // Ids chosen so 12 train and 4 are held out (the split is a hash of the id: the same every run).
            var trainIds = new List<string>();
            var heldIds = new List<string>();
            for (int i = 0; trainIds.Count < 12 || heldIds.Count < 4; i++)
            {
                string id = "p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var into = LearningTrainer.IsHeldOut(id, settings.HeldOutShare) ? heldIds : trainIds;
                if (into.Count < (into == heldIds ? 4 : 12))
                {
                    into.Add(id);
                }
            }

            EditorEpisode Prefer(string id, string chosen, string rejected) => new(id, DateTimeOffset.UtcNow, AiAnswerKinds.Chat, "Q", rejected, AiVerdicts.Rejected) { Kept = chosen };
            var good = trainIds.Concat(heldIds).Select(id => Prefer(id, "yes", "no")).ToList();
            var worse = trainIds.Select(id => Prefer(id, "no", "yes")).Concat(heldIds.Select(id => Prefer(id, "yes", "no"))).ToList();
            var versions = new AdapterVersions(temp.File("workspace-data"));

            TrainingResult Train(IReadOnlyList<EditorEpisode> episodes)
            {
                using var loaded = LearningTrainer.LoadModel(settings, model);
                return LearningTrainer.TrainPreferences(loaded, model, episodes, versions, settings);
            }

            var v1 = Train(good);
            Check.True(v1.Promoted && v1.Version!.ScoreAfter > v1.Version.ScoreBefore, $"v1 beats the base model on the held-out pairs and is promoted ({v1.Message})");
            var v2 = Train(worse);
            Check.True(v2.Version is { Promoted: false } && v2.Version.ScoreAfter <= v2.Version.ScoreBefore, $"v2, trained the wrong way, is measured worse and refused ({v2.Message})");
            Check.Equal(1, versions.Current?.Number ?? 0, "v1 stays current");
            Check.True(v2.Message.Contains("not promoted", StringComparison.Ordinal) && v2.Version!.Note.Contains("held-out margin", StringComparison.Ordinal), "the measure is shown");
            var v3 = Train(good);
            Check.True(v3.Promoted && versions.Current?.Number == 3, $"v3, better than v1, replaces it ({v3.Message})");
            Check.SequenceEqual([1, 2, 3], [.. versions.All.Select(v => v.Number)], "every version is kept until deleted");
            Check.Equal(1, versions.Rollback()?.Number ?? 0, "one rollback goes back to v1");
            Check.True(versions.Rollback() is null && versions.Current is null, "and once more to the base model");
            long freed = versions.Delete(2);
            Check.True(freed > 0 && versions.All.Count == 2, "a version is deleted on request, its bytes said");
        }
        finally
        {
            RewardFunctions.Unregister(EditorVerdictReward.RewardName);
        }
    }

    // ---- tasks from the workspace's tests, through the csharp environment ----

    [Test]
    public static async Task AFailingTestBecomesATaskTheCSharpEnvironmentRewardsAFakePolicyOn()
    {
        RegisterReward();
        Craft.Plugins.Learning.LearningLibraries.EnsureIncluded();
        string root = Path.Combine(Path.GetTempPath(), "craft-learning-csharp-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            string workspace = WriteCalcWorkspace(Path.Combine(root, "workspace"));
            var (failing, run) = await WorkspaceTasks.FindFailingAsync(workspace, CommandSandboxes.Process, Path.Combine(root, "results"), TimeSpan.FromMinutes(10), CancellationToken.None);
            if (failing.Count == 0 && !run.Succeeded && (run.Refused || run.ExitCode is null || run.Output.Contains("NU1301", StringComparison.Ordinal)
                                                        || run.Output.Contains("Unable to load the service index", StringComparison.Ordinal)))
            {
                Console.WriteLine($"SKIPPED (the C# environment part): dotnet test could not run here (exit {run.ExitCode}, refused {run.Refused}): {run.Output[..Math.Min(400, run.Output.Length)]}");
                return;
            }

            Check.True(failing.Count == 1 && failing[0].Name == "Lib.Tests.CalcTests.Adds", $"the failing test found from dotnet test's TRX ({string.Join(", ", failing.Select(f => f.Name))})\n{run.Output}");
            string suite = Path.Combine(root, "suite");
            var ids = WorkspaceTasks.Write(suite, workspace, failing);
            var tasks = Idrak.Reinforcement.Plugins.Coding.CodingSuite.Load(suite);
            Check.True(ids.Count == 1 && tasks.Count == 1 && tasks[0].Language == "csharp" && tasks[0].Base == Path.GetFullPath(workspace) && tasks[0].Prompt.Contains("Lib.Tests.CalcTests.Adds", StringComparison.Ordinal),
                "written in task.json form, read back by Idrak's CodingSuite (what idrak reinforce reads)");

            var settings = LearningSettings.From(key => key switch { "sandbox" => CommandSandboxes.Process, "taskReward" => "csharp-tests:1", _ => "" });
            var extra = new Dictionary<string, string>
            {
                ["rounds"] = "4", ["system"] = "Fix the code.", ["packages"] = Path.Combine(Path.GetTempPath(), "craft-learning-test-packages"), ["work"] = Path.Combine(root, "work"),
            };
            var watch = Stopwatch.StartNew();
            var fixedRun = await LearningTrainer.PlayTasksAsync(new ScriptedPolicy(fix: true), suite, settings, 1, extra);
            var fixedTime = watch.Elapsed;
            var idleRun = await LearningTrainer.PlayTasksAsync(new ScriptedPolicy(fix: false), suite, settings, 1, extra);
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  measured: the csharp environment, an episode with the fix {fixedTime.TotalSeconds:F1} s (setup included), without {(watch.Elapsed - fixedTime).TotalSeconds:F1} s; rewards {fixedRun[0].Return} and {idleRun[0].Return}"));
            Check.True(Math.Abs(fixedRun[0].Return - 1f) < 1e-5f, $"the fix passes both tests: csharp-tests 1 ({fixedRun[0].Return}; {Show(fixedRun[0].Info)})");
            Check.True(Math.Abs(idleRun[0].Return - 0.5f) < 1e-5f, $"no fix passes one of two: csharp-tests 0.5 ({idleRun[0].Return}; {Show(idleRun[0].Info)})");
        }
        finally
        {
            RewardFunctions.Unregister(EditorVerdictReward.RewardName);
            Idrak.Reinforcement.Plugins.Coding.CodingWorkspace.Delete(root);
        }
    }

    // ---- B4 and B7 (the owner's third Windows run) ----

    /// <summary>
    /// B4: a training round in a fresh process with no chat load (nothing loads at start since note 23) found no
    /// "Qwen2ForCausalLM": the plug-in registered its libraries and the C# environment, never a family. The child process
    /// (this program with --probe-learning) has no family registered, records 24 verdicts and trains a tiny Qwen2.
    /// </summary>
    [Test]
    public static void ATrainingRoundInAFreshProcessWithNoChatFindsTheModelFamily()
    {
        using var temp = new TempFolder();
        string model = TinyChatModel(temp, QwenPlugin.Qwen2ForCausalLM);
        var lines = PluginProbe.RunInFreshProcess("--probe-learning", model, temp.File("child"));
        string all = string.Join("\n", lines);
        Check.True(lines.Contains("Qwen2ForCausalLM registered before: False"), $"the child starts with no family registered:\n{all}");
        Check.True(lines.Any(l => l.StartsWith("trained: v1:", StringComparison.Ordinal)) && !all.Contains("not registered", StringComparison.Ordinal),
            $"the round loads the Qwen2 model and writes an adapter:\n{all}");
        Check.True(lines.Contains("Qwen2ForCausalLM registered after: True"), $"the family is registered by the round:\n{all}");
    }

    /// <summary>The child of <see cref="ATrainingRoundInAFreshProcessWithNoChatFindsTheModelFamily"/>: one round, its outcome as lines.</summary>
    internal static int ProbeTraining(string model, string folder)
    {
        Console.WriteLine("Qwen2ForCausalLM registered before: " + Idrak.Models.Abstractions.PretrainedArchitectures.Names.Contains("Qwen2ForCausalLM"));
        var vars = new Dictionary<string, string>
        {
            ["CRAFT_PLUGIN_LEARNING_MODEL"] = model, ["CRAFT_PLUGIN_LEARNING_DEVICE"] = "cpu", ["CRAFT_PLUGIN_LEARNING_STEPS"] = "2",
            ["CRAFT_PLUGIN_LEARNING_RANK"] = "4", ["CRAFT_PLUGIN_LEARNING_LEARNINGRATE"] = "0.01", ["CRAFT_PLUGIN_LEARNING_HELDOUTSHARE"] = "0",
            ["CRAFT_PLUGIN_LEARNING_SCHEDULE"] = "demand",
        };
        using var world = new World(folder, vars);
        world.Presenter.Answers.Enqueue(0);
        if (!world.Session.AskLearnAsync(again: true).GetAwaiter().GetResult())
        {
            Console.WriteLine("consent: not given");
            return 1;
        }

        RecordRejections(world, 24).GetAwaiter().GetResult();
        world.Session.TrainAsync(manual: true).GetAwaiter().GetResult();
        Console.WriteLine(world.Session.LastTraining is { } result ? "trained: " + result.Message : $"failed at {world.Session.LastFailureStage}: {world.Session.Status}");
        Console.WriteLine("Qwen2ForCausalLM registered after: " + Idrak.Models.Abstractions.PretrainedArchitectures.Names.Contains("Qwen2ForCausalLM"));
        return 0;
    }

    /// <summary>
    /// B7: rounds on the AI plug-in's copy of the model (its "local" provider holds it for the chat): the trainer takes that
    /// copy alone while it trains and never loads a second; the copy comes back as found (the chat's answer the same, rule
    /// 71) and the device memory in use does not grow over three rounds (rule 77).
    /// </summary>
    [Test]
    public static async Task RoundsTrainTheAIPluginsCopyInPlaceAndGiveItBackAsFound()
    {
        using var temp = new TempFolder();
        string model = TinyChatModel(temp);
        var previous = ModelProviders.Find(ModelProviders.Local);
        if (previous is not null)
        {
            ModelProviders.Unregister(ModelProviders.Local);
        }

        using var provider = new Craft.Ai.LocalModelProvider { Settings = new Craft.Ai.AiSettings { Device = "cpu" } };
        ModelProviders.Register(provider);
        try
        {
            // The chat's lease, as the AI plug-in holds it, and its greedy answer before any round.
            var chat = await provider.AcquireAsync(new ModelRequest(model));
            var greedy = new GenerationOptions { NumPredict = 8, Temperature = 0 };
            string before = (await chat.Text!.GenerateAsync("<|im_start|>user\nQ<|im_end|>\n", greedy)).Text;
            var vars = new Dictionary<string, string>
            {
                ["CRAFT_PLUGIN_LEARNING_MODEL"] = model, ["CRAFT_PLUGIN_LEARNING_STEPS"] = "3", ["CRAFT_PLUGIN_LEARNING_RANK"] = "4",
                ["CRAFT_PLUGIN_LEARNING_LEARNINGRATE"] = "0.01", ["CRAFT_PLUGIN_LEARNING_HELDOUTSHARE"] = "0.25", ["CRAFT_PLUGIN_LEARNING_SCHEDULE"] = "demand",
                ["CRAFT_PLUGIN_LEARNING_PROMOTIONMARGIN"] = "1000000",       // nothing promoted: the chat's copy keeps its key
            };
            using var world = new World(temp, vars);
            world.Presenter.Answers.Enqueue(0);
            Check.True(await world.Session.AskLearnAsync(again: true), "yes");
            await RecordRejections(world, 24);

            var device = Idrak.Abstraction.Device.Cpu.Backend;
            long start = device.GetMemoryUsage().InUse;
            var after = new List<long>();
            for (int round = 1; round <= 3; round++)
            {
                await world.Session.TrainAsync(manual: true);
                var result = world.Session.LastTraining;
                Check.True(result?.Version is not null, $"round {round} wrote an adapter ({result?.Message ?? world.Session.Status})");
                Check.True(provider.Loaded.Count == 1, $"round {round}: one copy loaded, the chat's ({provider.Loaded.Count})");
                after.Add(device.GetMemoryUsage().InUse);
            }

            Check.True(world.Presenter.Messages.Any(m => m.Contains("tokens a batch", StringComparison.Ordinal)), "the batch the round ran is said");
            Check.True(after[0] <= start + (1L << 20) && after[1] == after[0] && after[2] == after[0],
                $"device memory in use does not grow over the rounds: {start:N0} before, {string.Join(", ", after.Select(a => a.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)))} after each");
            string now = (await chat.Text!.GenerateAsync("<|im_start|>user\nQ<|im_end|>\n", greedy)).Text;
            Check.Equal(before, now, "the chat's copy answers as before: the round's adapters are gone, its own were put back");
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  measured: device memory in use {start:N0} B before, {after[0]:N0}, {after[1]:N0}, {after[2]:N0} B after three rounds on the shared copy"));
            chat.Dispose();
        }
        finally
        {
            ModelProviders.Unregister(ModelProviders.Local);
            if (previous is not null)
            {
                ModelProviders.Register(previous);
            }
        }
    }

    /// <summary>B7: the copy's gate: a hold waits for a running reply, and a reply asked for during the hold waits for its end.</summary>
    [Test]
    public static async Task TheCopysGateGivesATrainingRoundTheModelAlone()
    {
        var gate = new Craft.Ai.CopyGate();
        var reply = await gate.EnterAsync(CancellationToken.None);
        var hold = gate.ExclusiveAsync(CancellationToken.None);
        await Task.Delay(50);
        Check.True(!hold.IsCompleted, "the hold waits for the running reply");
        reply.Dispose();
        var held = await hold.WaitAsync(TimeSpan.FromSeconds(5));
        var next = gate.EnterAsync(CancellationToken.None);
        await Task.Delay(50);
        Check.True(!next.IsCompleted, "a reply asked for during the hold waits");
        held.Dispose();
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        var both = await Task.WhenAll(gate.EnterAsync(CancellationToken.None), gate.EnterAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));
        Check.True(both.Length == 2, "replies still run side by side");
        foreach (var pass in both)
        {
            pass.Dispose();
        }
    }

    /// <summary>
    /// B7: out of memory, a step halves the batch and runs again, said in the progress, before the round fails naming its
    /// stage and what to change; an automatic batch is fitted to the room the device reports; a model that does not load
    /// fails at the load stage.
    /// </summary>
    [Test]
    public static async Task OutOfMemoryStepsTheBatchDownAndAFailureNamesItsStage()
    {
        using var temp = new TempFolder();
        string model = TinyChatModel(temp);
        var vars = new Dictionary<string, string>
        {
            ["CRAFT_PLUGIN_LEARNING_MODEL"] = model, ["CRAFT_PLUGIN_LEARNING_DEVICE"] = "cpu", ["CRAFT_PLUGIN_LEARNING_STEPS"] = "1",
            ["CRAFT_PLUGIN_LEARNING_RANK"] = "4", ["CRAFT_PLUGIN_LEARNING_HELDOUTSHARE"] = "0", ["CRAFT_PLUGIN_LEARNING_SCHEDULE"] = "demand",
            ["CRAFT_PLUGIN_LEARNING_BATCHTOKENS"] = "4096",
        };
        using var world = new World(temp, vars);
        world.Presenter.Answers.Enqueue(0);
        Check.True(await world.Session.AskLearnAsync(again: true), "yes");
        await RecordRejections(world, 24);
        var episodes = world.Session.Store.All();
        var settings = world.Session.Settings();
        using var loaded = LearningTrainer.LoadModel(settings, model);
        var cpu = Idrak.Abstraction.Device.Cpu;
        var failures = new List<LearningStageException>();
        (TrainingResult Result, List<string> Notes)? first = null;
        long limit = 0;
        try
        {
            for (long extra = 32 << 10; extra <= 256L << 20 && first is null; extra *= 2)
            {
                var notes = new List<string>();
                using var versions = new TempFolder();
                limit = cpu.Backend.GetMemoryUsage().InUse + extra;
                Idrak.Abstraction.ComputeResources.CpuMemoryLimit = limit;
                try
                {
                    var result = LearningTrainer.TrainPreferences(loaded, model, episodes, new AdapterVersions(versions.Path), settings,
                        new Progress(p => { if (p.Note is { } note) { lock (notes) { notes.Add(note); } } }));
                    first = (result, notes);
                }
                catch (LearningStageException error)
                {
                    failures.Add(error);
                }
            }

            Check.True(first is { Result.Version: not null }, $"a limit the round fits in ({limit:N0} B)");
            Check.True(first!.Value.Notes.Any(n => n.StartsWith("out of memory at", StringComparison.Ordinal)),
                $"at the first limit that fits, the batch stepped down to fit, said in the progress ({string.Join("; ", first.Value.Notes)})");
            var stepFailure = failures.LastOrDefault(f => f.Stage == LearningStages.Step);
            Check.True(stepFailure is not null && stepFailure.Message.StartsWith("Training failed at its step stage: the device ran out of memory", StringComparison.Ordinal)
                       && stepFailure.Message.Contains("CRAFT_PLUGIN_LEARNING_BATCHTOKENS", StringComparison.Ordinal),
                $"below it the round fails at its step stage, in words, naming what to change ({string.Join(" | ", failures.Select(f => f.Stage + ": " + f.Message))})");

            // Automatic: fitted to the room the device reports, at twice that limit.
            Idrak.Abstraction.ComputeResources.CpuMemoryLimit = cpu.Backend.GetMemoryUsage().InUse + (2 * (limit - cpu.Backend.GetMemoryUsage().InUse));
            using var autoVersions = new TempFolder();
            var auto = LearningTrainer.TrainPreferences(loaded, model, episodes, new AdapterVersions(autoVersions.Path), settings with { BatchTokens = 0 });
            var batch = System.Text.RegularExpressions.Regex.Match(auto.Message, "at (\\d+) tokens a batch");
            Check.True(auto.Version is not null && batch.Success && int.Parse(batch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 4096,
                $"an automatic batch is fitted to the free memory ({auto.Message})");
        }
        finally
        {
            Idrak.Abstraction.ComputeResources.CpuMemoryLimit = null;
        }

        // A model that does not load: the load stage, in words.
        var missing = new World(temp.File("missing"), new Dictionary<string, string>(vars) { ["CRAFT_PLUGIN_LEARNING_MODEL"] = temp.File("no-such-model") });
        try
        {
            missing.Presenter.Answers.Enqueue(0);
            Check.True(await missing.Session.AskLearnAsync(again: true), "yes");
            await missing.Session.TrainAsync(manual: true);
            Check.True(missing.Session.LastFailureStage == LearningStages.Load && missing.Presenter.Messages.Any(m => m.Contains("Training failed at its load stage", StringComparison.Ordinal)),
                $"a model that does not load fails at the load stage ({missing.Session.LastFailureStage}: {string.Join(" | ", missing.Presenter.Messages)})");
        }
        finally
        {
            missing.Dispose();
        }
    }

    // `count` completions answered "no", each typed over with "yes": rejected, the typed text kept.
    private static async Task RecordRejections(World world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int at = world.Type("\n");
            world.AnswerNow(at, "no", prompt: "Q");
            world.Type("yes", at);
            world.Session.SettleAll();
        }

        await world.Idle();
    }

    private sealed class Progress(Action<TrainingProgress> report) : IProgress<TrainingProgress>
    {
        public void Report(TrainingProgress value) => report(value);
    }

    // ---- loaded from its folder ----

    [Test]
    public static void ThePluginLoadedFromACopiedFolderSeesIdraksRegistriesPopulated()
    {
        Check.True(RewardFunctions.Find(EditorVerdictReward.RewardName) is null, "before it loads, no editor-verdict");
        using var temp = new TempFolder();
        var loaded = PhasePTests.LoadFromFolder(temp, "Craft.Plugins.Learning");
        try
        {
            Check.True(AssemblyLoadContext.GetLoadContext(loaded) != AssemblyLoadContext.Default, "its own load context");
            Check.True(EnvironmentKinds.Names.Contains("csharp") && EnvironmentKinds.Names.Contains("coding"), $"the csharp environment registered ({string.Join(", ", EnvironmentKinds.Names)})");
            foreach (var reward in (string[])["csharp-tests", "csharp-build", "editor-verdict", "tests-passed"])
            {
                Check.True(RewardFunctions.Find(reward) is not null, $"the reward {reward} ({string.Join(", ", RewardFunctions.Names)})");
            }

            Check.True(RewardFunctions.Origin("editor-verdict") == "Craft.Plugins.Learning", $"editor-verdict is the plug-in's ({RewardFunctions.Origin("editor-verdict")})");
            Check.True(PreferencePairSources.Names.Contains("verdict") && PreferencePairSources.Names.Contains("group"), "the pair sources");
            Check.True(CommandSandboxes.Names.Contains(CommandSandboxes.Process), $"the process sandbox ({string.Join(", ", CommandSandboxes.Names)})");
            Check.True(AiEvents.Find("learning") is not null && ModelAdapters.Find("learning") is not null && EditorPlugins.Find("learning") is not null, "its sink, adapter source and editor plug-in");

            // The preference losses, as the plug-in's own Idrak.Text sees them (the copy Craft's loader shares).
            var context = AssemblyLoadContext.GetLoadContext(loaded)!;
            var text = context.LoadFromAssemblyName(new System.Reflection.AssemblyName("Idrak.Text"));
            var losses = text.GetType("Idrak.Text.Abstractions.PreferenceLosses", throwOnError: true)!.GetProperty("Names")!.GetValue(null) as IEnumerable<string>;
            Check.True(losses is not null && losses.Contains("dpo") && losses.Contains("orpo") && losses.Contains("simpo"), $"the preference losses ({string.Join(", ", losses ?? [])})");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(IdrakPlugins.PackageOf(loaded)!);
        }

        Check.True(RewardFunctions.Find(EditorVerdictReward.RewardName) is null && AiEvents.Find("learning") is null && EditorPlugins.Find("learning") is null,
            "unregistered, it leaves nothing");
    }

    // ---- helpers ----

    private static void RegisterReward()
    {
        if (RewardFunctions.Find(EditorVerdictReward.RewardName) is null)
        {
            RewardFunctions.Register(new EditorVerdictReward());
        }
    }

    private static string Show(EpisodeInfo? info) => info is null ? "no info" : string.Join(", ", info.Entries.Select(e => e.Key + "=" + e.Value));

    /// <summary>A tiny random-weight Qwen3 (or Qwen2) chat model (ChatML, a byte tokenizer) in a folder.</summary>
    private static string TinyChatModel(TempFolder temp, string architecture = QwenPlugin.Qwen3ForCausalLM)
    {
        QwenPlugin.Register();
        var tokenizerFolder = temp.File("tokenizer");
        Directory.CreateDirectory(tokenizerFolder);
        File.WriteAllText(Path.Combine(tokenizerFolder, "tokenizer.json"), PhasePTests.FimTokenizerJson(["<|im_start|>", "<|im_end|>", "<|endoftext|>"]).ToJsonString());
        var spec = new DecoderSpec
        {
            Vocabulary = 260, Dim = 32, Layers = 2, Heads = 2, KvHeads = 1, HeadDim = 16, FfDim = 64, MaxPositions = 256,
            Rope = new RopeSettings(10000f), NormEpsilon = 1e-6f, QkNorm = architecture == QwenPlugin.Qwen3ForCausalLM, TieEmbeddings = true,
            QkvBias = architecture == QwenPlugin.Qwen2ForCausalLM,
        };
        var files = new ModelFiles
        {
            Tokenizer = PretrainedTokenizers.Load(tokenizerFolder), EosToken = "<|im_end|>",
            ChatTemplate = "{%- for m in messages %}{{ '<|im_start|>' + m.role + '\\n' + m.content + '<|im_end|>\\n' }}{%- endfor %}{%- if add_generation_prompt %}{{ '<|im_start|>assistant\\n' }}{%- endif %}",
        };
        var folder = temp.File("model-" + architecture);
        using (PretrainedModel.Create(folder, spec, architecture, files, new CreateOptions { Device = Idrak.Abstraction.Device.Cpu, Seed = 91, Overwrite = true }))
        {
        }

        return folder;
    }

    /// <summary>A C# solution whose test Adds fails (Add subtracts) and Doubles passes.</summary>
    private static string WriteCalcWorkspace(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Lib"));
        Directory.CreateDirectory(Path.Combine(folder, "Lib.Tests"));
        File.WriteAllText(Path.Combine(folder, "Calc.slnx"), "<Solution>\n  <Project Path=\"Lib/Lib.csproj\" />\n  <Project Path=\"Lib.Tests/Lib.Tests.csproj\" />\n</Solution>\n");
        File.WriteAllText(Path.Combine(folder, "Lib", "Lib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(folder, "Lib", "Calc.cs"), "namespace Lib;\n\npublic static class Calc\n{\n    public static int Add(int a, int b) => a - b;\n\n    public static int Twice(int a) => a * 2;\n}\n");
        File.WriteAllText(Path.Combine(folder, "Lib.Tests", "Lib.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
                <PackageReference Include="xunit" Version="2.9.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(folder, "Lib.Tests", "CalcTests.cs"), """
            using Xunit;

            namespace Lib.Tests;

            public class CalcTests
            {
                [Fact]
                public void Adds() => Assert.Equal(5, Calc.Add(2, 3));

                [Fact]
                public void Doubles() => Assert.Equal(4, Calc.Twice(2));
            }
            """);
        return folder;
    }

    /// <summary>A policy of two replies: the fix of Add (or nothing), then done.</summary>
    private sealed class ScriptedPolicy(bool fix) : IPolicy<ChatObservation, ChatMessage>
    {
        public ValueTask<PolicyActions<ChatMessage>> ActAsync(IReadOnlyList<ChatObservation> observations, ActionSampling sampling, CancellationToken cancellationToken = default)
        {
            var actions = observations.Select(o => fix && !o.Messages.Any(m => m.Role == "assistant")
                ? new ChatMessage("assistant", "Fixing Add.", ToolCalls: [new ToolCall("edit_file", new JsonObject { ["path"] = "Lib/Calc.cs", ["old_text"] = "a - b", ["new_text"] = "a + b" })])
                : new ChatMessage("assistant", "Done.")).ToList();
            return ValueTask.FromResult(new PolicyActions<ChatMessage>(actions, [.. actions.Select(_ => new ActionRecord([0], [0f]))]));
        }

        public UpdateStats Update(RolloutBatch<ChatObservation, ChatMessage> batch, IPolicyLoss loss, IReadOnlyList<int>? episodes = null, IReadOnlyDictionary<string, string>? lossOptions = null) =>
            new(0f, 0, 0);

        public void Save(string path)
        {
        }

        public void Load(string path)
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A destination that keeps what it was sent.</summary>
    private sealed class FakeDestination : IShareDestination
    {
        public List<(string Target, IReadOnlyList<ShareItem> Items)> Sent { get; } = [];

        public string Name => "fake";

        public string Summary => "a test's destination";

        public Task SendAsync(string target, IReadOnlyList<ShareItem> items, CancellationToken cancellationToken)
        {
            lock (Sent)
            {
                Sent.Add((target, [.. items]));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A presenter that answers each question from a queue (none queued: dismissed) and keeps the questions and messages.</summary>
    private sealed class Presenter : IEditorPresenter
    {
        public Queue<int> Answers { get; } = new();

        public List<EditorQuestion> Questions { get; } = [];

        public List<string> Messages { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(title + ": " + message);
            return Task.CompletedTask;
        }

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Questions.Add(question);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : -1);
        }
    }

    /// <summary>A host with the learning plug-in active over a workspace with one open file, src/A.cs, its settings from <c>vars</c>.</summary>
    private sealed class World : IDisposable
    {
        private readonly LearningPackage _package = new();

        public World(TempFolder temp, Dictionary<string, string> vars)
            : this(temp.Path, vars)
        {
        }

        public World(string folder, Dictionary<string, string> vars)
        {
            Workspace = Path.Combine(folder, "workspace");
            Directory.CreateDirectory(Path.Combine(Workspace, "src"));
            File.WriteAllText(Path.Combine(Workspace, "src", "A.cs"), "");
            vars.TryAdd("CRAFT_PLUGIN_LEARNING_SETTLESECONDS", "3600");
            Destination = new FakeDestination();
            ShareDestinations.Register(Destination);
            IdrakPlugins.RegisterAll(_package);
            // The host's UI thread is the test's: what the session posts waits in a queue the test pumps.
            Host = new EditorHost(new CraftOptions { DataFolder = Path.Combine(folder, "data") }, _queue.Enqueue, Presenter, name => vars.TryGetValue(name, out var v) ? v : null)
            {
                WorkspaceFolder = Workspace,
            };
            Document = TextDocument.Create("");
            Open = Host.Open(Document, null);
            Host.RefreshPlugins();
            Session = LearningPlugin.Session ?? throw new InvalidOperationException("the learning plug-in did not activate");
            for (var watch = Stopwatch.StartNew(); Session.Consents is null && watch.Elapsed < TimeSpan.FromSeconds(10);)
            {
                Thread.Sleep(5);                                       // the consents are read off the UI thread
            }

            Pump();
        }

        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();

        /// <summary>Runs what was posted to the UI thread, until nothing is left.</summary>
        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        public string Workspace { get; }

        public Presenter Presenter { get; } = new();

        public FakeDestination Destination { get; }

        public EditorHost Host { get; }

        public TextDocument Document { get; }

        public IEditorDocument Open { get; }

        public LearningSession Session { get; }

        /// <summary>Types <paramref name="text"/> at <paramref name="at"/> (the end when -1); the end after it.</summary>
        public int Type(string text, int at = -1) => Insert(at < 0 ? Document.Buffer.Current.Length : at, text, EditKind.Typing);

        public int Insert(int at, string text, EditKind kind)
        {
            var selection = new SelectionState(at, at);
            Document.Edit([new TextEdit(at, 0, text)], kind, selection, new SelectionState(at + text.Length, at + text.Length));
            return Document.Buffer.Current.Length;
        }

        public void Replace(int at, int length, string text)
        {
            var selection = new SelectionState(at, at + length);
            Document.Edit([new TextEdit(at, length, text)], EditKind.Typing, selection, new SelectionState(at + text.Length, at + text.Length));
        }

        /// <summary>An inline completion's answer at <paramref name="at"/>, as the AI plug-in raises it.</summary>
        public void Answer(int at, string answer, string prompt = "class A { ") =>
            AiEvents.Answered(new AiAnswer(AiEvents.NewId(), AiAnswerKinds.Completion, prompt, answer)
            {
                Plugin = "ai", Model = "test", Snapshot = Document.Buffer.Current, Position = at, FilePath = Path.Combine(Workspace, "src", "A.cs"),
                LanguageName = "csharp", WorkspaceFolder = Workspace,
            });

        /// <summary>The answer, then the UI thread's turn (the sink posts it there).</summary>
        public void AnswerNow(int at, string answer, string prompt = "class A { ")
        {
            Answer(at, answer, prompt);
            Pump();
        }

        /// <summary>Waits for the session's file work (episodes, consents) to end.</summary>
        public async Task Idle()
        {
            Pump();
            await Session.Background;
            Pump();
        }

        public void Dispose()
        {
            Host.Dispose();
            Document.Dispose();
            IdrakPlugins.UnregisterAll(_package);
            ShareDestinations.Unregister("fake");
        }
    }
}
