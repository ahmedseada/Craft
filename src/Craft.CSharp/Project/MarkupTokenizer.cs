// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.CSharp.Project;

/// <summary>Which markup a <see cref="MarkupTokenizer"/> reads.</summary>
internal enum MarkupDialect
{
    /// <summary>XML (.xml, .xaml, .config, .resx, .slnx).</summary>
    Xml,

    /// <summary>MSBuild's XML (.csproj, .props, .targets): <c>$(Property)</c>, <c>@(Item)</c> and <c>%(Metadata)</c> coloured too.</summary>
    MsBuild,

    /// <summary>Razor (.razor, .cshtml): HTML with its C# (<c>@code { }</c>, <c>@{ }</c>, <c>@expr</c>, <c>@( )</c>, <c>@if</c>, directives) coloured as C#.</summary>
    Razor,
}

/// <summary>
/// XML's colours by lines (and MSBuild's and Razor's on the same reader): element names as keywords (a namespace prefix as
/// a namespace, a Razor component as a class), attribute names as properties, values as strings, entities as escapes,
/// comments, CDATA, processing instructions and the doctype. Its state across lines is where the markup is (in a comment,
/// a tag, a value) and, for Razor, the C# it is in (<see cref="CSharpLexer"/>'s state, the braces of the block, whether a
/// block's brace is still to come). Razor's markup inside a C# block is taken a line at a time: a line of the block that
/// starts with a tag (or <c>@:</c>) is markup, every other line C#.
/// </summary>
internal sealed class MarkupTokenizer(MarkupDialect dialect) : LineTokenizer
{
    private const int Content = 0;
    private const int Comment = 1;
    private const int CData = 2;
    private const int Tag = 3;
    private const int ValueDouble = 4;
    private const int ValueSingle = 5;
    private const int Doctype = 6;
    private const int RazorComment = 7;

    private const ulong PiFlag = 1UL << 4;
    private const ulong PendingFlag = 1UL << 5;
    private const ulong ElseFlag = 1UL << 6;

    private static readonly SearchValues<char> XmlContent = SearchValues.Create("<&");
    private static readonly SearchValues<char> MsBuildContent = SearchValues.Create("<&$@%");
    private static readonly SearchValues<char> RazorContent = SearchValues.Create("<&@");

    private static readonly FrozenSet<string> BlockDirectives = FrozenSet.Create(StringComparer.Ordinal, ["code", "functions", "section"]);
    private static readonly FrozenSet<string> LineDirectives = FrozenSet.Create(StringComparer.Ordinal,
    [
        "using", "inject", "page", "model", "namespace", "inherits", "implements", "layout", "attribute", "typeparam", "rendermode",
        "preservewhitespace", "addTagHelper", "removeTagHelper", "tagHelperPrefix",
    ]);

    private static readonly FrozenSet<string> ControlBlocks = FrozenSet.Create(StringComparer.Ordinal, ["if", "for", "foreach", "while", "switch", "lock", "try", "do"]);
    private static readonly FrozenSet<string> Continuations = FrozenSet.Create(StringComparer.Ordinal, ["else", "catch", "finally"]);
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> BlockDirectiveLookup = BlockDirectives.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> LineDirectiveLookup = LineDirectives.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ControlBlockLookup = ControlBlocks.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ContinuationLookup = Continuations.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly SearchValues<char> _content = dialect switch
    {
        MarkupDialect.MsBuild => MsBuildContent,
        MarkupDialect.Razor => RazorContent,
        _ => XmlContent,
    };

    private bool IsRazor => dialect == MarkupDialect.Razor;

    /// <summary>Which markup it reads.</summary>
    public MarkupDialect Dialect => dialect;

    /// <inheritdoc />
    /// <remarks>X part 9: its tags link (a start tag's name renamed with its end tag's).</remarks>
    public override bool LinksTags => true;

    /// <inheritdoc />
    /// <remarks>Razor's HTML has void elements (br, img, input, …); XML and MSBuild have none.</remarks>
    public override bool IsVoidElement(ReadOnlySpan<char> name) => IsRazor && MarkupTags.IsHtmlVoid(name);

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        var s = new State(state);
        if (IsRazor && s.InCode && s.Lex == CSharpLexer.Normal && StartsMarkup(line, out int markupAt))
        {
            // A line of a C# block that is markup: classified as markup; the block goes on below it.
            var inner = new State(0);
            if (line[markupAt] == '@')
            {
                Add(into, lineStart + markupAt, 2, TokenClasses.PreprocessorKeyword);
                return state;
            }

            Markup(line, markupAt, lineStart, ref inner, into);
            return state;
        }

        int i = 0;
        while (i < line.Length)
        {
            if (IsRazor && s.InCode)
            {
                i = Code(line, i, lineStart, ref s, into);
                continue;
            }

            i = Markup(line, i, lineStart, ref s, into);
        }

        return s.Pack();
    }

    // Razor's C# from i: lexed to the brace that closes the block (back to markup after it) or the line's end.
    private static int Code(ReadOnlySpan<char> line, int i, int lineStart, ref State s, TokenSpanBuffer? into)
    {
        int lex = s.Lex;
        int quotes = s.Quotes;
        int braces = s.Braces;
        int parens = 0;
        int at = CSharpLexer.Lex(line, i, line.Length, lineStart, ref lex, ref quotes, ref braces, ref parens, stopOnBraces: true, stopOnParens: false, out bool closed, into);
        s.Lex = lex;
        s.Quotes = quotes;
        s.Braces = braces;
        if (braces > 0)
        {
            s.Pending = false;
        }

        if (!closed)
        {
            return line.Length;
        }

        // The block's closing brace: back to markup, unless the statement goes on (} else {, } catch, } finally).
        Add(into, lineStart + at, 1, TokenClasses.PreprocessorKeyword);
        s.Braces = 0;
        s.Pending = false;
        s.Markup = Content;
        int next = at + 1;
        while (next < line.Length && line[next] is ' ' or '\t')
        {
            next++;
        }

        int word = WordEnd(line, next);
        if (word > next && ContinuationLookup.Contains(line[next..word]))
        {
            Add(into, lineStart + next, word - next, TokenClasses.ControlKeyword);
            s.Pending = true;
            return word;
        }

        s.Else = next >= line.Length;
        return at + 1;
    }

    // Markup from i, in s's markup state, to the line's end or the start of Razor's C#.
    private int Markup(ReadOnlySpan<char> line, int i, int lineStart, ref State s, TokenSpanBuffer? into)
    {
        bool afterEquals = false;
        while (i < line.Length)
        {
            if (IsRazor && s.InCode)
            {
                return i;
            }

            switch (s.Markup)
            {
                case Comment:
                    i = Until(line, i, "-->", lineStart, TokenClasses.Comment, ref s, into);
                    continue;
                case RazorComment:
                    i = Until(line, i, "*@", lineStart, TokenClasses.Comment, ref s, into);
                    continue;
                case CData:
                {
                    int found = line[i..].IndexOf("]]>", StringComparison.Ordinal);
                    if (found < 0)
                    {
                        Add(into, lineStart + i, line.Length - i, TokenClasses.String);
                        return line.Length;
                    }

                    Add(into, lineStart + i, found, TokenClasses.String);
                    Add(into, lineStart + i + found, 3, TokenClasses.Punctuation);
                    s.Markup = Content;
                    i += found + 3;
                    continue;
                }

                case Doctype:
                {
                    int close = line[i..].IndexOf('>');
                    if (close < 0)
                    {
                        return line.Length;
                    }

                    Add(into, lineStart + i + close, 1, TokenClasses.Punctuation);
                    s.Markup = Content;
                    i += close + 1;
                    continue;
                }

                case ValueDouble or ValueSingle:
                    i = Value(line, i, s.Markup == ValueDouble ? '"' : '\'', lineStart, ref s, into);
                    continue;
                case Tag:
                    i = InTag(line, i, lineStart, ref s, ref afterEquals, into);
                    continue;
                default:
                    i = InContent(line, i, lineStart, ref s, into);
                    continue;
            }
        }

        return i;
    }

    private int InContent(ReadOnlySpan<char> line, int i, int lineStart, ref State s, TokenSpanBuffer? into)
    {
        if (s.Else)
        {
            // After a Razor block closed at a line's end: "else" (or catch, finally) at the next text goes on in C#.
            int first = i;
            while (first < line.Length && line[first] is ' ' or '\t')
            {
                first++;
            }

            if (first < line.Length)
            {
                s.Else = false;
                int word = WordEnd(line, first);
                if (word > first && ContinuationLookup.Contains(line[first..word]))
                {
                    Add(into, lineStart + first, word - first, TokenClasses.ControlKeyword);
                    s.Pending = true;
                    return word;
                }
            }
        }

        int at = line[i..].IndexOfAny(_content);
        if (at < 0)
        {
            return line.Length;
        }

        i += at;
        char c = line[i];
        switch (c)
        {
            case '&':
                return Entity(line, i, lineStart, into);
            case '<':
                return OpenTag(line, i, lineStart, ref s, into);
            case '@' when IsRazor:
                return Transition(line, i, lineStart, ref s, into, blocks: true);
            default:
                // MSBuild's $(Property), @(Item), %(Metadata) in an element's text.
                return Reference(line, i, lineStart, into) is int after && after > i ? after : i + 1;
        }
    }

    private int OpenTag(ReadOnlySpan<char> line, int i, int lineStart, ref State s, TokenSpanBuffer? into)
    {
        var rest = line[i..];
        if (rest.StartsWith("<!--"))
        {
            int close = rest[4..].IndexOf("-->", StringComparison.Ordinal);
            if (close < 0)
            {
                Add(into, lineStart + i, rest.Length, TokenClasses.Comment);
                s.Markup = Comment;
                return line.Length;
            }

            Add(into, lineStart + i, close + 7, TokenClasses.Comment);
            return i + close + 7;
        }

        if (rest.StartsWith("<![CDATA["))
        {
            Add(into, lineStart + i, 9, TokenClasses.Punctuation);
            s.Markup = CData;
            return i + 9;
        }

        if (rest.StartsWith("<?"))
        {
            Add(into, lineStart + i, 2, TokenClasses.Punctuation);
            int end = NameEnd(line, i + 2);
            Add(into, lineStart + i + 2, end - i - 2, TokenClasses.PreprocessorKeyword);
            s.Markup = Tag;
            s.Pi = true;
            return end;
        }

        if (rest.StartsWith("<!"))
        {
            Add(into, lineStart + i, 2, TokenClasses.Punctuation);
            int end = NameEnd(line, i + 2);
            Add(into, lineStart + i + 2, end - i - 2, TokenClasses.Keyword);
            s.Markup = Doctype;
            return end;
        }

        int name = rest.StartsWith("</") ? i + 2 : i + 1;
        if (name >= line.Length || !IsNameStart(line[name]))
        {
            return i + 1;                                    // a lone '<': text (a malformed file colours what it can)
        }

        Add(into, lineStart + i, name - i, TokenClasses.Punctuation);
        int nameEnd = NameEnd(line, name);
        QualifiedName(line, name, nameEnd, lineStart, IsRazor && char.IsUpper(line[name]) ? TokenClasses.ClassName : TokenClasses.Keyword, into);
        s.Markup = Tag;
        return nameEnd;
    }

    private int InTag(ReadOnlySpan<char> line, int i, int lineStart, ref State s, ref bool afterEquals, TokenSpanBuffer? into)
    {
        char c = line[i];
        char next = i + 1 < line.Length ? line[i + 1] : '\0';
        if (c is ' ' or '\t')
        {
            return i + 1;
        }

        if (c == '>' || (c == '/' && next == '>') || (s.Pi && c == '?' && next == '>'))
        {
            int length = c == '>' ? 1 : 2;
            Add(into, lineStart + i, length, TokenClasses.Punctuation);
            s.Markup = Content;
            s.Pi = false;
            afterEquals = false;
            return i + length;
        }

        if (c == '=')
        {
            Add(into, lineStart + i, 1, TokenClasses.Punctuation);
            afterEquals = true;
            return i + 1;
        }

        if (c is '"' or '\'')
        {
            s.Markup = c == '"' ? ValueDouble : ValueSingle;
            afterEquals = false;
            return Value(line, i + 1, c, lineStart, ref s, into, i);
        }

        if (afterEquals)
        {
            // HTML's unquoted value (Razor): to the next space or the tag's end; "@x" is C#.
            afterEquals = false;
            if (IsRazor && c == '@')
            {
                return Transition(line, i, lineStart, ref s, into, blocks: false);
            }

            int end = i;
            while (end < line.Length && line[end] is not (' ' or '\t' or '>'))
            {
                end++;
            }

            Add(into, lineStart + i, end - i, TokenClasses.String);
            return end;
        }

        if (IsNameStart(c) || (IsRazor && c == '@'))
        {
            int start = c == '@' ? i + 1 : i;
            int end = NameEnd(line, start);
            if (c == '@')
            {
                Add(into, lineStart + i, end - i, TokenClasses.Keyword);  // a directive attribute: @onclick, @bind-Value
            }
            else
            {
                QualifiedName(line, start, end, lineStart, TokenClasses.PropertyName, into);
            }

            return Math.Max(end, i + 1);
        }

        return i + 1;
    }

    // An attribute's value from i (after its opening quote) to its closing quote, or the line's end (the value goes on).
    private int Value(ReadOnlySpan<char> line, int i, char quote, int lineStart, ref State s, TokenSpanBuffer? into, int from = -1)
    {
        from = from < 0 ? i : from;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == quote)
            {
                Add(into, lineStart + from, i + 1 - from, TokenClasses.String);
                s.Markup = Tag;
                return i + 1;
            }

            if (c == '&' || (dialect == MarkupDialect.MsBuild && c is '$' or '@' or '%') || (IsRazor && c == '@' && (i == 0 || !char.IsLetterOrDigit(line[i - 1]))))
            {
                int after = c == '&' ? Entity(line, i, lineStart, null) : IsRazor ? -1 : Reference(line, i, lineStart, null);
                if (IsRazor && c == '@')
                {
                    Add(into, lineStart + from, i - from, TokenClasses.String);
                    var inner = s;
                    int end = Transition(line, i, lineStart, ref inner, into, blocks: false, stopAt: quote);
                    from = i = Math.Max(end, i + 1);
                    continue;
                }

                if (after > i)
                {
                    Add(into, lineStart + from, i - from, TokenClasses.String);
                    if (c == '&')
                    {
                        Entity(line, i, lineStart, into);
                    }
                    else
                    {
                        Reference(line, i, lineStart, into);
                    }

                    from = i = after;
                    continue;
                }
            }

            i++;
        }

        Add(into, lineStart + from, line.Length - from, TokenClasses.String);
        return line.Length;
    }

    // Razor's '@' at i: an escaped "@@", a comment, a block (@{ }, @code { }, @if ( ) { }), a directive's line, @( ) or @expr.
    private static int Transition(ReadOnlySpan<char> line, int i, int lineStart, ref State s, TokenSpanBuffer? into, bool blocks, char stopAt = '\0')
    {
        char next = i + 1 < line.Length ? line[i + 1] : '\0';
        if (i > 0 && char.IsLetterOrDigit(line[i - 1]))
        {
            return i + 1;                                    // an e-mail address: name@host
        }

        if (next == '@')
        {
            return i + 2;                                    // "@@" is an '@'
        }

        if (next == '*')
        {
            int close = line[(i + 2)..].IndexOf("*@", StringComparison.Ordinal);
            if (close < 0)
            {
                Add(into, lineStart + i, line.Length - i, TokenClasses.Comment);
                s.Markup = RazorComment;
                return line.Length;
            }

            Add(into, lineStart + i, close + 4, TokenClasses.Comment);
            return i + close + 4;
        }

        if (next == '{' && blocks)
        {
            Add(into, lineStart + i, 2, TokenClasses.PreprocessorKeyword);
            s.Braces = 1;
            return i + 2;
        }

        if (next == '(')
        {
            Add(into, lineStart + i, 1, TokenClasses.PreprocessorKeyword);
            return Explicit(line, i + 1, lineStart, into);
        }

        int wordEnd = WordEnd(line, i + 1);
        if (wordEnd == i + 1)
        {
            return i + 1;
        }

        var word = line[(i + 1)..wordEnd];
        if (blocks && (BlockDirectiveLookup.Contains(word) || ControlBlockLookup.Contains(word) || (word.SequenceEqual("using") && NextIs(line, wordEnd, '('))))
        {
            bool control = !BlockDirectiveLookup.Contains(word);
            Add(into, lineStart + i, wordEnd - i, control ? TokenClasses.ControlKeyword : TokenClasses.Keyword);
            s.Pending = true;
            return wordEnd;
        }

        if (blocks && LineDirectiveLookup.Contains(word) && OnlySpaceBefore(line, i))
        {
            Add(into, lineStart + i, wordEnd - i, TokenClasses.Keyword);
            int lex = 0, quotes = 0, braces = 0, parens = 0;
            CSharpLexer.Lex(line, wordEnd, line.Length, lineStart, ref lex, ref quotes, ref braces, ref parens, false, false, out _, into);
            return line.Length;
        }

        // An implicit expression: @name, @a.b.c, @item.Name(x)[0], @await Load().
        Add(into, lineStart + i, 1, TokenClasses.PreprocessorKeyword);
        int end = ImplicitEnd(line, i + 1, stopAt);
        int state = 0, q = 0, b = 0, p = 0;
        CSharpLexer.Lex(line, i + 1, end, lineStart, ref state, ref q, ref b, ref p, false, false, out _, into);
        return end;
    }

    // @( ... ): C# to the matching parenthesis on the line.
    private static int Explicit(ReadOnlySpan<char> line, int open, int lineStart, TokenSpanBuffer? into)
    {
        Add(into, lineStart + open, 1, TokenClasses.PreprocessorKeyword);
        int state = 0, quotes = 0, braces = 0, parens = 1;
        int at = CSharpLexer.Lex(line, open + 1, line.Length, lineStart, ref state, ref quotes, ref braces, ref parens, false, true, out bool closed, into);
        if (!closed)
        {
            return line.Length;
        }

        Add(into, lineStart + at, 1, TokenClasses.PreprocessorKeyword);
        return at + 1;
    }

    // Where @expr's expression ends: names joined by '.' or "?.", with balanced ( ) and [ ] after them; "await " before.
    private static int ImplicitEnd(ReadOnlySpan<char> line, int i, char stopAt)
    {
        int j = WordEnd(line, i);
        if (line[i..j].SequenceEqual("await") && j < line.Length && line[j] == ' ')
        {
            j = WordEnd(line, j + 1);
        }

        while (j < line.Length)
        {
            char c = line[j];
            char next = j + 1 < line.Length ? line[j + 1] : '\0';
            if (c == '.' && IsWordStart(next))
            {
                j = WordEnd(line, j + 1);
            }
            else if (c == '?' && next == '.' && j + 2 < line.Length && IsWordStart(line[j + 2]))
            {
                j = WordEnd(line, j + 2);
            }
            else if (c is '(' or '[' && c != stopAt)
            {
                int close = Balanced(line, j);
                if (close < 0)
                {
                    break;
                }

                j = close + 1;
            }
            else
            {
                break;
            }
        }

        return j;
    }

    // The index of the bracket that closes the one at `open`, strings skipped; -1 when it does not close on the line.
    private static int Balanced(ReadOnlySpan<char> line, int open)
    {
        char opening = line[open];
        char closing = opening == '(' ? ')' : ']';
        int depth = 0;
        for (int j = open; j < line.Length; j++)
        {
            char c = line[j];
            if (c == '"')
            {
                j++;
                while (j < line.Length && line[j] != '"')
                {
                    j += line[j] == '\\' ? 2 : 1;
                }

                continue;
            }

            if (c == opening)
            {
                depth++;
            }
            else if (c == closing && --depth == 0)
            {
                return j;
            }
        }

        return -1;
    }

    // An entity at i ("&amp;", "&#x41;"): its end, or i + 1 when it is not one (classified only when `into` is given).
    private static int Entity(ReadOnlySpan<char> line, int i, int lineStart, TokenSpanBuffer? into)
    {
        int j = i + 1;
        while (j < line.Length && j - i < 32 && (char.IsAsciiLetterOrDigit(line[j]) || line[j] == '#'))
        {
            j++;
        }

        if (j < line.Length && line[j] == ';' && j > i + 1)
        {
            Add(into, lineStart + i, j + 1 - i, TokenClasses.StringEscape);
            return j + 1;
        }

        return i + 1;
    }

    // MSBuild's $(Name), @(Name) or %(Name) at i (also $(Name.Member) and $([System.IO.Path]::Combine(...))); its end, or i when it is not one.
    private int Reference(ReadOnlySpan<char> line, int i, int lineStart, TokenSpanBuffer? into)
    {
        if (dialect != MarkupDialect.MsBuild || i + 1 >= line.Length || line[i + 1] != '(')
        {
            return i;
        }

        int close = Balanced(line, i + 1);
        if (close < 0)
        {
            return i;
        }

        Add(into, lineStart + i, 2, TokenClasses.Punctuation);
        Add(into, lineStart + i + 2, close - i - 2, TokenClasses.ParameterName);
        Add(into, lineStart + close, 1, TokenClasses.Punctuation);
        return close + 1;
    }

    // A name, with its namespace prefix ("x:Name", "d:DesignWidth") coloured as a namespace.
    private static void QualifiedName(ReadOnlySpan<char> line, int start, int end, int lineStart, string tokenClass, TokenSpanBuffer? into)
    {
        int colon = line[start..end].IndexOf(':');
        if (colon > 0)
        {
            Add(into, lineStart + start, colon, TokenClasses.NamespaceName);
            Add(into, lineStart + start + colon, 1, TokenClasses.Punctuation);
            Add(into, lineStart + start + colon + 1, end - start - colon - 1, tokenClass);
            return;
        }

        Add(into, lineStart + start, end - start, tokenClass);
    }

    private static int Until(ReadOnlySpan<char> line, int i, string close, int lineStart, string tokenClass, ref State s, TokenSpanBuffer? into)
    {
        int found = line[i..].IndexOf(close, StringComparison.Ordinal);
        if (found < 0)
        {
            Add(into, lineStart + i, line.Length - i, tokenClass);
            return line.Length;
        }

        Add(into, lineStart + i, found + close.Length, tokenClass);
        s.Markup = Content;
        return i + found + close.Length;
    }

    // Whether a line of a C# block is markup: it starts with a tag ("<div", "</div", "<!--") or Razor's "@:" text.
    private static bool StartsMarkup(ReadOnlySpan<char> line, out int at)
    {
        at = 0;
        while (at < line.Length && line[at] is ' ' or '\t')
        {
            at++;
        }

        if (at + 1 >= line.Length)
        {
            return false;
        }

        char next = line[at + 1];
        return (line[at] == '<' && (char.IsLetter(next) || next is '/' or '!')) || (line[at] == '@' && next == ':');
    }

    private static bool OnlySpaceBefore(ReadOnlySpan<char> line, int i) => line[..i].Trim(" \t").IsEmpty;

    private static bool NextIs(ReadOnlySpan<char> line, int i, char c)
    {
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        return i < line.Length && line[i] == c;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or ':';

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static int NameEnd(ReadOnlySpan<char> line, int i)
    {
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '-' or '.' or ':'))
        {
            i++;
        }

        return i;
    }

    private static int WordEnd(ReadOnlySpan<char> line, int i)
    {
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
        {
            i++;
        }

        return i;
    }

    /// <summary>The state at a line's end, unpacked: the markup's place, Razor's C# (its lexer state, raw quotes, braces), the flags.</summary>
    private struct State(ulong packed)
    {
        public int Markup = (int)(packed & 0xF);
        public bool Pi = (packed & PiFlag) != 0;
        public bool Pending = (packed & PendingFlag) != 0;
        public bool Else = (packed & ElseFlag) != 0;
        public int Lex = (int)((packed >> 8) & 0xF);
        public int Quotes = (int)((packed >> 12) & 0x3F);
        public int Braces = (int)((packed >> 20) & 0xFFFF);

        public readonly bool InCode => Braces > 0 || Pending;

        public readonly ulong Pack() =>
            (uint)(Markup & 0xF) | (Pi ? PiFlag : 0) | (Pending ? PendingFlag : 0) | (Else ? ElseFlag : 0)
            | ((ulong)(uint)(Lex & 0xF) << 8) | ((ulong)(uint)(Quotes & 0x3F) << 12) | ((ulong)(uint)Math.Clamp(Braces, 0, 0xFFFF) << 20);
    }
}
