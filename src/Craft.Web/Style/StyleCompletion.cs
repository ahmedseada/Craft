// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.Web.Style;

/// <summary>
/// The built-in completion of CSS, SCSS and LESS (no Node, no language server): property names in a rule's body, values per
/// property (its keywords, the CSS-wide ones, its functions such as <c>color-mix()</c>, <c>oklch()</c>, <c>clamp()</c>, the named
/// colours where a colour goes, <c>var(--x)</c> from the file's own custom properties), at-rules after <c>@</c>, pseudo-classes
/// after <c>:</c> and pseudo-elements after <c>::</c> in a selector, and in SCSS the file's <c>$variables</c> and the
/// <c>@mixin</c> names after <c>@include</c>. A name newer than the session's Baseline year is not offered. The context is read
/// by one forward pass over the stylesheet (its blocks, statements, comments and strings: a style state carries no block
/// depth), on the thread pool; the HTML plug-in calls <see cref="Complete(ITextSnapshot, int, int, int, StyleDialect, int, bool, CancellationToken)"/>
/// for a <c>&lt;style&gt;</c> and a <c>style="…"</c>.
/// </summary>
public sealed class StyleCompletion : ILineCompletion
{
    private const int MaxBlocks = 128;

    // Values per property: its own keywords, "@color" for the named colours and the colour functions, "@image" for images,
    // "@transform", "@filter", "@timing", "@grid", "@length" for lengths' functions.
    private const string ValueData = """
        display: block inline inline-block flex inline-flex grid inline-grid flow-root contents none table table-row table-cell table-caption list-item ruby
        position: static relative absolute fixed sticky
        visibility: visible hidden collapse
        overflow: visible hidden clip scroll auto
        overflow-x: visible hidden clip scroll auto
        overflow-y: visible hidden clip scroll auto
        box-sizing: content-box border-box
        float: left right none inline-start inline-end
        clear: left right both none inline-start inline-end
        flex-direction: row row-reverse column column-reverse
        flex-wrap: nowrap wrap wrap-reverse
        flex-flow: row column wrap nowrap
        flex: auto none @length
        justify-content: flex-start flex-end center space-between space-around space-evenly start end left right stretch normal
        justify-items: start end center stretch normal legacy self-start self-end anchor-center
        justify-self: auto start end center stretch normal self-start self-end anchor-center
        align-items: flex-start flex-end center baseline stretch start end normal self-start self-end anchor-center
        align-content: flex-start flex-end center space-between space-around space-evenly stretch start end normal baseline
        align-self: auto flex-start flex-end center baseline stretch start end normal anchor-center
        place-items: center start end stretch normal baseline
        place-content: center start end space-between space-around space-evenly stretch normal
        text-align: left right center justify start end match-parent
        text-transform: none capitalize uppercase lowercase full-width
        text-decoration: none underline overline line-through @color
        text-decoration-line: none underline overline line-through
        text-decoration-style: solid double dotted dashed wavy
        text-overflow: clip ellipsis
        text-wrap: wrap nowrap balance pretty stable
        white-space: normal nowrap pre pre-wrap pre-line break-spaces
        word-break: normal break-all keep-all auto-phrase
        overflow-wrap: normal break-word anywhere
        font-weight: normal bold bolder lighter 100 200 300 400 500 600 700 800 900
        font-style: normal italic oblique
        font-family: serif sans-serif monospace cursive fantasy system-ui ui-serif ui-sans-serif ui-monospace ui-rounded math emoji fangsong
        font-display: auto block swap fallback optional
        font-size: xx-small x-small small medium large x-large xx-large xxx-large smaller larger @length
        line-height: normal @length
        vertical-align: baseline sub super text-top text-bottom middle top bottom
        cursor: auto default none pointer progress wait cell crosshair text vertical-text alias copy move no-drop not-allowed grab grabbing all-scroll col-resize row-resize n-resize e-resize s-resize w-resize zoom-in zoom-out help context-menu
        pointer-events: auto none visiblePainted visibleFill visibleStroke visible painted fill stroke all
        user-select: auto none text all contain
        resize: none both horizontal vertical block inline
        list-style-type: none disc circle square decimal decimal-leading-zero lower-roman upper-roman lower-alpha upper-alpha lower-greek
        list-style-position: inside outside
        border-style: none hidden dotted dashed solid double groove ridge inset outset
        border-collapse: collapse separate
        table-layout: auto fixed
        object-fit: fill contain cover none scale-down
        background-repeat: repeat repeat-x repeat-y no-repeat space round
        background-size: auto cover contain @length
        background-attachment: scroll fixed local
        background-clip: border-box padding-box content-box text
        background-image: none @image
        background: none @color @image
        mix-blend-mode: normal multiply screen overlay darken lighten color-dodge color-burn hard-light soft-light difference exclusion hue saturation color luminosity plus-lighter
        isolation: auto isolate
        content: none normal open-quote close-quote no-open-quote no-close-quote @content
        transform: none @transform
        transform-origin: center top bottom left right @length
        transition-timing-function: ease ease-in ease-out ease-in-out linear step-start step-end @timing
        animation-timing-function: ease ease-in ease-out ease-in-out linear step-start step-end @timing
        animation-direction: normal reverse alternate alternate-reverse
        animation-fill-mode: none forwards backwards both
        animation-play-state: running paused
        animation-iteration-count: infinite
        transition-behavior: normal allow-discrete
        filter: none @filter
        backdrop-filter: none @filter
        grid-template-columns: none subgrid masonry auto min-content max-content @grid
        grid-template-rows: none subgrid masonry auto min-content max-content @grid
        grid-auto-flow: row column dense
        width: auto min-content max-content fit-content stretch @length
        height: auto min-content max-content fit-content stretch @length
        min-width: auto min-content max-content fit-content @length
        max-width: none min-content max-content fit-content @length
        min-height: auto min-content max-content fit-content @length
        max-height: none min-content max-content fit-content @length
        inline-size: auto min-content max-content fit-content @length
        block-size: auto min-content max-content fit-content @length
        margin: auto @length
        padding: @length
        gap: normal @length
        inset: auto @length
        top: auto @length anchor()
        right: auto @length anchor()
        bottom: auto @length anchor()
        left: auto @length anchor()
        z-index: auto
        opacity: @length
        aspect-ratio: auto
        container-type: normal size inline-size scroll-state
        contain: none strict content size inline-size layout style paint
        content-visibility: visible auto hidden
        scroll-behavior: auto smooth
        scroll-snap-type: none x y block inline both mandatory proximity
        scroll-snap-align: none start end center
        overscroll-behavior: auto contain none
        touch-action: auto none pan-x pan-y manipulation pinch-zoom
        will-change: auto scroll-position contents transform opacity
        appearance: none auto base-select
        accent-color: auto @color
        color-scheme: normal light dark only
        forced-color-adjust: auto none preserve-parent-color
        print-color-adjust: economy exact
        field-sizing: fixed content
        interpolate-size: numeric-only allow-keywords
        position-area: top bottom left right center span-top span-bottom span-left span-right span-all
        position-try-fallbacks: none flip-block flip-inline flip-start
        anchor-name: none
        position-anchor: auto
        view-transition-name: none auto match-element
        writing-mode: horizontal-tb vertical-rl vertical-lr sideways-rl sideways-lr
        direction: ltr rtl
        hyphens: none manual auto
        caption-side: top bottom
        empty-cells: show hide
        image-rendering: auto smooth high-quality crisp-edges pixelated
        mask: none @image
        clip-path: none inset() circle() ellipse() polygon() path() shape()
        box-shadow: none inset @color
        text-shadow: none @color
        color: currentcolor transparent @color
        background-color: currentcolor transparent @color
        border-color: currentcolor transparent @color
        border: none solid dashed dotted double @color @length
        border-top-color: currentcolor transparent @color
        border-right-color: currentcolor transparent @color
        border-bottom-color: currentcolor transparent @color
        border-left-color: currentcolor transparent @color
        outline: none auto solid dashed dotted @color
        outline-color: currentcolor transparent @color
        caret-color: auto currentcolor transparent @color
        text-decoration-color: currentcolor transparent @color
        column-rule-color: currentcolor transparent @color
        fill: none currentcolor context-fill context-stroke @color
        stroke: none currentcolor context-fill context-stroke @color
        """;

    private static readonly string[] Wide = ["inherit", "initial", "unset", "revert", "revert-layer"];
    private static readonly string[] ColorFunctions = ["rgb", "hsl", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix", "light-dark", "contrast-color"];
    private static readonly string[] ImageFunctions = ["url", "linear-gradient", "radial-gradient", "conic-gradient", "repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient", "image-set", "cross-fade"];
    private static readonly string[] TransformFunctions = ["translate", "translateX", "translateY", "translate3d", "rotate", "rotateX", "rotateY", "rotate3d", "scale", "scaleX", "scaleY", "scale3d", "skew", "skewX", "skewY", "matrix", "matrix3d", "perspective"];
    private static readonly string[] FilterFunctions = ["blur", "brightness", "contrast", "drop-shadow", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia", "url"];
    private static readonly string[] TimingFunctions = ["cubic-bezier", "steps", "linear"];
    private static readonly string[] GridFunctions = ["repeat", "minmax", "fit-content"];
    private static readonly string[] ContentFunctions = ["attr", "counter", "counters", "url"];
    private static readonly string[] MathFunctions = ["var", "calc", "min", "max", "clamp", "env", "if", "calc-size", "round", "abs", "sign", "mod", "rem", "anchor", "anchor-size"];

    private static readonly FrozenSet<string> ScssAtRules = FrozenSet.Create(StringComparer.Ordinal,
        "use", "forward", "mixin", "include", "content", "return", "if", "else", "each", "for", "while", "extend", "at-root", "debug", "warn", "error");

    private static readonly FrozenSet<string> LessAtRules = FrozenSet.Create(StringComparer.Ordinal, "plugin");

    // At-rules whose block holds rules only (a statement in it is a selector).
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> GroupAtRules = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "media", "supports", "layer", "container", "scope", "document", "keyframes", "-webkit-keyframes", "custom-media").GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenSet<string> PseudoElementSet = FrozenSet.Create(StringComparer.Ordinal,
        "after", "backdrop", "before", "checkmark", "column", "cue", "details-content", "file-selector-button", "first-letter", "first-line",
        "grammar-error", "highlight", "marker", "part", "picker", "picker-icon", "placeholder", "scroll-button", "scroll-marker",
        "scroll-marker-group", "selection", "slotted", "spelling-error", "target-text", "view-transition", "view-transition-group",
        "view-transition-image-pair", "view-transition-new", "view-transition-old");

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Functional = FrozenSet.Create(StringComparer.Ordinal,
        "not", "is", "where", "has", "nth-child", "nth-last-child", "nth-of-type", "nth-last-of-type", "lang", "dir", "host", "host-context",
        "state", "part", "slotted", "highlight", "cue", "view-transition-group", "view-transition-image-pair", "view-transition-new",
        "view-transition-old", "picker", "scroll-button", "active-view-transition").GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenDictionary<string, string[]> Values = ParseValues();
    private static readonly FrozenDictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> ValueLookup = Values.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The one instance (it holds nothing per document).</summary>
    public static StyleCompletion Instance { get; } = new();

    /// <summary>The properties offered (all known), in order.</summary>
    public static IReadOnlyList<string> Properties { get; } = [.. StyleNames.AllProperties.Order(StringComparer.Ordinal)];

    /// <summary>The pseudo-classes, in order.</summary>
    public static IReadOnlyList<string> PseudoClasses { get; } = [.. StyleNames.AllPseudos.Where(p => !PseudoElementSet.Contains(p)).Order(StringComparer.Ordinal)];

    /// <summary>The pseudo-elements, in order.</summary>
    public static IReadOnlyList<string> PseudoElements { get; } = [.. StyleNames.AllPseudos.Where(PseudoElementSet.Contains).Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public bool IsTriggerCharacter(char c) => c is ':' or '@' or '$' or '(';

    /// <inheritdoc />
    public bool IsWordCharacter(char c) => c == '-';

    /// <summary>The keywords and functions offered for <paramref name="property"/> (its own, then the colours', images', … by its kind).</summary>
    public static IReadOnlyList<string> ValuesOf(ReadOnlySpan<char> property) => ValueLookup.TryGetValue(property, out var values) ? values : [];

    /// <inheritdoc />
    public CompletionList? Complete(LineCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokenizer = request.Tokenizer as StyleTokenizer;
        var dialect = tokenizer?.Dialect ?? StyleDialect.Css;
        int year = tokenizer?.Year ?? StyleNames.Latest;
        return Complete(request.Snapshot, 0, request.Snapshot.Length, request.Position, dialect, year, declarations: false, cancellationToken);
    }

    /// <summary>
    /// The list at <paramref name="position"/> in the stylesheet from <paramref name="start"/> to <paramref name="end"/> of
    /// <paramref name="snapshot"/> (a file, a <c>&lt;style&gt;</c>'s text), or in a declaration list when
    /// <paramref name="declarations"/> (a <c>style="…"</c>); null when nothing completes there.
    /// </summary>
    public static CompletionList? Complete(ITextSnapshot snapshot, int start, int end, int position, StyleDialect dialect, int year, bool declarations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        start = Math.Clamp(start, 0, snapshot.Length);
        end = Math.Clamp(end, start, snapshot.Length);
        if (position < start || position > end)
        {
            return null;
        }

        int length = end - start;
        char[] text = ArrayPool<char>.Shared.Rent(Math.Max(1, length));
        try
        {
            snapshot.CopyTo(start, text.AsSpan(0, length));
            cancellationToken.ThrowIfCancellationRequested();
            var scan = new Scan(text.AsSpan(0, length), position - start, dialect, declarations);
            scan.Run();
            if (scan.InCommentOrString)
            {
                return null;
            }

            var items = new List<CompletionItem>();
            int wordStart = Items(snapshot, start, text.AsSpan(0, length), position - start, scan, dialect, year, items);
            if (items.Count == 0)
            {
                return null;
            }

            items.Sort(static (a, b) => string.CompareOrdinal(a.SortText, b.SortText));
            return new CompletionList(snapshot, position, start + wordStart, items);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(text);
        }
    }

    // What the caret's context offers; returns the word's start (in the text).
    private static int Items(ITextSnapshot snapshot, int offset, ReadOnlySpan<char> text, int caret, in Scan scan, StyleDialect dialect, int year, List<CompletionItem> items)
    {
        int wordStart = caret;
        while (wordStart > 0 && (char.IsAsciiLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] is '-' or '_'))
        {
            wordStart--;
        }

        char before = wordStart > 0 ? text[wordStart - 1] : '\0';
        int statement = scan.CaretStatement;
        while (statement < caret && char.IsWhiteSpace(text[statement]))
        {
            statement++;
        }

        var stmt = text[statement..caret];
        int at = offset + wordStart;
        int after = WordEnd(text, caret);

        // @ at a statement's start: the at-rules (SCSS's and LESS's in theirs); SCSS's @include: the file's mixins.
        if (before == '@' && wordStart - 1 == statement)
        {
            foreach (var rule in StyleNames.AllAtRules)
            {
                if (StyleNames.AtRuleYear(rule) > year || (dialect != StyleDialect.Scss && dialect != StyleDialect.Sass && ScssAtRules.Contains(rule) && rule != "function")
                    || (dialect != StyleDialect.Less && LessAtRules.Contains(rule)) || rule.StartsWith('-'))
                {
                    continue;
                }

                items.Add(Item("@" + rule, CompletionItemKind.Keyword, "at-rule", at, offset + after, rule + " ", rule));
            }

            return wordStart;
        }

        if (dialect is StyleDialect.Scss or StyleDialect.Sass && stmt.StartsWith("@include") && wordStart >= statement + 8)
        {
            foreach (var mixin in scan.Mixins(text))
            {
                items.Add(Item(mixin, CompletionItemKind.Method, "mixin", at, offset + after, mixin, mixin));
            }

            return wordStart;
        }

        if (before == '$' && dialect is StyleDialect.Scss or StyleDialect.Sass)
        {
            foreach (var variable in scan.Variables(text))
            {
                items.Add(Item("$" + variable, CompletionItemKind.Local, "variable", at, offset + after, variable, variable));
            }

            return wordStart;
        }

        // A declaration's value: `property: …` in a body (a known property or a custom one before its ':').
        int colon = stmt.IndexOf(':');
        bool body = scan.CaretInBody;
        if (body && colon > 0 && statement + colon < wordStart)
        {
            var name = stmt[..colon].Trim();
            bool selectorish = name.Length > 0 && name[0] is '&' or '.' or '#' or ':' or '[' or '*' or '>' or '+' or '~';
            if (!selectorish && (name.StartsWith("--") || StyleNames.PropertyYear(name) >= 0 || ValueLookup.ContainsKey(name)))
            {
                Value(text, caret, wordStart, name, scan, dialect, year, at, offset + after, items);
                return wordStart;
            }
        }

        // A selector: pseudo-classes after ':', pseudo-elements after '::'.
        if (before == ':' && (!body || colon < 0 || stmt[0] is '&' or '.' or '#' or ':' or '[' or '*' or '>' or '+' or '~' || StyleNames.PropertyYear(stmt[..colon].Trim()) < 0))
        {
            bool element = wordStart >= 2 && text[wordStart - 2] == ':';
            foreach (var pseudo in element ? PseudoElements : PseudoClasses)
            {
                if (StyleNames.PseudoYear(pseudo) > year)
                {
                    continue;
                }

                bool function = Functional.Contains(pseudo);
                string insert = function ? pseudo + "()" : pseudo;
                items.Add(Item((element ? "::" : ":") + pseudo, CompletionItemKind.Keyword, element ? "pseudo-element" : "pseudo-class", at, offset + after, insert, pseudo,
                    function ? at + pseudo.Length + 1 : -1));
            }

            return wordStart;
        }

        // A property's name: in a body, a statement that is one word so far.
        if (body && colon < 0 && (stmt.IsEmpty || char.IsAsciiLetter(stmt[0]) || stmt[0] == '-') && wordStart == statement)
        {
            bool hasColon = after < text.Length && SkipBlank(text, after) < text.Length && text[SkipBlank(text, after)] == ':';
            foreach (var property in Properties)
            {
                if (StyleNames.PropertyYear(property) > year)
                {
                    continue;
                }

                items.Add(Item(property, CompletionItemKind.Property, null, at, offset + after, hasColon ? property : property + ": ", property));
            }

            foreach (var custom in scan.CustomProperties(text))
            {
                items.Add(Item(custom, CompletionItemKind.Local, "custom property", at, offset + after, hasColon ? custom : custom + ": ", custom));
            }
        }

        return wordStart;
    }

    // A property's values: var(--x) names inside var(, else its keywords, functions and colours, the CSS-wide keywords.
    private static void Value(ReadOnlySpan<char> text, int caret, int wordStart, ReadOnlySpan<char> property, in Scan scan, StyleDialect dialect, int year, int at, int to, List<CompletionItem> items)
    {
        int open = wordStart - 1;
        while (open >= 0 && text[open] is ' ' or '\t')
        {
            open--;
        }

        bool inVar = open >= 3 && text[open] == '(' && text[(open - 3)..open].SequenceEqual("var");
        if (inVar || (wordStart + 1 < text.Length && text[wordStart..].StartsWith("--")))
        {
            foreach (var custom in scan.CustomProperties(text))
            {
                items.Add(Item(custom, CompletionItemKind.Local, "custom property", at, to, custom, custom));
            }

            if (inVar)
            {
                return;
            }
        }

        bool color = false;
        foreach (var value in ValuesOf(property))
        {
            switch (value)
            {
                case "@color":
                    color = true;
                    Functions(ColorFunctions, year, at, to, items);
                    break;
                case "@image":
                    Functions(ImageFunctions, year, at, to, items);
                    break;
                case "@transform":
                    Functions(TransformFunctions, year, at, to, items);
                    break;
                case "@filter":
                    Functions(FilterFunctions, year, at, to, items);
                    break;
                case "@timing":
                    Functions(TimingFunctions, year, at, to, items);
                    break;
                case "@grid":
                    Functions(GridFunctions, year, at, to, items);
                    break;
                case "@content":
                    Functions(ContentFunctions, year, at, to, items);
                    break;
                case "@length":
                    break;
                default:
                    if (value.EndsWith("()", StringComparison.Ordinal))
                    {
                        string function = value[..^2];
                        items.Add(Item(value, CompletionItemKind.Method, "function", at, to, value, function, at + function.Length + 1));
                    }
                    else
                    {
                        items.Add(Item(value, CompletionItemKind.EnumMember, null, at, to, value, value));
                    }

                    break;
            }
        }

        if (color || property.EndsWith("color", StringComparison.OrdinalIgnoreCase))
        {
            if (!color)
            {
                Functions(ColorFunctions, year, at, to, items);
            }

            foreach (var name in StyleNames.AllColors)
            {
                items.Add(Item(name, CompletionItemKind.Constant, "colour", at, to, name, name, sort: "~" + name));
            }
        }

        Functions(MathFunctions, year, at, to, items);
        foreach (var wide in Wide)
        {
            items.Add(Item(wide, CompletionItemKind.Keyword, "CSS-wide", at, to, wide, wide, sort: "~~" + wide));
        }

        if (dialect is StyleDialect.Scss or StyleDialect.Sass)
        {
            foreach (var variable in scan.Variables(text))
            {
                items.Add(Item("$" + variable, CompletionItemKind.Local, "variable", at, to, "$" + variable, variable));
            }
        }
    }

    private static void Functions(string[] names, int year, int at, int to, List<CompletionItem> items)
    {
        foreach (var name in names)
        {
            if (StyleNames.FunctionYear(name) > year)
            {
                continue;
            }

            items.Add(Item(name + "()", CompletionItemKind.Method, "function", at, to, name + "()", name, at + name.Length + 1));
        }
    }

    private static CompletionItem Item(string label, CompletionItemKind kind, string? detail, int start, int end, string text, string filter, int caretAfter = -1, string? sort = null) =>
        new(label, kind, detail, new TextEdit(start, end - start, text), filter, sort ?? filter, caretAfter);

    private static int WordEnd(ReadOnlySpan<char> text, int caret)
    {
        int end = caret;
        while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '-' or '_'))
        {
            end++;
        }

        return end;
    }

    private static int SkipBlank(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        return i;
    }

    private static FrozenDictionary<string, string[]> ParseValues()
    {
        var table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ValueData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            table[line[..colon]] = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // One pass over the stylesheet: the caret's statement and block, and where the file defines custom properties, SCSS
    // variables and mixins (positions only; names are made when the items are).
    private ref struct Scan
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly int _caret;
        private readonly bool _lineComments;
        private readonly bool _declarations;
        private readonly List<(int Start, int Length, byte Kind)> _definitions = [];

        public Scan(ReadOnlySpan<char> text, int caret, StyleDialect dialect, bool declarations)
        {
            _text = text;
            _caret = caret;
            _lineComments = dialect != StyleDialect.Css;
            _declarations = declarations;
        }

        public bool InCommentOrString { get; private set; }

        public int CaretStatement { get; private set; }

        public bool CaretInBody { get; private set; }

        public void Run()
        {
            var text = _text;
            Span<bool> bodies = stackalloc bool[MaxBlocks];
            int depth = 0;
            int statement = 0;
            int parens = 0;
            bool fresh = true;
            bool captured = false;
            int i = 0;
            while (i <= text.Length)
            {
                if (i >= _caret && !captured)
                {
                    captured = true;
                    CaretStatement = statement;
                    CaretInBody = depth == 0 ? _declarations : bodies[Math.Min(depth, MaxBlocks) - 1];
                }

                if (i == text.Length)
                {
                    break;
                }

                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '*')
                {
                    int close = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                    int end = close < 0 ? text.Length : i + 2 + close + 2;
                    if (!captured && _caret > i && (_caret < end || close < 0))
                    {
                        InCommentOrString = true;
                        captured = true;
                    }

                    i = end;
                    continue;
                }

                if (c == '/' && next == '/' && _lineComments && (i == 0 || text[i - 1] != ':'))
                {
                    int newline = text[i..].IndexOfAny('\n', '\r');
                    int end = newline < 0 ? text.Length : i + newline;
                    if (!captured && _caret > i && _caret <= end)
                    {
                        InCommentOrString = true;
                        captured = true;
                    }

                    i = end;
                    continue;
                }

                if (c is '"' or '\'')
                {
                    int j = i + 1;
                    while (j < text.Length && text[j] != c && text[j] is not ('\n' or '\r'))
                    {
                        j += text[j] == '\\' ? 2 : 1;
                    }

                    int end = Math.Min(text.Length, j + 1);
                    if (!captured && _caret > i && _caret < end)
                    {
                        InCommentOrString = true;
                        captured = true;
                    }

                    i = end;
                    fresh = false;
                    continue;
                }

                if (fresh && !char.IsWhiteSpace(c))
                {
                    fresh = false;
                    Define(i);
                }

                switch (c)
                {
                    case '{' when !(i > 0 && text[i - 1] is '#' or '@'):
                        if (depth < MaxBlocks)
                        {
                            bodies[depth] = !IsGroup(text[statement..i]);
                        }

                        depth++;
                        statement = i + 1;
                        parens = 0;
                        fresh = true;
                        break;
                    case '}':
                        depth = Math.Max(0, depth - 1);
                        statement = i + 1;
                        parens = 0;
                        fresh = true;
                        break;
                    case '(':
                        parens++;
                        break;
                    case ')':
                        parens = Math.Max(0, parens - 1);
                        break;
                    case ';' when parens == 0:
                        statement = i + 1;
                        fresh = true;
                        break;
                }

                i++;
            }
        }

        // A statement's start: --name:, $name: (SCSS), @mixin name.
        private readonly void Define(int i)
        {
            var text = _text;
            if (text[i..].StartsWith("--"))
            {
                int end = NameEnd(text, i + 2);
                if (end > i + 2 && SkipBlank(text, end) < text.Length && text[SkipBlank(text, end)] == ':')
                {
                    _definitions.Add((i, end - i, 0));
                }
            }
            else if (text[i] == '$')
            {
                int end = NameEnd(text, i + 1);
                if (end > i + 1 && SkipBlank(text, end) < text.Length && text[SkipBlank(text, end)] == ':')
                {
                    _definitions.Add((i + 1, end - i - 1, 1));
                }
            }
            else if (text[i..].StartsWith("@mixin "))
            {
                int start = SkipBlank(text, i + 7);
                int end = NameEnd(text, start);
                if (end > start)
                {
                    _definitions.Add((start, end - start, 2));
                }
            }
        }

        private static int NameEnd(ReadOnlySpan<char> text, int i)
        {
            while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '-' or '_'))
            {
                i++;
            }

            return i;
        }

        // A block's header that holds rules only (@media, @supports, @layer, @container, @keyframes, …).
        private static bool IsGroup(ReadOnlySpan<char> header)
        {
            header = header.Trim();
            if (header.IsEmpty || header[0] != '@')
            {
                return false;
            }

            int end = 1;
            while (end < header.Length && (char.IsAsciiLetterOrDigit(header[end]) || header[end] == '-'))
            {
                end++;
            }

            return GroupAtRules.Contains(header[1..end]);
        }

        public readonly List<string> CustomProperties(ReadOnlySpan<char> text) => Names(text, 0);

        public readonly List<string> Variables(ReadOnlySpan<char> text) => Names(text, 1);

        public readonly List<string> Mixins(ReadOnlySpan<char> text) => Names(text, 2);

        private readonly List<string> Names(ReadOnlySpan<char> text, byte kind)
        {
            var names = new List<string>();
            foreach (var (start, length, of) in _definitions)
            {
                if (of != kind)
                {
                    continue;
                }

                var name = text.Slice(start, length);
                bool seen = false;
                foreach (var known in names)
                {
                    if (name.SequenceEqual(known))
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    names.Add(name.ToString());
                }
            }

            return names;
        }
    }
}
