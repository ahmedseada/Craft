// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The look group's command ids (X part 9, items 24-26).</summary>
public static class LookCommands
{
    /// <summary>Preferences: Color theme (Ctrl+K Ctrl+T): the themes in a quick pick, each previewed as the selection moves.</summary>
    public const string SelectTheme = "craft.selectTheme";

    /// <summary>Preferences: File icon theme: W's icon themes in the same quick pick, previewed the same way.</summary>
    public const string SelectIconTheme = "craft.selectIconTheme";

    /// <summary>View: Zen mode (Ctrl+K Z): the rail, side view, bottom panel, status bar and tabs hidden, the text centred; Escape twice leaves.</summary>
    public const string ToggleZenMode = "craft.toggleZenMode";

    /// <summary>View: Full screen (F11): the window's own full screen (the platform's).</summary>
    public const string ToggleFullScreen = "craft.toggleFullScreen";

    /// <summary>The status bar's indentation item: spaces or tabs, the tab size, convert the indentation.</summary>
    public const string ChangeIndentation = "craft.changeIndentation";

    /// <summary>The status bar's line ending item: LF or CRLF, the text converted as one undo step.</summary>
    public const string ChangeLineEnding = "craft.changeLineEnding";

    /// <summary>The status bar's encoding item: reopen or save with an encoding.</summary>
    public const string ChangeEncoding = "craft.changeEncoding";

    /// <summary>The status bar's language item: the file's language mode.</summary>
    public const string ChangeLanguage = "craft.changeLanguage";
}

/// <summary>An encoding the encoding picker offers: its name as shown, the encoding, and whether it writes a byte order mark.</summary>
/// <param name="Name">Its name ("UTF-8 with BOM", "Windows-1252").</param>
/// <param name="Encoding">The encoding.</param>
/// <param name="Bom">Whether a save writes its byte order mark.</param>
public sealed record EncodingChoice(string Name, Encoding Encoding, bool Bom);

/// <summary>The encodings the status bar's picker lists, the common ones that this runtime can read and write.</summary>
public static class EncodingChoices
{
    private static readonly Lazy<IReadOnlyList<EncodingChoice>> Choices = new(Make);

    /// <summary>The list, in order (the UTF forms first, then the code pages the runtime knows).</summary>
    public static IReadOnlyList<EncodingChoice> All => Choices.Value;

    /// <summary>The choice that names <paramref name="encoding"/> with or without its mark, or null.</summary>
    public static EncodingChoice? Of(Encoding encoding, bool bom)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        foreach (var choice in All)
        {
            if (choice.Encoding.CodePage == encoding.CodePage && (choice.Bom == bom || choice.Encoding.GetPreamble().Length == 0))
            {
                return choice;
            }
        }

        return null;
    }

    private static List<EncodingChoice> Make()
    {
        // The code pages (Windows-1252, Shift-JIS, …) come from the runtime's own provider, registered once.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var list = new List<EncodingChoice>
        {
            new("UTF-8", new UTF8Encoding(false), false),
            new("UTF-8 with BOM", new UTF8Encoding(true), true),
            new("UTF-16 LE", new UnicodeEncoding(false, true), true),
            new("UTF-16 BE", new UnicodeEncoding(true, true), true),
            new("UTF-32 LE", new UTF32Encoding(false, true), true),
        };
        foreach (var (name, page) in (ReadOnlySpan<(string, int)>)[("Windows-1252 (Western)", 1252), ("ISO-8859-1 (Latin-1)", 28591), ("ISO-8859-2 (Central European)", 28592),
            ("Windows-1250 (Central European)", 1250), ("Windows-1251 (Cyrillic)", 1251), ("KOI8-R (Cyrillic)", 20866), ("Windows-1253 (Greek)", 1253),
            ("Windows-1254 (Turkish)", 1254), ("Windows-1255 (Hebrew)", 1255), ("Windows-1256 (Arabic)", 1256), ("Shift-JIS (Japanese)", 932),
            ("GB2312 (Simplified Chinese)", 936), ("Big5 (Traditional Chinese)", 950), ("EUC-KR (Korean)", 949), ("US-ASCII", 20127)])
        {
            try
            {
                list.Add(new EncodingChoice(name, Encoding.GetEncoding(page), false));
            }
            catch (Exception e) when (e is NotSupportedException or ArgumentException)
            {
                // A code page this runtime cannot make is not offered.
            }
        }

        return list;
    }
}

// X part 9, items 24-26: the theme picker with live preview, the system's light or dark, Zen mode and full screen, and the
// status bar's items that change what they show (indentation, line ending, encoding, language) through quick picks.
public sealed partial class ShellModel
{
    private bool _systemDark = true;
    private bool _zen;
    private bool _fullScreen;
    private bool _zenTookFullScreen;
    private int _zenEscapes;

    /// <summary>Whether the system asks for dark (the app tells it, <see cref="SetSystemTheme"/>); <c>CRAFT_THEME</c> "system" follows it.</summary>
    public bool SystemPrefersDark => _systemDark;

    /// <summary>Whether Zen mode is on (Ctrl+K Z).</summary>
    public bool IsZen => _zen;

    /// <summary>Whether the window is in full screen (F11, or Zen mode with <c>CRAFT_ZEN_FULL_SCREEN</c>).</summary>
    public bool IsFullScreen => _fullScreen;

    /// <summary>Whether the platform can put the window in full screen (the app sets it from its platform; true until it says).</summary>
    public bool FullScreenSupported { get; set; } = true;

    /// <summary>Raised after Zen mode or full screen changed (the window lays itself out again and asks the platform).</summary>
    public event EventHandler? WindowModeChanged;

    /// <summary>The system's light or dark changed (the app's platform tells it): a theme of "system" follows at once.</summary>
    public void SetSystemTheme(bool dark)
    {
        if (_systemDark == dark)
        {
            return;
        }

        _systemDark = dark;
        if (string.Equals(Options.Theme, EditorThemes.System, StringComparison.OrdinalIgnoreCase))
        {
            SetTheme(ThemeOf(Options));
        }
    }

    /// <summary>
    /// The theme picker's rows: "system" first, then every registered theme (the library's and the plug-ins'), each with its
    /// summary; the current one and a plug-in's origin marked.
    /// </summary>
    public IReadOnlyList<(string Name, QuickPickItem Item)> ThemeChoices()
    {
        var rows = new List<(string, QuickPickItem)>
        {
            (EditorThemes.System, new QuickPickItem(EditorThemes.System, $"Follow the system's light or dark (now {(_systemDark ? "dark" : "light")})") { Mark = Current(EditorThemes.System) }),
        };
        foreach (var name in EditorThemes.Names)
        {
            var theme = EditorThemes.Find(name);
            string? mark = Current(name) ?? (EditorThemes.IsBuiltIn(name) ? null : "from " + (EditorThemes.Origin(name) ?? "a plug-in"));
            rows.Add((name, new QuickPickItem(name, theme?.Summary) { Mark = mark }));
        }

        return rows;

        string? Current(string name) => string.Equals(name, Options.Theme, StringComparison.OrdinalIgnoreCase) ? "current" : null;
    }

    /// <summary>
    /// Shows <paramref name="name"/>'s theme now, for this run (the theme picker's preview: one walk of the window, as any
    /// theme change); "system" the system's light or dark. Nothing is written.
    /// </summary>
    public void PreviewTheme(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetTheme(Accented(ResolveTheme(name), Options));
    }

    /// <summary>
    /// The theme picker (Ctrl+K Ctrl+T, Preferences: Color theme): a quick pick of <see cref="ThemeChoices"/>, the window
    /// re-themed as the selection moves; Enter keeps the theme (<c>CRAFT_THEME</c> written to the user's settings), Escape
    /// brings back the one before. The name kept, or null.
    /// </summary>
    public async Task<string?> PickThemeAsync(CancellationToken cancellationToken = default)
    {
        var rows = ThemeChoices();
        var before = Theme;
        int selected = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Name, Options.Theme, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        var items = new QuickPickItem[rows.Count];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = rows[i].Item;
        }

        var request = new QuickPickRequest("Color theme", items)
        {
            Placeholder = "Pick a theme (the window shows each as you move; Enter keeps it, Escape goes back)",
            Selected = selected,
            Moved = index =>
            {
                if (index >= 0 && index < rows.Count && !_disposed)
                {
                    PreviewTheme(rows[index].Name);
                }
            },
        };
        int picked = await _ui.QuickPickAsync(request, cancellationToken);
        if (_disposed)
        {
            return null;
        }

        if (picked < 0 || picked >= rows.Count)
        {
            SetTheme(before);
            return null;
        }

        var name = rows[picked].Name;
        if (ApplySetting(SettingLayer.User, "CRAFT_THEME", name) is { } error)
        {
            Host.ShowStatus("Color theme", $"{name} is shown for this run; CRAFT_THEME was not written: {error}", NotificationSeverity.Warning);
        }

        PreviewTheme(name);                                     // the environment's CRAFT_THEME wins the settings: this run keeps the pick
        return name;
    }

    /// <summary>
    /// The file icon theme picker (Preferences: File icon theme): W's icon themes in the same quick pick, each shown as the
    /// selection moves; Enter writes <c>CRAFT_ICON_THEME</c>, Escape goes back. The name kept, or null.
    /// </summary>
    public async Task<string?> PickIconThemeAsync(CancellationToken cancellationToken = default)
    {
        var names = FileIconThemes.Names;
        string before = FileIconThemes.Current;
        var items = new QuickPickItem[names.Count];
        int selected = 0;
        for (int i = 0; i < names.Count; i++)
        {
            bool current = string.Equals(names[i], before, StringComparison.OrdinalIgnoreCase);
            selected = current ? i : selected;
            items[i] = new QuickPickItem(names[i], FileIconThemes.Find(names[i])?.Summary) { Mark = current ? "current" : null };
        }

        var request = new QuickPickRequest("File icon theme", items)
        {
            Selected = selected,
            Moved = index =>
            {
                if (index >= 0 && index < names.Count && !_disposed && UseIconTheme(names[index]))
                {
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            },
        };
        int picked = await _ui.QuickPickAsync(request, cancellationToken);
        if (_disposed)
        {
            return null;
        }

        if (picked < 0 || picked >= names.Count)
        {
            if (UseIconTheme(before))
            {
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            return null;
        }

        ApplySetting(SettingLayer.User, "CRAFT_ICON_THEME", names[picked]);
        return names[picked];
    }

    /// <summary>
    /// Zen mode on or off (Ctrl+K Z): the window hides the rail, the side view, the bottom panel, the chat, the status bar
    /// and the tabs, and centres the editor at <c>CRAFT_ZEN_WIDTH</c>; with <c>CRAFT_ZEN_FULL_SCREEN</c> it goes full screen
    /// too (and back out when Zen ends, unless it was full screen before). Escape twice in a row leaves it.
    /// </summary>
    public void ToggleZen() => SetZen(!_zen);

    /// <summary>Turns Zen mode on or off (<see cref="ToggleZen"/>).</summary>
    public void SetZen(bool on)
    {
        if (on == _zen || _disposed)
        {
            return;
        }

        _zen = on;
        _zenEscapes = 0;
        if (on)
        {
            Host.KeyPressed += OnZenKey;
            _zenTookFullScreen = Options.ZenFullScreen && !_fullScreen && FullScreenSupported;
            if (_zenTookFullScreen)
            {
                _fullScreen = true;
            }
        }
        else
        {
            Host.KeyPressed -= OnZenKey;
            if (_zenTookFullScreen)
            {
                _fullScreen = false;
            }

            _zenTookFullScreen = false;
        }

        WindowModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Full screen on or off (F11): the window's own (the platform's); said when the platform has none.</summary>
    public void ToggleFullScreen()
    {
        if (!FullScreenSupported)
        {
            Host.ShowStatus("Full screen", "This platform cannot put the window in full screen.", NotificationSeverity.Warning);
            return;
        }

        _fullScreen = !_fullScreen;
        _zenTookFullScreen = false;                             // chosen by hand: Zen's end leaves it as it is
        WindowModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The indentation picker (the status bar's Spaces/Tab size item): indent with spaces or tabs (then the size), change the
    /// tab size, or convert the indentation to spaces or tabs (one undo step). A pick holds for this editor; a file's
    /// <c>.editorconfig</c> value is said in the rows, as the one that wins when the file is opened again. False when nothing was done.
    /// </summary>
    public async Task<bool> PickIndentationAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveEditor() is not { } editor)
        {
            return NoEditor("Indentation");
        }

        var options = editor.Options;
        var file = editor.FileSettings;
        string? styleNote = file.InsertSpaces is { } spaces && file.Sources.Count > 0
            ? $".editorconfig ({file.Sources[^1]}) says indent_style = {(spaces ? "space" : "tab")}: it wins when the file is opened again" : null;
        string? sizeNote = file.TabSize is { } size && file.Sources.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $".editorconfig ({file.Sources[^1]}) says {size}: it wins when the file is opened again") : null;
        QuickPickItem[] items =
        [
            new("Indent using spaces", styleNote ?? "Tab inserts spaces") { Mark = options.InsertSpaces ? "current" : null },
            new("Indent using tabs", styleNote ?? "Tab inserts a tab") { Mark = options.InsertSpaces ? null : "current" },
            new("Change the tab size", sizeNote ?? string.Create(CultureInfo.InvariantCulture, $"Now {options.TabSize}")),
            new("Convert the indentation to spaces", "Every line's leading tabs as spaces, one undo step"),
            new("Convert the indentation to tabs", "Every line's leading spaces as tabs where they fill one, one undo step"),
        ];
        int picked = await _ui.QuickPickAsync(new QuickPickRequest("Indentation", items) { Selected = options.InsertSpaces ? 0 : 1 }, cancellationToken);
        switch (picked)
        {
            case 0 or 1:
            {
                int? tabSize = await PickTabSizeAsync(options.TabSize, sizeNote, cancellationToken);
                if (tabSize is not int chosen)
                {
                    return false;
                }

                editor.SetIndentation(picked == 0, chosen);
                break;
            }

            case 2:
            {
                int? tabSize = await PickTabSizeAsync(options.TabSize, sizeNote, cancellationToken);
                if (tabSize is not int chosen)
                {
                    return false;
                }

                editor.SetIndentation(editor.Options.InsertSpaces, chosen);
                break;
            }

            case 3 or 4:
                editor.SetIndentation(picked == 3, editor.Options.TabSize);
                if (!editor.ConvertIndentation(picked == 3))
                {
                    Host.ShowStatus("Indentation", picked == 3 ? "No line is indented with tabs." : "No line's indentation fills a tab.");
                }

                break;
            default:
                return false;
        }

        UpdateActiveTab();
        return true;
    }

    /// <summary>
    /// The line ending picker (the status bar's LF/CRLF item): every break of the text converted as one undo step, and new
    /// lines take it; a file's <c>.editorconfig</c> end_of_line is said where it wins (for the lines the editor inserts).
    /// </summary>
    public async Task<bool> PickLineEndingAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveEditor() is not { } editor || Tabs.Active is not { } tab)
        {
            return NoEditor("Line ending");
        }

        var file = editor.FileSettings;
        string? note = file.NewLine is { } newLine && file.Sources.Count > 0
            ? $".editorconfig ({file.Sources[^1]}) says end_of_line = {ShellText.LineEnding(newLine).ToLowerInvariant()}: it wins for the lines Enter inserts" : null;
        string current = tab.Document is TextDocument document ? document.LineEnding : editor.Snapshot.LineCount > 1 && editor.Snapshot.GetLine(0).BreakLength == 2 ? "\r\n" : "\n";
        QuickPickItem[] items =
        [
            new("LF", note ?? "\\n: Linux, macOS, git's own") { Mark = current == "\n" ? "current" : null },
            new("CRLF", note ?? "\\r\\n: Windows") { Mark = current == "\r\n" ? "current" : null },
        ];
        int picked = await _ui.QuickPickAsync(new QuickPickRequest("Line ending", items) { Selected = current == "\r\n" ? 1 : 0 }, cancellationToken);
        if (picked < 0 || Groups.GroupOf(tab) is null)
        {
            return false;
        }

        string chosen = picked == 0 ? "\n" : "\r\n";
        bool changed = editor.ConvertLineEndings(chosen);
        if (tab.Document is TextDocument text)
        {
            text.LineEnding = chosen;
        }

        if (!changed)
        {
            Host.ShowStatus("Line ending", $"Every line already ends in {ShellText.LineEnding(chosen)}.");
        }

        UpdateActiveTab();
        return true;
    }

    /// <summary>
    /// The encoding picker (the status bar's encoding item): reopen the file with an encoding (its bytes read again off the
    /// UI thread, the text replaced as one undo step, asked first when it has unsaved changes) or save it with one; a file's
    /// <c>.editorconfig</c> charset is said (it applies to a new file's first save).
    /// </summary>
    public async Task<bool> PickEncodingAsync(CancellationToken cancellationToken = default)
    {
        if (Tabs.Active is not { Document: TextDocument document } tab)
        {
            return NoEditor("Encoding");
        }

        string now = ShellText.EncodingName(document.Encoding, document.HasBom);
        string? charset = ActiveEditor()?.FileSettings is { Charset: { } set, Sources.Count: > 0 } file ? $".editorconfig ({file.Sources[^1]}) says charset = {set}: it applies to a new file's first save" : null;
        QuickPickItem[] actions =
        [
            new("Reopen with an encoding", document.FilePath is null ? "The document has no file yet" : $"Read {tab.Title} again as another encoding (now {now})"),
            new("Save with an encoding", charset ?? $"Write {tab.Title} in another encoding (now {now})"),
        ];
        int action = await _ui.QuickPickAsync(new QuickPickRequest("Encoding", actions), cancellationToken);
        if (action < 0 || (action == 0 && document.FilePath is null))
        {
            return false;
        }

        var choices = EncodingChoices.All;
        var current = EncodingChoices.Of(document.Encoding, document.HasBom);
        var items = new QuickPickItem[choices.Count];
        int selected = 0;
        for (int i = 0; i < choices.Count; i++)
        {
            bool isCurrent = ReferenceEquals(choices[i], current);
            selected = isCurrent ? i : selected;
            items[i] = new QuickPickItem(choices[i].Name, choices[i].Encoding.WebName) { Mark = isCurrent ? "current" : null };
        }

        int picked = await _ui.QuickPickAsync(new QuickPickRequest(action == 0 ? "Reopen with an encoding" : "Save with an encoding", items) { Selected = selected }, cancellationToken);
        if (picked < 0 || Groups.GroupOf(tab) is null)
        {
            return false;
        }

        var choice = choices[picked];
        if (action == 1)
        {
            document.SetEncoding(choice.Encoding, choice.Bom);
            UpdateActiveTab();
            return await SaveAsync(tab, cancellationToken);
        }

        return await ReopenWithEncodingAsync(tab, document, choice, cancellationToken);
    }

    /// <summary>
    /// Reads <paramref name="tab"/>'s file again as <paramref name="choice"/> (off the UI thread): the text replaced as one
    /// undo step, the encoding taken, the document clean (it is the file's text); asked first when it has unsaved changes.
    /// </summary>
    public async Task<bool> ReopenWithEncodingAsync(DocumentTab tab, TextDocument document, EncodingChoice choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(choice);
        if (document.FilePath is not { } path)
        {
            return false;
        }

        if (document.IsDirty && !await _ui.ConfirmAsync("Reopen with an encoding", $"{tab.Title} has unsaved changes; reading it again as {choice.Name} replaces them (Ctrl+Z brings them back).", "Reopen", danger: true, cancellationToken))
        {
            return false;
        }

        var snapshot = document.Buffer.Current;
        int lineLimit = Options.DiffLineLimit;
        (TextEdit[] Edits, bool Bom) read;
        try
        {
            read = await Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(path);
                var preamble = choice.Encoding.GetPreamble();
                bool bom = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
                string text = choice.Encoding.GetString(bytes, bom ? preamble.Length : 0, bytes.Length - (bom ? preamble.Length : 0));
                return (LineEdits.Between(snapshot, text, lineLimit), bom);
            }, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            Host.ShowStatus("Encoding", $"{tab.Title} could not be read again: {e.Message}", NotificationSeverity.Warning);
            return false;
        }

        if (Groups.GroupOf(tab) is null || document.Buffer.Current.Version != snapshot.Version)
        {
            Host.ShowStatus("Encoding", $"{tab.Title} changed meanwhile: not reopened.", NotificationSeverity.Warning);
            return false;
        }

        if (read.Edits.Length > 0)
        {
            var before = tab.HostDocument.Selection;
            var after = SelectionState.At(Math.Min(before.Caret, read.Edits[0].Start));
            document.Edit(read.Edits, EditKind.Command, before, after);
            Host.Select(tab.HostDocument, after);
        }

        document.SetEncoding(choice.Encoding, read.Bom || choice.Bom);
        document.MarkUnchanged();
        UpdateActiveTab();
        return true;
    }

    /// <summary>
    /// The language picker (the status bar's language item): the file's language by its name, every registered language,
    /// plain text; the tab's document opened again in it in the host (the plug-ins see the change).
    /// </summary>
    public async Task<bool> PickLanguageAsync(CancellationToken cancellationToken = default)
    {
        if (Tabs.Active is not { } tab)
        {
            return NoEditor("Language");
        }

        var languages = EditorLanguages.All.OrderBy(LanguageTitle, StringComparer.OrdinalIgnoreCase).ToList();
        var byName = tab.FilePath is { } path ? EditorLanguages.ForFile(path, Workspace?.Root) : null;
        var items = new List<QuickPickItem>
        {
            new("Auto", byName is null ? "Plain text by the file's name" : $"{LanguageTitle(byName)} by the file's name"),
        };
        int selected = 0;
        foreach (var language in languages)
        {
            bool current = ReferenceEquals(language, tab.Language);
            selected = current ? items.Count : selected;
            items.Add(new QuickPickItem(LanguageTitle(language), language.Name) { Mark = current ? "current" : null });
        }

        items.Add(new QuickPickItem("Plain text") { Mark = tab.Language is null ? "current" : null });
        selected = tab.Language is null ? items.Count - 1 : selected;
        int picked = await _ui.QuickPickAsync(new QuickPickRequest("Language mode", items) { Selected = selected }, cancellationToken);
        if (picked < 0 || Groups.GroupOf(tab) is null)
        {
            return false;
        }

        var chosen = picked == 0 ? byName : picked <= languages.Count ? languages[picked - 1] : null;
        Groups.SetLanguage(tab, chosen, chosen?.Name);
        UpdateActiveTab();
        return true;
    }

    // The tab sizes offered (1 to 8), the current one first selected.
    private async Task<int?> PickTabSizeAsync(int current, string? note, CancellationToken cancellationToken)
    {
        var items = new QuickPickItem[8];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new QuickPickItem((i + 1).ToString(CultureInfo.InvariantCulture), note) { Mark = i + 1 == current ? "current" : null };
        }

        int picked = await _ui.QuickPickAsync(new QuickPickRequest("Tab size", items) { Selected = Math.Clamp(current - 1, 0, items.Length - 1) }, cancellationToken);
        return picked < 0 ? null : picked + 1;
    }

    private EditorModel? ActiveEditor() => Tabs.Active is { } tab ? EditorOf?.Invoke(tab) : null;

    private bool NoEditor(string what)
    {
        Host.ShowStatus(what, "No file is open in the editor.");
        return false;
    }

    // The status bar reads the tab's texts again (the page listens to the tab's change).
    private void UpdateActiveTab()
    {
        if (Tabs.Active is { } tab)
        {
            Groups.GroupOf(tab)?.Tabs.NotifyChanged(tab);
        }
    }

    // Escape twice in a row leaves Zen mode; any other key starts the count again.
    private void OnZenKey(object? sender, EditorKeyEventArgs e)
    {
        if (!_zen)
        {
            return;
        }

        if (e.Chord.Key == EditorKey.Escape && e.Chord.Modifiers == KeyModifiers.None)
        {
            if (++_zenEscapes >= 2)
            {
                SetZen(false);
            }

            return;
        }

        _zenEscapes = 0;
    }

    private ITheme ResolveTheme(string name) =>
        string.Equals(name, EditorThemes.System, StringComparison.OrdinalIgnoreCase) ? EditorThemes.ForSystem(_systemDark)
            : EditorThemes.Find(name) ?? EditorThemes.Get(EditorThemes.Dark);

    private void RegisterLookCommands()
    {
        Command(LookCommands.SelectTheme, "Preferences: Color theme", async (_, token) => await PickThemeAsync(token), null, "Ctrl+K Ctrl+T");
        Command(LookCommands.SelectIconTheme, "Preferences: File icon theme", async (_, token) => await PickIconThemeAsync(token), null, null);
        Command(LookCommands.ToggleZenMode, "View: Zen mode", (_, _) => { ToggleZen(); return Task.CompletedTask; }, _ => !Layout.IsNarrow, "Ctrl+K Z",
            _ => "Zen mode needs the wide layout.");
        Command(LookCommands.ToggleFullScreen, "View: Full screen", (_, _) => { ToggleFullScreen(); return Task.CompletedTask; }, null, "F11");
        Command(LookCommands.ChangeIndentation, "View: Change the indentation", async (_, token) => await PickIndentationAsync(token), _ => Tabs.Active is not null, null, _ => "No file is open.");
        Command(LookCommands.ChangeLineEnding, "View: Change the line ending", async (_, token) => await PickLineEndingAsync(token), _ => Tabs.Active is not null, null, _ => "No file is open.");
        Command(LookCommands.ChangeEncoding, "File: Reopen or save with an encoding", async (_, token) => await PickEncodingAsync(token), _ => Tabs.Active is not null, null, _ => "No file is open.");
        Command(LookCommands.ChangeLanguage, "View: Change the language mode", async (_, token) => await PickLanguageAsync(token), _ => Tabs.Active is not null, null, _ => "No file is open.");
        Menu(EditorMenus.View, LookCommands.SelectTheme, 4, 4);
        Menu(EditorMenus.View, LookCommands.ToggleZenMode, 6, 0);
        Menu(EditorMenus.View, LookCommands.ToggleFullScreen, 6, 1);
    }

    private void DisposeLook()
    {
        if (_zen)
        {
            Host.KeyPressed -= OnZenKey;
            _zen = false;
        }
    }
}
