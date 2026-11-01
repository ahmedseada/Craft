// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>X part 9, item 24: the theme picker's live preview, Enter and Escape; the new themes' contrast; a plug-in's theme listed; "system".</summary>
[TestGroup("ThemePicker")]
public static class ThemePickerTests
{
    [Test]
    public static async Task ThePickerPreviewsEachThemeLiveKeepsItOnEnterAndBringsTheOneBeforeBackOnEscape()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = fixture.Shell;
        int walks = 0;
        shell.ThemeChanged += (_, _) => walks++;
        Check.Equal(EditorThemes.Dark, shell.Theme.Name, "the default theme");
        var (chord, then) = KeyBinding.ParseKeys("Ctrl+K Ctrl+T");
        Check.Equal(LookCommands.SelectTheme, EditorKeyBindings.Find(chord, then!.Value)?.CommandId, "Ctrl+K Ctrl+T is the theme picker");
        Check.True(EditorCommands.Find(LookCommands.SelectTheme)?.Title == "Preferences: Color theme", "the palette's Preferences: Color theme");

        var seen = new List<string>();
        fixture.Ui.QuickPicks = request =>
        {
            Check.Equal(IndexOf(request, EditorThemes.Dark), request.Selected, "the current theme is selected first");
            Check.Equal("current", request.Items[request.Selected].Mark, "and marked");
            request.Moved!(IndexOf(request, EditorThemes.Dusk));
            seen.Add(shell.Theme.Name);
            request.Moved(IndexOf(request, EditorThemes.HighContrastDark));
            seen.Add(shell.Theme.Name);
            return -1;                                              // Escape
        };
        Check.True(await fixture.Host.ExecuteCommandAsync(LookCommands.SelectTheme), "the command runs");
        Check.SequenceEqual([EditorThemes.Dusk, EditorThemes.HighContrastDark], seen.ToArray(), "each row moved to is shown at once (the window re-themed live)");
        Check.Equal(EditorThemes.Dark, shell.Theme.Name, "Escape brings the theme before back");
        Check.Equal(3, walks, "one walk of the window per preview and one for the way back, nothing more");
        Check.Equal("dark", shell.Options.Theme, "nothing was written");

        walks = 0;
        fixture.Ui.QuickPicks = request =>
        {
            request.Moved!(IndexOf(request, EditorThemes.Dawn));
            return IndexOf(request, EditorThemes.Dawn);             // Enter
        };
        Check.Equal(EditorThemes.Dawn, await shell.PickThemeAsync(), "Enter keeps the theme");
        Check.True(shell.Theme.Name == EditorThemes.Dawn && !shell.Theme.IsDark, "the window stays in it");
        Check.Equal(EditorThemes.Dawn, shell.Options.Theme, "CRAFT_THEME written to the user's settings");
        Check.Equal(1, walks, "the kept theme read back from the settings walks the window no second time");
    }

    [Test]
    public static void TheHighContrastPairReadsAtSevenToOneForTextCodeAndFocusAndTheNewPairAtFourAndAHalf()
    {
        var classes = typeof(TokenClasses).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Check.True(classes.Length > 20, "the token classes are read");
        foreach (var (name, ratio) in (ReadOnlySpan<(string, double)>)[(EditorThemes.HighContrastDark, 7.0), (EditorThemes.HighContrastLight, 7.0), (EditorThemes.Dusk, 4.5), (EditorThemes.Dawn, 4.5)])
        {
            var theme = EditorThemes.Get(name);
            var c = theme.Colors;
            bool enhanced = ratio >= ColorContrast.Enhanced;
            uint[] grounds = enhanced
                ? [c.Ground, c.Surface, c.Raised, c.RaisedHigh, c.InterfaceSelection, c.CurrentLine, c.Selection]
                : [c.Ground, c.Surface, c.Raised, c.RaisedHigh, c.InterfaceSelection, c.CurrentLine];
            foreach (var (role, color) in (ReadOnlySpan<(string, uint)>)[("text", c.Text), ("secondary", c.SecondaryText), ("accent", c.Accent), ("success", c.Success),
                ("warning", c.Warning), ("error", c.Error), ("info", c.Info)])
            {
                double min = ColorContrast.MinRatio(color, grounds);
                Check.True(min >= ratio, $"{name}: {role} reads at {min:0.00}:1 on its grounds (needs {ratio}:1)");
            }

            uint[] code = enhanced ? [c.Background, c.CurrentLine, c.Selection] : [c.Background, c.CurrentLine];
            foreach (var tokenClass in classes)
            {
                double min = ColorContrast.MinRatio(theme.StyleOf(tokenClass).Argb, code);
                Check.True(min >= ratio, $"{name}: the code class {tokenClass} reads at {min:0.00}:1 (needs {ratio}:1)");
            }

            Check.True(ColorContrast.Ratio(c.AccentInk, c.Accent) >= ratio, $"{name}: the accent's ink on the accent at {ColorContrast.Ratio(c.AccentInk, c.Accent):0.00}:1");
            Check.True(ColorContrast.Ratio(c.Caret, c.Background) >= ratio, $"{name}: the caret (the focus in the text) at {ColorContrast.Ratio(c.Caret, c.Background):0.00}:1");
            Check.True(ColorContrast.MinRatio(c.GutterText, [c.Background, c.Gutter, c.CurrentLine]) >= ratio, $"{name}: the line numbers on the editor");
            Check.True(theme.IsDark == (name is EditorThemes.HighContrastDark or EditorThemes.Dusk), $"{name}: its darkness");
        }

        // The focus ring of the window is the accent: at the enhanced ratio on every ground of the high-contrast pair.
        foreach (var name in (ReadOnlySpan<string>)[EditorThemes.HighContrastDark, EditorThemes.HighContrastLight])
        {
            var c = EditorThemes.Get(name).Colors;
            Check.True(ColorContrast.MinRatio(c.Accent, [c.Ground, c.Surface, c.Raised, c.RaisedHigh, c.InterfaceSelection]) >= ColorContrast.Enhanced, $"{name}: the focus (the accent) at 7:1");
        }
    }

    [Test]
    public static async Task APluginsThemeIsListedWithItsOriginAndSystemFollowsTheSystemsLightOrDark()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = fixture.Shell;
        var plugin = new TableTheme("test-plugin-theme", "A plug-in's theme", isDark: true, EditorThemes.Get(EditorThemes.Dark).Colors, new Dictionary<string, TextStyle>());
        EditorThemes.Register(plugin);
        try
        {
            var choices = shell.ThemeChoices();
            Check.Equal(EditorThemes.System, choices[0].Name, "system first");
            foreach (var name in (ReadOnlySpan<string>)[EditorThemes.Dark, EditorThemes.Light, EditorThemes.Dusk, EditorThemes.Dawn, EditorThemes.HighContrastDark, EditorThemes.HighContrastLight])
            {
                Check.True(choices.Any(c => c.Name == name && c.Item.Mark is null or "current"), $"the built-in {name} is listed, unmarked");
            }

            var row = choices.Single(c => c.Name == "test-plugin-theme");
            Check.True(row.Item.Mark is { } mark && mark.StartsWith("from ", StringComparison.Ordinal) && mark.Contains("Craft.Tests", StringComparison.Ordinal), $"a plug-in's theme says where it comes from ({row.Item.Mark})");
            Check.Equal("A plug-in's theme", row.Item.Detail, "with its summary");
            fixture.Ui.QuickPicks = request => IndexOf(request, "test-plugin-theme");
            Check.Equal("test-plugin-theme", await shell.PickThemeAsync(), "a plug-in's theme is picked like any");
            Check.Equal("test-plugin-theme", shell.Theme.Name, "and shown");

            Check.True(shell.ApplySetting(SettingLayer.User, "CRAFT_THEME", EditorThemes.System) is null, "system is a theme setting's value");
            Check.True(shell.SystemPrefersDark && shell.Theme.Name == EditorThemes.Dark, "system: dark while the system is dark");
            shell.SetSystemTheme(dark: false);
            Check.Equal(EditorThemes.Light, shell.Theme.Name, "the system went light: the theme follows at once");
            shell.SetSystemTheme(dark: true);
            Check.Equal(EditorThemes.Dark, shell.Theme.Name, "and back");
            Check.True(choices[0].Item.Detail!.Contains("dark", StringComparison.Ordinal), "the system's row says what the system is now");

            // W's icon themes go through the same quick pick.
            fixture.Ui.QuickPicks = request => request.Items.Count - 1;
            Check.True(await shell.PickIconThemeAsync() is { } icons && FileIconThemes.Find(icons) is not null, "the icon theme picker keeps a registered icon theme");
        }
        finally
        {
            EditorThemes.Unregister("test-plugin-theme");
        }
    }

    private static int IndexOf(QuickPickRequest request, string text)
    {
        for (int i = 0; i < request.Items.Count; i++)
        {
            if (request.Items[i].Text == text)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"'{text}' is not in the pick '{request.Title}'.");
    }
}

/// <summary>X part 9, item 25: Zen mode (Ctrl+K Z, Escape twice) and full screen (F11).</summary>
[TestGroup("ZenMode")]
public static class ZenModeTests
{
    [Test]
    public static async Task ZenTakesFullScreenAndLeavesOnEscapeTwiceGivingItBack()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_ZEN_WIDTH" ? "720" : null);
        var shell = fixture.Shell;
        var host = fixture.Host;
        int changes = 0;
        shell.WindowModeChanged += (_, _) => changes++;
        Check.Equal(720, shell.Options.ZenWidth, "CRAFT_ZEN_WIDTH is read");
        var (chord, then) = KeyBinding.ParseKeys("Ctrl+K Z");
        Check.Equal(LookCommands.ToggleZenMode, EditorKeyBindings.Find(chord, then!.Value)?.CommandId, "Ctrl+K Z is Zen mode");
        Check.Equal(LookCommands.ToggleFullScreen, EditorKeyBindings.Find(new KeyChord(EditorKey.F11, KeyModifiers.None))?.CommandId, "F11 is full screen");

        Check.True(await host.ExecuteCommandAsync(LookCommands.ToggleZenMode), "Zen mode runs");
        Check.True(shell.IsZen && shell.IsFullScreen && changes == 1, "Zen is on and the window full screen (CRAFT_ZEN_FULL_SCREEN), one change");
        host.HandleKey(EditorKey.Escape, KeyModifiers.None, null);
        Check.True(shell.IsZen, "one Escape keeps Zen (it closes what Escape closes)");
        host.HandleKey(EditorKey.A, KeyModifiers.None, null);
        host.HandleKey(EditorKey.Escape, KeyModifiers.None, null);
        Check.True(shell.IsZen, "a key between starts the count again");
        host.HandleKey(EditorKey.Escape, KeyModifiers.None, null);
        Check.True(!shell.IsZen && !shell.IsFullScreen, "Escape twice in a row leaves Zen and the full screen it took");

        Check.True(await host.ExecuteCommandAsync(LookCommands.ToggleFullScreen), "F11's command runs");
        Check.True(shell.IsFullScreen && !shell.IsZen, "full screen alone");
        shell.ToggleZen();
        shell.ToggleZen();
        Check.True(shell.IsFullScreen, "Zen leaves a full screen chosen before it as it was");
        shell.ToggleFullScreen();
        Check.True(!shell.IsFullScreen, "F11 again: the ordinary window");

        shell.FullScreenSupported = false;
        shell.ToggleFullScreen();
        Check.True(!shell.IsFullScreen && host.Notifications.Entries.Any(e => e.Title == "Full screen"), "a platform without full screen says so");
        shell.ToggleZen();
        Check.True(shell.IsZen && !shell.IsFullScreen, "Zen without full screen where there is none");
        shell.ToggleZen();
    }

    [Test]
    public static void ZenStaysInTheWindowWhenItsFullScreenSettingIsOff()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_ZEN_FULL_SCREEN" ? "false" : null);
        var shell = fixture.Shell;
        shell.ToggleZen();
        Check.True(shell.IsZen && !shell.IsFullScreen, "CRAFT_ZEN_FULL_SCREEN off: Zen in the window");
        shell.ToggleZen();
        Check.True(!shell.IsZen, "Ctrl+K Z again leaves");
        Check.True(CraftOptions.FromVariables(name => name == "CRAFT_ZEN_WIDTH" ? "0" : null).ZenWidth == 0, "0: the whole width");
    }
}

/// <summary>X part 9, item 26: the status bar's indentation, line ending, encoding and language items through quick picks.</summary>
[TestGroup("StatusPickers")]
public static class StatusPickerTests
{
    [Test]
    public static async Task IndentationIsPickedForTheEditorAndConvertedAsOneUndoStepWithTheEditorConfigSaid()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var path = fixture.Write("a.txt", "\tif\n    x\n  y\n");
        var tab = (await shell.OpenFileAsync(path))!;
        var editor = fixture.EditorOf(tab);
        Check.Equal(4, editor.Options.TabSize, "the default tab size");

        fixture.Ui.QuickPicks = request => request.Title == "Indentation" ? 3 : -1;             // convert to spaces
        Check.True(await fixture.Host.ExecuteCommandAsync(LookCommands.ChangeIndentation), "the status bar's item runs its command");
        Check.Equal("    if\n    x\n  y\n", Text(tab), "the leading tab became four spaces; spaces stay");
        tab.Document.Undo();
        Check.Equal("\tif\n    x\n  y\n", Text(tab), "one undo step brings it back");

        fixture.Ui.QuickPicks = request => request.Title switch { "Indentation" => 1, "Tab size" => 1, _ => -1 };   // tabs, size 2
        Check.True(await shell.PickIndentationAsync(), "tabs at 2");
        Check.True(!editor.Options.InsertSpaces && editor.Options.TabSize == 2 && editor.IndentationOverride is (false, 2), "the editor indents with tabs at 2 now");
        Check.Equal("Tab size: 2", ShellText.Indentation(editor.Options), "the status bar's text");
        Check.True(ShellText.IndentationTooltip(editor).Contains("picked for this editor", StringComparison.Ordinal), "its tooltip says where it comes from");
        fixture.Ui.QuickPicks = request => request.Title == "Indentation" ? 4 : -1;             // convert to tabs
        Check.True(await shell.PickIndentationAsync(), "convert to tabs");
        Check.Equal("\tif\n\t\tx\n\ty\n", Text(tab), "four spaces are two tabs at 2, two spaces one");
        tab.Document.Undo();
        Check.Equal("\tif\n    x\n  y\n", Text(tab), "one undo step");

        var config = fixture.Write(".editorconfig", "root = true\n[*]\nindent_style = space\nindent_size = 3\n");
        editor.ApplyFileSettings(new FileSettings { InsertSpaces = true, TabSize = 3, Sources = [config] });
        fixture.Ui.QuickPicks = _ => -1;
        await shell.PickIndentationAsync();
        var asked = fixture.Ui.QuickPickRequests[^1];
        Check.True(asked.Items[0].Detail!.Contains(".editorconfig", StringComparison.Ordinal) && asked.Items[0].Detail!.Contains("wins", StringComparison.Ordinal)
            && asked.Items[2].Detail!.Contains("says 3", StringComparison.Ordinal), $"the .editorconfig's values are said where they win: {asked.Items[0].Detail} / {asked.Items[2].Detail}");
        Check.True(editor.Options.TabSize == 2 && !editor.Options.InsertSpaces, "the pick holds for this editor over the .editorconfig");
    }

    [Test]
    public static async Task LineEndingConvertsEveryBreakAsOneUndoStepAndEndOfLineIsSaid()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var path = fixture.Write("b.txt", "a\r\nb\nc\r\nd");
        var tab = (await shell.OpenFileAsync(path))!;
        var editor = fixture.EditorOf(tab);
        fixture.Ui.QuickPicks = request => request.Title == "Line ending" ? 0 : -1;             // LF
        Check.True(await fixture.Host.ExecuteCommandAsync(LookCommands.ChangeLineEnding), "the line ending item's command");
        Check.Equal("a\nb\nc\nd", Text(tab), "every break is LF");
        Check.Equal("\n", ((TextDocument)tab.Document).LineEnding, "new lines and the save take LF");
        Check.True(tab.Document.IsDirty, "an unsaved change");
        tab.Document.Undo();
        Check.Equal("a\r\nb\nc\r\nd", Text(tab), "one undo step brings every break back");

        fixture.Ui.QuickPicks = request => request.Title == "Line ending" ? 1 : -1;             // CRLF
        Check.True(await shell.PickLineEndingAsync(), "CRLF");
        Check.Equal("a\r\nb\r\nc\r\nd", Text(tab), "the lone LF became CRLF, the others stayed");
        editor.ApplyFileSettings(new FileSettings { NewLine = "\n", Sources = [Path.Combine(fixture.Workspace, ".editorconfig")] });
        fixture.Ui.QuickPicks = _ => -1;
        await shell.PickLineEndingAsync();
        var detail = fixture.Ui.QuickPickRequests[^1].Items[0].Detail!;
        Check.True(detail.Contains("end_of_line = lf", StringComparison.Ordinal) && detail.Contains("wins", StringComparison.Ordinal), $"the .editorconfig's end_of_line is said where it wins: {detail}");
        Check.True(fixture.Ui.QuickPickRequests[^1].Items[1].Mark == "current", "CRLF is marked current");
    }

    [Test]
    public static async Task EncodingReopensAndSavesWithAnEncodingAndTheLanguageIsPicked()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var path = Path.Combine(fixture.Workspace, "c.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("café\n"));
        var tab = (await shell.OpenFileAsync(path))!;
        fixture.EditorOf(tab);
        var choices = EncodingChoices.All;
        Check.True(choices.Count > 10 && choices[0].Name == "UTF-8" && choices.Any(c => c.Name.StartsWith("Windows-1252", StringComparison.Ordinal)), "the common encodings, the UTF forms first");
        int western = choices.ToList().FindIndex(c => c.Name.StartsWith("Windows-1252", StringComparison.Ordinal));
        int utf16 = choices.ToList().FindIndex(c => c.Name == "UTF-16 LE");

        fixture.Ui.QuickPicks = request => request.Title switch { "Encoding" => 0, "Reopen with an encoding" => western, _ => -1 };
        Check.True(await fixture.Host.ExecuteCommandAsync(LookCommands.ChangeEncoding), "the encoding item's command");
        var document = (TextDocument)tab.Document;
        Check.Equal("cafÃ©\n", Text(tab), "the bytes read again as Windows-1252");
        Check.True(document.Encoding.CodePage == 1252 && !document.IsDirty, "the encoding taken, the document the file's text (clean)");
        document.Undo();
        Check.Equal("café\n", Text(tab), "one undo step back to the text as it was read");

        fixture.Ui.QuickPicks = request => request.Title switch { "Encoding" => 1, "Save with an encoding" => utf16, _ => -1 };
        Check.True(await shell.PickEncodingAsync(), "save with UTF-16 LE");
        var bytes = await File.ReadAllBytesAsync(path);
        Check.True(bytes is [0xFF, 0xFE, ..] && Encoding.Unicode.GetString(bytes.AsSpan(2)) == "café\n", "saved in UTF-16 LE with its mark");
        Check.True(!document.IsDirty && document.Encoding.CodePage == 1200, "saved and clean");

        fixture.Ui.QuickPicks = request => request.Title == "Language mode" ? request.Items.ToList().FindIndex(i => i.Detail == fixture.Language.Name) : -1;
        Check.True(await fixture.Host.ExecuteCommandAsync(LookCommands.ChangeLanguage), "the language item's command");
        Check.True(ReferenceEquals(tab.Language, fixture.Language), "the tab's language is the one picked");
        fixture.Ui.QuickPicks = request => request.Title == "Language mode" ? request.Items.Count - 1 : -1;
        await shell.PickLanguageAsync();
        Check.True(tab.Language is null, "plain text");
        fixture.Ui.QuickPicks = request => request.Title == "Language mode" ? 0 : -1;
        await shell.PickLanguageAsync();
        Check.True(tab.Language is null && fixture.Ui.QuickPickRequests[^1].Items[0].Text == "Auto", "Auto: by the file's name (plain text for .txt)");
    }

    private static string Text(DocumentTab tab) => tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length);
}

/// <summary>The recovery prompt at start: copies found, restored as unsaved with one undo step to the disk, compared, discarded with Undo; nothing shown when none.</summary>
[TestGroup("RecoveryPrompt")]
public static class RecoveryPromptTests
{
    [Test]
    public static async Task CopiesAreOfferedRestoredAsUnsavedWithUndoToTheDiskComparedAndDiscardedWithUndo()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var path = fixture.Write("src/a.txt", "disk\n");
        var outside = folder.File("other.txt");
        await File.WriteAllTextAsync(outside, "elsewhere\n");
        var recoveryFolder = RecoveryStore.FolderOf(shell.Options);
        Check.True(recoveryFolder.StartsWith(folder.File("obj"), StringComparison.Ordinal), $"the copies live in the data folder's recovery ({recoveryFolder})");

        // The last run: three documents with unsaved changes, their copies written, then no clean end (the store is never disposed).
        var crashed = new RecoveryStore(new CraftOptions { RecoveryFolder = recoveryFolder, AutosaveSeconds = 0 });
        var file = await TextDocument.OpenAsync(path);
        file.Edit([new TextEdit(0, 4, "recovered")], EditKind.Paste, SelectionState.At(0), SelectionState.At(9));
        var draft = TextDocument.Create();
        draft.Edit([TextEdit.Insert(0, "a draft")], EditKind.Typing, SelectionState.At(0), SelectionState.At(7));
        var other = await TextDocument.OpenAsync(outside);
        other.Edit([TextEdit.Insert(0, "x")], EditKind.Typing, SelectionState.At(0), SelectionState.At(1));
        crashed.Track(file);
        crashed.Track(draft);
        crashed.Track(other);
        await crashed.WriteNowAsync();

        var presenter = new DiffRecorder();
        fixture.Host.Presenter = presenter;
        Check.Equal(2, await shell.OfferRecoveryAsync(), "this workspace's file and the untitled draft are offered; another folder's file waits for that folder");
        var offer = shell.Recovery;
        Check.True(offer.CardVisible && offer.Title == "2 unsaved files were recovered", $"the card: {offer.Title}");
        var a = offer.Files.Single(f => f.Name == "a.txt");
        var untitled = offer.Files.Single(f => f.Path is null);
        Check.True(a.FileExists && a.Folder == "src" && a.Bytes > 0 && a.Describe(DateTimeOffset.Now).StartsWith("a.txt · src · ", StringComparison.Ordinal), $"a row: {a.Describe(DateTimeOffset.Now)}");
        Check.True(untitled.Name == "Untitled" && untitled.Describe(DateTimeOffset.Now).Contains("never saved", StringComparison.Ordinal), "the untitled row");
        Check.True(fixture.Host.Notifications.Entries.Any(e => e.Title == "2 unsaved files were recovered" && e.Text.Contains("a.txt", StringComparison.Ordinal)), "the notification centre lists them");

        Check.True(await shell.CompareRecoveredAsync(a), "Compare shows the diff");
        Check.True(presenter.Diffs is [{ Removed: 1, Added: 1 }] && presenter.Titles[0].Contains("on the disk against recovered", StringComparison.Ordinal), "the disk's line against the recovered one");

        Check.True(await shell.RestoreRecoveredAsync(a), "Restore");
        var tab = shell.Groups.FindOpen(path)!;
        Check.True(Text(tab) == "recovered\n" && tab.Document.IsDirty, "the file opened with the recovered text as an unsaved change");
        tab.Document.Undo();
        Check.True(Text(tab) == "disk\n" && !tab.Document.IsDirty, "one undo step back to the disk's text");
        tab.Document.Redo();
        await shell.RecoveryDiscarded;
        Check.True(!Listed(recoveryFolder).Contains(a.Entry.Id), "the restored file's old copy is gone");
        Check.True(offer.Files.Count == 1 && offer.Title == "1 unsaved file was recovered", "one left");
        await shell.SaveSessionAsync();
        Check.True((await File.ReadAllTextAsync(ShellModel.SessionPathOf(shell.Options, fixture.Workspace))).Contains("a.txt", StringComparison.Ordinal), "the restored file joins the session");

        Check.True(shell.DiscardRecovered(untitled), "Discard");
        Check.True(offer.Files.Count == 0 && !offer.CardVisible, "taken off at once, the card gone");
        Check.True(shell.UndoBar.Current?.Text.Contains("Untitled", StringComparison.Ordinal) == true && shell.UndoBar.Undo(), "the Undo bar brings it back");
        Check.True(offer.Files.Count == 1 && offer.CardVisible && Listed(recoveryFolder).Contains(untitled.Entry.Id), "back, its copy untouched");
        shell.DiscardRecovered(untitled);
        shell.UndoBar.Dismiss();
        await shell.RecoveryDiscarded;
        Check.True(!Listed(recoveryFolder).Contains(untitled.Entry.Id), "kept: the copy is deleted");
        Check.True(!fixture.Host.Notifications.Entries.Any(e => e.Title.Contains("recovered", StringComparison.Ordinal)), "the notification goes with the last file");
        Check.True(Listed(recoveryFolder).Count >= 1, "the other workspace's copy stays for that folder");
    }

    [Test]
    public static async Task RestoreAllOpensTheUntitledAndTheGoneFilesAsNewUnsavedTabsAndTheShellKeepsCopies()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var recoveryFolder = RecoveryStore.FolderOf(shell.Options);
        var gone = fixture.Write("gone.txt", "old\n");
        var crashed = new RecoveryStore(new CraftOptions { RecoveryFolder = recoveryFolder, AutosaveSeconds = 0 });
        var file = await TextDocument.OpenAsync(gone);
        file.Edit([TextEdit.Insert(0, "new ")], EditKind.Typing, SelectionState.At(0), SelectionState.At(4));
        var draft = TextDocument.Create();
        draft.Edit([TextEdit.Insert(0, "draft")], EditKind.Typing, SelectionState.At(0), SelectionState.At(5));
        crashed.Track(file);
        crashed.Track(draft);
        await crashed.WriteNowAsync();
        File.Delete(gone);

        Check.Equal(2, await shell.OfferRecoveryAsync(), "both offered");
        Check.True(shell.Recovery.Files.Single(f => f.Path is not null).Describe(DateTimeOffset.Now).Contains("gone", StringComparison.Ordinal), "the gone file says so");
        Check.Equal(2, await shell.RestoreAllRecoveredAsync(), "Restore all");
        var tabs = shell.Groups.AllTabs.ToList();
        Check.True(tabs.Any(t => t.FilePath == gone && Text(t) == "new old\n" && t.Document.IsDirty), "the gone file's text in a tab that saves to its path");
        Check.True(tabs.Any(t => t.FilePath is null && Text(t) == "draft" && t.Document.IsDirty), "the draft as an untitled tab");
        Check.True(shell.Recovery.Files.Count == 0 && !shell.Recovery.CardVisible, "nothing left to offer");

        // The shell keeps the copies of its own open documents from now on.
        var store = shell.RecoveryStore!;
        var path = fixture.Write("kept.txt", "kept\n");
        var tab = (await shell.OpenFileAsync(path))!;
        tab.Document.Edit([TextEdit.Insert(0, "x")], EditKind.Typing, SelectionState.At(0), SelectionState.At(1));
        await store.WriteNowAsync();
        await shell.RecoveryDiscarded;
        var copies = Directory.GetFiles(recoveryFolder, "*.json").Select(File.ReadAllText).ToList();
        Check.True(copies.Any(c => c.Contains("kept.txt", StringComparison.Ordinal)), "an edited open document has its copy");
        Check.True(shell.StartRecovery() == store, "started once");
    }

    [Test]
    public static async Task NothingIsShownWhenNoCopyIsLeftOrThePromptIsOff()
    {
        using var folder = new TempFolder();
        {
            using var fixture = new SplitFixture(folder);
            var shell = await fixture.OpenWorkspaceAsync();
            Check.Equal(0, await shell.OfferRecoveryAsync(), "no copy: nothing offered");
            Check.True(!shell.Recovery.CardVisible && !fixture.Host.Notifications.Entries.Any(e => e.Title.Contains("recovered", StringComparison.Ordinal)), "no card, no notification");
        }

        using var off = new SplitFixture(folder, name => name == "CRAFT_RECOVERY_PROMPT" ? "false" : null);
        var quiet = await off.OpenWorkspaceAsync();
        var crashed = new RecoveryStore(new CraftOptions { RecoveryFolder = RecoveryStore.FolderOf(quiet.Options), AutosaveSeconds = 0 });
        var draft = TextDocument.Create();
        draft.Edit([TextEdit.Insert(0, "draft")], EditKind.Typing, SelectionState.At(0), SelectionState.At(5));
        crashed.Track(draft);
        await crashed.WriteNowAsync();
        Check.Equal(0, await quiet.OfferRecoveryAsync(), "CRAFT_RECOVERY_PROMPT off: nothing offered");
        Check.True(!quiet.Recovery.CardVisible, "no card");
        Check.Equal(Path.Combine(Path.GetFullPath(folder.File("elsewhere")), "recovery"), RecoveryStore.FolderOf(new CraftOptions { DataFolder = folder.File("elsewhere") }), "the default recovery folder follows CRAFT_DATA_FOLDER");
    }

    private static List<string> Listed(string recoveryFolder)
    {
        using var store = new RecoveryStore(new CraftOptions { RecoveryFolder = recoveryFolder, AutosaveSeconds = 0 });
        return [.. store.ListRecoverable().Select(e => e.Id)];
    }

    private static string Text(DocumentTab tab) => tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length);

    private sealed class DiffRecorder : IEditorPresenter
    {
        public List<TextDiff> Diffs { get; } = [];

        public List<string> Titles { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ShowDiffAsync(string title, string path, TextDiff diff, CancellationToken cancellationToken)
        {
            Titles.Add(title);
            Diffs.Add(diff);
            return Task.FromResult(true);
        }

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
