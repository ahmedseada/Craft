// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Plugins;
using Idrak.Abstraction.Plugins;

namespace Craft.Editor.Shell;

/// <summary>A plug-in's state on the Plug-ins page.</summary>
public enum PluginState
{
    /// <summary>Loaded and started.</summary>
    Active,

    /// <summary>Loaded, built against an older contract major (or shadowing the library's).</summary>
    Outdated,

    /// <summary>Refused, failed to load, or failed to start.</summary>
    Problem,

    /// <summary>Disabled by the user.</summary>
    Off,
}

/// <summary>A row of the Plug-ins page.</summary>
/// <param name="Name">The plug-in's folder (or Idrak plug-in's) name.</param>
/// <param name="Summary">What it brings.</param>
/// <param name="Registries">The registries it adds to ("EditorLanguages · EditorCommands"), or "".</param>
/// <param name="Source">"Built in" (beside the app), "Folder" (a user's plug-in folder) or "Idrak" (an Idrak plug-in).</param>
/// <param name="State">Its state.</param>
/// <param name="StateText">The state as the page writes it ("Active", "Not loaded · the reason").</param>
/// <param name="Version">Its version, or null.</param>
/// <param name="Folder">Its folder, or null.</param>
/// <param name="Capabilities">What it asks for (files, network, sandbox, model).</param>
/// <param name="Problem">The reason it is not running, or null.</param>
public sealed record PluginRow(string Name, string Summary, string Registries, string Source, PluginState State, string StateText,
    string? Version, string? Folder, IReadOnlyList<string> Capabilities, string? Problem);

/// <summary>The Plug-ins page's filter.</summary>
public enum PluginFilter
{
    /// <summary>Every row.</summary>
    All,

    /// <summary>The ones marked outdated or shadowing.</summary>
    Overrides,

    /// <summary>The ones with a problem.</summary>
    Problems,
}

/// <summary>
/// The Plug-ins page's rows (phase D, docs/design/Plugins.dc.html), over K's load results (<see cref="CraftPlugins"/>), the
/// host's activations and the public registries (what each plug-in's assembly registered), then Idrak's plug-ins
/// (<see cref="IdrakPlugins"/>). Made when the page opens or refreshes, never on a keystroke.
/// </summary>
public static class PluginList
{
    /// <summary>The rows: each installed Craft plug-in, then each Idrak plug-in registered in this process.</summary>
    public static IReadOnlyList<PluginRow> Rows(IReadOnlyList<PluginLoadResult> installed, IReadOnlyList<PluginActivation> activations)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(activations);
        var rows = new List<PluginRow>();
        string builtIn = Path.GetFullPath(CraftPlugins.InstalledFolder);
        foreach (var plugin in installed)
        {
            var manifest = plugin.Manifest;
            string folder = plugin.Folder;
            bool isBuiltIn = Path.GetFullPath(folder).StartsWith(builtIn, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            var registries = RegistriesOf(plugin.Name);
            string? startError = activations.FirstOrDefault(a => a.Error is not null && registries.Plugins.Contains(a.Name, StringComparer.OrdinalIgnoreCase))?.Error;
            (PluginState state, string text, string? problem) = plugin switch
            {
                { Disabled: true } => (PluginState.Off, "Off · disabled (CRAFT_PLUGINS_DISABLED)", (string?)null),
                { Loaded: false } => (PluginState.Problem, "Not loaded", plugin.Error),
                _ when startError is not null => (PluginState.Problem, "Did not start", startError),
                { Outdated: true } => (PluginState.Outdated, "Outdated contract · loaded", plugin.Note),
                _ => (PluginState.Active, "Active", (string?)null),
            };
            rows.Add(new PluginRow(plugin.Name, manifest?.Summary is { Length: > 0 } summary ? summary : plugin.Note ?? "", registries.Text,
                isBuiltIn ? "Built in" : "Folder", state, text, manifest?.Version, folder, manifest?.Capabilities ?? [], problem));
        }

        foreach (var name in IdrakPlugins.Names.Order(StringComparer.OrdinalIgnoreCase))
        {
            var plugin = IdrakPlugins.Find(name);
            string origin = IdrakPlugins.Origin(name) ?? "";
            rows.Add(new PluginRow(name, origin.Length > 0 ? $"An Idrak plug-in ({origin})" : "An Idrak plug-in", "IdrakPlugins", "Idrak",
                plugin is { IsRegistered: true } ? PluginState.Active : PluginState.Off, plugin is { IsRegistered: true } ? "Registered" : "Not registered",
                null, null, [], null));
        }

        return rows;
    }

    /// <summary>The rows <paramref name="filter"/> and <paramref name="text"/> (in the name, summary or registries, ignoring case) keep.</summary>
    public static IReadOnlyList<PluginRow> Filter(IReadOnlyList<PluginRow> rows, PluginFilter filter, string? text)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var kept = new List<PluginRow>();
        foreach (var row in rows)
        {
            bool byState = filter switch
            {
                PluginFilter.Overrides => row.State == PluginState.Outdated,
                PluginFilter.Problems => row.State == PluginState.Problem,
                _ => true,
            };
            bool byText = string.IsNullOrWhiteSpace(text)
                || row.Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.Summary.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.Registries.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
            if (byState && byText)
            {
                kept.Add(row);
            }
        }

        return kept;
    }

    /// <summary>How many rows each filter keeps.</summary>
    public static (int All, int Overrides, int Problems) Counts(IReadOnlyList<PluginRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return (rows.Count, rows.Count(r => r.State == PluginState.Outdated), rows.Count(r => r.State == PluginState.Problem));
    }

    // What the assembly named after the plug-in's folder registered in Craft's public registries (by the registered
    // object's type), and the commands under its editor plug-ins' names.
    private static (string Text, List<string> Plugins) RegistriesOf(string assembly)
    {
        bool Of(object item) => string.Equals(item.GetType().Assembly.GetName().Name, assembly, StringComparison.OrdinalIgnoreCase);
        var found = new List<string>();
        var plugins = EditorPlugins.All.Where(Of).Select(p => p.Name).ToList();
        if (plugins.Count > 0)
        {
            found.Add(nameof(EditorPlugins));
        }

        if (EditorLanguages.All.Any(Of))
        {
            found.Add(nameof(EditorLanguages));
        }

        if (EditorThemes.Names.Any(n => EditorThemes.Find(n) is { } theme && Of(theme)))
        {
            found.Add(nameof(EditorThemes));
        }

        if (ModelProviders.All.Any(Of))
        {
            found.Add(nameof(ModelProviders));
        }

        if (WorkspaceReaders.Names.Any(n => WorkspaceReaders.Find(n) is { } reader && Of(reader)))
        {
            found.Add(nameof(WorkspaceReaders));
        }

        if (plugins.Count > 0 && EditorCommands.Names.Any(id => plugins.Any(p => id.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase))))
        {
            found.Add(nameof(EditorCommands));
        }

        return (string.Join(" · ", found), plugins);
    }
}
