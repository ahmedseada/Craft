// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;

namespace Craft.Web.Markup;

// What an element's content is to the lexer.
internal enum ElementKind : byte
{
    Normal,
    Script,     // raw text, ended by </script
    Style,      // raw text, ended by </style
    Textarea,   // escapable raw text (RCDATA), ended by </textarea
    Title,      // RCDATA, ended by </title
}

// What a <script>'s content is, by its type attribute.
internal enum ScriptKind : byte
{
    JavaScript, // none, module, text/javascript and the other JavaScript MIME types
    Json,       // importmap, speculationrules, application/json, application/ld+json: JSON-like
    Html,       // text/x-template, text/ng-template, text/html: HTML
    Other,      // anything else (text/plain, a shader): plain text
}

// What an attribute's value is.
internal enum AttributeKind : byte
{
    Plain,      // a string
    Style,      // style="…": CSS declarations
    Event,      // on*="…": JavaScript
    Binding,    // Angular [x]="…", [(x)]="…", bind-x: an expression
    Statement,  // Angular (x)="…", on-x: a statement
    Micro,      // Angular *ngIf="…": microsyntax
    Interpolated, // a plain attribute in an Angular template: text with {{ }}
    Type,       // a <script>'s type
}

// The HTML living standard's and Angular's names the lexer, the formatter and the folding need, by span (rule 15).
internal static class HtmlNames
{
    // Void elements: no end tag, no content.
    private static readonly FrozenSet<string> Voids = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr", "param", "keygen");

    // Elements whose end tag may be left out (closed by what follows or by their parent's end).
    private static readonly FrozenSet<string> OptionalEnds = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "html", "head", "body", "li", "p", "dt", "dd", "rt", "rp", "optgroup", "option", "colgroup", "caption", "thead", "tbody", "tfoot", "tr", "td", "th");

    // Elements whose start closes an open <p>.
    private static readonly FrozenSet<string> ClosesP = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "address", "article", "aside", "blockquote", "details", "dialog", "div", "dl", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "main", "menu", "nav", "ol", "p", "pre", "search", "section", "table", "ul");

    // Angular's control flow blocks (Angular 17; @let 18.1).
    private static readonly FrozenSet<string> Blocks = FrozenSet.Create(StringComparer.Ordinal,
        "if", "else", "for", "empty", "switch", "case", "default", "defer", "placeholder", "loading", "error", "let");

    // The words that start a block's parameter (track, let, on, when, prefetch on, hydrate when, minimum, after, as).
    private static readonly FrozenSet<string> Parameters = FrozenSet.Create(StringComparer.Ordinal,
        "track", "let", "on", "when", "prefetch", "hydrate", "minimum", "after", "as", "never");

    // @defer's triggers.
    private static readonly FrozenSet<string> Triggers = FrozenSet.Create(StringComparer.Ordinal,
        "idle", "viewport", "interaction", "hover", "immediate", "timer", "never");

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Void = Voids.GetAlternateLookup<ReadOnlySpan<char>>();

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> OptionalEnd = OptionalEnds.GetAlternateLookup<ReadOnlySpan<char>>();

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ClosesParagraph = ClosesP.GetAlternateLookup<ReadOnlySpan<char>>();

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Block = Blocks.GetAlternateLookup<ReadOnlySpan<char>>();

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Parameter = Parameters.GetAlternateLookup<ReadOnlySpan<char>>();

    public static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Trigger = Triggers.GetAlternateLookup<ReadOnlySpan<char>>();

    public static ElementKind KindOf(ReadOnlySpan<char> name) =>
        name.Equals("script", StringComparison.OrdinalIgnoreCase) ? ElementKind.Script
        : name.Equals("style", StringComparison.OrdinalIgnoreCase) ? ElementKind.Style
        : name.Equals("textarea", StringComparison.OrdinalIgnoreCase) ? ElementKind.Textarea
        : name.Equals("title", StringComparison.OrdinalIgnoreCase) ? ElementKind.Title
        : ElementKind.Normal;

    // A <script type="…">'s content.
    public static ScriptKind ScriptKindOf(ReadOnlySpan<char> type)
    {
        type = type.Trim();
        if (type.IsEmpty || type.Equals("module", StringComparison.OrdinalIgnoreCase) || type.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/javascript", StringComparison.OrdinalIgnoreCase) || type.Equals("text/ecmascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("application/ecmascript", StringComparison.OrdinalIgnoreCase) || type.Equals("javascript", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/babel", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.JavaScript;
        }

        if (type.Equals("importmap", StringComparison.OrdinalIgnoreCase) || type.Equals("speculationrules", StringComparison.OrdinalIgnoreCase)
            || type.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.Json;
        }

        if (type.Equals("text/x-template", StringComparison.OrdinalIgnoreCase) || type.Equals("text/ng-template", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.Html;
        }

        return ScriptKind.Other;
    }

    // Whether opening `opening` closes the open `open` element (an optional end tag implied).
    public static bool Implies(ReadOnlySpan<char> opening, ReadOnlySpan<char> open)
    {
        static bool Is(ReadOnlySpan<char> name, string value) => name.Equals(value, StringComparison.OrdinalIgnoreCase);
        if (Is(open, "p"))
        {
            return ClosesParagraph.Contains(opening);
        }

        if (Is(open, "li"))
        {
            return Is(opening, "li");
        }

        if (Is(open, "dt") || Is(open, "dd"))
        {
            return Is(opening, "dt") || Is(opening, "dd");
        }

        if (Is(open, "option"))
        {
            return Is(opening, "option") || Is(opening, "optgroup");
        }

        if (Is(open, "optgroup"))
        {
            return Is(opening, "optgroup");
        }

        if (Is(open, "td") || Is(open, "th"))
        {
            return Is(opening, "td") || Is(opening, "th") || Is(opening, "tr") || Is(opening, "tbody") || Is(opening, "tfoot") || Is(opening, "thead");
        }

        if (Is(open, "tr"))
        {
            return Is(opening, "tr") || Is(opening, "tbody") || Is(opening, "tfoot") || Is(opening, "thead");
        }

        if (Is(open, "thead") || Is(open, "tbody"))
        {
            return Is(opening, "tbody") || Is(opening, "tfoot");
        }

        if (Is(open, "rt") || Is(open, "rp"))
        {
            return Is(opening, "rt") || Is(opening, "rp");
        }

        if (Is(open, "colgroup") || Is(open, "caption"))
        {
            return !Is(opening, "col");
        }

        if (Is(open, "head"))
        {
            return Is(opening, "body");
        }

        return false;
    }
}
