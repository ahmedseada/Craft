// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Craft's one text input style (the owner's note 12, the design's inputs: docs/design/Main.dc.html's search box and
/// composer): a single border with the design's radius on the raised fill, the strong border at rest and the accent's
/// ring while the field has the focus, around a platform text field drawn without a border or a fill of its own (on
/// Windows the TextBox's own border, fill and focus underline are taken away by
/// <c>Craft.Maui.WinUI.WindowsControlStyles</c>), so no input shows a box inside a box. Every text input of the shell
/// (the search box, the find bar, the composer, the settings and the pages' fields, the dialogs' prompt) is one. Phase X:
/// its hover (the secondary text's border) and disabled (faded) states too.
/// </summary>
public sealed class CraftInput : Border
{
    private readonly RoundRectangle _shape;
    private ShellPalette _palette;
    private bool _hovered;

    /// <summary>The style around <paramref name="input"/> (an Entry, or an Editor when <paramref name="multiline"/>), <paramref name="radius"/> round.</summary>
    public CraftInput(ShellPalette palette, InputView input, double radius = 6, bool multiline = false)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(input);
        _palette = palette;
        Input = input;
        _shape = new RoundRectangle { CornerRadius = radius };
        StrokeShape = _shape;
        StrokeThickness = 1;
        Padding = multiline ? new Thickness(10, 4) : new Thickness(8, 0);
        Content = input;
        input.BackgroundColor = Colors.Transparent;
        input.VerticalOptions = LayoutOptions.Center;
        input.Focused += (_, _) => ShowFocus(true);
        input.Unfocused += (_, _) => ShowFocus(false);
        // Phase X: the field answers the pointer too (the secondary text's border under it) and fades when it cannot be used.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            _hovered = true;
            ShowFocus(Input.IsFocused);
        };
        pointer.PointerExited += (_, _) =>
        {
            _hovered = false;
            ShowFocus(Input.IsFocused);
        };
        GestureRecognizers.Add(pointer);
        input.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == IsEnabledProperty.PropertyName)
            {
                ShowFocus(Input.IsFocused);
            }
        };
        Apply(palette);
    }

    /// <summary>The field inside.</summary>
    public InputView Input { get; }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BackgroundColor = palette.Raised;
        Input.TextColor = palette.Text;
        Input.PlaceholderColor = palette.Secondary;
        Input.BackgroundColor = Colors.Transparent;
        ShowFocus(Input.IsFocused);
    }

    private void ShowFocus(bool focused)
    {
        // The focus ring: the accent instead of the strong border, the same width (nothing moves when it changes); the
        // pointer over it: the secondary text's colour; disabled: the plain border, the field faded.
        Stroke = !Input.IsEnabled ? _palette.Border : focused ? _palette.Accent : _hovered ? _palette.Secondary : _palette.BorderStrong;
        Opacity = Input.IsEnabled ? 1 : 0.55;
    }
}
