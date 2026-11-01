// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>A splitter's size now: the panel's size, and whether a drag past its smallest size snapped it closed.</summary>
/// <param name="Size">The panel's open size (kept while it is snapped closed, so it opens again at it).</param>
/// <param name="Collapsed">Whether the panel shows closed (a drag below the smallest size, <see cref="SplitterModel.Snap"/>).</param>
public readonly record struct SplitterState(double Size, bool Collapsed)
{
    /// <summary>What the panel takes on screen: nothing while collapsed, else its size.</summary>
    public double Shown => Collapsed ? 0 : Size;
}

/// <summary>
/// A splitter's behaviour (B8), headless so it is tested without a window: a drag measured from where it started in the
/// window's coordinates (the grip moves with the panel, so a drag never reads positions relative to itself), kept within
/// <see cref="Clamp"/>, snapping the panel closed below its smallest size and open again past it (<see cref="Snap"/>);
/// a double-click restores <see cref="DefaultSize"/>; the arrows step it by <see cref="KeyStep"/> (Shift: five steps),
/// Home and End take it to its smallest and largest, Escape during a drag puts it back. <see cref="Grows"/> says which
/// way the pointer grows the panel: +1 for a panel at the window's left (its splitter at its right), -1 for one at the
/// right or the bottom. Used on the UI thread.
/// </summary>
public sealed class SplitterModel
{
    private double _startSize;
    private double _startPointer;

    /// <summary>A splitter whose panel grows when the pointer moves by <paramref name="grows"/> (+1 or -1) along its axis.</summary>
    public SplitterModel(int grows, bool vertical)
    {
        Grows = grows >= 0 ? 1 : -1;
        Vertical = vertical;
    }

    /// <summary>+1 when the pointer moving toward larger coordinates grows the panel, else -1.</summary>
    public int Grows { get; }

    /// <summary>Whether the splitter is a vertical line (it sizes a column, the arrows Left and Right move it), else a horizontal one (Up and Down).</summary>
    public bool Vertical { get; }

    /// <summary>The panel's size now (its open size), and whether it shows collapsed.</summary>
    public SplitterState State { get; private set; }

    /// <summary>The size a double-click restores (the option's default: <c>CRAFT_PANEL_*</c>'s).</summary>
    public double DefaultSize { get; set; } = 260;

    /// <summary>The panel's smallest size (<c>CRAFT_PANEL_MIN_SIZE</c>): a drag below it snaps the panel closed when <see cref="Snap"/>.</summary>
    public double Minimum { get; set; } = 120;

    /// <summary>Keeps a size within what the shell allows (the minimum, the room the editor keeps); null: at least 1.</summary>
    public Func<double, double>? Clamp { get; set; }

    /// <summary>Whether a drag below <see cref="Minimum"/> snaps the panel closed (<c>CRAFT_SPLITTER_SNAP</c>).</summary>
    public bool Snap { get; set; } = true;

    /// <summary>What an arrow moves it (<c>CRAFT_SPLITTER_KEY_STEP</c>); Shift moves five times as far.</summary>
    public double KeyStep { get; set; } = 10;

    /// <summary>Whether a drag is under way.</summary>
    public bool Dragging { get; private set; }

    /// <summary>Takes the panel's size from the shell (a setting, a toggle, the end of the shell's own change).</summary>
    public void SetSize(double size)
    {
        if (!Dragging && double.IsFinite(size) && size > 0)
        {
            State = new SplitterState(size, false);
        }
    }

    /// <summary>A drag starts with the pointer at <paramref name="pointer"/> (the window's coordinate along the splitter's axis).</summary>
    public void Begin(double pointer)
    {
        Dragging = true;
        _startSize = State.Size;
        _startPointer = pointer;
    }

    /// <summary>The pointer moved to <paramref name="pointer"/>: the panel's size and whether it snapped closed.</summary>
    public SplitterState Move(double pointer)
    {
        if (!Dragging)
        {
            return State;
        }

        double raw = _startSize + (Grows * (pointer - _startPointer));
        if (Snap && raw < Minimum)
        {
            // Closed while under the smallest size; the open size stays what the drag started from.
            State = new SplitterState(_startSize, true);
            return State;
        }

        State = new SplitterState(Bound(raw), false);
        return State;
    }

    /// <summary>The drag ended: the state it left (collapsed: the shell closes the panel, its size kept for when it opens).</summary>
    public SplitterState End()
    {
        Dragging = false;
        return State;
    }

    /// <summary>The drag was cancelled (Escape, the pointer lost): the size it started from.</summary>
    public SplitterState Cancel()
    {
        if (Dragging)
        {
            Dragging = false;
            State = new SplitterState(_startSize, false);
        }

        return State;
    }

    /// <summary>A double-click: the default size (within the clamp).</summary>
    public SplitterState Reset()
    {
        Dragging = false;
        State = new SplitterState(Bound(DefaultSize), false);
        return State;
    }

    /// <summary>
    /// A key on the focused splitter: the arrows of its axis step it (Shift five steps), Home and End take it to its
    /// smallest and largest, Enter restores the default, Escape cancels a drag. Null when the key is not the splitter's.
    /// </summary>
    public SplitterState? Key(EditorKey key, KeyModifiers modifiers)
    {
        double step = KeyStep * ((modifiers & KeyModifiers.Shift) != 0 ? 5 : 1);
        double? toward = key switch
        {
            EditorKey.Left when Vertical => -1,
            EditorKey.Right when Vertical => 1,
            EditorKey.Up when !Vertical => -1,
            EditorKey.Down when !Vertical => 1,
            _ => null,
        };
        if (toward is { } direction)
        {
            State = new SplitterState(Bound(State.Size + (Grows * direction * step)), false);
            return State;
        }

        switch (key)
        {
            case EditorKey.Home:
                State = new SplitterState(Bound(0), false);
                return State;
            case EditorKey.End:
                State = new SplitterState(Bound(double.MaxValue / 4), false);
                return State;
            case EditorKey.Enter:
                return Reset();
            case EditorKey.Escape when Dragging:
                return Cancel();
            default:
                return null;
        }
    }

    private double Bound(double size) => Clamp is { } clamp ? clamp(size) : Math.Max(1, size);
}
