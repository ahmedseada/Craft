// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor;

/// <summary>
/// Where the minimap's lines and slider are, for one frame: a minimap row is <see cref="RowHeight"/> tall and a column
/// <see cref="ColumnWidth"/> wide; <see cref="Top"/> is how far its rows are scrolled (it scrolls with the text once the
/// whole file does not fit); the slider covers the lines the text's viewport shows; moving the slider by one unit scrolls
/// the text by <see cref="ScrollPerSlider"/>.
/// </summary>
public readonly record struct MinimapGeometry(float RowHeight, float ColumnWidth, float MapWidth, float RulerWidth, float Top, float SliderTop, float SliderHeight, float ScrollPerSlider);

/// <summary>
/// The minimap (the X restart, piece 2b): the whole file in small beside one editor's text, drawn through
/// <see cref="IEditorCanvas"/> by its own view (one renderer per view). Each line of its window (the rows its height shows,
/// scrolled with the text) is one rect per coloured run, cut at white space, from a bounded per-line cache keyed by the
/// text's version, the theme and the colours' stamp: a frame that changes nothing classifies and builds nothing, and
/// allocates nothing. An edit moves the kept lines through the lines it touched (<see cref="EditorModel"/>'s line-change
/// log) and builds only those at once; the other kept lines' colours (a typed quote can colour the lines below) are checked
/// again at most <see cref="CraftOptions.MinimapLinesPerFrame"/> a frame, the next frames going on (<see cref="Pending"/>)
/// until the cache equals a fresh build, so no frame classifies the whole window after a keystroke. Colours are the syntactic layer's only (<see cref="ILanguageSession.ClassifySyntactic"/>: a window of
/// hundreds of lines never starts a semantic pass), and the classify never waits for a parse (a landed parse redraws). A
/// translucent slider covers the lines the viewport shows; a press centres the view on the line under it and a drag
/// moves the slider (<see cref="PointerPressed"/>). Problems and the find box's hits are marked behind the lines
/// (<see cref="CraftOptions.MinimapDiagnostics"/>, <see cref="CraftOptions.MinimapFind"/>), and the overview ruler sits at its
/// right edge. Its view redraws it only when <see cref="NeedsRedraw"/> says: a scroll, an edit, a theme, its marks, never
/// the caret's blink. Dispose it to return its buffers.
/// </summary>
public sealed class MinimapRenderer : IDisposable
{
    private const float Padding = 4;
    private readonly Dictionary<int, Line> _lines = [];
    private readonly Stack<Line> _free = new();
    private readonly TokenSpanBuffer _spans = new();
    private readonly OverviewRuler _ruler = new();
    private EditorModel? _model;
    private readonly List<(int Index, Line Line)> _moves = [];
    private Key _drawn;
    private int _baseVersion = -1;
    private Drag _drag;
    private float _grab;
    private bool _disposed;

    private enum Drag
    {
        None,
        Slider,
        Ruler,
    }

    /// <summary>Whether the minimap shows for an editor <paramref name="editorWidth"/> wide (<c>CRAFT_MINIMAP</c>, and at least <c>CRAFT_MINIMAP_MIN_WIDTH</c>).</summary>
    public static bool Shows(CraftOptions options, float editorWidth)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Minimap && editorWidth >= options.MinimapMinWidth && editorWidth > WidthOf(options) * 2;
    }

    /// <summary>The minimap's width with its options: its columns, a margin, and the overview ruler at its right edge.</summary>
    public static float WidthOf(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        float columns = MathF.Ceiling(Padding + (options.MinimapMaxColumns * ColumnWidthOf(options)));
        return columns + (options.OverviewRuler ? options.OverviewRulerWidth : 0);
    }

    private static float RowHeightOf(CraftOptions options) => Math.Max(1, options.MinimapScale);

    private static float ColumnWidthOf(CraftOptions options) => Math.Max(1, options.MinimapScale) * 0.5f;

    /// <summary>The overview ruler drawn at its right edge (its counters).</summary>
    public OverviewRuler OverviewRuler => _ruler;

    /// <summary>The frames drawn since it was made.</summary>
    public long Frames { get; private set; }

    /// <summary>The lines whose rects it built (the cache's misses) since it was made.</summary>
    public long LinesBuilt { get; private set; }

    /// <summary>The calls it made to the language's syntactic classify since it was made.</summary>
    public long ClassifyCalls { get; private set; }

    /// <summary>The rects of text the last frame drew.</summary>
    public int RectsDrawn { get; private set; }

    /// <summary>The problem and find marks the last frame drew.</summary>
    public int MarksDrawn { get; private set; }

    /// <summary>The lines of the last frame's window.</summary>
    public LineRange LastWindow { get; private set; }

    /// <summary>The lines the cache holds now (the window and as many again around it).</summary>
    public int LinesHeld => _lines.Count;

    /// <summary>Whether lines of the last frame's window still wait for their colours to be checked again (the next frame goes on).</summary>
    public bool Pending { get; private set; }

    /// <summary>Whether the slider (or the ruler) is being dragged.</summary>
    public bool IsDragging => _drag != Drag.None;

    /// <summary>The geometry of <paramref name="model"/>'s minimap <paramref name="height"/> tall and <paramref name="width"/> wide, at its current scroll.</summary>
    public static MinimapGeometry Geometry(EditorModel model, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(model);
        var options = model.Options;
        float row = RowHeightOf(options);
        float column = ColumnWidthOf(options);
        float ruler = model.RulerWidth;
        float lineHeight = Math.Max(1, model.Metrics.LineHeight);
        float ratio = row / lineHeight;
        height = Math.Max(1, height);
        float content = Math.Max(model.ContentHeight, height);
        float textMost = Math.Max(0, content - height);
        float mapped = content * ratio;
        float slider = Math.Min(height, height * ratio);
        float scroll = Math.Clamp(model.ScrollY, 0, textMost);
        if (mapped <= height)
        {
            return new MinimapGeometry(row, column, Math.Max(0, width - ruler), ruler, 0, scroll * ratio, slider, 1 / ratio);
        }

        float mapMost = mapped - height;
        float top = textMost > 0 ? scroll / textMost * mapMost : 0;
        float perSlider = textMost / Math.Max(1e-3f, height - slider);
        return new MinimapGeometry(row, column, Math.Max(0, width - ruler), ruler, top, (scroll * ratio) - top, slider, perSlider);
    }

    /// <summary>
    /// Whether a frame of <paramref name="model"/> <paramref name="width"/> by <paramref name="height"/> would differ from the
    /// last one drawn: the scroll, the text, the colours, the theme, the folds, the marks it shows, the size or a drag
    /// changed. The caret's blink changes none of them (its view does not redraw it then).
    /// </summary>
    public bool NeedsRedraw(EditorModel model, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Pending || !KeyOf(model, width, height).Equals(_drawn);
    }

    /// <summary>Draws the minimap of <paramref name="model"/> on a surface <paramref name="width"/> by <paramref name="height"/>.</summary>
    public void Draw(IEditorCanvas canvas, EditorModel model, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(model, _model))
        {
            ReleaseLines();
            _model = model;
        }

        Frames++;
        Remap(model, model.Snapshot.Version);
        _drawn = KeyOf(model, width, height);
        RectsDrawn = 0;
        MarksDrawn = 0;
        var colors = model.Theme.Colors;
        canvas.FillRect(0, 0, width, height, colors.Background);
        canvas.DrawLine(0.5f, 0, 0.5f, height, colors.Border, 1);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var g = Geometry(model, width, height);
        var snapshot = model.Snapshot;
        int rows = model.RowCount;
        int firstRow = Math.Clamp((int)(g.Top / g.RowHeight), 0, Math.Max(0, rows - 1));
        int lastRow = Math.Clamp((int)MathF.Ceiling((g.Top + height) / g.RowHeight), firstRow, Math.Max(0, rows - 1));
        var window = new LineRange(model.LineAtRow(firstRow), model.LineAtRow(lastRow));
        LastWindow = window;
        Trim(window);
        Refresh(model, snapshot, window, lastRow - firstRow + 1);

        DrawMarks(canvas, model, snapshot, window, g, height, colors);

        // The lines: one rect per piece of a coloured run.
        float rectHeight = g.RowHeight <= 1 ? g.RowHeight : g.RowHeight * 0.75f;
        for (int index = window.First; index <= window.Last && index < snapshot.LineCount; index = model.NextShownLine(index))
        {
            if (!_lines.TryGetValue(index, out var line))
            {
                continue;
            }

            float y = (model.RowOfLine(index) * g.RowHeight) - g.Top;     // X part 9: a code lens row stays a row here too
            for (int i = 0; i < line.Count; i++)
            {
                int cells = line.Cells[i];
                canvas.FillRect(Padding + ((cells >>> 16) * g.ColumnWidth), y, (cells & 0xFFFF) * g.ColumnWidth, rectHeight, line.Colors[i]);
            }

            RectsDrawn += line.Count;
        }

        canvas.FillRect(0, g.SliderTop, g.MapWidth, g.SliderHeight, _drag == Drag.Slider ? colors.MinimapSliderActive : colors.MinimapSlider);
        if (g.RulerWidth > 0)
        {
            _ruler.Draw(canvas, model, g.MapWidth, g.RulerWidth, height);
        }
    }

    /// <summary>
    /// A press at (<paramref name="x"/>, <paramref name="y"/>) on the minimap <paramref name="width"/> by <paramref name="height"/>:
    /// on the slider it starts a drag; elsewhere the view centres on the line under it (at once) and a drag goes on from
    /// there; on the ruler at its right edge, the ruler's own press (it scrolls there, a drag follows). True when taken.
    /// </summary>
    public bool PointerPressed(EditorModel model, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(model);
        var g = Geometry(model, width, height);
        model.CloseTooltips();
        if (g.RulerWidth > 0 && x >= g.MapWidth)
        {
            _drag = Drag.Ruler;
            model.ScrollToRuler(y);
            return true;
        }

        _drag = Drag.Slider;
        if (y >= g.SliderTop && y < g.SliderTop + g.SliderHeight)
        {
            _grab = y - g.SliderTop;
            return true;
        }

        float lineHeight = model.Metrics.LineHeight;
        float row = MathF.Floor((y + g.Top) / g.RowHeight);
        float target = Math.Clamp((row * lineHeight) + (lineHeight / 2) - (height / 2), 0, Math.Max(0, model.ContentHeight - height));
        model.ScrollToOffsets(model.ScrollX, target, instant: true);
        _grab = y - (target / g.ScrollPerSlider);
        return true;
    }

    /// <summary>A move while pressed: the slider follows the pointer (the text scrolls at once), or the ruler's drag does. True when a drag is on.</summary>
    public bool PointerMoved(EditorModel model, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(model);
        switch (_drag)
        {
            case Drag.Slider:
                var g = Geometry(model, width, height);
                model.ScrollToOffsets(model.ScrollX, (y - _grab) * g.ScrollPerSlider, instant: true);
                return true;
            case Drag.Ruler:
                model.ScrollToRuler(y);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The press ended (the slider's colour goes back).</summary>
    public void PointerReleased() => _drag = Drag.None;

    /// <summary>Drops the cached lines (memory pressure, another model); the next frame builds its window again.</summary>
    public void ReleaseLines()
    {
        _lines.Clear();
        _free.Clear();
        _drawn = default;
        _baseVersion = -1;
        Pending = false;
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
        _ruler.Dispose();
        ReleaseLines();
        _model = null;
    }

    private Key KeyOf(EditorModel model, float width, float height)
    {
        var options = model.Options;
        bool ruler = model.RulerWidth > 0;
        object? diagnostics = options.MinimapDiagnostics || ruler ? model.DiagnosticsArray : null;
        object? find = null;
        if (options.MinimapFind || ruler)
        {
            _ = model.Find.HighlightedStarts(out find);
        }

        return new Key(model, model.Theme, model.Snapshot.Version, model.ClassificationStamp, model.ScrollY, width, height, model.RowCount, model.FoldsStamp,
            diagnostics, find, ruler ? model.SelectionStamp : 0, ruler ? model.LineMarksVersion : 0, _drag);
    }

    // The cache keeps the window and as many lines again around it, trimmed once it holds half as many more; trimmed
    // lines are kept for reuse, so scrolling a long file allocates nothing once warm.
    private void Trim(LineRange window)
    {
        int count = Math.Max(1, window.Count);
        int keep = 2 * count;
        if (_lines.Count <= keep + (keep / 2) + 16)
        {
            return;
        }

        int first = window.First - (count / 2);
        int last = window.Last + (count / 2);
        foreach (var (index, line) in _lines)
        {
            if (index < first || index > last)
            {
                _lines.Remove(index);
                if (_free.Count < keep)
                {
                    _free.Push(line);
                }
            }
        }
    }

    // The kept lines follow the text: through the lines the changes since the cache's version touched (dropped, so they are
    // built again) and the lines after them (moved by the change's line delta); without the log, every line is dropped.
    private void Remap(EditorModel model, int version)
    {
        if (_baseVersion == version)
        {
            return;
        }

        if (_baseVersion >= 0 && model.LineChangesSince(_baseVersion, out int first, out int last, out int delta))
        {
            if (last >= first)
            {
                _moves.Clear();
                foreach (var (index, line) in _lines)
                {
                    if (index >= first)
                    {
                        _lines.Remove(index);
                        if (index > last)
                        {
                            _moves.Add((index + delta, line));
                        }
                        else
                        {
                            _free.Push(line);
                        }
                    }
                }

                foreach (var (index, line) in _moves)
                {
                    _lines[index] = line;
                }

                _moves.Clear();
            }
        }
        else
        {
            foreach (var line in _lines.Values)
            {
                _free.Push(line);
            }

            _lines.Clear();
        }

        _baseVersion = version;
        model.RestartLineLog();
    }

    // Builds the window's lines that are missing (never built, or touched by an edit) and checks again up to the budget of
    // kept lines whose version, colours or theme are old; each run of consecutive lines to build is classified in one call.
    private void Refresh(EditorModel model, ITextSnapshot snapshot, LineRange window, int shownRows)
    {
        var theme = model.Theme;
        int version = snapshot.Version;
        int stamp = model.ClassificationStamp;
        var session = model.LanguageSession;
        int budget = Math.Max(1, model.Options.MinimapLinesPerFrame);
        Pending = false;
        char[]? buffer = null;
        try
        {
            int index = window.First;
            int seen = 0;
            while (index <= window.Last && index < snapshot.LineCount && seen < shownRows)
            {
                int state = StateOf(index, version, stamp, theme);
                if (state == Fresh || (state == Old && budget <= 0))
                {
                    Pending |= state == Old;
                    index = model.NextShownLine(index);
                    seen++;
                    continue;
                }

                // A run of lines to build with consecutive numbers (folds, fresh lines and the spent budget end it): one classify.
                budget -= state == Old ? 1 : 0;
                int runLast = index;
                int next = model.NextShownLine(runLast);
                int runRows = 1;
                while (next == runLast + 1 && next <= window.Last && seen + runRows < shownRows)
                {
                    int after = StateOf(next, version, stamp, theme);
                    if (after == Fresh || (after == Old && budget <= 0))
                    {
                        break;
                    }

                    budget -= after == Old ? 1 : 0;
                    runLast = next;
                    next = model.NextShownLine(runLast);
                    runRows++;
                }

                int start = snapshot.GetLine(index).Start;
                int end = snapshot.GetLine(runLast).EndIncludingBreak;
                _spans.Clear();
                if (session is not null && end > start)
                {
                    session.ClassifySyntactic(snapshot, start, end - start, _spans);
                    ClassifyCalls++;
                }


                var spans = _spans.Spans;
                int spanIndex = 0;
                for (int at = index; at <= runLast; at++)
                {
                    var textLine = snapshot.GetLine(at);
                    while (spanIndex < spans.Length && spans[spanIndex].End <= textLine.Start)
                    {
                        spanIndex++;
                    }

                    var line = Take(at);
                    Build(line, TextReading.ReadLine(snapshot, textLine, ref buffer), textLine.Start, spans, spanIndex, theme, model.Options);
                    line.Version = version;
                    line.Stamp = stamp;
                    line.Theme = theme;
                    LinesBuilt++;
                }

                seen += runRows;
                index = next;
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private const int Fresh = 0;
    private const int Old = 1;
    private const int Missing = 2;

    // Fresh: built for this version, colours and theme; old: kept (its text unchanged) with older colours; missing: to build.
    private int StateOf(int index, int version, int stamp, ITheme theme) =>
        !_lines.TryGetValue(index, out var line) ? Missing
        : line.Version == version && line.Stamp == stamp && ReferenceEquals(line.Theme, theme) ? Fresh
        : Old;

    private Line Take(int index)
    {
        if (!_lines.TryGetValue(index, out var line))
        {
            line = _free.Count > 0 ? _free.Pop() : new Line();
            _lines[index] = line;
        }

        return line;
    }

    // A line's rects: the characters of one colour, cut at white space; pieces of one colour across a single space join,
    // so a sentence in a comment is a few rects. Columns past CRAFT_MINIMAP_MAX_COLUMNS are not drawn.
    private static void Build(Line line, ReadOnlySpan<char> text, int lineStart, ReadOnlySpan<TokenSpan> spans, int spanIndex, ITheme theme, CraftOptions options)
    {
        line.Count = 0;
        int tabSize = options.TabSize;
        int most = Math.Min(options.MinimapMaxColumns, 0xFFFF);
        uint plain = Soft(theme.Colors.Foreground);
        int column = 0;
        int position = 0;
        int pieceStart = -1;
        int pieceEnd = -1;
        uint pieceColor = 0;
        while (position < text.Length && column < most)
        {
            // The next segment: a span, or the plain text up to the next span.
            uint color;
            int end;
            while (spanIndex < spans.Length && spans[spanIndex].End - lineStart <= position)
            {
                spanIndex++;
            }

            if (spanIndex < spans.Length && spans[spanIndex].Start - lineStart <= position)
            {
                color = Soft(theme.StyleOf(spans[spanIndex].Class).Argb);
                end = Math.Min(text.Length, spans[spanIndex].End - lineStart);
            }
            else
            {
                color = plain;
                end = spanIndex < spans.Length ? Math.Clamp(spans[spanIndex].Start - lineStart, position + 1, text.Length) : text.Length;
            }

            for (; position < end && column < most; position++)
            {
                char c = text[position];
                int next = TextReading.Advance(column, c, tabSize);
                if (c is not (' ' or '\t'))
                {
                    if (pieceStart >= 0 && color == pieceColor && column - pieceEnd <= 1)
                    {
                        pieceEnd = Math.Min(next, most);
                    }
                    else
                    {
                        if (pieceStart >= 0)
                        {
                            line.Add(pieceStart, pieceEnd - pieceStart, pieceColor);
                        }

                        pieceStart = column;
                        pieceEnd = Math.Min(next, most);
                        pieceColor = color;
                    }
                }

                column = next;
            }
        }

        if (pieceStart >= 0)
        {
            line.Add(pieceStart, pieceEnd - pieceStart, pieceColor);
        }
    }

    // A run's colour on the minimap: its own, a little see-through so the small blocks read as texture.
    private static uint Soft(uint argb) => (argb & 0x00FFFFFFu) | 0xB0000000u;

    // Problems (right behind the line, the error or warning colour) and the find box's hits, behind the window's lines.
    private void DrawMarks(IEditorCanvas canvas, EditorModel model, ITextSnapshot snapshot, LineRange window, MinimapGeometry g, float height, EditorColors colors)
    {
        var options = model.Options;
        int from = snapshot.GetLine(window.First).Start;
        int to = snapshot.GetLine(window.Last).EndIncludingBreak;
        if (options.MinimapDiagnostics)
        {
            var diagnostics = model.Diagnostics;
            for (int d = diagnostics.IsEmpty ? 0 : model.FirstDiagnosticReaching(from); d < diagnostics.Length && diagnostics[d].Start <= to; d++)
            {
                var diagnostic = diagnostics[d];
                if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
                {
                    continue;
                }

                uint color = diagnostic.Severity == DiagnosticSeverity.Error ? colors.RulerError : colors.RulerWarning;
                MarkRow(canvas, model, snapshot, Math.Clamp(diagnostic.Start, 0, snapshot.Length), g, height, (color & 0x00FFFFFFu) | 0x60000000u);
            }
        }

        if (options.MinimapFind)
        {
            var hits = model.Find.HighlightedStarts(out _);
            int low = 0, high = hits.Length;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (hits[middle] < from)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            for (int i = low; i < hits.Length && hits[i] <= to; i++)
            {
                MarkRow(canvas, model, snapshot, Math.Clamp(hits[i], 0, snapshot.Length), g, height, colors.RulerFind);
            }
        }
    }

    private void MarkRow(IEditorCanvas canvas, EditorModel model, ITextSnapshot snapshot, int position, MinimapGeometry g, float height, uint color)
    {
        float y = (model.RowOfLine(snapshot.GetLineIndex(position)) * g.RowHeight) - g.Top;
        if (y + g.RowHeight < 0 || y > height)
        {
            return;
        }

        canvas.FillRect(0, y, g.MapWidth, g.RowHeight, color);
        MarksDrawn++;
    }

    // What a frame shows; equal keys draw equal frames (the view's redraw test).
    private readonly record struct Key(EditorModel? Model, ITheme? Theme, int Version, int Stamp, float ScrollY, float Width, float Height, int Rows, int Folds,
        object? Diagnostics, object? Find, int Selections, int Marks, Drag Drag);

    // One line's rects: (first column << 16 | columns) and colour each, in arrays that grow and are reused.
    private sealed class Line
    {
        public int[] Cells = new int[8];
        public uint[] Colors = new uint[8];
        public int Count;
        public int Version = -1;
        public int Stamp;
        public ITheme? Theme;

        public void Add(int start, int columns, uint color)
        {
            if (columns <= 0)
            {
                return;
            }

            if (Count == Cells.Length)
            {
                Array.Resize(ref Cells, Count * 2);
                Array.Resize(ref Colors, Count * 2);
            }

            Cells[Count] = (Math.Min(start, 0xFFFF) << 16) | Math.Min(columns, 0xFFFF);
            Colors[Count] = color;
            Count++;
        }
    }
}
