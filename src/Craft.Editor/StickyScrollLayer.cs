// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Sticky scroll (the X restart, piece 2a): the header lines of the blocks the top line is in stay at the top of the
/// editor, outermost first, up to <see cref="Core.CraftOptions.StickyScrollLines"/>, while their insides scroll under them;
/// a block's header leaves once its last line reaches the row the header would cover. The blocks are the folding ranges
/// (<see cref="EditorFolding.Ranges"/>, sorted by header): each range's enclosing range is found once when the ranges
/// change (one pass with a stack), and a frame finds the top line's innermost range by a binary search and walks up its
/// enclosing ranges, into arrays it keeps: nothing is allocated per frame. Drawn by <see cref="EditorRenderer"/> over the
/// text and the gutter (each header in its own colours, from the renderer's kept lines), under the popups; a click on a
/// header moves the caret there (<see cref="EditorModel.PointerPressed"/>). Off with <c>CRAFT_STICKY_SCROLL</c>, and where
/// the document does not fold.
/// </summary>
public sealed class StickyScrollLayer
{
    private readonly EditorModel _model;
    private int[] _parents = [];
    private int[] _stack = [];
    private int[] _chain = new int[16];
    private int[] _lines = [];
    private int _count;
    private int _parentsStamp = -1;
    private float _keyScroll = float.NaN;
    private int _keyVersion = -1;
    private int _keyRanges = -1;
    private int _keyFolds = -1;
    private float _keyLineHeight;

    internal StickyScrollLayer(EditorModel model) => _model = model;

    /// <summary>Whether the editor shows sticky headers: the option is on and the document folds.</summary>
    public bool IsEnabled => _model.Options.StickyScroll && _model.Folding.IsAvailable;

    /// <summary>The header lines shown at the top now (outermost first).</summary>
    public int Count
    {
        get
        {
            Update();
            return _count;
        }
    }

    /// <summary>The header lines shown at the top now, outermost first (valid until the next scroll or edit).</summary>
    public ReadOnlySpan<int> Lines
    {
        get
        {
            Update();
            return _lines.AsSpan(0, _count);
        }
    }

    /// <summary>The height the headers cover at the top of the view.</summary>
    public float Height => Count * _model.Metrics.LineHeight;

    /// <summary>How many times the headers were worked out (a frame with the same scroll and text reuses them).</summary>
    public long Searches { get; private set; }

    /// <summary>The header line drawn at view height <paramref name="viewY"/>, or -1 when none is there.</summary>
    public int LineAt(float viewY)
    {
        int count = Count;
        float height = _model.Metrics.LineHeight;
        if (viewY < 0 || height <= 0)
        {
            return -1;
        }

        int row = (int)(viewY / height);
        return row < count ? _lines[row] : -1;
    }

    /// <summary>Works out the headers for the editor's scroll and text, unless they are already for them.</summary>
    internal void Update()
    {
        var model = _model;
        var folding = model.Folding;
        float scrollY = model.ScrollY;
        int version = model.Snapshot.Version;
        float lineHeight = model.Metrics.LineHeight;
        if (scrollY == _keyScroll && version == _keyVersion && folding.RangesStamp == _keyRanges && model.FoldsStamp == _keyFolds && lineHeight == _keyLineHeight)
        {
            return;
        }

        _keyScroll = scrollY;
        _keyVersion = version;
        _keyRanges = folding.RangesStamp;
        _keyFolds = model.FoldsStamp;
        _keyLineHeight = lineHeight;
        _count = 0;
        int max = model.Options.StickyScrollLines;
        var ranges = folding.Ranges;
        if (!IsEnabled || ranges.IsEmpty || lineHeight <= 0 || max <= 0)
        {
            return;
        }

        Searches++;
        if (_parentsStamp != folding.RangesStamp || _parents.Length < ranges.Length)
        {
            Parents(ranges);
        }

        if (_lines.Length < max)
        {
            _lines = new int[max];
        }

        int topRow = Math.Clamp((int)MathF.Floor(scrollY / lineHeight), 0, Math.Max(0, model.RowCount - 1));
        int top = model.LineAtRow(topRow);

        // The last range whose header is above the top line; the ranges holding the top line are it or its enclosing ones.
        int lo = 0, hi = ranges.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (ranges[mid].StartLine < top)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        int depth = 0;
        for (int at = lo - 1; at >= 0; at = _parents[at])
        {
            if (ranges[at].EndLine >= top)
            {
                if (depth == _chain.Length)
                {
                    Array.Resize(ref _chain, depth * 2);
                }

                _chain[depth++] = at;
            }
        }

        // Outermost first: a header stays while it is above the row it covers and its block goes on below that row.
        for (int k = depth - 1; k >= 0 && _count < max; k--)
        {
            var range = ranges[_chain[k]];
            int row = topRow + _count;
            if (model.RowOfLine(range.StartLine) >= row || model.RowOfLine(range.EndLine) <= row)
            {
                break;
            }

            _lines[_count++] = range.StartLine;
        }
    }

    // Each range's innermost enclosing range (-1 for none), in one pass over the ranges sorted by header.
    private void Parents(ReadOnlySpan<FoldingRange> ranges)
    {
        if (_parents.Length < ranges.Length)
        {
            _parents = new int[ranges.Length];
            _stack = new int[ranges.Length];
        }

        int depth = 0;
        for (int i = 0; i < ranges.Length; i++)
        {
            while (depth > 0 && ranges[_stack[depth - 1]].EndLine < ranges[i].StartLine)
            {
                depth--;
            }

            _parents[i] = depth > 0 ? _stack[depth - 1] : -1;
            _stack[depth++] = i;
        }

        _parentsStamp = _model.Folding.RangesStamp;
    }

    /// <summary>Draws the headers over the top rows: their text in its colours, their line numbers, and a shadow under them.</summary>
    internal void Draw(IEditorCanvas canvas, EditorRenderer renderer, float scrollX, float viewportWidth)
    {
        int count = Count;
        if (count == 0)
        {
            return;
        }

        var model = _model;
        var colors = model.Theme.Colors;
        var metrics = model.Metrics;
        float height = metrics.LineHeight;
        float cell = metrics.CellWidth;
        float textLeft = model.TextLeft;
        float gutterWidth = model.GutterWidth;
        var number = new TextStyle(colors.GutterText);
        for (int k = 0; k < count; k++)
        {
            int line = _lines[k];
            float top = k * height;
            canvas.FillRect(0, top, viewportWidth, height, colors.Background);
            StyledLines.Draw(canvas, renderer.LineRunsFor(model, line), textLeft - scrollX, top, cell, textLeft, viewportWidth);
            canvas.FillRect(0, top, gutterWidth, height, model.ShowLineNumbers ? colors.Gutter : colors.Background);
            if (model.ShowLineNumbers)
            {
                string text = renderer.NumberText(line + 1);
                float right = gutterWidth - (model.Folding.IsAvailable ? EditorModel.FoldGutterCells * cell : 0);
                canvas.DrawText(text, right - (((model.Options.GutterPaddingCells > 0 ? 1 : 0) + text.Length) * cell), top, number);
            }
        }

        float bottom = count * height;
        canvas.DrawLine(0, bottom, viewportWidth, bottom, colors.Border, 1);
        canvas.FillRect(0, bottom + 1, viewportWidth, Math.Max(2, height / 6), 0x30000000u);   // a soft shadow, on dark and light themes
    }
}
