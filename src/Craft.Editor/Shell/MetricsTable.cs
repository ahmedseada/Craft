// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Shell;

/// <summary>A column of the metrics table.</summary>
public enum MetricColumn
{
    /// <summary>Who recorded it.</summary>
    Source,

    /// <summary>The metric's name.</summary>
    Measure,

    /// <summary>Its last value.</summary>
    Last,

    /// <summary>The mean of every value.</summary>
    Mean,

    /// <summary>The 95th percentile of the recent values.</summary>
    P95,

    /// <summary>How many were recorded.</summary>
    Count,

    /// <summary>Its unit.</summary>
    Unit,
}

/// <summary>
/// One metric of the table, as the UI thread last took it (<see cref="MetricsTable.Refresh"/>): its values, its recent
/// values for the sparkline (oldest first), and its cells' texts, made once per change, never per frame.
/// </summary>
public sealed class MetricRow
{
    internal MetricRow(string source, string measure, int samples)
    {
        Source = source;
        Measure = measure;
        _samples = new double[samples];
    }

    /// <summary>Who recorded it (a plug-in's name, or "host").</summary>
    public string Source { get; }

    /// <summary>The metric ("ai.model.load").</summary>
    public string Measure { get; }

    /// <summary>Its last value.</summary>
    public double Last { get; private set; }

    /// <summary>The mean of every value recorded.</summary>
    public double Mean { get; private set; }

    /// <summary>The 95th percentile (nearest rank) of the recent values.</summary>
    public double P95 { get; private set; }

    /// <summary>How many were recorded.</summary>
    public long Count { get; private set; }

    /// <summary>Its unit.</summary>
    public string Unit { get; private set; } = "";

    /// <summary>Whether it is a stall (its name says so) or its last value is slow (in ms, at least <c>CRAFT_TELEMETRY_SLOW_MS</c>).</summary>
    public bool IsSlow { get; private set; }

    /// <summary>The recent values, oldest first (<see cref="SampleCount"/> of them).</summary>
    public ReadOnlySpan<double> Samples => _samples.AsSpan(0, SampleCount);

    /// <summary>How many of <see cref="Samples"/> hold values.</summary>
    public int SampleCount { get; private set; }

    /// <summary>The cells' texts, by <see cref="MetricColumn"/>.</summary>
    public string Text(MetricColumn column) => column switch
    {
        MetricColumn.Source => Source,
        MetricColumn.Measure => Measure,
        MetricColumn.Last => _last,
        MetricColumn.Mean => _mean,
        MetricColumn.P95 => _p95,
        MetricColumn.Count => _count,
        _ => Unit,
    };

    internal long Seen { get; set; } = -1;

    internal int Order { get; init; }

    private readonly double[] _samples;
    private string _last = "";
    private string _mean = "";
    private string _p95 = "";
    private string _count = "";

    internal void Take(double last, double mean, long count, string unit, bool slow, ReadOnlySpan<double> ring, int ringCount, int next, Span<double> scratch)
    {
        Last = last;
        Mean = mean;
        Count = count;
        Unit = unit;
        IsSlow = slow;
        // The ring oldest first.
        int n = Math.Min(ringCount, _samples.Length);
        int start = ringCount < ring.Length ? 0 : next;
        for (int i = 0; i < n; i++)
        {
            _samples[i] = ring[(start + i) % ring.Length];
        }

        SampleCount = n;
        var sorted = scratch[..n];
        _samples.AsSpan(0, n).CopyTo(sorted);
        sorted.Sort();
        P95 = n == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(0.95 * n) - 1, 0, n - 1)];
        _last = MetricsTable.Format(last);
        _mean = MetricsTable.Format(mean);
        _p95 = MetricsTable.Format(P95);
        _count = count.ToString("N0", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Idrak telemetry as a live metrics table (the owner's note 26: measurements, not a log): one row per source and metric
/// recorded through the host (<see cref="IEditorHost.Record"/>) with its last value, mean, p95 of the recent values,
/// count, unit and a sparkline of its last <c>CRAFT_TELEMETRY_SAMPLES</c>; stalls and slow values marked in the theme's
/// warning colour; sorted by a header's click (again: the other way), filtered by a text; rows picked by click, Shift+click
/// and Ctrl+click, moved by Up and Down; Ctrl+C copies the picked rows as a tab-separated table with its header line (it
/// pastes into a sheet), Copy all every row shown. Measures come from any thread (<see cref="Add"/>, a short lock per
/// value); the UI thread takes them once per frame (<see cref="Refresh"/>); memory grows with the metrics' names, never
/// with the values.
/// </summary>
public sealed class MetricsTable : PanelList, IDisposable
{
    /// <summary>The columns in order, with their header text and their width in cells (0: the measure takes the rest).</summary>
    public static IReadOnlyList<(MetricColumn Column, string Title, int Cells, bool Numeric)> Columns { get; } =
    [
        (MetricColumn.Source, "Source", 16, false), (MetricColumn.Measure, "Measure", 0, false), (MetricColumn.Last, "Last", 11, true),
        (MetricColumn.Mean, "Mean", 11, true), (MetricColumn.P95, "p95", 11, true), (MetricColumn.Count, "Count", 9, true), (MetricColumn.Unit, "Unit", 8, false),
    ];

    private const int TrendCells = 14;
    private readonly EditorHost? _host;
    private readonly int _samples;
    private readonly double _slow;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Source, string Metric), Stats> _stats = [];
    private readonly List<Stats> _order = [];
    private readonly List<MetricRow> _rows = [];
    private readonly List<MetricRow> _shown = [];
    private readonly double[] _scratch;
    private readonly string[] _headers;
    private int _dirty;
    private string _filter = "";

    /// <summary>A table of <paramref name="host"/>'s measures from now on (none: fed through <see cref="Add"/> alone), with the options' samples and slow mark.</summary>
    public MetricsTable(EditorHost? host, CraftOptions? options = null)
    {
        options ??= new CraftOptions();
        _samples = Math.Max(2, options.TelemetrySamples);
        _slow = options.TelemetrySlowMilliseconds;
        _scratch = new double[_samples];
        _headers = new string[Columns.Count * 3];
        for (int i = 0; i < Columns.Count; i++)
        {
            _headers[i * 3] = Columns[i].Title;
            _headers[(i * 3) + 1] = Columns[i].Title + " ▲";
            _headers[(i * 3) + 2] = Columns[i].Title + " ▼";
        }

        _host = host;
        if (host is not null)
        {
            host.Recorded += OnRecorded;
        }
    }

    /// <summary>The rows shown: filtered, then sorted.</summary>
    public IReadOnlyList<MetricRow> Rows => _shown;

    /// <summary>Every metric's row, in the order they first came.</summary>
    public IReadOnlyList<MetricRow> AllRows => _rows;

    /// <summary>The picked rows.</summary>
    public RowSelection<MetricRow> Selection { get; } = new();

    /// <summary>The column the rows are sorted by, or null for the order the metrics first came in.</summary>
    public MetricColumn? SortColumn { get; private set; }

    /// <summary>Whether the sort runs from the largest.</summary>
    public bool Descending { get; private set; }

    /// <inheritdoc />
    public override int RowCount => _shown.Count;

    /// <inheritdoc />
    public override bool HasHeader => true;

    /// <summary>The text a row's source or measure must hold (ignoring case); empty shows every row.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= "";
            if (!string.Equals(value, _filter, StringComparison.Ordinal))
            {
                _filter = value;
                Arrange();
            }
        }
    }

    /// <summary>
    /// Adds a measure (any thread). True when it is the first since the last <see cref="Refresh"/>: the caller then
    /// schedules one refresh on the UI thread; the values recorded until it runs join it.
    /// </summary>
    public bool Add(string source, string metric, double value, string unit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(metric);
        lock (_gate)
        {
            if (!_stats.TryGetValue((source, metric), out var stats))
            {
                stats = new Stats(source, metric, _samples);
                _stats[(source, metric)] = stats;
                _order.Add(stats);
            }

            stats.Add(value, unit ?? "");
        }

        return Interlocked.Exchange(ref _dirty, 1) == 0;
    }

    /// <summary>On the UI thread: takes the values recorded since the last refresh into the rows, sorts and filters them. True when anything changed.</summary>
    public bool Refresh()
    {
        Volatile.Write(ref _dirty, 0);
        bool changed = false;
        lock (_gate)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                var stats = _order[i];
                if (i == _rows.Count)
                {
                    _rows.Add(new MetricRow(stats.Source, stats.Metric, _samples) { Order = i });
                }

                var row = _rows[i];
                if (row.Seen == stats.Version)
                {
                    continue;
                }

                bool slow = stats.Metric.Contains("stall", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(stats.Unit, "ms", StringComparison.OrdinalIgnoreCase) && stats.Last >= _slow);
                row.Take(stats.Last, stats.Mean, stats.Count, stats.Unit, slow, stats.Ring, stats.RingCount, stats.Next, _scratch);
                row.Seen = stats.Version;
                changed = true;
            }
        }

        if (changed)
        {
            Arrange();
        }

        return changed;
    }

    /// <summary>Whether a value is slow by the table's mark (in ms, at least <c>CRAFT_TELEMETRY_SLOW_MS</c>).</summary>
    public bool IsSlowValue(MetricRow row, double value)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Equals(row.Unit, "ms", StringComparison.OrdinalIgnoreCase) && value >= _slow;
    }

    /// <summary>Sorts by <paramref name="column"/>: ascending first (the numbers from the largest), the other way when it is sorted by it already.</summary>
    public void SortBy(MetricColumn column)
    {
        if (SortColumn == column)
        {
            Descending = !Descending;
        }
        else
        {
            SortColumn = column;
            Descending = column is MetricColumn.Last or MetricColumn.Mean or MetricColumn.P95 or MetricColumn.Count;
        }

        Arrange();
    }

    /// <summary>The header and the picked rows, tab-separated, a line each (null when none is picked).</summary>
    public override string? CopySelected()
    {
        var picked = Selection.InOrder(_shown);
        return picked.Count == 0 ? null : Table(picked);
    }

    /// <summary>The header and every row shown, tab-separated.</summary>
    public override string CopyAll() => Table(_shown);

    /// <summary>A value as its cell shows it ("4,210", "12.5", "0.031").</summary>
    public static string Format(double value) =>
        Math.Abs(value) >= 1000 ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_host is not null)
        {
            _host.Recorded -= OnRecorded;
        }
    }

    /// <inheritdoc />
    public override void Press(FontMetrics metrics, float viewX, float viewY, float scrollY, float width, KeyModifiers modifiers)
    {
        int row = RowAt(metrics, viewY, scrollY);
        if (row == -1)
        {
            if (ColumnAt(metrics, viewX, width) is { } column)
            {
                SortBy(column);
            }

            return;
        }

        if (row >= 0)
        {
            Selection.Click(_shown, _shown[row], modifiers);
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public override bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        bool control = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        switch (key)
        {
            case EditorKey.Up or EditorKey.Down when !control:
            case EditorKey.Home or EditorKey.End when !control:
                int delta = key switch { EditorKey.Up => -1, EditorKey.Down => 1, EditorKey.Home => -_shown.Count, _ => _shown.Count };
                if (Selection.Move(_shown, delta, shift))
                {
                    RevealFocus();
                    RaiseChanged();
                }

                return true;
            case EditorKey.A when control:
                Selection.SelectAll(_shown);
                RaiseChanged();
                return true;
            case EditorKey.C when control:
                return Copy(CopySelected() ?? CopyAll());
            case EditorKey.Escape when Selection.Count > 0:
                Selection.Clear();
                RaiseChanged();
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public override void Draw(IEditorCanvas canvas, FontMetrics metrics, EditorColors colors, float scrollY, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(colors);
        float rowHeight = RowHeight(metrics);
        float cell = metrics.CellWidth;
        float inset = (rowHeight - metrics.LineHeight) / 2;
        var text = new TextStyle(colors.Text);
        var quiet = new TextStyle(colors.SecondaryText);
        var warning = new TextStyle(colors.Warning);
        canvas.FillRect(0, 0, width, height, colors.Surface);
        var (first, last) = Visible(metrics, scrollY, height);
        float header = HeaderHeight(metrics);
        for (int index = first; index <= last; index++)
        {
            var row = _shown[index];
            float top = header + (index * rowHeight) - scrollY;
            bool picked = Selection.Contains(row);
            uint fill = RowFill(colors, index == Hover, picked);
            if (fill != colors.Surface)
            {
                canvas.FillRect(0, top, width, rowHeight, fill);
            }

            if (row.IsSlow)
            {
                canvas.FillRect(0, top, 2, rowHeight, colors.Warning);
            }

            float x = cell;
            for (int c = 0; c < Columns.Count; c++)
            {
                var (column, _, _, numeric) = Columns[c];
                float w = ColumnWidth(c, cell, width);
                canvas.FillRect(x, top, w, rowHeight, fill);
                string value = row.Text(column);
                var style = column == MetricColumn.Last && row.IsSlow ? warning : column is MetricColumn.Source or MetricColumn.Unit ? quiet : text;
                float left = numeric ? x + w - ((value.Length + 1) * cell) : x;
                canvas.DrawText(value, Math.Max(x, left), top + inset, style);
                x += w;
            }

            canvas.FillRect(x, top, Math.Max(0, width - x), rowHeight, fill);
            DrawTrend(canvas, row, colors, x + cell, top + inset, (TrendCells - 2) * cell, metrics.LineHeight);
            if (Selection.Focus == row && picked)
            {
                canvas.DrawLine(0, top + rowHeight - 0.5f, width, top + rowHeight - 0.5f, colors.Accent, 1);
            }
        }

        // The header stays at the top, over the rows scrolled under it.
        canvas.FillRect(0, 0, width, header, colors.Surface);
        float hx = cell;
        for (int c = 0; c < Columns.Count; c++)
        {
            var (column, title, _, numeric) = Columns[c];
            float w = ColumnWidth(c, cell, width);
            canvas.FillRect(hx, 0, w, header, colors.Surface);
            string label = _headers[(c * 3) + (SortColumn == column ? (Descending ? 2 : 1) : 0)];
            float left = numeric ? hx + w - ((label.Length + 1) * cell) : hx;
            canvas.DrawText(label, Math.Max(hx, left), inset, SortColumn == column ? text : quiet);
            hx += w;
        }

        canvas.FillRect(hx, 0, Math.Max(0, width - hx), header, colors.Surface);
        canvas.DrawText("Trend", hx + cell, inset, quiet);
        canvas.DrawLine(0, header - 0.5f, width, header - 0.5f, colors.Border, 1);
    }

    /// <summary>The column whose header is at <paramref name="viewX"/>, or null (the trend's).</summary>
    public static MetricColumn? ColumnAt(FontMetrics metrics, float viewX, float width)
    {
        float cell = metrics.CellWidth;
        float x = cell;
        for (int c = 0; c < Columns.Count; c++)
        {
            float w = ColumnWidth(c, cell, width);
            if (viewX >= x && viewX < x + w)
            {
                return Columns[c].Column;
            }

            x += w;
        }

        return null;
    }

    // A column's width: its cells, the measure the rest (at least 12 cells).
    private static float ColumnWidth(int column, float cell, float width)
    {
        int cells = Columns[column].Cells;
        if (cells > 0)
        {
            return cells * cell;
        }

        int fixedCells = 2 + TrendCells;
        for (int c = 0; c < Columns.Count; c++)
        {
            fixedCells += Columns[c].Cells;
        }

        return Math.Max(12 * cell, width - (fixedCells * cell));
    }

    // The sparkline: the recent values scaled to the row, a slow value's segment in the warning colour. Nothing allocated.
    private void DrawTrend(IEditorCanvas canvas, MetricRow row, EditorColors colors, float left, float top, float width, float height)
    {
        int n = row.SampleCount;
        if (n < 2 || width <= 0)
        {
            return;
        }

        var samples = row.Samples;
        double low = samples[0];
        double high = samples[0];
        for (int i = 1; i < n; i++)
        {
            low = Math.Min(low, samples[i]);
            high = Math.Max(high, samples[i]);
        }

        double span = high - low;
        float step = width / (_samples - 1);
        float x0 = left + ((_samples - n) * step);
        float Y(double v) => top + height - 2 - (float)(span <= 0 ? (height - 4) / 2 : (v - low) / span * (height - 4));
        float px = x0;
        float py = Y(samples[0]);
        for (int i = 1; i < n; i++)
        {
            float x = x0 + (i * step);
            float y = Y(samples[i]);
            canvas.DrawLine(px, py, x, y, IsSlowValue(row, samples[i]) ? colors.Warning : colors.Accent, 1.25f);
            px = x;
            py = y;
        }
    }

    private void RevealFocus()
    {
        if (Selection.Focus is { } focus)
        {
            int at = _shown.IndexOf(focus);
            if (at >= 0)
            {
                Reveal(at);
            }
        }
    }

    private void Arrange()
    {
        _shown.Clear();
        foreach (var row in _rows)
        {
            if (_filter.Length == 0 || row.Source.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.Measure.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                _shown.Add(row);
            }
        }

        if (SortColumn is { } column)
        {
            int sign = Descending ? -1 : 1;
            // A stable sort: equal rows keep the order their metrics came in.
            _shown.Sort((a, b) =>
            {
                int compared = column switch
                {
                    MetricColumn.Source => string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase),
                    MetricColumn.Measure => string.Compare(a.Measure, b.Measure, StringComparison.OrdinalIgnoreCase),
                    MetricColumn.Last => a.Last.CompareTo(b.Last),
                    MetricColumn.Mean => a.Mean.CompareTo(b.Mean),
                    MetricColumn.P95 => a.P95.CompareTo(b.P95),
                    MetricColumn.Count => a.Count.CompareTo(b.Count),
                    _ => string.Compare(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase),
                };
                return compared != 0 ? sign * compared : a.Order.CompareTo(b.Order);
            });
        }

        Selection.Keep(_shown);
        RaiseChanged();
    }

    private static string Table(IReadOnlyList<MetricRow> rows)
    {
        var text = new StringBuilder();
        for (int c = 0; c < Columns.Count; c++)
        {
            text.Append(c == 0 ? "" : "\t").Append(Columns[c].Title);
        }

        foreach (var row in rows)
        {
            text.Append(Environment.NewLine).Append(Clean(row.Source)).Append('\t').Append(Clean(row.Measure)).Append('\t')
                .Append(row.Last.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Mean.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.P95.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Clean(row.Unit));
        }

        return text.ToString();

        static string Clean(string cell) => cell.AsSpan().ContainsAny('\t', '\r', '\n') ? cell.Replace('\t', ' ').ReplaceLineEndings(" ") : cell;
    }

    private void OnRecorded(object? sender, EditorMeasureEventArgs e)
    {
        if (Add(e.Source, e.Metric, e.Value, e.Unit))
        {
            Recorded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised (on the recording thread) when a measure came and no refresh is scheduled: the panel schedules one.</summary>
    public event EventHandler? Recorded;

    // A metric's values as they come (under the table's lock).
    private sealed class Stats(string source, string metric, int samples)
    {
        public string Source { get; } = source;

        public string Metric { get; } = metric;

        public double[] Ring { get; } = new double[samples];

        public int RingCount { get; private set; }

        public int Next { get; private set; }

        public double Last { get; private set; }

        public double Mean { get; private set; }

        public long Count { get; private set; }

        public string Unit { get; private set; } = "";

        public long Version { get; private set; }

        public void Add(double value, string unit)
        {
            Count++;
            Last = value;
            Mean += (value - Mean) / Count;
            Unit = unit;
            Ring[Next] = value;
            Next = (Next + 1) % Ring.Length;
            RingCount = Math.Min(RingCount + 1, Ring.Length);
            Version++;
        }
    }
}
