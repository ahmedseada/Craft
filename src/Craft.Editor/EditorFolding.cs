// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Code folding for one editor (phase X): the ranges its language's providers give (<see cref="FoldingRangeProviders"/>),
/// asked off the UI thread once per text version the typing settles on (<see cref="Core.CraftOptions.FoldingDelayMilliseconds"/>
/// after the last change), mapped through the edits in between; and the folds (<see cref="Map"/>), which hide lines from
/// layout, paint and the caret's line moves. Used on the UI thread; the answers come back through the editor's dispatch.
/// </summary>
public sealed class EditorFolding : IDisposable
{
    private readonly EditorModel _model;
    private readonly IReadOnlyList<IFoldingRangeProvider> _providers;
    private FoldingRange[] _ranges = [];
    private CancellationTokenSource? _request;
    private EditorModel.DebounceTimer? _timer;
    private Action? _refresh;
    private bool _disposed;

    internal EditorFolding(EditorModel model)
    {
        _model = model;
        _providers = model.Options.Folding ? FoldingRangeProviders.For(model.Language?.Name) : [];
        Map.SetLineCount(model.Snapshot.LineCount);
        if (IsAvailable)
        {
            Schedule();
        }
    }

    /// <summary>Whether this document folds: the option is on and a provider folds its language.</summary>
    public bool IsAvailable => _providers.Count > 0;

    /// <summary>The folds and the line-to-row mapping.</summary>
    public FoldMap Map { get; } = new();

    /// <summary>The foldable ranges, sorted by header line, one per header (mapped through the edits since they came).</summary>
    public ReadOnlySpan<FoldingRange> Ranges => _ranges;

    /// <summary>The text version the providers last answered for (-1 before the first answer).</summary>
    public int RangesVersion { get; private set; } = -1;

    /// <summary>How many times the providers were asked.</summary>
    public long Requests { get; private set; }

    /// <summary>The last provider failure ("name: message"), or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised on the UI thread when new ranges landed.</summary>
    public event EventHandler? RangesChanged;

    /// <summary>Whether a range starts on <paramref name="line"/> (its chevron shows).</summary>
    public bool IsFoldable(int line) => FoldMap.FindRange(_ranges, line) >= 0;

    /// <summary>Whether the range starting on <paramref name="line"/> is folded.</summary>
    public bool IsFolded(int line) => Map.IsFolded(line);

    /// <summary>
    /// Asks the providers for the ranges of the current text now (off the UI thread); the task completes once they are
    /// applied (on the UI thread), or at once when the document does not fold or they are already current.
    /// </summary>
    public Task RefreshAsync()
    {
        if (!IsAvailable || _disposed)
        {
            return Task.CompletedTask;
        }

        _timer?.Cancel();
        var snapshot = _model.Snapshot;
        if (RangesVersion == snapshot.Version)
        {
            return Task.CompletedTask;
        }

        CancelRequest();
        var cancel = new CancellationTokenSource();
        _request = cancel;
        var token = cancel.Token;
        var request = new FoldingRequest(snapshot, _model.Language!.Name, _model.LanguageSession, _model.Options.TabSize);
        var providers = _providers;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests++;
        _ = Task.Run(async () =>
        {
            var all = new List<FoldingRange>();
            string? error = null;
            foreach (var provider in providers)
            {
                try
                {
                    all.AddRange(await provider.GetRangesAsync(request, token).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    done.TrySetResult();
                    return;
                }
#pragma warning disable CA1031 // One provider's failure must not take the others' ranges away; it is kept in LastError.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    error = $"{provider.Name}: {e.Message}";
                }
            }

            // Back on the UI thread: the folds and the paint belong to it.
            _model.Post(() =>
            {
                try
                {
                    if (!_disposed && !token.IsCancellationRequested)
                    {
                        LastError = error;
                        Apply(all, snapshot);
                    }
                }
                finally
                {
                    done.TrySetResult();
                }
            });
        }, CancellationToken.None);
        return done.Task;
    }

    /// <summary>
    /// Folds the innermost range around <paramref name="line"/> not folded yet (Ctrl+Shift+[); false when there is none.
    /// </summary>
    public bool Fold(int line)
    {
        int at = Innermost(line, unfoldedOnly: true);
        return at >= 0 && FoldRange(_ranges[at]);
    }

    /// <summary>Unfolds the fold on <paramref name="line"/>, else the innermost fold around it (Ctrl+Shift+]); false when there is none.</summary>
    public bool Unfold(int line)
    {
        bool changed = Map.Unfold(line);
        if (!changed)
        {
            var folded = Map.Folded;
            for (int i = folded.Count - 1; i >= 0 && !changed; i--)
            {
                if (folded[i].StartLine <= line && folded[i].EndLine >= line)
                {
                    changed = Map.Unfold(folded[i].StartLine);
                }
            }
        }

        if (changed)
        {
            _model.OnFoldsChanged();
        }

        return changed;
    }

    /// <summary>Folds or unfolds the range whose header is <paramref name="line"/> (the gutter's chevron); false when none starts there.</summary>
    public bool Toggle(int line)
    {
        if (Map.IsFolded(line))
        {
            Map.Unfold(line);
            _model.OnFoldsChanged();
            return true;
        }

        int at = FoldMap.FindRange(_ranges, line);
        return at >= 0 && FoldRange(_ranges[at]);
    }

    /// <summary>Folds every range (Ctrl+K Ctrl+0); the count folded anew.</summary>
    public int FoldAll()
    {
        int folded = Map.FoldAll(_ranges);
        if (folded > 0)
        {
            _model.OnFoldsChanged(movedOutOfFolds: true);
        }

        return folded;
    }

    /// <summary>Unfolds everything (Ctrl+K Ctrl+J); false when nothing was folded.</summary>
    public bool UnfoldAll()
    {
        bool changed = Map.Clear();
        if (changed)
        {
            _model.OnFoldsChanged();
        }

        return changed;
    }

    /// <summary>Unfolds what hides <paramref name="line"/> (the caret went there: a search, go to line, a click); false when it showed.</summary>
    public bool Reveal(int line)
    {
        if (!Map.HasHidden || !Map.Reveal(line))
        {
            return false;
        }

        _model.OnFoldsChanged();
        return true;
    }

    /// <summary>Sets the ranges directly (a test, or a host that computed them), as if a provider answered for the current text.</summary>
    public void SetRanges(IEnumerable<FoldingRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        Apply([.. ranges], _model.Snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        CancelRequest();
    }

    // Cancelled, not disposed: a provider may still be reading its token (a source without a timer holds nothing to free).
    private void CancelRequest()
    {
        _request?.Cancel();
        _request = null;
    }

    internal void OnBufferChanged(TextChangedEventArgs change)
    {
        var before = change.Before;
        var edits = change.Edits;
        int count = edits.Count;
        int newLines = change.After.LineCount;
        bool anyLines = before.LineCount != newLines;
        bool foldsChanged = false;
        if (Map.Folded.Count > 0 || anyLines)
        {
            // From the last edit to the first, so each one's lines (in the text before the change) are not yet moved by the others.
            int lines = before.LineCount;
            for (int i = count - 1; i >= 0; i--)
            {
                var edit = edits[i];
                int first = before.GetLineIndex(edit.Start);
                int last = edit.Length == 0 ? first : before.GetLineIndex(edit.End);
                int delta = Breaks(edit.Text) - (last - first);
                lines += delta;
                foldsChanged |= Map.OnEdited(first, last, delta, i == 0 ? newLines : lines);
            }

            Map.SetLineCount(newLines);
        }

        if (anyLines && _ranges.Length > 0)
        {
            MapRanges(before, edits, newLines);
        }

        if (foldsChanged)
        {
            _model.OnFoldsChanged();
        }

        if (IsAvailable)
        {
            Schedule();
        }
    }

    private bool FoldRange(FoldingRange range)
    {
        if (!Map.Fold(range))
        {
            return false;
        }

        _model.OnFoldsChanged(movedOutOfFolds: true);
        return true;
    }

    // The innermost range holding `line` (its header at or above it, its end at or below), optionally only one not folded.
    private int Innermost(int line, bool unfoldedOnly)
    {
        int lo = 0, hi = _ranges.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_ranges[mid].StartLine <= line)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        for (int i = found; i >= 0; i--)
        {
            var range = _ranges[i];
            if (range.EndLine >= line && !(unfoldedOnly && Map.IsFolded(range.StartLine)) && !Map.IsHidden(range.StartLine))
            {
                return i;
            }
        }

        return -1;
    }

    private void Schedule()
    {
        _timer ??= new EditorModel.DebounceTimer(_model);
        _refresh ??= () => _ = RefreshAsync();     // made once: a keystroke reschedules it without allocating
        _timer.Schedule(Math.Max(1, _model.Options.FoldingDelayMilliseconds), _refresh);
    }

    private void Apply(List<FoldingRange> found, ITextSnapshot snapshot)
    {
        if (snapshot.Version != _model.Snapshot.Version)
        {
            return;     // the text changed since: the request its change scheduled answers for it
        }

        int lines = snapshot.LineCount;
        found.RemoveAll(r => r.StartLine < 0 || r.EndLine <= r.StartLine || r.EndLine >= lines);
        found.Sort(static (a, b) => a.StartLine != b.StartLine ? a.StartLine.CompareTo(b.StartLine) : b.EndLine.CompareTo(a.EndLine));
        int kept = 0;
        for (int i = 0; i < found.Count; i++)
        {
            if (kept == 0 || found[kept - 1].StartLine != found[i].StartLine)
            {
                found[kept++] = found[i];
            }
        }

        found.RemoveRange(kept, found.Count - kept);
        _ranges = [.. found];
        RangesVersion = snapshot.Version;
        bool foldsChanged = Map.Reconcile(_ranges);
        if (foldsChanged)
        {
            _model.OnFoldsChanged();
        }
        else
        {
            _model.Invalidate();
        }

        RangesChanged?.Invoke(this, EventArgs.Empty);
    }

    // The ranges follow a change that added or removed lines: one pass over the edits' line deltas (prefix sums) and one
    // over the ranges. A range an edit split at its header or its end is dropped until the providers answer again.
    private void MapRanges(ITextSnapshot before, IReadOnlyList<TextEdit> edits, int newLines)
    {
        int count = edits.Count;
        var firsts = ArrayPool<int>.Shared.Rent(count);
        var lasts = ArrayPool<int>.Shared.Rent(count);
        var sums = ArrayPool<int>.Shared.Rent(count + 1);
        try
        {
            sums[0] = 0;
            for (int i = 0; i < count; i++)
            {
                var edit = edits[i];
                firsts[i] = before.GetLineIndex(edit.Start);
                lasts[i] = edit.Length == 0 ? firsts[i] : before.GetLineIndex(edit.End);
                sums[i + 1] = sums[i] + Breaks(edit.Text) - (lasts[i] - firsts[i]);
            }

            int kept = 0;
            int pointer = 0;
            for (int r = 0; r < _ranges.Length; r++)
            {
                var range = _ranges[r];
                while (pointer < count && lasts[pointer] < range.StartLine)
                {
                    pointer++;
                }

                // Edits [0, pointer) end above the header: they move it.
                bool split = pointer < count && firsts[pointer] <= range.StartLine && (sums[pointer + 1] - sums[pointer]) != 0;
                int after = Upper(lasts, count, range.EndLine);     // edits ending at or above the end line move it
                if (!split && after < count && firsts[after] <= range.EndLine)
                {
                    split = true;   // an edit that starts inside and ends below the range
                }

                if (split)
                {
                    continue;
                }

                int start = range.StartLine + sums[pointer];
                int end = range.EndLine + sums[after];
                if (end > start && end < newLines)
                {
                    _ranges[kept++] = range with { StartLine = start, EndLine = end };
                }
            }

            if (kept != _ranges.Length)
            {
                Array.Resize(ref _ranges, kept);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(firsts);
            ArrayPool<int>.Shared.Return(lasts);
            ArrayPool<int>.Shared.Return(sums);
        }

        // The first index whose last line is past `line`.
        static int Upper(int[] lasts, int count, int line)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (lasts[mid] <= line)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }

    private static int Breaks(string text)
    {
        var span = text.AsSpan();
        int breaks = 0;
        int at;
        while ((at = span.IndexOfAny('\r', '\n')) >= 0)
        {
            breaks++;
            if (span[at] == '\r' && at + 1 < span.Length && span[at + 1] == '\n')
            {
                at++;
            }

            span = span[(at + 1)..];
        }

        return breaks;
    }
}
