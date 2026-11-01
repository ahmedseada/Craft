// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

// X part 8: the document's line marks (a repository's added, changed and removed lines, a conflict's block) in the gutter
// and on the overview ruler, a click on one, and the inline card a plug-in shows for it (a change with Revert and Stage).
public sealed partial class EditorModel
{
    private DocumentLineMarks? _lineMarks;

    /// <summary>The marks of this editor's document (shared by its every editor), sorted by line.</summary>
    public ReadOnlySpan<LineMark> LineMarks => _lineMarks is { } marks ? marks.Marks : [];

    /// <summary>Changes whenever <see cref="LineMarks"/> change (the overview ruler keys on it).</summary>
    internal int LineMarksVersion => _lineMarks?.Version ?? 0;

    /// <summary>Whether the editor was disposed.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// The gutter's strip the marks are drawn in: at the left of the fold column (just right of the line numbers), or at
    /// the gutter's right edge when the document does not fold.
    /// </summary>
    internal (float Left, float Width) LineMarkStrip()
    {
        float cell = Metrics.CellWidth;
        float width = Math.Max(2, cell * 0.3f);
        float right = GutterWidth;
        return Folding.IsAvailable ? (right - (FoldGutterCells * cell) + (cell * 0.1f), width) : (right - (cell * 0.45f), width);
    }

    /// <summary>The index in <see cref="LineMarks"/> of the mark at the view point (its strip in the gutter, a little wider for the pointer), or null.</summary>
    internal int? LineMarkAt(float viewX, float viewY)
    {
        if (_lineMarks is not { } marks || marks.Marks.Length == 0)
        {
            return null;
        }

        var (left, width) = LineMarkStrip();
        float cell = Metrics.CellWidth;
        if (viewX < left - (cell * 0.25f) || viewX > left + width + (cell * 0.25f))
        {
            return null;
        }

        int row = (int)MathF.Floor((viewY + ScrollY) / Metrics.LineHeight);
        if (row < 0 || row >= RowCount)
        {
            return null;
        }

        int index = marks.IndexAt(LineAtRow(row));
        return index >= 0 ? index : null;
    }

    /// <summary>
    /// Shows <paramref name="card"/> under its line: its lines (a "- " line as removed, a "+ " line as added) and its
    /// actions as a row of buttons; it stays until Escape, a press outside it, or one of its actions.
    /// </summary>
    public void ShowInlineCard(InlineCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var snapshot = Snapshot;
        int line = Math.Clamp(card.Line, 0, Math.Max(0, snapshot.LineCount - 1));
        CloseTooltips();
        Tooltip = new EditorTooltip(card.Text.Length == 0 ? " " : card.Text, snapshot.GetLine(line).Start) { Kind = EditorTooltipKind.Card, Actions = card.Actions };
        Invalidate();
    }

    // Called once by the constructor: the document's marks, shared with its other editors.
    private void AttachLineMarks()
    {
        _lineMarks = DocumentLineMarks.Of(_document);
        _lineMarks.Changed += OnLineMarksChanged;
        _lineMarks.CardRequested += OnCardRequested;
    }

    private void DetachLineMarks()
    {
        if (_lineMarks is { } marks)
        {
            marks.Changed -= OnLineMarksChanged;
            marks.CardRequested -= OnCardRequested;
        }
    }

    // A press on a mark's strip: the plug-in that set it is told (it shows the change inline). True when taken.
    private bool PressOnLineMark(float viewX, float viewY)
    {
        if (LineMarkAt(viewX, viewY) is not int index || _lineMarks is not { } marks)
        {
            return false;
        }

        _pointerDown = false;
        marks.Click(this, index);
        return true;
    }

    // A press on the card's action row: the card closes and the action runs. True when taken.
    private bool PressOnCardAction(float viewX, float viewY)
    {
        if (Tooltip is not { Kind: EditorTooltipKind.Card, Actions: { } actions } || HoverCard.ActionAt(viewX, viewY) is not int index || index >= actions.Count)
        {
            return false;
        }

        _pointerDown = false;
        CloseTooltips();
        actions[index].Run();
        return true;
    }

    private void OnLineMarksChanged(object? sender, EventArgs e) => Invalidate();

    private void OnCardRequested(object? sender, EventArgs e)
    {
        if (_disposed || _lineMarks is not { Card: { } card } marks || !marks.IsCardTarget(this))
        {
            return;
        }

        ShowInlineCard(card);
    }
}
