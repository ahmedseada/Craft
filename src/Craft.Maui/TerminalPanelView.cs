// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;
using Craft.Editor.Terminal;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The bottom panel's Terminal tab (phase T, redesigned in B9, in the design's tokens): the open terminals as tabs (each
/// its profile's icon, a short name, a busy dot while a command runs, a close button on hover; a double-click renames it,
/// a right-click opens its menu), or, from <c>CRAFT_TERMINAL_LIST_FROM</c> terminals, a list at the panel's right
/// (<c>CRAFT_TERMINAL_TABS</c>); "+" opens the default profile and its chevron the profiles' menu ("New PowerShell 7",
/// …, "Set default profile…"); the active split group's terminals side by side; with none open, an empty-state card with
/// a primary "New terminal" and the profiles. A plug-in's read-only output tab is marked so.
/// </summary>
public sealed class TerminalPanelView : ContentView, IDisposable
{
    private const double HeaderHeight = 32;
    private const double ListWidth = 200;
    private readonly TerminalManager _manager;
    private readonly Craft.Editor.EditorHost _host;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 0, Padding = new Thickness(6, 0, 0, 0), VerticalOptions = LayoutOptions.Fill };
    private readonly HorizontalStackLayout _actions = new() { Spacing = 2, Padding = new Thickness(6, 0), VerticalOptions = LayoutOptions.Center };
    private readonly ScrollView _tabScroll;
    private readonly Grid _panes = new();
    private readonly VerticalStackLayout _list = new() { Spacing = 1, Padding = new Thickness(4, 6) };
    private readonly ScrollView _listScroll;
    private readonly BoxView _listLine = new() { WidthRequest = 1 };
    private readonly Grid _body = new() { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)] };
    private readonly EmptyStateView _empty;
    private readonly ContentView _emptyHost = new();
    private readonly List<TerminalView> _views = [];
    private IDispatcherTimer? _busyTimer;
    private ShellPalette _palette;
    private string _shownSignature = "";
    private string _paneSignature = "";

    /// <summary>The tab over <paramref name="manager"/>; <paramref name="host"/> names the keys in effect for its tooltips.</summary>
    public TerminalPanelView(TerminalManager manager, ShellPalette palette, Craft.Editor.EditorHost host)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(host);
        _manager = manager;
        _palette = palette;
        _host = host;
        _tabScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _tabs };
        _listScroll = new ScrollView { Content = _list, WidthRequest = ListWidth };
        _empty = new EmptyStateView(palette) { MaximumWidthRequest = 460, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _empty.ActionChosen += (_, action) =>
        {
            if (action.CommandId is { } command)
            {
                _ = host.ExecuteCommandAsync(command);
            }
        };
        _emptyHost.Content = new ScrollView { Content = _empty };
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], HeightRequest = HeaderHeight };
        header.Add(_tabScroll, 0, 0);
        header.Add(_actions, 1, 0);
        _body.Add(_panes, 0, 0);
        _body.Add(_listLine, 1, 0);
        _body.Add(_listScroll, 2, 0);
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(header, 0, 0);
        grid.Add(_body, 0, 1);
        grid.Add(_emptyHost, 0, 1);
        Content = grid;
        manager.Changed += OnChanged;
        Loaded += (_, _) => StartBusyTimer();
        Unloaded += (_, _) => _busyTimer?.Stop();
        Apply(palette);
    }

    /// <summary>
    /// Raised when a menu should open: the terminal's context menu (<see cref="EditorContextMenus.Terminal"/>, on a
    /// terminal) or the profiles' (<see cref="EditorContextMenus.TerminalProfiles"/>, the chevron), at a click's place.
    /// </summary>
    public event EventHandler<(string Menu, ContextMenuTarget Target, TappedEventArgs Where)>? MenuRequested;

    /// <summary>Raised when a file:line link was Ctrl+clicked in one of its terminals (X part 9).</summary>
    public event EventHandler<TerminalLinkClick>? LinkActivated;

    /// <summary>Gives the active terminal the keyboard.</summary>
    public void FocusTerminal()
    {
        foreach (var view in _views)
        {
            if (ReferenceEquals(view.Terminal, _manager.ActiveTerminal))
            {
                view.FocusTerminal();
                return;
            }
        }

        if (_views.Count > 0)
        {
            _views[0].FocusTerminal();
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and the options (padding, cursor, the tabs or the list).</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        foreach (var view in _views)
        {
            view.Apply(palette.Theme, palette.Options);
        }

        _empty.Apply(palette);
        _paneSignature = "";
        _listLine.Color = palette.Border;
        _listScroll.BackgroundColor = palette.Surface;
        _shownSignature = "";
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _manager.Changed -= OnChanged;
        _busyTimer?.Stop();
        foreach (var view in _views)
        {
            view.Dispose();
        }

        _views.Clear();
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    // The busy dots follow the commands: looked at twice a second while the tab is on screen; rebuilt only when one changed.
    private void StartBusyTimer()
    {
        if (_busyTimer is null && Dispatcher is { } dispatcher)
        {
            _busyTimer = dispatcher.CreateTimer();
            _busyTimer.Interval = TimeSpan.FromMilliseconds(500);
            _busyTimer.Tick += (_, _) => UiDispatch.Run("terminal: busy dots", () =>
            {
                if (_manager.Terminals.Count > 0)
                {
                    Rebuild();
                }
            });
        }

        _busyTimer?.Start();
    }

    private void Rebuild()
    {
        var rows = _manager.Rows();
        var options = _palette.Options;
        bool list = TerminalLaunch.ListsTerminals(options, rows.Count);
        var signature = string.Join("\u0001", rows.Select(r => $"{r.Group}:{r.Label}:{r.Active}:{r.Busy}:{r.Ended}:{string.Join(',', r.Terminals.Select(t => t.Id))}"))
            + (list ? "|list" : "|tabs") + "|" + _manager.ActiveTerminal?.Id + "|" + _manager.Profiles.Count;
        if (signature == _shownSignature)
        {
            return;
        }

        _shownSignature = signature;
        BuildActions();
        _tabs.Children.Clear();
        _list.Children.Clear();
        _tabScroll.IsVisible = !list;
        _listScroll.IsVisible = _listLine.IsVisible = list;
        foreach (var row in rows)
        {
            if (list)
            {
                _list.Children.Add(Row(row, listed: true));
            }
            else
            {
                _tabs.Children.Add(Row(row, listed: false));
            }
        }

        ShowGroup();
        bool none = _manager.ActiveTerminal is null;
        _body.IsVisible = !none;
        _emptyHost.IsVisible = none;
        if (none)
        {
            var start = TerminalLaunch.StartFolder(null, options.TerminalWorkingDirectory, _manager.WorkspaceFolder, _manager.HomeFolder);
            _empty.Show(ShellEmptyStates.Terminal(_host, _manager.Profiles, _manager.DefaultProfile.Name, start.Folder, start.Reason));
        }
    }

    // "+" (the default profile), its chevron (the profiles' menu), and split and kill for the active terminal.
    private void BuildActions()
    {
        _actions.Children.Clear();
        var add = IconButton("M12 5v14M5 12h14", ShellTooltips.For(_host, $"New terminal: {_manager.DefaultProfile.Name}", TerminalCommandIds.New));
        add.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(TerminalCommandIds.New);
        var chevron = IconButton("M7 10l5 5 5-5", "Open a terminal with another profile, or set the default");
        chevron.TappedAt += (_, e) => MenuRequested?.Invoke(this, (EditorContextMenus.TerminalProfiles, new ContextMenuTarget(EditorContextMenus.TerminalProfiles), e));
        _actions.Children.Add(add);
        _actions.Children.Add(chevron);
        if (_manager.ActiveTerminal is { IsReadOnly: false })
        {
            var split = IconButton("M4 5h16v14H4zM12 5v14", ShellTooltips.For(_host, "Split the terminal", TerminalCommandIds.Split));
            split.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(TerminalCommandIds.Split);
            _actions.Children.Add(split);
        }

        if (_manager.ActiveTerminal is not null)
        {
            var kill = IconButton("M5 7h14M10 7V5h4v2M7 7l1 12h8l1-12", ShellTooltips.For(_host, "Kill the terminal", TerminalCommandIds.Kill));
            kill.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(TerminalCommandIds.Kill);
            _actions.Children.Add(kill);
        }
    }

    private IconButtonView IconButton(string data, string tooltip)
    {
        var button = new IconButtonView(data, _palette);
        ToolTipProperties.SetText(button, tooltip);
        SemanticProperties.SetDescription(button, tooltip);
        return button;
    }

    // A tab (or a list row): the icon, the short name, the busy dot, the close button shown on hover and on the active one.
    private Grid Row(TerminalTabRow row, bool listed)
    {
        var p = _palette;
        var first = row.Terminals[0];
        var icon = new StrokeIcon(row.Icon, 14, 1.5) { VerticalOptions = LayoutOptions.Center, Color = row.Active ? p.Text : p.Secondary };
        var title = p.Label(row.ReadOnly ? row.Label + " · read-only" : row.Label, row.Active ? p.Text : p.Secondary, p.SmallSize);
        title.VerticalOptions = LayoutOptions.Center;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        if (row.Ended)
        {
            title.Opacity = 0.6;
        }

        var dot = new Ellipse
        {
            WidthRequest = 6,
            HeightRequest = 6,
            Fill = new SolidColorBrush(p.Accent),
            IsVisible = row.Busy,
            VerticalOptions = LayoutOptions.Center,
        };
        ToolTipProperties.SetText(dot, "A command is running");
        var close = IconButton("M7 7l10 10M17 7L7 17", ShellTooltips.For(_host, "Kill the terminal", TerminalCommandIds.Kill));
        close.Opacity = row.Active ? 1 : 0;
        close.Tapped += (_, _) =>
        {
            foreach (var terminal in row.Terminals.Reverse())
            {
                _manager.Close(terminal);
            }
        };
        var content = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(listed ? GridLength.Star : GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 6,
            Padding = new Thickness(listed ? 8 : 10, 0, 2, 0),
        };
        content.Add(icon, 0, 0);
        content.Add(title, 1, 0);
        content.Add(dot, 2, 0);
        content.Add(close, 3, 0);
        var cell = new Grid
        {
            RowDefinitions = listed ? [new RowDefinition(GridLength.Star)] : [new RowDefinition(GridLength.Star), new RowDefinition(new GridLength(2))],
            HeightRequest = listed ? 26 : HeaderHeight,
            MaximumWidthRequest = listed ? ListWidth : 220,
        };
        cell.Add(content, 0, 0);
        if (!listed)
        {
            cell.Add(new BoxView { Color = row.Active ? p.Accent : Colors.Transparent }, 0, 1);
        }

        var states = p.States(cell, listed ? InteractionKind.Row : InteractionKind.Tab);
        states.Label = title;
        states.Selected = row.Active;
        ToolTipProperties.SetText(cell, row.Tooltip);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => close.Opacity = 1;
        hover.PointerExited += (_, _) => close.Opacity = row.Active ? 1 : 0;
        cell.GestureRecognizers.Add(hover);
        var pick = new TapGestureRecognizer();
        pick.Tapped += (_, _) =>
        {
            _manager.Activate(first);
            FocusTerminal();
        };
        cell.GestureRecognizers.Add(pick);
        var rename = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        rename.Tapped += (_, e) => RunOn(first, TerminalCommandIds.Rename);
        cell.GestureRecognizers.Add(rename);
        var context = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        context.Tapped += (_, e) => MenuRequested?.Invoke(this, (EditorContextMenus.Terminal, Target(first), e));
        cell.GestureRecognizers.Add(context);
        return cell;
    }

    private static ContextMenuTarget Target(TerminalInstance terminal) => new(EditorContextMenus.Terminal, terminal.WorkingDirectory, true, terminal);

    // A command run on a given terminal, as its context menu runs it.
    private void RunOn(TerminalInstance terminal, string command)
    {
        if (terminal.IsReadOnly && command == TerminalCommandIds.Rename)
        {
            return;
        }

        EditorContextMenus.Target = Target(terminal);
        try
        {
            _ = _host.ExecuteCommandAsync(command);
        }
        finally
        {
            EditorContextMenus.Target = null;
        }
    }

    // The active split group's terminals, side by side, a one-unit line between them; views kept and reused.
    private void ShowGroup()
    {
        var group = _manager.GroupOf(_manager.ActiveTerminal);
        var panes = string.Join(",", group.Select(t => t.Id));
        if (panes == _paneSignature && _panes.Children.Count > 0)
        {
            return;                                   // the same terminals: the views keep their place and the keyboard
        }

        _paneSignature = panes;
        // At least one view, as the panes below lay out (with no terminal it waits, empty, for the first): the panel's
        // first build (its constructor's Apply, before any terminal exists) read a view it had not made.
        while (_views.Count < Math.Max(1, group.Count))
        {
            var view = new TerminalView(_manager);
            view.Apply(_palette.Theme, _palette.Options);
            view.Activated += (_, terminal) => _manager.Activate(terminal);
            view.ContextRequested += (_, e) => MenuRequested?.Invoke(this, (EditorContextMenus.Terminal, Target(e.Terminal), e.Where));
            view.LinkActivated += (_, link) => LinkActivated?.Invoke(this, link);         // X part 9: Ctrl+click on a file:line link
            _views.Add(view);
        }

        while (_views.Count > Math.Max(1, group.Count))
        {
            var extra = _views[^1];
            _views.RemoveAt(_views.Count - 1);
            extra.Dispose();
        }

        _panes.Children.Clear();
        _panes.ColumnDefinitions.Clear();
        for (int i = 0; i < Math.Max(1, group.Count); i++)
        {
            if (i > 0)
            {
                _panes.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1)));
                _panes.Add(new BoxView { Color = _palette.Border }, _panes.ColumnDefinitions.Count - 1, 0);
            }

            _panes.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var view = _views[i];
            view.Terminal = i < group.Count ? group[i] : null;
            _panes.Add(view, _panes.ColumnDefinitions.Count - 1, 0);
        }
    }
}

/// <summary>A small icon button of the design (a stroke icon on a flat surface, hover and pressed states from the tokens).</summary>
public sealed class IconButtonView : Grid
{
    private readonly StrokeIcon _icon;

    /// <summary>The button of <paramref name="data"/> (SVG path data on a 24 by 24 box).</summary>
    public IconButtonView(string data, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        WidthRequest = 24;
        HeightRequest = 24;
        VerticalOptions = LayoutOptions.Center;
        _icon = new StrokeIcon(data, 14, 1.6) { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Color = palette.Secondary };
        Children.Add(_icon);
        var states = palette.States(this, InteractionKind.Flat);
        states.Icon = _icon;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            Tapped?.Invoke(this, EventArgs.Empty);
            TappedAt?.Invoke(this, e);
        };
        GestureRecognizers.Add(tap);
    }

    /// <summary>Raised on a click.</summary>
    public event EventHandler? Tapped;

    /// <summary>Raised on a click, with where (a menu opens there).</summary>
    public event EventHandler<TappedEventArgs>? TappedAt;
}
