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
    private static readonly string[] Devices = ["auto", "cpu", "cuda:0", "vulkan:0"];
    private static readonly string[] Weights = ["", "int8", "int4", "bfloat16"];

    // A role that takes the chat's when empty: "" (the chat's) first, then float32 named, then the smaller ones.
    private static readonly string[] InheritedWeights = ["", "float32", "int8", "int4", "bfloat16"];

    /// <summary>The declared settings with their sections and shapes.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } = PluginSettingShapes.Apply(AiSettings.Declared, SectionOf, ShapeOf);

    /// <summary>The section of the setting <paramref name="setting"/>.</summary>
    public static string SectionOf(PluginSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.Key.StartsWith("completion", StringComparison.Ordinal) ? SettingSections.Completion
            : setting.Key.StartsWith("agent", StringComparison.Ordinal) ? SettingSections.Agent
            : setting.Key.StartsWith("chat", StringComparison.Ordinal) || setting.Key.StartsWith("draft", StringComparison.Ordinal)
              || setting.Key is "model" or "device" or "weights" or "context" or "keepAliveMinutes" ? SettingSections.Chat
            : SettingSections.Models;
    }

    private static SettingShape? ShapeOf(PluginSetting setting) => setting.Key switch
    {
        // The chat (its variables kept from before note 25: CRAFT_PLUGIN_AI_MODEL names the chat's model).
        "chatEnabled" => SettingShape.Toggle(SettingSections.Chat, "Chat", ModelGroup),
        "model" => SettingShape.Text(SettingSections.Chat, "Model", ModelGroup),
        "device" => Device(SettingSections.Chat, Devices),
        "weights" => SettingShape.Choice(SettingSections.Chat, "Weights once loaded", Weights, ModelGroup),
        "context" => Context(SettingSections.Chat),
        "keepAliveMinutes" => KeepAlive(SettingSections.Chat),
        "chatTemperature" => Temperature(SettingSections.Chat),
        "chatMaxTokens" => MaxTokens(SettingSections.Chat, "Most reply tokens"),
        "draftModel" => SettingShape.Text(SettingSections.Chat, "Draft model (empty: the completion model when it matches; off: none)", DraftGroup),
        "draftTokens" => DraftTokens(SettingSections.Chat, "Draft tokens a round (0: adapted)"),

        // Inline completion.
        "completion" => SettingShape.Toggle(SettingSections.Completion, "Inline completion (AI)", ModelGroup),
        "completionModel" => SettingShape.Text(SettingSections.Completion, "Model", ModelGroup),
        "completionDevice" => Device(SettingSections.Completion, ["", .. Devices]),
        "completionWeights" => SettingShape.Choice(SettingSections.Completion, "Weights once loaded", InheritedWeights, ModelGroup),
        "completionContext" => Context(SettingSections.Completion),
        "completionKeepAliveMinutes" => KeepAlive(SettingSections.Completion),
        "completionTemperature" => Temperature(SettingSections.Completion),
        "completionMaxTokens" => MaxTokens(SettingSections.Completion, "Most tokens per completion"),
        "completionDelayMs" => SettingShape.Whole(SettingSections.Completion, "Delay after typing", 0, 60_000, "ms", ModelGroup, step: 50),

        // The agent: an empty value is the chat's.
        "agentEnabled" => SettingShape.Toggle(SettingSections.Agent, "Agent", ModelGroup),
        "agentModel" => SettingShape.Text(SettingSections.Agent, "Model (empty: the chat's)", ModelGroup),
        "agentDevice" => Device(SettingSections.Agent, ["", .. Devices]),
        "agentWeights" => SettingShape.Choice(SettingSections.Agent, "Weights once loaded", InheritedWeights, ModelGroup),
        "agentContext" => Context(SettingSections.Agent),
        "agentKeepAliveMinutes" => KeepAlive(SettingSections.Agent),
        "agentTemperature" => Temperature(SettingSections.Agent),
        "agentMaxTokens" => MaxTokens(SettingSections.Agent, "Most tokens per turn"),
        "agentDraftModel" => SettingShape.Text(SettingSections.Agent, "Draft model (empty: the chat's; off: none)", DraftGroup),
        "agentDraftTokens" => DraftTokens(SettingSections.Agent, "Draft tokens a round (0: adapted; empty: the chat's)"),
        "agentNudges" => SettingShape.Whole(SettingSections.Agent, "Corrections for unreadable tool calls", 0, 20, null, "Tool calls"),
        "agentCallsInText" => SettingShape.Toggle(SettingSections.Agent, "Read tool calls written as text", "Tool calls"),

        "chatImageMaxMb" => SettingShape.Whole(SettingSections.Chat, "Largest image", 1, 4096, "MB", "Context"),

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
