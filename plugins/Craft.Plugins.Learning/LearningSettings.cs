// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Learning;

/// <summary>
/// The learning plug-in's settings (plug-in "learning", variables <c>CRAFT_PLUGIN_LEARNING_&lt;KEY&gt;</c>, on the settings
/// page). The two consents are never set here: they are the user's answers, stored with their time, scope and text
/// version (<see cref="ConsentStore"/>); a setting can only turn them off (<see cref="ConsentOff"/>, <see cref="SharingOff"/>).
/// </summary>
public sealed record LearningSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "learning";

    /// <summary>Learning and sharing turned off by the setting <c>consent</c> ("off"); "ask" (the default) leaves it to the user's answer.</summary>
    public bool ConsentOff { get; init; }

    /// <summary>Sharing turned off by the setting <c>sharing</c> ("off"); "ask" leaves it to the user's answer.</summary>
    public bool SharingOff { get; init; }

    /// <summary>What is recorded (<c>record</c>): the answer kinds of <see cref="AiAnswerKinds"/>.</summary>
    public IReadOnlyList<string> Record { get; init; } = [AiAnswerKinds.Completion, AiAnswerKinds.Chat, AiAnswerKinds.CodeAction, AiAnswerKinds.Agent];

    /// <summary>Where the episodes, tasks, adapters and consents are kept (<c>folder</c>); empty: the app's data folder's "learning".</summary>
    public string Folder { get; init; } = "";

    /// <summary>The most a workspace's episodes take on disk, in MiB (<c>capMb</c>); the oldest go first.</summary>
    public int CapMegabytes { get; init; } = 64;

    /// <summary>Days an episode is kept (<c>keepDays</c>).</summary>
    public int KeepDays { get; init; } = 90;

    /// <summary>Seconds after the last edit of an answer before its verdict is settled and its episode written (<c>settleSeconds</c>).</summary>
    public int SettleSeconds { get; init; } = 30;

    /// <summary>The editor-verdict reward's options (<c>verdictWeights</c>: "accepted=1,edited=0.5,rejected=-1,undone=-1").</summary>
    public IReadOnlyDictionary<string, string> VerdictWeights { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The reward of the workspace's tasks (<c>taskReward</c>): a coding reward expression over RewardFunctions' names.</summary>
    public string TaskReward { get; init; } = "csharp-tests:1,csharp-build:0.5";

    /// <summary>The command sandbox the tasks' builds and tests run in (<c>sandbox</c>, Idrak's CommandSandboxes): each command approved by default.</summary>
    public string Sandbox { get; init; } = "approve-each";

    /// <summary>When training runs (<c>schedule</c>): "idle" (after <see cref="IdleMinutes"/> without typing) or "demand" (only the command).</summary>
    public string Schedule { get; init; } = "idle";

    /// <summary>Minutes without typing or an AI answer before an idle training starts (<c>idleMinutes</c>).</summary>
    public int IdleMinutes { get; init; } = 10;

    /// <summary>New episodes since the last training before an idle training starts (<c>minEpisodes</c>).</summary>
    public int MinEpisodes { get; init; } = 20;

    /// <summary>What training does (<c>trainer</c>): "preference" (the recorded verdicts), "tasks" (the workspace's tests), "both".</summary>
    public string Trainer { get; init; } = "preference";

    /// <summary>The model trained (<c>model</c>, a name Idrak's model sources resolve); empty: the AI plug-in's model.</summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// The device training runs on (<c>device</c>: auto, cpu, cuda:0, vulkan:0); auto takes the AI plug-in's copy of the
    /// model where it is (one copy shared, note 22), else the library's choice.
    /// </summary>
    public string Device { get; init; } = "auto";

    /// <summary>
    /// The weights the model is trained over (<c>weights</c>: int8, int4, bf16, float32); empty: the AI plug-in's, so the
    /// copy it has loaded is the one trained (B7: the trainer loaded a float32 second copy beside it).
    /// </summary>
    public string Weights { get; init; } = "";

    /// <summary>
    /// The Idrak plug-in packages registered before the model loads (<c>idrakPlugins</c>, the model families); empty: the AI
    /// plug-in's <c>idrakPlugins</c>, or Idrak.Text.Plugins without it (B4).
    /// </summary>
    public IReadOnlyList<string> IdrakPlugins { get; init; } = [];

    /// <summary>CPU threads (<c>threads</c>, Idrak's ComputeResources; 0: all cores).</summary>
    public int Threads { get; init; }

    /// <summary>CPU memory cap in MiB (<c>cpuMemoryMb</c>; 0: none).</summary>
    public long CpuMemoryMegabytes { get; init; }

    /// <summary>Device memory cap in MiB (<c>gpuMemoryMb</c>; 0: none).</summary>
    public long GpuMemoryMegabytes { get; init; }

    /// <summary>The preference-pair source (<c>pairs</c>, Idrak's PreferencePairSources).</summary>
    public string Pairs { get; init; } = "verdict";

    /// <summary>The preference loss (<c>loss</c>, Idrak's PreferenceLosses).</summary>
    public string Loss { get; init; } = "dpo";

    /// <summary>The loss's options (<c>lossOptions</c>: "beta=0.1").</summary>
    public IReadOnlyDictionary<string, string> LossOptions { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>What a preference update trains against (<c>reference</c>: previous, base).</summary>
    public string Reference { get; init; } = "previous";

    /// <summary>The reinforcement algorithm on the tasks (<c>algorithm</c>, Idrak's PolicyLosses).</summary>
    public string Algorithm { get; init; } = "grpo";

    /// <summary>The advantage estimator on the tasks (<c>estimator</c>, Idrak's AdvantageEstimators); empty: the algorithm's own.</summary>
    public string Estimator { get; init; } = "";

    /// <summary>Optimizer steps of one preference training (<c>steps</c>), or iterations on the tasks.</summary>
    public int Steps { get; init; } = 40;

    /// <summary>The adapter's rank (<c>rank</c>).</summary>
    public int Rank { get; init; } = 8;

    /// <summary>The learning rate (<c>learningRate</c>).</summary>
    public float LearningRate { get; init; } = 1e-4f;

    /// <summary>
    /// Tokens a step's batch holds (<c>batchTokens</c>); 0 (auto, the default): the most of <see cref="BatchTokensMax"/>,
    /// halved, that the device's free memory fits, measured when the training starts (card-agnostic).
    /// </summary>
    public int BatchTokens { get; init; }

    /// <summary>The batch an automatic <see cref="BatchTokens"/> starts from before it is fitted to the free memory (<c>batchTokensMax</c>).</summary>
    public int BatchTokensMax { get; init; } = 4096;

    /// <summary>The longest sequence trained, in tokens (<c>maxLength</c>); 0 (auto): the longest pair's, at most the model's positions.</summary>
    public int MaxLength { get; init; }

    /// <summary>Whether a step that runs out of memory halves the batch and tries again before the training fails (<c>stepDown</c>).</summary>
    public bool StepDown { get; init; } = true;

    /// <summary>The share of episodes and tasks held out to measure an adapter on (<c>heldOutShare</c>).</summary>
    public double HeldOutShare { get; init; } = 0.2;

    /// <summary>How much better on the held-out set a new adapter must score to replace the current one (<c>promotionMargin</c>).</summary>
    public double PromotionMargin { get; init; }

    /// <summary>Where a share goes (<c>shareTo</c>: "folder:PATH", "https://…", or a destination a plug-in registers as "kind:target"); no default: nothing is shared until named.</summary>
    public string ShareTo { get; init; } = "";

    /// <summary>What a share sends (<c>shareWhat</c>): "scores", "episodes", "adapter", separated by commas.</summary>
    public IReadOnlyList<string> ShareWhat { get; init; } = [ShareKinds.Scores];

    /// <summary>More secret patterns the scanner leaves out (<c>secretPatterns</c>: regular expressions separated by ";;").</summary>
    public IReadOnlyList<string> SecretPatterns { get; init; } = [];

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("consent", "ask", "Learning and sharing: ask (your answers decide) or off (nothing recorded, nothing shared). A setting cannot say yes for you."),
        new("sharing", "ask", "Sharing alone: ask (your answer decides, after the preview) or off."),
        new("record", "completion,chat,code-action,agent", "What is recorded once you agree: completion, chat, code-action, agent, separated by commas."),
        new("folder", "", "Where episodes, tasks, adapters and your consents are kept (empty: the app's data folder, learning)."),
        new("capMb", "64", "The most one workspace's episodes take on disk, MiB (the oldest go first)."),
        new("keepDays", "90", "Days an episode is kept."),
        new("settleSeconds", "30", "Seconds after your last edit of an answer before its verdict is settled and the episode written."),
        new("verdictWeights", "accepted=1,edited=0.5,rejected=-1,undone=-1", "The editor-verdict reward: a score per verdict (edited scales down by how far you edited)."),
        new("taskReward", "csharp-tests:1,csharp-build:0.5", "The reward of the workspace's test tasks: reward names with weights."),
        new("sandbox", "approve-each", "The command sandbox the tasks' builds and tests run in (approve-each asks you for every command; process, container)."),
        new("schedule", "idle", "When training runs: idle (after idleMinutes without typing) or demand (only when you run it)."),
        new("idleMinutes", "10", "Minutes without typing before an idle training starts."),
        new("minEpisodes", "20", "New episodes since the last training before an idle training starts."),
        new("trainer", "preference", "What training does: preference (your verdicts), tasks (the workspace's failing tests), both."),
        new("model", "", "The model trained as an adapter (empty: the AI plug-in's model). Its base weights never change."),
        new("device", "auto", "The device training runs on: auto (the AI plug-in's copy where it is), cpu, cuda:0, vulkan:0."),
        new("weights", "", "The weights trained over: int8, int4, bf16, float32 (empty: the AI plug-in's, so its loaded copy is trained, never a second)."),
        new("idrakPlugins", "", "Idrak plug-in packages (the model families) registered before the model loads (empty: the AI plug-in's idrakPlugins)."),
        new("threads", "0", "CPU threads for training (0: all cores)."),
        new("cpuMemoryMb", "0", "CPU memory cap for training, MiB (0: none)."),
        new("gpuMemoryMb", "0", "Device memory cap for training, MiB (0: none)."),
        new("pairs", "verdict", "How episodes become preference pairs (Idrak's PreferencePairSources: verdict, group)."),
        new("loss", "dpo", "The preference loss (Idrak's PreferenceLosses: dpo, orpo, simpo)."),
        new("lossOptions", "", "The loss's options, key=value separated by commas (beta=0.1)."),
        new("reference", "previous", "What a preference update trains against: previous (the adapter as the run began) or base."),
        new("algorithm", "grpo", "The reinforcement algorithm on the tasks (Idrak's PolicyLosses)."),
        new("estimator", "", "The advantage estimator on the tasks (Idrak's AdvantageEstimators; empty: the algorithm's)."),
        new("steps", "40", "Optimizer steps of one training (iterations on the tasks)."),
        new("rank", "8", "The adapter's rank."),
        new("learningRate", "0.0001", "The learning rate."),
        new("batchTokens", "auto", "Tokens one step's batch holds: auto (fitted to the device's free memory when training starts) or a number."),
        new("batchTokensMax", "4096", "The batch an automatic batchTokens starts from before it is fitted to the free memory."),
        new("maxLength", "auto", "The longest sequence trained, in tokens: auto (the longest pair's, at most the model's positions) or a number."),
        new("stepDown", "on", "On a step that runs out of memory, halve the batch and try again before failing: on or off."),
        new("heldOutShare", "0.2", "The share of episodes and tasks held out to measure an adapter on."),
        new("promotionMargin", "0", "How much better (held-out margin) a new adapter must score to replace the current one."),
        new("shareTo", "", "Where a share goes: folder:PATH, https://ADDRESS, or kind:target. No default: nothing is shared until you name one."),
        new("shareWhat", "scores", "What a share sends: scores, episodes, adapter, separated by commas."),
        new("secretPatterns", "", "More secret patterns the scanner leaves out of a share: regular expressions separated by ;;."),
    ];

    /// <summary>The settings from the host's.</summary>
    /// <exception cref="ArgumentException">A value that does not parse, naming its variable.</exception>
    public static LearningSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return From(key => settings.Get(PluginName, key));
    }

    /// <summary>The settings from <paramref name="read"/> (a key's value, "" when not set).</summary>
    /// <exception cref="ArgumentException">A value that does not parse, naming its variable.</exception>
    public static LearningSettings From(Func<string, string> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var defaults = new LearningSettings();
        string Text(string key) => (read(key) ?? "").Trim();
        string Variable(string key) => PluginSetting.VariableName(PluginName, key);
        ArgumentException Bad(string key, string text, string takes) => new($"{Variable(key)} is '{text}': it takes {takes}.", key);
        int Whole(string key, int fallback, int min, int max)
        {
            string text = Text(key);
            return text.Length == 0 ? fallback
                : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max ? value
                : throw Bad(key, text, string.Create(CultureInfo.InvariantCulture, $"a whole number from {min} to {max}"));
        }

        double Number(string key, double fallback, double min, double max)
        {
            string text = Text(key);
            return text.Length == 0 ? fallback
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= min && value <= max ? value
                : throw Bad(key, text, string.Create(CultureInfo.InvariantCulture, $"a number from {min} to {max}"));
        }

        bool Off(string key)
        {
            string text = Text(key);
            if (text.Length == 0 || text.Equals("ask", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (text.Equals("off", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0")
            {
                return true;
            }

            throw Bad(key, text, "ask or off: a setting can only turn it off; saying yes is your answer to the question, never a variable's");
        }

        string Choice(string key, string fallback, params string[] allowed)
        {
            string text = Text(key);
            if (text.Length == 0)
            {
                return fallback;
            }

            foreach (string one in allowed)
            {
                if (one.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return one;
                }
            }

            throw Bad(key, text, string.Join(", ", allowed));
        }

        IReadOnlyList<string> Kinds(string key, IReadOnlyList<string> fallback, IReadOnlyList<string> allowed)
        {
            string text = Text(key);
            if (text.Length == 0)
            {
                return fallback;
            }

            var kinds = new List<string>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? known = allowed.FirstOrDefault(a => a.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    throw Bad(key, text, string.Join(", ", allowed) + ", separated by commas");
                }

                if (!kinds.Contains(known))
                {
                    kinds.Add(known);
                }
            }

            return kinds;
        }

        // "auto" (or empty, or 0) as 0, else a whole number in its range.
        int Auto(string key, int min, int max) =>
            Text(key) is { Length: > 0 } text && !text.Equals("auto", StringComparison.OrdinalIgnoreCase) && text != "0" ? Whole(key, 0, min, max) : 0;

        string Named(string key, string fallback)
        {
            string text = Text(key);
            return text.Length == 0 ? fallback : text;
        }

        return new LearningSettings
        {
            ConsentOff = Off("consent"),
            SharingOff = Off("sharing"),
            Record = Kinds("record", defaults.Record, [AiAnswerKinds.Completion, AiAnswerKinds.Chat, AiAnswerKinds.CodeAction, AiAnswerKinds.Agent]),
            Folder = Text("folder"),
            CapMegabytes = Whole("capMb", defaults.CapMegabytes, 1, 1_000_000),
            KeepDays = Whole("keepDays", defaults.KeepDays, 1, 100_000),
            SettleSeconds = Whole("settleSeconds", defaults.SettleSeconds, 0, 86_400),
            VerdictWeights = ParsePairs(Text("verdictWeights"), Variable("verdictWeights")),
            TaskReward = Named("taskReward", defaults.TaskReward),
            Sandbox = Named("sandbox", defaults.Sandbox),
            Schedule = Choice("schedule", defaults.Schedule, "idle", "demand"),
            IdleMinutes = Whole("idleMinutes", defaults.IdleMinutes, 1, 100_000),
            MinEpisodes = Whole("minEpisodes", defaults.MinEpisodes, 1, 10_000_000),
            Trainer = Choice("trainer", defaults.Trainer, "preference", "tasks", "both"),
            Model = Text("model"),
            Device = Named("device", defaults.Device),
            Weights = Choice("weights", "", "int8", "int4", "bf16", "bfloat16", "float32", "f32"),
            IdrakPlugins = Text("idrakPlugins").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Threads = Whole("threads", 0, 0, 4096),
            CpuMemoryMegabytes = Whole("cpuMemoryMb", 0, 0, int.MaxValue),
            GpuMemoryMegabytes = Whole("gpuMemoryMb", 0, 0, int.MaxValue),
            Pairs = Named("pairs", defaults.Pairs),
            Loss = Named("loss", defaults.Loss),
            LossOptions = ParsePairs(Text("lossOptions"), Variable("lossOptions")),
            Reference = Choice("reference", defaults.Reference, "previous", "base"),
            Algorithm = Named("algorithm", defaults.Algorithm),
            Estimator = Text("estimator"),
            Steps = Whole("steps", defaults.Steps, 1, 1_000_000),
            Rank = Whole("rank", defaults.Rank, 1, 1024),
            LearningRate = (float)Number("learningRate", defaults.LearningRate, 1e-9, 1),
            BatchTokens = Auto("batchTokens", 16, 1 << 24),
            BatchTokensMax = Whole("batchTokensMax", defaults.BatchTokensMax, 16, 1 << 24),
            MaxLength = Auto("maxLength", 16, 1 << 24),
            StepDown = Choice("stepDown", "on", "on", "off") == "on",
            HeldOutShare = Number("heldOutShare", defaults.HeldOutShare, 0, 0.9),
            PromotionMargin = Number("promotionMargin", defaults.PromotionMargin, -1e9, 1e9),
            ShareTo = Text("shareTo"),
            ShareWhat = Kinds("shareWhat", defaults.ShareWhat, ShareKinds.All),
            SecretPatterns = Text("secretPatterns") is { Length: > 0 } patterns ? patterns.Split(";;", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [],
        };
    }

    /// <summary>"key=value,key=value" as a dictionary.</summary>
    /// <exception cref="ArgumentException">A part without '=', naming the variable.</exception>
    public static Dictionary<string, string> ParsePairs(string text, string variable)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int at = part.IndexOf('=', StringComparison.Ordinal);
            if (at <= 0)
            {
                throw new ArgumentException($"{variable} is '{text}': it takes key=value pairs separated by commas.", nameof(text));
            }

            pairs[part[..at].Trim()] = part[(at + 1)..].Trim();
        }

        return pairs;
    }
}
