// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The rows picked in a list, as current editors pick them (the owner's note 26, no checkboxes): a click picks one row,
/// Ctrl+click adds or removes one, Shift+click picks the range from the anchor (Ctrl+Shift adds it), Up and Down move the
/// focus (Shift extends from the anchor). Rows are held by reference, so a list sorted or filtered again keeps its picks.
/// </summary>
/// <typeparam name="T">The row's type.</typeparam>
public sealed class RowSelection<T>
    where T : class
{
    private readonly HashSet<T> _picked = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where Shift's range starts, or null.</summary>
    public T? Anchor { get; private set; }

    /// <summary>The row the keyboard is on (the last clicked or moved to), or null.</summary>
    public T? Focus { get; private set; }

    /// <summary>How many rows are picked.</summary>
    public int Count => _picked.Count;

    /// <summary>Whether <paramref name="row"/> is picked.</summary>
    public bool Contains(T row) => _picked.Contains(row);

    /// <summary>A click on <paramref name="row"/> of <paramref name="rows"/> with <paramref name="modifiers"/>.</summary>
    public void Click(IReadOnlyList<T> rows, T row, KeyModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(row);
        bool control = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        if (shift && Anchor is { } anchor && IndexOf(rows, anchor) is var from && from >= 0)
        {
            if (!control)
            {
                _picked.Clear();
            }

            PickRange(rows, from, IndexOf(rows, row));
            Focus = row;
            return;
        }

        if (control)
        {
            if (!_picked.Remove(row))
            {
                _picked.Add(row);
            }
        }
        else
        {
            _picked.Clear();
            _picked.Add(row);
        }

        Anchor = row;
        Focus = row;
    }

    /// <summary>Moves the focus by <paramref name="delta"/> rows (clamped); with <paramref name="extend"/> the range from the anchor. False with no rows.</summary>
    public bool Move(IReadOnlyList<T> rows, int delta, bool extend)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return false;
        }

        int at = Focus is { } focus ? IndexOf(rows, focus) : -1;
        int to = at < 0 ? (delta >= 0 ? 0 : rows.Count - 1) : Math.Clamp(at + delta, 0, rows.Count - 1);
        Click(rows, rows[to], extend ? KeyModifiers.Shift : KeyModifiers.None);
        return true;
    }

    /// <summary>Picks every row of <paramref name="rows"/>.</summary>
    public void SelectAll(IReadOnlyList<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            _picked.Add(row);
        }

        Anchor ??= rows.Count > 0 ? rows[0] : null;
        Focus ??= Anchor;
    }

    /// <summary>Picks nothing.</summary>
    public void Clear()
    {
        _picked.Clear();
        Anchor = null;
        Focus = null;
    }

    /// <summary>Keeps only the picks <paramref name="keep"/> still lists (a row gone, filtered out or replaced is dropped).</summary>
    public void Keep(IReadOnlyList<T> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var listed = new HashSet<T>(keep, ReferenceEqualityComparer.Instance);
        _picked.RemoveWhere(row => !listed.Contains(row));
        if (Anchor is { } anchor && !listed.Contains(anchor))
        {
            Anchor = null;
        }

        if (Focus is { } focus && !listed.Contains(focus))
        {
            Focus = null;
        }
    }

    /// <summary>Picks <paramref name="row"/> as well (an update carried an earlier pick over to its new row).</summary>
    public void Add(T row, bool anchor = false, bool focus = false)
    {
        ArgumentNullException.ThrowIfNull(row);
        _picked.Add(row);
        if (anchor)
        {
            Anchor = row;
        }

        if (focus)
        {
            Focus = row;
        }
    }

    /// <summary>The picked rows in <paramref name="rows"/>' order (not the order they were picked in).</summary>
    public IReadOnlyList<T> InOrder(IReadOnlyList<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = new List<T>(_picked.Count);
        foreach (var row in rows)
        {
            if (_picked.Contains(row))
            {
                list.Add(row);
            }
        }

        return list;
    }

    private void PickRange(IReadOnlyList<T> rows, int from, int to)
    {
        if (to < 0)
        {
            return;
        }

        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            _picked.Add(rows[i]);
        }
    }

    private static int IndexOf(IReadOnlyList<T> rows, T row)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// A list the bottom panel draws itself on one surface (the owner's note 26: plain rows with hover, no row chrome, no
/// checkboxes): its rows (under an optional header that stays at the top), drawn through <see cref="IEditorCanvas"/> in
/// the code font's cells, only the rows in view; its pointer and keys; its copy. Headless: the panel's view hosts it, a
/// test draws it on a fake canvas. Used on the UI thread.
/// </summary>
public abstract class PanelList
{
    /// <summary>The row the pointer is over (-1 for none).</summary>
    public int Hover { get; set; } = -1;

    /// <summary>The rows drawn below the header.</summary>
    public abstract int RowCount { get; }

    /// <summary>Whether a header row stays at the top.</summary>
    public virtual bool HasHeader => false;

    /// <summary>Raised when what is drawn changed (the view redraws and sizes its content).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with a row the keyboard moved to: the view scrolls it into view.</summary>
    public event EventHandler<int>? RevealRequested;

    /// <summary>Raised with the text a copy put together: the view puts it on the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>A row's height for <paramref name="metrics"/>: the code font's line with room around it.</summary>
    public static float RowHeight(FontMetrics metrics) => MathF.Ceiling(metrics.LineHeight * 1.35f);

    /// <summary>The header's height (0 without one).</summary>
    public float HeaderHeight(FontMetrics metrics) => HasHeader ? RowHeight(metrics) : 0;

    /// <summary>The whole content's height.</summary>
    public float ContentHeight(FontMetrics metrics) => HeaderHeight(metrics) + (RowCount * RowHeight(metrics));

    /// <summary>The row at a point of the view (<paramref name="viewY"/> from its top, scrolled by <paramref name="scrollY"/>); -1 on the header, -2 below the rows.</summary>
    public int RowAt(FontMetrics metrics, float viewY, float scrollY)
    {
        float header = HeaderHeight(metrics);
        if (viewY < header)
        {
            return -1;
        }

        int row = (int)MathF.Floor((viewY - header + scrollY) / RowHeight(metrics));
        return row >= 0 && row < RowCount ? row : -2;
    }

    /// <summary>Draws the rows in view and the header over them.</summary>
    public abstract void Draw(IEditorCanvas canvas, FontMetrics metrics, EditorColors colors, float scrollY, float width, float height);

    /// <summary>A press of the primary button at a point of the view.</summary>
    public abstract void Press(FontMetrics metrics, float viewX, float viewY, float scrollY, float width, KeyModifiers modifiers);

    /// <summary>A key while the list has the focus; true when it took it (else the window's keys get it).</summary>
    public abstract bool OnKey(EditorKey key, KeyModifiers modifiers);

    /// <summary>The picked rows as text (none picked: null).</summary>
    public abstract string? CopySelected();

    /// <summary>Every row as text.</summary>
    public abstract string CopyAll();

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="RevealRequested"/>.</summary>
    protected void Reveal(int row) => RevealRequested?.Invoke(this, row);

    /// <summary>Copies <paramref name="text"/> through <see cref="CopyRequested"/>; false when it is null or empty.</summary>
    protected bool Copy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        CopyRequested?.Invoke(this, text);
        return true;
    }

    /// <summary>The fill behind a row: the theme's row look (hover, picked).</summary>
    protected static uint RowFill(EditorColors colors, bool hover, bool picked)
    {
        uint fill = InteractionStyle.Of(colors, InteractionKind.Row, hover ? InteractionState.Hover : InteractionState.Normal, picked).Fill;
        return fill == InteractionStyle.None ? colors.Surface : fill;
    }

    /// <summary>The first and last row in view (last below first when none).</summary>
    protected (int First, int Last) Visible(FontMetrics metrics, float scrollY, float height)
    {
        float row = RowHeight(metrics);
        float view = Math.Max(0, height - HeaderHeight(metrics));
        int first = Math.Max(0, (int)MathF.Floor(Math.Max(0, scrollY) / row));
        int last = Math.Min(RowCount - 1, (int)MathF.Ceiling((Math.Max(0, scrollY) + view) / row));
        return (first, last);
    }
}
