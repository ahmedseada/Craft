// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The recovery prompt's card (<see cref="RecoveryOffer"/>): at the top of the editor area, not modal, "N unsaved files were
/// recovered" and a row per file (name, folder, time, size) with Restore, Compare and Discard; Restore all and Discard all at
/// its foot; × hides it (the notification centre keeps the files). Its actions run the shell's (<see cref="ShellModel"/>).
/// </summary>
public sealed class RecoveryCardView : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly Label _title;
    private readonly Label _text;
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly HorizontalStackLayout _foot = new() { Spacing = 8 };
    private readonly Button _close;
    private readonly Border _frame;
    private ShellPalette _palette;

    /// <summary>The card over <paramref name="shell"/>'s offer, in <paramref name="palette"/>'s colours.</summary>
    public RecoveryCardView(ShellModel shell, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(palette);
        _shell = shell;
        _palette = palette;
        _title = palette.Label("", palette.Text);
        _title.FontFamily = palette.Font(FontWeight.SemiBold);
        _text = palette.Label("Craft ended without saving them. Restore opens each with its unsaved changes (Ctrl+Z goes back to the disk's text); Compare shows the difference first.", palette.Secondary, palette.SmallSize);
        _text.LineBreakMode = LineBreakMode.WordWrap;
        _close = palette.Flat("×", palette.Secondary, 26, palette.SmallSize);
        _close.WidthRequest = 26;
        _close.Padding = new Thickness(0);
        ToolTipProperties.SetText(_close, "Hide (the notification centre keeps them)");
        _close.Clicked += (_, _) => _shell.Recovery.HideCard();
        var head = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        head.Add(_title, 0, 0);
        head.Add(_close, 1, 0);
        _frame = new Border
        {
            Content = new VerticalStackLayout { Spacing = 8, Children = { head, _text, new ScrollView { Content = _rows, MaximumHeightRequest = 240 }, _foot } },
            Padding = new Thickness(16, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 16, Offset = new Point(0, 6) },
        };
        Content = _frame;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Start;
        Margin = new Thickness(16, 16, 16, 0);
        MaximumWidthRequest = 640;
        IsVisible = false;
        SemanticProperties.SetHeadingLevel(_title, SemanticHeadingLevel.Level2);
        shell.Recovery.Changed += OnChanged;
        Apply(palette);
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _frame.BackgroundColor = palette.Raised;
        _frame.Stroke = palette.BorderStrong;
        _title.TextColor = palette.Text;
        _text.TextColor = palette.Secondary;
        _close.TextColor = palette.Secondary;
        Build();
    }

    /// <inheritdoc />
    public void Dispose() => _shell.Recovery.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Build();

    private void Build()
    {
        var offer = _shell.Recovery;
        IsVisible = offer.CardVisible && offer.Files.Count > 0;
        if (!IsVisible)
        {
            return;
        }

        var p = _palette;
        _title.Text = offer.Title;
        _rows.Children.Clear();
        var now = DateTimeOffset.Now;
        foreach (var file in offer.Files)
        {
            var name = p.Label(file.Describe(now), p.Text, p.SmallSize);
            name.LineBreakMode = LineBreakMode.TailTruncation;
            name.VerticalOptions = LayoutOptions.Center;
            ToolTipProperties.SetText(name, file.Path ?? "A document that was never saved");
            var restore = p.Flat("Restore", p.Accent, 26, p.SmallSize);
            restore.Clicked += (_, _) => _ = _shell.RestoreRecoveredAsync(file);
            var compare = p.Flat("Compare", p.Text, 26, p.SmallSize);
            compare.Clicked += (_, _) => _ = _shell.CompareRecoveredAsync(file);
            var discard = p.Flat("Discard", p.Error, 26, p.SmallSize);
            discard.Clicked += (_, _) => _shell.DiscardRecovered(file);
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 4,
            };
            row.Add(name, 0, 0);
            row.Add(restore, 1, 0);
            row.Add(compare, 2, 0);
            row.Add(discard, 3, 0);
            _rows.Children.Add(row);
        }

        _foot.Children.Clear();
        var all = p.Primary(offer.Files.Count == 1 ? "Restore" : "Restore all", 30);
        all.Clicked += (_, _) => _ = _shell.RestoreAllRecoveredAsync();
        var discardAll = p.SecondaryButton(offer.Files.Count == 1 ? "Discard" : "Discard all", 30);
        discardAll.Clicked += (_, _) => _shell.DiscardAllRecovered();
        _foot.Children.Add(all);
        _foot.Children.Add(discardAll);
    }
}
