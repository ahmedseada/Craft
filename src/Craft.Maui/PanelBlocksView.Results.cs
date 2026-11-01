// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

// X part 6: the search field and the results list of a view panel. Both are kept by their id across redraws, so typing
// keeps the keyboard in the field, and a new batch of results changes only the rows that differ (the scroll stays).
public sealed partial class PanelBlocksView
{
    private readonly Dictionary<string, QueryField> _queries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResultsView> _results = new(StringComparer.Ordinal);

    private QueryField Query(PanelQuery query, bool nested)
    {
        if (!_queries.TryGetValue(query.ActionId, out var field))
        {
            field = new QueryField(this, query.ActionId);
            _queries[query.ActionId] = field;
        }

        if (nested && field.Parent is not null)
        {
            Detach(field);                                         // inside a card drawn again: out of the old one first
        }

        field.Update(query);
        return field;
    }

    private ResultsView Results(PanelResults results, bool nested)
    {
        if (!_results.TryGetValue(results.Id, out var view))
        {
            view = new ResultsView(this);
            _results[results.Id] = view;
        }

        if (nested && view.Parent is not null)
        {
            Detach(view);
        }

        view.Update(results);
        if (nested)
        {
            view.HeightRequest = 320;                               // inside a card: a fixed height of its own that scrolls
        }

        return view;
    }

    private static void Detach(View view)
    {
        switch (view.Parent)
        {
            case Layout layout:
                layout.Remove(view);
                break;
            case ContentView content when ReferenceEquals(content.Content, view):
                content.Content = null;
                break;
            case Border border when ReferenceEquals(border.Content, view):
                border.Content = null;
                break;
            case ScrollView scroll when ReferenceEquals(scroll.Content, view):
                scroll.Content = null;
                break;
        }
    }

    // An icon button: the icon, the title as its tooltip and name, the palette's hover, pressed and focus looks.
    private Border IconButton(PanelButton button, double size = 24)
    {
        var p = _palette;
        var color = button.Kind switch
        {
            PanelButtonKind.Primary => p.Accent,
            PanelButtonKind.Danger => p.Error,
            _ => p.Secondary,
        };
        var icon = new StrokeIcon(button.Icon ?? RailIcons.Panel, size * 0.62) { Color = color, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
        var box = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            BackgroundColor = Colors.Transparent,
            Content = icon,
            Opacity = button.Enabled ? 1 : 0.4,
            VerticalOptions = LayoutOptions.Center,
        };
        ToolTipProperties.SetText(box, button.Tooltip ?? button.Title);
        SemanticProperties.SetDescription(box, button.Title);
        p.States(box, InteractionKind.Flat);
        if (button.Enabled)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Act(button.ActionId, button.Value);
            box.GestureRecognizers.Add(tap);
        }

        return box;
    }

    /// <summary>
    /// A search field: Craft's input style with the toggles inside at its right (on in the accent's fill, as the find
    /// overlay's) and the icon buttons after it; every change of the text runs the action, Enter the submit action; the
    /// keyboard taken when the panel raises <see cref="PanelQuery.Focus"/>.
    /// </summary>
    private sealed class QueryField : ContentView
    {
        private readonly PanelBlocksView _owner;
        private readonly string _action;
        private readonly Entry _entry;
        private readonly CraftInput _field;
        private readonly HorizontalStackLayout _toggles = new() { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        private readonly HorizontalStackLayout _buttons = new() { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        private PanelQuery? _shown;
        private string? _submit;
        private bool _setting;
        private int _focus;

        public QueryField(PanelBlocksView owner, string action)
        {
            _owner = owner;
            _action = action;
            var p = owner._palette;
            _entry = p.Input("", 30);
            _entry.ReturnType = ReturnType.Search;
            _entry.TextChanged += (_, e) =>
            {
                if (!_setting)
                {
                    _owner.Act(_action, e.NewTextValue ?? "");
                }
            };
            _entry.Completed += (_, _) =>
            {
                if (_submit is { } submit)
                {
                    _owner.Act(submit, _entry.Text ?? "");
                }
            };
            _field = p.Field(_entry);
            // The toggles sit inside the field, at its right.
            var inside = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 4 };
            _field.Content = inside;
            inside.Add(_entry, 0, 0);
            inside.Add(_toggles, 1, 0);
            _field.HeightRequest = 32;
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 4 };
            row.Add(_field, 0, 0);
            row.Add(_buttons, 1, 0);
            Content = row;
        }

        public void Update(PanelQuery query)
        {
            var p = _owner._palette;
            _submit = query.SubmitActionId;
            _entry.Placeholder = query.Placeholder;
            _entry.FontFamily = query.Code ? p.Options.FontFamily : p.Font();
            SemanticProperties.SetDescription(_entry, query.Placeholder);
            if (!string.Equals(_entry.Text ?? "", query.Value, StringComparison.Ordinal) && !(_entry.IsFocused && _shown is not null && _shown.Value == query.Value))
            {
                _setting = true;
                _entry.Text = query.Value;
                _setting = false;
            }

            ToolTipProperties.SetText(_field, query.Problem ?? query.Tooltip ?? "");
            _field.Stroke = query.Problem is not null ? p.Error : _field.Stroke;
            if (_shown is null || !SameControls(_shown, query))
            {
                _toggles.Children.Clear();
                foreach (var toggle in query.Toggles)
                {
                    _toggles.Children.Add(Toggle(toggle));
                }

                _buttons.Children.Clear();
                foreach (var button in query.Buttons)
                {
                    _buttons.Children.Add(_owner.IconButton(button, 28));
                }
            }

            _shown = query;
            if (query.Focus > _focus)
            {
                _focus = query.Focus;
                Dispatcher?.Dispatch(() =>
                {
                    _entry.Focus();
                    _entry.CursorPosition = 0;
                    _entry.SelectionLength = (_entry.Text ?? "").Length;   // Ctrl+Shift+F again: type over the query
                });
            }
        }

        private static bool SameControls(PanelQuery a, PanelQuery b) =>
            a.Toggles.SequenceEqual(b.Toggles) && a.Buttons.Count == b.Buttons.Count
            && a.Buttons.Zip(b.Buttons).All(pair => pair.First.Title == pair.Second.Title && pair.First.Enabled == pair.Second.Enabled && pair.First.Value == pair.Second.Value);

        // A toggle: its glyph in a small box, on in the accent's fill with its ink; its title and keys in the tooltip.
        private Border Toggle(PanelToggle toggle)
        {
            var p = _owner._palette;
            var glyph = p.Label(toggle.Glyph, toggle.On ? p.AccentInk : p.Secondary, p.SmallSize, FontWeight.SemiBold);
            glyph.HorizontalOptions = LayoutOptions.Center;
            glyph.VerticalOptions = LayoutOptions.Center;
            glyph.InputTransparent = true;
            var box = new Border
            {
                WidthRequest = 24,
                HeightRequest = 22,
                StrokeThickness = 1,
                Stroke = toggle.On ? p.Accent : Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = 4 },
                BackgroundColor = toggle.On ? p.Accent : Colors.Transparent,
                Content = glyph,
                VerticalOptions = LayoutOptions.Center,
            };
            ToolTipProperties.SetText(box, toggle.Keys is { } keys ? $"{toggle.Title} ({keys})" : toggle.Title);
            SemanticProperties.SetDescription(box, toggle.Title + (toggle.On ? ", on" : ", off"));
            if (!toggle.On)
            {
                p.States(box, InteractionKind.Flat);
            }

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _owner.Act(toggle.ActionId, toggle.On ? "off" : "on");
            box.GestureRecognizers.Add(tap);
            return box;
        }
    }

    /// <summary>
    /// A results block: a virtualized list of its lines (group headers and rows; <see cref="PanelBlockLayout.ResultLines"/>),
    /// of which only the run that differs from what is shown is replaced, so the scroll and the rows that stayed are kept.
    /// </summary>
    private sealed class ResultsView : ContentView
    {
        private readonly ObservableCollection<PanelResultLine> _lines = [];
        private readonly Label _empty;
        private readonly CollectionView _list;
        private int _version = -1;

        public ResultsView(PanelBlocksView owner)
        {
            var p = owner._palette;
            _list = new CollectionView
            {
                ItemsSource = _lines,
                ItemTemplate = new DataTemplate(() => new ResultLineView(owner)),
                SelectionMode = SelectionMode.None,
                VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            };
            _empty = p.Label("", p.Secondary, p.SmallSize);
            _empty.Margin = new Thickness(12, 8);
            _empty.LineBreakMode = LineBreakMode.WordWrap;
            var grid = new Grid();
            grid.Add(_list);
            grid.Add(_empty);
            Content = grid;
        }

        public void Update(PanelResults results)
        {
            _empty.Text = results.Empty ?? "";
            if (results.Version == _version && _lines.Count > 0)
            {
                _empty.IsVisible = false;
                return;
            }

            _version = results.Version;
            var lines = PanelBlockLayout.ResultLines(results);
            var change = RowChange.Between(_lines, lines);
            if (!change.IsEmpty)
            {
                int common = Math.Min(change.Removed, change.Inserted);
                for (int i = 0; i < common; i++)
                {
                    _lines[change.Start + i] = lines[change.Start + i];
                }

                for (int i = change.Removed - 1; i >= common; i--)
                {
                    _lines.RemoveAt(change.Start + i);
                }

                for (int i = common; i < change.Inserted; i++)
                {
                    _lines.Insert(change.Start + i, lines[change.Start + i]);
                }
            }

            _empty.IsVisible = lines.Count == 0 && _empty.Text.Length > 0;
            // X part 9: the row that stands for where the user is (the Outline's symbol at the caret) is scrolled into view.
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Row is { Selected: true })
                {
                    _list.ScrollTo(i, position: ScrollToPosition.MakeVisible, animate: false);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// One line of a results list, built from its <see cref="PanelResultLine"/> when the list hands it one (lines are reused
    /// as the list scrolls): a header (chevron, the file's badge or an icon, title, folder, count) or a row (its icon, its
    /// prefix, its text with the matches highlighted, its badge); its buttons show while the pointer is over it.
    /// </summary>
    private sealed class ResultLineView : ContentView
    {
        private readonly PanelBlocksView _owner;
        private readonly Grid _grid = new() { ColumnSpacing = 6, MinimumHeightRequest = 24 };
        private readonly HorizontalStackLayout _buttons = new() { Spacing = 1, IsVisible = false, VerticalOptions = LayoutOptions.Center };
        private PanelResultLine? _line;

        public ResultLineView(PanelBlocksView owner)
        {
            _owner = owner;
            Content = _grid;
            owner._palette.States(this, InteractionKind.Row);
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) => _buttons.IsVisible = _buttons.Children.Count > 0;
            pointer.PointerExited += (_, _) => _buttons.IsVisible = false;
            GestureRecognizers.Add(pointer);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Activate();
            GestureRecognizers.Add(tap);
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (BindingContext is PanelResultLine line && !ReferenceEquals(line, _line))
            {
                _line = line;
                Build(line);
            }
        }

        private void Activate()
        {
            if (_line is null)
            {
                return;
            }

            if (_line.Row is { ActionId: { } action } row)
            {
                _owner.Act(action, row.Value);
            }
            else if (_line.Row is { ToggleActionId: { } nodeToggle } node)
            {
                _owner.Act(nodeToggle, node.Id);
            }
            else if (_line.Row is null && _line.Group.ToggleActionId is { } toggle)
            {
                _owner.Act(toggle, _line.Group.Id);
            }
        }

        private void Build(PanelResultLine line)
        {
            var p = _owner._palette;
            _grid.Children.Clear();
            _grid.ColumnDefinitions.Clear();
            _buttons.Children.Clear();
            _buttons.IsVisible = false;
            Padding = new Thickness(line.IsHeader ? 6 : 26 + ((line.Row?.Depth ?? 0) * 14) - (line.Row?.Expanded is null || line.Row is null ? 0 : 16), line.IsHeader ? 4 : 1, 6, line.IsHeader ? 4 : 1);
            BackgroundColor = line.Row is { Selected: true } ? p.Accent.WithAlpha(0.18f) : Colors.Transparent;
            void Column(View view, GridLength width)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition(width));
                _grid.Add(view, _grid.ColumnDefinitions.Count - 1, 0);
            }

            if (line.Row is not { } row)
            {
                var group = line.Group;
                ToolTipProperties.SetText(this, group.Tooltip ?? "");
                SemanticProperties.SetDescription(this, $"{group.Title}{(group.Detail is { } d ? " in " + d : "")}, {group.Badge}, {(group.Collapsed ? "folded" : "open")}");
                if (group.ToggleActionId is not null)
                {
                    Column(new StrokeIcon(group.Collapsed ? "M9 6l6 6l-6 6" : "M6 9l6 6l6-6", 12) { Color = p.Secondary, VerticalOptions = LayoutOptions.Center, InputTransparent = true }, new GridLength(14));
                }

                if (group.FileIcon is { } fileIcon)
                {
                    // W: the file's drawn icon (the tree's).
                    Column(FileIconView.Of(fileIcon, p, 15), GridLength.Auto);
                }
                else if (group.Glyph is { } glyph)
                {
                    var badge = p.Label(glyph, ThemeColors.Of(group.GlyphArgb), p.CaptionSize, FontWeight.SemiBold);
                    badge.VerticalOptions = LayoutOptions.Center;
                    badge.InputTransparent = true;
                    Column(badge, GridLength.Auto);
                }
                else if (group.Icon is { } icon)
                {
                    Column(new StrokeIcon(icon, 14) { Color = p.Accent, VerticalOptions = LayoutOptions.Center, InputTransparent = true }, GridLength.Auto);
                }

                var title = p.Label(group.Title, p.Text, p.SmallSize, FontWeight.SemiBold);
                title.VerticalOptions = LayoutOptions.Center;
                title.LineBreakMode = LineBreakMode.TailTruncation;
                title.InputTransparent = true;
                Column(title, GridLength.Auto);
                var detail = p.Label(group.Detail ?? "", p.Secondary, p.CaptionSize);
                detail.VerticalOptions = LayoutOptions.Center;
                detail.LineBreakMode = LineBreakMode.HeadTruncation;
                detail.InputTransparent = true;
                Column(detail, GridLength.Star);
                foreach (var button in group.Buttons)
                {
                    _buttons.Children.Add(_owner.IconButton(button, 22));
                }

                Column(_buttons, GridLength.Auto);
                if (group.Badge is { } count)
                {
                    Column(Pill(count, p.Secondary), GridLength.Auto);
                }

                return;
            }

            if (row.Expanded is { } open)
            {
                // X part 9: a tree node's chevron (the Outline's, a hierarchy's); a click on it opens or closes the node.
                var chevron = new StrokeIcon(open ? "M6 9l6 6l6-6" : "M9 6l6 6l-6 6", 12) { Color = p.Secondary, VerticalOptions = LayoutOptions.Center };
                var box = new ContentView { Content = chevron, WidthRequest = 14, VerticalOptions = LayoutOptions.Center };
                if (row.ToggleActionId is { } flipAction)
                {
                    var flip = new TapGestureRecognizer();
                    flip.Tapped += (_, _) => _owner.Act(flipAction, row.Id);
                    box.GestureRecognizers.Add(flip);
                }

                Column(box, new GridLength(14));
            }

            ToolTipProperties.SetText(this, row.Detail is { } tip ? row.Text.Trim() + "\n" + tip : "");
            SemanticProperties.SetDescription(this, (row.Prefix is { } at ? "line " + at + ": " : "") + row.Text);
            var ink = row.Tone switch
            {
                PanelTone.Accent => p.Accent,
                PanelTone.Warning => p.Warning,
                PanelTone.Error => p.Error,
                PanelTone.Success => p.Success,
                _ => p.Text,
            };
            if (row.Icon is { } rowIcon)
            {
                Column(new StrokeIcon(rowIcon, 13) { Color = row.Tone == PanelTone.Normal ? p.Secondary : ink, VerticalOptions = LayoutOptions.Center, InputTransparent = true }, GridLength.Auto);
            }

            if (row.Prefix is { } prefix)
            {
                var number = p.Label(prefix, p.Secondary, p.CaptionSize);
                number.FontFamily = p.Options.FontFamily;
                number.VerticalOptions = LayoutOptions.Center;
                number.InputTransparent = true;
                Column(number, GridLength.Auto);
            }

            var text = new Label
            {
                FontSize = p.SmallSize,
                FontFamily = row.Code ? p.Options.FontFamily : p.Font(),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
                FormattedText = Formatted(row, ink),
            };
            Column(text, GridLength.Star);
            if (row.Detail is { } more && row.Prefix is null)
            {
                var detail = p.Label(more, p.Secondary, p.CaptionSize);
                detail.VerticalOptions = LayoutOptions.Center;
                detail.LineBreakMode = LineBreakMode.HeadTruncation;
                detail.MaximumWidthRequest = 180;
                detail.InputTransparent = true;
                Column(detail, GridLength.Auto);
            }

            foreach (var button in row.Buttons)
            {
                _buttons.Children.Add(_owner.IconButton(button, 22));
            }

            Column(_buttons, GridLength.Auto);
            if (row.Badge is { } label)
            {
                Column(Pill(label, row.Tone == PanelTone.Normal ? p.Accent : ink), GridLength.Auto);
            }
        }

        private FormattedString Formatted(PanelResultRow row, Color ink)
        {
            var p = _owner._palette;
            var match = ThemeColors.Of(p.Theme.Colors.FindMatch);
            var formatted = new FormattedString();
            foreach (var (part, kind) in PanelBlockLayout.Segments(row.Text, row.Highlights))
            {
                formatted.Spans.Add(kind switch
                {
                    PanelSegmentKind.Match => new Span { Text = part, TextColor = p.Text, BackgroundColor = match },
                    PanelSegmentKind.Removed => new Span { Text = part, TextColor = p.Error, TextDecorations = TextDecorations.Strikethrough, BackgroundColor = match },
                    PanelSegmentKind.Inserted => new Span { Text = part, TextColor = p.Success },
                    _ => new Span { Text = part, TextColor = ink },
                });
            }

            return formatted;
        }

        private Border Pill(string text, Color color)
        {
            var p = _owner._palette;
            var label = p.Label(text, color, p.CaptionSize, FontWeight.Medium);
            label.InputTransparent = true;
            return new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = p.RaisedHigh,
                Padding = new Thickness(6, 0),
                Content = label,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            };
        }
    }
}
