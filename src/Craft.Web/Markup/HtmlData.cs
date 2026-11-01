// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Web.Markup;

/// <summary>
/// The names HTML completion offers (W-markup's completion): the living standard's elements, the global attributes, each
/// element's own, the ARIA attributes, the event handlers, the fixed value lists, the common named character references,
/// and for Angular the element properties and events a binding names. Data as of 2026-11-01 (the WHATWG HTML standard, WAI-
/// ARIA 1.3; confirm on day one), built once into frozen tables (rules 15, 75); nothing is made per request but the items.
/// </summary>
public static class HtmlData
{
    // Each element with its own attributes ("name: attribute attribute …").
    private const string ElementData = """
        a: href target download ping rel hreflang type referrerpolicy
        abbr:
        address:
        area: alt coords shape href target download ping rel referrerpolicy
        article:
        aside:
        audio: src crossorigin preload autoplay loop muted controls
        b:
        base: href target
        bdi:
        bdo:
        blockquote: cite
        body:
        br:
        button: command commandfor disabled form formaction formenctype formmethod formnovalidate formtarget name popovertarget popovertargetaction type value
        canvas: width height
        caption:
        cite:
        code:
        col: span
        colgroup: span
        data: value
        datalist:
        dd:
        del: cite datetime
        details: name open
        dfn:
        dialog: closedby open
        div:
        dl:
        dt:
        em:
        embed: src type width height
        fieldset: disabled form name
        figcaption:
        figure:
        footer:
        form: accept-charset action autocomplete enctype method name novalidate rel target
        h1:
        h2:
        h3:
        h4:
        h5:
        h6:
        head:
        header:
        hgroup:
        hr:
        html: manifest
        i:
        iframe: src srcdoc name sandbox allow allowfullscreen width height referrerpolicy loading
        img: alt src srcset sizes crossorigin usemap ismap width height referrerpolicy decoding loading fetchpriority
        input: accept alpha alt autocomplete checked colorspace dirname disabled form formaction formenctype formmethod formnovalidate formtarget height list max maxlength min minlength multiple name pattern placeholder popovertarget popovertargetaction readonly required size src step type value width
        ins: cite datetime
        kbd:
        label: for
        legend:
        li: value
        link: href crossorigin rel as media hreflang type sizes imagesrcset imagesizes referrerpolicy integrity blocking color disabled fetchpriority
        main:
        map: name
        mark:
        menu:
        meta: name http-equiv content charset media
        meter: value min max low high optimum
        nav:
        noscript:
        object: data type name form width height
        ol: reversed start type
        optgroup: disabled label
        option: disabled label selected value
        output: for form name
        p:
        picture:
        pre:
        progress: value max
        q: cite
        rp:
        rt:
        ruby:
        s:
        samp:
        script: src type nomodule async defer crossorigin integrity referrerpolicy blocking fetchpriority
        search:
        section:
        select: autocomplete disabled form multiple name required size
        selectedcontent:
        slot: name
        small:
        source: type media src srcset sizes width height
        span:
        strong:
        style: media blocking
        sub:
        summary:
        sup:
        table:
        tbody:
        td: colspan rowspan headers
        template: shadowrootmode shadowrootdelegatesfocus shadowrootclonable shadowrootserializable shadowrootcustomelementregistry
        textarea: autocomplete cols dirname disabled form maxlength minlength name placeholder readonly required rows wrap
        tfoot:
        th: colspan rowspan headers scope abbr
        thead:
        time: datetime
        title:
        tr:
        track: default kind label src srclang
        u:
        ul:
        var:
        video: src crossorigin poster preload autoplay playsinline loop muted controls width height
        wbr:
        svg: viewBox width height xmlns fill stroke
        math: display
        """;

    private static readonly string[] GlobalAttributes =
    [
        "accesskey", "autocapitalize", "autocorrect", "autofocus", "class", "contenteditable", "dir", "draggable", "enterkeyhint", "hidden",
        "id", "inert", "inputmode", "is", "itemid", "itemprop", "itemref", "itemscope", "itemtype", "lang", "nonce", "popover", "role", "slot",
        "spellcheck", "style", "tabindex", "title", "translate", "writingsuggestions", "data-",
    ];

    private static readonly string[] AriaAttributes =
    [
        "aria-activedescendant", "aria-atomic", "aria-autocomplete", "aria-braillelabel", "aria-brailleroledescription", "aria-busy",
        "aria-checked", "aria-colcount", "aria-colindex", "aria-colindextext", "aria-colspan", "aria-controls", "aria-current",
        "aria-describedby", "aria-description", "aria-details", "aria-disabled", "aria-errormessage", "aria-expanded", "aria-flowto",
        "aria-haspopup", "aria-hidden", "aria-invalid", "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-level", "aria-live",
        "aria-modal", "aria-multiline", "aria-multiselectable", "aria-orientation", "aria-owns", "aria-placeholder", "aria-posinset",
        "aria-pressed", "aria-readonly", "aria-relevant", "aria-required", "aria-roledescription", "aria-rowcount", "aria-rowindex",
        "aria-rowindextext", "aria-rowspan", "aria-selected", "aria-setsize", "aria-sort", "aria-valuemax", "aria-valuemin", "aria-valuenow",
        "aria-valuetext",
    ];

    private static readonly string[] Events =
    [
        "abort", "afterprint", "animationend", "animationiteration", "animationstart", "auxclick", "beforeinput", "beforematch", "beforeprint",
        "beforetoggle", "beforeunload", "blur", "cancel", "canplay", "canplaythrough", "change", "click", "close", "command", "contextlost",
        "contextmenu", "contextrestored", "copy", "cuechange", "cut", "dblclick", "drag", "dragend", "dragenter", "dragleave", "dragover",
        "dragstart", "drop", "durationchange", "emptied", "ended", "error", "focus", "focusin", "focusout", "formdata", "hashchange", "input",
        "invalid", "keydown", "keypress", "keyup", "load", "loadeddata", "loadedmetadata", "loadstart", "message", "mousedown", "mouseenter",
        "mouseleave", "mousemove", "mouseout", "mouseover", "mouseup", "offline", "online", "pagehide", "pagereveal", "pageshow", "pageswap",
        "paste", "pause", "play", "playing", "pointercancel", "pointerdown", "pointerenter", "pointerleave", "pointermove", "pointerout",
        "pointerover", "pointerup", "popstate", "progress", "ratechange", "reset", "resize", "scroll", "scrollend", "securitypolicyviolation",
        "seeked", "seeking", "select", "slotchange", "stalled", "storage", "submit", "suspend", "timeupdate", "toggle", "touchcancel",
        "touchend", "touchmove", "touchstart", "transitionend", "unhandledrejection", "unload", "volumechange", "waiting", "wheel",
    ];

    private static readonly FrozenSet<string> BooleanSet = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "allowfullscreen", "alpha", "async", "autofocus", "autoplay", "checked", "controls", "default", "defer", "disabled", "formnovalidate",
        "inert", "ismap", "itemscope", "loop", "multiple", "muted", "nomodule", "novalidate", "open", "playsinline", "readonly", "required",
        "reversed", "selected", "shadowrootclonable", "shadowrootdelegatesfocus", "shadowrootserializable");

    // Fixed value lists: "attribute: values" for any element, "element.attribute: values" for one element's.
    private const string ValueData = """
        input.type: text password email number tel url search date time datetime-local month week color range checkbox radio file hidden submit reset button image
        button.type: submit reset button
        ol.type: 1 a A i I
        script.type: module importmap speculationrules text/javascript application/json application/ld+json
        rel: alternate author bookmark canonical compression-dictionary dns-prefetch expect external help icon license manifest me modulepreload next nofollow noopener noreferrer opener pingback preconnect prefetch preload prev privacy-policy search stylesheet tag terms-of-service
        target: _blank _self _parent _top
        dir: ltr rtl auto
        loading: lazy eager
        decoding: sync async auto
        fetchpriority: high low auto
        crossorigin: anonymous use-credentials
        referrerpolicy: no-referrer no-referrer-when-downgrade origin origin-when-cross-origin same-origin strict-origin strict-origin-when-cross-origin unsafe-url
        method: get post dialog
        formmethod: get post dialog
        enctype: application/x-www-form-urlencoded multipart/form-data text/plain
        formenctype: application/x-www-form-urlencoded multipart/form-data text/plain
        autocomplete: on off name email username new-password current-password one-time-code organization street-address country postal-code cc-name cc-number cc-exp cc-csc tel url bday sex
        inputmode: none text decimal numeric tel search email url
        enterkeyhint: enter done go next previous search send
        contenteditable: true false plaintext-only
        draggable: true false
        spellcheck: true false
        translate: yes no
        autocapitalize: off none on sentences words characters
        autocorrect: on off
        writingsuggestions: true false
        hidden: hidden until-found
        popover: auto manual hint
        popovertargetaction: show hide toggle
        closedby: any closerequest none
        as: audio document embed fetch font image object script style track video worker
        preload: none metadata auto
        wrap: soft hard
        kind: subtitles captions descriptions chapters metadata
        shape: default rect circle poly
        scope: row col rowgroup colgroup
        sandbox: allow-downloads allow-forms allow-modals allow-orientation-lock allow-pointer-lock allow-popups allow-popups-to-escape-sandbox allow-presentation allow-same-origin allow-scripts allow-top-navigation allow-top-navigation-by-user-activation allow-top-navigation-to-custom-protocols
        shadowrootmode: open closed
        http-equiv: content-security-policy content-type default-style refresh x-ua-compatible
        meta.name: viewport description author keywords theme-color color-scheme robots generator referrer application-name
        blocking: render
        colorspace: limited-srgb display-p3
        role: alert alertdialog application article banner button cell checkbox columnheader combobox complementary contentinfo definition dialog document feed figure form grid gridcell group heading img link list listbox listitem log main marquee math menu menubar menuitem menuitemcheckbox menuitemradio meter navigation none note option presentation progressbar radio radiogroup region row rowgroup rowheader scrollbar search searchbox separator slider spinbutton status switch tab table tablist tabpanel term textbox timer toolbar tooltip tree treegrid treeitem
        aria-live: off polite assertive
        aria-current: page step location date time true false
        aria-haspopup: false true menu listbox tree grid dialog
        aria-orientation: horizontal vertical
        aria-sort: ascending descending none other
        aria-autocomplete: inline list both none
        aria-invalid: grammar false spelling true
        aria-expanded: true false
        aria-hidden: true false
        aria-pressed: true false mixed
        aria-checked: true false mixed
        aria-selected: true false
        aria-disabled: true false
        """;

    // The common named character references (the standard has 2,231; these are the ones typed by hand).
    private static readonly string[] EntityNames =
    [
        "amp", "lt", "gt", "quot", "apos", "nbsp", "copy", "reg", "trade", "hellip", "mdash", "ndash", "laquo", "raquo", "ldquo", "rdquo",
        "lsquo", "rsquo", "sbquo", "bdquo", "bull", "middot", "times", "divide", "plusmn", "minus", "deg", "micro", "para", "sect", "cent",
        "pound", "euro", "yen", "curren", "larr", "rarr", "uarr", "darr", "harr", "lArr", "rArr", "uArr", "dArr", "hArr", "hearts", "spades",
        "clubs", "diams", "frac12", "frac14", "frac34", "sup1", "sup2", "sup3", "iexcl", "iquest", "shy", "zwj", "zwnj", "lrm", "rlm", "thinsp",
        "ensp", "emsp", "prime", "Prime", "infin", "ne", "le", "ge", "asymp", "equiv", "sum", "prod", "radic", "part", "nabla", "isin", "notin",
        "cap", "cup", "sub", "sup", "and", "or", "not", "forall", "exist", "empty", "alpha", "beta", "gamma", "delta", "epsilon", "lambda", "mu",
        "pi", "sigma", "tau", "phi", "omega", "Delta", "Sigma", "Omega", "dagger", "Dagger", "permil", "check", "cross", "star", "starf",
        "aacute", "eacute", "iacute", "oacute", "uacute", "agrave", "egrave", "auml", "ouml", "uuml", "Auml", "Ouml", "Uuml", "szlig", "ccedil",
        "ntilde", "oslash", "aring", "aelig",
    ];

    // Angular: the properties a binding names beyond an element's attributes, and the events beyond the DOM's.
    private static readonly string[] AngularProperties =
    [
        "class", "style", "ngClass", "ngStyle", "innerHTML", "textContent", "hidden", "id", "title", "tabIndex", "attr.", "class.", "style.",
    ];

    private static readonly string[] AngularFormProperties = ["ngModel", "formControl", "formControlName", "value", "disabled", "checked"];

    private static readonly string[] AngularEvents = ["keydown.enter", "keyup.enter", "keydown.escape", "keyup.escape"];

    private static readonly string[] AngularFormEvents = ["ngModelChange"];

    private static readonly string[] AngularDirectives = ["*ngIf", "*ngFor", "*ngSwitchCase", "*ngSwitchDefault", "*ngTemplateOutlet", "ngSwitch", "ngClass", "ngStyle", "ngModel", "routerLink", "routerLinkActive"];

    private static readonly FrozenDictionary<string, string[]> ElementTable = Elements();
    private static readonly FrozenDictionary<string, string[]> ValueTable = Values();

    /// <summary>The living standard's elements (and svg, math), in order.</summary>
    public static IReadOnlyList<string> AllElements { get; } = [.. ElementTable.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The global attributes (and "data-").</summary>
    public static IReadOnlyList<string> Globals => GlobalAttributes;

    /// <summary>The ARIA attributes.</summary>
    public static IReadOnlyList<string> Aria => AriaAttributes;

    /// <summary>The DOM events (an event handler attribute is "on" and one of them).</summary>
    public static IReadOnlyList<string> AllEvents => Events;

    /// <summary>The event handler attributes ("onclick"), made once.</summary>
    public static IReadOnlyList<string> EventHandlers { get; } = [.. Events.Select(e => "on" + e)];

    /// <summary>The common named character references (without '&amp;' and ';').</summary>
    public static IReadOnlyList<string> Entities => EntityNames;

    /// <summary>Angular's structural and attribute directives offered in a tag.</summary>
    public static IReadOnlyList<string> Directives => AngularDirectives;

    internal static readonly FrozenDictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> ElementLookup = ElementTable.GetAlternateLookup<ReadOnlySpan<char>>();

    internal static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ElementNames = ElementTable.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();

    internal static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> BooleanLookup = BooleanSet.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Whether <paramref name="name"/> is a known element (ignoring case).</summary>
    public static bool IsElement(ReadOnlySpan<char> name) => ElementLookup.ContainsKey(name);

    /// <summary>Whether <paramref name="attribute"/> is a boolean attribute (written without a value).</summary>
    public static bool IsBoolean(ReadOnlySpan<char> attribute) => BooleanLookup.Contains(attribute);

    /// <summary>The element's own attributes (none for an unknown element).</summary>
    public static IReadOnlyList<string> AttributesOf(ReadOnlySpan<char> element) => ElementLookup.TryGetValue(element, out var attributes) ? attributes : [];

    /// <summary>The fixed values of <paramref name="attribute"/> on <paramref name="element"/> (the element's own list first), or none.</summary>
    public static IReadOnlyList<string> ValuesOf(ReadOnlySpan<char> element, ReadOnlySpan<char> attribute)
    {
        Span<char> key = stackalloc char[64];
        if (element.Length + 1 + attribute.Length <= key.Length)
        {
            element.CopyTo(key);
            key[element.Length] = '.';
            attribute.CopyTo(key[(element.Length + 1)..]);
            if (ValueLookup.TryGetValue(key[..(element.Length + 1 + attribute.Length)], out var own))
            {
                return own;
            }
        }

        return ValueLookup.TryGetValue(attribute, out var values) ? values : IsBoolean(attribute) ? [.. new[] { attribute.ToString() }] : [];
    }

    /// <summary>The properties an Angular binding names on <paramref name="element"/>: its attributes, the common ones, a form control's.</summary>
    public static IEnumerable<string> AngularPropertiesOf(ReadOnlySpan<char> element)
    {
        var own = AttributesOf(element);
        bool control = element.Equals("input", StringComparison.OrdinalIgnoreCase) || element.Equals("select", StringComparison.OrdinalIgnoreCase)
            || element.Equals("textarea", StringComparison.OrdinalIgnoreCase);
        return own.Concat(AngularProperties).Concat(control ? AngularFormProperties : []).Distinct(StringComparer.Ordinal);
    }

    /// <summary>The events an Angular binding names on <paramref name="element"/>.</summary>
    public static IEnumerable<string> AngularEventsOf(ReadOnlySpan<char> element)
    {
        bool control = element.Equals("input", StringComparison.OrdinalIgnoreCase) || element.Equals("select", StringComparison.OrdinalIgnoreCase)
            || element.Equals("textarea", StringComparison.OrdinalIgnoreCase);
        return Events.Concat(AngularEvents).Concat(control ? AngularFormEvents : []);
    }

    private static readonly FrozenDictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> ValueLookup = ValueTable.GetAlternateLookup<ReadOnlySpan<char>>();

    private static FrozenDictionary<string, string[]> Elements()
    {
        var table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ElementData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            table[line[..colon]] = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string[]> Values()
    {
        var table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ValueData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            table[line[..colon]] = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
