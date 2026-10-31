// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Core.Settings;

/// <summary>Where a setting's value comes from, lowest first.</summary>
public enum SettingSource
{
    /// <summary>Its default.</summary>
    Default,

    /// <summary>The user's <c>settings.json</c> in the data folder.</summary>
    User,

    /// <summary>The open workspace's <c>.craft/settings.json</c>.</summary>
    Workspace,

    /// <summary>The process's environment variable, which overrides both files.</summary>
    Environment,
}

/// <summary>A settings file the settings page writes.</summary>
public enum SettingLayer
{
    /// <summary>The user's <c>settings.json</c> in the data folder (every workspace).</summary>
    User,

    /// <summary>The open workspace's <c>.craft/settings.json</c>.</summary>
    Workspace,
}

/// <summary>A setting as the settings page shows it.</summary>
/// <param name="Variable">Its variable, default and meaning.</param>
/// <param name="Value">Its value in effect, or null for its default.</param>
/// <param name="Source">Where <paramref name="Value"/> comes from.</param>
/// <param name="UserValue">Its value in the user's file, or null.</param>
/// <param name="WorkspaceValue">Its value in the workspace's file, or null.</param>
/// <param name="Note">What limits it (a file that may not set it, a value that did not parse), or null.</param>
public sealed record SettingRow(CraftVariable Variable, string? Value, SettingSource Source, string? UserValue, string? WorkspaceValue, string? Note);

/// <summary>
/// The settings in layers: each variable's value is the environment's when set, else the open workspace's
/// <c>.craft/settings.json</c>, else the user's <c>settings.json</c> in the data folder, else its default. So a variable set
/// for a run wins and is shown as such, a workspace adjusts what it needs, and the user's file holds the rest. Two
/// variables are refused from files that could not or should not set them, said so: <c>CRAFT_DATA_FOLDER</c> (where the
/// user's file lives) is read from the environment only, and a workspace (a folder someone else may have written) sets
/// neither <c>CRAFT_PLUGIN_FOLDERS</c> (code to load) nor <c>CRAFT_PLUGINS_DISABLED</c>. <see cref="Read"/> is what
/// <see cref="CraftOptions.FromVariables"/> and the plug-ins' settings read through. Used on the UI thread.
/// </summary>
public sealed class CraftSettings
{
    /// <summary>The name of the settings file, in the data folder and in a workspace's <c>.craft</c> folder.</summary>
    public const string FileName = "settings.json";

    /// <summary>The workspace's folder of Craft's files.</summary>
    public const string WorkspaceFolderName = ".craft";

    private static readonly HashSet<string> EnvironmentOnly = new(StringComparer.Ordinal) { "CRAFT_DATA_FOLDER" };
    private static readonly HashSet<string> NotFromWorkspace = new(StringComparer.Ordinal) { "CRAFT_DATA_FOLDER", "CRAFT_PLUGIN_FOLDERS", "CRAFT_PLUGINS_DISABLED" };

    private readonly Func<string, string?> _environment;

    /// <summary>Settings over <paramref name="environment"/> (the process's when null), the user's file and a workspace's file (each may be null).</summary>
    public CraftSettings(Func<string, string?>? environment, SettingsFile? user, SettingsFile? workspace = null)
    {
        _environment = environment ?? System.Environment.GetEnvironmentVariable;
        User = user;
        Workspace = workspace;
    }

    /// <summary>The user's file, or null when there is none (no data folder).</summary>
    public SettingsFile? User { get; }

    /// <summary>The open workspace's file, or null when no folder is open.</summary>
    public SettingsFile? Workspace { get; set; }

    /// <summary>The user's file in <paramref name="options"/>' data folder.</summary>
    public static string UserFilePath(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Path.Combine(options.ResolveDataFolder(), FileName);
    }

    /// <summary>The workspace file of the folder <paramref name="root"/>.</summary>
    public static string WorkspaceFilePath(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return Path.Combine(Path.GetFullPath(root), WorkspaceFolderName, FileName);
    }

    /// <summary>Whether <paramref name="layer"/>'s file may set <paramref name="variable"/>.</summary>
    public static bool Allows(SettingLayer layer, string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return layer == SettingLayer.User ? !EnvironmentOnly.Contains(variable) : !NotFromWorkspace.Contains(variable);
    }

    /// <summary><paramref name="variable"/>'s value in effect (the environment's, the workspace's, the user's), or null for its default.</summary>
    public string? Read(string variable) => Resolve(variable).Value;

    /// <summary>Where <paramref name="variable"/>'s value in effect comes from.</summary>
    public SettingSource SourceOf(string variable) => Resolve(variable).Source;

    /// <summary>
    /// The options: the defaults with every variable's value in effect read over them, one by one, so a value that does not
    /// parse leaves its own setting at the default and is reported in <paramref name="problems"/> (naming the variable,
    /// where it came from and why) while every other setting is still taken.
    /// </summary>
    public CraftOptions BuildOptions(out IReadOnlyList<string> problems)
    {
        var options = new CraftOptions();
        var found = new List<string>();
        foreach (var variable in CraftOptions.Variables)
        {
            var (value, source) = Resolve(variable.Name);
            if (value is null)
            {
                continue;
            }

            try
            {
                options = CraftOptions.FromVariables(name => name == variable.Name ? value : null, options);
            }
            catch (ArgumentException e)
            {
                found.Add($"{e.Message} (from {Describe(source, variable.Name)}; its default is used)");
            }
        }

        foreach (var file in (ReadOnlySpan<SettingsFile?>)[User, Workspace])
        {
            if (file is not null)
            {
                found.AddRange(file.Problems);
            }
        }

        problems = found;
        return options;
    }

    /// <summary>Why <paramref name="value"/> is not a value of the Craft option <paramref name="variable"/>, or null when it is (a plug-in's setting, or a variable Craft does not know, is taken as it is).</summary>
    public static string? Validate(string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (string.IsNullOrWhiteSpace(value) || !CraftOptions.Variables.Any(v => v.Name == variable))
        {
            return null;
        }

        try
        {
            CraftOptions.FromVariables(name => name == variable ? value : null);
            return null;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="variable"/> into <paramref name="layer"/>'s file (null or empty
    /// removes it, so the next layer's value or the default applies) and saves the file; true when the file changed.
    /// </summary>
    /// <exception cref="InvalidOperationException">That layer has no file (no folder is open, or no data folder).</exception>
    /// <exception cref="ArgumentException">The file may not set the variable, or the value does not parse (the message says why).</exception>
    public bool Set(SettingLayer layer, string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var file = (layer == SettingLayer.User ? User : Workspace)
            ?? throw new InvalidOperationException(layer == SettingLayer.User ? "There is no user settings file (no data folder)." : "No folder is open, so there is no workspace settings file.");
        var name = variable.Trim().ToUpperInvariant();
        if (!Allows(layer, name) && !string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(layer == SettingLayer.User
                ? $"{name} is read from the environment only."
                : $"{name} cannot be set by a workspace (set it in the user's settings).", nameof(variable));
        }

        if (Validate(name, value) is { } error)
        {
            throw new ArgumentException(error, nameof(value));
        }

        if (!file.Set(name, value))
        {
            return false;
        }

        file.Save();
        return true;
    }

    /// <summary>
    /// The rows of the settings page: every Craft option (<see cref="CraftOptions.Variables"/>) in order, then
    /// <paramref name="extra"/> (the plug-ins' declared settings), each with its value in effect and where it comes from.
    /// </summary>
    public IReadOnlyList<SettingRow> Rows(IEnumerable<CraftVariable>? extra = null)
    {
        var rows = new List<SettingRow>();
        foreach (var variable in extra is null ? CraftOptions.Variables : CraftOptions.Variables.Concat(extra))
        {
            var (value, source) = Resolve(variable.Name);
            string? note = null;
            if (EnvironmentOnly.Contains(variable.Name))
            {
                note = "Read from the environment only.";
            }
            else if (NotFromWorkspace.Contains(variable.Name))
            {
                note = "A workspace cannot set it; the user's settings can.";
            }

            if (value is not null && Validate(variable.Name, value) is { } error)
            {
                note = (note is null ? "" : note + " ") + error + " Its default is used.";
            }

            rows.Add(new SettingRow(variable, value, source, User?.Get(variable.Name), Workspace?.Get(variable.Name), note));
        }

        return rows;
    }

    /// <summary>"the environment (CRAFT_X)", "the workspace's settings (PATH)", "the user's settings (PATH)" or "its default".</summary>
    public string Describe(SettingSource source, string variable) => source switch
    {
        SettingSource.Environment => $"the environment ({variable})",
        SettingSource.Workspace => $"the workspace's settings ({Workspace?.FilePath})",
        SettingSource.User => $"the user's settings ({User?.FilePath})",
        _ => "its default",
    };

    private (string? Value, SettingSource Source) Resolve(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (_environment(variable) is { } set && !string.IsNullOrWhiteSpace(set))
        {
            return (set.Trim(), SettingSource.Environment);
        }

        if (Workspace?.Get(variable) is { } workspace && !NotFromWorkspace.Contains(variable))
        {
            return (workspace, SettingSource.Workspace);
        }

        if (User?.Get(variable) is { } user && !EnvironmentOnly.Contains(variable))
        {
            return (user, SettingSource.User);
        }

        return (null, SettingSource.Default);
    }
}
