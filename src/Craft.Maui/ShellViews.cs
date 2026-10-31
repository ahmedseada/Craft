// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
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
/// <param name="Index">The row's index in the tree.</param>
public sealed record FolderTreeItem(string Text, Thickness Indent, Color Color, int Index);

/// <summary>
/// The folder tree (<see cref="FolderTree"/>) as a virtualized list (only the rows on screen are made): a folder's row
/// opens or closes it, a file's row raises <see cref="FileActivated"/>.
/// </summary>
public sealed class FolderTreeView : ContentView
{
    private readonly CollectionView _list;
    private FolderTree? _tree;
    private Color _textColor = Colors.Gray;
    private float _indent = 14;

    /// <summary>An empty tree (set <see cref="Tree"/>).</summary>
    public FolderTreeView()
    {
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(MakeRow),
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

    /// <summary>Raised with a file's full path when its row is picked.</summary>
    public event EventHandler<string>? FileActivated;

    private static Label MakeRow()
    {
        var label = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, Padding = new Thickness(0, 3) };
        label.SetBinding(Label.TextProperty, static (FolderTreeItem item) => item.Text);
        label.SetBinding(MarginProperty, static (FolderTreeItem item) => item.Indent);
        label.SetBinding(Label.TextColorProperty, static (FolderTreeItem item) => item.Color);
        return label;
    }

    private void OnTreeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_tree is null)
        {
            _list.ItemsSource = null;
            return;
        }

        var rows = _tree.Rows;
        var items = new FolderTreeItem[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var marker = row.Entry.IsFolder ? row.IsExpanded ? "▾ " : "▸ " : "   ";
            items[i] = new FolderTreeItem(marker + row.Entry.Name, new Thickness(6 + (row.Depth * _indent), 0, 6, 0), _textColor, i);
        }

        _list.ItemsSource = items;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not FolderTreeItem item || _tree is null)
        {
            return;
        }

        // Acted on after the list finished its selection (the items may be replaced).
        Dispatcher.Dispatch(() =>
        {
            _list.SelectedItem = null;
            if (_tree is not { } tree || item.Index >= tree.Rows.Count)
            {
                return;
            }

            var row = tree.Rows[item.Index];
            if (row.Entry.IsFolder)
            {
                tree.Toggle(item.Index);
            }
            else
            {
                FileActivated?.Invoke(this, row.Entry.FullPath);
            }
        });
    }
}

/// <summary>
/// The tabs (<see cref="DocumentTabs"/>): in the wide layout a scrolling strip of tabs, each its title (a dot when it has
/// unsaved changes) and a close button; in the <see cref="Compact"/> (narrow) layout one switcher: previous, the active
/// tab's title and place (which asks for the list), next, close. A click activates; a close raises
/// <see cref="CloseRequested"/>, so the shell asks about unsaved changes. A tab's title is redrawn only when it changes.
/// </summary>
public sealed class TabStripView : ContentView, IDisposable
{
    private readonly DocumentTabs _tabs;
    private readonly HorizontalStackLayout _strip = new() { Spacing = 2, Padding = new Thickness(4, 2) };
    private readonly ScrollView _scroll;
    private readonly Dictionary<DocumentTab, Button> _titles = [];
    private bool _compact;
    private Color _text = Colors.Gray;
    private Color _background = Colors.Transparent;
    private Color _active = Colors.LightGray;

    /// <summary>The strip of <paramref name="tabs"/>.</summary>
    public TabStripView(DocumentTabs tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs = tabs;
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

    /// <summary>Sets the colours: the text, the strip's background, the active tab's background.</summary>
    public void SetColors(Color text, Color background, Color active)
    {
        _text = text;
        _background = background;
        _active = active;
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
    }

    private void OnTabsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnTabChanged(object? sender, DocumentTab tab)
    {
        if (_compact)
        {
            Rebuild();
        }
        else if (_titles.TryGetValue(tab, out var button))
        {
            button.Text = TitleOf(tab);
        }
    }

    private Button Flat(string text) => new()
    {
        Text = text,
        TextColor = _text,
        BackgroundColor = Colors.Transparent,
        BorderWidth = 0,
        Padding = new Thickness(8, 2),
        FontSize = 13,
        MinimumHeightRequest = 28,
    };

    private void Rebuild()
    {
        _strip.Children.Clear();
        _titles.Clear();
        BackgroundColor = _background;
        IsVisible = _tabs.Tabs.Count > 0;
        if (_compact)
        {
            var active = _tabs.Active;
            var previous = Flat("‹");
            previous.Clicked += (_, _) => _tabs.Previous();
            var title = Flat(active is null ? "" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{TitleOf(active)}  ({_tabs.ActiveIndex + 1}/{_tabs.Tabs.Count}) ▾"));
            title.BackgroundColor = _active;
            title.Clicked += (_, _) => ListRequested?.Invoke(this, EventArgs.Empty);
            var next = Flat("›");
            next.Clicked += (_, _) => _tabs.Next();
            var close = Flat("×");
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

        foreach (var tab in _tabs.Tabs)
        {
            var title = Flat(TitleOf(tab));
            ToolTipProperties.SetText(title, tab.FilePath ?? tab.Title);
            title.Clicked += (_, _) => _tabs.Activate(tab);
            var close = Flat("×");
            ToolTipProperties.SetText(close, "Close (Ctrl+W)");
            close.Clicked += (_, _) => CloseRequested?.Invoke(this, tab);
            var cell = new HorizontalStackLayout { Spacing = 0, Children = { title, close } };
            cell.BackgroundColor = ReferenceEquals(tab, _tabs.Active) ? _active : Colors.Transparent;
            _titles[tab] = title;
            _strip.Children.Add(cell);
        }
    }
}

/// <summary>The shell's prompts as a page's dialogs (MAUI's, no platform code); the settings page is shown by <paramref name="showSettings"/>.</summary>
public sealed class MauiShellUi(Page page, Func<Task> showSettings) : IShellUi
{
    private const string CancelText = "Cancel";
    private const string SaveText = "Save";
    private const string DiscardText = "Don't save";
    private readonly Page _page = page ?? throw new ArgumentNullException(nameof(page));
    private readonly Func<Task> _showSettings = showSettings ?? throw new ArgumentNullException(nameof(showSettings));

    /// <inheritdoc />
    public async Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default)
    {
        var picked = await _page.DisplayActionSheetAsync($"{title} has unsaved changes. Save them?", CancelText, null, SaveText, DiscardText);
        return picked switch
        {
            SaveText => UnsavedChoice.Save,
            DiscardText => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    /// <inheritdoc />
    public async Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return -1;
        }

        var shown = items.ToArray();
        var picked = await _page.DisplayActionSheetAsync(title, CancelText, null, shown);
        return picked is null ? -1 : Array.IndexOf(shown, picked);
    }

    /// <inheritdoc />
    public Task ShowSettingsAsync(CancellationToken cancellationToken = default) => _showSettings();

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) => _page.DisplayAlertAsync(title, message, "OK");
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
}
