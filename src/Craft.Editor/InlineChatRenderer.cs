// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Draws the inline chat (X part 9 item 23) through <see cref="IEditorCanvas"/>: an accent bar beside the lines it is
/// about, the ghost diff over those lines (an overlay from the first line down: kept lines plain, removed ones struck on
/// the removed fill, added ones on the added fill, lines the stream has not reached faded; the text below moves only on
/// Accept), and the box (the input with its caret or placeholder, the status line and the buttons) over the selection or
/// under the lines. Only what the viewport shows is drawn; nothing when the box is closed.
/// </summary>
internal sealed class InlineChatRenderer
{
    /// <summary>The ghost rows drawn in the last frame (the tests' measure).</summary>
    public int RowsDrawn { get; private set; }

    /// <summary>Whether the box was drawn in the last frame.</summary>
    public bool BoxDrawn { get; private set; }

    public void Draw(IEditorCanvas canvas, EditorModel model, float viewportWidth, float viewportHeight)
    {
        RowsDrawn = 0;
        BoxDrawn = false;
        var chat = model.InlineChat;
        if (chat.Layout() is not { } layout)
        {
            return;
        }

        var colors = model.Theme.Colors;
        var metrics = model.Metrics;
        float cell = metrics.CellWidth;
        float line = metrics.LineHeight;
        float textLeft = model.TextLeft;

        // The lines it is about: an accent bar at the text's left edge.
        float barTop = Math.Max(0, layout.RowsTop);
        float barBottom = Math.Min(viewportHeight, layout.RowsTop + Math.Max(layout.RangeHeight, chat.State is InlineChatState.Streaming or InlineChatState.Answered ? chat.Rows.Count * line : 0));
        if (barBottom > barTop)
        {
            canvas.FillRect(textLeft - 3, barTop, 2, barBottom - barTop, colors.Accent);
        }

        // The ghost diff, from the first line down, over the text.
        if (chat.State is InlineChatState.Streaming or InlineChatState.Answered)
        {
            var rows = chat.Rows;
            float right = viewportWidth - model.OverviewRulerWidth;
            float width = Math.Max(0, right - textLeft);
            int columns = (int)(width / Math.Max(1, cell)) + 1;
            for (int i = 0; i < rows.Count; i++)
            {
                float y = layout.RowsTop + (i * line);
                if (y + line < 0)
                {
                    continue;
                }

                if (y > viewportHeight)
                {
                    break;
                }

                var row = rows[i];
                uint fill = row.Kind switch
                {
                    InlineChatRowKind.Added => colors.DiffAdded,
                    InlineChatRowKind.Removed => colors.DiffRemoved,
                    _ => colors.Background,
                };
                canvas.FillRect(textLeft, y, width, line, fill);
                string marker = row.Kind switch
                {
                    InlineChatRowKind.Added => "+",
                    InlineChatRowKind.Removed => "−",
                    _ => "",
                };
                if (marker.Length > 0)
                {
                    canvas.DrawText(marker, textLeft - (2 * cell) - 2, y, new TextStyle(row.Kind == InlineChatRowKind.Added ? colors.Success : colors.Error, Bold: true));
                }

                float x = layout.RowsLeft;
                string text = Visible(row.Text, model.ScrollX, cell, columns, out float shift);
                var style = row.Kind switch
                {
                    InlineChatRowKind.Removed => new TextStyle(colors.SecondaryText),
                    InlineChatRowKind.Pending => new TextStyle(colors.GhostText, Italic: true),
                    _ => new TextStyle(colors.Text),
                };
                canvas.DrawText(text, x + shift, y, style);
                if (row.Kind == InlineChatRowKind.Removed && text.Length > 0)
                {
                    float strike = y + (line / 2);
                    canvas.DrawLine(x + shift, strike, x + shift + (text.Length * cell), strike, colors.Error, 1);
                }

                RowsDrawn++;
            }
        }

        // The box.
        if (layout.Top + layout.Height < 0 || layout.Top > viewportHeight)
        {
            return;
        }

        canvas.FillRect(layout.Left, layout.Top, layout.Width, layout.Height, colors.PopupBackground);
        uint border = chat.HasFocus ? colors.Accent : colors.PopupBorder;
        canvas.DrawLine(layout.Left, layout.Top, layout.Left + layout.Width, layout.Top, border, 1);
        canvas.DrawLine(layout.Left, layout.Top + layout.Height, layout.Left + layout.Width, layout.Top + layout.Height, border, 1);
        canvas.DrawLine(layout.Left, layout.Top, layout.Left, layout.Top + layout.Height, border, 1);
        canvas.DrawLine(layout.Left + layout.Width, layout.Top, layout.Left + layout.Width, layout.Top + layout.Height, border, 1);
        float inner = layout.Left + cell;
        int room = Math.Max(4, (int)((layout.Width - (4 * cell)) / Math.Max(1, cell)));
        canvas.DrawText("✦", inner, layout.InputTop, new TextStyle(colors.Accent, Bold: true));
        float inputLeft = inner + (2 * cell);
        if (chat.Input.Length == 0)
        {
            canvas.DrawText(Cut(chat.Placeholder, room), inputLeft, layout.InputTop, new TextStyle(colors.SecondaryText, Italic: true));
        }
        else
        {
            // The input's end stays in view while it is longer than the box.
            int first = Math.Max(0, chat.InputCaret - room + 1);
            string shown = chat.Input.Length - first > room ? chat.Input.Substring(first, room) : chat.Input[first..];
            canvas.DrawText(shown, inputLeft, layout.InputTop, new TextStyle(colors.PopupText));
            if (chat.HasFocus)
            {
                float caretX = inputLeft + ((chat.InputCaret - first) * cell);
                canvas.FillRect(caretX, layout.InputTop + 1, 2, line - 2, colors.Caret);
            }
        }

        if (chat.Input.Length == 0 && chat.HasFocus)
        {
            canvas.FillRect(inputLeft, layout.InputTop + 1, 2, line - 2, colors.Caret);
        }

        float buttonsLeft = layout.ButtonCount > 0 ? layout.ButtonLeft(layout.ButtonCount - 1) : layout.Left + layout.Width;
        int statusRoom = Math.Max(0, (int)((buttonsLeft - inner - cell) / Math.Max(1, cell)));
        uint statusColor = chat.State is InlineChatState.Failed or InlineChatState.NeedsModel ? colors.Warning : colors.SecondaryText;
        canvas.DrawText(Cut(chat.StatusText, statusRoom), inner, layout.StatusTop, new TextStyle(statusColor));
        for (int i = 0; i < layout.ButtonCount; i++)
        {
            var button = layout.ButtonAt(i);
            bool primary = button is InlineChatButton.Accept or InlineChatButton.Send or InlineChatButton.Load;
            canvas.FillRect(layout.ButtonLeft(i), layout.StatusTop, layout.ButtonWidth(i), line, primary ? colors.Accent : colors.RaisedHigh);
            canvas.DrawText(chat.TitleOf(button), layout.ButtonLeft(i) + cell, layout.StatusTop, new TextStyle(primary ? colors.AccentInk : colors.Text, Bold: primary));
        }

        BoxDrawn = true;
    }

    // The part of a ghost row the viewport shows (scrolled sideways), and how far its first character sits from the row's left.
    private static string Visible(string text, float scrollX, float cell, int columns, out float shift)
    {
        int skip = Math.Max(0, (int)(scrollX / Math.Max(1, cell)));
        shift = skip * cell;
        if (skip >= text.Length)
        {
            return "";
        }

        int take = Math.Min(columns, text.Length - skip);
        return skip == 0 && take == text.Length ? text : text.Substring(skip, take);
    }

    private static string Cut(string text, int room) =>
        text.Length <= room ? text : room <= 1 ? "" : string.Concat(text.AsSpan(0, room - 1), "…");
}
