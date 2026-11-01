// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Abstractions;

/// <summary>How a run of text is drawn: its colour (0xAARRGGBB) and whether it is bold or italic.</summary>
/// <param name="Argb">The colour, alpha first.</param>
/// <param name="Bold">Drawn bold.</param>
/// <param name="Italic">Drawn italic.</param>
public readonly record struct TextStyle(uint Argb, bool Bold = false, bool Italic = false);

/// <summary>The colours of the editor's surface (0xAARRGGBB each).</summary>
/// <param name="Background">The text area.</param>
/// <param name="Foreground">Plain text.</param>
/// <param name="Selection">The selection's fill.</param>
/// <param name="CurrentLine">The caret line's fill.</param>
/// <param name="Caret">The caret.</param>
/// <param name="Gutter">The line numbers' column.</param>
/// <param name="GutterText">A line number.</param>
/// <param name="GutterCurrentText">The caret line's number.</param>
/// <param name="BracketMatch">The fill behind a matched bracket pair.</param>
/// <param name="FindMatch">The fill behind a find match.</param>
public sealed record EditorColors(uint Background, uint Foreground, uint Selection, uint CurrentLine, uint Caret, uint Gutter,
    uint GutterText, uint GutterCurrentText, uint BracketMatch, uint FindMatch)
{
    /// <summary>An error's squiggle (and its mark in the problems list).</summary>
    public uint Error { get; init; } = 0xFFE5534B;

    /// <summary>A warning's squiggle.</summary>
    public uint Warning { get; init; } = 0xFFC69026;

    /// <summary>An informational diagnostic's dotted underline.</summary>
    public uint Info { get; init; } = 0xFF4A90D9;

    /// <summary>The fill of the editor's popups (the completion list, signature help, hover).</summary>
    public uint PopupBackground { get; init; } = 0xFF2A2E36;

    /// <summary>The popups' border.</summary>
    public uint PopupBorder { get; init; } = 0xFF5A6270;

    /// <summary>The selected row of the completion list.</summary>
    public uint PopupSelection { get; init; } = 0xFF2C3A55;

    /// <summary>The popups' text.</summary>
    public uint PopupText { get; init; } = 0xFFD5D9E0;

    // The approved design's tokens (docs/design, phase D), for the app's own screens (the diff view first).

    /// <summary>The window's ground, behind the surfaces.</summary>
    public uint Ground { get; init; } = 0xFF101217;

    /// <summary>A panel's or a page's surface.</summary>
    public uint Surface { get; init; } = 0xFF16181D;

    /// <summary>A surface's border and dividers.</summary>
    public uint Border { get; init; } = 0xFF2A2E37;

    /// <summary>Secondary text (labels, line numbers, details).</summary>
    public uint SecondaryText { get; init; } = 0xFF9AA2B1;

    /// <summary>The accent (the primary action, the active tab's line).</summary>
    public uint Accent { get; init; } = 0xFFE5A54B;

    /// <summary>Success (an added line's marker, a check that passed).</summary>
    public uint Success { get; init; } = 0xFF5FC9B4;

    /// <summary>An added line's background in a diff.</summary>
    public uint DiffAdded { get; init; } = 0xFF15302C;

    /// <summary>A removed line's background in a diff.</summary>
    public uint DiffRemoved { get; init; } = 0xFF3A1F26;

    // Phase D: the rest of the design's interface tokens. Each defaults from the constructor's colours, so a theme made
    // before them (a plug-in's) still draws the shell in its own colours.

    /// <summary>A raised control's fill (an input, the search box, a card).</summary>
    public uint Raised { get; init; } = CurrentLine;

    /// <summary>A higher control's fill (a button, the active rail item, a chip).</summary>
    public uint RaisedHigh { get; init; } = CurrentLine;

    /// <summary>The interface's selected row (the explorer's file, a list's pick, a segmented control's choice).</summary>
    public uint InterfaceSelection { get; init; } = Selection;

    /// <summary>A stronger border (a button's, an input's, a popup's).</summary>
    public uint BorderStrong { get; init; } = GutterText;

    /// <summary>The interface's text (labels, menus, tabs).</summary>
    public uint Text { get; init; } = Foreground;

    /// <summary>Text drawn on the accent (the primary button's label).</summary>
    public uint AccentInk { get; init; } = Background;

    /// <summary>Ghost text: an inline completion not yet accepted, drawn italic (the design's #717A8A lifted to 4.8:1 on the dark editor ground).</summary>
    public uint GhostText { get; init; } = 0xFF7D8696;

    // Phase X: the editing surface's marks. Each defaults from the constructor's colours (a theme made before them draws
    // them in its own colours): faint lines from the line numbers' colour, the word's fill from the selection's.

    /// <summary>An indentation guide: a thin line at each indent level of the leading white space.</summary>
    public uint IndentGuide { get; init; } = (GutterText & 0x00FFFFFFu) | 0x40000000u;

    /// <summary>The other places of the word at the caret (the current word's highlight).</summary>
    public uint WordHighlight { get; init; } = (Selection & 0x00FFFFFFu) | 0x70000000u;

    private uint? _spelling;

    /// <summary>
    /// A word the spell check does not know (X part 9): its faint squiggle under a comment's or a string's word; by default the
    /// info colour at 60% (a theme made before it draws it in its own info colour).
    /// </summary>
    public uint Spelling
    {
        get => _spelling ?? ((Info & 0x00FFFFFFu) | 0x99000000u);
        init => _spelling = value;
    }

    /// <summary>A fold's chevron in the gutter.</summary>
    public uint FoldChevron { get; init; } = GutterText;

    /// <summary>The marker drawn after a folded line's text (the hidden lines' "...").</summary>
    public uint FoldMarker { get; init; } = (GutterText & 0x00FFFFFFu) | 0x50000000u;

    // The X restart: the overview ruler's marks. Each follows the theme's own colour for the same thing unless a theme sets
    // it (a plug-in's theme made before them draws the ruler in its own error, warning, find and caret colours).

    private uint? _rulerError;
    private uint? _rulerWarning;
    private uint? _rulerFind;
    private uint? _rulerCaret;

    /// <summary>An error's mark on the overview ruler (the theme's <see cref="Error"/> unless set).</summary>
    public uint RulerError { get => _rulerError ?? Error; init => _rulerError = value; }

    /// <summary>A warning's mark on the overview ruler (the theme's <see cref="Warning"/> unless set).</summary>
    public uint RulerWarning { get => _rulerWarning ?? Warning; init => _rulerWarning = value; }

    /// <summary>A search hit's mark on the overview ruler (the find colour, opaque enough to read on the ruler, unless set).</summary>
    public uint RulerFind { get => _rulerFind ?? ((FindMatch & 0x00FFFFFFu) | 0xC0000000u); init => _rulerFind = value; }

    /// <summary>A caret's mark on the overview ruler (the caret's colour, a little see-through, unless set).</summary>
    public uint RulerCaret { get => _rulerCaret ?? ((Caret & 0x00FFFFFFu) | 0xB0000000u); init => _rulerCaret = value; }

    // The X restart, piece 2b: the minimap's slider over the lines the viewport shows. It follows the theme's text colour,
    // see-through, unless a theme sets it (a theme made before it draws the slider in its own colours).

    private uint? _minimapSlider;
    private uint? _minimapSliderActive;

    /// <summary>The minimap's slider: the band of the lines the viewport shows (the text colour, faint, unless set).</summary>
    public uint MinimapSlider { get => _minimapSlider ?? ((Foreground & 0x00FFFFFFu) | 0x1C000000u); init => _minimapSlider = value; }

    /// <summary>The minimap's slider while it is dragged (the text colour, a little stronger, unless set).</summary>
    public uint MinimapSliderActive { get => _minimapSliderActive ?? ((Foreground & 0x00FFFFFFu) | 0x34000000u); init => _minimapSliderActive = value; }

    // X part 9: the editing helpers' marks. Each follows the theme's own colours unless a theme sets it (a plug-in's theme
    // made before them draws them in its own accent, info, success, warning, error and line-number colours).

    private uint? _bracket1;
    private uint? _bracket2;
    private uint? _bracket3;
    private uint? _bracket4;
    private uint? _bracket5;
    private uint? _bracket6;
    private uint? _bracketUnmatched;
    private uint? _whitespace;
    private uint? _verticalRuler;
    private uint? _inlineError;
    private uint? _inlineWarning;
    private uint? _inlineInfo;
    private uint? _lightbulb;

    /// <summary>A bracket at nesting depth 1 (outermost) with bracket pair colours on (the theme's <see cref="Accent"/> unless set).</summary>
    public uint BracketDepth1 { get => _bracket1 ?? Accent; init => _bracket1 = value; }

    /// <summary>A bracket at depth 2 (the theme's <see cref="Info"/> unless set).</summary>
    public uint BracketDepth2 { get => _bracket2 ?? Info; init => _bracket2 = value; }

    /// <summary>A bracket at depth 3 (the theme's <see cref="Success"/> unless set).</summary>
    public uint BracketDepth3 { get => _bracket3 ?? Success; init => _bracket3 = value; }

    /// <summary>A bracket at depth 4 (depth 1's colour mixed a third toward the text's, unless set).</summary>
    public uint BracketDepth4 { get => _bracket4 ?? ColorContrast.Mix(BracketDepth1, Foreground, 0.35); init => _bracket4 = value; }

    /// <summary>A bracket at depth 5 (depth 2's colour mixed a third toward the text's, unless set).</summary>
    public uint BracketDepth5 { get => _bracket5 ?? ColorContrast.Mix(BracketDepth2, Foreground, 0.35); init => _bracket5 = value; }

    /// <summary>A bracket at depth 6 (depth 3's colour mixed a third toward the text's, unless set); deeper brackets take the six in turn again.</summary>
    public uint BracketDepth6 { get => _bracket6 ?? ColorContrast.Mix(BracketDepth3, Foreground, 0.35); init => _bracket6 = value; }

    /// <summary>A bracket with no partner (the theme's <see cref="Error"/> unless set).</summary>
    public uint BracketUnmatched { get => _bracketUnmatched ?? Error; init => _bracketUnmatched = value; }

    /// <summary>Rendered white space: a space's dot and a tab's arrow (the line numbers' colour, faint, unless set).</summary>
    public uint Whitespace { get => _whitespace ?? ((GutterText & 0x00FFFFFFu) | 0x70000000u); init => _whitespace = value; }

    /// <summary>A vertical ruler at a column (the line numbers' colour, faint, unless set).</summary>
    public uint VerticalRuler { get => _verticalRuler ?? ((GutterText & 0x00FFFFFFu) | 0x38000000u); init => _verticalRuler = value; }

    /// <summary>An error's message drawn faded after its line's text (the error colour, see-through, unless set).</summary>
    public uint InlineError { get => _inlineError ?? ((Error & 0x00FFFFFFu) | 0xA8000000u); init => _inlineError = value; }

    /// <summary>A warning's message after its line's text (the warning colour, see-through, unless set).</summary>
    public uint InlineWarning { get => _inlineWarning ?? ((Warning & 0x00FFFFFFu) | 0xA8000000u); init => _inlineWarning = value; }

    /// <summary>An informational message after its line's text (the info colour, see-through, unless set).</summary>
    public uint InlineInfo { get => _inlineInfo ?? ((Info & 0x00FFFFFFu) | 0xA8000000u); init => _inlineInfo = value; }

    /// <summary>The lightbulb in the gutter where code actions exist (the theme's <see cref="Warning"/> unless set).</summary>
    public uint Lightbulb { get => _lightbulb ?? Warning; init => _lightbulb = value; }

    /// <summary>The colour of a bracket at <paramref name="depth"/> (1 the outermost): the six depth colours in turn.</summary>
    public uint BracketDepth(int depth) => ((Math.Max(1, depth) - 1) % 6) switch
    {
        0 => BracketDepth1,
        1 => BracketDepth2,
        2 => BracketDepth3,
        3 => BracketDepth4,
        4 => BracketDepth5,
        _ => BracketDepth6,
    };
}

/// <summary>
/// Colour contrast as WCAG 2 measures it, for themes: the ratio of two colours' relative luminance, and a colour moved
/// toward black or white until it reaches a ratio against every given background. A theme's text needs 4.5:1 on what it
/// is drawn on, large text and lines (a caret, the accent line) 3:1.
/// </summary>
public static class ColorContrast
{
    /// <summary>The ratio normal text needs.</summary>
    public const double Text = 4.5;

    /// <summary>The ratio large text and lines need.</summary>
    public const double Line = 3.0;

    /// <summary>The ratio WCAG's enhanced level (AAA) asks of normal text, which the high-contrast themes give text and focus.</summary>
    public const double Enhanced = 7.0;

    /// <summary>The relative luminance of <paramref name="argb"/> (0 black, 1 white; alpha ignored).</summary>
    public static double Luminance(uint argb) =>
        (0.2126 * Channel((argb >> 16) & 0xFF)) + (0.7152 * Channel((argb >> 8) & 0xFF)) + (0.0722 * Channel(argb & 0xFF));

    /// <summary>The contrast ratio of <paramref name="a"/> and <paramref name="b"/>, from 1 to 21.</summary>
    public static double Ratio(uint a, uint b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The smallest ratio of <paramref name="color"/> against any of <paramref name="backgrounds"/>.</summary>
    public static double MinRatio(uint color, ReadOnlySpan<uint> backgrounds)
    {
        double min = double.MaxValue;
        foreach (var background in backgrounds)
        {
            min = Math.Min(min, Ratio(color, background));
        }

        return min;
    }

    /// <summary>
    /// <paramref name="color"/> when it reads at <paramref name="ratio"/> or more on every one of
    /// <paramref name="backgrounds"/>; else the colour mixed toward black (on light backgrounds) or white (on dark ones), in
    /// steps of 1/64, until it does (black or white at the end).
    /// </summary>
    public static uint Ensure(uint color, double ratio, ReadOnlySpan<uint> backgrounds)
    {
        if (backgrounds.IsEmpty || MinRatio(color, backgrounds) >= ratio)
        {
            return color;
        }

        double average = 0;
        foreach (var background in backgrounds)
        {
            average += Luminance(background);
        }

        uint target = average / backgrounds.Length > 0.18 ? 0xFF000000u : 0xFFFFFFFFu;
        for (int step = 1; step <= 64; step++)
        {
            uint mixed = Mix(color, target, step / 64.0);
            if (MinRatio(mixed, backgrounds) >= ratio)
            {
                return mixed;
            }
        }

        return target;
    }

    /// <summary><paramref name="a"/> mixed with <paramref name="b"/> by <paramref name="amount"/> (0 all a, 1 all b), opaque.</summary>
    public static uint Mix(uint a, uint b, double amount)
    {
        static uint Lerp(uint x, uint y, double t) => (uint)Math.Round(x + ((double)y - x) * t);
        return 0xFF000000u
            | (Lerp((a >> 16) & 0xFF, (b >> 16) & 0xFF, amount) << 16)
            | (Lerp((a >> 8) & 0xFF, (b >> 8) & 0xFF, amount) << 8)
            | Lerp(a & 0xFF, b & 0xFF, amount);
    }

    private static double Channel(uint value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}

/// <summary>
/// The accent colours the design offers (<c>CRAFT_ACCENT</c>: one of these names or any <c>#RRGGBB</c>): amber (the
/// default), teal, blue and rose.
/// </summary>
public static class AccentColors
{
    /// <summary>The design's default, #E5A54B.</summary>
    public const uint Amber = 0xFFE5A54B;

    /// <summary>#5FC9B4.</summary>
    public const uint Teal = 0xFF5FC9B4;

    /// <summary>#82AAFF.</summary>
    public const uint Blue = 0xFF82AAFF;

    /// <summary>#E58BA0.</summary>
    public const uint Rose = 0xFFE58BA0;

    /// <summary>The names, in the design's order.</summary>
    public static IReadOnlyList<string> Names { get; } = ["amber", "teal", "blue", "rose"];

    /// <summary>Reads a name (ignoring case) or <c>#RRGGBB</c>; false when it is neither.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out uint argb)
    {
        text = text.Trim();
        argb = 0;
        if (text.Equals("amber", StringComparison.OrdinalIgnoreCase))
        {
            argb = Amber;
        }
        else if (text.Equals("teal", StringComparison.OrdinalIgnoreCase))
        {
            argb = Teal;
        }
        else if (text.Equals("blue", StringComparison.OrdinalIgnoreCase))
        {
            argb = Blue;
        }
        else if (text.Equals("rose", StringComparison.OrdinalIgnoreCase))
        {
            argb = Rose;
        }
        else if (text.Length == 7 && text[0] == '#'
            && uint.TryParse(text[1..], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
        {
            argb = 0xFF000000u | rgb;
        }
        else
        {
            return false;
        }

        return true;
    }
}

/// <summary>A theme, registered in <see cref="EditorThemes"/>: the surface's colours and a style per token class.</summary>
public interface ITheme : IKind
{
    /// <summary>Whether it is a dark theme (the app's own chrome follows it).</summary>
    bool IsDark { get; }

    /// <summary>The surface's colours.</summary>
    EditorColors Colors { get; }

    /// <summary>The style of a token class (see <see cref="TokenClasses"/>); plain text for a class it does not know.</summary>
    TextStyle StyleOf(string tokenClass);
}

/// <summary>A theme from a table of styles by class name; a class not in it is drawn as plain text.</summary>
public sealed class TableTheme : ITheme
{
    private readonly FrozenDictionary<string, TextStyle> _styles;
    private readonly TextStyle _plain;

    /// <summary>A theme named <paramref name="name"/> with <paramref name="colors"/> and <paramref name="styles"/> by class name.</summary>
    public TableTheme(string name, string summary, bool isDark, EditorColors colors, IReadOnlyDictionary<string, TextStyle> styles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(styles);
        Name = name;
        Summary = summary;
        IsDark = isDark;
        Colors = colors;
        _plain = new TextStyle(colors.Foreground);
        // A static name's class ("method name + static symbol"): its own class's style with the static symbol's bold and
        // italic over it, made here once, so StyleOf stays one lookup.
        var all = new Dictionary<string, TextStyle>(styles, StringComparer.Ordinal);
        if (styles.TryGetValue(TokenClasses.StaticSymbol, out var @static))
        {
            foreach (var (tokenClass, style) in styles)
            {
                if (tokenClass != TokenClasses.StaticSymbol && !tokenClass.EndsWith(TokenClasses.StaticSymbolSuffix, StringComparison.Ordinal))
                {
                    all.TryAdd(TokenClasses.WithStaticSymbol(tokenClass), style with { Bold = style.Bold || @static.Bold, Italic = style.Italic || @static.Italic });
                }
            }
        }

        _styles = all.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <inheritdoc />
    public bool IsDark { get; }

    /// <inheritdoc />
    public EditorColors Colors { get; }

    /// <inheritdoc />
    public TextStyle StyleOf(string tokenClass) => _styles.TryGetValue(tokenClass, out var style) ? style : _plain;
}

/// <summary>
/// The themes, by name (ignoring case). The library's: <see cref="Dark"/> and <see cref="Light"/>. Register another with
/// <see cref="Register"/>; one under a library name shadows it, which <see cref="Unregister"/> brings back.
/// </summary>
public static class EditorThemes
{
    /// <summary>The library's dark theme.</summary>
    public const string Dark = "dark";

    /// <summary>The library's light theme.</summary>
    public const string Light = "light";

    /// <summary>The library's second dark theme: a slate ground, the teal accent.</summary>
    public const string Dusk = "dusk";

    /// <summary>The library's second light theme: a warm paper ground, the blue accent.</summary>
    public const string Dawn = "dawn";

    /// <summary>The library's high-contrast dark theme: text, every code class and the focus at 7:1 or more (WCAG AAA).</summary>
    public const string HighContrastDark = "high-contrast-dark";

    /// <summary>The library's high-contrast light theme: text, every code class and the focus at 7:1 or more (WCAG AAA).</summary>
    public const string HighContrastLight = "high-contrast-light";

    /// <summary>The name a setting gives to follow the system's light or dark (<see cref="ForSystem"/>); no theme is registered under it.</summary>
    public const string System = "system";

    private static readonly KindTable<ITheme> Table = BuiltIn();

    private static KindTable<ITheme> BuiltIn()
    {
        var table = new KindTable<ITheme>(nameof(EditorThemes), "Theme", "add it with EditorThemes.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a theme is data, read as it is");
        table.RegisterDefault(MakeDark());
        table.RegisterDefault(MakeLight());
        table.RegisterDefault(MakeDusk());
        table.RegisterDefault(MakeDawn());
        table.RegisterDefault(MakeHighContrastDark());
        table.RegisterDefault(MakeHighContrastLight());
        return table;
    }

    /// <summary>Who registered the theme <paramref name="name"/>: the library ("library"), or the assembly of the app or plug-in; null when none is.</summary>
    public static string? Origin(string name) => Table.Origin(name);

    /// <summary>Whether the theme <paramref name="name"/> is one of the library's own (not shadowed by an app's or a plug-in's).</summary>
    public static bool IsBuiltIn(string name) => Table.Default(name) is { } builtIn && ReferenceEquals(Table.Find(name), builtIn);

    /// <summary>The theme the system's preference asks for (<see cref="System"/>): <see cref="Dark"/> or <see cref="Light"/>.</summary>
    public static ITheme ForSystem(bool dark) => Get(dark ? Dark : Light);

    /// <summary>Registers <paramref name="theme"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Table.Register(theme, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's theme of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The theme of that name, or null.</summary>
    public static ITheme? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The theme of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ITheme Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// <paramref name="theme"/> with <paramref name="accent"/> as its accent and caret (<c>CRAFT_ACCENT</c>): moved until it
    /// reads at 4.5:1 on the theme's ground, surfaces and selected row (it marks text too), and its ink the theme's
    /// background or text, whichever reads on it. The same name, styles and darkness.
    /// </summary>
    public static ITheme WithAccent(ITheme theme, uint accent)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var colors = theme.Colors;
        uint fitted = ColorContrast.Ensure(accent | 0xFF000000u, ColorContrast.Text,
            [colors.Ground, colors.Surface, colors.Raised, colors.RaisedHigh, colors.InterfaceSelection]);
        uint ink = ColorContrast.Ratio(colors.AccentInk, fitted) >= ColorContrast.Text ? colors.AccentInk
            : ColorContrast.Ratio(colors.Text, fitted) >= ColorContrast.Text ? colors.Text
            : ColorContrast.Ratio(0xFF000000u, fitted) >= ColorContrast.Ratio(0xFFFFFFFFu, fitted) ? 0xFF000000u : 0xFFFFFFFFu;
        return new AccentTheme(theme, colors with { Accent = fitted, Caret = fitted, AccentInk = ink });
    }

    private sealed class AccentTheme(ITheme inner, EditorColors colors) : ITheme
    {
        public string Name => inner.Name;

        public string Summary => inner.Summary;

        public bool IsDark => inner.IsDark;

        public EditorColors Colors { get; } = colors;

        public TextStyle StyleOf(string tokenClass) => inner.StyleOf(tokenClass);
    }

    private static TableTheme MakeDark() => new TableTheme(Dark, "Dark: a deep ground, amber accent", isDark: true,
        new EditorColors(Background: 0xFF16181D, Foreground: 0xFFD5D9E0, Selection: 0xFF2C3A55, CurrentLine: 0xFF1E222B, Caret: 0xFFE5A54B,
            Gutter: 0xFF16181D, GutterText: 0xFF7D8696, GutterCurrentText: 0xFFD5D9E0, BracketMatch: 0xFF34404F, FindMatch: 0xFF5A4520)
        {
            Error = 0xFFF07178, Warning = 0xFFE3B27A, Info = 0xFF6CB6FF,
            PopupBackground = 0xFF22262E, PopupBorder = 0xFF4A5260, PopupSelection = 0xFF2C3A55, PopupText = 0xFFD5D9E0,
            Ground = 0xFF101217, Surface = 0xFF16181D, Border = 0xFF2A2E37, SecondaryText = 0xFF9AA2B1, Accent = 0xFFE5A54B, Success = 0xFF5FC9B4,
            DiffAdded = 0xFF15302C, DiffRemoved = 0xFF3A1F26,
            Raised = 0xFF1A1D23, RaisedHigh = 0xFF1F232B, InterfaceSelection = 0xFF252A33, BorderStrong = 0xFF343944, Text = 0xFFD5D9E0,
            AccentInk = 0xFF1A1206,
        },
        // The design's code classes (docs/design/Main.dc.html): .k keyword, .t every type name (classes, interfaces, structs,
        // enums, delegates), .s string, .c comment (italic), .m method, .v locals and parameters, .p plain text and
        // punctuation. The classes the design does not show keep E5's hues.
        Styles(keyword: 0xFF82AAFF, type: 0xFF5FC9B4, @string: 0xFFE3B27A, comment: 0xFF8790A0, number: 0xFFD9A0E8, method: 0xFFE8D58A,
            local: 0xFFB8C7DC, preprocessor: 0xFFB09EE0, excluded: 0xFF6B7382, punctuation: 0xFFD5D9E0,
            new Semantic(Value: 0xFF5FC9B4, Interface: 0xFF5FC9B4, TypeParameter: 0xFF7FD3E6, Member: 0xFF9CCBF0, Parameter: 0xFFB8C7DC,
                Namespace: 0xFFC8CDD6, Constant: 0xFFE0A7C2)));

    // The light theme is derived from the dark one's roles: a paper ground, deep inks, and every status colour and the
    // accent moved (ColorContrast.Ensure) until it reads at 4.5:1 on the ground, the surfaces and the selected row.
    private static TableTheme MakeLight()
    {
        const uint ground = 0xFFF3F4F6, surface = 0xFFFFFFFF, raised = 0xFFF6F8FA, raisedHigh = 0xFFEEF1F4, selection = 0xFFDDE4EC, caretLine = 0xFFF3F5F8;
        uint[] grounds = [ground, surface, raised, raisedHigh, selection, caretLine];
        uint Fit(uint color) => ColorContrast.Ensure(color, ColorContrast.Text, grounds);
        uint accent = Fit(0xFFB2650F);
        return new TableTheme(Light, "Light: a paper ground, deep inks", isDark: false,
            new EditorColors(Background: surface, Foreground: 0xFF1F2328, Selection: 0xFFC8DAF8, CurrentLine: caretLine, Caret: accent,
                Gutter: surface, GutterText: 0xFF6E7781, GutterCurrentText: 0xFF1F2328, BracketMatch: 0xFFDDE6F0, FindMatch: 0xFFFFE3A3)
            {
                Error = Fit(0xFFCF222E), Warning = Fit(0xFF9A6700), Info = Fit(0xFF0969DA),
                PopupBackground = 0xFFF6F8FA, PopupBorder = 0xFF8C959F, PopupSelection = 0xFFC8DAF8, PopupText = 0xFF1F2328,
                Ground = ground, Surface = surface, Border = 0xFFD0D7DE, SecondaryText = Fit(0xFF57606A), Accent = accent, Success = Fit(0xFF0E7A6B),
                DiffAdded = 0xFFDDF4EC, DiffRemoved = 0xFFFCE4E6,
                Raised = raised, RaisedHigh = raisedHigh, InterfaceSelection = selection, BorderStrong = 0xFF8C959F, Text = 0xFF1F2328,
                AccentInk = 0xFFFFFFFF, GhostText = Fit(0xFF6E7781),
            },
            // The same class mapping as the dark theme's (every type name one hue, parameters as locals, punctuation plain).
            Styles(keyword: 0xFF0B4FC6, type: 0xFF0E7A6B, @string: 0xFF9A4A06, comment: 0xFF5F6B78, number: 0xFF8A2BB8, method: 0xFF795E00,
                local: 0xFF2A4A75, preprocessor: 0xFF6F42C1, excluded: 0xFF8C959F, punctuation: 0xFF1F2328,
                new Semantic(Value: 0xFF0E7A6B, Interface: 0xFF0E7A6B, TypeParameter: 0xFF16697F, Member: 0xFF0B5C8E, Parameter: 0xFF2A4A75,
                    Namespace: 0xFF404852, Constant: 0xFF9C2F6B)));
    }

    private static TableTheme MakeDusk() => Fitted(Dusk, "Dusk: a slate ground, teal accent", isDark: true, ColorContrast.Text, new Roles(
        Ground: 0xFF151821, Surface: 0xFF1B1F2A, Raised: 0xFF20242F, RaisedHigh: 0xFF262B37, Selection: 0xFF2D3342, CaretLine: 0xFF222734,
        TextSelection: 0xFF33415C, Border: 0xFF2F3544, BorderStrong: 0xFF3C4354, Text: 0xFFDCE1EA, Secondary: 0xFFA0A8B8, Accent: AccentColors.Teal,
        Success: 0xFF7FD69A, Warning: 0xFFE8C07A, Error: 0xFFF2838A, Info: 0xFF7FB2FF, GutterText: 0xFF838CA0, DiffAdded: 0xFF17332E,
        DiffRemoved: 0xFF3B2029, BracketMatch: 0xFF374357, FindMatch: 0xFF5A4A22),
        new Code(Keyword: 0xFFC3A6FF, Type: 0xFF5FC9B4, String: 0xFFE8C07A, Comment: 0xFF8B93A6, Number: 0xFFF0A3B5, Method: 0xFF82AAFF, Local: 0xFFC3CEDE,
            Preprocessor: 0xFFB7A2E8, Excluded: 0xFF6F778A, Punctuation: 0xFFDCE1EA,
            new Semantic(Value: 0xFF7AD3C0, Interface: 0xFF7AD3C0, TypeParameter: 0xFF8ADBEA, Member: 0xFFA6CCF2, Parameter: 0xFFC3CEDE, Namespace: 0xFFCBD1DC, Constant: 0xFFE6ABC8)));

    private static TableTheme MakeDawn() => Fitted(Dawn, "Dawn: a warm paper ground, blue accent", isDark: false, ColorContrast.Text, new Roles(
        Ground: 0xFFF6F3EE, Surface: 0xFFFFFDF9, Raised: 0xFFF9F6F1, RaisedHigh: 0xFFF1EDE6, Selection: 0xFFE4DED3, CaretLine: 0xFFF6F2EA,
        TextSelection: 0xFFD3E2F7, Border: 0xFFDDD6CA, BorderStrong: 0xFF948B7D, Text: 0xFF26231F, Secondary: 0xFF5E584F, Accent: 0xFF1F5FBF,
        Success: 0xFF2A7A4B, Warning: 0xFF8F5F00, Error: 0xFFB8323C, Info: 0xFF1F5FBF, GutterText: 0xFF7A7266, DiffAdded: 0xFFDFF2E3,
        DiffRemoved: 0xFFF8E1E1, BracketMatch: 0xFFE6E0D5, FindMatch: 0xFFFFE2A0),
        new Code(Keyword: 0xFF8A3FB8, Type: 0xFF0E6E80, String: 0xFF9A5B0A, Comment: 0xFF6E6759, Number: 0xFFB0306A, Method: 0xFF1F5FBF, Local: 0xFF374A66,
            Preprocessor: 0xFF6F42C1, Excluded: 0xFF948B7D, Punctuation: 0xFF26231F,
            new Semantic(Value: 0xFF0E6E80, Interface: 0xFF0E6E80, TypeParameter: 0xFF16697F, Member: 0xFF2F5C8A, Parameter: 0xFF374A66, Namespace: 0xFF4A453E, Constant: 0xFF9C2F6B)));

    // The high-contrast pair: every text colour, every code class, the accent (the focus ring, the caret) and the line
    // numbers fitted to 7:1 (WCAG AAA) on every ground they are drawn on, the text selection included; borders strong.
    private static TableTheme MakeHighContrastDark() => Fitted(HighContrastDark, "High contrast dark: black ground, 7:1 text and focus (WCAG AAA)", isDark: true, ColorContrast.Enhanced, new Roles(
        Ground: 0xFF000000, Surface: 0xFF000000, Raised: 0xFF0A0A0A, RaisedHigh: 0xFF141414, Selection: 0xFF1C2E4A, CaretLine: 0xFF101010,
        TextSelection: 0xFF1F3F6B, Border: 0xFF7A7A7A, BorderStrong: 0xFFE0E0E0, Text: 0xFFFFFFFF, Secondary: 0xFFD4D4D4, Accent: 0xFFFFD60A,
        Success: 0xFF7CF0B0, Warning: 0xFFFFC266, Error: 0xFFFF8A8A, Info: 0xFF8CC8FF, GutterText: 0xFFC8C8C8, DiffAdded: 0xFF0F3A1E,
        DiffRemoved: 0xFF4A1414, BracketMatch: 0xFF2A3A55, FindMatch: 0xFF5A4600),
        new Code(Keyword: 0xFF8CC8FF, Type: 0xFF7CF0D0, String: 0xFFFFD27A, Comment: 0xFFB8C4B0, Number: 0xFFF5B8FF, Method: 0xFFFFF09A, Local: 0xFFE6EEFF,
            Preprocessor: 0xFFD5C2FF, Excluded: 0xFFB0B0B0, Punctuation: 0xFFFFFFFF,
            new Semantic(Value: 0xFF7CF0D0, Interface: 0xFF7CF0D0, TypeParameter: 0xFF9EF2FF, Member: 0xFFC2E2FF, Parameter: 0xFFE6EEFF, Namespace: 0xFFEDEDED, Constant: 0xFFFFC2E2)));

    private static TableTheme MakeHighContrastLight() => Fitted(HighContrastLight, "High contrast light: white ground, 7:1 text and focus (WCAG AAA)", isDark: false, ColorContrast.Enhanced, new Roles(
        Ground: 0xFFFFFFFF, Surface: 0xFFFFFFFF, Raised: 0xFFFAFAFA, RaisedHigh: 0xFFF0F0F0, Selection: 0xFFD6E6FF, CaretLine: 0xFFF4F4F4,
        TextSelection: 0xFFC4DCFF, Border: 0xFF6B6B6B, BorderStrong: 0xFF1A1A1A, Text: 0xFF000000, Secondary: 0xFF2E2E2E, Accent: 0xFF0B3D91,
        Success: 0xFF0A5C2E, Warning: 0xFF6B4100, Error: 0xFF9E0F1E, Info: 0xFF0B3D91, GutterText: 0xFF3A3A3A, DiffAdded: 0xFFD4F2DC,
        DiffRemoved: 0xFFFBD9D9, BracketMatch: 0xFFD0DCEF, FindMatch: 0xFFFFE07A),
        new Code(Keyword: 0xFF0B3D91, Type: 0xFF00585E, String: 0xFF7A3B00, Comment: 0xFF3D4A34, Number: 0xFF7A1E6E, Method: 0xFF5A4200, Local: 0xFF1A2A44,
            Preprocessor: 0xFF4B2A8F, Excluded: 0xFF4A4A4A, Punctuation: 0xFF000000,
            new Semantic(Value: 0xFF00585E, Interface: 0xFF00585E, TypeParameter: 0xFF0A4F66, Member: 0xFF0A3E6B, Parameter: 0xFF1A2A44, Namespace: 0xFF2A2A2A, Constant: 0xFF7A1446)));

    /// <summary>A theme's role colours before fitting: the interface's grounds, its text and status colours, the editor's fills.</summary>
    private sealed record Roles(uint Ground, uint Surface, uint Raised, uint RaisedHigh, uint Selection, uint CaretLine, uint TextSelection,
        uint Border, uint BorderStrong, uint Text, uint Secondary, uint Accent, uint Success, uint Warning, uint Error, uint Info, uint GutterText,
        uint DiffAdded, uint DiffRemoved, uint BracketMatch, uint FindMatch);

    /// <summary>A theme's code classes before fitting.</summary>
    private sealed record Code(uint Keyword, uint Type, uint String, uint Comment, uint Number, uint Method, uint Local, uint Preprocessor,
        uint Excluded, uint Punctuation, Semantic Semantic);

    // A theme whose every text-like colour is moved (ColorContrast.Ensure) until it reads at `ratio` on every ground it is
    // drawn on: the interface's text, status colours and accent on the ground, the surfaces, the raised fills and the
    // selected row; the code classes on the editor, its caret line (and, at the enhanced ratio, its selection); the line
    // numbers on the editor and its gutter; the accent's ink on the accent.
    private static TableTheme Fitted(string name, string summary, bool isDark, double ratio, Roles r, Code c)
    {
        bool enhanced = ratio >= ColorContrast.Enhanced;
        uint[] interfaceGrounds = enhanced
            ? [r.Ground, r.Surface, r.Raised, r.RaisedHigh, r.Selection, r.CaretLine, r.TextSelection]
            : [r.Ground, r.Surface, r.Raised, r.RaisedHigh, r.Selection, r.CaretLine];
        uint[] codeGrounds = enhanced ? [r.Surface, r.CaretLine, r.TextSelection] : [r.Surface, r.CaretLine];
        uint Ui(uint color) => ColorContrast.Ensure(color, ratio, interfaceGrounds);
        uint Ink(uint color) => ColorContrast.Ensure(color, ratio, codeGrounds);
        uint accent = Ui(r.Accent);
        uint ink = ColorContrast.Ratio(0xFF000000u, accent) >= ColorContrast.Ratio(0xFFFFFFFFu, accent) ? 0xFF000000u : 0xFFFFFFFFu;
        uint text = Ui(r.Text);
        uint secondary = Ui(r.Secondary);
        uint gutterText = ColorContrast.Ensure(r.GutterText, ratio, [r.Surface, r.CaretLine]);
        var semantic = c.Semantic;
        return new TableTheme(name, summary, isDark,
            new EditorColors(Background: r.Surface, Foreground: text, Selection: r.TextSelection, CurrentLine: r.CaretLine, Caret: accent,
                Gutter: r.Surface, GutterText: gutterText, GutterCurrentText: text, BracketMatch: r.BracketMatch, FindMatch: r.FindMatch)
            {
                Error = Ui(r.Error), Warning = Ui(r.Warning), Info = Ui(r.Info),
                PopupBackground = r.Raised, PopupBorder = r.BorderStrong, PopupSelection = r.TextSelection, PopupText = text,
                Ground = r.Ground, Surface = r.Surface, Border = r.Border, SecondaryText = secondary, Accent = accent, Success = Ui(r.Success),
                DiffAdded = r.DiffAdded, DiffRemoved = r.DiffRemoved,
                Raised = r.Raised, RaisedHigh = r.RaisedHigh, InterfaceSelection = r.Selection, BorderStrong = r.BorderStrong, Text = text,
                AccentInk = ink, GhostText = Ink(c.Comment),
            },
            Styles(keyword: Ink(c.Keyword), type: Ink(c.Type), @string: Ink(c.String), comment: Ink(c.Comment), number: Ink(c.Number), method: Ink(c.Method),
                local: Ink(c.Local), preprocessor: Ink(c.Preprocessor), excluded: Ink(c.Excluded), punctuation: Ink(c.Punctuation),
                new Semantic(Value: Ink(semantic.Value), Interface: Ink(semantic.Interface), TypeParameter: Ink(semantic.TypeParameter), Member: Ink(semantic.Member),
                    Parameter: Ink(semantic.Parameter), Namespace: Ink(semantic.Namespace), Constant: Ink(semantic.Constant))));
    }

    /// <summary>
    /// The hues of the semantic classes beside the syntactic ones: value types (structs, enums), interfaces, type
    /// parameters, members (properties, fields, events), parameters, namespaces and constants (enum members too).
    /// </summary>
    private sealed record Semantic(uint Value, uint Interface, uint TypeParameter, uint Member, uint Parameter, uint Namespace, uint Constant);

    private static Dictionary<string, TextStyle> Styles(uint keyword, uint type, uint @string, uint comment, uint number, uint method,
        uint local, uint preprocessor, uint excluded, uint punctuation, Semantic semantic) => new(StringComparer.Ordinal)
    {
        [TokenClasses.Keyword] = new(keyword),
        [TokenClasses.ControlKeyword] = new(keyword),
        [TokenClasses.String] = new(@string),
        [TokenClasses.VerbatimString] = new(@string),
        [TokenClasses.StringEscape] = new(number),
        [TokenClasses.Number] = new(number),
        [TokenClasses.Comment] = new(comment, Italic: true),
        [TokenClasses.DocComment] = new(comment, Italic: true),
        [TokenClasses.DocCommentTag] = new(comment),
        [TokenClasses.Operator] = new(punctuation),
        [TokenClasses.Punctuation] = new(punctuation),
        [TokenClasses.PreprocessorKeyword] = new(preprocessor),
        [TokenClasses.PreprocessorText] = new(preprocessor),
        [TokenClasses.ExcludedCode] = new(excluded),
        [TokenClasses.ClassName] = new(type),
        [TokenClasses.RecordClassName] = new(type),
        [TokenClasses.StructName] = new(semantic.Value),
        [TokenClasses.RecordStructName] = new(semantic.Value),
        [TokenClasses.InterfaceName] = new(semantic.Interface),
        [TokenClasses.EnumName] = new(semantic.Value),
        [TokenClasses.DelegateName] = new(type),
        [TokenClasses.TypeParameterName] = new(semantic.TypeParameter),
        [TokenClasses.MethodName] = new(method),
        [TokenClasses.ExtensionMethodName] = new(method),
        [TokenClasses.PropertyName] = new(semantic.Member),
        [TokenClasses.FieldName] = new(semantic.Member),
        [TokenClasses.EventName] = new(semantic.Member),
        [TokenClasses.LocalName] = new(local),
        [TokenClasses.ParameterName] = new(semantic.Parameter),
        [TokenClasses.NamespaceName] = new(semantic.Namespace),
        [TokenClasses.ConstantName] = new(semantic.Constant),
        [TokenClasses.EnumMemberName] = new(semantic.Constant),
        [TokenClasses.LabelName] = new(local),
        // Static members and types in their own colour, italic (Roslyn's additive "static symbol").
        [TokenClasses.StaticSymbol] = new(type, Italic: true),
    };
}
