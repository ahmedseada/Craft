// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Editor.Activities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// The status bar's activity (phase U): the newest running operation with a spinner (or a bar when its share is known),
/// its text, rate and time left, and "+N" for the others; hidden when none runs. A tap opens the notification centre on
/// the running list, each with Cancel. Updated from <see cref="ActivityCenter.Changed"/> (on the UI thread, at most once a frame).
/// </summary>
public sealed class ActivityStatusView : ContentView, IDisposable
{
    private readonly ActivityCenter _activities;
    private readonly ActivityIndicator _spinner = new() { IsRunning = false, WidthRequest = 12, HeightRequest = 12, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ProgressBar _bar = new() { WidthRequest = 56, HeightRequest = 4, Margin = new Thickness(0, 0, 6, 0), IsVisible = false };
    private readonly Label _text;

    /// <summary>The activity of <paramref name="activities"/>, in <paramref name="palette"/>'s colours.</summary>
    public ActivityStatusView(ActivityCenter activities, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(palette);
        _activities = activities;
        _text = palette.Label("", palette.Secondary, palette.CaptionSize);
        _text.MaximumWidthRequest = 520;
        var row = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { _spinner, _bar, _text } };
        Content = row;
        Margin = new Thickness(12, 0);
        IsVisible = false;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
        Apply(palette);
        activities.Changed += OnChanged;
    }

    /// <summary>Raised when the activity is tapped (the page opens the centre).</summary>
    public event EventHandler? Tapped;

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _spinner.Color = palette.Accent;
        _bar.ProgressColor = palette.Accent;
        _bar.BackgroundColor = palette.Border;
        _text.TextColor = palette.Secondary;
    }

    /// <summary>Shows the activities as they are now.</summary>
    public void Refresh()
    {
        var running = _activities.Visible;
        if (running.Count == 0)
        {
            IsVisible = false;
            _spinner.IsRunning = false;
            return;
        }

        var newest = running[0];
        IsVisible = true;
        bool known = newest.Fraction is not null;
        _bar.IsVisible = known;
        _bar.Progress = newest.Fraction ?? 0;
        _spinner.IsVisible = !known;
        _spinner.IsRunning = !known;
        string text = ActivityText.StatusLine(running);
        if (!string.Equals(_text.Text, text, StringComparison.Ordinal))
        {
            _text.Text = text;
        }

        ToolTipProperties.SetText(this, string.Join("\n", running.Select(a => $"{a.Title}: {ActivityText.Progress(a)}")));
    }

    /// <inheritdoc />
    public void Dispose() => _activities.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Refresh();
}

/// <summary>The status bar's bell (phase U): its count is what the notification centre holds that the user has not seen; a tap opens or closes the centre.</summary>
public sealed class BellView : ContentView
{
    /// <summary>The bell, as SVG path data on a 24 by 24 box.</summary>
    public const string BellIcon = "M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 20a2 2 0 0 0 4 0";

    private readonly StrokeIcon _icon = new(BellIcon, 14, 1.6);
    private readonly Label _count;
    private readonly Border _badge;
    private int _unseen;
    private int _running;

    /// <summary>Shows the tooltip again (the keys changed).</summary>
    public void Refresh() => Show(_unseen, _running);

    /// <summary>The bell in <paramref name="palette"/>'s colours.</summary>
    public BellView(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _count = palette.Label("", palette.AccentInk, palette.CaptionSize, FontWeight.SemiBold);
        _count.HorizontalTextAlignment = TextAlignment.Center;
        _badge = new Border
        {
            Content = _count,
            Padding = new Thickness(4, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            HeightRequest = 14,
            MinimumWidthRequest = 14,
            Margin = new Thickness(2, 0, 0, 0),
            IsVisible = false,
        };
        Content = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { _icon, _badge } };
        Margin = new Thickness(10, 0, 0, 0);
        MinimumWidthRequest = 24;
        ToolTipProperties.SetText(this, "Notifications");
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
        Apply(palette);
        palette.States(this, Craft.Editor.Shell.InteractionKind.Flat).Icon = _icon;   // phase X: hover and pressed
    }

    /// <summary>The keys that open the centre (" (Ctrl+…)", or "") for the tooltip; the page sets it from the bindings.</summary>
    public string Keys { get; set; } = "";

    /// <summary>Raised when the bell is tapped.</summary>
    public event EventHandler? Tapped;

    /// <summary>Shows <paramref name="unseen"/> (none: no badge) and whether anything runs.</summary>
    public void Show(int unseen, int running)
    {
        _badge.IsVisible = unseen > 0;
        _count.Text = unseen > 99 ? "99+" : unseen.ToString(CultureInfo.InvariantCulture);
        _unseen = unseen;
        _running = running;
        ToolTipProperties.SetText(this, string.Create(CultureInfo.InvariantCulture, $"Notifications{Keys}: {unseen} new, {running} running"));
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _icon.Color = palette.Secondary;
        InteractiveStates.Of(this)?.Apply(palette);
        _badge.BackgroundColor = palette.Accent;
        _count.TextColor = palette.AccentInk;
    }
}

/// <summary>
/// The notification centre (phase U, the owner's request): a panel the user opens from the bell (or the activity, or
/// View: Notifications) and closes, listing every entry of <see cref="NotificationCenter"/> grouped by source: the
/// running operations with their live progress and Cancel, the status lines (a model's state, a sandbox's last command),
/// the ended operations with their outcome and time taken, failures with their reason, Retry and Open log, and the
/// plug-ins' messages with their buttons; each dismissable, a group's at once, or all. Its rows are kept per entry and
/// updated in place, so a progress report redraws a label, not the list. Styled from the theme's tokens.
/// </summary>
public sealed class NotificationCenterView : ContentView, IDisposable
{
    private readonly NotificationCenter _center;
    private readonly ActivityCenter _activities;
    private readonly VerticalStackLayout _list = new() { Spacing = 2 };
    private readonly Label _title;
    private readonly EmptyStateView _empty;
    private readonly Button _clear;
    private readonly Button _close;
    private readonly Border _frame;
    private readonly Dictionary<long, EntryRow> _rows = [];
    private readonly Dictionary<string, GroupHeader> _headers = new(StringComparer.Ordinal);
    private readonly List<View> _order = [];
    private ShellPalette _palette;

    /// <summary>The centre over <paramref name="center"/>, in <paramref name="palette"/>'s colours.</summary>
    public NotificationCenterView(NotificationCenter center, ActivityCenter activities, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(palette);
        _center = center;
        _activities = activities;
        _palette = palette;
        _title = palette.Label("Notifications", palette.Text, palette.FontSize, FontWeight.SemiBold);
        _empty = new EmptyStateView(palette);               // phase X: says what shows here
        _empty.Show(Craft.Editor.Shell.ShellEmptyStates.Notifications());
        _clear = palette.Flat("Clear all", palette.Secondary, 26, palette.SmallSize);
        _clear.Clicked += (_, _) =>
        {
            if (ClearAll is { } clear)
            {
                clear();                                         // the shell's: taken away with Undo in the inline bar
            }
            else
            {
                _center.DismissAll();
            }
        };
        _close = palette.Flat("✕", palette.Secondary, 26, palette.SmallSize);
        _close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            Padding = new Thickness(12, 6, 6, 6),
        };
        header.Add(_title, 0, 0);
        header.Add(_clear, 1, 0);
        header.Add(_close, 2, 0);
        var body = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        body.Add(header, 0, 0);
        body.Add(new ScrollView { Content = _list, Padding = new Thickness(8, 0, 8, 8) }, 0, 1);
        _frame = new Border
        {
            Content = body,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.35f, Radius = 18, Offset = new Point(0, 6) },
        };
        Content = _frame;
        WidthRequest = 420;
        MaximumHeightRequest = 560;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(0, 0, 10, 8);
        IsVisible = false;
        Apply(palette);
        center.Changed += OnChanged;
    }

    /// <summary>Raised when its close button is pressed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>What "Clear all" does (phase X: the shell clears with Undo in the inline bar); null dismisses every entry at once.</summary>
    public Func<int>? ClearAll { get; set; }

    /// <summary>Whether it is open (or opening).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The tooltips of its close and clear buttons (the keys in effect for the close).</summary>
    public void SetTooltips(string close, string clear)
    {
        ToolTipProperties.SetText(_close, close);
        ToolTipProperties.SetText(_clear, clear);
    }

    /// <summary>Opens it (its entries become seen) or closes it, sliding in and out over CRAFT_MOTION_MS (phase X).</summary>
    public void Toggle(bool? open = null)
    {
        IsOpen = open ?? !IsOpen;
        if (IsOpen)
        {
            IsVisible = true;
            _center.MarkSeen();
            Refresh();
            _ = ShellMotion.ShowAsync(this, 12);
        }
        else
        {
            _ = ShellMotion.HideAsync(this, 12);
        }
    }

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _frame.BackgroundColor = palette.Raised;
        _frame.Stroke = palette.BorderStrong;
        _title.TextColor = palette.Text;
        _empty.Apply(palette);
        _rows.Clear();
        _headers.Clear();
        _order.Clear();
        if (IsVisible)
        {
            Refresh();
        }
    }

    /// <summary>Shows the entries as they are now (only while open).</summary>
    public void Refresh()
    {
        if (!IsVisible)
        {
            return;
        }

        var groups = _center.Groups;
        var order = new List<View>();
        var live = new HashSet<long>();
        foreach (var group in groups)
        {
            if (!_headers.TryGetValue(group.Source, out var head))
            {
                _headers[group.Source] = head = new GroupHeader(this, group.Source);
            }

            head.Show(group.Entries.Count, group.Entries.Any(e => e.Kind != NotificationKind.Status && !e.IsRunning));
            order.Add(head.View);
            foreach (var entry in group.Entries)
            {
                live.Add(entry.Id);
                if (!_rows.TryGetValue(entry.Id, out var row))
                {
                    _rows[entry.Id] = row = new EntryRow(this);
                }

                row.Show(entry);
                order.Add(row.View);
            }
        }

        foreach (var gone in _rows.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            _rows.Remove(gone);
        }

        if (order.Count == 0)
        {
            order.Add(_empty);
        }

        // The children change only when the entries' order does; a progress report updates labels in place.
        if (!order.SequenceEqual(_order))
        {
            _order.Clear();
            _order.AddRange(order);
            _list.Children.Clear();
            foreach (var view in order)
            {
                _list.Children.Add(view);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _center.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private static string Mark(NotificationEntry entry) => entry.IsRunning ? "◌" : entry.Severity switch
    {
        NotificationSeverity.Error => "✕",
        NotificationSeverity.Warning => "⚠",
        NotificationSeverity.Success => "✓",
        NotificationSeverity.Question => "?",
        _ => entry.Kind == NotificationKind.Status ? "●" : "ℹ",
    };

    private Color ColorOf(NotificationEntry entry) => entry.IsRunning ? _palette.Accent : entry.Severity switch
    {
        NotificationSeverity.Error => _palette.Error,
        NotificationSeverity.Warning => _palette.Warning,
        NotificationSeverity.Success => _palette.Success,
        _ => _palette.Info,
    };

    private static string When(NotificationEntry entry)
    {
        string time = entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return entry.Duration is { } took ? $"{time} · took {ActivityText.Duration(took)}" : time;
    }

    /// <summary>A source's header: its name, its count, and Dismiss for its ended entries.</summary>
    private sealed class GroupHeader
    {
        private readonly Label _name;
        private readonly Button _dismiss;

        public GroupHeader(NotificationCenterView owner, string source)
        {
            var palette = owner._palette;
            _name = palette.Label(source.ToUpperInvariant(), palette.Secondary, palette.CaptionSize, FontWeight.SemiBold);
            _dismiss = palette.Flat("Dismiss", palette.Secondary, 22, palette.CaptionSize);
            _dismiss.Clicked += (_, _) => owner._center.DismissAll(source);
            var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(4, 10, 0, 2) };
            grid.Add(_name, 0, 0);
            grid.Add(_dismiss, 1, 0);
            View = grid;
            Source = source;
        }

        public View View { get; }

        public string Source { get; }

        public void Show(int count, bool dismissable)
        {
            _name.Text = string.Create(CultureInfo.InvariantCulture, $"{Source.ToUpperInvariant()} · {count}");
            _dismiss.IsVisible = dismissable;
        }
    }

    /// <summary>One entry's row: its mark, title, text, bar, time and buttons, updated in place.</summary>
    private sealed class EntryRow
    {
        private readonly NotificationCenterView _owner;
        private readonly Label _mark;
        private readonly Label _title;
        private readonly Label _text;
        private readonly Label _when;
        private readonly ProgressBar _bar = new() { HeightRequest = 3, IsVisible = false };
        private readonly HorizontalStackLayout _buttons = new() { Spacing = 6 };
        private readonly Button _dismiss;
        private string _actionsKey = "";
        private NotificationEntry? _entry;

        public EntryRow(NotificationCenterView owner)
        {
            _owner = owner;
            var palette = owner._palette;
            _mark = palette.Label("", palette.Info, palette.FontSize);
            _mark.WidthRequest = 18;
            _mark.VerticalTextAlignment = TextAlignment.Start;
            _title = palette.Label("", palette.Text, palette.SmallSize, FontWeight.Medium);
            _text = palette.Label("", palette.Secondary, palette.CaptionSize);
            _text.LineBreakMode = LineBreakMode.WordWrap;
            _text.MaxLines = 4;
            _when = palette.Label("", palette.Secondary, palette.CaptionSize);
            _bar.ProgressColor = palette.Accent;
            _bar.BackgroundColor = palette.Border;
            _dismiss = palette.Flat("✕", palette.Secondary, 22, palette.CaptionSize);
            _dismiss.Clicked += (_, _) =>
            {
                if (_entry is { } entry)
                {
                    owner._center.Dismiss(entry.Id);
                }
            };
            var lines = new VerticalStackLayout { Spacing = 2, Children = { _title, _text, _bar, _when, _buttons } };
            var grid = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Padding = new Thickness(4, 6),
                ColumnSpacing = 6,
            };
            grid.Add(_mark, 0, 0);
            grid.Add(lines, 1, 0);
            grid.Add(_dismiss, 2, 0);
            View = palette.Box(grid, 6, new Thickness(2), palette.Surface, palette.Border);
        }

        public View View { get; }

        public void Show(NotificationEntry entry)
        {
            _entry = entry;
            _mark.Text = Mark(entry);
            _mark.TextColor = _owner.ColorOf(entry);
            Set(_title, entry.Title);
            Set(_text, entry.Text);
            _text.IsVisible = entry.Text.Length > 0;
            Set(_when, entry.IsRunning ? "running" : When(entry));
            _bar.IsVisible = entry is { IsRunning: true, Activity.Fraction: not null };
            _bar.Progress = entry.Activity?.Fraction ?? 0;
            _dismiss.IsVisible = !entry.IsRunning && entry.Kind != NotificationKind.Status;
            string key = string.Join("|", entry.Actions.Select(a => a.Label)) + (entry is { IsRunning: true, Activity.Cancellable: true } ? "|cancel" : "");
            if (key != _actionsKey)
            {
                _actionsKey = key;
                _buttons.Children.Clear();
                if (entry is { IsRunning: true, Activity.Cancellable: true })
                {
                    var cancel = _owner._palette.SecondaryButton(ActivityText.Cancel, 24);
                    long id = entry.Id;
                    cancel.Clicked += (_, _) => _owner._activities.Cancel(id);
                    _buttons.Children.Add(cancel);
                }

                foreach (var action in entry.Actions)
                {
                    var button = action.Primary ? _owner._palette.Primary(action.Label, 24) : _owner._palette.SecondaryButton(action.Label, 24);
                    var chosen = action;
                    button.Clicked += (_, _) =>
                    {
                        if (_entry is { } current)
                        {
                            _ = _owner._center.RunAsync(current.Id, chosen);
                        }
                    };
                    _buttons.Children.Add(button);
                }
            }

            _buttons.IsVisible = _buttons.Children.Count > 0;
        }

        private static void Set(Label label, string text)
        {
            if (!string.Equals(label.Text, text, StringComparison.Ordinal))
            {
                label.Text = text;
            }
        }
    }
}

/// <summary>
/// A toast (phase U): a short card over the editor's corner for what needs the user (a failure, a question), with its
/// buttons and "Show" (the centre); it goes after <c>CRAFT_TOAST_MS</c> and the entry stays in the centre. One at a
/// time: a newer one takes its place.
/// </summary>
public sealed class ToastView : ContentView
{
    private readonly NotificationCenter _center;
    private readonly Label _mark;
    private readonly Label _title;
    private readonly Label _text;
    private readonly HorizontalStackLayout _buttons = new() { Spacing = 6 };
    private readonly Border _frame;
    private ShellPalette _palette;
    private IDispatcherTimer? _timer;

    /// <summary>The toast of <paramref name="center"/>'s entries.</summary>
    public ToastView(NotificationCenter center, ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(palette);
        _center = center;
        _palette = palette;
        _mark = palette.Label("", palette.Error, palette.FontSize);
        _mark.VerticalTextAlignment = TextAlignment.Start;
        _title = palette.Label("", palette.Text, palette.SmallSize, FontWeight.SemiBold);
        _text = palette.Label("", palette.Secondary, palette.CaptionSize);
        _text.LineBreakMode = LineBreakMode.WordWrap;
        _text.MaxLines = 3;
        var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 8, Padding = new Thickness(10, 8) };
        grid.Add(_mark, 0, 0);
        grid.Add(new VerticalStackLayout { Spacing = 3, Children = { _title, _text, _buttons } }, 1, 0);
        _frame = new Border
        {
            Content = grid,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.35f, Radius = 14, Offset = new Point(0, 4) },
        };
        Content = _frame;
        WidthRequest = 380;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(0, 0, 10, 8);
        IsVisible = false;
        Apply(palette);
    }

    /// <summary>Raised when its Show is pressed (the page opens the centre).</summary>
    public event EventHandler? ShowCentreRequested;

    /// <summary>Takes <paramref name="palette"/>'s colours.</summary>
    public void Apply(ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _frame.BackgroundColor = palette.RaisedHigh;
        _frame.Stroke = palette.BorderStrong;
        _title.TextColor = palette.Text;
        _text.TextColor = palette.Secondary;
    }

    /// <summary>Shows <paramref name="entry"/> for <paramref name="milliseconds"/> (0: not at all).</summary>
    public void Show(NotificationEntry entry, int milliseconds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (milliseconds <= 0)
        {
            return;
        }

        _mark.Text = entry.Severity == NotificationSeverity.Question ? "?" : "✕";
        _mark.TextColor = entry.Severity == NotificationSeverity.Question ? _palette.Accent : _palette.Error;
        _title.Text = entry.Title;
        _text.Text = entry.Text;
        _buttons.Children.Clear();
        foreach (var action in entry.Actions)
        {
            var button = action.Primary ? _palette.Primary(action.Label, 24) : _palette.SecondaryButton(action.Label, 24);
            var chosen = action;
            button.Clicked += (_, _) =>
            {
                Hide();
                _ = _center.RunAsync(entry.Id, chosen);
            };
            _buttons.Children.Add(button);
        }

        var show = _palette.Flat("Show", _palette.Secondary, 24, _palette.CaptionSize);
        show.Clicked += (_, _) =>
        {
            Hide();
            ShowCentreRequested?.Invoke(this, EventArgs.Empty);
        };
        var close = _palette.Flat("✕", _palette.Secondary, 24, _palette.CaptionSize);
        close.Clicked += (_, _) => Hide();
        _buttons.Children.Add(show);
        _buttons.Children.Add(close);
        _ = ShellMotion.ShowAsync(this, 16);                 // phase X: slides up into the corner
        _timer?.Stop();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hide();
        _timer.Start();
    }

    /// <summary>Takes it away (the entry stays in the centre).</summary>
    public void Hide()
    {
        _timer?.Stop();
        _ = ShellMotion.HideAsync(this, 16);
    }
}
