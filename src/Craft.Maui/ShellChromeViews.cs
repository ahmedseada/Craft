// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using System.Globalization;
using Craft.Abstractions;
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

    /// <summary>Raised after the popup closed (tapped outside, or <see cref="Hide"/>).</summary>
    public event EventHandler? Closed;

    /// <summary>Shows <paramref name="popup"/> (in place of any other) with its top-left at <paramref name="at"/>, <paramref name="width"/> wide.</summary>
    public void Show(View popup, Point at, double width)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (_shown is not null)
        {
            Children.Remove(_shown);
        }

        _shown = popup;
        double x = Width > 0 ? Math.Clamp(at.X, 0, Math.Max(0, Width - width)) : at.X;
        SetLayoutBounds((BindableObject)popup, new Rect(x, Math.Max(0, at.Y), width, AbsoluteLayout.AutoSize));
        SetLayoutFlags((BindableObject)popup, AbsoluteLayoutFlags.None);
        Children.Add(popup);
        IsVisible = true;
        InputTransparent = false;
    }

    /// <summary>Closes the popup.</summary>
    public void Hide()
    {
        if (_shown is null)
        {
            return;
        }

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
    private ShellPalette _palette;

    /// <summary>The bar in <paramref name="palette"/>'s colours.</summary>
    public TitleBarView(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _logo = new GraphicsView { Drawable = _logoDrawable, WidthRequest = 20, HeightRequest = 20, InputTransparent = true };
        _name = palette.Label("Craft", weight: FontWeight.SemiBold);
        Search = palette.Input("Search files, symbols and commands  (Ctrl+P)", 26);
        Search.TextChanged += (_, e) => SearchChanged?.Invoke(this, e.NewTextValue ?? "");
        Search.Completed += (_, _) => SearchSubmitted?.Invoke(this, EventArgs.Empty);
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
        _narrow.Add(NarrowButton("☰", "Files", () => NarrowFilesTapped?.Invoke(this, EventArgs.Empty)), 0, 0);
        _narrow.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { _narrowTitle, _narrowPlace } }, 1, 0);
        _narrow.Add(NarrowButton("↶", "Undo", () => NarrowUndoTapped?.Invoke(this, EventArgs.Empty)), 2, 0);
        _narrow.Add(NarrowButton("⋯", "Commands", () => NarrowCommandsTapped?.Invoke(this, EventArgs.Empty)), 3, 0);
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

    private Button NarrowButton(string text, string name, Action tapped)
    {
        double size = Math.Max(44, _palette.Options.TouchTarget + 4);
        var button = _palette.Flat(text, _palette.Text, size, _palette.FontSize + 7);
        button.WidthRequest = size;
        button.Padding = new Thickness(0);
        SemanticProperties.SetDescription(button, name);
        ToolTipProperties.SetText(button, name);
        button.Clicked += (_, _) => tapped();
        _narrowButtons.Add(button);
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
        Rebuild();
    }

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
        var cell = new Grid { WidthRequest = 44, HeightRequest = 44, HorizontalOptions = LayoutOptions.Center, BackgroundColor = active ? _palette.RaisedHigh : Colors.Transparent };
        cell.Add(icon);
        if (active)
        {
            cell.Add(new BoxView { Color = _palette.Accent, WidthRequest = 2, HorizontalOptions = LayoutOptions.Start, InputTransparent = true });
        }

        ToolTipProperties.SetText(cell, item.Title);
        SemanticProperties.SetDescription(cell, item.Title);
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
/// The explorer (docs/design/Main.dc.html): the folder's name in capitals, its tree, and the semantic layer's card at the
/// foot (the engine, its state, its projects and memory, from <see cref="SemanticTelemetry"/> through the language's
/// contract; hidden when the active file's language has none).
/// </summary>
public sealed class ExplorerView : ContentView
{
    private readonly Label _header;
    private readonly Border _card;
    private readonly Label _engine;
    private readonly Label _workspace;
    private readonly Label _state;
    private readonly Label _projects;
    private readonly Label _memory;
    private ShellPalette _palette;
    private SemanticCard? _shown;

    /// <summary>The explorer over <paramref name="tree"/>.</summary>
    public ExplorerView(FolderTreeView tree, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Tree = tree;
        _header = palette.Label("", palette.Secondary, palette.CaptionSize, FontWeight.SemiBold);
        _header.CharacterSpacing = 0.6;
        _header.Margin = new Thickness(14, 10, 14, 8);
        _engine = palette.Label("", palette.Secondary, palette.CaptionSize, FontWeight.SemiBold);
        _engine.CharacterSpacing = 0.6;
        _workspace = palette.Label("Workspace");
        _state = palette.Label("");
        _projects = palette.Label("", palette.Secondary);
        _memory = palette.Label("", palette.Secondary);
        var rowState = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        rowState.Add(_workspace, 0, 0);
        rowState.Add(_state, 1, 0);
        var rowSize = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        rowSize.Add(_projects, 0, 0);
        rowSize.Add(_memory, 1, 0);
        _card = palette.Box(new VerticalStackLayout { Spacing = 6, Children = { _engine, rowState, rowSize } }, 8, new Thickness(10));
        _card.Margin = new Thickness(10, 6, 10, 10);
        _card.IsVisible = false;
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        grid.Add(_header, 0, 0);
        grid.Add(tree, 0, 1);
        grid.Add(_card, 0, 2);
        Content = grid;
        Apply(palette);
    }

    /// <summary>The tree.</summary>
    public FolderTreeView Tree { get; }

    /// <summary>The folder's name (shown in capitals), or null.</summary>
    public void SetFolder(string? root)
    {
        _header.Text = root is null ? "NO FOLDER" : Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).ToUpperInvariant();
    }

    /// <summary>The card, or null to hide it; nothing is redrawn when it is the one shown.</summary>
    public void SetSemantic(SemanticCard? card)
    {
        if (card == _shown)
        {
            return;
        }

        _shown = card;
        _card.IsVisible = card is not null;
        if (card is null)
        {
            return;
        }

        _engine.Text = card.Engine.ToUpperInvariant();
        _state.Text = card.State;
        _state.TextColor = card.IsReady ? _palette.Success : _palette.Secondary;
        _projects.Text = card.Projects;
        _memory.Text = card.Memory;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        _header.TextColor = palette.Secondary;
        _engine.TextColor = palette.Secondary;
        _workspace.TextColor = palette.Text;
        _projects.TextColor = palette.Secondary;
        _memory.TextColor = palette.Secondary;
        _card.Stroke = palette.Border;
        Tree.TextColor = palette.Text;
        Tree.SelectedColor = palette.Selection;
        Tree.FontFamily = palette.Font();
        var shown = _shown;
        _shown = null;
        SetSemantic(shown);
    }
}

/// <summary>The breadcrumb (docs/design/Main.dc.html): the folder, the file, the type and the member, separated by '›'; redrawn only when its parts change.</summary>
public sealed class BreadcrumbView : ContentView
{
    private readonly HorizontalStackLayout _parts = new() { Spacing = 6, Padding = new Thickness(16, 0), VerticalOptions = LayoutOptions.Center };
    private IReadOnlyList<ScopeName> _shown = [];
    private ShellPalette _palette;

    /// <summary>An empty breadcrumb.</summary>
    public BreadcrumbView(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        HeightRequest = 28;
        Content = _parts;
    }

    /// <summary>Shows <paramref name="parts"/> (the same parts as shown: nothing done).</summary>
    public void Show(IReadOnlyList<ScopeName> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.SequenceEqual(_shown))
        {
            return;
        }

        _shown = parts;
        Redraw();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        Redraw();
    }

    private void Redraw()
    {
        _parts.Children.Clear();
        for (int i = 0; i < _shown.Count; i++)
        {
            if (i > 0)
            {
                _parts.Children.Add(_palette.Label("›", _palette.Secondary, _palette.SmallSize));
            }

            var part = _shown[i];
            var color = i == _shown.Count - 1 && part.Kind == ScopeKind.Method ? _palette.Method : _palette.Secondary;
            _parts.Children.Add(_palette.Label(part.Name, color, _palette.SmallSize));
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
}

/// <summary>A line of the Output or telemetry list.</summary>
/// <param name="Text">The line.</param>
public sealed record TextRow(string Text);

/// <summary>A row of the problems list.</summary>
/// <param name="Text">"IDE0005  Using directive is unnecessary."</param>
/// <param name="Place">"EditorTools.cs  1:1".</param>
/// <param name="Color">The severity's colour.</param>
/// <param name="Mark">The severity's mark ("✕", "⚠", "ℹ").</param>
/// <param name="Position">Where the caret goes when it is chosen.</param>
public sealed record ProblemRow(string Text, string Place, Color Color, string Mark, int Position)
{
    /// <summary>The diagnostic (the row's code actions are asked for it).</summary>
    public EditorDiagnostic Diagnostic { get; init; }

    /// <summary>Whether a code action provider is registered (the row shows Fix and Explain).</summary>
    public bool HasActions { get; init; }
}

/// <summary>
/// The bottom panel (docs/design/Main.dc.html): the tabs Problems (with the count), Terminal (the terminals, <see cref="TerminalContent"/>), Output (the host's
/// log, live) and Idrak telemetry (the measures recorded through the host), the active one in the text colour over the
/// accent's line. Each list is a virtualized list (only the rows on screen are made). Phase U2: the chevron at the strip's
/// end asks the shell to collapse it (<see cref="CollapseRequested"/>, as Ctrl+J does); its
/// height is its <see cref="SplitPanel"/>'s (a drag of the splitter above it, kept per workspace).
/// </summary>
public sealed class BottomPanelView : ContentView, IDisposable
{
    private readonly EditorHost _host;
    private readonly TelemetryLog _telemetry;
    private readonly HorizontalStackLayout _tabs = new() { Spacing = 18, Padding = new Thickness(16, 0) };
    private readonly ContentView _body = new();
    private readonly Button _collapse;
    private readonly CollectionView _problems;
    private readonly CollectionView _output;
    private readonly CollectionView _measures;
    private readonly ObservableCollection<TextRow> _outputLines = [];
    private readonly Label _terminal;
    private View? _terminalView;
    private readonly Label _noMeasures;
    private readonly Action<int> _problemChosen;
    private ShellPalette _palette;
    private int _problemCount;
    private int _shownCount = -1;
    private List<ProblemRow>? _shownRows;

    /// <summary>The panel over <paramref name="host"/>; a problem's row moves the caret through <paramref name="problemChosen"/>.</summary>
    public BottomPanelView(EditorHost host, ShellPalette palette, Action<int> problemChosen)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(problemChosen);
        _host = host;
        _palette = palette;
        _problemChosen = problemChosen;
        _telemetry = new TelemetryLog(host);
        _problems = new CollectionView { SelectionMode = SelectionMode.Single, ItemTemplate = new DataTemplate(MakeProblemRow) };
        _problems.SelectionChanged += OnProblemSelected;
        _output = new CollectionView { ItemsSource = _outputLines, ItemTemplate = new DataTemplate(MakeTextRow), ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView };
        _measures = new CollectionView { ItemTemplate = new DataTemplate(MakeTextRow) };
        _terminal = palette.Label("No terminal host is available here.", palette.Secondary);
        _terminal.Margin = new Thickness(16, 8);
        _noMeasures = palette.Label("Nothing recorded yet: the plug-ins' and the library's measures (IEditorHost.Record) show here.", palette.Secondary);
        _noMeasures.Margin = new Thickness(16, 8);
        foreach (var entry in host.RecentLog)
        {
            _outputLines.Add(new TextRow(LineOf(entry)));
        }

        host.Logged += OnLogged;
        _telemetry.Changed += OnMeasured;
        _collapse = palette.Flat("⌄", palette.Secondary, 28, palette.FontSize + 2);
        _collapse.WidthRequest = 32;
        _collapse.Padding = new Thickness(0);
        ToolTipProperties.SetText(_collapse, "Hide the panel (Ctrl+J)");
        SemanticProperties.SetDescription(_collapse, "Hide the bottom panel");
        _collapse.Clicked += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
        var strip = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        strip.Add(_tabs, 0, 0);
        strip.Add(_collapse, 1, 0);
        var grid = new Grid { RowDefinitions = [new RowDefinition(new GridLength(32)), new RowDefinition(GridLength.Star)] };
        grid.Add(strip, 0, 0);
        grid.Add(_body, 0, 1);
        Content = grid;
        Apply(palette);
        Select(BottomTab.Problems);
    }

    /// <summary>The tab shown.</summary>
    public BottomTab Tab { get; private set; }

    /// <summary>Raised when the chevron or the active tab is clicked: the shell collapses the panel.</summary>
    public event EventHandler? CollapseRequested;

    /// <summary>
    /// A Problems row's Fix or Explain (the design's buttons) was pressed: its diagnostic and the code action kind
    /// (<see cref="CodeActionKinds.Fix"/>, <see cref="CodeActionKinds.Explain"/>); the page runs the registered providers' action.
    /// </summary>
    public Action<EditorDiagnostic, string>? CodeActionRequested { get; set; }

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

    /// <summary>Shows <paramref name="tab"/>.</summary>
    public void Select(BottomTab tab)
    {
        Tab = tab;
        _body.Content = tab switch
        {
            BottomTab.Problems => _problems,
            BottomTab.Terminal => _terminalView ?? _terminal,
            BottomTab.Output => _output,
            _ => _telemetry.Rows.Count == 0 ? _noMeasures : _measures,
        };
        if (tab == BottomTab.Telemetry)
        {
            _measures.ItemsSource = _telemetry.Rows.Select(r => new TextRow(r.Text)).ToArray();
        }

        RebuildTabs();
    }

    /// <summary>Shows <paramref name="model"/>'s problems (errors first, then by place), at most <c>CRAFT_PROBLEMS_LIMIT</c> rows.</summary>
    public void ShowProblems(EditorModel? model, string? fileName)
    {
        if (model is null)
        {
            _problems.ItemsSource = null;
            _shownRows = null;
            _problemCount = 0;
            _shownCount = 0;
            RebuildTabs();
            return;
        }

        var snapshot = model.Snapshot;
        var shown = new List<EditorDiagnostic>();
        foreach (var diagnostic in model.Diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Hidden)
            {
                shown.Add(diagnostic);
            }
        }

        shown.Sort(static (a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : a.Start.CompareTo(b.Start));
        _problemCount = shown.Count;
        int limit = model.Options.ProblemsListLimit;
        var rows = new List<ProblemRow>(Math.Min(limit, shown.Count));
        bool actions = CodeActionProviders.All.Count > 0;
        foreach (var diagnostic in shown.Take(limit))
        {
            int start = Math.Clamp(diagnostic.Start, 0, snapshot.Length);
            int line = snapshot.GetLineIndex(start);
            int column = start - snapshot.GetLine(line).Start;
            var (color, mark) = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => (_palette.Error, "✕"),
                DiagnosticSeverity.Warning => (_palette.Warning, "⚠"),
                _ => (_palette.Info, "ℹ"),
            };
            rows.Add(new ProblemRow($"{diagnostic.Id}  {diagnostic.Message}", string.Create(CultureInfo.InvariantCulture, $"{fileName}  {line + 1}:{column + 1}"), color, mark, start)
            {
                Diagnostic = diagnostic,
                HasActions = actions,
            });
        }

        // Every keystroke moves the diagnostics: the list is replaced only when what it shows changed (a WinUI list's items
        // replaced while a row is clicked or measured is what the owner's error run may have hit), the strip only when
        // the count did.
        if (!SameRows(_shownRows, rows))
        {
            _shownRows = rows;
            _problems.ItemsSource = rows;
        }

        if (_problemCount != _shownCount)
        {
            _shownCount = _problemCount;
            RebuildTabs();
        }
    }

    private static bool SameRows(List<ProblemRow>? shown, List<ProblemRow> rows)
    {
        if (shown is null || shown.Count != rows.Count)
        {
            return false;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (shown[i] != rows[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        _terminal.TextColor = palette.Secondary;
        _noMeasures.TextColor = palette.Secondary;
        _collapse.TextColor = palette.Secondary;
        RebuildTabs();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _host.Logged -= OnLogged;
        _telemetry.Changed -= OnMeasured;
        _telemetry.Dispose();
    }

    private static string LineOf(EditorLogEntry entry) =>
        string.Create(CultureInfo.InvariantCulture, $"{entry.Time.ToLocalTime():HH:mm:ss}  {entry.Level,-11}  {entry.Source}: {entry.Message}{(entry.Error is null ? "" : "  (" + entry.Error.Message + ")")}");

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var tab in (ReadOnlySpan<BottomTab>)[BottomTab.Problems, BottomTab.Terminal, BottomTab.Output, BottomTab.Telemetry])
        {
            string title = tab switch
            {
                BottomTab.Problems => _problemCount > 0 ? string.Create(CultureInfo.InvariantCulture, $"Problems {_problemCount}") : "Problems",
                BottomTab.Terminal => "Terminal",
                BottomTab.Output => "Output",
                _ => "Idrak telemetry",
            };
            bool active = tab == Tab;
            var label = _palette.Label(title, active ? _palette.Text : _palette.Secondary, _palette.SmallSize);
            var cell = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(new GridLength(1))], MinimumHeightRequest = 32 };
            cell.Add(label, 0, 0);
            cell.Add(new BoxView { Color = active ? _palette.Accent : Colors.Transparent, HeightRequest = 1 }, 0, 1);
            var tap = new TapGestureRecognizer();
            var chosen = tab;
            // A click only selects; the active tab clicked again does nothing (the owner: a click must not hide the panel).
            // The chevron and Ctrl+J fold it.
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

    private HorizontalStackLayout MakeProblemRow()
    {
        var mark = new Label { FontSize = _palette.FontSize, VerticalTextAlignment = TextAlignment.Center, WidthRequest = 18 };
        mark.SetBinding(Label.TextProperty, static (ProblemRow row) => row.Mark);
        mark.SetBinding(Label.TextColorProperty, static (ProblemRow row) => row.Color);
        var text = _palette.Label("");
        text.SetBinding(Label.TextProperty, static (ProblemRow row) => row.Text);
        var place = _palette.Label("", _palette.Secondary);
        place.SetBinding(Label.TextProperty, static (ProblemRow row) => row.Place);
        var fix = _palette.SecondaryButton("Fix", 26);
        var explain = _palette.SecondaryButton("Explain", 26);
        foreach (var (button, kind) in (ReadOnlySpan<(Button, string)>)[(fix, CodeActionKinds.Fix), (explain, CodeActionKinds.Explain)])
        {
            button.SetBinding(IsVisibleProperty, static (ProblemRow row) => row.HasActions);
            button.Clicked += (sender, _) =>
            {
                if (sender is Button { BindingContext: ProblemRow row })
                {
                    CodeActionRequested?.Invoke(row.Diagnostic, kind);
                }
            };
        }

        return new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(16, 4), Children = { mark, text, place, fix, explain } };
    }

    private Label MakeTextRow()
    {
        var label = _palette.Label("", _palette.Text, _palette.SmallSize);
        label.FontFamily = _palette.Options.FontFamily;
        label.Padding = new Thickness(16, 1);
        label.SetBinding(Label.TextProperty, static (TextRow row) => row.Text);
        return label;
    }

    private void OnProblemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not ProblemRow row)
        {
            return;
        }

        Dispatcher.DispatchLabelled("problems: go to the problem", () =>
        {
            _problems.SelectedItem = null;
            _problemChosen(row.Position);
        });
    }

    private void OnLogged(object? sender, EditorLogEntry entry)
    {
        void Add()
        {
            _outputLines.Add(new TextRow(LineOf(entry)));
            int keep = Math.Max(1, _host.Options.HostLogLines);
            while (_outputLines.Count > keep)
            {
                _outputLines.RemoveAt(0);
            }
        }

        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("output: a log line", Add);
        }
        else
        {
            Add();
        }
    }

    private void OnMeasured(object? sender, EventArgs e)
    {
        if (Tab != BottomTab.Telemetry)
        {
            return;
        }

        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.DispatchLabelled("telemetry: a measure", () => Select(BottomTab.Telemetry));
        }
        else
        {
            Select(BottomTab.Telemetry);
        }
    }
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
