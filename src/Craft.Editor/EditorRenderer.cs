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
/// <para>
/// B1 (the owner's third Windows run): each line's runs are a <see cref="StyledLine"/>, drawn as one piece on a canvas that
/// implements <see cref="IStyledLineCanvas"/> (one layout per shown line, kept on the line while its text, its spans, the
/// theme and the font stay; released when the line leaves the cache, which keeps the lines on screen and
/// <see cref="Core.CraftOptions.PaintCacheLines"/> above and below them), else run by run as before. The carets can be
/// drawn alone (<see cref="DrawCarets"/>), on a layer of their own, so the caret's blink repaints no text.
/// </para>
/// <para>
/// The X restart (piece 2a): after the gutter, the sticky headers (<see cref="StickyScrollLayer"/>) and the overview ruler
/// (<see cref="OverviewRuler"/>) are drawn over the text, each its own layer; the hover card and a fold's preview are drawn
/// by the popups from <see cref="EditorModel.HoverCard"/>. A line outside the screen that a layer shows (a sticky header, a
/// previewed line) is classified alone and kept in the run cache with the lines on screen (<see cref="LineRunsFor"/>).
/// </para>
/// </summary>
public sealed class EditorRenderer : IDisposable
{
    private readonly TokenSpanBuffer _spans = new();
    private readonly TokenSpanBuffer _runSpans = new();
    private readonly TokenSpanBuffer _lineSpans = new();
    private readonly OverviewRuler _ruler = new();
    private readonly List<(int Column, float Top, int Line)> _carets = [];
    private readonly List<int> _drawnRows = [];
    private readonly Dictionary<int, LineRuns> _lines = [];
    private readonly Dictionary<int, string> _numbers = [];
    private readonly GhostTextRenderer _ghost = new();
    private TokenSpan[] _clipped = ArrayPool<TokenSpan>.Shared.Rent(64);
    private EditorModel? _model;
    private ITheme? _theme;
    private bool _disposed;

    /// <summary>The overview ruler it draws (its counters: rebuilds, marks).</summary>
    public OverviewRuler OverviewRuler => _ruler;

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

    /// <summary>The ghost lines of an inline completion drawn in the last frame.</summary>
    public int GhostLinesDrawn => _ghost.LinesDrawn;

    /// <summary>Whether the inline completion's hint bar was drawn in the last frame.</summary>
    public bool GhostHintDrawn => _ghost.HintDrawn;

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
        Step = StepStart;
        if (!ReferenceEquals(model, _model))
        {
            _numbers.Clear();
        }

        if (!ReferenceEquals(model, _model) || !ReferenceEquals(theme, _theme))
        {
            ReleaseLayouts();
            _model = model;
            _theme = theme;
        }

        var colors = theme.Colors;
        var metrics = model.Metrics;
        var options = model.Options;
        float cell = metrics.CellWidth;
        float height = metrics.LineHeight;
        float textLeft = model.TextLeft;
        float gutterWidth = model.GutterWidth;
        int tabSize = options.TabSize;
        var folding = model.Folding;
        bool folds = folding.IsAvailable;
        bool hidden = folding.Map.HasHidden;
        var selections = model.Selections;
        var rows = model.VisibleRows(scrollY, viewportHeight);
        var visible = new LineRange(model.LineAtRow(rows.First), model.LineAtRow(rows.Last));
        LastVisible = visible;
        TrimCaches(visible, options.PaintCacheLines);

        var firstLine = snapshot.GetLine(visible.First);
        var lastLine = snapshot.GetLine(visible.Last);
        canvas.FillRect(0, 0, viewportWidth, viewportHeight, colors.Background);

        // The caret lines' fill, under everything else: one per line holding a caret.
        int firstSelection = FirstSelectionReaching(selections, firstLine.Start);
        int previousCaretLine = -1;
        for (int i = firstSelection; i < selections.Length && selections[i].Start <= lastLine.EndIncludingBreak; i++)
        {
            int line = snapshot.GetLineIndex(selections[i].Caret);
            if (line != previousCaretLine && line >= visible.First && line <= visible.Last && !(hidden && folding.Map.IsHidden(line)))
            {
                canvas.FillRect(textLeft, (model.RowOfLine(line) * height) - scrollY, Math.Max(0, viewportWidth - textLeft), height, colors.CurrentLine);
            }

            previousCaretLine = line;
        }

        Step = StepClassify;
        _spans.Clear();
        LastFrameClassified = model.WaitForClassification(snapshot);
        if (!LastFrameClassified)
        {
            ParseWaitsExceeded++;
        }

        ClassifyShown(model, snapshot, visible);
        var spans = _spans.Spans;
        var find = model.Find;
        bool findShown = FindHighlightLayer.IsShown(find);
        var bracket = options.BracketHighlight ? model.MatchingBrackets : null;
        string? word = options.WordHighlight ? model.HighlightedWord : null;
        var diagnostics = model.Diagnostics;
        int firstDiagnostic = diagnostics.IsEmpty ? 0 : model.FirstDiagnosticReaching(firstLine.Start);
        SquiggleSegmentsDrawn = 0;
        GuidesDrawn = 0;
        WordHighlightsDrawn = 0;
        _carets.Clear();
        _drawnRows.Clear();
        int guidePrevious = 0;

        char[]? buffer = null;
        char[]? expanded = null;
        int spanIndex = 0;
        int selectionIndex = firstSelection;
        int drawn = 0;
        Step = StepLines;
        try
        {
            int row = rows.First;
            for (int index = visible.First; index <= visible.Last && index < snapshot.LineCount; index = model.NextShownLine(index), row++)
            {
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                float top = (row * height) - scrollY;
                float left = textLeft - scrollX;
                drawn++;
                _drawnRows.Add(index);

                while (spanIndex < spans.Length && spans[spanIndex].End <= line.Start)
                {
                    spanIndex++;
                }

                var runs = Runs(index, line, text, spans, spanIndex, theme, tabSize, ref expanded);
                model.NoteLineColumns(runs.Columns);

                model.LineBackdrop?.DrawLine(canvas, index, left, top, cell, height, viewportWidth);
                if (findShown)
                {
                    FindHighlightLayer.DrawLine(canvas, find, text, left, top, cell, height, tabSize, colors.FindMatch);
                }

                if (word is not null)
                {
                    DrawWord(canvas, text, word, left, top, cell, height, tabSize, colors.WordHighlight);
                }

                while (selectionIndex < selections.Length && selections[selectionIndex].End < line.Start)
                {
                    selectionIndex++;
                }

                for (int j = selectionIndex; j < selections.Length && selections[j].Start <= line.EndIncludingBreak; j++)
                {
                    var selection = selections[j];
                    if (!selection.IsEmpty && selection.End >= line.Start
                        && !(selection.End == line.Start && index > snapshot.GetLineIndex(selection.Start)))
                    {
                        int start = selection.Start <= line.Start ? 0 : TextReading.ColumnOf(text, selection.Start - line.Start, tabSize);
                        int end = selection.End > line.End ? runs.Columns + 1 : TextReading.ColumnOf(text, selection.End - line.Start, tabSize);
                        if (end > start)
                        {
                            canvas.FillRect(left + (start * cell), top, (end - start) * cell, height, colors.Selection);
                        }
                    }

                    if (selection.Caret >= line.Start && selection.Caret <= line.End)
                    {
                        _carets.Add((TextReading.ColumnOf(text, selection.Caret - line.Start, tabSize), top, index));
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

                if (options.IndentGuides)
                {
                    guidePrevious = DrawGuides(canvas, model, snapshot, index, text, visible.Last, guidePrevious, left, top, cell, height, textLeft, tabSize, colors.IndentGuide, ref buffer);
                    text = TextReading.ReadLine(snapshot, line, ref buffer);    // the guides may have read other lines into the buffer
                }

                // B1: the line as one piece (one kept layout on a canvas that lays lines out), else run by run as before.
                StyledLines.Draw(canvas, runs.Line, left, top, cell, textLeft, viewportWidth);

                if (hidden && folding.Map.IsFolded(index))
                {
                    // The hidden lines' marker after the header's text.
                    float x = left + ((runs.Columns + 1) * cell);
                    if (x + (3 * cell) > textLeft && x < viewportWidth)
                    {
                        canvas.FillRect(x, top + 2, 3 * cell, height - 4, colors.FoldMarker);
                        canvas.DrawText(FoldMarkerText, x, top, new TextStyle(colors.GutterCurrentText));
                    }
                }

                for (int d = firstDiagnostic; d < diagnostics.Length && diagnostics[d].Start <= line.End; d++)
                {
                    DrawSquiggle(canvas, diagnostics[d], line, text, left, top, metrics, tabSize, textLeft, viewportWidth, colors);
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
            TextReading.Return(expanded);
        }

        LinesDrawn = drawn;

        Step = StepGutter;
        canvas.FillRect(0, 0, gutterWidth, viewportHeight, model.ShowLineNumbers ? colors.Gutter : colors.Background);
        var plainNumber = new TextStyle(colors.GutterText);
        var currentNumber = new TextStyle(colors.GutterCurrentText);
        float numbersRight = gutterWidth - (folds ? EditorModel.FoldGutterCells * cell : 0);
        int caretAt = 0;
        for (int r = 0; r < _drawnRows.Count; r++)
        {
            int index = _drawnRows[r];
            float top = ((rows.First + r) * height) - scrollY;
            while (caretAt < _carets.Count && _carets[caretAt].Line < index)
            {
                caretAt++;
            }

            bool current = caretAt < _carets.Count && _carets[caretAt].Line == index;
            string number = Number(index + 1);
            float x = numbersRight - ((options.GutterPaddingCells > 0 ? 1 : 0) + number.Length) * cell;
            if (model.ShowLineNumbers)
            {
                canvas.DrawText(number, x, top, current ? currentNumber : plainNumber);
            }
            if (folds)
            {
                bool folded = hidden && folding.Map.IsFolded(index);
                if (folded || folding.IsFoldable(index))
                {
                    DrawChevron(canvas, numbersRight + (cell * 0.8f), top + (height / 2), cell * 0.28f, folded, folded ? colors.GutterCurrentText : colors.FoldChevron);
                }
            }
        }

        Step = StepSticky;
        var sticky = model.StickyScroll;
        sticky.Draw(canvas, this, scrollX, viewportWidth);
        float stickyHeight = sticky.Height;
        Step = StepRuler;
        _ruler.Draw(canvas, model, viewportWidth, viewportHeight);

        Step = StepCarets;
        if (caretVisible)
        {
            foreach (var (column, top, _) in _carets)
            {
                if (top >= stickyHeight)
                {
                    DrawCaret(canvas, model, column, top, scrollX, viewportWidth);
                }
            }
        }

        Step = StepGhost;
        _ghost.Draw(canvas, model, scrollX, scrollY, viewportWidth, viewportHeight);
        Step = StepPopups;
        EditorPopupRenderer.Draw(canvas, model, this);
        model.NotePainted();
        Step = StepDone;
    }

    /// <summary>
    /// Whether the carets must be drawn with the text (<see cref="Draw"/> with the caret shown) rather than on a layer of
    /// their own: while ghost text, the completion list or a tooltip shows, which the text frame draws over the carets.
    /// </summary>
    public static bool CaretsOnTextLayer(EditorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.InlineCompletion.IsShown || model.Tooltip is not null || model.CompletionBox() is not null;
    }

    /// <summary>
    /// Draws only the carets of <paramref name="model"/> on the lines shown at (<paramref name="scrollX"/>,
    /// <paramref name="scrollY"/>): the caret layer's frame (B1), so a blink repaints no text. The same fills, at the same
    /// places, as <see cref="Draw"/> with the caret shown; it reads the model, not the last text frame, so the layer is
    /// right whichever of the two is drawn first. Allocates nothing once warm.
    /// </summary>
    public void DrawCarets(IEditorCanvas canvas, EditorModel model, float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(_disposed, this);
        model.UpdateViewport(scrollX, scrollY, viewportWidth, viewportHeight);
        scrollX = model.ScrollX;
        scrollY = model.ScrollY;
        var snapshot = model.Snapshot;
        float height = model.Metrics.LineHeight;
        int tabSize = model.Options.TabSize;
        var folding = model.Folding.Map;
        bool hidden = folding.HasHidden;
        var selections = model.Selections;
        var rows = model.VisibleRows(scrollY, viewportHeight);
        var visible = new LineRange(model.LineAtRow(rows.First), model.LineAtRow(rows.Last));
        var firstLine = snapshot.GetLine(visible.First);
        var lastLine = snapshot.GetLine(visible.Last);
        CaretsDrawnAlone = 0;
        float stickyHeight = model.StickyScroll.Height;
        char[]? buffer = null;
        try
        {
            for (int i = FirstSelectionReaching(selections, firstLine.Start); i < selections.Length && selections[i].Start <= lastLine.EndIncludingBreak; i++)
            {
                int caret = selections[i].Caret;
                int index = snapshot.GetLineIndex(caret);
                if (index < visible.First || index > visible.Last || (hidden && folding.IsHidden(index)))
                {
                    continue;
                }

                var line = snapshot.GetLine(index);
                float top = (model.RowOfLine(index) * height) - scrollY;
                if (top < stickyHeight)
                {
                    continue;           // under a sticky header
                }

                int column = TextReading.ColumnOf(TextReading.ReadLine(snapshot, line, ref buffer), caret - line.Start, tabSize);
                DrawCaret(canvas, model, column, top, scrollX, viewportWidth);
                CaretsDrawnAlone++;
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>The carets the last <see cref="DrawCarets"/> drew (or would have, inside the viewport).</summary>
    public int CaretsDrawnAlone { get; private set; }

    /// <summary>The step of the frame being drawn, or of the last one when it ended ("done"): a frame that throws is reported with it (B2, rule 90).</summary>
    public string Step { get; private set; } = StepDone;

    /// <summary>The lines the run cache keeps now (on screen and in the margin).</summary>
    public int LinesHeld => _lines.Count;

    /// <summary>The kept lines that hold a canvas's layout now (a blank line has none); walks the cache: telemetry and tests, not the frame.</summary>
    public int LayoutsHeld
    {
        get
        {
            int held = 0;
            foreach (var runs in _lines.Values)
            {
                held += runs.Line.Layout is null ? 0 : 1;
            }

            return held;
        }
    }

    /// <summary>
    /// Drops every cached line and the layouts a canvas keeps on them (memory pressure, a lost device, another theme or
    /// model): the next frame builds the lines on screen again.
    /// </summary>
    public void ReleaseLayouts()
    {
        foreach (var runs in _lines.Values)
        {
            runs.Line.ReleaseLayout();
        }

        _lines.Clear();
    }

    private static void DrawCaret(IEditorCanvas canvas, EditorModel model, int column, float top, float scrollX, float viewportWidth)
    {
        var colors = model.Theme.Colors;
        float cell = model.Metrics.CellWidth;
        float textLeft = model.TextLeft;
        float x = textLeft - scrollX + (column * cell);
        if (x < textLeft - 1 || x > viewportWidth)
        {
            return;
        }

        float height = model.Metrics.LineHeight;
        if (model.Overwrite)
        {
            canvas.FillRect(x, top, cell, height, (colors.Caret & 0x00FFFFFFu) | 0x80000000u);
        }
        else
        {
            canvas.FillRect(x, top, model.Options.CaretWidth, height, colors.Caret);
        }
    }

    private const string StepStart = "start";
    private const string StepClassify = "classify the lines on screen";
    private const string StepLines = "draw the lines";
    private const string StepGutter = "draw the gutter";
    private const string StepSticky = "draw the sticky headers";
    private const string StepRuler = "draw the overview ruler";
    private const string StepCarets = "draw the carets";
    private const string StepGhost = "draw the ghost text";
    private const string StepPopups = "draw the popups";
    private const string StepDone = "done";

    /// <summary>The carets the last frame drew (or would have, with the caret shown).</summary>
    public int CaretsDrawn => _carets.Count;

    /// <summary>The indentation guide segments the last frame drew.</summary>
    public int GuidesDrawn { get; private set; }

    /// <summary>The current word's other places the last frame highlighted.</summary>
    public int WordHighlightsDrawn { get; private set; }

    private const string FoldMarkerText = "...";

    // The first selection whose end reaches `position` (selections are in order and do not overlap, so their ends are too).
    private static int FirstSelectionReaching(ReadOnlySpan<SelectionState> selections, int position)
    {
        int lo = 0, hi = selections.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (selections[mid].End < position)
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

    // The visible lines' spans: one call for the whole range when nothing is folded; with folds, one per run of shown
    // lines (a folded 100,000-line file classifies what is on screen, not what the folds hide), each run's spans clipped to it.
    private void ClassifyShown(EditorModel model, ITextSnapshot snapshot, LineRange visible)
    {
        var session = model.LanguageSession;
        if (session is null)
        {
            return;
        }

        var firstLine = snapshot.GetLine(visible.First);
        var lastLine = snapshot.GetLine(visible.Last);
        if (!model.Folding.Map.HasHidden)
        {
            session.Classify(snapshot, firstLine.Start, lastLine.EndIncludingBreak - firstLine.Start, _spans);
            return;
        }

        int runFirst = visible.First;
        while (runFirst <= visible.Last && runFirst < snapshot.LineCount)
        {
            int runLast = runFirst;
            int next = model.NextShownLine(runLast);
            while (next == runLast + 1 && next <= visible.Last)
            {
                runLast = next;
                next = model.NextShownLine(runLast);
            }

            int start = snapshot.GetLine(runFirst).Start;
            int end = snapshot.GetLine(runLast).EndIncludingBreak;
            _runSpans.Clear();
            session.Classify(snapshot, start, end - start, _runSpans);
            foreach (var span in _runSpans.Spans)
            {
                int from = Math.Max(span.Start, start);
                int to = Math.Min(span.End, end);
                if (to > from)
                {
                    _spans.Add(from, to - from, span.Class);
                }
            }

            runFirst = next;
        }
    }

    // The other places of the current word on a line (whole words), behind the text.
    private void DrawWord(IEditorCanvas canvas, ReadOnlySpan<char> text, string word, float left, float top, float cell, float height, int tabSize, uint color)
    {
        int from = 0;
        while (from <= text.Length - word.Length)
        {
            int found = text[from..].IndexOf(word.AsSpan(), StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            int at = from + found;
            int end = at + word.Length;
            if ((at == 0 || TextReading.KindOf(text[at - 1]) != CharKind.Word) && (end >= text.Length || TextReading.KindOf(text[end]) != CharKind.Word))
            {
                int startColumn = TextReading.ColumnOf(text, at, tabSize);
                int endColumn = TextReading.ColumnOf(text, end, tabSize);
                canvas.FillRect(left + (startColumn * cell), top, (endColumn - startColumn) * cell, height, color);
                WordHighlightsDrawn++;
            }

            from = end;
        }
    }

    // A line's indentation guides: one at each indent level of its leading white space; a blank line takes the smaller
    // of the levels around it (the line before, the next non-blank one), so the guides run through it. Returns this
    // line's indent in columns (a blank line's is the one it took).
    private int DrawGuides(IEditorCanvas canvas, EditorModel model, ITextSnapshot snapshot, int index, ReadOnlySpan<char> text, int lastVisible, int previous,
        float left, float top, float cell, float height, float textLeft, int tabSize, uint color, ref char[]? buffer)
    {
        int lead = TextReading.IndentLength(text);
        int columns;
        if (lead < text.Length)
        {
            columns = TextReading.ColumnOf(text, lead, tabSize);
        }
        else
        {
            int next = 0;
            int line = index;
            for (int step = 0; step < GuideScanLines; step++)
            {
                line = model.NextShownLine(line);
                if (line >= snapshot.LineCount)
                {
                    break;
                }

                var other = TextReading.ReadLine(snapshot, snapshot.GetLine(line), ref buffer);
                int otherLead = TextReading.IndentLength(other);
                if (otherLead < other.Length)
                {
                    next = TextReading.ColumnOf(other, otherLead, tabSize);
                    break;
                }
            }

            columns = Math.Min(previous, next);
            _ = lastVisible;
        }

        for (int level = 0; level * tabSize < columns; level++)
        {
            float x = left + (level * tabSize * cell) + 0.5f;
            if (x >= textLeft)
            {
                canvas.DrawLine(x, top, x, top + height, color, 1);
                GuidesDrawn++;
            }
        }

        return columns;
    }

    private const int GuideScanLines = 100;

    // A fold's chevron: pointing down when it can fold, right when folded.
    private static void DrawChevron(IEditorCanvas canvas, float cx, float cy, float size, bool folded, uint color)
    {
        if (folded)
        {
            canvas.DrawLine(cx - (size / 2), cy - size, cx + (size / 2), cy, color, 1.5f);
            canvas.DrawLine(cx + (size / 2), cy, cx - (size / 2), cy + size, color, 1.5f);
        }
        else
        {
            canvas.DrawLine(cx - size, cy - (size / 2), cx, cy + (size / 2), color, 1.5f);
            canvas.DrawLine(cx, cy + (size / 2), cx + size, cy - (size / 2), color, 1.5f);
        }
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
        _runSpans.Dispose();
        _lineSpans.Dispose();
        _ruler.Dispose();
        ArrayPool<TokenSpan>.Shared.Return(_clipped, clearArray: true);
        _clipped = [];
        ReleaseLayouts();
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

    // The caches keep the lines on screen and `margin` lines above and below them (a scroll back lays nothing out), trimmed
    // once they hold half as many again, so a scrolling frame does not walk them.
    private void TrimCaches(LineRange visible, int margin)
    {
        margin = Math.Max(0, margin);
        int keep = visible.Count + (2 * margin);
        int first = visible.First - margin;
        int last = visible.Last + margin;
        if (_lines.Count > keep + (keep / 2) + 8)
        {
            foreach (var (key, runs) in _lines)
            {
                if ((key < first || key > last) && !Pinned(key))
                {
                    runs.Line.ReleaseLayout();
                    _lines.Remove(key);
                }
            }
        }

        if (_numbers.Count > keep + (keep / 2) + 8)
        {
            foreach (var key in _numbers.Keys)
            {
                if (key < first + 1 || key > last + 1)
                {
                    _numbers.Remove(key);
                }
            }
        }
    }

    // A line a layer shows outside the screen (a sticky header, a fold preview's line) stays in the run cache.
    private bool Pinned(int line)
    {
        if (_model is not { } model)
        {
            return false;
        }

        foreach (int header in model.StickyScroll.Lines)
        {
            if (header == line)
            {
                return true;
            }
        }

        if (model.Tooltip is { Kind: EditorTooltipKind.FoldPreview } preview)
        {
            int header = model.Snapshot.GetLineIndex(Math.Clamp(preview.Position, 0, model.Snapshot.Length));
            return line > header && line <= header + model.Options.FoldPreviewLines;
        }

        return false;
    }

    /// <summary>
    /// The runs of <paramref name="index"/> in its colours, for a layer that shows it outside the lines on screen (a sticky
    /// header, a fold preview's line): classified alone and kept in the run cache like a shown line (a frame that shows it
    /// again makes nothing).
    /// </summary>
    internal StyledLine LineRunsFor(EditorModel model, int index)
    {
        var snapshot = model.Snapshot;
        var line = snapshot.GetLine(Math.Clamp(index, 0, snapshot.LineCount - 1));
        _lineSpans.Clear();
        model.LanguageSession?.Classify(snapshot, line.Start, line.Length, _lineSpans);
        char[]? buffer = null;
        char[]? expanded = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            return Runs(line.Index, line, text, _lineSpans.Spans, 0, model.Theme, model.Options.TabSize, ref expanded).Line;
        }
        finally
        {
            TextReading.Return(buffer);
            TextReading.Return(expanded);
        }
    }

    /// <summary>A line number's text, kept (no string a frame).</summary>
    internal string NumberText(int value) => Number(value);

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
        cached?.Line.ReleaseLayout();
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

        runs.Line.Add(new StyledRun(new string(expanded, 0, written), start, style));
        RunStringsBuilt++;
        return end;
    }

    private sealed class LineRuns
    {
        public StyledLine Line { get; } = new();

        public TokenSpan[] Spans { get; set; } = [];

        public char[] Source { get; set; } = [];

        public int Columns
        {
            get => Line.Columns;
            set => Line.Columns = value;
        }
    }
}
