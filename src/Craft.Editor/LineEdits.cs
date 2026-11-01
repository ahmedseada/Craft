// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// A change's edits as line splices (X part 9): for each edit, the first line it touches, the line breaks it removed and
/// the ones it added, so a per-line array (word wrap's rows, bracket colours' states) follows the change by splicing only
/// the lines the edits touched, never by reading the whole text again.
/// </summary>
internal static class LineEdits
{
    /// <summary>
    /// The splices of <paramref name="change"/>, in order, into <paramref name="into"/>: one per edit, or one over the whole
    /// changed range when the per-edit counts do not add up to the change's line count (a break joined across an edit).
    /// </summary>
    public static void Splices(TextChangedEventArgs change, List<(int First, int Removed, int Added)> into)
    {
        into.Clear();
        var before = change.Before;
        var edits = change.Edits;
        if (edits.Count == 0)
        {
            return;
        }

        int delta = change.After.LineCount - before.LineCount;
        int sum = 0;
        foreach (var edit in edits)
        {
            int first = before.GetLineIndex(Math.Clamp(edit.Start, 0, before.Length));
            int last = before.GetLineIndex(Math.Clamp(edit.End, 0, before.Length));
            int added = Breaks(edit.Text);
            into.Add((first, last - first, added));
            sum += added - (last - first);
        }

        if (sum != delta)
        {
            int first = into[0].First;
            int removed = into[^1].First + into[^1].Removed - first;
            into.Clear();
            into.Add((first, removed, removed + delta));
        }
    }

    /// <summary>
    /// Replaces the entries of lines <paramref name="first"/> to <paramref name="first"/> + <paramref name="removed"/> with
    /// <paramref name="added"/> + 1 entries: the first keeps its value, the others take <paramref name="fill"/>.
    /// <paramref name="count"/> is the entries in use (it follows); the array grows as needed. One move of the tail.
    /// </summary>
    public static void Splice(ref int[] array, ref int count, int first, int removed, int added, int fill)
    {
        int change = added - removed;
        if (change > 0 && count + change > array.Length)
        {
            Array.Resize(ref array, Math.Max(count + change, array.Length * 2));
        }

        int tail = first + removed + 1;
        if (tail < count && change != 0)
        {
            Array.Copy(array, tail, array, tail + change, count - tail);
        }

        count += change;
        for (int i = first + 1; i <= first + added && i < count; i++)
        {
            array[i] = fill;
        }
    }

    // The line breaks in an edit's text: "\r\n", "\n" and a lone "\r" one each.
    private static int Breaks(string text)
    {
        int breaks = 0;
        var span = text.AsSpan();
        int at;
        while ((at = span.IndexOfAny('\r', '\n')) >= 0)
        {
            breaks++;
            span = span[at] == '\r' && at + 1 < span.Length && span[at + 1] == '\n' ? span[(at + 2)..] : span[(at + 1)..];
        }

        return breaks;
    }
}
