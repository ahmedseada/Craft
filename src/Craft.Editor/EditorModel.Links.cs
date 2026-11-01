// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>A link clicked in an editor that finds links (X part 9: Output): the path as written, its line and column (from 1; a column of 0 names none).</summary>
public sealed class EditorLinkEventArgs(string path, int line, int column) : EventArgs
{
    /// <summary>The path as the line wrote it.</summary>
    public string Path { get; } = path;

    /// <summary>The line it names, from 1.</summary>
    public int Line { get; } = line;

    /// <summary>The column it names, from 1; 0 when none.</summary>
    public int Column { get; } = column;
}

// X part 9, item 17: links in a read-only text (Output). The line under the pointer is scanned for links (the registered
// OutputLinkDetectors, a span scan); the one under the pointer is underlined (CRAFT_LINKS_UNDERLINE "hover"; "always"
// underlines every link on screen, drawn by LinkUnderlineLayer), and a press and release on it without a drag raises
// LinkActivated. With LinksNeedControl (a surface whose plain click does something else) only while Ctrl is held.
public sealed partial class EditorModel
{
    private int _linkLine = -1;
    private OutputLink _link;
    private KeyModifiers _hoverModifiers;
    private bool _linkPressed;
    private float _linkPressX;
    private float _linkPressY;

    /// <summary>Whether the editor finds links in its lines (Output sets it).</summary>
    public bool DetectsLinks { get; set; }

    /// <summary>Whether a link opens only with Ctrl held (and shows on hover only then).</summary>
    public bool LinksNeedControl { get; set; }

    /// <summary>Whether links are off (<c>CRAFT_LINKS_UNDERLINE</c> "off") or the editor finds none.</summary>
    internal bool LinksOff => !DetectsLinks || string.Equals(Options.LinksUnderline, "off", StringComparison.Ordinal);

    /// <summary>Whether every link on screen is underlined (<c>CRAFT_LINKS_UNDERLINE</c> "always").</summary>
    internal bool LinksAlways => DetectsLinks && string.Equals(Options.LinksUnderline, "always", StringComparison.Ordinal);

    /// <summary>The link under the pointer (its line, from 0, and where it is in the line), or null.</summary>
    public (int Line, OutputLink Link)? HoveredLink => _linkLine >= 0 ? (_linkLine, _link) : null;

    /// <summary>Whether the hovered link shows underlined now (Ctrl held when <see cref="LinksNeedControl"/>).</summary>
    internal bool HoveredLinkShown => _linkLine >= 0 && !LinksOff && (!LinksNeedControl || (_hoverModifiers & KeyModifiers.Control) != 0);

    /// <summary>Raised when a link was clicked (<see cref="DetectsLinks"/>).</summary>
    public event EventHandler<EditorLinkEventArgs>? LinkActivated;

    /// <summary>As <see cref="PointerHovered(float, float)"/>, with the modifier keys held (a link under the pointer shows with Ctrl).</summary>
    public void PointerHovered(float viewX, float viewY, KeyModifiers modifiers)
    {
        _hoverModifiers = modifiers;
        HoverLink(viewX, viewY);
        PointerHovered(viewX, viewY);
    }

    /// <summary>The link at the view point (its line and where it is), or null; reads only that line.</summary>
    public (int Line, OutputLink Link)? LinkAt(float viewX, float viewY)
    {
        if (LinksOff || viewX < TextLeft)
        {
            return null;
        }

        // The model's own point to position (wrapped rows, folds), half a cell to the left so it is the character under
        // the pointer, not the nearest boundary.
        var snapshot = Snapshot;
        int position = PositionFromView(Math.Max(TextLeft, viewX - (Metrics.CellWidth * 0.5f)), viewY);
        int index = snapshot.GetLineIndex(position);
        var line = snapshot.GetLine(index);
        int offset = position - line.Start;
        if (offset >= line.Length)
        {
            return null;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            return OutputLinkDetectors.At(text, offset) is { } link ? (index, link) : null;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>The path a link of line <paramref name="line"/> names, as written.</summary>
    public string LinkPath(int line, OutputLink link)
    {
        var text = Snapshot.GetLine(Math.Clamp(line, 0, Snapshot.LineCount - 1));
        return Snapshot.GetText(text.Start + link.PathStart, Math.Min(link.PathLength, Math.Max(0, text.Length - link.PathStart)));
    }

    // The pointer moved over the text: the link under it (an invalidate only when it changed).
    private void HoverLink(float viewX, float viewY)
    {
        if (LinksOff)
        {
            return;
        }

        bool before = HoveredLinkShown;
        var previous = HoveredLink;
        var now = LinkAt(viewX, viewY);
        _linkLine = now?.Line ?? -1;
        _link = now?.Link ?? default;
        if (before != HoveredLinkShown || previous != HoveredLink)
        {
            Invalidate();
        }
    }

    // A press: on a link (with Ctrl when it needs it), the release opens it unless the pointer moved away first.
    private void NoteLinkPress(float viewX, float viewY, KeyModifiers modifiers)
    {
        _linkPressed = false;
        if (LinksOff || (LinksNeedControl && (modifiers & KeyModifiers.Control) == 0) || LinkAt(viewX, viewY) is not { } at)
        {
            return;
        }

        _linkPressed = true;
        _linkLine = at.Line;
        _link = at.Link;
        _linkPressX = viewX;
        _linkPressY = viewY;
    }

    private void NoteLinkMove(float viewX, float viewY)
    {
        if (_linkPressed && (Math.Abs(viewX - _linkPressX) > Options.DoubleClickDistance || Math.Abs(viewY - _linkPressY) > Options.DoubleClickDistance))
        {
            _linkPressed = false;                            // a drag: it selects, it does not open
        }
    }

    private void NoteLinkRelease()
    {
        if (!_linkPressed || _linkLine < 0)
        {
            return;
        }

        _linkPressed = false;
        var link = _link;
        LinkActivated?.Invoke(this, new EditorLinkEventArgs(LinkPath(_linkLine, link), link.Line, link.Column));
    }

    private void ClearLinkHover()
    {
        if (_linkLine >= 0 && !_linkPressed)
        {
            _linkLine = -1;
            Invalidate();
        }
    }
}

/// <summary>
/// Draws the links of a line (X part 9): every link with <c>CRAFT_LINKS_UNDERLINE</c> "always", else the hovered one when
/// it shows; a one-unit line under it in the theme's information colour. A warm frame allocates nothing (one buffer of
/// links per thread, made once).
/// </summary>
internal static class LinkUnderlineLayer
{
    [ThreadStatic]
    private static OutputLink[]? t_links;

    public static void DrawLine(IEditorCanvas canvas, EditorModel model, int index, ReadOnlySpan<char> text, WrappedLine? wrapped, float left, float top, float cell, float height,
        int tabSize, uint color)
    {
        if (model.LinksOff)
        {
            return;
        }

        if (model.LinksAlways)
        {
            var links = t_links ??= new OutputLink[32];
            int count = OutputLinkDetectors.FindAll(text, links);
            for (int i = 0; i < count; i++)
            {
                Underline(canvas, text, wrapped, links[i], left, top, cell, height, tabSize, color);
            }

            return;
        }

        if (model.HoveredLinkShown && model.HoveredLink is { } hovered && hovered.Line == index && hovered.Link.End <= text.Length)
        {
            Underline(canvas, text, wrapped, hovered.Link, left, top, cell, height, tabSize, color);
        }
    }

    // A one-unit line under the link's cells; a wrapped line's link under each of its rows' parts.
    private static void Underline(IEditorCanvas canvas, ReadOnlySpan<char> text, WrappedLine? wrapped, OutputLink link, float left, float top, float cell, float height,
        int tabSize, uint color)
    {
        int start = TextReading.ColumnOf(text, link.Start, tabSize);
        int end = TextReading.ColumnOf(text, link.End, tabSize);
        if (wrapped is not { Rows: > 1 })
        {
            float y = top + height - 2;
            canvas.DrawLine(left + (start * cell), y, left + (end * cell), y, color, 1);
            return;
        }

        int first = wrapped.RowOf(link.Start);
        int last = Math.Max(first, wrapped.RowOfEnd(link.End));
        for (int k = first; k <= last; k++)
        {
            int c0 = k == first ? wrapped.VisualColumn(k, start) : wrapped.RowIndent(k);
            int c1 = k == last ? wrapped.VisualColumn(k, end) : wrapped.RowWidth(k);
            if (c1 > c0)
            {
                float y = top + (k * height) + height - 2;
                canvas.DrawLine(left + (c0 * cell), y, left + (c1 * cell), y, color, 1);
            }
        }
    }
}
