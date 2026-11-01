// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Craft.Core.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MauiView = Microsoft.Maui.Controls.View;

namespace Craft.Maui.WinUI;

/// <summary>
/// B8 on Windows: a view's pointer shape (the element's <c>ProtectedCursor</c>, set through reflection since it is
/// protected, to an <see cref="InputSystemCursor"/>) and its drag with the pointer captured (<see cref="UIElement.CapturePointer"/>),
/// so a splitter's drag starts at the press, never waits for a pan threshold, and never drops when the pointer runs over
/// the editor's canvas. Positions are the window's (<c>GetCurrentPoint(null)</c>), in device-independent units. The
/// handlers see events a child already handled (<c>AddHandler(…, true)</c>).
/// </summary>
public sealed class WindowsPointerCursor : IPointerCursor
{
    /// <inheritdoc />
    public IDisposable Attach(MauiView view, PointerShape shape, IPointerDrag drag)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(drag);
        return new Hook(view, shape, drag);
    }

    private sealed class Hook : IDisposable
    {
        private static readonly PropertyInfo? CursorProperty =
            typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly MauiView _view;
        private readonly PointerShape _shape;
        private readonly IPointerDrag _drag;
        private readonly PointerEventHandler _pressed;
        private readonly PointerEventHandler _moved;
        private readonly PointerEventHandler _released;
        private readonly PointerEventHandler _lost;
        private readonly DoubleTappedEventHandler _doubleTapped;
        private UIElement? _element;
        private bool _held;

        public Hook(MauiView view, PointerShape shape, IPointerDrag drag)
        {
            _view = view;
            _shape = shape;
            _drag = drag;
            _pressed = OnPressed;
            _moved = OnMoved;
            _released = OnReleased;
            _lost = OnLost;
            _doubleTapped = OnDoubleTapped;
            view.HandlerChanged += OnHandlerChanged;
            OnHandlerChanged(view, EventArgs.Empty);
        }

        public void Dispose()
        {
            _view.HandlerChanged -= OnHandlerChanged;
            Detach();
        }

        private void OnHandlerChanged(object? sender, EventArgs e)
        {
            Detach();
            if (_view.Handler?.PlatformView is not UIElement element)
            {
                return;
            }

            _element = element;
            SetCursor(element);
            element.AddHandler(UIElement.PointerPressedEvent, _pressed, true);
            element.AddHandler(UIElement.PointerMovedEvent, _moved, true);
            element.AddHandler(UIElement.PointerReleasedEvent, _released, true);
            element.AddHandler(UIElement.PointerCaptureLostEvent, _lost, true);
            element.AddHandler(UIElement.DoubleTappedEvent, _doubleTapped, true);
        }

        private void Detach()
        {
            if (_element is not { } element)
            {
                return;
            }

            element.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
            element.RemoveHandler(UIElement.PointerMovedEvent, _moved);
            element.RemoveHandler(UIElement.PointerReleasedEvent, _released);
            element.RemoveHandler(UIElement.PointerCaptureLostEvent, _lost);
            element.RemoveHandler(UIElement.DoubleTappedEvent, _doubleTapped);
            _element = null;
            _held = false;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "UIElement.ProtectedCursor is a framework property kept by the WinUI projection; when it cannot be found the pointer keeps its arrow.")]
        private void SetCursor(UIElement element)
        {
            try
            {
                var cursor = InputSystemCursor.Create(_shape == PointerShape.ResizeNorthSouth ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast);
                CursorProperty?.SetValue(element, cursor);
            }
#pragma warning disable CA1031 // A cursor that cannot be set leaves the arrow; the drag still works.
            catch (Exception error)
#pragma warning restore CA1031
            {
                CallbackGuard.Report("a splitter: the resize cursor", error);
            }
        }

        private void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_element is not { } element || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var at = e.GetCurrentPoint(null).Position;
            _held = element.CapturePointer(e.Pointer);
            e.Handled = true;
            _drag.Pressed(at.X, at.Y);
        }

        private void OnMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_held)
            {
                return;
            }

            var at = e.GetCurrentPoint(null).Position;
            e.Handled = true;
            _drag.Moved(at.X, at.Y);
        }

        private void OnReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_held)
            {
                return;
            }

            _held = false;
            var at = e.GetCurrentPoint(null).Position;
            _element?.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            _drag.Released(at.X, at.Y);
        }

        private void OnLost(object sender, PointerRoutedEventArgs e)
        {
            if (_held)
            {
                _held = false;
                _drag.Lost();
            }
        }

        private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
            _drag.DoubleClicked();
        }
    }
}
#endif
