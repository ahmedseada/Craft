// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;

namespace Craft.Editor.Shell;

/// <summary>One setting as the settings page draws it, made from its declared shape (<see cref="CraftVariable.Shape"/>, <see cref="PluginSetting.Shape"/>).</summary>
/// <param name="Variable">Its variable (<c>CRAFT_TAB_SIZE</c>), shown small.</param>
/// <param name="Title">Its label.</param>
/// <param name="Description">What it sets.</param>
/// <param name="Shape">Its kind, section, range and choices: the control it gets.</param>
/// <param name="Default">Its default, as text.</param>
/// <param name="Value">Its value in effect (the default when nothing sets it).</param>
/// <param name="Source">Where <paramref name="Value"/> comes from.</param>
/// <param name="ScopeValue">Its value in the file the page writes now (<see cref="SettingsPageModel.Scope"/>), or null.</param>
/// <param name="UserValue">Its value in the user's file, or null.</param>
/// <param name="WorkspaceValue">Its value in the workspace's file, or null.</param>
/// <param name="ReadOnlyReason">Why it cannot be changed here (the environment overrides it, the file may not set it), or null.</param>
/// <param name="Note">What else limits it (a value that did not parse, a change taken at the next start), or null.</param>
/// <param name="Plugin">The plug-in that declares it, or null for a Craft option.</param>
/// <param name="Choices">The values a choice or a font offers.</param>
public sealed record SettingEntry(string Variable, string Title, string Description, SettingShape Shape, string Default, string Value, SettingSource Source,
    string? ScopeValue, string? UserValue, string? WorkspaceValue, string? ReadOnlyReason, string? Note, string? Plugin, IReadOnlyList<string> Choices)
{
    /// <summary>Whether something sets it (a file or the environment): the page marks it modified.</summary>
    public bool IsModified => Source != SettingSource.Default;

    /// <summary>Whether the page cannot change it (<see cref="ReadOnlyReason"/>).</summary>
    public bool IsReadOnly => ReadOnlyReason is not null;

    /// <summary>Whether the file the page writes holds a value of it (so Reset there does something).</summary>
    public bool CanReset => ScopeValue is not null && !IsReadOnly;
}

/// <summary>A heading of a section: a group of its settings ("Caret"), or a plug-in's settings under the plug-in's name.</summary>
/// <param name="Title">The heading ("" for the section's settings of no group).</param>
/// <param name="Plugin">The plug-in, when the group is one plug-in's settings.</param>
/// <param name="Entries">Its settings, in their declared order.</param>
public sealed record SettingGroup(string Title, string? Plugin, IReadOnlyList<SettingEntry> Entries);

/// <summary>A section of the left list: its id and title, how many settings it holds, and how many something sets.</summary>
/// <param name="Id">Its id (<see cref="SettingSections"/>).</param>
/// <param name="Title">Its title.</param>
/// <param name="Count">Its settings (the keyboard's: its commands).</param>
/// <param name="Modified">Its settings something sets (the keyboard's: the user's bindings).</param>
public sealed record SettingSectionRow(string Id, string Title, int Count, int Modified);

/// <summary>
/// The settings page without a UI (the owner's note 13): the sections in the left list, each setting with the control its
/// declared shape asks for, a search across every section, the file a change goes to (<see cref="Scope"/>: the user's
/// or this workspace's), a change applied at once (the shell's options reload), modified marks and a reset per setting
/// and per section, the environment's values read-only with why, and the plug-ins' settings grouped under their plug-in.
/// Nothing is written per setting: every row is made from the metadata declared next to the option. Used on the UI thread.
/// </summary>
public sealed class SettingsPageModel
{
    private readonly ShellModel _shell;
    private SettingLayer? _scope;

    /// <summary>The page over <paramref name="shell"/>'s settings.</summary>
    public SettingsPageModel(ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>The file a change goes to: this workspace's when a folder is open (unless the user's was chosen), else the user's.</summary>
    public SettingLayer Scope
    {
        get => _scope is { } chosen && (chosen == SettingLayer.User || CanWriteWorkspace) ? chosen : CanWriteWorkspace ? SettingLayer.Workspace : SettingLayer.User;
        set => _scope = value;
    }

    /// <summary>Whether a folder is open, so this workspace's file can be written.</summary>
    public bool CanWriteWorkspace => _shell.Settings.Workspace is not null;

    /// <summary>The settings files' paths, for the page's foot ("User: …", "Workspace: …").</summary>
    public (string? User, string? Workspace) Files => (_shell.Settings.User?.FilePath, _shell.Settings.Workspace?.FilePath);

    /// <summary>The problems of the settings in effect (a value that did not parse, a keymap not registered).</summary>
    public IReadOnlyList<string> Problems => _shell.Host.KeyMapProblem is { } keyMap ? [.. _shell.OptionsProblems, keyMap] : _shell.OptionsProblems;

    /// <summary>Every setting, in its section's order then its declared order.</summary>
    public IReadOnlyList<SettingEntry> Entries()
    {
        var plugins = new Dictionary<string, (string Plugin, string Key)>(StringComparer.Ordinal);
        foreach (var value in _shell.Host.Settings.All)
        {
            plugins[value.Variable] = (value.Plugin, value.Setting.Key);
        }

        var scope = Scope;
        var entries = new List<SettingEntry>();
        foreach (var row in _shell.SettingRows())
        {
            entries.Add(Make(row, scope, plugins.TryGetValue(row.Variable.Name, out var plugin) ? plugin : null));
        }

        return entries;
    }

    /// <summary>The left list: every section that has a setting (the Keyboard section always), in the page's order.</summary>
    public IReadOnlyList<SettingSectionRow> Sections()
    {
        var entries = Entries();
        var rows = new List<SettingSectionRow>();
        foreach (var (id, title) in SettingSections.All)
        {
            if (id == SettingSections.Keyboard)
            {
                rows.Add(new SettingSectionRow(id, title, EditorCommands.All.Count, _shell.Options.KeyBindings.Count));
                continue;
            }

            int count = 0;
            int modified = 0;
            foreach (var entry in entries)
            {
                if (string.Equals(SectionOf(entry), id, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    modified += entry.IsModified ? 1 : 0;
                }
            }

            if (count > 0)
            {
                rows.Add(new SettingSectionRow(id, title, count, modified));
            }
        }

        return rows;
    }

    /// <summary>
    /// The groups of section <paramref name="sectionId"/> (its settings by group, the plug-ins' under their names), or with
    /// a <paramref name="search"/>, the matching settings of every section (by title, variable, description, section or
    /// plug-in), grouped by section.
    /// </summary>
    public IReadOnlyList<SettingGroup> Groups(string sectionId, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        var query = (search ?? "").Trim();
        var groups = new List<SettingGroup>();
        var byTitle = new Dictionary<(string Title, string? Plugin), List<SettingEntry>>();
        foreach (var entry in Entries())
        {
            string section = SectionOf(entry);
            if (query.Length > 0 ? !Matches(entry, section, query) : !string.Equals(section, sectionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (string, string?) key = query.Length > 0 ? (SettingSections.TitleOf(section), null)
                : entry.Plugin is { } plugin ? (plugin, plugin)
                : (entry.Shape.Group ?? "", null);
            if (!byTitle.TryGetValue(key, out var list))
            {
                list = [];
                byTitle[key] = list;
                groups.Add(new SettingGroup(key.Item1, key.Item2, list));
            }

            list.Add(entry);
        }

        return groups;
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="variable"/> into <see cref="Scope"/>'s file and applies it at once;
    /// null when done, else why not (it is read-only, the value is out of its range or not one of its choices, the file
    /// could not be written). The value is checked against the setting's declared shape first (a plug-in's too).
    /// </summary>
    public string? Set(string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (Entries().FirstOrDefault(e => e.Variable == variable) is not { } entry)
        {
            return $"{variable} is not a setting of Craft or of a plug-in.";
        }

        if (entry.ReadOnlyReason is { } reason)
        {
            return reason;
        }

        if (value is not null && Check(entry, value.Trim()) is { } problem)
        {
            return problem;
        }

        return _shell.ApplySetting(Scope, variable, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Takes <paramref name="variable"/> out of <see cref="Scope"/>'s file, so the next layer's value or the default applies.</summary>
    public string? Reset(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (_shell.Settings.SourceOf(variable) == SettingSource.Environment)
        {
            return $"The environment sets {variable}; unset the variable to change it.";
        }

        return _shell.ApplySetting(Scope, variable, null);
    }

    /// <summary>Takes every setting of section <paramref name="sectionId"/> out of <see cref="Scope"/>'s file; the first problem, or null.</summary>
    public string? ResetSection(string sectionId)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        string? first = null;
        foreach (var entry in Entries())
        {
            if (entry.CanReset && string.Equals(SectionOf(entry), sectionId, StringComparison.OrdinalIgnoreCase))
            {
                first ??= _shell.ApplySetting(Scope, entry.Variable, null);
            }
        }

        return first;
    }

    /// <summary>The section of <paramref name="entry"/>: its declared one, else the plug-ins'.</summary>
    public static string SectionOf(SettingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrEmpty(entry.Shape.Section) ? SettingSections.Plugins : entry.Shape.Section;
    }

    /// <summary>Why <paramref name="value"/> does not fit <paramref name="entry"/>'s shape, or null when it does.</summary>
    public static string? Check(SettingEntry entry, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return null;
        }

        var shape = entry.Shape;
        switch (shape.Kind)
        {
            case SettingKind.Toggle when !bool.TryParse(value, out _) && value is not ("1" or "0"):
                return $"{entry.Variable} takes true or false.";
            case SettingKind.Whole:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
                {
                    return $"{entry.Variable} takes a whole number.";
                }

                return OutOfRange(entry, whole);
            case SettingKind.Number:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    return $"{entry.Variable} takes a number.";
                }

                return OutOfRange(entry, number);
            case SettingKind.Choice when !shape.OtherAllowed && entry.Choices.Count > 0 && !entry.Choices.Contains(value, StringComparer.OrdinalIgnoreCase):
                return $"{entry.Variable} takes one of: {string.Join(", ", entry.Choices.Where(c => c.Length > 0))}.";
        }

        return entry.Plugin is null ? CraftSettings.Validate(entry.Variable, value) : null;
    }

    /// <summary>A number's text as the page writes it (invariant, no trailing zeros).</summary>
    public static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The value a stepper gives: <paramref name="entry"/>'s value in effect plus <paramref name="steps"/> of its step, kept within its range.</summary>
    public static string Step(SettingEntry entry, int steps)
    {
        ArgumentNullException.ThrowIfNull(entry);
        double step = entry.Shape.Step ?? 1;
        double current = double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : entry.Shape.Min ?? 0;
        double next = Math.Clamp(current + (steps * step), entry.Shape.Min ?? double.MinValue, entry.Shape.Max ?? double.MaxValue);
        return entry.Shape.Kind == SettingKind.Whole ? FormatNumber(Math.Round(next)) : FormatNumber(Math.Round(next / step) * step);
    }

    private static string? OutOfRange(SettingEntry entry, double value) =>
        (entry.Shape.Min is { } min && value < min) || (entry.Shape.Max is { } max && value > max)
            ? $"{entry.Variable} takes {FormatNumber(entry.Shape.Min ?? double.MinValue)} to {FormatNumber(entry.Shape.Max ?? double.MaxValue)}{(entry.Shape.Unit is { } unit ? " " + unit : "")}."
            : null;

    private static bool Matches(SettingEntry entry, string section, string query) =>
        entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.Variable.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        || SettingSections.TitleOf(section).Contains(query, StringComparison.OrdinalIgnoreCase)
        || (entry.Shape.Group?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (entry.Plugin?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static SettingEntry Make(SettingRow row, SettingLayer scope, (string Plugin, string Key)? declared)
    {
        string? plugin = declared?.Plugin;
        var variable = row.Variable;
        var shape = variable.Shape;
        string? readOnly = null;
        if (row.Source == SettingSource.Environment)
        {
            readOnly = $"Set by the environment ({variable.Name}), which overrides both files; unset the variable to change it here.";
        }
        else if (!CraftSettings.Allows(scope, variable.Name))
        {
            readOnly = scope == SettingLayer.User
                ? $"{variable.Name} is read from the environment only."
                : $"A workspace cannot set {variable.Name} (a folder someone else may have written); switch to the user's settings.";
        }

        string? note = row.Note;
        if (ShellModel.AppliedAtStart.Contains(variable.Name))
        {
            note = (note is null ? "" : note + " ") + "A change applies at the next start.";
        }

        string description = plugin is not null && variable.Meaning.StartsWith(plugin + ": ", StringComparison.Ordinal) ? variable.Meaning[(plugin.Length + 2)..] : variable.Meaning;
        string title = shape.Title ?? (declared is { Key: var key } ? Humanize(key) : TitleOf(variable.Name));
        var choices = shape.Kind is SettingKind.Choice or SettingKind.Font ? Choices(shape, row.Value, variable.Default) : [];
        string value = row.Value ?? DefaultValue(variable.Default, shape);
        string? scopeValue = scope == SettingLayer.Workspace ? row.WorkspaceValue : row.UserValue;
        return new SettingEntry(variable.Name, title, description, shape, variable.Default, value, row.Source, scopeValue, row.UserValue, row.WorkspaceValue, readOnly, note, plugin, choices);
    }

    // A default written as "(the theme's)" or "(none)" stands for no value: the control shows it empty.
    private static string DefaultValue(string text, SettingShape shape) =>
        text.StartsWith('(') && shape.Kind is not (SettingKind.Whole or SettingKind.Number or SettingKind.Toggle) ? "" : text;

    private static List<string> Choices(SettingShape shape, string? value, string defaultValue)
    {
        var all = new List<string>(shape.AllChoices());
        foreach (var extra in (ReadOnlySpan<string?>)[defaultValue, value])
        {
            if (extra is { Length: > 0 } text && !text.StartsWith('(') && !all.Contains(text, StringComparer.OrdinalIgnoreCase) && (shape.OtherAllowed || shape.Kind == SettingKind.Font))
            {
                all.Add(text);
            }
        }

        return all;
    }

    // "CRAFT_CARET_BLINK_MS" → "Caret blink ms".
    private static string TitleOf(string variable)
    {
        var name = variable.StartsWith("CRAFT_", StringComparison.Ordinal) ? variable["CRAFT_".Length..] : variable;
        var words = name.Replace('_', ' ').ToLowerInvariant();
        return words.Length == 0 ? variable : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // A plug-in's key: "showCharacters" → "Show characters", "max_tokens" → "Max tokens".
    private static string Humanize(string key)
    {
        var text = new System.Text.StringBuilder(key.Length + 4);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c is '_' or '-' or '.')
            {
                text.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && char.IsLower(key[i - 1]))
            {
                text.Append(' ');
            }

            text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return text.Length == 0 ? key : text.ToString();
    }
}
