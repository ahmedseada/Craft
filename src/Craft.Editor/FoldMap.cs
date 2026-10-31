// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The folded ranges of a document and the mapping between its lines and the rows drawn (phase X). A folded range keeps
/// its header line and hides the lines after it through its end; folds may nest (an inner fold stays folded inside an
/// outer one). The hidden lines are kept as merged intervals with the count hidden before each, so every query (a
/// line's row, a row's line, the next shown line, whether a line is hidden) is a binary search over the intervals, never
/// a walk over the folds: a paint asks once per drawn line, whatever the folds' count. Changes (fold, unfold, an edit)
/// rebuild the intervals once. Used on the UI thread.
/// </summary>
public sealed class FoldMap
{
    private readonly List<FoldingRange> _folded = [];
    private int[] _start = new int[8];
    private int[] _end = new int[8];
    private int[] _before = new int[8];
    private int _count;
    private int _hidden;

    /// <summary>The document's line count the map was last told (<see cref="SetLineCount"/>).</summary>
    public int LineCount { get; private set; } = 1;

    /// <summary>The folded ranges, by header line (nested ones included).</summary>
    public IReadOnlyList<FoldingRange> Folded => _folded;

    /// <summary>How many lines the folds hide.</summary>
    public int HiddenLines => _hidden;

    /// <summary>Whether any line is hidden.</summary>
    public bool HasHidden => _count > 0;

    /// <summary>The rows drawn: the lines minus the hidden ones.</summary>
    public int RowCount => LineCount - _hidden;

    /// <summary>The hidden intervals (first and last hidden line of each, in order): for tests and the overview.</summary>
    public int IntervalCount => _count;

    /// <summary>Sets the document's line count (folds past it are dropped).</summary>
    public void SetLineCount(int lines)
    {
        LineCount = Math.Max(1, lines);
        if (_folded.RemoveAll(f => f.EndLine >= LineCount) > 0)
        {
            Rebuild();
        }
    }

    /// <summary>Whether a fold has its header at <paramref name="line"/>.</summary>
    public bool IsFolded(int line) => IndexOf(line) >= 0;

    /// <summary>Folds <paramref name="range"/> (replacing a fold with the same header); false when it is not a range of this text or already folded so.</summary>
    public bool Fold(FoldingRange range)
    {
        if (range.StartLine < 0 || range.EndLine <= range.StartLine || range.EndLine >= LineCount)
        {
            return false;
        }

        int at = IndexOf(range.StartLine);
        if (at >= 0)
        {
            if (_folded[at].EndLine == range.EndLine)
            {
                return false;
            }

            _folded[at] = range;
        }
        else
        {
            _folded.Insert(~at, range);
        }

        Rebuild();
        return true;
    }

    /// <summary>Unfolds the fold whose header is <paramref name="line"/>; false when there is none.</summary>
    public bool Unfold(int line)
    {
        int at = IndexOf(line);
        if (at < 0)
        {
            return false;
        }

        _folded.RemoveAt(at);
        Rebuild();
        return true;
    }

    /// <summary>Folds every range of <paramref name="ranges"/> at once (one rebuild); the count folded anew.</summary>
    public int FoldAll(IReadOnlyList<FoldingRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        int folded = 0;
        foreach (var range in ranges)
        {
            if (range.StartLine < 0 || range.EndLine <= range.StartLine || range.EndLine >= LineCount)
            {
                continue;
            }

            int at = IndexOf(range.StartLine);
            if (at >= 0)
            {
                if (_folded[at].EndLine != range.EndLine)
                {
                    _folded[at] = range;
                    folded++;
                }
            }
            else
            {
                _folded.Insert(~at, range);
                folded++;
            }
        }

        if (folded > 0)
        {
            Rebuild();
        }

        return folded;
    }

    /// <summary>Unfolds everything; false when nothing was folded.</summary>
    public bool Clear()
    {
        if (_folded.Count == 0)
        {
            return false;
        }

        _folded.Clear();
        Rebuild();
        return true;
    }

    /// <summary>
    /// Unfolds every fold that hides <paramref name="line"/> (outer and inner), so it shows; false when it was shown.
    /// </summary>
    public bool Reveal(int line)
    {
        if (!IsHidden(line))
        {
            return false;
        }

        _folded.RemoveAll(f => f.StartLine < line && f.EndLine >= line);
        Rebuild();
        return true;
    }

    /// <summary>Whether <paramref name="line"/> is hidden by a fold.</summary>
    public bool IsHidden(int line)
    {
        int i = IntervalAtOrBefore(line);
        return i >= 0 && line <= _end[i];
    }

    /// <summary>The row <paramref name="line"/> is drawn on (a hidden line: its fold's header row).</summary>
    public int RowOf(int line)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        int i = IntervalAtOrBefore(line);
        if (i < 0)
        {
            return line;
        }

        if (line <= _end[i])
        {
            return _start[i] - 1 - _before[i];
        }

        return line - (_before[i] + (_end[i] - _start[i] + 1));
    }

    /// <summary>The line drawn on <paramref name="row"/> (clamped to the rows).</summary>
    public int LineAt(int row)
    {
        row = Math.Clamp(row, 0, Math.Max(0, RowCount - 1));

        // The rows at or past interval i's first shown line after it start at _start[i] - _before[i]; a strictly increasing key.
        int lo = 0, hi = _count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_start[mid] - _before[mid] <= row)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found < 0 ? row : row + _before[found] + (_end[found] - _start[found] + 1);
    }

    /// <summary>The first shown line after <paramref name="line"/> (<see cref="LineCount"/> when there is none).</summary>
    public int NextShown(int line)
    {
        int next = line + 1;
        if (_count == 0)
        {
            return next;
        }

        int i = IntervalAtOrBefore(next);
        return i >= 0 && next <= _end[i] ? _end[i] + 1 : next;
    }

    /// <summary>The last shown line before <paramref name="line"/> (-1 when there is none).</summary>
    public int PreviousShown(int line)
    {
        int previous = line - 1;
        if (_count == 0 || previous < 0)
        {
            return previous;
        }

        int i = IntervalAtOrBefore(previous);
        return i >= 0 && previous <= _end[i] ? _start[i] - 1 : previous;
    }

    /// <summary>The first and last hidden line of interval <paramref name="index"/>.</summary>
    public (int First, int Last) Interval(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return (_start[index], _end[index]);
    }

    /// <summary>
    /// Follows a change of the text: the edit spanned lines <paramref name="firstLine"/> to <paramref name="lastLine"/> of
    /// the text before it and changed the line count by <paramref name="lineDelta"/>. A fold whose hidden lines it touched,
    /// or whose header it split or joined, is unfolded (an edit never happens out of sight); one after it moves; one before
    /// it stays. True when the folds changed.
    /// </summary>
    public bool OnEdited(int firstLine, int lastLine, int lineDelta, int newLineCount)
    {
        LineCount = Math.Max(1, newLineCount);
        if (_folded.Count == 0)
        {
            return false;
        }

        if (lineDelta == 0)
        {
            // Typing within lines (the keystroke's case): only an edit that reaches hidden lines changes a fold.
            int last = IntervalAtOrBefore(lastLine);
            if (last < 0 || _end[last] < firstLine)
            {
                return false;
            }
        }

        bool changed = false;
        for (int i = _folded.Count - 1; i >= 0; i--)
        {
            var fold = _folded[i];
            if (fold.EndLine < firstLine)
            {
                continue;
            }

            if (fold.StartLine > lastLine)
            {
                if (lineDelta != 0)
                {
                    _folded[i] = fold with { StartLine = fold.StartLine + lineDelta, EndLine = fold.EndLine + lineDelta };
                    changed = true;
                }

                continue;
            }

            // The edit touches the fold: its hidden lines, or its header with lines added or removed.
            if (lastLine > fold.StartLine || firstLine > fold.StartLine || lineDelta != 0)
            {
                _folded.RemoveAt(i);
                changed = true;
            }
        }

        if (_folded.RemoveAll(f => f.EndLine >= LineCount) > 0)
        {
            changed = true;
        }

        if (changed)
        {
            Rebuild();
        }

        return changed;
    }

    /// <summary>Keeps only the folds whose header still starts a range of <paramref name="ranges"/> (sorted by header), taking its new end.</summary>
    public bool Reconcile(ReadOnlySpan<FoldingRange> ranges)
    {
        if (_folded.Count == 0)
        {
            return false;
        }

        bool changed = false;
        for (int i = _folded.Count - 1; i >= 0; i--)
        {
            var fold = _folded[i];
            int at = FindRange(ranges, fold.StartLine);
            if (at < 0)
            {
                _folded.RemoveAt(i);
                changed = true;
            }
            else if (ranges[at].EndLine != fold.EndLine)
            {
                _folded[i] = ranges[at];
                changed = true;
            }
        }

        if (changed)
        {
            Rebuild();
        }

        return changed;
    }

    /// <summary>The index in <paramref name="ranges"/> (sorted by header, one per header) of the range starting at <paramref name="line"/>, or -1.</summary>
    public static int FindRange(ReadOnlySpan<FoldingRange> ranges, int line)
    {
        int lo = 0, hi = ranges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int start = ranges[mid].StartLine;
            if (start == line)
            {
                return mid;
            }

            if (start < line)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }

    // The index of the fold with header `line`, or the complement of where it would go.
    private int IndexOf(int line)
    {
        int lo = 0, hi = _folded.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int start = _folded[mid].StartLine;
            if (start == line)
            {
                return mid;
            }

            if (start < line)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    // The last hidden interval whose first line is at or before `line`, or -1.
    private int IntervalAtOrBefore(int line)
    {
        int lo = 0, hi = _count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_start[mid] <= line)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    // The hidden lines of the folds, merged: one pass over the folds in header order (an inner fold lies inside its outer one).
    private void Rebuild()
    {
        _count = 0;
        _hidden = 0;
        foreach (var fold in _folded)
        {
            int first = fold.StartLine + 1;
            int last = fold.EndLine;
            if (_count > 0 && fold.StartLine <= _end[_count - 1])
            {
                // Its header is hidden by the interval before (a nested fold, or one the fold before reaches into): one interval.
                if (last > _end[_count - 1])
                {
                    _hidden += last - _end[_count - 1];
                    _end[_count - 1] = last;
                }

                continue;
            }

            if (_count == _start.Length)
            {
                Array.Resize(ref _start, _count * 2);
                Array.Resize(ref _end, _count * 2);
                Array.Resize(ref _before, _count * 2);
            }

            _start[_count] = first;
            _end[_count] = last;
            _before[_count] = _hidden;
            _hidden += last - first + 1;
            _count++;
        }
    }
}
