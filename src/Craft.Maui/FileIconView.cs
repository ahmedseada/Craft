// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Numerics;
using Craft.Abstractions;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Craft.Maui;

/// <summary>
/// Draws file and folder icons (W: rich icons) on MAUI's cross-platform canvas: each layer's SVG path parsed once per size
/// by MAUI Graphics' own parser (<see cref="PathBuilder"/>; no package, rule 0) and kept (<see cref="IconPathCache{TPath}"/>,
/// rule 75), each colour made once, so a warm frame draws without parsing or allocating colours (rule 63). An icon with no
/// shape, or one whose path the parser refuses, is drawn as its badge. The UI thread's.
/// </summary>
public static class MauiFileIcons
{
    private static readonly IconPathCache<PathF> Paths = new(Build);
    private static readonly Dictionary<uint, Color> ColorCache = [];
    private static readonly HashSet<FileIconLayer> Refused = [];

    static MauiFileIcons() => FileIconThemes.Changed += (_, _) => Paths.Clear();

    /// <summary>How many paths were parsed since the start (a warm frame parses none).</summary>
    public static int Parses => Paths.Parses;

    /// <summary>
    /// Draws <paramref name="icon"/> (open when <paramref name="open"/>) in the square of <paramref name="size"/> at
    /// (<paramref name="x"/>, <paramref name="y"/>); false when it has no shape that can be drawn (draw its badge).
    /// </summary>
    public static bool Draw(ICanvas canvas, FileIcon icon, float x, float y, float size, bool open, bool dark)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(icon);
        if (icon.ShapeFor(open) is not { } shape || size <= 0)
        {
            return false;
        }

        var layers = shape.Layers;
        for (int i = 0; i < layers.Count; i++)
        {
            if (Refused.Contains(layers[i]))
            {
                return false;
            }
        }

        canvas.SaveState();
        try
        {
            canvas.Translate(x, y);
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                PathF path;
                try
                {
                    path = Paths.Get(shape, layer, size);
                }
                catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
                {
                    Refused.Add(layer);                  // the parser refused it: the badge from now on
                    return false;
                }

                var color = ColorOf(layer.ColorFor(dark));
                if (layer.StrokeWidth > 0)
                {
                    canvas.StrokeColor = color;
                    canvas.StrokeSize = layer.StrokeWidth * size / shape.ViewBox;
                    canvas.StrokeLineCap = LineCap.Round;
                    canvas.StrokeLineJoin = LineJoin.Round;
                    canvas.DrawPath(path);
                }
                else
                {
                    canvas.FillColor = color;
                    canvas.FillPath(path);
                }
            }
        }
        finally
        {
            canvas.RestoreState();
        }

        return true;
    }

    private static PathF Build(string data, float scale)
    {
        var path = PathBuilder.Build(data);
        if (scale != 1)
        {
            path.Transform(Matrix3x2.CreateScale(scale));
        }

        return path;
    }

    private static Color ColorOf(uint argb)
    {
        if (!ColorCache.TryGetValue(argb, out var color))
        {
            color = ThemeColors.Of(argb);
            ColorCache.Add(argb, color);
        }

        return color;
    }
}

/// <summary>
/// A file's or a folder's icon in a view (the tree, the tabs, quick open, the breadcrumb, the welcome page, menus, panels):
/// its drawn shape (<see cref="MauiFileIcons"/>), else its badge in its colour. Bindable, so a recycled row redraws only
/// when its icon, its open state or the theme's darkness changed.
/// </summary>
public sealed class FileIconView : GraphicsView
{
    /// <summary>The icon.</summary>
    public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(FileIcon), typeof(FileIconView), null, propertyChanged: Redraw);

    /// <summary>Whether a folder's icon is drawn open.</summary>
    public static readonly BindableProperty OpenProperty = BindableProperty.Create(nameof(Open), typeof(bool), typeof(FileIconView), false, propertyChanged: Redraw);

    /// <summary>Whether the theme is dark (the icon's dark colours) or light.</summary>
    public static readonly BindableProperty DarkProperty = BindableProperty.Create(nameof(Dark), typeof(bool), typeof(FileIconView), true, propertyChanged: Redraw);

    /// <summary>An icon view <paramref name="size"/> units square.</summary>
    public FileIconView(double size = 16)
    {
        WidthRequest = size;
        HeightRequest = size;
        InputTransparent = true;
        BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent;
        VerticalOptions = LayoutOptions.Center;
        Drawable = new IconDrawable(this);
    }

    /// <summary>The icon, or null for none.</summary>
    public FileIcon? Icon
    {
        get => (FileIcon?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Whether a folder's icon is drawn open.</summary>
    public bool Open
    {
        get => (bool)GetValue(OpenProperty);
        set => SetValue(OpenProperty, value);
    }

    /// <summary>Whether the theme is dark.</summary>
    public bool Dark
    {
        get => (bool)GetValue(DarkProperty);
        set => SetValue(DarkProperty, value);
    }

    /// <summary>The view of <paramref name="icon"/> in <paramref name="palette"/>'s darkness.</summary>
    public static FileIconView Of(FileIcon icon, ShellPalette palette, double size = 16, bool open = false)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new FileIconView(size) { Icon = icon, Dark = palette.IsDark, Open = open };
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((FileIconView)bindable).Invalidate();

    private sealed class IconDrawable(FileIconView view) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (view.Icon is not { } icon)
            {
                return;
            }

            float size = MathF.Min((float)view.Width, (float)view.Height);
            if (size <= 0)
            {
                return;
            }

            float x = ((float)view.Width - size) / 2, y = ((float)view.Height - size) / 2;
            if (MauiFileIcons.Draw(canvas, icon, x, y, size, view.Open, view.Dark))
            {
                return;
            }

            // The badge: the fallback of an icon with no shape (a plug-in's old text icon).
            canvas.FontColor = ThemeColors.Of(icon.BadgeArgb(view.Dark));
            canvas.FontSize = size * 0.62f;
            canvas.DrawString(icon.Badge, 0, 0, (float)view.Width, (float)view.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
