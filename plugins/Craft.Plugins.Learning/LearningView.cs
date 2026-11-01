// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using Craft.Abstractions;
using Craft.Plugins.Learning.Abstractions;

namespace Craft.Plugins.Learning;

/// <summary>Where learning stands, as the Learning view says it (B11).</summary>
public enum LearningPhase
{
    /// <summary>Recording is off (no yes to the consent, a changed text, or the setting turned it off).</summary>
    Off,

    /// <summary>Recording, with fewer episodes than a training needs.</summary>
    Recording,

    /// <summary>Enough episodes (or a trainer that needs none): Train now can run.</summary>
    Ready,

    /// <summary>A training round runs.</summary>
    Training,

    /// <summary>At least one round has run: its adapter was promoted or kept back.</summary>
    Trained,
}

/// <summary>The actions of the Learning view (<see cref="IEditorViewPanel.ActAsync"/>'s ids).</summary>
public static class LearningActions
{
    /// <summary>The recording switch ("on" asks the consent, "off" withdraws it).</summary>
    public const string Record = "record";

    /// <summary>The sharing switch ("on" asks for a destination inline when none is named, then the consent; "off" stops sharing).</summary>
    public const string Share = "share";

    /// <summary>The destination entered inline (the text).</summary>
    public const string ShareTo = "share.to";

    /// <summary>Closes the inline destination without naming one.</summary>
    public const string ShareCancel = "share.cancel";

    /// <summary>Shares now (after the preview).</summary>
    public const string ShareNow = "share.now";

    /// <summary>Opens the learning folder.</summary>
    public const string OpenFolder = "folder.open";

    /// <summary>Picks another learning folder.</summary>
    public const string ChangeFolder = "folder.change";

    /// <summary>Exports the episodes.</summary>
    public const string Export = "export";

    /// <summary>Deletes the episodes (with Undo).</summary>
    public const string Delete = "delete";

    /// <summary>Picks the model trained (the value: a model name, "" for the AI plug-in's).</summary>
    public const string Model = "model";

    /// <summary>Picks the method (preference, tasks, both).</summary>
    public const string Trainer = "trainer";

    /// <summary>Trains now.</summary>
    public const string Train = "train";

    /// <summary>Cancels the running training.</summary>
    public const string Cancel = "train.cancel";

    /// <summary>The idle-training switch.</summary>
    public const string Idle = "idle";

    /// <summary>The idle minutes (the text).</summary>
    public const string IdleMinutes = "idle.minutes";

    /// <summary>Goes back one adapter.</summary>
    public const string Rollback = "rollback";

    /// <summary>"base": the base model answers (the adapter kept); "adapter": the adapter answers again.</summary>
    public const string Use = "use";

    /// <summary>Deletes an adapter version (asked which).</summary>
    public const string DeleteVersion = "version.delete";

    /// <summary>Makes tasks from the failing tests.</summary>
    public const string Tasks = "tasks";

    /// <summary>Marks the test at the caret.</summary>
    public const string Mark = "mark";

    /// <summary>Opens the learning settings.</summary>
    public const string Settings = "settings";

    /// <summary>Opens the AI chat.</summary>
    public const string Chat = "chat";

    /// <summary>Takes the last message or error away.</summary>
    public const string Dismiss = "dismiss";
}

/// <summary>
/// Everything the Learning view shows, read once (the figures off the UI thread): the consents, the folder, the episodes,
/// the training's settings and state, the models the provider holds, the adapters, the tasks and the test at the caret.
/// </summary>
public sealed record LearningViewState
{
    /// <summary>The learning consent's state.</summary>
    public ConsentState Learn { get; init; } = ConsentState.Undecided;

    /// <summary>The learning consent's record (its scope and time), or null.</summary>
    public ConsentRecord? LearnRecord { get; init; }

    /// <summary>The sharing consent's state.</summary>
    public ConsentState Share { get; init; } = ConsentState.Undecided;

    /// <summary>The settings now.</summary>
    public LearningSettings Settings { get; init; } = new();

    /// <summary>Whether the destination is being asked inline (the share switch turned on with none named).</summary>
    public bool AskingDestination { get; init; }

    /// <summary>The learning folder.</summary>
    public string Folder { get; init; } = "";

    /// <summary>The workspace's episodes kept.</summary>
    public int Episodes { get; init; }

    /// <summary>Their bytes on disk.</summary>
    public long Bytes { get; init; }

    /// <summary>The verdicts and their counts ("accepted 3 · edited 1").</summary>
    public string Verdicts { get; init; } = "";

    /// <summary>The newest episodes (newest first).</summary>
    public IReadOnlyList<EditorEpisode> Recent { get; init; } = [];

    /// <summary>The answers waiting for their verdict.</summary>
    public int Pending { get; init; }

    /// <summary>The model trained (the setting's, else the AI plug-in's), or null.</summary>
    public string? Model { get; init; }

    /// <summary>The AI plug-in's (the chat's) model, or null.</summary>
    public string? ChatModel { get; init; }

    /// <summary>The models the provider offers (the one set, those loaded, those picked before).</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>The models loaded now, as the provider says them ("name · in use").</summary>
    public IReadOnlyList<string> Loaded { get; init; } = [];

    /// <summary>The device the provider's models run on, or null.</summary>
    public string? Device { get; init; }

    /// <summary>Whether a provider lends its copy (B7: training then takes the chat's copy, never a second).</summary>
    public bool HasProvider { get; init; }

    /// <summary>The running training's progress, or null when none runs.</summary>
    public TrainingProgress? Progress { get; init; }

    /// <summary>Whether a training runs.</summary>
    public bool Training { get; init; }

    /// <summary>Every adapter version, oldest first.</summary>
    public IReadOnlyList<AdapterVersion> Versions { get; init; } = [];

    /// <summary>The promoted adapter, or null.</summary>
    public AdapterVersion? Current { get; init; }

    /// <summary>Whether the base model answers though an adapter is promoted (Use base model).</summary>
    public bool UseBase { get; init; }

    /// <summary>The stage the last training failed in (<see cref="LearningStages"/>), or null.</summary>
    public string? FailureStage { get; init; }

    /// <summary>The last action's message (or the failure's words).</summary>
    public string Status { get; init; } = "";

    /// <summary>The failing tests the last run found (-1: not run yet).</summary>
    public int FailingTests { get; init; } = -1;

    /// <summary>The tasks written (training and held out).</summary>
    public int Tasks { get; init; }

    /// <summary>The held-out tasks.</summary>
    public int HeldTasks { get; init; }

    /// <summary>The test the caret is in ("CalcTests.AddsTwoNumbers"), or null when it is in none.</summary>
    public string? TestAtCaret { get; init; }

    /// <summary>The tests marked as tasks.</summary>
    public IReadOnlyList<string> Marked { get; init; } = [];

    /// <summary>Whether a folder is open.</summary>
    public bool HasWorkspace { get; init; }

    /// <summary>Whether the AI chat panel is there (the checklist's "Open the chat").</summary>
    public bool HasChat { get; init; }

    /// <summary>The time the state was read (ages are told from it).</summary>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether episodes are recorded.</summary>
    public bool Recording => Learn == ConsentState.Granted && !Settings.ConsentOff;

    /// <summary>Whether a training can start: recording, a model, and enough episodes for the preference trainer.</summary>
    public bool CanTrain => Recording && Model is not null && !Training && (Settings.Trainer != "preference" || Episodes >= Settings.MinEpisodes);

    /// <summary>Where learning stands.</summary>
    public LearningPhase Phase =>
        Training ? LearningPhase.Training
        : !Recording ? LearningPhase.Off
        : Versions.Count > 0 ? LearningPhase.Trained
        : Settings.Trainer != "preference" || Episodes >= Settings.MinEpisodes ? LearningPhase.Ready
        : LearningPhase.Recording;
}

/// <summary>
/// The Learning view (B11), headless: from a <see cref="LearningViewState"/> the blocks the shell draws (<see cref="PanelBlock"/>):
/// what learning does and where you are (Record → Train → Better suggestions), a guided start until the first round, the
/// switches for recording and sharing with their words, the folder, the storage, the training (the model and the method,
/// Train now with its state, idle training, the rounds' charts), the adapter that answers, and practice tasks from the
/// tests. Every control names its variable; every error says its stage and what to change.
/// </summary>
public static class LearningView
{
    /// <summary>A record dot (the first step).</summary>
    public const string RecordIcon = "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0M4 12a8 8 0 1 0 16 0a8 8 0 1 0 -16 0";

    /// <summary>A loop of arrows (the second step: training).</summary>
    public const string TrainIcon = "M4 12a8 8 0 0 1 14-5.3M20 4v4h-4M20 12a8 8 0 0 1-14 5.3M4 20v-4h4";

    /// <summary>A spark (the third step: better suggestions).</summary>
    public const string BetterIcon = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8zM19 16l.8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8z";

    /// <summary>A cap: the Learning view's rail icon and header.</summary>
    public const string LearningIcon = "M2 9l10-5 10 5-10 5zM6 11v5c3 2 9 2 12 0v-5M22 9v6";

    /// <summary>The two sentences of what learning does.</summary>
    public const string Explanation = "Craft can learn from how you accept, edit or reject the AI's suggestions, and train a small adapter so the model writes code more like you. Everything stays on this machine unless you choose to share.";

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>The variable of the learning setting <paramref name="key"/>.</summary>
    public static string Variable(string key) => PluginSetting.VariableName(LearningSettings.PluginName, key);

    /// <summary>The view's blocks for <paramref name="state"/>.</summary>
    public static IReadOnlyList<PanelBlock> Build(LearningViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var blocks = new List<PanelBlock> { Header(state) };
        if (state.FailureStage is not null && state.Status.Length > 0 && !state.Training)
        {
            blocks.Add(Failure(state));
        }
        else if (state.Status.Length > 0)
        {
            blocks.Add(new PanelText(state.Status, PanelTextStyle.Secondary));
        }

        if (state.Versions.Count == 0 && !state.Training)
        {
            blocks.Add(GettingStarted(state));
        }

        blocks.Add(RecordingAndSharing(state));
        if (state.Recording || state.Episodes > 0)
        {
            blocks.Add(Storage(state));
        }

        if (state.Recording || state.Versions.Count > 0)
        {
            blocks.Add(TrainingCard(state));
        }

        if (state.Versions.Count > 0)
        {
            blocks.Add(Adapter(state));
        }

        if (state.Recording && state.HasWorkspace)
        {
            blocks.Add(Tasks(state));
        }

        return blocks;
    }

    // ---- the cards ----

    private static PanelCard Header(LearningViewState s)
    {
        var phase = s.Phase;
        bool better = s.Current is not null && !s.UseBase;
        var record = phase == LearningPhase.Off ? PanelStepState.Next
            : phase == LearningPhase.Recording ? PanelStepState.Current : PanelStepState.Done;
        var train = phase is LearningPhase.Ready or LearningPhase.Training || (phase == LearningPhase.Trained && !better) ? PanelStepState.Current
            : better ? PanelStepState.Done : PanelStepState.Next;
        var suggestions = better ? PanelStepState.Current : PanelStepState.Next;
        string recordDetail = phase == LearningPhase.Off ? "off"
            : string.Create(C, $"{s.Episodes} episode{(s.Episodes == 1 ? "" : "s")}");
        string trainDetail = phase == LearningPhase.Training ? "running"
            : s.Versions.Count > 0 ? string.Create(C, $"{s.Versions.Count} round{(s.Versions.Count == 1 ? "" : "s")}")
            : phase == LearningPhase.Ready ? "ready" : string.Create(C, $"at {s.Settings.MinEpisodes}");
        string betterDetail = better ? $"v{s.Current!.Number.ToString(C)}" : s.Current is not null ? "base model in use" : "after a better round";
        return new PanelCard("Learn from your edits",
        [
            new PanelText(Explanation, PanelTextStyle.Secondary),
            new PanelSteps(
            [
                new PanelStep("Record", RecordIcon, record) { Detail = recordDetail },
                new PanelStep("Train", TrainIcon, train) { Detail = trainDetail },
                new PanelStep("Better suggestions", BetterIcon, suggestions) { Detail = betterDetail },
            ])
            {
                Tooltip = "Record: your verdicts on the AI's answers become episodes. Train: a small adapter learns from them. Better suggestions: it answers only once it scored better on episodes it never saw.",
            },
        ])
        {
            Icon = LearningIcon,
            Badge = phase switch
            {
                LearningPhase.Off => "off",
                LearningPhase.Recording => "recording",
                LearningPhase.Ready => "ready to train",
                LearningPhase.Training => "training",
                _ => better ? "adapter in use" : "trained",
            },
        };
    }

    private static PanelCard Failure(LearningViewState s) =>
        new($"Training failed at its {s.FailureStage} stage",
        [
            new PanelText(s.Status, PanelTextStyle.Secondary),
            new PanelText(StageMeaning(s.FailureStage!), PanelTextStyle.Caption),
            new PanelButtons(
            [
                new PanelButton("Learning settings", LearningActions.Settings) { Tooltip = "Opens the learning settings, where each variable named above is" },
                new PanelButton("Dismiss", LearningActions.Dismiss, PanelButtonKind.Flat),
            ]),
        ])
        { Tone = PanelTone.Error };

    /// <summary>What a stage does, in a line (B7's stage names), for an error that names it.</summary>
    public static string StageMeaning(string stage) => stage switch
    {
        LearningStages.Load => "The load stage loads the model (the chat's copy, or one of its own), its family and the current adapter.",
        LearningStages.Episodes => "The episodes stage reads the recorded episodes from the folder.",
        LearningStages.Pairs => "The pairs stage turns the episodes into preferred and rejected answers.",
        LearningStages.Step => "The step stage trains the adapter, a batch at a time.",
        LearningStages.Evaluate => "The evaluate stage scores the adapter on the held-out episodes.",
        LearningStages.Promote => "The promote stage writes the adapter and makes it current when it scored better.",
        _ => $"The {stage} stage.",
    };

    private static PanelCard GettingStarted(LearningViewState s)
    {
        bool recording = s.Recording;
        int min = s.Settings.MinEpisodes;
        var items = new List<PanelCheck>
        {
            new("Turn on recording", recording, new PanelButton("Turn on", LearningActions.Record, PanelButtonKind.Primary) { Value = "on", Enabled = !s.Settings.ConsentOff })
            {
                Detail = s.Settings.ConsentOff ? $"{Variable("consent")} is off: learning stays off until it is ask." : "You are asked once, with what is kept, where and for how long.",
            },
            new("Code with the AI: accept, edit or reject its suggestions", s.Episodes > 0, s.HasChat ? new PanelButton("Open the chat", LearningActions.Chat) : null)
            {
                Detail = "Each choice is one episode: what the AI proposed and what you kept.",
            },
            new(string.Create(C, $"Train when there are {min} episodes"), false,
                new PanelButton("Train now", LearningActions.Train, recording && s.CanTrain ? PanelButtonKind.Primary : PanelButtonKind.Secondary) { Enabled = s.CanTrain })
            {
                Detail = string.Create(C, $"{Math.Min(s.Episodes, min)} of {min} so far · {Variable("minEpisodes")}"),
            },
        };
        return new PanelCard("Getting started",
        [
            new PanelChecklist(items),
            new PanelDisclosure("sample", "What is recorded?",
            [
                new PanelText("One line per answer in episodes.jsonl in the folder below: the context the AI saw, its answer, what you did with it and the text you kept. Nothing else is read.", PanelTextStyle.Secondary),
                new PanelText(SampleEpisode(), PanelTextStyle.Code),
            ]),
        ])
        { Icon = "M9 11l3 3L22 4M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" };
    }

    private static PanelCard RecordingAndSharing(LearningViewState s)
    {
        var settings = s.Settings;
        string learnDetail = s.Recording
            ? string.Create(C, $"On {(s.LearnRecord?.Scope == ConsentStore.AllWorkspaces ? "in all workspaces" : "in this workspace")} since {s.LearnRecord?.Time.ToLocalTime():yyyy-MM-dd HH:mm}. Turning it off stops recording at once and asks whether to delete what was kept.")
            : settings.ConsentOff ? $"Off: {Variable("consent")} turns learning off, and a setting cannot turn it on."
            : s.Learn == ConsentState.Changed ? "Off until you answer again: what is recorded or where changed since you said yes."
            : string.Create(C, $"Keeps the AI's answers and what you did with them (accepted, edited, rejected, undone) in the folder below, at most {settings.CapMegabytes} MiB for {settings.KeepDays} days. Nothing leaves this machine. You are asked once, with every detail.");
        var blocks = new List<PanelBlock>
        {
            new PanelSwitch(LearningActions.Record, "Record on this machine", s.Recording)
            {
                Detail = learnDetail,
                Enabled = !settings.ConsentOff,
                Tooltip = "Your answer is stored with its time, scope and the words you agreed to; a changed text asks again.",
                Variable = Variable("consent"),
            },
        };

        bool shareOn = s.Share == ConsentState.Granted;
        string shareDetail = !s.Recording ? "Turn on recording first: nothing can be shared before."
            : settings.SharingOff ? $"Off: {Variable("sharing")} turns sharing off."
            : shareOn ? $"On, to {settings.ShareTo}: {string.Join(", ", settings.ShareWhat)}. Every share shows you each item first; .gitignore'd files and secrets are left out."
            : settings.ShareTo.Length == 0 ? "Nothing is shared until you name a destination and agree. Turn it on to name one."
            : $"Off. Turning it on asks you before anything goes to {settings.ShareTo}.";
        blocks.Add(new PanelSwitch(LearningActions.Share, "Share what was learned", shareOn || s.AskingDestination)
        {
            Detail = shareDetail,
            Enabled = s.Recording && !settings.SharingOff,
            Tooltip = "What a share sends: " + string.Join(", ", settings.ShareWhat) + $" ({Variable("shareWhat")})",
            Variable = Variable("shareTo"),
        });
        if (s.AskingDestination && !shareOn)
        {
            blocks.Add(new PanelInput(LearningActions.ShareTo, "Where to share: folder:PATH, https://ADDRESS or kind:target", settings.ShareTo)
            {
                Placeholder = "folder:D:\\Shared\\learning",
                Submit = "Save and ask",
                Code = true,
            });
            blocks.Add(new PanelButtons([new PanelButton("Cancel", LearningActions.ShareCancel, PanelButtonKind.Flat)]));
        }
        else if (shareOn)
        {
            blocks.Add(new PanelButtons([new PanelButton("Share now…", LearningActions.ShareNow) { Tooltip = "Shows every item that would go, and sends nothing until you confirm" }]));
        }

        blocks.Add(new PanelCard("Folder",
        [
            new PanelText(s.Folder, PanelTextStyle.Code) { Tooltip = "Episodes, tasks, adapters and your answers are kept here", Variable = Variable("folder") },
            new PanelButtons(
            [
                new PanelButton("Open", LearningActions.OpenFolder) { Tooltip = "Opens it in the system's file manager" },
                new PanelButton("Change…", LearningActions.ChangeFolder, PanelButtonKind.Flat) { Tooltip = "Picks another folder for what is kept from now on; what is there stays" },
            ]),
        ]));
        return new PanelCard("Recording and sharing", blocks) { Icon = RecordIcon };
    }

    private static PanelCard Storage(LearningViewState s)
    {
        long cap = (long)s.Settings.CapMegabytes * 1024 * 1024;
        double fraction = cap <= 0 ? 0 : Math.Min(1, (double)s.Bytes / cap);
        var blocks = new List<PanelBlock>
        {
            new PanelMeter(string.Create(C, $"{s.Episodes} episode{(s.Episodes == 1 ? "" : "s")}"), fraction)
            {
                Detail = $"{SharePlan.Bytes(s.Bytes)} of {s.Settings.CapMegabytes.ToString(C)} MiB",
                Tone = fraction >= 0.9 ? PanelTone.Warning : PanelTone.Accent,
                Tooltip = string.Create(C, $"Past the limit the oldest episodes go; each is kept {s.Settings.KeepDays} days ({Variable("keepDays")})"),
                Variable = Variable("capMb"),
            },
            new PanelText(s.Verdicts.Length == 0 ? "None yet: each answer you accept, edit or reject adds one." : s.Verdicts
                + (s.Pending > 0 ? string.Create(C, $" · {s.Pending} waiting for your edits to settle") : ""), PanelTextStyle.Secondary)
            {
                Tooltip = string.Create(C, $"An answer's verdict settles {s.Settings.SettleSeconds} s after your last edit of it ({Variable("settleSeconds")})"),
            },
        };
        foreach (var episode in s.Recent.Take(3))
        {
            string answer = episode.Answer.ReplaceLineEndings(" ⏎ ");
            blocks.Add(new PanelText($"{episode.Verdict} · {episode.Kind}{(episode.File is { } f ? " · " + f : "")}: {(answer.Length > 48 ? answer[..48] + "…" : answer)}", PanelTextStyle.Caption));
        }

        blocks.Add(new PanelButtons(
        [
            new PanelButton("Export", LearningActions.Export) { Enabled = s.Episodes > 0, Tooltip = "Writes them as JSON lines under the folder's exports" },
            new PanelButton("Delete", LearningActions.Delete, PanelButtonKind.Danger) { Enabled = s.Episodes > 0, Tooltip = "Deletes this workspace's episodes, with Undo for a few seconds; adapters stay" },
        ]));
        return new PanelCard("Storage", blocks) { Icon = "M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" };
    }

    private static PanelCard TrainingCard(LearningViewState s)
    {
        var settings = s.Settings;
        var blocks = new List<PanelBlock>
        {
            ModelPicker(s),
            new PanelFacts(
            [
                new PanelFact("Device", settings.Device.Equals("auto", StringComparison.OrdinalIgnoreCase) ? (s.Device is { } device ? $"the chat's copy, on {device}" : "where the chat's copy is") : settings.Device),
                new PanelFact("Weights", settings.Weights.Length == 0 ? "the chat's" : settings.Weights),
                new PanelFact("Adapter", string.Create(C, $"rank {settings.Rank} · {settings.Steps} steps · batch {(settings.BatchTokens == 0 ? "fitted to free memory" : settings.BatchTokens + " tokens")}")),
            ])
            { Tooltip = $"{Variable("device")}, {Variable("weights")}, {Variable("rank")}, {Variable("steps")}, {Variable("batchTokens")}" },
            new PanelPicker(LearningActions.Trainer, "Method",
            [
                new PanelOption("preference", "Your verdicts (preference)") { Detail = "Learns to prefer what you kept over what you rejected, from your recorded episodes." },
                new PanelOption("tasks", "Practice tasks (tests)") { Detail = "Learns to make your failing tests pass, rewarded by the build and the tests in the sandbox." },
                new PanelOption("both", "Both") { Detail = "Practice tasks first, then your verdicts." },
            ], settings.Trainer)
            { Enabled = !s.Training, Variable = Variable("trainer") },
        };

        if (s.Training)
        {
            var p = s.Progress;
            double fraction = p is { Steps: > 0, Step: > 0 } ? (double)p.Step / p.Steps : -1;
            string figure = p is null ? "starting" : p.Steps > 0 && p.Step > 0 ? string.Create(C, $"step {p.Step} of {p.Steps}") : p.Stage;
            blocks.Add(new PanelMeter(p?.Stage is { } stage ? Capital(stage) : "Training", fraction) { Detail = figure });
            var line = new List<string>();
            if (p is not null && float.IsFinite(p.Loss))
            {
                line.Add(string.Create(C, $"loss {p.Loss:F3}"));
            }

            if (p?.Left is { } left)
            {
                line.Add(string.Create(C, $"{left:m\\:ss} left"));
            }

            if (p is { Device.Length: > 0 })
            {
                line.Add("on " + p.Device);
            }

            if (p?.Note is { Length: > 0 } note)
            {
                line.Add(note);
            }

            if (line.Count > 0)
            {
                blocks.Add(new PanelText(string.Join(" · ", line), PanelTextStyle.Secondary));
            }

            blocks.Add(new PanelButtons([new PanelButton("Cancel", LearningActions.Cancel) { Tooltip = "Stops at the next step; nothing is written and the current adapter stays" }]));
        }
        else
        {
            if (s.Model is null)
            {
                blocks.Add(new PanelText("No model to train: pick one above, or set the chat's model.", PanelTextStyle.Secondary) { Tone = PanelTone.Warning });
            }
            else if (settings.Trainer == "preference" && s.Episodes < settings.MinEpisodes)
            {
                blocks.Add(new PanelMeter("Waiting for episodes", (double)s.Episodes / settings.MinEpisodes)
                {
                    Detail = string.Create(C, $"{s.Episodes} of {settings.MinEpisodes}"),
                    Tooltip = "A round needs this many episodes, so the adapter learns from enough of your choices",
                    Variable = Variable("minEpisodes"),
                });
            }
            else
            {
                blocks.Add(new PanelText(settings.Trainer == "preference"
                    ? string.Create(C, $"Ready: {s.Episodes} episodes. A round trains a new adapter and keeps it only if it scores better on episodes it never saw.")
                    : "Ready: a round practises the tasks made from your tests.", PanelTextStyle.Secondary));
            }

            blocks.Add(new PanelButtons([new PanelButton("Train now", LearningActions.Train, PanelButtonKind.Primary) { Enabled = s.CanTrain, Tooltip = "Runs one round off the editor's thread; the status bar shows it with Cancel" }]));
        }

        bool idle = settings.Schedule == "idle";
        blocks.Add(new PanelSwitch(LearningActions.Idle, string.Create(C, $"Also train when idle {settings.IdleMinutes} min"), idle)
        {
            Detail = string.Create(C, $"After {settings.IdleMinutes} minutes without typing, once {settings.MinEpisodes} new episodes are recorded."),
            Variable = Variable("schedule"),
        });
        if (idle)
        {
            blocks.Add(new PanelInput(LearningActions.IdleMinutes, "Idle minutes", settings.IdleMinutes.ToString(C)) { Numeric = true, Unit = "min", Submit = "Set", Variable = Variable("idleMinutes") });
        }

        if (s.Versions.Count > 0)
        {
            blocks.Add(Rounds(s));
        }

        return new PanelCard("Training", blocks) { Icon = TrainIcon, Badge = s.Training ? "running" : s.Phase == LearningPhase.Recording ? "waiting" : null };
    }

    private static PanelPicker ModelPicker(LearningViewState s)
    {
        var options = new List<PanelOption>
        {
            new("", s.ChatModel is { } chat ? $"The chat's model ({chat})" : "The chat's model (none set)")
            {
                Detail = s.HasProvider ? "Trains the copy the chat holds: one copy in memory, the chat's replies wait while a round runs." : "Loads its own copy for the round.",
            },
        };
        foreach (var name in s.Models)
        {
            if (options.Any(o => o.Value == name))
            {
                continue;
            }

            bool loaded = s.Loaded.Any(l => l.StartsWith(name, StringComparison.Ordinal));
            options.Add(new PanelOption(name, loaded ? name + " · loaded" : name)
            {
                Detail = name == s.ChatModel ? "The chat's model by name: its loaded copy is trained."
                    : loaded ? "Loaded now: its copy is trained in place." : "Not loaded: the provider loads it for the round (idle copies are let go first).",
            });
        }

        if (s.Settings.Model.Length > 0 && options.All(o => o.Value != s.Settings.Model))
        {
            options.Add(new PanelOption(s.Settings.Model, s.Settings.Model) { Detail = "Set in the settings." });
        }

        return new PanelPicker(LearningActions.Model, "Model", options, s.Settings.Model)
        {
            Enabled = !s.Training,
            Tooltip = "Its base weights never change: the adapter is a small set of weights beside them",
            Variable = Variable("model"),
        };
    }

    private static PanelCard Rounds(LearningViewState s)
    {
        var versions = s.Versions;
        var last = versions[^1];
        var marks = versions.Select(v => v.Promoted).ToList();
        var blocks = new List<PanelBlock>
        {
            new PanelChart("Loss over rounds", [.. versions.Select(v => (double)v.LastLoss)])
            {
                Marks = marks,
                Detail = string.Create(C, $"{last.FirstLoss:F3} → {last.LastLoss:F3}"),
                Tooltip = "The training loss at each round's last step: lower means it fits your choices better",
            },
            new PanelChart("Preference margin", [.. versions.Select(v => v.ScoreAfter)])
            {
                Marks = marks,
                Detail = string.Create(C, $"{last.ScoreBefore:F2} → {last.ScoreAfter:F2}"),
                Legend = "● promoted · ○ kept back. The margin is how much more the model prefers what you kept, on episodes it never trained on.",
                Tooltip = string.Create(C, $"A round is promoted when its margin beats the one before by more than {s.Settings.PromotionMargin} ({Variable("promotionMargin")})"),
            },
            last.Promoted
                ? new PanelText(string.Create(C, $"v{last.Number} was better ({last.ScoreBefore:F2} → {last.ScoreAfter:F2}) and was promoted: it answers now."), PanelTextStyle.Body) { Tone = PanelTone.Success }
                : new PanelText(string.Create(C, $"v{last.Number} was kept back: it scored {last.ScoreAfter:F2}, not better than {last.ScoreBefore:F2}. The answers stay as they were."), PanelTextStyle.Body) { Tone = PanelTone.Warning },
        };
        if (s.Current is not null)
        {
            blocks.Add(new PanelButtons([new PanelButton("Roll back", LearningActions.Rollback, PanelButtonKind.Flat) { Tooltip = "Goes back to the adapter before (or the base model)" }]));
        }

        return new PanelCard(string.Create(C, $"Rounds ({versions.Count})"), blocks);
    }

    private static PanelCard Adapter(LearningViewState s)
    {
        var current = s.Current;
        var facts = new List<PanelFact>();
        if (current is null)
        {
            facts.Add(new PanelFact("Answering", "the base model (no adapter promoted)"));
        }
        else
        {
            facts.Add(new PanelFact("Answering", s.UseBase ? $"the base model (v{current.Number.ToString(C)} kept aside)" : $"adapter v{current.Number.ToString(C)}") { Tone = s.UseBase ? PanelTone.Normal : PanelTone.Success });
            facts.Add(new PanelFact("Trained", Ago(s.Now - current.Time)));
            double gain = current.ScoreAfter - current.ScoreBefore;
            facts.Add(new PanelFact("Better by", string.Create(C, $"{(gain >= 0 ? "+" : "")}{gain:F2} margin on {current.HeldOut} held-out pairs")) { Tone = gain > 0 ? PanelTone.Success : PanelTone.Normal });
            facts.Add(new PanelFact("For", current.BaseModel));
        }

        var buttons = new List<PanelButton>();
        if (current is not null)
        {
            buttons.Add(s.UseBase
                ? new PanelButton($"Use adapter v{current.Number.ToString(C)}", LearningActions.Use, PanelButtonKind.Secondary) { Value = "adapter" }
                : new PanelButton("Use base model", LearningActions.Use) { Value = "base", Tooltip = "The base model answers; the adapter is kept and comes back with Use adapter" });
            buttons.Add(new PanelButton("Roll back", LearningActions.Rollback, PanelButtonKind.Flat) { Tooltip = "Goes back to the adapter promoted before (or the base model)" });
        }

        buttons.Add(new PanelButton("Delete a version…", LearningActions.DeleteVersion, PanelButtonKind.Danger) { Tooltip = string.Create(C, $"{s.Versions.Count} version(s) kept") });
        return new PanelCard("Adapter", [new PanelFacts(facts), new PanelButtons(buttons)]) { Icon = BetterIcon, Badge = current is null ? "none" : s.UseBase ? "paused" : "in use" };
    }

    private static PanelCard Tasks(LearningViewState s)
    {
        var blocks = new List<PanelBlock>
        {
            new PanelText("Turn failing tests into practice tasks the model is rewarded for solving.", PanelTextStyle.Secondary),
            new PanelFacts(
            [
                new PanelFact("Failing tests", s.FailingTests < 0 ? "not run yet" : s.FailingTests.ToString(C)) { Tone = s.FailingTests > 0 ? PanelTone.Warning : PanelTone.Normal },
                new PanelFact("Tasks", string.Create(C, $"{s.Tasks} ({s.HeldTasks} held out)")),
            ]),
            new PanelButtons(
            [
                new PanelButton("Make tasks", LearningActions.Tasks)
                {
                    Tooltip = $"Runs dotnet test through the sandbox {s.Settings.Sandbox}{(s.Settings.Sandbox == "approve-each" ? " (each command asks you first)" : "")} ({Variable("sandbox")})",
                },
            ]),
            new PanelText($"Through the sandbox {s.Settings.Sandbox}{(s.Settings.Sandbox == "approve-each" ? ": each command asks you first" : "")}.", PanelTextStyle.Caption) { Variable = Variable("sandbox") },
        };
        if (s.TestAtCaret is { } test)
        {
            blocks.Add(new PanelButtons([new PanelButton($"Mark {test} as a task", LearningActions.Mark, PanelButtonKind.Flat) { Tooltip = "The test your caret is in joins the next Make tasks, failing or not" }]));
        }

        if (s.Marked.Count > 0)
        {
            blocks.Add(new PanelText("Marked: " + string.Join(", ", s.Marked), PanelTextStyle.Caption));
        }

        return new PanelCard("Practice tasks from tests", blocks) { Icon = "M9 3h6M10 3v6L4 19a1.5 1.5 0 0 0 1.3 2h13.4a1.5 1.5 0 0 0 1.3-2L14 9V3" };
    }

    // ---- helpers ----

    /// <summary>A sample episode as the folder keeps it (indented), so the user sees exactly what one holds.</summary>
    public static string SampleEpisode()
    {
        var sample = new EditorEpisode("3f2a9c", new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), AiAnswerKinds.Completion,
            "public static int Add(int a, int b) => ", "a - b;", AiVerdicts.Edited)
        {
            Plugin = "ai",
            Model = "your model",
            File = "src/Calc.cs",
            Language = "csharp",
            Kept = "a + b;",
            Edited = 0.17,
        };
        using var json = JsonDocument.Parse(sample.ToJson());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>An age in words ("just now", "5 min ago", "3 h ago", "2 days ago").</summary>
    public static string Ago(TimeSpan age) =>
        age < TimeSpan.FromMinutes(1) ? "just now"
        : age < TimeSpan.FromHours(1) ? string.Create(C, $"{(int)age.TotalMinutes} min ago")
        : age < TimeSpan.FromDays(1) ? string.Create(C, $"{(int)age.TotalHours} h ago")
        : string.Create(C, $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago");

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
