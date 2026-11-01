// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Plugins.Css;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Craft.Web.Style;
using static Craft.Tests.ProjectLanguageTests;

namespace Craft.Tests;

/// <summary>
/// Phase W's styles (W-style): the CSS, SCSS and LESS lexers of Craft.Web (the latest syntax's colours from the theme's
/// existing classes, nesting told by the first '{' or ';'), proven equal to a fresh session after thousands of random
/// edits (rule 71), a warm screen at 0 bytes and the lines re-lexed per keystroke (rule 63), the built-in formatter
/// (white space only, idempotent, a malformed file left with the reason, the caret kept, Format Selection), folding, the
/// version setting and a project's browserslist, through the CSS plug-in's languages.
/// </summary>
[TestGroup("WebStyle")]
public static class WebStyleTests
{
    internal const string CssFixture = """
        /* #region tokens */
        @layer reset, base, components;
        @import url(theme.css) layer(theme) supports(display: grid);
        :root {
          --brand: oklch(70% 0.15 250);
          --accent: color-mix(in oklch, var(--brand) 60%, white);
          --muted: rgb(from var(--brand) r g b / 50%);
        }
        /* #endregion */
        @property --angle {
          syntax: "<angle>";
          inherits: false;
          initial-value: 0deg;
        }
        .card {
          container-type: inline-size;
          color: light-dark(#333, #eee);
          &:hover { background: #0af8; }
          .title:has(+ .subtitle) {
            margin-block: 0 .5rem;
          }
          @container card (inline-size > 30em) {
            display: grid;
          }
        }
        @scope (.card) to (.content) {
          img { border-radius: 50%; }
        }
        @starting-style {
          .dialog[open] { opacity: 0; }
        }
        .anchor { anchor-name: --tip; }
        .tip {
          position-anchor: --tip;
          top: anchor(bottom);
          position-try-fallbacks: flip-block;
        }
        @position-try --below { top: anchor(bottom); }
        :is(h1, h2):where(.x)::before { content: "\201C"; }
        @function --double(--x) { result: calc(var(--x) * 2); }
        .btn { width: if(media(width > 600px): 10rem; else: 100%) !important; }
        @media (width >= 600px) and (prefers-color-scheme: dark) {
          a { color: red; }
        }
        .nest {
          color:
            blue;
          b:hover,
          .c {
            grid-template-areas:
              "x y";
          }
          filter:blur(2px)
        }

        """;

    internal const string ScssFixture = """
        @use "sass:math" as m;
        @forward "src/list" hide list-reset;
        $base: 16px !default;
        $map: (
          primary: #0d6efd,
          "secondary": rgba(0, 0, 0, .5),
        );
        // a line comment
        %placeholder { margin: 0; }
        @mixin theme($color: blue, $args...) {
          background: $color;
          @content;
        }
        @function rem($px) {
          @return math.div($px, $base) * 1rem;
        }
        .alert {
          @extend %placeholder;
          @include theme($color: red);
          &-title { font-size: rem(20px); }
          #{$prop}-top: 1px;
          width: calc(100% - #{$gutter});
          content: "a-#{$name}-b";
          @if $a == 1 and not $b {
            color: red;
          } @else if $c {
            color: blue;
          } @else {
            color: green;
          }
          @each $key, $value in $map {
            .text-#{$key} { color: $value; }
          }
          @for $i from 1 through 3 { .m-#{$i} { margin: $i * 4px; } }
        }
        .multi-#{
          $x
        } { color: red; }

        """;

    internal const string LessFixture = """
        @import (reference) "theme.less";
        @primary: #428bca;
        @min768: ~"(min-width: 768px)";
        @detached: { background: red; };
        .bordered(@width: 2px; @style: solid) {
          border: @width @style black;
        }
        .mixin(@a) when (lightness(@a) >= 50%) {
          background-color: black;
        }
        // line comment
        #header {
          .navigation { font-size: 12px; }
          .bordered(4px; dashed);
          @media @min768 { width: ~"calc(100% - @{gutter})"; }
          .@{name}-suffix { color: @@var; }
          @detached();
          &:extend(.banner all);
          width: percentage(0.5);
          color: fade(@primary, 50%);
        }
        @plugin "my-plugin";
        .guard when (@mode = huge) { width: 100%; }

        """;

    private static StyleLanguage Language(string name) => StyleLanguages.All.Single(l => l.Name == name);

    [Test]
    public static void CssHasTheLatestSyntaxsColours()
    {
        var spans = Spans(StyleTokenizer.For(StyleDialect.Css), CssFixture);
        Expect(spans, "/* #region tokens */", TokenClasses.Comment, "a comment");
        Expect(spans, "@layer", TokenClasses.ControlKeyword, "@layer, an at-rule");
        Expect(spans, "reset", TokenClasses.NamespaceName, "a layer's name");
        Expect(spans, "url", TokenClasses.MethodName, "url()");
        Expect(spans, "theme.css", TokenClasses.String, "an unquoted url");
        Expect(spans, "layer", TokenClasses.MethodName, "@import's layer()");
        Expect(spans, "display", TokenClasses.PropertyName, "a supports() condition's property");
        Expect(spans, ":root", TokenClasses.TypeParameterName, "a pseudo-class");
        Expect(spans, "--brand", TokenClasses.LocalName, "a custom property");
        Expect(spans, "oklch", TokenClasses.MethodName, "oklch()");
        Expect(spans, "70%", TokenClasses.Number, "a percentage");
        Expect(spans, "0.15", TokenClasses.Number, "a number");
        Expect(spans, "color-mix", TokenClasses.MethodName, "color-mix()");
        Expect(spans, "var", TokenClasses.MethodName, "var()");
        Expect(spans, "white", TokenClasses.Number, "a named colour, as a number");
        Expect(spans, "rgb", TokenClasses.MethodName, "a relative colour's function");
        Expect(spans, "@property", TokenClasses.ControlKeyword, "@property");
        Expect(spans, "--angle", TokenClasses.LocalName, "@property's name");
        Expect(spans, "syntax", TokenClasses.PropertyName, "@property's descriptor");
        Expect(spans, "\"<angle>\"", TokenClasses.String, "a string");
        Expect(spans, "0deg", TokenClasses.Number, "a number with its unit");
        Expect(spans, ".card", TokenClasses.ClassName, "a class selector");
        Expect(spans, "container-type", TokenClasses.PropertyName, "a property");
        Expect(spans, "inline-size", TokenClasses.EnumMemberName, "a value's keyword");
        Expect(spans, "light-dark", TokenClasses.MethodName, "light-dark()");
        Expect(spans, "#333", TokenClasses.Number, "a hex colour");
        Expect(spans, "&", TokenClasses.Keyword, "the nesting selector");
        Expect(spans, ":hover", TokenClasses.TypeParameterName, "a nested rule's pseudo-class");
        Expect(spans, "background", TokenClasses.PropertyName, "a declaration in a nested rule on one line");
        Expect(spans, "#0af8", TokenClasses.Number, "a four-digit hex colour");
        Expect(spans, ":has", TokenClasses.TypeParameterName, ":has()");
        Expect(spans, ".subtitle", TokenClasses.ClassName, "a class inside :has()");
        Expect(spans, "margin-block", TokenClasses.PropertyName, "a nested rule's declaration");
        Expect(spans, ".5rem", TokenClasses.Number, "a number starting with '.'");
        Expect(spans, "@container", TokenClasses.ControlKeyword, "@container");
        Expect(spans, "card", TokenClasses.ClassName, "a container's name");
        Expect(spans, "30em", TokenClasses.Number, "a size query's length");
        Expect(spans, "@scope", TokenClasses.ControlKeyword, "@scope");
        Expect(spans, "to", TokenClasses.Keyword, "@scope's 'to'");
        Expect(spans, ".content", TokenClasses.ClassName, "@scope's limit");
        Expect(spans, "img", TokenClasses.Keyword, "a type selector");
        Expect(spans, "@starting-style", TokenClasses.ControlKeyword, "@starting-style");
        Expect(spans, "open", TokenClasses.ParameterName, "an attribute selector's name");
        Expect(spans, "anchor-name", TokenClasses.PropertyName, "anchor-name");
        Expect(spans, "--tip", TokenClasses.LocalName, "a dashed anchor name");
        Expect(spans, "position-anchor", TokenClasses.PropertyName, "position-anchor");
        Expect(spans, "anchor", TokenClasses.MethodName, "anchor()");
        Expect(spans, "position-try-fallbacks", TokenClasses.PropertyName, "position-try-fallbacks");
        Expect(spans, "@position-try", TokenClasses.ControlKeyword, "@position-try");
        Expect(spans, "--below", TokenClasses.LocalName, "@position-try's name");
        Expect(spans, ":is", TokenClasses.TypeParameterName, ":is()");
        Expect(spans, ":where", TokenClasses.TypeParameterName, ":where()");
        Expect(spans, "::before", TokenClasses.TypeParameterName, "a pseudo-element");
        Expect(spans, "\\201C", TokenClasses.StringEscape, "a hex escape in a string");
        Expect(spans, "@function", TokenClasses.ControlKeyword, "@function");
        Expect(spans, "--double", TokenClasses.MethodName, "a custom function's name");
        Expect(spans, "result", TokenClasses.PropertyName, "an unknown descriptor still lexes as a property (CSS Syntax 3)");
        Expect(spans, "if", TokenClasses.MethodName, "if()");
        Expect(spans, "media", TokenClasses.MethodName, "if()'s media()");
        Expect(spans, "10rem", TokenClasses.Number, "a branch's value, past a ';' inside if()'s parentheses");
        Expect(spans, "!important", TokenClasses.Keyword, "!important");
        Expect(spans, "@media", TokenClasses.ControlKeyword, "@media");
        Expect(spans, "and", TokenClasses.Keyword, "a media query's 'and'");
        Expect(spans, "prefers-color-scheme", TokenClasses.PropertyName, "a media feature");
        Expect(spans, "red", TokenClasses.Number, "a named colour");
        Expect(spans, "blue", TokenClasses.Number, "a value on the line after its property (a known property and ':' at a line's end)");
        Expect(spans, "b", TokenClasses.Keyword, "'b:hover,' with no '{' on its line is a nested rule's selector (b is no property)");
        Expect(spans, "grid-template-areas", TokenClasses.PropertyName, "a declaration whose value is on the next lines");
        Expect(spans, "\"x y\"", TokenClasses.String, "its value");
        Expect(spans, "filter", TokenClasses.PropertyName, "a known property with no ';' (the '}' on the next line)");
        Expect(spans, "blur", TokenClasses.MethodName, "its function");
        Check.True(!spans.Any(s => s.Class == TokenClasses.PropertyName && s.Text == "hover"), "a pseudo-class is never a property");
    }

    [Test]
    public static void ScssHasItsColours()
    {
        var spans = Spans(StyleTokenizer.For(StyleDialect.Scss), ScssFixture);
        Expect(spans, "@use", TokenClasses.ControlKeyword, "@use");
        Expect(spans, "\"sass:math\"", TokenClasses.String, "a module's URL");
        Expect(spans, "as", TokenClasses.Keyword, "@use's 'as'");
        Expect(spans, "@forward", TokenClasses.ControlKeyword, "@forward");
        Expect(spans, "hide", TokenClasses.Keyword, "@forward's 'hide'");
        Expect(spans, "$base", TokenClasses.LocalName, "a variable");
        Expect(spans, "16px", TokenClasses.Number, "a length");
        Expect(spans, "!default", TokenClasses.Keyword, "!default");
        Expect(spans, "$map", TokenClasses.LocalName, "a map's variable");
        Expect(spans, "primary", TokenClasses.PropertyName, "a map's key");
        Expect(spans, "#0d6efd", TokenClasses.Number, "a hex colour");
        Expect(spans, "\"secondary\"", TokenClasses.String, "a quoted key");
        Expect(spans, ".5", TokenClasses.Number, "a number");
        Expect(spans, "// a line comment", TokenClasses.Comment, "a // comment");
        Expect(spans, "%placeholder", TokenClasses.ClassName, "a placeholder selector");
        Expect(spans, "@mixin", TokenClasses.ControlKeyword, "@mixin");
        Expect(spans, "theme", TokenClasses.MethodName, "a mixin's name");
        Expect(spans, "$color", TokenClasses.LocalName, "a parameter");
        Expect(spans, "$args", TokenClasses.LocalName, "a rest parameter");
        Expect(spans, "@content", TokenClasses.ControlKeyword, "@content");
        Expect(spans, "@function", TokenClasses.ControlKeyword, "SCSS's @function");
        Expect(spans, "rem", TokenClasses.MethodName, "a function's name");
        Expect(spans, "@return", TokenClasses.ControlKeyword, "@return");
        Expect(spans, "math", TokenClasses.NamespaceName, "a module's namespace");
        Expect(spans, "div", TokenClasses.MethodName, "a module's function");
        Expect(spans, ".alert", TokenClasses.ClassName, "a class");
        Expect(spans, "@extend", TokenClasses.ControlKeyword, "@extend");
        Expect(spans, "@include", TokenClasses.ControlKeyword, "@include");
        Expect(spans, "-title", TokenClasses.ClassName, "a suffix on '&'");
        Expect(spans, "font-size", TokenClasses.PropertyName, "a property");
        Expect(spans, "#{", TokenClasses.Operator, "an interpolation's start");
        Expect(spans, "$prop", TokenClasses.LocalName, "an interpolated variable");
        Expect(spans, "-top", TokenClasses.PropertyName, "an interpolated property's rest");
        Expect(spans, "$gutter", TokenClasses.LocalName, "an interpolation in a value");
        Expect(spans, "\"a-", TokenClasses.String, "a string before its interpolation");
        Expect(spans, "$name", TokenClasses.LocalName, "an interpolation in a string");
        Expect(spans, "-b\"", TokenClasses.String, "the string after it");
        Expect(spans, "@if", TokenClasses.ControlKeyword, "@if");
        Expect(spans, "not", TokenClasses.Keyword, "'not'");
        Expect(spans, "@else", TokenClasses.ControlKeyword, "@else after '}'");
        Expect(spans, "@each", TokenClasses.ControlKeyword, "@each");
        Expect(spans, "in", TokenClasses.Keyword, "@each's 'in'");
        Expect(spans, ".text-", TokenClasses.ClassName, "a class before an interpolation");
        Expect(spans, "@for", TokenClasses.ControlKeyword, "@for");
        Expect(spans, "through", TokenClasses.Keyword, "@for's 'through'");
        Expect(spans, "4px", TokenClasses.Number, "a length in a product");
        Expect(spans, "$x", TokenClasses.LocalName, "an interpolation over three lines (#{} across lines)");
        var text = ScssFixture;
        int last = text.LastIndexOf("color", StringComparison.Ordinal);
        Check.True(spans.Any(s => s.Text == "color" && s.Class == TokenClasses.PropertyName), "after the multi-line interpolation closes, a declaration again");
        Check.True(last > 0, "the fixture holds its last rule");
    }

    [Test]
    public static void LessHasItsColours()
    {
        var spans = Spans(StyleTokenizer.For(StyleDialect.Less), LessFixture);
        Expect(spans, "@import", TokenClasses.ControlKeyword, "@import");
        Expect(spans, "\"theme.less\"", TokenClasses.String, "its file");
        Expect(spans, "@primary", TokenClasses.LocalName, "a variable's declaration");
        Expect(spans, "#428bca", TokenClasses.Number, "a hex colour");
        Expect(spans, "@min768", TokenClasses.LocalName, "another variable");
        Expect(spans, "~", TokenClasses.String, "an escape's '~'");
        Expect(spans, "\"(min-width: 768px)\"", TokenClasses.String, "its string");
        Expect(spans, "@detached", TokenClasses.LocalName, "a detached ruleset");
        Expect(spans, ".bordered", TokenClasses.MethodName, "a mixin's definition");
        Expect(spans, "@width", TokenClasses.LocalName, "a mixin's parameter");
        Expect(spans, "border", TokenClasses.PropertyName, "a property");
        Expect(spans, ".mixin", TokenClasses.MethodName, "a guarded mixin");
        Expect(spans, "when", TokenClasses.ControlKeyword, "a guard's 'when'");
        Expect(spans, "lightness", TokenClasses.MethodName, "a guard's function");
        Expect(spans, "// line comment", TokenClasses.Comment, "a // comment");
        Expect(spans, "#header", TokenClasses.ConstantName, "an id selector");
        Expect(spans, ".navigation", TokenClasses.ClassName, "a nested class");
        Expect(spans, "12px", TokenClasses.Number, "a length");
        Expect(spans, "@media", TokenClasses.ControlKeyword, "@media");
        Expect(spans, "@{", TokenClasses.Operator, "an interpolation");
        Expect(spans, "@@var", TokenClasses.LocalName, "a variable variable");
        Expect(spans, "@detached", TokenClasses.MethodName, "a detached ruleset's call");
        Expect(spans, ":extend", TokenClasses.TypeParameterName, ":extend");
        Expect(spans, ".banner", TokenClasses.ClassName, "the extended class");
        Expect(spans, "percentage", TokenClasses.MethodName, "a LESS function");
        Expect(spans, "fade", TokenClasses.MethodName, "another");
        Expect(spans, "@plugin", TokenClasses.ControlKeyword, "@plugin");
        Expect(spans, "@mode", TokenClasses.LocalName, "a guard's variable");
    }

    [Test]
    public static void ARuleIsToldFromADeclarationAndSassEndsAStatementAtTheLine()
    {
        // The same words, a rule or a declaration by what comes first, '{' or ';'.
        var css = Spans(StyleTokenizer.For(StyleDialect.Css), ".a {\n  color: red;\n  color:hover { x: y; }\n  unknown-thing: 1px;\n  span:not(.b) { }\n}\n");
        Check.True(css.Count(s => s is { Text: "color", Class: TokenClasses.PropertyName }) == 1, "'color: red;' is a declaration, 'color:hover {' a nested rule");
        Expect(css, "unknown-thing", TokenClasses.PropertyName, "an unknown property ending at ';' is a declaration");
        Expect(css, "span", TokenClasses.Keyword, "a nested rule's type selector");
        Expect(css, ":not", TokenClasses.TypeParameterName, "its pseudo-class");

        // .sass (indented): no braces, a statement ends at its line.
        var sass = Spans(StyleTokenizer.For(StyleDialect.Sass), "$c: red\n.a\n  color: $c\n  +theme(blue)\n  &:hover\n    margin: 0\n");
        Expect(sass, "$c", TokenClasses.LocalName, ".sass: a variable");
        Expect(sass, ".a", TokenClasses.ClassName, ".sass: a selector on its own line");
        Expect(sass, "color", TokenClasses.PropertyName, ".sass: a declaration on the next line, the selector ended at its line");
        Expect(sass, "+", TokenClasses.ControlKeyword, ".sass: '+' includes a mixin");
        Expect(sass, "theme", TokenClasses.MethodName, ".sass: the mixin");
        Expect(sass, "margin", TokenClasses.PropertyName, ".sass: a nested declaration");

        var rules = Language(StyleLanguages.Css).Rules;
        Check.True(rules is { LineComment: null, BlockCommentOpen: "/*", BlockCommentClose: "*/", StringDelimiters: "\"'" }, "CSS: block comments only, strings in either quote (for the bracket colours)");
        Check.True(rules.Brackets.Select(b => b.Open).SequenceEqual(['{', '(', '[']), "CSS's brackets: {} () []");
        Check.Equal("//", Language(StyleLanguages.Scss).Rules.LineComment, "SCSS comments a line with //");
        Check.Equal("//", Language(StyleLanguages.Less).Rules.LineComment, "LESS too");

        // Toggle comment: CSS wraps the line in /* */ (W0's block fallback), SCSS uses //.
        var (cssModel, cssDocument, _) = Editors.MakeMarked("a {\n  b: |c;\n}\n", null, Language(StyleLanguages.Css));
        cssModel.ToggleLineComment();
        Check.Equal("a {\n  /* b: c; */\n}\n", cssDocument.Text, "Ctrl+/ in CSS wraps the line in a block comment");
        var (scssModel, scssDocument, _) = Editors.MakeMarked("a {\n  b: |c;\n}\n", null, Language(StyleLanguages.Scss));
        scssModel.ToggleLineComment();
        Check.Equal("a {\n  // b: c;\n}\n", scssDocument.Text, "Ctrl+/ in SCSS comments the line with //");
        cssModel.Dispose();
        scssModel.Dispose();
    }

    [Test]
    public static void RandomEditsEqualAFreshSession()
    {
        // Thousands of random edits per dialect, weighted to the characters that change a line's state: after each check
        // the incremental session's spans equal a fresh session's (rule 71).
        string[] fragments = ["{", "}", ";", ":", "/", "*", "\"", "'", "#", "\n", "\n", "{", "}", ";", "#{", "@{", "/*", "*/", "//", "(", ")", "\\", "$", "@", "a", " ", "url(", "-"];
        int rounds = 0;
        foreach (var (dialect, fixture) in new[] { (StyleDialect.Css, CssFixture), (StyleDialect.Scss, ScssFixture), (StyleDialect.Less, LessFixture), (StyleDialect.Sass, "$c: red\n.a\n  color: $c\n  &:hover\n    margin: 0\n") })
        {
            var tokenizer = StyleTokenizer.For(dialect);
            var random = new Random(71 + (int)dialect);
            var buffer = new PieceTableBuffer(fixture);
            using var live = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
            using var into = new TokenSpanBuffer();
            for (int round = 0; round < 1500; round++, rounds++)
            {
                int length = buffer.Current.Length;
                int start = random.Next(0, length + 1);
                int remove = random.Next(4) == 0 ? random.Next(0, Math.Min(length - start, 6) + 1) : 0;
                buffer.Apply([new TextEdit(start, remove, fragments[random.Next(fragments.Length)])]);
                var shown = buffer.Current.GetLine(random.Next(0, buffer.Current.LineCount));
                into.Clear();
                live.Classify(buffer.Current, shown.Start, shown.Length, into);
                if (round % 15 == 14)
                {
                    var fresh = new PieceTableBuffer(buffer.Current.GetText(0, buffer.Current.Length));
                    using var again = new LineStateSession(fresh, tokenizer, null, LanguageSettings.Default);
                    using var expected = new TokenSpanBuffer();
                    into.Clear();
                    live.Classify(buffer.Current, 0, buffer.Current.Length, into);
                    again.Classify(fresh.Current, 0, fresh.Current.Length, expected);
                    Check.SequenceEqual(expected.Spans.ToArray(), into.Spans.ToArray(), $"{dialect}, round {round}: the spans equal a fresh session's");
                    int end = 0;
                    foreach (var span in into.Spans)
                    {
                        Check.True(span.Start >= end && span.Length > 0, $"{dialect}, round {round}: spans in order, not overlapping, not empty (at {span.Start})");
                        end = span.End;
                    }
                }
            }
        }

        Check.True(rounds >= 6000, $"thousands of edits ({rounds})");
    }

    [Test]
    public static void AWarmScreenAllocatesNothingAndAKeystrokeRelexesItsLine()
    {
        var results = new List<string>();
        foreach (var (dialect, fixture) in new[] { (StyleDialect.Css, CssFixture), (StyleDialect.Scss, ScssFixture), (StyleDialect.Less, LessFixture) })
        {
            var tokenizer = StyleTokenizer.For(dialect);
            var lines = new List<string>();
            while (lines.Count < 10_000)
            {
                lines.AddRange(fixture.Split('\n'));
            }

            string whole = string.Join('\n', lines);
            var buffer = new PieceTableBuffer(whole);
            using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
            using var into = new TokenSpanBuffer();
            var screen = buffer.Current.GetLine(5_000);
            var screenEnd = buffer.Current.GetLine(5_059);
            long started = Stopwatch.GetTimestamp();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            double coldMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            for (int warm = 0; warm < 50; warm++)
            {
                into.Clear();
                session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            }

            const int Frames = 1000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < Frames; frame++)
            {
                into.Clear();
                session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            }

            double warmUs = Stopwatch.GetElapsedTime(started).TotalMicroseconds / Frames;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(0L, allocated, $"{dialect}: {Frames} warm screens of 60 lines allocate nothing");

            // A keystroke re-lexes the lines painted and nothing past them: the states below settle on the next line.
            var far = buffer.Current.GetLine(9_000);
            var farEnd = buffer.Current.GetLine(9_059);
            into.Clear();
            session.Classify(buffer.Current, far.Start, farEnd.End - far.Start, into);
            var line = buffer.Current.GetLine(5_010);
            long scanned = session.LinesScanned;
            buffer.Apply([TextEdit.Insert(line.Start + Math.Min(2, line.Length), "x")]);
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            into.Clear();
            session.Classify(buffer.Current, far.Start, farEnd.End - far.Start, into);
            long perKey = session.LinesScanned - scanned;
            Check.True(perKey <= 1, $"{dialect}: typing a letter re-lexes no line past the screen ({perKey} read again to reach line 9,000)");
            scanned = session.LinesScanned;
            var selectorLine = buffer.Current.GetLine(5_020);
            buffer.Apply([TextEdit.Insert(selectorLine.End, " {")]);
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            into.Clear();
            session.Classify(buffer.Current, far.Start, farEnd.End - far.Start, into);
            long perBrace = session.LinesScanned - scanned;
            Check.True(perBrace <= 10, $"{dialect}: typing '{{' re-lexes a few lines past the screen at most, until the states settle ({perBrace})");

            // Typing '/*' at the top: every line below is in a comment until '*/': the screen's lines are read once.
            scanned = session.LinesScanned;
            buffer.Apply([TextEdit.Insert(0, "/*")]);
            started = Stopwatch.GetTimestamp();
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
            double commentMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long perComment = session.LinesScanned - scanned;

            // The formatter and the folding, cold (first call) and warm (median of five).
            string text = whole;
            var style = new FormatStyle("  ", "\n");
            var formatter = new StyleFormatter(dialect);
            var (formatCold, formatWarm) = ColdWarm(() =>
            {
                var edits = new List<TextEdit>();
                formatter.Format(text, style, edits);
            });
            var snapshot = new PieceTableBuffer(text).Current;
            var (foldCold, foldWarm) = ColdWarm(() => StyleFolding.Ranges(snapshot, tokenizer));
            results.Add(string.Create(CultureInfo.InvariantCulture,
                $"{dialect}, {buffer.Current.LineCount:N0} lines: a 60-line screen at line 5,000 cold {coldMs:F2} ms (5,000 states learned), warm {warmUs:F1} us and {allocated / Frames} B a screen; a keystroke re-lexes the 60 painted lines and {perKey} more (a letter), {perBrace} more (a '{{'); '/*' typed at the top: {perComment:N0} lines read, {commentMs:F2} ms; format {formatCold:F0} ms cold, {formatWarm:F0} ms warm; folding {foldCold:F0} ms cold, {foldWarm:F0} ms warm"));
        }

        foreach (var result in results)
        {
            Console.WriteLine("    measured: " + result);
        }

        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "web-style.txt"), string.Join(Environment.NewLine, results) + Environment.NewLine);
    }

    [Test]
    public static void TheFormatterChangesOnlyWhiteSpaceAndIsIdempotent()
    {
        var style = new FormatStyle("  ", "\n");
        Check.Equal(
            "@media screen and (min-width:600px) {\n  .a,\n  .b>c {\n    color: red;\n    background: blue\n  }\n  /* note */\n\n  .d {}\n}",
            Format(new StyleFormatter(StyleDialect.Css), "@media screen and (min-width:600px){.a,.b>c{color:red;background : blue}\n/* note */\n\n\n.d{}}", style),
            "CSS: a block a level, one selector and one declaration a line, ': ' in a declaration, one empty line kept, a selector's ':' untouched");
        Check.Equal(
            "$map: (a:1, b:2);\n.x {\n  @include m($a, $b);\n  &:hover {\n    color: red\n  }\n}\n@if $a {\n  b: c\n} @else {\n  b: d\n}\n// end\n",
            Format(new StyleFormatter(StyleDialect.Scss), "$map:(a:1,b:2);\n.x{@include m($a,$b);&:hover{color:red}}\n@if $a{b:c}\n@else{b:d}\n// end\n", style),
            "SCSS: a space after a value's comma, '&:hover' kept, '} @else {' on one line, a // comment kept on its line");
        Check.Equal(
            "@w: 10px;\n.m(@a;@b) {\n  width: @a\n}\n.x {\n  .m(1;2);\n  color: ~\"a\"\n}",
            Format(new StyleFormatter(StyleDialect.Less), "@w:10px;\n.m(@a;@b){width:@a}\n.x{.m(1;2);color:~\"a\"}", style),
            "LESS: a variable's ':' spaced, a mixin's call and its ';' inside parentheses untouched");
        Check.Equal(
            "$m: (\n  a: 1,\n  b: (\n    c: 2\n  ),\n);",
            Format(new StyleFormatter(StyleDialect.Scss), "$m: (\na: 1,\n      b: (\n c: 2\n  ),\n    );", style),
            "a map's lines indented by its parentheses, its ')' back at the declaration's");
        Check.Equal("a {\n\tb: c; /* why */\n}\n", Format(new StyleFormatter(StyleDialect.Css), "a {  b : c ;   /* why */\n}\n", new FormatStyle("\t", "\n")),
            "tabs when the editor indents with tabs; a comment after a declaration stays on its line");
        Check.Equal("a {\r\n    b: url(x y.png);\r\n}", Format(new StyleFormatter(StyleDialect.Scss), "a{b:url(x y.png);}", new FormatStyle("    ", "\r\n")),
            "the tab size and the document's line break; an unquoted url() left whole (no // comment inside it)");

        foreach (var (dialect, fixture) in new[] { (StyleDialect.Css, CssFixture), (StyleDialect.Scss, ScssFixture), (StyleDialect.Less, LessFixture) })
        {
            var formatter = new StyleFormatter(dialect);
            string once = Format(formatter, fixture, style);
            Check.Equal(Squeeze(fixture), Squeeze(once), $"{dialect}: only white space changed");
            Check.Equal(once, Format(formatter, once, style), $"{dialect}: formatting again changes nothing");
            var edits = new List<TextEdit>();
            formatter.Format(once, style, edits);
            Check.Equal(0, edits.Count, $"{dialect}: and makes no edits");
            string messy = Mess(fixture);
            Check.Equal(Squeeze(fixture), Squeeze(Format(formatter, messy, style)), $"{dialect}: a messy copy keeps its characters");
            Check.Equal(once, Format(formatter, messy, style), $"{dialect}: and formats to the same text");
        }

        foreach (var (dialect, bad, where) in new[]
        {
            (StyleDialect.Css, "a { color: red;", "line 1, column 3: a '{' is not closed"),
            (StyleDialect.Css, "a { b: c; } }", "line 1, column 13: a '}' closes nothing"),
            (StyleDialect.Css, "a { b: rgb(1, 2; }", "line 1, column 11: '(' is not closed"),
            (StyleDialect.Css, "a { b: \"x; }", "line 1, column 8: a string is not closed"),
            (StyleDialect.Css, "a {}\n/* x", "line 2, column 1: a comment is not closed"),
            (StyleDialect.Css, "a { b: c) }", "line 1, column 9: a ')' closes nothing"),
            (StyleDialect.Scss, "a { b: #{$x", "line 1, column 8: an interpolation is not closed"),
        })
        {
            var error = Check.Throws<FormatException>(() => Format(new StyleFormatter(dialect), bad, style), $"{dialect}: malformed is refused: {bad}");
            Check.True(error.Message.StartsWith(where, StringComparison.Ordinal), $"the message says where and what ({error.Message})");
        }

        Check.Throws<ArgumentException>(() => _ = new StyleFormatter(StyleDialect.Sass), ".sass's indented syntax is not formatted");
    }

    [Test]
    public static async Task FormatThroughTheEditorKeepsTheCaretAndFormatsASelection()
    {
        var options = new CraftOptions { TabSize = 2, InsertSpaces = true };
        var css = Language(StyleLanguages.Css);
        const string messy = "a{color:red;background:blue}\nb{margin:0}\n";
        var (model, document, _) = Editors.Make(messy, options, css);
        model.Dispatch = action => action();
        int caret = messy.IndexOf("blue", StringComparison.Ordinal) + 2;
        model.Select(caret, caret);
        int steps = document.Kinds.Count;
        Check.True(model.FormattingSession is LineStateSession, "CSS formats through the public kit's session");
        Check.True(await model.FormatDocumentAsync(), "the format applied edits");
        Check.Equal("a {\n  color: red;\n  background: blue\n}\nb {\n  margin: 0\n}\n", document.Text, "formatted with the editor's indent");
        Check.Equal("ue", document.Text.Substring(model.Selection.Caret, 2), "the caret stayed on its character");
        Check.Equal(steps + 1, document.Kinds.Count, "as one step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal(messy, document.Text, "one undo takes it back");

        // Format Selection (Ctrl+K Ctrl+F): only the selected lines.
        var (selection, selected, _) = Editors.Make("a{color:red}\n  b{margin:0}\nc{x:y}\n", options, css);
        selection.Dispatch = action => action();
        int second = selected.Text.IndexOf("b{", StringComparison.Ordinal);
        selection.Select(second, second + 3);
        Check.True(await selection.FormatSelectionAsync(), "the selection was formatted");
        Check.Equal("a{color:red}\nb {\n  margin: 0\n}\nc{x:y}\n", selected.Text, "only the selected line changed (its indentation set too)");
        Check.Equal("b {", selected.Text.Substring(selection.Selection.Start, 3), "the selection kept on its characters");
        selection.Select(1, 1);
        selection.OnKey(EditorKey.K, KeyModifiers.Control);
        selection.OnKey(EditorKey.F, KeyModifiers.Control);
        for (int wait = 0; wait < 500 && !selected.Text.StartsWith("a {", StringComparison.Ordinal); wait++)
        {
            await Task.Delay(10);
        }

        Check.True(selected.Text.StartsWith("a {\n  color: red\n}\n", StringComparison.Ordinal), $"Ctrl+K Ctrl+F formats the caret's line ({selected.Text})");
        Check.True(Craft.Editor.Shell.StandardShortcuts.LineCommands.Any(c => c.Id == Craft.Editor.Shell.StandardShortcuts.FormatSelection && c.InMenu), "Format selection is an Edit menu command");
        Check.True(Craft.Editor.Shell.StandardShortcuts.Bindings.Any(b => b.CommandId == Craft.Editor.Shell.StandardShortcuts.FormatSelection), "bound to Ctrl+K Ctrl+F");

        // A malformed file is left as it is, with the reason.
        var (broken, brokenDocument, _) = Editors.Make("a { b: c;\n", options, css);
        broken.Dispatch = action => action();
        Check.True(!await broken.FormatDocumentAsync() && brokenDocument.Text == "a { b: c;\n", "a malformed file is not formatted");
        Check.True(broken.LastFormatMessage is { } message && message.Contains("line 1, column 3", StringComparison.Ordinal), $"the message says where ({broken.LastFormatMessage})");

        // The format setting off: the built-in formatter does nothing.
        CssSettings.Source = new Settings(("format", "false"));
        try
        {
            var (off, offDocument, _) = Editors.Make(messy, options, css);
            off.Dispatch = action => action();
            Check.True(!await off.FormatDocumentAsync() && offDocument.Text == messy, "CRAFT_PLUGIN_CSS_FORMAT=false: Format document changes nothing");
            off.Dispose();
        }
        finally
        {
            CssSettings.Source = null;
        }

        var (sass, sassDocument, _) = Editors.Make("a\n  b:   c\n", options, Language(StyleLanguages.Scss), path: "/w/a.sass");
        sass.Dispatch = action => action();
        Check.True(!await sass.FormatDocumentAsync() && sassDocument.Text == "a\n  b:   c\n", ".sass opens as SCSS-like and is not formatted");
        model.Dispose();
        selection.Dispose();
        broken.Dispose();
        sass.Dispose();
    }

    [Test]
    public static void FoldingByBracesRegionsAndCommentRuns()
    {
        const string css = "/* #region vars */\n:root {\n  --a: 1;\n}\n/* #endregion */\n/* a\n   b */\n.x {\n  .y { content: \"{\"; }\n  .z {\n    b: url({);\n  }\n}\n";
        var ranges = StyleFolding.Ranges(new PieceTableBuffer(css).Current, StyleTokenizer.For(StyleDialect.Css)).OrderBy(r => r.StartLine).ThenBy(r => r.EndLine).ToList();
        Check.SequenceEqual(
            new[] { new FoldingRange(0, 4, FoldingRangeKind.Marked), new FoldingRange(1, 3), new FoldingRange(5, 6, FoldingRangeKind.Comment), new FoldingRange(7, 12), new FoldingRange(9, 11) },
            ranges.ToArray(), "CSS: a #region, the blocks over several lines, a block comment; braces in strings and url() skipped");

        const string scss = "// one\n// two\n// three\n.a {\n  // #region x\n  b: c;\n  // #endregion\n}\n.b-#{$i} {\n}\n";
        var scssRanges = StyleFolding.Ranges(new PieceTableBuffer(scss).Current, StyleTokenizer.For(StyleDialect.Scss)).OrderBy(r => r.StartLine).ThenBy(r => r.EndLine).ToList();
        Check.SequenceEqual(
            new[] { new FoldingRange(0, 2, FoldingRangeKind.Comment), new FoldingRange(3, 7), new FoldingRange(4, 6, FoldingRangeKind.Marked), new FoldingRange(8, 9) },
            scssRanges.ToArray(), "SCSS: a run of // comments, a // #region, an interpolation's braces never fold");

        Check.True(CssFolding.Instance.Languages.SequenceEqual([StyleLanguages.Css, StyleLanguages.Scss, StyleLanguages.Less]), "the provider folds the three languages");
        var buffer = new PieceTableBuffer(scss);
        using var session = Language(StyleLanguages.Scss).Open(buffer);
        var request = new FoldingRequest(buffer.Current, StyleLanguages.Scss, session, 4);
        var fromProvider = CssFolding.Instance.GetRangesAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Check.Equal(scssRanges.Count, fromProvider.Count, "the provider answers with the session's own dialect");
    }

    [Test]
    public static async Task AVersionReadsNewerNamesAsPlainAndAProjectNamesItsVersion()
    {
        const string text = "@layer a;\n@starting-style { .a { opacity: 0; } }\n.b { color: light-dark(red, blue); position-anchor: --x; inset: oklch(1 0 0); }\n:has(.c) {}\n";
        var latest = Spans(StyleTokenizer.For(StyleDialect.Css), text);
        var old = Spans(StyleTokenizer.For(StyleDialect.Css, 2023), text);
        Expect(latest, "@starting-style", TokenClasses.ControlKeyword, "latest: @starting-style");
        Check.True(!old.Any(s => s.Text == "@starting-style"), "2023: @starting-style (2024) reads as plain");
        Expect(old, "@layer", TokenClasses.ControlKeyword, "2023: @layer (2022) is known");
        Check.True(!old.Any(s => s.Text == "light-dark") && !old.Any(s => s.Text == "position-anchor"), "2023: light-dark() and position-anchor read as plain");
        Expect(old, "oklch", TokenClasses.MethodName, "2023: oklch() is known");
        Expect(old, ":has", TokenClasses.TypeParameterName, "2023: :has() is known");
        Expect(Spans(StyleTokenizer.For(StyleDialect.Scss, 2020), "@function f() { @return 1; }\n"), "@function", TokenClasses.ControlKeyword, "SCSS's own @function is never a newer CSS name");
        Check.True(StyleNames.PropertyYear("-webkit-backdrop-filter") == 2024 && StyleNames.PropertyYear("nope") == -1, "a vendor prefix is dropped; an unknown name is -1");

        Check.Equal(null, CssSettings.ParseYear("latest"), "'latest' names no year");
        Check.Equal(2024, CssSettings.ParseYear("2024"), "a year");
        var read = CssSettings.Read(new Settings(("version", "2023"), ("projectVersion", "false"), ("format", "off")));
        Check.True(read is { Year: 2023, ProjectVersion: false, Format: false }, "the settings read from the plug-in's values");
        Check.True(CssSettings.Declared.Select(s => PluginSetting.VariableName(CssPlugin.PluginName, s.Key)).SequenceEqual(["CRAFT_PLUGIN_CSS_VERSION", "CRAFT_PLUGIN_CSS_PROJECTVERSION", "CRAFT_PLUGIN_CSS_FORMAT"]),
            "the variables CRAFT_PLUGIN_CSS_VERSION, _PROJECTVERSION, _FORMAT");
        Check.True(CssSettings.Declared.All(s => s.Shape is not null), "each has its shape on the settings page");

        Check.Equal(2024, StyleProjectVersion.FromQuery("> 0.5%, Baseline 2024, not dead"), "browserslist's Baseline query");
        Check.Equal(null, StyleProjectVersion.FromQuery("baseline widely available"), "no year, none");
        Check.True(StyleProjectVersion.FromPackage("{\"name\":\"x\",\"browserslist\":{\"development\":[\"baseline 2026\"],\"production\":[\"baseline 2022\"]}}"u8, out var production) && production == 2022,
            "per environment, production first");

        // A project's browserslist names the version, read off the opening thread and applied when it lands.
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File("package.json"), "{\n  \"name\": \"web\",\n  // a comment\n  \"browserslist\": [\"baseline 2023\", \"not dead\"],\n}\n");
        Directory.CreateDirectory(Path.Combine(folder.Path, "src", "app"));
        string file = Path.Combine(folder.Path, "src", "app", "a.css");
        StyleLanguage.ForgetProjectVersions();
        var css = Language(StyleLanguages.Css);
        using var session = (LineStateSession)css.Open(new PieceTableBuffer(text), new LanguageOpenContext(file, folder.Path, LanguageSettings.Default));
        using var changed = new ManualResetEventSlim();
        session.Reclassified += (_, _) => changed.Set();
        Check.True(session.Tokenizer is StyleTokenizer { Year: 2023 } || changed.Wait(TimeSpan.FromSeconds(10)), "the project's version landed");
        Check.True(session.Tokenizer is StyleTokenizer { Year: 2023, Dialect: StyleDialect.Css }, "the session reads the project's Baseline 2023");
        using var second = (LineStateSession)css.Open(new PieceTableBuffer(text), new LanguageOpenContext(file, folder.Path, LanguageSettings.Default));
        Check.True(second.Tokenizer is StyleTokenizer { Year: 2023 }, "a second file of the folder opens with it at once (read once per folder)");

        // A version set in the settings wins over the project's.
        CssSettings.Source = new Settings(("version", "2025"));
        try
        {
            using var pinned = (LineStateSession)css.Open(new PieceTableBuffer(text), new LanguageOpenContext(file, folder.Path, LanguageSettings.Default));
            Check.True(pinned.Tokenizer is StyleTokenizer { Year: 2025 }, "the setting's version first");
        }
        finally
        {
            CssSettings.Source = null;
        }

        StyleLanguage.ForgetProjectVersions();
    }

    // The spans of `text` from a fresh session of `tokenizer`, as (text, class), checked in order and not overlapping.
    private static List<(string Text, string Class)> Spans(StyleTokenizer tokenizer, string text)
    {
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        var result = new List<(string, string)>();
        int end = 0;
        foreach (var span in spans.Spans)
        {
            Check.True(span.Start >= end, $"{tokenizer.Dialect}: the spans are in order and do not overlap (at {span.Start})");
            end = span.End;
            result.Add((text.Substring(span.Start, span.Length), span.Class));
        }

        return result;
    }

    private static string Squeeze(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    // The fixture with its white space shuffled where that cannot change its meaning: indentation dropped, runs widened.
    private static string Mess(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimStart() == "$x" ? l
            : l.Contains('"', StringComparison.Ordinal) || l.Contains('\'', StringComparison.Ordinal) ? l.TrimStart()
            : l.TrimStart().Replace("; ", ";   ", StringComparison.Ordinal).Replace(": ", ":    ", StringComparison.Ordinal));
        return string.Join("\n", lines);
    }

    private static (double ColdMs, double WarmMs) ColdWarm(Action action)
    {
        long started = Stopwatch.GetTimestamp();
        action();
        double cold = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var warm = new double[5];
        for (int i = 0; i < warm.Length; i++)
        {
            started = Stopwatch.GetTimestamp();
            action();
            warm[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(warm);
        return (cold, warm[2]);
    }

    /// <summary>The plug-in's settings as a host would hold them (the others at their defaults).</summary>
    private sealed class Settings(params (string Key, string Value)[] values) : IPluginSettings
    {
        public IReadOnlyList<PluginSettingValue> All => [];

        public string Get(string plugin, string key)
        {
            foreach (var (k, v) in values)
            {
                if (k == key)
                {
                    return v;
                }
            }

            return CssSettings.Declared.Single(s => s.Key == key).Default;
        }
    }
}
