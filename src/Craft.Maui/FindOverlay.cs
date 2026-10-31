// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Find and replace as an overlay in the editor's top-right corner (phase X: inline, not modal; Ctrl+F, Ctrl+H), over
/// <see cref="EditorFind"/>: the query with the match case, whole word and regular expression toggles (Alt+C, Alt+W,
/// Alt+R), "3 of 17", previous and next (Shift+Enter and Enter, Shift+F3 and F3), and the replace row (Ctrl+H, the chevron)
/// with Replace (Enter in its box) and Replace all (Ctrl+Alt+Enter), one undo step. Every match is drawn in the editor
/// (its find layer). Typing selects the first match from the caret; a large document is searched off the UI thread by
/// snapshot version. Escape closes it, the highlights go and the editor takes the keyboard back.
/// </summary>
public sealed class FindOverlay : ContentView
{
    private readonly RoutedEntry _query;
    private readonly RoutedEntry _replacement;
    private readonly CraftInput _queryBox;
    private readonly CraftInput _replacementBox;
    private readonly Button _expand;
    private readonly Button _matchCase;
    private readonly Button _wholeWord;
    private readonly Button _regex;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _close;
    private readonly Button _replaceOne;
    private readonly Button _replaceAll;
    private readonly Label _count;
    private readonly Grid _replaceRow;
    private readonly Border _frame;
    private ShellPalette _palette;
    private EditorModel? _model;
    private CancellationTokenSource? _counting;
    private bool _replaceShown;

    /// <summary>The overlay in <paramref name="palette"/>'s colours, hidden.</summary>
    public FindOverlay(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _query = new RoutedEntry { Placeholder = "Find", HeightRequest = 28, FontSize = palette.FontSize, FontFamily = palette.Font() };
        _query.RouteKey = OnQueryKey;
        _query.TextChanged += (_, _) => OnQueryChanged();
        _queryBox = palette.Field(_query);
        _replacement = new RoutedEntry { Placeholder = "Replace", HeightRequest = 28, FontSize = palette.FontSize, FontFamily = palette.Font() };
        _replacement.RouteKey = OnReplacementKey;
        _replacementBox = palette.Field(_replacement);
        _expand = Icon("›", "Show or hide replace (Ctrl+H)", () => SetReplace(!_replaceShown, focus: false));
        _matchCase = Icon("Aa", "Match case (Alt+C)", () => Toggle(f => f.MatchCase = !f.MatchCase));
        _wholeWord = Icon("ab", "Whole word (Alt+W)", () => Toggle(f => f.WholeWord = !f.WholeWord));
        _regex = Icon(".*", "Regular expression (Alt+R)", () => Toggle(f => f.UseRegex = !f.UseRegex));
        _previous = Icon("↑", "Previous match (Shift+Enter)", () => _ = StepAsync(forward: false));
        _next = Icon("↓", "Next match (Enter)", () => _ = StepAsync(forward: true));
        _close = Icon("×", "Close (Escape)", Close);
        _replaceOne = Icon("⇄", "Replace (Enter)", ReplaceOne);
        _replaceAll = Icon("⇶", "Replace all (Ctrl+Alt+Enter)", () => _ = ReplaceAllInBackgroundAsync());
        _count = palette.Label("No results", palette.Secondary, palette.SmallSize);
        _count.VerticalOptions = LayoutOptions.Center;
        _count.MinimumWidthRequest = 72;
        _count.Margin = new Thickness(6, 0);

        var findRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(220)), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 2,
        };
        findRow.Add(_queryBox, 0, 0);
        findRow.Add(_matchCase, 1, 0);
        findRow.Add(_wholeWord, 2, 0);
        findRow.Add(_regex, 3, 0);
        findRow.Add(_count, 4, 0);
        findRow.Add(_previous, 5, 0);
        findRow.Add(_next, 6, 0);
        findRow.Add(_close, 7, 0);
        _replaceRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(220)), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 2,
            IsVisible = false,
        };
        _replaceRow.Add(_replacementBox, 0, 0);
        _replaceRow.Add(_replaceOne, 1, 0);
        _replaceRow.Add(_replaceAll, 2, 0);
        var rows = new VerticalStackLayout { Spacing = 4, Children = { findRow, _replaceRow } };
        var body = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 2 };
        body.Add(_expand, 0, 0);
        body.Add(rows, 1, 0);
        _frame = new Border
        {
            Content = body,
            Padding = new Thickness(4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 12, Offset = new Point(0, 4) },
        };
        Content = _frame;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        Margin = new Thickness(0, 4, 18, 0);
        IsVisible = false;
        Apply(palette);
    }

    /// <summary>Raised when it closed (the page gives the editor the keyboard).</summary>
    public event EventHandler? Closed;

    /// <summary>Raised with what to show in the status bar ("Replaced 17").</summary>
    public event EventHandler<string>? Notice;

    /// <summary>The editor it finds in (the shown one); setting it carries the query over and counts again.</summary>
    public EditorModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value))
            {
                return;
            }

            _model = value;
            if (IsVisible && value is not null)
            {
                Push(value);
            }
        }
    }

    /// <summary>Shows it (with the replace row when <paramref name="replace"/>), the query set to <paramref name="query"/> when given, its text selected and focused.</summary>
    public void Open(bool replace, string? query)
    {
        IsVisible = true;
        SetReplace(replace, focus: false);
        if (query is not null && query.Length > 0)
        {
            _query.Text = query;
        }

        if (_model is { } model)
        {
            Push(model);
        }

        var target = replace && (_query.Text ?? "").Length > 0 ? _replacement : _query;
        Dispatcher.DispatchLabelled("find: focus the box", () =>
        {
            target.Focus();
            target.CursorPosition = 0;
            target.SelectionLength = (target.Text ?? "").Length;
        });
    }

    /// <summary>Closes it: the highlights go, and <see cref="Closed"/> gives the editor the keyboard back.</summary>
    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        _counting?.Cancel();
        if (_model is { } model)
        {
            model.Find.IsHighlighting = false;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Counts again ("3 of 17") after the caret moved or the text changed (off the UI thread for a large document).</summary>
    public void Recount()
    {
        if (IsVisible)
        {
            _ = CountAsync();
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _frame.BackgroundColor = palette.Raised;
        _frame.Stroke = palette.BorderStrong;
        _queryBox.Apply(palette);
        _replacementBox.Apply(palette);
        foreach (var button in (ReadOnlySpan<Button>)[_expand, _previous, _next, _close, _replaceOne, _replaceAll])
        {
            button.TextColor = palette.Text;
            button.BackgroundColor = Colors.Transparent;
        }

        ShowToggles();
        _ = CountAsync();
    }

    private Button Icon(string text, string tooltip, Action clicked)
    {
        var button = _palette.Flat(text, _palette.Text, 26, _palette.SmallSize);
        button.WidthRequest = 28;
        button.Padding = new Thickness(0);
        button.CornerRadius = 4;
        ToolTipProperties.SetText(button, tooltip);
        SemanticProperties.SetDescription(button, tooltip);
        button.Clicked += (_, _) => clicked();
        return button;
    }

    private void SetReplace(bool show, bool focus)
    {
        _replaceShown = show;
        _replaceRow.IsVisible = show;
        _expand.Text = show ? "⌄" : "›";
        if (focus && show)
        {
            _replacement.Focus();
        }
    }

    private void Toggle(Action<EditorFind> change)
    {
        if (_model is { } model)
        {
            change(model.Find);
            ShowToggles();
            _ = FindFromCaretAsync();
        }
    }

    // The toggles in the accent's fill when on.
    private void ShowToggles()
    {
        var find = _model?.Find;
        foreach (var (button, on) in (ReadOnlySpan<(Button, bool)>)[(_matchCase, find?.MatchCase == true), (_wholeWord, find?.WholeWord == true), (_regex, find?.UseRegex == true)])
        {
            button.BackgroundColor = on ? _palette.Accent : Colors.Transparent;
            button.TextColor = on ? _palette.AccentInk : _palette.Text;
        }
    }

    // The query and toggles into a newly shown editor.
    private void Push(EditorModel model)
    {
        model.Find.Query = _query.Text ?? "";
        ShowToggles();
        _ = CountAsync();
    }

    private void OnQueryChanged()
    {
        if (_model is { } model && IsVisible)
        {
            model.Find.Query = _query.Text ?? "";
            _ = FindFromCaretAsync();
        }
    }

    private async Task FindFromCaretAsync()
    {
        if (_model is not { } model)
        {
            return;
        }

        await model.Find.FindAsync(forward: true, fromSelectionStart: true);
        await CountAsync();
    }

    private async Task StepAsync(bool forward)
    {
        if (_model is not { } model)
        {
            return;
        }

        await model.Find.FindAsync(forward);
        await CountAsync();
    }

    private void ReplaceOne()
    {
        if (_model is { } model)
        {
            model.Find.ReplaceOne(_replacement.Text ?? "");
            _ = CountAsync();
        }
    }

    private async Task ReplaceAllInBackgroundAsync()
    {
        if (_model is not { } model)
        {
            return;
        }

        int replaced = await model.Find.ReplaceAllInBackgroundAsync(_replacement.Text ?? "");
        Notice?.Invoke(this, replaced == 1 ? "Replaced 1 match (Ctrl+Z takes it back)." : $"Replaced {replaced:N0} matches (one Ctrl+Z takes them back).");
        await CountAsync();
    }

    // "3 of 17", "No results", or why the expression does not parse (in the error's colour).
    private async Task CountAsync()
    {
        if (_model is not { } model)
        {
            _count.Text = "";
            return;
        }

        var find = model.Find;
        if (find.Problem is { } problem)
        {
            _count.Text = "Invalid";
            _count.TextColor = _palette.Error;
            ToolTipProperties.SetText(_count, problem);
            return;
        }

        _count.TextColor = _palette.Secondary;
        ToolTipProperties.SetText(_count, "");
        if (find.Query.Length == 0)
        {
            _count.Text = "No results";
            return;
        }

        if (_counting is { } previous)
        {
            await previous.CancelAsync();
        }

        var cancel = new CancellationTokenSource();
        _counting = cancel;
        if (!find.IsListed)
        {
            _count.Text = "…";
        }

        try
        {
            var count = await find.CountInBackgroundAsync(cancel.Token);
            if (ReferenceEquals(_counting, cancel) && ReferenceEquals(_model, model))
            {
                _count.Text = count.Text + (find.Pattern.TimedOut ? " (a line took too long)" : "");
            }
        }
        catch (OperationCanceledException)
        {
            // A newer count took its place.
        }
        finally
        {
            if (ReferenceEquals(_counting, cancel))
            {
                _counting = null;
            }

            cancel.Dispose();
        }
    }

    private bool OnQueryKey(EditorKey key, KeyModifiers modifiers)
    {
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        bool alt = (modifiers & KeyModifiers.Alt) != 0;
        bool control = (modifiers & KeyModifiers.Control) != 0;
        switch (key)
        {
            case EditorKey.Escape:
                Close();
                return true;
            case EditorKey.Enter when control && alt:
                _ = ReplaceAllInBackgroundAsync();
                return true;
            case EditorKey.Enter or EditorKey.F3:
                _ = StepAsync(forward: !shift);
                return true;
            case EditorKey.C when alt && !control:
                Toggle(f => f.MatchCase = !f.MatchCase);
                return true;
            case EditorKey.W when alt && !control:
                Toggle(f => f.WholeWord = !f.WholeWord);
                return true;
            case EditorKey.R when alt && !control:
                Toggle(f => f.UseRegex = !f.UseRegex);
                return true;
            case EditorKey.H when control && !alt:
                SetReplace(true, focus: true);
                return true;
            case EditorKey.F when control && !alt:
                _query.CursorPosition = 0;
                _query.SelectionLength = (_query.Text ?? "").Length;
                return true;
            default:
                return false;
        }
    }

    private bool OnReplacementKey(EditorKey key, KeyModifiers modifiers)
    {
        bool alt = (modifiers & KeyModifiers.Alt) != 0;
        bool control = (modifiers & KeyModifiers.Control) != 0;
        switch (key)
        {
            case EditorKey.Escape:
                Close();
                return true;
            case EditorKey.Enter when control && alt:
                _ = ReplaceAllInBackgroundAsync();
                return true;
            case EditorKey.Enter:
                ReplaceOne();
                return true;
            default:
                return OnQueryKey(key, modifiers);
        }
    }
}
