// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Settings;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The settings page: every Craft option and every plug-in's declared setting, each with its variable, its meaning, its
/// default, its value in effect and where that comes from (its default, the user's <c>settings.json</c>, the workspace's
/// <c>.craft/settings.json</c>, or the environment, which overrides both files and is shown as such). A value is written
/// into the file picked at the top (the workspace's when a folder is open, else the user's), checked first: one that does
/// not parse is refused with the reason and nothing is written. "Clear" takes it out of that file. Written values apply at
/// once, except those read at start (said so). A search filters by variable or meaning. Phase U2: it is a tab of the
/// editor area (<see cref="ShellPageIds.Settings"/>), never a page over the window, so the menus stay in reach.
/// </summary>
public sealed class SettingsPage : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly VerticalStackLayout _rows = new() { Spacing = 10, Padding = new Thickness(12, 4, 12, 16) };
    private readonly Picker _layer = new() { Title = "Write to", MinimumWidthRequest = 240 };
    private readonly Entry _search = new() { Placeholder = "Search settings", MinimumWidthRequest = 200 };
    private readonly Label _problems = new() { FontSize = 12, IsVisible = false };
    private readonly Label _files = new() { FontSize = 12 };
    private Color _text = Colors.Gray;
    private Color _muted = Colors.Gray;
    private Color _error = Colors.IndianRed;

    /// <summary>The page of <paramref name="shell"/>'s settings, filtered by <paramref name="search"/> when given.</summary>
    public SettingsPage(ShellModel shell, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _search.Text = search ?? "";
        var close = new Button { Text = "Close" };
        close.Clicked += (_, _) => _shell.Pages.Close(ShellPageIds.Settings);
        _search.TextChanged += (_, _) => Rebuild();
        _layer.SelectedIndexChanged += (_, _) => Rebuild();
        var header = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Padding = new Thickness(12, 8),
            Children =
            {
                new Label { Text = "Settings", FontSize = 20, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 16, 0), VerticalTextAlignment = TextAlignment.Center },
                _layer,
                Field(_search),
                close,
            },
        };
        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(header, 0, 0);
        grid.Add(new VerticalStackLayout { Padding = new Thickness(12, 0), Spacing = 4, Children = { _files, _problems } }, 0, 1);
        grid.Add(new ScrollView { Content = _rows }, 0, 2);
        Content = grid;
        ApplyTheme();
        FillLayers();
        _shell.OptionsChanged += OnShellChanged;
        _shell.WorkspaceChanged += OnWorkspaceChanged;
        _shell.ThemeChanged += OnThemeChanged;
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shell.OptionsChanged -= OnShellChanged;
        _shell.WorkspaceChanged -= OnWorkspaceChanged;
        _shell.ThemeChanged -= OnThemeChanged;
    }

    // Craft's one input style (the owner's note 12), in the shell's theme.
    private CraftInput Field(Entry entry)
    {
        var palette = new ShellPalette(_shell.Theme, _shell.Options);
        entry.FontFamily = palette.Font();
        entry.FontSize = palette.FontSize;
        var field = palette.Field(entry);
        field.HeightRequest = 34;
        field.MinimumWidthRequest = entry.MinimumWidthRequest + 16;
        return field;
    }

    private SettingLayer Layer => _layer.SelectedIndex == 0 && _shell.Settings.Workspace is not null ? SettingLayer.Workspace : SettingLayer.User;

    private void OnShellChanged(object? sender, EventArgs e) => Rebuild();

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        FillLayers();
        Rebuild();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyTheme();
        Rebuild();
    }

    private void ApplyTheme()
    {
        var colors = _shell.Theme.Colors;
        _text = ThemeColors.Of(colors.Foreground);
        _muted = ThemeColors.Of(colors.GutterText);
        _error = _shell.Theme.IsDark ? Colors.LightCoral : Colors.Firebrick;
        BackgroundColor = ThemeColors.Of(colors.Background);
        _problems.TextColor = _error;
        _files.TextColor = _muted;
    }

    private void FillLayers()
    {
        _layer.Items.Clear();
        if (_shell.Settings.Workspace is { } workspace)
        {
            _layer.Items.Add("This workspace (.craft/settings.json)");
            _files.Text = $"Workspace: {workspace.FilePath}\nUser: {_shell.Settings.User?.FilePath ?? "(none)"}\nThe environment's CRAFT_* variables override both files.";
        }
        else
        {
            _files.Text = $"User: {_shell.Settings.User?.FilePath ?? "(none)"} (open a folder to write its .craft/settings.json)\nThe environment's CRAFT_* variables override the file.";
        }

        _layer.Items.Add("User (every workspace)");
        _layer.SelectedIndex = 0;
    }

    private void Rebuild()
    {
        var problems = _shell.OptionsProblems;
        _problems.IsVisible = problems.Count > 0;
        _problems.Text = string.Join("\n", problems);
        var query = (_search.Text ?? "").Trim();
        var layer = Layer;
        _rows.Children.Clear();
        foreach (var row in _shell.SettingRows())
        {
            var variable = row.Variable;
            if (query.Length > 0 && !variable.Name.Contains(query, StringComparison.OrdinalIgnoreCase) && !variable.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _rows.Children.Add(MakeRow(row, layer));
        }
    }

    private VerticalStackLayout MakeRow(SettingRow row, SettingLayer layer)
    {
        var variable = row.Variable;
        var fileValue = layer == SettingLayer.Workspace ? row.WorkspaceValue : row.UserValue;
        bool allowed = CraftSettings.Allows(layer, variable.Name);
        var entry = new Entry
        {
            Text = fileValue ?? "",
            Placeholder = row.Value ?? variable.Default,
            MinimumWidthRequest = 220,
            IsEnabled = allowed,
            TextColor = _text,
        };
        var error = new Label { FontSize = 12, TextColor = _error, IsVisible = false };
        var set = new Button { Text = "Set", IsEnabled = allowed };
        var clear = new Button { Text = "Clear", IsEnabled = allowed && fileValue is not null };
        set.Clicked += (_, _) => Write(variable.Name, entry.Text, error);
        entry.Completed += (_, _) => Write(variable.Name, entry.Text, error);
        clear.Clicked += (_, _) => Write(variable.Name, null, error);

        string source = row.Source switch
        {
            SettingSource.Environment => $"from the environment ({variable.Name}), which overrides the files",
            SettingSource.Workspace => "from this workspace's .craft/settings.json",
            SettingSource.User => "from the user's settings.json",
            _ => "the default",
        };
        string effect = row.Value is null ? $"In effect: {variable.Default} (the default)" : $"In effect: {row.Value}, {source}";
        if (ShellModel.AppliedAtStart.Contains(variable.Name))
        {
            effect += ". A change applies at the next start.";
        }

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = variable.Name, FontAttributes = FontAttributes.Bold, TextColor = _text },
                new Label { Text = $"{variable.Meaning} Default: {variable.Default}.", FontSize = 12, TextColor = _muted },
                new FlexLayout
                {
                    Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                    AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
                    Children = { Field(entry), set, clear },
                },
                new Label { Text = effect, FontSize = 12, TextColor = row.Source == SettingSource.Environment ? _error : _muted },
                error,
            },
        };
        if (row.Note is { } note)
        {
            stack.Children.Insert(4, new Label { Text = note, FontSize = 12, TextColor = _muted });
        }

        return stack;
    }

    private void Write(string variable, string? value, Label error)
    {
        if (_shell.ApplySetting(Layer, variable, value) is { } problem)
        {
            error.Text = problem;
            error.IsVisible = true;
            return;
        }

        // Applied: OptionsChanged rebuilds the rows when the options changed; a plug-in's setting or a value equal to the
        // one in effect changes no option, so the rows are rebuilt here too.
        Rebuild();
    }
}
