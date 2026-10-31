// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>A theme colour (0xAARRGGBB) as a MAUI colour.</summary>
public static class ThemeColors
{
    /// <summary>The colour of <paramref name="argb"/>.</summary>
    public static Color Of(uint argb) => Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}

/// <summary>A row of the folder tree as the list shows it.</summary>
/// <param name="Text">The marker and the name.</param>
/// <param name="Indent">The row's indent.</param>
/// <param name="Color">The text's colour.</param>
/// <param name="Path">The entry's full path (a row is found by it, so rows after an inserted one stay the same items).</param>
/// <param name="Background">The row's fill (the active file's is the selection's).</param>
/// <param name="FontFamily">The interface's font.</param>
public sealed record FolderTreeItem(string Text, Thickness Indent, Color Color, string Path, Color Background, string? FontFamily);

/// <summary>
/// The folder tree (<see cref="FolderTree"/>) as a virtualized list (only the rows on screen are made): a folder's row
/// opens or closes it, a file's row raises <see cref="FileActivated"/>. A change replaces only the rows that differ
/// (<see cref="RowChange"/>: a folder opened inserts its children, the active file moves two fills, a file the disk added
/// inserts one row), so the list keeps its scroll and the rows on screen are not made again (the owner's note 9); the
/// active file's row is scrolled into view when it changes.
/// </summary>
public sealed class FolderTreeView : ContentView
{
    private readonly CollectionView _list;
    private readonly System.Collections.ObjectModel.ObservableCollection<FolderTreeItem> _items = [];
    private FolderTree? _tree;
    private Color _textColor = Colors.Gray;
    private float _indent = 14;
    private Color _selected = Colors.Transparent;
    private string? _fontFamily;
    private string? _activePath;

    /// <summary>An empty tree (set <see cref="Tree"/>).</summary>
    public FolderTreeView()
    {
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(MakeRow),
            ItemsSource = _items,
        };
        _list.SelectionChanged += OnSelectionChanged;
        Content = _list;
    }

    /// <summary>The tree shown, or null for none.</summary>
    public FolderTree? Tree
    {
        get => _tree;
        set
        {
            if (_tree is not null)
            {
                _tree.Changed -= OnTreeChanged;
            }

            _tree = value;
            if (value is not null)
            {
                value.Changed += OnTreeChanged;
            }

            Refresh();
        }
    }

    /// <summary>The text's colour.</summary>
    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            Refresh();
        }
    }

    /// <summary>The indent of each level (<c>CRAFT_TREE_INDENT</c>).</summary>
    public float Indent
    {
        get => _indent;
        set
        {
            _indent = value;
            Refresh();
        }
    }

    /// <summary>The fill of the active file's row (the design's selection).</summary>
    public Color SelectedColor
    {
        get => _selected;
        set
        {
            _selected = value;
            Refresh();
        }
    }

    /// <summary>The rows' font family, or null for the platform's.</summary>
    public string? FontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = value;
            Refresh();
        }
    }

    /// <summary>The active document's path (its row is filled), or null.</summary>
    public string? ActivePath
    {
        get => _activePath;
        set
        {
            if (!string.Equals(_activePath, value, StringComparison.OrdinalIgnoreCase))
            {
                _activePath = value;
                Refresh();
                Reveal();
            }
        }
    }

    /// <summary>Raised with a file's full path when its row is picked.</summary>
    public event EventHandler<string>? FileActivated;

    /// <summary>Raised with a folder's full path (a file's row gives its folder) when "Open in terminal" is chosen on its row.</summary>
    public event EventHandler<string>? OpenInTerminalRequested;

    private ContentView MakeRow()
    {
        var label = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, Padding = new Thickness(0, 4) };
        label.SetBinding(Label.TextProperty, static (FolderTreeItem item) => item.Text);
        label.SetBinding(MarginProperty, static (FolderTreeItem item) => item.Indent);
        label.SetBinding(Label.TextColorProperty, static (FolderTreeItem item) => item.Color);
        label.SetBinding(Label.FontFamilyProperty, static (FolderTreeItem item) => item.FontFamily);
        var row = new ContentView { Content = label };
        row.SetBinding(BackgroundColorProperty, static (FolderTreeItem item) => item.Background);
        var openInTerminal = new MenuFlyoutItem { Text = "Open in terminal" };
        openInTerminal.Clicked += (_, _) =>
        {
            if (row.BindingContext is FolderTreeItem item && _tree is { } tree && tree.IndexOf(item.Path) is >= 0 and var index)
            {
                var entry = tree.Rows[index].Entry;
                var folder = entry.IsFolder ? entry.FullPath : System.IO.Path.GetDirectoryName(entry.FullPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    OpenInTerminalRequested?.Invoke(this, folder);
                }
            }
        };
        FlyoutBase.SetContextFlyout(row, new MenuFlyout { openInTerminal });
        return row;
    }

    private void OnTreeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var rows = _tree?.Rows ?? [];
        var items = new FolderTreeItem[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var marker = row.Entry.IsFolder ? row.IsExpanded ? "▾ " : "▸ " : "   ";
            bool active = !row.Entry.IsFolder && string.Equals(row.Entry.FullPath, _activePath, StringComparison.OrdinalIgnoreCase);
            items[i] = new FolderTreeItem(marker + row.Entry.Name, new Thickness(14 + (row.Depth * _indent), 0, 14, 0), _textColor, row.Entry.FullPath,
                active ? _selected : Colors.Transparent, _fontFamily);
        }

        // Only the run that differs is replaced; the rows before and after it stay the same items on screen.
        var change = RowChange.Between(_items, items);
        if (change.IsEmpty)
        {
            return;
        }

        int common = Math.Min(change.Removed, change.Inserted);
        for (int i = 0; i < common; i++)
        {
            if (!_items[change.Start + i].Equals(items[change.Start + i]))
            {
                _items[change.Start + i] = items[change.Start + i];   // the active file moved: its old and new rows only
            }
        }

        for (int i = change.Removed - 1; i >= common; i--)
        {
            _items.RemoveAt(change.Start + i);
        }

        for (int i = common; i < change.Inserted; i++)
        {
            _items.Insert(change.Start + i, items[change.Start + i]);
        }
    }

    // The active file's row into view (only when it is shown: a file outside the tree scrolls nothing).
    private void Reveal()
    {
        if (_activePath is null)
        {
            return;
        }

        foreach (var item in _items)
        {
            if (string.Equals(item.Path, _activePath, StringComparison.OrdinalIgnoreCase))
            {
                _list.ScrollTo(item, position: ScrollToPosition.MakeVisible, animate: false);
                return;
            }
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not FolderTreeItem item || _tree is null)
        {
            return;
        }

        // Acted on after the list finished its selection (the items may be replaced).
        Dispatcher.DispatchLabelled("explorer: open the chosen row", () =>
        {
            _list.SelectedItem = null;
            if (_tree is not { } tree || tree.IndexOf(item.Path) is not (>= 0 and var index))
            {
                return;
            }

            var row = tree.Rows[index];
            if (row.Entry.IsFolder)
            {
                tree.Toggle(index);
            }
            else
            {
                FileActivated?.Invoke(this, row.Entry.FullPath);
            }
        });
    }
}

/// <summary>
/// The tabs (<see cref="DocumentTabs"/>, docs/design/Main.dc.html): in the wide layout a scrolling strip on the ground,
/// the active tab on the surface under the accent's line, each its title, a dot when it has unsaved changes, and a close
/// button; in the <see cref="Compact"/> (narrow) layout one switcher of touch targets: previous, the active tab's title and
/// place (which asks for the list), next, close. A click activates; a close raises <see cref="CloseRequested"/>, so the
/// shell asks about unsaved changes. A tab is redrawn only when its title or dirty marker changes. Phase U2: the pages
/// open as tabs (<see cref="PageTabs"/>: settings, Models and devices, Plug-ins) follow the documents' tabs, each with its
/// close button; a page shown makes its tab the active one, a document's tab clicked shows the document again.
/// </summary>
public sealed class TabStripView : ContentView, IDisposable
{
    private readonly DocumentTabs _tabs;
    private readonly PageTabs? _pages;
    private readonly HorizontalStackLayout _strip = new() { Spacing = 0 };
    private readonly ScrollView _scroll;
    private readonly Dictionary<DocumentTab, (Label Title, Ellipse Dot)> _titles = [];
    private bool _compact;
    private ShellPalette? _palette;

    /// <summary>The strip of <paramref name="tabs"/>, and of <paramref name="pages"/> when given.</summary>
    public TabStripView(DocumentTabs tabs, PageTabs? pages = null)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs = tabs;
        _pages = pages;
        if (pages is not null)
        {
            pages.Changed += OnTabsChanged;
        }

        _scroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Default };
        Content = _scroll;
        tabs.TabsChanged += OnTabsChanged;
        tabs.ActiveChanged += OnTabsChanged;
        tabs.TabChanged += OnTabChanged;
        Rebuild();
    }

    /// <summary>Whether it shows the narrow layout's single switcher.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (value != _compact)
            {
                _compact = value;
                Rebuild();
            }
        }
    }

    /// <summary>Raised when a tab's close button is clicked.</summary>
    public event EventHandler<DocumentTab>? CloseRequested;

    /// <summary>Raised when the compact switcher's title is clicked (the page lists the tabs).</summary>
    public event EventHandler? ListRequested;

    /// <summary>Takes <paramref name="palette"/>'s colours and fonts.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Rebuild();
    }

    /// <summary>The title a tab shows: its title, with a dot when it has unsaved changes.</summary>
    public static string TitleOf(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return tab.IsDirty ? tab.Title + " •" : tab.Title;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tabs.TabsChanged -= OnTabsChanged;
        _tabs.ActiveChanged -= OnTabsChanged;
        _tabs.TabChanged -= OnTabChanged;
        if (_pages is not null)
        {
            _pages.Changed -= OnTabsChanged;
        }
    }

    private void OnTabsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnTabChanged(object? sender, DocumentTab tab)
    {
        if (_compact)
        {
            Rebuild();
        }
        else if (_titles.TryGetValue(tab, out var shown))
        {
            shown.Title.Text = tab.Title;
            shown.Dot.IsVisible = tab.IsDirty;
        }
    }

    private void Rebuild()
    {
        _strip.Children.Clear();
        _titles.Clear();
        var page = _pages?.Active;
        IsVisible = _tabs.Tabs.Count > 0 || _pages is { Tabs.Count: > 0 };
        if (_palette is not { } palette)
        {
            return;
        }

        BackgroundColor = palette.Ground;
        if (_compact)
        {
            double size = Math.Max(44, palette.Options.TouchTarget);
            HeightRequest = size;
            var active = _tabs.Active;
            if (page is not null)
            {
                // A page in the editor's place: its title, back to the document, and close.
                var back = palette.Flat("‹", palette.Text, size, palette.FontSize + 4);
                back.WidthRequest = size;
                back.Clicked += (_, _) => _pages!.ShowDocument();
                var pageTitle = palette.Flat(page.Title, palette.Text, size);
                pageTitle.BackgroundColor = palette.Surface;
                var closePage = palette.Flat("×", palette.Text, size, palette.FontSize + 4);
                closePage.WidthRequest = size;
                closePage.Clicked += (_, _) => _pages!.Close(page.Id);
                _strip.Children.Add(back);
                _strip.Children.Add(pageTitle);
                _strip.Children.Add(closePage);
                return;
            }

            var previous = palette.Flat("‹", palette.Text, size, palette.FontSize + 4);
            previous.WidthRequest = size;
            previous.Clicked += (_, _) => _tabs.Previous();
            var title = palette.Flat(active is null ? "" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{TitleOf(active)}  ({_tabs.ActiveIndex + 1}/{_tabs.Tabs.Count}) ▾"), palette.Text, size);
            title.BackgroundColor = palette.Surface;
            title.Clicked += (_, _) => ListRequested?.Invoke(this, EventArgs.Empty);
            var next = palette.Flat("›", palette.Text, size, palette.FontSize + 4);
            next.WidthRequest = size;
            next.Clicked += (_, _) => _tabs.Next();
            var close = palette.Flat("×", palette.Text, size, palette.FontSize + 4);
            close.WidthRequest = size;
            close.Clicked += (_, _) =>
            {
                if (_tabs.Active is { } tab)
                {
                    CloseRequested?.Invoke(this, tab);
                }
            };
            _strip.Children.Add(previous);
            _strip.Children.Add(title);
            _strip.Children.Add(next);
            _strip.Children.Add(close);
            return;
        }

        HeightRequest = 36;
        foreach (var tab in _tabs.Tabs)
        {
            bool isActive = page is null && ReferenceEquals(tab, _tabs.Active);
            var title = palette.Label(tab.Title, isActive ? palette.Text : palette.Secondary);
            var dot = new Ellipse { WidthRequest = 8, HeightRequest = 8, Fill = new SolidColorBrush(palette.Text), IsVisible = tab.IsDirty, VerticalOptions = LayoutOptions.Center };
            var close = palette.Flat("×", palette.Secondary, 24, palette.SmallSize);
            close.WidthRequest = 24;
            close.Padding = new Thickness(0);
            ToolTipProperties.SetText(close, "Close (Ctrl+W)");
            close.Clicked += (_, _) => CloseRequested?.Invoke(this, tab);
            var row = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(14, 0, 6, 0), VerticalOptions = LayoutOptions.Center, Children = { title, dot, close } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _pages?.ShowDocument();
                _tabs.Activate(tab);
            };
            _titles[tab] = (title, dot);
            _strip.Children.Add(Cell(palette, row, isActive, tab.FilePath ?? tab.Title, tap));
        }

        foreach (var open in _pages?.Tabs ?? [])
        {
            bool isActive = ReferenceEquals(open, page);
            var title = palette.Label(open.Title, isActive ? palette.Text : palette.Secondary);
            var close = palette.Flat("×", palette.Secondary, 24, palette.SmallSize);
            close.WidthRequest = 24;
            close.Padding = new Thickness(0);
            ToolTipProperties.SetText(close, "Close (Ctrl+W)");
            var shown = open;
            close.Clicked += (_, _) => _pages!.Close(shown.Id);
            var row = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(14, 0, 6, 0), VerticalOptions = LayoutOptions.Center, Children = { title, close } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _pages!.Activate(shown);
            _strip.Children.Add(Cell(palette, row, isActive, open.Title, tap));
        }
    }

    // A tab's cell: the accent's line over the active one, on the surface; a border between the others.
    private static Grid Cell(ShellPalette palette, View row, bool isActive, string tooltip, TapGestureRecognizer tap)
    {
        var cell = new Grid
        {
            RowDefinitions = [new RowDefinition(new GridLength(2)), new RowDefinition(GridLength.Star)],
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(1))],
            BackgroundColor = isActive ? palette.Surface : Colors.Transparent,
            HeightRequest = 36,
        };
        var accentLine = new BoxView { Color = isActive ? palette.Accent : Colors.Transparent };
        cell.Add(accentLine, 0, 0);
        Grid.SetColumnSpan(accentLine, 2);
        cell.Add(row, 0, 1);
        cell.Add(new BoxView { Color = isActive ? Colors.Transparent : palette.Border }, 1, 1);
        ToolTipProperties.SetText(cell, tooltip);
        cell.GestureRecognizers.Add(tap);
        return cell;
    }
}

/// <summary>The shell's prompts as Craft's dialogs (<see cref="CraftDialog"/>, no platform message box); its pages open as tabs (<see cref="PageTabs"/>), not here.</summary>
public sealed class MauiShellUi(CraftDialog dialog) : IShellUi
{
    private readonly CraftDialog _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

    /// <inheritdoc />
    public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => _dialog.AskUnsavedAsync(title, cancellationToken);

    /// <inheritdoc />
    public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => _dialog.PickAsync(title, items, cancellationToken);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) => _dialog.ShowMessageAsync(title, message);
}

/// <summary>
/// What the shell needs from the platform, each platform's implementation under its <c>Platforms/</c> folder (Windows:
/// <c>Craft.Maui.WinUI.WindowsShellPlatform</c>; Android in phase M): the file dialogs, the keys pressed while the focus is
/// outside the editor (sent to the host's key bindings, so Ctrl+O works from the tree too), and the question before the
/// window closes with unsaved changes.
/// </summary>
public interface IShellPlatform
{
    /// <summary>The platform's file dialogs.</summary>
    IFileDialogs FileDialogs { get; }

    /// <summary>
    /// Sends the keys <paramref name="window"/> receives that no control handled to <paramref name="handler"/> (with no
    /// editor, so only key bindings run) until the result is disposed. Call it once the window has its platform view.
    /// </summary>
    IDisposable RouteKeys(Window window, IEditorKeyHandler handler);

    /// <summary>
    /// Before <paramref name="window"/> closes while <paramref name="hasUnsaved"/> says so, asks <paramref name="confirm"/>
    /// and keeps the window open unless it answers true, until the result is disposed. Call it once the window has its
    /// platform view.
    /// </summary>
    IDisposable GuardClose(Window window, Func<bool> hasUnsaved, Func<Task<bool>> confirm);

    /// <summary>The platform's terminal host (phase T: ConPTY on Windows), registered while the shell runs; null when it has none (the library's "pipe" serves).</summary>
    ITerminalHost? TerminalHost => null;

    /// <summary>The bundled code font as the platform's drawn surface reaches it (the owner's note 14: Windows names its files), or null.</summary>
    IDrawnFonts? DrawnFonts => null;

    /// <summary>
    /// The platform's own record of a crash (the owner's note 21: Windows' Application event log), registered in
    /// <see cref="Craft.Core.Abstractions.CrashRecordSources"/> while the shell runs; null when it has none.
    /// </summary>
    Craft.Core.Abstractions.ICrashRecordSource? CrashRecords => null;
}
