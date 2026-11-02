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
        : this(index, hunk, placement, rows, null, 0)
    {
    }

    /// <summary>
    /// The hunk <paramref name="index"/> of <paramref name="hunk"/>'s rows, of the file <paramref name="file"/> (its name
    /// heads the hunk) which is proposal <paramref name="proposal"/> of a review across files (X part 6).
    /// </summary>
    public DiffHunkGroup(int index, DiffHunk hunk, string? placement, IEnumerable<DiffRow> rows, string? file, int proposal)
        : base(rows)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        Index = index;
        Proposal = proposal;
        Title = file is null ? string.Create(CultureInfo.InvariantCulture, $"Hunk {index + 1}") : string.Create(CultureInfo.InvariantCulture, $"{file} · hunk {index + 1}");
        int first = hunk.NewCount > 0 ? hunk.NewStart : hunk.OldStart;
        int last = first + Math.Max(hunk.NewCount, hunk.OldCount) - 1;
        Lines = string.Create(CultureInfo.InvariantCulture, $"lines {first}–{Math.Max(first, last)}");
        Placement = placement is null ? "" : "placed by " + placement;
    }

    /// <summary>Its index in the diff.</summary>
    public int Index { get; }

    /// <summary>The proposal (the file) of a review across files it belongs to; 0 for a single file's.</summary>
    public int Proposal { get; }

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

    /// <summary>
    /// Whether the user answered this hunk with its own Accept or Reject (<see cref="Answer"/>): once every hunk is, the
    /// review ends with those answers (the owner, 2026-11-02: a hunk's Accept left the view open with no way out).
    /// </summary>
    public bool Answered { get; private set; }

    /// <summary>The user's answer for this hunk: <see cref="Accepted"/> set and <see cref="Answered"/> raised.</summary>
    public void Answer(bool accepted)
    {
        Accepted = accepted;
        Answered = true;
        Changed(nameof(Answered));
    }

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
    private readonly TaskCompletionSource<IReadOnlyList<IReadOnlyList<bool>>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<DiffHunkGroup> _groups;
    private readonly Grid _body;
    private readonly View _diffView;
    private readonly View _side;
    private readonly int[] _answerCounts;
    private bool _narrow;

    /// <summary>The view of <paramref name="proposal"/> in <paramref name="theme"/>'s colours.</summary>
    public ProposalDiffPage(EditProposal proposal, ITheme theme)
        : this([proposal ?? throw new ArgumentNullException(nameof(proposal))], proposal.Title, theme)
    {
    }

    /// <summary>
    /// One review of the edits to several files (X part 6: Replace all in files) titled <paramref name="title"/>, each file's
    /// hunks under its name, in <paramref name="theme"/>'s colours; Accept all, Reject all, or the hunks accepted.
    /// </summary>
    public ProposalDiffPage(IReadOnlyList<EditProposal> proposals, string title, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(theme);
        if (proposals.Count == 0)
        {
            throw new ArgumentException("A review needs at least one file's edits.", nameof(proposals));
        }

        var colors = theme.Colors;
        Color C(uint argb) => ThemeColors.Of(argb);
        var proposal = proposals[0];
        bool several = proposals.Count > 1;
        Title = several ? title : proposal.Title;
        BackgroundColor = C(colors.Ground);
        var diff = proposal.Diff;
        _answerCounts = [.. proposals.Select(p => Math.Max(1, p.Diff.Hunks.Count))];
        _groups = [];
        for (int i = 0; i < proposals.Count; i++)
        {
            var name = several ? (proposals[i].Document.FilePath is { } one ? Path.GetFileName(one) : "untitled") : null;
            _groups.AddRange(Groups(proposals[i].Diff, proposals[i].Placement, colors, name, i));
        }

        int hunks = proposals.Sum(p => p.Diff.Hunks.Count);
        int added = proposals.Sum(p => p.Diff.Added), removed = proposals.Sum(p => p.Diff.Removed);
        var file = several ? string.Create(CultureInfo.InvariantCulture, $"{proposals.Count} files") : proposal.Document.FilePath is { } path ? Path.GetFileName(path) : "untitled";
        var heading = new Label { Text = $"{file} · Review changes", FontSize = 13, TextColor = C(colors.SecondaryText) };
        var titleLabel = new Label { Text = Title, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = C(colors.Foreground) };
        var detail = new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture,
                $"{proposal.Origin ?? proposal.Source} · {hunks} {(hunks == 1 ? "hunk" : "hunks")}, +{added} −{removed}{(diff.Coarse ? " · a long change, shown whole" : "")}"),
            FontSize = 13,
            TextColor = C(colors.SecondaryText),
        };
        var rejectAll = Button("Reject all", colors, primary: false);
        var acceptAll = Button("Accept all", colors, primary: true);
        rejectAll.Clicked += (_, _) => _answer.TrySetResult(Fill(false));
        acceptAll.Clicked += (_, _) => _answer.TrySetResult(Fill(true));
        var apply = Button("Apply accepted hunks", colors, primary: false);
        apply.Clicked += (_, _) => _answer.TrySetResult(Answers());
        apply.IsVisible = _groups.Count > 1;
        // A way out at any time (the owner, 2026-11-02: "I can't exit this view or close it"): closing leaves the document as
        // it was, as Reject all does.
        var close = Button("✕", colors, primary: false);
        close.Padding = new Thickness(10, 6);
        ToolTipProperties.SetText(close, "Close without changing the document (the same as Reject all)");
        SemanticProperties.SetDescription(close, "Close the review without changing the document");
        close.Clicked += (_, _) => Reject();
        ToolTipProperties.SetText(rejectAll, "Keep the document as it is and close");
        ToolTipProperties.SetText(acceptAll, "Apply every hunk as one undo step and close");
        ToolTipProperties.SetText(apply, "Apply the hunks marked Accepted as one undo step and close");
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { rejectAll, apply, acceptAll, close }, VerticalOptions = LayoutOptions.Center };
        // Each hunk answered with its own Accept or Reject: the review ends with those answers (one hunk's Accept applies it).
        foreach (var group in _groups)
        {
            group.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DiffHunkGroup.Answered) && EveryHunkAnswered(_groups))
                {
                    _answer.TrySetResult(Answers());
                }
            };
        }
        var header = new Grid
        {
            Padding = new Thickness(16, 12),
            BackgroundColor = C(colors.Surface),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
        };
        header.Add(new VerticalStackLayout { Spacing = 2, Children = { heading, titleLabel, detail } }, 0, 0);
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

    /// <summary>The user's answer for the first (or only) file: one a hunk, true to apply it (all false: rejected).</summary>
    public Task<IReadOnlyList<bool>> Answer => FirstAnswer();

    /// <summary>The user's answers, one list a file (one answer a hunk).</summary>
    public Task<IReadOnlyList<IReadOnlyList<bool>>> FileAnswers => _answer.Task;

    /// <summary>Whether every hunk of <paramref name="groups"/> was answered with its own Accept or Reject (the review then ends).</summary>
    public static bool EveryHunkAnswered(IReadOnlyList<DiffHunkGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            return false;
        }

        foreach (var group in groups)
        {
            if (!group.Answered)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Answers Reject all (a cancelled request, the page closed another way).</summary>
    public void Reject() => _answer.TrySetResult(Fill(false));

    private async Task<IReadOnlyList<bool>> FirstAnswer() => (await _answer.Task.ConfigureAwait(true))[0];

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
    public static List<DiffHunkGroup> Groups(TextDiff diff, string? placement, EditorColors colors) => Groups(diff, placement, colors, null, 0);

    /// <summary>The hunks of <paramref name="diff"/> (of the file <paramref name="file"/>, proposal <paramref name="proposal"/> of a review across files) as the list's groups.</summary>
    public static List<DiffHunkGroup> Groups(TextDiff diff, string? placement, EditorColors colors, string? file, int proposal)
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

            groups.Add(new DiffHunkGroup(h, hunk, placement, rows, file, proposal));
        }

        return groups;
    }

    private IReadOnlyList<bool>[] Fill(bool value)
    {
        var answers = new IReadOnlyList<bool>[_answerCounts.Length];
        for (int i = 0; i < answers.Length; i++)
        {
            var one = new bool[_answerCounts[i]];
            Array.Fill(one, value);
            answers[i] = one;
        }

        return answers;
    }

    private IReadOnlyList<bool>[] Answers()
    {
        var answers = Fill(true);
        foreach (var group in _groups)
        {
            ((bool[])answers[group.Proposal])[group.Index] = group.Accepted;
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
                group.Answer(accepted);
            }
        }
    }

    internal static Grid MakeRow()
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

/// <summary>
/// The diff view read-only (X part 8: a changed file's working copy against the index or the last commit): the same rows as
/// <see cref="ProposalDiffPage"/>, hunk by hunk, with the file, the counts and Close; nothing to accept.
/// </summary>
public sealed class ChangesDiffPage : ContentPage
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The view of <paramref name="diff"/> of <paramref name="path"/> under <paramref name="title"/>, in <paramref name="theme"/>'s colours.</summary>
    public ChangesDiffPage(string title, string path, TextDiff diff, ITheme theme)
        : this(title, path, diff, theme, [])
    {
    }

    /// <summary>
    /// The view of <paramref name="diff"/> with <paramref name="notes"/> (X part 11: a review's findings) as rows under
    /// their new lines; a note on a line no hunk shows heads the list.
    /// </summary>
    public ChangesDiffPage(string title, string path, TextDiff diff, ITheme theme, IReadOnlyList<DiffNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(theme);
        var colors = theme.Colors;
        Title = title;
        BackgroundColor = ThemeColors.Of(colors.Ground);
        var heading = new Label { Text = $"{System.IO.Path.GetFileName(path)} · Changes", FontSize = 13, TextColor = ThemeColors.Of(colors.SecondaryText) };
        var name = new Label { Text = title, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = ThemeColors.Of(colors.Foreground) };
        var detail = new Label
        {
            Text = diff.IsEmpty ? "No change." : string.Create(CultureInfo.InvariantCulture,
                $"{diff.Hunks.Count} {(diff.Hunks.Count == 1 ? "hunk" : "hunks")}, +{diff.Added} −{diff.Removed}{(diff.Coarse ? " · a long change, shown whole" : "")}"),
            FontSize = 13,
            TextColor = ThemeColors.Of(colors.SecondaryText),
        };
        var close = new Button
        {
            Text = "Close",
            FontSize = 13,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 32,
            CornerRadius = 6,
            BorderWidth = 1,
            BorderColor = ThemeColors.Of(colors.Border),
            BackgroundColor = ThemeColors.Of(colors.Surface),
            TextColor = ThemeColors.Of(colors.Foreground),
            VerticalOptions = LayoutOptions.Center,
        };
        close.Clicked += (_, _) => _closed.TrySetResult();
        var header = new Grid
        {
            Padding = new Thickness(16, 12),
            BackgroundColor = ThemeColors.Of(colors.Surface),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
        };
        header.Add(new VerticalStackLayout { Spacing = 2, Children = { heading, name, detail } }, 0, 0);
        header.Add(close, 1, 0);
        var list = new CollectionView
        {
            IsGrouped = true,
            ItemsSource = WithNotes(ProposalDiffPage.Groups(diff, null, colors), notes, colors),
            GroupHeaderTemplate = new DataTemplate(() =>
            {
                var lines = new Label { FontSize = 12, TextColor = ThemeColors.Of(colors.SecondaryText), Padding = new Thickness(12, 6), BackgroundColor = ThemeColors.Of(colors.Surface) };
                lines.SetBinding(Label.TextProperty, static (DiffHunkGroup g) => g.Lines);
                return lines;
            }),
            ItemTemplate = new DataTemplate(ProposalDiffPage.MakeRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = ThemeColors.Of(colors.Background),
        };
        var page = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(1)), new RowDefinition(GridLength.Star)] };
        page.Add(header, 0, 0);
        page.Add(new BoxView { Color = ThemeColors.Of(colors.Border) }, 0, 1);
        page.Add(list, 0, 2);
        Content = page;
    }

    /// <summary>Completes when the user closed it.</summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// <paramref name="groups"/> with each note as a row under the row of its new line ("● warning · message — reason" on the
    /// severity's tint); the notes no row shows go first, in a group of their own.
    /// </summary>
    public static List<DiffHunkGroup> WithNotes(List<DiffHunkGroup> groups, IReadOnlyList<DiffNote> notes, EditorColors colors)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(colors);
        if (notes.Count == 0)
        {
            return groups;
        }

        var placed = new bool[notes.Count];
        foreach (var group in groups)
        {
            for (int r = group.Count - 1; r >= 0; r--)
            {
                if (!int.TryParse(group[r].NewNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int line))
                {
                    continue;
                }

                for (int n = notes.Count - 1; n >= 0; n--)
                {
                    if (!placed[n] && notes[n].Line == line)
                    {
                        group.Insert(r + 1, NoteRow(notes[n], colors));
                        placed[n] = true;
                    }
                }
            }
        }

        var rest = new List<DiffRow>();
        for (int n = 0; n < notes.Count; n++)
        {
            if (!placed[n])
            {
                rest.Add(NoteRow(notes[n], colors));
            }
        }

        if (rest.Count > 0)
        {
            groups.Insert(0, new DiffHunkGroup(0, new DiffHunk(1, 0, 1, 0, []), null, rest, "Review notes", 0));
        }

        return groups;
    }

    private static DiffRow NoteRow(DiffNote note, EditorColors colors)
    {
        uint tone = note.Severity switch
        {
            DiagnosticSeverity.Error => colors.Error,
            DiagnosticSeverity.Warning => colors.Warning,
            _ => colors.Info,
        };
        string word = ReviewFinding.SeverityWord(note.Severity);
        string text = note.Reason.Length > 0 ? $"{word} · {note.Text} — {note.Reason}" : $"{word} · {note.Text}";
        string line = note.Line > 0 ? note.Line.ToString(CultureInfo.InvariantCulture) : "";
        return new DiffRow("", line, "●", text, ThemeColors.Of(colors.PopupBackground), ThemeColors.Of(tone), ThemeColors.Of(tone));
    }

    /// <summary>Closes it (a cancelled request).</summary>
    public void Close() => _closed.TrySetResult();

    /// <inheritdoc />
    protected override bool OnBackButtonPressed()
    {
        Close();
        return base.OnBackButtonPressed();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        Close();
        base.OnDisappearing();
    }
}
