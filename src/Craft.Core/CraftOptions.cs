// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Core;

/// <summary>A Craft setting's environment variable: its name, its default and what it sets.</summary>
/// <param name="Name">The variable ("CRAFT_TAB_SIZE").</param>
/// <param name="Default">Its default, as text.</param>
/// <param name="Meaning">What it sets.</param>
public sealed record CraftVariable(string Name, string Default, string Meaning)
{
    /// <summary>Its shape on the settings page (kind, section, title, range, choices): declared next to it in <see cref="CraftOptions.Variables"/>.</summary>
    public SettingShape Shape { get; init; } = SettingShape.Unknown;
}

/// <summary>
/// The editor's settings. Every one has a default and an environment variable (<see cref="Variables"/>); the app's
/// settings page, the user's <c>settings.json</c> and a workspace's <c>.craft/settings.json</c> set them too (<c>Craft.Core.Settings.CraftSettings</c>). Nothing the editor does is fixed
/// where someone may need to adjust it.
/// </summary>
public sealed record CraftOptions
{
    /// <summary>
    /// The code font's family (<c>CRAFT_FONT</c>): JetBrains Mono, bundled with the app (phase D); a family that does not
    /// draw monospaced on the platform gives way to <see cref="FontFallback"/>.
    /// </summary>
    public string FontFamily { get; init; } = "JetBrains Mono";

    /// <summary>The code font's size in device-independent units (<c>CRAFT_FONT_SIZE</c>; the design's 13).</summary>
    public float FontSize { get; init; } = 13;

    /// <summary>A line's height as a multiple of the font's (<c>CRAFT_LINE_SPACING</c>; 1.69 gives the design's 22-unit lines at 13).</summary>
    public float LineSpacing { get; init; } = 1.69f;

    /// <summary>
    /// The code fonts tried, in order, when <see cref="FontFamily"/> does not draw monospaced on the platform
    /// (<c>CRAFT_FONT_FALLBACK</c>, families separated by commas; the first that draws monospaced is taken).
    /// </summary>
    public string FontFallback { get; init; } = "Cascadia Mono, Consolas";

    /// <summary>The interface's font family (<c>CRAFT_INTERFACE_FONT</c>): Inter, bundled with the app.</summary>
    public string InterfaceFont { get; init; } = "Inter";

    /// <summary>The interface's text size in device-independent units (<c>CRAFT_INTERFACE_FONT_SIZE</c>; the design's 13).</summary>
    public float InterfaceFontSize { get; init; } = 13;

    /// <summary>
    /// The accent over the theme's (<c>CRAFT_ACCENT</c>: amber, teal, blue, rose, or #RRGGBB), or null for the theme's
    /// own; moved until it reads at 4.5:1 on the theme's grounds.
    /// </summary>
    public uint? Accent { get; init; }

    /// <summary>The columns a tab spans (<c>CRAFT_TAB_SIZE</c>).</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Whether Tab inserts spaces rather than a tab character (<c>CRAFT_INSERT_SPACES</c>).</summary>
    public bool InsertSpaces { get; init; } = true;

    /// <summary>The theme's name in <c>EditorThemes</c> (<c>CRAFT_THEME</c>).</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Seconds between autosaves of a recovery copy; 0 turns autosave off (<c>CRAFT_AUTOSAVE_SECONDS</c>).</summary>
    public int AutosaveSeconds { get; init; } = 30;

    /// <summary>Typing with pauses shorter than this many milliseconds is one undo step (<c>CRAFT_UNDO_GROUP_MS</c>).</summary>
    public int UndoGroupMilliseconds { get; init; } = 1000;

    /// <summary>Where recovery copies are kept; null for the user's local application data folder, under "Craft/recovery" (<c>CRAFT_RECOVERY_FOLDER</c>).</summary>
    public string? RecoveryFolder { get; init; }

    /// <summary>Where crash reports are written; null for the user's local application data folder, under "Craft/crashes" (<c>CRAFT_CRASH_FOLDER</c>).</summary>
    public string? CrashFolder { get; init; }

    /// <summary>The characters of each block a text buffer's add buffer grows by (<c>CRAFT_BUFFER_CHUNK</c>).</summary>
    public int BufferChunkCharacters { get; init; } = 64 * 1024;

    /// <summary>
    /// The line break a new document (one with no line break to follow) uses: "\n", "\r\n", or null for the running
    /// system's (<c>CRAFT_LINE_ENDING</c>: lf, crlf or auto). A file read from disk keeps its own.
    /// </summary>
    public string? NewLine { get; init; }

    /// <summary>The folder names a workspace skips when it lists its files, ignoring case (<c>CRAFT_WORKSPACE_IGNORE</c>, separated by ';' or ',').</summary>
    public IReadOnlyList<string> WorkspaceIgnore { get; init; } = DefaultWorkspaceIgnore;

    /// <summary>The folder names a workspace skips by default.</summary>
    public static IReadOnlyList<string> DefaultWorkspaceIgnore { get; } = ["bin", "obj", ".git", ".vs", "node_modules", "test-output"];
    /// <summary>Milliseconds between the caret's blinks; 0 shows it solid (<c>CRAFT_CARET_BLINK_MS</c>).</summary>
    public int CaretBlinkMilliseconds { get; init; } = 530;

    /// <summary>The caret bar's width in device-independent units (<c>CRAFT_CARET_WIDTH</c>).</summary>
    public float CaretWidth { get; init; } = 2;

    /// <summary>Clicks closer in time than this many milliseconds count as a double or triple click (<c>CRAFT_DOUBLE_CLICK_MS</c>).</summary>
    public int DoubleClickMilliseconds { get; init; } = 500;

    /// <summary>Clicks farther apart than this, in device-independent units, start a new click count (<c>CRAFT_DOUBLE_CLICK_DISTANCE</c>).</summary>
    public float DoubleClickDistance { get; init; } = 4;

    /// <summary>The lines a bracket match scans each way from the caret before giving up (<c>CRAFT_BRACKET_SCAN_LINES</c>).</summary>
    public int BracketScanLines { get; init; } = 5000;

    /// <summary>The lines kept between the caret and the viewport's top or bottom edge when it is scrolled into view (<c>CRAFT_SCROLL_MARGIN_LINES</c>).</summary>
    public int ScrollMarginLines { get; init; } = 2;

    /// <summary>The columns kept between the caret and the viewport's left or right edge when it is scrolled into view (<c>CRAFT_SCROLL_MARGIN_COLUMNS</c>).</summary>
    public int ScrollMarginColumns { get; init; } = 4;

    /// <summary>The empty lines the editor can scroll past the last line (<c>CRAFT_BOTTOM_MARGIN_LINES</c>).</summary>
    public int BottomMarginLines { get; init; } = 3;

    /// <summary>The empty cells of the line-number gutter beside the widest number (<c>CRAFT_GUTTER_PADDING</c>).</summary>
    public int GutterPaddingCells { get; init; } = 2;

    /// <summary>Whether typing an opening bracket inserts its closing one too (<c>CRAFT_AUTO_CLOSE_BRACKETS</c>).</summary>
    public bool AutoCloseBrackets { get; init; }

    /// <summary>
    /// Whether Save formats the document first (its language's Format document, as Shift+Alt+F does: C#, XML, JSON), as
    /// one undo step; a malformed file is saved unformatted and said so. Off by default (<c>CRAFT_FORMAT_ON_SAVE</c>).
    /// </summary>
    public bool FormatOnSave { get; init; }

    /// <summary>Find stops counting matches at this many (the count then shows as "at least") (<c>CRAFT_FIND_COUNT_LIMIT</c>).</summary>
    public int FindCountLimit { get; init; } = 100_000;

    /// <summary>
    /// The milliseconds a frame waits on the UI thread for the language's parse of the text it draws (a parse off the UI
    /// thread); a longer parse draws the changed text plain and repaints when it lands. 0 never waits (<c>CRAFT_PARSE_WAIT_MS</c>).
    /// </summary>
    public int ParseWaitMilliseconds { get; init; } = 8;

    /// <summary>
    /// Milliseconds after the last change before the language computes the document's diagnostics again, off the UI
    /// thread (<c>CRAFT_DIAGNOSTICS_DELAY_MS</c>).
    /// </summary>
    public int DiagnosticsDelayMilliseconds { get; init; } = 400;

    /// <summary>
    /// Above this many characters a document's semantic layer (diagnostics, completion while typing) waits for a command
    /// instead of running on its own; 0 never waits (<c>CRAFT_SEMANTIC_MAX_CHARS</c>).
    /// </summary>
    public int SemanticMaxCharacters { get; init; } = 1_000_000;

    /// <summary>The workspace readers tried in order to make a folder's projects (<c>CRAFT_WORKSPACE_READERS</c>, separated by ',').</summary>
    public IReadOnlyList<string> WorkspaceReaders { get; init; } = DefaultWorkspaceReaders;

    /// <summary>The workspace readers tried by default: project files, then loose files.</summary>
    public static IReadOnlyList<string> DefaultWorkspaceReaders { get; } = ["csproj", "loose"];

    /// <summary>The build configuration a project is read for (<c>CRAFT_WORKSPACE_CONFIGURATION</c>).</summary>
    public string WorkspaceConfiguration { get; init; } = "Debug";

    /// <summary>
    /// Milliseconds after typing an identifier character or '.' before the completion list is asked for; Ctrl+Space asks
    /// at once (<c>CRAFT_COMPLETION_DELAY_MS</c>).
    /// </summary>
    public int CompletionDelayMilliseconds { get; init; } = 50;

    /// <summary>The rows the completion list shows before it scrolls (<c>CRAFT_COMPLETION_ROWS</c>).</summary>
    public int CompletionRows { get; init; } = 10;

    /// <summary>
    /// The inline completion provider the editor asks for ghost text (<c>CRAFT_INLINE_COMPLETION_PROVIDER</c>): a name in
    /// Craft.Abstractions' InlineCompletionProviders; empty: the first registered one that accepts the document; "none": off.
    /// </summary>
    public string InlineCompletionProvider { get; init; } = "";

    /// <summary>The chat and agent column's width in the wide layout, device-independent units; a splitter drag sets it per workspace (<c>CRAFT_PANEL_CHAT_WIDTH</c>).</summary>
    public float ChatWidth { get; init; } = 360;

    /// <summary>Milliseconds the pointer rests on a symbol before its description shows (<c>CRAFT_HOVER_DELAY_MS</c>).</summary>
    public int HoverDelayMilliseconds { get; init; } = 500;

    /// <summary>The most rows the problems list shows (the counts stay exact) (<c>CRAFT_PROBLEMS_LIMIT</c>).</summary>
    public int ProblemsListLimit { get; init; } = 500;

    /// <summary>Whether the projects' source generators run in the semantic model (<c>CRAFT_SOURCE_GENERATORS</c>).</summary>
    public bool SourceGenerators { get; init; } = true;

    /// <summary>
    /// Milliseconds after a change before the visible lines are classified by meaning, off the UI thread; -1 turns
    /// semantic colours off (<c>CRAFT_SEMANTIC_COLORS_DELAY_MS</c>).
    /// </summary>
    public int SemanticColorsDelayMilliseconds { get; init; } = 30;

    /// <summary>
    /// The memory-pressure monitor, by name in <see cref="MemoryPressureMonitors"/>: when memory runs low the languages free
    /// what they can build again ("gc" by default; "none" never) (<c>CRAFT_MEMORY_MONITOR</c>).
    /// </summary>
    public string MemoryMonitor { get; init; } = MemoryPressureMonitors.Gc;

    /// <summary>The settings a language's semantic layer is opened with (these options, carried to the language).</summary>
    public LanguageSettings ToLanguageSettings() => new()
    {
        DiagnosticsDelayMilliseconds = DiagnosticsDelayMilliseconds,
        SemanticMaxCharacters = SemanticMaxCharacters,
        WorkspaceReaders = WorkspaceReaders,
        Configuration = WorkspaceConfiguration,
        TabSize = TabSize,
        InsertSpaces = InsertSpaces,
        SourceGenerators = SourceGenerators,
        SemanticColorsDelayMilliseconds = SemanticColorsDelayMilliseconds,
    };

    /// <summary>
    /// Folders of user plug-ins (each holding a <c>Name/Name.dll</c> folder per plug-in), loaded after the app's own
    /// <c>plugins</c> folder; none by default, since a plug-in is code (<c>CRAFT_PLUGIN_FOLDERS</c>, separated by ';').
    /// </summary>
    public IReadOnlyList<string> PluginFolders { get; init; } = [];

    /// <summary>The plug-ins (by folder name, ignoring case) not loaded, the pre-installed ones included (<c>CRAFT_PLUGINS_DISABLED</c>, separated by ',').</summary>
    public IReadOnlyList<string> DisabledPlugins { get; init; } = [];

    /// <summary>
    /// The user's key bindings, over the plug-ins' and the app's (<c>CRAFT_KEY_BINDINGS</c>: "Chord=command" separated by
    /// ';', "Ctrl+Shift+W=sample.wordCount;Ctrl+K="): a user's binding runs before the editor's own keys; an empty command
    /// unbinds the chord from its plug-in (the editor's own key, if any, stays).
    /// </summary>
    public IReadOnlyList<KeyBinding> KeyBindings { get; init; } = [];

    /// <summary>
    /// The keymap of the default shortcuts, by name in <c>EditorKeyMaps</c> (<c>CRAFT_KEYMAP</c>: "vscode", the default,
    /// or "visualstudio"); the user's own bindings come before it. A name not registered is reported, and no keymap applies.
    /// </summary>
    public string KeyMap { get; init; } = EditorKeyMaps.Default;

    /// <summary>What zooming in or out adds to or takes from the code font's size (<c>CRAFT_ZOOM_STEP</c>).</summary>
    public float ZoomStep { get; init; } = 1;

    /// <summary>The lines of the editor host's log kept for the log view (<c>CRAFT_HOST_LOG_LINES</c>); 0 keeps none.</summary>
    public int HostLogLines { get; init; } = 500;

    /// <summary>The characters of an edit's new text a proposal's confirmation shows before it cuts (<c>CRAFT_PROPOSAL_PREVIEW_CHARS</c>).</summary>
    public int ProposalPreviewCharacters { get; init; } = 200;

    // Phase A1: the diff-and-accept view.

    /// <summary>The unchanged lines a proposed edit's diff shows around each change (<c>CRAFT_DIFF_CONTEXT_LINES</c>).</summary>
    public int DiffContextLines { get; init; } = 3;

    /// <summary>
    /// The changed lines (removed plus added) a diff aligns line by line; a longer changed run is shown removed then added,
    /// so the work stays bounded (<c>CRAFT_DIFF_LINE_LIMIT</c>).
    /// </summary>
    public int DiffLineLimit { get; init; } = 2000;

    /// <summary>Whether the diff view asks the language for its diagnostics after the change before the user accepts (<c>CRAFT_DIFF_CHECK</c>).</summary>
    public bool DiffCheck { get; init; } = true;

    // Phase E4: the app's shell (its data folder, the recent list, the layout's widths).

    /// <summary>
    /// The folder the app keeps its own files in (the recent list, the user's <c>settings.json</c>); null for the user's
    /// local application data folder, under "Craft". Read from the environment only (<c>CRAFT_DATA_FOLDER</c>).
    /// </summary>
    public string? DataFolder { get; init; }

    /// <summary>The recent files and folders kept; 0 keeps none (<c>CRAFT_RECENT_LIMIT</c>).</summary>
    public int RecentLimit { get; init; } = 20;

    /// <summary>
    /// The window width, in device-independent units, below which the shell shows its narrow (phone) layout: the folder
    /// tree in place of the editor, the tabs as one switcher, no rail, side views or bottom panel. Measured from the window, never the platform (<c>CRAFT_NARROW_WIDTH</c>).
    /// </summary>
    public float NarrowWidth { get; init; } = 720;

    /// <summary>The explorer's (the side views') width in the wide layout, device-independent units; a splitter drag sets it per workspace (<c>CRAFT_PANEL_EXPLORER_WIDTH</c>).</summary>
    public float TreeWidth { get; init; } = 260;

    /// <summary>The indent of each level of the folder tree, in device-independent units (<c>CRAFT_TREE_INDENT</c>).</summary>
    public float TreeIndent { get; init; } = 14;

    /// <summary>Whether the explorer (the rail's side view) is open in the wide layout; Ctrl+B and the rail set it per workspace (<c>CRAFT_PANEL_EXPLORER_OPEN</c>).</summary>
    public bool ExplorerOpen { get; init; } = true;

    /// <summary>Whether the chat and agent column is open in the wide layout; its close button and Ctrl+Alt+I set it per workspace (<c>CRAFT_PANEL_CHAT_OPEN</c>).</summary>
    public bool ChatOpen { get; init; } = true;

    /// <summary>Whether the bottom panel is open; its chevron, Ctrl+J and a click on its active tab set it per workspace (<c>CRAFT_PANEL_BOTTOM_OPEN</c>).</summary>
    public bool BottomPanelOpen { get; init; } = true;

    /// <summary>The smallest size a splitter drag leaves a panel, and the least it leaves the editor, device-independent units (<c>CRAFT_PANEL_MIN_SIZE</c>).</summary>
    public float PanelMinimumSize { get; init; } = 120;

    /// <summary>What tells the folder tree a file or folder was made, removed or renamed (<c>CRAFT_WORKSPACE_WATCHER</c>): system, none, or a registered watcher.</summary>
    public string WorkspaceWatcher { get; init; } = "system";

    /// <summary>Milliseconds the folder tree gathers the disk's changes before it reads the folders they touched once (<c>CRAFT_WORKSPACE_WATCH_MS</c>).</summary>
    public int WorkspaceWatchMilliseconds { get; init; } = 150;

    // Phase D: the approved design's shell.

    /// <summary>The bottom panel's height (Problems, Terminal, Output, Idrak telemetry), device-independent units; a splitter drag sets it per workspace (<c>CRAFT_PANEL_BOTTOM_HEIGHT</c>).</summary>
    public float BottomPanelHeight { get; init; } = 132;

    /// <summary>The most files quick open (Ctrl+P) lists from the workspace; the rest are said to be left out (<c>CRAFT_QUICK_OPEN_FILES</c>).</summary>
    public int QuickOpenFiles { get; init; } = 200_000;

    /// <summary>The rows quick open shows (<c>CRAFT_QUICK_OPEN_ROWS</c>).</summary>
    public int QuickOpenRows { get; init; } = 12;

    /// <summary>The smallest touch target of the narrow layout, in device-independent units (<c>CRAFT_TOUCH_TARGET</c>; the design's 44).</summary>
    public float TouchTarget { get; init; } = 44;

    /// <summary>
    /// Where the title bar's row (the logo, the menus, the search box) goes (<c>CRAFT_TITLE_BAR</c>):
    /// "window", in the window's own caption where the platform lets an app draw there, or "page", a row under the caption.
    /// </summary>
    public string TitleBar { get; init; } = "window";

    // Phase T: the integrated terminal. Each is read when a terminal opens, so a change applies to the next one.

    /// <summary>
    /// The terminal's shell (<c>CRAFT_TERMINAL_SHELL</c>): a program on the PATH or a full path; null for the terminal
    /// host's default (on Windows pwsh when on the PATH, else Windows PowerShell, else cmd; elsewhere $SHELL, else /bin/sh).
    /// </summary>
    public string? TerminalShell { get; init; }

    /// <summary>The shell's arguments, as one command line (<c>CRAFT_TERMINAL_ARGS</c>); null for the host's default ones.</summary>
    public string? TerminalArguments { get; init; }

    /// <summary>
    /// Variables added to the terminal's environment over the app's own (<c>CRAFT_TERMINAL_ENV</c>: <c>NAME=value</c>
    /// entries separated by '|'; <c>${NAME}</c> in a value is the app's value of NAME; an empty value removes the variable).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> TerminalEnvironment { get; init; } = [];

    /// <summary>
    /// The folder every terminal starts in (<c>CRAFT_TERMINAL_CWD</c>), over the rule (the open folder's root, else the
    /// shell's home); null keeps the rule. A folder chosen in the explorer ("Open in terminal") still starts there.
    /// </summary>
    public string? TerminalWorkingDirectory { get; init; }

    /// <summary>The lines a terminal keeps above its screen (<c>CRAFT_TERMINAL_SCROLLBACK</c>); its memory stays flat at that many.</summary>
    public int TerminalScrollback { get; init; } = 5000;

    /// <summary>The terminal host (<c>CRAFT_TERMINAL_HOST</c>): auto (the platform's: conpty on Windows; else pipe), or a registered one.</summary>
    public string TerminalHost { get; init; } = "auto";

    /// <summary>A terminal row's height as a multiple of the code font's height (<c>CRAFT_TERMINAL_LINE_SPACING</c>).</summary>
    public float TerminalLineSpacing { get; init; } = 1.3f;

    /// <summary>
    /// The commands whose key bindings still run while a terminal has the keyboard (<c>CRAFT_TERMINAL_EDITOR_COMMANDS</c>,
    /// separated by ','); every other chord goes to the shell. The terminal's own commands always keep theirs.
    /// </summary>
    public IReadOnlyList<string> TerminalEditorCommands { get; init; } = ["craft.commandPalette", "craft.quickOpen", "craft.togglePanel", "craft.settings"];

    /// <summary>
    /// Milliseconds between the UI watchdog's ticks through the dispatcher (<c>CRAFT_UI_WATCHDOG_MS</c>, phase U); 0
    /// turns the watchdog off.
    /// </summary>
    public int UiWatchdogMilliseconds { get; init; } = 100;

    /// <summary>A tick later than this many milliseconds is a stall of the UI thread, reported (<c>CRAFT_UI_STALL_MS</c>).</summary>
    public int UiStallMilliseconds { get; init; } = 200;

    /// <summary>The most stall reports written to the crash folder in a session (<c>CRAFT_UI_STALL_REPORTS</c>); 0 writes none (they still show).</summary>
    public int UiStallReports { get; init; } = 20;

    /// <summary>The notifications the centre keeps for the session, running ones and status entries aside (<c>CRAFT_NOTIFICATIONS_KEEP</c>).</summary>
    public int NotificationsKeep { get; init; } = 200;

    /// <summary>Milliseconds a toast (a failure, a question) shows before it goes to the centre (<c>CRAFT_TOAST_MS</c>); 0 shows none.</summary>
    public int ToastMilliseconds { get; init; } = 6000;

    /// <summary>
    /// An activity that ends within this many milliseconds shows neither in the status bar nor in the centre's history
    /// (a failure always does) (<c>CRAFT_ACTIVITY_MIN_MS</c>).
    /// </summary>
    public int ActivityMinimumMilliseconds { get; init; } = 300;

    /// <summary>The fewest milliseconds between two updates of the activities and notifications on screen (<c>CRAFT_ACTIVITY_FRAME_MS</c>; 16: once a frame).</summary>
    public int ActivityFrameMilliseconds { get; init; } = 16;

    /// <summary>
    /// Whether the run keeps a breadcrumb trail in the crash folder (<c>CRAFT_CRASH_TRAIL</c>, the owner's note 21): the
    /// last UI work, commands, key names (never the text typed), first-chance exceptions and Output lines, flushed as they
    /// happen, for the problem report after a crash.
    /// </summary>
    public bool CrashTrail { get; init; } = true;

    /// <summary>The most the trail's two files hold together, in KiB (<c>CRAFT_CRASH_TRAIL_KB</c>).</summary>
    public int CrashTrailKilobytes { get; init; } = 512;

    /// <summary>The most first-chance exceptions the trail records a second (<c>CRAFT_CRASH_TRAIL_EXCEPTIONS</c>); 0 records none.</summary>
    public int CrashTrailExceptionsPerSecond { get; init; } = 20;

    /// <summary>The trail's last lines a problem report holds (<c>CRAFT_CRASH_TICKET_TRAIL_LINES</c>).</summary>
    public int CrashTicketTrailLines { get; init; } = 600;

    /// <summary>Whether the next start after a run that did not close cleanly offers a problem report (<c>CRAFT_CRASH_TICKET_OFFER</c>).</summary>
    public bool CrashTicketOffer { get; init; } = true;

    /// <summary>The runs that died kept with their trails for a report (<c>CRAFT_CRASH_RUNS_KEPT</c>); older ones are removed.</summary>
    public int CrashRunsKept { get; init; } = 5;

    /// <summary>What reads the platform's record of a crash (<c>CRAFT_CRASH_RECORDS</c>): auto (the platform's: Windows' event log), none, or a registered source.</summary>
    public string CrashRecords { get; init; } = "auto";

    /// <summary>Whether Ctrl+C in a terminal copies its selection when there is one (else it is the shell's interrupt) (<c>CRAFT_TERMINAL_CTRL_C_COPIES</c>).</summary>
    public bool TerminalCtrlCCopies { get; init; } = true;

    /// <summary>The data folder as a full path: <see cref="DataFolder"/>, else the local application data folder's "Craft".</summary>
    public string ResolveDataFolder() =>
        Path.GetFullPath(DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Craft"));

    /// <summary>The environment variables, with their defaults and meanings, in order.</summary>
    public static IReadOnlyList<CraftVariable> Variables { get; } =
    [
        new("CRAFT_FONT", "JetBrains Mono", "The code font's family (bundled; one that does not draw monospaced gives way to CRAFT_FONT_FALLBACK).") { Shape = SettingShape.Font(SettingSections.Appearance, "Code font", "Fonts") },
        new("CRAFT_FONT_SIZE", "13", "The code font's size, in device-independent units.") { Shape = SettingShape.Number(SettingSections.Appearance, "Code font size", 1, 400, 0.5, "units", "Fonts") },
        new("CRAFT_LINE_SPACING", "1.69", "A line's height as a multiple of the font's height (1.69 at 13: 22-unit lines).") { Shape = SettingShape.Number(SettingSections.Appearance, "Line height", 0.5, 5, 0.01, "× the font", "Fonts") },
        new("CRAFT_FONT_FALLBACK", "Cascadia Mono, Consolas", "The code fonts tried in order (separated by commas) when CRAFT_FONT does not draw monospaced on this platform.") { Shape = SettingShape.ListOf(SettingSections.Appearance, "Code font fallbacks", "Fonts") },
        new("CRAFT_INTERFACE_FONT", "Inter", "The interface's font family (bundled).") { Shape = SettingShape.Font(SettingSections.Appearance, "Interface font", "Fonts") },
        new("CRAFT_INTERFACE_FONT_SIZE", "13", "The interface's text size, in device-independent units.") { Shape = SettingShape.Number(SettingSections.Appearance, "Interface text size", 6, 72, 0.5, "units", "Fonts") },
        new("CRAFT_ACCENT", "(the theme's)", "The accent: amber, teal, blue, rose, or #RRGGBB; moved until it reads at 4.5:1 on the theme.") { Shape = SettingShape.From(SettingSections.Appearance, "Accent", SettingChoiceSources.Accents, [""], true, "Theme") },
        new("CRAFT_TAB_SIZE", "4", "The columns a tab spans.") { Shape = SettingShape.Whole(SettingSections.Editor, "Tab size", 1, 32, "columns", "Typing") },
        new("CRAFT_INSERT_SPACES", "true", "Whether Tab inserts spaces rather than a tab character.") { Shape = SettingShape.Toggle(SettingSections.Editor, "Insert spaces for Tab", "Typing") },
        new("CRAFT_THEME", "dark", "The theme's name (dark, light, or a registered one).") { Shape = SettingShape.From(SettingSections.Appearance, "Theme", SettingChoiceSources.Themes, null, false, "Theme") },
        new("CRAFT_AUTOSAVE_SECONDS", "30", "Seconds between autosaves of a recovery copy; 0 turns autosave off.") { Shape = SettingShape.Whole(SettingSections.Files, "Autosave a recovery copy every", 0, 86_400, "s", "Recovery") },
        new("CRAFT_UNDO_GROUP_MS", "1000", "Typing with pauses shorter than this is one undo step.") { Shape = SettingShape.Whole(SettingSections.Editor, "Undo groups typing paused less than", 0, 60_000, "ms", "Typing", step: 100) },
        new("CRAFT_RECOVERY_FOLDER", "(local app data)/Craft/recovery", "Where recovery copies are kept.") { Shape = SettingShape.PathTo(SettingSections.Files, "Recovery folder", "Recovery") },
        new("CRAFT_CRASH_FOLDER", "(local app data)/Craft/crashes", "Where crash reports are written (the crash dialog names the file).") { Shape = SettingShape.PathTo(SettingSections.Files, "Crash reports folder", "Recovery") },
        new("CRAFT_BUFFER_CHUNK", "65536", "The characters of each block a text buffer's add buffer grows by.") { Shape = SettingShape.Whole(SettingSections.Editor, "Text buffer block", 16, 64 * 1024 * 1024, "characters", "Advanced", step: 4096) },
        new("CRAFT_LINE_ENDING", "auto", "The line break of a new document: lf, crlf, or auto (the running system's).") { Shape = SettingShape.Choice(SettingSections.Files, "Line ending of a new file", ["auto", "lf", "crlf"], "Files") },
        new("CRAFT_PLUGIN_FOLDERS", "(none)", "Folders of user plug-ins (a Name/Name.dll folder each), loaded after the app's own; separated by ';'.") { Shape = SettingShape.ListOf(SettingSections.Plugins, "Plug-in folders", "Loading") },
        new("CRAFT_PLUGINS_DISABLED", "(none)", "Plug-ins not loaded, by folder name (Craft.CSharp), the pre-installed ones included; separated by ','.") { Shape = SettingShape.ListOf(SettingSections.Plugins, "Disabled plug-ins", "Loading") },
        new("CRAFT_WORKSPACE_IGNORE", "bin;obj;.git;.vs;node_modules;test-output", "The folder names a workspace skips when it lists its files, separated by ';' or ','.") { Shape = SettingShape.ListOf(SettingSections.Files, "Folders the workspace skips", "Workspace") },
        new("CRAFT_CARET_BLINK_MS", "530", "Milliseconds between the caret's blinks; 0 shows it solid.") { Shape = SettingShape.Whole(SettingSections.Editor, "Caret blink", 0, 10_000, "ms", "Caret", step: 10) },
        new("CRAFT_CARET_WIDTH", "2", "The caret bar's width, in device-independent units.") { Shape = SettingShape.Number(SettingSections.Editor, "Caret width", 0.5, 50, 0.5, "units", "Caret") },
        new("CRAFT_DOUBLE_CLICK_MS", "500", "Clicks closer in time than this count as a double or triple click.") { Shape = SettingShape.Whole(SettingSections.Editor, "Double-click time", 0, 10_000, "ms", "Mouse", step: 10) },
        new("CRAFT_DOUBLE_CLICK_DISTANCE", "4", "Clicks farther apart than this (device-independent units) start a new click count.") { Shape = SettingShape.Number(SettingSections.Editor, "Double-click distance", 0, 1000, 1, "units", "Mouse") },
        new("CRAFT_BRACKET_SCAN_LINES", "5000", "The lines a bracket match scans each way before giving up.") { Shape = SettingShape.Whole(SettingSections.Editor, "Bracket match scans", 0, 100_000_000, "lines", "Advanced", step: 100) },
        new("CRAFT_SCROLL_MARGIN_LINES", "2", "Lines kept between the caret and the viewport's top or bottom when scrolled into view.") { Shape = SettingShape.Whole(SettingSections.Editor, "Scroll margin", 0, 1000, "lines", "Scrolling") },
        new("CRAFT_SCROLL_MARGIN_COLUMNS", "4", "Columns kept between the caret and the viewport's left or right when scrolled into view.") { Shape = SettingShape.Whole(SettingSections.Editor, "Horizontal scroll margin", 0, 1000, "columns", "Scrolling") },
        new("CRAFT_BOTTOM_MARGIN_LINES", "3", "Empty lines the editor can scroll past the last line.") { Shape = SettingShape.Whole(SettingSections.Editor, "Scroll past the end", 0, 10_000, "lines", "Scrolling") },
        new("CRAFT_GUTTER_PADDING", "2", "Empty cells of the line-number gutter beside the widest number.") { Shape = SettingShape.Whole(SettingSections.Appearance, "Line number padding", 0, 100, "cells", "Editor") },
        new("CRAFT_AUTO_CLOSE_BRACKETS", "false", "Whether typing an opening bracket inserts its closing one too.") { Shape = SettingShape.Toggle(SettingSections.Editor, "Close brackets as you type", "Typing") },
        new("CRAFT_FORMAT_ON_SAVE", "false", "Whether Save formats the document first (C#, XML, JSON), as one undo step; a malformed file is saved as it is.") { Shape = SettingShape.Toggle(SettingSections.Editor, "Format on save", "Formatting") },
        new("CRAFT_FIND_COUNT_LIMIT", "100000", "Find stops counting matches at this many.") { Shape = SettingShape.Whole(SettingSections.Editor, "Find counts at most", 1, int.MaxValue, "matches", "Find", step: 1000) },
        new("CRAFT_PARSE_WAIT_MS", "8", "Milliseconds a frame waits for the language's parse of its text; longer parses paint the changed text plain, then repaint. 0 never waits.") { Shape = SettingShape.Whole(SettingSections.Editor, "A frame waits for the parse", 0, 10_000, "ms", "Advanced") },
        new("CRAFT_KEY_BINDINGS", "(none)", "The user's key bindings, 'Keys=command' separated by ';' (Ctrl+Shift+W=sample.wordCount;Ctrl+K Ctrl+C=craft.editor.comment); the keyboard section writes them; they run before the editor's keys; an empty command unbinds a plug-in's chord.") { Shape = new SettingShape(SettingKind.KeyBindings, SettingSections.Keyboard, "Your key bindings") },
        new("CRAFT_HOST_LOG_LINES", "500", "Lines of the editor host's log (plug-ins' lines included) kept for the log view; 0 keeps none.") { Shape = SettingShape.Whole(SettingSections.Plugins, "Host log keeps", 0, 1_000_000, "lines", "Log", step: 100) },
        new("CRAFT_PROPOSAL_PREVIEW_CHARS", "200", "Characters of a proposed edit's new text its confirmation shows before it cuts.") { Shape = SettingShape.Whole(SettingSections.Agent, "Proposal preview", 0, 1_000_000, "characters", "Proposals", step: 10) },
        new("CRAFT_DIFF_CONTEXT_LINES", "3", "Unchanged lines a proposed edit's diff shows around each change.") { Shape = SettingShape.Whole(SettingSections.Agent, "Diff context", 0, 1000, "lines", "Diff view") },
        new("CRAFT_DIFF_LINE_LIMIT", "2000", "Changed lines a diff aligns line by line; a longer run is shown removed then added.") { Shape = SettingShape.Whole(SettingSections.Agent, "Diff aligns at most", 0, 100_000, "lines", "Diff view", step: 100) },
        new("CRAFT_DIFF_CHECK", "true", "Whether the diff view shows the language's errors and warnings after the change before it is accepted.") { Shape = SettingShape.Toggle(SettingSections.Agent, "Check the change before accepting", "Diff view") },
        new("CRAFT_DATA_FOLDER", "(local app data)/Craft", "The folder of the app's own files (the recent list, the user's settings.json); from the environment only.") { Shape = SettingShape.PathTo(SettingSections.Files, "Data folder", "Files") },
        new("CRAFT_RECENT_LIMIT", "20", "The recent files and folders kept; 0 keeps none.") { Shape = SettingShape.Whole(SettingSections.Files, "Recent files and folders kept", 0, 10_000, null, "Files") },
        new("CRAFT_NARROW_WIDTH", "720", "The window width (device-independent units) below which the narrow (phone) layout shows.") { Shape = SettingShape.Number(SettingSections.Appearance, "Narrow layout below", 0, 100_000, 10, "units", "Layout") },
        new("CRAFT_PANEL_EXPLORER_WIDTH", "260", "The explorer's (the rail's side views') width in the wide layout, device-independent units; a splitter drag writes it to the workspace's settings.") { Shape = SettingShape.Number(SettingSections.Appearance, "Explorer width", 40, 10_000, 10, "units", "Layout") },
        new("CRAFT_TREE_INDENT", "14", "The indent of each level of the folder tree (device-independent units).") { Shape = SettingShape.Number(SettingSections.Files, "Tree indent", 0, 200, 1, "units", "Tree") },
        new("CRAFT_PANEL_EXPLORER_OPEN", "true", "Whether the explorer (the rail's side view) is open in the wide layout; Ctrl+B and the rail write it to the workspace's settings.") { Shape = SettingShape.Toggle(SettingSections.Appearance, "Explorer open", "Layout") },
        new("CRAFT_PANEL_CHAT_OPEN", "true", "Whether the chat and agent column is open in the wide layout; its close button and Ctrl+Alt+I write it to the workspace's settings.") { Shape = SettingShape.Toggle(SettingSections.Appearance, "Chat and agent column open", "Layout") },
        new("CRAFT_PANEL_BOTTOM_OPEN", "true", "Whether the bottom panel is open; its chevron, Ctrl+J and a click on its active tab write it to the workspace's settings.") { Shape = SettingShape.Toggle(SettingSections.Appearance, "Bottom panel open", "Layout") },
        new("CRAFT_PANEL_MIN_SIZE", "120", "The smallest size a splitter drag leaves a panel, and the least it leaves the editor (device-independent units).") { Shape = SettingShape.Number(SettingSections.Appearance, "Smallest panel", 0, 10_000, 10, "units", "Layout") },
        new("CRAFT_PANEL_BOTTOM_HEIGHT", "132", "The bottom panel's height (Problems, Terminal, Output, Idrak telemetry), device-independent units; a splitter drag writes it to the workspace's settings.") { Shape = SettingShape.Number(SettingSections.Appearance, "Bottom panel height", 40, 10_000, 10, "units", "Layout") },
        new("CRAFT_WORKSPACE_WATCHER", "system", "What tells the folder tree a file or folder was made, removed or renamed: system (the platform's file watcher), none, or a registered watcher.") { Shape = SettingShape.From(SettingSections.Files, "Watch the disk with", SettingChoiceSources.FolderWatchers, null, true, "Tree") },
        new("CRAFT_WORKSPACE_WATCH_MS", "150", "Milliseconds the folder tree gathers the disk's changes before it reads the folders they touched, once.") { Shape = SettingShape.Whole(SettingSections.Files, "Gather disk changes for", 0, 60_000, "ms", "Tree", step: 10) },
        new("CRAFT_QUICK_OPEN_FILES", "200000", "The most files quick open (Ctrl+P) lists from the workspace.") { Shape = SettingShape.Whole(SettingSections.Files, "Quick open lists at most", 0, 100_000_000, "files", "Quick open", step: 1000) },
        new("CRAFT_QUICK_OPEN_ROWS", "12", "The rows quick open shows.") { Shape = SettingShape.Whole(SettingSections.Files, "Quick open rows", 1, 200, "rows", "Quick open") },
        new("CRAFT_TOUCH_TARGET", "44", "The smallest touch target of the narrow layout (device-independent units).") { Shape = SettingShape.Number(SettingSections.Appearance, "Touch target (narrow layout)", 16, 200, 1, "units", "Layout") },
        new("CRAFT_TITLE_BAR", "window", "Where the title bar's row goes: window (in the window's caption) or page (a row under it).") { Shape = SettingShape.Choice(SettingSections.Appearance, "Title bar", ["window", "page"], "Layout") },
        new("CRAFT_DIAGNOSTICS_DELAY_MS", "400", "Milliseconds after the last change before the document's diagnostics are computed again (off the UI thread).") { Shape = SettingShape.Whole(SettingSections.CSharp, "Diagnostics after a pause of", 0, 600_000, "ms", "Diagnostics", step: 50) },
        new("CRAFT_SEMANTIC_MAX_CHARS", "1000000", "Above this many characters the semantic layer (diagnostics, completion while typing) waits for a command; 0 never waits.") { Shape = SettingShape.Whole(SettingSections.CSharp, "Semantic layer waits above", 0, int.MaxValue, "characters", "Diagnostics", step: 100000) },
        new("CRAFT_WORKSPACE_READERS", "csproj,loose", "The workspace readers tried in order to make a folder's projects, separated by ','.") { Shape = SettingShape.ListOf(SettingSections.CSharp, "Workspace readers", "Workspace") },
        new("CRAFT_WORKSPACE_CONFIGURATION", "Debug", "The build configuration a project is read for (Debug defines DEBUG).") { Shape = SettingShape.Choice(SettingSections.CSharp, "Build configuration", ["Debug", "Release"], "Workspace") with { OtherAllowed = true } },
        new("CRAFT_COMPLETION_DELAY_MS", "50", "Milliseconds after typing an identifier character or '.' before the completion list is asked for.") { Shape = SettingShape.Whole(SettingSections.Completion, "List after typing", 0, 60_000, "ms", "Completion list", step: 10) },
        new("CRAFT_COMPLETION_ROWS", "10", "The rows the completion list shows before it scrolls.") { Shape = SettingShape.Whole(SettingSections.Completion, "List rows", 1, 100, "rows", "Completion list") },
        new("CRAFT_INLINE_COMPLETION_PROVIDER", "", "The inline completion (ghost text) provider: a registered name; empty: the first that accepts the document; none: off.") { Shape = SettingShape.From(SettingSections.Completion, "Inline completion (ghost text)", SettingChoiceSources.InlineCompletion, ["", "none"], true, "Inline completion") },
        new("CRAFT_PANEL_CHAT_WIDTH", "360", "The chat and agent column's width in the wide layout, device-independent units; a splitter drag writes it to the workspace's settings.") { Shape = SettingShape.Number(SettingSections.Appearance, "Chat column width", 120, 10_000, 10, "units", "Layout") },
        new("CRAFT_PROBLEMS_LIMIT", "500", "The most rows the problems list shows (the counts stay exact).") { Shape = SettingShape.Whole(SettingSections.CSharp, "Problems list shows at most", 1, 1_000_000, "rows", "Diagnostics", step: 50) },
        new("CRAFT_HOVER_DELAY_MS", "500", "Milliseconds the pointer rests on a symbol before its description shows.") { Shape = SettingShape.Whole(SettingSections.CSharp, "Hover after", 0, 60_000, "ms", "Hover", step: 50) },
        new("CRAFT_SOURCE_GENERATORS", "true", "Whether the projects' source generators run in the semantic model (their generated code counts, as in the build).") { Shape = SettingShape.Toggle(SettingSections.CSharp, "Run source generators", "Workspace") },
        new("CRAFT_SEMANTIC_COLORS_DELAY_MS", "30", "Milliseconds after a change before the visible lines are coloured by meaning (off the UI thread); -1 turns semantic colours off.") { Shape = SettingShape.Whole(SettingSections.CSharp, "Semantic colours after (-1: off)", -1, 60_000, "ms", "Colours", step: 10) },
        new("CRAFT_MEMORY_MONITOR", "gc", "What tells the editor memory runs low, so the languages free what they can build again: gc (the runtime's memory load), none, or a registered one.") { Shape = SettingShape.From(SettingSections.Editor, "Memory monitor", SettingChoiceSources.MemoryMonitors, null, true, "Advanced") },
        new("CRAFT_TERMINAL_SHELL", "(the host's: pwsh, else Windows PowerShell, else cmd on Windows)", "The terminal's shell: a program on the PATH or a full path. A workspace cannot set it.") { Shape = SettingShape.Text(SettingSections.Terminal, "Shell", "Shell") },
        new("CRAFT_TERMINAL_ARGS", "(the host's)", "The shell's arguments, as one command line. A workspace cannot set it.") { Shape = SettingShape.Text(SettingSections.Terminal, "Shell arguments", "Shell") },
        new("CRAFT_TERMINAL_ENV", "(none)", "Variables added to the terminal's environment: NAME=value entries separated by '|'; ${NAME} is the app's value; an empty value removes it. A workspace cannot set it.") { Shape = SettingShape.Text(SettingSections.Terminal, "Environment", "Shell") },
        new("CRAFT_TERMINAL_CWD", "(the open folder, else the home folder)", "The folder every new terminal starts in, over the rule (the explorer's Open in terminal still starts in its folder).") { Shape = SettingShape.PathTo(SettingSections.Terminal, "Start folder", "Shell") },
        new("CRAFT_TERMINAL_SCROLLBACK", "5000", "The lines a terminal keeps above its screen; its memory stays flat at that many.") { Shape = SettingShape.Whole(SettingSections.Terminal, "Scrollback", 0, 1_000_000, "lines", "Screen", step: 500) },
        new("CRAFT_TERMINAL_HOST", "auto", "What runs the shell: auto (conpty on Windows, else pipe), pipe (plain redirected pipes), or a registered host. A workspace cannot set it.") { Shape = SettingShape.From(SettingSections.Terminal, "Host", SettingChoiceSources.TerminalHosts, ["auto"], true, "Shell") },
        new("CRAFT_TERMINAL_LINE_SPACING", "1.3", "A terminal row's height as a multiple of the code font's height.") { Shape = SettingShape.Number(SettingSections.Terminal, "Row height", 0.8, 4, 0.05, "× the font", "Screen") },
        new("CRAFT_TERMINAL_EDITOR_COMMANDS", "craft.commandPalette,craft.quickOpen,craft.togglePanel,craft.settings", "Commands whose keys still run while a terminal has the keyboard (every other chord goes to the shell), separated by ','.") { Shape = SettingShape.ListOf(SettingSections.Terminal, "Commands kept from the shell", "Keys") },
        new("CRAFT_UI_WATCHDOG_MS", "100", "Milliseconds between the UI watchdog's ticks through the dispatcher; 0 turns it off.") { Shape = SettingShape.Whole(SettingSections.Editor, "UI watchdog tick", 0, 10_000, "ms", "Responsiveness", step: 10) },
        new("CRAFT_UI_STALL_MS", "200", "A watchdog tick later than this many milliseconds is a stall of the window: shown, logged and written as a stall report.") { Shape = SettingShape.Whole(SettingSections.Editor, "A stall is longer than", 1, 600_000, "ms", "Responsiveness", step: 10) },
        new("CRAFT_UI_STALL_REPORTS", "20", "The most stall reports written to the crash folder in a session; 0 writes none (stalls still show).") { Shape = SettingShape.Whole(SettingSections.Editor, "Stall reports per session", 0, 100_000, null, "Responsiveness") },
        new("CRAFT_NOTIFICATIONS_KEEP", "200", "The notifications the centre keeps for the session (running operations and status entries aside).") { Shape = SettingShape.Whole(SettingSections.Appearance, "Notifications kept", 0, 100_000, null, "Notifications", step: 10) },
        new("CRAFT_TOAST_MS", "6000", "Milliseconds a toast (a failure, a question) shows before it waits in the centre; 0 shows none.") { Shape = SettingShape.Whole(SettingSections.Appearance, "Toasts show for", 0, 600_000, "ms", "Notifications", step: 500) },
        new("CRAFT_ACTIVITY_MIN_MS", "300", "An operation that ends within this many milliseconds shows neither in the status bar nor in the centre (a failure always does).") { Shape = SettingShape.Whole(SettingSections.Appearance, "Operations show after", 0, 600_000, "ms", "Notifications", step: 50) },
        new("CRAFT_ACTIVITY_FRAME_MS", "16", "The fewest milliseconds between two updates of the running operations and notifications on screen.") { Shape = SettingShape.Whole(SettingSections.Appearance, "Activity updates at most every", 1, 1000, "ms", "Notifications") },
        new("CRAFT_KEYMAP", "vscode", "The default shortcuts: vscode (VS Code's), visualstudio (Visual Studio's where the two differ), or a registered keymap; your own bindings come first.") { Shape = SettingShape.From(SettingSections.Keyboard, "Keymap", SettingChoiceSources.KeyMaps, null, false, null) },
        new("CRAFT_ZOOM_STEP", "1", "What Ctrl+= and Ctrl+- add to or take from the code font's size (written to your settings; Ctrl+0 takes it back).") { Shape = SettingShape.Number(SettingSections.Appearance, "Zoom step (Ctrl+= and Ctrl+-)", 0.5, 10, 0.5, "units", "Fonts") },
        // The owner's note 21: the crash reporter that needs no hook (the run marker, the trail, the problem report).
        new("CRAFT_CRASH_TRAIL", "true", "Whether the run keeps a breadcrumb trail beside the crash reports (the last UI work, commands, key names never the text typed, first-chance exceptions, Output lines), flushed as it happens, for the problem report after a crash. Applied at the next start.") { Shape = SettingShape.Toggle(SettingSections.Files, "Keep a breadcrumb trail for crash reports", "Crash reports") },
        new("CRAFT_CRASH_TRAIL_KB", "512", "The most the trail's two files hold together, in KiB (the older half is dropped first).") { Shape = SettingShape.Whole(SettingSections.Files, "Trail size", 16, 1_048_576, "KiB", "Crash reports", step: 64) },
        new("CRAFT_CRASH_TRAIL_EXCEPTIONS", "20", "The most first-chance exceptions the trail records a second (cancellations are never recorded); 0 records none.") { Shape = SettingShape.Whole(SettingSections.Files, "Exceptions recorded a second", 0, 10_000, null, "Crash reports") },
        new("CRAFT_CRASH_TICKET_TRAIL_LINES", "600", "The trail's last lines a problem report holds.") { Shape = SettingShape.Whole(SettingSections.Files, "Trail lines in a report", 0, 1_000_000, "lines", "Crash reports", step: 100) },
        new("CRAFT_CRASH_TICKET_OFFER", "true", "Whether the start after a run that did not close cleanly offers to write a problem report (Help › Report a problem always can).") { Shape = SettingShape.Toggle(SettingSections.Files, "Offer a report after a crash", "Crash reports") },
        new("CRAFT_CRASH_RUNS_KEPT", "5", "The runs that did not close cleanly kept with their trails for a report; older ones are removed.") { Shape = SettingShape.Whole(SettingSections.Files, "Crashed runs kept", 0, 1000, null, "Crash reports") },
        new("CRAFT_CRASH_RECORDS", "auto", "What reads the platform's own record of a crash for the report: auto (Windows' Application event log on Windows), none, or a registered source.") { Shape = SettingShape.Text(SettingSections.Files, "Read the platform's crash record with", "Crash reports") },
        new("CRAFT_TERMINAL_CTRL_C_COPIES", "true", "Whether Ctrl+C in a terminal copies the selection when there is one (with none it is the shell's interrupt, ^C).") { Shape = SettingShape.Toggle(SettingSections.Terminal, "Ctrl+C copies the selection", "Keys") },
    ];

    /// <summary>
    /// <paramref name="baseline"/> (the defaults when null) with every variable that is set read over it. A value that does
    /// not parse throws, naming the variable and what it takes; nothing is guessed.
    /// </summary>
    /// <exception cref="ArgumentException">A variable's value does not parse or is out of range.</exception>
    public static CraftOptions FromEnvironment(CraftOptions? baseline = null) => FromVariables(Environment.GetEnvironmentVariable, baseline);

    /// <summary>As <see cref="FromEnvironment"/>, reading the variables through <paramref name="read"/> (tests pass a dictionary).</summary>
    public static CraftOptions FromVariables(Func<string, string?> read, CraftOptions? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        var options = baseline ?? new CraftOptions();
        if (Text(read, "CRAFT_FONT") is { } font)
        {
            options = options with { FontFamily = font };
        }

        if (Text(read, "CRAFT_FONT_SIZE") is { } size)
        {
            options = options with { FontSize = Positive(size, "CRAFT_FONT_SIZE", 1, 400) };
        }

        if (Text(read, "CRAFT_LINE_SPACING") is { } spacing)
        {
            options = options with { LineSpacing = Positive(spacing, "CRAFT_LINE_SPACING", 0.5f, 5) };
        }

        if (Text(read, "CRAFT_TAB_SIZE") is { } tab)
        {
            options = options with { TabSize = Whole(tab, "CRAFT_TAB_SIZE", 1, 32) };
        }

        if (Text(read, "CRAFT_INSERT_SPACES") is { } spaces)
        {
            options = options with { InsertSpaces = Flag(spaces, "CRAFT_INSERT_SPACES") };
        }

        if (Text(read, "CRAFT_THEME") is { } theme)
        {
            options = options with { Theme = theme };
        }

        if (Text(read, "CRAFT_AUTOSAVE_SECONDS") is { } autosave)
        {
            options = options with { AutosaveSeconds = Whole(autosave, "CRAFT_AUTOSAVE_SECONDS", 0, 86_400) };
        }

        if (Text(read, "CRAFT_UNDO_GROUP_MS") is { } group)
        {
            options = options with { UndoGroupMilliseconds = Whole(group, "CRAFT_UNDO_GROUP_MS", 0, 60_000) };
        }

        if (Text(read, "CRAFT_RECOVERY_FOLDER") is { } recovery)
        {
            options = options with { RecoveryFolder = recovery };
        }

        if (Text(read, "CRAFT_CRASH_FOLDER") is { } crashes)
        {
            options = options with { CrashFolder = crashes };
        }

        if (Text(read, "CRAFT_BUFFER_CHUNK") is { } chunk)
        {
            options = options with { BufferChunkCharacters = Whole(chunk, "CRAFT_BUFFER_CHUNK", 16, 64 * 1024 * 1024) };
        }

        if (Text(read, "CRAFT_LINE_ENDING") is { } ending)
        {
            options = options with
            {
                NewLine = ending.Equals("lf", StringComparison.OrdinalIgnoreCase) ? "\n"
                    : ending.Equals("crlf", StringComparison.OrdinalIgnoreCase) ? "\r\n"
                    : ending.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null
                    : throw new ArgumentException($"CRAFT_LINE_ENDING is '{ending}': it takes lf, crlf or auto.", "CRAFT_LINE_ENDING"),
            };
        }

        if (Text(read, "CRAFT_WORKSPACE_IGNORE") is { } ignore)
        {
            options = options with { WorkspaceIgnore = ignore.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        }

        if (Text(read, "CRAFT_CARET_BLINK_MS") is { } blink)
        {
            options = options with { CaretBlinkMilliseconds = Whole(blink, "CRAFT_CARET_BLINK_MS", 0, 10_000) };
        }

        if (Text(read, "CRAFT_CARET_WIDTH") is { } caretWidth)
        {
            options = options with { CaretWidth = Positive(caretWidth, "CRAFT_CARET_WIDTH", 0.5f, 50) };
        }

        if (Text(read, "CRAFT_DOUBLE_CLICK_MS") is { } doubleClick)
        {
            options = options with { DoubleClickMilliseconds = Whole(doubleClick, "CRAFT_DOUBLE_CLICK_MS", 0, 10_000) };
        }

        if (Text(read, "CRAFT_DOUBLE_CLICK_DISTANCE") is { } distance)
        {
            options = options with { DoubleClickDistance = Positive(distance, "CRAFT_DOUBLE_CLICK_DISTANCE", 0, 1000) };
        }

        if (Text(read, "CRAFT_BRACKET_SCAN_LINES") is { } scan)
        {
            options = options with { BracketScanLines = Whole(scan, "CRAFT_BRACKET_SCAN_LINES", 0, 100_000_000) };
        }

        if (Text(read, "CRAFT_SCROLL_MARGIN_LINES") is { } marginLines)
        {
            options = options with { ScrollMarginLines = Whole(marginLines, "CRAFT_SCROLL_MARGIN_LINES", 0, 1000) };
        }

        if (Text(read, "CRAFT_SCROLL_MARGIN_COLUMNS") is { } marginColumns)
        {
            options = options with { ScrollMarginColumns = Whole(marginColumns, "CRAFT_SCROLL_MARGIN_COLUMNS", 0, 1000) };
        }

        if (Text(read, "CRAFT_BOTTOM_MARGIN_LINES") is { } bottom)
        {
            options = options with { BottomMarginLines = Whole(bottom, "CRAFT_BOTTOM_MARGIN_LINES", 0, 10_000) };
        }

        if (Text(read, "CRAFT_GUTTER_PADDING") is { } gutter)
        {
            options = options with { GutterPaddingCells = Whole(gutter, "CRAFT_GUTTER_PADDING", 0, 100) };
        }

        if (Text(read, "CRAFT_AUTO_CLOSE_BRACKETS") is { } autoClose)
        {
            options = options with { AutoCloseBrackets = Flag(autoClose, "CRAFT_AUTO_CLOSE_BRACKETS") };
        }

        if (Text(read, "CRAFT_FORMAT_ON_SAVE") is { } formatOnSave)
        {
            options = options with { FormatOnSave = Flag(formatOnSave, "CRAFT_FORMAT_ON_SAVE") };
        }

        if (Text(read, "CRAFT_FIND_COUNT_LIMIT") is { } findLimit)
        {
            options = options with { FindCountLimit = Whole(findLimit, "CRAFT_FIND_COUNT_LIMIT", 1, int.MaxValue) };
        }

        if (Text(read, "CRAFT_PARSE_WAIT_MS") is { } parseWait)
        {
            options = options with { ParseWaitMilliseconds = Whole(parseWait, "CRAFT_PARSE_WAIT_MS", 0, 10_000) };
        }


        if (Text(read, "CRAFT_KEY_BINDINGS") is { } bindings)
        {
            try
            {
                options = options with { KeyBindings = KeyBinding.ParseList(bindings) };
            }
            catch (FormatException e)
            {
                throw new ArgumentException($"CRAFT_KEY_BINDINGS is '{bindings}': {e.Message}. It takes 'Chord=command' entries separated by ';'.", "CRAFT_KEY_BINDINGS", e);
            }
        }

        if (Text(read, "CRAFT_KEYMAP") is { } keyMap)
        {
            options = options with { KeyMap = keyMap };
        }

        if (Text(read, "CRAFT_ZOOM_STEP") is { } zoomStep)
        {
            options = options with { ZoomStep = Positive(zoomStep, "CRAFT_ZOOM_STEP", 0.5f, 10) };
        }

        if (Text(read, "CRAFT_HOST_LOG_LINES") is { } logLines)
        {
            options = options with { HostLogLines = Whole(logLines, "CRAFT_HOST_LOG_LINES", 0, 1_000_000) };
        }

        if (Text(read, "CRAFT_PROPOSAL_PREVIEW_CHARS") is { } preview)
        {
            options = options with { ProposalPreviewCharacters = Whole(preview, "CRAFT_PROPOSAL_PREVIEW_CHARS", 0, 1_000_000) };
        }

        // Phase A1: the diff view.
        if (Text(read, "CRAFT_DIFF_CONTEXT_LINES") is { } diffContext)
        {
            options = options with { DiffContextLines = Whole(diffContext, "CRAFT_DIFF_CONTEXT_LINES", 0, 1000) };
        }

        if (Text(read, "CRAFT_DIFF_LINE_LIMIT") is { } diffLimit)
        {
            options = options with { DiffLineLimit = Whole(diffLimit, "CRAFT_DIFF_LINE_LIMIT", 0, 100_000) };
        }

        if (Text(read, "CRAFT_DIFF_CHECK") is { } diffCheck)
        {
            options = options with { DiffCheck = Flag(diffCheck, "CRAFT_DIFF_CHECK") };
        }

        // Phase E4: the shell.
        if (Text(read, "CRAFT_DATA_FOLDER") is { } dataFolder)
        {
            options = options with { DataFolder = dataFolder };
        }

        if (Text(read, "CRAFT_RECENT_LIMIT") is { } recent)
        {
            options = options with { RecentLimit = Whole(recent, "CRAFT_RECENT_LIMIT", 0, 10_000) };
        }

        if (Text(read, "CRAFT_NARROW_WIDTH") is { } narrow)
        {
            options = options with { NarrowWidth = Positive(narrow, "CRAFT_NARROW_WIDTH", 0, 100_000) };
        }

        if (Text(read, "CRAFT_PANEL_EXPLORER_WIDTH") is { } treeWidth)
        {
            options = options with { TreeWidth = Positive(treeWidth, "CRAFT_PANEL_EXPLORER_WIDTH", 40, 10_000) };
        }

        if (Text(read, "CRAFT_TREE_INDENT") is { } treeIndent)
        {
            options = options with { TreeIndent = Positive(treeIndent, "CRAFT_TREE_INDENT", 0, 200) };
        }

        if (Text(read, "CRAFT_PANEL_EXPLORER_OPEN") is { } explorerOpen)
        {
            options = options with { ExplorerOpen = Flag(explorerOpen, "CRAFT_PANEL_EXPLORER_OPEN") };
        }

        if (Text(read, "CRAFT_PANEL_CHAT_OPEN") is { } chatOpen)
        {
            options = options with { ChatOpen = Flag(chatOpen, "CRAFT_PANEL_CHAT_OPEN") };
        }

        if (Text(read, "CRAFT_PANEL_BOTTOM_OPEN") is { } bottomOpen)
        {
            options = options with { BottomPanelOpen = Flag(bottomOpen, "CRAFT_PANEL_BOTTOM_OPEN") };
        }

        if (Text(read, "CRAFT_PANEL_MIN_SIZE") is { } panelMinimum)
        {
            options = options with { PanelMinimumSize = Positive(panelMinimum, "CRAFT_PANEL_MIN_SIZE", 0, 10_000) };
        }

        if (Text(read, "CRAFT_WORKSPACE_WATCHER") is { } watcher)
        {
            options = options with { WorkspaceWatcher = watcher };
        }

        if (Text(read, "CRAFT_WORKSPACE_WATCH_MS") is { } watchDelay)
        {
            options = options with { WorkspaceWatchMilliseconds = Whole(watchDelay, "CRAFT_WORKSPACE_WATCH_MS", 0, 60_000) };
        }

        if (Text(read, "CRAFT_FONT_FALLBACK") is { } fallback)
        {
            options = options with { FontFallback = fallback };
        }

        if (Text(read, "CRAFT_INTERFACE_FONT") is { } interfaceFont)
        {
            options = options with { InterfaceFont = interfaceFont };
        }

        if (Text(read, "CRAFT_INTERFACE_FONT_SIZE") is { } interfaceSize)
        {
            options = options with { InterfaceFontSize = Positive(interfaceSize, "CRAFT_INTERFACE_FONT_SIZE", 6, 72) };
        }

        if (Text(read, "CRAFT_ACCENT") is { } accent)
        {
            options = options with
            {
                Accent = AccentColors.TryParse(accent, out uint argb) ? argb
                    : throw new ArgumentException($"CRAFT_ACCENT is '{accent}': it takes {string.Join(", ", AccentColors.Names)} or #RRGGBB.", "CRAFT_ACCENT"),
            };
        }

        if (Text(read, "CRAFT_PANEL_BOTTOM_HEIGHT") is { } bottomHeight)
        {
            options = options with { BottomPanelHeight = Positive(bottomHeight, "CRAFT_PANEL_BOTTOM_HEIGHT", 40, 10_000) };
        }

        if (Text(read, "CRAFT_QUICK_OPEN_FILES") is { } quickFiles)
        {
            options = options with { QuickOpenFiles = Whole(quickFiles, "CRAFT_QUICK_OPEN_FILES", 0, 100_000_000) };
        }

        if (Text(read, "CRAFT_QUICK_OPEN_ROWS") is { } quickRows)
        {
            options = options with { QuickOpenRows = Whole(quickRows, "CRAFT_QUICK_OPEN_ROWS", 1, 200) };
        }

        if (Text(read, "CRAFT_TITLE_BAR") is { } titleBar)
        {
            options = options with
            {
                TitleBar = titleBar.Equals("window", StringComparison.OrdinalIgnoreCase) ? "window"
                    : titleBar.Equals("page", StringComparison.OrdinalIgnoreCase) ? "page"
                    : throw new ArgumentException($"CRAFT_TITLE_BAR is '{titleBar}': it takes window or page.", "CRAFT_TITLE_BAR"),
            };
        }

        if (Text(read, "CRAFT_TOUCH_TARGET") is { } touch)
        {
            options = options with { TouchTarget = Positive(touch, "CRAFT_TOUCH_TARGET", 16, 200) };
        }

        if (Text(read, "CRAFT_DIAGNOSTICS_DELAY_MS") is { } diagnosticsDelay)
        {
            options = options with { DiagnosticsDelayMilliseconds = Whole(diagnosticsDelay, "CRAFT_DIAGNOSTICS_DELAY_MS", 0, 600_000) };
        }

        if (Text(read, "CRAFT_SEMANTIC_MAX_CHARS") is { } semanticMax)
        {
            options = options with { SemanticMaxCharacters = Whole(semanticMax, "CRAFT_SEMANTIC_MAX_CHARS", 0, int.MaxValue) };
        }

        if (Text(read, "CRAFT_WORKSPACE_READERS") is { } readers)
        {
            options = options with { WorkspaceReaders = readers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        }

        if (Text(read, "CRAFT_WORKSPACE_CONFIGURATION") is { } configuration)
        {
            options = options with { WorkspaceConfiguration = configuration };
        }

        if (Text(read, "CRAFT_COMPLETION_DELAY_MS") is { } completionDelay)
        {
            options = options with { CompletionDelayMilliseconds = Whole(completionDelay, "CRAFT_COMPLETION_DELAY_MS", 0, 60_000) };
        }

        if (Text(read, "CRAFT_COMPLETION_ROWS") is { } rows)
        {
            options = options with { CompletionRows = Whole(rows, "CRAFT_COMPLETION_ROWS", 1, 100) };
        }

        if (Text(read, "CRAFT_INLINE_COMPLETION_PROVIDER") is { } inlineProvider)
        {
            options = options with { InlineCompletionProvider = inlineProvider };
        }

        if (Text(read, "CRAFT_PANEL_CHAT_WIDTH") is { } chatWidth)
        {
            options = options with { ChatWidth = Positive(chatWidth, "CRAFT_PANEL_CHAT_WIDTH", 120, 10_000) };
        }

        if (Text(read, "CRAFT_PROBLEMS_LIMIT") is { } problems)
        {
            options = options with { ProblemsListLimit = Whole(problems, "CRAFT_PROBLEMS_LIMIT", 1, 1_000_000) };
        }

        if (Text(read, "CRAFT_HOVER_DELAY_MS") is { } hoverDelay)
        {
            options = options with { HoverDelayMilliseconds = Whole(hoverDelay, "CRAFT_HOVER_DELAY_MS", 0, 60_000) };
        }

        if (Text(read, "CRAFT_SOURCE_GENERATORS") is { } generators)
        {
            options = options with { SourceGenerators = Flag(generators, "CRAFT_SOURCE_GENERATORS") };
        }

        if (Text(read, "CRAFT_SEMANTIC_COLORS_DELAY_MS") is { } colorsDelay)
        {
            options = options with { SemanticColorsDelayMilliseconds = Whole(colorsDelay, "CRAFT_SEMANTIC_COLORS_DELAY_MS", -1, 60_000) };
        }

        if (Text(read, "CRAFT_MEMORY_MONITOR") is { } monitor)
        {
            options = options with { MemoryMonitor = monitor };
        }

        if (Text(read, "CRAFT_TERMINAL_SHELL") is { } terminalShell)
        {
            options = options with { TerminalShell = terminalShell };
        }

        if (Text(read, "CRAFT_TERMINAL_ARGS") is { } terminalArgs)
        {
            options = options with { TerminalArguments = terminalArgs };
        }

        if (Text(read, "CRAFT_TERMINAL_ENV") is { } terminalEnv)
        {
            options = options with { TerminalEnvironment = ParseEnvironment(terminalEnv, "CRAFT_TERMINAL_ENV") };
        }

        if (Text(read, "CRAFT_TERMINAL_CWD") is { } terminalCwd)
        {
            options = options with { TerminalWorkingDirectory = terminalCwd };
        }

        if (Text(read, "CRAFT_TERMINAL_SCROLLBACK") is { } scrollback)
        {
            options = options with { TerminalScrollback = Whole(scrollback, "CRAFT_TERMINAL_SCROLLBACK", 0, 1_000_000) };
        }

        if (Text(read, "CRAFT_TERMINAL_HOST") is { } terminalHost)
        {
            options = options with { TerminalHost = terminalHost };
        }

        if (Text(read, "CRAFT_TERMINAL_LINE_SPACING") is { } terminalSpacing)
        {
            options = options with { TerminalLineSpacing = Positive(terminalSpacing, "CRAFT_TERMINAL_LINE_SPACING", 0.8f, 4) };
        }

        if (Text(read, "CRAFT_TERMINAL_EDITOR_COMMANDS") is { } terminalCommands)
        {
            options = options with { TerminalEditorCommands = terminalCommands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        }

        if (Text(read, "CRAFT_UI_WATCHDOG_MS") is { } watchdog)
        {
            options = options with { UiWatchdogMilliseconds = Whole(watchdog, "CRAFT_UI_WATCHDOG_MS", 0, 10_000) };
        }

        if (Text(read, "CRAFT_UI_STALL_MS") is { } stall)
        {
            options = options with { UiStallMilliseconds = Whole(stall, "CRAFT_UI_STALL_MS", 1, 600_000) };
        }

        if (Text(read, "CRAFT_UI_STALL_REPORTS") is { } stallReports)
        {
            options = options with { UiStallReports = Whole(stallReports, "CRAFT_UI_STALL_REPORTS", 0, 100_000) };
        }

        if (Text(read, "CRAFT_NOTIFICATIONS_KEEP") is { } keep)
        {
            options = options with { NotificationsKeep = Whole(keep, "CRAFT_NOTIFICATIONS_KEEP", 0, 100_000) };
        }

        if (Text(read, "CRAFT_TOAST_MS") is { } toast)
        {
            options = options with { ToastMilliseconds = Whole(toast, "CRAFT_TOAST_MS", 0, 600_000) };
        }

        if (Text(read, "CRAFT_ACTIVITY_MIN_MS") is { } activityMin)
        {
            options = options with { ActivityMinimumMilliseconds = Whole(activityMin, "CRAFT_ACTIVITY_MIN_MS", 0, 600_000) };
        }

        if (Text(read, "CRAFT_ACTIVITY_FRAME_MS") is { } frame)
        {
            options = options with { ActivityFrameMilliseconds = Whole(frame, "CRAFT_ACTIVITY_FRAME_MS", 1, 1000) };
        }

        if (Text(read, "CRAFT_CRASH_TRAIL") is { } trail)
        {
            options = options with { CrashTrail = Flag(trail, "CRAFT_CRASH_TRAIL") };
        }

        if (Text(read, "CRAFT_CRASH_TRAIL_KB") is { } trailKb)
        {
            options = options with { CrashTrailKilobytes = Whole(trailKb, "CRAFT_CRASH_TRAIL_KB", 16, 1_048_576) };
        }

        if (Text(read, "CRAFT_CRASH_TRAIL_EXCEPTIONS") is { } trailExceptions)
        {
            options = options with { CrashTrailExceptionsPerSecond = Whole(trailExceptions, "CRAFT_CRASH_TRAIL_EXCEPTIONS", 0, 10_000) };
        }

        if (Text(read, "CRAFT_CRASH_TICKET_TRAIL_LINES") is { } ticketLines)
        {
            options = options with { CrashTicketTrailLines = Whole(ticketLines, "CRAFT_CRASH_TICKET_TRAIL_LINES", 0, 1_000_000) };
        }

        if (Text(read, "CRAFT_CRASH_TICKET_OFFER") is { } ticketOffer)
        {
            options = options with { CrashTicketOffer = Flag(ticketOffer, "CRAFT_CRASH_TICKET_OFFER") };
        }

        if (Text(read, "CRAFT_CRASH_RUNS_KEPT") is { } runsKept)
        {
            options = options with { CrashRunsKept = Whole(runsKept, "CRAFT_CRASH_RUNS_KEPT", 0, 1000) };
        }

        if (Text(read, "CRAFT_CRASH_RECORDS") is { } records)
        {
            options = options with { CrashRecords = records };
        }

        if (Text(read, "CRAFT_TERMINAL_CTRL_C_COPIES") is { } ctrlC)
        {
            options = options with { TerminalCtrlCCopies = Flag(ctrlC, "CRAFT_TERMINAL_CTRL_C_COPIES") };
        }

        if (Text(read, "CRAFT_PLUGIN_FOLDERS") is { } folders)
        {
            options = options with { PluginFolders = folders.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        }

        if (Text(read, "CRAFT_PLUGINS_DISABLED") is { } disabled)
        {
            options = options with { DisabledPlugins = disabled.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
        }

        return options;
    }

    /// <summary>"A=1|B=two" as its entries in order; an entry without '=' or with an empty name throws, naming the variable.</summary>
    private static KeyValuePair<string, string>[] ParseEnvironment(string value, string name)
    {
        var entries = new List<KeyValuePair<string, string>>();
        foreach (var range in value.AsSpan().Split('|'))
        {
            var entry = value.AsSpan()[range].Trim();
            if (entry.IsEmpty)
            {
                continue;
            }

            int equals = entry.IndexOf('=');
            if (equals <= 0)
            {
                throw new ArgumentException($"{name} is '{value}': it takes NAME=value entries separated by '|' ('{entry}' has no name or no '=').", name);
            }

            entries.Add(new(entry[..equals].Trim().ToString(), entry[(equals + 1)..].ToString()));
        }

        return [.. entries];
    }

    private static string? Text(Func<string, string?> read, string name) => read(name) is { Length: > 0 } value ? value.Trim() : null;

    private static float Positive(string value, string name, float min, float max) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
            ? parsed
            : throw new ArgumentException($"{name} is '{value}': it takes a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.", name);

    private static int Whole(string value, string name, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
            ? parsed
            : throw new ArgumentException($"{name} is '{value}': it takes a whole number from {min} to {max}.", name);

    private static bool Flag(string value, string name) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" ? true
        : value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" ? false
        : throw new ArgumentException($"{name} is '{value}': it takes true or false (or 1 or 0).", name);
}
