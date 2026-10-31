// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// A rail side view (<see cref="EditorSideViews"/>: the code search's "Search") in the explorer's column: its title in
/// capitals as the explorer's, its input (made once: the view's changes redraw only its status and items, so typing goes
/// on), its status line and its items, a tap on one handed to <see cref="IQueryPanel.Activate"/>. The input's text goes
/// to <see cref="IQueryPanel.SetQuery"/> as typed; Enter takes the first item. Colours only from the theme.
/// </summary>
public sealed class SideQueryView : ContentView, IDisposable
{
    private readonly IQueryPanel _view;
    private readonly Label _title;
    private readonly Entry _input;
    private readonly Label _status;
    private readonly VerticalStackLayout _items = new() { Spacing = 0 };
    private ShellPalette _palette;

    /// <summary>The side view of <paramref name="view"/>.</summary>
    public SideQueryView(IQueryPanel view, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(palette);
        _view = view;
        _palette = palette;
        _title = palette.Label("", palette.Secondary, palette.CaptionSize, FontWeight.SemiBold);
        _title.CharacterSpacing = 0.6;
        _title.Margin = new Thickness(14, 10, 14, 8);
        _input = new Entry { Placeholder = view.Placeholder, Text = view.Query, FontSize = palette.FontSize, ClearButtonVisibility = ClearButtonVisibility.WhileEditing, Margin = new Thickness(10, 0) };
        _input.TextChanged += (_, e) => _view.SetQuery(e.NewTextValue ?? "");
        _input.Completed += (_, _) =>
        {
            if (_view.Items.Count > 0)
            {
                _view.Activate(0);
            }
        };
        _status = palette.Label("", palette.Secondary, palette.CaptionSize);
        _status.Margin = new Thickness(14, 6, 14, 4);
        _status.LineBreakMode = LineBreakMode.TailTruncation;
        var layout = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        layout.Add(_title, 0, 0);
        layout.Add(_input, 0, 1);
        layout.Add(_status, 0, 2);
        layout.Add(new ScrollView { Content = _items }, 0, 3);
        Content = layout;
        view.Changed += OnChanged;
        Apply(palette);
    }

    /// <summary>The view drawn.</summary>
    public IQueryPanel View => _view;

    /// <summary>Puts the keyboard's focus in the input.</summary>
    public void FocusInput() => _input.Focus();

    /// <summary>Takes the theme's colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Surface;
        _title.TextColor = palette.Secondary;
        _status.TextColor = palette.Secondary;
        _input.TextColor = palette.Text;
        _input.PlaceholderColor = palette.Secondary;
        _input.BackgroundColor = palette.Raised;
        Fill();
    }

    /// <inheritdoc />
    public void Dispose() => _view.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Fill();

    private void Fill()
    {
        _title.Text = _view.Title.ToUpperInvariant();
        _status.Text = _view.Status ?? "";
        _status.IsVisible = _view.Status is { Length: > 0 };
        _items.Children.Clear();
        var items = _view.Items;
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var name = _palette.Label(items[i].Text, _palette.Text);
            name.LineBreakMode = LineBreakMode.TailTruncation;
            var row = new VerticalStackLayout { Padding = new Thickness(14, 4), Spacing = 1, Children = { name } };
            if (items[i].Detail is { } detail)
            {
                var place = _palette.Label(detail, _palette.Secondary, _palette.CaptionSize);
                place.LineBreakMode = LineBreakMode.TailTruncation;
                row.Children.Add(place);
            }

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _view.Activate(index);
            row.GestureRecognizers.Add(tap);
            _items.Children.Add(row);
        }
    }
}
