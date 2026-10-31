// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Shell;
using Idrak.Abstraction.Formats;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Path = System.IO.Path;

namespace Craft.Maui;

/// <summary>The design pages' shared parts: the header row (the logo, "Craft", the page's place, a close button).</summary>
internal static class PageParts
{
    /// <summary>Closes the page's tab (phase U2: the pages are tabs of the editor area).</summary>
    public static Task Close(ShellModel shell, string pageId)
    {
        shell.Pages.Close(pageId);
        return Task.CompletedTask;
    }

    public static Grid Header(ShellPalette palette, string place, Func<Task> close)
    {
        var logo = new GraphicsView { Drawable = new LogoDrawable { Accent = palette.Accent }, WidthRequest = 20, HeightRequest = 20 };
        var closeButton = palette.SecondaryButton("Close");
        closeButton.Clicked += async (_, _) => await close();
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 10,
            Padding = new Thickness(16, 0),
            HeightRequest = 40,
            BackgroundColor = palette.Ground,
        };
        header.Add(logo, 0, 0);
        header.Add(palette.Label("Craft", weight: FontWeight.SemiBold), 1, 0);
        header.Add(palette.Label(place, palette.Secondary), 2, 0);
        header.Add(closeButton, 3, 0);
        return header;
    }

    public static Grid Pair(ShellPalette palette, string name, string value, bool mono = false)
    {
        var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(1, GridUnitType.Star))], ColumnSpacing = 12 };
        grid.Add(palette.Label(name, palette.Secondary), 0, 0);
        var text = palette.Label(value);
        text.LineBreakMode = LineBreakMode.WordWrap;
        if (mono)
        {
            text.FontFamily = palette.Options.FontFamily;
            text.FontSize = palette.SmallSize;
        }

        grid.Add(text, 1, 0);
        return grid;
    }

    public static View Segmented(ShellPalette palette, IReadOnlyList<(string Text, bool Active, bool Enabled, Action Chosen)> choices)
    {
        var row = new HorizontalStackLayout { Spacing = 4 };
        foreach (var (text, active, enabled, chosen) in choices)
        {
            var button = palette.Flat(text, active ? palette.Text : palette.Secondary, 28);
            button.BackgroundColor = active ? palette.Selection : Colors.Transparent;
            button.IsEnabled = enabled;
            button.Padding = new Thickness(12, 0);
            button.Clicked += (_, _) => chosen();
            row.Children.Add(button);
        }

        return palette.Box(row, 8, new Thickness(3), palette.Ground);
    }
}

/// <summary>
/// The Plug-ins page (phase D, docs/design/Plugins.dc.html) over K's plug-in list and Idrak's plug-ins
/// (<see cref="PluginList"/>): the filters All / Overrides / Problems with their counts, a filter by name or registry,
/// "Load from folder…" (a user plug-in folder, <c>CRAFT_PLUGIN_FOLDERS</c>, taken at the next start), the table (plug-in,
/// registry, source, state), and the chosen plug-in's details with its problem and "disable at the next start".
/// </summary>
public sealed class PluginsPage : ContentView
{
    private readonly ShellModel _shell;
    private readonly Func<IReadOnlyList<PluginRow>> _rows;
    private readonly ShellPalette _palette;
    private readonly VerticalStackLayout _table = new() { Spacing = 0 };
    private readonly VerticalStackLayout _detail = new() { Spacing = 18, Padding = new Thickness(20) };
    private readonly ContentView _filters = new();
    private readonly Entry _filterText;
    private readonly TypingDebounce _filterDebounce;
    private readonly Label _notice;
    private IReadOnlyList<PluginRow> _all = [];
    private PluginFilter _filter;
    private PluginRow? _chosen;

    /// <summary>The page of <paramref name="rows"/> (made again on Refresh), in <paramref name="shell"/>'s theme.</summary>
    public PluginsPage(ShellModel shell, Func<IReadOnlyList<PluginRow>> rows)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(rows);
        _shell = shell;
        _rows = rows;
        _palette = new ShellPalette(shell.Theme, shell.Options);
        var p = _palette;
        BackgroundColor = p.Ground;
        _filterText = p.Input("Filter by name or registry", 32);
        var filterBox = p.Field(_filterText);
        filterBox.WidthRequest = 320;
        _filterDebounce = new TypingDebounce(this, () => _shell.Options.SearchDelayMilliseconds, Rebuild);
        _filterText.TextChanged += (_, _) => _filterDebounce.Restart();
        _filterText.Completed += (_, _) => _filterDebounce.Flush();
        var load = p.SecondaryButton("Load from folder…", 32);
        load.Clicked += async (_, _) => await LoadFolderAsync();
        _notice = p.Label("", p.Secondary, p.SmallSize);
        var folders = p.Label(string.Join("; ", [Path.GetFileName(Path.TrimEndingDirectorySeparator(Craft.Core.Plugins.CraftPlugins.InstalledFolder)) + Path.DirectorySeparatorChar, .. shell.Options.PluginFolders]), p.Secondary, p.SmallSize);
        folders.FontFamily = shell.Options.FontFamily;
        var tools = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            Padding = new Thickness(22, 18),
            Children = { _filters, filterBox, load, folders },
        };
        foreach (var child in tools.Children.OfType<View>())
        {
            child.Margin = new Thickness(0, 0, 10, 4);
        }

        var main = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)], BackgroundColor = p.Surface };
        main.Add(tools, 0, 0);
        main.Add(p.Line(), 0, 1);
        main.Add(new ContentView { Content = _notice, Padding = new Thickness(22, 4) }, 0, 2);
        main.Add(new ScrollView { Content = _table, Padding = new Thickness(22, 0) }, 0, 3);
        var side = new ScrollView { Content = _detail, BackgroundColor = p.Surface, WidthRequest = 380 };
        var body = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(1)), new ColumnDefinition(GridLength.Auto)] };
        body.Add(main, 0, 0);
        body.Add(p.Line(vertical: true), 1, 0);
        body.Add(side, 2, 0);
        var page = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)] };
        page.Add(PageParts.Header(p, "Plug-ins", () => PageParts.Close(shell, ShellPageIds.Plugins)), 0, 0);
        page.Add(p.Line(), 0, 1);
        page.Add(body, 0, 2);
        Content = page;
        SizeChanged += (_, _) => side.IsVisible = Width <= 0 || Width >= shell.Options.NarrowWidth;
        Refresh();
    }

    /// <summary>Reads the rows again and redraws.</summary>
    public void Refresh()
    {
        _all = _rows();
        _chosen = _chosen is null ? _all.FirstOrDefault(r => r.State == PluginState.Problem) ?? (_all.Count > 0 ? _all[0] : null) : _all.FirstOrDefault(r => r.Name == _chosen.Name);
        Rebuild();
    }

    private void Rebuild()
    {
        var p = _palette;
        var (all, overrides, problems) = PluginList.Counts(_all);
        _filters.Content = PageParts.Segmented(p,
        [
            (string.Create(CultureInfo.InvariantCulture, $"All {all}"), _filter == PluginFilter.All, true, () => Choose(PluginFilter.All)),
            (string.Create(CultureInfo.InvariantCulture, $"Overrides {overrides}"), _filter == PluginFilter.Overrides, true, () => Choose(PluginFilter.Overrides)),
            (string.Create(CultureInfo.InvariantCulture, $"Problems {problems}"), _filter == PluginFilter.Problems, true, () => Choose(PluginFilter.Problems)),
        ]);
        _table.Children.Clear();
        var header = Row(p.Label("Plug-in", p.Secondary, p.SmallSize, FontWeight.Medium), p.Label("Registry", p.Secondary, p.SmallSize, FontWeight.Medium),
            p.Label("Source", p.Secondary, p.SmallSize, FontWeight.Medium), p.Label("State", p.Secondary, p.SmallSize, FontWeight.Medium));
        header.Padding = new Thickness(0, 12, 0, 8);
        _table.Children.Add(header);
        var rows = PluginList.Filter(_all, _filter, _filterText.Text);
        if (rows.Count == 0)
        {
            _table.Children.Add(p.Line());
            var none = p.Label(_all.Count == 0 ? "No plug-in is installed." : "No plug-in matches.", p.Secondary);
            none.Margin = new Thickness(0, 11);
            _table.Children.Add(none);
        }

        foreach (var row in rows)
        {
            _table.Children.Add(p.Line());
            var name = new Label { FontFamily = p.Font(), FontSize = p.FontSize, TextColor = p.Text, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
            name.FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = row.Name, FontFamily = p.Font(FontWeight.SemiBold) },
                    new Span { Text = row.Summary.Length == 0 ? "" : "  " + row.Summary, TextColor = p.Secondary },
                },
            };
            var registry = p.Label(row.Registries.Length == 0 ? "—" : row.Registries, p.Secondary, p.SmallSize);
            registry.FontFamily = p.Options.FontFamily;
            var cells = Row(name, registry, p.Label(row.Source, p.Secondary), p.Chip(row.StateText, row.State));
            cells.Padding = new Thickness(row == _chosen ? 8 : 0, 11, 0, 11);
            bool chosen = row == _chosen;
            var line = new Grid { BackgroundColor = chosen ? p.RaisedHigh : Colors.Transparent };
            line.Add(cells);
            if (chosen)
            {
                line.Add(new BoxView { Color = p.Accent, WidthRequest = 2, HorizontalOptions = LayoutOptions.Start });
            }

            var tap = new TapGestureRecognizer();
            var picked = row;
            tap.Tapped += (_, _) =>
            {
                _chosen = picked;
                Rebuild();
            };
            line.GestureRecognizers.Add(tap);
            _table.Children.Add(line);
        }

        _table.Children.Add(p.Line());
        ShowDetail();
    }

    private static Grid Row(View name, View registry, View source, View state)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(2, GridUnitType.Star))],
            ColumnSpacing = 8,
        };
        grid.Add(name, 0, 0);
        grid.Add(registry, 1, 0);
        grid.Add(source, 2, 0);
        state.HorizontalOptions = LayoutOptions.Start;
        grid.Add(state, 3, 0);
        return grid;
    }

    private void Choose(PluginFilter filter)
    {
        _filter = filter;
        Rebuild();
    }

    private void ShowDetail()
    {
        var p = _palette;
        _detail.Children.Clear();
        if (_chosen is not { } row)
        {
            _detail.Children.Add(p.Label("Choose a plug-in to see its details.", p.Secondary));
            return;
        }

        var title = p.Label(row.Name, size: p.FontSize + 3, weight: FontWeight.SemiBold);
        var summary = p.Label(row.Summary, p.Secondary);
        summary.LineBreakMode = LineBreakMode.WordWrap;
        _detail.Children.Add(new VerticalStackLayout { Spacing = 6, Children = { title, summary } });
        var facts = new VerticalStackLayout { Spacing = 10 };
        facts.Children.Add(PageParts.Pair(p, "Registry", row.Registries.Length == 0 ? "—" : row.Registries, mono: true));
        facts.Children.Add(PageParts.Pair(p, "Source", row.Source));
        facts.Children.Add(PageParts.Pair(p, "Version", row.Version ?? "—"));
        facts.Children.Add(PageParts.Pair(p, "Asks for", row.Capabilities.Count == 0 ? "nothing" : string.Join(", ", row.Capabilities)));
        if (row.Folder is { } folder)
        {
            facts.Children.Add(PageParts.Pair(p, "Folder", folder, mono: true));
        }

        _detail.Children.Add(facts);
        uint hue = row.State switch
        {
            PluginState.Active => p.Theme.Colors.Success,
            PluginState.Problem => p.Theme.Colors.Error,
            PluginState.Outdated => p.Theme.Colors.Info,
            _ => p.Theme.Colors.SecondaryText,
        };
        var stateText = p.Label(row.StateText, weight: FontWeight.SemiBold);
        var stateBox = p.Box(new VerticalStackLayout { Spacing = 6, Children = { stateText } }, 8, new Thickness(12),
            ThemeColors.Of(ColorContrast.Mix(p.Theme.Colors.Surface, hue, 0.12)), ThemeColors.Of(ColorContrast.Mix(p.Theme.Colors.Surface, hue, 0.45)));
        if (row.Problem is { } problem)
        {
            var reason = p.Label(problem, ThemeColors.Of(ColorContrast.Ensure(hue, ColorContrast.Text, [ColorContrast.Mix(p.Theme.Colors.Surface, hue, 0.12)])), p.SmallSize);
            reason.LineBreakMode = LineBreakMode.WordWrap;
            ((VerticalStackLayout)stateBox.Content!).Children.Add(reason);
        }

        _detail.Children.Add(stateBox);
        if (row.Source != "Idrak")
        {
            bool disabled = _shell.Options.DisabledPlugins.Contains(row.Name, StringComparer.OrdinalIgnoreCase);
            var toggle = p.SecondaryButton(disabled ? "Enable at the next start" : "Disable at the next start", 34);
            toggle.Clicked += (_, _) => ToggleDisabled(row.Name, disabled);
            var refresh = p.SecondaryButton("Refresh", 34);
            refresh.Clicked += (_, _) => Refresh();
            _detail.Children.Add(new VerticalStackLayout { Spacing = 8, Children = { toggle, refresh } });
        }
    }

    private void ToggleDisabled(string name, bool disabled)
    {
        var list = _shell.Options.DisabledPlugins.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!disabled)
        {
            list.Add(name);
        }

        string? error = _shell.ApplySetting(SettingLayer.User, "CRAFT_PLUGINS_DISABLED", list.Count == 0 ? null : string.Join(",", list));
        _notice.Text = error ?? $"{name} is {(disabled ? "enabled" : "disabled")} from the next start (CRAFT_PLUGINS_DISABLED in your settings.json).";
        _notice.TextColor = error is null ? _palette.Secondary : _palette.Error;
        ShowDetail();
    }

    private async Task LoadFolderAsync()
    {
        if (await _shell.FileDialogs.PickFolderAsync() is not { } folder)
        {
            return;
        }

        var folders = _shell.Options.PluginFolders.ToList();
        if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(folder);
        }

        string? error = _shell.ApplySetting(SettingLayer.User, "CRAFT_PLUGIN_FOLDERS", string.Join(";", folders));
        _notice.Text = error ?? $"{folder}: its plug-ins load at the next start (CRAFT_PLUGIN_FOLDERS in your settings.json). A plug-in is code: only folders you trust.";
        _notice.TextColor = error is null ? _palette.Secondary : _palette.Error;
    }
}

/// <summary>
/// The Models and devices page (phase D, docs/design/Models.dc.html), the surface a model provider fills through
/// <see cref="IModelProvider.Status"/> (A1's "local"): where models run (local now, remote later), the models by role,
/// "Add a model" (the provider's model setting, then its load command, with the host's progress), how a name is resolved
/// (Idrak's <see cref="ModelSources"/>, in order), the device and what it measured, and the limits (the provider's
/// settings, Idrak's ComputeResources). With no provider it says so, and every section stays empty rather than guessed.
/// </summary>
public sealed class ModelsPage : ContentView, IDisposable
{
    // The settings page's sections (the owner's note 13); this page stands in for "Models and AI" beside them.
    private static readonly (string Id, string Title)[] Sections = [.. SettingSections.All.Select(s => s.Id == SettingSections.Models ? (s.Id, "Models and devices") : s)];
    private readonly ShellModel _shell;
    private readonly ShellPalette _palette;
    private readonly VerticalStackLayout _main = new() { Spacing = 24, Padding = new Thickness(32, 24) };
    private readonly VerticalStackLayout _side = new() { Spacing = 16, Padding = new Thickness(24) };
    private readonly Label _notice;
    private IModelProvider? _provider;

    /// <summary>The page in <paramref name="shell"/>'s theme, a tab of the editor area; a section other than this one opens the settings tab.</summary>
    public ModelsPage(ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _palette = new ShellPalette(shell.Theme, shell.Options);
        var p = _palette;
        BackgroundColor = p.Ground;
        _notice = p.Label("", p.Secondary, p.SmallSize);
        _notice.LineBreakMode = LineBreakMode.WordWrap;
        var nav = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(10, 16) };
        foreach (var (section, sectionTitle) in Sections)
        {
            bool here = section == SettingSections.Models;
            var link = p.Flat(sectionTitle, here ? p.Text : p.Secondary, 34);
            link.BackgroundColor = here ? p.Selection : Colors.Transparent;
            link.HorizontalOptions = LayoutOptions.Fill;
            if (!here)
            {
                link.Clicked += async (_, _) => await OpenSectionAsync(section);
            }

            nav.Children.Add(link);
        }

        var body = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(210)), new ColumnDefinition(new GridLength(1)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(1)), new ColumnDefinition(new GridLength(340))],
            BackgroundColor = p.Surface,
        };
        body.Add(nav, 0, 0);
        body.Add(p.Line(vertical: true), 1, 0);
        body.Add(new ScrollView { Content = _main }, 2, 0);
        body.Add(p.Line(vertical: true), 3, 0);
        body.Add(new ScrollView { Content = _side }, 4, 0);
        SizeChanged += (_, _) =>
        {
            bool narrow = Width > 0 && Width < shell.Options.NarrowWidth;
            body.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(210);
            body.ColumnDefinitions[4].Width = narrow ? new GridLength(0) : new GridLength(340);
        };
        var page = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)] };
        page.Add(PageParts.Header(p, "Settings › Models and devices", () => PageParts.Close(shell, ShellPageIds.Models)), 0, 0);
        page.Add(p.Line(), 0, 1);
        page.Add(body, 0, 2);
        Content = page;
        _shell.Host.ProgressChanged += OnProgress;
        Watch();
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_provider is not null)
        {
            _provider.StatusChanged -= OnStatusChanged;
            _provider = null;
        }

        _shell.Host.ProgressChanged -= OnProgress;
    }

    // The provider whose status the page shows (the first one; a plug-in that registered later is taken on the next refresh).
    private void Watch()
    {
        if (ShellText.ModelStatus() is { } found && !ReferenceEquals(found.Provider, _provider))
        {
            if (_provider is not null)
            {
                _provider.StatusChanged -= OnStatusChanged;
            }

            _provider = found.Provider;
            _provider.StatusChanged += OnStatusChanged;
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.DispatchLabelled("models page: refresh", Refresh);

    private void OnProgress(object? sender, EditorProgressItem e) => Dispatcher.DispatchLabelled("models page: progress", Refresh);

    private Task OpenSectionAsync(string section)
    {
        _shell.ShowSettings(section);
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        var p = _palette;
        Watch();
        var found = ShellText.ModelStatus();
        var status = found?.Status;
        _main.Children.Clear();
        _side.Children.Clear();

        _main.Children.Add(Section("Where models run", PageParts.Segmented(p,
        [
            (found is null ? "Local (no provider installed)" : "Local (this machine)", true, true, () => { }),
            ("Remote endpoint · later (A3)", false, false, () => { }),
        ])));
        if (found is null)
        {
            var none = p.Label("No model provider is installed: the AI plug-in (Craft.Ai) brings \"local\". The Plug-ins page shows whether it loaded.", p.Secondary);
            none.LineBreakMode = LineBreakMode.WordWrap;
            _main.Children.Add(none);
        }

        var roles = new FlexLayout { Wrap = FlexWrap.Wrap };
        foreach (var role in status?.Roles ?? [])
        {
            var card = p.Box(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    p.Label(role.Role, p.Secondary, p.SmallSize),
                    Mono(role.Model ?? "none set", p.Text),
                    Wrapped(role.State, role.IsProblem ? p.Error : p.Secondary, p.SmallSize),
                },
            }, 10, new Thickness(14));
            card.WidthRequest = 220;
            card.Margin = new Thickness(0, 0, 12, 12);
            roles.Children.Add(card);
        }

        if (status is { Roles.Count: > 0 })
        {
            _main.Children.Add(Section("Models by role", roles));
        }

        if (status?.ModelSetting is { } modelSetting)
        {
            var name = p.Input("owner/coder-model", 36, code: true);
            name.Text = _shell.Settings.Read(modelSetting.Variable) ?? "";
            name.HorizontalOptions = LayoutOptions.Fill;
            var use = p.Primary(status.LoadCommand is null ? "Use" : "Use and load", 36);
            use.Clicked += async (_, _) => await UseModelAsync(modelSetting, name.Text, status.LoadCommand);
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            row.Add(p.Field(name), 0, 0);
            row.Add(use, 1, 0);
            var add = new VerticalStackLayout { Spacing = 6, Children = { p.Label(modelSetting.Label, p.Secondary, p.SmallSize), row } };
            foreach (var progress in _shell.Host.Progress)
            {
                add.Children.Add(ProgressRow(progress));
            }

            add.Children.Add(_notice);
            _main.Children.Add(Section("Add a model", add));
        }

        var sources = new VerticalStackLayout { Spacing = 0 };
        bool first = true;
        foreach (var source in ModelSources.Names)
        {
            if (!first)
            {
                sources.Children.Add(p.Line());
            }

            first = false;
            var item = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(140)), new ColumnDefinition(GridLength.Star)], Padding = new Thickness(14, 10), ColumnSpacing = 12 };
            item.Add(Mono(source, p.Text), 0, 0);
            item.Add(Wrapped(ModelSources.Origin(source) is { } origin ? $"registered by {origin}" : "the library's", p.Secondary, p.FontSize), 1, 0);
            sources.Children.Add(item);
        }

        var resolved = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Wrapped("Idrak's ModelSources, asked in order. The same names work in the idrak tool.", p.Secondary, p.FontSize),
                p.Box(sources, 10, new Thickness(0)),
            },
        };
        _main.Children.Add(Section("How a name is resolved", resolved));
        if (status is { Loaded.Count: > 0 })
        {
            // What the right-hand "Models and devices" panel listed before phase U2.
            var loaded = new VerticalStackLayout { Spacing = 6 };
            foreach (var loadedName in status.Loaded)
            {
                loaded.Children.Add(Mono(loadedName, p.Text));
            }

            _main.Children.Add(Section("Loaded now", loaded));
        }

        // The side: the device and what it measured, then the limits.
        _side.Children.Add(Heading("Device"));
        if (status?.DeviceSetting is { } deviceSetting)
        {
            _side.Children.Add(SettingField(deviceSetting));
        }

        var card2 = new VerticalStackLayout { Spacing = 8 };
        card2.Children.Add(PageParts.Pair(p, "In use", status?.Device ?? "nothing loaded"));
        card2.Children.Add(PageParts.Pair(p, "Device memory used", status?.DeviceMemoryBytes is { } memory ? ShellText.Bytes(memory) : "not measured"));
        var semantic = _shell.Tabs.Active?.View is EditorView { Model: { } model } ? ShellText.Semantic(model) : null;
        card2.Children.Add(PageParts.Pair(p, "Roslyn workspace", semantic is { IsReady: true } ? semantic.Memory : semantic?.State ?? "none"));
        card2.Children.Add(PageParts.Pair(p, "Completion, last", status?.FirstToken is { } firstToken
            ? string.Create(CultureInfo.InvariantCulture, $"{firstToken.TotalMilliseconds:0} ms first token") : "none yet"));
        _side.Children.Add(p.Box(card2, 10, new Thickness(14)));
        _side.Children.Add(Heading("Limits"));
        _side.Children.Add(Wrapped(status is { Limits.Count: > 0 } ? "None set: Craft uses what the device has. A limit here sets Idrak's ComputeResources." : "The provider has no limits to set.", p.Secondary, p.FontSize));
        foreach (var limit in status?.Limits ?? [])
        {
            _side.Children.Add(SettingField(limit));
        }
    }

    private VerticalStackLayout Section(string title, View content) => new VerticalStackLayout { Spacing = 12, Children = { Heading(title), content } };

    private Label Heading(string text) => _palette.Label(text, _palette.Text, _palette.FontSize + 2, FontWeight.SemiBold);

    private Label Mono(string text, Color color)
    {
        var label = _palette.Label(text, color);
        label.FontFamily = _palette.Options.FontFamily;
        return label;
    }

    private Label Wrapped(string text, Color color, double size)
    {
        var label = _palette.Label(text, color, size);
        label.LineBreakMode = LineBreakMode.WordWrap;
        return label;
    }

    private VerticalStackLayout ProgressRow(EditorProgressItem progress)
    {
        var p = _palette;
        var line = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
        line.Add(p.Label(progress.Title, size: p.SmallSize), 0, 0);
        line.Add(p.Label(progress.Message ?? "", p.Secondary, p.SmallSize), 1, 0);
        var track = new Grid { HeightRequest = 6, BackgroundColor = p.Selection };
        if (progress.Fraction is { } fraction)
        {
            track.ColumnDefinitions = [new ColumnDefinition(new GridLength(Math.Clamp(fraction, 0, 1), GridUnitType.Star)), new ColumnDefinition(new GridLength(Math.Clamp(1 - fraction, 0, 1), GridUnitType.Star))];
            track.Add(new BoxView { Color = p.Accent, HeightRequest = 6 }, 0, 0);
        }

        return new VerticalStackLayout { Spacing = 6, Children = { line, track } };
    }

    private VerticalStackLayout SettingField(ModelSettingField field)
    {
        var p = _palette;
        var input = p.Input(field.Placeholder ?? "", 36);
        input.Text = _shell.Settings.Read(field.Variable) ?? "";
        var error = p.Label("", p.Error, p.SmallSize);
        error.IsVisible = false;
        input.Completed += (_, _) =>
        {
            string? problem = _shell.ApplySetting(SettingLayer.User, field.Variable, string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim());
            error.Text = problem ?? "";
            error.IsVisible = problem is not null;
        };
        var label = p.Label(field.Label, p.Secondary, p.SmallSize);
        ToolTipProperties.SetText(label, field.Variable);
        return new VerticalStackLayout { Spacing = 6, Children = { label, p.Field(input), error } };
    }

    private async Task UseModelAsync(ModelSettingField setting, string? name, string? loadCommand)
    {
        string? problem = _shell.ApplySetting(SettingLayer.User, setting.Variable, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
        _notice.Text = problem ?? (string.IsNullOrWhiteSpace(name) ? "The model is cleared." : $"{name!.Trim()} is the model ({setting.Variable}).");
        _notice.TextColor = problem is null ? _palette.Secondary : _palette.Error;
        if (problem is null && loadCommand is not null && !string.IsNullOrWhiteSpace(name))
        {
            await _shell.Host.ExecuteCommandAsync(loadCommand);
        }
    }
}
