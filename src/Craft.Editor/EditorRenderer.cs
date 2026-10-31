// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Draws an <see cref="EditorModel"/> through an <see cref="IEditorCanvas"/>: only the visible lines (plus one), in the
/// order background, current line, find matches, selection, bracket match, text, the diagnostics' squiggles, gutter,
/// caret, then the popups (completion list, tooltip). The visible range is
/// classified in one call per frame into a reused buffer; each line's runs (its text cut where its style changes, tabs
/// expanded) are kept while the line's characters and spans stay the same (compared as spans, whatever the snapshot's
/// version), so scrolling in place and the caret's blink make no strings and a keystroke rebuilds only its own line, and a
/// landed background parse (<see cref="EditorModel.Reclassified"/>) rebuilds only the lines whose spans it changed. Before
/// classifying, a frame waits for the language's parse up to the options' budget, never longer. One renderer per drawn
/// surface; dispose it to return its buffers.
/// </summary>
public sealed class EditorRenderer : IDisposable
{
    private readonly TokenSpanBuffer _spans = new();
    private readonly Dictionary<int, LineRuns> _lines = [];
    private readonly Dictionary<int, string> _numbers = [];
    private readonly EditorPopupRenderer _popups = new();
    private TokenSpan[] _clipped = ArrayPool<TokenSpan>.Shared.Rent(64);
    private EditorModel? _model;
    private ITheme? _theme;
    private bool _disposed;

    /// <summary>How many run strings it has made since it was created (the run cache's misses).</summary>
    public long RunStringsBuilt { get; private set; }

    /// <summary>The frames whose language parse did not land within the wait budget (drawn with the changed text plain).</summary>
    public long ParseWaitsExceeded { get; private set; }

    /// <summary>Whether the last frame's spans were final (its parse had landed, or it has no language).</summary>
    public bool LastFrameClassified { get; private set; } = true;

    /// <summary>The lines the last frame drew.</summary>
    public int LinesDrawn { get; private set; }

    /// <summary>The visible line range of the last frame.</summary>
    public LineRange LastVisible { get; private set; }

    /// <summary>The squiggle segments the last frame drew (only for diagnostics on its visible lines).</summary>
    public int SquiggleSegmentsDrawn { get; private set; }

    /// <summary>
    /// Draws <paramref name="model"/> scrolled to (<paramref name="scrollX"/>, <paramref name="scrollY"/>) in a viewport of
    /// <paramref name="viewportWidth"/> by <paramref name="viewportHeight"/>, with the caret when <paramref name="caretVisible"/>.
    /// </summary>
    public void Draw(IEditorCanvas canvas, EditorModel model, float scrollX, float scrollY, float viewportWidth, float viewportHeight, bool caretVisible)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(_disposed, this);
        model.UpdateViewport(scrollX, scrollY, viewportWidth, viewportHeight);
        scrollX = model.ScrollX;
        scrollY = model.ScrollY;
        var snapshot = model.Snapshot;
        var theme = model.Theme;
        if (!ReferenceEquals(model, _model))
        {
            _numbers.Clear();
            _lines.Clear();
        }

        if (!ReferenceEquals(model, _model) || !ReferenceEquals(theme, _theme))
        {
            _lines.Clear();
            _model = model;
            _theme = theme;
        }

        var colors = theme.Colors;
        var metrics = model.Metrics;
        float cell = metrics.CellWidth;
        float height = metrics.LineHeight;
        float textLeft = model.TextLeft;
        float gutterWidth = model.GutterWidth;
        int tabSize = model.Options.TabSize;
        var selection = model.Selection;
        int caretLine = snapshot.GetLineIndex(selection.Caret);
        var visible = model.VisibleLines(scrollY, viewportHeight);
        LastVisible = visible;
        TrimCaches(visible);

        canvas.FillRect(0, 0, viewportWidth, viewportHeight, colors.Background);
        if (caretLine >= visible.First && caretLine <= visible.Last)
        {
            canvas.FillRect(textLeft, (caretLine * height) - scrollY, Math.Max(0, viewportWidth - textLeft), height, colors.CurrentLine);
        }

        var firstLine = snapshot.GetLine(visible.First);
        var lastLine = snapshot.GetLine(visible.Last);
        _spans.Clear();
        LastFrameClassified = model.WaitForClassification(snapshot);
        if (!LastFrameClassified)
        {
            ParseWaitsExceeded++;
        }

        model.LanguageSession?.Classify(snapshot, firstLine.Start, lastLine.EndIncludingBreak - firstLine.Start, _spans);
        var spans = _spans.Spans;
        var find = model.Find;
        bool findShown = find.IsHighlighting && find.Searchable;
        int queryLength = find.Query.Length;
        var bracket = model.MatchingBrackets;
        int caretColumn = 0;
        var diagnostics = model.Diagnostics;
        int firstDiagnostic = diagnostics.IsEmpty ? 0 : model.FirstDiagnosticReaching(firstLine.Start);
        SquiggleSegmentsDrawn = 0;

        char[]? buffer = null;
        char[]? expanded = null;
        int spanIndex = 0;
        try
        {
            for (int index = visible.First; index <= visible.Last; index++)
            {
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                float top = (index * height) - scrollY;
                float left = textLeft - scrollX;

                while (spanIndex < spans.Length && spans[spanIndex].End <= line.Start)
                {
                    spanIndex++;
                }

                var runs = Runs(index, line, text, spans, spanIndex, theme, tabSize, ref expanded);
                model.NoteLineColumns(runs.Columns);

                if (findShown)
                {
                    int from = 0;
                    int found;
                    while ((found = find.NextIn(text, from)) >= 0)
                    {
                        int start = TextReading.ColumnOf(text, found, tabSize);
                        int end = TextReading.ColumnOf(text, found + queryLength, tabSize);
                        canvas.FillRect(left + (start * cell), top, (end - start) * cell, height, colors.FindMatch);
                        from = found + Math.Max(1, queryLength);
                    }
                }

                if (!selection.IsEmpty && selection.Start <= line.EndIncludingBreak && selection.End >= line.Start
                    && !(selection.End == line.Start && index > snapshot.GetLineIndex(selection.Start)))
                {
                    int start = selection.Start <= line.Start ? 0 : TextReading.ColumnOf(text, selection.Start - line.Start, tabSize);
                    int end = selection.End > line.End ? runs.Columns + 1 : TextReading.ColumnOf(text, selection.End - line.Start, tabSize);
                    if (end > start)
                    {
                        canvas.FillRect(left + (start * cell), top, (end - start) * cell, height, colors.Selection);
                    }
                }

                if (bracket is { } match)
                {
                    foreach (int at in (ReadOnlySpan<int>)[match.Open, match.Close])
                    {
                        if (at >= line.Start && at < line.End)
                        {
                            int column = TextReading.ColumnOf(text, at - line.Start, tabSize);
                            canvas.FillRect(left + (column * cell), top, cell, height, colors.BracketMatch);
                        }
                    }
                }

                var drawn = runs.Items;
                for (int r = 0; r < runs.Count; r++)
                {
                    ref readonly var run = ref drawn[r];
                    float x = left + (run.Column * cell);
                    if (x > viewportWidth || x + (run.Text.Length * cell) < textLeft)
                    {
                        continue;
                    }

                    canvas.DrawText(run.Text, x, top, run.Style);
                }

                for (int d = firstDiagnostic; d < diagnostics.Length && diagnostics[d].Start <= line.End; d++)
                {
                    DrawSquiggle(canvas, diagnostics[d], line, text, left, top, metrics, tabSize, textLeft, viewportWidth, colors);
                }

                if (index == caretLine)
                {
                    caretColumn = TextReading.ColumnOf(text, selection.Caret - line.Start, tabSize);
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
            TextReading.Return(expanded);
        }

        LinesDrawn = visible.Count;

        canvas.FillRect(0, 0, gutterWidth, viewportHeight, colors.Gutter);
        var plainNumber = new TextStyle(colors.GutterText);
        var currentNumber = new TextStyle(colors.GutterCurrentText);
        for (int index = visible.First; index <= visible.Last; index++)
        {
            string number = Number(index + 1);
            float x = gutterWidth - ((model.Options.GutterPaddingCells > 0 ? 1 : 0) + number.Length) * cell;
            canvas.DrawText(number, x, (index * height) - scrollY, index == caretLine ? currentNumber : plainNumber);
        }

        if (caretVisible && caretLine >= visible.First && caretLine <= visible.Last)
        {
            float x = textLeft - scrollX + (caretColumn * cell);
            if (x >= textLeft - 1 && x <= viewportWidth)
            {
                float top = (caretLine * height) - scrollY;
                if (model.Overwrite)
                {
                    canvas.FillRect(x, top, cell, height, (colors.Caret & 0x00FFFFFFu) | 0x80000000u);
                }
                else
                {
                    canvas.FillRect(x, top, model.Options.CaretWidth, height, colors.Caret);
                }
            }
        }

        _popups.Draw(canvas, model);
        model.NotePainted();
    }

    /// <summary>A diagnostic's part on one line: a wave (errors, warnings) or dots (information) under its columns; hidden ones are not drawn.</summary>
    private void DrawSquiggle(IEditorCanvas canvas, in EditorDiagnostic diagnostic, in TextLine line, ReadOnlySpan<char> text, float left, float top,
        FontMetrics metrics, int tabSize, float textLeft, float viewportWidth, EditorColors colors)
    {
        if (diagnostic.Severity == DiagnosticSeverity.Hidden || diagnostic.End < line.Start)
        {
            return;
        }

        int start = Math.Max(diagnostic.Start, line.Start);
        int end = Math.Min(diagnostic.End, line.End);
        int startColumn;
        int endColumn;
        if (end > start)
        {
            startColumn = TextReading.ColumnOf(text, start - line.Start, tabSize);
            endColumn = TextReading.ColumnOf(text, end - line.Start, tabSize);
        }
        else if (diagnostic.Length == 0 || diagnostic.Start >= line.Start)
        {
            // A point (a missing semicolon), or a span that starts at the line's end: one cell wide.
            startColumn = TextReading.ColumnOf(text, Math.Min(start, line.End) - line.Start, tabSize);
            endColumn = startColumn + 1;
        }
        else
        {
            return;
        }

        float cell = metrics.CellWidth;
        float x0 = Math.Max(textLeft, left + (startColumn * cell));
        float x1 = Math.Min(viewportWidth, left + (endColumn * cell));
        if (x1 <= x0)
        {
            return;
        }

        uint color = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => colors.Error,
            DiagnosticSeverity.Warning => colors.Warning,
            _ => colors.Info,
        };
        float baseY = top + metrics.LineHeight - 1.5f;
        float step = Math.Max(2f, cell / 3f);
        bool dotted = diagnostic.Severity == DiagnosticSeverity.Info;
        bool up = false;
        for (float x = x0; x < x1; x += step)
        {
            float next = Math.Min(x1, x + step);
            if (dotted)
            {
                if (!up)
                {
                    canvas.DrawLine(x, baseY, next, baseY, color, 1);
                    SquiggleSegmentsDrawn++;
                }
            }
            else
            {
                canvas.DrawLine(x, up ? baseY - 2 : baseY, next, up ? baseY : baseY - 2, color, 1);
                SquiggleSegmentsDrawn++;
            }

            up = !up;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spans.Dispose();
        ArrayPool<TokenSpan>.Shared.Return(_clipped, clearArray: true);
        _clipped = [];
        _lines.Clear();
        _numbers.Clear();
    }

    private string Number(int value)
    {
        if (!_numbers.TryGetValue(value, out var text))
        {
            text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _numbers[value] = text;
        }

        return text;
    }

    private void TrimCaches(LineRange visible)
    {
        int keep = Math.Max(64, visible.Count * 4);
        if (_lines.Count > keep)
        {
            foreach (var key in _lines.Keys)
            {
                if (key < visible.First || key > visible.Last)
                {
                    _lines.Remove(key);
                }
            }
        }

        if (_numbers.Count > keep)
        {
            foreach (var key in _numbers.Keys)
            {
                if (key < visible.First + 1 || key > visible.Last + 1)
                {
                    _numbers.Remove(key);
                }
            }
        }
    }

    private LineRuns Runs(int index, in TextLine line, ReadOnlySpan<char> text, ReadOnlySpan<TokenSpan> spans, int spanIndex, ITheme theme, int tabSize, ref char[]? expanded)
    {
        // This line's spans, clipped to it and made relative to its start: the key the cached runs were built from.
        int clippedCount = 0;
        for (int i = spanIndex; i < spans.Length && spans[i].Start < line.End; i++)
        {
            int start = Math.Max(spans[i].Start, line.Start);
            int end = Math.Min(spans[i].End, line.End);
            if (end <= start)
            {
                continue;
            }

            if (clippedCount == _clipped.Length)
            {
                var bigger = ArrayPool<TokenSpan>.Shared.Rent(_clipped.Length * 2);
                _clipped.AsSpan(0, clippedCount).CopyTo(bigger);
                ArrayPool<TokenSpan>.Shared.Return(_clipped, clearArray: true);
                _clipped = bigger;
            }

            _clipped[clippedCount++] = new TokenSpan(start - line.Start, end - start, spans[i].Class);
        }

        var clipped = _clipped.AsSpan(0, clippedCount);
        if (_lines.TryGetValue(index, out var cached) && SameSpans(cached.Spans, clipped) && text.SequenceEqual(cached.Source))
        {
            return cached;
        }

        var runs = Build(text, clipped, theme, tabSize, ref expanded);
        runs.Spans = clipped.ToArray();
        runs.Source = text.ToArray();
        _lines[index] = runs;
        return runs;
    }

    private static bool SameSpans(TokenSpan[] cached, ReadOnlySpan<TokenSpan> now)
    {
        if (cached.Length != now.Length)
        {
            return false;
        }

        for (int i = 0; i < now.Length; i++)
        {
            var a = cached[i];
            var b = now[i];
            if (a.Start != b.Start || a.Length != b.Length || !(ReferenceEquals(a.Class, b.Class) || string.Equals(a.Class, b.Class, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private LineRuns Build(ReadOnlySpan<char> text, ReadOnlySpan<TokenSpan> spans, ITheme theme, int tabSize, ref char[]? expanded)
    {
        var plain = new TextStyle(theme.Colors.Foreground);
        var runs = new LineRuns();
        int column = 0;
        int position = 0;
        int spanAt = 0;
        int runStart = 0;
        var runStyle = plain;
        while (position < text.Length)
        {
            // The next segment: a span, or the plain text up to the next span. Neighbours of the same style join one run.
            TextStyle style;
            int end;
            if (spanAt < spans.Length && spans[spanAt].Start <= position)
            {
                style = theme.StyleOf(spans[spanAt].Class);
                end = Math.Min(text.Length, Math.Max(position + 1, spans[spanAt].End));
                spanAt++;
            }
            else
            {
                style = plain;
                end = spanAt < spans.Length ? Math.Min(text.Length, spans[spanAt].Start) : text.Length;
            }

            if (position > runStart && style != runStyle)
            {
                column = AddRun(runs, text[runStart..position], column, runStyle, tabSize, ref expanded);
                runStart = position;
            }

            runStyle = style;
            position = end;
        }

        if (position > runStart)
        {
            column = AddRun(runs, text[runStart..position], column, runStyle, tabSize, ref expanded);
        }

        runs.Columns = column;
        return runs;
    }

    private int AddRun(LineRuns runs, ReadOnlySpan<char> segment, int column, in TextStyle style, int tabSize, ref char[]? expanded)
    {
        int start = column;
        int end = column;
        foreach (char c in segment)
        {
            end = TextReading.Advance(end, c, tabSize);
        }

        if (segment.IndexOfAnyExcept(' ', '\t') < 0)
        {
            return end;
        }

        // Tabs expanded to spaces, so a run's characters fill its cells.
        int length = segment.Length + (end - start);
        if (expanded is null || expanded.Length < length)
        {
            TextReading.Return(expanded);
            expanded = ArrayPool<char>.Shared.Rent(length);
        }

        int written = 0;
        int at = start;
        foreach (char c in segment)
        {
            if (c == '\t')
            {
                int next = TextReading.Advance(at, c, tabSize);
                expanded.AsSpan(written, next - at).Fill(' ');
                written += next - at;
                at = next;
            }
            else
            {
                expanded[written++] = c;
                at = TextReading.Advance(at, c, tabSize);
            }
        }

        runs.Add(new Run(new string(expanded, 0, written), start, style));
        RunStringsBuilt++;
        return end;
    }

    private readonly record struct Run(string Text, int Column, TextStyle Style);

    private sealed class LineRuns
    {
        private Run[] _items = [];

        public TokenSpan[] Spans { get; set; } = [];

        public char[] Source { get; set; } = [];

        public int Columns { get; set; }

        public int Count { get; private set; }

        public Run[] Items => _items;

        public void Add(Run run)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Math.Max(4, _items.Length * 2));
            }

            _items[Count++] = run;
        }
    }
}
