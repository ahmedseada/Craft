// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The overview ruler (the X restart, piece 2a): a strip at the viewport's right edge, <see cref="Core.CraftOptions.OverviewRulerWidth"/>
/// wide, that maps the whole file onto the viewport's height and marks its errors and warnings (right half), the find
/// overlay's hits (left half) and the carets (across), with the band the viewport shows. The marks are bucketed in one pass
/// over each sorted list (the diagnostics, the listed find hits, the selections) into a pooled array of one bucket per unit
/// of height, then joined into runs; that happens only when one of them, the text, the folds or the viewport's height
/// changed (rules 73, 75): a frame that changes nothing draws the kept runs. Colours: the theme's <see cref="EditorColors.RulerError"/>,
/// <see cref="EditorColors.RulerWarning"/>, <see cref="EditorColors.RulerFind"/>, <see cref="EditorColors.RulerCaret"/>.
/// A click or a drag on it scrolls there (<see cref="EditorModel.PointerPressed"/>). Owned by one <see cref="EditorRenderer"/>.
/// </summary>
public sealed class OverviewRuler : IDisposable
{
    private const byte Error = 1;
    private const byte Warning = 2;
    private const byte Find = 4;
    private const byte Caret = 8;
    private const byte Added = 16;
    private const byte Changed = 32;
    private const byte Deleted = 64;
    private const byte Conflict = 128;

    private byte[] _buckets = [];
    private int[] _runs = [];              // (first bucket, buckets, lane) triples
    private int _runCount;
    private EditorModel? _keyModel;
    private int _keyVersion = -1;
    private object? _keyDiagnostics;
    private object? _keyFind;
    private int _keySelections = -1;
    private int _keyFolds = -1;
    private int _keyRows = -1;
    private float _keyContent = -1;
    private int _keyBuckets = -1;
    private int _keyMarks = -1;

    internal OverviewRuler()
    {
    }

    /// <summary>How many times the marks were bucketed again (a frame with nothing changed reuses them).</summary>
    public long Rebuilds { get; private set; }

    /// <summary>The marks (runs of buckets of one kind) the ruler holds now.</summary>
    public int Marks => _runCount;

    /// <summary>The marks the last frame drew.</summary>
    public int MarksDrawn { get; private set; }

    /// <summary>Draws the ruler at the right edge of a viewport of <paramref name="viewportWidth"/> by <paramref name="viewportHeight"/>.</summary>
    internal void Draw(IEditorCanvas canvas, EditorModel model, float viewportWidth, float viewportHeight) =>
        Draw(canvas, model, viewportWidth - model.OverviewRulerWidth, model.OverviewRulerWidth, viewportHeight);

    /// <summary>Draws the ruler <paramref name="width"/> wide from <paramref name="left"/> (the minimap's right edge, piece 2b), the viewport's height tall.</summary>
    internal void Draw(IEditorCanvas canvas, EditorModel model, float left, float width, float viewportHeight)
    {
        MarksDrawn = 0;
        if (width <= 0 || viewportHeight <= 0)
        {
            return;
        }

        Rebuild(model, viewportHeight);
        var colors = model.Theme.Colors;
        float scale = Scale(model, viewportHeight);
        canvas.FillRect(left, 0, width, viewportHeight, colors.Gutter);
        canvas.DrawLine(left, 0, left, viewportHeight, colors.Border, 1);

        // The band the viewport shows.
        canvas.FillRect(left + 1, model.ScrollY * scale, width - 1, Math.Max(2, viewportHeight * scale), (colors.Foreground & 0x00FFFFFFu) | 0x18000000u);

        float half = (width - 1) / 2;
        float least = Math.Max(2, model.Metrics.LineHeight * scale);
        for (int i = 0; i < _runCount; i++)
        {
            int first = _runs[3 * i];
            int count = _runs[(3 * i) + 1];
            int lane = _runs[(3 * i) + 2];
            float top = first;
            float height = Math.Max(count, least);
            if (top + height > viewportHeight)
            {
                top = Math.Max(0, viewportHeight - height);
            }

            switch (lane)
            {
                case Find:
                    canvas.FillRect(left + 1, top, half, height, colors.RulerFind);
                    break;
                case Warning:
                    canvas.FillRect(left + 1 + half, top, half, height, colors.RulerWarning);
                    break;
                case Error:
                    canvas.FillRect(left + 1 + half, top, half, height, colors.RulerError);
                    break;
                case Added or Changed or Deleted or Conflict:
                    // X part 8: a repository's changes, a thin strip at the ruler's left edge (the gutter's colours).
                    canvas.FillRect(left + 1, top, Math.Max(2, (width - 1) / 4), height, EditorRenderer.LineMarkColor(lane switch
                    {
                        Added => LineMarkKind.Added,
                        Changed => LineMarkKind.Changed,
                        Deleted => LineMarkKind.Deleted,
                        _ => LineMarkKind.Conflict,
                    }, colors));
                    break;
                default:
                    canvas.FillRect(left + 1, top, width - 1, Math.Min(height, 2), colors.RulerCaret);
                    break;
            }

            MarksDrawn++;
        }
    }

    /// <summary>The units of ruler height a unit of content height takes (the whole content fits the viewport's height).</summary>
    internal static float Scale(EditorModel model, float viewportHeight) => viewportHeight / Math.Max(model.ContentHeight, viewportHeight);

    /// <summary>Returns the pooled buckets.</summary>
    public void Dispose()
    {
        if (_buckets.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buckets);
            _buckets = [];
        }

        if (_runs.Length > 0)
        {
            ArrayPool<int>.Shared.Return(_runs);
            _runs = [];
        }

        _keyModel = null;
        _keyBuckets = -1;
    }

    // Buckets the marks again when what they show changed: one pass over each sorted list, then one over the buckets.
    private void Rebuild(EditorModel model, float viewportHeight)
    {
        var snapshot = model.Snapshot;
        var diagnostics = model.DiagnosticsArray;
        var hits = model.Find.HighlightedStarts(out var findList);
        int buckets = Math.Max(1, (int)MathF.Ceiling(viewportHeight));
        float content = model.ContentHeight;
        if (ReferenceEquals(model, _keyModel) && snapshot.Version == _keyVersion && ReferenceEquals(diagnostics, _keyDiagnostics) && ReferenceEquals(findList, _keyFind)
            && model.SelectionStamp == _keySelections && model.FoldsStamp == _keyFolds && model.RowCount == _keyRows && content == _keyContent && buckets == _keyBuckets
            && model.LineMarksVersion == _keyMarks)
        {
            return;
        }

        _keyModel = model;
        _keyVersion = snapshot.Version;
        _keyDiagnostics = diagnostics;
        _keyFind = findList;
        _keySelections = model.SelectionStamp;
        _keyFolds = model.FoldsStamp;
        _keyRows = model.RowCount;
        _keyContent = content;
        _keyBuckets = buckets;
        _keyMarks = model.LineMarksVersion;
        Rebuilds++;
        if (_buckets.Length < buckets)
        {
            if (_buckets.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_buckets);
            }

            _buckets = ArrayPool<byte>.Shared.Rent(buckets);
        }

        var marks = _buckets.AsSpan(0, buckets);
        marks.Clear();
        float rowHeight = model.Metrics.LineHeight * Scale(model, viewportHeight);
        int length = snapshot.Length;
        foreach (ref readonly var diagnostic in diagnostics.AsSpan())
        {
            byte kind = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => Error,
                DiagnosticSeverity.Warning => Warning,
                _ => 0,
            };
            if (kind != 0)
            {
                marks[Bucket(model, snapshot, Math.Clamp(diagnostic.Start, 0, length), rowHeight, buckets)] |= kind;
            }
        }

        foreach (int start in hits)
        {
            marks[Bucket(model, snapshot, Math.Clamp(start, 0, length), rowHeight, buckets)] |= Find;
        }

        foreach (ref readonly var selection in model.Selections)
        {
            marks[Bucket(model, snapshot, Math.Clamp(selection.Caret, 0, length), rowHeight, buckets)] |= Caret;
        }

        int lines = snapshot.LineCount;
        foreach (ref readonly var mark in model.LineMarks)
        {
            if (mark.IsGlyph)
            {
                continue;                                    // a test's ▶, ✓ or ✗ stays in the gutter (X part 9)
            }

            byte kind = mark.Kind switch
            {
                LineMarkKind.Added => Added,
                LineMarkKind.Changed => Changed,
                LineMarkKind.Deleted => Deleted,
                _ => Conflict,
            };
            int firstLine = Math.Clamp(mark.Line, 0, lines - 1);
            int from = Math.Clamp((int)(model.RowOfLine(firstLine) * rowHeight), 0, buckets - 1);
            int to = Math.Clamp((int)(model.RowOfLine(Math.Clamp(mark.LastLine, 0, lines - 1)) * rowHeight), 0, buckets - 1);
            for (int b = from; b <= to; b++)
            {
                marks[b] |= kind;
            }
        }

        // The runs, lane by lane (find, the diagnostics, the carets, the repository's marks), each a run of equal neighbours.
        _runCount = 0;
        AddRuns(marks, Find);
        AddRuns(marks, Error | Warning);
        AddRuns(marks, Caret);
        AddRuns(marks, Added | Changed | Deleted | Conflict);
    }

    private static int Bucket(EditorModel model, ITextSnapshot snapshot, int position, float rowHeight, int buckets) =>
        Math.Clamp((int)(model.RowOfLine(snapshot.GetLineIndex(position)) * rowHeight), 0, buckets - 1);

    private void AddRuns(ReadOnlySpan<byte> marks, int mask)
    {
        int at = 0;
        while (at < marks.Length)
        {
            int lane = Lane(marks[at], mask);
            if (lane == 0)
            {
                at++;
                continue;
            }

            int first = at;
            while (at < marks.Length && Lane(marks[at], mask) == lane)
            {
                at++;
            }

            if ((3 * _runCount) + 3 > _runs.Length)
            {
                var bigger = ArrayPool<int>.Shared.Rent(Math.Max(48, _runs.Length * 2));
                _runs.AsSpan(0, 3 * _runCount).CopyTo(bigger);
                if (_runs.Length > 0)
                {
                    ArrayPool<int>.Shared.Return(_runs);
                }

                _runs = bigger;
            }

            _runs[3 * _runCount] = first;
            _runs[(3 * _runCount) + 1] = at - first;
            _runs[(3 * _runCount) + 2] = lane;
            _runCount++;
        }
    }

    // A bucket's mark in a lane: an error wins over a warning in the diagnostics' lane.
    private static int Lane(byte mark, int mask)
    {
        int value = mark & mask;
        return (value & Error) != 0 ? Error
            : (value & Conflict) != 0 ? Conflict
            : (value & Deleted) != 0 ? Deleted
            : (value & Changed) != 0 ? Changed
            : value;
    }
}
