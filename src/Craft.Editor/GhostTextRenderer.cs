// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Draws an inline completion's ghost text through <see cref="IEditorCanvas"/>: its first line after the caret on the
/// caret's line, its other lines over the lines below (an overlay: the text moves only when the user accepts), in the
/// theme's <see cref="EditorColors.GhostText"/>, italic; then the hint bar of the approved design under it
/// ("Completion · Tab accept · Ctrl+→ word · Esc dismiss · 41 tok/s · first token 86 ms"). Only what the viewport shows is
/// drawn; nothing is drawn when no ghost text shows.
/// </summary>
internal sealed class GhostTextRenderer
{
    private static readonly string[] Keys = ["Tab", "Ctrl+→", "Esc"];
    private static readonly string[] Meanings = [" accept", " word", " dismiss"];
    private string _source = "";
    private string[] _lines = [];

    /// <summary>The ghost lines drawn in the last frame (the tests' measure).</summary>
    public int LinesDrawn { get; private set; }

    /// <summary>Whether the hint bar was drawn in the last frame.</summary>
    public bool HintDrawn { get; private set; }

    public void Draw(IEditorCanvas canvas, EditorModel model, float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        LinesDrawn = 0;
        HintDrawn = false;
        var inline = model.InlineCompletion;
        if (!inline.IsShown)
        {
            return;
        }

        if (!ReferenceEquals(_source, inline.Text))
        {
            _source = inline.Text;
            _lines = _source.Split('\n');
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = _lines[i].TrimEnd('\r').Replace("\t", new string(' ', model.Options.TabSize), StringComparison.Ordinal);
            }
        }

        var colors = model.Theme.Colors;
        var metrics = model.Metrics;
        float cell = metrics.CellWidth;
        float height = metrics.LineHeight;
        var point = model.PointFromPosition(inline.Position);
        float x = point.X - scrollX;
        float top = point.Y - scrollY;
        float textLeft = model.TextLeft;
        var style = new TextStyle(colors.GhostText, Italic: true);
        for (int i = 0; i < _lines.Length; i++)
        {
            string line = _lines[i];
            float left = i == 0 ? x : textLeft - scrollX;
            float y = top + (i * height);
            if (y + height < 0 || y > viewportHeight || line.Length == 0)
            {
                continue;
            }

            // Behind the ghost: the caret line's colour on its own line, the ground over the lines it covers below.
            float width = Math.Min(line.Length * cell, Math.Max(0, viewportWidth - left));
            canvas.FillRect(left, y, width, height, i == 0 ? colors.CurrentLine : colors.Background);
            canvas.DrawText(line, left, y, style);
            LinesDrawn++;
        }

        // The hint bar, under the last ghost line, from the caret's column (kept inside the viewport).
        float barTop = top + (_lines.Length * height) + 4;
        if (barTop > viewportHeight || barTop + height < 0)
        {
            return;
        }

        string stats = inline.StatisticsText;
        int columns = "Completion".Length + 2;
        for (int k = 0; k < Keys.Length; k++)
        {
            columns += Keys[k].Length + Meanings[k].Length + 2;
        }

        columns += stats.Length;
        float padding = cell;
        float barWidth = (columns * cell) + (2 * padding);
        float barLeft = Math.Max(textLeft, Math.Min(x, viewportWidth - barWidth));
        canvas.FillRect(barLeft, barTop, barWidth, height, colors.PopupBackground);
        canvas.DrawLine(barLeft, barTop, barLeft + barWidth, barTop, colors.PopupBorder, 1);
        canvas.DrawLine(barLeft, barTop + height, barLeft + barWidth, barTop + height, colors.PopupBorder, 1);
        float at = barLeft + padding;
        canvas.DrawText("Completion", at, barTop, new TextStyle(colors.Accent, Bold: true));
        at += ("Completion".Length + 2) * cell;
        var key = new TextStyle(colors.PopupText, Bold: true);
        var meaning = new TextStyle(colors.PopupText);
        for (int k = 0; k < Keys.Length; k++)
        {
            canvas.DrawText(Keys[k], at, barTop, key);
            at += Keys[k].Length * cell;
            canvas.DrawText(Meanings[k], at, barTop, meaning);
            at += (Meanings[k].Length + 2) * cell;
        }

        if (stats.Length > 0)
        {
            canvas.DrawText(stats, at, barTop, new TextStyle(colors.SecondaryText));
        }

        HintDrawn = true;
    }
}
