// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;
using System.Reflection;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The registry inventory: every registry Craft's libraries hold (a static KindTable or SlotTable field), with its kind
/// and the names it has, listed in plans/inventory.md, which must match. CRAFT_UPDATE=1 rewrites it.
/// </summary>
[TestGroup("Inventory")]
public static class InventoryTests
{
    [Test]
    public static void RegistriesMatchTheInventory()
    {
        var actual = Inventory();
        var path = TestRunner.RepositoryPath("plans", "inventory.md");
        if (Environment.GetEnvironmentVariable("CRAFT_UPDATE") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Check.True(File.Exists(path), "plans/inventory.md exists (run with CRAFT_UPDATE=1 to write it)");
        Check.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual, "plans/inventory.md against the libraries' registries (a deliberate change: run with CRAFT_UPDATE=1)");
    }

    [Test]
    public static void EveryRegistryRefusesAnUnknownName()
    {
        Check.Throws<NotRegisteredException>(() => EditorLanguages.Get("no-such-language"), "EditorLanguages.Get of an unknown name");
        Check.Throws<NotRegisteredException>(() => EditorThemes.Get("no-such-theme"), "EditorThemes.Get of an unknown name");
        Check.Throws<NotRegisteredException>(() => EditorPlugins.Get("no-such-plugin"), "EditorPlugins.Get of an unknown name");
        Check.Throws<NotRegisteredException>(() => EditorCommands.Get("no.such.command"), "EditorCommands.Get of an unknown id");
        Check.Throws<NotRegisteredException>(() => TerminalHosts.Get("no-such-host"), "TerminalHosts.Get of an unknown name");
        Check.True(EditorKeyBindings.Find(new KeyChord(EditorKey.F12, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) is null, "an unbound chord finds no binding");
        Check.True(EditorLanguages.ForFile("readme.no-such-extension") is null, "a file of no registered language opens as plain text");
    }

    [Test]
    public static void ThemesHaveTheLibraryDefaults()
    {
        Check.True(EditorThemes.Find(EditorThemes.Dark) is { IsDark: true }, "the library's dark theme is registered and dark");
        Check.True(EditorThemes.Find(EditorThemes.Light) is { IsDark: false }, "the library's light theme is registered and light");
        var dark = EditorThemes.Get("DARK");
        Check.Equal(dark.Colors.Foreground, dark.StyleOf("a class no theme knows").Argb, "an unknown class is drawn as plain text");
    }

    [Test]
    public static void OptionsReadEveryVariable()
    {
        var values = new Dictionary<string, string>
        {
            ["CRAFT_FONT"] = "Consolas", ["CRAFT_FONT_SIZE"] = "16.5", ["CRAFT_LINE_SPACING"] = "1.5", ["CRAFT_TAB_SIZE"] = "2",
            ["CRAFT_INSERT_SPACES"] = "false", ["CRAFT_THEME"] = "light", ["CRAFT_AUTOSAVE_SECONDS"] = "0", ["CRAFT_UNDO_GROUP_MS"] = "250",
            ["CRAFT_RECOVERY_FOLDER"] = "rec", ["CRAFT_CRASH_FOLDER"] = "crashes", ["CRAFT_BUFFER_CHUNK"] = "4096", ["CRAFT_LINE_ENDING"] = "crlf",
            ["CRAFT_WORKSPACE_IGNORE"] = "bin; out,dist", ["CRAFT_CARET_BLINK_MS"] = "0", ["CRAFT_CARET_WIDTH"] = "1.5",
            ["CRAFT_DOUBLE_CLICK_MS"] = "300", ["CRAFT_DOUBLE_CLICK_DISTANCE"] = "6", ["CRAFT_BRACKET_SCAN_LINES"] = "100",
            ["CRAFT_SCROLL_MARGIN_LINES"] = "1", ["CRAFT_SCROLL_MARGIN_COLUMNS"] = "3", ["CRAFT_BOTTOM_MARGIN_LINES"] = "0",
            ["CRAFT_GUTTER_PADDING"] = "1", ["CRAFT_AUTO_CLOSE_BRACKETS"] = "true", ["CRAFT_FIND_COUNT_LIMIT"] = "50", ["CRAFT_PARSE_WAIT_MS"] = "0",
            ["CRAFT_PLUGIN_FOLDERS"] = "p1; p2", ["CRAFT_PLUGINS_DISABLED"] = "Craft.CSharp, Other",
            ["CRAFT_KEY_BINDINGS"] = "Ctrl+Shift+W=sample.wordCount;Ctrl+K=", ["CRAFT_HOST_LOG_LINES"] = "20", ["CRAFT_PROPOSAL_PREVIEW_CHARS"] = "40",
            // Phase A1: the diff view.
            ["CRAFT_DIFF_CONTEXT_LINES"] = "1", ["CRAFT_DIFF_LINE_LIMIT"] = "50", ["CRAFT_DIFF_CHECK"] = "false",
            // Phase E4: the shell.
            ["CRAFT_DATA_FOLDER"] = "data", ["CRAFT_RECENT_LIMIT"] = "5", ["CRAFT_NARROW_WIDTH"] = "600", ["CRAFT_TREE_WIDTH"] = "300",
            ["CRAFT_TREE_INDENT"] = "10", ["CRAFT_PANEL_WIDTH"] = "320",
            ["CRAFT_DIAGNOSTICS_DELAY_MS"] = "250", ["CRAFT_SEMANTIC_MAX_CHARS"] = "1000", ["CRAFT_WORKSPACE_READERS"] = "loose, csproj",
            ["CRAFT_WORKSPACE_CONFIGURATION"] = "Release", ["CRAFT_COMPLETION_DELAY_MS"] = "0", ["CRAFT_COMPLETION_ROWS"] = "7",
            ["CRAFT_PROBLEMS_LIMIT"] = "20", ["CRAFT_HOVER_DELAY_MS"] = "100",
            ["CRAFT_SOURCE_GENERATORS"] = "false", ["CRAFT_SEMANTIC_COLORS_DELAY_MS"] = "-1", ["CRAFT_MEMORY_MONITOR"] = "none",
            // Phase D: the approved design's shell.
            ["CRAFT_FONT_FALLBACK"] = "Consolas", ["CRAFT_INTERFACE_FONT"] = "Segoe UI", ["CRAFT_INTERFACE_FONT_SIZE"] = "14", ["CRAFT_ACCENT"] = "#82AAFF",
            ["CRAFT_BOTTOM_PANEL_HEIGHT"] = "200", ["CRAFT_QUICK_OPEN_FILES"] = "1000", ["CRAFT_QUICK_OPEN_ROWS"] = "8", ["CRAFT_TOUCH_TARGET"] = "48",
            ["CRAFT_TITLE_BAR"] = "page",
            // Phase A2: inline completion's provider, the chat column.
            ["CRAFT_INLINE_COMPLETION_PROVIDER"] = "ai", ["CRAFT_CHAT_WIDTH"] = "400",
            // Phase T: the terminal.
            ["CRAFT_TERMINAL_SHELL"] = "bash", ["CRAFT_TERMINAL_ARGS"] = "-l", ["CRAFT_TERMINAL_ENV"] = "A=1|B=2", ["CRAFT_TERMINAL_CWD"] = "start",
            ["CRAFT_TERMINAL_SCROLLBACK"] = "200", ["CRAFT_TERMINAL_HOST"] = "pipe", ["CRAFT_TERMINAL_LINE_SPACING"] = "1.5",
            ["CRAFT_TERMINAL_EDITOR_COMMANDS"] = "craft.quickOpen, craft.settings",
            // Phase U: the watchdog, activities and notifications.
            ["CRAFT_UI_WATCHDOG_MS"] = "50", ["CRAFT_UI_STALL_MS"] = "500", ["CRAFT_UI_STALL_REPORTS"] = "3", ["CRAFT_NOTIFICATIONS_KEEP"] = "10",
            ["CRAFT_TOAST_MS"] = "0", ["CRAFT_ACTIVITY_MIN_MS"] = "0", ["CRAFT_ACTIVITY_FRAME_MS"] = "33",
        };
        var options = CraftOptions.FromVariables(name => values.GetValueOrDefault(name));
        Check.Equal(new CraftOptions
        {
            FontFamily = "Consolas", FontSize = 16.5f, LineSpacing = 1.5f, TabSize = 2, InsertSpaces = false, Theme = "light",
            AutosaveSeconds = 0, UndoGroupMilliseconds = 250, RecoveryFolder = "rec", CrashFolder = "crashes", BufferChunkCharacters = 4096, NewLine = "\r\n",
            WorkspaceIgnore = options.WorkspaceIgnore, CaretBlinkMilliseconds = 0, CaretWidth = 1.5f, DoubleClickMilliseconds = 300,
            DoubleClickDistance = 6, BracketScanLines = 100, ScrollMarginLines = 1, ScrollMarginColumns = 3, BottomMarginLines = 0,
            GutterPaddingCells = 1, AutoCloseBrackets = true, FindCountLimit = 50, ParseWaitMilliseconds = 0,
            PluginFolders = options.PluginFolders, DisabledPlugins = options.DisabledPlugins,
            KeyBindings = options.KeyBindings, HostLogLines = 20, ProposalPreviewCharacters = 40,
            DiffContextLines = 1, DiffLineLimit = 50, DiffCheck = false,
            DataFolder = "data", RecentLimit = 5, NarrowWidth = 600, TreeWidth = 300, TreeIndent = 10, PanelWidth = 320,
            DiagnosticsDelayMilliseconds = 250, SemanticMaxCharacters = 1000, WorkspaceReaders = options.WorkspaceReaders, WorkspaceConfiguration = "Release",
            CompletionDelayMilliseconds = 0, CompletionRows = 7, ProblemsListLimit = 20, HoverDelayMilliseconds = 100,
            SourceGenerators = false, SemanticColorsDelayMilliseconds = -1, MemoryMonitor = "none",
            FontFallback = "Consolas", InterfaceFont = "Segoe UI", InterfaceFontSize = 14, Accent = AccentColors.Blue, BottomPanelHeight = 200,
            QuickOpenFiles = 1000, QuickOpenRows = 8, TouchTarget = 48, TitleBar = "page",
            InlineCompletionProvider = "ai", ChatWidth = 400,
            TerminalShell = "bash", TerminalArguments = "-l", TerminalEnvironment = options.TerminalEnvironment, TerminalWorkingDirectory = "start",
            TerminalScrollback = 200, TerminalHost = "pipe", TerminalLineSpacing = 1.5f, TerminalEditorCommands = options.TerminalEditorCommands,
            UiWatchdogMilliseconds = 50, UiStallMilliseconds = 500, UiStallReports = 3, NotificationsKeep = 10, ToastMilliseconds = 0,
            ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 33,
        }, options, "options read from every variable");
        Check.True(options.TerminalEnvironment is [{ Key: "A", Value: "1" }, { Key: "B", Value: "2" }], "CRAFT_TERMINAL_ENV split at '|'");
        Check.SequenceEqual(["craft.quickOpen", "craft.settings"], options.TerminalEditorCommands, "CRAFT_TERMINAL_EDITOR_COMMANDS split at ','");
        Check.SequenceEqual(["loose", "csproj"], options.WorkspaceReaders, "CRAFT_WORKSPACE_READERS split at ','");
        var settings = options.ToLanguageSettings();
        Check.True(settings is { DiagnosticsDelayMilliseconds: 250, SemanticMaxCharacters: 1000, Configuration: "Release", TabSize: 2, InsertSpaces: false, SourceGenerators: false, SemanticColorsDelayMilliseconds: -1 }
            && ReferenceEquals(settings.WorkspaceReaders, options.WorkspaceReaders), "the language settings carry the options");
        Check.True(options.KeyBindings is [{ Name: "Ctrl+Shift+W", CommandId: "sample.wordCount" }, { Name: "Ctrl+K", CommandId: "" }], "CRAFT_KEY_BINDINGS parsed, an empty command kept as an unbinding");
        Check.SequenceEqual(["p1", "p2"], options.PluginFolders, "CRAFT_PLUGIN_FOLDERS split at ';'");
        Check.SequenceEqual(["Craft.CSharp", "Other"], options.DisabledPlugins, "CRAFT_PLUGINS_DISABLED split at ','");
        Check.SequenceEqual(["bin", "out", "dist"], options.WorkspaceIgnore, "CRAFT_WORKSPACE_IGNORE split at ';' and ','");
        Check.Equal(values.Count, CraftOptions.Variables.Count, "every variable is listed in CraftOptions.Variables");
        var error = Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_TAB_SIZE" ? "wide" : null), "a bad value");
        Check.True(error.Message.Contains("CRAFT_TAB_SIZE", StringComparison.Ordinal), "the error names the variable");
    }

    private static string Inventory()
    {
        var text = new StringBuilder("# Craft's registries\n\nGenerated by tests/Craft.Tests (Inventory group); CRAFT_UPDATE=1 rewrites it.\n\n| Registry | Assembly | Holds | Names |\n|---|---|---|---|\n");
        foreach (var assembly in ApiGuardTests.Libraries)
        {
            foreach (var type in assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
                {
                    var fieldType = field.FieldType;
                    if (!fieldType.IsGenericType)
                    {
                        continue;
                    }

                    var definition = fieldType.GetGenericTypeDefinition();
                    if (definition != typeof(KindTable<>) && definition != typeof(SlotTable<,>))
                    {
                        continue;
                    }

                    var held = fieldType.GetGenericArguments()[^1];
                    var names = (fieldType.GetProperty(definition == typeof(KindTable<>) ? "Names" : "Keys")!.GetValue(field.GetValue(null)) as IEnumerable)!
                        .Cast<object>().Select(n => n.ToString()).ToList();
                    text.Append($"| `{type.Name}` | {assembly.GetName().Name} | `{held.Name}` | {(names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"`{n}`")))} |\n");
                }
            }
        }

        return text.ToString();
    }
}
