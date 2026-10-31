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
/// telemetry), the plug-ins' panels, and the status bar. Every colour comes from the theme's tokens
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
    private readonly PanelsView _panels;
    private readonly ContentView _panelsBox;
    private readonly ConversationsView _conversations;
    private readonly ContentView _chatBox;
    private readonly BoxView _chatLine;
    private readonly Grid _body;
    private readonly Grid _center;
    private readonly BoxView _railLine;
    private readonly BoxView _explorerLine;
    private readonly BoxView _panelsLine;
    private readonly BoxView _bottomLine;
    private readonly BoxView _statusLine;
    private readonly BoxView _titleLine;
    private readonly List<IDisposable> _platformHooks = [];
    private readonly List<IModelProvider> _watchedProviders = [];
    private ShellPalette _palette;
    private TitleBar? _windowTitleBar;
    private bool _bottomVisible = true;
    private bool _chatOpen = true;
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
        _host = new EditorHost(startup.Options, action => Dispatcher.Dispatch(action), new MauiEditorPresenter(this, () => shell?.Theme ?? EditorThemes.Get(EditorThemes.Dark)), startup.Settings.Read);
        shell = new ShellModel(_host, startup.Settings, startup.Recent, startup.Platform.FileDialogs, new MauiShellUi(this, ShowSettingsAsync));
        _shell = shell;
        _palette = new ShellPalette(_shell.Theme, _shell.Options);
        _editCommands = new ShellEditCommands(() => _shown?.Model);
        _quickOpen = new QuickOpen(_host, _shell.Options.QuickOpenRows) { CraftOptionsLimit = _shell.Options.QuickOpenFiles };
        _pluginStatus = new StatusItemsView(_host);
        _panels = new PanelsView(_host);
        _panelsBox = new ContentView { Content = _panels, WidthRequest = _shell.Options.PanelWidth };
        // The plug-ins' conversation panels (the AI plug-in's chat and agent run): the design's chat column when wide, a
        // sheet over the editor when narrow (PhoneChat.dc.html); craft.chat (Ctrl+Alt+I) shows or hides it, a plug-in's
        // IEditorHost.ShowPanel brings one forward.
        _conversations = new ConversationsView(_host, _shell.Options.FontFamily);
        _chatBox = new ContentView { Content = _conversations, WidthRequest = _shell.Options.ChatWidth };
        _conversations.PanelsChanged += (_, _) => ApplyLayout();
        _host.PanelShowRequested += OnPanelShowRequested;
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
            bar.ChipTapped += (_, _) => _ = ShowModelsAsync();
            bar.NarrowFilesTapped += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.ToggleTree);
            bar.NarrowUndoTapped += (_, _) => _shown?.Model?.OnKey(EditorKey.Z, KeyModifiers.Control);
            bar.NarrowCommandsTapped += (_, _) => _ = CommandPalette.ShowAsync(this, _host);
        }

        _titleWide.SearchChanged += (_, text) => ShowQuickOpen(text);
        _titleWide.SearchFocused += (_, _) => ShowQuickOpen(_titleWide.Search.Text ?? "");
        _titleWide.SearchSubmitted += (_, _) => _ = PickQuickOpenAsync(0);
        _popup.Closed += (_, _) => _lastQuery = "";

        _rail = new RailView(_host, _palette);
        _tree.Indent = _shell.Options.TreeIndent;
        _tree.FileActivated += (_, file) => _ = _shell.OpenFileAsync(file, fromTree: true);
        _explorer = new ExplorerView(_tree, _palette) { WidthRequest = _shell.Options.TreeWidth };
        _explorer.SetFolder(null);
        _tabStrip = new TabStripView(_shell.Tabs);
        _tabStrip.CloseRequested += (_, tab) => _ = _shell.CloseTabAsync(tab);
        _tabStrip.ListRequested += (_, _) => _ = PickTabAsync();
        _breadcrumb = new BreadcrumbView(_palette);
        _empty = _palette.Label("No file is open.\nCtrl+O opens a file, Ctrl+Shift+O a folder, Ctrl+P a file of the folder by name, Ctrl+N a new file; Ctrl+Shift+P lists every command.", _palette.Secondary);
        _empty.HorizontalTextAlignment = TextAlignment.Center;
        _empty.LineBreakMode = LineBreakMode.WordWrap;
        _empty.Margin = new Thickness(24);

        _query = _palette.Input("Find", 30);
        _query.WidthRequest = 220;
        _query.TextChanged += OnQueryChanged;
        _query.Completed += (_, _) => FindNext();
        _replacement = _palette.Input("Replace", 30);
        _replacement.WidthRequest = 220;
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
        _replaceRow = new HorizontalStackLayout { Spacing = 6, Children = { _replacement, replaceOne, replaceAll } };
        _findBar = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            Padding = new Thickness(12, 4),
            IsVisible = false,
            Children =
            {
                _query,
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
        _explorerLine = _palette.Line(vertical: true);
        _panelsLine = _palette.Line(vertical: true);
        _chatLine = _palette.Line(vertical: true);
        _bottomLine = _palette.Line();
        _statusLine = _palette.Line();
        _titleLine = _palette.Line();

        _center = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
            ],
        };
        _center.Add(_tabStrip, 0, 0);
        _center.Add(_breadcrumb, 0, 1);
        _center.Add(_findBar, 0, 2);
        _center.Add(_editorArea, 0, 3);
        _center.Add(_bottomLine, 0, 4);
        _center.Add(_bottom, 0, 5);

        _body = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
            ],
        };
        _body.Add(_rail, 0, 0);
        _body.Add(_railLine, 1, 0);
        _body.Add(_explorer, 2, 0);
        _body.Add(_explorerLine, 3, 0);
        _body.Add(_center, 4, 0);
        _body.Add(_panelsLine, 5, 0);
        _body.Add(_panelsBox, 6, 0);
        _body.Add(_chatLine, 7, 0);
        _body.Add(_chatBox, 8, 0);

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
        _host.SelectionRequested -= OnSelectionRequested;
        _conversations.Dispose();
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
                _platformHooks.Add(_startup.Platform.RouteKeys(_window, _host));
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
        EditorCommands.Register(new EditorCommand(ShellCommandIds.CommandPalette, "View: Show all commands", (_, _) => CommandPalette.ShowAsync(this, _host)));
        EditorCommands.Register(new EditorCommand(ShellCommandIds.Plugins, "Plug-ins: Show the installed plug-ins", (_, _) => ShowPluginsAsync()));
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
        }
        else
        {
            _chatOpen = !_chatOpen;
        }

        ApplyLayout();
        if (_chatBox.IsVisible)
        {
            _conversations.FocusInput();
        }
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
            _chatOpen = true;
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

    private async Task ShowSettingsAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            await Navigation.PushModalAsync(new SettingsPage(_shell));
        }
    }

    private async Task ShowPluginsAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            await Navigation.PushModalAsync(new PluginsPage(_shell, () => PluginList.Rows(CraftPlugins.Installed, _host.Plugins)));
        }
    }

    private async Task ShowModelsAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            await Navigation.PushModalAsync(new ModelsPage(_shell, ShowSettingsAsync));
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
            string why = text.TrimStart().StartsWith('>') ? "No command matches."
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
        if (item.CommandId is { } command)
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
        string? text = await DisplayPromptAsync("Open", "A file's name, or '>' and a command", "Search", "Cancel");
        if (text is null)
        {
            return;
        }

        var items = _quickOpen.Query(text);
        if (items.Count == 0)
        {
            await DisplayAlertAsync("Open", "Nothing matches.", "OK");
            return;
        }

        var titles = items.Select(i => i.Detail is null ? i.Text : $"{i.Text}  ({i.Detail})").ToArray();
        string? picked = await DisplayActionSheetAsync("Open", "Cancel", null, titles);
        int index = picked is null ? -1 : Array.IndexOf(titles, picked);
        if (index >= 0)
        {
            _lastQuery = text;
            await PickQuickOpenAsync(index);
        }
    }

    private void OnViewRequested(string view)
    {
        switch (view)
        {
            case ShellViewNames.QuickOpen when _shell.Layout.IsNarrow:
                _ = QuickOpenNarrowAsync();
                break;
            case ShellViewNames.QuickOpen:
                _titleWide.Search.Focus();
                ShowQuickOpen(_titleWide.Search.Text ?? "");
                break;
            case ShellViewNames.TogglePanel:
                _bottomVisible = !_bottomVisible;
                ApplyLayout();
                break;
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
            case ShellViewNames.Models:
                _ = ShowModelsAsync();
                break;
        }
    }

    private void OnTerminalView(TerminalViewRequest request)
    {
        if (request == TerminalViewRequest.Toggle && _bottomVisible && _bottom.Tab == BottomTab.Terminal)
        {
            _bottomVisible = false;
            ApplyLayout();
            return;
        }

        ShowBottom(BottomTab.Terminal);
        _host.Dispatch("shell: focus the terminal", _terminalPanel.FocusTerminal);
    }

    private void ShowBottom(BottomTab tab)
    {
        _bottomVisible = true;
        _bottom.Select(tab);
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
        _titleWide.SetModel(chip, status?.ChatModel is not null, tooltip);
        _status.Model.Text = ShellText.ModelUsage(status);
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
        _panelsBox.WidthRequest = options.PanelWidth;
        _chatBox.WidthRequest = options.ChatWidth;
        _tree.Indent = options.TreeIndent;
        ApplyTheme();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var layout = _shell.Layout;
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
        _rail.Active = layout.TreeVisible ? "craft.explorer" : null;
        _explorer.IsVisible = layout.TreeVisible;
        _explorerLine.IsVisible = layout.TreeVisible && !narrow;
        _center.IsVisible = layout.EditorVisible;
        _panelsBox.IsVisible = layout.PanelsVisible && _panels.IsVisible;
        _panelsLine.IsVisible = _panelsBox.IsVisible;
        _chatBox.IsVisible = _conversations.HasPanels && (narrow ? _chatSheetOpen : _chatOpen);
        _chatLine.IsVisible = _chatBox.IsVisible && !narrow;
        if (narrow)
        {
            // The sheet: over the body's lower part, the width of the window.
            Grid.SetColumn(_chatBox, 0);
            Grid.SetColumnSpan(_chatBox, 9);
            _chatBox.WidthRequest = -1;
            _chatBox.VerticalOptions = LayoutOptions.End;
            _chatBox.HeightRequest = Math.Max(240, Height * 0.62);
        }
        else
        {
            Grid.SetColumn(_chatBox, 8);
            Grid.SetColumnSpan(_chatBox, 1);
            _chatBox.WidthRequest = _shell.Options.ChatWidth;
            _chatBox.VerticalOptions = LayoutOptions.Fill;
            _chatBox.HeightRequest = -1;
        }
        _bottom.IsVisible = _bottomLine.IsVisible = _bottomVisible && !narrow;
        _breadcrumb.IsVisible = !narrow;
        _body.ColumnDefinitions[2].Width = narrow && layout.TreeVisible ? GridLength.Star : GridLength.Auto;
        _body.ColumnDefinitions[4].Width = layout.EditorVisible ? GridLength.Star : new GridLength(0);
        _explorer.WidthRequest = narrow ? -1 : _shell.Options.TreeWidth;
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

        foreach (var entry in (ReadOnlySpan<Entry>)[_query, _replacement])
        {
            entry.TextColor = p.Text;
            entry.BackgroundColor = p.Raised;
            entry.PlaceholderColor = p.Secondary;
        }

        foreach (var line in (ReadOnlySpan<BoxView>)[_railLine, _explorerLine, _panelsLine, _chatLine, _bottomLine, _statusLine, _titleLine])
        {
            line.Color = p.Border;
        }

        _pluginStatus.TextColor = p.Secondary;
        _panels.TextColor = p.Text;
        _panelsBox.BackgroundColor = p.Surface;
        _conversations.ApplyTheme(theme);
        _chatBox.BackgroundColor = p.Surface;
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
