// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Web.Style;

/// <summary>
/// The names CSS knows (data, updated as the language moves; checked 2026-11-01 against CSS Baseline): properties,
/// at-rules (with SCSS's and LESS's), functions, pseudo-classes and pseudo-elements, and the named colours. Each newer
/// name carries the year it became Baseline (newly available in every major engine); <see cref="Future"/> marks one no
/// Baseline holds yet (only "latest" colours it). A name with no year (0) is long established. The lexer colours any
/// name by its syntax, known or not (CSS Syntax 3: new syntax always lexes); these lists only tell a declaration from a
/// nested rule where the line does not (a known property before ':') and let an older version read a newer name as
/// plain. Looked up by span, ignoring ASCII case, with nothing allocated (frozen alternate lookups, rule 15).
/// </summary>
public static class StyleNames
{
    /// <summary>The year of a name no Baseline holds yet: only the "latest" version colours it.</summary>
    public const int Future = 9999;

    /// <summary>The year of the "latest" version: every known name.</summary>
    public const int Latest = int.MaxValue;

    private static readonly FrozenDictionary<string, short> PropertyTable = Table(
    [
        "align-content", "align-items", "align-self", "all", ("anchor-name", 2026), ("anchor-scope", Future), "animation",
        ("animation-composition", 2023), "animation-delay", "animation-direction", "animation-duration", "animation-fill-mode",
        "animation-iteration-count", "animation-name", "animation-play-state", ("animation-range", Future), ("animation-range-end", Future),
        ("animation-range-start", Future), ("animation-timeline", Future), "animation-timing-function", ("appearance", 2022),
        ("aspect-ratio", 2021), ("backdrop-filter", 2024), "backface-visibility", "background", "background-attachment",
        "background-blend-mode", "background-clip", "background-color", "background-image", "background-origin", "background-position",
        "background-position-x", "background-position-y", "background-repeat", "background-size", "block-size", "border", "border-block",
        "border-block-color", "border-block-end", "border-block-end-color", "border-block-end-style", "border-block-end-width",
        "border-block-start", "border-block-start-color", "border-block-start-style", "border-block-start-width", "border-block-style",
        "border-block-width", "border-bottom", "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
        "border-bottom-style", "border-bottom-width", "border-collapse", "border-color", "border-end-end-radius", "border-end-start-radius",
        "border-image", "border-image-outset", "border-image-repeat", "border-image-slice", "border-image-source", "border-image-width",
        "border-inline", "border-inline-color", "border-inline-end", "border-inline-end-color", "border-inline-end-style",
        "border-inline-end-width", "border-inline-start", "border-inline-start-color", "border-inline-start-style",
        "border-inline-start-width", "border-inline-style", "border-inline-width", "border-left", "border-left-color", "border-left-style",
        "border-left-width", "border-radius", "border-right", "border-right-color", "border-right-style", "border-right-width",
        "border-spacing", "border-start-end-radius", "border-start-start-radius", "border-style", "border-top", "border-top-color",
        "border-top-left-radius", "border-top-right-radius", "border-top-style", "border-top-width", "border-width", "bottom",
        "box-decoration-break", "box-shadow", "box-sizing", "break-after", "break-before", "break-inside", "caption-side", "caret",
        "caret-color", ("caret-shape", Future), "clear", "clip", "clip-path", "clip-rule", "color", "color-interpolation",
        "color-interpolation-filters", ("color-scheme", 2022), "column-count", "column-fill", "column-gap", "column-rule",
        "column-rule-color", "column-rule-style", "column-rule-width", "column-span", "column-width", "columns", "contain",
        ("contain-intrinsic-block-size", 2023), ("contain-intrinsic-height", 2023), ("contain-intrinsic-inline-size", 2023),
        ("contain-intrinsic-size", 2023), ("contain-intrinsic-width", 2023), ("container", 2023), ("container-name", 2023),
        ("container-type", 2023), "content", ("content-visibility", 2024), ("corner-shape", Future), "counter-increment", "counter-reset",
        "counter-set", "cursor", "cx", "cy", "d", "direction", "display", "dominant-baseline", ("dynamic-range-limit", Future),
        "empty-cells", ("field-sizing", Future), "fill", "fill-opacity", "fill-rule", "filter", "flex", "flex-basis", "flex-direction",
        "flex-flow", "flex-grow", "flex-shrink", "flex-wrap", "float", "flood-color", "flood-opacity", "font", "font-display",
        "font-family", "font-feature-settings", "font-kerning", "font-language-override", "font-optical-sizing", ("font-palette", 2022),
        "font-size", ("font-size-adjust", 2024), "font-stretch", "font-style", "font-synthesis", "font-synthesis-position",
        "font-synthesis-small-caps", "font-synthesis-style", "font-synthesis-weight", "font-variant", "font-variant-alternates",
        "font-variant-caps", "font-variant-east-asian", "font-variant-emoji", "font-variant-ligatures", "font-variant-numeric",
        "font-variant-position", "font-variation-settings", "font-weight", "font-width", ("forced-color-adjust", 2022), "gap", "grid",
        "grid-area", "grid-auto-columns", "grid-auto-flow", "grid-auto-rows", "grid-column", "grid-column-end", "grid-column-gap",
        "grid-column-start", "grid-gap", "grid-row", "grid-row-end", "grid-row-gap", "grid-row-start", "grid-template",
        "grid-template-areas", "grid-template-columns", "grid-template-rows", "hanging-punctuation", "height", "hyphenate-character",
        "hyphenate-limit-chars", "hyphens", "image-orientation", "image-rendering", "image-resolution", "initial-letter", "inherits",
        "initial-value", "inline-size", "inset", "inset-block", "inset-block-end", "inset-block-start", "inset-inline", "inset-inline-end",
        "inset-inline-start", ("interpolate-size", Future), "isolation", "justify-content", "justify-items", "justify-self",
        "justify-tracks", "left", "letter-spacing", "lighting-color", "line-break", "line-clamp", "line-height", "line-height-step",
        "list-style", "list-style-image", "list-style-position", "list-style-type", "margin", "margin-block", "margin-block-end",
        "margin-block-start", "margin-bottom", "margin-inline", "margin-inline-end", "margin-inline-start", "margin-left", "margin-right",
        "margin-top", "margin-trim", "marker", "marker-end", "marker-mid", "marker-start", "mask", "mask-border", "mask-border-mode",
        "mask-border-outset", "mask-border-repeat", "mask-border-slice", "mask-border-source", "mask-border-width", "mask-clip",
        "mask-composite", "mask-image", "mask-mode", "mask-origin", "mask-position", "mask-repeat", "mask-size", "mask-type",
        "masonry-auto-flow", "math-depth", "math-shift", "math-style", "max-block-size", "max-height", "max-inline-size", "max-width",
        "min-block-size", "min-height", "min-inline-size", "min-width", "mix-blend-mode", "object-fit", "object-position", "offset",
        "offset-anchor", "offset-distance", "offset-path", "offset-position", "offset-rotate", "opacity", "order", "orphans", "outline",
        "outline-color", "outline-offset", "outline-style", "outline-width", "overflow", "overflow-anchor", "overflow-block",
        "overflow-clip-margin", "overflow-inline", "overflow-wrap", "overflow-x", "overflow-y", ("overlay", Future), "overscroll-behavior",
        "overscroll-behavior-block", "overscroll-behavior-inline", "overscroll-behavior-x", "overscroll-behavior-y", "padding",
        "padding-block", "padding-block-end", "padding-block-start", "padding-bottom", "padding-inline", "padding-inline-end",
        "padding-inline-start", "padding-left", "padding-right", "padding-top", "page", "page-break-after", "page-break-before",
        "page-break-inside", "paint-order", "perspective", "perspective-origin", "place-content", "place-items", "place-self",
        "pointer-events", "position", ("position-anchor", 2026), ("position-area", 2026), ("position-try", 2026),
        ("position-try-fallbacks", 2026), ("position-try-order", 2026), ("position-visibility", 2026), "print-color-adjust", "quotes", "r",
        ("reading-flow", Future), ("reading-order", Future), "resize", "right", "rotate", "row-gap", "ruby-align", "ruby-position", "rx",
        "ry", "scale", "scroll-behavior", "scroll-margin", "scroll-margin-block", "scroll-margin-block-end", "scroll-margin-block-start",
        "scroll-margin-bottom", "scroll-margin-inline", "scroll-margin-inline-end", "scroll-margin-inline-start", "scroll-margin-left",
        "scroll-margin-right", "scroll-margin-top", "scroll-padding", "scroll-padding-block", "scroll-padding-block-end",
        "scroll-padding-block-start", "scroll-padding-bottom", "scroll-padding-inline", "scroll-padding-inline-end",
        "scroll-padding-inline-start", "scroll-padding-left", "scroll-padding-right", "scroll-padding-top", "scroll-snap-align",
        "scroll-snap-stop", "scroll-snap-type", ("scroll-timeline", Future), ("scroll-timeline-axis", Future),
        ("scroll-timeline-name", Future), ("scrollbar-color", 2025), ("scrollbar-gutter", 2024), ("scrollbar-width", 2024),
        "shape-image-threshold", "shape-margin", "shape-outside", "shape-rendering", "size", "speak-as", "src", "stop-color", "stop-opacity",
        "stroke", "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-opacity",
        "stroke-width", "syntax", "tab-size", "table-layout", "text-align", "text-align-last", "text-anchor", ("text-autospace", Future),
        ("text-box", Future), ("text-box-edge", Future), ("text-box-trim", Future), "text-combine-upright", "text-decoration",
        "text-decoration-color", "text-decoration-line", "text-decoration-skip", "text-decoration-skip-ink", "text-decoration-style",
        "text-decoration-thickness", "text-emphasis", "text-emphasis-color", "text-emphasis-position", "text-emphasis-style",
        "text-indent", "text-justify", "text-orientation", "text-overflow", "text-rendering", "text-shadow", "text-size-adjust",
        ("text-spacing-trim", Future), "text-transform", "text-underline-offset", "text-underline-position", ("text-wrap", 2024),
        ("text-wrap-mode", 2024), ("text-wrap-style", 2024), ("timeline-scope", Future), "top", "touch-action", "transform",
        "transform-box", "transform-origin", "transform-style", "transition", ("transition-behavior", 2024), "transition-delay",
        "transition-duration", "transition-property", "transition-timing-function", "translate", "unicode-bidi", "unicode-range",
        "user-select", "vector-effect", "vertical-align", ("view-timeline", Future), ("view-timeline-axis", Future),
        ("view-timeline-inset", Future), ("view-timeline-name", Future), ("view-transition-class", 2025), ("view-transition-name", 2025),
        "visibility", "white-space", ("white-space-collapse", 2024), "widows", "width", "will-change", "word-break", "word-spacing",
        "word-wrap", "writing-mode", "x", "y", "z-index", ("zoom", 2024),
    ]);

    private static readonly FrozenDictionary<string, short> AtRuleTable = Table(
    [
        // CSS
        "charset", "import", "namespace", "media", "supports", "font-face", "keyframes", "page", ("counter-style", 2023),
        "font-feature-values", ("font-palette-values", 2022), ("layer", 2022), ("container", 2023), ("property", 2024), ("scope", 2025),
        ("starting-style", 2024), ("position-try", 2026), ("view-transition", 2025), ("function", Future), ("custom-media", Future),
        "swash", "annotation", "ornaments", "stylistic", "styleset", "character-variant", "top-left-corner", "top-left", "top-center",
        "top-right", "top-right-corner", "bottom-left-corner", "bottom-left", "bottom-center", "bottom-right", "bottom-right-corner",
        "left-top", "left-middle", "left-bottom", "right-top", "right-middle", "right-bottom", "document", "-webkit-keyframes",
        // SCSS
        "use", "forward", "mixin", "include", "content", "return", "if", "else", "each", "for", "while", "extend", "at-root", "debug",
        "warn", "error",
        // LESS
        "plugin",
    ]);

    private static readonly FrozenDictionary<string, short> FunctionTable = Table(
    [
        ("abs", 2025), ("acos", 2023), ("anchor", 2026), ("anchor-size", 2026), ("asin", 2023), ("atan", 2023), ("atan2", 2023), "attr",
        "blur", "brightness", ("calc", 0), ("calc-size", Future), "circle", "clamp", "color", ("color-mix", 2023), "conic-gradient",
        ("contrast-color", Future), "contrast", ("cos", 2023), "counter", "counters", "cross-fade", "cubic-bezier", "drop-shadow",
        "element", "ellipse", "env", ("exp", 2023), "fit-content", "format", "grayscale", "hsl", "hsla", "hue-rotate", "hwb",
        ("hypot", 2023), ("if", Future), "image", "image-set", "inset", "invert", ("lab", 2023), ("layer", 2022), ("lch", 2023),
        ("light-dark", 2024), ("linear", 2023), "linear-gradient", "local", ("log", 2023), "matrix", "matrix3d", "max", "media", "min",
        "minmax", ("mod", 2024), ("oklab", 2023), ("oklch", 2023), "opacity", "paint", "path", "perspective", "polygon", ("pow", 2023),
        ("progress", Future), "radial-gradient", "ray", "rect", ("rem", 2024), "repeat", "repeating-conic-gradient",
        "repeating-linear-gradient", "repeating-radial-gradient", "rgb", "rgba", "rotate", "rotate3d", "rotateX", "rotateY", "rotateZ",
        ("round", 2024), "saturate", "scale", "scale3d", "scaleX", "scaleY", "scaleZ", ("scroll", Future), "selector", "sepia",
        ("shape", 2025), ("sibling-count", Future), ("sibling-index", Future), ("sign", 2025), ("sin", 2023), "skew", "skewX", "skewY",
        ("sqrt", 2023), "steps", ("style", Future), "supports", "symbols", ("tan", 2023), "tech", "translate", "translate3d",
        "translateX", "translateY", "translateZ", "url", "var", ("view", Future), "xywh",
    ]);

    private static readonly FrozenDictionary<string, short> PseudoTable = Table(
    [
        // Pseudo-classes
        "active", ("active-view-transition", Future), "any-link", ("autofill", 2023), "blank", ("buffering", Future), "checked",
        ("closed", Future), "current", "default", ("defined", 0), ("dir", 2023), "disabled", "empty", "enabled", "first", "first-child",
        "first-of-type", "focus", ("focus-visible", 2022), "focus-within", ("fullscreen", 2024), "future", ("has", 2023), "host",
        "host-context", "hover", "in-range", "indeterminate", "invalid", ("is", 2021), "lang", "last-child", "last-of-type", "left", "link",
        "local-link", ("modal", 2022), ("muted", Future), "not", "nth-child", "nth-last-child", "nth-last-of-type", "nth-of-type",
        "only-child", "only-of-type", ("open", Future), "optional", "out-of-range", "past", ("paused", Future), "picture-in-picture",
        "placeholder-shown", ("playing", Future), ("popover-open", 2024), "read-only", "read-write", "required", "right", "root", "scope",
        ("seeking", Future), ("stalled", Future), ("state", 2024), "target", "target-current", ("user-invalid", 2023),
        ("user-valid", 2023), "valid", "visited", ("volume-locked", Future), ("where", 2021),
        // Pseudo-elements
        "after", "backdrop", "before", ("checkmark", Future), ("column", Future), "cue", ("details-content", Future),
        ("file-selector-button", 2021), "first-letter", "first-line", "grammar-error", ("highlight", 2025), "marker", "part",
        ("picker", Future), ("picker-icon", Future), "placeholder", ("scroll-button", Future), ("scroll-marker", Future),
        ("scroll-marker-group", Future), "selection", "slotted", "spelling-error", "target-text", ("view-transition", 2025),
        ("view-transition-group", 2025), ("view-transition-image-pair", 2025), ("view-transition-new", 2025),
        ("view-transition-old", 2025),
    ]);

    private static readonly FrozenSet<string> ColorSet = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
    [
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond", "blue", "blueviolet",
        "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "currentcolor",
        "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta",
        "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen", "darkslateblue", "darkslategray",
        "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick",
        "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow", "grey",
        "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush", "lawngreen", "lemonchiffon",
        "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink",
        "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow", "lime",
        "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen",
        "mediumslateblue", "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose",
        "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod",
        "palegreen", "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple",
        "rebeccapurple", "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
        "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan", "teal", "thistle",
        "tomato", "transparent", "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow", "yellowgreen",
    ]);

    private static readonly FrozenDictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> Properties = PropertyTable.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenDictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> AtRules = AtRuleTable.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenDictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> Functions = FunctionTable.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenDictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> Pseudos = PseudoTable.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Colors = ColorSet.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The properties known (vendor prefixes aside).</summary>
    public static IReadOnlyCollection<string> AllProperties => PropertyTable.Keys;

    /// <summary>The at-rules known, without their '@' (CSS's, SCSS's and LESS's).</summary>
    public static IReadOnlyCollection<string> AllAtRules => AtRuleTable.Keys;

    /// <summary>The functions known.</summary>
    public static IReadOnlyCollection<string> AllFunctions => FunctionTable.Keys;

    /// <summary>The pseudo-classes and pseudo-elements known, without their colons.</summary>
    public static IReadOnlyCollection<string> AllPseudos => PseudoTable.Keys;

    /// <summary>The year <paramref name="name"/> (a property, a vendor prefix dropped) became Baseline (0: long established); -1 when it is not known.</summary>
    public static int PropertyYear(ReadOnlySpan<char> name) => Year(Properties, WithoutVendor(name));

    /// <summary>The year the at-rule <paramref name="name"/> (without '@') became Baseline; -1 when it is not known.</summary>
    public static int AtRuleYear(ReadOnlySpan<char> name) => Year(AtRules, name);

    /// <summary>The year the function <paramref name="name"/> became Baseline; -1 when it is not known.</summary>
    public static int FunctionYear(ReadOnlySpan<char> name) => Year(Functions, WithoutVendor(name));

    /// <summary>The year the pseudo-class or pseudo-element <paramref name="name"/> (without colons) became Baseline; -1 when it is not known.</summary>
    public static int PseudoYear(ReadOnlySpan<char> name) => Year(Pseudos, WithoutVendor(name));

    /// <summary>Whether <paramref name="name"/> is a named colour (or <c>transparent</c>, <c>currentcolor</c>).</summary>
    public static bool IsColor(ReadOnlySpan<char> name) => Colors.Contains(name);

    /// <summary><paramref name="name"/> without a vendor prefix (<c>-webkit-</c>, <c>-moz-</c>, <c>-ms-</c>, <c>-o-</c>).</summary>
    public static ReadOnlySpan<char> WithoutVendor(ReadOnlySpan<char> name)
    {
        if (name.Length > 3 && name[0] == '-' && name[1] != '-')
        {
            int dash = name[1..].IndexOf('-');
            if (dash > 0 && dash + 2 < name.Length)
            {
                return name[(dash + 2)..];
            }
        }

        return name;
    }

    private static int Year(FrozenDictionary<string, short>.AlternateLookup<ReadOnlySpan<char>> table, ReadOnlySpan<char> name) =>
        table.TryGetValue(name, out short year) ? year : -1;

    private static FrozenDictionary<string, short> Table(ReadOnlySpan<Named> names)
    {
        var all = new Dictionary<string, short>(names.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var named in names)
        {
            all[named.Name] = named.Year;
        }

        return all.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // A name and its Baseline year: a plain string converts to a name with no year.
    private readonly record struct Named(string Name, short Year)
    {
        public static implicit operator Named(string name) => new(name, 0);

        public static implicit operator Named((string Name, int Year) named) => new(named.Name, (short)named.Year);
    }
}
