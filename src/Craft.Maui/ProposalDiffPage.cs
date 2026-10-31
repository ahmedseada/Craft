// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Craft.Editor;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>A line of the diff view: the old and new line numbers, the marker and the text, and the row's colours.</summary>
/// <param name="OldNumber">The line number before the change, or "" for an added line.</param>
/// <param name="NewNumber">The line number after the change, or "" for a removed line.</param>
/// <param name="Marker">"−", "+" or "".</param>
/// <param name="Text">The line.</param>
/// <param name="Background">The row's background.</param>
/// <param name="MarkerColor">The marker's colour.</param>
/// <param name="Foreground">The text's colour.</param>
public sealed record DiffRow(string OldNumber, string NewNumber, string Marker, string Text, Color Background, Color MarkerColor, Color Foreground);

/// <summary>A hunk of the diff view: its header (number, lines, how it was placed), its lines, and the user's answer.</summary>
public sealed class DiffHunkGroup : ObservableCollection<DiffRow>, INotifyPropertyChanged
{
    private bool _accepted = true;

    /// <summary>The hunk <paramref name="index"/> of <paramref name="hunk"/>'s rows.</summary>
    public DiffHunkGroup(int index, DiffHunk hunk, string? placement, IEnumerable<DiffRow> rows)
        : base(rows)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        Index = index;
        Title = string.Create(CultureInfo.InvariantCulture, $"Hunk {index + 1}");
        int first = hunk.NewCount > 0 ? hunk.NewStart : hunk.OldStart;
        int last = first + Math.Max(hunk.NewCount, hunk.OldCount) - 1;
        Lines = string.Create(CultureInfo.InvariantCulture, $"lines {first}–{Math.Max(first, last)}");
        Placement = placement is null ? "" : "placed by " + placement;
    }

    /// <summary>Its index in the diff.</summary>
    public int Index { get; }

    /// <summary>"Hunk 1".</summary>
    public string Title { get; }

    /// <summary>"lines 11–13" (after the change).</summary>
    public string Lines { get; }

    /// <summary>"placed by exact text match", or "".</summary>
    public string Placement { get; }

    /// <summary>Whether the user keeps it (Accept, the default) or not (Reject).</summary>
    public bool Accepted
    {
        get => _accepted;
        set
        {
            if (_accepted != value)
            {
                _accepted = value;
                Changed();
                Changed(nameof(State));
            }
        }
    }

    /// <summary>"Accepted" or "Rejected", for the header.</summary>
    public string State => _accepted ? "Accepted" : "Rejected";

    /// <inheritdoc cref="INotifyPropertyChanged.PropertyChanged" />
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The diff-and-accept view (plan phase A1, the approved design's <c>docs/design/Diff.dc.html</c>): a proposal's edits
/// as an inline diff, hunk by hunk, each with its lines and how it was placed and its own Accept and Reject; Accept all
/// and Reject all; and beside it "Before you accept" (the language's errors and warnings after the change, when its
/// semantic layer can tell), how the edit was placed, and that an accepted change is one undo step. Apply keeps the
/// accepted hunks, as one undo step (the host applies them); Reject all, closing it or a cancelled request leaves the
/// document as it was. Its colours come from the theme (<see cref="EditorColors"/>), so the design phase can retarget
/// them; the column moves below the diff in a narrow window (the measured width).
/// </summary>
public sealed class ProposalDiffPage : ContentPage
{
    private const double NarrowWidth = 720;
    private readonly TaskCompletionSource<IReadOnlyList<bool>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<DiffHunkGroup> _groups;
    private readonly Grid _body;
    private readonly View _diffView;
    private readonly View _side;
    private readonly int _answerCount;
    private bool _narrow;

    /// <summary>The view of <paramref name="proposal"/> in <paramref name="theme"/>'s colours.</summary>
    public ProposalDiffPage(EditProposal proposal, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(theme);
        var colors = theme.Colors;
        Color C(uint argb) => ThemeColors.Of(argb);
        Title = proposal.Title;
        BackgroundColor = C(colors.Ground);
        var diff = proposal.Diff;
        _answerCount = Math.Max(1, diff.Hunks.Count);
        _groups = Groups(diff, proposal.Placement, colors);

        var file = proposal.Document.FilePath is { } path ? Path.GetFileName(path) : "untitled";
        var heading = new Label { Text = $"{file} · Review changes", FontSize = 13, TextColor = C(colors.SecondaryText) };
        var title = new Label { Text = proposal.Title, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = C(colors.Foreground) };
        var detail = new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture,
                $"{proposal.Origin ?? proposal.Source} · {diff.Hunks.Count} {(diff.Hunks.Count == 1 ? "hunk" : "hunks")}, +{diff.Added} −{diff.Removed}{(diff.Coarse ? " · a long change, shown whole" : "")}"),
            FontSize = 13,
            TextColor = C(colors.SecondaryText),
        };
        var rejectAll = Button("Reject all", colors, primary: false);
        var acceptAll = Button("Accept all", colors, primary: true);
        rejectAll.Clicked += (_, _) => _answer.TrySetResult(new bool[_answerCount]);
        acceptAll.Clicked += (_, _) => _answer.TrySetResult(Fill(true));
        var apply = Button("Apply accepted hunks", colors, primary: false);
        apply.Clicked += (_, _) => _answer.TrySetResult(Answers());
        apply.IsVisible = _groups.Count > 1;
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { rejectAll, apply, acceptAll }, VerticalOptions = LayoutOptions.Center };
        var header = new Grid
        {
            Padding = new Thickness(16, 12),
            BackgroundColor = C(colors.Surface),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
        };
        header.Add(new VerticalStackLayout { Spacing = 2, Children = { heading, title, detail } }, 0, 0);
        header.Add(buttons, 1, 0);

        _diffView = new CollectionView
        {
            IsGrouped = true,
            ItemsSource = _groups,
            GroupHeaderTemplate = new DataTemplate(() => MakeHunkHeader(colors)),
            ItemTemplate = new DataTemplate(MakeRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = C(colors.Background),
        };
        _side = MakeSide(proposal, colors);
        _body = new Grid();
        Layout(narrow: false);

        var page = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)] };
        page.Add(header, 0, 0);
        page.Add(new BoxView { Color = C(colors.Border) }, 0, 1);
        page.Add(_body, 0, 2);
        Content = page;
        SizeChanged += (_, _) => Layout(Width > 0 && Width < NarrowWidth);
    }

    /// <summary>The user's answer: one a hunk, true to apply it (all false: rejected).</summary>
    public Task<IReadOnlyList<bool>> Answer => _answer.Task;

    /// <summary>Answers Reject all (a cancelled request, the page closed another way).</summary>
    public void Reject() => _answer.TrySetResult(new bool[_answerCount]);

    /// <inheritdoc />
    protected override bool OnBackButtonPressed()
    {
        Reject();
        return base.OnBackButtonPressed();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        Reject();
        base.OnDisappearing();
    }

    /// <summary>The hunks of <paramref name="diff"/> as the list's groups, their rows in <paramref name="colors"/>.</summary>
    public static List<DiffHunkGroup> Groups(TextDiff diff, string? placement, EditorColors colors)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(colors);
        var culture = CultureInfo.InvariantCulture;
        var text = ThemeColors.Of(colors.Foreground);
        var secondary = ThemeColors.Of(colors.SecondaryText);
        var removedBack = ThemeColors.Of(colors.DiffRemoved);
        var addedBack = ThemeColors.Of(colors.DiffAdded);
        var removed = ThemeColors.Of(colors.Error);
        var added = ThemeColors.Of(colors.Success);
        var groups = new List<DiffHunkGroup>(diff.Hunks.Count);
        for (int h = 0; h < diff.Hunks.Count; h++)
        {
            var hunk = diff.Hunks[h];
            var rows = new DiffRow[hunk.Lines.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                var line = hunk.Lines[i];
                string oldNumber = line.OldLine > 0 ? line.OldLine.ToString(culture) : "";
                string newNumber = line.NewLine > 0 ? line.NewLine.ToString(culture) : "";
                rows[i] = line.Kind switch
                {
                    DiffLineKind.Removed => new DiffRow(oldNumber, newNumber, "−", line.Text, removedBack, removed, text),
                    DiffLineKind.Added => new DiffRow(oldNumber, newNumber, "+", line.Text, addedBack, added, text),
                    _ => new DiffRow(oldNumber, newNumber, "", line.Text, Colors.Transparent, secondary, text),
                };
            }

            groups.Add(new DiffHunkGroup(h, hunk, placement, rows));
        }

        return groups;
    }

    private bool[] Fill(bool value)
    {
        var answers = new bool[_answerCount];
        Array.Fill(answers, value);
        return answers;
    }

    private bool[] Answers()
    {
        var answers = Fill(true);
        foreach (var group in _groups)
        {
            answers[group.Index] = group.Accepted;
        }

        return answers;
    }

    private void Layout(bool narrow)
    {
        if (_narrow == narrow && _body.Children.Count > 0)
        {
            return;
        }

        _narrow = narrow;
        _body.Children.Clear();
        _body.RowDefinitions.Clear();
        _body.ColumnDefinitions.Clear();
        if (narrow)
        {
            _body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _body.Add(_diffView, 0, 0);
            _body.Add(_side, 0, 1);
        }
        else
        {
            _body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(300)));
            _body.Add(_diffView, 0, 0);
            _body.Add(_side, 1, 0);
        }
    }

    private static Button Button(string text, EditorColors colors, bool primary) => new()
    {
        Text = text,
        FontSize = 13,
        Padding = new Thickness(14, 6),
        MinimumHeightRequest = 32,
        CornerRadius = 6,
        BorderWidth = 1,
        BorderColor = ThemeColors.Of(primary ? colors.Accent : colors.Border),
        BackgroundColor = ThemeColors.Of(primary ? colors.Accent : colors.Surface),
        TextColor = ThemeColors.Of(primary ? colors.Ground : colors.Foreground),
    };

    private static Grid MakeHunkHeader(EditorColors colors)
    {
        var secondary = ThemeColors.Of(colors.SecondaryText);
        var title = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.Of(colors.Foreground), VerticalTextAlignment = TextAlignment.Center };
        title.SetBinding(Label.TextProperty, static (DiffHunkGroup g) => g.Title);
        var lines = new Label { FontSize = 12, TextColor = secondary, VerticalTextAlignment = TextAlignment.Center };
        lines.SetBinding(Label.TextProperty, static (DiffHunkGroup g) => g.Lines);
        var placement = new Label { FontSize = 12, TextColor = secondary, VerticalTextAlignment = TextAlignment.Center };
        placement.SetBinding(Label.TextProperty, static (DiffHunkGroup g) => g.Placement);
        var state = new Label { FontSize = 12, TextColor = ThemeColors.Of(colors.Accent), VerticalTextAlignment = TextAlignment.Center };
        state.SetBinding(Label.TextProperty, static (DiffHunkGroup g) => g.State);
        var reject = Button("Reject", colors, primary: false);
        var accept = Button("Accept", colors, primary: false);
        reject.Clicked += (sender, _) => SetAccepted(sender, false);
        accept.Clicked += (sender, _) => SetAccepted(sender, true);
        var grid = new Grid
        {
            Padding = new Thickness(12, 8),
            ColumnSpacing = 10,
            BackgroundColor = ThemeColors.Of(colors.Surface),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
            ],
        };
        grid.Add(title, 0, 0);
        grid.Add(lines, 1, 0);
        grid.Add(placement, 2, 0);
        grid.Add(state, 3, 0);
        grid.Add(reject, 4, 0);
        grid.Add(accept, 5, 0);
        return grid;

        static void SetAccepted(object? sender, bool accepted)
        {
            if (sender is BindableObject { BindingContext: DiffHunkGroup group })
            {
                group.Accepted = accepted;
            }
        }
    }

    private static Grid MakeRow()
    {
        var oldNumber = Mono(12, TextAlignment.End);
        oldNumber.SetBinding(Label.TextProperty, static (DiffRow row) => row.OldNumber);
        oldNumber.SetBinding(Label.TextColorProperty, static (DiffRow row) => row.MarkerColor);
        var newNumber = Mono(12, TextAlignment.End);
        newNumber.SetBinding(Label.TextProperty, static (DiffRow row) => row.NewNumber);
        newNumber.SetBinding(Label.TextColorProperty, static (DiffRow row) => row.MarkerColor);
        var marker = Mono(13, TextAlignment.Center);
        marker.SetBinding(Label.TextProperty, static (DiffRow row) => row.Marker);
        marker.SetBinding(Label.TextColorProperty, static (DiffRow row) => row.MarkerColor);
        var text = Mono(13, TextAlignment.Start);
        text.LineBreakMode = LineBreakMode.NoWrap;
        text.SetBinding(Label.TextProperty, static (DiffRow row) => row.Text);
        text.SetBinding(Label.TextColorProperty, static (DiffRow row) => row.Foreground);
        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(48)), new ColumnDefinition(new GridLength(48)), new ColumnDefinition(new GridLength(22)),
                new ColumnDefinition(GridLength.Star),
            ],
        };
        grid.SetBinding(BackgroundColorProperty, static (DiffRow row) => row.Background);
        grid.Add(oldNumber, 0, 0);
        grid.Add(newNumber, 1, 0);
        grid.Add(marker, 2, 0);
        grid.Add(text, 3, 0);
        return grid;

        static Label Mono(double size, TextAlignment alignment) => new()
        {
            FontFamily = "Cascadia Mono",
            FontSize = size,
            Padding = new Thickness(4, 2),
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = TextAlignment.Center,
        };
    }

    private static ScrollView MakeSide(EditProposal proposal, EditorColors colors)
    {
        var secondary = ThemeColors.Of(colors.SecondaryText);
        var text = ThemeColors.Of(colors.Foreground);
        Label Caption(string value) => new() { Text = value, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = secondary, Margin = new Thickness(0, 12, 0, 4) };
        Label Body(string value, Color? color = null) => new() { Text = value, FontSize = 13, TextColor = color ?? text, LineBreakMode = LineBreakMode.WordWrap };

        var errors = Body("Checking…", secondary);
        var warnings = Body("", secondary);
        var firstErrors = Body("", ThemeColors.Of(colors.Error));
        _ = ShowCheckAsync(proposal.Check, errors, warnings, firstErrors, colors);

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(16, 4, 16, 16),
            Children =
            {
                Caption("BEFORE YOU ACCEPT"),
                Body("The language after the change", secondary),
                errors,
                warnings,
                firstErrors,
                Caption("HOW THE EDIT WAS PLACED"),
                Body(proposal.Placement ?? "Given as offsets by the plug-in, applied as they are."),
                Caption("UNDO"),
                Body("An accepted change is one undo step (Ctrl+Z), named after the action."),
            },
        };
        return new ScrollView { Content = stack, BackgroundColor = ThemeColors.Of(colors.Surface) };
    }

    private static async Task ShowCheckAsync(Task<ProposalCheck?> pending, Label errors, Label warnings, Label firstErrors, EditorColors colors)
    {
        ProposalCheck? check;
        try
        {
            check = await pending;
        }
#pragma warning disable CA1031 // A failed check leaves the column saying so; the user still decides.
        catch (Exception)
#pragma warning restore CA1031
        {
            check = null;
        }

        var culture = CultureInfo.InvariantCulture;
        if (check is null)
        {
            errors.Text = "Not checked: this document's language has no semantic layer, or the check is off (CRAFT_DIFF_CHECK).";
            return;
        }

        errors.Text = string.Create(culture, $"{check.ErrorsAfter} {(check.ErrorsAfter == 1 ? "error" : "errors")} (now {check.ErrorsBefore})");
        errors.TextColor = ThemeColors.Of(check.ErrorsAfter > check.ErrorsBefore ? colors.Error : colors.Success);
        warnings.Text = string.Create(culture, $"Warnings: {check.WarningsAfter}{(check.WarningsAfter == check.WarningsBefore ? " (unchanged)" : $" (now {check.WarningsBefore})")}");
        warnings.TextColor = ThemeColors.Of(check.WarningsAfter > check.WarningsBefore ? colors.Warning : colors.SecondaryText);
        firstErrors.Text = string.Join("\n", check.FirstErrors);
    }
}
