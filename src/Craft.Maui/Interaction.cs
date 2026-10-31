// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The one mechanism by which every control of the shell answers the pointer and the keyboard (phase X): hover, pressed,
/// keyboard focus (the focus ring: the accent's border) and disabled, each look from the theme's tokens
/// (<see cref="InteractionStyle"/> through <see cref="ShellPalette.Look"/>). <see cref="Attach"/> it to a button, a rail
/// item, a tab, a tree or list row, a chip or a menu item; <see cref="Selected"/> marks the active one of its kind. A
/// button the palette makes (<see cref="ShellPalette.Flat"/>, <see cref="ShellPalette.SecondaryButton"/>,
/// <see cref="ShellPalette.Primary"/>) has it already. A focus the pointer gave (a click) shows no ring; the keyboard's does.
/// </summary>
public sealed class InteractiveStates
{
    private static readonly BindableProperty StatesProperty = BindableProperty.CreateAttached("CraftInteractiveStates", typeof(InteractiveStates), typeof(InteractiveStates), null);
    private readonly VisualElement _view;
    private ShellPalette _palette;
    private InteractionKind _kind;
    private uint? _text;
    private bool _hovered;
    private bool _pressed;
    private bool _focused;
    private bool _pointerFocus;
    private bool _selected;

    private InteractiveStates(VisualElement view, ShellPalette palette, InteractionKind kind)
    {
        _view = view;
        _palette = palette;
        _kind = kind;
        if (view is View withGestures)
        {
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) => SetHovered(true);
            pointer.PointerExited += (_, _) =>
            {
                _pressed = false;
                SetHovered(false);
            };
            if (view is not Button)
            {
                pointer.PointerPressed += (_, _) => SetPressed(true);
                pointer.PointerReleased += (_, _) => SetPressed(false);
            }

            withGestures.GestureRecognizers.Add(pointer);
        }

        if (view is Button button)
        {
            button.Pressed += (_, _) => SetPressed(true);
            button.Released += (_, _) => SetPressed(false);
            button.BorderWidth = 1;                          // always one unit: the ring changes its colour only, nothing moves
        }

        view.Focused += (_, _) =>
        {
            _focused = true;
            _pointerFocus = _hovered;                        // a click's focus shows no ring; Tab's does
            Paint();
        };
        view.Unfocused += (_, _) =>
        {
            _focused = false;
            _pointerFocus = false;
            Paint();
        };
        view.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
            {
                Paint();
            }
        };
    }

    /// <summary>Whether it is the active one of its kind (the active tab, the active file's row, a menu's keyboard row).</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (value != _selected)
            {
                _selected = value;
                Paint();
            }
        }
    }

    /// <summary>The state it shows now.</summary>
    public InteractionState State => InteractionStyle.StateOf(_view.IsEnabled, _pressed, _focused && !_pointerFocus, _hovered);

    /// <summary>A label inside it whose colour follows the state (a row's text), or null.</summary>
    public Label? Label { get; set; }

    /// <summary>An icon inside it whose colour follows the state (a rail item's), or null.</summary>
    public StrokeIcon? Icon { get; set; }

    /// <summary>
    /// The states of <paramref name="view"/> as a <paramref name="kind"/> control in <paramref name="palette"/>'s colours
    /// (its resting text in <paramref name="text"/> when given); attached once, so attaching again changes the kind.
    /// </summary>
    public static InteractiveStates Attach(VisualElement view, ShellPalette palette, InteractionKind kind, Color? text = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(palette);
        var states = Of(view);
        if (states is null)
        {
            states = new InteractiveStates(view, palette, kind);
            view.SetValue(StatesProperty, states);
        }

        states._palette = palette;
        states._kind = kind;
        states._text = text is null ? null : Argb(text);
        states.Paint();
        return states;
    }

    /// <summary>The states attached to <paramref name="view"/>, or null.</summary>
    public static InteractiveStates? Of(VisualElement view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.GetValue(StatesProperty) as InteractiveStates;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours (a theme change).</summary>
    public void Apply(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        Paint();
    }

    /// <summary>Its resting text in <paramref name="text"/> (a losing action in the error's colour), or the kind's when null.</summary>
    public void SetText(Color? text)
    {
        _text = text is null ? null : Argb(text);
        Paint();
    }

    private static uint Argb(Color color)
    {
        color.ToRgba(out byte r, out byte g, out byte b, out byte a);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        Paint();
    }

    private void SetPressed(bool pressed)
    {
        _pressed = pressed;
        if (pressed)
        {
            _pointerFocus = true;
        }

        Paint();
    }

    private void Paint()
    {
        var look = _palette.Look(_kind, State, _selected, _text);
        _view.BackgroundColor = look.Fill;
        switch (_view)
        {
            case Button button:
                button.TextColor = look.Text;
                button.BorderColor = look.Stroke;
                break;
            case Border border when look.Stroke.Alpha > 0 || _kind is InteractionKind.Chip or InteractionKind.Secondary:
                border.Stroke = look.Stroke;
                break;
        }

        if (Label is { } label)
        {
            label.TextColor = look.Text;
        }

        if (Icon is { } icon)
        {
            icon.Color = look.Text;
        }
    }
}

/// <summary>
/// The shell's transitions (phase X: 120-200 ms, <c>CRAFT_MOTION_MS</c>; none with <c>CRAFT_REDUCED_MOTION</c>): a view
/// fades in while it slides the last few units into place, and fades out the same way; a later call takes over an earlier
/// one. The length is the shell's (<see cref="Milliseconds"/>, set from the options by the page).
/// </summary>
public static class ShellMotion
{
    private static readonly BindableProperty GenerationProperty = BindableProperty.CreateAttached("CraftMotionGeneration", typeof(int), typeof(ShellMotion), 0);

    /// <summary>How long a transition lasts (<see cref="Craft.Editor.Motion.Duration"/> of the options); 0 shows and hides at once.</summary>
    public static int Milliseconds { get; set; } = 160;

    /// <summary>Shows <paramref name="view"/>, fading in from <paramref name="offsetY"/> units below its place (above when negative).</summary>
    public static Task ShowAsync(VisualElement view, double offsetY = 8) => Run(view, true, offsetY);

    /// <summary>Hides <paramref name="view"/>, fading out toward <paramref name="offsetY"/>; it is not visible when this completes (unless shown again meanwhile).</summary>
    public static Task HideAsync(VisualElement view, double offsetY = 8) => Run(view, false, offsetY);

    private static async Task Run(VisualElement view, bool show, double offsetY)
    {
        ArgumentNullException.ThrowIfNull(view);
        int generation = (int)view.GetValue(GenerationProperty) + 1;
        view.SetValue(GenerationProperty, generation);
        view.CancelAnimations();
        uint length = (uint)Math.Max(0, Milliseconds);
        if (length == 0 || (!show && !view.IsVisible))
        {
            view.Opacity = 1;
            view.TranslationY = 0;
            view.IsVisible = show;
            return;
        }

        if (show)
        {
            if (!view.IsVisible)
            {
                view.Opacity = 0;
                view.TranslationY = offsetY;
            }

            view.IsVisible = true;
            await Task.WhenAll(view.FadeToAsync(1, length, Easing.CubicOut), view.TranslateToAsync(0, 0, length, Easing.CubicOut));
            return;
        }

        await Task.WhenAll(view.FadeToAsync(0, length, Easing.CubicIn), view.TranslateToAsync(0, offsetY, length, Easing.CubicIn));
        if ((int)view.GetValue(GenerationProperty) == generation)
        {
            view.IsVisible = false;
            view.Opacity = 1;
            view.TranslationY = 0;
        }
    }
}

/// <summary>
/// An empty state (phase X: <see cref="ShellEmptyStates"/>): its title, what to do, its actions as buttons (the first
/// thing to do in the accent) and a list of recent folders; <see cref="ActionChosen"/> says which was picked (the page
/// runs its command or opens its folder).
/// </summary>
public sealed class EmptyStateView : ContentView
{
    private readonly VerticalStackLayout _stack = new() { Spacing = 10, Padding = new Thickness(16, 18) };
    private ShellPalette _palette;
    private EmptyState? _shown;

    /// <summary>An empty view in <paramref name="palette"/>'s colours.</summary>
    public EmptyStateView(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        Content = _stack;
    }

    /// <summary>Raised with the action picked.</summary>
    public event EventHandler<EmptyAction>? ActionChosen;

    /// <summary>Shows <paramref name="state"/> (the same one shown: nothing done).</summary>
    public void Show(EmptyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state == _shown || (_shown is not null && state.Title == _shown.Title && state.Text == _shown.Text && state.Actions.SequenceEqual(_shown.Actions) && state.Recent.SequenceEqual(_shown.Recent)))
        {
            return;
        }

        _shown = state;
        Build();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        Build();
    }

    private void Build()
    {
        _stack.Children.Clear();
        if (_shown is not { } state)
        {
            return;
        }

        var p = _palette;
        var title = p.Label(state.Title, p.Text, p.FontSize, FontWeight.SemiBold);
        title.LineBreakMode = LineBreakMode.WordWrap;
        var text = p.Label(state.Text, p.Secondary, p.SmallSize);
        text.LineBreakMode = LineBreakMode.WordWrap;
        text.LineHeight = 1.3;
        _stack.Children.Add(title);
        _stack.Children.Add(text);
        if (state.Actions.Count > 0)
        {
            var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var action in state.Actions)
            {
                var button = action.Primary ? p.Primary(action.Label, 30) : p.SecondaryButton(action.Label, 30);
                button.Margin = new Thickness(0, 0, 8, 6);
                var chosen = action;
                button.Clicked += (_, _) => ActionChosen?.Invoke(this, chosen);
                buttons.Children.Add(button);
            }

            _stack.Children.Add(buttons);
        }

        if (state.Recent.Count > 0)
        {
            var caption = p.Label("RECENT", p.Secondary, p.CaptionSize, FontWeight.SemiBold);
            caption.CharacterSpacing = 0.6;
            caption.Margin = new Thickness(0, 6, 0, 0);
            _stack.Children.Add(caption);
            foreach (var recent in state.Recent)
            {
                var row = p.Flat(recent.Label, p.Text, 26, p.SmallSize);
                InteractiveStates.Attach(row, p, InteractionKind.Row);
                row.HorizontalOptions = LayoutOptions.Fill;
                row.ClassId = CraftDialog.RowClassId;
                ToolTipProperties.SetText(row, recent.Path ?? recent.Label);
                var chosen = recent;
                row.Clicked += (_, _) => ActionChosen?.Invoke(this, chosen);
                _stack.Children.Add(row);
            }
        }
    }
}

/// <summary>
/// The open menu (<see cref="MenuModel"/>: a title-bar menu or a context menu) drawn in the design's style: the raised
/// card with the strong border and a shadow, each row its title and keys, a line between groups, the rows that cannot run
/// faded, the pointer's or the keyboard's row on the selection's fill. A click runs the row; the keys are the model's.
/// </summary>
public sealed class MenuView : ContentView
{
    private readonly MenuModel _model;
    private readonly List<(Grid Cell, InteractiveStates States)> _rows = [];
    private ShellPalette _palette;

    /// <summary>The view of <paramref name="model"/>'s open menu.</summary>
    public MenuView(MenuModel model, ShellPalette palette)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        model.FocusMoved += (_, _) => MarkFocused();
    }

    /// <summary>Draws the menu open now (call after <see cref="MenuModel.Open"/>).</summary>
    public void Build(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        var p = palette;
        _rows.Clear();
        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(4) };
        var rows = _model.Rows;
        if (rows.Count == 0)
        {
            var none = p.Label("Nothing here yet: plug-ins add items to this menu.", p.Secondary, p.SmallSize);
            none.Margin = new Thickness(10, 6);
            none.LineBreakMode = LineBreakMode.WordWrap;
            stack.Children.Add(none);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.StartsGroup)
            {
                var line = p.Line();
                line.Margin = new Thickness(-4, 4);
                stack.Children.Add(line);
            }

            var cell = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 24, Padding = new Thickness(10, 0), MinimumHeightRequest = 28 };
            var title = p.Label(row.Title);
            cell.Add(title, 0, 0);
            if (row.Chord is { } chord)
            {
                cell.Add(p.Label(chord, p.Secondary, p.SmallSize), 1, 0);
            }

            cell.IsEnabled = row.Enabled;
            var states = InteractiveStates.Attach(cell, p, InteractionKind.MenuItem);
            states.Label = title;
            SemanticProperties.SetDescription(cell, row.Chord is null ? row.Title : $"{row.Title}, {row.Chord}");
            if (row.Enabled)
            {
                int index = i;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => _ = _model.RunAsync(index);
                cell.GestureRecognizers.Add(tap);
                var pointer = new PointerGestureRecognizer();
                pointer.PointerEntered += (_, _) => _model.SetFocus(index);
                cell.GestureRecognizers.Add(pointer);
            }

            _rows.Add((cell, states));
            stack.Children.Add(cell);
        }

        Content = new Border
        {
            Content = stack,
            Padding = new Thickness(0),
            Stroke = p.BorderStrong,
            StrokeThickness = 1,
            BackgroundColor = p.Raised,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.35f, Radius = 14, Offset = new Point(0, 4) },
        };
        MarkFocused();
    }

    private void MarkFocused()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].States.Selected = i == _model.Focused;
        }
    }
}
