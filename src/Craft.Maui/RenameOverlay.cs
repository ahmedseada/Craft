// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// F2's rename box (the X restart's piece 3b): a raised field over the name being renamed, the name in it selected. Enter
/// commits (the shell computes the edits off the UI thread and shows them as one diff), Escape cancels; the editor keeps
/// its text until the user accepts the diff.
/// </summary>
public sealed class RenameOverlay : ContentView
{
    private readonly RoutedEntry _input;
    private readonly Label _hint;
    private readonly Border _card;
    private bool _ended;

    /// <summary>The box for <paramref name="request"/>, in <paramref name="palette"/>'s colours.</summary>
    public RenameOverlay(RenameRequest request, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(palette);
        Request = request;
        _input = new RoutedEntry
        {
            Text = request.Target.Name,
            FontFamily = palette.Options.FontFamily,
            FontSize = palette.Options.FontSize,
            TextColor = palette.Text,
            HeightRequest = 30,
            MinimumWidthRequest = 180,
        };
        _input.RouteKey = OnKey;
        _hint = palette.Label("Enter to rename · Escape to cancel", palette.Secondary, palette.CaptionSize);
        _hint.Margin = new Thickness(4, 2, 4, 0);
        _card = new Border
        {
            Content = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(6, 4), Children = { _input, _hint } },
            BackgroundColor = palette.Raised,
            Stroke = palette.Accent,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 12, Offset = new Point(0, 4) },
        };
        Content = _card;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;
        _input.Unfocused += (_, _) => End(commit: false);
    }

    /// <summary>The rename the box is for.</summary>
    public RenameRequest Request { get; }

    /// <summary>Raised with the new name on Enter.</summary>
    public event EventHandler<string>? Committed;

    /// <summary>Raised on Escape, or when the box loses the keyboard without Enter.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Takes the keyboard with the whole name selected.</summary>
    public void FocusInput()
    {
        _input.Focus();
        _input.CursorPosition = 0;
        _input.SelectionLength = (_input.Text ?? "").Length;
    }

    private bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        switch (RenameRequest.ActionFor(key, modifiers))
        {
            case RenameKeyAction.Commit:
                End(commit: true);
                return true;
            case RenameKeyAction.Cancel:
                End(commit: false);
                return true;
            default:
                return false;
        }
    }

    private void End(bool commit)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        if (commit)
        {
            Committed?.Invoke(this, _input.Text ?? "");
        }
        else
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}
