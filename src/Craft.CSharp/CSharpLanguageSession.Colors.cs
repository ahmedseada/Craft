// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Microsoft.CodeAnalysis.Text;
using RoslynClassifier = Microsoft.CodeAnalysis.Classification.Classifier;
using RoslynNames = Microsoft.CodeAnalysis.Classification.ClassificationTypeNames;

namespace Craft.CSharp;

/// <summary>
/// The session's semantic colours: Roslyn's own classification (<c>Classifier.GetClassifiedSpansAsync</c> over the
/// workspace's document, so names are classified by what they bind to) of the lines the editor asks for and a screen
/// either side, computed on the thread pool after <see cref="LanguageSettings.SemanticColorsDelayMilliseconds"/>; when it
/// lands, <see cref="Reclassified"/> is raised. <see cref="Classify"/> stays on the UI thread and never waits: inside the
/// range Roslyn classified it gives Roslyn's spans, mapped through the edits made since (the spans before and after the
/// changed text kept, shifted; the changed text itself given the syntactic layer's fresh spans), and the syntactic spans
/// outside it. Roslyn's additive "static symbol" joins the name's own class (<see cref="TokenClasses.WithStaticSymbol"/>).
/// </summary>
public sealed partial class CSharpLanguageSession
{
    private Colors? _colors;
    private TextChangeRange? _colorsChange;
    private ColorsWant? _colorsWant;
    private ITimer? _colorsTimer;
    private bool _colorsRunning;
    private bool _colorsAgain;
    private TextChangeRange? _colorsRunChange;
    private bool _colorsRunUnknown;
    private CancellationTokenSource? _colorsStop;
    // Made only when someone waits (WhenColorsIdle), never on the frame's path: a pass asked for by a frame marks
    // _colorsDue and allocates nothing (the paint's 0 B a warm frame; before, a new source and its task, 88 B, each time a
    // finished pass was asked for again).
    private TaskCompletionSource? _colorsIdle;
    private bool _colorsDue;
    private TokenSpanBuffer? _colorScratch;
    private int _colorsPasses;
    private int _colorsStarted;
    // X part 9's speed fix: a pass that found no workspace document (a file no reader gives a project) or failed for a
    // version is not asked again while nothing can change it (a release for the first, an edit for the second): before, a
    // file with no document started a pass every delay while frames came.
    private bool _colorsNoDocument;
    private int _colorsFailedVersion = -1;
    private long _lastColorsTicks;

    /// <summary>How many semantic colour passes landed since the session opened.</summary>
    public int ColorsPasses => Volatile.Read(ref _colorsPasses);

    /// <summary>How many semantic colour passes started since the session opened (landed or not).</summary>
    internal int ColorsPassesStarted => Volatile.Read(ref _colorsStarted);

    /// <summary>What the last semantic colour pass cost on the thread pool (the document brought to its snapshot, and Roslyn's classification).</summary>
    public TimeSpan LastColorsTime => TimeSpan.FromTicks(Volatile.Read(ref _lastColorsTicks));

    /// <summary>The range (in its snapshot's positions) and version the last landed semantic colours are of, or null.</summary>
    public (int Version, int Start, int End)? ColorsRange => Volatile.Read(ref _colors) is { } colors ? (colors.Version, colors.Start, colors.End) : null;

    /// <summary>Completes when no semantic colour pass is due or running (the tests' and the benches' idle hook).</summary>
    public Task WhenColorsIdle()
    {
        lock (_gate)
        {
            return !_colorsDue ? Task.CompletedTask : (_colorsIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private bool ColorsOn => _automatic && _context.Settings.SemanticColorsDelayMilliseconds >= 0;

    /// <summary>
    /// Classifies the range with the semantic colours over the syntactic spans; false when there are none to give (the
    /// caller gives the syntactic spans alone). Asks for a new pass when the colours do not cover the range or are older
    /// than the snapshot.
    /// </summary>
    private bool ClassifyWithColors(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        if (!ColorsOn || !ReferenceEquals(snapshot.Buffer, _buffer))
        {
            return false;
        }

        Colors? colors;
        TextChangeRange? change = null;
        lock (_gate)
        {
            colors = _colors;
            bool newest = snapshot.Version == _latest.Version;
            if (colors is not null && colors.Version != snapshot.Version)
            {
                if (newest && !_colorsUnknown)
                {
                    change = _colorsChange;
                }
                else
                {
                    colors = null;
                }
            }

            if (newest && (colors is null || change is not null || colors.Start > start || colors.End < start + length))
            {
                WantColors(snapshot, start, length);
            }
        }

        if (colors is null)
        {
            return false;
        }

        var syntactic = Interlocked.Exchange(ref _colorScratch, null) ?? new TokenSpanBuffer(512);
        try
        {
            syntactic.Clear();
            ClassifySyntactic(snapshot, start, length, syntactic);
            Merge(colors, change, syntactic.Spans, start, start + length, into);
        }
        finally
        {
            syntactic.Clear();
            if (Interlocked.CompareExchange(ref _colorScratch, syntactic, null) is not null)
            {
                syntactic.Dispose();
            }
        }

        return true;
    }

    /// <summary>
    /// Roslyn's spans of <paramref name="colors"/> (mapped through <paramref name="change"/>) where they reach the range,
    /// the syntactic ones where they do not: before and after the range Roslyn classified, and in the changed text.
    /// </summary>
    internal static void Merge(Colors colors, TextChangeRange? change, ReadOnlySpan<TokenSpan> syntactic, int start, int end, TokenSpanBuffer into)
    {
        bool changed = change is not null;
        int at = change?.Span.Start ?? int.MaxValue;
        int oldEnd = change?.Span.End ?? int.MaxValue;
        int delta = change is { } c ? c.NewLength - c.Span.Length : 0;
        int newEnd = change is { } d ? d.Span.Start + d.NewLength : int.MaxValue;
        // The classified range in the newest positions, its edges moved as the spans are.
        int covStart = Map(colors.Start);
        int covEnd = Map(colors.End);

        int s = 0;
        // The syntactic spans before the classified range.
        while (s < syntactic.Length && syntactic[s].End <= covStart)
        {
            into.Add(syntactic[s].Start, syntactic[s].Length, syntactic[s].Class);
            s++;
        }

        int lastEnd = Math.Max(covStart, start);
        if (covStart < covEnd)
        {
            var spans = colors.Spans;
            for (int i = FirstReaching(spans, !changed || start < at ? start : start - delta); i < spans.Length; i++)
            {
                var span = spans[i];
                int mapped;
                if (span.End < at)
                {
                    mapped = span.Start;
                }
                else if (span.Start > oldEnd)
                {
                    mapped = span.Start + delta;
                }
                else
                {
                    // Touches the changed text: dropped; the stretch is filled from the syntactic spans.
                    continue;
                }

                if (mapped >= end)
                {
                    break;
                }

                Fill(syntactic, ref s, lastEnd, mapped, changed, at, newEnd, into);
                into.Add(mapped, span.Length, span.Class);
                lastEnd = mapped + span.Length;
            }

            Fill(syntactic, ref s, lastEnd, covEnd, changed, at, newEnd, into);
        }

        // The syntactic spans after the classified range.
        for (; s < syntactic.Length; s++)
        {
            if (syntactic[s].Start >= covEnd)
            {
                into.Add(syntactic[s].Start, syntactic[s].Length, syntactic[s].Class);
            }
        }

        int Map(int position) => position < at ? position : position > oldEnd ? position + delta : newEnd;
    }

    /// <summary>
    /// Passes the syntactic spans that start before <paramref name="to"/>; adds those wholly in <paramref name="from"/> to
    /// <paramref name="to"/> when that stretch reaches the changed text (elsewhere Roslyn's spans are the answer).
    /// </summary>
    private static void Fill(ReadOnlySpan<TokenSpan> syntactic, ref int s, int from, int to, bool changed, int at, int newEnd, TokenSpanBuffer into)
    {
        bool reaches = changed && from <= newEnd && to >= at;
        while (s < syntactic.Length && syntactic[s].Start < to)
        {
            var span = syntactic[s];
            if (reaches && span.Start >= from && span.End <= to)
            {
                into.Add(span.Start, span.Length, span.Class);
            }

            s++;
        }
    }

    /// <summary>The first span that ends after <paramref name="position"/> (a binary search: the spans are in order and apart).</summary>
    private static int FirstReaching(TokenSpan[] spans, int position)
    {
        int low = 0;
        int high = spans.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (spans[middle].End <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private bool _colorsUnknown;

    /// <summary>Called holding the gate on each change: the colours' mapping and the running pass's follow it.</summary>
    private void NoteColorsChange(TextChangedEventArgs change, bool continuous)
    {
        if (_colors is not null)
        {
            if (continuous && !_colorsUnknown)
            {
                _colorsChange = SnapshotSourceText.Compose(_colorsChange, SnapshotSourceText.Collapse(change.Edits));
            }
            else
            {
                _colorsUnknown = true;
            }
        }

        if (_colorsRunning)
        {
            if (continuous && !_colorsRunUnknown)
            {
                _colorsRunChange = SnapshotSourceText.Compose(_colorsRunChange, SnapshotSourceText.Collapse(change.Edits));
            }
            else
            {
                _colorsRunUnknown = true;
            }
        }
    }

    /// <summary>Called holding the gate: the range a pass should classify next (the asked range and a screen either side).</summary>
    private void WantColors(ITextSnapshot snapshot, int start, int length)
    {
        if (_disposed || IsSemanticDeferred || _options.Kind != Microsoft.CodeAnalysis.SourceCodeKind.Regular || _colorsNoDocument || _colorsFailedVersion == snapshot.Version)
        {
            return;
        }

        if (_colorsWant is { } want && want.Version == snapshot.Version && want.Start <= start && want.End >= start + length)
        {
            return;
        }

        int margin = Math.Max(length, 1);
        int from = Math.Max(0, start - margin);
        int to = (int)Math.Min(snapshot.Length, (long)start + length + margin);
        _colorsWant = new ColorsWant(snapshot.Version, from, to);
        _colorsDue = true;

        if (_colorsRunning)
        {
            _colorsAgain = true;
            return;
        }

        _colorsTimer ??= TimeProvider.System.CreateTimer(static state => ((CSharpLanguageSession)state!).OnColorsDue(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _colorsTimer.Change(TimeSpan.FromMilliseconds(Math.Max(0, _context.Settings.SemanticColorsDelayMilliseconds)), Timeout.InfiniteTimeSpan);
    }

    private void OnColorsDue()
    {
        ITextSnapshot snapshot;
        ColorsWant want;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _colorsWant is not { } wanted)
            {
                ColorsIdle();
                return;
            }

            if (_colorsRunning)
            {
                _colorsAgain = true;
                return;
            }

            _colorsRunning = true;
            Interlocked.Increment(ref _colorsStarted);
            _colorsWant = null;
            _colorsRunChange = null;
            _colorsRunUnknown = false;
            snapshot = _latest;
            // A want of an older version: the same range of the newest text (the screen moves little meanwhile).
            want = wanted with { End = Math.Min(wanted.End, snapshot.Length), Start = Math.Min(wanted.Start, snapshot.Length) };
            _colorsStop ??= new CancellationTokenSource();
            token = _colorsStop.Token;
        }

        _ = RunColorsAsync(snapshot, want, token);
    }

    private async Task RunColorsAsync(ITextSnapshot snapshot, ColorsWant want, CancellationToken cancellationToken)
    {
        bool landed = false;
        try
        {
            long started = Stopwatch.GetTimestamp();
            var found = await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                lock (_gate)
                {
                    _colorsNoDocument = true;                  // nothing to classify until the workspace is built again
                    _colorsWant = null;
                }
            }

            if (found is { } document)
            {
                var roslyn = await RoslynClassifier.GetClassifiedSpansAsync(document, TextSpan.FromBounds(want.Start, want.End), cancellationToken).ConfigureAwait(false);
                var spans = Convert(roslyn);
                var colors = new Colors(snapshot.Version, spans.Length > 0 ? Math.Min(want.Start, spans[0].Start) : want.Start,
                    spans.Length > 0 ? Math.Max(want.End, spans[^1].End) : want.End, spans);
                lock (_gate)
                {
                    if (!_colorsRunUnknown && !_disposed)
                    {
                        _colors = colors;
                        _colorsChange = _colorsRunChange;
                        _colorsUnknown = false;
                        landed = true;
                    }
                }

                Volatile.Write(ref _lastColorsTicks, Stopwatch.GetElapsedTime(started).Ticks);
            }
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // A failed pass is kept in SemanticError; the syntactic colours stay, the next version asks again.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Volatile.Write(ref _semanticError, error);
            lock (_gate)
            {
                _colorsFailedVersion = snapshot.Version;       // not again for this text: an edit asks again
                _colorsWant = null;
            }
        }

        if (landed)
        {
            Interlocked.Increment(ref _colorsPasses);
            try
            {
                Reclassified?.Invoke(this, EventArgs.Empty);
            }
#pragma warning disable CA1031 // A subscriber's failure is kept, never thrown into the pass.
            catch (Exception error)
#pragma warning restore CA1031
            {
                Volatile.Write(ref _error, error);
            }
        }

        bool again;
        lock (_gate)
        {
            _colorsRunning = false;
            _colorsRunChange = null;
            again = _colorsAgain && _colorsWant is not null && !_disposed;
            _colorsAgain = false;
            if (again)
            {
                _colorsTimer?.Change(TimeSpan.FromMilliseconds(Math.Max(0, _context.Settings.SemanticColorsDelayMilliseconds)), Timeout.InfiniteTimeSpan);
            }
            else
            {
                ColorsIdle();
            }
        }
    }

    /// <summary>
    /// Roslyn's classified spans as Craft's: in order, one per range, a name's class joined with "static symbol" when
    /// Roslyn adds it, its other additive classes (a reassigned variable, an obsolete symbol) left out, and no
    /// whitespace or plain-text spans.
    /// </summary>
    internal static TokenSpan[] Convert(IEnumerable<Microsoft.CodeAnalysis.Classification.ClassifiedSpan> roslyn)
    {
        var list = new List<Microsoft.CodeAnalysis.Classification.ClassifiedSpan>(roslyn);
        list.Sort(static (a, b) => a.TextSpan.Start != b.TextSpan.Start ? a.TextSpan.Start.CompareTo(b.TextSpan.Start) : a.TextSpan.Length.CompareTo(b.TextSpan.Length));
        var result = new List<TokenSpan>(list.Count);
        for (int i = 0; i < list.Count;)
        {
            var span = list[i].TextSpan;
            string? primary = null;
            bool isStatic = false;
            int j = i;
            for (; j < list.Count && list[j].TextSpan == span; j++)
            {
                var name = list[j].ClassificationType;
                if (name == RoslynNames.StaticSymbol)
                {
                    isStatic = true;
                }
                else if (!IsAdditive(name) && name is not (RoslynNames.WhiteSpace or RoslynNames.Text))
                {
                    primary ??= name;
                }
            }

            i = j;
            if (primary is null || span.Length == 0 || (result.Count > 0 && result[^1].End > span.Start))
            {
                continue;
            }

            result.Add(new TokenSpan(span.Start, span.Length, isStatic ? TokenClasses.WithStaticSymbol(primary) : primary));
        }

        return [.. result];
    }

    private static bool IsAdditive(string name)
    {
        foreach (var additive in RoslynNames.AdditiveTypeNames)
        {
            if (additive == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Called holding the gate from Dispose: no colour pass runs again.</summary>
    private void StopColors()
    {
        _colorsStop?.Cancel();
        _colorsTimer?.Dispose();
        _colorsTimer = null;
        _colorsWant = null;
        if (!_colorsRunning)
        {
            ColorsIdle();
        }
    }

    /// <summary>Called holding the gate: no colour pass is due or running; whoever waits for it is let go.</summary>
    private void ColorsIdle()
    {
        _colorsDue = false;
        var waiting = _colorsIdle;
        _colorsIdle = null;
        waiting?.TrySetResult();
    }

    /// <summary>Roslyn's spans of a range of one version: <paramref name="Start"/> to <paramref name="End"/>, in that version's positions.</summary>
    internal sealed record Colors(int Version, int Start, int End, TokenSpan[] Spans);

    private readonly record struct ColorsWant(int Version, int Start, int End);
}
