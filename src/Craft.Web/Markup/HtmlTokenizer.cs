// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Web.Script;
using Craft.Web.Style;

namespace Craft.Web.Markup;

/// <summary>
/// What an HTML lexer reads: plain HTML or an Angular template (of <see cref="AngularVersion"/>: <c>@if</c>, <c>@for</c> and
/// <c>@defer</c> from 17, <c>@let</c> from 18.1), with or without the embedded languages (<c>&lt;script&gt;</c> as
/// JavaScript, <c>&lt;style&gt;</c> as CSS), and with or without <c>style="…"</c> as CSS and <c>on*="…"</c> as JavaScript.
/// </summary>
/// <param name="Angular">An Angular template (bindings, events, interpolation, control flow blocks).</param>
/// <param name="AngularVersion">Angular's major × 100 + minor (1801 for 18.1), or <see cref="ScriptVersion.Latest"/>.</param>
/// <param name="Embedded">Colour <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> with their languages.</param>
/// <param name="AttributesAsCode">Colour <c>style="…"</c> as CSS and <c>on*="…"</c> as JavaScript.</param>
public readonly record struct HtmlOptions(bool Angular, int AngularVersion, bool Embedded, bool AttributesAsCode)
{
    /// <summary>Plain HTML with everything embedded coloured.</summary>
    public static HtmlOptions Html { get; } = new(false, ScriptVersion.Latest, true, true);

    /// <summary>An Angular template of the newest Angular.</summary>
    public static HtmlOptions AngularTemplate { get; } = new(true, ScriptVersion.Latest, true, true);
}

// The lexer's modes (what a line starts in).
internal enum MarkupMode : byte
{
    Text,
    Tag,
    CloseTag,
    AttrDouble,
    AttrSingle,
    Comment,
    Script,
    Style,
    Rcdata,
    Bogus,
    Interp,
    BlockParams,
    LetExpr,
}

// The tokens a sink receives (the formatter's and the folding's view).
internal enum MarkupTokenKind : byte
{
    TagOpen,
    TagName,
    CloseTagOpen,
    AttrName,
    AttrEquals,
    AttrValue,
    TagEnd,
    SelfClose,
    CloseTagEnd,
    Text,
    Comment,
    Doctype,
    RawText,
    InterpOpen,
    InterpBody,
    InterpClose,
    BlockKeyword,
    BlockParams,
    BlockOpen,
    BlockClose,
    Let,
}

internal interface IMarkupSink
{
    void Token(int start, int length, MarkupTokenKind kind);
}

/// <summary>
/// The HTML lexer (W-markup, the living standard) and its Angular template dialect: tags, attributes, entities, the doctype
/// and comments; raw text <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> ended by <c>&lt;/script</c> and <c>&lt;/style</c>
/// (also inside a script's string, as the browser does); RCDATA <c>&lt;textarea&gt;</c> and <c>&lt;title&gt;</c>; a script
/// by its type (none, module and JavaScript's types as JavaScript, importmap and JSON types JSON-like, text/x-template and
/// text/ng-template as HTML, others plain); <c>style="…"</c> as CSS declarations and <c>on*="…"</c> as JavaScript. The
/// embedded lexer's state rides in HTML's (interned in <see cref="States"/>), so a long <c>&lt;script&gt;</c> is incremental
/// and its spans are those of the language's own session. Angular: <c>{{ }}</c> in text and attributes, <c>[x]</c>,
/// <c>(x)</c>, <c>[(x)]</c>, <c>bind-</c>/<c>on-</c>, <c>#ref</c>, <c>*ngIf</c>/<c>*ngFor</c> microsyntax, <c>@if</c>/<c>@else
/// if</c>/<c>@else</c>, <c>@for (…; track …; let i = $index)</c>/<c>@empty</c>, <c>@switch</c>/<c>@case</c>/<c>@default</c>,
/// <c>@defer (on …; prefetch on …)</c>/<c>@placeholder</c>/<c>@loading</c>/<c>@error</c>, <c>@let x = expr;</c>, a <c>}</c>
/// in text closing the innermost block; expressions in the Angular-expression dialect (pipes, <c>?.</c>, <c>!</c>, template
/// literals). Colours through the theme's classes: tags keywords (custom elements classes), attributes properties (events
/// as events, <c>#ref</c> as locals, structural directives as control keywords), values strings, entities escapes, the
/// doctype preprocessor, blocks control keywords. Nothing is allocated per token once warm (rules 15, 17, 63).
/// </summary>
public sealed class HtmlTokenizer : LineTokenizer
{
    private static readonly ConcurrentDictionary<HtmlOptions, HtmlTokenizer> Shared = new();

    /// <summary>A lexer reading <paramref name="options"/>, its embedded states interned in <paramref name="states"/> (a new table when null).</summary>
    public HtmlTokenizer(HtmlOptions options, LineStates? states = null)
    {
        Options = options;
        States = states ?? new LineStates();
        JavaScript = ScriptTokenizer.For(ScriptDialect.JavaScript, ScriptVersion.Newest);
        Expression = ScriptTokenizer.For(ScriptDialect.AngularExpression, ScriptVersion.Newest);
        Css = StyleTokenizer.For(StyleDialect.Css);
    }

    /// <summary>What it reads.</summary>
    public HtmlOptions Options { get; }

    /// <summary>The table its states with an embedded language's are interned in.</summary>
    public LineStates States { get; }

    /// <summary>HTML's rules: <c>&lt;!-- --&gt;</c> comments, <c>&lt;&gt;</c> <c>{}</c> <c>()</c> <c>[]</c> brackets, <c>" '</c> strings.</summary>
    public static LanguageRules HtmlRules { get; } = new(null, "<!--", "-->", [new('<', '>'), new('{', '}'), new('(', ')'), new('[', ']')]) { StringDelimiters = "\"'" };

    /// <summary>The rules inside a <c>&lt;style&gt;</c>: <c>/* */</c>.</summary>
    public static LanguageRules StyleRules { get; } = new(null, "/*", "*/", [new('{', '}'), new('(', ')'), new('[', ']')]) { StringDelimiters = "\"'" };

    internal ScriptTokenizer JavaScript { get; }

    internal ScriptTokenizer Expression { get; }

    internal StyleTokenizer Css { get; }

    /// <summary>The shared lexer of <paramref name="options"/> (made once).</summary>
    public static HtmlTokenizer For(HtmlOptions options) => Shared.GetOrAdd(options, static o => new HtmlTokenizer(o));

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into) => Lex(line, lineStart, state, into, null);

    /// <inheritdoc />
    /// <remarks>X part 9: HTML's (and Angular's) tags link; its void elements open none.</remarks>
    public override bool LinksTags => true;

    /// <inheritdoc />
    public override bool IsVoidElement(ReadOnlySpan<char> name) => HtmlNames.Void.Contains(name);

    /// <inheritdoc />
    public override LanguageRules? RulesAt(ReadOnlySpan<char> line, int column, ulong state)
    {
        var mode = ModeOf(state, out _, out var script);
        return mode switch
        {
            MarkupMode.Script when script is ScriptKind.JavaScript or ScriptKind.Json => ScriptLanguage.ScriptRules,
            MarkupMode.Style => StyleRules,
            _ => null,
        };
    }

    // The mode a line in `state` starts in, and the element and script kind it carries.
    internal MarkupMode ModeOf(ulong state, out ElementKind element, out ScriptKind script)
    {
        ulong frame = LineStates.IsInterned(state) ? States.Frames(state)[0] : state;
        element = (ElementKind)((frame >> 4) & 0x7);
        script = (ScriptKind)((frame >> 7) & 0x3);
        return (MarkupMode)(frame & 0xF);
    }

    // The block depth of Angular's control flow a line in `state` starts at.
    internal int BlocksOf(ulong state)
    {
        ulong frame = LineStates.IsInterned(state) ? States.Frames(state)[0] : state;
        return (int)((frame >> 13) & 0x3F);
    }

    internal ulong Lex(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into, IMarkupSink? sink)
    {
        var lexer = new Lexer(this, line, lineStart, into, sink);
        lexer.Start(state);
        lexer.Run();
        return lexer.End();
    }

    private ref struct Lexer
    {
        private static readonly SearchValues<char> NameChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-:._");
        private static readonly SearchValues<char> AttributeStops = SearchValues.Create(" \t\f\r\n=>\"'");
        private static readonly SearchValues<char> HtmlSpecials = SearchValues.Create("<&");
        private static readonly SearchValues<char> AngularSpecials = SearchValues.Create("<&{@}");

        private readonly HtmlTokenizer _t;
        private readonly ReadOnlySpan<char> _line;
        private readonly int _lineStart;
        private readonly TokenSpanBuffer? _into;
        private readonly IMarkupSink? _sink;
        private readonly bool _angular;
        private MarkupMode _mode;
        private ElementKind _element;
        private ScriptKind _script;
        private AttributeKind _attr;
        private bool _afterEquals;
        private int _blocks;
        private int _parens;
        private bool _expectBlock;
        private ulong _embedded;

        public Lexer(HtmlTokenizer tokenizer, ReadOnlySpan<char> line, int lineStart, TokenSpanBuffer? into, IMarkupSink? sink)
        {
            _t = tokenizer;
            _line = line;
            _lineStart = lineStart;
            _into = into;
            _sink = sink;
            _angular = tokenizer.Options.Angular;
        }

        // The frame: mode 0-3, element 4-6, script kind 7-8, attribute kind 9-11, after '=' 12, blocks 13-18, block
        // parameters' parentheses 19-22, a block's '{' expected 23. With an embedded state: [frame, embedded], interned.
        public void Start(ulong state)
        {
            ulong frame = state;
            if (LineStates.IsInterned(state))
            {
                var frames = _t.States.Frames(state);
                frame = frames[0];
                _embedded = frames.Length > 1 ? frames[1] : 0;
            }

            _mode = (MarkupMode)(frame & 0xF);
            _element = (ElementKind)((frame >> 4) & 0x7);
            _script = (ScriptKind)((frame >> 7) & 0x3);
            _attr = (AttributeKind)((frame >> 9) & 0x7);
            _afterEquals = ((frame >> 12) & 1) != 0;
            _blocks = (int)((frame >> 13) & 0x3F);
            _parens = (int)((frame >> 19) & 0xF);
            _expectBlock = ((frame >> 23) & 1) != 0;
        }

        public readonly ulong End()
        {
            ulong frame = (ulong)_mode | ((ulong)_element << 4) | ((ulong)_script << 7) | ((ulong)_attr << 9) | (_afterEquals ? 1UL << 12 : 0)
                | ((ulong)(uint)_blocks << 13) | ((ulong)(uint)_parens << 19) | (_expectBlock ? 1UL << 23 : 0);
            if (_mode is not (MarkupMode.Script or MarkupMode.Style or MarkupMode.AttrDouble or MarkupMode.AttrSingle or MarkupMode.Interp or MarkupMode.LetExpr))
            {
                return frame;
            }

            Span<ulong> frames = [frame, _embedded];
            return _t.States.Intern(frames);
        }

        public void Run()
        {
            int i = 0;
            var line = _line;
            if (line.Length == 0)
            {
                // An empty line inside an embedded language goes through it too (its session would read it).
                if (_mode is MarkupMode.Script or MarkupMode.Style)
                {
                    Raw(0);
                }

                return;
            }

            while (i < line.Length)
            {
                i = _mode switch
                {
                    MarkupMode.Tag => Tag(i),
                    MarkupMode.CloseTag => CloseTag(i),
                    MarkupMode.AttrDouble => AttrValue(i, i, '"'),
                    MarkupMode.AttrSingle => AttrValue(i, i, '\''),
                    MarkupMode.Comment => Comment(SkipBlank(i), continued: true),
                    MarkupMode.Script or MarkupMode.Style => Raw(i),
                    MarkupMode.Rcdata => Rcdata(i),
                    MarkupMode.Bogus => Bogus(i, i),
                    MarkupMode.Interp => InterpBody(i),
                    MarkupMode.BlockParams => Params(i, i),
                    MarkupMode.LetExpr => LetBody(i),
                    _ => Text(i),
                };
            }
        }

        // ---- spans and tokens ----

        private readonly void Span(int start, int length, string tokenClass)
        {
            if (length > 0)
            {
                _into?.Add(_lineStart + start, length, tokenClass);
            }
        }

        private readonly void Token(int start, int length, MarkupTokenKind kind)
        {
            if (length > 0)
            {
                _sink?.Token(_lineStart + start, length, kind);
            }
        }

        private readonly void Emit(int start, int length, string tokenClass, MarkupTokenKind kind)
        {
            Span(start, length, tokenClass);
            Token(start, length, kind);
        }

        private readonly char At(int i) => i < _line.Length ? _line[i] : '\0';

        private readonly int SkipBlank(int i)
        {
            while (i < _line.Length && _line[i] is ' ' or '\t' or '\f')
            {
                i++;
            }

            return i;
        }

        private readonly int NameEnd(int i)
        {
            int run = _line[i..].IndexOfAnyExcept(NameChars);
            return run < 0 ? _line.Length : i + run;
        }

        // ---- text ----

        private int Text(int i)
        {
            var line = _line;
            char c = line[i];
            if (_expectBlock)
            {
                if (c is ' ' or '\t' or '\f')
                {
                    return SkipBlank(i);
                }

                _expectBlock = false;
                if (c == '{')
                {
                    Emit(i, 1, TokenClasses.Punctuation, MarkupTokenKind.BlockOpen);
                    _blocks = Math.Min(_blocks + 1, 63);
                    return i + 1;
                }
            }

            char next = At(i + 1);
            switch (c)
            {
                case '<' when char.IsAsciiLetter(next):
                    return StartTag(i);
                case '<' when next == '/' && char.IsAsciiLetter(At(i + 2)):
                    return EndTag(i);
                case '<' when next == '!' && line[i..].StartsWith("<!--"):
                    return Comment(i, continued: false);
                case '<' when next == '!' && line[i..].StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase):
                    Span(i, 9, TokenClasses.PreprocessorKeyword);
                    return Bogus(i, i + 9);
                case '<' when next is '!' or '?':
                    return Bogus(i, i);
                case '&':
                    return Entity(i);
                case '{' when _angular && next == '{':
                    Emit(i, 2, TokenClasses.Punctuation, MarkupTokenKind.InterpOpen);
                    _mode = MarkupMode.Interp;
                    _embedded = 0;
                    return InterpBody(i + 2);
                case '@' when _angular:
                    return Block(i);
                case '}' when _angular && _blocks > 0:
                    Emit(i, 1, TokenClasses.Punctuation, MarkupTokenKind.BlockClose);
                    _blocks--;
                    return i + 1;
            }

            return TextRun(i);
        }

        // Text from i to the next character that may start something else; trimmed for the sink, no colour.
        private readonly int TextRun(int i)
        {
            var line = _line;
            int stop = line[(i + 1)..].IndexOfAny(_angular ? AngularSpecials : HtmlSpecials);
            int end = stop < 0 ? line.Length : i + 1 + stop;
            if (_sink is not null)
            {
                var text = line[i..end];
                int lead = text.Length - text.TrimStart().Length;
                Token(i + lead, text.Trim().Length, MarkupTokenKind.Text);
            }

            return end;
        }

        private readonly int Entity(int i)
        {
            int end = EntityEnd(i);
            if (end < 0)
            {
                return TextRun(i);
            }

            Emit(i, end - i, TokenClasses.StringEscape, MarkupTokenKind.Text);
            return end;
        }

        // &name; &#123; &#x1F; (to 32 characters), or -1.
        private readonly int EntityEnd(int i)
        {
            var line = _line;
            int j = i + 1;
            if (At(j) == '#')
            {
                j++;
                if (At(j) is 'x' or 'X')
                {
                    j++;
                }
            }

            int start = j;
            while (j < line.Length && j - i < 32 && char.IsAsciiLetterOrDigit(line[j]))
            {
                j++;
            }

            return j > start && At(j) == ';' ? j + 1 : -1;
        }

        // ---- tags ----

        private int StartTag(int i)
        {
            Emit(i, 1, TokenClasses.Punctuation, MarkupTokenKind.TagOpen);
            int end = NameEnd(i + 1);
            var name = _line[(i + 1)..end];
            Emit(i + 1, end - i - 1, name.Contains('-') ? TokenClasses.ClassName : TokenClasses.Keyword, MarkupTokenKind.TagName);
            _mode = MarkupMode.Tag;
            _element = HtmlNames.KindOf(name);
            _script = ScriptKind.JavaScript;
            _attr = AttributeKind.Plain;
            _afterEquals = false;
            return end;
        }

        private int EndTag(int i)
        {
            Emit(i, 2, TokenClasses.Punctuation, MarkupTokenKind.CloseTagOpen);
            int end = NameEnd(i + 2);
            var name = _line[(i + 2)..end];
            Emit(i + 2, end - i - 2, name.Contains('-') ? TokenClasses.ClassName : TokenClasses.Keyword, MarkupTokenKind.TagName);
            _mode = MarkupMode.CloseTag;
            return end;
        }

        private int CloseTag(int i)
        {
            char c = _line[i];
            if (c == '>')
            {
                Emit(i, 1, TokenClasses.Punctuation, MarkupTokenKind.CloseTagEnd);
                _mode = MarkupMode.Text;
                return i + 1;
            }

            return c is ' ' or '\t' or '\f' ? SkipBlank(i) : i + 1;
        }

        private int Tag(int i)
        {
            var line = _line;
            char c = line[i];
            switch (c)
            {
                case ' ' or '\t' or '\f':
                    return SkipBlank(i);
                case '/' when At(i + 1) == '>':
                    Emit(i, 2, TokenClasses.Punctuation, MarkupTokenKind.SelfClose);
                    EnterContent(selfClosed: true);
                    return i + 2;
                case '>':
                    Emit(i, 1, TokenClasses.Punctuation, MarkupTokenKind.TagEnd);
                    EnterContent(selfClosed: false);
                    return i + 1;
                case '=':
                    Emit(i, 1, TokenClasses.Operator, MarkupTokenKind.AttrEquals);
                    _afterEquals = true;
                    return i + 1;
                case '"' or '\'' when _afterEquals:
                    return AttrValue(i, i + 1, c);
            }

            if (_afterEquals)
            {
                return UnquotedValue(i);
            }

            return AttributeName(i);
        }

        // After a start tag's '>' (or a '/>': raw text elements ignore it, as the standard says; other elements are closed).
        private void EnterContent(bool selfClosed)
        {
            _afterEquals = false;
            _attr = AttributeKind.Plain;
            _embedded = 0;
            _mode = _element switch
            {
                ElementKind.Script => MarkupMode.Script,
                ElementKind.Style => MarkupMode.Style,
                ElementKind.Textarea or ElementKind.Title => MarkupMode.Rcdata,
                _ => MarkupMode.Text,
            };
            if (selfClosed && _mode == MarkupMode.Text)
            {
                _element = ElementKind.Normal;
            }
        }

        private int AttributeName(int i)
        {
            var line = _line;
            int end = i + 1;
            while (end < line.Length)
            {
                int stop = line[end..].IndexOfAny(AttributeStops);
                end = stop < 0 ? line.Length : end + stop;
                if (end < line.Length && line[end] == '/' && At(end + 1) != '>')
                {
                    end++;
                    continue;
                }

                break;
            }

            if (end > i + 1 && line[end - 1] == '/' && At(end) == '>')
            {
                end--;
            }

            var name = line[i..end];
            Token(i, end - i, MarkupTokenKind.AttrName);
            _attr = _angular ? AngularAttribute(i, name) : HtmlAttribute(i, name);
            _afterEquals = false;
            return Math.Max(end, i + 1);
        }

        private readonly AttributeKind HtmlAttribute(int i, ReadOnlySpan<char> name)
        {
            Span(i, name.Length, TokenClasses.PropertyName);
            bool code = _t.Options.AttributesAsCode;
            if (code && name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                return AttributeKind.Style;
            }

            if (code && name.Length > 2 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && IsLetters(name[2..]))
            {
                return AttributeKind.Event;
            }

            return _element == ElementKind.Script && name.Equals("type", StringComparison.OrdinalIgnoreCase) ? AttributeKind.Type : AttributeKind.Plain;
        }

        private readonly AttributeKind AngularAttribute(int i, ReadOnlySpan<char> name)
        {
            if (name.StartsWith("[(") && name.EndsWith(")]") && name.Length > 4)
            {
                Span(i, 2, TokenClasses.Punctuation);
                Span(i + 2, name.Length - 4, TokenClasses.PropertyName);
                Span(i + name.Length - 2, 2, TokenClasses.Punctuation);
                return AttributeKind.Binding;
            }

            if (name[0] == '[' && name.EndsWith("]") && name.Length > 2)
            {
                Span(i, 1, TokenClasses.Punctuation);
                Span(i + 1, name.Length - 2, TokenClasses.PropertyName);
                Span(i + name.Length - 1, 1, TokenClasses.Punctuation);
                return AttributeKind.Binding;
            }

            if (name[0] == '(' && name.EndsWith(")") && name.Length > 2)
            {
                Span(i, 1, TokenClasses.Punctuation);
                Span(i + 1, name.Length - 2, TokenClasses.EventName);
                Span(i + name.Length - 1, 1, TokenClasses.Punctuation);
                return AttributeKind.Statement;
            }

            if (name[0] == '*')
            {
                Span(i, name.Length, TokenClasses.ControlKeyword);
                return AttributeKind.Micro;
            }

            if (name[0] == '#' || name.StartsWith("ref-") || name.StartsWith("let-"))
            {
                Span(i, name.Length, TokenClasses.LocalName);
                return AttributeKind.Plain;
            }

            if (name.StartsWith("bind-") || name.StartsWith("bindon-"))
            {
                Span(i, name.Length, TokenClasses.PropertyName);
                return AttributeKind.Binding;
            }

            if (name.StartsWith("on-"))
            {
                Span(i, name.Length, TokenClasses.EventName);
                return AttributeKind.Statement;
            }

            var kind = HtmlAttribute(i, name);
            return kind == AttributeKind.Plain ? AttributeKind.Interpolated : kind;
        }

        private static bool IsLetters(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
            {
                if (!char.IsAsciiLetter(c))
                {
                    return false;
                }
            }

            return true;
        }

        // A quoted value from `from` (its quote, or the line's start when it continues), its text from i.
        private int AttrValue(int from, int i, char quote)
        {
            var line = _line;
            int close = line[i..].IndexOf(quote);
            int end = close < 0 ? line.Length : i + close;
            bool code = _attr is not (AttributeKind.Plain or AttributeKind.Type);
            if (!code)
            {
                Span(from, (close < 0 ? line.Length : end + 1) - from, TokenClasses.String);   // a plain value: one string
            }
            else
            {
                if (from < i)
                {
                    Span(from, 1, TokenClasses.String);
                }

                Content(i, end);
            }

            if (close < 0)
            {
                Token(from, line.Length - from, MarkupTokenKind.AttrValue);
                _mode = quote == '"' ? MarkupMode.AttrDouble : MarkupMode.AttrSingle;
                return line.Length;
            }

            if (code)
            {
                Span(end, 1, TokenClasses.String);
            }

            Token(from, end + 1 - from, MarkupTokenKind.AttrValue);
            if (_attr == AttributeKind.Type)
            {
                _script = HtmlNames.ScriptKindOf(line[i..end]);
            }

            _mode = MarkupMode.Tag;
            _attr = AttributeKind.Plain;
            _afterEquals = false;
            _embedded = 0;
            return end + 1;
        }

        private int UnquotedValue(int i)
        {
            var line = _line;
            int end = i;
            while (end < line.Length && line[end] is not (' ' or '\t' or '\f' or '>'))
            {
                end++;
            }

            _embedded = 0;
            Content(i, end);
            Token(i, end - i, MarkupTokenKind.AttrValue);
            if (_attr == AttributeKind.Type)
            {
                _script = HtmlNames.ScriptKindOf(line[i..end]);
            }

            _attr = AttributeKind.Plain;
            _afterEquals = false;
            _embedded = 0;
            return Math.Max(end, i + 1);
        }

        // An attribute's text by its kind: CSS, JavaScript, an Angular expression, text with {{ }}, or a string.
        private void Content(int i, int end)
        {
            var text = _line[i..end];
            switch (_attr)
            {
                case AttributeKind.Style:
                    _embedded = _t.Css.Tokenize(text, _lineStart + i, _embedded, _into);
                    break;
                case AttributeKind.Event:
                    _embedded = _t.JavaScript.Tokenize(text, _lineStart + i, _embedded, _into);
                    break;
                case AttributeKind.Binding or AttributeKind.Statement or AttributeKind.Micro:
                    _embedded = _t.Expression.Tokenize(text, _lineStart + i, _embedded, _into);
                    break;
                case AttributeKind.Interpolated:
                    Interpolated(i, end);
                    break;
                default:
                    Span(i, end - i, TokenClasses.String);
                    break;
            }
        }

        // Text with {{ expressions }} (an Angular attribute's value), on its line.
        private readonly void Interpolated(int i, int end)
        {
            var line = _line;
            int run = i;
            int j = i;
            while (j < end)
            {
                int open = line[j..end].IndexOf("{{", StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }

                j += open;
                Span(run, j - run, TokenClasses.String);
                Span(j, 2, TokenClasses.Punctuation);
                int close = line[(j + 2)..end].IndexOf("}}", StringComparison.Ordinal);
                int stop = close < 0 ? end : j + 2 + close;
                _t.Expression.Tokenize(line[(j + 2)..stop], _lineStart + j + 2, _t.Expression.Initial, _into);
                if (close < 0)
                {
                    return;
                }

                Span(stop, 2, TokenClasses.Punctuation);
                j = stop + 2;
                run = j;
            }

            Span(run, end - run, TokenClasses.String);
        }

        // ---- raw text, RCDATA, comments, the doctype ----

        private int Raw(int i)
        {
            bool script = _mode == MarkupMode.Script;
            int end = FindEndTag(i, script ? "script" : "style");
            int stop = end < 0 ? _line.Length : end;
            if (_t.Options.Embedded)
            {
                var text = _line[i..stop];
                if (!script)
                {
                    _embedded = _t.Css.Tokenize(text, _lineStart + i, _embedded, _into);
                }
                else if (_script is ScriptKind.JavaScript or ScriptKind.Json)
                {
                    _embedded = _t.JavaScript.Tokenize(text, _lineStart + i, _embedded, _into);
                }
                else if (_script == ScriptKind.Html)
                {
                    _embedded = _t.Lex(text, _lineStart + i, _embedded, _into, null);
                }
            }

            Token(i, stop - i, MarkupTokenKind.RawText);
            if (end < 0)
            {
                return _line.Length;
            }

            _mode = MarkupMode.Text;
            _element = ElementKind.Normal;
            _embedded = 0;
            return end;
        }

        private int Rcdata(int i)
        {
            int end = FindEndTag(i, _element == ElementKind.Title ? "title" : "textarea");
            int stop = end < 0 ? _line.Length : end;
            for (int j = i; j < stop; j++)
            {
                if (_line[j] == '&' && EntityEnd(j) is var entity and > 0 && entity <= stop)
                {
                    Span(j, entity - j, TokenClasses.StringEscape);
                    j = entity - 1;
                }
            }

            Token(i, stop - i, MarkupTokenKind.RawText);
            if (end < 0)
            {
                return _line.Length;
            }

            _mode = MarkupMode.Text;
            _element = ElementKind.Normal;
            return end;
        }

        // The '<' of "</name" followed by white space, '/', '>' or the line's end (any case), or -1.
        private readonly int FindEndTag(int i, string name)
        {
            var line = _line;
            int j = i;
            while (j < line.Length)
            {
                int open = line[j..].IndexOf("</", StringComparison.Ordinal);
                if (open < 0)
                {
                    return -1;
                }

                int at = j + open;
                int after = at + 2 + name.Length;
                if (after <= line.Length && line.Slice(at + 2, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase)
                    && (after == line.Length || line[after] is ' ' or '\t' or '\f' or '/' or '>'))
                {
                    return at;
                }

                j = at + 2;
            }

            return -1;
        }

        private int Comment(int i, bool continued)
        {
            var line = _line;
            if (i >= line.Length)
            {
                _mode = MarkupMode.Comment;
                return line.Length;
            }

            int close = line[(continued ? i : i + 4)..].IndexOf("-->", StringComparison.Ordinal);
            int end = close < 0 ? line.Length : (continued ? i : i + 4) + close + 3;
            Emit(i, end - i, TokenClasses.Comment, MarkupTokenKind.Comment);
            _mode = close < 0 ? MarkupMode.Comment : MarkupMode.Text;
            return end;
        }

        // <!DOCTYPE …>, <!…>, <?…>: to the '>'.
        private int Bogus(int from, int i)
        {
            var line = _line;
            int close = line[i..].IndexOf('>');
            int end = close < 0 ? line.Length : i + close + 1;
            bool doctype = i > from;
            Span(i, end - i, doctype ? TokenClasses.PreprocessorText : TokenClasses.Comment);
            Token(from, end - from, MarkupTokenKind.Doctype);
            _mode = close < 0 ? MarkupMode.Bogus : MarkupMode.Text;
            return end;
        }

        // ---- Angular ----

        private int InterpBody(int i)
        {
            var line = _line;
            int close = CloseOf(i, "}}");
            int stop = close < 0 ? line.Length : close;
            _embedded = _t.Expression.Tokenize(line[i..stop], _lineStart + i, _embedded, _into);
            Token(i, stop - i, MarkupTokenKind.InterpBody);
            if (close < 0)
            {
                return line.Length;
            }

            Emit(close, 2, TokenClasses.Punctuation, MarkupTokenKind.InterpClose);
            _mode = MarkupMode.Text;
            _embedded = 0;
            return close + 2;
        }

        // The first `what` from i outside a quoted string, or -1.
        private readonly int CloseOf(int i, string what)
        {
            var line = _line;
            char quote = '\0';
            for (int j = i; j < line.Length; j++)
            {
                char c = line[j];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        j++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (c is '"' or '\'' or '`')
                {
                    quote = c;
                }
                else if (line[j..].StartsWith(what))
                {
                    return j;
                }
            }

            return -1;
        }

        private int Block(int i)
        {
            var line = _line;
            int end = i + 1;
            while (end < line.Length && char.IsAsciiLetter(line[end]))
            {
                end++;
            }

            var name = line[(i + 1)..end];
            int version = _t.Options.AngularVersion;
            bool known = HtmlNames.Block.Contains(name) && version >= 1700 && (!name.SequenceEqual("let") || version >= 1801);
            if (!known)
            {
                return TextRun(i);   // an '@' that starts no block (before 17, @let before 18.1): text
            }

            Emit(i, end - i, TokenClasses.ControlKeyword, MarkupTokenKind.BlockKeyword);
            if (name.SequenceEqual("let"))
            {
                return LetDeclaration(end);
            }

            if (name.SequenceEqual("else"))
            {
                int word = SkipBlank(end);
                if (line[word..].StartsWith("if") && !char.IsAsciiLetterOrDigit(At(word + 2)))
                {
                    Emit(word, 2, TokenClasses.ControlKeyword, MarkupTokenKind.BlockKeyword);
                    end = word + 2;
                }
            }

            int open = SkipBlank(end);
            if (At(open) == '(')
            {
                Span(open, 1, TokenClasses.Punctuation);
                _mode = MarkupMode.BlockParams;
                _parens = 0;
                return Params(open, open + 1);
            }

            _expectBlock = true;
            return end;
        }

        // A block's parameters to their ')': each ';'-separated part's leading words (track, let, on and the triggers, …) as
        // keywords, the rest an expression.
        private int Params(int from, int i)
        {
            var line = _line;
            int depth = _parens;
            int part = i;
            for (int j = i; j < line.Length; j++)
            {
                char c = line[j];
                if (c is '"' or '\'' or '`')
                {
                    int close = line[(j + 1)..].IndexOf(c);
                    j = close < 0 ? line.Length - 1 : j + 1 + close;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && depth > 0)
                {
                    depth--;
                }
                else if (c == ')')
                {
                    Parameter(part, j);
                    Span(j, 1, TokenClasses.Punctuation);
                    Token(from, j + 1 - from, MarkupTokenKind.BlockParams);
                    _mode = MarkupMode.Text;
                    _parens = 0;
                    _expectBlock = true;
                    return j + 1;
                }
                else if (c == ';' && depth == 0)
                {
                    Parameter(part, j);
                    Span(j, 1, TokenClasses.Punctuation);
                    part = j + 1;
                }
            }

            Parameter(part, line.Length);
            Token(from, line.Length - from, MarkupTokenKind.BlockParams);
            _parens = Math.Min(depth, 15);
            return line.Length;
        }

        private readonly void Parameter(int start, int end)
        {
            var line = _line;
            int at = start;
            bool trigger = false;
            while (true)
            {
                at = SkipBlank(at);
                int wordEnd = at;
                while (wordEnd < end && char.IsAsciiLetter(line[wordEnd]))
                {
                    wordEnd++;
                }

                if (wordEnd == at)
                {
                    break;
                }

                var word = line[at..wordEnd];
                if (HtmlNames.Parameter.Contains(word) || (trigger && HtmlNames.Trigger.Contains(word)))
                {
                    Span(at, wordEnd - at, TokenClasses.Keyword);
                    trigger = word.SequenceEqual("on");
                    at = wordEnd;
                    continue;
                }

                break;
            }

            if (at < end)
            {
                _t.Expression.Tokenize(line[at..end], _lineStart + at, _t.Expression.Initial, _into);
            }
        }

        // @let name = expression;
        private int LetDeclaration(int i)
        {
            var line = _line;
            int name = SkipBlank(i);
            int nameEnd = name;
            while (nameEnd < line.Length && (char.IsAsciiLetterOrDigit(line[nameEnd]) || line[nameEnd] is '_' or '$'))
            {
                nameEnd++;
            }

            Span(name, nameEnd - name, TokenClasses.LocalName);
            int equals = SkipBlank(nameEnd);
            if (At(equals) != '=')
            {
                Token(i, nameEnd - i, MarkupTokenKind.Let);
                return Math.Max(nameEnd, i);
            }

            Span(equals, 1, TokenClasses.Operator);
            Token(i, equals + 1 - i, MarkupTokenKind.Let);
            _mode = MarkupMode.LetExpr;
            _embedded = 0;
            return LetBody(equals + 1);
        }

        private int LetBody(int i)
        {
            var line = _line;
            int close = CloseOf(i, ";");
            int stop = close < 0 ? line.Length : close;
            _embedded = _t.Expression.Tokenize(line[i..stop], _lineStart + i, _embedded, _into);
            if (close < 0)
            {
                Token(i, stop - i, MarkupTokenKind.Let);
                return line.Length;
            }

            Span(close, 1, TokenClasses.Punctuation);
            Token(i, close + 1 - i, MarkupTokenKind.Let);
            _mode = MarkupMode.Text;
            _embedded = 0;
            return close + 1;
        }
    }
}
