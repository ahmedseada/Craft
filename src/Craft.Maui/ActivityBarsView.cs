// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Activities;
using Microsoft.Maui.Controls;

namespace Craft.Maui;

/// <summary>
/// The running activities of some kinds as rows, each its title, its progress text (percent, rate, time left) and Craft's
/// progress bar, with Cancel: the Models page's downloads and loads. Rows are kept per activity and updated in place on
/// <see cref="ActivityCenter.Changed"/> (at most once a frame); an ended one goes. Hidden while none runs.
/// </summary>
public sealed class ActivityBarsView : VerticalStackLayout, IDisposable
{
    private readonly ActivityCenter _activities;
    private readonly string[] _kinds;
    private readonly Dictionary<long, Row> _rows = [];
    private ShellPalette _palette;

    /// <summary>The rows of <paramref name="activities"/>' running ones of <paramref name="kinds"/>, in <paramref name="palette"/>.</summary>
    public ActivityBarsView(ActivityCenter activities, ShellPalette palette, params string[] kinds)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(palette);
        _activities = activities;
        _palette = palette;
        _kinds = kinds ?? [];
        Spacing = 10;
        IsVisible = false;
        activities.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Takes <paramref name="palette"/>'s colours (the rows are made again).</summary>
    public void Apply(ShellPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _rows.Clear();
        Children.Clear();
        Refresh();
    }

    /// <summary>Shows the running ones as they are now.</summary>
    public void Refresh()
    {
        var seen = new HashSet<long>();
        foreach (var activity in _activities.Running)
        {
            if (Array.FindIndex(_kinds, k => string.Equals(k, activity.Kind, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                continue;
            }

            seen.Add(activity.Id);
            if (!_rows.TryGetValue(activity.Id, out var row))
            {
                row = new Row(this, activity.Id);
                _rows[activity.Id] = row;
                Children.Add(row.View);
            }

            row.Show(activity);
        }

        foreach (var gone in _rows.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Children.Remove(_rows[gone].View);
            _rows.Remove(gone);
        }

        IsVisible = _rows.Count > 0;
    }

    /// <inheritdoc />
    public void Dispose() => _activities.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    // One activity: its title and progress on a line, Cancel at its right, the bar under them.
    private sealed class Row
    {
        private readonly Label _title;
        private readonly Label _progress;
        private readonly ProgressBarView _bar;
        private readonly Button _cancel;

        public Row(ActivityBarsView owner, long id)
        {
            var p = owner._palette;
            _title = p.Label("", p.Text, p.SmallSize);
            _progress = p.Label("", p.Secondary, p.CaptionSize);
            _cancel = p.Flat(ActivityText.Cancel, p.Secondary, 24, p.CaptionSize);
            _cancel.Clicked += (_, _) => owner._activities.Cancel(id);
            _bar = new ProgressBarView(5, p.Options.LoaderCycleMilliseconds)
            {
                Fill = p.Accent,
                Track = p.RaisedHigh,
                ReducedMotion = p.Options.ReducedMotion || Craft.Editor.Motion.Duration(p.Options) == 0,
            };
            var line = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            var text = new VerticalStackLayout { Spacing = 1, Children = { _title, _progress } };
            line.Add(text, 0, 0);
            line.Add(_cancel, 1, 0);
            View = new VerticalStackLayout { Spacing = 6, Children = { line, _bar } };
        }

        public View View { get; }

        public void Show(ActivitySnapshot activity)
        {
            Set(_title, activity.Title);
            Set(_progress, ActivityText.Progress(activity));
            _cancel.IsVisible = activity.Cancellable;
            _bar.Fraction = activity.Fraction;
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
