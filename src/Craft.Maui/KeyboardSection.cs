// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The settings page's Keyboard section (the owner's note 16) over <see cref="KeyboardModel"/>: every command with its
/// keys (yours, the keymap's, the defaults, the editor's own), searchable by text or by pressing the keys, conflicts
/// marked, a new chord recorded by pressing it (two for "Ctrl+K Ctrl+C") or typed, a chord removed, a command's keys or
/// all of them reset, and the keymap chosen (VS Code or Visual Studio). While it records, the host's
/// <see cref="Craft.Editor.EditorHost.KeyCapture"/> hands it every chord before any binding runs.
/// </summary>
public sealed class KeyboardSection : ContentView, IDisposable
{
    private readonly ShellModel _shell;
    private readonly KeyboardModel _model;
    private readonly ShellPalette _palette;
    private readonly VerticalStackLayout _rows = new() { Spacing = 0 };
    private readonly Entry _search;
    private readonly Button _searchKeys;
    private readonly Label _notice;
    private readonly KeyRecorder _searchRecorder = new();
    private bool _searchingByKeys;
    private string? _editing;
    private Label? _recorded;
    private Label? _conflict;

    /// <summary>The section over <paramref name="model"/>, in <paramref name="palette"/>'s look.</summary>
    public KeyboardSection(ShellModel shell, KeyboardModel model, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);
        _shell = shell;
        _model = model;
        _palette = palette;
        var p = palette;
        _search = p.Input("Search commands or keys", 32);
        _search.WidthRequest = 320;
        _search.TextChanged += (_, _) => FillRows();
        _searchKeys = p.SecondaryButton("Search by keys", 32);
        ToolTipProperties.SetText(_searchKeys, "Press a shortcut to see what it runs");
        _searchKeys.Clicked += (_, _) => ToggleSearchByKeys();
        _notice = p.Label("", p.Warning, p.SmallSize);
        _notice.LineBreakMode = LineBreakMode.WordWrap;
        _notice.IsVisible = false;
        Content = new VerticalStackLayout { Spacing = 12 };
    }

    /// <inheritdoc />
    public void Dispose() => StopCapture();

    /// <summary>Draws the section again from the model (after a change of the settings).</summary>
    public void Refresh()
    {
        var p = _palette;
        var stack = (VerticalStackLayout)Content;
        stack.Children.Clear();
        var top = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        var heading = p.Label("Keyboard", p.Text, p.FontSize + 7, FontWeight.SemiBold);
        heading.Margin = new Thickness(0, 0, 20, 6);
        top.Children.Add(heading);
        top.Children.Add(KeyMapPicker());
        if (_model.UserBindings.Count > 0)
        {
            var resetAll = p.SecondaryButton("Reset all keys", 32);
            ToolTipProperties.SetText(resetAll, "Take every one of your bindings back: the keymap's and the defaults apply");
            resetAll.Margin = new Thickness(12, 0, 0, 6);
            resetAll.Clicked += (_, _) => Report(_model.ResetAll());
            top.Children.Add(resetAll);
        }

        stack.Children.Add(top);
        if (_model.ReadOnlyReason is { } reason)
        {
            stack.Children.Add(Wrapped(reason, p.Warning));
        }

        var searchField = p.Field(_search);
        searchField.WidthRequest = 320;
        searchField.HeightRequest = 34;
        stack.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { searchField, _searchKeys } });
        stack.Children.Add(Wrapped("Give a command keys with +: press them (a second chord after the first makes one like Ctrl+K Ctrl+C), or type them, then Save. "
            + "Your keys come first, then the keymap's, then the defaults; the editor's own keys show as built in. Saved in your settings (CRAFT_KEY_BINDINGS).", p.Secondary));
        stack.Children.Add(_notice);
        stack.Children.Add(_rows);
        FillRows();
    }

    private Picker KeyMapPicker()
    {
        var p = _palette;
        var maps = KeyboardModel.KeyMaps;
        var picker = new Picker { Title = "Keymap", TextColor = p.Text, BackgroundColor = p.Raised, TitleColor = p.Secondary, WidthRequest = 200, FontFamily = p.Font(), FontSize = p.FontSize };
        foreach (var (_, title) in maps)
        {
            picker.Items.Add($"Keymap: {title}");
        }

        picker.SelectedIndex = maps.ToList().FindIndex(m => string.Equals(m.Name, _model.KeyMap, StringComparison.OrdinalIgnoreCase));
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < maps.Count && !string.Equals(maps[picker.SelectedIndex].Name, _model.KeyMap, StringComparison.OrdinalIgnoreCase))
            {
                Report(_model.SetKeyMap(maps[picker.SelectedIndex].Name));
            }
        };
        picker.Margin = new Thickness(0, 0, 0, 6);
        return picker;
    }

    private void FillRows()
    {
        var p = _palette;
        _rows.Children.Clear();
        var rows = _searchingByKeys && _searchRecorder.First is { } first
            ? _model.Rows(null, first, _searchRecorder.Then)
            : _model.Rows(_searchingByKeys ? null : _search.Text);
        if (rows.Count == 0)
        {
            _rows.Children.Add(p.Label(_searchingByKeys ? $"Nothing runs {_searchRecorder.Text}." : "No command matches.", p.Secondary));
        }

        foreach (var row in rows)
        {
            _rows.Children.Add(Row(row));
            if (row.CommandId == _editing)
            {
                _rows.Children.Add(Editor(row));
            }

            _rows.Children.Add(p.Line());
        }
    }

    private Grid Row(ShortcutRow row)
    {
        var p = _palette;
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 16,
            Padding = new Thickness(0, 8),
        };
        var name = new VerticalStackLayout { Spacing = 2 };
        var title = new HorizontalStackLayout { Spacing = 8, Children = { p.Label(row.Title, p.Text) } };
        if (row.Modified)
        {
            title.Children.Add(p.Label("modified", p.Accent, p.CaptionSize));
        }

        name.Children.Add(title);
        var id = p.Label(row.CommandId, p.Secondary, p.CaptionSize);
        id.FontFamily = _shell.Options.FontFamily;
        id.Opacity = 0.75;
        name.Children.Add(id);
        grid.Add(name, 0, 0);

        var keys = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        if (row.Keys.Count == 0)
        {
            keys.Children.Add(p.Label("no keys", p.Secondary, p.SmallSize));
        }

        foreach (var key in row.Keys)
        {
            keys.Children.Add(Chip(row, key));
        }

        grid.Add(keys, 1, 0);
        var actions = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var add = p.Flat("+", p.Text, 28, p.FontSize + 3);
        ToolTipProperties.SetText(add, "Give it new keys (press them)");
        add.IsEnabled = _model.ReadOnlyReason is null;
        add.Clicked += (_, _) => StartEditing(row.CommandId);
        actions.Children.Add(add);
        if (row.Modified)
        {
            var reset = p.Flat("↺", p.Secondary, 28, p.FontSize + 2);
            ToolTipProperties.SetText(reset, "Reset its keys to the keymap's and the defaults");
            reset.Clicked += (_, _) => Report(_model.Reset(row.CommandId));
            actions.Children.Add(reset);
        }

        grid.Add(actions, 2, 0);
        return grid;
    }

    private Border Chip(ShortcutRow row, ShortcutKeys key)
    {
        var p = _palette;
        var content = new HorizontalStackLayout { Spacing = 6 };
        var text = p.Label(key.Text, p.Text, p.SmallSize);
        text.FontFamily = _shell.Options.FontFamily;
        content.Children.Add(text);
        content.Children.Add(p.Label(key.Source switch
        {
            ShortcutSource.User => "yours",
            ShortcutSource.KeyMap => "keymap",
            ShortcutSource.Editor => "built in",
            _ => "default",
        }, key.Source == ShortcutSource.User ? p.Accent : p.Secondary, p.CaptionSize));
        if (key.Conflict is { } conflict)
        {
            var warning = p.Label("⚠", p.Warning, p.SmallSize);
            ToolTipProperties.SetText(warning, conflict);
            content.Children.Add(warning);
        }

        if (key.Source != ShortcutSource.Editor && _model.ReadOnlyReason is null)
        {
            var remove = p.Flat("×", p.Secondary, 20, p.SmallSize);
            remove.Padding = new Thickness(2, 0);
            ToolTipProperties.SetText(remove, $"Remove {key.Text} from this command");
            remove.Clicked += (_, _) => Report(_model.Remove(row.CommandId, key.Chord, key.Then));
            content.Children.Add(remove);
        }
        else if (key.Source == ShortcutSource.Editor)
        {
            ToolTipProperties.SetText(content, "The editor answers this key itself; give it to another command to take it");
        }

        var chip = p.Box(content, 6, new Thickness(8, 2), key.Conflict is null ? p.Raised : p.RaisedHigh, key.Conflict is null ? p.Border : p.Warning);
        chip.Margin = new Thickness(0, 2, 6, 2);
        return chip;
    }

    // Under the row being given keys: what was pressed, what else they run, a box to type them, Save and Cancel.
    private VerticalStackLayout Editor(ShortcutRow row)
    {
        var p = _palette;
        _recorded = p.Label(_model.Recorder.Text.Length == 0 ? "Press the keys…" : _model.Recorder.Text, p.Accent, p.FontSize + 1, FontWeight.SemiBold);
        _recorded.FontFamily = _shell.Options.FontFamily;
        _conflict = p.Label("", p.Warning, p.SmallSize);
        _conflict.LineBreakMode = LineBreakMode.WordWrap;
        ShowConflict(row.CommandId);
        var typed = p.Input("or type them: Ctrl+K Ctrl+C", 32, code: true);
        typed.WidthRequest = 260;
        typed.Focused += (_, _) => StopCapture();
        typed.Unfocused += (_, _) =>
        {
            if (_editing == row.CommandId)
            {
                StartCapture();
            }
        };
        var save = p.Primary("Save", 32);
        save.Clicked += (_, _) =>
        {
            string? problem;
            var text = (typed.Text ?? "").Trim();
            if (text.Length > 0)
            {
                problem = KeyBinding.TryParseKeys(text, out var first, out var then, out var error) ? _model.Add(row.CommandId, first, then) : error;
            }
            else
            {
                problem = _model.AddRecorded(row.CommandId);
            }

            if (problem is null)
            {
                StopEditing();
            }

            Report(problem);
        };
        var cancel = p.SecondaryButton("Cancel", 32);
        cancel.Clicked += (_, _) =>
        {
            StopEditing();
            FillRows();
        };
        var typedField = p.Field(typed);
        typedField.WidthRequest = 260;
        typedField.HeightRequest = 34;
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { typedField, save, cancel } };
        var box = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(12, 8), BackgroundColor = p.Raised, Children = { _recorded, _conflict, buttons } };
        return box;
    }

    private void StartEditing(string commandId)
    {
        _editing = commandId;
        _model.Recorder.Clear();
        _searchingByKeys = false;
        StartCapture();
        FillRows();
    }

    private void StopEditing()
    {
        _editing = null;
        _model.Recorder.Clear();
        StopCapture();
    }

    private void StartCapture() => _shell.Host.KeyCapture = OnCapture;

    private void StopCapture()
    {
        if (_shell.Host.KeyCapture == OnCapture)
        {
            _shell.Host.KeyCapture = null;
        }
    }

    // A chord pressed while recording (or while searching by keys): Escape with nothing recorded stops.
    private bool OnCapture(KeyChord chord)
    {
        var recorder = _searchingByKeys ? _searchRecorder : _model.Recorder;
        if (!recorder.Press(chord))
        {
            if (chord.Key == EditorKey.Escape)
            {
                if (_searchingByKeys)
                {
                    ToggleSearchByKeys();
                }
                else
                {
                    StopEditing();
                    FillRows();
                }

                return true;
            }

            return false;
        }

        if (_searchingByKeys)
        {
            _searchKeys.Text = $"Keys: {recorder.Text}  ×";
            FillRows();
        }
        else if (_recorded is not null && _editing is { } editing)
        {
            _recorded.Text = recorder.Text;
            ShowConflict(editing);
        }

        return true;
    }

    private void ShowConflict(string commandId)
    {
        if (_conflict is null)
        {
            return;
        }

        string? conflict = _model.Recorder.First is { } first ? _model.ConflictOf(commandId, first, _model.Recorder.Then) : null;
        _conflict.Text = conflict is null ? "" : $"These keys run {conflict} now; saving gives them to this command.";
        _conflict.IsVisible = conflict is not null;
    }

    private void ToggleSearchByKeys()
    {
        _searchingByKeys = !_searchingByKeys;
        _searchRecorder.Clear();
        _searchKeys.Text = _searchingByKeys ? "Press the keys…  ×" : "Search by keys";
        if (_searchingByKeys)
        {
            _editing = null;
            StartCapture();
        }
        else
        {
            StopCapture();
        }

        FillRows();
    }

    private void Report(string? problem)
    {
        _notice.Text = problem ?? "";
        _notice.IsVisible = problem is not null;
        FillRows();
    }

    private Label Wrapped(string text, Color color)
    {
        var label = _palette.Label(text, color, _palette.SmallSize);
        label.LineBreakMode = LineBreakMode.WordWrap;
        return label;
    }
}
