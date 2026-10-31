// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Idrak.Abstraction.Reinforcement;

namespace Craft.Plugins.Learning;

/// <summary>
/// "editor-verdict", the editor's reward (registered in Idrak's <see cref="RewardFunctions"/> by this plug-in): what the
/// user did with the answer, from the episode's info (<see cref="PreferenceInfoKeys.Verdict"/>): accepted and kept scores
/// <c>accepted</c> (1), edited scores <c>edited</c> (0.5) scaled down by how far it was edited
/// (<see cref="EditedKey"/>: 0 kept, 1 nothing left), rejected <c>rejected</c> (−1), undone <c>undone</c> (−1); an input
/// without a verdict (a task's episode) scores <c>none</c> (0). Deterministic and thread-safe.
/// </summary>
public sealed class EditorVerdictReward : IRewardFunction, IOptionSchema
{
    /// <summary>The reward's name.</summary>
    public const string RewardName = "editor-verdict";

    private static readonly OptionSpec<float> AcceptedOption = OptionSpec.Number("accepted", 1f, -100f, 100f, summary: "an answer accepted and kept");
    private static readonly OptionSpec<float> EditedOption = OptionSpec.Number("edited", 0.5f, -100f, 100f, summary: "an answer accepted, then edited (times 1 less how far)");
    private static readonly OptionSpec<float> RejectedOption = OptionSpec.Number("rejected", -1f, -100f, 100f, summary: "an answer rejected");
    private static readonly OptionSpec<float> UndoneOption = OptionSpec.Number("undone", -1f, -100f, 100f, summary: "an answer accepted, then undone");
    private static readonly OptionSpec<float> NoneOption = OptionSpec.Number("none", 0f, -100f, 100f, summary: "an input without a verdict");

    /// <summary>The info key of how far an accepted answer was edited (0 to 1): "edited".</summary>
    public static EpisodeInfoKey<double> EditedKey { get; } = new("edited");

    /// <inheritdoc />
    public string Name => RewardName;

    /// <inheritdoc />
    public string Summary => "what the user did with the editor's answer: accepted (1), edited (0.5, less by how far), rejected (-1), undone (-1)";

    /// <inheritdoc />
    public OptionSchema Options { get; } = new($"reward {RewardName}", AcceptedOption, EditedOption, RejectedOption, UndoneOption, NoneOption);

    /// <inheritdoc />
    public IReadOnlySet<string> Keys => Options.Keys;

    /// <inheritdoc />
    public ValueTask<float[]> ScoreAsync(IReadOnlyList<RewardInput> inputs, IReadOnlyDictionary<string, string>? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        var values = Options.Parse(options);
        var scores = new float[inputs.Count];
        for (int i = 0; i < scores.Length; i++)
        {
            var info = inputs[i]?.Info;
            if (info is null || !info.TryGet(PreferenceInfoKeys.Verdict, out string verdict))
            {
                scores[i] = values.Get(NoneOption);
                continue;
            }

            scores[i] = verdict.ToLowerInvariant() switch
            {
                PreferenceInfoKeys.Accepted => values.Get(AcceptedOption),
                PreferenceInfoKeys.Edited => values.Get(EditedOption) * (1f - (float)Math.Clamp(info.GetOrDefault(EditedKey, 0d), 0d, 1d)),
                PreferenceInfoKeys.Rejected => values.Get(RejectedOption),
                PreferenceInfoKeys.Undone => values.Get(UndoneOption),
                _ => values.Get(NoneOption),
            };
        }

        return ValueTask.FromResult(scores);
    }
}
