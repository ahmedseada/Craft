// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor;

/// <summary>What a tooltip is: plain (signature help, a definition's description), a hover card, or a folded block's preview.</summary>
public enum EditorTooltipKind
{
    /// <summary>Signature help or a description: closes when the caret or the text moves on.</summary>
    Plain,

    /// <summary>A hover card (the pointer rested on a symbol or a problem): it stays while the pointer is inside it.</summary>
    Hover,

    /// <summary>A folded block's preview (the pointer rested on its "..." or its chevron): its hidden lines in their colours.</summary>
    FoldPreview,

    /// <summary>
    /// A plug-in's inline card under a line (X part 8: a change with Revert and Stage): it stays until Escape, a press outside
    /// it or one of its actions, and a "- " or "+ " line is drawn as removed or added.
    /// </summary>
    Card,
}

/// <summary>
/// The tooltip's card (the X restart, piece 2a): where it is drawn (<see cref="Box"/>, worked out from the model whenever
/// asked, so the paint and the pointer agree), its wrapped lines, and for a hover card or a fold preview what the user does
/// in it: the pointer inside keeps it, leaving closes it after <see cref="Core.CraftOptions.HoverHideMilliseconds"/>, a drag
/// selects its text (Ctrl+C copies the selection), the wheel scrolls a card taller than half the editor, and a hover on a
/// symbol has a "Go to definition" row. The wrapped lines are kept while the text and the width stay, so a frame makes no
/// strings. Owned by its <see cref="EditorModel"/>; on the UI thread.
/// </summary>
public sealed class HoverCard
{
    /// <summary>The definition row's label.</summary>
    public const string DefinitionLabel = "Go to definition";

    /// <summary>The definition row's key hint.</summary>
    public const string DefinitionKey = "F12";

    private readonly EditorModel _model;
    private readonly List<(int Start, int Length)> _lines = [];
    private readonly List<string> _lineTexts = [];
    private string? _wrappedText;
    private int _wrappedColumns;
    private string? _forText;
    private int _selectionAnchor;
    private int _selectionCaret;

    internal HoverCard(EditorModel model) => _model = model;

    /// <summary>Whether a tooltip shows.</summary>
    public bool IsShown => _model.Tooltip is not null;

    /// <summary>Whether the tooltip is a hover card or a fold preview (it stays under the pointer, selects, scrolls).</summary>
    public bool IsInteractive => _model.Tooltip is { Kind: EditorTooltipKind.Hover or EditorTooltipKind.FoldPreview };

    /// <summary>The card's box in view coordinates, as last worked out (empty while nothing shows).</summary>
    public PopupBox Box { get; private set; }

    /// <summary>The first text row shown (the wheel scrolls it).</summary>
    public int FirstRow { get; private set; }

    /// <summary>The text rows (the tooltip's lines wrapped to the card's width; a fold preview's lines unwrapped).</summary>
    public int Rows => _lines.Count;

    /// <summary>The text rows the card shows at once.</summary>
    public int ShownRows { get; private set; }

    /// <summary>Whether the card ends with the "Go to definition" row (a hover on a symbol, in a language that finds definitions).</summary>
    public bool HasDefinitionRow { get; private set; }

    /// <summary>Whether the card ends with its actions' row (an inline card with actions).</summary>
    public bool HasActionRow { get; private set; }

    /// <summary>The cells between two actions on the actions' row.</summary>
    internal const int ActionGap = 3;

    /// <summary>The selection in the tooltip's text: its start and end offsets (equal when nothing is selected).</summary>
    public (int Start, int End) Selection => (Math.Min(_selectionAnchor, _selectionCaret), Math.Max(_selectionAnchor, _selectionCaret));

    /// <summary>The selected text of the card, or "" (what Ctrl+C copies while it is not empty).</summary>
    public string SelectedText
    {
        get
        {
            var (start, end) = Selection;
            return _model.Tooltip is { } tooltip && end > start && end <= tooltip.Text.Length ? tooltip.Text[start..end] : "";
        }
    }

    /// <summary>The columns of the fold preview's widest line and its least indent (set when the preview opens).</summary>
    internal int PreviewColumns { get; set; }

    internal int PreviewIndent { get; set; }

    /// <summary>The wrapped line <paramref name="row"/>: its start and length in the tooltip's text, and its text.</summary>
    internal (int Start, int Length, string Text) Line(int row) => (_lines[row].Start, _lines[row].Length, _lineTexts[row]);

    /// <summary>
    /// Works out the card's box for the model's tooltip, viewport and scroll (null while nothing shows): above its anchor
    /// when there is room (a fold preview below its header), away from the completion list, at most half the editor high
    /// for a card the user can scroll.
    /// </summary>
    public PopupBox? Layout()
    {
        var model = _model;
        if (model.Tooltip is not { } tooltip)
        {
            Box = default;
            _forText = null;
            return null;
        }

        if (!ReferenceEquals(tooltip.Text, _forText))
        {
            _forText = tooltip.Text;
            FirstRow = 0;
            _selectionAnchor = _selectionCaret = 0;
        }

        var metrics = model.Metrics;
        float cell = metrics.CellWidth;
        float height = metrics.LineHeight;
        bool preview = tooltip.Kind == EditorTooltipKind.FoldPreview;
        bool interactive = tooltip.Kind != EditorTooltipKind.Plain;
        int widest = 0;
        if (preview)
        {
            Wrap(tooltip.Text, int.MaxValue);
            int room = Math.Max(16, (int)((model.ViewportWidth - model.TextLeft - model.OverviewRulerWidth) / cell) - 2);
            widest = Math.Clamp(PreviewColumns - PreviewIndent, 16, room);
        }
        else
        {
            Wrap(tooltip.Text, Math.Clamp((int)(model.ViewportWidth / cell) - 4, 16, EditorModel.PopupMaxColumns));
            foreach (var (_, length) in _lines)
            {
                widest = Math.Max(widest, length);
            }
        }

        HasDefinitionRow = tooltip.Kind == EditorTooltipKind.Hover && model.HoverHasSymbol && model.SemanticSession is not null;
        if (HasDefinitionRow)
        {
            widest = Math.Max(widest, DefinitionLabel.Length + 2 + DefinitionKey.Length);
        }

        HasActionRow = tooltip.Kind == EditorTooltipKind.Card && tooltip.Actions is { Count: > 0 };
        if (HasActionRow)
        {
            int actions = 0;
            foreach (var action in tooltip.Actions!)
            {
                actions += action.Title.Length + ActionGap;
            }

            widest = Math.Max(widest, actions - ActionGap);
        }

        int most = interactive ? Math.Max(3, (int)(model.ViewportHeight / 2 / height)) : int.MaxValue;
        ShownRows = Math.Min(_lines.Count, most);
        FirstRow = Math.Clamp(FirstRow, 0, _lines.Count - ShownRows);
        float width = (widest + (2 * EditorModel.PopupPadding) + (ShownRows < _lines.Count ? 1 : 0)) * cell;
        float boxHeight = (ShownRows + (HasDefinitionRow || HasActionRow ? 1 : 0)) * height;
        var anchor = model.PointFromPosition(Math.Clamp(tooltip.Position, 0, model.Snapshot.Length));
        float x = Math.Clamp(anchor.X - model.ScrollX, model.TextLeft, Math.Max(model.TextLeft, model.ViewportWidth - model.OverviewRulerWidth - width));
        float above = anchor.Y - model.ScrollY - boxHeight - 2;
        float below = anchor.Y + height - model.ScrollY + 2;
        float y;
        if (tooltip.Kind == EditorTooltipKind.Card)
        {
            // Under its line, at the text's left edge; above it when it does not fit below.
            x = Math.Clamp(model.TextLeft - model.ScrollX, model.TextLeft, Math.Max(model.TextLeft, model.ViewportWidth - model.OverviewRulerWidth - width));
            y = below + boxHeight <= model.ViewportHeight || above < 0 ? below : above;
        }
        else if (preview)
        {
            x = Math.Clamp(model.TextLeft - model.ScrollX + (PreviewIndent * cell), model.TextLeft, Math.Max(model.TextLeft, model.ViewportWidth - model.OverviewRulerWidth - width));
            y = below + boxHeight <= model.ViewportHeight || above < 0 ? below : above;
        }
        else
        {
            var avoid = model.CompletionBox();
            bool listBelow = avoid is { } list && list.Y >= anchor.Y - model.ScrollY;
            y = above >= 0 || listBelow ? above : below;
            if (y < 0 && avoid is { } box)
            {
                y = box.Y + box.Height + 2;
            }
        }

        Box = new PopupBox(x, y, width, boxHeight, ShownRows);
        return Box;
    }

    /// <summary>Whether the view point is inside the card (worked out again for the model's state).</summary>
    public bool Contains(float viewX, float viewY) => Layout() is { } box && box.Contains(viewX, viewY);

    /// <summary>Whether the view point is on the "Go to definition" row.</summary>
    public bool IsOnDefinitionRow(float viewX, float viewY) =>
        Layout() is { } box && HasDefinitionRow && box.Contains(viewX, viewY) && viewY >= box.Y + (ShownRows * _model.Metrics.LineHeight);

    /// <summary>The index of the action under the view point on an inline card's actions' row, or null.</summary>
    public int? ActionAt(float viewX, float viewY)
    {
        if (Layout() is not { } box || !HasActionRow || !box.Contains(viewX, viewY) || _model.Tooltip is not { Actions: { } actions })
        {
            return null;
        }

        var metrics = _model.Metrics;
        if (viewY < box.Y + (ShownRows * metrics.LineHeight))
        {
            return null;
        }

        float x = box.X + (EditorModel.PopupPadding * metrics.CellWidth);
        for (int i = 0; i < actions.Count; i++)
        {
            float right = x + ((actions[i].Title.Length + 1) * metrics.CellWidth);
            if (viewX >= x - metrics.CellWidth && viewX < right)
            {
                return i;
            }

            x += (actions[i].Title.Length + ActionGap) * metrics.CellWidth;
        }

        return null;
    }

    /// <summary>Scrolls the text rows by <paramref name="rows"/> (down when positive); false when it cannot move.</summary>
    public bool ScrollBy(int rows)
    {
        if (Layout() is null || ShownRows >= _lines.Count || rows == 0)
        {
            return false;
        }

        int first = Math.Clamp(FirstRow + rows, 0, _lines.Count - ShownRows);
        if (first == FirstRow)
        {
            return false;
        }

        FirstRow = first;
        return true;
    }

    /// <summary>The offset in the tooltip's text nearest the view point (a row past the text's rows takes the last one).</summary>
    internal int OffsetAt(float viewX, float viewY)
    {
        if (Layout() is not { } box || _lines.Count == 0)
        {
            return 0;
        }

        var metrics = _model.Metrics;
        int row = Math.Clamp(FirstRow + (int)MathF.Floor((viewY - box.Y) / metrics.LineHeight), 0, _lines.Count - 1);
        var (start, length) = _lines[row];
        int column = (int)MathF.Round((viewX - box.X - (EditorModel.PopupPadding * metrics.CellWidth)) / metrics.CellWidth);
        return start + Math.Clamp(column, 0, length);
    }

    /// <summary>Sets the selection in the tooltip's text (a press and a drag in the card).</summary>
    internal void Select(int anchor, int caret)
    {
        _selectionAnchor = anchor;
        _selectionCaret = caret;
    }

    // Splits the text at its line breaks and wraps each line at a space before `columns` (kept until the text or width changes).
    private void Wrap(string text, int columns)
    {
        if (ReferenceEquals(text, _wrappedText) && columns == _wrappedColumns)
        {
            return;
        }

        _wrappedText = text;
        _wrappedColumns = columns;
        _lines.Clear();
        _lineTexts.Clear();
        int at = 0;
        while (at <= text.Length)
        {
            int newline = text.IndexOf('\n', at);
            int end = newline < 0 ? text.Length : newline;
            int start = at;
            while (end - start > columns)
            {
                int cut = text.LastIndexOf(' ', start + columns, columns);
                if (cut <= start)
                {
                    cut = start + columns;
                }

                Add(text, start, cut - start);
                start = cut < text.Length && text[cut] == ' ' ? cut + 1 : cut;
            }

            Add(text, start, end - start);
            if (newline < 0)
            {
                break;
            }

            at = newline + 1;
        }
    }

    private void Add(string text, int start, int length)
    {
        _lines.Add((start, length));
        _lineTexts.Add(text.Substring(start, length));
    }
}
