// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.Maui.Controls;

namespace Craft.Maui;

/// <summary>The pointer's shape over a view (B8: a splitter's resize arrows).</summary>
public enum PointerShape
{
    /// <summary>Left and right arrows: a column's splitter.</summary>
    ResizeWestEast,

    /// <summary>Up and down arrows: a row's splitter.</summary>
    ResizeNorthSouth,
}

/// <summary>What a view with a captured pointer is told (B8): positions in the window's device-independent units.</summary>
public interface IPointerDrag
{
    /// <summary>The pointer went down on the view at (<paramref name="x"/>, <paramref name="y"/>); the view holds the pointer until it is released.</summary>
    void Pressed(double x, double y);

    /// <summary>The held pointer moved to (<paramref name="x"/>, <paramref name="y"/>), wherever it is over the window.</summary>
    void Moved(double x, double y);

    /// <summary>The held pointer was released at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    void Released(double x, double y);

    /// <summary>The view lost the pointer without a release (another window took it).</summary>
    void Lost();

    /// <summary>A double-click on the view.</summary>
    void DoubleClicked();
}

/// <summary>
/// The platform's pointer pieces MAUI lacks (B8): the pointer's shape over a view and a drag that captures the pointer,
/// so it starts at the press and never drops when the pointer runs over another view. Windows: the element's
/// <c>ProtectedCursor</c> with an <c>InputSystemCursor</c> and <c>CapturePointer</c> (under <c>Platforms/Windows</c>);
/// a platform without it gives null (<see cref="IShellPlatform.PointerCursor"/>) and the views fall back to MAUI's pan.
/// </summary>
public interface IPointerCursor
{
    /// <summary>
    /// Shows <paramref name="shape"/> over <paramref name="view"/> and sends its pointer's press, moves, release and
    /// double-click to <paramref name="drag"/>, from when the view has its platform view until the result is disposed.
    /// </summary>
    IDisposable Attach(View view, PointerShape shape, IPointerDrag drag);
}
