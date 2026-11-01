// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Abstractions;
using Craft.Core.Diagnostics;
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
public sealed record ShellStartup(CraftOptions Options, CraftSettings Settings, IReadOnlyList<PluginLoadResult> Plugins, RecentList Recent, IShellPlatform Platform, IReadOnlyList<string> Problems)
{
    /// <summary>The run's crash watch (the owner's note 21: its marker, its trail), started before the window; null when none.</summary>
    public CrashWatch? Crashes { get; init; }
}

/// <summary>
/// The app's shell (phase D: the approved design, docs/design/Main.dc.html, on E4's <see cref="ShellModel"/> and K's
/// contracts): the title bar (the logo, the menus of registered commands, the search box for quick open, the model chip),
/// the activity rail, the explorer with the semantic layer's card, the editor groups (the X restart: each its tabs and editor, one focused; a document in two groups shares one text and session), the breadcrumb, the editor (one
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
    private const double QuickOpenWidth = 600;
    private const string ContextMenuCommand = "craft.contextMenu";
    private readonly ShellStartup _startup;
    private readonly string? _initialPath;
    private readonly EditorHost _host;
    private readonly ShellModel _shell;
    private readonly ShellEditCommands _editCommands;
    private readonly QuickOpen _quickOpen;
    private readonly FolderTreeView _tree = new();
    private readonly ExplorerView _explorer;
    private readonly BreadcrumbView _breadcrumb;
    private readonly SymbolPreview _symbolPreview = new();
    private RenameOverlay? _rename;
    private readonly EditorGroupsView _groupsView;
    private readonly List<EditorGroupPane> _panes = [];
    private readonly List<EditorView> _views = [];
    private readonly string _emptyText;
    private PeekView? _peek;
    private EditorGroupPane? _peekPane;
    private EditorView? _peekSource;
    private readonly FindOverlay _find;
    private readonly QuickOpenOverlay _overlay;
    private TaskCompletionSource<int>? _quickPickAnswer;
    private readonly UndoBarView _undoBar;
    private readonly RecoveryCardView _recoveryCard;
    private readonly Grid _editorStack = new();
    private readonly TitleBarView _titleWide;
    private readonly TitleBarView _titleNarrow;
    private readonly ContentView _titleRow = new();
    private readonly PopupLayer _popup = new();
    private readonly CraftDialog _dialog;
    private readonly IEditorKeyHandler _keys;
    private readonly ProblemReport _report;
    private readonly MenuModel _menu;
    private readonly MenuView _menuView;
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
    private readonly LoaderView _loader;
    private readonly WorkspaceNotices _workspaceNotices;
    private int _workspaceWork;
    private readonly List<IDisposable> _platformHooks = [];
    private readonly List<IModelProvider> _watchedProviders = [];
    private ShellPalette _palette;
    private TitleBar? _windowTitleBar;
    private bool _chatSheetOpen;
    private bool _keysRouted;
    private bool _closeGuarded;
    private bool _disposed;
    private Craft.Abstractions.FileSettings? _statusSettings;
    private Window? _window;
    private EditorView? _shown;
    private bool _started;
    private CraftOptions? _appliedOptions;
    private ITheme? _appliedTheme;

    /// <summary>The shell, opening <paramref name="path"/> when given (a folder as the workspace, a file in a tab; else an untitled document).</summary>
    public ShellPage(ShellStartup startup, string? path)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _initialPath = path;
        ShellModel? shell = null;
        CodeFontPicker.DrawnFonts = startup.Platform.DrawnFonts;
        // B1: the platform's kept line layouts (Win2D on Windows) unless CRAFT_PAINT_LINE_LAYOUTS is off.
        MauiEditorCanvas.TextLayouts = startup.Options.PaintLineLayouts ? startup.Platform.TextLayouts : null;
        // The owner's note 10: every question, message and choice is Craft's dialog over the window (themed in ApplyTheme).
        _dialog = new CraftDialog(new ShellPalette(EditorThemes.Get(EditorThemes.Dark), startup.Options), action => Dispatcher.DispatchLabelled("dialog: next", action));
        _host = new EditorHost(startup.Options, action => Dispatcher.Dispatch(action), new MauiEditorPresenter(this, _dialog, () => shell?.Theme ?? EditorThemes.Get(EditorThemes.Dark)), startup.Settings.Read);
        UiDispatch.Labels = _host.Labels;                    // the views' dispatches run labelled (the watchdog names them)
        // The owner's note 19: Ctrl+C that no control took copies the bottom panel's chosen rows, before the key bindings.
        // Phase X: the open menu (a title-bar menu or a context menu) takes its keys first, then the dialogs, then the bindings.
        _menu = new MenuModel(_host);
        // The X restart: Escape closes an open peek first, from the editor it came from too.
        _keys = new PeekEscapeKeys(_menu.Before(new PanelCopyKeys(_dialog.Queue.Before(_host), () => _bottom?.CopyChosen() ?? false)), () => ClosePeek());
        // The owner's note 21: the keys' names, the commands, the UI work and the log lines go to the run's trail.
        _host.Trail = startup.Crashes?.Trail;
        CallbackGuard.Options = startup.Options;
        CallbackGuard.Failed += OnCallbackFailed;
        _dialog.FallbackFocus = () => _shown;
        var shellUi = new MauiShellUi(_dialog) { QuickPick = (request, token) => QuickPickAsync(request, token) };
        shell = new ShellModel(_host, startup.Settings, startup.Recent, startup.Platform.FileDialogs, shellUi, null, startup.Platform.ShellFiles, new MauiEditorClipboard());
        _shell = shell;
        // B1: low memory drops the editors' kept line layouts too (the next frame lays out only what it shows).
        shell.MemoryPressure += (_, _) => Dispatcher.DispatchLabelled("editor: drop the kept line layouts (low memory)", () =>
        {
            foreach (var view in _views)
            {
                view.ReleaseDrawingMemory();
            }
        });
        _shell.EditorOf = tab => (tab.View as EditorView)?.Model;     // format on save goes through the tab's editor
        _palette = new ShellPalette(_shell.Theme, _shell.Options);
        ShellMotion.Milliseconds = Motion.Duration(_shell.Options);
        _menuView = new MenuView(_menu, _palette) { SubmenuDelayMilliseconds = _shell.Options.MenuSubmenuDelayMilliseconds };
        // X part 7: the menus' targets described for their rows' "when"; a submenu's card beside its row; typing jumps.
        _menu.Describe = _shell.DescribeMenuTarget;
        _menu.TypeAheadMilliseconds = _shell.Options.MenuTypeAheadMilliseconds;
        _menuView.SubmenuShown += (_, e) =>
        {
            _popup.ShowBeside(e.Card, e.OffsetY, MenuWidth);
            _ = ShellMotion.ShowAsync(e.Card, -4);
        };
        _menuView.SubmenuHidden += (_, _) => _popup.HideBeside();
        _menu.Changed += (_, _) =>
        {
            if (!_menu.IsOpen && ReferenceEquals(_popup.Shown, _menuView))
            {
                _popup.Hide();
            }
        };
        _editCommands = new ShellEditCommands(() => _shown?.Model, _shell);
        _quickOpen = new QuickOpen(_host, _shell.Options.QuickOpenRows * 10)
        {
            CraftOptionsLimit = _shell.Options.QuickOpenFiles,
            BackgroundFiles = _shell.Options.QuickOpenBackgroundFiles,
            RecentLimit = _shell.Options.QuickOpenRecent,
        };
        // Piece 3b: '@' lists the shown editor's symbols, asked once per version off the UI thread.
        _quickOpen.SymbolSource = token => _shown?.Model is { } shownModel ? shownModel.DocumentSymbolsAsync(token) : Task.FromResult<SymbolIndex?>(null);
        foreach (var entry in startup.Recent.Entries.Where(e => e.Kind == Craft.Core.Workspaces.RecentKind.File).Reverse())
        {
            _quickOpen.NoteFile(entry.Path);                     // the recent list's files come first in quick open
        }

        _pluginStatus = new StatusItemsView(_host);
        // The plug-ins' conversation panels (the AI plug-in's chat and agent run): the design's chat column when wide (its
        // splitter and close button, phase U2), a sheet over the editor when narrow (PhoneChat.dc.html); craft.chat
        // (Ctrl+Alt+I) shows or hides it, a plug-in's IEditorHost.ShowPanel brings one forward.
        _conversations = new ConversationsView(_host, _shell.Options.FontFamily, _shell.UndoBar);
        _conversations.WindowKeys = _keys;
        _conversations.MenuRequested += (_, e) => OpenConversationMenu(e);
        _conversations.ExpandRequested += (_, _) => _ = ToggleChatExpandedAsync();
        _conversations.SymbolsSource = () => ConversationComposer.SymbolsOf(_shown?.Model, () => _conversations.RefreshMentions());
        _conversations.LeaveRequested += (_, _) => _shown?.Focus();
        _chatSplit.Panel = _conversations;
        _conversations.PanelsChanged += (_, _) => ApplyLayout();
        _conversations.CloseRequested += (_, _) => CloseChat();
        _conversations.ModelChip.Tapped += (_, _) => _ = PickModelAsync();
        _conversations.ShownChanged += (_, _) => UpdateModel();                    // the chip shows the shown panel's role (note 25)
        _conversations.DroppedPaths = e => _startup.Platform.DroppedPathsAsync(e);  // X part 5: files dropped on the chat
        _conversations.Notice += (_, text) => _status!.Notice.Text = text;
        _host.PanelShowRequested += OnPanelShowRequested;
        _host.PanelsChanged += OnHostPanelsChanged;
        _host.PublishedDiagnosticsChanged += OnPublishedDiagnosticsChanged;     // W: closed files' problems from plug-ins
        _host.PendingChordChanged += OnPendingChordChanged;
        _host.SelectionRequested += OnSelectionRequested;
        // A plug-in's IEditorHost.OpenExternalAsync (the learning plug-in's folder): the system's own launcher, any platform.
        _host.OpenExternal = path => Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(path));
        RegisterAppCommands();
        if (startup.Platform.CrashRecords is { } crashRecords)
        {
            CrashRecordSources.Register(crashRecords);
        }

        _report = new ProblemReport(_host, startup.Crashes, TicketPlugin.Of(startup.Plugins), MauiEditorClipboard.Shared,
            folder => Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(folder)),
            path => _shell.OpenFileAsync(path));

        _titleWide = new TitleBarView(_palette);
        _titleNarrow = new TitleBarView(_palette);
        _titleNarrow.SetNarrow(true);
        foreach (var bar in (ReadOnlySpan<TitleBarView>)[_titleWide, _titleNarrow])
        {
            bar.MenuRequested += (_, request) => ShowMenu(request.Menu, request.Anchor);
            bar.NarrowFilesTapped += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.ToggleTree);
            bar.NarrowUndoTapped += (_, _) => _shown?.Model?.OnKey(EditorKey.Z, KeyModifiers.Control);
            bar.NarrowCommandsTapped += (_, _) => ShowOverlay(">");
        }

        // Phase X: the title bar's search box opens the one overlay (quick open and the palette) under it, with what was typed.
        _titleWide.SearchFocused += (_, _) => ShowOverlay(TakeSearchText());
        _titleWide.SearchChanged += (_, text) =>
        {
            if (text.Length > 0 && !_popup.IsOpen)
            {
                ShowOverlay(TakeSearchText());
            }
        };
        _overlay = new QuickOpenOverlay(_quickOpen, _palette) { VisibleRows = _shell.Options.QuickOpenRows };
        _overlay.Picked += (_, item) =>
        {
            if (_quickOpen.Picking is not null)
            {
                EndQuickPick(item.PickIndex);
                return;
            }

            _ = PickAsync(item);
        };
        _overlay.EmptyStateFor = text => ShellEmptyStates.Search(_host, text, _shell.Workspace is not null, _shown is not null, _quickOpen.IsIndexing);
        _overlay.EmptyActionChosen += (_, action) =>
        {
            _popup.Hide();
            RunEmptyAction(action);
        };
        _overlay.Dismissed += (_, _) =>
        {
            if (_quickOpen.Picking is not null)
            {
                EndQuickPick(-1);
                return;
            }

            _popup.Hide();
            _symbolPreview.Restore();                            // Escape after '@' previews: back where the editor was
            _quickOpen.EndBrowse();
            _shown?.Focus();
        };
        _overlay.SelectionMoved += (_, item) =>
        {
            if (_quickOpen.Picking is { Moved: { } moved })
            {
                moved(item.PickIndex);                           // X part 9: the theme picker previews the row's theme
                return;
            }

            _symbolPreview.Show(item);
        };
        _popup.Closed += (_, _) =>
        {
            _menu.Close();
            EndQuickPick(-1);                                    // a click outside an open quick pick cancels it
        };

        _rail = new RailView(_host, _palette) { Badge = _shell.RailBadge };
        // X part 8: the rail's counts, the tree's file states and the status bar's branch follow the plug-ins' source control.
        _shell.RailBadgesChanged += (_, _) => _rail.Rebuild();
        _tree.StateOf = path => _shell.FileState(path) is { } file ? (file.Letter, StateColor(file.State)) : null;
        _shell.SourceControlChanged += (_, _) =>
        {
            _tree.RefreshStates();
            _status!.Branch.Text = _shell.BranchText;            // raised after the page is made
        };
        _tree.Indent = _shell.Options.TreeIndent;
        // X part 9: a single click previews the file (an italic tab the next preview replaces), a double click keeps it.
        _tree.FileActivated += (_, file) => _ = _shell.OpenPreviewAsync(file);
        _tree.FileKept += (_, file) => _ = _shell.OpenKeptAsync(file);
        _tree.DragsFiles = true;                                 // phase X: a file dragged from the tree onto the editor opens
        // The X restart: a row dragged onto a folder moves there (Ctrl copies), with Undo.
        _tree.MovesEntries = startup.Options.TreeDragMove;
        _tree.CopiesOnDrop = startup.Platform.DropCopies;
        _tree.MoveRequested += (_, e) => _ = MoveEntryAsync(e.Source, e.TargetFolder, e.Copy);
        _explorer = new ExplorerView(_tree, _palette);
        _explorer.SetFolder(null);
        _explorer.Empty.ActionChosen += (_, action) => RunEmptyAction(action);
        _tree.ContextRequested += (_, e) => OpenContextMenu(EditorContextMenus.Tree,
            e.Item is { } item ? new ContextMenuTarget(EditorContextMenus.Tree, item.Path, item.IsFolder) : new ContextMenuTarget(EditorContextMenus.Tree), e.Where);
        _sideSplit.Panel = _explorer;
        _breadcrumb = new BreadcrumbView(_palette);
        _breadcrumb.PartClicked += (_, part) => _ = OnBreadcrumbAsync(part);
        _shell.RenameRequested += (_, request) => ShowRename(request);
        _emptyText = "No file is open.\nCtrl+O opens a file, Ctrl+K Ctrl+O a folder, Ctrl+P a file of the folder by name, Ctrl+N a new file; Ctrl+Shift+P lists every command.";
        // The X restart: the editor area's groups, each a pane of its own tabs and editor (the focused one has the find
        // overlay and the breadcrumb).
        _groupsView = new EditorGroupsView(_shell.Groups, PaneOf, _palette) { MinimumSize = _shell.Options.PanelMinimumSize, Pointer = startup.Platform.PointerCursor };

        // Phase X: find and replace as an overlay in the editor's top-right corner, the undo bar at its bottom.
        _find = new FindOverlay(_palette);
        _find.Closed += (_, _) =>
        {
            _shown?.Invalidate();
            _shown?.Focus();
        };
        _find.Notice += (_, text) => _status!.Notice.Text = text;      // raised after the page is made
        _undoBar = new UndoBarView(_shell.UndoBar, _palette);
        _editorStack.Add(_groupsView);
        _editorStack.Add(_undoBar);
        // The recovery prompt: the files an unclean end left unsaved, offered at the top of the editor area (not modal).
        _recoveryCard = new RecoveryCardView(_shell, _palette);
        _editorStack.Add(_recoveryCard);
        _bottom = new BottomPanelView(_host, _palette, OnProblemChosen) { CodeActionRequested = OnCodeActionRequested, WindowKeys = _keys };
        // The owner's note 26: Ctrl+F in Output searches its text; its right-click opens the read-only text's menu.
        _bottom.FindRequested += (_, e) => ShowFind(false, e.InitialQuery, e.Model);
        _bottom.ContextRequested += (_, e) => OpenContextMenu(EditorContextMenus.ReadOnlyText, new ContextMenuTarget(EditorContextMenus.ReadOnlyText, null, false, e.Model), e.Where);
        _bottom.CollapseRequested += (_, _) => _ = _shell.Panels.SetOpenAsync(ShellPanel.Bottom, false);
        _bottom.SetHierarchy(_shell.Hierarchy);              // X part 9: the call and type hierarchies' tab
        // X part 9: a file:line link clicked in Output opens the file there.
        _bottom.LinkActivated += (_, e) => _ = _shell.OpenLinkAsync(e.Path, e.Line, e.Column);
        _bottomSplit.Panel = _bottom;

        // Each panel's splitter: a drag kept within what the panel and the editor share, written to the workspace's
        // settings when it ends (not on every move).
        Wire(_sideSplit, ShellPanel.Explorer, () => Width - _rail.Width - (_chatSplit.IsShown ? _chatSplit.Width : 0));
        Wire(_chatSplit, ShellPanel.Chat, () => Width - _rail.Width - (_sideSplit.IsShown ? _sideSplit.Width : 0));
        Wire(_bottomSplit, ShellPanel.Bottom, () => (_center?.Height ?? 0) - 36 - _breadcrumb.Height);

        // Phase T: the terminals in the Terminal tab, on the platform's host (ConPTY on Windows) when it has one.
        if (startup.Platform.TerminalHost is { } terminalHost)
        {
            TerminalHosts.Register(terminalHost);
        }

        // B9: the platform's profiles (Windows: the registry's WSL distributions and Git for Windows) over the library's.
        if (startup.Platform.TerminalProfiles is { } profileSource)
        {
            TerminalProfiles.Register(profileSource);
        }

        _terminals = new TerminalManager(_host, () => _shell.Options, MauiEditorClipboard.Shared)
        {
            Ui = new MauiShellUi(_dialog),
            // "Set default profile…": per workspace when a folder is open, else the user's.
            StoreDefaultProfile = name => _shell.ApplySetting(_shell.Workspace is not null ? SettingLayer.Workspace : SettingLayer.User, "CRAFT_TERMINAL_DEFAULT_PROFILE", name),
        };
        _host.Terminals = _terminals;
        _terminalPanel = new TerminalPanelView(_terminals, _palette, _host);
        _terminalPanel.MenuRequested += (_, e) => OpenContextMenu(e.Menu, e.Target, e.Where);
        // X part 9: Ctrl+click on a file:line link in a terminal opens it, resolved against the terminal's folder first.
        _terminalPanel.LinkActivated += (_, link) => _ = _shell.OpenLinkAsync(link.Path, link.TargetLine, link.TargetColumn, link.Folder);
        _bottom.TerminalContent = _terminalPanel;
        _bottom.Todo = _shell.Todo;                                      // X part 9: the TODO list's tab
        _terminals.ViewRequested += (_, request) => OnTerminalView(request);
        // Phase U: the running operations in the status bar, the bell, the notification centre and its toasts.
        _activity = new ActivityStatusView(_host.ActivityCenter, _palette);
        _bell = new BellView(_palette);
        _centre = new NotificationCenterView(_host.Notifications, _host.ActivityCenter, _palette) { ClearAll = () => _shell.ClearNotifications() };
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
        // B10: a workspace ready or failed is one notification; the status bar names it only while it loads or failed.
        _workspaceNotices = new WorkspaceNotices((source, title, text, severity, actions) => _host.Notifications.Post(source, title, text, severity, actions));
        var semanticTap = new TapGestureRecognizer();
        semanticTap.Tapped += (_, _) => ToggleCentre(true);
        _status.Semantic.GestureRecognizers.Add(semanticTap);
        // B10: the global loader, a thin line under the title bar while a workspace's work runs; a click opens the activities.
        _loader = new LoaderView(_host.ActivityCenter, _shell.Options) { ZIndex = 20 };
        _loader.Tapped += (_, _) => ToggleCentre(true);
        _host.ActivityCenter.Changed += OnActivitiesChanged;
        _status.ProblemsTapped += (_, _) => ShowBottom(BottomTab.Problems);
        _status.BranchTapped += (_, _) =>
        {
            if (EditorCommands.Find(SourceControlProviders.BranchCommand) is not null)
            {
                _ = _host.ExecuteCommandAsync(SourceControlProviders.BranchCommand);
            }
        };
        var goToLine = new TapGestureRecognizer();
        goToLine.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.GoToLine);
        _status.Position.GestureRecognizers.Add(goToLine);
        _palette.States(_status.Position, InteractionKind.Flat).Label = _status.Position;
        // X part 9, item 26: the indentation, encoding, line ending and language items each open their quick pick.
        foreach (var (label, command) in (ReadOnlySpan<(Label, string)>)[(_status.Indentation, LookCommands.ChangeIndentation), (_status.Encoding, LookCommands.ChangeEncoding),
            (_status.LineEnding, LookCommands.ChangeLineEnding), (_status.Language, LookCommands.ChangeLanguage)])
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(command);
            label.GestureRecognizers.Add(tap);
            _palette.States(label, InteractionKind.Flat).Label = label;
        }

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
        _center.Add(_editorStack, 0, 3);
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
        root.Add(_loader, 0, 2);
        root.Add(_statusLine, 0, 3);
        root.Add(_status, 0, 4);
        root.Add(_toast, 0, 2);
        root.Add(_centre, 0, 2);
        root.Add(_popup, 0, 0);
        Grid.SetRowSpan(_popup, 5);
        root.Add(_dialog, 0, 0);
        Grid.SetRowSpan(_dialog, 5);
        // Phase X: a file dragged from the tree, or files and a folder dropped from the system's file manager, anywhere on
        // the window open (the platform reads the outside drop's paths: IShellPlatform.DroppedPathsAsync).
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Copy;
        drop.Drop += OnDrop;
        root.GestureRecognizers.Add(drop);
        Content = root;

        _shell.Groups.TabsChanged += (_, _) => OnTabsChanged();
        _shell.Groups.ActiveChanged += (_, group) =>
        {
            ShowActive();
            if (ReferenceEquals(group, _shell.Groups.Focused) && _shell.Tabs.Active?.FilePath is { } opened)
            {
                _quickOpen.NoteFile(opened);                     // every file opened counts as recent in quick open
            }
        };
        _shell.Groups.TabChanged += (_, _) =>
        {
            UpdateStatus();
            UpdateTitle();
        };
        _shell.Groups.FocusChanged += (_, _) =>
        {
            ShowActive();
            _shown?.Focus();                                     // the keyboard follows the focused group (Ctrl+1, Ctrl+2)
        };
        _shell.Groups.GroupsChanged += (_, _) => OnGroupsChanged();
        _groupsView.Resized += (_, _) => _shown?.Focus();
        _shell.WorkspaceChanged += (_, _) => OnWorkspaceChanged();
        _shell.OptionsChanged += (_, _) => OnOptionsChanged();
        _shell.Run.ProblemsChanged += (_, _) => UpdateProblems();      // X part 6: a run's errors in Problems
        _shell.Tests.ProblemsChanged += (_, _) => UpdateProblems();    // X part 9: the failed tests in Problems
        // A plug-in's setting changes no option: the model chip and the model item read the AI plug-in's settings again.
        _shell.SettingApplied += (_, variable) =>
        {
            if (variable.StartsWith("CRAFT_PLUGIN_", StringComparison.OrdinalIgnoreCase))
            {
                UpdateModel();
            }
        };
        _shell.ThemeChanged += (_, _) => ApplyTheme();
        // X part 9, item 25: Zen mode and full screen lay the window out again; the platform's full screen, when it has one.
        _shell.FullScreenSupported = startup.Platform.FullScreen is not null;
        _shell.WindowModeChanged += (_, _) =>
        {
            ApplyLayout();
            if (_window is { } window && !(startup.Platform.FullScreen?.Set(window, _shell.IsFullScreen) ?? false) && _shell.IsFullScreen)
            {
                _status!.Notice.Text = "The window could not go full screen yet.";
            }
        };
        // CRAFT_THEME "system": the system's light or dark, followed as it changes.
        if (Application.Current is { } application)
        {
            _shell.SetSystemTheme(application.PlatformAppTheme != AppTheme.Light);
            application.RequestedThemeChanged += (_, _) => UiDispatch.Run("shell: the system's theme changed", () => _shell.SetSystemTheme(application.PlatformAppTheme != AppTheme.Light));
        }
        _shell.FileSettingsChanged += OnFileSettingsChanged;
        _shell.ViewRequested += (_, view) => OnViewRequested(view);
        _shell.RevealRequested += (_, path) => _tree.ScrollTo(path);
        // X part 7: names typed in place in the tree (new file, new folder, rename), checked as they are typed.
        _shell.TreeEditChanged += (_, _) => _tree.Edit = _shell.TreeEdit;
        _tree.EditTextChanged += (_, text) => _tree.EditProblem = _shell.TreeEditProblem(text);
        _tree.EditCommitted += (_, text) => _ = _shell.CommitTreeEditAsync(text);
        _tree.EditCancelled += (_, _) =>
        {
            _shell.CancelTreeEdit();
            _shown?.Focus();
        };
        _shell.Recent.Changed += OnRecentChanged;
        _host.KeyBindingsChanged += OnKeyBindingsChanged;
        _shell.Layout.Changed += (_, _) => ApplyLayout();
        _shell.Panels.Changed += (_, _) => ApplyLayout();
        _shell.Pages.Changed += (_, _) => ShowActive();
        _shell.Pages.Closed += (_, page) =>
        {
            (page.View as IDisposable)?.Dispose();
            page.View = null;
        };
        EditorMenus.Changed += OnMenusChanged;
        SizeChanged += (_, _) => UiDispatch.Run("shell: the window resized", () => _shell.Layout.UpdateWidth(Width));
        ShowActive();
        ApplyTheme();
        _appliedOptions = _shell.Options;
        _appliedTheme = _shell.Theme;
        ApplyLayout();
        UpdateExplorerEmpty();
        ApplyTooltips();
    }

    /// <summary>The window the page is in: its title shows the file, the dirty marker and the folder; with <c>CRAFT_TITLE_BAR=window</c> the title bar's row goes into its caption.</summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        if (_shell.Options.TitleBar == "window")
        {
            _windowTitleBar = new TitleBar { Content = _titleWide, HeightRequest = 40, BackgroundColor = _palette.Ground, ForegroundColor = _palette.Text };
            _windowTitleBar.Loaded += (_, _) => KeepTitleBarDraggable();
        }

        UpdateTitle();
        ApplyLayout();

        // CRAFT_WINDOW_STATE: maximized by default, once the platform window exists (its handler set).
        if (_shell.Options.WindowState == "maximized" && _startup.Platform.FullScreen is { } screen && !screen.Maximize(window))
        {
            window.HandlerChanged += OnWindowHandler;
        }
    }

    // MAUI's TitleBar marks its whole Content part click-through, and the title row fills it, so nothing was left to drag
    // the window by (the owner's run, 2026-11-01). Only the menus and the search box take clicks now; the platform reads
    // the list again on any property change of the bar, so one is made (its AutomationId, which nothing else reads).
    private void KeepTitleBarDraggable()
    {
        if (_windowTitleBar is not { } bar)
        {
            return;
        }

        var through = bar.PassthroughElements;
        through.Clear();
        foreach (var part in _titleWide.InteractiveParts)
        {
            through.Add(part);
        }

        bar.AutomationId = bar.AutomationId == "craft.titlebar" ? "craft.titlebar.parts" : "craft.titlebar";
    }

    private void OnWindowHandler(object? sender, EventArgs e)
    {
        if (sender is Window window && window.Handler is not null && (_startup.Platform.FullScreen?.Maximize(window) ?? true))
        {
            window.HandlerChanged -= OnWindowHandler;
        }
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
        CallbackGuard.Failed -= OnCallbackFailed;
        _report.Dispose();
        if (_startup.Platform.CrashRecords is { } crashRecords)
        {
            CrashRecordSources.Unregister(crashRecords.Name);
        }
        _noticeTimer?.Stop();
        _host.Notifications.Changed -= OnNotificationsChanged;
        _host.ActivityCenter.Changed -= OnActivitiesChanged;
        _host.Notifications.Toast -= OnToast;
        _activity.Dispose();
        _centre.Dispose();
        EditorMenus.Changed -= OnMenusChanged;
        _shell.Recent.Changed -= OnRecentChanged;
        _host.KeyBindingsChanged -= OnKeyBindingsChanged;
        _host.PanelShowRequested -= OnPanelShowRequested;
        _host.PanelsChanged -= OnHostPanelsChanged;
        _host.PublishedDiagnosticsChanged -= OnPublishedDiagnosticsChanged;
        _host.PendingChordChanged -= OnPendingChordChanged;
        _host.SelectionRequested -= OnSelectionRequested;
        _conversationCommands?.Dispose();
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
        _host.Trail = null;
        _startup.Crashes?.EndCleanly();                              // the clean exit: the run's marker and trail go
    }

    // A platform callback threw and was caught (the owner's note 20): said in Output, the app goes on.
    private void OnCallbackFailed(object? sender, CallbackFailure failure)
    {
        var line = $"A platform callback failed and was caught ({failure.Where}): {failure.Error.GetType().Name}: {failure.Error.Message}"
            + (failure.ReportPath is { } path ? " Report: " + path : "");
        _host.Dispatch("shell: a caught callback failure", () =>
        {
            _host.Log(EditorLogLevel.Error, line, failure.Error);
            // The owner (2026-11-01): a failure never ends the app; it is shown in Craft's dialog with Copy report, once per
            // place and exception type (the guard writes a report only for the first of each), and the app goes on.
            if (failure.ReportPath is { } report)
            {
                _ = ShowFailureAsync(failure, report);
            }
        });
    }

    private async Task ShowFailureAsync(CallbackFailure failure, string report)
    {
        try
        {
            var question = new EditorQuestion(
                "Something failed; Craft kept running",
                $"{failure.Error.GetType().Name}: {failure.Error.Message}",
                ["Copy report", "Continue"])
            {
                Details = [$"Where: {failure.Where}", $"Report: {report}"],
                Dismissed = 1,
            };
            if (await _dialog.AskAsync(question) == 0)
            {
                string text;
                try
                {
                    text = await File.ReadAllTextAsync(report);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    text = CrashReports.Format(failure.Error, failure.Where);
                }

                await MauiEditorClipboard.Shared.SetTextAsync(text);
                _status.Notice.Text = "The report is on the clipboard.";
            }
        }
#pragma warning disable CA1031 // Showing a failure never fails in its turn: it stays in Output.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, "The failure's dialog could not show.", error);
        }
    }

    /// <summary>Ctrl+C that reached the window with no control taking it (no editor): the bottom panel's chosen rows first.</summary>
    private sealed class PanelCopyKeys(IEditorKeyHandler inner, Func<bool> copy) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
            (editor is null && key == EditorKey.C && modifiers == KeyModifiers.Control && copy()) || inner.HandleKey(key, modifiers, editor);
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
        // The owner's start-up stalls were "unlabelled UI work": this is a window event's handler, so each part of it
        // runs under its own label (the watchdog names it), the awaits between them leaving the UI thread free.
        using (UiDispatch.Enter("shell: hook the platform"))
        {
            HookPlatform();
        }

        using (UiDispatch.Enter("shell: the plug-ins' load results"))
        {
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
        }

        // The plug-ins start first, so they see the first document open (the sample's word count counts it); a failure
        // is a notification with a toast (Craft's own card), not the platform's message box. Each activation is labelled
        // by the host (its name), so a slow one is named.
        using (UiDispatch.Enter("shell: start the plug-ins"))
        {
            foreach (var activation in _host.RefreshPlugins())
            {
                if (activation.Error is not null)
                {
                    _status.Notice.Text = $"Plug-in {activation.Name} did not start";
                    _host.Notify($"Plug-in {activation.Name} did not start", activation.Error, NotificationSeverity.Error);
                }
            }
        }

        // What the plug-ins registered: their rail items' commands, their menus, the model providers' status.
        using (UiDispatch.Enter("shell: the plug-ins' rail, menus and models"))
        {
            _rail.Rebuild();
            _titleWide.RebuildMenus();
            WatchModels();
        }

        // The recovery copies are kept from the start (every document opened from now on), off the UI thread's work.
        _shell.StartRecovery();
        if (_initialPath is not null && Directory.Exists(_initialPath))
        {
            // X part 9: the folder's last session (its groups, tabs, carets, scroll, folds, pins), read off the UI thread.
            if (await _shell.OpenFolderAsync(_initialPath))
            {
                await _shell.RestoreSessionAsync();
            }
        }
        else if (_initialPath is not null)
        {
            await _shell.OpenFileAsync(_initialPath);
        }

        // The recovery prompt: the copies an unclean end left for this folder (and the untitled ones), listed off the UI thread.
        try
        {
            await _shell.OfferRecoveryAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _host.Log(EditorLogLevel.Warning, $"The recovery copies could not be listed: {error.Message}", error);
        }

        using (UiDispatch.Enter("shell: the first document"))
        {
            if (_shell.Tabs.Tabs.Count == 0 && _shell.Workspace is null)
            {
                // Phase X: nothing asked to open and no folder: the welcome page (CRAFT_WELCOME_AT_START), else a new file.
                if (_shell.WelcomeAtStart)
                {
                    _shell.ShowWelcome();
                }
                else
                {
                    _shell.NewFile();
                }
            }

            if (_startup.Problems.Count > 0 || _shell.OptionsProblems.Count > 0)
            {
                var problems = _startup.Problems.Concat(_shell.OptionsProblems).Distinct(StringComparer.Ordinal);
                _host.Notify("A setting was not taken", string.Join("\n", problems) + "\nThe settings page (Ctrl+Comma) shows each one.", NotificationSeverity.Error,
                    [new NotificationAction("Open settings", _ => _host.ExecuteCommandAsync(ShellCommandIds.Settings), Primary: true)]);
            }
        }

        // The owner's note 21: the last run did not close cleanly, so its problem report is offered.
        try
        {
            await _report.OfferAsync();
        }
#pragma warning disable CA1031 // The offer never ends the start.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Error, $"The problem report's offer failed: {error.Message}", error);
        }
    }

    // The window's close: the unsaved tabs asked about, then the session written (awaited: its write is on the thread pool).
    private async Task<bool> CloseAsync()
    {
        if (!await _shell.ConfirmCloseAllAsync())
        {
            return false;
        }

        try
        {
            await _shell.SaveSessionAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _host.Log(EditorLogLevel.Warning, $"The session was not written: {error.Message}", error);
        }

        return true;
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
        if (_centre.IsOpen)
        {
            _toast.Hide();
        }

        OnNotificationsChanged(this, EventArgs.Empty);
    }

    private void OnNotificationsChanged(object? sender, EventArgs e) => _bell.Show(_host.Notifications.Unseen, _host.ActivityCenter.Running.Count);

    private void OnToast(object? sender, NotificationEntry entry)
    {
        if (!_centre.IsOpen)
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
        if (!_startup.Platform.IsReady(_window))
        {
            // B2's trail: WinUI had not filled the window yet; asked again below, nothing thrown.
            missing = "The window has no platform content yet: the keys outside the editor and the unsaved-changes question are hooked once it has.";
        }
        else if (!_keysRouted)
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

        if (!_closeGuarded && _startup.Platform.IsReady(_window))
        {
            try
            {
                // X part 9: the close also writes the session (the carets and scroll as they are), once the unsaved tabs are settled.
                _platformHooks.Add(_startup.Platform.GuardClose(_window, () => _shell.Groups.HasUnsaved || _shell.SessionToWrite, CloseAsync));
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
            Dispatcher.DispatchDelayedLabelled(TimeSpan.FromMilliseconds(PlatformHookRetryMilliseconds), "shell: hook the platform", () => HookPlatform(attempt + 1));
        }
        else
        {
            _status.Notice.Text = missing;
        }
    }

    /// <summary>The app's own commands, on the same contracts as the plug-ins': the palette (Ctrl+Shift+P) and the Plug-ins page (the shell registers the rest).</summary>
    private void RegisterAppCommands()
    {
        EditorCommands.Register(new EditorCommand(ShellCommandIds.CommandPalette, "View: Show all commands", (_, _) =>
        {
            ShowOverlay(">");
            return Task.CompletedTask;
        }));
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
        // X part 10: the conversation's menus and commands; Expand (Ctrl+Shift+Alt+M, a standard binding in both keymaps).
        _conversationCommands = new ConversationCommands(ToggleChatExpandedAsync, () =>
        {
            ShowChatColumn();
            _conversations.ShowList(true);
        }, ExportConversationAsync);
        EditorCommands.Register(new EditorCommand(ContextMenuCommand, "Edit: Show the context menu", (_, _) =>
        {
            OpenEditorMenuAtCaret();
            return Task.CompletedTask;
        }, _ => _shown?.Model is not null, "The editor's context menu at the caret, its first row focused (the arrows, Enter, Escape)"));
        EditorKeyBindings.Register(new KeyBinding(new KeyChord(EditorKey.F10, KeyModifiers.Shift), ContextMenuCommand, When: KeyContext.TextEditor));
        EditorCommands.Register(new EditorCommand("craft.notifications", "View: Show or hide the notifications", (_, _) =>
        {
            ToggleCentre();
            return Task.CompletedTask;
        }, null, "The notification centre: running operations with Cancel, how they ended, the plug-ins' messages"));
    }

    private ConversationCommands? _conversationCommands;

    // The room the editor would have beside the chat column (0 before the page is measured).
    private double EditorRoom() => Width <= 0 ? 0
        : Width - _rail.Width - (_sideSplit.IsShown ? _sideSplit.Width : 0) - _shell.Panels.SizeOf(ShellPanel.Chat);

    // X part 10: Expand over the editor area, or back to the column at its kept width (the button, the command, Escape).
    private async Task ToggleChatExpandedAsync()
    {
        bool narrow = _shell.Layout.IsNarrow;
        bool open = narrow ? _chatSheetOpen : _shell.Panels.IsOpen(ShellPanel.Chat);
        if (!open)
        {
            ShowChatColumn();
            if (_shell.Panels.IsChatExpanded(narrow, EditorRoom()))
            {
                return;                                   // opened, and it shows expanded already
            }
        }

        await _shell.Panels.ToggleChatExpandedAsync(narrow, EditorRoom());
        ApplyLayout();
        _conversations.FocusInput();
    }

    private void ShowChatColumn()
    {
        if (_shell.Layout.IsNarrow)
        {
            _chatSheetOpen = true;
            ApplyLayout();
        }
        else if (!_shell.Panels.IsOpen(ShellPanel.Chat))
        {
            _ = _shell.Panels.SetOpenAsync(ShellPanel.Chat, true);
        }
    }

    // A conversation's menu (a message's, the panel's) at its place in the anchor.
    private void OpenConversationMenu(ConversationMenuEventArgs e)
    {
        var below = _popup.Below(e.Anchor);
        var at = new Point(below.X + e.At.X, below.Y - e.Anchor.Height + e.At.Y);
        if (_menu.Open(e.Menu, e.Target))
        {
            ShowMenuAt(at);
        }
    }

    // Export the conversation: where, through the save dialog (the workspace's folder first), then written off the UI thread.
    private async Task ExportConversationAsync(ConversationController controller)
    {
        try
        {
            if (await _shell.FileDialogs.PickSavePathAsync(controller.ExportName(), _host.WorkspaceFolder) is { } path)
            {
                await controller.ExportAsync(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status!.Notice.Text = "The conversation was not exported: " + error.Message;
        }
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

        if (_chatSplit.IsShown)
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

    // A splitter's drag: within the room the panel and the editor share; its end written to the settings once. B8: the
    // platform's pointer (the resize arrows, a captured drag), a double-click back to the option's default, a drag below
    // the smallest size closing the panel, and the grip above its neighbours (it reaches over them).
    private void Wire(SplitPanel split, ShellPanel panel, Func<double> room)
    {
        split.Clamp = size => _shell.Panels.Clamp(size, room());
        split.DefaultSize = PanelLayout.DefaultSizeOf(panel);
        split.Minimum = _shell.Panels.MinimumSize;
        split.Apply(_shell.Options);
        split.Pointer = _startup.Platform.PointerCursor;
        split.ZIndex = 10;
        split.Resized += (_, size) =>
        {
            _shell.Panels.Resize(panel, size);
            _ = _shell.Panels.CommitAsync(panel);
        };
        split.CollapseRequested += (_, _) => _ = _shell.Panels.SetOpenAsync(panel, false);
    }

    // B10: the loader follows the activities; when the last workspace work ends, the language's state is read again (its
    // notification, the status bar's text), since a build that failed changes no diagnostics.
    private void OnActivitiesChanged(object? sender, EventArgs e)
    {
        int running = 0;
        foreach (var activity in _host.ActivityCenter.Running)
        {
            if (activity.Kind == ActivityKinds.Workspace)
            {
                running++;
            }
        }

        bool ended = running < _workspaceWork;
        _workspaceWork = running;
        if (ended)
        {
            UpdateProblems();
        }
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
        view.SetPalette(_palette);
        _sideViews[id] = view;
        return view;
    }

    private void OnPublishedDiagnosticsChanged(object? sender, EventArgs e) => UpdateProblems();

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
            ShellPageIds.Welcome => new WelcomeView(_shell, _palette),
            ShellPageIds.Plugins => new PluginsPage(_shell, () => PluginList.Rows(CraftPlugins.Installed, _host.Plugins)),
            // X part 9: a Markdown document's preview and an image's.
            _ when _shell.MarkdownPreviews.TryGetValue(page.Id, out var preview) => new MarkdownPreviewView(_shell, preview, _palette),
            _ when page.Id.StartsWith(ShellModel.ImagePagePrefix, StringComparison.Ordinal) => new ImagePreviewView(_shell, page.Id, _palette),
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
    private void OnCodeActionRequested(ProblemItem item, string kind)
    {
        // X part 11 item 5: a build's or test's row: Fix with AI fixes its one error (a diff to accept; Run again in the
        // Run view); Explain opens its file first, then explains it there.
        if (item.Group.Owner is Craft.Editor.Run.RunProblemFile built)
        {
            if (kind == CodeActionKinds.Fix)
            {
                _ = FixBuildProblemAsync(built, item.Diagnostic);
            }
            else
            {
                _ = OpenRunProblemAsync(built.Path, item.Line, item.Column);
            }
            return;
        }

        if (item.Group.Owner is ClosedProblemFile closed)
        {
            // W: a closed file's row has no text to act on: the file opens at its place, its problems then on its text.
            OnProblemChosen(item);
            _status.Notice.Text = $"Opened {Path.GetFileName(closed.Path)}: its problems are on its text now; {kind} from its row again.";
            return;
        }

        if (item.Group.Owner is DocumentTab owner && !ReferenceEquals(owner, _shell.Tabs.Active) && _shell.Groups.GroupOf(owner) is { } ownerGroup)
        {
            ownerGroup.Tabs.Activate(owner);                // a row of another file: its file first (its group focused)
        }

        if (_shell.Tabs.Active is not { } active || _shown?.Model is not { } model)
        {
            return;
        }

        // The row was listed before the last edit: its problem is taken as the editor has it now (moved, or gone).
        if (ShellText.CurrentProblem(model.Diagnostics, item.Diagnostic) is not { } diagnostic)
        {
            _status.Notice.Text = "That problem is gone: the text changed since it was listed.";
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

    private async Task FixBuildProblemAsync(Craft.Editor.Run.RunProblemFile file, EditorDiagnostic diagnostic)
    {
        if (await _shell.Run.FixProblemAsync(file, diagnostic) is null && !_shell.Run.IsFixing)
        {
            _status.Notice.Text = "No plug-in offers to fix this build problem (the AI plug-in's Fix with AI).";
        }
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

    private async Task PickTabAsync(EditorGroup group)
    {
        var tabs = group.Tabs.Tabs.ToArray();
        int picked = await _dialog.PickAsync("Tabs", [.. tabs.Select(TabStripView.TitleOf)]);
        if (picked >= 0 && picked < tabs.Length && group.Tabs.Contains(tabs[picked]))
        {
            group.Tabs.Activate(tabs[picked]);
        }
    }

    private void OnMenusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("shell: rebuild the menus", _titleWide.RebuildMenus);
        }
        else
        {
            _titleWide.RebuildMenus();
        }
    }

    // A title-bar menu under its button (phase X: the menu model's rows, keys and states).
    private void ShowMenu(string menu, View anchor)
    {
        _menu.Open(menu);
        ShowMenuAt(_popup.Below(anchor));
    }

    // A context menu (phase X) at the right-click, on what it was opened on; each row runs a registered command.
    private void OpenContextMenu(string menu, ContextMenuTarget target, TappedEventArgs where)
    {
        var at = where.GetPosition(_popup) ?? new Point(0, 0);
        _menu.Open(menu, target);
        ShowMenuAt(at);
    }

    // Shift+F10: the editor's menu at the caret, its first row focused.
    private void OpenEditorMenuAtCaret()
    {
        if (_shown is not { Model: { } model } view || _shell.Tabs.Active is not { } tab)
        {
            return;
        }

        var caret = model.PointFromPosition(model.Selection.Caret);
        var below = _popup.Below(view);
        var at = new Point(below.X + caret.X - model.ScrollX, below.Y - view.Height + caret.Y - model.ScrollY + model.Metrics.LineHeight);
        _menu.Open(EditorContextMenus.Editor, new ContextMenuTarget(EditorContextMenus.Editor, tab.FilePath, false, tab), focusFirst: true);
        ShowMenuAt(at);
    }

    private void ShowMenuAt(Point at)
    {
        _menuView.Build(_palette);
        _popup.Show(_menuView, at, MenuWidth);
        _ = ShellMotion.ShowAsync(_menuView, -4);
    }

    // An empty state's action: its command, or its folder opened.
    private void RunEmptyAction(EmptyAction action)
    {
        if (action.Path is { } folder)
        {
            _ = _shell.OpenFolderAsync(folder);
        }
        else if (action.CommandId is { } command)
        {
            _ = _host.ExecuteCommandAsync(command);
        }
    }

    // The explorer with no folder says what to do (phase X).
    private void UpdateExplorerEmpty() =>
        _explorer.SetEmpty(_shell.Workspace is null ? ShellEmptyStates.Explorer(_host, _shell.Recent) : null);

    private void OnRecentChanged(object? sender, EventArgs e) => _host.Dispatch("shell: the recent folders", UpdateExplorerEmpty);

    private void OnKeyBindingsChanged(object? sender, EventArgs e) => _host.Dispatch("shell: the keys in the tooltips", () =>
    {
        ApplyTooltips();
        UpdateExplorerEmpty();
    });

    // Phase X: every icon-only control's tooltip names its action and the keys in effect (a remap shows at once).
    private void ApplyTooltips()
    {
        string Tip(string title, string command) => ShellTooltips.For(_host, title, command);
        _titleWide.SetTooltips(Tip);
        _titleNarrow.SetTooltips(Tip);
        foreach (var pane in _panes)
        {
            pane.Strip.Refresh();
        }

        _conversations.SetCloseTooltip(Tip("Close the chat", "craft.chat"));
        _centre.SetTooltips(Tip("Close the notifications", "craft.notifications"), "Dismiss every notification that has ended");
        _bell.Keys = _host.KeysOf("craft.notifications") is { } keys ? $" ({keys})" : "";
        _bell.Refresh();
        ToolTipProperties.SetText(_status.Problems, Tip("Show the problems", ShellCommandIds.ShowProblems));
        ToolTipProperties.SetText(_status.Position, Tip("Go to line", ShellCommandIds.GoToLine));
        ToolTipProperties.SetText(_status.Branch, EditorCommands.Find(SourceControlProviders.BranchCommand) is null
            ? "The folder's git branch"
            : Tip("Switch or create a branch (↑ commits to push, ↓ to pull)", SourceControlProviders.BranchCommand));
        ToolTipProperties.SetText(_status.Indentation, "Indentation: CRAFT_TAB_SIZE and CRAFT_INSERT_SPACES (Settings, Editor)");
        ToolTipProperties.SetText(_status.Encoding, "The file's encoding (kept when it is saved)");
        ToolTipProperties.SetText(_status.LineEnding, "The file's line breaks (kept when it is saved; CRAFT_LINE_ENDING for new files)");
        ToolTipProperties.SetText(_status.Language, "The file's language and the plug-in that brings it");
        ToolTipProperties.SetText(_status.Paint, "The last frame's paint time");
        ToolTipProperties.SetText(_status.Model, "The models in use: pick another with the model chip under the chat's or the agent's input");
    }

    // The search box's text, taken into the overlay (the box itself stays a button-like field).
    private string TakeSearchText()
    {
        var text = _titleWide.Search.Text ?? "";
        if (text.Length > 0)
        {
            _titleWide.Search.Text = "";
        }

        return text;
    }

    // Phase X: the one overlay for quick open, the palette ('>') and go to line (':'), under the title bar's search box
    // (across the top in the narrow layout).
    private void ShowOverlay(string text, bool keepScope = false)
    {
        if (!keepScope)
        {
            _quickOpen.SymbolsUnder = null;                      // Ctrl+Shift+O lists every symbol; the breadcrumb a level
            _quickOpen.EndBrowse();
        }

        if (_shown?.Model is { } previewed)
        {
            _symbolPreview.Begin(previewed);
        }

        _menu.Close();
        _overlay.Apply(_palette);
        double width;
        Point at;
        if (_shell.Layout.IsNarrow || _windowTitleBar is not null)
        {
            width = Math.Max(200, Width - 16);
            at = new Point(8, _shell.Layout.IsNarrow ? _titleNarrow.Height : 4);
        }
        else
        {
            width = Math.Min(QuickOpenWidth, Math.Max(240, Width - 32));
            var below = _popup.Below(_titleWide.Search);
            double center = below.X + (_titleWide.Search.Width / 2);
            at = new Point(Math.Max(8, center - (width / 2)), Math.Max(4, below.Y + 2));
        }

        _popup.Show(_overlay, at, width);
        _overlay.Start(text);
    }

    // X part 9: a quick pick in the quick-open overlay (the theme picker, the status bar's pickers): the index picked, or -1.
    private Task<int> QuickPickAsync(QuickPickRequest request, CancellationToken cancellationToken)
    {
        EndQuickPick(-1);                                        // one at a time: an open one is cancelled
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _quickPickAnswer = answer;
        _quickOpen.BeginPick(request);
        ShowOverlay("");
        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(() => Dispatcher.DispatchLabelled("quick pick: cancelled", () =>
            {
                if (ReferenceEquals(_quickPickAnswer, answer))
                {
                    EndQuickPick(-1);
                }
            }));
            _ = answer.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return answer.Task;
    }

    private void EndQuickPick(int index)
    {
        if (_quickPickAnswer is not { } answer)
        {
            return;
        }

        _quickPickAnswer = null;
        _quickOpen.EndPick();
        _popup.Hide();
        _shown?.Focus();
        answer.TrySetResult(index);
    }

    private async Task PickAsync(QuickOpenItem item)
    {
        if (item.IsFolder && item.Path is { } folder)
        {
            await _quickOpen.BrowseAsync(folder);               // a folder of the breadcrumb's listing: its entries in turn
            ShowOverlay("", keepScope: true);
            return;
        }

        _popup.Hide();
        _symbolPreview.End();
        _quickOpen.EndBrowse();
        if (item.SymbolStart >= 0 && _shown?.Model is { } model)
        {
            int length = model.Snapshot.Length;
            int start = Math.Clamp(item.SymbolStart, 0, length);
            model.Select(Math.Clamp(start + item.SymbolLength, start, length), start);
            model.EnsureCaretVisible();
        }
        else if (item.Line > 0)
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

        if (item.CommandId is null)
        {
            _shown?.Focus();
        }
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

    // A drop on the window: a file dragged from the tree (its path in the drag's properties), else what the system's file
    // manager dropped (the platform reads its paths). Files open in tabs, a folder as the workspace.
    private async void OnDrop(object? sender, DropEventArgs e)
    {
        try
        {
            if (_conversations.Holds(e))
            {
                return;     // dropped on the chat column: its panel takes the files (ConversationsView)
            }

            // A drop a pane or a tree row took (a tab, a move), a folder of the tree or a tab let go elsewhere: nothing opens.
            if (e.Handled || e.Data.Properties.ContainsKey(FolderTreeView.DragFolderKey) || e.Data.Properties.ContainsKey(TabStripView.DragTabKey))
            {
                return;
            }

            IReadOnlyList<string> paths = e.Data.Properties.TryGetValue(FolderTreeView.DragPathKey, out var dragged) && dragged is string path
                ? [path]
                : await _startup.Platform.DroppedPathsAsync(e);
            if (paths.Count == 0)
            {
                return;
            }

            e.Handled = true;
            var result = await _shell.OpenDroppedAsync(paths);
            if (result.Skipped.Count > 0)
            {
                _status.Notice.Text = "Not opened: " + string.Join("; ", result.Skipped);
            }

            _shown?.Focus();
        }
#pragma warning disable CA1031 // An async void drop handler: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the window: a drop", error);
        }
    }

    private void OnViewRequested(string view)
    {
        switch (view)
        {
            case ShellViewNames.GoToLine:
                ShowOverlay(":");
                break;
            case ShellViewNames.GoToSymbol:
                ShowOverlay("@");
                break;
            case ShellViewNames.QuickOpen:
                ShowOverlay(TakeSearchText());
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
            case ShellViewNames.Hierarchy:
                ShowBottom(BottomTab.Hierarchy);
                break;

            case ShellViewNames.Todo:
                ShowBottom(BottomTab.Todo);
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
        // Note 25: the chip is the shown panel's role (the chat's, the agent's), each with its own model and device.
        string? role = _conversations.Shown?.ModelRoleKey;
        var shown = status?.RoleOf(role);
        string chip = ShellText.ModelChip(status, role);
        string? tooltip = shown is { } r ? $"{r.Role}: {r.Model ?? "none set"} · {(r.Enabled ? r.State : "off")}" : "No model provider is installed (the AI plug-in brings \"local\").";
        var state = shown?.Model is null || shown is { Enabled: false } ? ModelChipState.None
            : shown.IsProblem ? ModelChipState.Problem
            : shown.IsLoading || (shown.Key is null && status!.IsLoading) ? ModelChipState.Loading
            : ModelChipState.Ready;
        // The owner's note 11: the model chip is the composer's (the chat's and the agent's), with its device (note 6).
        _conversations.ModelChip.Set(chip, state, tooltip);
        _conversations.ModelChanged();                           // an empty chat says which model answers
        _status.Model.Text = ShellText.ModelUsage(status);
        ToolTipProperties.SetText(_status.Model, ShellText.ModelRolesTooltip(status));   // each role's model and device
    }

    // The composer's model chip: the provider's models and devices in a Craft dialog; a model picked becomes the shown
    // panel's role's model setting and loads (off the UI thread, as an activity), a device its device setting, the last
    // row opens the page (note 25: the chat's chip writes the chat's settings, the agent's the agent's).
    private async Task PickModelAsync()
    {
        var found = ShellText.ModelStatus();
        var status = found?.Status;
        string? role = _conversations.Shown?.ModelRoleKey;
        var picks = ShellText.ModelPicks(status, role);
        string text = status?.RoleOf(role) is { Model: { } model } shown
            ? $"{shown.Role}: {model} · {ShellText.PlannedDevice(status, role) ?? "device not known yet"}"
            : "No model is set.";
        int index = await _dialog.PickAsync("Model", [.. picks.Select(p => p.Label)], text: text);
        if (index < 0)
        {
            return;
        }

        var pick = picks[index];
        if (ShellText.WriteOf(status, role, pick) is not { } write)
        {
            await _host.ExecuteCommandAsync(ShellCommandIds.Models);
            UpdateModel();
            return;
        }

        if (Environment.GetEnvironmentVariable(write.Variable) is { Length: > 0 } fixedValue)
        {
            // The environment overrides the settings files: say so rather than write a value that cannot take effect.
            await _dialog.ShowMessageAsync("Set by the environment", $"{write.Variable} is {fixedValue} in this process's environment, which overrides the settings. Clear it (or change it) and start Craft again to pick here.");
            return;
        }

        if (_shell.ApplySetting(SettingLayer.User, write.Variable, write.Value) is { } problem)
        {
            await _dialog.ShowMessageAsync(pick.Kind == ModelPickKind.Model ? "The model was not set" : "The device was not set", problem);
        }
        else if (write.LoadCommand is { } load)
        {
            await _host.ExecuteCommandAsync(load);
        }

        UpdateModel();
    }

    // A tab removed has its view disposed (a tab gets its view when its group first shows it); a view moved with its tab to
    // another group (the split editor) is kept.
    private void OnTabsChanged()
    {
        var owned = new HashSet<EditorView>(ReferenceEqualityComparer.Instance);
        foreach (var tab in _shell.Groups.AllTabs)
        {
            if (tab.View is EditorView view)
            {
                owned.Add(view);
            }
        }

        foreach (var view in _views.ToArray())
        {
            if (owned.Contains(view))
            {
                continue;
            }

            _views.Remove(view);
            if (ReferenceEquals(_peekSource, view))
            {
                ClosePeek();
            }

            foreach (var pane in _panes)
            {
                if (ReferenceEquals(pane.Shown, view))
                {
                    pane.Shown = null;
                    if (ReferenceEquals(pane.Area.Content, view))
                    {
                        pane.Area.Content = pane.Empty;
                    }
                }
            }

            if (ReferenceEquals(_shown, view))
            {
                _shown = null;
            }

            view.Dispose();
        }

        UpdateTitle();
    }

    // The tab whose editor is `view` (in any group), or null.
    private DocumentTab? OwnerOf(EditorView view) => _shell.Groups.AllTabs.FirstOrDefault(t => ReferenceEquals(t.View, view));

    // A group's pane, made the first time the group shows: its strip (the first group's with the page tabs), its editor
    // area taking drops.
    private EditorGroupPane PaneOf(EditorGroup group)
    {
        if (group.View is EditorGroupPane made)
        {
            return made;
        }

        var strip = new TabStripView(group.Tabs) { Tooltip = (title, command) => ShellTooltips.For(_host, title, command) };
        strip.CloseRequested += (_, tab) => _ = _shell.CloseTabAsync(tab);
        strip.ListRequested += (_, _) => _ = PickTabAsync(group);
        strip.ContextRequested += (_, e) => OpenContextMenu(EditorContextMenus.Tab, new ContextMenuTarget(EditorContextMenus.Tab, e.Tab.FilePath, false, e.Tab), e.Where);
        strip.UnpinRequested += (_, tab) => _shell.Groups.GroupOf(tab)?.Tabs.SetPinned(tab, false);
        strip.KeepRequested += (_, tab) => _shell.KeepTab(tab);           // X part 9: a double click keeps a preview
        strip.NewFileRequested += (_, _) =>
        {
            _shell.Groups.Focus(group);
            _ = _host.ExecuteCommandAsync(ShellCommandIds.NewFile);
        };
        strip.ForeignTabDropped += (_, e) => _shell.Groups.MoveTab(e.Dragged, group, group.Tabs.IndexOf(e.Target));
        var pane = new EditorGroupPane(group, strip, _palette);
        if (group.Id == 1)
        {
            pane.Empty.Text = _emptyText;
        }

        pane.Engaged += (_, _) => _shell.Groups.Focus(group);
        pane.TabDropped += (_, e) =>
        {
            _shell.Groups.Drop(e.Tab, group, e.Zone);
            _shown?.Focus();
        };
        pane.FileDropped += (_, e) => _ = OpenDroppedFileAsync(group, e.Path, e.Zone);
        strip.Compact = _shell.Layout.IsNarrow;
        group.View = pane;
        _panes.Add(pane);
        return pane;
    }

    // A file from the tree dropped on a group's zone: opened in the group, or in a new group beside it.
    private async Task OpenDroppedFileAsync(EditorGroup group, string path, EditorDropZone zone)
    {
        try
        {
            var target = zone is EditorDropZone.Right or EditorDropZone.Down
                ? _shell.Groups.Split(group, zone == EditorDropZone.Right ? SplitDirection.Right : SplitDirection.Down) ?? group
                : group;
            await _shell.OpenInGroupAsync(target, path);
            _shown?.Focus();
        }
#pragma warning disable CA1031 // A drop's work: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the editor: a file dropped on a group", error);
        }
    }

    // Groups added or closed: the closed ones' panes go, the first group's strip holds the page tabs, the layout is laid
    // out again (the panes and their editors are kept).
    private void OnGroupsChanged()
    {
        var open = _shell.Groups.Groups;
        foreach (var pane in _panes.ToArray())
        {
            if (!open.Contains(pane.Group))
            {
                if (ReferenceEquals(_peekPane, pane))
                {
                    ClosePeek();
                }

                _panes.Remove(pane);
                pane.Strip.Dispose();
                pane.Group.View = null;
            }
        }

        for (int i = 0; i < open.Count; i++)
        {
            PaneOf(open[i]).Strip.Pages = i == 0 ? _shell.Pages : null;
        }

        _groupsView.Rebuild();
        ShowActive();
    }

    private async Task MoveEntryAsync(string source, string targetFolder, bool copy)
    {
        try
        {
            await _shell.MoveEntryAsync(source, targetFolder, copy);
        }
#pragma warning disable CA1031 // A drop's work: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the explorer: a move by drag", error);
        }
    }

    private IEditorNotification? _completionStatus;

    // X part 9: a file's own settings on its view: at once when its folders were read, else read off the UI thread and
    // applied when they come back (the view's model is not made again).
    private void ApplyFileSettings(EditorView view, string? path)
    {
        if (path is null)
        {
            return;
        }

        if (_shell.CachedFileSettings(path) is { } known)
        {
            view.FileSettings = known;
            return;
        }

        _ = ApplyFileSettingsAsync(view, path);
    }

    private async Task ApplyFileSettingsAsync(EditorView view, string path)
    {
        try
        {
            var settings = await _shell.FileSettingsAsync(path);
            if (!_disposed && _views.Contains(view))
            {
                view.FileSettings = settings;
                if (ReferenceEquals(view, _shown))
                {
                    UpdateStatus();
                }
            }
        }
#pragma warning disable CA1031 // An unreadable .editorconfig leaves the options as they are, never a crash.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(Craft.Abstractions.EditorLogLevel.Warning, $"The settings of {path} could not be read: {error.Message}", error);
        }
    }

    private void OnFileSettingsChanged(object? sender, Craft.Editor.Shell.FileSettingsChangedEventArgs e)
    {
        foreach (var tab in _shell.Groups.AllTabs)
        {
            if (tab.View is EditorView view && tab.FilePath is { } path
                && (e.Path is null || string.Equals(Path.GetFullPath(e.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            {
                ApplyFileSettings(view, path);
            }
        }

        UpdateStatus();
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
            Host = _host,
            WorkspaceRoot = _shell.Workspace?.Root,
            ShowsMinimap = true,                            // the X restart, piece 2b: one minimap per document view
        };
        view.Sessions = _shell.Sessions;                    // one language session per document, shared by its views
        view.Open(tab.Document, tab.Language);
        // X part 9: the lightbulb asks the code action providers; the file's .editorconfig applies over the options.
        view.LightbulbProbe = ShellModel.LightbulbProbe(tab);
        view.QuickFixRequested += (_, _) => _ = _host.ExecuteCommandAsync(Craft.Editor.Shell.StandardShortcuts.QuickFix);
        ApplyFileSettings(view, tab.FilePath);
        view.Engaged += (_, _) =>
        {
            if (OwnerOf(view) is { } owner && _shell.Groups.GroupOf(owner) is { } group)
            {
                _shell.Groups.Focus(group);
            }

            if (_peek is not null)
            {
                FocusPeek(ReferenceEquals(view, _peek.Editor));
            }
        };
        view.PeekRequested += OnPeekRequested;
        view.StatusRequested += (_, message) =>
        {
            // One completion status line, replaced by the next (applied at 3 of 4 carets).
            _completionStatus?.Dispose();
            _completionStatus = _shell.Host.ShowStatus("Completion", message);
        };
        // X part 9: a tab the last session reopened gets its caret, scroll and folds when its editor is made.
        view.ModelChanged += (_, _) =>
        {
            if (view.Model is { } made && OwnerOf(view) is { } owner)
            {
                _shell.ApplyViewState(owner, made);
            }
        };
        if (view.Model is { } first)
        {
            _shell.ApplyViewState(tab, first);              // the model Open made, before the tab holds the view
        }
        view.DiagnosticsChanged += (_, _) =>
        {
            // Phase A1: the plug-ins read a document's diagnostics, and the diff view checks a change, through the host.
            if (view.Model is { } shownModel && OwnerOf(view) is { } owner)
            {
                _host.SetDiagnostics(owner.HostDocument, shownModel.Diagnostics);
                _host.SetSemanticSession(owner.HostDocument, shownModel.SemanticSession);
            }

            UpdateProblems();                               // every open file's problems are listed
        };
        view.DefinitionRequested += OnDefinitionRequested;
        view.ContextRequested += (_, e) =>
        {
            if (OwnerOf(view) is { } owner)
            {
                OpenContextMenu(EditorContextMenus.Editor, new ContextMenuTarget(EditorContextMenus.Editor, owner.FilePath, false, owner), e);
            }
        };
        view.SaveRequested += (_, _) => _ = _host.ExecuteCommandAsync(ShellCommandIds.Save);
        view.FindRequested += (_, e) => ShowFind(e.Replace, e.InitialQuery);   // the document's own find (not Output's)
        view.CaretMoved += (_, _) =>
        {
            // A document in two groups: the focused view's selection is the host's.
            if (view.Model is { } model && OwnerOf(view) is { } owner && (ReferenceEquals(view, _shown) || !_shell.Groups.IsShared(owner)))
            {
                _host.UpdateSelection(owner.HostDocument, model.Selection);
            }

            if (ReferenceEquals(view, _shown))
            {
                UpdateStatus();
                _find.Recount();
            }
        };
        view.Painted += (_, _) =>
        {
            if (ReferenceEquals(view, _shown))
            {
                UpdatePaintTime();
                // B1 (rules 76, 78): each frame's time in the telemetry table, cold (it laid text out) apart from warm.
                _host.Record(view.LastFrameCold ? "editor.paint.cold" : "editor.paint.warm", view.LastFrameMilliseconds, "ms");
            }
        };
        _views.Add(view);
        return view;
    }

    // The editor area: each group's pane shows its active document's editor (the first group a page tab when one shows),
    // else its hint; the focused group's pane has the find overlay and the breadcrumb, and only its editor's caret blinks.
    private void ShowActive()
    {
        var groups = _shell.Groups;
        var focused = groups.Focused;
        var page = _shell.Pages.Active;
        EditorView? shown = null;
        bool focusedChanged = false;
        for (int i = 0; i < groups.Groups.Count; i++)
        {
            var group = groups.Groups[i];
            var pane = PaneOf(group);
            var groupPage = i == 0 ? page : null;
            var tab = group.Tabs.Active;
            if (tab is not null && groupPage is null)
            {
                tab.View ??= MakeView(tab);
            }

            var view = groupPage is null ? tab?.View as EditorView : null;
            var content = groupPage is not null ? PageViewOf(groupPage) : (View?)view ?? pane.Empty;
            bool isFocused = ReferenceEquals(group, focused);
            if (!ReferenceEquals(view, pane.Shown) || !ReferenceEquals(pane.Area.Content, content))
            {
                if (ReferenceEquals(_peekPane, pane))
                {
                    ClosePeek();                                 // the peek belongs to the editor it came from
                }

                pane.Shown = view;
                pane.Area.Content = content;
                focusedChanged |= isFocused;
            }

            pane.Strip.FocusedGroup = isFocused || groups.Groups.Count == 1;
            if (view is not null)
            {
                view.IsFocusedView = isFocused && (_peek is null || !ReferenceEquals(_peekSource, view) || !_peek.Editor.IsFocusedView);
            }

            if (isFocused)
            {
                shown = view;
                MoveInto(_breadcrumb, pane.BreadcrumbSlot);
                if (!ReferenceEquals(_find.Parent, pane.Stack))
                {
                    (_find.Parent as Layout)?.Remove(_find);
                    pane.Stack.Add(_find);
                }
            }
        }

        _breadcrumb.IsVisible = !_shell.Layout.IsNarrow && !(ReferenceEquals(focused, groups.Groups[0]) && page is not null);
        if (!ReferenceEquals(shown, _shown) || focusedChanged)
        {
            _shown = shown;
            _find.Model = shown?.Model;                          // the find overlay follows the focused group's editor
            if (shown?.Model is { } model)
            {
                model.EnsureCaretVisible();
                shown.Focus();
            }
        }

        _tree.ActivePath = _shell.Tabs.Active?.FilePath;
        UpdateStatus();
        UpdateTitle();
        UpdateProblems();
    }

    private static void MoveInto(View view, ContentView slot)
    {
        if (ReferenceEquals(slot.Content, view))
        {
            return;
        }

        if (view.Parent is ContentView holder)
        {
            holder.Content = null;
        }

        slot.Content = view;
    }

    // Alt+F12 from an editor: the peek, resolved off the UI thread, shown under the caret's line of that editor.
    private async void OnPeekRequested(object? sender, DefinitionRequestedEventArgs e)
    {
        try
        {
            if (sender is not EditorView source || source.Model is not { } model)
            {
                return;
            }

            var peek = await _shell.PeekAsync(model, e.Location);
            if (peek is null)
            {
                return;
            }

            if (_disposed || source.Model is null || _panes.FirstOrDefault(p => ReferenceEquals(p.Shown, source)) is not { } pane)
            {
                peek.Dispose();                                  // the editor went meanwhile
                return;
            }

            ShowPeek(pane, source, peek);
        }
#pragma warning disable CA1031 // An async void handler: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the editor: peek definition", error);
        }
    }

    private void ShowPeek(EditorGroupPane pane, EditorView source, PeekSession peek)
    {
        ClosePeek();
        var view = new PeekView(peek, _palette, editor =>
        {
            editor.Options = _shell.Options;
            editor.Theme = _shell.Theme;
            editor.KeyHandler = new PeekKeys(() => _peek, _keys);
            editor.Labels = _host.Labels;
            editor.Activities = _host.Activities;
        }, _shell.Options.PeekLines);
        view.Editor.Engaged += (_, _) => FocusPeek(true);
        view.OpenRequested += (_, _) => _ = OpenPeekAsync();
        view.CloseRequested += (_, _) => ClosePeek();
        // Under the caret's line, kept inside the editor area.
        var model = source.Model!;
        double line = model.Metrics.LineHeight;
        double below = ((model.RowOfLine(model.CaretLine) + 1) * line) - model.ScrollY;
        double room = Math.Max(0, pane.Stack.Height - view.OverlayHeight - 8);
        view.Margin = new Thickness(8, Math.Clamp(below, 0, room), 8, 0);
        _peek = view;
        _peekPane = pane;
        _peekSource = source;
        pane.Stack.Add(view);
        FocusPeek(true);
        view.Editor.Focus();
        _ = ShellMotion.ShowAsync(view, -6);
    }

    // The peek's editor or the one it came from has the keyboard: only that one blinks.
    private void FocusPeek(bool peekHasFocus)
    {
        if (_peek is null)
        {
            return;
        }

        _peek.Editor.IsFocusedView = peekHasFocus;
        if (_peekSource is { } source)
        {
            source.IsFocusedView = !peekHasFocus && ReferenceEquals(source, _shown);
        }
    }

    private async Task OpenPeekAsync()
    {
        if (_peek is not { } view)
        {
            return;
        }

        var peek = view.Peek;
        ClosePeek();
        try
        {
            await _shell.OpenPeekAsync(peek);
            ShowActive();
            _shown?.Focus();
        }
#pragma warning disable CA1031 // A peek's open: nothing may leave it.
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the editor: open the peeked definition", error);
        }
    }

    // Closes the peek (Escape, ×, its editor gone); false when none was open.
    private bool ClosePeek()
    {
        if (_peek is not { } view)
        {
            return false;
        }

        _peek = null;
        var source = _peekSource;
        (view.Parent as Layout)?.Remove(view);
        _peekPane = null;
        _peekSource = null;
        view.Dispose();
        if (source is not null)
        {
            source.IsFocusedView = ReferenceEquals(source, _shown);
            if (ReferenceEquals(source, _shown))
            {
                source.Focus();
            }
        }

        return true;
    }

    /// <summary>Escape with a peek open closes it, wherever the key arrived; every other key goes on.</summary>
    private sealed class PeekEscapeKeys(IEditorKeyHandler inner, Func<bool> closePeek) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
            (key == EditorKey.Escape && modifiers == KeyModifiers.None && closePeek()) || inner.HandleKey(key, modifiers, editor);
    }

    private void UpdateProblems()
    {
        var model = _shown?.Model;
        // The owner's note 26: every open document's problems, grouped by file in the tabs' order.
        // A document in two groups is listed once (its first shown view's diagnostics).
        var sources = new List<ProblemSource>();
        var listed = new HashSet<ITextDocument>(ReferenceEqualityComparer.Instance);
        foreach (var tab in _shell.Groups.AllTabs)
        {
            // W, contract 6: a document with plug-ins' diagnostics only (a linted .ts) is listed too.
            if (tab.View is EditorView { Model: { } open } && (open.SemanticSession is not null || open.Diagnostics.Length > 0) && listed.Add(tab.Document))
            {
                sources.Add(new ProblemSource(tab.FilePath ?? tab.Title, tab.Title, open.Snapshot, open.Diagnostics.ToArray(), tab));
            }
        }

        // X part 6: the last run's (a build's) problems, each file's after the open documents'.
        foreach (var file in _shell.Run.ProblemFiles)
        {
            sources.Add(new ProblemSource(file.Path + "|run", Path.GetFileName(file.Path) + " (build)", file.Snapshot, [.. file.Diagnostics], file));
        }

        // X part 9: the last test run's failures, each at its failure's place.
        foreach (var file in _shell.Tests.ProblemFiles)
        {
            sources.Add(new ProblemSource(file.Path + "|tests", Path.GetFileName(file.Path) + " (tests)", file.Snapshot, [.. file.Diagnostics], file));
        }

        // W, contract 6: closed files' published diagnostics, by line and column (no file read).
        foreach (var file in _host.ClosedProblemFiles)
        {
            sources.Add(ProblemSource.ForClosedFile(file.Path, file.Diagnostics, file));
        }

        _bottom.ShowProblems(sources, model, _shell.Tabs.Active?.Title);
        _status.Problems.Text = model is null || (model.SemanticSession is null && model.Diagnostics.Length == 0) ? "" : ShellText.Problems(model.ErrorCount, model.WarningCount);
        _status.Problems.TextColor = model is { ErrorCount: > 0 } ? _palette.Error : _palette.Warning;
        var card = ShellText.Semantic(model);
        _workspaceNotices.Observe(model);
        _status.Semantic.Text = card?.StatusBarText ?? "";
        _status.Semantic.TextColor = card is { IsFailed: true } ? _palette.Error : _palette.Secondary;
        ToolTipProperties.SetText(_status.Semantic, card is { IsFailed: true } ? card.Tooltip + " (a click shows the notification)" : card?.Tooltip ?? "");
        string formatter = ShellText.FormatterTooltip(model);
        ToolTipProperties.SetText(_status.Language, card is null ? "The file's language and the plug-in that brings it · " + formatter
            : "The file's language and the plug-in that brings it · " + formatter + " · " + card.Tooltip);
    }

    private void OnProblemChosen(ProblemItem item)
    {
        if (item.Group.Owner is Craft.Editor.Run.RunProblemFile built)
        {
            _ = OpenRunProblemAsync(built.Path, item.Line, item.Column);
            return;
        }

        if (item.Group.Owner is ClosedProblemFile closed)
        {
            _ = OpenRunProblemAsync(closed.Path, item.Line, item.Column);  // W: a closed file's problem opens it at its line and column
            return;
        }

        if (item.Group.Owner is DocumentTab owner && !ReferenceEquals(owner, _shell.Tabs.Active) && _shell.Groups.GroupOf(owner) is { } ownerGroup)
        {
            ownerGroup.Tabs.Activate(owner);                // a problem of another open file: its tab first (its group focused)
        }

        if (_shown is { Model: { } model } view)
        {
            int position = Math.Clamp(item.Position, 0, model.Snapshot.Length);   // a row listed before the text shrank
            model.Select(position, position);
            model.EnsureCaretVisible();
            view.Focus();
        }
    }

    // A run's problem: its file opened (or shown), the caret at its line and column.
    private async Task OpenRunProblemAsync(string path, int line, int column)
    {
        if (await _host.OpenDocumentAsync(path) is not { } document)
        {
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        var text = snapshot.GetLine(Math.Clamp(line - 1, 0, snapshot.LineCount - 1));
        int position = text.Start + Math.Clamp(column - 1, 0, text.Length);
        _host.Select(document, SelectionState.At(position));
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
        // The focused group's view of the document first, then any other group's.
        foreach (var tab in _shell.Tabs.Tabs.Concat(_shell.Groups.AllTabs))
        {
            if (ReferenceEquals(tab.HostDocument, e.Document) && tab.View is EditorView { Model: { } model })
            {
                model.Select(e.Selection.Anchor, e.Selection.Caret);
                model.EnsureCaretVisible();
                return;
            }
        }
    }

    // A file state's colour in the tree (X part 8): the panel blocks' tones of the state, from the theme.
    private Color StateColor(SourceControlState state) => SourceControlStates.Tone(state) switch
    {
        PanelTone.Success => _palette.Success,
        PanelTone.Error => _palette.Error,
        PanelTone.Accent => _palette.Accent,
        _ => _palette.Warning,
    };

    private void OnWorkspaceChanged()
    {
        using var label = UiDispatch.Enter("shell: the workspace opened (tree, explorer, layout)");
        _tree.Tree = _shell.Tree;
        _explorer.SetFolder(_shell.Workspace?.Root);
        UpdateExplorerEmpty();
        _status.Branch.Text = _shell.BranchText;
        _ = _quickOpen.IndexAsync(_shell.Workspace);
        UpdateTitle();
        ApplyLayout();
    }

    private void OnOptionsChanged()
    {
        using var label = UiDispatch.Enter("shell: the options changed");
        var options = _shell.Options;
        // A folder opened with no setting of its own reloads options equal to the ones in use: nothing is rebuilt then
        // (each editor's model and language session, the whole window's theme), which took the UI thread at start-up.
        bool same = Equals(options, _appliedOptions) && ReferenceEquals(_shell.Theme, _appliedTheme);
        MauiEditorCanvas.TextLayouts = options.PaintLineLayouts ? _startup.Platform.TextLayouts : null;
        _appliedOptions = options;
        _appliedTheme = _shell.Theme;
        if (same)
        {
            return;
        }

        _groupsView.MinimumSize = options.PanelMinimumSize;
        _tree.MovesEntries = options.TreeDragMove;
        _menu.TypeAheadMilliseconds = options.MenuTypeAheadMilliseconds;
        _menuView.SubmenuDelayMilliseconds = options.MenuSubmenuDelayMilliseconds;
        foreach (var tab in _shell.Groups.AllTabs)
        {
            if (tab.View is EditorView view && !ReferenceEquals(view.Options, options) && !Equals(view.Options, options))
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

        ShellMotion.Milliseconds = Motion.Duration(options);
        _find.Model = _shown?.Model;                             // a new model on the new options
        _quickOpen.Rows = options.QuickOpenRows * 10;
        _quickOpen.CraftOptionsLimit = options.QuickOpenFiles;
        _quickOpen.BackgroundFiles = options.QuickOpenBackgroundFiles;
        _quickOpen.RecentLimit = options.QuickOpenRecent;
        _overlay.VisibleRows = options.QuickOpenRows;
        _tree.Indent = options.TreeIndent;
        _terminals.ApplyOptions();
        foreach (var split in (ReadOnlySpan<SplitPanel>)[_sideSplit, _chatSplit, _bottomSplit])
        {
            split.Apply(options);
            split.Minimum = _shell.Panels.MinimumSize;
        }

        ApplyTheme();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var layout = _shell.Layout;
        var panels = _shell.Panels;
        bool narrow = layout.IsNarrow;
        _shell.InlineTreeEdits = !narrow;                    // X part 7: names in place where the explorer shows beside the editor
        bool windowBar = _windowTitleBar is not null && !narrow;
        if (_window is not null)
        {
            _window.TitleBar = windowBar ? _windowTitleBar : null;
        }

        _titleRow.Content = narrow ? _titleNarrow : windowBar ? null : _titleWide;
        _titleRow.IsVisible = _titleRow.Content is not null;
        _titleLine.IsVisible = _titleRow.IsVisible;
        // X part 9, item 25: Zen mode hides the rail, the side view, the bottom panel, the chat, the status bar and the tabs,
        // and centres the editor at CRAFT_ZEN_WIDTH (the wide layout only).
        bool zen = _shell.IsZen && !narrow;
        _rail.IsVisible = _railLine.IsVisible = !narrow && !zen;

        // The side view: the explorer, or (wide only) the plug-in panel the rail chose; the narrow layout's tree as before.
        View side = _explorer;
        if (!narrow && panels.SideView != PanelLayout.ExplorerView && SideViewOf(panels.SideView) is { } panelView)
        {
            side = panelView;
        }

        bool sideVisible = narrow ? layout.TreeVisible : !zen && panels.IsOpen(ShellPanel.Explorer) && (!ReferenceEquals(side, _explorer) || layout.TreeVisible);
        if (!ReferenceEquals(_sideSplit.Panel, side))
        {
            _sideSplit.Panel = side;
        }

        _sideSplit.Fill = narrow;
        _sideSplit.Resizable = !narrow;
        _sideSplit.Size = panels.SizeOf(ShellPanel.Explorer);
        _sideSplit.SetVisible(sideVisible, narrow ? 0 : ShellMotion.Milliseconds);   // phase X: the panel grows open and shrinks closed
        _rail.Active = !narrow && sideVisible ? (ReferenceEquals(side, _explorer) ? "craft.explorer" : panels.SideView) : null;
        _center.IsVisible = layout.EditorVisible;

        bool chatOpen = _conversations.HasPanels && (narrow ? _chatSheetOpen : !zen && panels.IsOpen(ShellPanel.Chat));
        bool expanded = chatOpen && panels.IsChatExpanded(narrow, EditorRoom());
        _conversations.IsExpanded = expanded;
        Place(_chatSplit, expanded && !narrow ? _center : _body);
        _editorStack.IsVisible = !(expanded && !narrow);
        if (narrow)
        {
            // The sheet: over the body's lower part (all of it expanded, the narrow default), the width of the window, no splitter.
            Grid.SetColumn(_chatSplit, 0);
            Grid.SetColumnSpan(_chatSplit, 5);
            Grid.SetRow(_chatSplit, 0);
            Grid.SetRowSpan(_chatSplit, 1);
            _chatSplit.Fill = true;
            _chatSplit.Resizable = false;
            _chatSplit.VerticalOptions = expanded ? LayoutOptions.Fill : LayoutOptions.End;
            _chatSplit.HeightRequest = expanded ? -1 : Math.Max(240, Height * 0.62);
        }
        else if (expanded)
        {
            // X part 10: over the editor groups only (the tab strip, the breadcrumb and the editors), between the rail and
            // side view and the window's edge, following their widths; the bottom panel stays below.
            Grid.SetColumn(_chatSplit, 0);
            Grid.SetColumnSpan(_chatSplit, 1);
            Grid.SetRow(_chatSplit, 0);
            Grid.SetRowSpan(_chatSplit, 4);
            _chatSplit.Fill = true;
            _chatSplit.Resizable = false;
            _chatSplit.VerticalOptions = LayoutOptions.Fill;
            _chatSplit.HeightRequest = -1;
        }
        else
        {
            Grid.SetRow(_chatSplit, 0);
            Grid.SetRowSpan(_chatSplit, 1);
            Grid.SetColumn(_chatSplit, 4);
            Grid.SetColumnSpan(_chatSplit, 1);
            _chatSplit.Fill = false;
            _chatSplit.Resizable = true;
            _chatSplit.Size = panels.SizeOf(ShellPanel.Chat);
            _chatSplit.VerticalOptions = LayoutOptions.Fill;
            _chatSplit.HeightRequest = -1;
        }

        _chatSplit.SetVisible(chatOpen, narrow || expanded ? 0 : ShellMotion.Milliseconds);
        _bottomSplit.Size = panels.SizeOf(ShellPanel.Bottom);
        _bottomSplit.SetVisible(panels.IsOpen(ShellPanel.Bottom) && !narrow && !zen, narrow ? 0 : ShellMotion.Milliseconds);
        _breadcrumb.IsVisible = !narrow && !expanded && !(ReferenceEquals(_shell.Groups.Focused, _shell.Groups.Groups[0]) && _shell.Pages.Active is not null);
        _body.ColumnDefinitions[2].Width = narrow && layout.TreeVisible ? GridLength.Star : GridLength.Auto;
        _body.ColumnDefinitions[3].Width = layout.EditorVisible ? GridLength.Star : new GridLength(0);
        foreach (var pane in _panes)
        {
            pane.Strip.Compact = narrow;
            pane.Strip.IsVisible = !zen;
        }

        _status.IsVisible = _statusLine.IsVisible = !zen;
        int zenWidth = _shell.Options.ZenWidth;
        _editorStack.MaximumWidthRequest = zen && zenWidth > 0 ? zenWidth : double.PositiveInfinity;
        _editorStack.HorizontalOptions = zen && zenWidth > 0 ? LayoutOptions.Center : LayoutOptions.Fill;
        if (zen)
        {
            _breadcrumb.IsVisible = false;
        }

        if (_groupsView.Narrow != narrow || _groupsView.Content is null)
        {
            _groupsView.Narrow = narrow;
            _groupsView.Rebuild();
        }

        _status.SetNarrow(narrow);
        if (_popup.IsOpen)
        {
            _popup.Hide();
        }
    }

    // Moves `view` into `grid` (the expanded chat goes into the editor area's grid and back).
    private static void Place(View view, Grid grid)
    {
        if (ReferenceEquals(view.Parent, grid))
        {
            return;
        }

        (view.Parent as Grid)?.Children.Remove(view);
        grid.Children.Add(view);
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
        _find.Apply(p);
        _overlay.Apply(p);
        _undoBar.Apply(p);
        _recoveryCard.Apply(p);
        _titleNarrow.Apply(p);
        if (_windowTitleBar is not null)
        {
            _windowTitleBar.BackgroundColor = p.Ground;
            _windowTitleBar.ForegroundColor = p.Text;
        }

        _rail.Apply(p);
        _explorer.Apply(p);
        foreach (var pane in _panes)
        {
            pane.Apply(p);
        }

        if (_peek is not null)
        {
            _peek.Apply(p);
            _peek.Editor.Theme = theme;
        }

        _groupsView.Apply(p);
        _breadcrumb.Apply(p);
        _bottom.Apply(p);
        foreach (var page in _shell.Pages.Tabs)
        {
            (page.View as MarkdownPreviewView)?.Apply(p);
            (page.View as ImagePreviewView)?.Apply(p);
        }

        _terminalPanel.Apply(p);
        _status.Apply(p);
        _loader.Apply(_shell.Options, p.Accent);
        _activity.Apply(p);
        _bell.Apply(p);
        _centre.Apply(p);
        _toast.Apply(p);
        _center.BackgroundColor = p.Surface;
        // Phase X: every control's states take the new theme's tokens (one walk, at a theme change only).
        foreach (var element in ((IVisualTreeElement)this).GetVisualTreeDescendants())
        {
            if (element is VisualElement visual && InteractiveStates.Of(visual) is { } states)
            {
                states.Apply(p);
            }
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
            view.SetPalette(p);
        }

        _pluginStatus.TextColor = p.Secondary;
        _conversations.ApplyTheme(theme);
        _conversations.ApplyPalette(p);
        _chatSplit.BackgroundColor = p.Surface;
        UpdateProblems();
        UpdateModel();
    }

    private void ShowFind(bool replace, string? query, EditorModel? target = null)
    {
        // Output's Ctrl+F (the owner's note 26) searches Output's read-only text (nothing to replace there).
        _find.Model = target ?? _shown?.Model;
        _find.Open(replace && target is not { IsReadOnly: true }, query);
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
        if (!ReferenceEquals(model.FileSettings, _statusSettings))
        {
            _statusSettings = model.FileSettings;
            ToolTipProperties.SetText(_status.Indentation, ShellText.IndentationTooltip(model));
            ToolTipProperties.SetText(_status.LineEnding, ShellText.LineEndingTooltip(model));
        }
        if (tab.Document is TextDocument document)
        {
            Set(_status.Encoding, ShellText.EncodingName(document.Encoding, document.HasBom));
            Set(_status.LineEnding, ShellText.LineEnding(document.LineEnding));
        }

        var scopes = model.LanguageSession?.ScopeAt(model.Snapshot, Math.Min(model.Selection.Caret, model.Snapshot.Length)) ?? [];
        _breadcrumb.Show(BreadcrumbLayout.Parts(tab.FilePath, _shell.Workspace?.Root, scopes));
        string place = (_shell.Workspace is { } workspace ? Path.GetFileName(workspace.Root) : tab.FilePath is { } file ? Path.GetDirectoryName(file) ?? "" : "")
            + (tab.IsDirty ? " · edited" : tab.FilePath is null ? " · new" : "");
        _titleNarrow.SetNarrowTitle(tab.Title, place.TrimStart(' ', '·'));
    }

    // A breadcrumb part clicked: a folder (or the file's folder) lists its entries in the overlay; a symbol lists its
    // siblings with '@' (the children of its parent, from the editor's symbols of this version).
    private async Task OnBreadcrumbAsync(BreadcrumbPart part)
    {
        try
        {
            switch (part.Kind)
            {
                case BreadcrumbPartKind.Folder when part.Path is { } folder:
                    await _quickOpen.BrowseAsync(folder);
                    ShowOverlay("", keepScope: true);
                    break;
                case BreadcrumbPartKind.File when part.Path is { } file && Path.GetDirectoryName(file) is { } parent:
                    await _quickOpen.BrowseAsync(parent);
                    ShowOverlay("", keepScope: true);
                    break;
                case BreadcrumbPartKind.Symbol when _shown?.Model is { } model:
                    int caret = Math.Min(model.Selection.Caret, model.Snapshot.Length);
                    var symbols = await model.DocumentSymbolsAsync();
                    _quickOpen.EndBrowse();
                    _quickOpen.SymbolsUnder = symbols?.SiblingsParentAt(caret, part.Text);
                    ShowOverlay("@", keepScope: true);
                    break;
            }
        }
#pragma warning disable CA1031 // A click's handler: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the breadcrumb: a part clicked", error);
        }
    }

    // F2 found a name: the rename box over it in the editor it came from (the focused group's).
    private void ShowRename(RenameRequest request)
    {
        CloseRename();
        if (_panes.FirstOrDefault(p => ReferenceEquals(p.Shown?.Model, request.Editor)) is not { } pane)
        {
            _shell.CancelRename();
            return;
        }

        var model = request.Editor;
        var point = model.PointFromPosition(request.Target.Start);
        double x = Math.Max(0, point.X - model.ScrollX - 8);
        double y = Math.Max(0, point.Y - model.ScrollY - 6);
        var box = new RenameOverlay(request, _palette) { Margin = new Thickness(x, y, 0, 0), ZIndex = 10 };
        box.Committed += (_, name) =>
        {
            CloseRename();
            _ = CommitRenameAsync(request, name);
        };
        box.Cancelled += (_, _) =>
        {
            CloseRename();
            _shell.CancelRename();
            _shown?.Focus();
        };
        _rename = box;
        pane.Stack.Add(box);
        _ = ShellMotion.ShowAsync(box, -4);
        Dispatcher.DispatchLabelled("rename: focus the box", box.FocusInput);
    }

    private void CloseRename()
    {
        if (_rename is { } box)
        {
            _rename = null;
            (box.Parent as Layout)?.Remove(box);
        }
    }

    private async Task CommitRenameAsync(RenameRequest request, string name)
    {
        try
        {
            _shown?.Focus();
            await _shell.CommitRenameAsync(request, name);
        }
#pragma warning disable CA1031 // A rename started from the box: nothing may leave it (the owner's note 20).
        catch (Exception error)
#pragma warning restore CA1031
        {
            CallbackGuard.Report("the editor: rename", error);
        }
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
