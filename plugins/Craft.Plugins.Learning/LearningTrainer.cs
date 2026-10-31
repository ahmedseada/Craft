// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Craft.Abstractions;
using Idrak.Abstraction.Diagnostics;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Reinforcement;
using Idrak.Models;
using Idrak.Reinforcement;
using Idrak.Text;
using Idrak.Text.Abstractions;
using Idrak.Text.Reinforcement;

namespace Craft.Plugins.Learning;

/// <summary>One trained adapter of a workspace: what it was trained on, how it scored on the held-out set before and after, and whether it was promoted.</summary>
/// <param name="Number">Its version (1, 2, …).</param>
/// <param name="Folder">Its folder (PEFT layout).</param>
/// <param name="Time">When it was trained.</param>
/// <param name="BaseModel">The model it adapts, as named.</param>
/// <param name="Episodes">The episodes (or task episodes) it trained on.</param>
/// <param name="Pairs">The preference pairs it trained on (0 for the tasks).</param>
/// <param name="HeldOut">The held-out pairs (or tasks' episodes) it was measured on.</param>
/// <param name="Steps">The optimizer steps (or iterations) it ran.</param>
/// <param name="FirstLoss">The first step's loss.</param>
/// <param name="LastLoss">The last step's loss.</param>
/// <param name="ScoreBefore">The held-out score of the adapter it would replace (the base model when none was promoted).</param>
/// <param name="ScoreAfter">Its own held-out score.</param>
/// <param name="Promoted">Whether it was promoted when it was trained.</param>
/// <param name="Note">Why it was or was not promoted.</param>
public sealed record AdapterVersion(int Number, string Folder, DateTimeOffset Time, string BaseModel, int Episodes, int Pairs, int HeldOut, int Steps, float FirstLoss,
    float LastLoss, double ScoreBefore, double ScoreAfter, bool Promoted, string Note);

/// <summary>
/// A workspace's adapters: every version kept (<c>adapters/vN</c>, until the user deletes it), the current one
/// (<c>current.txt</c>) and the promotions in order (<c>promotions.txt</c>), so a rollback goes back to the one before.
/// </summary>
public sealed class AdapterVersions
{
    private readonly string _folder;

    /// <summary>The adapters under <paramref name="workspaceFolder"/> (the workspace's learning folder).</summary>
    public AdapterVersions(string workspaceFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceFolder);
        _folder = Path.Combine(workspaceFolder, "adapters");
    }

    /// <summary>The adapters' folder.</summary>
    public string Folder => _folder;

    /// <summary>Every version kept, oldest first.</summary>
    public IReadOnlyList<AdapterVersion> All
    {
        get
        {
            if (!Directory.Exists(_folder))
            {
                return [];
            }

            var versions = new List<AdapterVersion>();
            foreach (var folder in Directory.GetDirectories(_folder, "v*"))
            {
                if (Read(folder) is { } version)
                {
                    versions.Add(version);
                }
            }

            versions.Sort((a, b) => a.Number.CompareTo(b.Number));
            return versions;
        }
    }

    /// <summary>The current adapter, or null when none is promoted (the base model answers).</summary>
    public AdapterVersion? Current => CurrentNumber is { } number ? All.FirstOrDefault(v => v.Number == number) : null;

    /// <summary>The promotions in order (the last is the current one).</summary>
    public IReadOnlyList<int> Promotions
    {
        get
        {
            string path = Path.Combine(_folder, "promotions.txt");
            return File.Exists(path)
                ? [.. File.ReadAllLines(path).Select(l => int.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1).Where(n => n > 0)]
                : [];
        }
    }

    private int? CurrentNumber
    {
        get
        {
            var promotions = Promotions;
            return promotions.Count == 0 ? null : promotions[^1];
        }
    }

    /// <summary>The folder of the next version (not made yet).</summary>
    public (int Number, string Folder) Next()
    {
        int number = All.Select(v => v.Number).DefaultIfEmpty(0).Max() + 1;
        return (number, Path.Combine(_folder, string.Create(CultureInfo.InvariantCulture, $"v{number}")));
    }

    /// <summary>Writes <paramref name="version"/>'s record beside its files; promoted, it becomes the current one.</summary>
    public void Save(AdapterVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Directory.CreateDirectory(version.Folder);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", version.Number);
            writer.WriteString("time", version.Time.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("baseModel", version.BaseModel);
            writer.WriteNumber("episodes", version.Episodes);
            writer.WriteNumber("pairs", version.Pairs);
            writer.WriteNumber("heldOut", version.HeldOut);
            writer.WriteNumber("steps", version.Steps);
            writer.WriteNumber("firstLoss", Finite(version.FirstLoss));
            writer.WriteNumber("lastLoss", Finite(version.LastLoss));
            writer.WriteNumber("scoreBefore", Finite(version.ScoreBefore));
            writer.WriteNumber("scoreAfter", Finite(version.ScoreAfter));
            writer.WriteBoolean("promoted", version.Promoted);
            writer.WriteString("note", version.Note);
            writer.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine(version.Folder, "version.json"), stream.ToArray());
        if (version.Promoted)
        {
            Promote(version.Number);
        }
    }

    /// <summary>Makes version <paramref name="number"/> the current one.</summary>
    public void Promote(int number)
    {
        Directory.CreateDirectory(_folder);
        File.AppendAllText(Path.Combine(_folder, "promotions.txt"), number.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    /// <summary>Goes back to the adapter promoted before the current one (none: the base model); the version now current, or null.</summary>
    public AdapterVersion? Rollback()
    {
        var promotions = Promotions.ToList();
        if (promotions.Count == 0)
        {
            return null;
        }

        promotions.RemoveAt(promotions.Count - 1);
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "promotions.txt"), promotions.Select(n => n.ToString(CultureInfo.InvariantCulture)));
        return Current;
    }

    /// <summary>Deletes version <paramref name="number"/> (taken out of the promotions too); the bytes freed.</summary>
    public long Delete(int number)
    {
        var version = All.FirstOrDefault(v => v.Number == number);
        if (version is null)
        {
            return 0;
        }

        long bytes = FolderBytes(version.Folder);
        Directory.Delete(version.Folder, recursive: true);
        var promotions = Promotions.Where(n => n != number).ToList();
        File.WriteAllLines(Path.Combine(_folder, "promotions.txt"), promotions.Select(n => n.ToString(CultureInfo.InvariantCulture)));
        return bytes;
    }

    /// <summary>The bytes of the files under <paramref name="folder"/>.</summary>
    public static long FolderBytes(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static AdapterVersion? Read(string folder)
    {
        string path = Path.Combine(folder, "version.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            return new AdapterVersion(root.GetProperty("version").GetInt32(), folder,
                DateTimeOffset.Parse(root.GetProperty("time").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                root.GetProperty("baseModel").GetString() ?? "", root.GetProperty("episodes").GetInt32(), root.GetProperty("pairs").GetInt32(),
                root.GetProperty("heldOut").GetInt32(), root.GetProperty("steps").GetInt32(), root.GetProperty("firstLoss").GetSingle(),
                root.GetProperty("lastLoss").GetSingle(), root.GetProperty("scoreBefore").GetDouble(), root.GetProperty("scoreAfter").GetDouble(),
                root.GetProperty("promoted").GetBoolean(), root.GetProperty("note").GetString() ?? "");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IOException)
        {
            return null;
        }
    }
}

/// <summary>How far a training is: its stage, its step of the steps, the last loss, the time, the device and its memory.</summary>
/// <param name="Stage">What it does ("loading the model", "training", "measuring on the held-out set").</param>
/// <param name="Step">The steps done.</param>
/// <param name="Steps">The steps in all.</param>
/// <param name="Loss">The last step's loss (NaN before the first).</param>
/// <param name="Elapsed">The time so far.</param>
/// <param name="Left">The time left, when it can be told.</param>
/// <param name="Device">The device it runs on.</param>
/// <param name="DeviceBytes">The device memory held.</param>
/// <param name="ManagedBytes">The managed memory held.</param>
public sealed record TrainingProgress(string Stage, int Step, int Steps, float Loss, TimeSpan Elapsed, TimeSpan? Left, string Device, long DeviceBytes, long ManagedBytes)
{
    /// <summary>One line for the status bar ("Training 12/40 · loss 0.52 · cuda:0 · 0:41 left").</summary>
    public string Line => Step == 0 || Steps == 0
        ? $"Learning: {Stage} · {Device}"
        : string.Create(CultureInfo.InvariantCulture,
            $"Learning: {Stage} {Step}/{Steps}{(float.IsFinite(Loss) ? $" · loss {Loss:F3}" : "")} · {Device}{(Left is { } left ? $" · {left:m\\:ss} left" : "")}");
}

/// <summary>What a training did.</summary>
/// <param name="Version">The adapter it wrote, or null when it wrote none (nothing to learn yet, stopped).</param>
/// <param name="Message">What happened, for the user.</param>
/// <param name="Losses">Each step's loss.</param>
/// <param name="DeviceMemory">The device memory held after each step.</param>
/// <param name="ManagedMemory">The managed memory after each step.</param>
public sealed record TrainingResult(AdapterVersion? Version, string Message, IReadOnlyList<float> Losses, IReadOnlyList<long> DeviceMemory, IReadOnlyList<long> ManagedMemory)
{
    /// <summary>Whether its adapter was promoted.</summary>
    public bool Promoted => Version?.Promoted == true;
}

/// <summary>
/// Learning on Idrak (plan 1, phase R): recorded editor episodes as Idrak trajectories (the prompt's ids as the model reads
/// them, the answer's, the verdict and the kept text in <see cref="PreferenceInfoKeys"/>, the editor-verdict reward),
/// preference rounds over them (pairs by <see cref="PreferencePairSources"/>, the loss by <c>PreferenceLosses</c>, through
/// Idrak.Text's <see cref="LanguageModelPolicy.UpdatePreferences"/>), the workspace's tasks through Idrak's
/// <see cref="ReinforcementTrainer"/> on the "csharp" environment, and the held-out measure that decides a promotion. The
/// model is trained as an adapter (LoRA), never its base weights. Stoppable at every step.
/// </summary>
public static class LearningTrainer
{
    /// <summary>The C# coding environment (Idrak.Reinforcement.Plugins).</summary>
    public const string TaskEnvironment = "csharp";

    /// <summary>Whether <paramref name="id"/> is held out (a fixed share of ids, by a hash of the id: the same every run).</summary>
    public static bool IsHeldOut(string id, double share)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (share <= 0)
        {
            return false;
        }

        uint hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0);
        return hash < share * uint.MaxValue;
    }

    /// <summary>The model <paramref name="name"/> (Idrak's model sources) on the setting's device, under the setting's limits (Idrak's ComputeResources).</summary>
    public static PretrainedModel LoadModel(LearningSettings settings, string name)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (settings.Threads > 0)
        {
            ComputeResources.MaxCpuThreads = settings.Threads;
        }

        if (settings.CpuMemoryMegabytes > 0)
        {
            ComputeResources.CpuMemoryLimit = settings.CpuMemoryMegabytes * 1024 * 1024;
        }

        if (settings.GpuMemoryMegabytes > 0)
        {
            ComputeResources.GpuMemoryLimit = settings.GpuMemoryMegabytes * 1024 * 1024;
        }

        string folder = ModelSource.Resolve(name);
        var device = settings.Device.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Device.Default : Device.Parse(settings.Device);
        return PretrainedModel.Load(folder, new PretrainedOptions { Device = device });
    }

    /// <summary>The editor-verdict reward of each episode (the setting's weights).</summary>
    public static float[] Rewards(IReadOnlyList<EditorEpisode> episodes, LearningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(settings);
        var inputs = episodes.Select(e => new RewardInput(e.Prompt, e.Answer, null, Info(e))).ToList();
        return RewardFunctions.Get(EditorVerdictReward.RewardName).ScoreAsync(inputs, settings.VerdictWeights.Count == 0 ? null : settings.VerdictWeights)
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>The episode's info: the verdict, the kept text at step 0 (when there is one) and how far it was edited.</summary>
    public static EpisodeInfo Info(EditorEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var info = new EpisodeInfo().Set(PreferenceInfoKeys.Verdict, episode.Verdict).Set(EditorVerdictReward.EditedKey, episode.Edited);
        if (episode.Kept is { Length: > 0 } kept)
        {
            info.Set(PreferenceInfoKeys.Kept, kept).Set(PreferenceInfoKeys.KeptStep, 0);
        }

        return info;
    }

    /// <summary>
    /// The episodes as one-step trajectories over <paramref name="model"/>'s tokenizer: a completion's prompt read as the raw
    /// text it was (the model saw no chat turn), any other's as one user message in the model's chat template; the answer's
    /// ids with the template's end of an assistant turn (so a kept text, which Idrak's pairs end the same way, compares
    /// like for like); the rewards given; the episode info of <see cref="Info"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model has no chat template or tokenizer.</exception>
    public static List<Trajectory<ChatObservation, ChatMessage>> Trajectories(IReadOnlyList<EditorEpisode> episodes, PretrainedModel model, IReadOnlyList<float> rewards)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rewards);
        var encoder = Encoder(model);
        var trajectories = new List<Trajectory<ChatObservation, ChatMessage>>(episodes.Count);
        for (int i = 0; i < episodes.Count; i++)
        {
            var episode = episodes[i];
            IReadOnlyList<ChatMessage> prompt = [new ChatMessage("user", episode.Prompt)];
            string rendered = episode.Kind == AiAnswerKinds.Completion ? episode.Prompt : encoder.Template.Render(prompt, [], null, addGenerationPrompt: true);
            int[] context = [.. encoder.Tokenizer.Encode(rendered)];
            int[] reply = [.. encoder.Tokenizer.Encode(episode.Answer + encoder.AssistantEnd)];
            if (context.Length == 0 || reply.Length == 0)
            {
                continue;
            }

            var observation = new ChatObservation(prompt);
            var record = new ActionRecord(reply, new float[reply.Length], context: context);
            trajectories.Add(new Trajectory<ChatObservation, ChatMessage>(trajectories.Count, 0, [observation, observation], [new ChatMessage("assistant", episode.Answer)], [record],
                [rewards[i]], terminated: true, truncated: false, info: Info(episode)));
        }

        return trajectories;
    }

    /// <summary>The model's transcript encoder (its chat template and tokenizer).</summary>
    /// <exception cref="InvalidOperationException">The model has no chat template or tokenizer.</exception>
    public static ChatTranscriptEncoder Encoder(PretrainedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var template = model.JinjaTemplate ?? throw new InvalidOperationException("The model has no chat template: the learning plug-in trains a chat model's adapter.");
        var tokenizer = model.Tokenizer ?? throw new InvalidOperationException("The model has no tokenizer.");
        return new ChatTranscriptEncoder(template, tokenizer);
    }

    /// <summary>The mean log p(chosen) − log p(rejected) of <paramref name="pairs"/> under the session's model, with its adapters or without them (the base).</summary>
    public static double Margin(FineTuningSession session, IReadOnlyList<PreferencePair> pairs, bool adapters)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
        {
            return double.NaN;
        }

        var sequences = new List<TrainingSequence>(pairs.Count * 2);
        foreach (var pair in pairs)
        {
            sequences.Add(pair.Chosen);
            sequences.Add(pair.Rejected);
        }

        var values = session.LogProbabilities(sequences, adapters);
        double sum = 0;
        for (int p = 0; p < pairs.Count; p++)
        {
            sum += Sum(values[2 * p]) - Sum(values[(2 * p) + 1]);
        }

        return sum / pairs.Count;
    }

    /// <summary>
    /// Trains an adapter of <paramref name="model"/> (loaded with the current adapter, when one is promoted, so training goes
    /// on from it) on <paramref name="episodes"/>' preferences: the held-out share set aside, the rest's pairs chosen by the
    /// setting's source, <see cref="LearningSettings.Steps"/> updates with the setting's loss; then measured on the
    /// held-out pairs against the current adapter (or the base model) and promoted only when it beats it by the margin.
    /// Every version is kept. Stops at the next step when <paramref name="cancellationToken"/> is cancelled (nothing written).
    /// </summary>
    public static TrainingResult TrainPreferences(PretrainedModel model, string baseModel, IReadOnlyList<EditorEpisode> episodes, AdapterVersions versions,
        LearningSettings settings, IProgress<TrainingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(settings);
        var watch = Stopwatch.StartNew();
        string device = model.Device.ToString();
        var losses = new List<float>();
        var deviceMemory = new List<long>();
        var managedMemory = new List<long>();
        var current = versions.Current is { } promoted && promoted.BaseModel == baseModel ? promoted : null;
        if (current is not null)
        {
            model.LoadAdapter(current.Folder);                             // go on from the current adapter
        }

        var encoder = Encoder(model);
        var rewards = Rewards(episodes, settings);
        var all = Trajectories(episodes, model, rewards);
        var train = new List<Trajectory<ChatObservation, ChatMessage>>();
        var held = new List<Trajectory<ChatObservation, ChatMessage>>();
        for (int i = 0; i < all.Count; i++)
        {
            (IsHeldOut(episodes[i].Id, settings.HeldOutShare) ? held : train).Add(all[i]);
        }

        using var batch = new RolloutBatch<ChatObservation, ChatMessage>(Renumber(train));
        var choices = new List<PreferenceChoice>();
        PreferencePairSources.Get(settings.Pairs).Choose(batch.Trajectories, choices);
        if (choices.Count == 0)
        {
            return new TrainingResult(null, string.Create(CultureInfo.InvariantCulture,
                $"Nothing to learn yet: {episodes.Count} episodes, none of the {train.Count} trained on is a choice ({settings.Pairs}: a rejected, edited or undone answer with the text kept instead)."),
                losses, deviceMemory, managedMemory);
        }

        var heldPairs = held.Count == 0 ? [] : PreferencePairs.Build(Renumber(held), settings.Pairs, encoder);
        var options = new FineTuningOptions
        {
            Rank = settings.Rank, Alpha = 2f * settings.Rank, LearningRate = settings.LearningRate, BatchTokens = settings.BatchTokens, Seed = 7,
        };
        using var policy = new LanguageModelPolicy(new FineTuningSession(model, options, totalSteps: settings.Steps), new LanguageModelPolicyOptions { MaxNewTokens = 64 });
        var session = policy.Session;
        double before = heldPairs.Count == 0 ? double.NaN : Margin(session, heldPairs, adapters: current is not null);
        progress?.Report(new TrainingProgress("training", 0, settings.Steps, float.NaN, watch.Elapsed, null, device, 0, GC.GetTotalMemory(false)));
        var lossOptions = settings.LossOptions.Count == 0 ? null : settings.LossOptions;
        for (int step = 1; step <= settings.Steps; step++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new TrainingResult(null, string.Create(CultureInfo.InvariantCulture, $"Stopped at step {step - 1} of {settings.Steps}: no adapter written, the current one stays."),
                    losses, deviceMemory, managedMemory);
            }

            var stats = policy.UpdatePreferences(batch, choices, settings.Loss, lossOptions, settings.Reference);
            losses.Add(stats.Loss);
            long held1 = model.Device.Backend.GetMemoryUsage().InUse;
            deviceMemory.Add(held1);
            managedMemory.Add(GC.GetTotalMemory(forceFullCollection: true));   // a step takes far longer than a collection
            var left = TimeSpan.FromTicks(watch.Elapsed.Ticks / step * (settings.Steps - step));
            progress?.Report(new TrainingProgress("training", step, settings.Steps, stats.Loss, watch.Elapsed, left, device, held1, managedMemory[^1]));
        }

        progress?.Report(new TrainingProgress("measuring on the held-out set", settings.Steps, settings.Steps, losses[^1], watch.Elapsed, null, device, deviceMemory[^1], managedMemory[^1]));
        double after = heldPairs.Count == 0 ? double.NaN : Margin(session, heldPairs, adapters: true);
        var (number, folder) = versions.Next();
        model.SaveAdapter(folder);
        bool promote = heldPairs.Count > 0 && after > before + settings.PromotionMargin;
        string note = heldPairs.Count == 0
            ? "not promoted: no held-out pair to measure it on yet (more episodes with a kept text are needed)"
            : promote
                ? string.Create(CultureInfo.InvariantCulture, $"promoted: held-out margin {after:F3} against {before:F3} for {(current is null ? "the base model" : $"v{current.Number}")}")
                : string.Create(CultureInfo.InvariantCulture, $"not promoted: held-out margin {after:F3} is not above {before:F3} (+{settings.PromotionMargin:F3}) for {(current is null ? "the base model" : $"v{current.Number}")}");
        var version = new AdapterVersion(number, folder, DateTimeOffset.UtcNow, baseModel, train.Count, choices.Count, heldPairs.Count, settings.Steps, losses[0], losses[^1],
            before, after, promote, note);
        versions.Save(version);
        return new TrainingResult(version, string.Create(CultureInfo.InvariantCulture,
            $"v{number}: {choices.Count} pairs from {train.Count} episodes, {settings.Steps} {settings.Loss} steps on {device} (loss {losses[0]:F3} to {losses[^1]:F3}), {note}."),
            losses, deviceMemory, managedMemory);
    }

    /// <summary>The C# environment's options for the tasks of <paramref name="suite"/> (the setting's sandbox and reward), with <paramref name="extra"/> over them.</summary>
    public static Dictionary<string, string> TaskOptions(string suite, LearningSettings settings, IReadOnlyDictionary<string, string>? extra = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suite);
        ArgumentNullException.ThrowIfNull(settings);
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["suite"] = suite, ["sandbox"] = settings.Sandbox, ["reward"] = settings.TaskReward,
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            options[key] = value;
        }

        return options;
    }

    /// <summary>
    /// Plays <paramref name="episodes"/> episodes of the tasks of <paramref name="suite"/> in Idrak's "csharp" environment
    /// with <paramref name="policy"/> (greedy) and gives their trajectories, each with its reward (the task reward of the
    /// settings: csharp-tests and csharp-build by default) and the environment's info.
    /// </summary>
    /// <exception cref="NotRegisteredException">The "csharp" environment is not registered (Idrak.Reinforcement.Plugins).</exception>
    public static async Task<IReadOnlyList<Trajectory<ChatObservation, ChatMessage>>> PlayTasksAsync(IPolicy<ChatObservation, ChatMessage> policy, string suite,
        LearningSettings settings, int episodes, IReadOnlyDictionary<string, string>? extra = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(episodes);
        var kind = EnvironmentKinds.Get(TaskEnvironment);
        using var collector = new RolloutCollector<ChatObservation, ChatMessage>(kind, TaskOptions(suite, settings, extra), seed: 0, environments: 1);
        var starts = Enumerable.Range(0, episodes).Select(i => new EpisodeStart(i, i, i)).ToList();
        using var batch = await collector.CollectAsync(policy, starts, new ActionSampling { Greedy = true }, 0, cancellationToken).ConfigureAwait(false);
        return [.. batch.Trajectories];
    }

    /// <summary>
    /// Trains an adapter of <paramref name="model"/> on the tasks of <paramref name="suite"/> with Idrak's
    /// <see cref="ReinforcementTrainer"/> (the setting's algorithm and estimator; with "both", the iterations' groups also
    /// make preference rounds, the "group" pairs), measured before and after on <paramref name="heldOutSuite"/> (its mean
    /// return), promoted only when better by the margin.
    /// </summary>
    public static TrainingResult TrainTasks(PretrainedModel model, string baseModel, string suite, string? heldOutSuite, AdapterVersions versions, LearningSettings settings,
        IProgress<TrainingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(settings);
        var watch = Stopwatch.StartNew();
        string device = model.Device.ToString();
        var current = versions.Current is { } promoted && promoted.BaseModel == baseModel ? promoted : null;
        if (current is not null)
        {
            model.LoadAdapter(current.Folder);
        }

        var options = new FineTuningOptions { Rank = settings.Rank, Alpha = 2f * settings.Rank, LearningRate = settings.LearningRate, BatchTokens = settings.BatchTokens, Seed = 7 };
        using var policy = new LanguageModelPolicy(new FineTuningSession(model, options, totalSteps: Math.Max(1, settings.Steps * 2)), new LanguageModelPolicyOptions { MaxNewTokens = 512 });
        double Score() => heldOutSuite is null ? double.NaN
            : PlayTasksAsync(policy, heldOutSuite, settings, 2, null, cancellationToken).GetAwaiter().GetResult().Average(t => (double)t.Return);
        double before = Score();
        using var reporter = new StepReporter(step => progress?.Report(new TrainingProgress("training on the tasks", (int)step, settings.Steps, float.NaN, watch.Elapsed,
            step > 0 ? TimeSpan.FromTicks(watch.Elapsed.Ticks / step * Math.Max(0, settings.Steps - step)) : null, device, model.Device.Backend.GetMemoryUsage().InUse,
            GC.GetTotalMemory(false))));
        var run = ReinforcementTrainer.Train(TaskEnvironment, policy, new ReinforcementOptions
        {
            Algorithm = settings.Algorithm,
            Estimator = settings.Estimator.Length == 0 ? null : settings.Estimator,
            Iterations = settings.Steps,
            GroupSize = 4,
            EpisodesPerIteration = 4,
            Environments = 1,
            Pairs = settings.Trainer == "both" ? PreferencePairSources.Group : null,
            PreferenceLoss = settings.Loss,
            EnvironmentOptions = TaskOptions(suite, settings),
        }, reporter, cancellationToken);
        var losses = run.History.Select(h => (float)h.Loss).ToList();
        if (run.Cancelled || losses.Count == 0)
        {
            return new TrainingResult(null, string.Create(CultureInfo.InvariantCulture, $"Stopped after {run.Iterations} of {settings.Steps} iterations: no adapter written."), losses, [], []);
        }

        double after = Score();
        var (number, folder) = versions.Next();
        model.SaveAdapter(folder);
        bool promote = heldOutSuite is not null && after > before + settings.PromotionMargin;
        string note = heldOutSuite is null ? "not promoted: no held-out task to measure it on"
            : promote ? string.Create(CultureInfo.InvariantCulture, $"promoted: held-out mean reward {after:F3} against {before:F3}")
            : string.Create(CultureInfo.InvariantCulture, $"not promoted: held-out mean reward {after:F3} is not above {before:F3} (+{settings.PromotionMargin:F3})");
        var version = new AdapterVersion(number, folder, DateTimeOffset.UtcNow, baseModel, run.History.Sum(h => h.Episodes), 0, heldOutSuite is null ? 0 : 2, run.Iterations,
            losses[0], losses[^1], before, after, promote, note);
        versions.Save(version);
        return new TrainingResult(version, $"v{number}: {run.Iterations} iterations of {settings.Algorithm} on the tasks on {device}, {note}.", losses, [], []);
    }

    // Trajectories numbered 0, 1, … in list order (what a batch and a pair source count by).
    private static List<Trajectory<ChatObservation, ChatMessage>> Renumber(List<Trajectory<ChatObservation, ChatMessage>> trajectories)
    {
        var numbered = new List<Trajectory<ChatObservation, ChatMessage>>(trajectories.Count);
        foreach (var t in trajectories)
        {
            int i = numbered.Count;
            numbered.Add(new Trajectory<ChatObservation, ChatMessage>(i, t.Seed, t.Observations, t.Actions, t.Records, t.Rewards.ToArray(), t.Terminated, t.Truncated, i, t.FinalValue, t.Info));
        }

        return numbered;
    }

    private static double Sum(float[] values)
    {
        double sum = 0;
        foreach (float value in values)
        {
            sum += value;
        }

        return sum;
    }

    // The trainer's progress (iterations done) as the plug-in's.
    private sealed class StepReporter(Action<long> report) : IProgressReporter
    {
        private long _done;

        public string Label { get; set; } = "";

        public long? Total { get; set; }

        public ProgressUnit Unit { get; set; }

        public long Done => _done;

        public bool Enabled => true;

        public Func<string>? Detail { get; set; }

        public void Advance(long amount = 1) => report(Interlocked.Add(ref _done, amount));

        public void Report(long value)
        {
            Interlocked.Exchange(ref _done, value);
            report(value);
        }

        public void Show()
        {
        }

        public void NextStage(string label, long? total, ProgressUnit unit, string? message = null, bool keepLine = true)
        {
            Label = label;
            Total = total;
            Unit = unit;
            Interlocked.Exchange(ref _done, 0);
        }

        public void Erase()
        {
        }

        public void Clear()
        {
        }

        public void Finish(string? message = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
