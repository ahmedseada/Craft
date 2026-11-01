// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Craft.Maui;

/// <summary>
/// A view panel's blocks (<see cref="IEditorViewPanel"/>, B11) drawn with the shell's own controls and the theme's
/// tokens: cards (a card inside a card is a titled section, never a box inside a box), text, a row of steps with their
/// icons, a numbered checklist with its buttons, switches, the palette's buttons with their hover, pressed, focus and
/// disabled states, meters and sparklines drawn on a canvas, pickers, Craft's input style, facts and a disclosure. Each
/// block's tooltip shows on hover and its variable small under it. Only the top-level blocks whose signature changed are
/// drawn again (<see cref="PanelBlockLayout.Changed"/>); what is typed in a field and whether a disclosure is open stay
/// across redraws. Called on the UI thread; an action runs through <see cref="IEditorViewPanel.ActAsync"/>.
/// </summary>
public sealed class PanelBlocksView : ContentView
{
    private readonly IEditorViewPanel _panel;
    private readonly Action<string, Exception> _failed;
    private readonly VerticalStackLayout _stack = new() { Spacing = 12, Padding = new Thickness(12, 4, 12, 20) };
    private readonly List<string> _signatures = [];
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);
    private ShellPalette _palette;
    private string? _focused;

    /// <summary>The blocks of <paramref name="panel"/> in <paramref name="palette"/>; an action that throws is told to <paramref name="failed"/> (what, the error).</summary>
    public PanelBlocksView(IEditorViewPanel panel, ShellPalette palette, Action<string, Exception> failed)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        Content = new ScrollView { Content = _stack };
        Update();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours and draws every block again.</summary>
    public void Apply(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _signatures.Clear();
        _stack.Children.Clear();
        Update();
    }

    /// <summary>Draws again the top-level blocks that changed since they were drawn.</summary>
    public void Update()
    {
        var blocks = _panel.Blocks;
        var signatures = blocks.Select(PanelBlockLayout.Signature).ToList();
        foreach (int index in PanelBlockLayout.Changed(_signatures, signatures))
        {
            var view = Block(blocks[index], nested: false);
            if (index < _stack.Children.Count)
            {
                _stack.Children[index] = view;
            }
            else
            {
                _stack.Children.Add(view);
            }
        }

        while (_stack.Children.Count > blocks.Count)
        {
            _stack.Children.RemoveAt(_stack.Children.Count - 1);
        }

        _signatures.Clear();
        _signatures.AddRange(signatures);
    }

    private View Block(PanelBlock block, bool nested)
    {
        var view = block switch
        {
            PanelCard card => Card(card, nested),
            PanelText text => Text(text),
            PanelSteps steps => Steps(steps),
            PanelChecklist list => Checklist(list),
            PanelButtons buttons => Buttons(buttons.Buttons),
            PanelSwitch toggle => Switch(toggle),
            PanelMeter meter => Meter(meter),
            PanelChart chart => Chart(chart),
            PanelPicker picker => Picker(picker),
            PanelInput input => Input(input),
            PanelFacts facts => Facts(facts),
            PanelDisclosure disclosure => Disclosure(disclosure),
            _ => new ContentView(),
        };
        if (block.Tooltip is { } tip)
        {
            ToolTipProperties.SetText(view, block.Variable is { } variable ? tip + Environment.NewLine + variable : tip);
        }

        if (block.Variable is not { } shown || block is PanelCard)
        {
            return view;
        }

        // The variable small under the block, as the settings page shows a setting's.
        var p = _palette;
        var name = p.Label(shown, p.Secondary, p.CaptionSize);
        name.FontFamily = p.Options.FontFamily;
        name.Opacity = 0.75;
        var stack = new VerticalStackLayout { Spacing = 2 };
        stack.Children.Add(view);
        stack.Children.Add(name);
        return stack;
    }

    // ---- the kinds ----

    private View Card(PanelCard card, bool nested)
    {
        var p = _palette;
        var body = new VerticalStackLayout { Spacing = 10 };
        if (card.Title is not null || card.Badge is not null || card.Icon is not null)
        {
            var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            var ink = ToneColor(card.Tone, p.Text);
            if (card.Icon is { } icon)
            {
                header.Add(new StrokeIcon(icon, 16) { Color = card.Tone == PanelTone.Normal ? p.Accent : ink, VerticalOptions = LayoutOptions.Center }, 0, 0);
            }

            if (card.Title is { } title)
            {
                var label = p.Label(title, ink, nested ? p.SmallSize : p.FontSize, FontWeight.SemiBold);
                label.LineBreakMode = LineBreakMode.WordWrap;
                header.Add(label, 1, 0);
            }

            if (card.Badge is { } badge)
            {
                header.Add(p.Label(badge, p.Secondary, p.CaptionSize), 2, 0);
            }

            body.Children.Add(header);
        }

        foreach (var inner in card.Blocks)
        {
            body.Children.Add(Block(inner, nested: true));
        }

        if (nested)
        {
            // A card inside a card: a titled section of it, no second box.
            body.Margin = new Thickness(0, 4, 0, 0);
            return body;
        }

        var stroke = card.Tone switch
        {
            PanelTone.Error => p.Error,
            PanelTone.Warning => p.Warning,
            PanelTone.Accent => p.Accent,
            _ => p.Border,
        };
        return p.Box(body, 10, new Thickness(14), p.Raised, stroke);
    }

    private Label Text(PanelText text)
    {
        var p = _palette;
        var label = text.Style switch
        {
            PanelTextStyle.Heading => p.Label(text.Text, p.Text, p.FontSize, FontWeight.SemiBold),
            PanelTextStyle.Secondary => p.Label(text.Text, p.Secondary, p.SmallSize),
            PanelTextStyle.Caption => p.Label(text.Text, p.Secondary, p.CaptionSize),
            PanelTextStyle.Code => p.Label(text.Text, p.Text, p.SmallSize),
            _ => p.Label(text.Text, p.Text, p.FontSize),
        };
        if (text.Style == PanelTextStyle.Code)
        {
            label.FontFamily = p.Options.FontFamily;
        }

        if (text.Tone != PanelTone.Normal)
        {
            label.TextColor = ToneColor(text.Tone, label.TextColor);
        }

        label.LineBreakMode = LineBreakMode.WordWrap;
        label.LineHeight = 1.25;
        return label;
    }

    // The steps left to right, each a circle with its icon and its name under it, joined by a line that is green once the
    // step before it is done; the step you are on in the accent.
    private Grid Steps(PanelSteps steps)
    {
        var p = _palette;
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 6, RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)] };
        for (int i = 0; i < steps.Steps.Count; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));
                var line = new BoxView
                {
                    HeightRequest = 2,
                    VerticalOptions = LayoutOptions.Center,
                    Color = steps.Steps[i - 1].State == PanelStepState.Done ? p.Success : p.BorderStrong,
                };
                grid.Add(line, grid.ColumnDefinitions.Count - 1, 0);
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            int column = grid.ColumnDefinitions.Count - 1;
            var step = steps.Steps[i];
            uint surface = p.Theme.Colors.Raised;
            (Color fill, Color stroke, Color icon, Color text) = step.State switch
            {
                PanelStepState.Current => (p.Accent, p.Accent, p.AccentInk, p.Text),
                PanelStepState.Done => (ThemeColors.Of(ColorContrast.Mix(surface, p.Theme.Colors.Success, 0.18)), p.Success, p.Success, p.Text),
                _ => (Colors.Transparent, p.BorderStrong, p.Secondary, p.Secondary),
            };
            var circle = new Border
            {
                WidthRequest = 34,
                HeightRequest = 34,
                StrokeShape = new RoundRectangle { CornerRadius = 17 },
                Stroke = stroke,
                StrokeThickness = 1.5,
                BackgroundColor = fill,
                HorizontalOptions = LayoutOptions.Center,
                Content = step.State == PanelStepState.Done
                    ? new StrokeIcon("M5 12.5l4.5 4.5L19 7.5", 16, 2) { Color = icon, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                    : step.Icon is { } data ? new StrokeIcon(data, 16) { Color = icon, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                    : p.Label((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), icon, p.SmallSize, FontWeight.SemiBold),
            };
            SemanticProperties.SetDescription(circle, $"Step {i + 1}: {step.Title}, {step.State switch { PanelStepState.Done => "done", PanelStepState.Current => "you are here", _ => "next" }}");
            grid.Add(circle, column, 0);
            var names = new VerticalStackLayout { Spacing = 1 };
            var title = p.Label(step.Title, text, p.SmallSize, step.State == PanelStepState.Current ? FontWeight.SemiBold : FontWeight.Regular);
            title.HorizontalTextAlignment = TextAlignment.Center;
            title.LineBreakMode = LineBreakMode.WordWrap;
            names.Children.Add(title);
            if (step.Detail is { } detail)
            {
                var under = p.Label(detail, p.Secondary, p.CaptionSize);
                under.HorizontalTextAlignment = TextAlignment.Center;
                under.LineBreakMode = LineBreakMode.WordWrap;
                names.Children.Add(under);
            }

            grid.Add(names, column, 1);
        }

        return grid;
    }

    // Numbered items: a badge (its number, or a check once done), what to do and why, its button under it.
    private VerticalStackLayout Checklist(PanelChecklist list)
    {
        var p = _palette;
        var stack = new VerticalStackLayout { Spacing = 12 };
        for (int i = 0; i < list.Items.Count; i++)
        {
            var item = list.Items[i];
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(24)), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10 };
            uint surface = p.Theme.Colors.Raised;
            var badge = new Border
            {
                WidthRequest = 22,
                HeightRequest = 22,
                StrokeShape = new RoundRectangle { CornerRadius = 11 },
                StrokeThickness = 1,
                Stroke = item.Done ? p.Success : p.BorderStrong,
                BackgroundColor = item.Done ? ThemeColors.Of(ColorContrast.Mix(surface, p.Theme.Colors.Success, 0.18)) : Colors.Transparent,
                VerticalOptions = LayoutOptions.Start,
                Content = item.Done
                    ? new StrokeIcon("M5 12.5l4.5 4.5L19 7.5", 12, 2.2) { Color = p.Success, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                    : p.Label((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), p.Secondary, p.CaptionSize, FontWeight.SemiBold),
            };
            if (badge.Content is Label number)
            {
                number.HorizontalTextAlignment = TextAlignment.Center;
                number.HorizontalOptions = LayoutOptions.Center;
            }

            row.Add(badge, 0, 0);
            var text = new VerticalStackLayout { Spacing = 4 };
            var what = p.Label(item.Text, item.Done ? p.Secondary : p.Text, p.FontSize, item.Done ? FontWeight.Regular : FontWeight.Medium);
            what.LineBreakMode = LineBreakMode.WordWrap;
            text.Children.Add(what);
            if (item.Detail is { } detail)
            {
                var why = p.Label(detail, p.Secondary, p.SmallSize);
                why.LineBreakMode = LineBreakMode.WordWrap;
                text.Children.Add(why);
            }

            if (item.Action is { } action && !item.Done)
            {
                var button = Button(action);
                button.HorizontalOptions = LayoutOptions.Start;
                button.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(button);
            }

            row.Add(text, 1, 0);
            stack.Children.Add(row);
        }

        return stack;
    }

    private FlexLayout Buttons(IReadOnlyList<PanelButton> buttons)
    {
        var row = new FlexLayout { Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center };
        foreach (var one in buttons)
        {
            var button = Button(one);
            button.Margin = new Thickness(0, 0, 8, 6);
            row.Children.Add(button);
        }

        return row;
    }

    private Button Button(PanelButton action)
    {
        var p = _palette;
        var button = action.Kind switch
        {
            PanelButtonKind.Primary => p.Primary(action.Title, 32),
            PanelButtonKind.Flat => p.Flat(action.Title, p.Text, 30, p.SmallSize),
            PanelButtonKind.Danger => p.Flat(action.Title, p.Error, 30, p.SmallSize),
            _ => p.SecondaryButton(action.Title, 30),
        };
        button.IsEnabled = action.Enabled;
        if (action.Tooltip is { } tip)
        {
            ToolTipProperties.SetText(button, tip);
        }

        button.Clicked += (_, _) => Act(action.ActionId, action.Value);
        return button;
    }

    // The switch at the right of its title; what being on means under the title (a consent's words).
    private Grid Switch(PanelSwitch toggle)
    {
        var p = _palette;
        var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 12 };
        var text = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        var title = p.Label(toggle.Title, toggle.Enabled ? p.Text : p.Secondary, p.FontSize, FontWeight.Medium);
        title.LineBreakMode = LineBreakMode.WordWrap;
        text.Children.Add(title);
        if (toggle.Detail is { } detail)
        {
            var under = p.Label(detail, p.Secondary, p.SmallSize);
            under.LineBreakMode = LineBreakMode.WordWrap;
            under.LineHeight = 1.25;
            text.Children.Add(under);
        }

        grid.Add(text, 0, 0);
        var control = new Switch { IsToggled = toggle.On, IsEnabled = toggle.Enabled, OnColor = p.Accent, ThumbColor = p.Text, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(control, toggle.Title);
        control.Toggled += (_, e) => Act(toggle.ActionId, e.Value ? "on" : "off");
        grid.Add(control, 1, 0);
        return grid;
    }

    private VerticalStackLayout Meter(PanelMeter meter)
    {
        var p = _palette;
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(TitleRow(meter.Title, meter.Detail));
        var drawable = new MeterDrawable { Fraction = meter.Fraction, Track = p.RaisedHigh, Fill = ToneColor(meter.Tone, p.Accent) };
        stack.Children.Add(new GraphicsView { Drawable = drawable, HeightRequest = 6, InputTransparent = true });
        return stack;
    }

    private VerticalStackLayout Chart(PanelChart chart)
    {
        var p = _palette;
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(TitleRow(chart.Title, chart.Detail));
        var drawable = new SparklineDrawable { Values = chart.Values, Marks = chart.Marks, Line = p.Accent, Mark = p.Accent, Base = p.Border, Hollow = p.Raised };
        stack.Children.Add(new GraphicsView { Drawable = drawable, HeightRequest = 40, InputTransparent = true });
        if (chart.Legend is { } legend)
        {
            var under = p.Label(legend, p.Secondary, p.CaptionSize);
            under.LineBreakMode = LineBreakMode.WordWrap;
            stack.Children.Add(under);
        }

        return stack;
    }

    private Grid TitleRow(string title, string? detail)
    {
        var p = _palette;
        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
        row.Add(p.Label(title, p.Text, p.SmallSize, FontWeight.Medium), 0, 0);
        if (detail is not null)
        {
            row.Add(p.Label(detail, p.Secondary, p.SmallSize), 1, 0);
        }

        return row;
    }

    private VerticalStackLayout Picker(PanelPicker picker)
    {
        var p = _palette;
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(p.Label(picker.Title, p.Secondary, p.SmallSize));
        int selected = -1;
        for (int i = 0; i < picker.Options.Count; i++)
        {
            if (picker.Options[i].Value == picker.Selected)
            {
                selected = i;
            }
        }

        var control = new Picker
        {
            ItemsSource = picker.Options.Select(o => o.Title).ToList(),
            SelectedIndex = selected,
            IsEnabled = picker.Enabled,
            TextColor = p.Text,
            TitleColor = p.Secondary,
            BackgroundColor = Colors.Transparent,
            FontFamily = p.Font(),
            FontSize = p.FontSize,
        };
        SemanticProperties.SetDescription(control, picker.Title);
        control.SelectedIndexChanged += (_, _) =>
        {
            if (control.SelectedIndex >= 0 && control.SelectedIndex < picker.Options.Count && control.SelectedIndex != selected)
            {
                Act(picker.ActionId, picker.Options[control.SelectedIndex].Value);
            }
        };
        var box = p.Box(control, 6, new Thickness(6, 0), p.RaisedHigh, p.BorderStrong);
        box.Opacity = picker.Enabled ? 1 : 0.55;
        stack.Children.Add(box);
        if (selected >= 0 && picker.Options[selected].Detail is { } detail)
        {
            var under = p.Label(detail, p.Secondary, p.SmallSize);
            under.LineBreakMode = LineBreakMode.WordWrap;
            stack.Children.Add(under);
        }

        return stack;
    }

    // Craft's input style around a field, its unit and its button; Enter submits; what is typed survives a redraw.
    private VerticalStackLayout Input(PanelInput input)
    {
        var p = _palette;
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(p.Label(input.Title, p.Secondary, p.SmallSize));
        var entry = p.Input(input.Placeholder ?? "", 32, input.Code);
        entry.Text = _drafts.TryGetValue(input.ActionId, out var draft) ? draft : input.Value;
        if (input.Numeric)
        {
            entry.Keyboard = Keyboard.Numeric;
        }

        SemanticProperties.SetDescription(entry, input.Title);
        entry.TextChanged += (_, e) => _drafts[input.ActionId] = e.NewTextValue ?? "";
        entry.Focused += (_, _) => _focused = input.ActionId;
        entry.Unfocused += (_, _) =>
        {
            if (_focused == input.ActionId)
            {
                _focused = null;
            }
        };
        void Submit()
        {
            string text = (entry.Text ?? "").Trim();
            _drafts.Remove(input.ActionId);
            Act(input.ActionId, text);
        }

        entry.Completed += (_, _) => Submit();
        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
        var field = p.Field(entry);
        field.HeightRequest = 32;
        row.Add(field, 0, 0);
        if (input.Unit is { } unit)
        {
            row.Add(p.Label(unit, p.Secondary, p.SmallSize), 1, 0);
        }

        var button = p.SecondaryButton(input.Submit, 32);
        button.Clicked += (_, _) => Submit();
        row.Add(button, 2, 0);
        stack.Children.Add(row);
        if (_focused == input.ActionId)
        {
            Dispatcher.Dispatch(() => entry.Focus());               // drawn again while typing: the caret stays in the field
        }

        return stack;
    }

    private Grid Facts(PanelFacts facts)
    {
        var p = _palette;
        var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 12, RowSpacing = 4 };
        for (int i = 0; i < facts.Facts.Count; i++)
        {
            var fact = facts.Facts[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(p.Label(fact.Label, p.Secondary, p.SmallSize), 0, i);
            var value = p.Label(fact.Value, ToneColor(fact.Tone, p.Text), p.SmallSize);
            value.LineBreakMode = LineBreakMode.WordWrap;
            grid.Add(value, 1, i);
        }

        return grid;
    }

    // A flat row with a chevron that opens what it holds below it, set off by a line at its left (no second box).
    private VerticalStackLayout Disclosure(PanelDisclosure disclosure)
    {
        var p = _palette;
        bool open = _open.TryGetValue(disclosure.Id, out bool kept) ? kept : disclosure.Open;
        var stack = new VerticalStackLayout { Spacing = 6 };
        var header = p.Flat((open ? "▾  " : "▸  ") + disclosure.Title, p.Accent, 28, p.SmallSize);
        header.HorizontalOptions = LayoutOptions.Start;
        header.Padding = new Thickness(4, 0);
        SemanticProperties.SetDescription(header, disclosure.Title + (open ? ", open" : ", closed"));
        header.Clicked += (_, _) =>
        {
            _open[disclosure.Id] = !open;
            _signatures.Clear();                                   // drawn again with it open or closed
            _stack.Children.Clear();
            Update();
        };
        stack.Children.Add(header);
        if (open)
        {
            var inner = new VerticalStackLayout { Spacing = 8 };
            foreach (var block in disclosure.Blocks)
            {
                inner.Children.Add(Block(block, nested: true));
            }

            var body = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(2)), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 10, Margin = new Thickness(6, 0, 0, 0) };
            body.Add(new BoxView { Color = p.BorderStrong, WidthRequest = 2, VerticalOptions = LayoutOptions.Fill }, 0, 0);
            body.Add(inner, 1, 0);
            stack.Children.Add(body);
        }

        return stack;
    }

    // ---- helpers ----

    private Color ToneColor(PanelTone tone, Color normal) => tone switch
    {
        PanelTone.Accent => _palette.Accent,
        PanelTone.Success => _palette.Success,
        PanelTone.Warning => _palette.Warning,
        PanelTone.Error => _palette.Error,
        _ => normal,
    };

    private async void Act(string actionId, string? value)
    {
        try
        {
            await _panel.ActAsync(actionId, value).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // An action's failure is logged; the panel stays.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _failed(actionId, error);
        }
    }

    /// <summary>A meter's bar: the track, and the fraction filled (a third for work without a known end).</summary>
    private sealed class MeterDrawable : IDrawable
    {
        public double Fraction { get; set; }

        public Color Track { get; set; } = Colors.Gray;

        public Color Fill { get; set; } = Colors.Orange;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float radius = dirtyRect.Height / 2;
            canvas.FillColor = Track;
            canvas.FillRoundedRectangle(dirtyRect, radius);
            float width = PanelBlockLayout.MeterWidth(Fraction, dirtyRect.Width);
            if (width > 0)
            {
                canvas.FillColor = Fill;
                canvas.FillRoundedRectangle(new RectF(dirtyRect.X, dirtyRect.Y, Math.Max(width, dirtyRect.Height), dirtyRect.Height), radius);
            }
        }
    }

    /// <summary>A sparkline: a base line, the values joined, each point a dot (filled when marked, hollow when not).</summary>
    private sealed class SparklineDrawable : IDrawable
    {
        public IReadOnlyList<double> Values { get; set; } = [];

        public IReadOnlyList<bool> Marks { get; set; } = [];

        public Color Line { get; set; } = Colors.Orange;

        public Color Mark { get; set; } = Colors.Orange;

        public Color Base { get; set; } = Colors.Gray;

        public Color Hollow { get; set; } = Colors.Black;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var points = PanelBlockLayout.ChartPoints(Values, dirtyRect.Width, dirtyRect.Height, 4);
            canvas.StrokeColor = Base;
            canvas.StrokeSize = 1;
            canvas.DrawLine(dirtyRect.X, dirtyRect.Bottom - 0.5f, dirtyRect.Right, dirtyRect.Bottom - 0.5f);
            canvas.StrokeColor = Line;
            canvas.StrokeSize = 1.5f;
            canvas.StrokeLineCap = LineCap.Round;
            (float X, float Y)? previous = null;
            foreach (var point in points)
            {
                if (float.IsNaN(point.Y))
                {
                    previous = null;
                    continue;
                }

                if (previous is { } from)
                {
                    canvas.DrawLine(dirtyRect.X + from.X, dirtyRect.Y + from.Y, dirtyRect.X + point.X, dirtyRect.Y + point.Y);
                }

                previous = point;
            }

            for (int i = 0; i < points.Length; i++)
            {
                if (float.IsNaN(points[i].Y))
                {
                    continue;
                }

                bool marked = Marks.Count == 0 || (i < Marks.Count && Marks[i]);
                float x = dirtyRect.X + points[i].X, y = dirtyRect.Y + points[i].Y;
                canvas.FillColor = marked ? Mark : Hollow;
                canvas.FillCircle(x, y, 3);
                canvas.StrokeColor = Mark;
                canvas.StrokeSize = 1.25f;
                canvas.DrawCircle(x, y, 3);
            }
        }
    }
}
