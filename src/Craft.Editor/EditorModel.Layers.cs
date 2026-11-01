// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Editor;

// The X restart, piece 2a: the pointer's side of the layers over the text: hover cards that stay (and a fold's preview),
// the overview ruler and the sticky headers.
public sealed partial class EditorModel
{
    private HoverCard? _hoverCard;
    private StickyScrollLayer? _sticky;
    private DebounceTimer? _hoverHideTimer;
    private Action? _hideHover;
    private bool _hideScheduled;
    private bool _hoverHasSymbol;
    private bool _cardSelecting;
    private int _cardAnchor;
    private float _pointerX = -1;
    private float _pointerY = -1;

    /// <summary>The tooltip's card: its box, its rows, its scroll and its selection.</summary>
    public HoverCard HoverCard => _hoverCard ??= new HoverCard(this);

    /// <summary>The sticky headers at the top of the view.</summary>
    public StickyScrollLayer StickyScroll => _sticky ??= new StickyScrollLayer(this);

    /// <summary>The overview ruler's width at the viewport's right edge (0 when <c>CRAFT_OVERVIEW_RULER</c> is off).</summary>
    public float OverviewRulerWidth => Options.OverviewRuler ? Options.OverviewRulerWidth : 0;

    /// <summary>Whether the hover card shown describes a symbol (its card has the "Go to definition" row).</summary>
    internal bool HoverHasSymbol => _hoverHasSymbol;

    /// <summary>
    /// The wheel turned by <paramref name="pixelsDown"/> (down when positive) while the pointer rests where it last moved: over
    /// a hover card the card scrolls (when it is taller than it shows) and the text under it does not; true when the card
    /// took the wheel.
    /// </summary>
    public bool WheelOverPopup(float pixelsDown)
    {
        if (_pointerX < 0 || Tooltip is not { Kind: not EditorTooltipKind.Plain } || !HoverCard.Contains(_pointerX, _pointerY))
        {
            return false;
        }

        float height = Metrics.LineHeight;
        int rows = height > 0 ? (int)MathF.Round(pixelsDown / height) : 0;
        if (rows == 0 && pixelsDown != 0)
        {
            rows = Math.Sign(pixelsDown);
        }

        if (HoverCard.ScrollBy(rows))
        {
            Invalidate();
        }

        return true;
    }

    /// <summary>Goes to the definition of the symbol at <paramref name="position"/> (a hover card's row): the caret goes there first.</summary>
    public void GoToDefinitionAt(int position)
    {
        CloseTooltips();
        SetSelection(SelectionState.At(Math.Clamp(position, 0, Snapshot.Length)));
        GoToDefinition();
    }

    // A move over a hover card or a fold's preview keeps it (no new hover, no hide); true when the pointer is in it.
    private bool HoverCardHovered(float viewX, float viewY)
    {
        if (_cardSelecting)
        {
            return true;
        }

        if (Tooltip is not { Kind: not EditorTooltipKind.Plain } || !HoverCard.Contains(viewX, viewY))
        {
            return false;
        }

        _hoverTimer?.Cancel();
        CancelHoverHide();
        _hoverPosition = -1;
        return true;
    }

    // The pointer left a hover card and its word: it closes after CRAFT_HOVER_HIDE_MS (once; moving on does not postpone it).
    private void ScheduleHoverHide()
    {
        if (Tooltip is not { Kind: not EditorTooltipKind.Plain } || Tooltip is { Kind: EditorTooltipKind.Card } || SignatureHelp is not null || _hideScheduled || _cardSelecting)
        {
            return;
        }

        _hideScheduled = true;
        _hoverHideTimer ??= new DebounceTimer(this);
        _hideHover ??= HideHover;
        _hoverHideTimer.Schedule(Math.Max(0, Options.HoverHideMilliseconds), _hideHover);
    }

    private void CancelHoverHide()
    {
        if (_hideScheduled)
        {
            _hideScheduled = false;
            _hoverHideTimer?.Cancel();
        }
    }

    private void HideHover()
    {
        _hideScheduled = false;
        if (_disposed || _cardSelecting)
        {
            return;
        }

        if (Tooltip is { Kind: not EditorTooltipKind.Plain } && SignatureHelp is null)
        {
            Tooltip = null;
            Invalidate();
        }
    }

    // The folded header whose "..." (after its text) or chevron (in the gutter) is at the view point, or null.
    private int? FoldPreviewAt(float viewX, float viewY)
    {
        if (!Folding.Map.HasHidden || Options.FoldPreviewLines <= 0)
        {
            return null;
        }

        if (viewX < TextLeft)
        {
            return FoldChevronAt(viewX, viewY) is int chevron && Folding.Map.IsFolded(chevron) ? chevron : null;
        }

        float height = Metrics.LineHeight;
        int row = (int)MathF.Floor((viewY + ScrollY) / height);
        if (row < 0 || row >= RowCount || (StickyScroll.Count > 0 && viewY < StickyScroll.Height))
        {
            return null;
        }

        int line = LineAtRow(row);
        if (!Folding.Map.IsFolded(line))
        {
            return null;
        }

        int columns = ColumnOf(Snapshot.GetLine(line).End);
        float column = (viewX + ScrollX - TextLeft) / Metrics.CellWidth;
        return column >= columns + 1 && column < columns + 1 + FoldMarkerColumns ? line : null;
    }

    /// <summary>The columns of the "..." drawn after a folded header's text.</summary>
    internal const int FoldMarkerColumns = 3;

    // The folded block under `header`: up to CRAFT_FOLD_PREVIEW_LINES of its hidden lines, drawn from the kept runs.
    private void ShowFoldPreview(int header, int marker)
    {
        if (_disposed || marker != _hoverPosition || !Folding.Map.IsFolded(header))
        {
            return;
        }

        int end = header;
        foreach (var fold in Folding.Map.Folded)
        {
            if (fold.StartLine == header)
            {
                end = fold.EndLine;
                break;
            }
        }

        var snapshot = Snapshot;
        int last = Math.Min(Math.Min(end, header + Options.FoldPreviewLines), snapshot.LineCount - 1);
        if (last <= header)
        {
            return;
        }

        var text = new StringBuilder();
        int widest = 0;
        int indent = int.MaxValue;
        char[]? buffer = null;
        try
        {
            for (int index = header + 1; index <= last; index++)
            {
                var line = snapshot.GetLine(index);
                var chars = TextReading.ReadLine(snapshot, line, ref buffer);
                if (index > header + 1)
                {
                    text.Append('\n');
                }

                text.Append(chars);
                widest = Math.Max(widest, TextReading.ColumnOf(chars, chars.Length, Options.TabSize));
                int lead = TextReading.IndentLength(chars);
                if (lead < chars.Length)
                {
                    indent = Math.Min(indent, TextReading.ColumnOf(chars, lead, Options.TabSize));
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        HoverCard.PreviewColumns = widest;
        HoverCard.PreviewIndent = indent == int.MaxValue ? 0 : indent;
        CancelHoverHide();
        _hoverHasSymbol = false;
        Tooltip = new EditorTooltip(text.ToString(), snapshot.GetLine(header).End) { Kind = EditorTooltipKind.FoldPreview };
        Invalidate();
    }

    // A press on a layer over the text: the hover card (its definition row, a fold preview's unfold, or a selection in its
    // text), the overview ruler (scroll there; a drag follows), a sticky header (the caret goes to it). True when taken.
    private bool PressOnLayers(float viewX, float viewY, KeyModifiers modifiers)
    {
        if (Tooltip is { Kind: not EditorTooltipKind.Plain } tooltip && HoverCard.Contains(viewX, viewY))
        {
            CancelHoverHide();
            _hoverTimer?.Cancel();
            if (PressOnCardAction(viewX, viewY))
            {
                return true;
            }

            if (HoverCard.IsOnDefinitionRow(viewX, viewY))
            {
                _pointerDown = false;
                GoToDefinitionAt(tooltip.Position);
                return true;
            }

            if (tooltip.Kind == EditorTooltipKind.FoldPreview)
            {
                _pointerDown = false;
                int header = Snapshot.GetLineIndex(Math.Clamp(tooltip.Position, 0, Snapshot.Length));
                CloseTooltips();
                Folding.Unfold(header);
                return true;
            }

            int at = HoverCard.OffsetAt(viewX, viewY);
            _cardAnchor = (modifiers & KeyModifiers.Shift) != 0 ? HoverCard.Selection.Start : at;
            HoverCard.Select(_cardAnchor, at);
            _cardSelecting = true;
            _pointerMode = PointerMode.Card;
            _clickCount = 1;
            Invalidate();
            return true;
        }

        if (OverviewRulerWidth > 0 && viewX >= ViewportWidth - OverviewRulerWidth && ViewportWidth > OverviewRulerWidth)
        {
            CloseTooltips();
            _completion?.Close();
            _pointerMode = PointerMode.Ruler;
            _clickCount = 1;
            ScrollToRuler(viewY);
            return true;
        }

        int sticky = StickyScroll.LineAt(viewY);
        if (sticky >= 0)
        {
            _pointerDown = false;
            CloseTooltips();
            _completion?.Close();
            int row = (int)(viewY / Metrics.LineHeight);
            var snapshot = Snapshot;
            var line = snapshot.GetLine(sticky);
            char[]? buffer = null;
            int lead;
            try
            {
                lead = TextReading.IndentLength(TextReading.ReadLine(snapshot, line, ref buffer));
            }
            finally
            {
                TextReading.Return(buffer);
            }

            SetSelection(SelectionState.At(line.Start + lead));
            ScrollToOffsets(ScrollX, (RowOfLine(sticky) - row) * Metrics.LineHeight, instant: true);
            return true;
        }

        return false;
    }

    // A drag that started on a layer: the card's selection or the ruler's scroll follows it. True when taken.
    private bool MoveOnLayers(float viewX, float viewY)
    {
        switch (_pointerMode)
        {
            case PointerMode.Card:
                HoverCard.Select(_cardAnchor, HoverCard.OffsetAt(viewX, viewY));
                Invalidate();
                return true;
            case PointerMode.Ruler:
                ScrollToRuler(viewY);
                return true;
            default:
                return false;
        }
    }

    // The overview ruler maps the content's height onto the viewport's: a point on it centres the view there.
    private void ScrollToRuler(float viewY)
    {
        float scale = OverviewRuler.Scale(this, ViewportHeight);
        if (scale <= 0)
        {
            return;
        }

        ScrollToOffsets(ScrollX, (Math.Max(0, viewY) / scale) - (ViewportHeight / 2), instant: true);
    }

    // Ctrl+C with text selected in a hover card copies that text.
    private bool CopyFromCard()
    {
        if (Tooltip is not { Kind: not EditorTooltipKind.Plain } || HoverCard.SelectedText is not { Length: > 0 } text)
        {
            return false;
        }

        _ = SetClipboardAsync(text);
        return true;
    }

    private bool SameWord(int a, int b)
    {
        int length = Snapshot.Length;
        if (a > length || b > length)
        {
            return false;
        }

        var (start, end) = WordAt(a);
        return end > start && b >= start && b <= end;
    }
}
