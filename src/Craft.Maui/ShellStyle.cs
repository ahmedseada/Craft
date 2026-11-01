// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Shell;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace Craft.Maui;

/// <summary>
/// The fonts the app bundles (phase D, both SIL Open Font License 1.1): the aliases the app registers them under
/// (<c>Craft.App</c>'s MauiProgram) and the files. Inter is the interface's, JetBrains Mono the code's default
/// (<c>CRAFT_FONT</c> still overrides it).
/// </summary>
public static class CraftFonts
{
    /// <summary>Inter, regular.</summary>
    public const string Interface = "Inter";

    /// <summary>Inter, medium (500).</summary>
    public const string InterfaceMedium = "InterMedium";

    /// <summary>Inter, semibold (600).</summary>
    public const string InterfaceSemiBold = "InterSemiBold";

    /// <summary>JetBrains Mono, regular (the code font's alias is its family name, so <c>CRAFT_FONT</c> names it as written).</summary>
    public const string Code = "JetBrains Mono";

    /// <summary>The bundled files and their aliases, in the order the app registers them.</summary>
    public static IReadOnlyList<(string File, string Alias)> Files { get; } =
    [
        ("Inter-Regular.ttf", Interface),
        ("Inter-Medium.ttf", InterfaceMedium),
        ("Inter-SemiBold.ttf", InterfaceSemiBold),
        ("JetBrainsMono-Regular.ttf", Code),
        ("JetBrainsMono-Italic.ttf", "JetBrainsMonoItalic"),
        ("JetBrainsMono-Bold.ttf", "JetBrainsMonoBold"),
        ("JetBrainsMono-BoldItalic.ttf", "JetBrainsMonoBoldItalic"),
    ];

    /// <summary>The interface font of a weight: <paramref name="options"/>' family, the bundled weight when it is Inter.</summary>
    public static string InterfaceOf(CraftOptions options, FontWeight weight = FontWeight.Regular)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.InterfaceFont.Equals(Interface, StringComparison.OrdinalIgnoreCase))
        {
            return options.InterfaceFont;
        }

        return weight switch
        {
            FontWeight.Medium => InterfaceMedium,
            FontWeight.SemiBold => InterfaceSemiBold,
            _ => Interface,
        };
    }
}

/// <summary>The interface font weights the design uses.</summary>
public enum FontWeight
{
    /// <summary>400.</summary>
    Regular,

    /// <summary>500.</summary>
    Medium,

    /// <summary>600.</summary>
    SemiBold,
}

/// <summary>
/// The shell's colours and type as MAUI values, made from the theme's tokens (<see cref="EditorColors"/>) and the options'
/// fonts: every view takes its colours from here, never a colour of its own.
/// </summary>
public sealed class ShellPalette
{
    /// <summary>The palette of <paramref name="theme"/> with <paramref name="options"/>' fonts.</summary>
    public ShellPalette(ITheme theme, CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);
        Theme = theme;
        Options = options;
        var c = theme.Colors;
        Ground = ThemeColors.Of(c.Ground);
        Surface = ThemeColors.Of(c.Surface);
        Raised = ThemeColors.Of(c.Raised);
        RaisedHigh = ThemeColors.Of(c.RaisedHigh);
        Selection = ThemeColors.Of(c.InterfaceSelection);
        Border = ThemeColors.Of(c.Border);
        BorderStrong = ThemeColors.Of(c.BorderStrong);
        Text = ThemeColors.Of(c.Text);
        Secondary = ThemeColors.Of(c.SecondaryText);
        Accent = ThemeColors.Of(c.Accent);
        AccentInk = ThemeColors.Of(c.AccentInk);
        Success = ThemeColors.Of(c.Success);
        Warning = ThemeColors.Of(c.Warning);
        Error = ThemeColors.Of(c.Error);
        Info = ThemeColors.Of(c.Info);
        Method = ThemeColors.Of(theme.StyleOf(TokenClasses.MethodName).Argb);
        TypeName = ThemeColors.Of(theme.StyleOf(TokenClasses.ClassName).Argb);
    }

    /// <summary>The theme.</summary>
    public ITheme Theme { get; }

    /// <summary>Whether the theme is dark (file icons take their dark colours).</summary>
    public bool IsDark => Theme.IsDark;

    /// <summary>The options (fonts, sizes, touch target).</summary>
    public CraftOptions Options { get; }

#pragma warning disable CS1591 // The tokens are named as the design's; each is documented on EditorColors.
    public Color Ground { get; }

    public Color Surface { get; }

    public Color Raised { get; }

    public Color RaisedHigh { get; }

    public Color Selection { get; }

    public Color Border { get; }

    public Color BorderStrong { get; }

    public Color Text { get; }

    public Color Secondary { get; }

    public Color Accent { get; }

    public Color AccentInk { get; }

    public Color Success { get; }

    public Color Warning { get; }

    public Color Error { get; }

    public Color Info { get; }

    public Color Method { get; }

    public Color TypeName { get; }
#pragma warning restore CS1591

    /// <summary>The interface's text size (13 in the design).</summary>
    public double FontSize => Options.InterfaceFontSize;

    /// <summary>The smaller size (12: the status bar, the breadcrumb, details).</summary>
    public double SmallSize => Math.Max(6, Options.InterfaceFontSize - 1);

    /// <summary>The caption size (11: section titles in capitals).</summary>
    public double CaptionSize => Math.Max(6, Options.InterfaceFontSize - 2);

    /// <summary>The interface font of a weight.</summary>
    public string Font(FontWeight weight = FontWeight.Regular) => CraftFonts.InterfaceOf(Options, weight);

    /// <summary>A label in the interface's font.</summary>
    public Label Label(string text, Color? color = null, double? size = null, FontWeight weight = FontWeight.Regular) => new()
    {
        Text = text,
        TextColor = color ?? Text,
        FontFamily = Font(weight),
        FontSize = size ?? FontSize,
        VerticalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    /// <summary>A flat button (no fill, no border), at least <paramref name="height"/> tall, with its hover, pressed, focus and disabled states.</summary>
    public Button Flat(string text, Color? color = null, double height = 28, double? size = null) => WithStates(new Button
    {
        Text = text,
        TextColor = color ?? Secondary,
        BackgroundColor = Colors.Transparent,
        BorderWidth = 0,
        CornerRadius = 6,
        Padding = new Thickness(8, 0),
        FontFamily = Font(),
        FontSize = size ?? FontSize,
        MinimumHeightRequest = height,
        HeightRequest = height,
    }, InteractionKind.Flat, color);

    /// <summary>A bordered button on the raised fill (the design's secondary action), with its states.</summary>
    public Button SecondaryButton(string text, double height = 28) => WithStates(new Button
    {
        Text = text,
        TextColor = Text,
        BackgroundColor = RaisedHigh,
        BorderColor = BorderStrong,
        BorderWidth = 1,
        CornerRadius = 6,
        Padding = new Thickness(12, 0),
        FontFamily = Font(),
        FontSize = SmallSize,
        HeightRequest = height,
        MinimumHeightRequest = height,
    }, InteractionKind.Secondary, null);

    /// <summary>The accent button (the design's primary action), its label in the accent's ink, with its states.</summary>
    public Button Primary(string text, double height = 32) => WithStates(new Button
    {
        Text = text,
        TextColor = AccentInk,
        BackgroundColor = Accent,
        BorderWidth = 0,
        CornerRadius = 6,
        Padding = new Thickness(16, 0),
        FontFamily = Font(FontWeight.SemiBold),
        FontSize = FontSize,
        HeightRequest = height,
        MinimumHeightRequest = height,
    }, InteractionKind.Primary, null);

    /// <summary>
    /// A control's colours in a state (phase X), from the theme's tokens (<see cref="InteractionStyle"/>): its fill, its
    /// border (the accent while the focus ring shows) and its text.
    /// </summary>
    public (Color Fill, Color Stroke, Color Text, bool Ring) Look(InteractionKind kind, InteractionState state, bool selected = false, uint? text = null)
    {
        var look = InteractionStyle.Of(Theme.Colors, kind, state, selected, text);
        return (ThemeColors.Of(look.Fill), ThemeColors.Of(look.Stroke), ThemeColors.Of(look.Text), look.Ring);
    }

    /// <summary>Attaches the hover, pressed, focus and disabled states of a <paramref name="kind"/> control to <paramref name="view"/> (<see cref="InteractiveStates"/>).</summary>
    public InteractiveStates States(VisualElement view, InteractionKind kind, Color? text = null) => InteractiveStates.Attach(view, this, kind, text);

    private T WithStates<T>(T view, InteractionKind kind, Color? text)
        where T : VisualElement
    {
        InteractiveStates.Attach(view, this, kind, text);
        return view;
    }

    /// <summary>
    /// A text field in the interface's (or the code's) font, drawn without a box of its own: shown inside a
    /// <see cref="Field"/> (Craft's one input style, the owner's note 12), <paramref name="height"/> tall with its border.
    /// </summary>
    public Entry Input(string placeholder, double height = 32, bool code = false) => new()
    {
        Placeholder = placeholder,
        PlaceholderColor = Secondary,
        TextColor = Text,
        BackgroundColor = Colors.Transparent,
        FontFamily = code ? Options.FontFamily : Font(),
        FontSize = FontSize,
        HeightRequest = Math.Max(16, height - 2),
        MinimumHeightRequest = Math.Max(16, height - 2),
    };

    /// <summary>Craft's input style around <paramref name="input"/> (one border, the design's radius and fill, the accent's focus ring).</summary>
    public CraftInput Field(InputView input, double radius = 6, bool multiline = false) => new(this, input, radius, multiline);

    /// <summary>A rounded box with the border (a card, a chip, a list).</summary>
    public Border Box(View content, double radius = 8, Thickness? padding = null, Color? fill = null, Color? stroke = null) => new()
    {
        Content = content,
        Padding = padding ?? new Thickness(10),
        Stroke = stroke ?? Border,
        StrokeThickness = 1,
        BackgroundColor = fill ?? Colors.Transparent,
        StrokeShape = new RoundRectangle { CornerRadius = radius },
    };

    /// <summary>A one-unit line (a divider) across or down.</summary>
    public BoxView Line(bool vertical = false) => vertical
        ? new BoxView { Color = Border, WidthRequest = 1, VerticalOptions = LayoutOptions.Fill }
        : new BoxView { Color = Border, HeightRequest = 1, HorizontalOptions = LayoutOptions.Fill };

    /// <summary>A state chip in the design's colours (ok, overrides, problem, off).</summary>
    public Border Chip(string text, Craft.Editor.Shell.PluginState state)
    {
        // The chip's fill is its hue mixed into the surface, its text the hue mixed toward the text: both from the theme.
        uint surface = Theme.Colors.Surface;
        uint hue = state switch
        {
            Craft.Editor.Shell.PluginState.Active => Theme.Colors.Success,
            Craft.Editor.Shell.PluginState.Outdated => Theme.Colors.Info,
            Craft.Editor.Shell.PluginState.Problem => Theme.Colors.Error,
            _ => Theme.Colors.SecondaryText,
        };
        uint fill = ColorContrast.Mix(surface, hue, 0.18);
        uint ink = ColorContrast.Ensure(ColorContrast.Mix(hue, Theme.Colors.Text, 0.55), ColorContrast.Text, [fill]);
        return Box(Label(text, ThemeColors.Of(ink), SmallSize), 10, new Thickness(8, 2), ThemeColors.Of(fill), Colors.Transparent);
    }
}

/// <summary>
/// The logo (docs/design/logo.svg, the owner's choice): the rounded dark square, the thick amber C, the white caret and
/// spark, drawn from the file's geometry at any size; the C takes the theme's accent.
/// </summary>
public sealed class LogoDrawable : IDrawable
{
    /// <summary>The C's colour (the accent).</summary>
    public Color Accent { get; set; } = Color.FromRgb(0xE5, 0xA5, 0x4B);

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        float size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        float k = size / 456f;
        canvas.SaveState();
        canvas.Translate(dirtyRect.X, dirtyRect.Y);
        var square = new RectF(16 * k, 16 * k, 424 * k, 424 * k);
        canvas.SetFillPaint(new LinearGradientPaint(
            [new PaintGradientStop(0, Color.FromRgb(0x26, 0x2B, 0x36)), new PaintGradientStop(1, Color.FromRgb(0x10, 0x12, 0x17))],
            new Point(0, 0), new Point(1, 1)), square);
        canvas.FillRoundedRectangle(square, 140 * k);

        // The C: an arc of radius 120 about (228, 228) from 316 degrees to 44 degrees the long way (the file's path).
        canvas.StrokeColor = Accent;
        canvas.StrokeSize = 44 * k;
        canvas.StrokeLineCap = LineCap.Round;
        var arc = new PathF();
        const float Cx = 228, Cy = 228, R = 120;
        for (int step = 0; step <= 48; step++)
        {
            double angle = (-44 - (272.0 * step / 48)) * Math.PI / 180;
            float x = (float)(Cx + (R * Math.Cos(angle))) * k;
            float y = (float)(Cy - (R * Math.Sin(angle))) * k;
            if (step == 0)
            {
                arc.MoveTo(x, y);
            }
            else
            {
                arc.LineTo(x, y);
            }
        }

        canvas.DrawPath(arc);
        var ink = Color.FromRgb(0xF4, 0xF1, 0xEA);
        canvas.FillColor = ink;
        canvas.FillRoundedRectangle(250 * k, 176 * k, 26 * k, 104 * k, 13 * k);
        var spark = new PathF();
        const float Sx = 336, Sy = 112, S = 0.75f;
        ReadOnlySpan<float> points = [0, -36, 10, -10, 36, 0, 10, 10, 0, 36, -10, 10, -36, 0, -10, -10];
        for (int i = 0; i < points.Length; i += 2)
        {
            float x = (Sx + (points[i] * S)) * k;
            float y = (Sy + (points[i + 1] * S)) * k;
            if (i == 0)
            {
                spark.MoveTo(x, y);
            }
            else
            {
                spark.LineTo(x, y);
            }
        }

        spark.Close();
        canvas.FillPath(spark);
        canvas.RestoreState();
    }
}

/// <summary>A stroke icon from SVG path data on a 24 by 24 box (the rail's icons, <see cref="RailIcons"/>).</summary>
public sealed class StrokeIcon : ContentView
{
    private readonly MauiPath _path;

    /// <summary>The icon of <paramref name="data"/>, <paramref name="size"/> units square.</summary>
    public StrokeIcon(string data, double size = 20, double strokeWidth = 1.6)
    {
        ArgumentNullException.ThrowIfNull(data);
        _path = new MauiPath
        {
            Data = (Geometry?)new PathGeometryConverter().ConvertFromInvariantString(data),
            StrokeThickness = strokeWidth,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Colors.Transparent,
            WidthRequest = size,
            HeightRequest = size,
            Aspect = Stretch.Uniform,
        };
        WidthRequest = size;
        HeightRequest = size;
        InputTransparent = true;
        Content = _path;
    }

    /// <summary>The stroke's colour.</summary>
    public Color Color
    {
        get => (_path.Stroke as SolidColorBrush)?.Color ?? Colors.Gray;
        set => _path.Stroke = new SolidColorBrush(value);
    }
}
