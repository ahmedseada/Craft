// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// Where the AI plug-in's settings (<see cref="AiSettings.Declared"/>) sit on the settings page, and the shapes of those
/// a switch or a number box read off the default would not fit (the owner's note 13): inline completion's under
/// Completion, the agent's under Agent and sandbox, the rest under Models and AI, each group under the plug-in's name.
/// </summary>
public static class AiSettingShapes
{
    /// <summary>The declared settings with their sections and shapes.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } = PluginSettingShapes.Apply(AiSettings.Declared, SectionOf, ShapeOf);

    /// <summary>The section of the setting <paramref name="setting"/>.</summary>
    public static string SectionOf(PluginSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.Key.StartsWith("completion", StringComparison.Ordinal) ? SettingSections.Completion
            : setting.Key.StartsWith("agent", StringComparison.Ordinal) ? SettingSections.Agent
            : SettingSections.Models;
    }

    private static SettingShape? ShapeOf(PluginSetting setting) => setting.Key switch
    {
        "device" => SettingShape.From(SettingSections.Models, "Device", "devices", ["auto", "cpu", "cuda:0", "vulkan:0"], other: true, group: "Local model"),
        "weights" => SettingShape.Choice(SettingSections.Models, "Weights once loaded", ["", "int8", "int4", "bfloat16"], "Local model"),
        "model" => SettingShape.Text(SettingSections.Models, "Model", "Local model"),
        "completionTemperature" => SettingShape.Number(SettingSections.Completion, "Temperature", 0, 2, 0.05),
        "placementMaxError" => SettingShape.Number(SettingSections.Models, "Placement's largest difference", 0, 1, 0.05),
        _ => null,
    };
}
