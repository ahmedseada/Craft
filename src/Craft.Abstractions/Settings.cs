// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What kind of value a setting takes, so the settings page draws the control that fits it.</summary>
public enum SettingKind
{
    /// <summary>Any text (a text box).</summary>
    Text,

    /// <summary>true or false (a switch).</summary>
    Toggle,

    /// <summary>A whole number within <see cref="SettingShape.Min"/> and <see cref="SettingShape.Max"/> (a number box with steppers).</summary>
    Whole,

    /// <summary>A number within <see cref="SettingShape.Min"/> and <see cref="SettingShape.Max"/> (a number box with steppers).</summary>
    Number,

    /// <summary>One of <see cref="SettingShape.Choices"/> or of a registered source's names (a drop-down).</summary>
    Choice,

    /// <summary>A font family (a drop-down of the known families, any other typed, a preview).</summary>
    Font,

    /// <summary>A folder or a file (a text box).</summary>
    Path,

    /// <summary>Several values separated by a character (a text box).</summary>
    List,

    /// <summary>Key bindings (the keyboard section edits them).</summary>
    KeyBindings,
}

/// <summary>The sections of the settings page, in order: each setting declares one (<see cref="SettingShape.Section"/>).</summary>
public static class SettingSections
{
    /// <summary>Typing, the caret, find, undo.</summary>
    public const string Editor = "editor";

    /// <summary>The theme, the accent, the fonts, the layout.</summary>
    public const string Appearance = "appearance";

    /// <summary>Files, the workspace, recovery and the tree.</summary>
    public const string Files = "files";

    /// <summary>The C# language: Roslyn's workspace, diagnostics, colours.</summary>
    public const string CSharp = "csharp";

    /// <summary>The models, the devices, the chat.</summary>
    public const string Models = "models";

    /// <summary>The completion list and inline completion.</summary>
    public const string Completion = "completion";

    /// <summary>The chat's model and its replies.</summary>
    public const string Chat = "chat";

    /// <summary>The agent and its sandbox, proposals and the diff view.</summary>
    public const string Agent = "agent";

    /// <summary>The terminal.</summary>
    public const string Terminal = "terminal";

    /// <summary>Voice coding.</summary>
    public const string Voice = "voice";

    /// <summary>Learning in the editor.</summary>
    public const string Learning = "learning";

    /// <summary>The plug-ins and the settings of a plug-in that names no other section.</summary>
    public const string Plugins = "plugins";

    /// <summary>The keyboard shortcuts.</summary>
    public const string Keyboard = "keyboard";

    /// <summary>Every section's id and title, in the page's order.</summary>
    public static IReadOnlyList<(string Id, string Title)> All { get; } =
    [
        (Editor, "Editor"),
        (Appearance, "Appearance"),
        (Files, "Files and workspace"),
        (CSharp, "C# and Roslyn"),
        (Models, "Models and AI"),
        (Completion, "Completion"),
        (Chat, "Chat"),
        (Agent, "Agent and sandbox"),
        (Terminal, "Terminal"),
        (Voice, "Voice"),
        (Learning, "Learning"),
        (Plugins, "Plug-ins"),
        (Keyboard, "Keyboard"),
    ];

    /// <summary>The prefix of the commands that open the settings page at a section (<see cref="CommandOf"/>).</summary>
    public const string CommandPrefix = "craft.settings.";

    /// <summary>
    /// The command that opens the settings page at section <paramref name="id"/> ("craft.settings.chat"): the shell
    /// registers one per section, so a plug-in links to its settings ("the chat is off: open its settings").
    /// </summary>
    public static string CommandOf(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return CommandPrefix + id;
    }

    /// <summary>The title of section <paramref name="id"/> (the id itself for one not listed).</summary>
    public static string TitleOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var (section, title) in All)
        {
            if (string.Equals(section, id, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        return id;
    }
}

/// <summary>
/// A setting's shape, declared next to it (a Craft option's <c>CraftVariable</c>, a plug-in's <see cref="PluginSetting"/>):
/// its kind, its section and title, its range, step and unit, its choices (listed, or a registered source's names in
/// <see cref="SettingChoiceSources"/>), so the settings page is made from it and never written per setting.
/// </summary>
/// <param name="Kind">What it takes.</param>
/// <param name="Section">Its section (<see cref="SettingSections"/>).</param>
/// <param name="Title">Its label ("Tab size"); null: made from the variable.</param>
public sealed record SettingShape(SettingKind Kind, string Section, string? Title = null)
{
    /// <summary>The least value of a number.</summary>
    public double? Min { get; init; }

    /// <summary>The greatest value of a number.</summary>
    public double? Max { get; init; }

    /// <summary>What a stepper adds or takes (1 for a whole number by default).</summary>
    public double? Step { get; init; }

    /// <summary>The unit shown after a number ("ms", "lines", "units").</summary>
    public string? Unit { get; init; }

    /// <summary>The values a choice takes, listed.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The source in <see cref="SettingChoiceSources"/> whose names a choice also takes ("themes"), or null.</summary>
    public string? ChoicesFrom { get; init; }

    /// <summary>Whether a choice or a font takes a value outside its choices (typed).</summary>
    public bool OtherAllowed { get; init; }

    /// <summary>A heading within the section ("Caret", "Advanced"), or null.</summary>
    public string? Group { get; init; }

    /// <summary>A text setting of no declared shape, in the plug-ins' section.</summary>
    public static SettingShape Unknown { get; } = new(SettingKind.Text, SettingSections.Plugins);

    /// <summary>A switch.</summary>
    public static SettingShape Toggle(string section, string title, string? group = null) => new(SettingKind.Toggle, section, title) { Group = group };

    /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static SettingShape Whole(string section, string title, long min, long max, string? unit = null, string? group = null, double step = 1) =>
        new(SettingKind.Whole, section, title) { Min = min, Max = max, Unit = unit, Group = group, Step = step };

    /// <summary>A number from <paramref name="min"/> to <paramref name="max"/>, stepped by <paramref name="step"/>.</summary>
    public static SettingShape Number(string section, string title, double min, double max, double step, string? unit = null, string? group = null) =>
        new(SettingKind.Number, section, title) { Min = min, Max = max, Step = step, Unit = unit, Group = group };

    /// <summary>One of <paramref name="choices"/>.</summary>
    public static SettingShape Choice(string section, string title, IReadOnlyList<string> choices, string? group = null) =>
        new(SettingKind.Choice, section, title) { Choices = choices, Group = group };

    /// <summary>One of a registered source's names (and <paramref name="also"/>), or any other when <paramref name="other"/>.</summary>
    public static SettingShape From(string section, string title, string source, IReadOnlyList<string>? also = null, bool other = false, string? group = null) =>
        new(SettingKind.Choice, section, title) { ChoicesFrom = source, Choices = also ?? [], OtherAllowed = other, Group = group };

    /// <summary>A font family.</summary>
    public static SettingShape Font(string section, string title, string? group = null) =>
        new(SettingKind.Font, section, title) { ChoicesFrom = SettingChoiceSources.Fonts, OtherAllowed = true, Group = group };

    /// <summary>Text.</summary>
    public static SettingShape Text(string section, string title, string? group = null) => new(SettingKind.Text, section, title) { Group = group };

    /// <summary>A folder or a file.</summary>
    public static SettingShape PathTo(string section, string title, string? group = null) => new(SettingKind.Path, section, title) { Group = group };

    /// <summary>A list of values.</summary>
    public static SettingShape ListOf(string section, string title, string? group = null) => new(SettingKind.List, section, title) { Group = group };

    /// <summary>The values a choice offers: its listed ones, then its source's names (those not listed already).</summary>
    public IReadOnlyList<string> AllChoices()
    {
        if (ChoicesFrom is null)
        {
            return Choices;
        }

        var all = new List<string>(Choices);
        if (SettingChoiceSources.Find(ChoicesFrom) is { } source)
        {
            foreach (var name in source.Choices)
            {
                if (!all.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    all.Add(name);
                }
            }
        }

        return all;
    }
}

/// <summary>A source of a choice setting's values (the registered themes, the keymaps), by name in <see cref="SettingChoiceSources"/>.</summary>
public interface ISettingChoices : IKind
{
    /// <summary>The values now.</summary>
    IReadOnlyList<string> Choices { get; }
}

/// <summary>A source of choices made of a delegate.</summary>
public sealed class SettingChoices(string name, Func<IReadOnlyList<string>> read, string summary) : ISettingChoices
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string Summary { get; } = summary;

    /// <inheritdoc />
    public IReadOnlyList<string> Choices => read();
}

/// <summary>
/// The sources of the settings' choices, by name (ignoring case). The library's defaults read its own registries: the
/// themes, the accents, the folder watchers, the memory monitors, the inline completion providers, the terminal hosts,
/// the keymaps, and the fonts it knows (the bundled ones; a platform registers its installed families over it). A
/// plug-in registers its own (the AI plug-in: the devices and the models).
/// </summary>
public static class SettingChoiceSources
{
    /// <summary>The registered themes.</summary>
    public const string Themes = "themes";

    /// <summary>The named accents.</summary>
    public const string Accents = "accents";

    /// <summary>The folder watchers.</summary>
    public const string FolderWatchers = "folderWatchers";

    /// <summary>The memory-pressure monitors.</summary>
    public const string MemoryMonitors = "memoryMonitors";

    /// <summary>The inline completion providers.</summary>
    public const string InlineCompletion = "inlineCompletion";

    /// <summary>The terminal hosts.</summary>
    public const string TerminalHosts = "terminalHosts";

    /// <summary>The terminal's profiles (B9: the shells found on the machine and the user's own), by name.</summary>
    public const string TerminalProfiles = "terminalProfiles";

    /// <summary>The keymaps.</summary>
    public const string KeyMaps = "keymaps";

    /// <summary>The font families known.</summary>
    public const string Fonts = "fonts";

    private static readonly KindTable<ISettingChoices> Table = MakeTable();

    /// <summary>Registers <paramref name="source"/> under its name (over a default of that name).</summary>
    public static void Register(ISettingChoices source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the registration of that name (a library default comes back); false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The source of that name, or null.</summary>
    public static ISettingChoices? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ISettingChoices Get(ReadOnlySpan<char> name) => Table.Get(name);

    private static KindTable<ISettingChoices> MakeTable()
    {
        var table = new KindTable<ISettingChoices>(nameof(SettingChoiceSources), "Setting choices", "add it with SettingChoiceSources.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a source only lists names");
        table.RegisterDefault(new SettingChoices(Themes, () => EditorThemes.Names, "the registered themes"));
        table.RegisterDefault(new SettingChoices(Accents, () => AccentColors.Names, "the named accents"));
        table.RegisterDefault(new SettingChoices(FolderWatchers, () => Craft.Abstractions.FolderWatchers.Names, "the folder watchers"));
        table.RegisterDefault(new SettingChoices(MemoryMonitors, () => MemoryPressureMonitors.Names, "the memory-pressure monitors"));
        table.RegisterDefault(new SettingChoices(InlineCompletion, () => InlineCompletionProviders.Names, "the inline completion providers"));
        table.RegisterDefault(new SettingChoices(TerminalHosts, () => Craft.Abstractions.TerminalHosts.Names, "the terminal hosts"));
        table.RegisterDefault(new SettingChoices(TerminalProfiles, () => Craft.Abstractions.TerminalProfiles.LastFound, "the terminal profiles found"));
        table.RegisterDefault(new SettingChoices(KeyMaps, () => EditorKeyMaps.Names, "the keymaps"));
        table.RegisterDefault(new SettingChoices(Fonts, () => ["JetBrains Mono", "Inter"], "the bundled font families"));
        return table;
    }
}

/// <summary>
/// Shapes for a plug-in's settings: its declared <see cref="PluginSetting.Shape"/> where it has one, else a shape read off
/// its default (true or false: a switch; a whole number: a number box from 0 when the default is not negative; else text),
/// in the section the plug-in names. So a plug-in places its settings in one line and shapes only the ones that need it.
/// </summary>
public static class PluginSettingShapes
{
    /// <summary>
    /// <paramref name="settings"/>, each with its shape: <paramref name="shape"/>'s when it gives one, else its own, else
    /// one read off its default, in the section <paramref name="section"/> gives it.
    /// </summary>
    public static IReadOnlyList<PluginSetting> Apply(IReadOnlyList<PluginSetting> settings, Func<PluginSetting, string> section, Func<PluginSetting, SettingShape?>? shape = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(section);
        var shaped = new PluginSetting[settings.Count];
        for (int i = 0; i < shaped.Length; i++)
        {
            var setting = settings[i];
            shaped[i] = setting with { Shape = shape?.Invoke(setting) ?? setting.Shape ?? Infer(setting, section(setting)) };
        }

        return shaped;
    }

    /// <summary>The shape read off <paramref name="setting"/>'s default, in <paramref name="section"/>.</summary>
    public static SettingShape Infer(PluginSetting setting, string section)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(section);
        var value = setting.Default.Trim();
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return new SettingShape(SettingKind.Toggle, section);
        }

        if (long.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long whole))
        {
            return new SettingShape(SettingKind.Whole, section) { Min = whole >= 0 ? 0 : null, Step = 1 };
        }

        return new SettingShape(SettingKind.Text, section);
    }
}
