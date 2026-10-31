// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Core;

/// <summary>A Craft setting's environment variable: its name, its default and what it sets.</summary>
/// <param name="Name">The variable ("CRAFT_TAB_SIZE").</param>
/// <param name="Default">Its default, as text.</param>
/// <param name="Meaning">What it sets.</param>
public sealed record CraftVariable(string Name, string Default, string Meaning);

/// <summary>
/// The editor's settings. Every one has a default and an environment variable (<see cref="Variables"/>); the app's
/// settings page, the user's <c>settings.json</c> and a workspace's <c>.craft/settings.json</c> set them too (<c>Craft.Core.Settings.CraftSettings</c>). Nothing the editor does is fixed
/// where someone may need to adjust it.
/// </summary>
public sealed record CraftOptions
{
    /// <summary>The font family; the platform's monospaced font when it has none of this name (<c>CRAFT_FONT</c>).</summary>
    public string FontFamily { get; init; } = "Cascadia Mono";

    /// <summary>The font size in device-independent units (<c>CRAFT_FONT_SIZE</c>).</summary>
    public float FontSize { get; init; } = 14;

    /// <summary>A line's height as a multiple of the font's (<c>CRAFT_LINE_SPACING</c>).</summary>
    public float LineSpacing { get; init; } = 1.35f;

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

    /// <summary>The lines of the editor host's log kept for the log view (<c>CRAFT_HOST_LOG_LINES</c>); 0 keeps none.</summary>
    public int HostLogLines { get; init; } = 500;

    /// <summary>The characters of an edit's new text a proposal's confirmation shows before it cuts (<c>CRAFT_PROPOSAL_PREVIEW_CHARS</c>).</summary>
    public int ProposalPreviewCharacters { get; init; } = 200;

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
    /// tree in place of the editor, the tabs as one switcher, no side panels. Measured from the window, never the platform (<c>CRAFT_NARROW_WIDTH</c>).
    /// </summary>
    public float NarrowWidth { get; init; } = 720;

    /// <summary>The folder tree's width in the wide layout, in device-independent units (<c>CRAFT_TREE_WIDTH</c>).</summary>
    public float TreeWidth { get; init; } = 260;

    /// <summary>The indent of each level of the folder tree, in device-independent units (<c>CRAFT_TREE_INDENT</c>).</summary>
    public float TreeIndent { get; init; } = 14;

    /// <summary>The plug-ins' side panels' width in the wide layout, in device-independent units (<c>CRAFT_PANEL_WIDTH</c>).</summary>
    public float PanelWidth { get; init; } = 280;

    /// <summary>The data folder as a full path: <see cref="DataFolder"/>, else the local application data folder's "Craft".</summary>
    public string ResolveDataFolder() =>
        Path.GetFullPath(DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Craft"));

    /// <summary>The environment variables, with their defaults and meanings, in order.</summary>
    public static IReadOnlyList<CraftVariable> Variables { get; } =
    [
        new("CRAFT_FONT", "Cascadia Mono", "The editor's font family (the platform's monospaced font when it has none of this name)."),
        new("CRAFT_FONT_SIZE", "14", "The font size, in device-independent units."),
        new("CRAFT_LINE_SPACING", "1.35", "A line's height as a multiple of the font's height."),
        new("CRAFT_TAB_SIZE", "4", "The columns a tab spans."),
        new("CRAFT_INSERT_SPACES", "true", "Whether Tab inserts spaces rather than a tab character."),
        new("CRAFT_THEME", "dark", "The theme's name (dark, light, or a registered one)."),
        new("CRAFT_AUTOSAVE_SECONDS", "30", "Seconds between autosaves of a recovery copy; 0 turns autosave off."),
        new("CRAFT_UNDO_GROUP_MS", "1000", "Typing with pauses shorter than this is one undo step."),
        new("CRAFT_RECOVERY_FOLDER", "(local app data)/Craft/recovery", "Where recovery copies are kept."),
        new("CRAFT_CRASH_FOLDER", "(local app data)/Craft/crashes", "Where crash reports are written (the crash dialog names the file)."),
        new("CRAFT_BUFFER_CHUNK", "65536", "The characters of each block a text buffer's add buffer grows by."),
        new("CRAFT_LINE_ENDING", "auto", "The line break of a new document: lf, crlf, or auto (the running system's)."),
        new("CRAFT_PLUGIN_FOLDERS", "(none)", "Folders of user plug-ins (a Name/Name.dll folder each), loaded after the app's own; separated by ';'."),
        new("CRAFT_PLUGINS_DISABLED", "(none)", "Plug-ins not loaded, by folder name (Craft.CSharp), the pre-installed ones included; separated by ','."),
        new("CRAFT_WORKSPACE_IGNORE", "bin;obj;.git;.vs;node_modules;test-output", "The folder names a workspace skips when it lists its files, separated by ';' or ','."),
        new("CRAFT_CARET_BLINK_MS", "530", "Milliseconds between the caret's blinks; 0 shows it solid."),
        new("CRAFT_CARET_WIDTH", "2", "The caret bar's width, in device-independent units."),
        new("CRAFT_DOUBLE_CLICK_MS", "500", "Clicks closer in time than this count as a double or triple click."),
        new("CRAFT_DOUBLE_CLICK_DISTANCE", "4", "Clicks farther apart than this (device-independent units) start a new click count."),
        new("CRAFT_BRACKET_SCAN_LINES", "5000", "The lines a bracket match scans each way before giving up."),
        new("CRAFT_SCROLL_MARGIN_LINES", "2", "Lines kept between the caret and the viewport's top or bottom when scrolled into view."),
        new("CRAFT_SCROLL_MARGIN_COLUMNS", "4", "Columns kept between the caret and the viewport's left or right when scrolled into view."),
        new("CRAFT_BOTTOM_MARGIN_LINES", "3", "Empty lines the editor can scroll past the last line."),
        new("CRAFT_GUTTER_PADDING", "2", "Empty cells of the line-number gutter beside the widest number."),
        new("CRAFT_AUTO_CLOSE_BRACKETS", "false", "Whether typing an opening bracket inserts its closing one too."),
        new("CRAFT_FIND_COUNT_LIMIT", "100000", "Find stops counting matches at this many."),
        new("CRAFT_PARSE_WAIT_MS", "8", "Milliseconds a frame waits for the language's parse of its text; longer parses paint the changed text plain, then repaint. 0 never waits."),
        new("CRAFT_KEY_BINDINGS", "(none)", "The user's key bindings, 'Chord=command' separated by ';' (Ctrl+Shift+W=sample.wordCount); they run before the editor's keys; an empty command unbinds a plug-in's chord."),
        new("CRAFT_HOST_LOG_LINES", "500", "Lines of the editor host's log (plug-ins' lines included) kept for the log view; 0 keeps none."),
        new("CRAFT_PROPOSAL_PREVIEW_CHARS", "200", "Characters of a proposed edit's new text its confirmation shows before it cuts."),
        new("CRAFT_DATA_FOLDER", "(local app data)/Craft", "The folder of the app's own files (the recent list, the user's settings.json); from the environment only."),
        new("CRAFT_RECENT_LIMIT", "20", "The recent files and folders kept; 0 keeps none."),
        new("CRAFT_NARROW_WIDTH", "720", "The window width (device-independent units) below which the narrow (phone) layout shows."),
        new("CRAFT_TREE_WIDTH", "260", "The folder tree's width in the wide layout (device-independent units)."),
        new("CRAFT_TREE_INDENT", "14", "The indent of each level of the folder tree (device-independent units)."),
        new("CRAFT_PANEL_WIDTH", "280", "The plug-ins' side panels' width in the wide layout (device-independent units)."),
        new("CRAFT_DIAGNOSTICS_DELAY_MS", "400", "Milliseconds after the last change before the document's diagnostics are computed again (off the UI thread)."),
        new("CRAFT_SEMANTIC_MAX_CHARS", "1000000", "Above this many characters the semantic layer (diagnostics, completion while typing) waits for a command; 0 never waits."),
        new("CRAFT_WORKSPACE_READERS", "csproj,loose", "The workspace readers tried in order to make a folder's projects, separated by ','."),
        new("CRAFT_WORKSPACE_CONFIGURATION", "Debug", "The build configuration a project is read for (Debug defines DEBUG)."),
        new("CRAFT_COMPLETION_DELAY_MS", "50", "Milliseconds after typing an identifier character or '.' before the completion list is asked for."),
        new("CRAFT_COMPLETION_ROWS", "10", "The rows the completion list shows before it scrolls."),
        new("CRAFT_PROBLEMS_LIMIT", "500", "The most rows the problems list shows (the counts stay exact)."),
        new("CRAFT_HOVER_DELAY_MS", "500", "Milliseconds the pointer rests on a symbol before its description shows."),
        new("CRAFT_SOURCE_GENERATORS", "true", "Whether the projects' source generators run in the semantic model (their generated code counts, as in the build)."),
        new("CRAFT_SEMANTIC_COLORS_DELAY_MS", "30", "Milliseconds after a change before the visible lines are coloured by meaning (off the UI thread); -1 turns semantic colours off."),
        new("CRAFT_MEMORY_MONITOR", "gc", "What tells the editor memory runs low, so the languages free what they can build again: gc (the runtime's memory load), none, or a registered one."),
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

        if (Text(read, "CRAFT_HOST_LOG_LINES") is { } logLines)
        {
            options = options with { HostLogLines = Whole(logLines, "CRAFT_HOST_LOG_LINES", 0, 1_000_000) };
        }

        if (Text(read, "CRAFT_PROPOSAL_PREVIEW_CHARS") is { } preview)
        {
            options = options with { ProposalPreviewCharacters = Whole(preview, "CRAFT_PROPOSAL_PREVIEW_CHARS", 0, 1_000_000) };
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

        if (Text(read, "CRAFT_TREE_WIDTH") is { } treeWidth)
        {
            options = options with { TreeWidth = Positive(treeWidth, "CRAFT_TREE_WIDTH", 40, 10_000) };
        }

        if (Text(read, "CRAFT_TREE_INDENT") is { } treeIndent)
        {
            options = options with { TreeIndent = Positive(treeIndent, "CRAFT_TREE_INDENT", 0, 200) };
        }

        if (Text(read, "CRAFT_PANEL_WIDTH") is { } panelWidth)
        {
            options = options with { PanelWidth = Positive(panelWidth, "CRAFT_PANEL_WIDTH", 40, 10_000) };
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
