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
/// <param name="Text">The name.</param>
/// <param name="Indent">The row's indent.</param>
/// <param name="Color">The text's colour.</param>
/// <param name="Path">The entry's full path (a row is found by it, so rows after an inserted one stay the same items).</param>
/// <param name="Active">Whether it is the active file's row (on the selection's fill).</param>
/// <param name="FontFamily">The interface's font.</param>
/// <param name="IsFolder">Whether it is a folder's row.</param>
/// <param name="Badge">A folder's chevron ("▸", "▾"), or "" for a file.</param>
/// <param name="BadgeColor">The chevron's colour.</param>
/// <param name="StateLetter">The file's source control state's letter (X part 8: "M", "U"), or "".</param>
/// <param name="StateColor">The name's and the letter's colour for that state, or null for the row's own.</param>
/// <param name="Editing">Whether its name is being typed in place (X part 7: rename, or a new entry's row).</param>
/// <param name="EditText">The name the field starts with.</param>
/// <param name="Icon">The file's or the folder's icon (W: <see cref="FileIcons.For"/>, <see cref="FileIcons.ForFolder"/>, as quick open shows it).</param>
/// <param name="Open">Whether a folder's icon is drawn open.</param>
/// <param name="Dark">Whether the theme is dark (the icon's colours).</param>
/// <param name="Dimmed">Whether it is a folder the workspace skips or lies in one (W: node_modules, bin, obj), drawn dimmed.</param>
public sealed record FolderTreeItem(string Text, Thickness Indent, Color Color, string Path, bool Active, string? FontFamily, bool IsFolder = false, string Badge = "", Color? BadgeColor = null,
    string StateLetter = "", Color? StateColor = null, bool Editing = false, string EditText = "", FileIcon? Icon = null, bool Open = false, bool Dark = true, bool Dimmed = false);

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
    private TreeEdit? _edit;
    private RoutedEntry? _editor;
    private bool _committing;

    /// <summary>An empty tree (set <see cref="Tree"/>).</summary>
    public FolderTreeView()
    {
        // X part 7: the space under the rows takes a right-click too: the workspace's menu (New file, Paste, Collapse all…).
        var footer = new BoxView { Color = Colors.Transparent, HeightRequest = 160 };
        var footerMenu = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        footerMenu.Tapped += (_, e) => ContextRequested?.Invoke(this, (null, e));
        footer.GestureRecognizers.Add(footerMenu);
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.None,                     // a row's own tap opens it (below), not the list's selection
            ItemTemplate = new DataTemplate(MakeRow),
            ItemsSource = _items,
            Footer = footer,
        };
        Content = _list;
    }

    /// <summary>The name being typed in place (X part 7: <see cref="ShellModel.TreeEdit"/>), or null; its row shows a field.</summary>
    public TreeEdit? Edit
    {
        get => _edit;
        set
        {
            if (value != _edit)
            {
                _edit = value;
                if (value is null)
                {
                    _editor = null;
                }

                Refresh();
            }
        }
    }

    /// <summary>Raised as the name in place is typed (the page says what is wrong with it: <see cref="EditProblem"/>).</summary>
    public event EventHandler<string>? EditTextChanged;

    /// <summary>Raised with the name typed in place when Enter is pressed or the field loses the keyboard.</summary>
    public event EventHandler<string>? EditCommitted;

    /// <summary>Raised when Escape ends the name in place without a change.</summary>
    public event EventHandler? EditCancelled;

    /// <summary>Raises the workspace's menu (the explorer's header was right-clicked).</summary>
    public void RequestRootMenu(TappedEventArgs where) => ContextRequested?.Invoke(this, (null, where));

    /// <summary>Why the name typed in place cannot be (its field shows the error's colour and says it in its tooltip), or null.</summary>
    public string? EditProblem
    {
        set
        {
            if (_editor is not { } editor)
            {
                return;
            }

            editor.TextColor = value is null ? _textColor : (Palette?.Error ?? Colors.IndianRed);
            ToolTipProperties.SetText(editor, value ?? "");
        }
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

    /// <summary>
    /// The source control state of a file by its full path (X part 8: its letter and colour), or null for none; call
    /// <see cref="RefreshStates"/> when the states changed.
    /// </summary>
    public Func<string, (string Letter, Color Color)?>? StateOf { get; set; }

    /// <summary>Shows the files' states again (the repository's status changed).</summary>
    public void RefreshStates() => Refresh();

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

    /// <summary>The key of a dragged file row's full path in the drag's properties (a drop on the window opens it).</summary>
    public const string DragPathKey = "craft.path";

    /// <summary>Whether a file's row can be dragged (onto the editor, to open it).</summary>
    public bool DragsFiles { get; set; }

    /// <summary>The key of a dragged folder row's full path in the drag's properties (a drop on a folder of the tree moves it).</summary>
    public const string DragFolderKey = "craft.folder";

    /// <summary>
    /// Whether a row dragged onto a folder's row moves there (the X restart: <c>CRAFT_TREE_DRAG_MOVE</c>): folders can be
    /// dragged too, every folder's row takes a drop (a file's row takes it for its folder, so the workspace's root is
    /// reachable), the row under the drag shows it.
    /// </summary>
    public bool MovesEntries { get; set; } = true;

    /// <summary>Whether a drop copies rather than moves (the platform's Ctrl at the drop), or null (moves).</summary>
    public Func<DropEventArgs, bool>? CopiesOnDrop { get; set; }

    /// <summary>Raised when a row was dropped on a folder: the dragged path, the folder, and whether to copy.</summary>
    public event EventHandler<(string Source, string TargetFolder, bool Copy)>? MoveRequested;

    /// <summary>Raised with a file's full path when its row is picked.</summary>
    public event EventHandler<string>? FileActivated;

    /// <summary>Raised with a file's full path when its row is double-clicked (X part 9: its tab kept, not a preview).</summary>
    public event EventHandler<string>? FileKept;

    /// <summary>
    /// Raised when a row is right-clicked (its item, null for the tree's empty space) with where: the page opens the tree's
    /// context menu (<see cref="EditorContextMenus.Tree"/>) on it.
    /// </summary>
    public event EventHandler<(FolderTreeItem? Item, TappedEventArgs Where)>? ContextRequested;

    /// <summary>The palette the rows' hover, pressed and active looks come from (phase X), or null before the explorer sets it.</summary>
    public ShellPalette? Palette { get; set; }

    /// <summary>Scrolls <paramref name="path"/>'s row into view (a command revealed it; its folders are open).</summary>
    public void ScrollTo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Refresh();
        foreach (var item in _items)
        {
            if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                _list.ScrollTo(item, position: ScrollToPosition.MakeVisible, animate: false);
                return;
            }
        }
    }

    private ContentView MakeRow()
    {
        var label = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, Padding = new Thickness(0, 4), VerticalOptions = LayoutOptions.Center };
        label.SetBinding(Label.TextProperty, static (FolderTreeItem item) => item.Text);
        label.SetBinding(Label.FontFamilyProperty, static (FolderTreeItem item) => item.FontFamily);
        // A folder's chevron, then the file's or the folder's drawn icon (W: FileIcons, a plug-in's too; quick open's).
        var badge = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, WidthRequest = 14, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
        badge.SetBinding(Label.TextProperty, static (FolderTreeItem item) => item.Badge);
        badge.SetBinding(Label.TextColorProperty, static (FolderTreeItem item) => item.BadgeColor);
        var icon = new FileIconView(16);
        icon.SetBinding(FileIconView.IconProperty, static (FolderTreeItem item) => item.Icon);
        icon.SetBinding(FileIconView.OpenProperty, static (FolderTreeItem item) => item.Open);
        icon.SetBinding(FileIconView.DarkProperty, static (FolderTreeItem item) => item.Dark);
        var lead = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { badge, icon } };
        // X part 8: the file's source control letter at the row's right, in its state's colour.
        var state = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(4, 0, 0, 0) };
        state.SetBinding(Label.TextProperty, static (FolderTreeItem item) => item.StateLetter);
        state.SetBinding(Label.TextColorProperty, static (FolderTreeItem item) => item.StateColor);
        var line = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 4 };
        line.SetBinding(MarginProperty, static (FolderTreeItem item) => item.Indent);
        line.Add(lead, 0, 0);
        line.Add(label, 1, 0);
        line.Add(state, 2, 0);
        // X part 7: the name typed in place (rename, new file, new folder) over the label, on the row's own line.
        var field = new RoutedEntry { FontSize = 13, HeightRequest = 26, IsVisible = false, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(-4, 0, 0, 0) };
        field.RouteKey = (key, modifiers) =>
        {
            if (key == EditorKey.Enter && modifiers == KeyModifiers.None)
            {
                CommitEdit(field);
                return true;
            }

            if (key == EditorKey.Escape)
            {
                _committing = true;
                EditCancelled?.Invoke(this, EventArgs.Empty);
                _committing = false;
                return true;
            }

            return false;
        };
        field.TextChanged += (_, e) =>
        {
            if (field.IsVisible && ReferenceEquals(_editor, field))
            {
                EditTextChanged?.Invoke(this, e.NewTextValue ?? "");
            }
        };
        field.Unfocused += (_, _) =>
        {
            if (field.IsVisible && ReferenceEquals(_editor, field))
            {
                CommitEdit(field);
            }
        };
        line.Add(field, 1, 0);
        var row = new ContentView { Content = line };
        // Phase X: the row's hover, pressed and active looks from the theme (one mechanism); its text follows them.
        InteractiveStates? states = null;
        row.BindingContextChanged += (_, _) =>
        {
            if (Palette is not { } palette || row.BindingContext is not FolderTreeItem item)
            {
                return;
            }

            states ??= InteractiveStates.Attach(row, palette, InteractionKind.Row);
            states.Label = label;
            states.Apply(palette);
            states.SetText(item.StateColor);                 // a changed file's name in its state's colour
            states.Selected = item.Active;
            line.Opacity = item.Dimmed ? 0.5 : 1;             // W: a skipped folder (node_modules) and what is in it, dimmed
            label.IsVisible = !item.Editing;
            field.IsVisible = item.Editing;
            if (item.Editing && !ReferenceEquals(_editor, field))
            {
                // The field takes the keyboard with the name selected (a file's without its extension).
                _editor = field;
                field.FontFamily = item.FontFamily;
                field.TextColor = _textColor;
                field.Text = item.EditText;
                int stem = item.IsFolder ? item.EditText.Length : System.IO.Path.GetFileNameWithoutExtension(item.EditText).Length;
                Dispatcher.DispatchLabelled("explorer: the name in place takes the keyboard", () =>
                {
                    field.Focus();
                    field.CursorPosition = 0;
                    field.SelectionLength = stem > 0 ? stem : item.EditText.Length;
                });
            }
        };
        // A click opens the file or toggles the folder through the row's own tap: on Windows the drag recognizer below
        // takes the press from the list, so the list's selection never changed and a click opened nothing (the owner's
        // run, 2026-11-01).
        var open = new TapGestureRecognizer { Buttons = ButtonsMask.Primary };
        open.Tapped += (_, _) =>
        {
            if (row.BindingContext is FolderTreeItem item)
            {
                Activate(item);
            }
        };
        row.GestureRecognizers.Add(open);
        // X part 9: a double click on a file keeps its tab (a single click previews it).
        var keep = new TapGestureRecognizer { Buttons = ButtonsMask.Primary, NumberOfTapsRequired = 2 };
        keep.Tapped += (_, _) =>
        {
            if (row.BindingContext is FolderTreeItem { IsFolder: false } item)
            {
                FileKept?.Invoke(this, item.Path);
            }
        };
        row.GestureRecognizers.Add(keep);
        var context = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
        context.Tapped += (_, e) => ContextRequested?.Invoke(this, (row.BindingContext as FolderTreeItem, e));
        row.GestureRecognizers.Add(context);
        // Phase X: a file's row dragged onto the editor opens it (the drop is the window's); the X restart: a row dragged
        // onto a folder's row moves there (a folder's row too).
        var drag = new DragGestureRecognizer { CanDrag = true };
        drag.DragStarting += (_, e) =>
        {
            if (row.BindingContext is not FolderTreeItem item || _tree is not { } tree || tree.IndexOf(item.Path) is not (>= 0 and var index))
            {
                e.Cancel = true;
                return;
            }

            if (tree.Rows[index].Entry.IsFolder)
            {
                if (!MovesEntries)
                {
                    e.Cancel = true;
                    return;
                }

                e.Data.Properties[DragFolderKey] = item.Path;
                return;
            }

            if (!DragsFiles && !MovesEntries)
            {
                e.Cancel = true;
                return;
            }

            e.Data.Properties[DragPathKey] = item.Path;
            e.Data.Text = item.Path;
        };
        row.GestureRecognizers.Add(drag);
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) =>
        {
            if (MovesEntries && DraggedEntry(e.Data) is { } dragged && row.BindingContext is FolderTreeItem target && FolderOf(target) is { } folder
                && !string.Equals(dragged, folder, StringComparison.OrdinalIgnoreCase))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                row.BackgroundColor = (Palette?.Accent ?? Colors.Orange).WithAlpha(0.18f);
            }
        };
        drop.DragLeave += (_, _) => row.BackgroundColor = Colors.Transparent;
        drop.Drop += (_, e) =>
        {
            row.BackgroundColor = Colors.Transparent;
            if (!MovesEntries || DraggedEntry(e.Data) is not { } dragged || row.BindingContext is not FolderTreeItem target || FolderOf(target) is not { } folder)
            {
                return;
            }

            e.Handled = true;                            // not the window's drop: nothing opens
            MoveRequested?.Invoke(this, (dragged, folder, CopiesOnDrop?.Invoke(e) ?? false));
        };
        row.GestureRecognizers.Add(drop);
        return row;
    }

    // The tree row a drag carries (a file's or a folder's path), or null for anything else.
    private static string? DraggedEntry(DataPackage data) =>
        data.Properties.TryGetValue(DragPathKey, out var file) && file is string filePath ? filePath
        : data.Properties.TryGetValue(DragFolderKey, out var folder) && folder is string folderPath ? folderPath
        : null;

    private static string? DraggedEntry(DataPackageView data) =>
        data.Properties.TryGetValue(DragPathKey, out var file) && file is string filePath ? filePath
        : data.Properties.TryGetValue(DragFolderKey, out var folder) && folder is string folderPath ? folderPath
        : null;

    // The folder a drop on a row goes to: the row's folder, or a file row's own folder.
    private static string? FolderOf(FolderTreeItem item) => item.IsFolder ? item.Path : System.IO.Path.GetDirectoryName(item.Path);

    private void OnTreeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var rows = _tree?.Rows ?? [];
        var items = new FolderTreeItem[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool active = !row.Entry.IsFolder && string.Equals(row.Entry.FullPath, _activePath, StringComparison.OrdinalIgnoreCase);
            string badge;
            Color badgeColor;
            var state = row.Entry.IsFolder ? null : StateOf?.Invoke(row.Entry.FullPath);
            FileIcon icon;
            if (row.Entry.IsFolder)
            {
                badge = row.IsExpanded ? "▾" : "▸";
                badgeColor = _textColor;
                icon = FileIcons.ForFolder(row.Entry.Name);
            }
            else
            {
                badge = "";
                badgeColor = _textColor;
                icon = FileIcons.For(row.Entry.Name);
            }

            bool editing = _edit is { } edit && string.Equals(edit.RowPath, row.Entry.FullPath, StringComparison.OrdinalIgnoreCase);
            items[i] = new FolderTreeItem(row.Entry.Name, new Thickness(8 + (row.Depth * _indent), 0, 14, 0), _textColor, row.Entry.FullPath,
                active, _fontFamily, row.Entry.IsFolder, badge, badgeColor, state?.Letter ?? "", state?.Color, editing, editing ? _edit!.Name : "",
                icon, row.Entry.IsFolder && row.IsExpanded, Palette?.IsDark ?? true, row.Entry.IsIgnored);
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

    // The name in place is taken (Enter, or the field lost the keyboard); once per edit.
    private void CommitEdit(RoutedEntry field)
    {
        if (_committing || !ReferenceEquals(_editor, field))
        {
            return;
        }

        _committing = true;
        try
        {
            EditCommitted?.Invoke(this, field.Text ?? "");
        }
        finally
        {
            _committing = false;
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

    private void Activate(FolderTreeItem item)
    {
        if (_tree is null)
        {
            return;
        }

        // Acted on after the tap's handler returns (toggling a folder replaces the items).
        Dispatcher.DispatchLabelled("explorer: open the chosen row", () =>
        {
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
    private PageTabs? _pages;
    private bool _focusedGroup = true;
    private readonly HorizontalStackLayout _strip = new() { Spacing = 0 };
    private readonly Grid _row;
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

        // The strip and the empty space after the tabs: a double tap there makes a new file (phase X).
        var empty = new BoxView { Color = Colors.Transparent, MinimumWidthRequest = 48 };
        var newFile = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        newFile.Tapped += (_, _) => NewFileRequested?.Invoke(this, EventArgs.Empty);
        empty.GestureRecognizers.Add(newFile);
        _row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)] };
        _row.Add(_strip, 0, 0);
        _row.Add(empty, 1, 0);
        _scroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _row, HorizontalScrollBarVisibility = ScrollBarVisibility.Default };
        Content = _scroll;
        tabs.TabsChanged += OnTabsChanged;
        tabs.ActiveChanged += OnTabsChanged;
        tabs.TabChanged += OnTabChanged;
        Rebuild();
    }

    /// <summary>The page tabs it shows after the documents' (the first editor group's strip), or null.</summary>
    public PageTabs? Pages
    {
        get => _pages;
        set
        {
            if (ReferenceEquals(_pages, value))
            {
                return;
            }

            if (_pages is not null)
            {
                _pages.Changed -= OnTabsChanged;
            }

            _pages = value;
            if (value is not null)
            {
                value.Changed += OnTabsChanged;
            }

            Rebuild();
        }
    }

    /// <summary>
    /// Whether its group is the focused one (the split editor): the active tab keeps the accent's line; in a group not
    /// focused the line is the strong border's, so the focus reads at a glance.
    /// </summary>
    public bool FocusedGroup
    {
        get => _focusedGroup;
        set
        {
            if (value != _focusedGroup)
            {
                _focusedGroup = value;
                Rebuild();
            }
        }
    }

    /// <summary>Raised when another group's tab is dropped on one of these tabs (the page moves it here, at that place).</summary>
    public event EventHandler<(DocumentTab Dragged, DocumentTab Target)>? ForeignTabDropped;

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

    /// <summary>Raised when a pinned tab's pin is clicked (X part 7: the page unpins it).</summary>
    public event EventHandler<DocumentTab>? UnpinRequested;

    /// <summary>Raised when a preview tab is double-clicked (X part 9: the page keeps it).</summary>
    public event EventHandler<DocumentTab>? KeepRequested;

    /// <summary>Raised when the compact switcher's title is clicked (the page lists the tabs).</summary>
    public event EventHandler? ListRequested;

    /// <summary>Raised when a document's tab is right-clicked, with where: the page opens the tab's context menu (<see cref="EditorContextMenus.Tab"/>).</summary>
    public event EventHandler<(DocumentTab Tab, TappedEventArgs Where)>? ContextRequested;

    /// <summary>The tooltip of an action and its command (the keys in effect: <see cref="ShellTooltips.For"/>); null shows the action alone.</summary>
    public Func<string, string, string>? Tooltip { get; set; }

    /// <summary>Draws the tabs again (the keys in the tooltips changed).</summary>
    public void Refresh() => Rebuild();

    /// <summary>Raised on a double-click (a double tap) on the strip's empty space: a new file.</summary>
    public event EventHandler? NewFileRequested;

    /// <summary>The key of a dragged tab in the drag's properties (a drop on another tab moves it there).</summary>
    public const string DragTabKey = "craft.tab";

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
            shown.Title.FontAttributes = tab.IsPreview ? FontAttributes.Italic : FontAttributes.None;
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
            title.FontAttributes = tab.IsPreview ? FontAttributes.Italic : FontAttributes.None;   // X part 9: a preview tab's title is italic
            var dot = new Ellipse { WidthRequest = 8, HeightRequest = 8, Fill = new SolidColorBrush(palette.Text), IsVisible = tab.IsDirty, VerticalOptions = LayoutOptions.Center };
            View close;
            if (tab.IsPinned)
            {
                // X part 7: a pinned tab shows its pin where the close button is; a click on it unpins.
                var pin = new ContentView { WidthRequest = 24, HeightRequest = 24, Content = new StrokeIcon(MenuIcons.Pin, 14, 1.5) { Color = palette.Secondary, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } };
                var unpin = new TapGestureRecognizer();
                unpin.Tapped += (_, _) => UnpinRequested?.Invoke(this, tab);
                pin.GestureRecognizers.Add(unpin);
                palette.States(pin, InteractionKind.Flat);
                ToolTipProperties.SetText(pin, "Pinned: click to unpin");
                close = pin;
            }
            else
            {
                var button = palette.Flat("×", palette.Secondary, 24, palette.SmallSize);
                button.WidthRequest = 24;
                button.Padding = new Thickness(0);
                ToolTipProperties.SetText(button, CloseTip());
                button.Clicked += (_, _) => CloseRequested?.Invoke(this, tab);
                close = button;
            }

            // W: the file's drawn icon before its title (the tree's and quick open's).
            var icon = FileIconView.Of(FileIcons.For(tab.FilePath ?? tab.Title), palette, 15);
            icon.Opacity = isActive ? 1 : 0.8;
            var row = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(12, 0, 6, 0), VerticalOptions = LayoutOptions.Center, Children = { icon, title, dot, close } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _pages?.ShowDocument();
                _tabs.Activate(tab);
            };
            _titles[tab] = (title, dot);
            var cell = Cell(palette, row, title, isActive, tab.FilePath ?? tab.Title, tap, _focusedGroup);
            var context = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
            context.Tapped += (_, e) => ContextRequested?.Invoke(this, (tab, e));
            cell.GestureRecognizers.Add(context);
            var keep = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            keep.Tapped += (_, _) => KeepRequested?.Invoke(this, tab);
            cell.GestureRecognizers.Add(keep);
            if (tab.IsPreview)
            {
                ToolTipProperties.SetText(cell, (tab.FilePath ?? tab.Title) + "\nPreview: the next file opened from the explorer takes its place; double-click or edit to keep it");
            }
            Draggable(cell, tab);
            _strip.Children.Add(cell);
        }

        foreach (var open in _pages?.Tabs ?? [])
        {
            bool isActive = ReferenceEquals(open, page);
            var title = palette.Label(open.Title, isActive ? palette.Text : palette.Secondary);
            var close = palette.Flat("×", palette.Secondary, 24, palette.SmallSize);
            close.WidthRequest = 24;
            close.Padding = new Thickness(0);
            ToolTipProperties.SetText(close, CloseTip());
            var shown = open;
            close.Clicked += (_, _) => _pages!.Close(shown.Id);
            var row = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(14, 0, 6, 0), VerticalOptions = LayoutOptions.Center, Children = { title, close } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _pages!.Activate(shown);
            _strip.Children.Add(Cell(palette, row, title, isActive, open.Title, tap, _focusedGroup));
        }
    }

    private string CloseTip() => Tooltip?.Invoke("Close", ShellCommandIds.CloseTab) ?? "Close";

    // Phase X: a document's tab dragged along the strip and dropped on another moves there (DocumentTabs.MoveOnto).
    private void Draggable(View cell, DocumentTab tab)
    {
        var drag = new DragGestureRecognizer { CanDrag = true };
        drag.DragStarting += (_, e) => e.Data.Properties[DragTabKey] = tab;
        cell.GestureRecognizers.Add(drag);
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Copy;     // a file from outside may land here too (the window's drop opens it)
        drop.Drop += (_, e) =>
        {
            if (e.Data.Properties.TryGetValue(DragTabKey, out var value) && value is DocumentTab dragged)
            {
                e.Handled = true;
                if (_tabs.Contains(dragged))
                {
                    _tabs.MoveOnto(dragged, tab);
                }
                else
                {
                    ForeignTabDropped?.Invoke(this, (dragged, tab));     // another group's tab: the page moves it here
                }
            }
        };
        cell.GestureRecognizers.Add(drop);
    }

    // A tab's cell: the accent's line over the active one, on the surface; a border between the others; its hover and
    // pressed looks (phase X) from the theme, its title following them.
    private static Grid Cell(ShellPalette palette, View row, Label title, bool isActive, string tooltip, TapGestureRecognizer tap, bool focusedGroup = true)
    {
        var cell = new Grid
        {
            RowDefinitions = [new RowDefinition(new GridLength(2)), new RowDefinition(GridLength.Star)],
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(1))],
            HeightRequest = 36,
        };
        var states = palette.States(cell, InteractionKind.Tab);
        states.Label = title;
        states.Selected = isActive;
        var accentLine = new BoxView { Color = isActive ? (focusedGroup ? palette.Accent : palette.BorderStrong) : Colors.Transparent };
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

    /// <inheritdoc />
    public Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default) =>
        _dialog.PromptAsync(title, text, placeholder, ok, cancellationToken, initial);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string text, string action, bool danger = false, CancellationToken cancellationToken = default) =>
        _dialog.ConfirmAsync(title, text, action, danger, cancellationToken);
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
    /// The platform's laid-out text for the drawn surfaces (B1: Win2D's text layouts on Windows, one kept per shown line),
    /// or null (each run drawn with MAUI's <c>DrawString</c>).
    /// </summary>
    IDrawnTextLayouts? TextLayouts => null;

    /// <summary>
    /// Whether <paramref name="window"/> has its platform view and content, so <see cref="RouteKeys"/> and
    /// <see cref="GuardClose"/> can hook it; the shell asks again later while it has not (B2's trail: no exception thrown
    /// at start for a window WinUI has not filled yet).
    /// </summary>
    bool IsReady(Window window) => window?.Handler?.PlatformView is not null;

    /// <summary>
    /// The platform's own record of a crash (the owner's note 21: Windows' Application event log), registered in
    /// <see cref="Craft.Core.Abstractions.CrashRecordSources"/> while the shell runs; null when it has none.
    /// </summary>
    Craft.Core.Abstractions.ICrashRecordSource? CrashRecords => null;

    /// <summary>The system's file manager and trash (phase X's Reveal and Delete: File Explorer and the Recycle Bin on Windows), or null.</summary>
    IShellFiles? ShellFiles => null;

    /// <summary>The pointer's shape and captured drags (B8: the splitters' resize arrows), or null (MAUI's pan gesture serves).</summary>
    IPointerCursor? PointerCursor => null;

    /// <summary>
    /// The platform's terminal profiles (B9: Windows reads the registry for WSL and Git for Windows), registered in
    /// <see cref="TerminalProfiles"/> over the library's "machine" source while the shell runs; null keeps the library's.
    /// </summary>
    ITerminalProfileSource? TerminalProfiles => null;

    /// <summary>
    /// The paths of the files and folders a drop from outside the app carries (the system's file manager), read from the
    /// platform's drop data while the drop lasts; none when it carries none, or on a platform without the mapping.
    /// </summary>
    Task<IReadOnlyList<string>> DroppedPathsAsync(DropEventArgs drop) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// Whether the user asked a drop to copy rather than move (Ctrl held when it landed: a tree row dragged onto a folder
    /// copies it); false on a platform that does not say.
    /// </summary>
    bool DropCopies(DropEventArgs drop) => false;
}
