// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Craft.Abstractions;

/// <summary>What a line of a diff is.</summary>
public enum DiffLineKind
{
    /// <summary>The same before and after (shown around a change).</summary>
    Context,

    /// <summary>Only before: the change removes it.</summary>
    Removed,

    /// <summary>Only after: the change adds it.</summary>
    Added,
}

/// <summary>A line of a diff, without its line break.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="OldLine">Its line number before the change (from 1), or 0 for an added line.</param>
/// <param name="NewLine">Its line number after the change (from 1), or 0 for a removed line.</param>
/// <param name="Text">Its text.</param>
public readonly record struct DiffLine(DiffLineKind Kind, int OldLine, int NewLine, string Text);

/// <summary>A run of changed lines with the unchanged lines around it.</summary>
/// <param name="OldStart">The first line it covers before the change (from 1; the line before it when it covers none).</param>
/// <param name="OldCount">The lines it covers before the change.</param>
/// <param name="NewStart">The first line it covers after the change (from 1; the line before it when it covers none).</param>
/// <param name="NewCount">The lines it covers after the change.</param>
/// <param name="Lines">Its lines, in order.</param>
public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines);

/// <summary>
/// A line diff: what a proposed change does to a text, as hunks of removed and added lines with unchanged lines around
/// them. The diff-and-accept view shows it (<see cref="OfEdits"/>), and a host that cannot ask the user writes it as a
/// patch (<see cref="ToUnified"/>). The lines are aligned by a shortest edit script (Myers) between the common start and
/// end; a changed middle longer than the line limit is shown whole, removed then added (<see cref="Coarse"/>), so the
/// work stays bounded. Used on any thread.
/// </summary>
public sealed class TextDiff
{
    private TextDiff(IReadOnlyList<DiffHunk> hunks, int removed, int added, bool coarse, bool oldMissingNewline, bool newMissingNewline, int oldLines, int newLines)
    {
        Hunks = hunks;
        Removed = removed;
        Added = added;
        Coarse = coarse;
        OldMissingNewline = oldMissingNewline;
        NewMissingNewline = newMissingNewline;
        OldLineCount = oldLines;
        NewLineCount = newLines;
    }

    /// <summary>No change.</summary>
    public static TextDiff Empty { get; } = new([], 0, 0, false, false, false, 0, 0);

    /// <summary>The hunks, in order.</summary>
    public IReadOnlyList<DiffHunk> Hunks { get; }

    /// <summary>The lines removed.</summary>
    public int Removed { get; }

    /// <summary>The lines added.</summary>
    public int Added { get; }

    /// <summary>Whether a changed run was longer than the line limit and is shown removed then added, not aligned line by line.</summary>
    public bool Coarse { get; }

    /// <summary>Whether the old text's last line has no line break (a patch then marks it); false for <see cref="OfEdits"/>.</summary>
    public bool OldMissingNewline { get; }

    /// <summary>Whether the new text's last line has no line break; false for <see cref="OfEdits"/>.</summary>
    public bool NewMissingNewline { get; }

    /// <summary>The old text's lines (for <see cref="OfTexts"/>; 0 for <see cref="OfEdits"/>).</summary>
    public int OldLineCount { get; }

    /// <summary>The new text's lines (for <see cref="OfTexts"/>; 0 for <see cref="OfEdits"/>).</summary>
    public int NewLineCount { get; }

    /// <summary>Whether it changes nothing.</summary>
    public bool IsEmpty => Hunks.Count == 0;

    /// <summary>
    /// The diff from <paramref name="oldText"/> to <paramref name="newText"/>, with <paramref name="contextLines"/>
    /// unchanged lines around each change and changed runs aligned up to <paramref name="lineLimit"/> lines.
    /// </summary>
    public static TextDiff OfTexts(string oldText, string newText, int contextLines = 3, int lineLimit = 2000)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);
        ArgumentOutOfRangeException.ThrowIfNegative(lineLimit);
        var oldLines = SplitLines(oldText, out bool oldBreak);
        var newLines = SplitLines(newText, out bool newBreak);
        var hunks = Compute(oldLines, newLines, 1, 1, contextLines, lineLimit, out int removed, out int added, out bool coarse);
        return hunks.Count == 0
            ? Empty
            : new TextDiff(hunks, removed, added, coarse, oldLines.Length > 0 && !oldBreak, newLines.Length > 0 && !newBreak, oldLines.Length, newLines.Length);
    }

    /// <summary>
    /// The diff <paramref name="edits"/> (in the positions of <paramref name="snapshot"/>, in order, not overlapping) make:
    /// only the lines they touch and <paramref name="contextLines"/> around them are read, never the whole text.
    /// </summary>
    /// <exception cref="ArgumentException">An edit runs outside the text, or the edits overlap or are out of order.</exception>
    public static TextDiff OfEdits(ITextSnapshot snapshot, IReadOnlyList<TextEdit> edits, int contextLines = 3, int lineLimit = 2000)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);
        ArgumentOutOfRangeException.ThrowIfNegative(lineLimit);
        if (edits.Count == 0)
        {
            return Empty;
        }

        int previousEnd = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < previousEnd || edit.Length < 0 || edit.End > snapshot.Length || edit.Text is null)
            {
                throw new ArgumentException($"The edit at {edit.Start} (length {edit.Length}) runs outside the text, or overlaps or comes before the one ahead of it.", nameof(edits));
            }

            previousEnd = edit.End;
        }

        int first = Math.Max(0, snapshot.GetLineIndex(edits[0].Start) - contextLines);
        int last = Math.Min(snapshot.LineCount - 1, snapshot.GetLineIndex(edits[^1].End) + contextLines);
        int regionStart = snapshot.GetLine(first).Start;
        int regionEnd = snapshot.GetLine(last).EndIncludingBreak;
        string before = snapshot.GetText(regionStart, regionEnd - regionStart);
        var after = new StringBuilder(before.Length + 64);
        int at = 0;
        foreach (var edit in edits)
        {
            int start = edit.Start - regionStart;
            after.Append(before.AsSpan(at, start - at)).Append(edit.Text);
            at = start + edit.Length;
        }

        after.Append(before.AsSpan(at));
        var hunks = Compute(SplitLines(before, out _), SplitLines(after.ToString(), out _), first + 1, first + 1, contextLines, lineLimit,
            out int removed, out int added, out bool coarse);
        return hunks.Count == 0 ? Empty : new TextDiff(hunks, removed, added, coarse, false, false, 0, 0);
    }

    /// <summary>
    /// The diff as a unified patch (<c>git apply</c> and <c>patch -p1</c> read it): the file names after <c>--- </c> and
    /// <c>+++ </c>, then each hunk; a last line without a line break is marked. Lines end with "\n".
    /// </summary>
    public string ToUnified(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("--- ").Append(oldPath).Append('\n').Append("+++ ").Append(newPath).Append('\n');
        foreach (var hunk in Hunks)
        {
            text.Append(culture, $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@\n");
            foreach (var line in hunk.Lines)
            {
                text.Append(line.Kind switch { DiffLineKind.Removed => '-', DiffLineKind.Added => '+', _ => ' ' }).Append(line.Text).Append('\n');
                bool oldLast = line.Kind != DiffLineKind.Added && OldMissingNewline && line.OldLine == OldLineCount;
                bool newLast = line.Kind != DiffLineKind.Removed && NewMissingNewline && line.NewLine == NewLineCount;
                if (oldLast || newLast)
                {
                    text.Append("\\ No newline at end of file\n");
                }
            }
        }

        return text.ToString();
    }

    /// <summary>The lines of <paramref name="text"/> without their breaks ("\r\n", "\n" or "\r"); whether the last one has one.</summary>
    internal static string[] SplitLines(string text, out bool endsWithBreak)
    {
        endsWithBreak = text.Length > 0 && text[^1] is '\n' or '\r';
        if (text.Length == 0)
        {
            return [];
        }

        var lines = new List<string>();
        var span = text.AsSpan();
        int start = 0;
        while (start < span.Length)
        {
            int index = span[start..].IndexOfAny('\r', '\n');
            if (index < 0)
            {
                lines.Add(text[start..]);
                break;
            }

            lines.Add(text.Substring(start, index));
            start += index + (span[start + index] == '\r' && start + index + 1 < span.Length && span[start + index + 1] == '\n' ? 2 : 1);
        }

        return [.. lines];
    }

    private static List<DiffHunk> Compute(string[] a, string[] b, int oldFirst, int newFirst, int context, int lineLimit, out int removed, out int added, out bool coarse)
    {
        var script = Script(a, b, lineLimit, out coarse);
        removed = 0;
        added = 0;
        var hunks = new List<DiffHunk>();
        var ops = CollectionsMarshal.AsSpan(script);
        int i = 0, previousEnd = 0;
        while (i < ops.Length)
        {
            if (ops[i].Kind == DiffLineKind.Context)
            {
                i++;
                continue;
            }

            // A hunk: from the change's context before it to the last change whose gap to the next stays within 2 x context.
            int begin = Math.Max(previousEnd, i - context);

            int end = i;
            int lastChange = i;
            while (end < ops.Length)
            {
                if (ops[end].Kind != DiffLineKind.Context)
                {
                    lastChange = end;
                }
                else if (end - lastChange > 2 * context)
                {
                    break;
                }

                end++;
            }

            end = Math.Min(ops.Length, lastChange + 1 + context);
            var lines = new DiffLine[end - begin];
            int oldCount = 0, newCount = 0;
            for (int j = begin; j < end; j++)
            {
                var op = ops[j];
                int oldLine = op.Kind == DiffLineKind.Added ? 0 : oldFirst + op.Old;
                int newLine = op.Kind == DiffLineKind.Removed ? 0 : newFirst + op.New;
                lines[j - begin] = new DiffLine(op.Kind, oldLine, newLine, op.Kind == DiffLineKind.Added ? b[op.New] : a[op.Old]);
                oldCount += op.Kind == DiffLineKind.Added ? 0 : 1;
                newCount += op.Kind == DiffLineKind.Removed ? 0 : 1;
                removed += op.Kind == DiffLineKind.Removed ? 1 : 0;
                added += op.Kind == DiffLineKind.Added ? 1 : 0;
            }

            // A side with no line starts at the line before the hunk (unified diff's rule).
            int oldStart = oldCount > 0 ? oldFirst + ops[begin..end].FirstOld() : oldFirst + ops[begin].Old - 1;
            int newStart = newCount > 0 ? newFirst + ops[begin..end].FirstNew() : newFirst + ops[begin].New - 1;
            hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, lines));
            i = end;
            previousEnd = end;
        }

        return hunks;
    }

    /// <summary>One step of an edit script: a line kept (Old, New), removed (Old) or added (New), as indexes into the lines.</summary>
    internal readonly record struct Op(DiffLineKind Kind, int Old, int New);

    private static List<Op> Script(string[] a, string[] b, int lineLimit, out bool coarse)
    {
        coarse = false;
        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && string.Equals(a[^(suffix + 1)], b[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        int n = a.Length - prefix - suffix, m = b.Length - prefix - suffix;
        var ops = new List<Op>(a.Length + m);
        for (int i = 0; i < prefix; i++)
        {
            ops.Add(new Op(DiffLineKind.Context, i, i));
        }

        if (n + m > lineLimit)
        {
            coarse = true;
            for (int i = 0; i < n; i++)
            {
                ops.Add(new Op(DiffLineKind.Removed, prefix + i, prefix));
            }

            for (int j = 0; j < m; j++)
            {
                ops.Add(new Op(DiffLineKind.Added, prefix + n, prefix + j));
            }
        }
        else if (n + m > 0)
        {
            Myers(a, b, prefix, n, m, ops);
        }

        for (int i = 0; i < suffix; i++)
        {
            ops.Add(new Op(DiffLineKind.Context, prefix + n + i, prefix + m + i));
        }

        return ops;
    }

    // Myers' greedy shortest edit script over the middle lines (as ids, so a comparison is one int), the furthest-reaching
    // x of each diagonal kept per step only for the diagonals that step could reach (2d + 1 values), then walked back.
    private static void Myers(string[] a, string[] b, int offset, int n, int m, List<Op> ops)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var x1 = new int[n];
        var y1 = new int[m];
        for (int i = 0; i < n; i++)
        {
            x1[i] = Id(ids, a[offset + i]);
        }

        for (int j = 0; j < m; j++)
        {
            y1[j] = Id(ids, b[offset + j]);
        }

        int max = n + m;
        var v = new int[(2 * max) + 3];
        int zero = max + 1;
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= max && found < 0; d++)
        {
            trace.Add(v.AsSpan(zero - d, (2 * d) + 1).ToArray());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[zero + k - 1] < v[zero + k + 1]) ? v[zero + k + 1] : v[zero + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && x1[x] == y1[y])
                {
                    x++;
                    y++;
                }

                v[zero + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                    break;
                }
            }
        }

        var reversed = new List<Op>(n + m);
        int cx = n, cy = m;
        for (int d = found; d > 0; d--)
        {
            var before = trace[d];
            int k = cx - cy;
            int previousK = k == -d || (k != d && before[k - 1 + d] < before[k + 1 + d]) ? k + 1 : k - 1;
            int px = before[previousK + d];
            int py = px - previousK;
            while (cx > px && cy > py)
            {
                cx--;
                cy--;
                reversed.Add(new Op(DiffLineKind.Context, offset + cx, offset + cy));
            }

            if (cx == px)
            {
                cy--;
                reversed.Add(new Op(DiffLineKind.Added, offset + cx, offset + cy));
            }
            else
            {
                cx--;
                reversed.Add(new Op(DiffLineKind.Removed, offset + cx, offset + cy));
            }
        }

        while (cx > 0 && cy > 0)
        {
            cx--;
            cy--;
            reversed.Add(new Op(DiffLineKind.Context, offset + cx, offset + cy));
        }

        for (int i = reversed.Count - 1; i >= 0; i--)
        {
            ops.Add(reversed[i]);
        }

        static int Id(Dictionary<string, int> ids, string line)
        {
            ref int id = ref CollectionsMarshal.GetValueRefOrAddDefault(ids, line, out bool exists);
            if (!exists)
            {
                id = ids.Count;
            }

            return id;
        }
    }
}

/// <summary>The first old and new line indexes of a run of edit-script steps.</summary>
internal static class DiffOps
{
    public static int FirstOld(this Span<TextDiff.Op> ops)
    {
        foreach (var op in ops)
        {
            if (op.Kind != DiffLineKind.Added)
            {
                return op.Old;
            }
        }

        return 0;
    }

    public static int FirstNew(this Span<TextDiff.Op> ops)
    {
        foreach (var op in ops)
        {
            if (op.Kind != DiffLineKind.Removed)
            {
                return op.New;
            }
        }

        return 0;
    }
}
