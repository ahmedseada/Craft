// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Craft's own slider (the owner, 2026-11-01: "the design's controls, not default ones"): a track drawn in the theme's
/// colours, its filled part and thumb in the accent, the value beside it; a press or a drag moves it (snapped to its step),
/// and the value is committed when the press ends (one action, not one a pixel).
/// </summary>
public sealed class CraftSlider : Grid
{
    private readonly GraphicsView _track;
    private readonly Label _value;
    private readonly Track _drawing;
    private readonly double _min;
    private readonly double _max;
    private readonly double _step;
    private readonly string? _unit;
    private readonly Action<double> _committed;
    private double _shown;

    /// <summary>A slider from <paramref name="min"/> to <paramref name="max"/> by <paramref name="step"/> at <paramref name="value"/>; <paramref name="committed"/> hears the value when a press ends.</summary>
    public CraftSlider(ShellPalette palette, double value, double min, double max, double step, string? unit, bool accent, bool enabled, Action<double> committed)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _min = min;
        _max = Math.Max(min, max);
        _step = step > 0 ? step : (max - min) / 100;
        _unit = unit;
        _committed = committed ?? throw new ArgumentNullException(nameof(committed));
        _shown = Math.Clamp(value, _min, _max);
        _drawing = new Track(palette.Raised, accent ? palette.Accent : palette.Secondary, palette.Accent) { Fraction = Fraction(_shown) };
        _track = new GraphicsView { Drawable = _drawing, HeightRequest = 22, HorizontalOptions = LayoutOptions.Fill, IsEnabled = enabled, Opacity = enabled ? 1 : 0.55 };
        _value = palette.Label(Text(_shown), accent ? palette.Accent : palette.Text, palette.SmallSize);
        _value.VerticalOptions = LayoutOptions.Center;
        _value.MinimumWidthRequest = 64;
        _value.HorizontalTextAlignment = TextAlignment.End;
        ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];
        ColumnSpacing = 8;
        this.Add(_track, 0, 0);
        this.Add(_value, 1, 0);
        if (!enabled)
        {
            return;
        }

        var pointer = new PointerGestureRecognizer();
        bool pressed = false;
        pointer.PointerPressed += (_, e) =>
        {
            pressed = true;
            MoveTo(e.GetPosition(_track));
        };
        pointer.PointerMoved += (_, e) =>
        {
            if (pressed)
            {
                MoveTo(e.GetPosition(_track));
            }
        };
        pointer.PointerReleased += (_, e) =>
        {
            if (pressed)
            {
                pressed = false;
                MoveTo(e.GetPosition(_track));
                _committed(_shown);
            }
        };
        _track.GestureRecognizers.Add(pointer);
    }

    /// <summary>The value shown now.</summary>
    public double Value => _shown;

    private void MoveTo(Point? point)
    {
        if (point is not { } at || _track.Width <= 0)
        {
            return;
        }

        double fraction = Math.Clamp(at.X / _track.Width, 0, 1);
        double raw = _min + (fraction * (_max - _min));
        _shown = Math.Clamp(_min + (Math.Round((raw - _min) / _step) * _step), _min, _max);
        _drawing.Fraction = Fraction(_shown);
        _value.Text = Text(_shown);
        _track.Invalidate();
    }

    private double Fraction(double value) => _max > _min ? (value - _min) / (_max - _min) : 0;

    private string Text(double value)
    {
        string number = _step >= 1 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);
        return _unit is null ? number : number + " " + _unit;
    }

    // The track: a rounded line, its filled part and a round thumb.
    private sealed class Track(Color ground, Color fill, Color thumb) : IDrawable
    {
        public double Fraction { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float middle = dirtyRect.Height / 2;
            float radius = 7;
            float left = radius;
            float right = Math.Max(left, dirtyRect.Width - radius);
            float x = left + (float)(Fraction * (right - left));
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeSize = 4;
            canvas.StrokeColor = ground;
            canvas.DrawLine(left, middle, right, middle);
            canvas.StrokeColor = fill;
            canvas.DrawLine(left, middle, x, middle);
            canvas.FillColor = thumb;
            canvas.FillCircle(x, middle, radius);
        }
    }
}

/// <summary>
/// Craft's own drop-down: the value picked in a field of the theme's colours with a chevron; a press opens its options as
/// an inline list under it (no platform picker or sheet), a press on one picks it and closes the list.
/// </summary>
public sealed class CraftDropdown : VerticalStackLayout
{
    private readonly VerticalStackLayout _list = new() { Spacing = 0, IsVisible = false };

    /// <summary>A drop-down of <paramref name="options"/> (value, title) with <paramref name="selected"/> picked; <paramref name="picked"/> hears a pick.</summary>
    public CraftDropdown(ShellPalette palette, IReadOnlyList<(string Value, string Title)> options, string selected, bool accent, bool enabled, Action<string> picked)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(picked);
        Spacing = 2;
        string shown = options.FirstOrDefault(o => string.Equals(o.Value, selected, StringComparison.OrdinalIgnoreCase)).Title ?? selected;
        var text = palette.Label(shown, accent ? palette.Accent : palette.Text, palette.SmallSize);
        text.VerticalOptions = LayoutOptions.Center;
        var chevron = palette.Label("▾", palette.Secondary, palette.SmallSize);
        chevron.VerticalOptions = LayoutOptions.Center;
        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 6 };
        row.Add(text, 0, 0);
        row.Add(chevron, 1, 0);
        var field = new Border
        {
            Content = row,
            Padding = new Thickness(10, 5),
            BackgroundColor = palette.Raised,
            Stroke = palette.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            MinimumHeightRequest = 30,
            Opacity = enabled ? 1 : 0.55,
        };
        Children.Add(field);
        var listBox = new Border
        {
            Content = _list,
            BackgroundColor = palette.Raised,
            Stroke = palette.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            IsVisible = false,
        };
        Children.Add(listBox);
        foreach (var (value, title) in options)
        {
            bool current = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase);
            var label = palette.Label((current ? "✓ " : "   ") + title, current ? palette.Accent : palette.Text, palette.SmallSize);
            var item = new Border { Content = label, Padding = new Thickness(10, 5), StrokeThickness = 0, BackgroundColor = Colors.Transparent };
            var hover = new PointerGestureRecognizer();
            hover.PointerEntered += (_, _) => item.BackgroundColor = palette.Selection;
            hover.PointerExited += (_, _) => item.BackgroundColor = Colors.Transparent;
            item.GestureRecognizers.Add(hover);
            var tap = new TapGestureRecognizer();
            string chosen = value;
            tap.Tapped += (_, _) =>
            {
                listBox.IsVisible = _list.IsVisible = false;
                if (!string.Equals(chosen, selected, StringComparison.OrdinalIgnoreCase))
                {
                    picked(chosen);
                }
            };
            item.GestureRecognizers.Add(tap);
            _list.Children.Add(item);
        }

        if (enabled)
        {
            var open = new TapGestureRecognizer();
            open.Tapped += (_, _) => listBox.IsVisible = _list.IsVisible = !_list.IsVisible;
            field.GestureRecognizers.Add(open);
        }
    }
}
