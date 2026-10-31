// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Maui;

namespace Craft.App;

/// <summary>
/// The minimal window of phase E3: Open, Save and the theme on top, the find and replace bar (Ctrl+F, Ctrl+H), the editor,
/// and a status bar (language, line and column, encoding, line ending, dirty marker, input-to-paint time). The bars wrap
/// when the window is narrow (their layout follows the width). Code only, no XAML.
/// </summary>
public sealed class EditorPage : ContentPage
{
    private readonly AppStartup _startup;
    private readonly string? _initialPath;
    private readonly EditorView _editor = new();
    private readonly Button _save;
    private readonly Button _themeButton;
    private readonly FlexLayout _findBar;
    private readonly Entry _query;
    private readonly Entry _replacement;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWord;
    private readonly Label _matchCount;
    private readonly HorizontalStackLayout _replaceRow;
    private readonly Label _language = StatusLabel();
    private readonly Label _position = StatusLabel();
    private readonly Label _encoding = StatusLabel();
    private readonly Label _lineEnding = StatusLabel();
    private readonly Label _dirty = StatusLabel();
    private readonly Label _paint = StatusLabel();
    private readonly Label _notice = StatusLabel();
    private readonly View _topBar;
    private readonly View _statusBar;
    private Window? _window;
    private TextDocument? _document;
    private ITheme _theme;
    private bool _started;

    /// <summary>The page, opening <paramref name="path"/> when given (else an empty untitled C# document).</summary>
    public EditorPage(AppStartup startup, string? path)
    {
        ArgumentNullException.ThrowIfNull(startup);
        _startup = startup;
        _initialPath = path;
        _theme = EditorThemes.Find(startup.Options.Theme) ?? EditorThemes.Get(EditorThemes.Dark);

        var open = new Button { Text = "Open" };
        open.Clicked += OnOpenClicked;
        _save = new Button { Text = "Save", IsEnabled = false };
        _save.Clicked += OnSaveClicked;
        _themeButton = new Button { Text = ThemeButtonText() };
        _themeButton.Clicked += OnThemeClicked;
        var find = new Button { Text = "Find" };
        find.Clicked += (_, _) => ShowFind(replace: false, null);
        _topBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(6, 4),
            Children = { open, _save, _themeButton, find },
        };

        _query = new Entry { Placeholder = "Find", WidthRequest = 220 };
        _query.TextChanged += OnQueryChanged;
        _query.Completed += (_, _) => FindNext();
        _replacement = new Entry { Placeholder = "Replace", WidthRequest = 220 };
        _matchCase = new CheckBox();
        _matchCase.CheckedChanged += OnFindOptionChanged;
        _wholeWord = new CheckBox();
        _wholeWord.CheckedChanged += OnFindOptionChanged;
        _matchCount = new Label { VerticalTextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0) };
        var previous = new Button { Text = "Previous" };
        previous.Clicked += (_, _) => FindPrevious();
        var next = new Button { Text = "Next" };
        next.Clicked += (_, _) => FindNext();
        var replaceOne = new Button { Text = "Replace" };
        replaceOne.Clicked += OnReplaceClicked;
        var replaceAll = new Button { Text = "Replace all" };
        replaceAll.Clicked += OnReplaceAllClicked;
        var close = new Button { Text = "Close" };
        close.Clicked += OnCloseFindClicked;
        _replaceRow = new HorizontalStackLayout { Spacing = 4, Children = { _replacement, replaceOne, replaceAll } };
        _findBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(6, 2),
            IsVisible = false,
            Children =
            {
                _query,
                new HorizontalStackLayout { Children = { _matchCase, new Label { Text = "Match case", VerticalTextAlignment = TextAlignment.Center } } },
                new HorizontalStackLayout { Children = { _wholeWord, new Label { Text = "Whole word", VerticalTextAlignment = TextAlignment.Center } } },
                previous,
                next,
                _matchCount,
                _replaceRow,
                close,
            },
        };

        _statusBar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Padding = new Thickness(8, 2),
            Children = { _language, _position, _encoding, _lineEnding, _dirty, _paint, _notice },
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(_topBar, 0, 0);
        grid.Add(_findBar, 0, 1);
        grid.Add(_editor, 0, 2);
        grid.Add(_statusBar, 0, 3);
        Content = grid;

        _editor.Options = startup.Options;
        _editor.Theme = _theme;
        _editor.SaveRequested += OnSaveRequested;
        _editor.FindRequested += OnFindRequested;
        _editor.CaretMoved += (_, _) => UpdateStatus();
        _editor.Painted += (_, _) => UpdatePaintTime();
        ApplyTheme();
    }

    /// <summary>The window the page is in (its title shows the file and the dirty marker).</summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        UpdateTitle();
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        if (_initialPath is not null)
        {
            await OpenAsync(_initialPath);
        }
        else
        {
            Show(TextDocument.Create("", _startup.Options), EditorLanguages.Find("csharp"));
        }

        if (_startup.OptionsError is { } error)
        {
            await DisplayAlertAsync("Craft: a setting did not parse", error + " The defaults are used.", "OK");
        }

        foreach (var plugin in _startup.Plugins)
        {
            if (!plugin.Loaded && plugin.Error is not null)
            {
                _notice.Text = $"Plug-in {plugin.Name} did not load";
                await DisplayAlertAsync("Craft: a plug-in did not load", $"{plugin.Name} ({plugin.Folder}): {plugin.Error}", "OK");
            }
        }
    }

    private static Label StatusLabel() => new() { Margin = new Thickness(0, 0, 16, 0), FontSize = 12, VerticalTextAlignment = TextAlignment.Center };

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open a file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = [".cs", ".csx", ".txt", "*"],
                }),
            });
            if (result is not null)
            {
                await OpenAsync(result.FullPath);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await DisplayAlertAsync("Craft: could not open", error.Message, "OK");
        }
    }

    private async Task OpenAsync(string path)
    {
        TextDocument document;
        try
        {
            document = await TextDocument.OpenAsync(path, _startup.Options);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await DisplayAlertAsync("Craft: could not open", $"{path}: {error.Message}", "OK");
            if (_document is null)
            {
                Show(TextDocument.Create("", _startup.Options), EditorLanguages.Find("csharp"));
            }

            return;
        }

        Show(document, EditorLanguages.ForFile(path));
    }

    private void Show(TextDocument document, ILanguage? language)
    {
        var old = _document;
        if (old is not null)
        {
            old.StateChanged -= OnDocumentStateChanged;
        }

        _document = document;
        document.StateChanged += OnDocumentStateChanged;
        _editor.Open(document, language);
        old?.Dispose();
        if (_findBar.IsVisible && _editor.Model is { } model)
        {
            ApplyFind(model);
        }

        UpdateStatus();
        UpdateTitle();
    }

    private void OnSaveClicked(object? sender, EventArgs e) => _ = SaveAsync();

    private void OnSaveRequested(object? sender, EventArgs e) => _ = SaveAsync();

    private async Task SaveAsync()
    {
        if (_document is not { } document)
        {
            return;
        }

        if (document.FilePath is null)
        {
            await DisplayAlertAsync("Craft: not saved", "This document has no file yet. Open a file to edit and save it (Save As comes with the shell, phase E4).", "OK");
            return;
        }

        try
        {
            await document.SaveAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await DisplayAlertAsync("Craft: could not save", $"{document.FilePath}: {error.Message}", "OK");
        }

        UpdateStatus();
        UpdateTitle();
    }

    private void OnThemeClicked(object? sender, EventArgs e)
    {
        _theme = EditorThemes.Get(_theme.IsDark ? EditorThemes.Light : EditorThemes.Dark);
        _editor.Theme = _theme;
        _themeButton.Text = ThemeButtonText();
        ApplyTheme();
    }

    private string ThemeButtonText() => _theme.IsDark ? "Light theme" : "Dark theme";

    private void ApplyTheme()
    {
        if (Application.Current is { } application)
        {
            application.UserAppTheme = _theme.IsDark ? AppTheme.Dark : AppTheme.Light;
        }

        var colors = _theme.Colors;
        var background = ColorOf(colors.Background);
        var foreground = ColorOf(colors.Foreground);
        BackgroundColor = background;
        _topBar.BackgroundColor = ColorOf(colors.Gutter);
        _findBar.BackgroundColor = ColorOf(colors.CurrentLine);
        _statusBar.BackgroundColor = ColorOf(colors.Gutter);
        foreach (var label in (ReadOnlySpan<Label>)[_language, _position, _encoding, _lineEnding, _dirty, _paint, _notice, _matchCount])
        {
            label.TextColor = foreground;
        }
    }

    private static Color ColorOf(uint argb) => Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    private void OnFindRequested(object? sender, FindRequestedEventArgs e) => ShowFind(e.Replace, e.InitialQuery);

    private void ShowFind(bool replace, string? query)
    {
        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace;
        if (query is not null)
        {
            _query.Text = query;
        }

        if (_editor.Model is { } model)
        {
            ApplyFind(model);
        }

        _query.Focus();
    }

    private void ApplyFind(EditorModel model)
    {
        model.Find.MatchCase = _matchCase.IsChecked;
        model.Find.WholeWord = _wholeWord.IsChecked;
        model.Find.Query = _query.Text ?? "";
        UpdateCount();
    }

    private void OnQueryChanged(object? sender, Microsoft.Maui.Controls.TextChangedEventArgs e)
    {
        if (_editor.Model is { } model)
        {
            ApplyFind(model);
        }
    }

    private void OnFindOptionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_editor.Model is { } model)
        {
            ApplyFind(model);
        }
    }

    private void FindNext()
    {
        _editor.Model?.Find.FindNext();
        UpdateCount();
    }

    private void FindPrevious()
    {
        _editor.Model?.Find.FindPrevious();
        UpdateCount();
    }

    private void OnReplaceClicked(object? sender, EventArgs e)
    {
        _editor.Model?.Find.ReplaceOne(_replacement.Text ?? "");
        UpdateCount();
    }

    private void OnReplaceAllClicked(object? sender, EventArgs e)
    {
        if (_editor.Model is { } model)
        {
            int replaced = model.Find.ReplaceAll(_replacement.Text ?? "");
            _notice.Text = string.Create(CultureInfo.InvariantCulture, $"Replaced {replaced}");
        }

        UpdateCount();
    }

    private void OnCloseFindClicked(object? sender, EventArgs e)
    {
        _findBar.IsVisible = false;
        if (_editor.Model is { } model)
        {
            model.Find.IsHighlighting = false;
        }

        _editor.Invalidate();
    }

    private void UpdateCount()
    {
        if (_editor.Model is not { } model || (_query.Text ?? "").Length == 0)
        {
            _matchCount.Text = "";
            return;
        }

        var count = model.Find.CountMatches();
        string wrapped = model.Find.LastWrapped ? ", wrapped" : "";
        _matchCount.Text = string.Create(CultureInfo.InvariantCulture, $"{(count.Capped ? "at least " : "")}{count.Count} {(count.Count == 1 ? "match" : "matches")}{wrapped}");
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        UpdateStatus();
        UpdateTitle();
    }

    private void UpdateStatus()
    {
        if (_editor.Model is not { } model || _document is not { } document)
        {
            return;
        }

        _language.Text = LanguageText(model, document);
        _position.Text = string.Create(CultureInfo.InvariantCulture, $"Ln {model.CaretLine + 1}, Col {model.CaretColumn + 1}");
        _encoding.Text = document.Encoding.WebName.ToUpperInvariant() + (document.HasBom ? " with BOM" : "");
        _lineEnding.Text = document.LineEnding switch { "\r\n" => "CRLF", "\n" => "LF", "\r" => "CR", var other => other };
        _dirty.Text = document.IsDirty ? "• modified" : "saved";
        _save.IsEnabled = document.FilePath is not null;
    }

    private static string LanguageText(EditorModel model, TextDocument document)
    {
        if (model.Language is not null || document.FilePath is null)
        {
            return model.Language is null ? "Plain text — the C# plug-in is not installed or is disabled" : model.LanguageName;
        }

        var extension = Path.GetExtension(document.FilePath);
        bool csharp = extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csx", StringComparison.OrdinalIgnoreCase);
        return csharp ? model.LanguageName + " (the C# plug-in is not installed or is disabled)" : model.LanguageName;
    }

    private void UpdatePaintTime()
    {
        if (_editor.Model is { } model)
        {
            _paint.Text = string.Create(CultureInfo.InvariantCulture, $"input to paint {model.LastInputToPaintMilliseconds:F1} ms (max {model.MaxInputToPaintMilliseconds:F1})");
        }
    }

    private void UpdateTitle()
    {
        if (_window is null)
        {
            return;
        }

        string name = _document?.FilePath is { } path ? Path.GetFileName(path) : "untitled";
        _window.Title = $"Craft — {name}{(_document?.IsDirty == true ? " •" : "")}";
    }
}
