// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>A box the editor draws over its text, in view coordinates (from the viewport's top-left).</summary>
/// <param name="X">Its left.</param>
/// <param name="Y">Its top.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
/// <param name="Rows">The rows it shows (the completion list's), or the tooltip's lines.</param>
public readonly record struct PopupBox(float X, float Y, float Width, float Height, int Rows)
{
    /// <summary>Whether the view point is inside it.</summary>
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

public sealed partial class EditorModel
{
    /// <summary>The cells of padding at a popup's left and right.</summary>
    internal const float PopupPadding = 0.5f;

    /// <summary>The columns a completion row's kind mark takes (the mark and a space).</summary>
    internal const int KindColumns = 3;

    /// <summary>The widest a popup grows, in columns.</summary>
    internal const int PopupMaxColumns = 80;

    /// <summary>The completion list's box under (or over) the word, or null while it is closed.</summary>
    public PopupBox? CompletionBox()
    {
        if (_completion is not { IsOpen: true } completion || completion.List is not { } list)
        {
            _completionShown = false;
            return null;
        }

        if (!_completionShown)
        {
            // Phase X's motion: the list unfolds from its first row over CRAFT_MOTION_MS when it opens (not while it filters).
            _completionShown = true;
            _completionShownAt = _time.GetTimestamp();
        }

        var metrics = Metrics;
        int rows = Math.Min(completion.Items.Count, Math.Max(1, Options.CompletionRows));
        int columns = 12;
        foreach (var item in completion.Items)
        {
            int length = item.Label.Length + (item.Detail is { Length: > 0 } detail ? detail.Length + 2 : 0);
            columns = Math.Max(columns, Math.Min(PopupMaxColumns / 2 + 20, length));
        }

        float width = ((columns + KindColumns) + (2 * PopupPadding)) * metrics.CellWidth;
        float height = rows * metrics.LineHeight;
        var anchor = PointFromPosition(Math.Clamp(list.SpanStart, 0, Snapshot.Length));
        float x = anchor.X - ScrollX - ((KindColumns + PopupPadding) * metrics.CellWidth);
        float below = anchor.Y + metrics.LineHeight - ScrollY;
        float above = anchor.Y - ScrollY - height;
        float y = below + height > ViewportHeight && above >= 0 ? above : below;
        x = Math.Clamp(x, TextLeft, Math.Max(TextLeft, ViewportWidth - width));
        return new PopupBox(x, y, width, height, rows);
    }

    /// <summary>The height of the completion list drawn now: its box's while it unfolds over the transition (at least a row), then the whole box; 0 while closed.</summary>
    public float CompletionShownHeight() => CompletionBox() is { } box
        ? Math.Min(box.Height, Math.Max(Metrics.LineHeight, box.Height * (float)CompletionReveal))
        : 0;

    private bool _completionShown;
    private long _completionShownAt;

    /// <summary>How far the completion list has unfolded since it opened (0 to 1, eased; 1 at once with reduced motion or no list).</summary>
    public double CompletionReveal => _completionShown ? Motion.Progress(_time.GetElapsedTime(_completionShownAt), Motion.Duration(Options)) : 1;

    /// <summary>Whether a transition is drawing (the completion list unfolding): the view draws another frame after this one.</summary>
    public bool IsAnimating => _completionShown && CompletionReveal < 1;

    /// <summary>The completion row under a view point, or null when the point is not on the list.</summary>
    public int? PopupRowAt(float viewX, float viewY)
    {
        if (CompletionBox() is not { } box || !box.Contains(viewX, viewY))
        {
            return null;
        }

        return Math.Clamp((int)((viewY - box.Y) / Metrics.LineHeight), 0, box.Rows - 1);
    }
}

/// <summary>
/// Draws the editor's popups through <see cref="IEditorCanvas"/>, over the text: the completion list (its rows, the
/// selected one filled, a kind mark, the label and its detail) and the tooltip (signature help or hover: wrapped lines,
/// the active parameter bold). Drawn by the surface itself, so every platform shows them with no native popup. The
/// tooltip's wrapped lines are kept while its text and width stay the same, so the caret's blink makes no strings.
/// </summary>
internal sealed class EditorPopupRenderer
{
    private string? _wrappedText;
    private int _wrappedColumns;
    private readonly List<(int Start, int Length)> _lines = [];
    private readonly List<string> _lineTexts = [];

    public void Draw(IEditorCanvas canvas, EditorModel model)
    {
        var colors = model.Theme.Colors;
        var metrics = model.Metrics;
        PopupBox? completionBox = model.CompletionBox();
        if (model.Tooltip is { } tooltip)
        {
            DrawTooltip(canvas, model, tooltip, colors, metrics, completionBox);
        }

        if (completionBox is { } box)
        {
            // Phase X's motion: the list unfolds from its first row (the hit test keeps the whole box).
            DrawCompletion(canvas, model, box with { Height = model.CompletionShownHeight() }, colors, metrics);
        }
    }

    private static void DrawCompletion(IEditorCanvas canvas, EditorModel model, PopupBox box, EditorColors colors, FontMetrics metrics)
    {
        var completion = model.Completion;
        canvas.FillRect(box.X, box.Y, box.Width, box.Height, colors.PopupBackground);
        var items = completion.Items;
        float cell = metrics.CellWidth;
        float padding = EditorModel.PopupPadding * cell;
        int maxColumns = (int)((box.Width - (2 * padding)) / cell) - EditorModel.KindColumns;
        var text = new TextStyle(colors.PopupText);
        var mark = new TextStyle(colors.GutterText);
        for (int row = 0; row < box.Rows; row++)
        {
            int index = completion.FirstVisible + row;
            if (index >= items.Count)
            {
                break;
            }

            var item = items[index];
            float top = box.Y + (row * metrics.LineHeight);
            if (top + metrics.LineHeight > box.Y + box.Height + 0.5f)
            {
                break;                                           // the rows the unfolding list does not show yet
            }

            if (index == completion.SelectedIndex)
            {
                canvas.FillRect(box.X, top, box.Width, metrics.LineHeight, colors.PopupSelection);
            }

            canvas.DrawText(KindMark(item.Kind), box.X + padding, top, mark);
            float x = box.X + padding + (EditorModel.KindColumns * cell);
            var label = item.Label.Length > maxColumns ? item.Label[..Math.Max(1, maxColumns - 1)] + "…" : item.Label;
            canvas.DrawText(label, x, top, text);
            if (item.Detail is { Length: > 0 } detail && label.Length + 2 < maxColumns)
            {
                int room = maxColumns - label.Length - 2;
                canvas.DrawText(detail.Length > room ? detail[..Math.Max(1, room - 1)] + "…" : detail, x + ((label.Length + 2) * cell), top, mark);
            }
        }

        DrawBorder(canvas, box, colors.PopupBorder);
    }

    private void DrawTooltip(IEditorCanvas canvas, EditorModel model, EditorTooltip tooltip, EditorColors colors, FontMetrics metrics, PopupBox? avoid)
    {
        float cell = metrics.CellWidth;
        int columns = Math.Clamp((int)(model.ViewportWidth / cell) - 4, 16, EditorModel.PopupMaxColumns);
        Wrap(tooltip.Text, columns);
        int widest = 0;
        foreach (var (_, length) in _lines)
        {
            widest = Math.Max(widest, length);
        }

        float width = (widest + (2 * EditorModel.PopupPadding)) * cell;
        float height = _lines.Count * metrics.LineHeight;
        var anchor = model.PointFromPosition(Math.Clamp(tooltip.Position, 0, model.Snapshot.Length));
        float x = Math.Clamp(anchor.X - model.ScrollX, model.TextLeft, Math.Max(model.TextLeft, model.ViewportWidth - width));
        float above = anchor.Y - model.ScrollY - height - 2;
        float below = anchor.Y + metrics.LineHeight - model.ScrollY + 2;
        bool listBelow = avoid is { } list && list.Y >= anchor.Y - model.ScrollY;
        float y = above >= 0 || listBelow ? above : below;
        if (y < 0 && avoid is { } box)
        {
            y = box.Y + box.Height + 2;
        }

        var popup = new PopupBox(x, y, width, height, _lines.Count);
        canvas.FillRect(popup.X, popup.Y, popup.Width, popup.Height, colors.PopupBackground);
        var plain = new TextStyle(colors.PopupText);
        var bold = new TextStyle(colors.PopupText, Bold: true);
        var (highlightStart, highlightLength) = tooltip.Highlight;
        int highlightEnd = highlightStart + highlightLength;
        float left = x + (EditorModel.PopupPadding * cell);
        for (int i = 0; i < _lines.Count; i++)
        {
            var (start, length) = _lines[i];
            float top = y + (i * metrics.LineHeight);
            int end = start + length;
            if (highlightLength > 0 && highlightStart < end && highlightEnd > start)
            {
                // The line in three runs: before, the active parameter (bold), after.
                int from = Math.Max(start, highlightStart);
                int to = Math.Min(end, highlightEnd);
                var text = tooltip.Text;
                if (from > start)
                {
                    canvas.DrawText(text[start..from], left, top, plain);
                }

                canvas.DrawText(text[from..to], left + ((from - start) * cell), top, bold);
                if (to < end)
                {
                    canvas.DrawText(text[to..end], left + ((to - start) * cell), top, plain);
                }
            }
            else
            {
                canvas.DrawText(_lineTexts[i], left, top, plain);
            }
        }

        DrawBorder(canvas, popup, colors.PopupBorder);
    }

    /// <summary>Splits <paramref name="text"/> at its line breaks and wraps each line at a space before <paramref name="columns"/> (kept until the text or width changes).</summary>
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

    private static void DrawBorder(IEditorCanvas canvas, PopupBox box, uint argb)
    {
        float right = box.X + box.Width;
        float bottom = box.Y + box.Height;
        canvas.DrawLine(box.X, box.Y, right, box.Y, argb, 1);
        canvas.DrawLine(right, box.Y, right, bottom, argb, 1);
        canvas.DrawLine(right, bottom, box.X, bottom, argb, 1);
        canvas.DrawLine(box.X, bottom, box.X, box.Y, argb, 1);
    }

    private static string KindMark(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Keyword => "kw",
        CompletionItemKind.Namespace => "ns",
        CompletionItemKind.Class => "C",
        CompletionItemKind.Struct => "S",
        CompletionItemKind.Interface => "I",
        CompletionItemKind.Enum => "E",
        CompletionItemKind.EnumMember => "e",
        CompletionItemKind.Delegate => "D",
        CompletionItemKind.Method => "M",
        CompletionItemKind.ExtensionMethod => "M+",
        CompletionItemKind.Property => "P",
        CompletionItemKind.Field => "F",
        CompletionItemKind.Constant => "K",
        CompletionItemKind.Event => "ev",
        CompletionItemKind.Local => "l",
        CompletionItemKind.Parameter => "p",
        CompletionItemKind.TypeParameter => "T",
        CompletionItemKind.Snippet => "sn",
        _ => "·",
    };
}
