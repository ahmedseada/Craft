// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// Where the AI plug-in's settings (<see cref="AiSettings.Declared"/>) sit on the settings page, and the shapes of those
/// a switch or a number box read off the default would not fit (the owner's notes 13 and 25): each model role's set
/// (enabled, model, device, weights, context, keep-alive, temperature, most reply tokens) in its own section, Completion,
/// Chat and Agent and sandbox, with the device a choice of the measured devices (others typed), the weights a choice, and
/// the numbers with their ranges, steps and units; the rest under Models and AI. Each group under the plug-in's name.
/// </summary>
public static class AiSettingShapes
{
    private const string ModelGroup = "Model";
    private const string DraftGroup = "Speculative decoding";
    private const string InlineChatGroup = "Inline chat";
    private const string ReviewGroup = "Review changes";
    private const string BuildFixGroup = "Fix the build";
    private static readonly string[] Devices = ["auto", "cpu", "cuda:0", "vulkan:0"];
    private static readonly string[] Weights = [WeightsAuto.Auto, WeightsAuto.Float32, "bfloat16", "int8", "int4"];

    // A role that takes the chat's when empty: "" (the chat's) first, then float32 named, then the smaller ones.
    private static readonly string[] InheritedWeights = ["", WeightsAuto.Auto, WeightsAuto.Float32, "bfloat16", "int8", "int4"];

    // The decoding cache's formats (a layout registered by name is typed in .craft/settings.json or its variable).
    private static readonly string[] CacheFormats = [WeightsAuto.Auto, .. WeightsAuto.CacheFormats];
    private static readonly string[] InheritedCacheFormats = ["", WeightsAuto.Auto, .. WeightsAuto.CacheFormats];
    private const string SamplingGroup = "Sampling";

    /// <summary>The declared settings with their sections and shapes.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } = PluginSettingShapes.Apply(AiSettings.Declared, SectionOf, ShapeOf);

    /// <summary>The section of the setting <paramref name="setting"/>.</summary>
    public static string SectionOf(PluginSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.Key.StartsWith("inlineChat", StringComparison.Ordinal) || setting.Key.StartsWith("buildFix", StringComparison.Ordinal) ? SettingSections.Chat
            : setting.Key.StartsWith("review", StringComparison.Ordinal) ? SettingSections.SourceControl
            : setting.Key.StartsWith("completion", StringComparison.Ordinal) ? SettingSections.Completion
            : setting.Key.StartsWith("agent", StringComparison.Ordinal) ? SettingSections.Agent
            : setting.Key.StartsWith("chat", StringComparison.Ordinal) || setting.Key.StartsWith("draft", StringComparison.Ordinal)
              || setting.Key is "model" or "device" or "weights" or "cacheFormat" or "context" or "keepAliveMinutes" or "offload" ? SettingSections.Chat
            : SettingSections.Models;
    }

    private static SettingShape? ShapeOf(PluginSetting setting) => setting.Key switch
    {
        // The chat (its variables kept from before note 25: CRAFT_PLUGIN_AI_MODEL names the chat's model).
        "chatEnabled" => SettingShape.Toggle(SettingSections.Chat, "Chat", ModelGroup),
        "model" => SettingShape.Text(SettingSections.Chat, "Model", ModelGroup),
        "device" => Device(SettingSections.Chat, Devices),
        "weights" => SettingShape.Choice(SettingSections.Chat, "Weights once loaded", Weights, ModelGroup),
        "cacheFormat" => SettingShape.Choice(SettingSections.Chat, "Decoding cache format", CacheFormats, ModelGroup),
        "context" => Context(SettingSections.Chat),
        "keepAliveMinutes" => KeepAlive(SettingSections.Chat),
        "chatTemperature" => Temperature(SettingSections.Chat),
        "chatMaxTokens" => MaxTokens(SettingSections.Chat, "Most reply tokens"),
        "offload" => SettingShape.Toggle(SettingSections.Chat, "Offload to system memory when the device is full", ModelGroup),
        "chatTopK" => SettingShape.Whole(SettingSections.Chat, "Top-k (0: all)", 0, 1000, null, SamplingGroup),
        "chatTopP" => SettingShape.Number(SettingSections.Chat, "Top-p", 0, 1, 0.01, null, SamplingGroup),
        "chatMinP" => SettingShape.Number(SettingSections.Chat, "Min-p", 0, 1, 0.01, null, SamplingGroup),
        "chatRepeatPenalty" => SettingShape.Number(SettingSections.Chat, "Repeat penalty", 1, 2, 0.01, null, SamplingGroup),
        "chatSeed" => SettingShape.Text(SettingSections.Chat, "Seed (empty: random)", SamplingGroup),
        "weightsAutoContext" => SettingShape.Whole(SettingSections.Models, "Weights auto keeps room for", 256, 16_777_216, "tokens", "Weights auto", 1024),
        "weightsAutoHeadroom" => SettingShape.Number(SettingSections.Models, "Weights auto headroom (share of the weights)", 0, 1, 0.05, null, "Weights auto"),
        "draftModel" => SettingShape.Text(SettingSections.Chat, "Draft model (empty: the completion model when it matches; off: none)", DraftGroup),
        "draftTokens" => DraftTokens(SettingSections.Chat, "Draft tokens a round (0: adapted)"),

        // Inline completion.
        "completion" => SettingShape.Toggle(SettingSections.Completion, "Inline completion (AI)", ModelGroup),
        "completionModel" => SettingShape.Text(SettingSections.Completion, "Model", ModelGroup),
        "completionDevice" => Device(SettingSections.Completion, ["", .. Devices]),
        "completionWeights" => SettingShape.Choice(SettingSections.Completion, "Weights once loaded", InheritedWeights, ModelGroup),
        "completionCacheFormat" => SettingShape.Choice(SettingSections.Completion, "Decoding cache format (empty: the chat's)", InheritedCacheFormats, ModelGroup),
        "completionContext" => Context(SettingSections.Completion),
        "completionOffload" => SettingShape.Choice(SettingSections.Completion, "Offload (empty: the chat's)", ["", "true", "false"], ModelGroup),
        "completionKeepAliveMinutes" => KeepAlive(SettingSections.Completion),
        "completionTemperature" => Temperature(SettingSections.Completion),
        "completionMaxTokens" => MaxTokens(SettingSections.Completion, "Most tokens per completion"),
        "completionDelayMs" => SettingShape.Whole(SettingSections.Completion, "Delay after typing", 0, 60_000, "ms", ModelGroup, step: 50),

        // The agent: an empty value is the chat's.
        "agentEnabled" => SettingShape.Toggle(SettingSections.Agent, "Agent", ModelGroup),
        "agentModel" => SettingShape.Text(SettingSections.Agent, "Model (empty: the chat's)", ModelGroup),
        "agentDevice" => Device(SettingSections.Agent, ["", .. Devices]),
        "agentWeights" => SettingShape.Choice(SettingSections.Agent, "Weights once loaded", InheritedWeights, ModelGroup),
        "agentCacheFormat" => SettingShape.Choice(SettingSections.Agent, "Decoding cache format (empty: the chat's)", InheritedCacheFormats, ModelGroup),
        "agentContext" => Context(SettingSections.Agent),
        "agentOffload" => SettingShape.Choice(SettingSections.Agent, "Offload (empty: the chat's)", ["", "true", "false"], ModelGroup),
        "agentTopK" => SettingShape.Text(SettingSections.Agent, "Top-k (empty: the chat's)", SamplingGroup),
        "agentTopP" => SettingShape.Text(SettingSections.Agent, "Top-p (empty: the chat's)", SamplingGroup),
        "agentMinP" => SettingShape.Text(SettingSections.Agent, "Min-p (empty: the chat's)", SamplingGroup),
        "agentRepeatPenalty" => SettingShape.Text(SettingSections.Agent, "Repeat penalty (empty: the chat's)", SamplingGroup),
        "agentSeed" => SettingShape.Text(SettingSections.Agent, "Seed (empty: the chat's)", SamplingGroup),
        "agentKeepAliveMinutes" => KeepAlive(SettingSections.Agent),
        "agentTemperature" => Temperature(SettingSections.Agent),
        "agentMaxTokens" => MaxTokens(SettingSections.Agent, "Most tokens per turn"),
        "agentDraftModel" => SettingShape.Text(SettingSections.Agent, "Draft model (empty: the chat's; off: none)", DraftGroup),
        "agentDraftTokens" => DraftTokens(SettingSections.Agent, "Draft tokens a round (0: adapted; empty: the chat's)"),
        "agentNudges" => SettingShape.Whole(SettingSections.Agent, "Corrections for unreadable tool calls", 0, 20, null, "Tool calls"),
        "agentCallsInText" => SettingShape.Toggle(SettingSections.Agent, "Read tool calls written as text", "Tool calls"),

        "chatImageMaxMb" => SettingShape.Whole(SettingSections.Chat, "Largest image", 1, 4096, "MB", "Context"),

        // X part 9 item 23 and X part 11 items 4 and 5.
        "inlineChat" => SettingShape.Toggle(SettingSections.Chat, "Inline chat (Ctrl+I)", InlineChatGroup),
        "inlineChatContextLines" => SettingShape.Whole(SettingSections.Chat, "File lines around it", 0, 100_000, "lines", InlineChatGroup, step: 10),
        "inlineChatMaxTokens" => SettingShape.Whole(SettingSections.Chat, "Most answer tokens (0: the chat's)", 0, 1_000_000, "tokens", InlineChatGroup, step: 64),
        "reviewModel" => SettingShape.From(SettingSections.SourceControl, "Model", "commit-models", [ModelRoleKeys.Chat, "loaded", ModelRoleKeys.Agent, ModelRoleKeys.Completion], other: true, group: ReviewGroup),
        "reviewPrompt" => SettingShape.Text(SettingSections.SourceControl, "Instructions", ReviewGroup),
        "reviewMaxTokens" => SettingShape.Whole(SettingSections.SourceControl, "Most reply tokens (0: the role's)", 0, 1_000_000, "tokens", ReviewGroup, step: 64),
        "buildFix" => SettingShape.Toggle(SettingSections.Chat, "Fix with AI on a failed build or test", BuildFixGroup),
        "buildFixContextLines" => SettingShape.Whole(SettingSections.Chat, "Code lines around each error", 0, 10_000, "lines", BuildFixGroup),

        "placementMaxError" => SettingShape.Number(SettingSections.Models, "Placement's largest difference", 0, 1, 0.05),
        _ => null,
    };

    // The device: a choice of the devices found on this machine (the "devices" source, as the composer's chip lists
    // them), the usual names before they are found, any other typed.
    private static SettingShape Device(string section, IReadOnlyList<string> also) =>
        SettingShape.From(section, "Device", "devices", also, other: true, group: ModelGroup);

    private static SettingShape Context(string section) =>
        SettingShape.Whole(section, "Context window (0: the model's own)", 0, 16_777_216, "tokens", ModelGroup, step: 1024);

    private static SettingShape KeepAlive(string section) =>
        SettingShape.Whole(section, "Keep loaded once unused", 0, 1_000_000, "min", ModelGroup);

    private static SettingShape Temperature(string section) =>
        SettingShape.Number(section, "Temperature", 0, 2, 0.05, null, ModelGroup);

    private static SettingShape DraftTokens(string section, string title) =>
        SettingShape.Whole(section, title, 0, 1024, "tokens", DraftGroup);

    private static SettingShape MaxTokens(string section, string title) =>
        SettingShape.Whole(section, title, 1, 1_000_000, "tokens", ModelGroup, step: 64);
}
