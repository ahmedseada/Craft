// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The shell's command ids (each registered in <see cref="EditorCommands"/> by <see cref="ShellModel"/>).</summary>
public static class ShellCommandIds
{
    /// <summary>A new, untitled document (Ctrl+N).</summary>
    public const string NewFile = "craft.newFile";

    /// <summary>Opens a file (Ctrl+O).</summary>
    public const string OpenFile = "craft.openFile";

    /// <summary>Opens a folder as the workspace (Ctrl+Shift+O).</summary>
    public const string OpenFolder = "craft.openFolder";

    /// <summary>Opens a recent file or folder (Ctrl+R).</summary>
    public const string OpenRecent = "craft.openRecent";

    /// <summary>Closes the workspace folder (its tabs stay).</summary>
    public const string CloseFolder = "craft.closeFolder";

    /// <summary>Saves the active document (Ctrl+S; the editor's own Ctrl+S runs it too).</summary>
    public const string Save = "craft.save";

    /// <summary>Saves the active document to a new file (Ctrl+Shift+S).</summary>
    public const string SaveAs = "craft.saveAs";

    /// <summary>Closes the active tab, asking about unsaved changes (Ctrl+W).</summary>
    public const string CloseTab = "craft.closeTab";

    /// <summary>The next tab (Ctrl+Tab).</summary>
    public const string NextTab = "craft.nextTab";

    /// <summary>The previous tab (Ctrl+Shift+Tab).</summary>
    public const string PreviousTab = "craft.previousTab";

    /// <summary>The settings page (Ctrl+Comma).</summary>
    public const string Settings = "craft.settings";

    /// <summary>Shows or hides the folder tree (Ctrl+B).</summary>
    public const string ToggleTree = "craft.toggleTree";

    /// <summary>Reads the folder tree again.</summary>
    public const string RefreshTree = "craft.refreshTree";

    /// <summary>Switches between the light and the dark theme (for this run; CRAFT_THEME sets it for good).</summary>
    public const string ToggleTheme = "craft.toggleTheme";
}

/// <summary>
/// The app's shell without a UI: the workspace folder and its tree, the tabs, the layout, the recent list, the layered
/// settings and the options built from them, and the theme. Its actions are commands in <see cref="EditorCommands"/> with
/// key bindings in <see cref="EditorKeyBindings"/> (<see cref="ShellCommandIds"/>), registered on the same public
/// contracts a plug-in uses, so they show in the command palette, a user's <c>CRAFT_KEY_BINDINGS</c> can rebind them, and
/// a plug-in can run them. What needs the user goes through <see cref="IFileDialogs"/> (the platform's) and
/// <see cref="IShellUi"/> (the prompts and pages). Disposing it takes its commands and bindings back and closes the tabs.
/// Used on the UI thread.
/// </summary>
public sealed class ShellModel : IDisposable
{
    /// <summary>The settings that take effect at the next start (the plug-ins load, the host reads its bindings, at start).</summary>
    public static IReadOnlySet<string> AppliedAtStart { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CRAFT_PLUGIN_FOLDERS", "CRAFT_PLUGINS_DISABLED", "CRAFT_KEY_BINDINGS", "CRAFT_HOST_LOG_LINES", "CRAFT_PROPOSAL_PREVIEW_CHARS", "CRAFT_DATA_FOLDER",
    };

    private readonly IFileDialogs _dialogs;
    private readonly IShellUi _ui;
    private readonly Func<string, CraftOptions, CancellationToken, Task<ITextDocument>> _openDocument;
    private readonly List<string> _commandIds = [];
    private readonly List<KeyBinding> _bindings = [];
    private string _optionsSignature;
    private bool _disposed;

    /// <summary>The shell over <paramref name="host"/>, its commands and bindings registered.</summary>
    /// <param name="host">The editor host (the tabs open their documents in it).</param>
    /// <param name="settings">The layered settings (the workspace's file is set when a folder opens).</param>
    /// <param name="recent">The recent list.</param>
    /// <param name="dialogs">The platform's file dialogs.</param>
    /// <param name="ui">The prompts and pages.</param>
    /// <param name="openDocument">Reads a file into a document (<see cref="TextDocument.OpenAsync"/> when null).</param>
    public ShellModel(EditorHost host, CraftSettings settings, RecentList recent, IFileDialogs dialogs, IShellUi ui,
        Func<string, CraftOptions, CancellationToken, Task<ITextDocument>>? openDocument = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(ui);
        Host = host;
        Settings = settings;
        Recent = recent;
        _dialogs = dialogs;
        _ui = ui;
        _openDocument = openDocument ?? (static async (path, options, token) => await TextDocument.OpenAsync(path, options, token).ConfigureAwait(true));
        Options = settings.BuildOptions(out var problems);
        OptionsProblems = problems;
        _optionsSignature = Signature();
        Recent.Limit = Options.RecentLimit;
        Theme = ThemeOf(Options);
        Tabs = new DocumentTabs(host);
        Layout = new ShellLayout(Options);
        RegisterCommands();
    }

    /// <summary>The editor host.</summary>
    public EditorHost Host { get; }

    /// <summary>The layered settings.</summary>
    public CraftSettings Settings { get; }

    /// <summary>The recent files and folders.</summary>
    public RecentList Recent { get; }

    /// <summary>The tabs.</summary>
    public DocumentTabs Tabs { get; }

    /// <summary>The layout and what shows in it.</summary>
    public ShellLayout Layout { get; }

    /// <summary>The open folder, or null.</summary>
    public Workspace? Workspace { get; private set; }

    /// <summary>The open folder's tree, or null.</summary>
    public FolderTree? Tree { get; private set; }

    /// <summary>The options in effect (the settings' layers over the defaults).</summary>
    public CraftOptions Options { get; private set; }

    /// <summary>What did not parse in the settings when the options were last built (each names its variable and source).</summary>
    public IReadOnlyList<string> OptionsProblems { get; private set; }

    /// <summary>The theme in use.</summary>
    public ITheme Theme { get; private set; }

    /// <summary>Raised after a folder opened or closed.</summary>
    public event EventHandler? WorkspaceChanged;

    /// <summary>Raised after <see cref="Options"/> changed (a setting written, a workspace's settings read).</summary>
    public event EventHandler? OptionsChanged;

    /// <summary>Raised after <see cref="Theme"/> changed.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>The settings page's rows: every Craft option, then every plug-in's declared setting.</summary>
    public IReadOnlyList<SettingRow> SettingRows() =>
        Settings.Rows(Host.Settings.All.Select(p => new CraftVariable(p.Variable, p.Setting.Default, $"{p.Plugin}: {p.Setting.Description}")));

    /// <summary>
    /// Opens the folder <paramref name="path"/> as the workspace (in place of the open one; the tabs stay): its
    /// <c>.craft/settings.json</c> read into the settings, the tree listed, the folder put first in the recent list.
    /// False, with a message, when it cannot be opened.
    /// </summary>
    public async Task<bool> OpenFolderAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Workspace workspace;
        try
        {
            workspace = new Workspace(path, Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _ui.ShowMessageAsync("Craft: could not open the folder", $"{path}: {e.Message}");
            return false;
        }

        Workspace?.Dispose();
        Settings.Workspace = SettingsFile.Load(CraftSettings.WorkspaceFilePath(workspace.Root));
        ReloadOptions();

        // Made again with the options that now include the workspace's own (its CRAFT_WORKSPACE_IGNORE applies to its tree);
        // a workspace lists nothing until asked, so this costs nothing.
        Workspace = new Workspace(workspace.Root, Options);
        Tree = new FolderTree(Workspace);
        Recent.Add(Workspace.Root, RecentKind.Folder);
        Layout.HasFolder = true;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Closes the workspace folder (its settings no longer apply; the tabs stay open).</summary>
    public void CloseFolder()
    {
        if (Workspace is null)
        {
            return;
        }

        Workspace.Dispose();
        Workspace = null;
        Tree = null;
        Settings.Workspace = null;
        ReloadOptions();
        Layout.HasFolder = false;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the file <paramref name="path"/> in a tab (the open one when it is already open), in the language its name
    /// registers (plain text otherwise), puts it first in the recent list, and makes it active; null, with a message, when
    /// it cannot be read. <paramref name="fromTree"/> tells the narrow layout to go back to the editor.
    /// </summary>
    public async Task<DocumentTab?> OpenFileAsync(string path, bool fromTree = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var tab = Tabs.Find(full);
        if (tab is null)
        {
            ITextDocument document;
            try
            {
                document = await _openDocument(full, Options, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
            {
                await _ui.ShowMessageAsync("Craft: could not open", $"{full}: {e.Message}");
                return null;
            }

            // Opened meanwhile (a second click while the first read): keep the first.
            if (Tabs.Find(full) is { } opened)
            {
                document.Dispose();
                tab = opened;
            }
            else
            {
                tab = Tabs.Add(document, EditorLanguages.ForFile(full), activate: false);
            }
        }

        Tabs.Activate(tab);
        Recent.Add(full, RecentKind.File);
        if (fromTree)
        {
            Layout.FileOpenedFromTree();
        }

        return tab;
    }

    /// <summary>A new, empty, untitled document (plain text until it is saved under a name) in a new active tab.</summary>
    public DocumentTab NewFile() => Tabs.Add(TextDocument.Create("", Options), null);

    /// <summary>
    /// Saves <paramref name="tab"/> (the active one when null): to its file, or, for a new document, where the user picks.
    /// False when it was not saved (no tab, cancelled, or failed, said so).
    /// </summary>
    public async Task<bool> SaveAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        if (tab.FilePath is null)
        {
            return await SaveAsAsync(tab, cancellationToken);
        }

        return await WriteAsync(tab, null, cancellationToken);
    }

    /// <summary>Saves <paramref name="tab"/> (the active one when null) to a file the user picks; its language follows the new name.</summary>
    public async Task<bool> SaveAsAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        var suggested = tab.FilePath is { } current ? Path.GetFileName(current) : tab.Title;
        var folder = tab.FilePath is { } file ? Path.GetDirectoryName(file) : Workspace?.Root;
        var path = await _dialogs.PickSavePathAsync(suggested, folder, cancellationToken);
        if (path is null)
        {
            return false;
        }

        if (!await WriteAsync(tab, Path.GetFullPath(path), cancellationToken))
        {
            return false;
        }

        var language = EditorLanguages.ForFile(path);
        if (!ReferenceEquals(language, tab.Language))
        {
            Tabs.SetLanguage(tab, language);
        }

        return true;
    }

    /// <summary>
    /// Closes <paramref name="tab"/> (the active one when null), first asking what to do with its unsaved changes: save
    /// (a new document asks where), discard, or keep it open. True when it closed.
    /// </summary>
    public async Task<bool> CloseTabAsync(DocumentTab? tab = null, CancellationToken cancellationToken = default)
    {
        tab ??= Tabs.Active;
        if (tab is null)
        {
            return false;
        }

        if (tab.IsDirty && !await ResolveUnsavedAsync(tab, cancellationToken))
        {
            return false;
        }

        Tabs.Remove(tab);
        return true;
    }

    /// <summary>
    /// Before the window closes: asks about each tab with unsaved changes in turn (each shown first); true when every one
    /// was saved or discarded, false as soon as one is kept open (the window stays).
    /// </summary>
    public async Task<bool> ConfirmCloseAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var tab in Tabs.Tabs.ToArray())
        {
            if (!tab.IsDirty)
            {
                continue;
            }

            Tabs.Activate(tab);
            if (!await ResolveUnsavedAsync(tab, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lists the recent files and folders and opens the one picked; a missing one is taken off the list, said so.</summary>
    public async Task OpenRecentAsync(CancellationToken cancellationToken = default)
    {
        var entries = Recent.Entries.ToArray();
        if (entries.Length == 0)
        {
            await _ui.ShowMessageAsync("Recent", Recent.Limit == 0 ? "The recent list is off (CRAFT_RECENT_LIMIT is 0)." : "Nothing opened yet.");
            return;
        }

        var items = entries.Select(e => (e.Kind == RecentKind.Folder ? "Folder: " : "File: ") + e.Path).ToArray();
        int picked = await _ui.PickAsync("Open a recent file or folder", items, cancellationToken);
        if (picked < 0 || picked >= entries.Length)
        {
            return;
        }

        var entry = entries[picked];
        bool exists = entry.Kind == RecentKind.Folder ? Directory.Exists(entry.Path) : File.Exists(entry.Path);
        if (!exists)
        {
            Recent.Remove(entry.Path);
            await _ui.ShowMessageAsync("Recent", $"{entry.Path} no longer exists; it was taken off the list.");
            return;
        }

        if (entry.Kind == RecentKind.Folder)
        {
            await OpenFolderAsync(entry.Path);
        }
        else
        {
            await OpenFileAsync(entry.Path, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="variable"/> into <paramref name="layer"/>'s settings file (null
    /// or empty removes it) and applies the options; null when done, else why not (a file that may not set it, a value
    /// that does not parse, a file that could not be written).
    /// </summary>
    public string? ApplySetting(SettingLayer layer, string variable, string? value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        try
        {
            Settings.Set(layer, variable, value);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }

        ReloadOptions();
        return null;
    }

    /// <summary>Builds the options from the settings again; raises <see cref="OptionsChanged"/> (and <see cref="ThemeChanged"/>) when they changed.</summary>
    public void ReloadOptions()
    {
        var options = Settings.BuildOptions(out var problems);
        OptionsProblems = problems;
        var signature = Signature();
        if (signature == _optionsSignature)
        {
            return;
        }

        _optionsSignature = signature;
        bool themeChanged = !string.Equals(options.Theme, Options.Theme, StringComparison.OrdinalIgnoreCase);
        Options = options;
        Recent.Limit = options.RecentLimit;
        Layout.Options = options;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        if (themeChanged)
        {
            SetTheme(ThemeOf(options));
        }
    }

    /// <summary>Switches between the light and the dark theme for this run.</summary>
    public void ToggleTheme() => SetTheme(EditorThemes.Get(Theme.IsDark ? EditorThemes.Light : EditorThemes.Dark));

    /// <summary>Takes the shell's commands and bindings back and closes every tab (unsaved changes are lost: ask first).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var binding in _bindings)
        {
            // Only while the chord is still the shell's (a later registration, a plug-in's, keeps its own).
            if (EditorKeyBindings.Find(binding.Chord) is { } current && ReferenceEquals(current, binding))
            {
                EditorKeyBindings.Unregister(binding.Chord);
            }
        }

        foreach (var id in _commandIds)
        {
            EditorCommands.Unregister(id);
        }

        Tabs.Dispose();
        Workspace?.Dispose();
    }

    private static ITheme ThemeOf(CraftOptions options) => EditorThemes.Find(options.Theme) ?? EditorThemes.Get(EditorThemes.Dark);

    private void SetTheme(ITheme theme)
    {
        if (ReferenceEquals(theme, Theme))
        {
            return;
        }

        Theme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    // The values in effect of every Craft option: equal signatures, equal options (the lists inside compare by reference).
    private string Signature()
    {
        var text = new StringBuilder();
        foreach (var variable in CraftOptions.Variables)
        {
            text.Append(Settings.Read(variable.Name)).Append('\u0001');
        }

        return text.ToString();
    }

    private async Task<bool> WriteAsync(DocumentTab tab, string? path, CancellationToken cancellationToken)
    {
        try
        {
            await Host.SaveAsync(tab.HostDocument, path, cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or EncoderFallbackException)
        {
            await _ui.ShowMessageAsync("Craft: could not save", $"{path ?? tab.FilePath}: {e.Message}");
            return false;
        }

        if (tab.FilePath is { } saved)
        {
            Recent.Add(saved, RecentKind.File);
        }

        return true;
    }

    private async Task<bool> ResolveUnsavedAsync(DocumentTab tab, CancellationToken cancellationToken) =>
        await _ui.AskUnsavedAsync(tab.Title, cancellationToken) switch
        {
            UnsavedChoice.Save => await SaveAsync(tab, cancellationToken),
            UnsavedChoice.Discard => true,
            _ => false,
        };

    private void RegisterCommands()
    {
        Command(ShellCommandIds.NewFile, "File: New file", (_, _) => { NewFile(); return Task.CompletedTask; }, null, "Ctrl+N");
        Command(ShellCommandIds.OpenFile, "File: Open a file", async (_, token) =>
        {
            if (await _dialogs.PickFileAsync(token) is { } path)
            {
                await OpenFileAsync(path, cancellationToken: token);
            }
        }, null, "Ctrl+O");
        Command(ShellCommandIds.OpenFolder, "File: Open a folder", async (_, token) =>
        {
            if (await _dialogs.PickFolderAsync(token) is { } path)
            {
                await OpenFolderAsync(path);
            }
        }, null, "Ctrl+Shift+O");
        Command(ShellCommandIds.OpenRecent, "File: Open a recent file or folder", (_, token) => OpenRecentAsync(token), null, "Ctrl+R");
        Command(ShellCommandIds.CloseFolder, "File: Close the folder", (_, _) => { CloseFolder(); return Task.CompletedTask; }, _ => Workspace is not null, null);
        Command(ShellCommandIds.Save, "File: Save", (_, token) => SaveAsync(null, token), _ => Tabs.Active is not null, "Ctrl+S");
        Command(ShellCommandIds.SaveAs, "File: Save as", (_, token) => SaveAsAsync(null, token), _ => Tabs.Active is not null, "Ctrl+Shift+S");
        Command(ShellCommandIds.CloseTab, "Tab: Close", (_, token) => CloseTabAsync(null, token), _ => Tabs.Active is not null, "Ctrl+W");
        Command(ShellCommandIds.NextTab, "Tab: Next", (_, _) => { Tabs.Next(); return Task.CompletedTask; }, _ => Tabs.Tabs.Count > 1, "Ctrl+Tab");
        Command(ShellCommandIds.PreviousTab, "Tab: Previous", (_, _) => { Tabs.Previous(); return Task.CompletedTask; }, _ => Tabs.Tabs.Count > 1, "Ctrl+Shift+Tab");
        Command(ShellCommandIds.Settings, "Settings: Open", (_, token) => _ui.ShowSettingsAsync(token), null, "Ctrl+Comma");
        Command(ShellCommandIds.ToggleTree, "View: Show or hide the folder tree", (_, _) => { Layout.ToggleTree(); return Task.CompletedTask; }, _ => Workspace is not null, "Ctrl+B");
        Command(ShellCommandIds.RefreshTree, "View: Refresh the folder tree", (_, _) => { Tree?.Refresh(); return Task.CompletedTask; }, _ => Tree is not null, null);
        Command(ShellCommandIds.ToggleTheme, "View: Switch light and dark", (_, _) => { ToggleTheme(); return Task.CompletedTask; }, null, null);
    }

    private void Command(string id, string title, Func<IEditorHost, CancellationToken, Task> execute, Func<IEditorHost, bool>? canExecute, string? chord)
    {
        EditorCommands.Register(new EditorCommand(id, title, execute, canExecute));
        _commandIds.Add(id);
        if (chord is not null)
        {
            var binding = new KeyBinding(KeyChord.Parse(chord), id);
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }
    }
}
