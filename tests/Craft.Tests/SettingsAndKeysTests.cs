// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>A shell over a user settings file (and a workspace when asked), its dispatch run at once, for the settings and keys tests.</summary>
internal sealed class SettingsFixture : IDisposable
{
    public SettingsFixture(TempFolder folder, Func<string, string?>? environment = null)
    {
        Folder = folder;
        User = SettingsFile.Load(folder.File("data/settings.json"));
        Settings = new CraftSettings(environment ?? (_ => null), User);
        Host = new EditorHost(readVariable: Settings.Read);
        Shell = new ShellModel(Host, Settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), new NoUi());
    }

    public TempFolder Folder { get; }

    public SettingsFile User { get; }

    public CraftSettings Settings { get; }

    public EditorHost Host { get; }

    public ShellModel Shell { get; }

    public string? UserValue(string variable) => SettingsFile.Load(User.FilePath).Get(variable);

    public void Dispose()
    {
        Shell.Dispose();
        Host.Dispose();
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoUi : IShellUi
    {
        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}

/// <summary>The owner's note 13: the settings page made from the options' declared shapes, applied live, in the right file.</summary>
[TestGroup("Settings")]
public static class SettingsPageTests
{
    [Test]
    public static void EveryOptionDeclaresAShapeWhoseRangeTheParserTakes()
    {
        var sections = SettingSections.All.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var variable in CraftOptions.Variables)
        {
            var shape = variable.Shape;
            if (ReferenceEquals(shape, SettingShape.Unknown) || shape.Title is null || !sections.Contains(shape.Section))
            {
                problems.Add($"{variable.Name}: no declared shape with a title and a known section ({shape.Section})");
                continue;
            }

            if (shape.Kind is SettingKind.Whole or SettingKind.Number && shape.Min is { } min && shape.Max is { } max)
            {
                string Text(double value) => SettingsPageModel.FormatNumber(value);
                foreach (var (value, fits) in (ReadOnlySpan<(double, bool)>)[(min, true), (max, true), (min - 1, false), (max + 1, false)])
                {
                    if (value > int.MaxValue)
                    {
                        continue;
                    }

                    bool takes = CraftSettings.Validate(variable.Name, Text(value)) is null;
                    if (takes != fits)
                    {
                        problems.Add($"{variable.Name}: the declared range {Text(min)} to {Text(max)} says {Text(value)} {(fits ? "fits" : "does not")}, the parser {(takes ? "takes" : "refuses")} it");
                    }
                }
            }

            if (shape.Kind == SettingKind.Choice && shape.ChoicesFrom is null)
            {
                foreach (var choice in shape.Choices.Where(c => c.Length > 0))
                {
                    if (CraftSettings.Validate(variable.Name, choice) is { } refused)
                    {
                        problems.Add($"{variable.Name}: its choice '{choice}' is refused ({refused})");
                    }
                }
            }
        }

        Check.True(problems.Count == 0, "every option's shape is declared next to it and agrees with its parser:\n" + string.Join("\n", problems));
    }

    [Test]
    public static void ThePageMakesSectionsAndControlsFromTheMetadata()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var page = new SettingsPageModel(fixture.Shell);
        var sections = page.Sections().Select(s => s.Id).ToArray();
        foreach (var expected in (string[])[SettingSections.Editor, SettingSections.Appearance, SettingSections.Files, SettingSections.CSharp, SettingSections.Completion,
            SettingSections.Agent, SettingSections.Terminal, SettingSections.Keyboard])
        {
            Check.True(sections.Contains(expected), $"the left list has the section '{expected}' (has: {string.Join(", ", sections)})");
        }

        Check.True(sections.ToList().IndexOf(SettingSections.Editor) < sections.ToList().IndexOf(SettingSections.Keyboard), "in the page's order");
        var editor = page.Groups(SettingSections.Editor).SelectMany(g => g.Entries).ToArray();
        var tab = editor.Single(e => e.Variable == "CRAFT_TAB_SIZE");
        Check.True(tab is { Title: "Tab size", Shape.Kind: SettingKind.Whole, Shape.Unit: "columns", Value: "4", IsModified: false, IsReadOnly: false }, "Tab size: a number box in columns, its default, not modified");
        Check.True(editor.Single(e => e.Variable == "CRAFT_INSERT_SPACES").Shape.Kind == SettingKind.Toggle, "a boolean is a switch");
        Check.True(page.Groups(SettingSections.Editor).Any(g => g.Title == "Caret"), "the section's settings are grouped by their declared group");
        var appearance = page.Groups(SettingSections.Appearance).SelectMany(g => g.Entries).ToArray();
        var theme = appearance.Single(e => e.Variable == "CRAFT_THEME");
        Check.True(theme.Shape.Kind == SettingKind.Choice && theme.Choices.Contains("dark") && theme.Choices.Contains("light"), "the theme is a drop-down of the registered themes");
        var accent = appearance.Single(e => e.Variable == "CRAFT_ACCENT");
        Check.True(accent.Choices.Contains("teal") && accent.Shape.OtherAllowed, "the accent offers the named accents and takes a #RRGGBB");
        var font = appearance.Single(e => e.Variable == "CRAFT_FONT");
        Check.True(font.Shape.Kind == SettingKind.Font && font.Choices.Contains("JetBrains Mono"), "the code font is a font picker with the bundled family");
        var found = page.Groups(SettingSections.Editor, "blink").SelectMany(g => g.Entries).ToArray();
        Check.True(found.Length == 2 && found[0].Variable == "CRAFT_CARET_BLINK_MS" && found[1].Variable == "CRAFT_TERMINAL_CURSOR_BLINK",
            $"the search finds the settings by their words, across sections (the caret's blink, then the terminal cursor's, B9): {string.Join(", ", found.Select(f => f.Variable))}");
        Check.True(page.Groups(SettingSections.Editor, "terminal").Any(g => g.Title == "Terminal"), "the search runs across every section, grouped by section");
        Check.True(page.Groups(SettingSections.Editor, "CRAFT_TOAST_MS").SelectMany(g => g.Entries).Single().Variable == "CRAFT_TOAST_MS", "and by the variable");
        Check.Equal("8", SettingsPageModel.Step(tab with { Value = "8" }, 0), "a stepper keeps the value");
        Check.Equal("32", SettingsPageModel.Step(tab with { Value = "32" }, 1), "a stepper stays within the range");
    }

    [Test]
    public static async Task AChangeAppliesLiveAndLandsInTheChosenFile()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var shell = fixture.Shell;
        var workspace = folder.File("ws");
        Directory.CreateDirectory(workspace);
        Check.True(await shell.OpenFolderAsync(workspace), "the folder opens");
        var page = new SettingsPageModel(shell);
        Check.Equal(SettingLayer.Workspace, page.Scope, "with a folder open, a change goes to this workspace's file by default");
        int changed = 0;
        shell.OptionsChanged += (_, _) => changed++;

        Check.True(page.Set("CRAFT_TAB_SIZE", "2") is null && shell.Options.TabSize == 2 && changed == 1, "the change applies at once (the options reload)");
        var workspaceFile = SettingsFile.Load(CraftSettings.WorkspaceFilePath(workspace));
        Check.True(workspaceFile.Get("CRAFT_TAB_SIZE") == "2" && fixture.UserValue("CRAFT_TAB_SIZE") is null, "it landed in the workspace's file, not the user's");
        var tab = page.Entries().Single(e => e.Variable == "CRAFT_TAB_SIZE");
        Check.True(tab is { IsModified: true, Source: SettingSource.Workspace, CanReset: true, Value: "2" }, "marked modified, from the workspace, with a reset");

        Check.True(page.Set("CRAFT_TAB_SIZE", "99") is { } outOfRange && outOfRange.Contains("1 to 32", StringComparison.Ordinal), "a value out of the declared range is refused with the range");
        Check.True(page.Set("CRAFT_INSERT_SPACES", "sometimes") is { } notBoolean && notBoolean.Contains("true or false", StringComparison.Ordinal), "a switch takes true or false");
        Check.Equal("2", SettingsFile.Load(CraftSettings.WorkspaceFilePath(workspace)).Get("CRAFT_TAB_SIZE"), "and nothing was written");

        page.Scope = SettingLayer.User;
        int themes = 0;
        shell.ThemeChanged += (_, _) => themes++;
        Check.True(page.Set("CRAFT_THEME", "light") is null && !shell.Theme.IsDark && themes == 1, "the theme changes live");
        Check.Equal("light", fixture.UserValue("CRAFT_THEME"), "in the user's file when the user's scope is chosen");
        Check.True(page.Set("CRAFT_THEME", "no-such-theme") is { } notAChoice && notAChoice.Contains("dark", StringComparison.Ordinal), "a drop-down's value must be one of its choices");

        Check.True(page.ResetSection(SettingSections.Appearance) is null && shell.Theme.IsDark && fixture.UserValue("CRAFT_THEME") is null, "the section's reset takes its values out of the user's file");
        page.Scope = SettingLayer.Workspace;
        Check.True(page.Reset("CRAFT_TAB_SIZE") is null && shell.Options.TabSize == 4, "a setting's reset brings the default back");
        Check.True(page.Entries().Single(e => e.Variable == "CRAFT_TAB_SIZE") is { IsModified: false, CanReset: false }, "no longer modified");
    }

    [Test]
    public static void EnvironmentOverridesAreReadOnlyAndSaySo()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder, name => name == "CRAFT_TAB_SIZE" ? "3" : null);
        var page = new SettingsPageModel(fixture.Shell);
        var tab = page.Entries().Single(e => e.Variable == "CRAFT_TAB_SIZE");
        Check.True(tab is { IsReadOnly: true, Source: SettingSource.Environment, Value: "3", IsModified: true }, "the environment's value shows read-only");
        Check.True(tab.ReadOnlyReason!.Contains("environment", StringComparison.Ordinal) && tab.ReadOnlyReason.Contains("CRAFT_TAB_SIZE", StringComparison.Ordinal), "with why");
        Check.True(page.Set("CRAFT_TAB_SIZE", "8") is { } refused && refused.Contains("environment", StringComparison.Ordinal) && fixture.UserValue("CRAFT_TAB_SIZE") is null, "a change is refused, nothing written");
        Check.True(page.Entries().Single(e => e.Variable == "CRAFT_DATA_FOLDER").IsReadOnly, "a variable read from the environment only is read-only in the user's file");
    }

    [Test]
    public static void APluginsSettingsSitUnderThePluginInTheSectionItNames()
    {
        var plugin = new ShapedPlugin();
        EditorPlugins.Register(plugin);
        try
        {
            using var folder = new TempFolder();
            using var fixture = new SettingsFixture(folder);
            var page = new SettingsPageModel(fixture.Shell);
            var group = page.Groups(SettingSections.Completion).SingleOrDefault(g => g.Plugin == "settings-kit");
            Check.True(group is { Title: "settings-kit" } && group.Entries.Count == 2, "the plug-in's settings are grouped under it in the section it names");
            var flag = group!.Entries.Single(e => e.Variable == "CRAFT_PLUGIN_SETTINGS_KIT_SHOWHINTS");
            Check.True(flag is { Title: "Show hints", Shape.Kind: SettingKind.Toggle }, "a setting with no declared shape is read off its default (a switch), titled from its key");
            Check.True(group.Entries.Single(e => e.Variable == "CRAFT_PLUGIN_SETTINGS_KIT_RATE").Shape is { Kind: SettingKind.Number, Max: 2 }, "a declared shape is kept");
            Check.True(page.Set(flag.Variable, "maybe") is not null, "a plug-in's value is checked against its shape");
            Check.True(page.Set(flag.Variable, "false") is null && fixture.Host.Settings.Get("settings-kit", "showHints") == "false", "and applied: the plug-in reads it at once");
            Check.True(page.Set("CRAFT_PLUGIN_SETTINGS_KIT_RATE", "5") is { } rate && rate.Contains("0 to 2", StringComparison.Ordinal), "a declared range holds for a plug-in too");
        }
        finally
        {
            EditorPlugins.Unregister("settings-kit");
        }
    }

    private sealed class ShapedPlugin : IEditorPlugin
    {
        public string Name => "settings-kit";

        public string Summary => "test: shaped settings";

        public IReadOnlyList<PluginSetting> Settings { get; } = PluginSettingShapes.Apply(
            [new("showHints", "true", "Whether hints show."), new("rate", "1", "A rate.") { Shape = SettingShape.Number(SettingSections.Completion, "Rate", 0, 2, 0.1) }],
            static _ => SettingSections.Completion);

        public IDisposable Activate(IEditorHost host) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

/// <summary>The owner's note 16: the shortcuts registered, routed, recorded and remapped, and the editor commands they run.</summary>
[TestGroup("Keys")]
public static class KeysTests
{
    [Test]
    public static void ChordsParseAndShowLikeTheEditorsTheyComeFrom()
    {
        Check.True(KeyChord.Parse("Ctrl+/") is { Key: EditorKey.Slash, Modifiers: KeyModifiers.Control }, "Ctrl+/ names the slash key");
        Check.Equal("Ctrl+/", KeyChord.Parse("ctrl+slash").DisplayText, "shown with its character");
        Check.Equal("Ctrl+Slash", KeyChord.Parse("Ctrl+/").ToString(), "written by its name (the registry's key stays stable)");
        Check.Equal(EditorKey.Plus, KeyChord.Parse("Ctrl+=").Key, "Ctrl+= is the equals-and-plus key");
        Check.Equal(EditorKey.Plus, KeyChord.Parse("Ctrl++").Key, "and so is Ctrl++");
        Check.Equal("Ctrl+1", KeyChord.Parse("Ctrl+D1").DisplayText, "a digit shows as itself");
        var (first, then) = KeyBinding.ParseKeys("Ctrl+K  Ctrl+C");
        Check.True(first == KeyChord.Parse("Ctrl+K") && then == KeyChord.Parse("Ctrl+C"), "two chords make a two-key chord");
        Check.Throws<FormatException>(() => KeyBinding.ParseKeys("Ctrl+K Ctrl+C Ctrl+X"), "three chords are refused");
        var list = KeyBinding.ParseList("Ctrl+==craft.zoomIn; Ctrl+K Ctrl+C=craft.editor.comment;Ctrl+K S=craft.saveAll;Ctrl+L=;Ctrl+D=!dup");
        Check.True(list is [{ Chord.Key: EditorKey.Plus, CommandId: "craft.zoomIn" }, { Name: "Ctrl+K Ctrl+C", CommandId: "craft.editor.comment" }, { Name: "Ctrl+K S" }, { Name: "Ctrl+L", CommandId: "" }, { OverridesEditor: true }],
            "a list with the = key, two-key chords, an unbinding and an override");
        Check.Equal("Ctrl+Plus=craft.zoomIn;Ctrl+K Ctrl+C=craft.editor.comment;Ctrl+K S=craft.saveAll;Ctrl+L=;Ctrl+D=!dup", KeyBinding.FormatList(list), "written back");
        Check.SequenceEqual(list, KeyBinding.ParseList(KeyBinding.FormatList(list)), "and read again the same");
    }

    [Test]
    public static void TheStandardShortcutsRegisterWithoutUnintendedConflicts()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        using var edit = new ShellEditCommands(() => null, fixture.Shell);
        var problems = new List<string>();
        foreach (var binding in StandardShortcuts.Bindings)
        {
            var found = binding.Then is { } then ? EditorKeyBindings.Find(binding.Chord, then) : EditorKeyBindings.Find(binding.Chord);
            if (!ReferenceEquals(found, binding))
            {
                problems.Add($"{binding.Name} runs {found?.CommandId ?? "nothing"}, not {binding.CommandId} (another registration took it)");
            }

            // The command palette is the app page's command (ShellPage), not registered headless.
            if (EditorCommands.Find(binding.CommandId) is null && binding.CommandId != ShellCommandIds.CommandPalette)
            {
                problems.Add($"{binding.Name}: its command {binding.CommandId} is not registered");
            }
        }

        foreach (var (keys, command) in (ReadOnlySpan<(string, string)>)[("Ctrl+G", ShellCommandIds.GoToLine), ("Ctrl+K S", ShellCommandIds.SaveAll), ("Ctrl+Shift+T", ShellCommandIds.ReopenClosedTab),
            ("Ctrl+=", ShellCommandIds.ZoomIn), ("Ctrl+-", ShellCommandIds.ZoomOut), ("Ctrl+0", ShellCommandIds.ZoomReset), ("Ctrl+K Ctrl+S", ShellCommandIds.KeyboardShortcuts),
            ("Ctrl+W", ShellCommandIds.CloseTab), ("Ctrl+P", ShellCommandIds.QuickOpen), ("Ctrl+B", ShellCommandIds.ToggleTree), ("Ctrl+J", ShellCommandIds.TogglePanel)])
        {
            var (chord, then) = KeyBinding.ParseKeys(keys);
            var found = then is { } second ? EditorKeyBindings.Find(chord, second) : EditorKeyBindings.Find(chord);
            if (found?.CommandId != command)
            {
                problems.Add($"{keys} runs {found?.CommandId ?? "nothing"}, not {command}");
            }
        }

        foreach (var binding in EditorKeyBindings.All.Where(b => b.Then is null && b.CommandId.Length > 0))
        {
            if (EditorKeyBindings.IsPrefix(binding.Chord))
            {
                problems.Add($"{binding.Name} ({binding.CommandId}) is also the first key of a two-key chord, so it never runs alone");
            }
        }

        var keyMap = StandardShortcuts.VisualStudioKeyMap.Bindings;
        foreach (var twice in keyMap.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"the Visual Studio keymap binds {twice.Key} twice");
        }

        var conflicts = new KeyboardModel(fixture.Shell).Rows().Where(r => r.HasConflict).Select(r => $"{r.Title}: {string.Join(", ", r.Keys.Where(k => k.Conflict is not null).Select(k => $"{k.Text} ({k.Conflict})"))}").ToArray();
        problems.AddRange(conflicts);
        Check.True(problems.Count == 0, "the shortcuts register as declared, with no conflict:\n" + string.Join("\n", problems));
        Check.True(EditorKeyMaps.Find(StandardShortcuts.VisualStudio) is not null, "the Visual Studio keymap is registered");
    }

    [Test]
    public static async Task TwoKeyChordsTheKeymapAndTheTextEditorContextRouteKeys()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var (model, document, _) = Editors.MakeMarked("int a;|\nint b;", language: new QuoteLanguage());
        using var edit = new ShellEditCommands(() => model, fixture.Shell);
        var host = fixture.Host;

        Check.True(host.HandleKey(EditorKey.K, KeyModifiers.Control, model) && host.PendingChord == KeyChord.Parse("Ctrl+K"), "Ctrl+K waits for the second key");
        Check.True(host.HandleKey(EditorKey.C, KeyModifiers.Control, model) && host.PendingChord is null, "Ctrl+C ends the wait");
        await Task.Yield();
        Check.Equal("// int a;\nint b;", document.Text, "Ctrl+K Ctrl+C commented the line");
        host.HandleKey(EditorKey.Slash, KeyModifiers.Control, model);
        await Task.Yield();
        Check.Equal("int a;\nint b;", document.Text, "Ctrl+/ toggled the comment off");
        host.HandleKey(EditorKey.Down, KeyModifiers.Alt, model);
        await Task.Yield();
        Check.Equal("int b;\nint a;", document.Text, "Alt+Down moved the line (before the editor's own Alt+arrow)");

        var terminal = new DecliningSink();
        Check.True(!host.HandleKey(EditorKey.K, KeyModifiers.Control, terminal) && host.PendingChord is null && terminal.Keys == 1, "a terminal's Ctrl+K stays the shell's (no wait)");
        Check.True(!host.HandleKey(EditorKey.Down, KeyModifiers.Alt, terminal) && terminal.Keys == 2, "and a text-editor binding does not run there");
        Check.True(host.HandleKey(EditorKey.K, KeyModifiers.Control, model) && host.HandleKey(EditorKey.Q, KeyModifiers.Control, model) && document.Text == "int b;\nint a;",
            "a second key no layer binds ends the wait and does nothing");

        Check.True(fixture.Shell.ApplySetting(SettingLayer.User, "CRAFT_KEYMAP", StandardShortcuts.VisualStudio) is null && host.KeyMap?.Name == StandardShortcuts.VisualStudio, "the keymap applies at once");
        host.HandleKey(EditorKey.D, KeyModifiers.Control, model);
        await Task.Yield();
        Check.Equal("int b;\nint a;\nint a;", document.Text, "Visual Studio's Ctrl+D duplicated the line");
        host.HandleKey(EditorKey.L, KeyModifiers.Control | KeyModifiers.Shift, model);
        await Task.Yield();
        Check.Equal("int b;\nint a;", document.Text, "and its Ctrl+Shift+L deleted it");
        Check.True(fixture.Shell.ApplySetting(SettingLayer.User, "CRAFT_KEYMAP", "no-such-keymap") is null && host.KeyMap is null && host.KeyMapProblem!.Contains("not registered", StringComparison.Ordinal),
            "a keymap not registered is said so, and the registered bindings apply alone");
    }

    [Test]
    public static async Task RecordingRemappingRemovingAndResettingRoundTripThroughSettings()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var (model, document, _) = Editors.MakeMarked("one\ntw|o\nthree");
        using var edit = new ShellEditCommands(() => model, fixture.Shell);
        var keys = new KeyboardModel(fixture.Shell);
        var host = fixture.Host;

        Check.True(!keys.Recorder.Press(KeyChord.Parse("Escape")) && !keys.Recorder.Press(KeyChord.Parse("Q")), "Escape or a plain letter does not start a shortcut");
        Check.True(keys.Recorder.Press(KeyChord.Parse("Ctrl+K")) && keys.Recorder.Press(KeyChord.Parse("Ctrl+L")) && keys.Recorder.Text == "Ctrl+K Ctrl+L", "two chords pressed are recorded");
        Check.True(keys.AddRecorded(StandardShortcuts.SelectLine) is null, "the recorded keys are given to Select the line");
        Check.Equal("Ctrl+K Ctrl+L=craft.editor.selectLine", fixture.UserValue("CRAFT_KEY_BINDINGS"), "saved in the user's settings");
        host.HandleKey(EditorKey.K, KeyModifiers.Control, model);
        host.HandleKey(EditorKey.L, KeyModifiers.Control, model);
        await Task.Yield();
        Check.True(model.Selection is { Start: 4, End: 8 }, "the new keys run it at once (no restart)");
        var row = keys.Rows(StandardShortcuts.SelectLine).Single(r => r.CommandId == StandardShortcuts.SelectLine);
        Check.True(row.Modified && row.Keys.Any(k => k is { Text: "Ctrl+L", Source: ShortcutSource.Default }) && row.Keys.Any(k => k is { Text: "Ctrl+K Ctrl+L", Source: ShortcutSource.User }),
            "the row shows the default and the user's keys, modified");
        Check.True(keys.Rows(null, KeyChord.Parse("Ctrl+K"), KeyChord.Parse("Ctrl+L")).Select(r => r.CommandId).SequenceEqual([StandardShortcuts.SelectLine]), "searching by the keys pressed finds it");

        Check.True(keys.Remove(StandardShortcuts.SelectLine, KeyChord.Parse("Ctrl+L")) is null, "the default Ctrl+L is removed");
        Check.Equal("Ctrl+K Ctrl+L=craft.editor.selectLine;Ctrl+L=", fixture.UserValue("CRAFT_KEY_BINDINGS"), "as an unbinding in the user's settings");
        model.Select(0, 0);
        host.HandleKey(EditorKey.L, KeyModifiers.Control, model);
        await Task.Yield();
        Check.True(model.Selection.IsEmpty, "Ctrl+L no longer selects the line");
        Check.True(keys.Remove("craft.copy", KeyChord.Parse("Ctrl+C")) is { } editorKey && editorKey.Contains("editor's own", StringComparison.Ordinal), "the editor's own key cannot be removed, said so");

        Check.True(keys.ConflictOf(StandardShortcuts.DeleteLines, KeyChord.Parse("Ctrl+/"), null) is { } conflict && conflict.Contains("Toggle line comment", StringComparison.Ordinal), "giving taken keys names what they run now");
        Check.True(keys.Add(StandardShortcuts.DeleteLines, KeyChord.Parse("Ctrl+/")) is null, "remapped");
        var remapped = keys.Rows("Delete lines").Single(r => r.CommandId == StandardShortcuts.DeleteLines);
        Check.True(remapped.Keys.Single(k => k.Text == "Ctrl+/").Conflict!.Contains("Toggle line comment", StringComparison.Ordinal), "and shown over the command it took the keys from");
        Check.True(keys.Rows("Toggle line comment").Single(r => r.CommandId == StandardShortcuts.ToggleComment).Keys.All(k => k.Text != "Ctrl+/"), "which no longer lists them");

        var reread = new CraftSettings(_ => null, SettingsFile.Load(fixture.User.FilePath)).BuildOptions(out var problems);
        Check.True(problems.Count == 0 && reread.KeyBindings.SequenceEqual(fixture.Shell.Options.KeyBindings), "the user's bindings read back from the file the same");

        Check.True(keys.Reset(StandardShortcuts.DeleteLines) is null && keys.Reset(StandardShortcuts.SelectLine) is null, "each command's keys reset");
        Check.True(fixture.UserValue("CRAFT_KEY_BINDINGS") is null && fixture.Shell.Options.KeyBindings.Count == 0, "nothing of the user's is left");
        model.Select(0, 0);
        host.HandleKey(EditorKey.L, KeyModifiers.Control, model);
        await Task.Yield();
        Check.True(model.Selection is { Start: 0, End: 4 }, "and the default Ctrl+L selects the line again");
        Check.True(keys.Add("craft.save", KeyChord.Parse("F2")) is null && keys.ResetAll() is null && fixture.UserValue("CRAFT_KEY_BINDINGS") is null, "Reset all takes every binding back");
        _ = document;
    }

    [Test]
    public static void EachEditorCommandDoesWhatItSays()
    {
        var language = new QuoteLanguage();
        string Run(string marked, Func<EditorModel, bool> command, out EditorModel model)
        {
            var (m, document, _) = Editors.MakeMarked(marked, language: language);
            command(m);
            model = m;
            return Editors.Marked(m, document);
        }

        Check.Equal("[  // a\n\n  // b]\nc", Run("[  a\n\n  b]\nc", m => m.ToggleLineComment(), out _), "toggle comments every non-blank line at the least indent, blank lines left");
        Check.Equal("  a|\nb", Run("  // a|\nb", m => m.ToggleLineComment(), out _), "and takes the comment (and its space) off when every line has one");
        Check.Equal("// // x|", Run("// x|", m => m.CommentLines(), out _), "Comment adds one even on a commented line");
        Check.Equal("x|", Run("// x|", m => m.UncommentLines(), out _), "Uncomment takes it off");
        Check.Equal("one\ntwo\ntw|o\nthree", Run("one\ntw|o\nthree", m => m.DuplicateLines(), out _), "duplicate copies the line below, the caret on the copy");
        Check.Equal("one\ntw|o\ntwo\nthree", Run("one\ntw|o\nthree", m => m.CopyLines(LineDirection.Up), out _), "copy up leaves the caret on the upper copy");
        Check.Equal("one\n|three", Run("one\ntw|o\nthree", m => m.DeleteLines(), out _), "delete line takes the line and its break");
        Check.Equal("one\n|two", Run("one\ntwo\nthr|ee", m => m.DeleteLines(), out _), "the last line takes the break before it");
        Check.Equal("tw|o\none\nthree", Run("one\ntw|o\nthree", m => m.MoveLines(LineDirection.Up), out _), "move up swaps with the line above, the caret goes with it");
        Check.Equal("one\nthree\ntw|o", Run("one\ntw|o\nthree", m => m.MoveLines(LineDirection.Down), out _), "move down");
        Check.Equal("o|ne\ntwo", Run("o|ne\ntwo", m => m.MoveLines(LineDirection.Up), out _), "the first line does not move up");
        Check.Equal("three\n[one\ntwo]", Run("[one\ntwo]\nthree", m => m.MoveLines(LineDirection.Down), out _), "a selection's lines move together");
        Check.Equal("    a|", Run("a|", m => m.IndentLines(), out _), "indent lines adds a level");
        Check.Equal("a|", Run("    a|", m => m.OutdentLines(), out _), "outdent takes it back");
        Check.Equal("one\n[two\n]three", Run("one\nt|wo\nthree", m => m.SelectLine(), out var selected), "select line takes the line with its break");
        selected.SelectLine();
        Check.True(selected.Selection is { Start: 4, End: 13 }, "again: the next line too");
        Check.Equal("f([abc], 1)", Run("f(a|bc, 1)", m => m.ExpandSelection(), out var expanding), "expand: the word");
        expanding.ExpandSelection();
        Check.True(expanding.Selection is { Start: 2, End: 8 }, "then the inside of the brackets");
        expanding.ExpandSelection();
        Check.True(expanding.Selection is { Start: 1, End: 9 }, "then the brackets too");
        expanding.ShrinkSelection();
        Check.True(expanding.Selection is { Start: 2, End: 8 }, "shrink walks back");
        Check.Equal("one\ntw|o", Run("|one\ntwo", m => m.GoToLine(2, 3), out _), "go to line and column");
        Check.Equal("one\ntwo|", Run("|one\ntwo", m => m.GoToLine(99, 99), out _), "kept within the text");

        var (model, document, _) = Editors.MakeMarked("one\ntw|o", language: language);
        model.MoveLines(LineDirection.Up);
        model.ToggleLineComment();
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("two\none", document.Text, "each command is one undo step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("one\ntwo", document.Text, "and the move is another");
        Check.True(!Editors.Make("x").Model.ToggleLineComment(), "plain text has no line comment: the command declines");
    }

    [Test]
    public static async Task QuickOpenGoesToALineAndTheShellCommandsWork()
    {
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 5);
        Check.True(quick.Query(":12:3") is [{ Line: 12, Column: 3, Text: "Go to line 12, column 3" }], "':12:3' is a line and a column");
        Check.True(quick.Query(" :7") is [{ Line: 7, Column: 1 }] && QuickOpen.IsLineQuery(" :7"), "':7' a line");
        Check.True(quick.Query(":x").Count == 0 && quick.Query(":0").Count == 0, "not a line number: nothing");

        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var shell = fixture.Shell;
        Check.True(await shell.ZoomAsync(2) is null && Math.Abs(shell.Options.FontSize - 15) < 0.01, "zoom in twice: the font two steps larger, at once");
        Check.Equal("15", fixture.UserValue("CRAFT_FONT_SIZE"), "written to the user's settings");
        Check.True(await shell.ZoomAsync(0) is null && Math.Abs(shell.Options.FontSize - 13) < 0.01 && fixture.UserValue("CRAFT_FONT_SIZE") is null, "Ctrl+0 takes it back");

        var path = folder.File("a.txt");
        await File.WriteAllTextAsync(path, "a");
        Check.True(await shell.OpenFileAsync(path) is { } tab && await shell.CloseTabAsync(tab) && shell.ClosedFiles.Contains(path), "a closed tab's file is remembered");
        Check.True(await shell.ReopenClosedTabAsync() && shell.Tabs.Find(path) is not null, "and reopened");
        shell.ShowSettings(SettingSections.Keyboard);
        Check.True(shell.SettingsSection == SettingSections.Keyboard && shell.Pages.Active?.Id == ShellPageIds.Settings, "Ctrl+K Ctrl+S opens the settings at Keyboard");
    }

    [Test]
    public static async Task TheComposerSendsOnEnterOnlyThroughItsRemappableBindings()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        using var edit = new ShellEditCommands(() => null, fixture.Shell);
        var host = fixture.Host;
        int sent = 0, lines = 0, left = 0;
        var composer = new ComposerInput(() => sent++, () => lines++, () => left++);

        Check.True(host.HandleKey(EditorKey.Enter, KeyModifiers.None, composer), "Enter in the composer is taken (the box adds no line)");
        await Task.Yield();
        Check.Equal(1, sent, "and sends");
        Check.True(host.HandleKey(EditorKey.Enter, KeyModifiers.Shift, composer), "Shift+Enter is taken");
        await Task.Yield();
        Check.True(lines == 1 && sent == 1, "and makes a new line, without sending");
        Check.True(host.HandleKey(EditorKey.Escape, KeyModifiers.None, composer), "Escape is taken");
        await Task.Yield();
        Check.Equal(1, left, "and leaves the composer");
        Check.True(!host.HandleKey(EditorKey.A, KeyModifiers.Control, composer), "a key no binding takes stays the box's (select all)");

        var (model, document, _) = Editors.MakeMarked("ab|");
        Check.True(host.HandleKey(EditorKey.Enter, KeyModifiers.None, model) && document.Text == "ab\n" && sent == 1, "Enter in the text editor is the editor's (a line), never a send");
        Check.True(!host.HandleKey(EditorKey.Enter, KeyModifiers.None, null) && sent == 1, "nor anywhere else");

        var keys = new KeyboardModel(fixture.Shell);
        Check.True(keys.Rows("Send the message").Single().Keys.Single() is { Text: "Enter", Source: ShortcutSource.Default }, "the Keyboard page lists Enter for Send");
        Check.True(keys.Remove(ComposerCommandIds.Send, KeyChord.Parse("Enter")) is null && keys.Add(ComposerCommandIds.Send, KeyChord.Parse("Ctrl+Enter")) is null, "remapped to Ctrl+Enter");
        Check.True(!host.HandleKey(EditorKey.Enter, KeyModifiers.None, composer) && sent == 1, "Enter now stays the box's (a new line)");
        Check.True(host.HandleKey(EditorKey.Enter, KeyModifiers.Control, composer), "Ctrl+Enter is taken");
        await Task.Yield();
        Check.Equal(2, sent, "and sends");
        Check.True(!host.HandleKey(EditorKey.Enter, KeyModifiers.Control, model) || sent == 2, "the user's Ctrl+Enter answers in the composer only");
        await Task.Yield();
        Check.Equal(2, sent, "not from the text editor");
    }

    private sealed class DecliningSink : IEditorInputSink
    {
        public int Keys { get; private set; }

        public bool OnKey(EditorKey key, KeyModifiers modifiers)
        {
            Keys++;
            return false;
        }

        public void OnText(string text)
        {
        }
    }
}
