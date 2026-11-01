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
            ["CRAFT_GUTTER_PADDING"] = "1", ["CRAFT_AUTO_CLOSE_BRACKETS"] = "true", ["CRAFT_FORMAT_ON_SAVE"] = "true", ["CRAFT_FIND_COUNT_LIMIT"] = "50", ["CRAFT_PARSE_WAIT_MS"] = "0",
            ["CRAFT_PLUGIN_FOLDERS"] = "p1; p2", ["CRAFT_PLUGINS_DISABLED"] = "Craft.CSharp, Other",
            ["CRAFT_KEY_BINDINGS"] = "Ctrl+Shift+W=sample.wordCount;Ctrl+K=", ["CRAFT_HOST_LOG_LINES"] = "20", ["CRAFT_PROPOSAL_PREVIEW_CHARS"] = "40",
            // Phase A1: the diff view.
            ["CRAFT_DIFF_CONTEXT_LINES"] = "1", ["CRAFT_DIFF_LINE_LIMIT"] = "50", ["CRAFT_DIFF_CHECK"] = "false",
            // Phase E4: the shell.
            ["CRAFT_DATA_FOLDER"] = "data", ["CRAFT_RECENT_LIMIT"] = "5", ["CRAFT_NARROW_WIDTH"] = "600", ["CRAFT_PANEL_EXPLORER_WIDTH"] = "300",
            ["CRAFT_TREE_INDENT"] = "10",
            ["CRAFT_DIAGNOSTICS_DELAY_MS"] = "250", ["CRAFT_SEMANTIC_MAX_CHARS"] = "1000", ["CRAFT_WORKSPACE_READERS"] = "loose, csproj",
            ["CRAFT_WORKSPACE_CONFIGURATION"] = "Release", ["CRAFT_SEARCH_DELAY_MS"] = "75", ["CRAFT_COMPLETION_DELAY_MS"] = "0", ["CRAFT_COMPLETION_ROWS"] = "7",
            ["CRAFT_PROBLEMS_LIMIT"] = "20", ["CRAFT_HOVER_DELAY_MS"] = "100",
            ["CRAFT_SOURCE_GENERATORS"] = "false", ["CRAFT_SEMANTIC_COLORS_DELAY_MS"] = "-1", ["CRAFT_MEMORY_MONITOR"] = "none",
            // Phase D: the approved design's shell.
            ["CRAFT_FONT_FALLBACK"] = "Consolas", ["CRAFT_INTERFACE_FONT"] = "Segoe UI", ["CRAFT_INTERFACE_FONT_SIZE"] = "14", ["CRAFT_ACCENT"] = "#82AAFF",
            ["CRAFT_PANEL_BOTTOM_HEIGHT"] = "200", ["CRAFT_QUICK_OPEN_FILES"] = "1000", ["CRAFT_QUICK_OPEN_ROWS"] = "8", ["CRAFT_TOUCH_TARGET"] = "48",
            ["CRAFT_TITLE_BAR"] = "page",
            // Phase A2: inline completion's provider, the chat column.
            ["CRAFT_INLINE_COMPLETION_PROVIDER"] = "ai", ["CRAFT_PANEL_CHAT_WIDTH"] = "400",
            // Phase T: the terminal.
            ["CRAFT_TERMINAL_SHELL"] = "bash", ["CRAFT_TERMINAL_ARGS"] = "-l", ["CRAFT_TERMINAL_ENV"] = "A=1|B=2", ["CRAFT_TERMINAL_CWD"] = "start",
            ["CRAFT_TERMINAL_SCROLLBACK"] = "200", ["CRAFT_TERMINAL_HOST"] = "pipe", ["CRAFT_TERMINAL_LINE_SPACING"] = "1.5",
            ["CRAFT_TERMINAL_EDITOR_COMMANDS"] = "craft.quickOpen, craft.settings",
            // Phase U: the watchdog, activities and notifications.
            ["CRAFT_UI_WATCHDOG_MS"] = "50", ["CRAFT_UI_STALL_MS"] = "500", ["CRAFT_UI_STALL_REPORTS"] = "3", ["CRAFT_NOTIFICATIONS_KEEP"] = "10",
            ["CRAFT_TOAST_MS"] = "0", ["CRAFT_ACTIVITY_MIN_MS"] = "0", ["CRAFT_ACTIVITY_FRAME_MS"] = "33",
            // Phase U2: panels and layout, the tree following the disk.
            ["CRAFT_PANEL_EXPLORER_OPEN"] = "false", ["CRAFT_PANEL_CHAT_OPEN"] = "false", ["CRAFT_PANEL_BOTTOM_OPEN"] = "false", ["CRAFT_PANEL_MIN_SIZE"] = "80",
            ["CRAFT_WORKSPACE_WATCHER"] = "none", ["CRAFT_WORKSPACE_WATCH_MS"] = "40",
            // The owner's note 16: the keymap and the zoom.
            ["CRAFT_KEYMAP"] = "visualstudio", ["CRAFT_ZOOM_STEP"] = "2",
            // The owner's note 21: the crash watch and its report; note 20: Ctrl+C in a terminal.
            ["CRAFT_CRASH_TRAIL"] = "false", ["CRAFT_CRASH_TRAIL_KB"] = "64", ["CRAFT_CRASH_TRAIL_EXCEPTIONS"] = "3", ["CRAFT_CRASH_TICKET_TRAIL_LINES"] = "50",
            ["CRAFT_CRASH_TICKET_OFFER"] = "false", ["CRAFT_CRASH_RUNS_KEPT"] = "2", ["CRAFT_CRASH_RECORDS"] = "none", ["CRAFT_TERMINAL_CTRL_C_COPIES"] = "false",
            // Phase X: motion, smooth scrolling, Find references.
            ["CRAFT_REDUCED_MOTION"] = "true", ["CRAFT_MOTION_MS"] = "120", ["CRAFT_SCROLL_MS"] = "0", ["CRAFT_SCROLL_WHEEL_LINES"] = "4", ["CRAFT_REFERENCES_LIMIT"] = "77",
            // Phase X: the editing surface.
            ["CRAFT_INDENT_GUIDES"] = "false", ["CRAFT_BRACKET_HIGHLIGHT"] = "false", ["CRAFT_WORD_HIGHLIGHT"] = "false", ["CRAFT_FOLDING"] = "false",
            ["CRAFT_FOLDING_DELAY_MS"] = "100", ["CRAFT_MAX_CARETS"] = "50",
            // The owner's third Windows run (B1): the paint.
            ["CRAFT_PAINT_LINE_LAYOUTS"] = "false", ["CRAFT_PAINT_CACHE_LINES"] = "8",
            // The X restart, piece 2a.
            ["CRAFT_PIECE_COALESCE_CHARS"] = "32", ["CRAFT_PIECE_BATCH_MIN_EDITS"] = "4", ["CRAFT_PIECE_BATCH_PIECES_PER_EDIT"] = "16", ["CRAFT_HOVER_HIDE_MS"] = "200",
            ["CRAFT_FOLD_PREVIEW_LINES"] = "6", ["CRAFT_OVERVIEW_RULER"] = "false", ["CRAFT_OVERVIEW_RULER_WIDTH"] = "20", ["CRAFT_STICKY_SCROLL"] = "false", ["CRAFT_STICKY_SCROLL_LINES"] = "3",
            // The X restart, piece 2b: the minimap; completion and ghost text at several carets.
            ["CRAFT_MINIMAP"] = "true", ["CRAFT_MINIMAP_SCALE"] = "3", ["CRAFT_MINIMAP_MAX_COLUMNS"] = "80", ["CRAFT_MINIMAP_MIN_WIDTH"] = "400", ["CRAFT_MINIMAP_LINES_PER_FRAME"] = "32",
            ["CRAFT_MINIMAP_DIAGNOSTICS"] = "false", ["CRAFT_MINIMAP_FIND"] = "false", ["CRAFT_COMPLETION_ALL_CARETS"] = "false",
            // Phase X slice 3: inline, not modal; the welcome page.
            ["CRAFT_WELCOME_AT_START"] = "false", ["CRAFT_UNDO_BAR_MS"] = "3000", ["CRAFT_FIND_BACKGROUND_CHARS"] = "1000", ["CRAFT_FIND_REGEX_TIMEOUT_MS"] = "100",
            ["CRAFT_QUICK_OPEN_BACKGROUND_FILES"] = "50", ["CRAFT_QUICK_OPEN_RECENT"] = "5",
            // The X restart, piece 3a: the split editor, moving files in the tree, peek definition.
            ["CRAFT_EDITOR_GROUPS_MAX"] = "3", ["CRAFT_EDITOR_GROUPS_CLOSE_EMPTY"] = "false", ["CRAFT_TREE_DRAG_MOVE"] = "false", ["CRAFT_PEEK_LINES"] = "8",
            ["CRAFT_PEEK_MAX_MB"] = "4",
            // The owner's note 26: the telemetry table.
            ["CRAFT_TELEMETRY_SAMPLES"] = "30", ["CRAFT_TELEMETRY_SLOW_MS"] = "250",
            // B8-B10: the splitters, the terminal's profiles and design, the global loader.
            ["CRAFT_SPLITTER_GRIP"] = "12", ["CRAFT_SPLITTER_HOVER_MS"] = "50", ["CRAFT_SPLITTER_KEY_STEP"] = "20", ["CRAFT_SPLITTER_SNAP"] = "false",
            ["CRAFT_TERMINAL_DEFAULT_PROFILE"] = "Git Bash", ["CRAFT_TERMINAL_PROFILES"] = "Node=node -i", ["CRAFT_TERMINAL_TABS"] = "tabs", ["CRAFT_TERMINAL_LIST_FROM"] = "5",
            ["CRAFT_TERMINAL_PADDING"] = "4", ["CRAFT_TERMINAL_CURSOR"] = "bar", ["CRAFT_TERMINAL_CURSOR_BLINK"] = "false", ["CRAFT_TERMINAL_BUSY_AFTER_MS"] = "250",
            ["CRAFT_TERMINAL_BUSY_QUIET_MS"] = "300", ["CRAFT_LOADER_DELAY_MS"] = "100", ["CRAFT_LOADER_KINDS"] = "workspace, indexing", ["CRAFT_LOADER_CYCLE_MS"] = "900",
            // X part 6: the Search view and the Run view.
            ["CRAFT_SEARCH_MAX_RESULTS"] = "500", ["CRAFT_SEARCH_MAX_FILE_MB"] = "3", ["CRAFT_SEARCH_THREADS"] = "2", ["CRAFT_SEARCH_IGNORE_FILES"] = "false",
            ["CRAFT_SEARCH_PROVIDER"] = "code", ["CRAFT_RUN_SANDBOX"] = "approve-each", ["CRAFT_RUN_TIME_LIMIT_MINUTES"] = "30", ["CRAFT_RUN_APPROVAL"] = "each", ["CRAFT_RUN_INTERACTIVE"] = "off",
            // X part 10: the chat and the agent redesigned.
            ["CRAFT_CHAT_EXPANDED"] = "true", ["CRAFT_COMPOSER_MAX_LINES"] = "6", ["CRAFT_CHAT_COPY_FORMAT"] = "plain", ["CRAFT_CHAT_FOLD_CODE_LINES"] = "10",
            ["CRAFT_CHAT_SHOW_METRICS"] = "false",
            // X part 7: rich context menus.
            ["CRAFT_TABS_REOPEN_LIMIT"] = "7", ["CRAFT_DELETE_UNDO_MAX_MB"] = "2", ["CRAFT_MENU_TYPE_AHEAD_MS"] = "400", ["CRAFT_MENU_SUBMENU_DELAY_MS"] = "0",
        };
        var options = CraftOptions.FromVariables(name => values.GetValueOrDefault(name));
        Check.Equal(new CraftOptions
        {
            FontFamily = "Consolas", FontSize = 16.5f, LineSpacing = 1.5f, TabSize = 2, InsertSpaces = false, Theme = "light",
            AutosaveSeconds = 0, UndoGroupMilliseconds = 250, RecoveryFolder = "rec", CrashFolder = "crashes", BufferChunkCharacters = 4096, NewLine = "\r\n",
            WorkspaceIgnore = options.WorkspaceIgnore, CaretBlinkMilliseconds = 0, CaretWidth = 1.5f, DoubleClickMilliseconds = 300,
            DoubleClickDistance = 6, BracketScanLines = 100, ScrollMarginLines = 1, ScrollMarginColumns = 3, BottomMarginLines = 0,
            GutterPaddingCells = 1, AutoCloseBrackets = true, FormatOnSave = true, FindCountLimit = 50, ParseWaitMilliseconds = 0,
            PluginFolders = options.PluginFolders, DisabledPlugins = options.DisabledPlugins,
            KeyBindings = options.KeyBindings, HostLogLines = 20, ProposalPreviewCharacters = 40,
            DiffContextLines = 1, DiffLineLimit = 50, DiffCheck = false,
            DataFolder = "data", RecentLimit = 5, NarrowWidth = 600, TreeWidth = 300, TreeIndent = 10,
            DiagnosticsDelayMilliseconds = 250, SemanticMaxCharacters = 1000, WorkspaceReaders = options.WorkspaceReaders, WorkspaceConfiguration = "Release",
            SearchDelayMilliseconds = 75, CompletionDelayMilliseconds = 0, CompletionRows = 7, ProblemsListLimit = 20, HoverDelayMilliseconds = 100,
            SourceGenerators = false, SemanticColorsDelayMilliseconds = -1, MemoryMonitor = "none",
            FontFallback = "Consolas", InterfaceFont = "Segoe UI", InterfaceFontSize = 14, Accent = AccentColors.Blue, BottomPanelHeight = 200,
            QuickOpenFiles = 1000, QuickOpenRows = 8, TouchTarget = 48, TitleBar = "page",
            InlineCompletionProvider = "ai", ChatWidth = 400,
            TerminalShell = "bash", TerminalArguments = "-l", TerminalEnvironment = options.TerminalEnvironment, TerminalWorkingDirectory = "start",
            TerminalScrollback = 200, TerminalHost = "pipe", TerminalLineSpacing = 1.5f, TerminalEditorCommands = options.TerminalEditorCommands,
            UiWatchdogMilliseconds = 50, UiStallMilliseconds = 500, UiStallReports = 3, NotificationsKeep = 10, ToastMilliseconds = 0,
            ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 33,
            ExplorerOpen = false, ChatOpen = false, BottomPanelOpen = false, PanelMinimumSize = 80, WorkspaceWatcher = "none", WorkspaceWatchMilliseconds = 40,
            KeyMap = "visualstudio", ZoomStep = 2,
            CrashTrail = false, CrashTrailKilobytes = 64, CrashTrailExceptionsPerSecond = 3, CrashTicketTrailLines = 50, CrashTicketOffer = false, CrashRunsKept = 2,
            CrashRecords = "none", TerminalCtrlCCopies = false,
            ReducedMotion = true, MotionMilliseconds = 120, ScrollMilliseconds = 0, ScrollWheelLines = 4, ReferencesLimit = 77,
            IndentGuides = false, BracketHighlight = false, WordHighlight = false, Folding = false, FoldingDelayMilliseconds = 100, MaxCarets = 50,
            PaintLineLayouts = false, PaintCacheLines = 8,
            PieceCoalesceCharacters = 32, PieceBatchMinEdits = 4, PieceBatchPiecesPerEdit = 16, HoverHideMilliseconds = 200,
            FoldPreviewLines = 6, OverviewRuler = false, OverviewRulerWidth = 20, StickyScroll = false, StickyScrollLines = 3,
            Minimap = true, MinimapScale = 3, MinimapMaxColumns = 80, MinimapMinWidth = 400, MinimapLinesPerFrame = 32, MinimapDiagnostics = false, MinimapFind = false, CompletionAllCarets = false,
            WelcomeAtStart = false, UndoBarMilliseconds = 3000, FindBackgroundCharacters = 1000, FindRegexTimeoutMilliseconds = 100,
            QuickOpenBackgroundFiles = 50, QuickOpenRecent = 5,
            EditorGroupsMax = 3, EditorGroupsCloseEmpty = false, TreeDragMove = false, PeekLines = 8, PeekMaxMegabytes = 4,
            TelemetrySamples = 30, TelemetrySlowMilliseconds = 250,
            SplitterGrip = 12, SplitterHoverMilliseconds = 50, SplitterKeyStep = 20, SplitterSnap = false,
            TerminalDefaultProfile = "Git Bash", TerminalProfiles = options.TerminalProfiles, TerminalTabs = "tabs", TerminalListFrom = 5, TerminalPadding = 4,
            TerminalCursor = "bar", TerminalCursorBlink = false, TerminalBusyAfterMilliseconds = 250, TerminalBusyQuietMilliseconds = 300,
            LoaderDelayMilliseconds = 100, LoaderKinds = options.LoaderKinds, LoaderCycleMilliseconds = 900,
            SearchMaxResults = 500, SearchMaxFileMegabytes = 3, SearchThreads = 2, SearchIgnoreFiles = false, SearchProvider = "code",
            RunSandbox = "approve-each", RunTimeLimitMinutes = 30, RunApproval = "each", RunInteractive = "off",
            ChatExpanded = true, ComposerMaxLines = 6, ChatCopyFormat = "plain", ChatFoldCodeLines = 10, ChatShowMetrics = false,
            TabsReopenLimit = 7, DeleteUndoMaxMegabytes = 2, MenuTypeAheadMilliseconds = 400, MenuSubmenuDelayMilliseconds = 0,
        }, options, "options read from every variable");
        Check.True(options.TerminalProfiles is [{ Key: "Node", Value: "node -i" }] && options.LoaderKinds is ["workspace", "indexing"], "CRAFT_TERMINAL_PROFILES and CRAFT_LOADER_KINDS split");
        Check.True(Equals(new CraftOptions(), new CraftOptions()), "two default options compare equal (no list made per instance)");
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
