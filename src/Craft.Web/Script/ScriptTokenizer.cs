// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Concurrent;
using Craft.Abstractions;

namespace Craft.Web.Script;

// The kinds of token the lexer reports to a sink (the formatter and the folding read structure, not colours).
internal enum ScriptTokenKind : byte
{
    Word,
    Keyword,
    Number,
    String,
    StringUnterminated,
    Regex,
    RegexUnterminated,
    PrivateName,
    Decorator,
    Directive,
    LineComment,
    BlockComment,
    Punct,
    Operator,
    TemplateStart,
    TemplateString,
    TemplateExprOpen,
    TemplateExprClose,
    TemplateEnd,
    JsxTagOpen,
    JsxCloseTagOpen,
    JsxTagName,
    JsxAttrName,
    JsxAttrEquals,
    JsxAttrString,
    JsxTagEnd,
    JsxSelfClose,
    JsxCloseTagEnd,
    JsxExprOpen,
    JsxExprClose,
    JsxText,
}

// A frame's kind: code at the top level, code inside a template's ${ } or a JSX { }, a template's text, a JSX tag, a JSX
// element's children.
internal enum FrameKind : byte
{
    Code,
    TemplateExpr,
    JsxExpr,
    Template,
    JsxOpenTag,
    JsxChildren,
    JsxCloseTag,
}

// What continues across a line break inside a frame.
internal enum Sub : byte
{
    None,
    Block,      // a /* */ comment
    Doc,        // a /** */ comment (JSDoc)
    Double,     // a "..." string continued by a '\' (in a JSX tag: an attribute's string, which may span lines)
    Single,     // a '...' string, likewise
}

// Receives the tokens of a line (the formatter's and the folding's view): one token per string, regex, comment part or
// template text run, with the frame it was read in.
internal interface IScriptTokenSink
{
    void Token(int start, int length, ScriptTokenKind kind, FrameKind frame);
}

/// <summary>
/// The JavaScript, JSX, TypeScript and TSX lexer (W-script), and the Angular-expression dialect W-markup builds on: one
/// state per line, a stack of frames (template literals inside <c>${ }</c>, JSX elements and their <c>{ }</c>, to any depth
/// across lines) interned in its <see cref="LineStates"/> table past one frame. A <c>/</c> is a regular expression or a
/// division by the token before it (carried in the state across lines); a <c>{</c> a block or an object likewise; a
/// <c>&lt;</c> in JSX an element where an expression may start. Keywords from a frozen table by span, contextual words
/// (<c>using</c>, <c>accessor</c>, <c>satisfies</c>, <c>type</c>, ...) by what is around them, gated by the
/// <see cref="ScriptVersion"/>. Colours through the theme's classes: decorators as preprocessor keywords, a regular
/// expression's body as a string with its escapes and group syntax as escapes, <c>${</c> <c>}</c> as punctuation, private
/// names as fields, interface, enum and type names by their keyword, <c>/// &lt;reference&gt;</c> as preprocessor text,
/// JSDoc as documentation, JSX tags as keywords, components as classes, attributes as properties. Nothing is allocated per
/// token or per line once warm (rules 15, 17, 63); one instance serves every session, on any thread.
/// </summary>
public sealed class ScriptTokenizer : LineTokenizer
{
    private static readonly ConcurrentDictionary<(ScriptDialect, ScriptVersion, int, int), ScriptTokenizer> Shared = new();
    private readonly int _depth;

    /// <summary>A lexer of <paramref name="dialect"/> reading <paramref name="version"/>, its deep states in <paramref name="states"/> (a new table of the defaults when null).</summary>
    public ScriptTokenizer(ScriptDialect dialect, ScriptVersion version, LineStates? states = null)
    {
        Dialect = dialect;
        Version = version;
        States = states ?? new LineStates();
        _depth = Math.Clamp(States.MaxDepth, 2, 127) + 1;   // one frame past the table's depth, so a deeper stack reaches it and is counted; stack-allocated, bounded (rule 45)
        Jsx = dialect is ScriptDialect.Jsx or ScriptDialect.Tsx;
        TypeScript = dialect is ScriptDialect.TypeScript or ScriptDialect.Tsx;
        Angular = dialect == ScriptDialect.AngularExpression;
    }

    /// <summary>The dialect it reads.</summary>
    public ScriptDialect Dialect { get; }

    /// <summary>The version it reads (a newer word is a plain name).</summary>
    public ScriptVersion Version { get; }

    /// <summary>The table its states past one frame are interned in.</summary>
    public LineStates States { get; }

    /// <summary>The editing rules inside JSX (between tags and in a tag): no line comment, <c>{/* */}</c> as the block comment.</summary>
    public static LanguageRules JsxRules { get; } = new(null, "{/*", "*/}", [new('{', '}'), new('<', '>')]) { StringDelimiters = "\"'" };

    internal bool Jsx { get; }

    internal bool TypeScript { get; }

    internal bool Angular { get; }

    /// <summary>
    /// The shared lexer of <paramref name="dialect"/> and <paramref name="version"/> with a table of
    /// <paramref name="capacity"/> stacks of <paramref name="maxDepth"/> frames: one per combination, made once.
    /// </summary>
    public static ScriptTokenizer For(ScriptDialect dialect, ScriptVersion version, int capacity = LineStates.DefaultCapacity, int maxDepth = LineStates.DefaultMaxDepth) =>
        Shared.GetOrAdd((dialect, version, Math.Max(1, capacity), Math.Clamp(maxDepth, 2, 127)),
            static key => new ScriptTokenizer(key.Item1, key.Item2, new LineStates(key.Item3, key.Item4)));

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into) => Lex(line, lineStart, state, into, null);

    /// <inheritdoc />
    public override LanguageRules? RulesAt(ReadOnlySpan<char> line, int column, ulong state)
    {
        var kind = TopOf(state, out _);
        return kind is FrameKind.JsxChildren or FrameKind.JsxOpenTag or FrameKind.JsxCloseTag ? JsxRules : null;
    }

    // The innermost frame's kind and what continues in it, of a line's start state.
    internal FrameKind TopOf(ulong state, out Sub sub)
    {
        ulong top = state;
        if (LineStates.IsInterned(state))
        {
            var frames = States.Frames(state);
            top = frames[^1];
        }

        sub = Frames.SubOf(top);
        return Frames.KindOf(top);
    }

    internal ulong Lex(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into, IScriptTokenSink? sink)
    {
        Span<ulong> stack = stackalloc ulong[_depth];
        var lexer = new Lexer(this, line, lineStart, into, sink, stack);
        lexer.Start(state);
        lexer.Run();
        return lexer.End();
    }

    // A frame's 64 bits: kind 0-2, sub 3-5, prev 6-10, brace depth 11-18, brace kinds 19-42 (1: an object's), paren depth
    // 43-47, paren kinds 48-59 (1: a control's); bit 63 stays clear (the interned bit).
    internal static class Frames
    {
        public const int BraceKindBits = 24;
        public const int ParenKindBits = 12;
        public const int MaxBraces = 255;
        public const int MaxParens = 31;

        public static FrameKind KindOf(ulong frame) => (FrameKind)(frame & 0x7);

        public static Sub SubOf(ulong frame) => (Sub)((frame >> 3) & 0x7);

        public static ulong Encode(FrameKind kind, Sub sub, Prev prev, int braces, uint braceKinds, int parens, uint parenKinds) =>
            (ulong)kind
            | ((ulong)sub << 3)
            | ((ulong)prev << 6)
            | ((ulong)(uint)braces << 11)
            | ((ulong)(braceKinds & ((1u << BraceKindBits) - 1)) << 19)
            | ((ulong)(uint)parens << 43)
            | ((ulong)(parenKinds & ((1u << ParenKindBits) - 1)) << 48);

        public static void Decode(ulong frame, out FrameKind kind, out Sub sub, out Prev prev, out int braces, out uint braceKinds, out int parens, out uint parenKinds)
        {
            kind = (FrameKind)(frame & 0x7);
            sub = (Sub)((frame >> 3) & 0x7);
            prev = (Prev)((frame >> 6) & 0x1F);
            braces = (int)((frame >> 11) & 0xFF);
            braceKinds = (uint)((frame >> 19) & ((1u << BraceKindBits) - 1));
            parens = (int)((frame >> 43) & 0x1F);
            parenKinds = (uint)((frame >> 48) & ((1u << ParenKindBits) - 1));
        }
    }

    // One line's lexing: the frames below the current one on a stack-allocated span, the current one in fields.
    private ref struct Lexer
    {
        private static readonly SearchValues<char> NameChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_$");
        private static readonly SearchValues<char> TagNameChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_$-:.");
        private static readonly SearchValues<char> TemplateSpecials = SearchValues.Create("\\`$");
        private static readonly SearchValues<char> ChildrenSpecials = SearchValues.Create("{<&");

        private readonly ScriptTokenizer _t;
        private readonly ReadOnlySpan<char> _line;
        private readonly int _lineStart;
        private readonly TokenSpanBuffer? _into;
        private readonly IScriptTokenSink? _sink;
        private readonly Span<ulong> _stack;
        private int _depth;
        private bool _overflow;
        private FrameKind _kind;
        private Sub _sub;
        private Prev _prev;
        private int _braces;
        private uint _braceKinds;
        private int _parens;
        private uint _parenKinds;

        public Lexer(ScriptTokenizer tokenizer, ReadOnlySpan<char> line, int lineStart, TokenSpanBuffer? into, IScriptTokenSink? sink, Span<ulong> stack)
        {
            _t = tokenizer;
            _line = line;
            _lineStart = lineStart;
            _into = into;
            _sink = sink;
            _stack = stack;
        }

        private readonly bool InCode => _kind is FrameKind.Code or FrameKind.TemplateExpr or FrameKind.JsxExpr;

        public void Start(ulong state)
        {
            ulong top = state;
            if (LineStates.IsInterned(state))
            {
                var frames = _t.States.Frames(state);
                int below = Math.Min(frames.Length - 1, _stack.Length - 1);
                frames[..below].CopyTo(_stack);
                _depth = below;
                top = frames[^1];
            }

            Frames.Decode(top, out _kind, out _sub, out _prev, out _braces, out _braceKinds, out _parens, out _parenKinds);
        }

        public readonly ulong End()
        {
            if (_overflow)
            {
                return _t.States.Intern(_stack);   // deeper than the table holds: its fallback, counted in its Overflows (rule 77)
            }

            ulong top = Frames.Encode(_kind, _sub, _prev, _braces, _braceKinds, _parens, _parenKinds);
            if (_depth == 0)
            {
                return top;
            }

            _stack[_depth] = top;
            return _t.States.Intern(_stack[..(_depth + 1)]);
        }

        public void Run()
        {
            int i = 0;
            int length = _line.Length;
            if (_lineStart == 0 && _depth == 0 && _kind == FrameKind.Code && _sub == Sub.None && _line.StartsWith("#!"))
            {
                Emit(0, length, TokenClasses.PreprocessorText, ScriptTokenKind.Directive);   // a hashbang (ES2023)
                return;
            }

            while (i < length)
            {
                if (_sub != Sub.None)
                {
                    i = Continue(i);
                    continue;
                }

                i = _kind switch
                {
                    FrameKind.Template => TemplateText(i),
                    FrameKind.JsxOpenTag or FrameKind.JsxCloseTag => Tag(i),
                    FrameKind.JsxChildren => Children(i),
                    _ => Code(i),
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

        private readonly void Token(int start, int length, ScriptTokenKind kind)
        {
            if (length > 0)
            {
                _sink?.Token(_lineStart + start, length, kind, _kind);
            }
        }

        private readonly void Emit(int start, int length, string? tokenClass, ScriptTokenKind kind)
        {
            if (tokenClass is not null)
            {
                Span(start, length, tokenClass);
            }

            Token(start, length, kind);
        }

        // ---- frames ----

        private bool Push(FrameKind kind, Prev prev)
        {
            if (_depth >= _stack.Length - 1)
            {
                _overflow = true;   // deeper than the table holds: the line ends in the fallback state (rule 77)
                return false;
            }

            _stack[_depth++] = Frames.Encode(_kind, _sub, _prev, _braces, _braceKinds, _parens, _parenKinds);
            _kind = kind;
            _sub = Sub.None;
            _prev = prev;
            _braces = 0;
            _braceKinds = 0;
            _parens = 0;
            _parenKinds = 0;
            return true;
        }

        private void Pop()
        {
            if (_depth == 0)
            {
                _kind = FrameKind.Code;
                _sub = Sub.None;
                _prev = Prev.Value;
                return;
            }

            Frames.Decode(_stack[--_depth], out _kind, out _sub, out _prev, out _braces, out _braceKinds, out _parens, out _parenKinds);
        }

        // ---- what continues from the line above: a comment, a string ----

        private int Continue(int i)
        {
            if (_sub is Sub.Block or Sub.Doc)
            {
                int first = SkipBlank(i);
                if (first >= _line.Length)
                {
                    return _line.Length;
                }

                int close = _line[first..].IndexOf("*/", StringComparison.Ordinal);
                int end = close < 0 ? _line.Length : first + close + 2;
                Comment(first, end, _sub == Sub.Doc);
                if (close >= 0)
                {
                    _sub = Sub.None;
                }

                return end;
            }

            char quote = _sub == Sub.Double ? '"' : '\'';
            _sub = Sub.None;
            if (_kind is FrameKind.JsxOpenTag or FrameKind.JsxCloseTag)
            {
                return AttributeString(i, i, quote);
            }

            return String(i, i, quote);
        }

        // ---- code ----

        private int Code(int i)
        {
            var line = _line;
            char c = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';
            switch (c)
            {
                case ' ' or '\t' or ' ' or '﻿':
                    return SkipBlank(i);
                case '/' when next == '/':
                    LineComment(i);
                    return line.Length;
                case '/' when next == '*':
                    return BlockComment(i);
                case '/':
                    if (RegexAllowed())
                    {
                        return Regex(i);
                    }

                    return Operator(i);
                case '"' or '\'':
                    _prev = Prev.Value;
                    return String(i, i + 1, c);
                case '`':
                    Emit(i, 1, TokenClasses.String, ScriptTokenKind.TemplateStart);
                    Push(FrameKind.Template, Prev.Value);
                    return i + 1;
                case '{':
                    return OpenBrace(i);
                case '}':
                    return CloseBrace(i);
                case '(':
                    {
                        bool control = _prev == Prev.ControlKeyword;
                        Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                        _parenKinds = (_parenKinds << 1) | (control ? 1u : 0u);
                        _parens = Math.Min(_parens + 1, Frames.MaxParens);
                        _prev = Prev.OperatorStart;
                        return i + 1;
                    }

                case ')':
                    {
                        Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                        bool control = false;
                        if (_parens > 0)
                        {
                            control = (_parenKinds & 1) != 0;
                            _parenKinds >>= 1;
                            _parens--;
                        }

                        _prev = control ? Prev.ControlParenClose : Prev.Value;
                        return i + 1;
                    }

                case '[':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                    _prev = Prev.OperatorStart;
                    return i + 1;
                case ']':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                    _prev = Prev.Value;
                    return i + 1;
                case ';':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                    _prev = Prev.StatementStart;
                    return i + 1;
                case ',':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                    _prev = Prev.OperatorStart;
                    return i + 1;
                case '.' when char.IsAsciiDigit(next):
                    return Number(i);
                case '.' when next == '.' && i + 2 < line.Length && line[i + 2] == '.':
                    Emit(i, 3, TokenClasses.Operator, ScriptTokenKind.Operator);
                    _prev = Prev.OperatorStart;
                    return i + 3;
                case '.':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
                    _prev = Prev.Dot;
                    return i + 1;
                case '#' when IsNameStart(next):
                    {
                        int end = NameEnd(i + 1);
                        Emit(i, end - i, TokenClasses.FieldName, ScriptTokenKind.PrivateName);
                        _prev = Prev.Value;
                        return end;
                    }

                case '@':
                    return Decorator(i);
                case '<' when _t.Jsx && JsxAllowed() && LooksLikeJsx(i):
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxTagOpen);
                    Push(FrameKind.JsxOpenTag, Prev.TagName);
                    return i + 1;
            }

            if (char.IsAsciiDigit(c))
            {
                return Number(i);
            }

            if (IsNameStart(c))
            {
                return Word(i);
            }

            return Operator(i);
        }

        private readonly bool RegexAllowed() => !_t.Angular && _prev is Prev.StatementStart or Prev.OperatorStart or Prev.ControlParenClose
            or Prev.BlockClose or Prev.KeywordExpr or Prev.KeywordBlock or Prev.Arrow or Prev.ImportExport or Prev.Modifier;

        private readonly bool JsxAllowed() => _prev is Prev.StatementStart or Prev.OperatorStart or Prev.KeywordExpr or Prev.Arrow
            or Prev.BlockClose or Prev.KeywordBlock or Prev.ControlParenClose;

        // A '<' that starts an element: a name or '>' follows; in .tsx not '<T,>', '<T extends U>' or '<const T>' (generics).
        private readonly bool LooksLikeJsx(int i)
        {
            var line = _line;
            int at = i + 1;
            if (at >= line.Length)
            {
                return false;
            }

            if (line[at] == '>')
            {
                return true;
            }

            if (!IsNameStart(line[at]))
            {
                return false;
            }

            int end = NameEnd(at);
            var name = line[at..end];
            int after = SkipBlank(end);
            if (name.SequenceEqual("const") && after > end && after < line.Length && IsNameStart(line[after]))
            {
                return false;
            }

            if (after < line.Length && line[after] == ',')
            {
                return false;
            }

            if (after > end && after < line.Length && IsNameStart(line[after]))
            {
                int wordEnd = NameEnd(after);
                if (line[after..wordEnd].SequenceEqual("extends"))
                {
                    int value = SkipBlank(wordEnd);
                    return value < line.Length && line[value] is '=' or '/' or '>';
                }
            }

            return true;
        }

        private int OpenBrace(int i)
        {
            bool expression = _prev is Prev.OperatorStart or Prev.KeywordExpr or Prev.ImportExport;
            Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
            _braceKinds = (_braceKinds << 1) | (expression ? 1u : 0u);
            _braces = Math.Min(_braces + 1, Frames.MaxBraces);
            _prev = expression ? Prev.OperatorStart : Prev.StatementStart;
            return i + 1;
        }

        private int CloseBrace(int i)
        {
            if (_braces == 0 && _kind == FrameKind.TemplateExpr)
            {
                Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.TemplateExprClose);
                Pop();
                return i + 1;
            }

            if (_braces == 0 && _kind == FrameKind.JsxExpr)
            {
                Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxExprClose);
                Pop();
                return i + 1;
            }

            Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.Punct);
            bool expression = false;
            if (_braces > 0)
            {
                expression = (_braceKinds & 1) != 0;
                _braceKinds >>= 1;
                _braces--;
            }

            _prev = expression ? Prev.Value : Prev.BlockClose;
            return i + 1;
        }

        private int Operator(int i)
        {
            var rest = _line[i..];
            int length = OperatorLength(rest);
            if (length == 0)
            {
                return i + 1;   // a character no token starts with: left plain
            }

            Emit(i, length, TokenClasses.Operator, ScriptTokenKind.Operator);
            var op = rest[..length];
            if (op.SequenceEqual("=>"))
            {
                _prev = Prev.Arrow;
            }
            else if (op.SequenceEqual("?."))
            {
                _prev = Prev.Dot;
            }
            else if (op.SequenceEqual("++") || op.SequenceEqual("--"))
            {
                // postfix keeps the value before it, prefix the operator before it
            }
            else if (op.SequenceEqual("!"))
            {
                _prev = _prev == Prev.Value ? Prev.Value : Prev.OperatorStart;   // TS's non-null x! keeps a value
            }
            else if (op.SequenceEqual("*") && _prev == Prev.ImportExport)
            {
                _prev = Prev.Value;                                             // import * as x, export * from
            }
            else if (op.SequenceEqual("*") && _prev == Prev.ExpectFunction)
            {
                // function* name
            }
            else if (_t.Angular && op.SequenceEqual("|"))
            {
                _prev = Prev.Pipe;
            }
            else
            {
                _prev = Prev.OperatorStart;
            }

            return i + length;
        }

        private static int OperatorLength(ReadOnlySpan<char> rest)
        {
            char c = rest[0];
            char n = rest.Length > 1 ? rest[1] : '\0';
            char m = rest.Length > 2 ? rest[2] : '\0';
            char o = rest.Length > 3 ? rest[3] : '\0';
            switch (c)
            {
                case '>':
                    if (n == '>' && m == '>')
                    {
                        return o == '=' ? 4 : 3;
                    }

                    return n == '>' ? (m == '=' ? 3 : 2) : n == '=' ? 2 : 1;
                case '<':
                    return n == '<' ? (m == '=' ? 3 : 2) : n == '=' ? 2 : 1;
                case '=':
                    return n == '=' ? (m == '=' ? 3 : 2) : n == '>' ? 2 : 1;
                case '!':
                    return n == '=' ? (m == '=' ? 3 : 2) : 1;
                case '*':
                    return n == '*' ? (m == '=' ? 3 : 2) : n == '=' ? 2 : 1;
                case '&' or '|':
                    return n == c ? (m == '=' ? 3 : 2) : n == '=' ? 2 : 1;
                case '?':
                    if (n == '?')
                    {
                        return m == '=' ? 3 : 2;
                    }

                    return n == '.' && !char.IsAsciiDigit(m) ? 2 : 1;
                case '+' or '-':
                    return n == c || n == '=' ? 2 : 1;
                case '/' or '%' or '^':
                    return n == '=' ? 2 : 1;
                case '~' or ':':
                    return 1;
                default:
                    return 0;
            }
        }

        private int Decorator(int i)
        {
            int end = i + 1;
            while (end < _line.Length && IsNameStart(_line[end]))
            {
                end = NameEnd(end);
                if (end + 1 < _line.Length && _line[end] == '.' && IsNameStart(_line[end + 1]))
                {
                    end++;
                    continue;
                }

                break;
            }

            Emit(i, end - i, TokenClasses.PreprocessorKeyword, ScriptTokenKind.Decorator);
            _prev = end > i + 1 ? Prev.Value : Prev.OperatorStart;
            return end;
        }

        // ---- names and words ----

        private int Word(int i)
        {
            int end = Math.Max(NameEnd(i), i + 1);   // a lone '\' is one character
            var name = _line[i..end];
            if (_prev == Prev.Dot)
            {
                Emit(i, end - i, FollowedByParen(end) ? TokenClasses.MethodName : TokenClasses.PropertyName, ScriptTokenKind.Word);
                _prev = Prev.Value;
                return end;
            }

            if (_prev == Prev.Value && name.SequenceEqual("with"))
            {
                Emit(i, end - i, TokenClasses.Keyword, ScriptTokenKind.Keyword);   // import attributes: from "./a.json" with { type: "json" }
                _prev = Prev.OperatorStart;
                return end;
            }

            if (ScriptWords.Lookup.TryGetValue(name, out var word) && IsKeyword(word, name, end))
            {
                Emit(i, end - i, word.Control ? TokenClasses.ControlKeyword : TokenClasses.Keyword, ScriptTokenKind.Keyword);
                _prev = NextAfter(word, name);
                return end;
            }

            string? tokenClass = _prev switch
            {
                Prev.ExpectClass => TokenClasses.ClassName,
                Prev.ExpectInterface or Prev.ExpectImplements => TokenClasses.InterfaceName,
                Prev.ExpectEnum => TokenClasses.EnumName,
                Prev.ExpectType => TokenClasses.ClassName,
                Prev.ExpectNamespace => TokenClasses.NamespaceName,
                Prev.ExpectFunction or Prev.Pipe => TokenClasses.MethodName,
                _ => FollowedByParen(end) ? TokenClasses.MethodName : null,
            };
            Emit(i, end - i, tokenClass, ScriptTokenKind.Word);
            _prev = Prev.Value;
            return end;
        }

        private readonly Prev NextAfter(Word word, ReadOnlySpan<char> name)
        {
            if (word.Rule == WordRule.Type)
            {
                return _prev is Prev.StatementStart or Prev.BlockClose or Prev.Modifier or Prev.KeywordExpr ? Prev.ExpectType : Prev.OperatorStart;
            }

            if (name.SequenceEqual("extends") && _prev != Prev.ExpectClass && _prev != Prev.Value)
            {
                return Prev.OperatorStart;
            }

            return word.Next;
        }

        private readonly bool IsKeyword(Word word, ReadOnlySpan<char> name, int end)
        {
            var t = _t;
            if (word.TypeScriptOnly && !t.TypeScript)
            {
                return false;
            }

            if (t.Angular && word.Rule != WordRule.Always && word.Rule != WordRule.AfterValue)
            {
                return false;
            }

            if (t.TypeScript ? t.Version.TypeScript < word.MinTypeScript : t.Version.EcmaScript < word.MinEcmaScript)
            {
                return false;   // a newer word read by an older version: a plain name
            }

            switch (word.Rule)
            {
                case WordRule.Always:
                    return true;
                case WordRule.Modifier:
                    return _prev is not Prev.Value && NamedAfter(end, allowBrace: true);
                case WordRule.AfterValue:
                    return _prev == Prev.Value && NamedAfter(end, allowBrace: true, allowParen: true);
                case WordRule.From:
                    {
                        int at = SkipBlank(end);
                        return at < _line.Length && _line[at] is '"' or '\'';
                    }

                case WordRule.Async:
                    {
                        int at = SkipBlank(end);
                        return at > end && at < _line.Length && (IsNameStart(_line[at]) || _line[at] is '(' or '*') && _prev is not Prev.Value;
                    }

                case WordRule.Using:
                    return _prev is Prev.StatementStart or Prev.BlockClose or Prev.KeywordExpr or Prev.OperatorStart or Prev.KeywordBlock or Prev.Arrow
                        && NamedAfter(end, allowBrace: false, nameOnly: true);
                case WordRule.Type:
                    return _prev is Prev.StatementStart or Prev.BlockClose or Prev.ImportExport or Prev.Modifier or Prev.KeywordExpr or Prev.OperatorStart
                        && NamedAfter(end, allowBrace: true)
                        && (_prev != Prev.ImportExport || t.Version.TypeScript >= 308)
                        && (!StarAfter(end) || t.Version.TypeScript >= 500);
                case WordRule.TypeWord:
                    {
                        // A type's place: after ':' '|' '&' '<' (x: string = ""), else not before '=' '(' ':' '.' (let string = 1).
                        int before = end - name.Length - 1;
                        while (before >= 0 && _line[before] is ' ' or '\t')
                        {
                            before--;
                        }

                        if (before >= 0 && _line[before] is ':' or '|' or '&' or '<' && _prev != Prev.Dot)
                        {
                            return true;
                        }

                        int at = SkipBlank(end);
                        return at >= _line.Length || _line[at] is not ('=' or '(' or ':' or '.');
                    }

                case WordRule.Of:
                    return _prev == Prev.Value && _parens > 0;
                case WordRule.Out:
                    return _prev is Prev.OperatorStart or Prev.KeywordExpr && NamedAfter(end, allowBrace: false, nameOnly: true);
                case WordRule.ImportPhase:
                    return _prev == Prev.ImportExport && NamedAfter(end, allowBrace: true);
                case WordRule.Unique:
                    {
                        int at = SkipBlank(end);
                        return at > end && at + 6 <= _line.Length && _line.Slice(at, 6).SequenceEqual("symbol");
                    }

                case WordRule.Global:
                    {
                        int at = SkipBlank(end);
                        return _prev == Prev.Modifier && at < _line.Length && _line[at] == '{';
                    }

                default:
                    return false;
            }
        }

        // Whether white space and then a name (not a word that follows a value), '#', '[', '*', a quote or '{' follow.
        private readonly bool NamedAfter(int end, bool allowBrace, bool allowParen = false, bool nameOnly = false)
        {
            int at = SkipBlank(end);
            if (at == end || at >= _line.Length)
            {
                return false;
            }

            char c = _line[at];
            if (IsNameStart(c))
            {
                int wordEnd = NameEnd(at);
                return !ScriptWords.BinaryLookup.Contains(_line[at..wordEnd]);
            }

            if (nameOnly)
            {
                return c == '[' || c == '{';
            }

            return c is '#' or '[' or '*' or '"' or '\'' || (allowBrace && c == '{') || (allowParen && c is '(' or '`');
        }

        private readonly bool StarAfter(int end)
        {
            int at = SkipBlank(end);
            return at < _line.Length && _line[at] == '*';
        }

        private readonly bool FollowedByParen(int end)
        {
            int at = SkipBlank(end);
            return at < _line.Length && _line[at] == '(';
        }

        private readonly int SkipBlank(int i)
        {
            while (i < _line.Length && _line[i] is ' ' or '\t' or ' ' or '﻿')
            {
                i++;
            }

            return i;
        }

        private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$' or '\\' || (c > 0x7F && char.IsLetter(c));

        private readonly int NameEnd(int i)
        {
            var line = _line;
            while (i < line.Length)
            {
                int run = line[i..].IndexOfAnyExcept(NameChars);
                if (run < 0)
                {
                    return line.Length;
                }

                i += run;
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == 'u')
                {
                    i = EscapeEnd(i);   // A or \u{41} in a name
                    continue;
                }

                if (c > 0x7F && (char.IsLetterOrDigit(c) || c is '‌' or '‍'))
                {
                    i++;
                    continue;
                }

                return i;
            }

            return i;
        }

        // ---- literals ----

        private int Number(int i)
        {
            var line = _line;
            int j = i;
            bool prefixed = false;
            bool dot = false;
            bool exponent = false;
            if (line[i] == '0' && i + 1 < line.Length && line[i + 1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
            {
                prefixed = true;
                j += 2;
            }

            while (j < line.Length)
            {
                char c = line[j];
                if (char.IsAsciiLetterOrDigit(c) || c == '_')
                {
                    if (!prefixed && c is 'e' or 'E')
                    {
                        exponent = true;
                        if (j + 1 < line.Length && line[j + 1] is '+' or '-')
                        {
                            j++;
                        }
                    }

                    j++;
                }
                else if (c == '.' && !prefixed && !dot && !exponent && !(j + 1 < line.Length && line[j + 1] == '.'))
                {
                    dot = true;
                    j++;
                }
                else
                {
                    break;
                }
            }

            Emit(i, j - i, TokenClasses.Number, ScriptTokenKind.Number);
            _prev = Prev.Value;
            return j;
        }

        // A "..." or '...' string's text from `from` (its opening quote, or the line's start when it continues), its escapes
        // as escapes; a '\' at the line's end continues it on the next line.
        private int String(int from, int i, char quote)
        {
            var line = _line;
            int run = from;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == quote)
                {
                    Span(run, i + 1 - run, TokenClasses.String);
                    Token(from, i + 1 - from, ScriptTokenKind.String);
                    _prev = Prev.Value;
                    return i + 1;
                }

                if (c == '\\')
                {
                    Span(run, i - run, TokenClasses.String);
                    if (i + 1 >= line.Length)
                    {
                        Span(i, 1, TokenClasses.StringEscape);
                        Token(from, line.Length - from, ScriptTokenKind.String);
                        _sub = quote == '"' ? Sub.Double : Sub.Single;   // a line continuation
                        return line.Length;
                    }

                    int end = EscapeEnd(i);
                    Span(i, end - i, TokenClasses.StringEscape);
                    i = end;
                    run = i;
                    continue;
                }

                i++;
            }

            Span(run, line.Length - run, TokenClasses.String);
            Token(from, line.Length - from, ScriptTokenKind.StringUnterminated);
            _prev = Prev.Value;
            return line.Length;
        }

        private readonly int EscapeEnd(int i)
        {
            var line = _line;
            int at = i + 1;
            if (at >= line.Length)
            {
                return line.Length;
            }

            char c = line[at];
            if (c == 'u' && at + 1 < line.Length && line[at + 1] == '{')
            {
                int close = line[at..].IndexOf('}');
                return close < 0 ? line.Length : at + close + 1;
            }

            int length = c switch
            {
                'u' => 5,
                'x' => 3,
                _ => 1,
            };
            int end = at + length;
            if (c is 'u' or 'x')
            {
                int j = at + 1;
                while (j < end && j < line.Length && char.IsAsciiHexDigit(line[j]))
                {
                    j++;
                }

                return j;
            }

            return Math.Min(end, line.Length);
        }

        // A template literal's text: escapes as escapes, '`' closes it, '${' opens an expression frame.
        private int TemplateText(int i)
        {
            var line = _line;
            int run = i;
            while (i < line.Length)
            {
                int special = line[i..].IndexOfAny(TemplateSpecials);
                if (special < 0)
                {
                    break;
                }

                i += special;
                char c = line[i];
                if (c == '\\')
                {
                    TextRun(run, i);
                    int end = i + 1 >= line.Length ? line.Length : EscapeEnd(i);
                    Span(i, end - i, TokenClasses.StringEscape);
                    Token(i, end - i, ScriptTokenKind.TemplateString);
                    i = end;
                    run = i;
                    continue;
                }

                if (c == '`')
                {
                    TextRun(run, i);
                    Emit(i, 1, TokenClasses.String, ScriptTokenKind.TemplateEnd);
                    Pop();
                    _prev = Prev.Value;
                    return i + 1;
                }

                if (i + 1 < line.Length && line[i + 1] == '{')
                {
                    TextRun(run, i);
                    Emit(i, 2, TokenClasses.Punctuation, ScriptTokenKind.TemplateExprOpen);
                    Push(FrameKind.TemplateExpr, Prev.OperatorStart);
                    return i + 2;
                }

                i++;
            }

            TextRun(run, line.Length);
            return line.Length;
        }

        private readonly void TextRun(int from, int to)
        {
            Span(from, to - from, TokenClasses.String);
            Token(from, to - from, ScriptTokenKind.TemplateString);
        }

        // A regular expression from its '/': classes ([...], nested in the v flag's), escapes, group syntax, flags.
        private int Regex(int i)
        {
            var line = _line;
            int close = RegexClose(i, nested: true);
            if (close < 0)
            {
                close = RegexClose(i, nested: false);   // [[] outside the v flag: a class holding '['
            }

            if (close < 0)
            {
                Span(i, line.Length - i, TokenClasses.String);
                Token(i, line.Length - i, ScriptTokenKind.RegexUnterminated);
                _prev = Prev.Value;
                return line.Length;
            }

            int flagsEnd = close + 1;
            while (flagsEnd < line.Length && char.IsAsciiLetter(line[flagsEnd]))
            {
                flagsEnd++;
            }

            if (_into is not null)
            {
                RegexBody(i, close + 1);
                Span(close + 1, flagsEnd - close - 1, TokenClasses.Keyword);
            }

            Token(i, flagsEnd - i, ScriptTokenKind.Regex);
            _prev = Prev.Value;
            return flagsEnd;
        }

        private readonly int RegexClose(int i, bool nested)
        {
            var line = _line;
            int classes = 0;
            for (int j = i + 1; j < line.Length; j++)
            {
                char c = line[j];
                if (c == '\\')
                {
                    j++;
                }
                else if (c == '[')
                {
                    classes = nested || classes == 0 ? classes + 1 : classes;
                }
                else if (c == ']' && classes > 0)
                {
                    classes--;
                }
                else if (c == '/' && classes == 0)
                {
                    return j;
                }
            }

            return -1;
        }

        private readonly void RegexBody(int start, int end)
        {
            var line = _line;
            int run = start;
            int j = start + 1;
            while (j < end - 1)
            {
                char c = line[j];
                int length = 0;
                if (c == '\\')
                {
                    length = RegexEscapeLength(j, end - 1);
                }
                else if (c == '(' && j + 1 < end && line[j + 1] == '?')
                {
                    length = GroupOpenerLength(j, end - 1);
                }

                if (length > 0)
                {
                    Span(run, j - run, TokenClasses.String);
                    Span(j, length, TokenClasses.StringEscape);
                    j += length;
                    run = j;
                    continue;
                }

                j++;
            }

            Span(run, end - run, TokenClasses.String);
        }

        private readonly int RegexEscapeLength(int j, int limit)
        {
            var line = _line;
            if (j + 1 >= limit)
            {
                return 1;
            }

            char c = line[j + 1];
            int to;
            switch (c)
            {
                case 'u' when j + 2 < limit && line[j + 2] == '{':
                case 'p' or 'P' or 'q' when j + 2 < limit && line[j + 2] == '{':
                    to = line[j..limit].IndexOf('}');
                    return to < 0 ? limit - j : to + 1;
                case 'k' when j + 2 < limit && line[j + 2] == '<':
                    to = line[j..limit].IndexOf('>');
                    return to < 0 ? limit - j : to + 1;
                case 'u':
                    return Math.Min(6, limit - j);
                case 'x':
                    return Math.Min(4, limit - j);
                case 'c':
                    return Math.Min(3, limit - j);
                default:
                    return 2;
            }
        }

        // (?: (?= (?! (?<= (?<! (?<name> and the modifiers (?i: (?ims-s: of ES2025.
        private readonly int GroupOpenerLength(int j, int limit)
        {
            var line = _line;
            int at = j + 2;
            if (at >= limit)
            {
                return 2;
            }

            char c = line[at];
            if (c is ':' or '=' or '!')
            {
                return 3;
            }

            if (c == '<')
            {
                if (at + 1 < limit && line[at + 1] is '=' or '!')
                {
                    return 4;
                }

                int close = line[at..limit].IndexOf('>');
                return close < 0 ? 3 : close + 3;
            }

            int k = at;
            while (k < limit && (char.IsAsciiLetter(line[k]) || line[k] == '-'))
            {
                k++;
            }

            return k < limit && line[k] == ':' && k > at ? k - j + 1 : 2;
        }

        // ---- comments ----

        private void LineComment(int i)
        {
            var rest = _line[i..];
            if (rest.StartsWith("///"))
            {
                var directive = rest[3..].TrimStart();
                if (directive.StartsWith("<reference") || directive.StartsWith("<amd-"))
                {
                    Emit(i, rest.Length, TokenClasses.PreprocessorText, ScriptTokenKind.LineComment);
                    return;
                }
            }

            Emit(i, rest.Length, TokenClasses.Comment, ScriptTokenKind.LineComment);
        }

        private int BlockComment(int i)
        {
            var line = _line;
            bool doc = i + 2 < line.Length && line[i + 2] == '*' && !(i + 3 < line.Length && line[i + 3] == '/');
            int close = i + 2 < line.Length ? line[(i + 2)..].IndexOf("*/", StringComparison.Ordinal) : -1;
            int end = close < 0 ? line.Length : i + 2 + close + 2;
            Comment(i, end, doc);
            if (close < 0)
            {
                _sub = doc ? Sub.Doc : Sub.Block;
            }

            return end;
        }

        // A comment's part on this line; JSDoc's @tags as documentation tags.
        private readonly void Comment(int start, int end, bool doc)
        {
            Token(start, end - start, ScriptTokenKind.BlockComment);
            if (!doc)
            {
                Span(start, end - start, TokenClasses.Comment);
                return;
            }

            var line = _line;
            int run = start;
            for (int j = start; j < end; j++)
            {
                if (line[j] == '@' && j + 1 < end && char.IsAsciiLetter(line[j + 1]) && (j == start || line[j - 1] is ' ' or '\t' or '*' or '{'))
                {
                    int tagEnd = j + 1;
                    while (tagEnd < end && (char.IsAsciiLetterOrDigit(line[tagEnd]) || line[tagEnd] == '-'))
                    {
                        tagEnd++;
                    }

                    Span(run, j - run, TokenClasses.DocComment);
                    Span(j, tagEnd - j, TokenClasses.DocCommentTag);
                    j = tagEnd - 1;
                    run = tagEnd;
                }
            }

            Span(run, end - run, TokenClasses.DocComment);
        }

        // ---- JSX ----

        private int Tag(int i)
        {
            var line = _line;
            char c = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';
            switch (c)
            {
                case ' ' or '\t' or ' ':
                    return SkipBlank(i);
                case '/' when next == '*':
                    return BlockComment(i);
                case '{':
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxExprOpen);
                    Push(FrameKind.JsxExpr, Prev.OperatorStart);
                    return i + 1;
                case '"' or '\'':
                    return AttributeString(i, i + 1, c);
                case '=':
                    Emit(i, 1, TokenClasses.Operator, ScriptTokenKind.JsxAttrEquals);
                    return i + 1;
                case '/' when next == '>' && _kind == FrameKind.JsxOpenTag:
                    Emit(i, 2, TokenClasses.Punctuation, ScriptTokenKind.JsxSelfClose);
                    EndElement();
                    return i + 2;
                case '>':
                    if (_kind == FrameKind.JsxOpenTag)
                    {
                        Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxTagEnd);
                        _kind = FrameKind.JsxChildren;
                        _prev = Prev.StatementStart;
                    }
                    else
                    {
                        Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxCloseTagEnd);
                        EndElement();
                    }

                    return i + 1;
            }

            if (IsNameStart(c))
            {
                int end = i + 1;
                int run = line[end..].IndexOfAnyExcept(TagNameChars);
                end = run < 0 ? line.Length : end + run;
                if (_prev == Prev.TagName)
                {
                    var name = line[i..end];
                    bool component = char.IsUpper(name[0]) || name.Contains('.');
                    Emit(i, end - i, component ? TokenClasses.ClassName : TokenClasses.Keyword, ScriptTokenKind.JsxTagName);
                    _prev = Prev.TagAttributes;
                }
                else
                {
                    Emit(i, end - i, TokenClasses.PropertyName, ScriptTokenKind.JsxAttrName);
                }

                return end;
            }

            return i + 1;
        }

        // An attribute's string in a tag (no escapes in JSX); it may span lines.
        private int AttributeString(int from, int i, char quote)
        {
            int close = _line[i..].IndexOf(quote);
            if (close < 0)
            {
                Emit(from, _line.Length - from, TokenClasses.String, ScriptTokenKind.JsxAttrString);
                _sub = quote == '"' ? Sub.Double : Sub.Single;
                return _line.Length;
            }

            int end = i + close + 1;
            Emit(from, end - from, TokenClasses.String, ScriptTokenKind.JsxAttrString);
            return end;
        }

        // An element ended (its '/>' or its closing tag's '>'): back in the code or the children around it.
        private void EndElement()
        {
            Pop();
            if (InCode)
            {
                _prev = Prev.Value;
            }
        }

        // An element's children: text (trimmed, unclassified), entities as escapes, '{' an expression, '<' an element or the closing tag.
        private int Children(int i)
        {
            var line = _line;
            int text = i;
            while (i < line.Length)
            {
                int special = line[i..].IndexOfAny(ChildrenSpecials);
                if (special < 0)
                {
                    break;
                }

                i += special;
                char c = line[i];
                if (c == '&')
                {
                    int semi = line[i..].IndexOf(';');
                    if (semi > 1 && semi <= 32)
                    {
                        Span(i, semi + 1, TokenClasses.StringEscape);
                        i += semi + 1;
                        continue;
                    }

                    i++;
                    continue;
                }

                ChildText(text, i);
                if (c == '{')
                {
                    Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxExprOpen);
                    Push(FrameKind.JsxExpr, Prev.OperatorStart);
                    return i + 1;
                }

                if (i + 1 < line.Length && line[i + 1] == '/')
                {
                    Emit(i, 2, TokenClasses.Punctuation, ScriptTokenKind.JsxCloseTagOpen);
                    _kind = FrameKind.JsxCloseTag;
                    _prev = Prev.TagName;
                    return i + 2;
                }

                Emit(i, 1, TokenClasses.Punctuation, ScriptTokenKind.JsxTagOpen);
                Push(FrameKind.JsxOpenTag, Prev.TagName);
                return i + 1;
            }

            ChildText(text, line.Length);
            return line.Length;
        }

        private readonly void ChildText(int from, int to)
        {
            if (_sink is null)
            {
                return;
            }

            var text = _line[from..to];
            int lead = text.Length - text.TrimStart().Length;
            int length = text.Trim().Length;
            Token(from + lead, length, ScriptTokenKind.JsxText);
        }
    }
}
