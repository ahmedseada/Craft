// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The inline bar with Undo (<see cref="UndoBar"/>): at the bottom of the editor area, what was done and Undo, for
/// <c>CRAFT_UNDO_BAR_MS</c>, in place of a confirmation dialog; × keeps the change and hides it at once.
/// </summary>
public sealed class UndoBarView : ContentView, IDisposable
{
    private readonly UndoBar _bar;
    private readonly Label _text;
    private readonly Button _undo;
    private readonly Button _close;
    private readonly Border _frame;

    /// <summary>The bar over <paramref name="bar"/>, in <paramref name="palette"/>'s colours.</summary>
    public UndoBarView(UndoBar bar, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(palette);
        _bar = bar;
        _text = palette.Label("");
        _text.VerticalOptions = LayoutOptions.Center;
        _undo = palette.Flat("Undo", palette.Accent, 26);
        _undo.FontFamily = palette.Font(FontWeight.SemiBold);
        _undo.Clicked += (_, _) => _bar.Undo();
        _close = palette.Flat("×", palette.Secondary, 26, palette.SmallSize);
        _close.WidthRequest = 26;
        _close.Padding = new Thickness(0);
        ToolTipProperties.SetText(_close, "Keep the change");
        _close.Clicked += (_, _) => _bar.Dismiss();
        var row = new HorizontalStackLayout { Spacing = 12, Children = { _text, _undo, _close } };
        _frame = new Border
        {
            Content = row,
            Padding = new Thickness(14, 4, 6, 4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 12, Offset = new Point(0, 4) },
        };
        Content = _frame;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(0, 0, 0, 12);
        IsVisible = false;
        Apply(palette);
        bar.Changed += OnChanged;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _frame.BackgroundColor = palette.Raised;
        _frame.Stroke = palette.BorderStrong;
        _text.TextColor = palette.Text;
        _undo.TextColor = palette.Accent;
        _close.TextColor = palette.Secondary;
    }

    /// <inheritdoc />
    public void Dispose() => _bar.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e)
    {
        var current = _bar.Current;
        IsVisible = current is not null;
        _text.Text = current?.Text ?? "";
    }
}
