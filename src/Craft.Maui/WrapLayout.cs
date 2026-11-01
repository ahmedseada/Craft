// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Craft.Maui;

/// <summary>
/// A row of views that wraps at the measured width (a panel's buttons), each child at its own measured size and the
/// layout as tall as its rows (<see cref="PanelBlockLayout.Wrap"/>), so the block under it is never drawn over it (the
/// owner's Windows run: a wrapping <see cref="FlexLayout"/> reported less height than it drew).
/// </summary>
public sealed class WrapLayout : Layout
{
    /// <summary>The space between two items of a row.</summary>
    public double Spacing { get; set; } = 8;

    /// <summary>The space between two rows.</summary>
    public double RowSpacing { get; set; } = 6;

    /// <inheritdoc />
    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(WrapLayout layout) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var padding = layout.Padding;
            double inner = Math.Max(0, widthConstraint - padding.HorizontalThickness);
            var sizes = new (double, double)[layout.Count];
            for (int i = 0; i < layout.Count; i++)
            {
                var child = layout[i];
                sizes[i] = child.Visibility == Visibility.Collapsed ? (0, 0) : Size(child.Measure(inner, double.PositiveInfinity));
            }

            var (_, width, height) = PanelBlockLayout.Wrap(Shown(sizes), inner, layout.Spacing, layout.RowSpacing);
            return new Size(width + padding.HorizontalThickness, height + padding.VerticalThickness);
        }

        public Size ArrangeChildren(Rect bounds)
        {
            var padding = layout.Padding;
            double inner = Math.Max(0, bounds.Width - padding.HorizontalThickness);
            var sizes = new (double, double)[layout.Count];
            for (int i = 0; i < layout.Count; i++)
            {
                var child = layout[i];
                sizes[i] = child.Visibility == Visibility.Collapsed ? (0, 0) : Size(child.DesiredSize);
            }

            var shown = Shown(sizes);
            var (places, _, _) = PanelBlockLayout.Wrap(shown, inner, layout.Spacing, layout.RowSpacing);
            int at = 0;
            for (int i = 0; i < layout.Count; i++)
            {
                var child = layout[i];
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var place = places[at++];
                child.Arrange(new Rect(bounds.X + padding.Left + place.X, bounds.Y + padding.Top + place.Y, place.Width, place.Height));
            }

            return bounds.Size;
        }

        private static (double, double) Size(Size size) => (double.IsFinite(size.Width) ? size.Width : 0, double.IsFinite(size.Height) ? size.Height : 0);

        // The sizes of the visible children only, in order (a collapsed child takes no place in a row).
        private (double, double)[] Shown((double, double)[] sizes)
        {
            var shown = new List<(double, double)>(sizes.Length);
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Visibility != Visibility.Collapsed)
                {
                    shown.Add(sizes[i]);
                }
            }

            return [.. shown];
        }
    }
}
