// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Path = System.IO.Path;

namespace Craft.Maui;

/// <summary>
/// An overlay over the page for the shell's popups (a menu, quick open's list): a transparent cover that closes the popup
/// when tapped, and the popup at a place. Hidden, it takes no input.
/// </summary>
public sealed class PopupLayer : AbsoluteLayout
{
    private readonly BoxView _cover = new() { Color = Colors.Transparent };
    private View? _shown;
    private View? _beside;

    /// <summary>An empty, hidden layer.</summary>
    public PopupLayer()
    {
        IsVisible = false;
        InputTransparent = true;
        CascadeInputTransparent = false;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Hide();
        _cover.GestureRecognizers.Add(tap);
        SetLayoutBounds((BindableObject)_cover, new Rect(0, 0, 1, 1));
        SetLayoutFlags((BindableObject)_cover, AbsoluteLayoutFlags.All);
        Children.Add(_cover);
    }

    /// <summary>Whether a popup is shown.</summary>
    public bool IsOpen => _shown is not null;

    /// <summary>The popup shown, or null.</summary>
    public View? Shown => _shown;

    /// <summary>Raised after the popup closed (tapped outside, or <see cref="Hide"/>).</summary>
    public event EventHandler? Closed;

    /// <summary>Shows <paramref name="popup"/> (in place of any other) with its top-left at <paramref name="at"/>, <paramref name="width"/> wide.</summary>
    public void Show(View popup, Point at, double width)
    {
        ArgumentNullException.ThrowIfNull(popup);
        HideBeside();
        if (_shown is not null)
        {
            _shown.SizeChanged -= OnShownSizeChanged;
            Children.Remove(_shown);
        }

        _shown = popup;
        double x = Width > 0 ? Math.Clamp(at.X, 0, Math.Max(0, Width - width)) : at.X;
        SetLayoutBounds((BindableObject)popup, new Rect(x, Math.Max(0, at.Y), width, AbsoluteLayout.AutoSize));
        SetLayoutFlags((BindableObject)popup, AbsoluteLayoutFlags.None);
        // A popup that would run past the window's bottom (a context menu low in the editor) moves up once it is measured.
        popup.SizeChanged += OnShownSizeChanged;
        Children.Add(popup);
        IsVisible = true;
        InputTransparent = false;
    }

    private void OnShownSizeChanged(object? sender, EventArgs e)
    {
        if (sender is View popup && Height > 0 && popup.Height > 0)
        {
            KeepInside(popup);
        }
    }

    private void KeepInside(View popup)
    {
        if (Height <= 0 || popup.Height <= 0)
        {
            return;
        }

        var bounds = GetLayoutBounds((BindableObject)popup);
        if (bounds.Y + popup.Height > Height)
        {
            SetLayoutBounds((BindableObject)popup, bounds with { Y = Math.Max(0, Height - popup.Height - 4) });
        }
    }

    /// <summary>The popup shown beside the main one (a submenu), or null.</summary>
    public View? Beside => _beside;

    /// <summary>
    /// Shows <paramref name="popup"/> beside the shown popup (X part 7: a submenu next to its row): its top
    /// <paramref name="offsetY"/> below the shown popup's top, to its right, else to its left when the window has no room;
    /// it goes with the shown popup.
    /// </summary>
    public void ShowBeside(View popup, double offsetY, double width)
    {
        ArgumentNullException.ThrowIfNull(popup);
        HideBeside();
        if (_shown is not { } shown)
        {
            return;
        }

        var bounds = GetLayoutBounds((BindableObject)shown);
        double right = bounds.X + shown.Width - 4;
        double x = Width > 0 && right + width > Width ? Math.Max(0, bounds.X - width + 4) : right;
        _beside = popup;
        SetLayoutBounds((BindableObject)popup, new Rect(x, Math.Max(0, bounds.Y + offsetY), width, AbsoluteLayout.AutoSize));
        SetLayoutFlags((BindableObject)popup, AbsoluteLayoutFlags.None);
        popup.SizeChanged += OnShownSizeChanged;
        Children.Add(popup);
    }

    /// <summary>Takes the popup beside the main one away.</summary>
    public void HideBeside()
    {
        if (_beside is not { } beside)
        {
            return;
        }

        beside.SizeChanged -= OnShownSizeChanged;
        Children.Remove(beside);
        _beside = null;
    }

    /// <summary>Closes the popup.</summary>
    public void Hide()
    {
        if (_shown is null)
        {
            return;
        }

        HideBeside();
        _shown.SizeChanged -= OnShownSizeChanged;
        Children.Remove(_shown);
        _shown = null;
        IsVisible = false;
        InputTransparent = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Where <paramref name="anchor"/>'s bottom-left is in this layer: its bounds added up its parents to the page that
    /// holds this layer. An anchor outside the page (in the window's caption) keeps its x and gives the page's top.
    /// </summary>
    public Point Below(VisualElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        double x = 0, y = anchor.Height;
        bool inPage = false;
        Element? page = Parent;
        while (page is not null and not Page)
        {
            page = page.Parent;
        }

        for (Element? element = anchor; element is not null; element = element.Parent)
        {
            if (ReferenceEquals(element, page))
            {
                inPage = true;
                break;
            }

            if (element is VisualElement visual)
            {
                x += visual.X + visual.TranslationX;
                y += visual.Y + visual.TranslationY;
            }
        }

        return inPage ? new Point(x, y) : new Point(x, 0);
    }
}

/// <summary>
/// The title bar's row (docs/design/Main.dc.html): the logo and "Craft", the menus (<see cref="EditorMenus"/>), the search
/// box (quick open; the model chip is the composer's since the owner's note 11, <see cref="ModelChip"/>). Narrow, it is the phone's top bar (docs/design/PhoneEditor.dc.html): the files
/// button, the file's title and place, undo, and the commands, each a touch target. It raises what the user asks; the page acts.
/// </summary>
public sealed class TitleBarView : ContentView
{
    private readonly Grid _wide = new() { ColumnSpacing = 16, Padding = new Thickness(12, 0, 8, 0) };
    private readonly Grid _narrow = new() { ColumnSpacing = 4, Padding = new Thickness(4, 0) };
    private readonly GraphicsView _logo;
    private readonly LogoDrawable _logoDrawable = new();
    private readonly Label _name;
    private readonly HorizontalStackLayout _menus = new() { Spacing = 4 };
    private readonly CraftInput _searchBox;
    private readonly Label _narrowTitle;
    private readonly Label _narrowPlace;
    private readonly List<Button> _narrowButtons = [];
    private readonly List<(Button Button, string Name, string Command)> _narrowTips = [];
    private ShellPalette _palette;
    private readonly TypingDebounce _typing;

    /// <summary>The bar in <paramref name="palette"/>'s colours.</summary>
    public TitleBarView(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _logo = new GraphicsView { Drawable = _logoDrawable, WidthRequest = 20, HeightRequest = 20, InputTransparent = true };
        _name = palette.Label("Craft", weight: FontWeight.SemiBold);
        Search = palette.Input("Search files, symbols and commands  (Ctrl+P)", 26);
        // Debounced (CRAFT_SEARCH_DELAY_MS); Enter runs a waiting search before it picks.
        _typing = new TypingDebounce(this, () => _palette.Options.SearchDelayMilliseconds, () => SearchChanged?.Invoke(this, Search.Text ?? ""));
        Search.TextChanged += (_, _) => _typing.Restart();
        Search.Completed += (_, _) =>
        {
            _typing.Flush();
            SearchSubmitted?.Invoke(this, EventArgs.Empty);
        };
        Search.Focused += (_, _) => SearchFocused?.Invoke(this, EventArgs.Empty);
        _searchBox = palette.Field(Search);
        _searchBox.MaximumWidthRequest = 460;
        _searchBox.HorizontalOptions = LayoutOptions.Fill;
        _searchBox.VerticalOptions = LayoutOptions.Center;
        _searchBox.HeightRequest = 28;

        _wide.ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)];
        _wide.Add(new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { _logo, _name } }, 0, 0);
        _wide.Add(_menus, 1, 0);
        _wide.Add(_searchBox, 2, 0);

        _narrowTitle = palette.Label("", weight: FontWeight.SemiBold);
        _narrowPlace = palette.Label("", palette.Secondary, palette.SmallSize);
        _narrow.ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)];
        _narrow.Add(NarrowButton("☰", "Files", ShellCommandIds.ToggleTree, () => NarrowFilesTapped?.Invoke(this, EventArgs.Empty)), 0, 0);
        _narrow.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { _narrowTitle, _narrowPlace } }, 1, 0);
        _narrow.Add(NarrowButton("↶", "Undo", "craft.undo", () => NarrowUndoTapped?.Invoke(this, EventArgs.Empty)), 2, 0);
        _narrow.Add(NarrowButton("⋯", "Commands", ShellCommandIds.CommandPalette, () => NarrowCommandsTapped?.Invoke(this, EventArgs.Empty)), 3, 0);
        Content = _wide;
        HeightRequest = 40;
        RebuildMenus();
        Apply(palette);
    }

    /// <summary>The search box.</summary>
    public Entry Search { get; }

    /// <summary>Raised with the menu's name and its button when a menu is clicked.</summary>
    public event EventHandler<(string Menu, View Anchor)>? MenuRequested;

    /// <summary>Raised when the search text changes.</summary>
    public event EventHandler<string>? SearchChanged;

    /// <summary>Raised on Enter in the search box.</summary>
    public event EventHandler? SearchSubmitted;

    /// <summary>Raised when the search box takes the focus.</summary>
    public event EventHandler? SearchFocused;

    /// <summary>Narrow: the files button.</summary>
    public event EventHandler? NarrowFilesTapped;

    /// <summary>Narrow: undo.</summary>
    public event EventHandler? NarrowUndoTapped;

    /// <summary>Narrow: the commands.</summary>
    public event EventHandler? NarrowCommandsTapped;

    /// <summary>The wide row's view (the page puts it in the window's caption or in its own first row).</summary>
    public View WideRow => _wide;

    /// <summary>Shows the narrow (phone) bar or the wide row.</summary>
    public void SetNarrow(bool narrow)
    {
        Content = narrow ? _narrow : _wide;
        HeightRequest = narrow ? Math.Max(56, _palette.Options.TouchTarget + 8) : 40;
    }

    /// <summary>Narrow: the active file's title and its place ("Craft.Plugins.Tools · edited").</summary>
    public void SetNarrowTitle(string title, string place)
    {
        _narrowTitle.Text = title;
        _narrowPlace.Text = place;
    }

    /// <summary>Makes the menus' buttons again (a plug-in added a menu).</summary>
    public void RebuildMenus()
    {
        _menus.Children.Clear();
        foreach (var menu in EditorMenus.Menus())
        {
            var button = _palette.Flat(menu, _palette.Secondary, 28);
            button.Padding = new Thickness(8, 0);
            button.VerticalOptions = LayoutOptions.Center;
            string name = menu;
            button.Clicked += (_, _) => MenuRequested?.Invoke(this, (name, button));
            _menus.Children.Add(button);
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and fonts.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Ground;
        _logoDrawable.Accent = palette.Accent;
        _logo.Invalidate();
        _name.TextColor = palette.Text;
        _name.FontFamily = palette.Font(FontWeight.SemiBold);
        _searchBox.Apply(palette);
        _narrowTitle.TextColor = palette.Text;
        _narrowPlace.TextColor = palette.Secondary;
        foreach (var button in _narrowButtons)
        {
            button.TextColor = palette.Text;
        }

        RebuildMenus();
    }

    /// <summary>
    /// The tooltips of the bar's icon-only buttons and the search box's hint, each the action and its keys in effect
    /// (<paramref name="tip"/>: <see cref="ShellTooltips.For"/>); called again when the bindings change.
    /// </summary>
    public void SetTooltips(Func<string, string, string> tip)
    {
        ArgumentNullException.ThrowIfNull(tip);
        Search.Placeholder = tip("Search files, symbols and commands", ShellCommandIds.QuickOpen);
        foreach (var (button, name, command) in _narrowTips)
        {
            ToolTipProperties.SetText(button, tip(name, command));
        }
    }

    private Button NarrowButton(string text, string name, string command, Action tapped)
    {
        double size = Math.Max(44, _palette.Options.TouchTarget + 4);
        var button = _palette.Flat(text, _palette.Text, size, _palette.FontSize + 7);
        button.WidthRequest = size;
        button.Padding = new Thickness(0);
        SemanticProperties.SetDescription(button, name);
        ToolTipProperties.SetText(button, name);
        button.Clicked += (_, _) => tapped();
        _narrowButtons.Add(button);
        _narrowTips.Add((button, name, command));
        return button;
    }
}

/// <summary>
/// The model chip (the owner's note 11): the chat model, its device and its state at the chat's and the agent's composer
/// (docs/design/Main.dc.html's chip and the chat header's "model · local"): a dot in the success colour when a model is
/// set and ready, the warning's while it loads, the error's when it failed, the secondary's with none; a tap opens the
/// picker of the models and devices. Its hover and press show on the raised fills.
/// </summary>
public sealed class ModelChip : Border
{
    private readonly Ellipse _dot = new() { WidthRequest = 8, HeightRequest = 8, VerticalOptions = LayoutOptions.Center };
    private readonly Label _text;
    private readonly Label _caret;
    private ShellPalette _palette;
    private ModelChipState _state;
    private bool _hover;

    /// <summary>A chip in <paramref name="palette"/>'s colours.</summary>
    public ModelChip(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _text = palette.Label("no model", size: palette.SmallSize);
        _text.MaximumWidthRequest = 260;
        _caret = palette.Label("▾", palette.Secondary, palette.SmallSize);
        Content = new HorizontalStackLayout { Spacing = 6, Children = { _dot, _text, _caret } };
        StrokeShape = new RoundRectangle { CornerRadius = 12 };
        StrokeThickness = 1;
        Padding = new Thickness(10, 0);
        HeightRequest = 24;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHover(true);
        pointer.PointerExited += (_, _) => SetHover(false);
        GestureRecognizers.Add(pointer);
        SemanticProperties.SetDescription(this, "The model: pick a model and its device");
        ToolTipProperties.SetText(this, "The model: pick a model and its device");
        Apply(palette);
    }

    /// <summary>Raised when the chip is tapped.</summary>
    public event EventHandler? Tapped;

    /// <summary>Shows <paramref name="text"/> ("Qwen2.5-Coder-7B-Instruct · cuda:0") in <paramref name="state"/>, with <paramref name="tooltip"/>.</summary>
    public void Set(string text, ModelChipState state, string? tooltip)
    {
        _text.Text = text;
        _state = state;
        ToolTipProperties.SetText(this, tooltip ?? "The model: pick a model and its device");
        Paint();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _text.FontFamily = palette.Font();
        _caret.TextColor = palette.Secondary;
        Paint();
    }

    private void SetHover(bool hover)
    {
        _hover = hover;
        Paint();
    }

    private void Paint()
    {
        var p = _palette;
        BackgroundColor = _hover ? p.RaisedHigh : p.Raised;
        Stroke = _hover ? p.BorderStrong : p.Border;
        _text.TextColor = p.Text;
        _dot.Fill = new SolidColorBrush(_state switch
        {
            ModelChipState.Ready => p.Success,
            ModelChipState.Loading => p.Warning,
            ModelChipState.Problem => p.Error,
            _ => p.Secondary,
        });
    }
}

/// <summary>What the model chip's dot says.</summary>
public enum ModelChipState
{
    /// <summary>No model set.</summary>
    None,

    /// <summary>A model is set (loaded, or loaded on first use).</summary>
    Ready,

    /// <summary>It is loading.</summary>
    Loading,

    /// <summary>Its load failed (or its family is not registered).</summary>
    Problem,
}

/// <summary>
/// The activity rail (docs/design/Main.dc.html): the registered rail items (<see cref="EditorRailItems"/>) whose commands
/// are registered, as 44-unit icon buttons, the bottom ones (Settings) at the foot; the active one on the raised fill with
/// the accent's line at its left.
/// </summary>
public sealed class RailView : ContentView
{
    private readonly EditorHost _host;
    private readonly VerticalStackLayout _top = new() { Spacing = 4, Padding = new Thickness(0, 8, 0, 0) };
    private readonly VerticalStackLayout _bottom = new() { Spacing = 4, Padding = new Thickness(0, 0, 0, 8) };
    private ShellPalette _palette;
    private string? _active;

    /// <summary>The rail of <paramref name="host"/>'s commands.</summary>
    public RailView(EditorHost host, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(palette);
        _host = host;
        _palette = palette;
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)], WidthRequest = 48 };
        grid.Add(_top, 0, 0);
        grid.Add(_bottom, 0, 1);
        Content = grid;
        EditorRailItems.Changed += OnItemsChanged;
        host.KeyBindingsChanged += OnItemsChanged;               // a remap shows in the tooltips at once
        Rebuild();
    }

    /// <summary>
    /// The badge of a rail item by its id (X part 8: the Source Control view's count of changes; the shell's
    /// <c>RailBadge</c>), or null for none; call <see cref="Rebuild"/> when a badge changed.
    /// </summary>
    public Func<string, string?>? Badge { get; set; }

    /// <summary>The id of the item shown active (the explorer while the tree shows), or null.</summary>
    public string? Active
    {
        get => _active;
        set
        {
            if (!string.Equals(_active, value, StringComparison.OrdinalIgnoreCase))
            {
                _active = value;
                Rebuild();
            }
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Rebuild();
    }

    /// <summary>Makes the buttons again (a command was registered or removed).</summary>
    public void Rebuild()
    {
        BackgroundColor = _palette.Ground;
        _top.Children.Clear();
        _bottom.Children.Clear();
        foreach (var item in EditorRailItems.All)
        {
            if (EditorCommands.Find(item.CommandId) is null)
            {
                continue;
            }

            (item.AtBottom ? _bottom : _top).Children.Add(Button(item));
        }
    }

    private Grid Button(RailItem item)
    {
        bool active = string.Equals(item.Id, _active, StringComparison.OrdinalIgnoreCase);
        // The icon (a stroked Path) and the accent line are input-transparent, so a click anywhere on the button reaches the
        // cell's tap, never a child shape that swallows it (the owner's note 7: the explorer button did nothing).
        var icon = new StrokeIcon(item.Icon) { Color = active ? _palette.Text : _palette.Secondary, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
        var cell = new Grid { WidthRequest = 44, HeightRequest = 44, HorizontalOptions = LayoutOptions.Center };
        cell.Add(icon);
        if (active)
        {
            cell.Add(new BoxView { Color = _palette.Accent, WidthRequest = 2, HorizontalOptions = LayoutOptions.Start, InputTransparent = true });
        }

        // X part 8: a count on the item (the changes of the Source Control view), a pill at the icon's top right.
        string? badge = Badge?.Invoke(item.Id);
        if (badge is { Length: > 0 })
        {
            var count = _palette.Label(badge, _palette.AccentInk, _palette.CaptionSize - 1, FontWeight.SemiBold);
            count.HorizontalTextAlignment = TextAlignment.Center;
            count.VerticalTextAlignment = TextAlignment.Center;
            cell.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                BackgroundColor = _palette.Accent,
                MinimumWidthRequest = 16,
                HeightRequest = 16,
                Padding = new Thickness(4, 0),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 5, 4, 0),
                InputTransparent = true,
                Content = count,
            });
        }

        // Phase X: hover and pressed from the theme (the icon follows), and the tooltip names the action and its keys in effect.
        var states = _palette.States(cell, InteractionKind.Rail);
        states.Icon = icon;
        states.Selected = active;
        ToolTipProperties.SetText(cell, ShellTooltips.For(_host, item.Title, item.CommandId) + (badge is { Length: > 0 } ? $" ({badge})" : ""));
        SemanticProperties.SetDescription(cell, badge is { Length: > 0 } ? $"{item.Title}, {badge}" : item.Title);
        var tap = new TapGestureRecognizer();
        string command = item.CommandId;
        tap.Tapped += (_, _) => _ = _host.ExecuteCommandAsync(command);
        cell.GestureRecognizers.Add(tap);
        return cell;
    }

    private void OnItemsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("shell: rebuild the status items", Rebuild);
        }
        else
        {
            Rebuild();
        }
    }
}

/// <summary>
/// The explorer (docs/design/Main.dc.html): the folder's name in capitals and its tree. B10: the semantic layer's card
/// that sat at its foot is gone; its facts are the workspace's notification and the language item's tooltip.
/// </summary>
public sealed class ExplorerView : ContentView
{
    private readonly Label _header;

    /// <summary>The explorer over <paramref name="tree"/>.</summary>
    public ExplorerView(FolderTreeView tree, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(palette);
        Tree = tree;
        _header = palette.Label("", palette.Secondary, palette.CaptionSize, FontWeight.SemiBold);
        _header.CharacterSpacing = 0.6;
        _header.Margin = new Thickness(14, 10, 14, 8);
        // X part 7: the folder's name takes a right-click: the workspace's menu (New file, Paste, Collapse all, Refresh…).
        var rootMenu = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        rootMenu.Tapped += (_, e) => tree.RequestRootMenu(e);
        _header.GestureRecognizers.Add(rootMenu);
        Empty = new EmptyStateView(palette) { IsVisible = false };
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(_header, 0, 0);
        grid.Add(tree, 0, 1);
        grid.Add(new ScrollView { Content = Empty, IsVisible = false }, 0, 1);
        Content = grid;
        Apply(palette);
    }

    /// <summary>The tree.</summary>
    public FolderTreeView Tree { get; }

    /// <summary>What the explorer says with no folder open (phase X: Open folder, the recent folders); the page handles its actions.</summary>
    public EmptyStateView Empty { get; }

    /// <summary>Shows <paramref name="state"/> in the tree's place, or the tree when null.</summary>
    public void SetEmpty(EmptyState? state)
    {
        if (state is not null)
        {
            Empty.Show(state);
        }

        Empty.IsVisible = state is not null;
        if (Empty.Parent is View scroll)
        {
            scroll.IsVisible = state is not null;
        }

        Tree.IsVisible = state is null;
    }

    /// <summary>The folder's name (shown in capitals), or null.</summary>
    public void SetFolder(string? root)
    {
        _header.Text = root is null ? "NO FOLDER" : Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).ToUpperInvariant();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        BackgroundColor = palette.Surface;
        _header.TextColor = palette.Secondary;
        Tree.Palette = palette;
        Tree.TextColor = palette.Text;
        Tree.SelectedColor = palette.Selection;
        Empty.Apply(palette);
        Tree.FontFamily = palette.Font();
    }
}

/// <summary>
/// The breadcrumb (docs/design/Main.dc.html; the X restart's piece 3b): the file's folders within the workspace, the file and
/// the symbols around the caret, separated by '›', each part clickable (<see cref="PartClicked"/>: a folder lists its
/// entries, a symbol its siblings). When the parts are wider than the bar the middle ones fold into "…"
/// (<see cref="BreadcrumbLayout.Fold"/>) by the widths the labels measured when laid out (kept per text). Redrawn only
/// when its parts or its width change.
/// </summary>
public sealed class BreadcrumbView : ContentView
{
    private const double Gap = 6;
    private const double SidePadding = 16;
    private const string Separator = "›";
    private readonly HorizontalStackLayout _parts = new() { Spacing = Gap, Padding = new Thickness(SidePadding, 0), VerticalOptions = LayoutOptions.Center };
    private readonly Dictionary<string, double> _widths = new(StringComparer.Ordinal);
    private readonly List<(View Label, string Key)> _labels = [];
    private IReadOnlyList<BreadcrumbPart> _all = [];
    private ShellPalette _palette;
    private double _width;
    private bool _learning;

    /// <summary>An empty breadcrumb.</summary>
    public BreadcrumbView(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        HeightRequest = 28;
        Content = _parts;
        _parts.SizeChanged += (_, _) => Learn();
    }

    /// <summary>Raised with the part clicked (for "…", the part it stands for: <see cref="BreadcrumbPart.Target"/>).</summary>
    public event EventHandler<BreadcrumbPart>? PartClicked;

    /// <summary>The parts on screen (folded or not).</summary>
    public IReadOnlyList<BreadcrumbPart> Shown { get; private set; } = [];

    /// <summary>Shows <paramref name="parts"/> (the same parts as shown: nothing done).</summary>
    public void Show(IReadOnlyList<BreadcrumbPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.SequenceEqual(_all))
        {
            return;
        }

        _all = parts;
        Arrange();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        _widths.Clear();                                          // the font may have changed: measured again
        Arrange();
    }

    /// <inheritdoc />
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (Math.Abs(width - _width) > 0.5)
        {
            _width = width;
            Arrange();
        }
    }

    // The parts folded to the width when every width is known; else all of them, measured once laid out (Learn).
    private void Arrange()
    {
        double available = _width - (2 * SidePadding);
        if (available > 0 && Widths() is { } widths)
        {
            double separator = _widths.GetValueOrDefault(Separator) + (2 * Gap);
            double ellipsis = _widths.TryGetValue(BreadcrumbLayout.Ellipsis, out var e) ? e : _widths.GetValueOrDefault(Separator) * 2;
            Render(BreadcrumbLayout.Fold(_all, widths, separator, ellipsis, available));
            _learning = false;
            return;
        }

        Render(_all);
        _learning = true;
    }

    // Once the full set is laid out, each label's measured width is kept and the parts folded if they do not fit.
    private void Learn()
    {
        if (!_learning)
        {
            return;
        }

        bool all = true;
        foreach (var (label, key) in _labels)
        {
            if (label.Width > 0)
            {
                _widths[key] = label.Width;
            }
            else
            {
                all = false;
            }
        }

        if (all && _labels.Count > 0)
        {
            _learning = false;
            if (Widths() is not null)
            {
                Arrange();
            }
        }
    }

    private List<double>? Widths()
    {
        if (!_widths.ContainsKey(Separator) && _all.Count > 1)
        {
            return null;
        }

        var widths = new List<double>(_all.Count);
        foreach (var part in _all)
        {
            if (!_widths.TryGetValue(part.Text, out double width))
            {
                return null;
            }

            widths.Add(width);
        }

        return widths;
    }

    private void Render(IReadOnlyList<BreadcrumbPart> parts)
    {
        if (parts.SequenceEqual(Shown) && _parts.Children.Count > 0)
        {
            return;
        }

        Shown = parts;
        _parts.Children.Clear();
        _labels.Clear();
        int lastSymbol = -1;
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            if (parts[i].Kind == BreadcrumbPartKind.Symbol)
            {
                lastSymbol = i;
                break;
            }
        }

        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                var separator = _palette.Label(Separator, _palette.Secondary, _palette.SmallSize);
                _labels.Add((separator, Separator));
                _parts.Children.Add(separator);
            }

            var part = parts[i];
            var color = part.Kind == BreadcrumbPartKind.Symbol && part.Scope == ScopeKind.Method ? _palette.Method
                : i == lastSymbol || (lastSymbol < 0 && part.Kind == BreadcrumbPartKind.File) ? _palette.Text
                : _palette.Secondary;
            var label = _palette.Label(part.Text, color, _palette.SmallSize);
            label.Padding = new Thickness(2, 1);
            View shown = label;
            if (part.Kind == BreadcrumbPartKind.File)
            {
                // W: the file's drawn icon before its name (the tree's and the tab's), measured with it.
                var icon = FileIconView.Of(FileIcons.For(part.Path ?? part.Text), _palette, 14);
                shown = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { icon, label } };
            }

            _labels.Add((shown, part.Text));
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => PartClicked?.Invoke(this, part.Target);
            shown.GestureRecognizers.Add(tap);
            ToolTipProperties.SetText(label, part.Kind switch
            {
                BreadcrumbPartKind.Ellipsis => string.Join(" › ", part.Folded.Select(f => f.Text)),
                BreadcrumbPartKind.Symbol => $"{SymbolKinds.Word(part.Scope)} {part.Text}: its siblings ('@')",
                _ => part.Path is { } path ? path + ": its entries" : part.Text,
            });
            _parts.Children.Add(shown);
        }
    }
}

/// <summary>A tab of the bottom panel.</summary>
public enum BottomTab
{
    /// <summary>The active document's problems.</summary>
    Problems,

    /// <summary>The terminals (phase T).</summary>
    Terminal,

    /// <summary>The host's log.</summary>
    Output,

    /// <summary>The measures recorded through the host.</summary>
    Telemetry,

    /// <summary>The TODO list (X part 9): the tags in the workspace's comments.</summary>
    Todo,
}

/// <summary>
/// The bottom panel (docs/design/Main.dc.html; the owner's note 26): the tabs Problems (with the count), Terminal (the
/// terminals, <see cref="TerminalContent"/>), Output and Idrak telemetry, the active one in the text colour over the
/// accent's line, each tab's tools at the strip's right (a filter, Output's "Follow", Copy all). Output is one read-only
/// text in Craft's own editor (<see cref="LogSurface"/>: a drag selects across lines, Ctrl+A, Ctrl+C, Ctrl+F and the
/// context menu's Copy work, only the visible lines are drawn, new lines appended a batch per dispatch and followed
/// unless the user scrolled up); the telemetry a live metrics table (<see cref="MetricsTable"/>) and Problems plain rows
/// grouped by file (<see cref="ProblemsList"/>), both drawn on one surface each (<see cref="PanelListView"/>): no checkbox,
/// no row chrome. Phase U2: the chevron at the strip's end asks the shell to collapse it (<see cref="CollapseRequested"/>,
/// as Ctrl+J does); its height is its <see cref="SplitPanel"/>'s.
/// </summary>
public sealed class BottomPanelView : ContentView, IDisposable
{
    private readonly EditorHost _host;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 18, Padding = new Thickness(16, 0) };
    private readonly HorizontalStackLayout _tools = new() { Spacing = 6, Padding = new Thickness(0, 0, 4, 0), VerticalOptions = LayoutOptions.Center };
    private readonly ContentView _body = new();
    private readonly Button _collapse;
    private readonly Button _copyAll;
    private readonly Button _follow;
    private readonly Entry _filter;
    private readonly CraftInput _filterField;
    private readonly EmptyStateView _problemsEmpty;
    private readonly LogSurface _log;
    private readonly EditorView _output;
    private readonly MetricsTable _metrics;
    private readonly PanelListView _metricsView;
    private readonly ProblemsList _problems;
    private readonly PanelListView _problemsView;
    private readonly Grid _problemsBody = new();
    private readonly Label _terminal;
    private readonly Label _noMeasures;
    private readonly Action<ProblemItem> _problemChosen;
    private View? _terminalView;
    private PanelView? _todoView;
    private Craft.Editor.Tools.TodoListView? _todo;
    private ShellPalette _palette;
    private string _outputFilter = "";
    private string _metricsFilter = "";
    private int _shownCount = -1;
    private bool _filling;

    /// <summary>The panel over <paramref name="host"/>; a problem's row opens its file and moves the caret through <paramref name="problemChosen"/>.</summary>
    public BottomPanelView(EditorHost host, ShellPalette palette, Action<ProblemItem> problemChosen)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(problemChosen);
        _host = host;
        _palette = palette;
        _problemChosen = problemChosen;

        // Output: the log in Craft's editor, read-only, in the code font on the panel's surface, no line numbers.
        _log = new LogSurface(() => _host.Options.HostLogLines);
        _output = new EditorView
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            Theme = LogClasses.Theme(palette.Theme),
            Options = OutputOptions(palette.Options),
            KeyHandler = new ReadOnlySurfaceKeys(() => WindowKeys),
        };
        _output.ModelChanged += (_, _) =>
        {
            _log.Attach(_output.Model);
            if (_output.Model is { } model)
            {
                // X part 9: file:line links in Output, a click opens the place (the page resolves and opens it).
                model.DetectsLinks = true;
                model.LinkActivated += (_, e) => LinkActivated?.Invoke(this, e);
            }
        };
        _output.Open(_log.Shown, LogSurface.Language);
        _output.FindRequested += (_, e) =>
        {
            if (_output.Model is { } model)
            {
                FindRequested?.Invoke(this, new BottomFindEventArgs(model, e.InitialQuery));
            }
        };
        _output.ContextRequested += (_, e) =>
        {
            if (_output.Model is { } model)
            {
                ContextRequested?.Invoke(this, new BottomContextEventArgs(model, e));
            }
        };
        _log.ShownChanged += (_, _) => _output.Open(_log.Shown, LogSurface.Language);
        _log.FollowingChanged += (_, _) => Dispatcher.DispatchLabelled("output: the follow toggle", UpdateTools);
        foreach (var entry in host.RecentLog)
        {
            _log.Post(LogText.Line(entry));
        }

        _log.Flush();
        host.Logged += OnLogged;

        // Idrak telemetry: the live metrics table.
        _metrics = new MetricsTable(host, palette.Options);
        _metrics.Recorded += OnMeasured;
        _metricsView = new PanelListView(_metrics, palette.Options, palette.Theme);
        _noMeasures = palette.Label("Nothing recorded yet: the plug-ins' and the library's measures (IEditorHost.Record) show here.", palette.Secondary);
        _noMeasures.Margin = new Thickness(16, 8);

        // Problems: plain rows grouped by file.
        _problems = new ProblemsList();
        _problems.Chosen += (_, item) => _problemChosen(item);
        _problems.ActionRequested += (_, request) => CodeActionRequested?.Invoke(request.Item, request.Kind);
        _problemsView = new PanelListView(_problems, palette.Options, palette.Theme);
        _problemsEmpty = new EmptyStateView(palette);
        _problemsEmpty.ActionChosen += (_, action) =>
        {
            if (action.CommandId is { } command)
            {
                _ = _host.ExecuteCommandAsync(command);
            }
        };
        _problemsEmpty.Show(ShellEmptyStates.Problems(null, null));
        _problemsBody.Add(_problemsView);
        _problemsBody.Add(_problemsEmpty);

        _terminal = palette.Label("No terminal host is available here.", palette.Secondary);
        _terminal.Margin = new Thickness(16, 8);

        // The strip's tools: the filter, Output's follow toggle, Copy all, the chevron.
        _filter = palette.Input("Filter", 24);
        _filter.FontSize = palette.SmallSize;
        _filter.TextChanged += OnFilterChanged;
        _filterField = palette.Field(_filter);
        _filterField.WidthRequest = 180;
        _filterField.VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(_filter, "Show only the lines that hold this text");
        _follow = palette.Flat("Follow", palette.Secondary, 24, palette.SmallSize);
        ToolTipProperties.SetText(_follow, "Follow the end as lines come (scrolling up stops it, the end starts it again)");
        _follow.Clicked += (_, _) => _log.Following = !_log.Following;
        _copyAll = palette.Flat("Copy all", palette.Secondary, 24, palette.SmallSize);
        _copyAll.Padding = new Thickness(10, 0);
        SemanticProperties.SetDescription(_copyAll, "Copy the whole tab to the clipboard");
        _copyAll.Clicked += (_, _) => _ = CopyAsync(AllText());
        _tools.Children.Add(_filterField);
        _tools.Children.Add(_follow);
        _tools.Children.Add(_copyAll);
        _collapse = palette.Flat("⌄", palette.Secondary, 28, palette.FontSize + 2);
        _collapse.WidthRequest = 32;
        _collapse.Padding = new Thickness(0);
        ToolTipProperties.SetText(_collapse, ShellTooltips.For(host, "Hide the panel", ShellCommandIds.TogglePanel));
        SemanticProperties.SetDescription(_collapse, "Hide the bottom panel");
        _collapse.Clicked += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
        host.KeyBindingsChanged += OnKeyBindingsChanged;

        var strip = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)] };
        strip.Add(_tabs, 0, 0);
        strip.Add(_tools, 1, 0);
        strip.Add(_collapse, 2, 0);
        var grid = new Grid { RowDefinitions = [new RowDefinition(new GridLength(32)), new RowDefinition(GridLength.Star)] };
        grid.Add(strip, 0, 0);
        grid.Add(_body, 0, 1);
        Content = grid;
        Apply(palette);
        Select(BottomTab.Problems);
    }

    /// <summary>The tab shown.</summary>
    public BottomTab Tab { get; private set; }

    /// <summary>Where a key Output or a list does not take goes (the window's keys), or null.</summary>
    public Craft.Editor.Abstractions.IEditorKeyHandler? WindowKeys
    {
        get => _metricsView.WindowKeys;
        set
        {
            _metricsView.WindowKeys = value;
            _problemsView.WindowKeys = value;
        }
    }

    /// <summary>Raised when a file:line link in Output was clicked (X part 9).</summary>
    public event EventHandler<EditorLinkEventArgs>? LinkActivated;

    /// <summary>Output's log (the tests' and the page's way to it).</summary>
    public LogSurface Log => _log;

    /// <summary>Output's editor (read-only), or null before its first model.</summary>
    public EditorModel? OutputModel => _output.Model;

    /// <summary>Raised when the chevron is clicked: the shell collapses the panel.</summary>
    public event EventHandler? CollapseRequested;

    /// <summary>Raised on Ctrl+F in Output: the page's find searches Output's text.</summary>
    public event EventHandler<BottomFindEventArgs>? FindRequested;

    /// <summary>Raised on a right-click in Output: the page opens the read-only text's menu (<see cref="EditorContextMenus.ReadOnlyText"/>).</summary>
    public event EventHandler<BottomContextEventArgs>? ContextRequested;

    /// <summary>
    /// A Problems row's Fix or Explain was pressed: its problem and the code action kind (<see cref="CodeActionKinds.Fix"/>,
    /// <see cref="CodeActionKinds.Explain"/>); the page runs the registered providers' action.
    /// </summary>
    public Action<ProblemItem, string>? CodeActionRequested { get; set; }

    /// <summary>The Terminal tab's content (phase T's terminals); null shows that there is none.</summary>
    public View? TerminalContent
    {
        get => _terminalView;
        set
        {
            _terminalView = value;
            if (Tab == BottomTab.Terminal)
            {
                _body.Content = value ?? _terminal;
            }
        }
    }

    /// <summary>The TODO tab's list (X part 9): drawn from its blocks; its count in the tab's title.</summary>
    public Craft.Editor.Tools.TodoListView? Todo
    {
        get => _todo;
        set
        {
            if (_todo is not null)
            {
                _todo.Changed -= OnTodoChanged;
            }

            _todoView?.Dispose();
            _todo = value;
            _todoView = null;
            if (value is not null)
            {
                _todoView = new PanelView(_host, value);
                _todoView.SetPalette(_palette);
                value.Changed += OnTodoChanged;
            }

            if (Tab == BottomTab.Todo)
            {
                Select(BottomTab.Todo);
            }

            RebuildTabs();
        }
    }

    /// <summary>Shows <paramref name="tab"/>.</summary>
    public void Select(BottomTab tab)
    {
        Tab = tab;
        if (tab == BottomTab.Telemetry)
        {
            _metrics.Refresh();
        }

        _body.Content = tab switch
        {
            BottomTab.Problems => _problemsBody,
            BottomTab.Terminal => _terminalView ?? _terminal,
            BottomTab.Output => _output,
            BottomTab.Todo => _todoView ?? (View)_noMeasures,
            _ => _metrics.AllRows.Count == 0 ? _noMeasures : _metricsView,
        };
        UpdateTools();
        RebuildTabs();
    }

    /// <summary>
    /// Ctrl+C on the panel that no control took: Output's selection, the telemetry's or Problems' picked rows, to the
    /// clipboard; false when the tab shown has nothing picked.
    /// </summary>
    public bool CopyChosen()
    {
        string? text = Tab switch
        {
            BottomTab.Output => _output.Model is { Selection.IsEmpty: false } model ? model.Snapshot.GetText(model.Selection.Start, model.Selection.Length) : null,
            BottomTab.Telemetry => _metrics.CopySelected(),
            BottomTab.Problems => _problems.CopySelected(),
            _ => null,
        };
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        _ = CopyAsync(text);
        return true;
    }

    /// <summary>
    /// Shows <paramref name="sources"/>' problems grouped by file (at most <c>CRAFT_PROBLEMS_LIMIT</c> rows), the empty
    /// state from <paramref name="active"/> (the document shown, <paramref name="activeName"/>) when there are none.
    /// Nothing is redrawn when what is shown did not change.
    /// </summary>
    public void ShowProblems(IReadOnlyList<ProblemSource> sources, EditorModel? active, string? activeName)
    {
        ArgumentNullException.ThrowIfNull(sources);
        int limit = (active?.Options ?? _palette.Options).ProblemsListLimit;
        _problems.Update(sources, limit, CodeActionProviders.All.Count > 0);
        bool none = _problems.Count == 0;
        _problemsEmpty.IsVisible = none;
        _problemsView.IsVisible = !none;
        if (none)
        {
            _problemsEmpty.Show(ShellEmptyStates.Problems(active, activeName));
        }

        if (_problems.Count != _shownCount)
        {
            _shownCount = _problems.Count;
            RebuildTabs();
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and settings.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        _terminal.TextColor = palette.Secondary;
        _noMeasures.TextColor = palette.Secondary;
        _problemsEmpty.Apply(palette);
        _collapse.TextColor = palette.Secondary;
        _copyAll.TextColor = palette.Secondary;
        _output.Theme = LogClasses.Theme(palette.Theme);
        var options = OutputOptions(palette.Options);
        if (options != _output.Options)
        {
            _output.Options = options;
        }

        _metricsView.Apply(palette.Options, palette.Theme);
        _problemsView.Apply(palette.Options, palette.Theme);
        _todoView?.SetPalette(palette);
        UpdateTools();
        RebuildTabs();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _host.KeyBindingsChanged -= OnKeyBindingsChanged;
        _host.Logged -= OnLogged;
        if (_todo is not null)
        {
            _todo.Changed -= OnTodoChanged;
        }

        _todoView?.Dispose();
        _metrics.Recorded -= OnMeasured;
        _metrics.Dispose();
        _metricsView.Dispose();
        _problemsView.Dispose();
        _log.Dispose();
        _output.Dispose();
    }

    // The editor's settings for Output: no scrolling past the end (the follow rule reads the end), no inline completion.
    private static CraftOptions OutputOptions(CraftOptions options) => options with { BottomMarginLines = 0, InlineCompletionProvider = "none" };

    // The tab's whole text.
    private string AllText() => Tab switch
    {
        BottomTab.Output => _log.Shown.Buffer.Current is { } text ? text.GetText(0, text.Length) : "",
        BottomTab.Telemetry => _metrics.CopyAll(),
        BottomTab.Problems => _problems.CopyAll(),
        _ => "",
    };

    // Through the dispatched clipboard: a failure is said in Output, never thrown into a click's or a key's handler.
    private async Task CopyAsync(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            await MauiEditorClipboard.Shared.SetTextAsync(text);
        }
#pragma warning disable CA1031 // A copy that fails is said; the panel goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, $"The copy did not reach the clipboard: {error.Message}", error);
        }
    }

    // Each tab's tools: Output's filter and follow, the telemetry's filter, Copy all but in Terminal.
    private void UpdateTools()
    {
        bool output = Tab == BottomTab.Output;
        _filterField.IsVisible = output || Tab == BottomTab.Telemetry;
        _follow.IsVisible = output;
        _copyAll.IsVisible = Tab is not (BottomTab.Terminal or BottomTab.Todo);
        ToolTipProperties.SetText(_copyAll, Tab == BottomTab.Output ? "Copy every line shown (Ctrl+C copies the selection)" : "Copy every row shown (Ctrl+C copies the rows picked)");
        _filling = true;
        try
        {
            _filter.Text = output ? _outputFilter : _metricsFilter;
        }
        finally
        {
            _filling = false;
        }

        bool on = _log.Following;
        if (InteractiveStates.Of(_follow) is { } states)
        {
            states.Selected = on;
        }

        _follow.TextColor = on ? _palette.Text : _palette.Secondary;
        _follow.Text = on ? "Following" : "Follow";
    }

    private void OnFilterChanged(object? sender, Microsoft.Maui.Controls.TextChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        string text = e.NewTextValue ?? "";
        if (Tab == BottomTab.Output)
        {
            _outputFilter = text;
            _log.Filter = text;
        }
        else if (Tab == BottomTab.Telemetry)
        {
            _metricsFilter = text;
            _metrics.Filter = text;
        }
    }

    private void OnKeyBindingsChanged(object? sender, EventArgs e) =>
        Dispatcher.DispatchLabelled("bottom panel: the keys in the tooltip", () => ToolTipProperties.SetText(_collapse, ShellTooltips.For(_host, "Hide the panel", ShellCommandIds.TogglePanel)));

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var tab in (ReadOnlySpan<BottomTab>)[BottomTab.Problems, BottomTab.Terminal, BottomTab.Output, BottomTab.Todo, BottomTab.Telemetry])
        {
            if (tab == BottomTab.Todo && _todo is null)
            {
                continue;
            }

            string title = tab switch
            {
                BottomTab.Problems => _problems.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"Problems {_problems.Count}") : "Problems",
                BottomTab.Terminal => "Terminal",
                BottomTab.Output => "Output",
                BottomTab.Todo => _todo is { Count: > 0 } todo ? string.Create(CultureInfo.InvariantCulture, $"TODO {todo.Count}") : "TODO",
                _ => "Idrak telemetry",
            };
            bool active = tab == Tab;
            var label = _palette.Label(title, active ? _palette.Text : _palette.Secondary, _palette.SmallSize);
            var cell = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(new GridLength(1))], MinimumHeightRequest = 32 };
            cell.Add(label, 0, 0);
            cell.Add(new BoxView { Color = active ? _palette.Accent : Colors.Transparent, HeightRequest = 1 }, 0, 1);
            var states = _palette.States(cell, InteractionKind.Tab);     // phase X: hover and pressed from the theme
            states.Label = label;
            states.Selected = active;
            var tap = new TapGestureRecognizer();
            var chosen = tab;
            // A click only selects; the active tab clicked again does nothing (the owner: a click must not hide the panel).
            tap.Tapped += (_, _) =>
            {
                if (chosen != Tab)
                {
                    Select(chosen);
                }
            };
            cell.GestureRecognizers.Add(tap);
            ToolTipProperties.SetText(cell, title);
            _tabs.Children.Add(cell);
        }
    }

    // The TODO list changed: its count in the tab's title (on the UI thread; the list raises it there).
    private void OnTodoChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("todo: the tab's count", RebuildTabs);
        }
        else
        {
            RebuildTabs();
        }
    }

    // A log line from any thread: posted, and one flush dispatched for every line that comes before it runs.
    private void OnLogged(object? sender, EditorLogEntry entry)
    {
        if (_log.Post(LogText.Line(entry)))
        {
            Dispatcher.DispatchLabelled("output: the log's new lines", () => _log.Flush());
        }
    }

    // A measure from any thread: one refresh dispatched for every measure that comes before it runs.
    private void OnMeasured(object? sender, EventArgs e) => Dispatcher.DispatchLabelled("telemetry: the new measures", () =>
    {
        bool first = _metrics.AllRows.Count == 0;
        _metrics.Refresh();
        if (first && Tab == BottomTab.Telemetry)
        {
            Select(BottomTab.Telemetry);
        }
    });
}

/// <summary>Ctrl+F in Output: its editor (the find searches its text) and the selection's text to search for, or null.</summary>
public sealed class BottomFindEventArgs(EditorModel model, string? initialQuery) : EventArgs
{
    /// <summary>Output's editor.</summary>
    public EditorModel Model { get; } = model;

    /// <summary>The text to search for, or null.</summary>
    public string? InitialQuery { get; } = initialQuery;
}

/// <summary>A right-click in Output: its editor (the menu's target) and where.</summary>
public sealed class BottomContextEventArgs(EditorModel model, TappedEventArgs where) : EventArgs
{
    /// <summary>Output's editor.</summary>
    public EditorModel Model { get; } = model;

    /// <summary>Where it was.</summary>
    public TappedEventArgs Where { get; } = where;
}

/// <summary>
/// The status bar (docs/design/Main.dc.html), its texts set by the page: at the left the branch, the problems (a tap
/// shows them) and the semantic layer's state; at the right the caret's place, the indentation, the encoding, the line
/// break, the language, the plug-ins' items, and the model's place, device, memory and speed.
/// </summary>
public sealed class StatusBarView : ContentView
{
    private readonly FlexLayout _bar = new() { Wrap = FlexWrap.NoWrap, AlignItems = FlexAlignItems.Center, Padding = new Thickness(12, 0) };
    private ShellPalette _palette;

    /// <summary>
    /// The bar with <paramref name="pluginItems"/> (the plug-ins' status items) before the model's text; phase U's running
    /// <paramref name="activity"/> after the notice and the <paramref name="bell"/> at the far right, when given.
    /// </summary>
    public StatusBarView(ShellPalette palette, View pluginItems, View? activity = null, View? bell = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(pluginItems);
        _palette = palette;
        Branch = Item();
        Problems = Item();
        Semantic = Item();
        Notice = Item();
        Position = Item();
        Indentation = Item();
        Encoding = Item();
        LineEnding = Item();
        Language = Item();
        Paint = Item();
        Model = Item();
        var spacer = new BoxView { Color = Colors.Transparent, HeightRequest = 1 };
        _bar.Children.Add(Branch);
        _bar.Children.Add(Problems);
        _bar.Children.Add(Semantic);
        _bar.Children.Add(Notice);
        if (activity is not null)
        {
            _bar.Children.Add(activity);
            _bar.SetShrink(activity, 1);
        }

        _bar.Children.Add(spacer);
        _bar.SetGrow(spacer, 1);
        _bar.SetShrink(Notice, 1);
        _bar.Children.Add(Position);
        _bar.Children.Add(Indentation);
        _bar.Children.Add(Encoding);
        _bar.Children.Add(LineEnding);
        _bar.Children.Add(Language);
        _bar.Children.Add(Paint);
        _bar.Children.Add(pluginItems);
        _bar.Children.Add(Model);
        if (bell is not null)
        {
            _bar.Children.Add(bell);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ProblemsTapped?.Invoke(this, EventArgs.Empty);
        Problems.GestureRecognizers.Add(tap);
        var branch = new TapGestureRecognizer();
        branch.Tapped += (_, _) => BranchTapped?.Invoke(this, EventArgs.Empty);
        Branch.GestureRecognizers.Add(branch);
        Content = _bar;
        HeightRequest = 26;
        Apply(palette);
    }

#pragma warning disable CS1591 // The bar's items, named as they show.
    public Label Branch { get; }

    public Label Problems { get; }

    public Label Semantic { get; }

    public Label Notice { get; }

    public Label Position { get; }

    public Label Indentation { get; }

    public Label Encoding { get; }

    public Label LineEnding { get; }

    public Label Language { get; }

    public Label Paint { get; }

    public Label Model { get; }
#pragma warning restore CS1591

    /// <summary>Raised when the problems' count is tapped.</summary>
    public event EventHandler? ProblemsTapped;

    /// <summary>Raised when the branch is tapped (X part 8: the source control plug-in's branch command).</summary>
    public event EventHandler? BranchTapped;

    /// <summary>Narrow: only the caret's place, the problems and the notice show, each a touch target tall.</summary>
    public void SetNarrow(bool narrow)
    {
        foreach (var label in (ReadOnlySpan<Label>)[Branch, Semantic, Indentation, Encoding, LineEnding, Language, Paint, Model])
        {
            label.IsVisible = !narrow;
        }

        HeightRequest = narrow ? Math.Max(26, _palette.Options.TouchTarget) : 26;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Ground;
        foreach (var label in (ReadOnlySpan<Label>)[Branch, Semantic, Notice, Position, Indentation, Encoding, LineEnding, Language, Paint])
        {
            label.TextColor = palette.Secondary;
            label.FontFamily = palette.Font();
            label.FontSize = palette.SmallSize;
        }

        Problems.TextColor = palette.Warning;
        Model.TextColor = palette.Text;
        Problems.FontSize = Model.FontSize = palette.SmallSize;
    }

    private Label Item()
    {
        var label = _palette.Label("", _palette.Secondary, _palette.SmallSize);
        label.Margin = new Thickness(0, 0, 16, 0);
        return label;
    }
}
