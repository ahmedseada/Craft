// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.Web.Style;

/// <summary>The dialects of the style languages.</summary>
public enum StyleDialect : byte
{
    /// <summary>CSS (CSS Syntax 3, nesting included).</summary>
    Css,

    /// <summary>SCSS: <c>$var</c>, <c>#{}</c>, <c>@use</c>, <c>@mixin</c>, <c>%placeholder</c>, <c>//</c> comments, maps.</summary>
    Scss,

    /// <summary>LESS: <c>@var:</c>, mixins and guards, <c>@{}</c>, <c>~"…"</c>, <c>//</c> comments.</summary>
    Less,

    /// <summary>Sass's indented syntax (.sass), lexed as SCSS whose statements end at the line's end (not formatted).</summary>
    Sass,
}

/// <summary>
/// The style languages' colours, a line at a time (CSS, SCSS, LESS, and .sass as SCSS-like): CSS Syntax 3's tokens, so any
/// new syntax lexes; nesting (a statement in a block is a nested rule when its first <c>{</c> comes before a <c>;</c>, a
/// declaration otherwise, and on a line with neither, a declaration when a known property, a <c>--name</c> or a ':' and
/// a space starts it); at-rules (<c>@layer</c>, <c>@container</c>, <c>@scope</c>, <c>@starting-style</c>,
/// <c>@property</c>, <c>@position-try</c>, <c>@function</c>, SCSS's and LESS's) in the control keyword's colour;
/// properties, class selectors, functions (<c>color-mix()</c>, <c>oklch()</c>, <c>anchor()</c>, <c>if()</c>), custom
/// properties and variables, units and colours (numbers), strings, comments; <c>#{}</c> and <c>@{}</c> nested to five
/// frames across lines. Colours come from the theme's existing classes only. One state per line (a plain number of
/// under 63 bits, so an embedding language can carry it), nothing allocated per token, names looked up by span
/// (<see cref="StyleNames"/>); an older <see cref="Year"/> reads a name newer than it as plain. Stateless: one instance
/// per dialect and year serves every session (<see cref="For"/>).
/// </summary>
public sealed class StyleTokenizer : LineTokenizer
{
    /// <summary>The frames of <c>#{</c>, <c>@{</c> and strings a state holds at most (deeper ones are lexed flat).</summary>
    public const int MaxFrames = 5;

    // The state: the mode (4 bits), a block comment open, the parenthesis depth (4), an at-rule's prelude kind (3), its
    // first name pending, the frames' count (3), then each frame (the mode and the depth to go back to, 8 bits each).
    private const ulong ModeMask = 0xF;
    private const ulong CommentBit = 1UL << 4;
    private const int ParenShift = 5;
    private const int SubShift = 9;
    private const ulong NameFirstBit = 1UL << 12;
    private const int DepthShift = 13;
    private const int FrameShift = 16;

    private const int Statement = 0;
    private const int Selector = 1;
    private const int Prelude = 2;
    private const int Value = 3;
    private const int DeclName = 4;
    private const int Interp = 5;
    private const int StringDouble = 6;
    private const int StringSingle = 7;

    private const int SubGeneric = 0;
    private const int SubMethod = 1;
    private const int SubType = 2;
    private const int SubLayer = 3;
    private const int SubSelector = 4;

    private static readonly SearchValues<char> NameChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_");
    private static readonly SearchValues<char> HexChars = SearchValues.Create("0123456789abcdefABCDEF");
    private static readonly SearchValues<char> DecideStops = SearchValues.Create("{};()\"'/#@\\");
    private static readonly SearchValues<char> StringStops = SearchValues.Create("\"'\\#@");
    private static readonly ConcurrentDictionary<(StyleDialect Dialect, int Year), StyleTokenizer> Cache = new();

    private static readonly FrozenDictionary<string, byte>.AlternateLookup<ReadOnlySpan<char>> PreludeKinds = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["mixin"] = SubMethod, ["include"] = SubMethod, ["function"] = SubMethod,
        ["keyframes"] = SubType, ["-webkit-keyframes"] = SubType, ["counter-style"] = SubType, ["font-palette-values"] = SubType,
        ["position-try"] = SubType, ["property"] = SubType, ["container"] = SubType, ["font-feature-values"] = SubType,
        ["layer"] = SubLayer,
        ["extend"] = SubSelector, ["at-root"] = SubSelector, ["page"] = SubSelector, ["scope"] = SubSelector,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> PreludeKeywords = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ["and", "or", "not", "only", "from", "through", "to", "in", "as", "with", "show", "hide", "if", "using"]).GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> WideKeywords = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ["inherit", "initial", "unset", "revert", "revert-layer"]).GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ScriptKeywords = FrozenSet.Create(StringComparer.Ordinal,
        ["true", "false", "null", "and", "or", "not"]).GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly bool _scss;
    private readonly bool _less;
    private readonly bool _lineComments;

    private StyleTokenizer(StyleDialect dialect, int year)
    {
        Dialect = dialect;
        Year = year;
        _scss = dialect is StyleDialect.Scss or StyleDialect.Sass;
        _less = dialect == StyleDialect.Less;
        _lineComments = dialect != StyleDialect.Css;
    }

    /// <summary>Its dialect.</summary>
    public StyleDialect Dialect { get; }

    /// <summary>The Baseline year it reads (<see cref="StyleNames.Latest"/>: every name known); a known name newer than it is plain.</summary>
    public int Year { get; }

    /// <summary>The tokenizer of <paramref name="dialect"/> reading <paramref name="year"/> (made once, then shared).</summary>
    public static StyleTokenizer For(StyleDialect dialect, int year = StyleNames.Latest) =>
        Cache.GetOrAdd((dialect, year), static key => new StyleTokenizer(key.Dialect, key.Year));

    /// <summary>Whether a line that ends in <paramref name="state"/> ends inside a block comment.</summary>
    public static bool IsInComment(ulong state) => (state & CommentBit) != 0;

    /// <summary>Whether a line that ends in <paramref name="state"/> ends at the start of a statement (nothing open but blocks).</summary>
    public static bool IsAtStatement(ulong state) => state == Statement;

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        var scan = new Scan(this, line, lineStart, into, state);
        scan.Run();
        return scan.State();
    }

    private ref struct Scan
    {
        private readonly StyleTokenizer _t;
        private readonly ReadOnlySpan<char> _l;
        private readonly int _at;
        private readonly TokenSpanBuffer? _into;
        private int _i;
        private int _mode;
        private int _paren;
        private int _sub;
        private int _depth;
        private bool _comment;
        private bool _nameFirst;
        private bool _continued;
        private ulong _frames;
        private int _pieceStart;

        public Scan(StyleTokenizer tokenizer, ReadOnlySpan<char> line, int lineStart, TokenSpanBuffer? into, ulong state)
        {
            _t = tokenizer;
            _l = line;
            _at = lineStart;
            _into = into;
            _mode = (int)(state & ModeMask);
            _comment = (state & CommentBit) != 0;
            _paren = (int)((state >> ParenShift) & 0xF);
            _sub = (int)((state >> SubShift) & 0x7);
            _nameFirst = (state & NameFirstBit) != 0;
            _depth = (int)((state >> DepthShift) & 0x7);
            _frames = state >> FrameShift;
            _pieceStart = -1;
        }

        public readonly ulong State() =>
            (ulong)(uint)_mode | (_comment ? CommentBit : 0UL) | ((ulong)(uint)_paren << ParenShift) | ((ulong)(uint)_sub << SubShift)
            | (_nameFirst ? NameFirstBit : 0UL) | ((ulong)(uint)_depth << DepthShift) | (_frames << FrameShift);

        public void Run()
        {
            // .sass: a statement ends at the line's end (nothing open but blocks and comments).
            if (_t.Dialect == StyleDialect.Sass && !_comment && _depth == 0 && _paren == 0 && _mode is Selector or Prelude or Value or DeclName)
            {
                ToStatement();
            }

            var l = _l;
            while (_i < l.Length)
            {
                if (_comment)
                {
                    CommentBody();
                    continue;
                }

                if (_mode is StringDouble or StringSingle)
                {
                    StringBody();
                    continue;
                }

                char c = l[_i];
                if (c <= ' ' || c is ' ' or '﻿')
                {
                    _i++;
                    continue;
                }

                char next = _i + 1 < l.Length ? l[_i + 1] : '\0';
                if (c == '/' && next == '*')
                {
                    BlockComment();
                    continue;
                }

                if (c == '/' && next == '/' && _t._lineComments)
                {
                    Add(_i, l.Length - _i, TokenClasses.Comment);
                    _i = l.Length;
                    break;
                }

                switch (_mode)
                {
                    case Statement:
                        StatementStart();
                        break;
                    case Selector:
                        SelectorToken();
                        break;
                    case Prelude when _sub == SubSelector:
                        SelectorToken();
                        break;
                    case DeclName:
                        DeclNameToken();
                        break;
                    default:
                        ValueToken();
                        break;
                }
            }

            // A string not continued by a '\' at the line's end ends there (CSS's bad string): back to what held it.
            if (_mode is StringDouble or StringSingle && !_continued && _depth > 0)
            {
                Pop();
            }
        }

        private readonly void Add(int start, int length, string tokenClass) => _into?.Add(_at + start, length, tokenClass);

        private void ToStatement()
        {
            _mode = Statement;
            _paren = 0;
            _sub = SubGeneric;
            _nameFirst = false;
        }

        private bool Push(int mode)
        {
            if (_depth >= MaxFrames)
            {
                return false;
            }

            _frames |= (ulong)(uint)((mode & 0xF) | (_paren << 4)) << (8 * _depth);
            _depth++;
            return true;
        }

        private void Pop()
        {
            _depth--;
            int shift = 8 * _depth;
            int frame = (int)((_frames >> shift) & 0xFF);
            _frames &= ~(0xFFUL << shift);
            _mode = frame & 0xF;
            _paren = frame >> 4;
        }

        private readonly char At(int index) => index < _l.Length ? _l[index] : '\0';

        private readonly bool IsInterpolation(int index) =>
            index + 1 < _l.Length && _l[index + 1] == '{' && ((_t._scss && _l[index] == '#') || (_t._less && _l[index] == '@'));

        private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c >= '\u0080';

        private static bool IsNameStartChar(char c) => char.IsAsciiLetter(c) || c == '_' || c > ' ' || c == '\\';

        private readonly bool IsNameStart(int index)
        {
            char c = At(index);
            if (IsNameStartChar(c))
            {
                return c != '\\' || index + 1 < _l.Length;
            }

            if (c == '-')
            {
                char next = At(index + 1);
                return next == '-' || IsNameStartChar(next);
            }

            return false;
        }

        // The end of the name from `index`: letters, digits, '-', '_', anything past ASCII, and '\' escapes.
        private readonly int NameEnd(int index)
        {
            var l = _l;
            while (index < l.Length)
            {
                int run = l[index..].IndexOfAnyExcept(NameChars);
                if (run < 0)
                {
                    return l.Length;
                }

                index += run;
                char c = l[index];
                if (c >= '\u0080')
                {
                    index++;
                }
                else if (c == '\\' && index + 1 < l.Length)
                {
                    index += 2;
                }
                else
                {
                    return index;
                }
            }

            return index;
        }

        private readonly int SkipBlanks(int index)
        {
            while (index < _l.Length && _l[index] is ' ' or '\t')
            {
                index++;
            }

            return index;
        }

        private void CommentBody()
        {
            int close = _l[_i..].IndexOf("*/", StringComparison.Ordinal);
            if (close < 0)
            {
                Add(_i, _l.Length - _i, TokenClasses.Comment);
                _i = _l.Length;
                return;
            }

            Add(_i, close + 2, TokenClasses.Comment);
            _i += close + 2;
            _comment = false;
        }

        private void BlockComment()
        {
            int close = _l[(_i + 2)..].IndexOf("*/", StringComparison.Ordinal);
            if (close < 0)
            {
                Add(_i, _l.Length - _i, TokenClasses.Comment);
                _i = _l.Length;
                _comment = true;
                return;
            }

            Add(_i, close + 4, TokenClasses.Comment);
            _i += close + 4;
        }

        private void Interpolation()
        {
            Add(_i, 2, TokenClasses.Operator);
            _i += 2;
            if (Push(_mode))
            {
                _mode = Interp;
                _paren = 0;
            }
        }

        // A string from its opening quote: a frame of its own, so an interpolation in it or a '\' line break carries on.
        private void StringFrom()
        {
            char quote = _l[_i];
            int start = _i;
            if (!Push(_mode))
            {
                int end = start + 1;
                while (end < _l.Length && _l[end] != quote)
                {
                    end += _l[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(_l.Length, end + 1);
                Add(start, end - start, TokenClasses.String);
                _i = end;
                return;
            }

            _mode = quote == '"' ? StringDouble : StringSingle;
            _pieceStart = start;
            _i = start + 1;
        }

        private void StringBody()
        {
            char quote = _mode == StringDouble ? '"' : '\'';
            _continued = false;
            int from = _pieceStart >= 0 ? _pieceStart : _i;
            _pieceStart = -1;
            int j = _i;
            var l = _l;
            while (true)
            {
                int stop = l[j..].IndexOfAny(StringStops);
                if (stop < 0)
                {
                    Add(from, l.Length - from, TokenClasses.String);
                    _i = l.Length;
                    return;
                }

                j += stop;
                char c = l[j];
                if (c == quote)
                {
                    Add(from, j + 1 - from, TokenClasses.String);
                    _i = j + 1;
                    Pop();
                    return;
                }

                if (c == '\\')
                {
                    Add(from, j - from, TokenClasses.String);
                    if (j + 1 >= l.Length)
                    {
                        Add(j, 1, TokenClasses.StringEscape);
                        _continued = true;
                        _i = l.Length;
                        return;
                    }

                    int length = 2;
                    if (HexChars.Contains(l[j + 1]))
                    {
                        int hex = j + 1;
                        while (hex < l.Length && hex < j + 7 && HexChars.Contains(l[hex]))
                        {
                            hex++;
                        }

                        length = hex - j + (hex < l.Length && l[hex] == ' ' ? 1 : 0);
                    }

                    Add(j, length, TokenClasses.StringEscape);
                    j += length;
                    from = j;
                    continue;
                }

                if (IsInterpolation(j) && _depth < MaxFrames)
                {
                    Add(from, j - from, TokenClasses.String);
                    _i = j;
                    Interpolation();
                    return;
                }

                j++;
            }
        }

        private void StatementStart()
        {
            char c = _l[_i];
            switch (c)
            {
                case '}':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    ToStatement();
                    return;
                case '{' or ';':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    return;
                case '@' when IsInterpolation(_i):
                    _mode = Decide();
                    return;
                case '@':
                    if (_t._less && LessVariableStatement())
                    {
                        return;
                    }

                    AtRule();
                    return;
                case '$' when _t._scss:
                    _mode = DeclName;
                    return;
                case '=' or '+' when _t.Dialect == StyleDialect.Sass && IsNameStart(_i + 1):
                    Add(_i++, 1, TokenClasses.ControlKeyword);
                    _mode = Prelude;
                    _sub = SubMethod;
                    _nameFirst = true;
                    return;
            }

            _mode = IsNameStart(_i) || IsInterpolation(_i) ? Decide() : Selector;
        }

        // LESS: `@name:` declares a variable, `@name();` calls a detached ruleset; true when it was one of them.
        private bool LessVariableStatement()
        {
            int nameStart = _i + 1 + (At(_i + 1) == '@' ? 1 : 0);
            int end = NameEnd(nameStart);
            if (end == nameStart)
            {
                return false;
            }

            int after = SkipBlanks(end);
            if (At(after) == ':')
            {
                Add(_i, end - _i, TokenClasses.LocalName);
                _i = end;
                _mode = DeclName;
                return true;
            }

            if (At(after) == '(' && StyleNames.AtRuleYear(_l[(_i + 1)..end]) < 0)
            {
                Add(_i, end - _i, TokenClasses.MethodName);
                _i = end;
                _mode = Value;
                return true;
            }

            return false;
        }

        private void AtRule()
        {
            int end = NameEnd(_i + 1);
            var name = _l[(_i + 1)..end];
            int year = _t._scss && name.Equals("function", StringComparison.OrdinalIgnoreCase) ? 0 : StyleNames.AtRuleYear(name);
            if (year <= _t.Year)
            {
                Add(_i, end - _i, TokenClasses.ControlKeyword);
            }

            _i = end;
            _mode = Prelude;
            _paren = 0;
            _sub = PreludeKinds.TryGetValue(name, out byte sub) ? sub : SubGeneric;
            _nameFirst = _sub is SubMethod or SubType;
        }

        // A statement that starts with a name: a nested rule when its first '{' comes before a ';' or '}', a declaration
        // when not; on a line with neither, a declaration when a ':' and a space, a known property or a --name starts it.
        private readonly int Decide()
        {
            var l = _l;
            int parens = 0;
            int j = _i;
            while (j < l.Length)
            {
                int stop = l[j..].IndexOfAny(DecideStops);
                if (stop < 0)
                {
                    break;
                }

                j += stop;
                char c = l[j];
                switch (c)
                {
                    case '"' or '\'':
                        j = SkipString(j);
                        continue;
                    case '/' when At(j + 1) == '*':
                        int close = l[(j + 2)..].IndexOf("*/", StringComparison.Ordinal);
                        j = close < 0 ? l.Length : j + close + 4;
                        continue;
                    case '/' when At(j + 1) == '/' && _t._lineComments && parens == 0:
                        j = l.Length;
                        continue;
                    case '#' or '@' when IsInterpolation(j):
                        j = SkipBraces(j + 1);
                        continue;
                    case '(':
                        parens++;
                        break;
                    case ')':
                        parens = Math.Max(0, parens - 1);
                        break;
                    case '{' when parens == 0:
                        return Selector;
                    case ';' or '}' when parens == 0:
                        return DeclName;
                    case '\\':
                        j++;
                        break;
                }

                j++;
            }

            if (!IsNameStart(_i))
            {
                return Selector;
            }

            int end = NameEnd(_i);
            int colon = SkipBlanks(end);
            if (At(colon) != ':')
            {
                return Selector;
            }

            char after = At(colon + 1);
            if (after is ' ' or '\t' or '\0' || (_l[_i] == '-' && At(_i + 1) == '-') || StyleNames.PropertyYear(_l[_i..end]) >= 0)
            {
                return DeclName;
            }

            return Selector;
        }

        private readonly int SkipString(int index)
        {
            char quote = _l[index];
            int j = index + 1;
            while (j < _l.Length && _l[j] != quote)
            {
                j += _l[j] == '\\' ? 2 : 1;
            }

            return Math.Min(_l.Length, j + 1);
        }

        // From an opening brace: the index after its matching close (strings skipped), or the line's end.
        private readonly int SkipBraces(int index)
        {
            int depth = 0;
            int j = index;
            while (j < _l.Length)
            {
                char c = _l[j];
                if (c is '"' or '\'')
                {
                    j = SkipString(j);
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}' && --depth == 0)
                {
                    return j + 1;
                }

                j++;
            }

            return _l.Length;
        }

        private void DeclNameToken()
        {
            char c = _l[_i];
            if (IsInterpolation(_i))
            {
                Interpolation();
                return;
            }

            switch (c)
            {
                case ':':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    _mode = Value;
                    _paren = 0;
                    return;
                case ';' or '}' or '{':
                    _mode = Statement;
                    return;
                case '$' when _t._scss:
                case '@' when _t._less:
                    int end = NameEnd(_i + 1 + (c == '@' && At(_i + 1) == '@' ? 1 : 0));
                    Add(_i, end - _i, TokenClasses.LocalName);
                    _i = end;
                    return;
                case '+' or '*' or '!':
                    Add(_i++, 1, TokenClasses.Operator);
                    return;
            }

            if (IsNameStart(_i) || c == '_')
            {
                int end = NameEnd(_i);
                var name = _l[_i..end];
                if (name.Length > 1 && name[0] == '-' && name[1] == '-')
                {
                    Add(_i, end - _i, TokenClasses.LocalName);
                }
                else if (StyleNames.PropertyYear(name) <= _t.Year)
                {
                    Add(_i, end - _i, TokenClasses.PropertyName);
                }

                _i = end;
                return;
            }

            _i++;
        }

        private void SelectorToken()
        {
            char c = _l[_i];
            if (IsInterpolation(_i))
            {
                Interpolation();
                return;
            }

            switch (c)
            {
                case '{' or '}':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    ToStatement();
                    return;
                case ';':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    if (_paren == 0)
                    {
                        ToStatement();
                    }

                    return;
                case ',':
                case ']':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    return;
                case '(':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    _paren = Math.Min(15, _paren + 1);
                    return;
                case ')':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    _paren = Math.Max(0, _paren - 1);
                    return;
                case '[':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    int name = SkipBlanks(_i);
                    if (IsNameStart(name))
                    {
                        int end = NameEnd(name);
                        Add(name, end - name, TokenClasses.ParameterName);
                        _i = end;
                    }

                    return;
                case '>' or '~' or '|' or '=' or '<':
                    Add(_i++, 1, TokenClasses.Operator);
                    return;
                case '+' or '-' when _paren > 0 && (char.IsAsciiDigit(At(_i + 1)) || At(_i + 1) == 'n'):
                    Number();
                    return;
                case '+':
                    Add(_i++, 1, TokenClasses.Operator);
                    return;
                case '.' when char.IsAsciiDigit(At(_i + 1)):
                    Number();
                    return;
                case '.' when IsNameStart(_i + 1) || At(_i + 1) == '-' || IsInterpolation(_i + 1):
                {
                    int end = NameEnd(_i + 1);
                    Add(_i, end - _i, At(end) == '(' ? TokenClasses.MethodName : TokenClasses.ClassName);
                    _i = end;
                    return;
                }

                case '#' when IsNameChar(At(_i + 1)):
                case '%' when _t._scss && IsNameChar(At(_i + 1)):
                {
                    int end = NameEnd(_i + 1);
                    Add(_i, end - _i, c == '#' ? TokenClasses.ConstantName : TokenClasses.ClassName);
                    _i = end;
                    return;
                }

                case '&':
                    Add(_i++, 1, TokenClasses.Keyword);
                    if (IsNameChar(At(_i)))
                    {
                        int end = NameEnd(_i);
                        Add(_i, end - _i, TokenClasses.ClassName);
                        _i = end;
                    }

                    return;
                case '*':
                    Add(_i++, 1, TokenClasses.Keyword);
                    return;
                case ':':
                    Pseudo();
                    return;
                case '!':
                {
                    int end = NameEnd(_i + 1);
                    Add(_i, Math.Max(1, end - _i), TokenClasses.Keyword);
                    _i = Math.Max(_i + 1, end);
                    return;
                }

                case '@' when _t._less:
                case '$' when _t._scss:
                {
                    int end = NameEnd(_i + 1);
                    Add(_i, end - _i, TokenClasses.LocalName);
                    _i = Math.Max(_i + 1, end);
                    return;
                }

                case '"' or '\'':
                    StringFrom();
                    return;
            }

            if (char.IsAsciiDigit(c))
            {
                Number();
                return;
            }

            if (IsNameStart(_i))
            {
                int end = NameEnd(_i);
                var word = _l[_i..end];
                string tokenClass = At(end) == '(' ? TokenClasses.MethodName
                    : _t._less && word.SequenceEqual("when") ? TokenClasses.ControlKeyword
                    : TokenClasses.Keyword;
                Add(_i, end - _i, tokenClass);
                _i = end;
                return;
            }

            _i += c == '\\' ? 2 : 1;
            _i = Math.Min(_i, _l.Length);
        }

        // ':hover', '::before', ':has(' (its argument lexed as a selector); a ':' alone is punctuation.
        private void Pseudo()
        {
            int name = _i + 1 + (At(_i + 1) == ':' ? 1 : 0);
            if (!IsNameStart(name))
            {
                Add(_i, name - _i, TokenClasses.Punctuation);
                _i = name;
                return;
            }

            int end = NameEnd(name);
            if (StyleNames.PseudoYear(_l[name..end]) <= _t.Year)
            {
                Add(_i, end - _i, TokenClasses.TypeParameterName);
            }

            _i = end;
        }

        private void Number()
        {
            var l = _l;
            int j = _i;
            if (l[j] is '+' or '-')
            {
                j++;
            }

            while (j < l.Length && char.IsAsciiDigit(l[j]))
            {
                j++;
            }

            if (j + 1 < l.Length && l[j] == '.' && char.IsAsciiDigit(l[j + 1]))
            {
                j++;
                while (j < l.Length && char.IsAsciiDigit(l[j]))
                {
                    j++;
                }
            }

            if (j + 1 < l.Length && l[j] is 'e' or 'E' && (char.IsAsciiDigit(l[j + 1]) || (l[j + 1] is '+' or '-' && char.IsAsciiDigit(At(j + 2)))))
            {
                j += 2;
                while (j < l.Length && char.IsAsciiDigit(l[j]))
                {
                    j++;
                }
            }

            if (j < l.Length && l[j] == '%')
            {
                j++;
            }
            else if (j < l.Length && (char.IsAsciiLetter(l[j]) || l[j] >= '\u0080'))
            {
                j = NameEnd(j);
            }

            Add(_i, j - _i, TokenClasses.Number);
            _i = j;
        }

        private void ValueToken()
        {
            char c = _l[_i];
            if (IsInterpolation(_i))
            {
                Interpolation();
                return;
            }

            if (_mode == Interp && c == '}')
            {
                Add(_i++, 1, TokenClasses.Operator);
                if (_depth > 0)
                {
                    Pop();
                }

                return;
            }

            switch (c)
            {
                case '{' when _mode == Interp:
                    Add(_i++, 1, TokenClasses.Punctuation);
                    return;
                case '{' or '}':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    ToStatement();
                    return;
                case ';':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    if (_paren == 0 && _mode != Interp)
                    {
                        ToStatement();
                    }

                    return;
                case '(' or '[':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    _paren = Math.Min(15, _paren + 1);
                    if (c == '(')
                    {
                        _nameFirst = false;
                    }

                    return;
                case ')' or ']':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    _paren = Math.Max(0, _paren - 1);
                    return;
                case ',' or ':':
                    Add(_i++, 1, TokenClasses.Punctuation);
                    return;
                case '"' or '\'':
                    StringFrom();
                    return;
                case '~' when _t._less && At(_i + 1) is '"' or '\'':
                    Add(_i++, 1, TokenClasses.String);
                    return;
                case '!':
                {
                    int end = NameEnd(_i + 1);
                    Add(_i, Math.Max(1, end - _i), end > _i + 1 ? TokenClasses.Keyword : TokenClasses.Operator);
                    _i = Math.Max(_i + 1, end);
                    return;
                }

                case '#':
                {
                    int end = NameEnd(_i + 1);
                    int length = end - _i - 1;
                    bool hex = length is 3 or 4 or 6 or 8 && _l[(_i + 1)..end].IndexOfAnyExcept(HexChars) < 0;
                    Add(_i, Math.Max(1, end - _i), hex ? TokenClasses.Number : length > 0 ? TokenClasses.ConstantName : TokenClasses.Operator);
                    _i = Math.Max(_i + 1, end);
                    return;
                }

                case '$' when _t._scss:
                case '@' when _t._less:
                {
                    int end = NameEnd(_i + 1 + (c == '@' && At(_i + 1) == '@' ? 1 : 0));
                    Add(_i, Math.Max(1, end - _i), TokenClasses.LocalName);
                    _i = Math.Max(_i + 1, end);
                    return;
                }

                case '&':
                    Add(_i++, 1, TokenClasses.Keyword);
                    return;
                case '.' when char.IsAsciiDigit(At(_i + 1)):
                    Number();
                    return;
                case '+' or '-' when IsSign():
                    Number();
                    return;
                case '\\':
                    _i = Math.Min(_l.Length, _i + 2);
                    return;
            }

            if (char.IsAsciiDigit(c))
            {
                Number();
                return;
            }

            if (IsNameStart(_i))
            {
                Name();
                return;
            }

            if (c is '/' or '*' or '%' or '=' or '<' or '>' or '+' or '-' or '?' or '.' or '^')
            {
                Add(_i, 1, c == '.' ? TokenClasses.Punctuation : TokenClasses.Operator);
            }

            _i++;
        }

        // A '+' or '-' that signs a number: a digit (or '.' and a digit) after it, and no operand right before it.
        private readonly bool IsSign()
        {
            char next = At(_i + 1);
            if (!char.IsAsciiDigit(next) && !(next == '.' && char.IsAsciiDigit(At(_i + 2))))
            {
                return false;
            }

            return _i == 0 || !(IsNameChar(_l[_i - 1]) || _l[_i - 1] is ')' or ']' or '%');
        }

        private void Name()
        {
            var l = _l;
            int end = NameEnd(_i);
            var name = l[_i..end];
            char after = At(end);
            if (after == '(')
            {
                if (name.Equals("url", StringComparison.OrdinalIgnoreCase) && At(SkipBlanks(end + 1)) is not ('"' or '\''))
                {
                    Add(_i, end - _i, TokenClasses.MethodName);
                    Add(end, 1, TokenClasses.Punctuation);
                    int close = l[(end + 1)..].IndexOf(')');
                    int stop = close < 0 ? l.Length : end + 1 + close;
                    Add(end + 1, stop - end - 1, TokenClasses.String);
                    if (close >= 0)
                    {
                        Add(stop, 1, TokenClasses.Punctuation);
                    }

                    _i = Math.Min(l.Length, stop + 1);
                    return;
                }

                if (StyleNames.FunctionYear(name) <= _t.Year)
                {
                    Add(_i, end - _i, TokenClasses.MethodName);
                }

                _i = end;
                return;
            }

            if (after == '.' && _t._scss && (IsNameStart(end + 1) || At(end + 1) == '$'))
            {
                Add(_i, end - _i, TokenClasses.NamespaceName);
                _i = end;
                return;
            }

            string tokenClass;
            if (name.Length > 1 && name[0] == '-' && name[1] == '-')
            {
                tokenClass = TokenClasses.LocalName;
            }
            else if (_mode == Prelude && _sub != SubMethod && PreludeKeywords.Contains(name))
            {
                tokenClass = TokenClasses.Keyword;
            }
            else if (_mode == Prelude && _nameFirst)
            {
                tokenClass = _sub == SubMethod ? TokenClasses.MethodName : TokenClasses.ClassName;
                _nameFirst = false;
            }
            else if (_mode == Prelude && _sub == SubLayer)
            {
                tokenClass = TokenClasses.NamespaceName;
            }
            else if (_paren > 0 && At(SkipBlanks(end)) == ':' && At(SkipBlanks(end) + 1) != ':')
            {
                tokenClass = TokenClasses.PropertyName;
            }
            else if (WideKeywords.Contains(name) || ((_t._scss || _t._less) && ScriptKeywords.Contains(name)))
            {
                tokenClass = TokenClasses.Keyword;
            }
            else if (StyleNames.IsColor(name))
            {
                tokenClass = TokenClasses.Number;
            }
            else
            {
                tokenClass = TokenClasses.EnumMemberName;
            }

            Add(_i, end - _i, tokenClass);
            _i = end;
        }
    }
}
