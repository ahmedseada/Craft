// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// What drawing a view panel (<see cref="IEditorViewPanel"/>, B11) decides headless: which top-level blocks changed since
/// they were drawn (only those are drawn again: a training's progress redraws its card, not the panel), where a small
/// chart's points fall, and how a meter's fraction is held within its bar.
/// </summary>
public static class PanelBlockLayout
{
    /// <summary>A block's signature: what it says (<see cref="PanelBlocks.Describe"/>) and every tooltip within it; equal signatures draw the same.</summary>
    public static string Signature(PanelBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var text = new StringBuilder(PanelBlocks.Describe(block));
        foreach (var one in PanelBlocks.All([block]))
        {
            if (one.Tooltip is { } tip)
            {
                text.Append('\u0001').Append(tip);
            }

            switch (one)
            {
                case PanelButtons buttons:
                    foreach (var button in buttons.Buttons)
                    {
                        text.Append('\u0002').Append(button.Tooltip).Append(button.Value);
                    }

                    break;
                case PanelChecklist list:
                    foreach (var item in list.Items)
                    {
                        text.Append('\u0002').Append(item.Action?.Tooltip).Append(item.Action?.Value).Append(item.Action?.ActionId);
                    }

                    break;
                case PanelPicker picker:
                    foreach (var option in picker.Options)
                    {
                        text.Append('\u0003').Append(option.Value).Append('=').Append(option.Detail);
                    }

                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The indexes of <paramref name="after"/> to draw again, given the signatures drawn (<paramref name="before"/>): each
    /// index whose signature differs, and every index past the shorter list.
    /// </summary>
    public static IReadOnlyList<int> Changed(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changed = new List<int>();
        for (int i = 0; i < after.Count; i++)
        {
            if (i >= before.Count || !string.Equals(before[i], after[i], StringComparison.Ordinal))
            {
                changed.Add(i);
            }
        }

        return changed;
    }

    /// <summary>
    /// Where a chart's points fall in a box of <paramref name="width"/> by <paramref name="height"/> with
    /// <paramref name="inset"/> kept free on every side: evenly across, the lowest value at the bottom and the highest at
    /// the top; one value, or values all equal, at mid-height; a value that is not finite is left out (NaN).
    /// </summary>
    public static (float X, float Y)[] ChartPoints(IReadOnlyList<double> values, float width, float height, float inset = 3)
    {
        ArgumentNullException.ThrowIfNull(values);
        var points = new (float X, float Y)[values.Count];
        if (values.Count == 0)
        {
            return points;
        }

        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        }

        float left = inset, right = Math.Max(inset, width - inset), top = inset, bottom = Math.Max(inset, height - inset);
        double span = high - low;
        for (int i = 0; i < values.Count; i++)
        {
            float x = values.Count == 1 ? (left + right) / 2 : left + ((right - left) * i / (values.Count - 1));
            double value = values[i];
            float y = !double.IsFinite(value) ? float.NaN
                : !(span > 0) ? (top + bottom) / 2
                : (float)(bottom - ((value - low) / span * (bottom - top)));
            points[i] = (x, y);
        }

        return points;
    }

    /// <summary>
    /// The width a query field keeps for its text before its toggles and buttons move to the row under it (the owner's
    /// Windows run: toggles drawn inside a narrow box clipped the typed text).
    /// </summary>
    public const double QueryTextMinimum = 140;

    /// <summary>
    /// Whether a query field's toggles and buttons (<paramref name="controls"/> wide, spacing included) fit beside its box in
    /// <paramref name="width"/> while the box keeps <paramref name="textMinimum"/> for the text; else they go on the row
    /// under the box, so the row fits the side view's measured width at any width. A width not known yet (0 or less) is
    /// laid out on one row until it is measured; a field with no controls is always one row.
    /// </summary>
    public static bool QueryOnOneRow(double width, double controls, double textMinimum = QueryTextMinimum) =>
        controls <= 0 || width <= 0 || width - controls >= textMinimum;

    /// <summary>
    /// Items of <paramref name="sizes"/> placed left to right in rows no wider than <paramref name="width"/> (a wrapping
    /// row of buttons): <paramref name="gap"/> between items, <paramref name="rowGap"/> between rows, a new row when the next
    /// item does not fit (an item wider than the width alone on its row), each row as tall as its tallest item with its items
    /// centred in it. Every item's place, the widest row and the height of all the rows: the block takes this height, so
    /// nothing under it is drawn over it. An infinite width keeps one row.
    /// </summary>
    public static (PanelPlace[] Places, double Width, double Height) Wrap(ReadOnlySpan<(double Width, double Height)> sizes, double width, double gap = 8, double rowGap = 6)
    {
        var places = new PanelPlace[sizes.Length];
        double x = 0, y = 0, rowHeight = 0, widest = 0;
        int rowStart = 0;
        for (int i = 0; i < sizes.Length; i++)
        {
            var (w, h) = (Math.Max(0, sizes[i].Width), Math.Max(0, sizes[i].Height));
            if (i > rowStart && x + gap + w > width)
            {
                Center(places, rowStart, i, rowHeight);
                y += rowHeight + rowGap;
                x = 0;
                rowHeight = 0;
                rowStart = i;
            }

            double left = i == rowStart ? 0 : x + gap;
            places[i] = new PanelPlace(left, y, w, h);
            x = left + w;
            rowHeight = Math.Max(rowHeight, h);
            widest = Math.Max(widest, x);
        }

        if (sizes.Length == 0)
        {
            return (places, 0, 0);
        }

        Center(places, rowStart, sizes.Length, rowHeight);
        return (places, widest, y + rowHeight);

        static void Center(PanelPlace[] places, int start, int end, double height)
        {
            for (int j = start; j < end; j++)
            {
                places[j] = places[j] with { Y = places[j].Y + ((height - places[j].Height) / 2) };
            }
        }
    }

    /// <summary>A meter's filled width in a bar of <paramref name="width"/>: the fraction held from 0 to 1 (a fraction below 0 is work without a known end: a third of the bar).</summary>
    public static float MeterWidth(double fraction, float width) =>
        fraction < 0 ? width / 3 : (float)(Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * Math.Max(0, width));

    /// <summary>
    /// A results block's lines as the host draws them (X part 6): each group's header, then its rows unless it is folded;
    /// each line with a signature of what it shows, so the host replaces only the lines that differ (a new batch of a
    /// search appends its files, a fold removes one group's rows).
    /// </summary>
    public static IReadOnlyList<PanelResultLine> ResultLines(PanelResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var lines = new List<PanelResultLine>();
        foreach (var group in results.Groups)
        {
            var header = new StringBuilder("G").Append(group.Title).Append('\u0001').Append(group.Detail).Append('\u0001').Append(group.Badge)
                .Append('\u0001').Append(group.Glyph).Append(group.Collapsed ? "+" : "-").Append(group.Tooltip);
            foreach (var button in group.Buttons)
            {
                header.Append('\u0002').Append(button.Title).Append(button.Enabled ? "" : "!");
            }

            lines.Add(new PanelResultLine("g:" + group.Id, group, null, header.ToString()));
            if (group.Collapsed)
            {
                continue;
            }

            foreach (var row in group.Rows)
            {
                var text = new StringBuilder("R").Append(row.Text).Append('\u0001').Append(row.Prefix).Append('\u0001').Append(row.Detail).Append('\u0001')
                    .Append(row.Badge).Append('\u0001').Append(row.Tone).Append(row.Value).Append(row.Icon is null ? "" : "i")
                    .Append('\u0001').Append(row.Depth).Append(row.Expanded switch { true => "+", false => "-", _ => "" }).Append(row.Selected ? "*" : "");
                foreach (var span in row.Highlights)
                {
                    text.Append('\u0002').Append(span.Start).Append(',').Append(span.Length).Append(span.Replacement is null ? "" : "=" + span.Replacement);
                }

                foreach (var button in row.Buttons)
                {
                    text.Append('\u0003').Append(button.Title).Append(button.Enabled ? "" : "!");
                }

                lines.Add(new PanelResultLine("r:" + group.Id + "\u0001" + row.Id, group, row, text.ToString()));
            }
        }

        return lines;
    }

    /// <summary>
    /// A row's text cut at its highlights, in order: plain parts, each match, and, when a replacement is shown, the match
    /// struck through followed by its replacement. A highlight outside the text is left out.
    /// </summary>
    public static IReadOnlyList<(string Text, PanelSegmentKind Kind)> Segments(string text, IReadOnlyList<PanelSpan> highlights)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(highlights);
        var segments = new List<(string, PanelSegmentKind)>();
        int at = 0;
        foreach (var span in highlights.OrderBy(h => h.Start))
        {
            if (span.Start < at || span.Length <= 0 || span.Start + span.Length > text.Length)
            {
                continue;
            }

            if (span.Start > at)
            {
                segments.Add((text[at..span.Start], PanelSegmentKind.Plain));
            }

            var matched = text.Substring(span.Start, span.Length);
            if (span.Replacement is { } replacement)
            {
                segments.Add((matched, PanelSegmentKind.Removed));
                if (replacement.Length > 0)
                {
                    segments.Add((replacement, PanelSegmentKind.Inserted));
                }
            }
            else
            {
                segments.Add((matched, PanelSegmentKind.Match));
            }

            at = span.Start + span.Length;
        }

        if (at < text.Length)
        {
            segments.Add((text[at..], PanelSegmentKind.Plain));
        }

        return segments;
    }
}

/// <summary>Where an item of a wrapping row is placed (<see cref="PanelBlockLayout.Wrap"/>): its left, its top and its size.</summary>
/// <param name="X">Its left.</param>
/// <param name="Y">Its top.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
public readonly record struct PanelPlace(double X, double Y, double Width, double Height);

/// <summary>How a part of a result row's text is drawn.</summary>
public enum PanelSegmentKind
{
    /// <summary>As the row's text.</summary>
    Plain,

    /// <summary>A match: the highlight's fill.</summary>
    Match,

    /// <summary>A match a replace takes out: struck through in the error's colour.</summary>
    Removed,

    /// <summary>The replacement put in: in the success colour.</summary>
    Inserted,
}

/// <summary>A line of a drawn results block: a group's header (<see cref="Row"/> null) or one of its rows, and its signature.</summary>
/// <param name="Key">Its key, unique in the block.</param>
/// <param name="Group">Its group.</param>
/// <param name="Row">Its row, or null for the group's header.</param>
/// <param name="Signature">What it shows, as text: two lines with the same key and signature draw the same.</param>
public sealed record PanelResultLine(string Key, PanelResultGroup Group, PanelResultRow? Row, string Signature)
{
    /// <summary>Whether it is a group's header.</summary>
    public bool IsHeader => Row is null;

    /// <inheritdoc />
    public bool Equals(PanelResultLine? other) => other is not null && Key == other.Key && Signature == other.Signature;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Key, Signature);
}
