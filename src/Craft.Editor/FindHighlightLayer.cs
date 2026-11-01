// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The find overlay's layer of the editor's drawing: every match of the query on a visible line, filled in the theme's
/// find colour under the selection (the current match is the selection). Its own type, so the editor's other layers
/// (brackets, guides, carets) change apart from it; the renderer calls it once per visible line, and it searches only
/// that line's span (no string).
/// </summary>
internal static class FindHighlightLayer
{
    /// <summary>Whether a frame draws the layer at all (a query that searches, shown).</summary>
    public static bool IsShown(EditorFind find) => find.IsHighlighting && find.Searchable;

    /// <summary>Fills the matches of <paramref name="text"/> (one line, drawn from <paramref name="left"/> at <paramref name="top"/>, row by row when <paramref name="wrapped"/>).</summary>
    public static void DrawLine(IEditorCanvas canvas, EditorFind find, ReadOnlySpan<char> text, WrappedLine? wrapped, float left, float top, float cell, float height, int tabSize, uint argb) =>
        DrawLine(canvas, find, text, wrapped, left, top, cell, height, tabSize, argb, default);

    /// <summary>As the overload without hints, the matches moved past the line's inlay hints (X part 9).</summary>
    public static void DrawLine(IEditorCanvas canvas, EditorFind find, ReadOnlySpan<char> text, WrappedLine? wrapped, float left, float top, float cell, float height, int tabSize, uint argb,
        scoped in HintColumns hints)
    {
        int from = 0;
        int found;
        while ((found = find.NextIn(text, from, out int length)) >= 0)
        {
            int start = TextReading.ColumnOf(text, found, tabSize);
            int end = TextReading.ColumnOf(text, found + length, tabSize);
            EditorRenderer.FillColumns(canvas, wrapped, found, start, found + length, end, left, top, cell, height, argb, hints);
            from = found + Math.Max(1, length);
        }
    }
}
