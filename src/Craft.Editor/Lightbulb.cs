// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>What the lightbulb asks about: a range of the current text and the diagnostics there (the code actions' context without its document).</summary>
/// <param name="Start">The range's start (the diagnostics', else the selection's).</param>
/// <param name="End">Its end.</param>
/// <param name="Diagnostics">The diagnostics at the caret, else those on its line, in order.</param>
public sealed record LightbulbQuery(int Start, int End, IReadOnlyList<EditorDiagnostic> Diagnostics);

/// <summary>
/// The lightbulb in the gutter (X part 9, <c>CRAFT_LIGHTBULB</c>): on the caret's line when code actions exist for the caret
/// or a diagnostic on that line. <c>CRAFT_LIGHTBULB_DELAY_MS</c> after the caret and the diagnostics stop changing,
/// the host's <see cref="Probe"/> is asked off the UI thread; the answer is kept per text version and range, so a caret
/// that comes back asks nothing. A click on it opens Quick fix (<see cref="EditorModel.QuickFixRequested"/>).
/// </summary>
public sealed class Lightbulb : IDisposable
{
    private const int KeptAnswers = 64;
    private readonly EditorModel _model;
    private readonly List<(int Version, int Start, int End, int Diagnostics, bool Has)> _answers = [];
    private EditorModel.DebounceTimer? _timer;
    private LightbulbQuery? _query;
    private bool _disposed;

    internal Lightbulb(EditorModel model)
    {
        _model = model;
        model.CaretMoved += OnChanged;
        model.DiagnosticsChanged += OnChanged;
    }

    /// <summary>
    /// Whether code actions exist for a query (the host asks its code action providers); called off the UI thread. Null:
    /// no lightbulb.
    /// </summary>
    public Func<LightbulbQuery, bool>? Probe { get; set; }

    /// <summary>The line the lightbulb shows on, or -1.</summary>
    public int Line { get; private set; } = -1;

    /// <summary>The questions sent to <see cref="Probe"/> (a caret that comes back to a range already asked sends none).</summary>
    public int Asks { get; private set; }

    /// <summary>The query of the lightbulb shown (what a click fixes), or null.</summary>
    public LightbulbQuery? Query => Line >= 0 ? _query : null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.CaretMoved -= OnChanged;
        _model.DiagnosticsChanged -= OnChanged;
        _timer?.Dispose();
    }

    /// <summary>Asks again now for the caret's place (the tests, and a host whose providers changed).</summary>
    public void Refresh()
    {
        _answers.Clear();
        Schedule();
    }

    /// <summary>Whether the view point is on the lightbulb (its cell at the gutter's left, on its line's first row).</summary>
    internal bool HitTest(float viewX, float viewY)
    {
        if (Line < 0 || viewX < 0 || viewX >= _model.Metrics.CellWidth * 1.2f)
        {
            return false;
        }

        float top = (_model.RowOfLine(Line) * _model.Metrics.LineHeight) - _model.ScrollY;
        return viewY >= top && viewY < top + _model.Metrics.LineHeight;
    }

    /// <summary>A click on it: the caret goes to the problem it is for (when it was on the line, not at the caret), then Quick fix opens.</summary>
    internal void Press()
    {
        if (Query is { Diagnostics.Count: > 0 } query)
        {
            int caret = _model.Selection.Caret;
            bool atCaret = false;
            foreach (var diagnostic in query.Diagnostics)
            {
                atCaret |= caret >= diagnostic.Start && caret <= diagnostic.End;
            }

            if (!atCaret)
            {
                _model.Select(query.Diagnostics[0].Start, query.Diagnostics[0].Start);
            }
        }

        _model.RequestQuickFix();
    }

    private void OnChanged(object? sender, EventArgs e) => Schedule();

    private void Schedule()
    {
        if (_disposed)
        {
            return;
        }

        if (!_model.Options.Lightbulb || Probe is null || _model.IsReadOnly || _model.HasMultipleCarets)
        {
            Hide();
            return;
        }

        var query = QueryAtCaret();
        int version = _model.Snapshot.Version;
        int caretLine = _model.CaretLine;
        if (Line >= 0 && Line != caretLine)
        {
            Hide();
        }

        if (Find(version, query) is { } has)
        {
            Show(query, version, has ? caretLine : -1);
            return;
        }

        _timer ??= new EditorModel.DebounceTimer(_model);
        _timer.Schedule(_model.Options.LightbulbDelayMilliseconds, Ask);
    }

    // Off the UI thread: the probe; back on it, kept and shown when the text and the caret's range are still the same.
    private void Ask()
    {
        if (_disposed || Probe is not { } probe)
        {
            return;
        }

        var query = QueryAtCaret();
        int version = _model.Snapshot.Version;
        int line = _model.CaretLine;
        Asks++;
        _ = Task.Run(() =>
        {
            try
            {
                return probe(query);
            }
#pragma warning disable CA1031 // A provider's failure means no lightbulb, never a crash.
            catch (Exception)
#pragma warning restore CA1031
            {
                return false;
            }
        }).ContinueWith(done => _model.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            bool has = done.IsCompletedSuccessfully && done.Result;
            Keep(version, query, has);
            if (_model.Snapshot.Version == version && Same(QueryAtCaret(), query))
            {
                Show(query, version, has ? line : -1);
            }
        }), TaskScheduler.Default);
    }

    private LightbulbQuery QueryAtCaret()
    {
        int caret = _model.Selection.Caret;
        var diagnostics = _model.DiagnosticsAt(caret);
        if (diagnostics.Count == 0)
        {
            diagnostics = _model.DiagnosticsOnLine(_model.CaretLine);
        }

        if (diagnostics.Count > 0)
        {
            int start = int.MaxValue, end = 0;
            foreach (var d in diagnostics)
            {
                start = Math.Min(start, d.Start);
                end = Math.Max(end, d.End);
            }

            return new LightbulbQuery(start, end, diagnostics);
        }

        return new LightbulbQuery(_model.Selection.Start, _model.Selection.End, []);
    }

    private static bool Same(LightbulbQuery a, LightbulbQuery b) =>
        a.Start == b.Start && a.End == b.End && a.Diagnostics.Count == b.Diagnostics.Count;

    private bool? Find(int version, LightbulbQuery query)
    {
        foreach (var answer in _answers)
        {
            if (answer.Version == version && answer.Start == query.Start && answer.End == query.End && answer.Diagnostics == query.Diagnostics.Count)
            {
                return answer.Has;
            }
        }

        return null;
    }

    private void Keep(int version, LightbulbQuery query, bool has)
    {
        // Only the current version's answers are worth keeping.
        _answers.RemoveAll(a => a.Version != version);
        if (_answers.Count >= KeptAnswers)
        {
            _answers.RemoveAt(0);
        }

        _answers.Add((version, query.Start, query.End, query.Diagnostics.Count, has));
    }

    private void Show(LightbulbQuery query, int version, int line)
    {
        _query = query;
        _ = version;
        if (Line != line)
        {
            Line = line;
            _model.Invalidate();
        }
    }

    private void Hide()
    {
        _timer?.Cancel();
        if (Line >= 0)
        {
            Line = -1;
            _model.Invalidate();
        }
    }
}
