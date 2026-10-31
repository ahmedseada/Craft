// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Activities;
using Craft.Editor.Shell;
using Craft.Editor.Terminal;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Path = System.IO.Path;

namespace Craft.Maui;

/// <summary>What the shell starts with: the settings and the options built from them, the plug-ins loaded (or not), the recent list and the platform's pieces.</summary>
/// <param name="Options">The options (the environment over the user's settings.json over the defaults; a value that did not parse keeps its default).</param>
/// <param name="Settings">The layered settings (a folder's .craft/settings.json joins them when it opens).</param>
/// <param name="Plugins">Each installed plug-in's load result.</param>
/// <param name="Recent">The recent files and folders.</param>
/// <param name="Platform">The platform's file dialogs, key routing and close guard.</param>
/// <param name="Problems">What did not parse in the settings (each naming its variable and where it came from).</param>
public sealed record ShellStartup(CraftOptions Options, CraftSettings Settings, IReadOnlyList<PluginLoadResult> Plugins, RecentList Recent, IShellPlatform Platform, IReadOnlyList<string> Problems);

/// <summary>
/// The app's shell (phase D: the approved design, docs/design/Main.dc.html, on E4's <see cref="ShellModel"/> and K's
/// contracts): the title bar (the logo, the menus of registered commands, the search box for quick open, the model chip),
/// the activity rail, the explorer with the semantic layer's card, the tabs, the breadcrumb, the editor (one
/// <see cref="EditorView"/> per tab, only the active one in the page), the bottom panel (Problems, Terminal, Output, Idrak
/// telemetry), the chat and agent column, and the status bar. Phase U2: the settings, Models and Plug-ins pages are tabs
/// of the editor area (<see cref="PageTabs"/>), never over the window; there is no right-hand column of panels (a plug-in
/// panel is a side view of the rail, in the explorer's place); the side view, the chat column and the bottom panel each
/// sit in a <see cref="SplitPanel"/> that collapses and resizes, sizes kept per workspace (<see cref="PanelLayout"/>).
/// Every colour comes from the theme's tokens
/// (<see cref="ShellPalette"/>), every action is a command (the menus, the rail and the palette run the same ones), and the
/// layout follows the window's measured width (<c>CRAFT_NARROW_WIDTH</c>): narrow, the phone's top bar, the tree in the
/// editor's place, the tabs as one switcher, 44-unit touch targets, no rail, panels or bottom panel. No platform code (the
/// platform's pieces come through <see cref="IShellPlatform"/>).
/// </summary>
public sealed class ShellPage : ContentPage, IDisposable
{
    private const int PlatformHookAttempts = 50;                 // 50 tries 100 ms apart: five seconds for WinUI to attach the content
    private const int PlatformHookRetryMilliseconds = 100;
    private const double MenuWidth = 300;
    private const double QuickOpenWidth = 460;
    private readonly ShellStartup _startup;
    private readonly string? _initialPath;
    private readonly EditorHost _host;
    private readonly ShellModel _shell;
    private readonly ShellEditCommands _editCommands;
    private readonly QuickOpen _quickOpen;
    private readonly FolderTreeView _tree = new();
    private readonly ExplorerView _explorer;
    private readonly TabStripView _tabStrip;
    private readonly BreadcrumbView _breadcrumb;
    private readonly ContentView _editorArea = new();
    private readonly List<EditorView> _views = [];
    private readonly Label _empty;
    private readonly FlexLayout _findBar;
    private readonly Entry _query;
    private readonly Entry _replacement;
    private readonly CraftInput _queryBox;
    private readonly CraftInput _replacementBox;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWord;
    private readonly Label _matchCount;
    private readonly HorizontalStackLayout _replaceRow;
    private readonly List<Label> _findLabels = [];
    private readonly List<Button> _findButtons = [];
    private readonly TitleBarView _titleWide;
    private readonly TitleBarView _titleNarrow;
    private readonly ContentView _titleRow = new();
    private readonly PopupLayer _popup = new();
    private readonly CraftDialog _dialog;
    private readonly IEditorKeyHandler _keys;
    private readonly RailView _rail;
    private readonly BottomPanelView _bottom;
    private readonly TerminalManager _terminals;
    private readonly TerminalPanelView _terminalPanel;
    private readonly StatusItemsView _pluginStatus;
    private readonly StatusBarView _status;
    private readonly ActivityStatusView _activity;
    private readonly BellView _bell;
    private readonly NotificationCenterView _centre;
    private readonly ToastView _toast;
    private readonly UiStallReports _stallReports;
    private UiWatchdog? _watchdog;
    private IDispatcherTimer? _noticeTimer;
    private readonly ConversationsView _conversations;
    private readonly SplitPanel _sideSplit = new(SplitEdge.End);
    private readonly SplitPanel _chatSplit = new(SplitEdge.Start);
    private readonly SplitPanel _bottomSplit = new(SplitEdge.Top);
    private readonly Dictionary<string, PanelView> _sideViews = new(StringComparer.Ordinal);
    private readonly Grid _body;
    private readonly Grid _center;
    private readonly BoxView _railLine;
    private readonly BoxView _statusLine;
    private readonly BoxView _titleLine;
    private readonly List<IDisposable> _platformHooks = [];
    private readonly List<IModelProvider> _watchedProviders = [];
    private ShellPalette _palette;
    private TitleBar? _windowTitleBar;
    private bool _chatSheetOpen;
    private bool _keysRouted;
    private bool _closeGuarded;
    private bool _disposed;
    private Window? _window;
    private EditorView? _shown;
    private bool _started;
    private string _lastQuery = "";

    /// <summary>The shell, opening <paramref name="path"/> when given (a folder as the workspace, a file in a tab; else an untitled document).</summary>
    public ShellPage(ShellStartup startup, string? path)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _initialPath = path;
        ShellModel? shell = null;
        CodeFontPicker.DrawnFonts = startup.Platform.DrawnFonts;
        // The owner's note 10: every question, message and choice is Craft's dialog over the window (themed in ApplyTheme).
        _dialog = new CraftDialog(new ShellPalette(EditorThemes.Get(EditorThemes.Dark), startup.Options), action => Dispatcher.Dispatch(action));
        _host = new EditorHost(startup.Options, action => Dispatcher.Dispatch(action), new MauiEditorPresenter(this, _dialog, () => shell?.Theme ?? EditorThemes.Get(EditorThemes.Dark)), startup.Settings.Read);
        _keys = _dialog.Queue.Before(_host);
        _dialog.FallbackFocus = () => _shown;
        shell = new ShellModel(_host, startup.Settings, startup.Recent, startup.Platform.FileDialogs, new MauiShellUi(_dialog));
        _shell = shell;
        _shell.EditorOf = tab => (tab.View as EditorView)?.Model;     // format on save goes through the tab's editor
        _palette = new ShellPalette(_shell.Theme, _shell.Options);
        _editCommands = new ShellEditCommands(() => _shown?.Model, _shell);
        _quickOpen = new QuickOpen(_host, _shell.Options.QuickOpenRows) { CraftOptionsLimit = _shell.Options.QuickOpenFiles };
        _pluginStatus = new StatusItemsView(_host);
        // The plug-ins' conversation panels (the AI plug-in's chat and agent run): the design's chat column when wide (its
        // splitter and close button, phase U2), a sheet over the editor when narrow (PhoneChat.dc.html); craft.chat
        // (Ctrl+Alt+I) shows or hides it, a plug-in's IEditorHost.ShowPanel brings one forward.
        _conversations = new ConversationsView(_host, _shell.Options.FontFamily);
        _conversations.LeaveRequested += (_, _) => _shown?.Focus();
        _chatSplit.Panel = _conversations;
        _conversations.PanelsChanged += (_, _) => ApplyLayout();
        _conversations.CloseRequested += (_, _) => CloseChat();
        _conversations.ModelChip.Tapped += (_, _) => _ = PickModelAsync();
        _host.PanelShowRequested += OnPanelShowRequested;
        _host.PanelsChanged += OnHostPanelsChanged;
        _host.PendingChordChanged += OnPendingChordChanged;
        _host.SelectionRequested += OnSelectionRequested;
        // A plug-in's IEditorHost.OpenExternalAsync (the learning plug-in's folder): the system's own launcher, any platform.
        _host.OpenExternal = path => Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(path));
        RegisterAppCommands();

        _titleWide = new TitleBarView(_palette);
        _titleNarrow = new TitleBarView(_palette);
        _titleNarrow.SetNarrow(true);
        foreach (var bar in (ReadOnlySpan<TitleBarView>)[_titleWide, _titleNarrow])
        {
            bar.MenuRequested += (_, request) => ShowMenu(request.Menu, request.Anchor);
            bar.NarrowFilesTapped += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.ToggleTree);
            bar.NarrowUndoTapped += (_, _) => _shown?.Model?.OnKey(EditorKey.Z, KeyModifiers.Control);
            bar.NarrowCommandsTapped += (_, _) => _ = CommandPalette.ShowAsync(_dialog, _host);
        }

        _titleWide.SearchChanged += (_, text) => ShowQuickOpen(text);
        _titleWide.SearchFocused += (_, _) => ShowQuickOpen(_titleWide.Search.Text ?? "");
        _titleWide.SearchSubmitted += (_, _) => _ = PickQuickOpenAsync(0);
        _popup.Closed += (_, _) => _lastQuery = "";

        _rail = new RailView(_host, _palette);
        _tree.Indent = _shell.Options.TreeIndent;
        _tree.FileActivated += (_, file) => _ = _shell.OpenFileAsync(file, fromTree: true);
        _explorer = new ExplorerView(_tree, _palette);
        _explorer.SetFolder(null);
        _sideSplit.Panel = _explorer;
        _tabStrip = new TabStripView(_shell.Tabs, _shell.Pages);
        _tabStrip.CloseRequested += (_, tab) => _ = _shell.CloseTabAsync(tab);
        _tabStrip.ListRequested += (_, _) => _ = PickTabAsync();
        _breadcrumb = new BreadcrumbView(_palette);
        _empty = _palette.Label("No file is open.\nCtrl+O opens a file, Ctrl+Shift+O a folder, Ctrl+P a file of the folder by name, Ctrl+N a new file; Ctrl+Shift+P lists every command.", _palette.Secondary);
        _empty.HorizontalTextAlignment = TextAlignment.Center;
        _empty.LineBreakMode = LineBreakMode.WordWrap;
        _empty.Margin = new Thickness(24);

        _query = _palette.Input("Find", 30);
        _queryBox = _palette.Field(_query);
        _queryBox.WidthRequest = 220;
        _query.TextChanged += OnQueryChanged;
        _query.Completed += (_, _) => FindNext();
        _replacement = _palette.Input("Replace", 30);
        _replacementBox = _palette.Field(_replacement);
        _replacementBox.WidthRequest = 220;
        _matchCase = new CheckBox();
        _matchCase.CheckedChanged += OnFindOptionChanged;
        _wholeWord = new CheckBox();
        _wholeWord.CheckedChanged += OnFindOptionChanged;
        _matchCount = FindLabel("");
        _matchCount.Margin = new Thickness(8, 0);
        var previous = FindButton("Previous", FindPrevious);
        var next = FindButton("Next", FindNext);
        var replaceOne = FindButton("Replace", () =>
        {
            _shown?.Model?.Find.ReplaceOne(_replacement.Text ?? "");
            UpdateCount();
        });
        var replaceAll = FindButton("Replace all", ReplaceAll);
        var close = FindButton("Close", CloseFind);
        _replaceRow = new HorizontalStackLayout { Spacing = 6, Children = { _replacementBox, replaceOne, replaceAll } };
        _findBar = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            Padding = new Thickness(12, 4),
            IsVisible = false,
            Children =
            {
                _queryBox,
                new HorizontalStackLayout { Children = { _matchCase, FindLabel("Match case") } },
                new HorizontalStackLayout { Children = { _wholeWord, FindLabel("Whole word") } },
                previous,
                next,
                _matchCount,
                _replaceRow,
                close,
            },
        };

        _bottom = new BottomPanelView(_host, _palette, OnProblemChosen) { CodeActionRequested = OnCodeActionRequested };
        _bottom.CollapseRequested += (_, _) => _ = _shell.Panels.SetOpenAsync(ShellPanel.Bottom, false);
        _bottomSplit.Panel = _bottom;

        // Each panel's splitter: a drag kept within what the panel and the editor share, written to the workspace's
        // settings when it ends (not on every move).
        Wire(_sideSplit, ShellPanel.Explorer, () => Width - _rail.Width - (_chatSplit.IsVisible ? _chatSplit.Width : 0));
        Wire(_chatSplit, ShellPanel.Chat, () => Width - _rail.Width - (_sideSplit.IsVisible ? _sideSplit.Width : 0));
        Wire(_bottomSplit, ShellPanel.Bottom, () => (_center?.Height ?? 0) - _tabStrip.Height - _breadcrumb.Height);

        // Phase T: the terminals in the Terminal tab, on the platform's host (ConPTY on Windows) when it has one.
        if (startup.Platform.TerminalHost is { } terminalHost)
        {
            TerminalHosts.Register(terminalHost);
        }

        _terminals = new TerminalManager(_host, () => _shell.Options, new MauiEditorClipboard());
        _host.Terminals = _terminals;
        _terminalPanel = new TerminalPanelView(_terminals, _palette);
        _bottom.TerminalContent = _terminalPanel;
        _terminals.ViewRequested += (_, request) => OnTerminalView(request);
        _tree.OpenInTerminalRequested += (_, folder) => _terminals.OpenInFolder(folder);
        // Phase U: the running operations in the status bar, the bell, the notification centre and its toasts.
        _activity = new ActivityStatusView(_host.ActivityCenter, _palette);
        _bell = new BellView(_palette);
        _centre = new NotificationCenterView(_host.Notifications, _host.ActivityCenter, _palette);
        _toast = new ToastView(_host.Notifications, _palette);
        _stallReports = new UiStallReports(startup.Options);
        _activity.Tapped += (_, _) => ToggleCentre(true);
        _bell.Tapped += (_, _) => ToggleCentre();
        _centre.CloseRequested += (_, _) => ToggleCentre(false);
        _toast.ShowCentreRequested += (_, _) => ToggleCentre(true);
        _host.Notifications.OpenLog = () => ShowBottom(BottomTab.Output);
        _host.Notifications.Changed += OnNotificationsChanged;
        _host.Notifications.Toast += OnToast;
        _status = new StatusBarView(_palette, _pluginStatus, _activity, _bell);
        _status.ProblemsTapped += (_, _) => ShowBottom(BottomTab.Problems);

        _railLine = _palette.Line(vertical: true);
        _statusLine = _palette.Line();
        _titleLine = _palette.Line();

        _center = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            ],
        };
        _center.Add(_tabStrip, 0, 0);
        _center.Add(_breadcrumb, 0, 1);
        _center.Add(_findBar, 0, 2);
        _center.Add(_editorArea, 0, 3);
        _center.Add(_bottomSplit, 0, 4);

        // The rail, its side view (the explorer or a plug-in panel), the editor area, the chat column: no fixed column of
        // panels at the right (phase U2).
        _body = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto),
            ],
        };
        _body.Add(_rail, 0, 0);
        _body.Add(_railLine, 1, 0);
        _body.Add(_sideSplit, 2, 0);
        _body.Add(_center, 3, 0);
        _body.Add(_chatSplit, 4, 0);

        var root = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
        };
        root.Add(_titleRow, 0, 0);
        root.Add(_titleLine, 0, 1);
        root.Add(_body, 0, 2);
        root.Add(_statusLine, 0, 3);
        root.Add(_status, 0, 4);
        root.Add(_toast, 0, 2);
        root.Add(_centre, 0, 2);
        root.Add(_popup, 0, 0);
        Grid.SetRowSpan(_popup, 5);
        root.Add(_dialog, 0, 0);
        Grid.SetRowSpan(_dialog, 5);
        Content = root;

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
        _shell.ViewRequested += (_, view) => OnViewRequested(view);
        _shell.Layout.Changed += (_, _) => ApplyLayout();
        _shell.Panels.Changed += (_, _) => ApplyLayout();
        _shell.Pages.Changed += (_, _) => ShowActive();
        _shell.Pages.Closed += (_, page) =>
        {
            (page.View as IDisposable)?.Dispose();
            page.View = null;
        };
        EditorMenus.Changed += OnMenusChanged;
        SizeChanged += (_, _) => _shell.Layout.UpdateWidth(Width);
        ShowActive();
        ApplyTheme();
        ApplyLayout();
    }

    /// <summary>The window the page is in: its title shows the file, the dirty marker and the folder; with <c>CRAFT_TITLE_BAR=window</c> the title bar's row goes into its caption.</summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        if (_shell.Options.TitleBar == "window")
        {
            _windowTitleBar = new TitleBar { Content = _titleWide, HeightRequest = 40, BackgroundColor = _palette.Ground, ForegroundColor = _palette.Text };
        }

        UpdateTitle();
        ApplyLayout();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchdog?.Dispose();
        _noticeTimer?.Stop();
        _host.Notifications.Changed -= OnNotificationsChanged;
        _host.Notifications.Toast -= OnToast;
        _activity.Dispose();
        _centre.Dispose();
        EditorMenus.Changed -= OnMenusChanged;
        _host.PanelShowRequested -= OnPanelShowRequested;
        _host.PanelsChanged -= OnHostPanelsChanged;
        _host.PendingChordChanged -= OnPendingChordChanged;
        _host.SelectionRequested -= OnSelectionRequested;
        _conversations.Dispose();
        foreach (var view in _sideViews.Values)
        {
            view.Dispose();
        }

        _sideViews.Clear();
        foreach (var provider in _watchedProviders)
        {
            provider.StatusChanged -= OnModelStatusChanged;
        }

        foreach (var hook in _platformHooks)
        {
            hook.Dispose();
        }

        _bottom.Dispose();
        _host.Terminals = null;
        _terminalPanel.Dispose();
        _terminals.Dispose();
        if (_startup.Platform.TerminalHost is { } terminalHost)
        {
            TerminalHosts.Unregister(terminalHost.Name);
        }

        _editCommands.Dispose();
        _shell.Dispose();
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
        StartWatchdog();
        HookPlatform();

        // How each plug-in's load went (at start-up, before the window), kept in the notification centre; a failure toasts.
        foreach (var plugin in _startup.Plugins)
        {
            if (plugin.Disabled)
            {
                continue;
            }

            _host.Notifications.AddEnded(EditorHost.HostSource, $"Plug-in {plugin.Name}",
                plugin.Loaded ? "loaded" + (plugin.Note is { } note ? " · " + note : "") : $"did not load ({plugin.Folder}): {plugin.Error}",
                plugin.Loaded ? NotificationSeverity.Success : NotificationSeverity.Error, plugin.LoadTime);
            if (!plugin.Loaded && plugin.Error is not null)
            {
                _status.Notice.Text = $"Plug-in {plugin.Name} did not load";
                _host.Log(EditorLogLevel.Error, $"Plug-in {plugin.Name} ({plugin.Folder}) did not load: {plugin.Error}");
            }
        }

        // The plug-ins start first, so they see the first document open (the sample's word count counts it); a failure
        // is a notification with a toast (Craft's own card), not the platform's message box.
        foreach (var activation in _host.RefreshPlugins())
        {
            if (activation.Error is not null)
            {
                _status.Notice.Text = $"Plug-in {activation.Name} did not start";
                _host.Notify($"Plug-in {activation.Name} did not start", activation.Error, NotificationSeverity.Error);
            }
        }

        // What the plug-ins registered: their rail items' commands, their menus, the model providers' status.
        _rail.Rebuild();
        _titleWide.RebuildMenus();
        WatchModels();

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
            _host.Notify("A setting was not taken", string.Join("\n", problems) + "\nThe settings page (Ctrl+Comma) shows each one.", NotificationSeverity.Error,
                [new NotificationAction("Open settings", _ => _host.ExecuteCommandAsync(ShellCommandIds.Settings), Primary: true)]);
        }
    }

    // Phase U: the UI watchdog (CRAFT_UI_WATCHDOG_MS, CRAFT_UI_STALL_MS); its stalls show in the status bar, Output, the
    // centre and the telemetry tab, each written as a stall report under CRAFT_CRASH_FOLDER.
    private void StartWatchdog()
    {
        var options = _startup.Options;
        if (options.UiWatchdogMilliseconds <= 0 || _watchdog is not null)
        {
            return;
        }

        _watchdog = new UiWatchdog(action => Dispatcher.Dispatch(action), _host.Labels, options.UiWatchdogMilliseconds, options.UiStallMilliseconds)
        {
            WriteReport = _stallReports.Write,
        };
        _watchdog.Stalled += (_, stall) => _host.Dispatch("watchdog: show a stall", () => OnStall(stall));
        _watchdog.Start();
    }

    private void OnStall(UiStall stall)
    {
        if (_disposed)
        {
            return;
        }

        string line = ActivityText.Stall(stall.Duration, stall.Label);
        _status.Notice.Text = line;
        _noticeTimer?.Stop();
        _noticeTimer = Dispatcher.CreateTimer();
        _noticeTimer.Interval = TimeSpan.FromSeconds(10);
        _noticeTimer.IsRepeating = false;
        _noticeTimer.Tick += (_, _) =>
        {
            if (_status.Notice.Text == line)
            {
                _status.Notice.Text = "";
            }
        };
        _noticeTimer.Start();
        _host.Record("ui.stall", stall.Duration.TotalMilliseconds, "ms");
        string report = stall.ReportPath is { } path ? " Report: " + path : "";
        _host.Log(EditorLogLevel.Warning, $"{line}.{report}");
        var actions = stall.ReportPath is { } file
            ? (IReadOnlyList<NotificationAction>)[new NotificationAction("Open report", _ => Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(
                new Microsoft.Maui.ApplicationModel.OpenFileRequest("Stall report", new Microsoft.Maui.Storage.ReadOnlyFile(file))), Dismisses: false)]
            : [];
        _host.Notifications.Post(NotificationCenter.WatchdogSource, "The window was blocked", $"{ActivityText.Duration(stall.Duration)} by {stall.Label}"
            + (stall.FullCollections > 0 ? $" · {stall.FullCollections} full collections, {stall.CollectionPause.TotalMilliseconds:F0} ms paused" : ""), NotificationSeverity.Warning, actions);
    }

    private void ToggleCentre(bool? open = null)
    {
        _centre.Toggle(open);
        if (_centre.IsVisible)
        {
            _toast.Hide();
        }

        OnNotificationsChanged(this, EventArgs.Empty);
    }

    private void OnNotificationsChanged(object? sender, EventArgs e) => _bell.Show(_host.Notifications.Unseen, _host.ActivityCenter.Running.Count);

    private void OnToast(object? sender, NotificationEntry entry)
    {
        if (!_centre.IsVisible)
        {
            _toast.Show(entry, _shell.Options.ToastMilliseconds);
        }
    }

    private static string LanguageText(EditorModel model, string? filePath)
    {
        if (model.Language is not null || filePath is null)
        {
            return model.Language is { } language ? LanguageLabel(language) : model.LanguageName;
        }

        var extension = Path.GetExtension(filePath);
        bool csharp = extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csx", StringComparison.OrdinalIgnoreCase);
        return csharp ? model.LanguageName + " (the C# plug-in is not installed or is disabled)" : model.LanguageName;
    }

    // The language as the status bar names it: its summary's first words before a comma ("C#"), else its name.
    private static string LanguageLabel(ILanguage language)
    {
        var summary = language.Summary.AsSpan();
        int comma = summary.IndexOf(',');
        return comma is > 0 and <= 16 ? summary[..comma].ToString() : language.Name;
    }

    private Label FindLabel(string text)
    {
        var label = _palette.Label(text);
        _findLabels.Add(label);
        return label;
    }

    private Button FindButton(string text, Action clicked)
    {
        var button = _palette.SecondaryButton(text);
        button.Clicked += (_, _) => clicked();
        _findButtons.Add(button);
        return button;
    }

    // The platform's pieces need the window's native view and its content, which WinUI may attach after the page has
    // appeared: each piece is hooked on its own and retried until it takes (the keys outside the editor, the
    // unsaved-changes question on closing), and only a piece still missing after the tries is reported.
    private void HookPlatform(int attempt = 0)
    {
        if (_window is null)
        {
            return;
        }

        string? missing = null;
        if (!_keysRouted)
        {
            try
            {
                _platformHooks.Add(_startup.Platform.RouteKeys(_window, _keys));
                _keysRouted = true;
            }
            catch (InvalidOperationException error)
            {
                missing = error.Message;
            }
        }

        if (!_closeGuarded)
        {
            try
            {
                _platformHooks.Add(_startup.Platform.GuardClose(_window, () => _shell.Tabs.HasUnsaved, () => _shell.ConfirmCloseAllAsync()));
                _closeGuarded = true;
            }
            catch (InvalidOperationException error)
            {
                missing ??= error.Message;
            }
        }

        if (missing is null)
        {
            return;
        }

        if (attempt < PlatformHookAttempts)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(PlatformHookRetryMilliseconds), () => _host.Labels.Run("shell: hook the platform", () => HookPlatform(attempt + 1)));
        }
        else
        {
            _status.Notice.Text = missing;
        }
    }

    /// <summary>The app's own commands, on the same contracts as the plug-ins': the palette (Ctrl+Shift+P) and the Plug-ins page (the shell registers the rest).</summary>
    private void RegisterAppCommands()
    {
        EditorCommands.Register(new EditorCommand(ShellCommandIds.CommandPalette, "View: Show all commands", (_, _) => CommandPalette.ShowAsync(_dialog, _host)));
        EditorCommands.Register(new EditorCommand(ShellCommandIds.Plugins, "Plug-ins: Show the installed plug-ins", (_, _) =>
        {
            _shell.Pages.Open(ShellPageIds.Plugins, "Plug-ins");
            return Task.CompletedTask;
        }));
        EditorKeyBindings.Register(new KeyBinding(new KeyChord(EditorKey.P, KeyModifiers.Control | KeyModifiers.Shift), ShellCommandIds.CommandPalette));
        EditorCommands.Register(new EditorCommand("craft.chat", "View: Show or hide the chat", (_, _) =>
        {
            ToggleChat();
            return Task.CompletedTask;
        }, _ => _conversations.HasPanels, "The plug-ins' chat column (a sheet in the narrow layout)"));
        EditorKeyBindings.Register(new KeyBinding(new KeyChord(EditorKey.I, KeyModifiers.Control | KeyModifiers.Alt), "craft.chat"));
        EditorCommands.Register(new EditorCommand("craft.notifications", "View: Show or hide the notifications", (_, _) =>
        {
            ToggleCentre();
            return Task.CompletedTask;
        }, null, "The notification centre: running operations with Cancel, how they ended, the plug-ins' messages"));
    }

    private void ToggleChat()
    {
        if (_shell.Layout.IsNarrow)
        {
            _chatSheetOpen = !_chatSheetOpen;
            ApplyLayout();
        }
        else
        {
            _ = _shell.Panels.ToggleAsync(ShellPanel.Chat);   // kept per workspace; Changed lays the page out
        }

        if (_chatSplit.IsVisible)
        {
            _conversations.FocusInput();
        }
    }

    // The chat column's close button.
    private void CloseChat()
    {
        if (_shell.Layout.IsNarrow)
        {
            _chatSheetOpen = false;
            ApplyLayout();
        }
        else
        {
            _ = _shell.Panels.SetOpenAsync(ShellPanel.Chat, false);
        }

        _shown?.Focus();
    }

    // A splitter's drag: within the room the panel and the editor share; its end written to the settings once.
    private void Wire(SplitPanel split, ShellPanel panel, Func<double> room)
    {
        split.Clamp = size => _shell.Panels.Clamp(size, room());
        split.Resized += (_, size) =>
        {
            _shell.Panels.Resize(panel, size);
            _ = _shell.Panels.CommitAsync(panel);
        };
    }

    // A plug-in panel as the rail's side view, made when first shown and kept while the panel is the host's.
    private PanelView? SideViewOf(string id)
    {
        if (_sideViews.TryGetValue(id, out var view))
        {
            return view;
        }

        if (_host.Panels.FirstOrDefault(p => p.Id == id && p is not IConversationPanel) is not { } panel)
        {
            return null;
        }

        view = new PanelView(_host, panel) { BackgroundColor = _palette.Surface };
        view.SetColors(_palette.Text, _palette.Secondary);
        _sideViews[id] = view;
        return view;
    }

    private void OnHostPanelsChanged(object? sender, EventArgs e)
    {
        foreach (var (id, view) in _sideViews.ToArray())
        {
            if (!_host.Panels.Any(p => ReferenceEquals(p, view.Panel)))
            {
                view.Dispose();
                _sideViews.Remove(id);
            }
        }

        ApplyLayout();
    }

    // A page tab's view, made when the page is first shown.
    private View PageViewOf(PageTab page)
    {
        page.View ??= page.Id switch
        {
            ShellPageIds.Settings => new SettingsPage(_shell),
            ShellPageIds.Models => new ModelsPage(_shell),
            ShellPageIds.Plugins => new PluginsPage(_shell, () => PluginList.Rows(CraftPlugins.Installed, _host.Plugins)),
            _ => _palette.Label($"No view for the page '{page.Id}'.", _palette.Secondary),
        };
        return (View)page.View;
    }

    private void OnPanelShowRequested(object? sender, string panelId)
    {
        if (!_conversations.Show(panelId))
        {
            return;
        }

        if (_shell.Layout.IsNarrow)
        {
            _chatSheetOpen = true;
        }
        else
        {
            _ = _shell.Panels.SetOpenAsync(ShellPanel.Chat, true);
        }

        ApplyLayout();
        _conversations.FocusInput();
    }

    // A Problems row's Fix or Explain: the first action of that kind the registered providers offer for the diagnostic.
    // An explanation answers in the chat, so the chat shows; an edit arrives as a diff the user accepts.
    private void OnCodeActionRequested(EditorDiagnostic diagnostic, string kind)
    {
        if (_shell.Tabs.Active is not { } active)
        {
            return;
        }

        var context = new CodeActionContext(active.HostDocument, diagnostic.Start, diagnostic.End, [diagnostic]);
        var actions = CodeActionProviders.ActionsFor(context, (name, error) => _host.Log(EditorLogLevel.Warning, $"The code actions of {name} failed.", error));
        if (actions.FirstOrDefault(a => a.Kind == kind) is not { } action)
        {
            _status.Notice.Text = $"No plug-in offers to {kind} this problem.";
            return;
        }

        if (kind is CodeActionKinds.Explain)
        {
            OnPanelShowRequested(this, "ai.chat");
        }

        _ = RunCodeActionAsync(action);
    }

    private async Task RunCodeActionAsync(CodeAction action)
    {
        try
        {
            await action.RunAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _host.Log(EditorLogLevel.Error, $"The code action '{action.Title}' failed.", error);
        }
    }

    private async Task PickTabAsync()
    {
        var tabs = _shell.Tabs.Tabs.ToArray();
        int picked = await _dialog.PickAsync("Tabs", [.. tabs.Select(TabStripView.TitleOf)]);
        if (picked >= 0 && picked < tabs.Length)
        {
            _shell.Tabs.Activate(tabs[picked]);
        }
    }

    private void OnMenusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => _host.Labels.Run("shell: rebuild the menus", _titleWide.RebuildMenus));
        }
        else
        {
            _titleWide.RebuildMenus();
        }
    }

    // A menu: its rows (the registered items whose command is registered) under its button; a click runs the command.
    private void ShowMenu(string menu, View anchor)
    {
        var rows = ShellMenus.Rows(menu, _host);
        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 4) };
        if (rows.Count == 0)
        {
            var none = _palette.Label("Nothing here yet: plug-ins add items to this menu.", _palette.Secondary, _palette.SmallSize);
            none.Margin = new Thickness(12, 6);
            none.LineBreakMode = LineBreakMode.WordWrap;
            stack.Children.Add(none);
        }

        foreach (var row in rows)
        {
            if (row.StartsGroup)
            {
                var line = _palette.Line();
                line.Margin = new Thickness(0, 4);
                stack.Children.Add(line);
            }

            var cell = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(12, 0), MinimumHeightRequest = 28 };
            cell.Add(_palette.Label(row.Title, row.Enabled ? _palette.Text : _palette.Secondary), 0, 0);
            if (row.Chord is { } chord)
            {
                cell.Add(_palette.Label(chord, _palette.Secondary, _palette.SmallSize), 1, 0);
            }

            if (row.Enabled)
            {
                var tap = new TapGestureRecognizer();
                string command = row.CommandId;
                tap.Tapped += (_, _) =>
                {
                    _popup.Hide();
                    _ = _host.ExecuteCommandAsync(command);
                };
                cell.GestureRecognizers.Add(tap);
            }

            stack.Children.Add(cell);
        }

        var box = _palette.Box(stack, 6, new Thickness(0), _palette.Raised, _palette.BorderStrong);
        _popup.Show(box, _popup.Below(anchor), MenuWidth);
    }

    // Quick open's list under the search box; the files are listed when a folder opens.
    private void ShowQuickOpen(string text)
    {
        if (_shell.Layout.IsNarrow)
        {
            return;
        }

        _lastQuery = text;
        var items = _quickOpen.Query(text);
        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 4) };
        if (items.Count == 0)
        {
            string why = QuickOpen.IsLineQuery(text) ? (_shown is null ? "Open a file to go to one of its lines." : "Type a line number (:42, or :42:7 for a column).")
                : text.TrimStart().StartsWith('>') ? "No command matches."
                : _shell.Workspace is null ? "Open a folder (Ctrl+Shift+O) to find its files by name; type '>' for a command."
                : _quickOpen.IsIndexing ? "Listing the folder's files…" : "No file matches; type '>' for a command.";
            var none = _palette.Label(why, _palette.Secondary, _palette.SmallSize);
            none.Margin = new Thickness(12, 6);
            stack.Children.Add(none);
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var cell = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10, Padding = new Thickness(12, 0), MinimumHeightRequest = 28 };
            if (i == 0)
            {
                cell.BackgroundColor = _palette.Selection;
            }

            cell.Add(_palette.Label(item.Text), 0, 0);
            if (item.Detail is { } detail)
            {
                cell.Add(_palette.Label(detail, _palette.Secondary, _palette.SmallSize), 1, 0);
            }

            var tap = new TapGestureRecognizer();
            int index = i;
            tap.Tapped += (_, _) => _ = PickQuickOpenAsync(index);
            cell.GestureRecognizers.Add(tap);
            stack.Children.Add(cell);
        }

        if (_quickOpen.Capped && !text.TrimStart().StartsWith('>'))
        {
            var capped = _palette.Label(string.Create(CultureInfo.InvariantCulture, $"The first {_quickOpen.Files.Count:N0} files are listed (CRAFT_QUICK_OPEN_FILES)."), _palette.Secondary, _palette.CaptionSize);
            capped.Margin = new Thickness(12, 4);
            stack.Children.Add(capped);
        }

        var box = _palette.Box(stack, 6, new Thickness(0), _palette.Raised, _palette.BorderStrong);
        var at = _popup.Below(_titleWide.Search);
        _popup.Show(box, at with { X = Math.Max(0, at.X) }, QuickOpenWidth);
    }

    // A two-key chord's first key: the status bar says the second is awaited (VS Code's "(Ctrl+K) was pressed").
    private void OnPendingChordChanged(object? sender, EventArgs e)
    {
        if (_host.PendingChord is { } first)
        {
            _status.Notice.Text = $"({first.DisplayText}) was pressed. Waiting for the second key…";
        }
        else if ((_status.Notice.Text ?? "").EndsWith("Waiting for the second key…", StringComparison.Ordinal))
        {
            _status.Notice.Text = "";
        }
    }

    private async Task PickQuickOpenAsync(int index)
    {
        var items = _quickOpen.Query(_lastQuery.Length > 0 ? _lastQuery : _titleWide.Search.Text ?? "");
        _popup.Hide();
        _titleWide.Search.Text = "";
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        var item = items[index];
        if (item.Line > 0)
        {
            _shown?.Model?.GoToLine(item.Line, item.Column);
        }
        else if (item.CommandId is { } command)
        {
            await _host.ExecuteCommandAsync(command);
        }
        else if (item.Path is { } path)
        {
            await _shell.OpenFileAsync(path);
        }

        _shown?.Focus();
    }

    // Narrow: a prompt for the name, then the matches as a list (the phone has no room for the box).
    private async Task QuickOpenNarrowAsync()
    {
        string? text = await _dialog.PromptAsync("Open", "", "A file's name, or '>' and a command", "Search");
        if (text is null)
        {
            return;
        }

        var items = _quickOpen.Query(text);
        if (items.Count == 0)
        {
            await _dialog.ShowMessageAsync("Open", "Nothing matches.");
            return;
        }

        var titles = items.Select(i => i.Detail is null ? i.Text : $"{i.Text}  ({i.Detail})").ToArray();
        int index = await _dialog.PickAsync("Open", titles);
        if (index >= 0)
        {
            _lastQuery = text;
            await PickQuickOpenAsync(index);
        }
    }

    // Narrow: a prompt for the line (and column).
    private async Task GoToLineNarrowAsync()
    {
        if (_shown?.Model is not { } model)
        {
            return;
        }

        string? text = await _dialog.PromptAsync("Go to line", "", $"A line from 1 to {model.Snapshot.LineCount} (line:column for a column)", "Go");
        if (text is not null && _quickOpen.Query(":" + text) is [{ Line: > 0 } item])
        {
            model.GoToLine(item.Line, item.Column);
            _shown?.Focus();
        }
    }

    private void OnViewRequested(string view)
    {
        switch (view)
        {
            case ShellViewNames.QuickOpen when _shell.Layout.IsNarrow:
                _ = QuickOpenNarrowAsync();
                break;
            case ShellViewNames.GoToLine when _shell.Layout.IsNarrow:
                _ = GoToLineNarrowAsync();
                break;
            case ShellViewNames.GoToLine:
                _titleWide.Search.Text = ":";
                _titleWide.Search.Focus();
                _titleWide.Search.CursorPosition = 1;
                ShowQuickOpen(":");
                break;
            case ShellViewNames.QuickOpen:
                _titleWide.Search.Focus();
                ShowQuickOpen(_titleWide.Search.Text ?? "");
                break;
            case ShellViewNames.TogglePanel:
                break;                       // the command toggled the panel's state (PanelLayout); Changed laid it out
            case ShellViewNames.Problems:
                ShowBottom(BottomTab.Problems);
                break;
            case ShellViewNames.Terminal:
                ShowBottom(BottomTab.Terminal);
                break;
            case ShellViewNames.Output:
                ShowBottom(BottomTab.Output);
                break;
            case ShellViewNames.Telemetry:
                ShowBottom(BottomTab.Telemetry);
                break;
        }
    }

    private void OnTerminalView(TerminalViewRequest request)
    {
        if (request == TerminalViewRequest.Toggle && _shell.Panels.IsOpen(ShellPanel.Bottom) && _bottom.Tab == BottomTab.Terminal)
        {
            _ = _shell.Panels.SetOpenAsync(ShellPanel.Bottom, false);
            return;
        }

        ShowBottom(BottomTab.Terminal);
        if (request != TerminalViewRequest.Reveal)
        {
            _host.Dispatch("shell: focus the terminal", _terminalPanel.FocusTerminal);   // a plug-in's output tab shows without taking the keyboard
        }
    }

    private void ShowBottom(BottomTab tab)
    {
        _bottom.Select(tab);
        _ = _shell.Panels.SetOpenAsync(ShellPanel.Bottom, true);
        ApplyLayout();
    }

    // The model chip and the status bar's model text follow the first provider's status (A1's "local").
    private void WatchModels()
    {
        foreach (var provider in ModelProviders.All)
        {
            if (!_watchedProviders.Contains(provider))
            {
                provider.StatusChanged += OnModelStatusChanged;
                _watchedProviders.Add(provider);
            }
        }

        UpdateModel();
    }

    private void OnModelStatusChanged(object? sender, EventArgs e) => _host.Dispatch("shell: the model's status", UpdateModel);

    private void UpdateModel()
    {
        var status = ShellText.ModelStatus()?.Status;
        string chip = ShellText.ModelChip(status);
        string? tooltip = status?.Roles is [{ } chat, ..] ? $"{chat.Role}: {chat.Model ?? "none set"} · {chat.State}" : "No model provider is installed (the AI plug-in brings \"local\").";
        var state = status?.ChatModel is null ? ModelChipState.None
            : status.Roles is [{ IsProblem: true }, ..] ? ModelChipState.Problem
            : status.IsLoading ? ModelChipState.Loading
            : ModelChipState.Ready;
        // The owner's note 11: the model chip is the composer's (the chat's and the agent's), with its device (note 6).
        _conversations.ModelChip.Set(chip, state, tooltip);
        _status.Model.Text = ShellText.ModelUsage(status);
    }

    // The composer's model chip: the provider's models and devices in a Craft dialog; a model picked becomes the model
    // setting and loads (off the UI thread, as an activity), a device the device setting, the last row opens the page.
    private async Task PickModelAsync()
    {
        var found = ShellText.ModelStatus();
        var status = found?.Status;
        var picks = ShellText.ModelPicks(status);
        string text = status?.ChatModel is { } model ? $"In use: {model} · {ShellText.PlannedDevice(status) ?? "device not known yet"}" : "No model is set.";
        int index = await _dialog.PickAsync("Model", [.. picks.Select(p => p.Label)], text: text);
        if (index < 0)
        {
            return;
        }

        var pick = picks[index];
        string? variable = pick.Kind switch
        {
            ModelPickKind.Model => status?.ModelSetting?.Variable,
            ModelPickKind.Device => status?.DeviceSetting?.Variable,
            _ => null,
        };
        if (variable is not null && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } fixedValue)
        {
            // The environment overrides the settings files: say so rather than write a value that cannot take effect.
            await _dialog.ShowMessageAsync("Set by the environment", $"{variable} is {fixedValue} in this process's environment, which overrides the settings. Clear it (or change it) and start Craft again to pick here.");
            return;
        }

        switch (pick.Kind)
        {
            case ModelPickKind.Model when status?.ModelSetting is { } setting:
                if (_shell.ApplySetting(SettingLayer.User, setting.Variable, pick.Value) is { } problem)
                {
                    await _dialog.ShowMessageAsync("The model was not set", problem);
                }
                else if (status.LoadCommand is { } load)
                {
                    await _host.ExecuteCommandAsync(load);
                }

                break;
            case ModelPickKind.Device when status?.DeviceSetting is { } setting:
                if (_shell.ApplySetting(SettingLayer.User, setting.Variable, pick.Value.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : pick.Value) is { } refused)
                {
                    await _dialog.ShowMessageAsync("The device was not set", refused);
                }

                break;
            default:
                await _host.ExecuteCommandAsync(ShellCommandIds.Models);
                break;
        }

        UpdateModel();
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
                    if (_shell.Pages.Active is null)
                    {
                        _editorArea.Content = _empty;
                    }
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
            KeyHandler = _keys,
            Labels = _host.Labels,
            Activities = _host.Activities,
            WorkspaceRoot = _shell.Workspace?.Root,
        };
        view.Open(tab.Document, tab.Language);
        view.DiagnosticsChanged += (_, _) =>
        {
            // Phase A1: the plug-ins read a document's diagnostics, and the diff view checks a change, through the host.
            if (view.Model is { } shownModel && _shell.Tabs.Tabs.FirstOrDefault(t => ReferenceEquals(t.View, view)) is { } owner)
            {
                _host.SetDiagnostics(owner.HostDocument, shownModel.Diagnostics);
                _host.SetSemanticSession(owner.HostDocument, shownModel.SemanticSession);
            }

            if (ReferenceEquals(view, _shown))
            {
                UpdateProblems();
            }
        };
        view.DefinitionRequested += OnDefinitionRequested;
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

    // The editor area: the page tab shown (settings, Models, Plug-ins), else the active document's editor, else the hint.
    private void ShowActive()
    {
        var page = _shell.Pages.Active;
        var tab = _shell.Tabs.Active;
        if (tab is not null && page is null)
        {
            tab.View ??= MakeView(tab);
        }

        var view = page is null ? tab?.View as EditorView : null;
        var content = page is not null ? PageViewOf(page) : (View?)view ?? _empty;
        _breadcrumb.IsVisible = !_shell.Layout.IsNarrow && page is null;
        if (!ReferenceEquals(view, _shown) || !ReferenceEquals(_editorArea.Content, content))
        {
            _shown = view;
            _editorArea.Content = content;
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

        _tree.ActivePath = tab?.FilePath;
        UpdateStatus();
        UpdateTitle();
        UpdateProblems();
    }

    private void UpdateProblems()
    {
        var model = _shown?.Model;
        _bottom.ShowProblems(model, _shell.Tabs.Active?.Title);
        _status.Problems.Text = model?.SemanticSession is null ? "" : ShellText.Problems(model.ErrorCount, model.WarningCount);
        _status.Problems.TextColor = model is { ErrorCount: > 0 } ? _palette.Error : _palette.Warning;
        var card = ShellText.Semantic(model);
        _explorer.SetSemantic(card);
        _status.Semantic.Text = card?.StatusText ?? "";
    }

    private void OnProblemChosen(int position)
    {
        if (_shown is { Model: { } model } view)
        {
            model.Select(position, position);
            model.EnsureCaretVisible();
            view.Focus();
        }
    }

    private async void OnDefinitionRequested(object? sender, DefinitionRequestedEventArgs e)
    {
        var location = e.Location;
        if (location.FilePath is not { } path)
        {
            _status.Notice.Text = location.Description;
            return;
        }

        try
        {
            await _shell.OpenFileAsync(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _status.Notice.Text = $"Could not open {path}: {error.Message}";
            return;
        }

        ShowActive();
        if (_shown?.Model is { } model && model.Document.FilePath is { } opened
            && string.Equals(Path.GetFullPath(opened), Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            int start = Math.Clamp(location.Start, 0, model.Snapshot.Length);
            model.Select(Math.Min(model.Snapshot.Length, start + location.Length), start);
            model.EnsureCaretVisible();
        }
    }

    // A plug-in moved a document's selection or typed into it (phase V: "go to line 40", dictation): its editor shows it.
    private void OnSelectionRequested(object? sender, EditorSelectionRequestEventArgs e)
    {
        foreach (var tab in _shell.Tabs.Tabs)
        {
            if (ReferenceEquals(tab.HostDocument, e.Document) && tab.View is EditorView { Model: { } model })
            {
                model.Select(e.Selection.Anchor, e.Selection.Caret);
                model.EnsureCaretVisible();
                return;
            }
        }
    }

    private void OnWorkspaceChanged()
    {
        _tree.Tree = _shell.Tree;
        _explorer.SetFolder(_shell.Workspace?.Root);
        _status.Branch.Text = _shell.Branch ?? "";
        _ = _quickOpen.IndexAsync(_shell.Workspace);
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

        _quickOpen.Rows = options.QuickOpenRows;
        _quickOpen.CraftOptionsLimit = options.QuickOpenFiles;
        _tree.Indent = options.TreeIndent;
        ApplyTheme();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var layout = _shell.Layout;
        var panels = _shell.Panels;
        bool narrow = layout.IsNarrow;
        bool windowBar = _windowTitleBar is not null && !narrow;
        if (_window is not null)
        {
            _window.TitleBar = windowBar ? _windowTitleBar : null;
        }

        _titleRow.Content = narrow ? _titleNarrow : windowBar ? null : _titleWide;
        _titleRow.IsVisible = _titleRow.Content is not null;
        _titleLine.IsVisible = _titleRow.IsVisible;
        _rail.IsVisible = _railLine.IsVisible = !narrow;

        // The side view: the explorer, or (wide only) the plug-in panel the rail chose; the narrow layout's tree as before.
        View side = _explorer;
        if (!narrow && panels.SideView != PanelLayout.ExplorerView && SideViewOf(panels.SideView) is { } panelView)
        {
            side = panelView;
        }

        bool sideVisible = narrow ? layout.TreeVisible : panels.IsOpen(ShellPanel.Explorer) && (!ReferenceEquals(side, _explorer) || layout.TreeVisible);
        if (!ReferenceEquals(_sideSplit.Panel, side))
        {
            _sideSplit.Panel = side;
        }

        _sideSplit.IsVisible = sideVisible;
        _sideSplit.Fill = narrow;
        _sideSplit.Resizable = !narrow;
        _sideSplit.Size = panels.SizeOf(ShellPanel.Explorer);
        _rail.Active = !narrow && sideVisible ? (ReferenceEquals(side, _explorer) ? "craft.explorer" : panels.SideView) : null;
        _center.IsVisible = layout.EditorVisible;

        _chatSplit.IsVisible = _conversations.HasPanels && (narrow ? _chatSheetOpen : panels.IsOpen(ShellPanel.Chat));
        if (narrow)
        {
            // The sheet: over the body's lower part, the width of the window, no splitter.
            Grid.SetColumn(_chatSplit, 0);
            Grid.SetColumnSpan(_chatSplit, 5);
            _chatSplit.Fill = true;
            _chatSplit.Resizable = false;
            _chatSplit.VerticalOptions = LayoutOptions.End;
            _chatSplit.HeightRequest = Math.Max(240, Height * 0.62);
        }
        else
        {
            Grid.SetColumn(_chatSplit, 4);
            Grid.SetColumnSpan(_chatSplit, 1);
            _chatSplit.Fill = false;
            _chatSplit.Resizable = true;
            _chatSplit.Size = panels.SizeOf(ShellPanel.Chat);
            _chatSplit.VerticalOptions = LayoutOptions.Fill;
            _chatSplit.HeightRequest = -1;
        }

        _bottomSplit.IsVisible = panels.IsOpen(ShellPanel.Bottom) && !narrow;
        _bottomSplit.Size = panels.SizeOf(ShellPanel.Bottom);
        _breadcrumb.IsVisible = !narrow && _shell.Pages.Active is null;
        _body.ColumnDefinitions[2].Width = narrow && layout.TreeVisible ? GridLength.Star : GridLength.Auto;
        _body.ColumnDefinitions[3].Width = layout.EditorVisible ? GridLength.Star : new GridLength(0);
        _tabStrip.Compact = narrow;
        _status.SetNarrow(narrow);
        if (_popup.IsOpen)
        {
            _popup.Hide();
        }
    }

    private void ApplyTheme()
    {
        var theme = _shell.Theme;
        _palette = new ShellPalette(theme, _shell.Options);
        if (Application.Current is { } application)
        {
            application.UserAppTheme = theme.IsDark ? AppTheme.Dark : AppTheme.Light;
        }

        foreach (var view in _views)
        {
            view.Theme = theme;
            view.Invalidate();
        }

        var p = _palette;
        BackgroundColor = p.Ground;
        _titleWide.Apply(p);
        _dialog.Apply(p);
        _queryBox.Apply(p);
        _replacementBox.Apply(p);
        _titleNarrow.Apply(p);
        if (_windowTitleBar is not null)
        {
            _windowTitleBar.BackgroundColor = p.Ground;
            _windowTitleBar.ForegroundColor = p.Text;
        }

        _rail.Apply(p);
        _explorer.Apply(p);
        _tabStrip.Apply(p);
        _breadcrumb.Apply(p);
        _bottom.Apply(p);
        _terminalPanel.Apply(p);
        _status.Apply(p);
        _activity.Apply(p);
        _bell.Apply(p);
        _centre.Apply(p);
        _toast.Apply(p);
        _center.BackgroundColor = p.Surface;
        _editorArea.BackgroundColor = p.Surface;
        _empty.TextColor = p.Secondary;
        _findBar.BackgroundColor = p.Raised;
        foreach (var label in _findLabels)
        {
            label.TextColor = p.Text;
        }

        foreach (var button in _findButtons)
        {
            button.TextColor = p.Text;
            button.BackgroundColor = p.RaisedHigh;
            button.BorderColor = p.BorderStrong;
        }


        foreach (var line in (ReadOnlySpan<BoxView>)[_railLine, _statusLine, _titleLine])
        {
            line.Color = p.Border;
        }

        foreach (var split in (ReadOnlySpan<SplitPanel>)[_sideSplit, _chatSplit, _bottomSplit])
        {
            split.SetColors(p.Border, p.Accent);
        }

        foreach (var view in _sideViews.Values)
        {
            view.BackgroundColor = p.Surface;
            view.SetColors(p.Text, p.Secondary);
        }

        _pluginStatus.TextColor = p.Secondary;
        _conversations.ApplyTheme(theme);
        _conversations.ApplyPalette(p);
        _chatSplit.BackgroundColor = p.Surface;
        UpdateProblems();
        UpdateModel();
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

    private void ReplaceAll()
    {
        if (_shown?.Model is { } model)
        {
            int replaced = model.Find.ReplaceAll(_replacement.Text ?? "");
            _status.Notice.Text = string.Create(CultureInfo.InvariantCulture, $"Replaced {replaced}");
        }

        UpdateCount();
    }

    private void CloseFind()
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

    // On a caret move (a keystroke too): only the texts that changed are set; the breadcrumb's scopes come from the
    // language's last parse, the same list while the caret stays in one member.
    private void UpdateStatus()
    {
        if (_shown?.Model is not { } model || _shell.Tabs.Active is not { } tab)
        {
            Set(_status.Language, "");
            Set(_status.Position, "");
            Set(_status.Encoding, "");
            Set(_status.LineEnding, "");
            Set(_status.Paint, "");
            _breadcrumb.Show([]);
            _titleNarrow.SetNarrowTitle("no file", "");
            return;
        }

        Set(_status.Language, LanguageText(model, tab.FilePath));
        Set(_status.Position, ShellText.Position(model.CaretLine, model.CaretColumn));
        Set(_status.Indentation, ShellText.Indentation(model.Options));
        if (tab.Document is TextDocument document)
        {
            Set(_status.Encoding, ShellText.EncodingName(document.Encoding, document.HasBom));
            Set(_status.LineEnding, ShellText.LineEnding(document.LineEnding));
        }

        var scopes = model.LanguageSession?.ScopeAt(model.Snapshot, Math.Min(model.Selection.Caret, model.Snapshot.Length)) ?? [];
        _breadcrumb.Show(ShellText.Breadcrumb(tab.FilePath, _shell.Workspace?.Root, scopes));
        string place = (_shell.Workspace is { } workspace ? Path.GetFileName(workspace.Root) : tab.FilePath is { } file ? Path.GetDirectoryName(file) ?? "" : "")
            + (tab.IsDirty ? " · edited" : tab.FilePath is null ? " · new" : "");
        _titleNarrow.SetNarrowTitle(tab.Title, place.TrimStart(' ', '·'));
    }

    private static void Set(Label label, string text)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.Text = text;
        }
    }

    private void UpdatePaintTime()
    {
        if (_shown?.Model is { } model && _status.Paint.IsVisible)
        {
            Set(_status.Paint, string.Create(CultureInfo.InvariantCulture, $"paint {model.LastInputToPaintMilliseconds:F1} ms"));
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
