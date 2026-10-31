// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Maui;

namespace Craft.App;

/// <summary>
/// The app's shell (phase E4) over <see cref="ShellModel"/>: the folder tree, the tabs (one <see cref="EditorView"/> per
/// tab, only the active one in the page, so a tab keeps its parse, caret and find state), the find and replace bar, the
/// plug-ins' panels and status items, and the status bar; the settings page and the recent list from the shell's
/// commands. Every button runs a command of <see cref="EditorCommands"/> (the palette lists them with their chords), so
/// the page does nothing a plug-in could not ask for. The layout follows the window's measured width
/// (<c>CRAFT_NARROW_WIDTH</c>): narrow, the tree takes the editor's place, the tabs are one switcher, the side panels and
/// most buttons hide. Code only, no XAML; no platform code (the platform's pieces come through <see cref="IShellPlatform"/>).
/// </summary>
public sealed class EditorPage : ContentPage
{
    private readonly AppStartup _startup;
    private readonly string? _initialPath;
    private readonly EditorHost _host;
    private readonly ShellModel _shell;
    private readonly FolderTreeView _tree = new();
    private readonly ContentView _treeBox;
    private readonly TabStripView _tabStrip;
    private readonly ContentView _editorArea = new();
    private readonly List<EditorView> _views = [];
    private readonly Label _empty;
    private readonly FlexLayout _findBar;
    private readonly Entry _query;
    private readonly Entry _replacement;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWord;
    private readonly Label _matchCount;
    private readonly HorizontalStackLayout _replaceRow;
    private readonly Label _language = StatusLabel();
    private readonly Label _position = StatusLabel();
    private readonly Label _encoding = StatusLabel();
    private readonly Label _lineEnding = StatusLabel();
    private readonly Label _dirty = StatusLabel();
    private readonly Label _paint = StatusLabel();
    private readonly Label _notice = StatusLabel();
    private readonly FlexLayout _topBar;
    private readonly View[] _wideOnly;
    private readonly FlexLayout _statusBar;
    private readonly StatusItemsView _pluginStatus;
    private readonly PanelsView _panels;
    private readonly ContentView _panelsBox;
    private readonly Grid _body;
    private readonly List<IDisposable> _platformHooks = [];
    private Window? _window;
    private EditorView? _shown;
    private bool _started;

    /// <summary>The shell, opening <paramref name="path"/> when given (a folder as the workspace, a file in a tab; else an untitled document).</summary>
    public EditorPage(AppStartup startup, string? path)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _initialPath = path;
        _host = new EditorHost(startup.Options, action => Dispatcher.Dispatch(action), new MauiEditorPresenter(this), startup.Settings.Read);
        _shell = new ShellModel(_host, startup.Settings, startup.Recent, startup.Platform.FileDialogs, new MauiShellUi(this, ShowSettingsAsync));
        _pluginStatus = new StatusItemsView(_host);
        _panels = new PanelsView(_host);
        _panelsBox = new ContentView { Content = _panels, WidthRequest = _shell.Options.PanelWidth };
        RegisterAppCommands();

        _tabStrip = new TabStripView(_shell.Tabs);
        _tabStrip.CloseRequested += (_, tab) => _ = _shell.CloseTabAsync(tab);
        _tabStrip.ListRequested += (_, _) => _ = PickTabAsync();
        _tree.Indent = _shell.Options.TreeIndent;
        _tree.FileActivated += (_, file) => _ = _shell.OpenFileAsync(file, fromTree: true);
        _treeBox = new ContentView { Content = _tree, WidthRequest = _shell.Options.TreeWidth };
        _empty = new Label
        {
            Text = "No file is open.\nCtrl+O opens a file, Ctrl+Shift+O a folder, Ctrl+R a recent one, Ctrl+N a new file; Ctrl+Shift+P lists every command.",
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24),
        };

        var menu = CommandButton("☰", "craft.commandPalette", "Every command (Ctrl+Shift+P)");
        var folder = CommandButton("Folder", ShellCommandIds.OpenFolder, "Open a folder (Ctrl+Shift+O)");
        var open = CommandButton("Open", ShellCommandIds.OpenFile, "Open a file (Ctrl+O)");
        var save = CommandButton("Save", ShellCommandIds.Save, "Save (Ctrl+S)");
        var tree = CommandButton("Tree", ShellCommandIds.ToggleTree, "Show or hide the folder tree (Ctrl+B)");
        var newFile = CommandButton("New", ShellCommandIds.NewFile, "New file (Ctrl+N)");
        var saveAs = CommandButton("Save as", ShellCommandIds.SaveAs, "Save as (Ctrl+Shift+S)");
        var recent = CommandButton("Recent", ShellCommandIds.OpenRecent, "Recent files and folders (Ctrl+R)");
        var find = new Button { Text = "Find" };
        ToolTipProperties.SetText(find, "Find (Ctrl+F), replace (Ctrl+H)");
        find.Clicked += (_, _) => ShowFind(replace: false, null);
        var settings = CommandButton("Settings", ShellCommandIds.Settings, "Settings (Ctrl+Comma)");
        var theme = CommandButton("Theme", ShellCommandIds.ToggleTheme, "Switch light and dark");
        var plugins = CommandButton("Plug-ins", "craft.plugins", "The installed plug-ins");
        _wideOnly = [newFile, saveAs, recent, find, settings, theme, plugins];
        _topBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(6, 4),
            Children = { menu, folder, open, save, tree, newFile, saveAs, recent, find, settings, theme, plugins },
        };

        _query = new Entry { Placeholder = "Find", WidthRequest = 220 };
        _query.TextChanged += OnQueryChanged;
        _query.Completed += (_, _) => FindNext();
        _replacement = new Entry { Placeholder = "Replace", WidthRequest = 220 };
        _matchCase = new CheckBox();
        _matchCase.CheckedChanged += OnFindOptionChanged;
        _wholeWord = new CheckBox();
        _wholeWord.CheckedChanged += OnFindOptionChanged;
        _matchCount = new Label { VerticalTextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0) };
        var previous = new Button { Text = "Previous" };
        previous.Clicked += (_, _) => FindPrevious();
        var next = new Button { Text = "Next" };
        next.Clicked += (_, _) => FindNext();
        var replaceOne = new Button { Text = "Replace" };
        replaceOne.Clicked += OnReplaceClicked;
        var replaceAll = new Button { Text = "Replace all" };
        replaceAll.Clicked += OnReplaceAllClicked;
        var close = new Button { Text = "Close" };
        close.Clicked += OnCloseFindClicked;
        _replaceRow = new HorizontalStackLayout { Spacing = 4, Children = { _replacement, replaceOne, replaceAll } };
        _findBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(6, 2),
            IsVisible = false,
            Children =
            {
                _query,
                new HorizontalStackLayout { Children = { _matchCase, new Label { Text = "Match case", VerticalTextAlignment = TextAlignment.Center } } },
                new HorizontalStackLayout { Children = { _wholeWord, new Label { Text = "Whole word", VerticalTextAlignment = TextAlignment.Center } } },
                previous,
                next,
                _matchCount,
                _replaceRow,
                close,
            },
        };

        _statusBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(8, 2),
            Children = { _language, _position, _encoding, _lineEnding, _dirty, _paint, _notice, _pluginStatus },
        };
        _statusBar.SetGrow(_pluginStatus, 1);

        _body = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        _body.Add(_treeBox, 0, 0);
        _body.Add(_editorArea, 1, 0);
        _body.Add(_panelsBox, 2, 0);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(_topBar, 0, 0);
        grid.Add(_tabStrip, 0, 1);
        grid.Add(_findBar, 0, 2);
        grid.Add(_body, 0, 3);
        grid.Add(_statusBar, 0, 4);
        Content = grid;

        _shell.Tabs.TabsChanged += OnTabsChanged;
        _shell.Tabs.ActiveChanged += (_, _) => ShowActive();
        _shell.Tabs.TabChanged += (_, _) =>
        {
            UpdateStatus();
            UpdateTitle();
        };
        _shell.WorkspaceChanged += (_, _) => OnWorkspaceChanged();
        _shell.OptionsChanged += (_, _) => OnOptionsChanged();
        _shell.ThemeChanged += (_, _) => ApplyTheme();
        _shell.Layout.Changed += (_, _) => ApplyLayout();
        SizeChanged += (_, _) => _shell.Layout.UpdateWidth(Width);
        ShowActive();
        ApplyTheme();
        ApplyLayout();
    }

    /// <summary>The window the page is in (its title shows the file, the dirty marker and the folder).</summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        UpdateTitle();
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        HookPlatform();

        // The plug-ins start first, so they see the first document open (the sample's word count counts it).
        foreach (var activation in _host.RefreshPlugins())
        {
            if (activation.Error is not null)
            {
                _notice.Text = $"Plug-in {activation.Name} did not start";
                await DisplayAlertAsync("Craft: a plug-in did not start", $"{activation.Name}: {activation.Error}", "OK");
            }
        }

        if (_initialPath is not null && Directory.Exists(_initialPath))
        {
            await _shell.OpenFolderAsync(_initialPath);
        }
        else if (_initialPath is not null)
        {
            await _shell.OpenFileAsync(_initialPath);
        }

        if (_shell.Tabs.Tabs.Count == 0 && _shell.Workspace is null)
        {
            _shell.NewFile();
        }

        if (_startup.Problems.Count > 0 || _shell.OptionsProblems.Count > 0)
        {
            var problems = _startup.Problems.Concat(_shell.OptionsProblems).Distinct(StringComparer.Ordinal);
            await DisplayAlertAsync("Craft: a setting was not taken", string.Join("\n", problems) + "\nThe settings page (Ctrl+Comma) shows each one.", "OK");
        }

        foreach (var plugin in _startup.Plugins)
        {
            if (!plugin.Loaded && plugin.Error is not null)
            {
                _notice.Text = $"Plug-in {plugin.Name} did not load";
                await DisplayAlertAsync("Craft: a plug-in did not load", $"{plugin.Name} ({plugin.Folder}): {plugin.Error}", "OK");
            }
        }
    }

    private static Label StatusLabel() => new() { Margin = new Thickness(0, 0, 16, 0), FontSize = 12, VerticalTextAlignment = TextAlignment.Center };

    private static string LanguageText(EditorModel model, string? filePath)
    {
        if (model.Language is not null || filePath is null)
        {
            return model.LanguageName;
        }

        var extension = Path.GetExtension(filePath);
        bool csharp = extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csx", StringComparison.OrdinalIgnoreCase);
        return csharp ? model.LanguageName + " (the C# plug-in is not installed or is disabled)" : model.LanguageName;
    }

    // The platform's pieces need the window's native view, which exists once the page has appeared.
    private void HookPlatform()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            _platformHooks.Add(_startup.Platform.RouteKeys(_window, _host));
            _platformHooks.Add(_startup.Platform.GuardClose(_window, () => _shell.Tabs.HasUnsaved, () => _shell.ConfirmCloseAllAsync()));
        }
        catch (InvalidOperationException error)
        {
            _notice.Text = error.Message;
        }
    }

    /// <summary>The app's own commands, on the same contracts as the plug-ins': the palette (Ctrl+Shift+P) and the Plug-ins list (the shell registers the rest).</summary>
    private void RegisterAppCommands()
    {
        EditorCommands.Register(new EditorCommand("craft.commandPalette", "Show all commands", (_, _) => CommandPalette.ShowAsync(this, _host)));
        EditorCommands.Register(new EditorCommand("craft.plugins", "Show the installed plug-ins", (_, _) => ShowPluginsAsync()));
        EditorKeyBindings.Register(new KeyBinding(new KeyChord(EditorKey.P, KeyModifiers.Control | KeyModifiers.Shift), "craft.commandPalette"));
    }

    private Button CommandButton(string text, string command, string tooltip)
    {
        var button = new Button { Text = text };
        ToolTipProperties.SetText(button, tooltip);
        button.Clicked += (_, _) => _ = _host.ExecuteCommandAsync(command);
        return button;
    }

    private async Task ShowSettingsAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            await Navigation.PushModalAsync(new SettingsPage(_shell));
        }
    }

    private async Task PickTabAsync()
    {
        var tabs = _shell.Tabs.Tabs.ToArray();
        int picked = await new MauiShellUi(this, ShowSettingsAsync).PickAsync("Tabs", [.. tabs.Select(TabStripView.TitleOf)]);
        if (picked >= 0 && picked < tabs.Length)
        {
            _shell.Tabs.Activate(tabs[picked]);
        }
    }

    private Task ShowPluginsAsync()
    {
        var lines = new List<string>();
        foreach (var plugin in CraftPlugins.Installed)
        {
            string state = plugin.Disabled ? "disabled" : plugin.Loaded ? plugin.Outdated ? "loaded, outdated" : "loaded" : "not loaded";
            string version = plugin.Version is { } v ? " " + v : "";
            string summary = plugin.Summary is { Length: > 0 } text ? " — " + text : "";
            string? detail = plugin.Error ?? plugin.Note;
            lines.Add($"{plugin.Name}{version} ({state}){summary}{(detail is null ? "" : "\n    " + detail)}");
        }

        foreach (var activation in _host.Plugins)
        {
            if (activation.Error is not null)
            {
                lines.Add($"{activation.Name}: did not start: {activation.Error}");
            }
        }

        return DisplayAlertAsync("Plug-ins", lines.Count == 0 ? "No plug-in is installed." : string.Join("\n", lines), "OK");
    }

    // A tab added gets its editor view; a tab removed has its view disposed.
    private void OnTabsChanged(object? sender, EventArgs e)
    {
        foreach (var tab in _shell.Tabs.Tabs)
        {
            tab.View ??= MakeView(tab);
        }

        foreach (var view in _views.ToArray())
        {
            if (!_shell.Tabs.Tabs.Any(t => ReferenceEquals(t.View, view)))
            {
                _views.Remove(view);
                if (ReferenceEquals(_shown, view))
                {
                    _shown = null;
                    _editorArea.Content = _empty;
                }

                view.Dispose();
            }
        }

        UpdateTitle();
    }

    private EditorView MakeView(DocumentTab tab)
    {
        var view = new EditorView
        {
            Options = _shell.Options,
            Theme = _shell.Theme,
            KeyHandler = _host,
        };
        view.Open(tab.Document, tab.Language);
        view.SaveRequested += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.Save);
        view.FindRequested += (_, e) => ShowFind(e.Replace, e.InitialQuery);
        view.CaretMoved += (_, _) =>
        {
            if (view.Model is { } model && _shell.Tabs.Tabs.FirstOrDefault(t => ReferenceEquals(t.View, view)) is { } owner)
            {
                _host.UpdateSelection(owner.HostDocument, model.Selection);
            }

            if (ReferenceEquals(view, _shown))
            {
                UpdateStatus();
            }
        };
        view.Painted += (_, _) =>
        {
            if (ReferenceEquals(view, _shown))
            {
                UpdatePaintTime();
            }
        };
        _views.Add(view);
        return view;
    }

    private void ShowActive()
    {
        var tab = _shell.Tabs.Active;
        if (tab is not null)
        {
            tab.View ??= MakeView(tab);
        }

        var view = tab?.View as EditorView;
        if (!ReferenceEquals(view, _shown) || _editorArea.Content is null)
        {
            _shown = view;
            _editorArea.Content = (View?)view ?? _empty;
            if (view?.Model is { } model)
            {
                if (_findBar.IsVisible)
                {
                    ApplyFind(model);
                }

                model.EnsureCaretVisible();
                view.Focus();
            }
        }

        UpdateStatus();
        UpdateTitle();
    }

    private void OnWorkspaceChanged()
    {
        _tree.Tree = _shell.Tree;
        UpdateTitle();
        ApplyLayout();
    }

    private void OnOptionsChanged()
    {
        var options = _shell.Options;
        foreach (var tab in _shell.Tabs.Tabs)
        {
            if (tab.View is EditorView view && !ReferenceEquals(view.Options, options))
            {
                // A new model on the new options; the selection is kept.
                var selection = view.Model?.Selection;
                view.Options = options;
                if (selection is { } kept && view.Model is { } model)
                {
                    int length = model.Snapshot.Length;
                    model.Select(Math.Min(kept.Anchor, length), Math.Min(kept.Caret, length));
                }
            }
        }

        _panelsBox.WidthRequest = options.PanelWidth;
        _tree.Indent = options.TreeIndent;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var layout = _shell.Layout;
        bool narrow = layout.IsNarrow;
        _treeBox.IsVisible = layout.TreeVisible;
        _editorArea.IsVisible = layout.EditorVisible;
        _panelsBox.IsVisible = layout.PanelsVisible;
        _body.ColumnDefinitions[0].Width = narrow && layout.TreeVisible ? GridLength.Star : GridLength.Auto;
        _body.ColumnDefinitions[1].Width = layout.EditorVisible ? GridLength.Star : new GridLength(0);
        _treeBox.WidthRequest = narrow ? -1 : _shell.Options.TreeWidth;
        _tabStrip.Compact = narrow;
        foreach (var view in _wideOnly)
        {
            view.IsVisible = !narrow;
        }

        foreach (var label in (ReadOnlySpan<Label>)[_encoding, _lineEnding, _paint])
        {
            label.IsVisible = !narrow;
        }
    }

    private void ApplyTheme()
    {
        var theme = _shell.Theme;
        if (Application.Current is { } application)
        {
            application.UserAppTheme = theme.IsDark ? AppTheme.Dark : AppTheme.Light;
        }

        foreach (var view in _views)
        {
            view.Theme = theme;
            view.Invalidate();
        }

        var colors = theme.Colors;
        var foreground = ThemeColors.Of(colors.Foreground);
        var bar = ThemeColors.Of(colors.Gutter);
        BackgroundColor = ThemeColors.Of(colors.Background);
        _topBar.BackgroundColor = bar;
        _findBar.BackgroundColor = ThemeColors.Of(colors.CurrentLine);
        _statusBar.BackgroundColor = bar;
        _tabStrip.SetColors(foreground, bar, ThemeColors.Of(colors.CurrentLine));
        _treeBox.BackgroundColor = bar;
        _tree.TextColor = foreground;
        _empty.TextColor = ThemeColors.Of(colors.GutterText);
        _pluginStatus.TextColor = foreground;
        _panels.TextColor = foreground;
        _panelsBox.BackgroundColor = bar;
        foreach (var label in (ReadOnlySpan<Label>)[_language, _position, _encoding, _lineEnding, _dirty, _paint, _notice, _matchCount])
        {
            label.TextColor = foreground;
        }
    }

    private void ShowFind(bool replace, string? query)
    {
        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace;
        if (query is not null)
        {
            _query.Text = query;
        }

        if (_shown?.Model is { } model)
        {
            ApplyFind(model);
        }

        _query.Focus();
    }

    private void ApplyFind(EditorModel model)
    {
        model.Find.MatchCase = _matchCase.IsChecked;
        model.Find.WholeWord = _wholeWord.IsChecked;
        model.Find.Query = _query.Text ?? "";
        UpdateCount();
    }

    private void OnQueryChanged(object? sender, Microsoft.Maui.Controls.TextChangedEventArgs e)
    {
        if (_shown?.Model is { } model)
        {
            ApplyFind(model);
        }
    }

    private void OnFindOptionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_shown?.Model is { } model)
        {
            ApplyFind(model);
        }
    }

    private void FindNext()
    {
        _shown?.Model?.Find.FindNext();
        UpdateCount();
    }

    private void FindPrevious()
    {
        _shown?.Model?.Find.FindPrevious();
        UpdateCount();
    }

    private void OnReplaceClicked(object? sender, EventArgs e)
    {
        _shown?.Model?.Find.ReplaceOne(_replacement.Text ?? "");
        UpdateCount();
    }

    private void OnReplaceAllClicked(object? sender, EventArgs e)
    {
        if (_shown?.Model is { } model)
        {
            int replaced = model.Find.ReplaceAll(_replacement.Text ?? "");
            _notice.Text = string.Create(CultureInfo.InvariantCulture, $"Replaced {replaced}");
        }

        UpdateCount();
    }

    private void OnCloseFindClicked(object? sender, EventArgs e)
    {
        _findBar.IsVisible = false;
        foreach (var view in _views)
        {
            if (view.Model is { } model)
            {
                model.Find.IsHighlighting = false;
            }
        }

        _shown?.Invalidate();
    }

    private void UpdateCount()
    {
        if (_shown?.Model is not { } model || (_query.Text ?? "").Length == 0)
        {
            _matchCount.Text = "";
            return;
        }

        var count = model.Find.CountMatches();
        string wrapped = model.Find.LastWrapped ? ", wrapped" : "";
        _matchCount.Text = string.Create(CultureInfo.InvariantCulture, $"{(count.Capped ? "at least " : "")}{count.Count} {(count.Count == 1 ? "match" : "matches")}{wrapped}");
    }

    private void UpdateStatus()
    {
        if (_shown?.Model is not { } model || _shell.Tabs.Active is not { } tab)
        {
            _language.Text = _position.Text = _encoding.Text = _lineEnding.Text = _dirty.Text = _paint.Text = "";
            return;
        }

        _language.Text = LanguageText(model, tab.FilePath);
        _position.Text = string.Create(CultureInfo.InvariantCulture, $"Ln {model.CaretLine + 1}, Col {model.CaretColumn + 1}");
        if (tab.Document is TextDocument document)
        {
            _encoding.Text = document.Encoding.WebName.ToUpperInvariant() + (document.HasBom ? " with BOM" : "");
            _lineEnding.Text = document.LineEnding switch { "\r\n" => "CRLF", "\n" => "LF", "\r" => "CR", var other => other };
        }

        _dirty.Text = tab.IsDirty ? "• modified" : tab.FilePath is null ? "new" : "saved";
    }

    private void UpdatePaintTime()
    {
        if (_shown?.Model is { } model && _paint.IsVisible)
        {
            _paint.Text = string.Create(CultureInfo.InvariantCulture, $"input to paint {model.LastInputToPaintMilliseconds:F1} ms (max {model.MaxInputToPaintMilliseconds:F1})");
        }
    }

    private void UpdateTitle()
    {
        if (_window is null)
        {
            return;
        }

        var tab = _shell.Tabs.Active;
        string name = tab is null ? "no file" : tab.Title + (tab.IsDirty ? " •" : "");
        string folder = _shell.Workspace is { } workspace ? " — " + Path.GetFileName(workspace.Root) : "";
        _window.Title = $"Craft — {name}{folder}";
    }
}
