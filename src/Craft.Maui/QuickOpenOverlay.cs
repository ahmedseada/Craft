// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using Craft.Abstractions;
using Craft.Editor;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>A row of the overlay's list as it is drawn (the item, its highlighted texts, its icon and keys, its fill).</summary>
/// <param name="Item">The item it picks.</param>
/// <param name="Title">The title with the matched characters highlighted.</param>
/// <param name="Detail">The folder (matched characters highlighted), or null.</param>
/// <param name="Badge">The file icon's badge ("" for a command or a line).</param>
/// <param name="BadgeColor">The badge's colour.</param>
/// <param name="Keys">A command's keys, or "".</param>
/// <param name="Background">Its fill (the selected row's is the selection's).</param>
/// <param name="Recent">"recent" when it was picked recently, else "".</param>
public sealed record QuickOpenRow(QuickOpenItem Item, FormattedString Title, FormattedString? Detail, string Badge, Color BadgeColor, string Keys, Color Background, string Recent);

/// <summary>
/// The command palette and quick open as one overlay (phase X: inline, not modal), under the title bar's search box: an
/// input and a virtualized list (only the rows on screen are made). No prefix finds files, '&gt;' commands with their keys,
/// ':' a line ("42:7"); the matched characters are drawn in the accent, recent picks first and marked, files with their
/// language's icon. Up, Down, Page Up, Page Down, Ctrl+Home and Ctrl+End move, Enter picks, Escape closes (the focus goes
/// back to the editor). A query over a long file list is matched off the UI thread; only the newest query's rows land.
/// </summary>
public sealed class QuickOpenOverlay : ContentView
{
    private const double RowHeight = 30;
    private readonly QuickOpen _model;
    private readonly RoutedEntry _input;
    private readonly CraftInput _inputBox;
    private readonly CollectionView _list;
    private readonly ObservableCollection<QuickOpenRow> _rows = [];
    private readonly Label _hint;
    private readonly EmptyStateView _empty;
    private readonly Label _mode;
    private readonly Border _frame;
    private readonly OverlaySelection _selection = new();
    private IReadOnlyList<QuickOpenItem> _items = [];
    private CancellationTokenSource? _query;
    private ShellPalette _palette;
    private bool _settingText;
    private readonly TypingDebounce _typing;

    /// <summary>The overlay over <paramref name="model"/>, in <paramref name="palette"/>'s colours.</summary>
    public QuickOpenOverlay(QuickOpen model, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);
        _model = model;
        _palette = palette;
        _input = new RoutedEntry { Placeholder = "Search files by name   ·   > commands   ·   @ symbols   ·   : go to a line", HeightRequest = 32, FontSize = palette.FontSize, FontFamily = palette.Font() };
        _input.RouteKey = OnKey;
        // Debounced (CRAFT_SEARCH_DELAY_MS): typing never waits on a match; a key the overlay takes runs a waiting one first.
        _typing = new TypingDebounce(this, () => _palette.Options.SearchDelayMilliseconds, () => _ = RefreshAsync(_input.Text ?? ""));
        _input.TextChanged += (_, _) =>
        {
            if (!_settingText)
            {
                _typing.Restart();
            }
        };
        _inputBox = palette.Field(_input);
        _mode = palette.Label("", palette.Secondary, palette.CaptionSize);
        _mode.Margin = new Thickness(12, 2, 12, 0);
        _hint = palette.Label("", palette.Secondary, palette.SmallSize);
        _hint.Margin = new Thickness(12, 6);
        _hint.LineBreakMode = LineBreakMode.WordWrap;
        _empty = new EmptyStateView(palette) { IsVisible = false };
        _empty.ActionChosen += (_, action) => EmptyActionChosen?.Invoke(this, action);
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsSource = _rows,
            ItemTemplate = new DataTemplate(MakeRow),
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
        };
        var body = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(6), Children = { _inputBox, _mode, _hint, _empty, _list } };
        _frame = new Border
        {
            Content = body,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.35f, Radius = 18, Offset = new Point(0, 6) },
        };
        Content = _frame;
        Apply(palette);
    }

    /// <summary>Raised with the item picked (Enter, or a click); the overlay has closed.</summary>
    public event EventHandler<QuickOpenItem>? Picked;

    /// <summary>What the list says when nothing matches a query (slice 1's <see cref="ShellEmptyStates.Search"/>), or null for a plain hint.</summary>
    public Func<string, EmptyState>? EmptyStateFor { get; set; }

    /// <summary>Raised with an empty state's action picked (open a folder, show every command).</summary>
    public event EventHandler<EmptyAction>? EmptyActionChosen;

    /// <summary>Raised when it closed without a pick (Escape, a click outside).</summary>
    public event EventHandler? Dismissed;

    /// <summary>Raised with the row the arrows moved to ('@' previews its symbol in the editor).</summary>
    public event EventHandler<QuickOpenItem>? SelectionMoved;

    /// <summary>The overlay's input (focused when it opens).</summary>
    public Entry Input => _input;

    /// <summary>The rows it shows on screen at once (<c>CRAFT_QUICK_OPEN_ROWS</c>); the list holds ten pages and scrolls.</summary>
    public int VisibleRows { get; set; } = 12;

    /// <summary>Whether a refresh is running (a background match).</summary>
    public bool IsMatching => _query is not null;

    /// <summary>Starts with <paramref name="text"/> ("" files, "&gt;" commands, ":" a line), the caret at its end, focused.</summary>
    public void Start(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _settingText = true;
        _input.Text = text;
        _settingText = false;
        _input.CursorPosition = text.Length;
        _ = RefreshAsync(text);
        Dispatcher.DispatchLabelled("quick open: focus the input", () => _input.Focus());
    }

    /// <summary>Closes it without a pick.</summary>
    public void Dismiss()
    {
        _query?.Cancel();
        _query = null;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _frame.BackgroundColor = palette.Raised;
        _frame.Stroke = palette.BorderStrong;
        _inputBox.Apply(palette);
        _hint.TextColor = palette.Secondary;
        _mode.TextColor = palette.Secondary;
        _empty.Apply(palette);
        Rebuild();
    }

    // The keys of the open overlay (the input hands them over before its box).
    private bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        _typing.Flush();
        switch (key)
        {
            case EditorKey.Escape:
                Dismiss();
                return true;
            case EditorKey.Enter:
                Pick(_selection.Selected);
                return true;
        }

        if (!_selection.HandleKey(key, modifiers))
        {
            return false;
        }

        Rebuild();
        if (_selection.Selected >= 0 && _selection.Selected < _rows.Count)
        {
            _list.ScrollTo(_selection.Selected, position: ScrollToPosition.MakeVisible, animate: false);
            SelectionMoved?.Invoke(this, _items[_selection.Selected]);
        }

        return true;
    }

    private void Pick(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        var item = _items[index];
        _query?.Cancel();
        _query = null;
        _model.NoteChosen(item);
        Picked?.Invoke(this, item);
    }

    // The newest query's rows: an older one still matching is cancelled, and its rows never land.
    private async Task RefreshAsync(string text)
    {
        if (_query is { } previous)
        {
            await previous.CancelAsync();
        }

        var cancel = new CancellationTokenSource();
        _query = cancel;
        IReadOnlyList<QuickOpenItem> items;
        try
        {
            items = await _model.QueryInBackgroundAsync(text, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_query, cancel))
            {
                _query = null;
            }

            cancel.Dispose();
        }

        if (!string.Equals(text, _input.Text ?? "", StringComparison.Ordinal))
        {
            return;                                               // typed on meanwhile: that query's rows come
        }

        _items = items;
        _selection.Reset(items.Count);
        _selection.PageRows = VisibleRows;
        var mode = QuickOpen.ModeOf(text);
        _mode.Text = mode switch
        {
            QuickOpenMode.Commands => "Commands",
            QuickOpenMode.Line => "Go to a line of the active editor",
            QuickOpenMode.Symbols => text.TrimStart().StartsWith("@:", StringComparison.Ordinal) ? "Symbols by kind" : "Symbols of the active editor   ·   @: groups them by kind",
            _ when _model.BrowsedFolder is { } folder => $"In {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder))}",
            _ => _model.Root is null ? "Files" : $"Files in {System.IO.Path.GetFileName(_model.Root)}",
        };
        _hint.Text = items.Count > 0 ? (_model.Capped && mode == QuickOpenMode.Files ? $"The first {_model.Files.Count:N0} files are listed (CRAFT_QUICK_OPEN_FILES)." : "")
            : mode == QuickOpenMode.Line ? "Type a line number (42, or 42:7 for a column)."
            : mode == QuickOpenMode.Commands ? "No command matches."
            : mode == QuickOpenMode.Symbols ? (_model.Symbols.Count == 0 ? "The active editor's language lists no symbols (or no file is open)." : "No symbol matches.")
            : _model.BrowsedFolder is not null ? "Nothing in this folder matches."
            : _model.Root is null ? "Open a folder (Ctrl+K Ctrl+O) to find its files by name; type '>' for a command."
            : _model.IsIndexing ? "Listing the folder's files…" : "No file matches; type '>' for a command or ':' for a line.";
        bool emptyState = items.Count == 0 && EmptyStateFor is not null && mode != QuickOpenMode.Symbols && _model.BrowsedFolder is null;
        if (emptyState)
        {
            _empty.Show(EmptyStateFor!(text));
            _hint.Text = "";
        }

        _empty.IsVisible = emptyState;
        _hint.IsVisible = _hint.Text.Length > 0;
        Rebuild();
    }

    // Every row again (the selected row's fill moved, or new items): the list keeps only the visible rows' views.
    private void Rebuild()
    {
        var palette = _palette;
        var rows = new QuickOpenRow[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var icon = item.Icon;
            rows[i] = new QuickOpenRow(item, Highlighted(item.Text, item.TextHighlights, palette.Text, palette), item.Detail is { } detail ? Highlighted(detail, item.DetailHighlights, palette.Secondary, palette) : null,
                icon?.Badge ?? (item.SymbolKind is { } kind ? SymbolKinds.Badge(kind) : item.IsFolder ? "▸" : item.CommandId is not null ? "›" : item.Line > 0 ? "#" : ""),
                icon is null ? (item.SymbolKind is ScopeKind.Method ? palette.Method : item.SymbolKind is not null ? palette.Accent : palette.Secondary) : ThemeColors.Of(icon.Argb),
                item.Shortcut ?? "", i == _selection.Selected ? palette.Selection : Colors.Transparent, item.Group ?? (item.IsRecent ? "recent" : ""));
        }

        // Only the rows that differ are replaced (an arrow key moves two fills).
        for (int i = 0; i < rows.Length; i++)
        {
            if (i < _rows.Count)
            {
                if (!ReferenceEquals(_rows[i].Item, rows[i].Item) || _rows[i].Background != rows[i].Background)
                {
                    _rows[i] = rows[i];
                }
            }
            else
            {
                _rows.Add(rows[i]);
            }
        }

        while (_rows.Count > rows.Length)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }

        _list.HeightRequest = Math.Min(Math.Max(1, VisibleRows), Math.Max(0, rows.Length)) * RowHeight;
        _list.IsVisible = rows.Length > 0;
    }

    private static FormattedString Highlighted(string text, IReadOnlyList<(int Start, int Length)> runs, Color color, ShellPalette palette)
    {
        var formatted = new FormattedString();
        int at = 0;
        foreach (var (start, length) in runs)
        {
            if (start < at || start + length > text.Length)
            {
                continue;
            }

            if (start > at)
            {
                formatted.Spans.Add(new Span { Text = text[at..start], TextColor = color });
            }

            formatted.Spans.Add(new Span { Text = text.Substring(start, length), TextColor = palette.Accent, FontFamily = palette.Font(FontWeight.SemiBold) });
            at = start + length;
        }

        if (at < text.Length)
        {
            formatted.Spans.Add(new Span { Text = text[at..], TextColor = color });
        }

        return formatted;
    }

    private Border MakeRow()
    {
        var palette = _palette;
        var badge = new Label { FontSize = palette.CaptionSize, FontFamily = palette.Font(FontWeight.SemiBold), WidthRequest = 22, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
        badge.SetBinding(Label.TextProperty, static (QuickOpenRow row) => row.Badge);
        badge.SetBinding(Label.TextColorProperty, static (QuickOpenRow row) => row.BadgeColor);
        var title = new Label { FontSize = palette.FontSize, FontFamily = palette.Font(), LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center };
        title.SetBinding(Label.FormattedTextProperty, static (QuickOpenRow row) => row.Title);
        var detail = new Label { FontSize = palette.SmallSize, FontFamily = palette.Font(), LineBreakMode = LineBreakMode.HeadTruncation, VerticalOptions = LayoutOptions.Center };
        detail.SetBinding(Label.FormattedTextProperty, static (QuickOpenRow row) => row.Detail);
        var recent = new Label { FontSize = palette.CaptionSize, FontFamily = palette.Font(), TextColor = palette.Secondary, VerticalOptions = LayoutOptions.Center };
        recent.SetBinding(Label.TextProperty, static (QuickOpenRow row) => row.Recent);
        var keys = new Label { FontSize = palette.SmallSize, FontFamily = palette.Options.FontFamily, TextColor = palette.Secondary, VerticalOptions = LayoutOptions.Center };
        keys.SetBinding(Label.TextProperty, static (QuickOpenRow row) => row.Keys);
        var cell = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8,
            Padding = new Thickness(8, 0),
            HeightRequest = RowHeight,
        };
        cell.Add(badge, 0, 0);
        cell.Add(title, 1, 0);
        cell.Add(detail, 2, 0);
        cell.Add(recent, 3, 0);
        cell.Add(keys, 4, 0);
        var row = new Border { Content = cell, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
        row.SetBinding(BackgroundColorProperty, static (QuickOpenRow row) => row.Background);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (row.BindingContext is QuickOpenRow shown)
            {
                Pick(IndexOf(shown.Item));
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private int IndexOf(QuickOpenItem item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
