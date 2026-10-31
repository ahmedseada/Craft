// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Editor;

namespace Craft.App;

/// <summary>
/// The problems list under the editor (phase E5): a header that collapses it, and one row per diagnostic of the open
/// document ("line:column  id  message", errors first by position), up to the options' limit; a tap on a row moves the
/// caret there. Plain labels in a scroll view, no templates: rows are rebuilt when the diagnostics change.
/// </summary>
internal sealed class ProblemsPanel
{
    private readonly int _limit;
    private readonly Action<int> _chosen;
    private readonly Button _header = new() { Text = "Problems", HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(8, 2) };
    private readonly VerticalStackLayout _rows = new() { Padding = new Thickness(8, 0) };
    private readonly ScrollView _scroll;
    private Color _text = Colors.Gray;
    private Color _error = Colors.Red;
    private Color _warning = Colors.Orange;
    private Color _info = Colors.SteelBlue;

    public ProblemsPanel(int limit, Action<int> chosen)
    {
        _limit = limit;
        _chosen = chosen;
        _scroll = new ScrollView { Content = _rows, HeightRequest = 140, IsVisible = false };
        _header.Clicked += (_, _) => Toggle();
        View = new VerticalStackLayout { Children = { _header, _scroll } };
    }

    /// <summary>The panel's view.</summary>
    public View View { get; }

    /// <summary>Opens or collapses the list.</summary>
    public void Toggle() => _scroll.IsVisible = !_scroll.IsVisible;

    /// <summary>Takes the theme's colours.</summary>
    public void ApplyTheme(ITheme theme)
    {
        var colors = theme.Colors;
        _text = ColorOf(colors.Foreground);
        _error = ColorOf(colors.Error);
        _warning = ColorOf(colors.Warning);
        _info = ColorOf(colors.Info);
        View.BackgroundColor = ColorOf(colors.Gutter);
        _header.TextColor = _text;
    }

    /// <summary>Empties the list (no document shown).</summary>
    public void Clear()
    {
        _rows.Children.Clear();
        _header.Text = "Problems";
    }

    /// <summary>Lists <paramref name="model"/>'s diagnostics (hidden ones left out).</summary>
    public void Show(EditorModel model)
    {
        _rows.Children.Clear();
        var snapshot = model.Snapshot;
        var shown = new List<EditorDiagnostic>();
        foreach (var diagnostic in model.Diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Hidden)
            {
                shown.Add(diagnostic);
            }
        }

        shown.Sort(static (a, b) => a.Severity != b.Severity ? b.Severity.CompareTo(a.Severity) : a.Start.CompareTo(b.Start));
        _header.Text = string.Create(CultureInfo.InvariantCulture, $"Problems ({shown.Count})");
        foreach (var diagnostic in shown.Take(_limit))
        {
            int start = Math.Clamp(diagnostic.Start, 0, snapshot.Length);
            int line = snapshot.GetLineIndex(start);
            int column = start - snapshot.GetLine(line).Start;
            var row = new Label
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{line + 1}:{column + 1}  {diagnostic.Id}  {diagnostic.Message}"),
                FontSize = 12,
                TextColor = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => _error,
                    DiagnosticSeverity.Warning => _warning,
                    _ => _info,
                },
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _chosen(start);
            row.GestureRecognizers.Add(tap);
            _rows.Children.Add(row);
        }

        if (shown.Count > _limit)
        {
            _rows.Children.Add(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"… and {shown.Count - _limit} more (CRAFT_PROBLEMS_LIMIT)"), FontSize = 12, TextColor = _text });
        }
    }

    private static Color ColorOf(uint argb) => Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
