// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Settings;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The settings page (the owner's note 13), a tab of the editor area (phase U2): the sections in a left list, a search
/// across every section, and each setting with the control its declared shape asks for (a switch, a number box with
/// steppers and its unit, a drop-down of its choices or of a registered source's names, a font picker with a preview,
/// a text box), drawn from <see cref="SettingsPageModel"/>, never written per setting. A change applies at once and goes
/// to the file chosen at the top (the user's or this workspace's); a setting something sets is marked modified with a
/// reset, a section has its reset, the variable is shown small, and a value the environment sets is read-only with why.
/// The Keyboard section is <see cref="KeyboardSection"/>. Controls come from <see cref="ShellPalette"/>, so the design's
/// input style applies to them in one place.
/// </summary>
public sealed class SettingsPage : ContentView, IDisposable
{
    private const double NavWidth = 220;
    private const double ControlWidth = 300;
    private readonly ShellModel _shell;
    private readonly SettingsPageModel _model;
    private readonly KeyboardModel _keyboard;
    private readonly VerticalStackLayout _nav = new() { Spacing = 2, Padding = new Thickness(10, 12) };
    private readonly VerticalStackLayout _main = new() { Spacing = 14, Padding = new Thickness(28, 20) };
    private readonly ScrollView _mainScroll = new();
    private readonly Picker _narrowSections = new();
    private Grid _body = new();
    private ShellPalette _palette;
    private Entry _search;
    private TypingDebounce? _searchDebounce;
    private CraftInput _searchField;
    private string _section;
    private KeyboardSection? _keyboardView;

    /// <summary>The page of <paramref name="shell"/>'s settings, filtered by <paramref name="search"/> when given.</summary>
    public SettingsPage(ShellModel shell, string? search = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _model = new SettingsPageModel(shell);
        _keyboard = new KeyboardModel(shell);
        _section = shell.SettingsSection;
        _palette = new ShellPalette(shell.Theme, shell.Options);
        _search = _palette.Input("Search every setting", 32);
        _search.Text = search ?? "";
        _searchField = _palette.Field(_search);
        Build();
        _shell.OptionsChanged += OnChanged;
        _shell.WorkspaceChanged += OnChanged;
        _shell.ThemeChanged += OnThemeChanged;
        _shell.SettingsSectionChanged += OnSectionRequested;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shell.OptionsChanged -= OnChanged;
        _shell.WorkspaceChanged -= OnChanged;
        _shell.ThemeChanged -= OnThemeChanged;
        _shell.SettingsSectionChanged -= OnSectionRequested;
        _keyboardView?.Dispose();
        _keyboardView = null;
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void OnThemeChanged(object? sender, EventArgs e) => Build();

    private void OnSectionRequested(object? sender, EventArgs e)
    {
        _section = _shell.SettingsSection;
        _search.Text = "";
        Rebuild();
    }

    // The frame: the header, the left list beside the section; made again when the theme changes.
    private void Build()
    {
        _palette = new ShellPalette(_shell.Theme, _shell.Options);
        var p = _palette;
        string text = _search.Text ?? "";
        _search = p.Input("Search every setting", 32);
        _search.Text = text;
        var debounce = _searchDebounce ??= new TypingDebounce(this, () => _shell.Options.SearchDelayMilliseconds, Rebuild);
        _search.TextChanged += (_, _) => debounce.Restart();
        _search.Completed += (_, _) => debounce.Flush();
        BackgroundColor = p.Ground;
        _narrowSections.TextColor = p.Text;
        _narrowSections.BackgroundColor = p.Raised;
        _narrowSections.IsVisible = false;
        _narrowSections.SelectedIndexChanged -= OnNarrowSection;
        _narrowSections.SelectedIndexChanged += OnNarrowSection;
        _searchField = Wrap(_search, 420);
        var bar = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(16, 10), BackgroundColor = p.Surface, Children = { _searchField, _narrowSections } };
        var left = new ScrollView { Content = _nav, BackgroundColor = p.Surface };
        _mainScroll.Content = _main;
        _body = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(NavWidth)), new ColumnDefinition(new GridLength(1)), new ColumnDefinition(GridLength.Star)],
            BackgroundColor = p.Surface,
        };
        _body.Add(left, 0, 0);
        _body.Add(p.Line(vertical: true), 1, 0);
        _body.Add(_mainScroll, 2, 0);
        var page = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)] };
        page.Add(PageParts.Header(p, "Settings", () => PageParts.Close(_shell, ShellPageIds.Settings)), 0, 0);
        page.Add(p.Line(), 0, 1);
        page.Add(bar, 0, 2);
        page.Add(p.Line(), 0, 3);
        page.Add(_body, 0, 4);
        Content = page;
        SizeChanged -= OnSizeChanged;
        SizeChanged += OnSizeChanged;
        _keyboardView?.Dispose();
        _keyboardView = null;
        Rebuild();
    }

    // The layout follows the measured width: below the narrow width the left list folds into a drop-down.
    private void OnSizeChanged(object? sender, EventArgs e)
    {
        bool narrow = Width > 0 && Width < _shell.Options.NarrowWidth;
        _body.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(NavWidth);
        _narrowSections.IsVisible = narrow;
        _main.Padding = narrow ? new Thickness(12, 12) : new Thickness(28, 20);
        _searchField.WidthRequest = narrow ? Math.Max(120, Width - 200) : 420;
    }

    private void OnNarrowSection(object? sender, EventArgs e)
    {
        var sections = _model.Sections();
        if (_narrowSections.SelectedIndex >= 0 && _narrowSections.SelectedIndex < sections.Count && sections[_narrowSections.SelectedIndex].Id != _section)
        {
            Choose(sections[_narrowSections.SelectedIndex].Id);
        }
    }

    private void Choose(string section)
    {
        _section = section;
        _search.Text = "";
        Rebuild();
        _ = _mainScroll.ScrollToAsync(0, 0, false);
    }

    private void Rebuild()
    {
        var p = _palette;
        var sections = _model.Sections();
        if (!sections.Any(s => s.Id == _section))
        {
            _section = sections.Count > 0 ? sections[0].Id : SettingSections.Editor;
        }

        _nav.Children.Clear();
        _narrowSections.Items.Clear();
        string query = (_search.Text ?? "").Trim();
        foreach (var section in sections)
        {
            bool here = query.Length == 0 && section.Id == _section;
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(10, 0), HeightRequest = 32 };
            row.BackgroundColor = here ? p.Selection : Colors.Transparent;
            row.Add(p.Label(section.Title, here ? p.Text : p.Secondary, weight: here ? FontWeight.Medium : FontWeight.Regular), 0, 0);
            if (section.Modified > 0)
            {
                var count = p.Label(section.Modified.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Accent, p.CaptionSize);
                ToolTipProperties.SetText(count, $"{section.Modified} set here (modified)");
                row.Add(count, 1, 0);
            }

            var tap = new TapGestureRecognizer();
            string id = section.Id;
            tap.Tapped += (_, _) => Choose(id);
            row.GestureRecognizers.Add(tap);
            _nav.Children.Add(new Border { Content = row, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, BackgroundColor = row.BackgroundColor });
            _narrowSections.Items.Add(section.Title);
        }

        _narrowSections.SelectedIndex = sections.ToList().FindIndex(s => s.Id == _section);
        _main.Children.Clear();
        if (query.Length == 0 && _section == SettingSections.Keyboard)
        {
            _keyboardView ??= new KeyboardSection(_shell, _keyboard, _palette);
            _keyboardView.Refresh();
            _main.Children.Add(_keyboardView);
            return;
        }

        _main.Children.Add(Top(query, sections));
        foreach (var problem in _model.Problems)
        {
            var label = p.Label(problem, p.Error, p.SmallSize);
            label.LineBreakMode = LineBreakMode.WordWrap;
            _main.Children.Add(label);
        }

        var groups = _model.Groups(_section, query);
        if (groups.Count == 0)
        {
            _main.Children.Add(p.Label(query.Length > 0 ? $"No setting matches '{query}'." : "Nothing to set here.", p.Secondary));
        }

        foreach (var group in groups)
        {
            var stack = new VerticalStackLayout { Spacing = 0 };
            if (group.Title.Length > 0)
            {
                var heading = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
                heading.Children.Add(p.Label(group.Title, p.Text, p.FontSize + 1, FontWeight.SemiBold));
                if (group.Plugin is not null)
                {
                    heading.Children.Add(p.Label("plug-in", p.Secondary, p.CaptionSize));
                }

                stack.Children.Add(heading);
            }

            foreach (var entry in group.Entries)
            {
                stack.Children.Add(Row(entry));
                stack.Children.Add(p.Line());
            }

            _main.Children.Add(stack);
        }

        _main.Children.Add(Files());
    }

    // The section's title, where a change goes (the user's file or this workspace's), the section's reset.
    private FlexLayout Top(string query, IReadOnlyList<SettingSectionRow> sections)
    {
        var p = _palette;
        string title = query.Length > 0 ? $"Results for '{query}'" : SettingSections.TitleOf(_section);
        var top = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        var heading = p.Label(title, p.Text, p.FontSize + 7, FontWeight.SemiBold);
        heading.Margin = new Thickness(0, 0, 20, 6);
        top.Children.Add(heading);
        var scope = _model.Scope;
        var segmented = PageParts.Segmented(p,
        [
            ("User", scope == SettingLayer.User, true, () => SetScope(SettingLayer.User)),
            ("This workspace", scope == SettingLayer.Workspace, _model.CanWriteWorkspace, () => SetScope(SettingLayer.Workspace)),
        ]);
        ToolTipProperties.SetText(segmented, _model.CanWriteWorkspace
            ? "Where a change goes: your settings (every workspace) or this workspace's .craft/settings.json"
            : "Open a folder to change its own .craft/settings.json");
        segmented.Margin = new Thickness(0, 0, 12, 6);
        top.Children.Add(segmented);
        if (query.Length == 0 && sections.FirstOrDefault(s => s.Id == _section) is { Modified: > 0 })
        {
            var reset = p.SecondaryButton("Reset section");
            ToolTipProperties.SetText(reset, $"Take this section's values out of {(scope == SettingLayer.User ? "your settings" : "this workspace's settings")}");
            reset.Clicked += (_, _) => Report(_model.ResetSection(_section));
            reset.Margin = new Thickness(0, 0, 0, 6);
            top.Children.Add(reset);
        }

        if (query.Length == 0 && _section == SettingSections.Models)
        {
            var models = p.SecondaryButton("Models and devices…");
            models.Clicked += async (_, _) => await _shell.Host.ExecuteCommandAsync(ShellCommandIds.Models);
            models.Margin = new Thickness(12, 0, 0, 6);
            top.Children.Add(models);
        }

        return top;
    }

    private void SetScope(SettingLayer layer)
    {
        _model.Scope = layer;
        Rebuild();
    }

    // One setting: its title (marked when modified), what it does, its variable small, then its control and its reset.
    private Grid Row(SettingEntry entry)
    {
        var p = _palette;
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 24,
            Padding = new Thickness(0, 12),
        };
        var text = new VerticalStackLayout { Spacing = 3 };
        var titleRow = new HorizontalStackLayout { Spacing = 8 };
        titleRow.Children.Add(p.Label(entry.Title, p.Text, weight: FontWeight.Medium));
        if (entry.IsModified)
        {
            var mark = p.Label(entry.Source switch
            {
                SettingSource.Environment => "environment",
                SettingSource.Workspace => "modified · workspace",
                _ => "modified · user",
            }, entry.Source == SettingSource.Environment ? p.Warning : p.Accent, p.CaptionSize);
            ToolTipProperties.SetText(mark, $"Its default is {Shown(entry.Default)}");
            titleRow.Children.Add(mark);
        }

        text.Children.Add(titleRow);
        var description = p.Label(entry.Description, p.Secondary, p.SmallSize);
        description.LineBreakMode = LineBreakMode.WordWrap;
        text.Children.Add(description);
        var variable = p.Label(entry.Variable, p.Secondary, p.CaptionSize);
        variable.FontFamily = _shell.Options.FontFamily;
        variable.Opacity = 0.75;
        text.Children.Add(variable);
        foreach (var why in (ReadOnlySpan<string?>)[entry.ReadOnlyReason, entry.Note])
        {
            if (why is not null)
            {
                var label = p.Label(why, entry.IsReadOnly ? p.Warning : p.Secondary, p.CaptionSize);
                label.LineBreakMode = LineBreakMode.WordWrap;
                text.Children.Add(label);
            }
        }

        var error = p.Label("", p.Error, p.CaptionSize);
        error.LineBreakMode = LineBreakMode.WordWrap;
        error.IsVisible = false;
        text.Children.Add(error);
        grid.Add(text, 0, 0);

        var control = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        control.Children.Add(Control(entry, error));
        if (entry.CanReset)
        {
            var reset = p.Flat("↺", p.Secondary, 28, p.FontSize + 2);
            ToolTipProperties.SetText(reset, $"Reset (take it out of {(_model.Scope == SettingLayer.User ? "your settings" : "this workspace's settings")}; default {Shown(entry.Default)})");
            reset.Clicked += (_, _) => Apply(error, _model.Reset(entry.Variable));
            control.Children.Add(reset);
        }

        grid.Add(control, 1, 0);
        return grid;
    }

    private View Control(SettingEntry entry, Label error)
    {
        var p = _palette;
        bool enabled = !entry.IsReadOnly;
        switch (entry.Shape.Kind)
        {
            case SettingKind.Toggle:
            {
                var toggle = new Switch { IsToggled = IsTrue(entry.Value), IsEnabled = enabled, OnColor = p.Accent, ThumbColor = p.Text };
                toggle.Toggled += (_, e) => Apply(error, _model.Set(entry.Variable, e.Value ? "true" : "false"));
                return toggle;
            }

            case SettingKind.Whole or SettingKind.Number:
                return NumberBox(entry, error, enabled);

            case SettingKind.Choice:
                return ChoiceBox(entry, error, enabled);

            case SettingKind.Font:
                return FontBox(entry, error, enabled);

            case SettingKind.KeyBindings:
            {
                var open = p.SecondaryButton(_shell.Options.KeyBindings.Count == 0 ? "Edit in Keyboard…" : $"{_shell.Options.KeyBindings.Count} bindings · Edit in Keyboard…", 32);
                open.Clicked += (_, _) => Choose(SettingSections.Keyboard);
                return open;
            }

            default:
                return Wrap(TextBox(entry, error, enabled), ControlWidth);
        }
    }

    // Craft's one input style (CraftInput, the owner's note 12) around a field, so every box of the page looks alike.
    private CraftInput Wrap(Entry entry, double width)
    {
        var field = _palette.Field(entry);
        field.WidthRequest = width;
        field.HeightRequest = 34;
        return field;
    }

    private Entry TextBox(SettingEntry entry, Label error, bool enabled)
    {
        var box = _palette.Input(Shown(entry.Default), 32, code: entry.Shape.Kind is SettingKind.Path or SettingKind.List);
        box.Text = entry.Source == SettingSource.Default ? "" : entry.Value;
        box.IsEnabled = enabled;
        string start = box.Text ?? "";
        void Commit()
        {
            var text = (box.Text ?? "").Trim();
            if (text != start.Trim())
            {
                Apply(error, _model.Set(entry.Variable, text.Length == 0 ? null : text));
            }
        }

        box.Completed += (_, _) => Commit();
        box.Unfocused += (_, _) => Commit();
        return box;
    }

    // A number box with steppers on each side and its unit after it; the steppers keep the value within its range.
    private HorizontalStackLayout NumberBox(SettingEntry entry, Label error, bool enabled)
    {
        var p = _palette;
        var minus = p.SecondaryButton("−", 32);
        var plus = p.SecondaryButton("+", 32);
        minus.WidthRequest = plus.WidthRequest = 32;
        minus.Padding = plus.Padding = new Thickness(0);
        var box = TextBox(entry, error, enabled);
        box.Text = entry.Value;
        box.Keyboard = Keyboard.Numeric;
        box.HorizontalTextAlignment = TextAlignment.End;
        minus.IsEnabled = plus.IsEnabled = enabled;
        minus.Clicked += (_, _) => Apply(error, _model.Set(entry.Variable, SettingsPageModel.Step(entry, -1)));
        plus.Clicked += (_, _) => Apply(error, _model.Set(entry.Variable, SettingsPageModel.Step(entry, 1)));
        var range = entry.Shape.Min is { } min && entry.Shape.Max is { } max ? $"{SettingsPageModel.FormatNumber(min)} to {SettingsPageModel.FormatNumber(max)}" : null;
        if (range is not null)
        {
            ToolTipProperties.SetText(box, range + (entry.Shape.Unit is { } u ? " " + u : ""));
        }

        var row = new HorizontalStackLayout { Spacing = 4, Children = { minus, Wrap(box, 96), plus } };
        if (entry.Shape.Unit is { } unit)
        {
            row.Children.Add(p.Label(unit, p.Secondary, p.SmallSize));
        }

        return row;
    }

    // A drop-down of the choices; a setting that takes another value too has a box beside it.
    private HorizontalStackLayout ChoiceBox(SettingEntry entry, Label error, bool enabled)
    {
        var p = _palette;
        var picker = new Picker { TextColor = p.Text, BackgroundColor = p.Raised, TitleColor = p.Secondary, WidthRequest = entry.Shape.OtherAllowed ? 180 : ControlWidth, IsEnabled = enabled, FontFamily = p.Font(), FontSize = p.FontSize };
        foreach (var choice in entry.Choices)
        {
            picker.Items.Add(choice.Length == 0 ? "(automatic)" : choice);
        }

        int index = -1;
        for (int i = 0; i < entry.Choices.Count; i++)
        {
            if (string.Equals(entry.Choices[i], entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
            }
        }

        picker.SelectedIndex = index;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < entry.Choices.Count && !string.Equals(entry.Choices[picker.SelectedIndex], entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                var value = entry.Choices[picker.SelectedIndex];
                Apply(error, _model.Set(entry.Variable, value.Length == 0 ? null : value));
            }
        };
        var row = new HorizontalStackLayout { Spacing = 6, Children = { picker } };
        if (entry.Shape.OtherAllowed)
        {
            var other = TextBox(entry, error, enabled);
            other.Placeholder = "or another";
            other.Text = index < 0 && entry.Source != SettingSource.Default ? entry.Value : "";
            row.Children.Add(Wrap(other, 120));
        }

        return row;
    }

    // A font: the known families in a drop-down, any other typed, and a preview drawn in it.
    private VerticalStackLayout FontBox(SettingEntry entry, Label error, bool enabled)
    {
        var p = _palette;
        var preview = p.Label("public int Count => 0O1lI; // {}[]", p.Text, Math.Max(p.FontSize, 13));
        preview.FontFamily = entry.Value.Length > 0 ? entry.Value : null;
        ToolTipProperties.SetText(preview, $"A preview in {Shown(entry.Value)}");
        var choices = ChoiceBox(entry with { Shape = entry.Shape with { OtherAllowed = true } }, error, enabled);
        return new VerticalStackLayout { Spacing = 6, Children = { choices, p.Box(preview, 6, new Thickness(10, 6), p.Raised) } };
    }

    private Label Files()
    {
        var (user, workspace) = _model.Files;
        var text = $"Your settings: {user ?? "(none: no data folder)"}" + (workspace is null ? "\nOpen a folder to set its own .craft/settings.json." : $"\nThis workspace: {workspace}")
            + "\nA CRAFT_* environment variable overrides both files; such a value is shown read-only.";
        var label = _palette.Label(text, _palette.Secondary, _palette.CaptionSize);
        label.LineBreakMode = LineBreakMode.WordWrap;
        label.Margin = new Thickness(0, 16, 0, 0);
        return label;
    }

    // A change applied (the options reload and the page with them), or the reason it was refused under its row.
    private void Apply(Label error, string? problem)
    {
        if (problem is null)
        {
            Rebuild();      // a plug-in's setting or an unchanged value raises no OptionsChanged
            return;
        }

        error.Text = problem;
        error.IsVisible = true;
    }

    private void Report(string? problem)
    {
        if (problem is not null)
        {
            _shell.Host.ShowStatus("Settings", problem, NotificationSeverity.Warning);
        }

        Rebuild();
    }

    private static bool IsTrue(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static string Shown(string value) => value.Length == 0 ? "(empty)" : value;
}
