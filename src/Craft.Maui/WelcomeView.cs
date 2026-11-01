// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Craft.Maui;

/// <summary>
/// The welcome page (<see cref="WelcomePageModel"/>), a tab of the editor area: Open folder and New file, the recent
/// folders (a click opens one, × takes it off with Undo in the inline bar), the main shortcuts as they are bound now, and
/// the "Show at start" switch (<c>CRAFT_WELCOME_AT_START</c>). Its two columns wrap into one when the width is narrow.
/// It follows the recent list and the options (a remap shows at once).
/// </summary>
public sealed class WelcomeView : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly WelcomePageModel _model;
    private readonly VerticalStackLayout _recent = new() { Spacing = 2 };
    private readonly VerticalStackLayout _shortcuts = new() { Spacing = 2 };
    private readonly Switch _atStart;
    private readonly Label _atStartNote;
    private ShellPalette _palette;
    private bool _settingSwitch;

    /// <summary>The page over <paramref name="shell"/>, in <paramref name="palette"/>'s colours.</summary>
    public WelcomeView(ShellModel shell, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(palette);
        _shell = shell;
        _model = new WelcomePageModel(shell);
        _palette = palette;
        _atStart = new Switch { OnColor = palette.Accent };
        _atStart.Toggled += OnSwitchToggled;
        _atStartNote = palette.Label("", palette.Secondary, palette.CaptionSize);
        BuildPage();
        shell.Recent.Changed += OnChanged;
        shell.OptionsChanged += OnChanged;
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        BuildPage();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shell.Recent.Changed -= OnChanged;
        _shell.OptionsChanged -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.DispatchLabelled("welcome: the recent list or the keys changed", Fill);

    private void BuildPage()
    {
        var p = _palette;
        BackgroundColor = p.Surface;
        var title = p.Label("Craft", p.Text, p.FontSize + 15, FontWeight.SemiBold);
        var subtitle = p.Label("A C# code editor on Idrak.", p.Secondary, p.FontSize + 1);
        var openFolder = p.Primary("Open folder…");
        openFolder.HorizontalOptions = LayoutOptions.Start;
        openFolder.Clicked += (_, _) => _ = _model.OpenFolderAsync();
        var newFile = p.SecondaryButton("New file", 32);
        newFile.HorizontalOptions = LayoutOptions.Start;
        newFile.Clicked += (_, _) => _model.NewFile();
        var start = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Heading("Start"), new HorizontalStackLayout { Spacing = 10, Children = { openFolder, newFile } }, Heading("Recent folders"), _recent },
        };
        var keys = new VerticalStackLayout { Spacing = 10, Children = { Heading("Shortcuts"), _shortcuts } };
        var columns = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            Direction = FlexDirection.Row,
            AlignItems = FlexAlignItems.Start,
            Children = { Column(start), Column(keys) },
        };
        _atStart.OnColor = p.Accent;
        var showAtStart = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _atStart, p.Label("Show the welcome page at start", p.Text), _atStartNote },
        };
        foreach (var child in showAtStart.Children.OfType<View>())
        {
            child.VerticalOptions = LayoutOptions.Center;
        }

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 24,
                Padding = new Thickness(40, 32),
                MaximumWidthRequest = 980,
                Children = { new VerticalStackLayout { Spacing = 4, Children = { title, subtitle } }, columns, showAtStart },
            },
        };
        Fill();
    }

    private static View Column(View content)
    {
        content.Margin = new Thickness(0, 0, 48, 24);
        content.WidthRequest = 400;
        return content;
    }

    private Label Heading(string text) => _palette.Label(text, _palette.Secondary, _palette.SmallSize, FontWeight.SemiBold);

    // The recent folders, the shortcuts and the switch from the model.
    private void Fill()
    {
        var p = _palette;
        _recent.Children.Clear();
        var folders = _model.RecentFolders;
        if (folders.Count == 0)
        {
            var none = p.Label("No folder opened yet: Open folder (Ctrl+K Ctrl+O) puts one here.", p.Secondary, p.SmallSize);
            none.LineBreakMode = LineBreakMode.WordWrap;
            _recent.Children.Add(none);
        }

        foreach (var folder in folders)
        {
            var name = p.Label(folder.Name, p.Accent);
            name.VerticalOptions = LayoutOptions.Center;
            var place = p.Label(folder.Place, p.Secondary, p.SmallSize);
            place.LineBreakMode = LineBreakMode.HeadTruncation;
            place.VerticalOptions = LayoutOptions.Center;
            var remove = p.Flat("×", p.Secondary, 24, p.SmallSize);
            remove.WidthRequest = 24;
            remove.Padding = new Thickness(0);
            ToolTipProperties.SetText(remove, "Remove from the recent list (Undo in the bar below)");
            string path = folder.Path;
            remove.Clicked += (_, _) => _model.Remove(path);
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 10,
                Padding = new Thickness(6, 2),
                MinimumHeightRequest = 28,
            };
            row.Add(name, 0, 0);
            row.Add(place, 1, 0);
            row.Add(remove, 2, 0);
            ToolTipProperties.SetText(row, path);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _ = _model.OpenAsync(path);
            row.GestureRecognizers.Add(tap);
            _recent.Children.Add(new Border { Content = row, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } });
        }

        _shortcuts.Children.Clear();
        foreach (var shortcut in _model.Shortcuts)
        {
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], MinimumHeightRequest = 26 };
            var what = p.Label(shortcut.Title, p.Text);
            what.VerticalOptions = LayoutOptions.Center;
            row.Add(what, 0, 0);
            var keys = p.Label(shortcut.Keys ?? "not bound", p.Secondary, p.SmallSize);
            keys.FontFamily = p.Options.FontFamily;
            keys.VerticalOptions = LayoutOptions.Center;
            row.Add(new Border
            {
                Content = keys,
                Padding = new Thickness(8, 2),
                StrokeThickness = 1,
                Stroke = p.Border,
                BackgroundColor = p.Raised,
                StrokeShape = new RoundRectangle { CornerRadius = 4 },
            }, 1, 0);
            var tap = new TapGestureRecognizer();
            string command = shortcut.CommandId;
            tap.Tapped += (_, _) => _ = _shell.Host.ExecuteCommandAsync(command);
            row.GestureRecognizers.Add(tap);
            _shortcuts.Children.Add(row);
        }

        _settingSwitch = true;
        _atStart.IsToggled = _model.ShowAtStart;
        _settingSwitch = false;
        _atStart.IsEnabled = _model.ShowAtStartReadOnly is null;
        _atStartNote.Text = _model.ShowAtStartReadOnly is null ? "CRAFT_WELCOME_AT_START" : _model.ShowAtStartReadOnly;
    }

    private void OnSwitchToggled(object? sender, ToggledEventArgs e)
    {
        if (!_settingSwitch && _model.SetShowAtStart(e.Value) is { } problem)
        {
            _atStartNote.Text = problem;
        }
    }
}
