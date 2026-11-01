// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Web.Style;

/// <summary>
/// The style languages' built-in Format document (CSS, SCSS, LESS): each declaration and nested rule on its own line,
/// indented by the editor's step per block; one selector of a list per line; one space before '{', after a declaration's
/// ':' and after a comma in a value; none before ';' or ','; a run of white space inside a selector or a value made one
/// space, a value's own line breaks kept (indented one step past the declaration, or by its parentheses: SCSS maps);
/// one empty line between statements kept where there was one; comments kept where they were (after a token on its line,
/// or on their own line); <c>} @else</c> on one line. Only the white space between tokens changes (never inside a
/// string, a comment, an interpolation or an unquoted <c>url()</c>, and never where a space would change a selector), so
/// the caret keeps its place. A text with an unclosed block, parenthesis, string, comment or interpolation, or a close
/// with nothing open, is left as it is, the message naming the line and column. .sass (indented) is not formatted.
/// </summary>
public sealed class StyleFormatter : ITextFormatter
{
    private enum Kind : byte
    {
        Open,
        Close,
        Semi,
        Comma,
        Colon,
        ParenOpen,
        ParenClose,
        Word,
        String,
        BlockComment,
        LineComment,
    }

    private enum Statement : byte
    {
        None,
        Rule,
        AtBlock,
        AtStatement,
        Declaration,
        Other,
    }

    private readonly record struct Token(int Start, int End, Kind Kind);

    private readonly bool _scss;
    private readonly bool _less;
    private readonly bool _lineComments;

    /// <summary>A formatter of <paramref name="dialect"/> (CSS, SCSS or LESS).</summary>
    /// <exception cref="ArgumentException">The dialect is Sass's indented syntax, which this formatter does not format.</exception>
    public StyleFormatter(StyleDialect dialect)
    {
        if (dialect == StyleDialect.Sass)
        {
            throw new ArgumentException("Sass's indented syntax (.sass) is not formatted: its line breaks and indentation are its syntax.", nameof(dialect));
        }

        Dialect = dialect;
        _scss = dialect == StyleDialect.Scss;
        _less = dialect == StyleDialect.Less;
        _lineComments = dialect != StyleDialect.Css;
    }

    /// <summary>Its dialect.</summary>
    public StyleDialect Dialect { get; }

    /// <inheritdoc />
    public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(edits);
        var tokens = Lex(text);
        int count = tokens.Count;
        if (count == 0)
        {
            return;
        }

        var statements = new Statement[count];
        var real = new bool[count];
        int[] colons = Structure(text, tokens, statements, real);
        int first = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        int depth = 0;
        int paren = 0;
        int colon = 0;
        for (int k = 0; k <= count; k++)
        {
            int gapStart = k == 0 ? first : tokens[k - 1].End;
            int gapEnd = k == count ? text.Length : tokens[k].Start;
            var gap = text[gapStart..gapEnd];
            string wanted;
            if (k == 0)
            {
                wanted = "";
            }
            else if (k == count)
            {
                wanted = gap.ContainsAny('\n', '\r') ? style.NewLine : "";
            }
            else
            {
                while (colon < colons.Length && colons[colon] < k - 1)
                {
                    colon++;
                }

                bool prevColon = colon < colons.Length && colons[colon] == k - 1;
                bool curColon = (colon < colons.Length && colons[colon] == k) || (colon + 1 < colons.Length && colons[colon + 1] == k);
                wanted = Gap(text, tokens, statements, real, k, gap, depth, paren, prevColon, curColon, style);
            }

            if (!gap.SequenceEqual(wanted))
            {
                edits.Add(new TextEdit(gapStart, gapEnd - gapStart, wanted));
            }

            if (k < count)
            {
                var token = tokens[k];
                if (real[k])
                {
                    depth += token.Kind == Kind.Open ? 1 : -1;
                }
                else if (token.Kind == Kind.ParenOpen)
                {
                    paren++;
                }
                else if (token.Kind == Kind.ParenClose)
                {
                    paren--;
                }
            }
        }
    }

    // The white space wanted before token k, `depth` blocks and `paren` parentheses deep after the token before it.
    private string Gap(ReadOnlySpan<char> text, List<Token> tokens, Statement[] statements, bool[] real, int k, ReadOnlySpan<char> gap, int depth, int paren,
        bool prevIsColon, bool curIsColon, FormatStyle style)
    {
        var prev = tokens[k - 1];
        var cur = tokens[k];
        bool newline = gap.ContainsAny('\n', '\r');
        int blank = BlankLines(gap);
        var statement = statements[k];
        bool rule = statement == Statement.Rule;
        bool curComment = cur.Kind is Kind.BlockComment or Kind.LineComment;

        if (cur.Kind == Kind.Close && real[k])
        {
            return prev.Kind == Kind.Open && real[k - 1] ? "" : style.Break(depth - 1);
        }

        if (prev.Kind == Kind.LineComment)
        {
            return statement == Statement.None ? style.Break(depth, cur.Kind is Kind.Semi or Kind.Comma ? 0 : blank) : style.Break(Continuation(depth, paren, rule, cur));
        }

        if (cur.Kind is Kind.Semi or Kind.Comma)
        {
            return "";
        }

        bool prevEnds = (prev.Kind == Kind.Semi && paren == 0) || (prev.Kind is Kind.Open or Kind.Close && real[k - 1])
            || (prev.Kind is Kind.BlockComment && statements[k - 1] == Statement.None);
        if (curComment && !newline)
        {
            return gap.IsEmpty && !prevEnds ? "" : " ";
        }

        if (prev.Kind == Kind.Open && real[k - 1])
        {
            return style.Break(depth);
        }

        if (cur.Kind == Kind.Open && real[k])
        {
            return " ";
        }

        if (curComment)
        {
            return statement == Statement.None ? style.Break(depth, blank) : style.Break(Continuation(depth, paren, rule, cur));
        }

        if (prevEnds)
        {
            if (prev.Kind == Kind.Close && _scss && cur.Kind == Kind.Word && text[cur.Start..cur.End].Equals("@else", StringComparison.OrdinalIgnoreCase))
            {
                return " ";
            }

            if (prev.Kind == Kind.BlockComment && !newline)
            {
                return gap.IsEmpty ? "" : " ";
            }

            return style.Break(depth, blank);
        }

        if (prevIsColon)
        {
            return newline ? style.Break(Continuation(depth, paren, rule, cur)) : " ";
        }

        if (curIsColon)
        {
            return "";
        }

        if (prev.Kind == Kind.Comma)
        {
            if (rule && paren == 0)
            {
                return style.Break(depth);
            }

            return newline ? style.Break(Continuation(depth, paren, rule, cur)) : " ";
        }

        if (prev.Kind == Kind.ParenOpen)
        {
            return newline ? style.Break(Continuation(depth, paren, rule, cur)) : "";
        }

        if (cur.Kind == Kind.ParenClose)
        {
            return newline ? style.Break(Continuation(depth, paren, rule, cur)) : "";
        }

        if (gap.IsEmpty)
        {
            return "";
        }

        if (!newline || (rule && paren == 0))
        {
            return " ";
        }

        return style.Break(Continuation(depth, paren, rule, cur));
    }

    // A continued line's indentation: a value one step past its declaration, or as deep as its parentheses.
    private static int Continuation(int depth, int paren, bool rule, Token cur)
    {
        int parens = cur.Kind == Kind.ParenClose ? paren - 1 : paren;
        return rule ? depth + Math.Max(0, parens) : depth + (cur.Kind == Kind.ParenClose ? Math.Max(0, parens) : Math.Max(1, parens));
    }

    private static int BlankLines(ReadOnlySpan<char> gap)
    {
        int lines = 0;
        for (int i = 0; i < gap.Length; i++)
        {
            if (gap[i] == '\n' || (gap[i] == '\r' && (i + 1 >= gap.Length || gap[i + 1] != '\n')))
            {
                lines++;
            }
        }

        return lines >= 2 ? 1 : 0;
    }

    // The statements: each token's statement kind, which braces are blocks (not inside parentheses: a LESS ruleset passed
    // to a mixin), and the declarations' colons (the first ':' outside parentheses of a declaration), in order.
    private int[] Structure(ReadOnlySpan<char> text, List<Token> tokens, Statement[] statements, bool[] real)
    {
        var colons = new List<int>();
        var blocks = new Stack<int>();
        var parens = new Stack<int>();
        int count = tokens.Count;
        int k = 0;
        while (k < count)
        {
            var token = tokens[k];
            switch (token.Kind)
            {
                case Kind.BlockComment or Kind.LineComment:
                    statements[k++] = Statement.None;
                    continue;
                case Kind.Close:
                    if (blocks.Count == 0)
                    {
                        throw ITextFormatter.Malformed(text, token.Start, "a '}' closes nothing");
                    }

                    blocks.Pop();
                    real[k] = true;
                    statements[k++] = Statement.None;
                    continue;
                case Kind.Open:
                    blocks.Push(token.Start);
                    real[k] = true;
                    statements[k++] = Statement.None;
                    continue;
                case Kind.Semi:
                    statements[k++] = Statement.None;
                    continue;
            }

            int start = k;
            int braces = 0;
            int end = k;
            for (; end < count; end++)
            {
                var t = tokens[end];
                if (t.Kind == Kind.ParenOpen)
                {
                    parens.Push(end);
                }
                else if (t.Kind == Kind.ParenClose)
                {
                    if (parens.Count == 0)
                    {
                        throw ITextFormatter.Malformed(text, t.Start, $"a '{text[t.Start]}' closes nothing");
                    }

                    var open = tokens[parens.Pop()];
                    if ((text[open.Start] == '(') != (text[t.Start] == ')'))
                    {
                        throw ITextFormatter.Malformed(text, t.Start, $"'{text[t.Start]}' closes '{text[open.Start]}'");
                    }
                }
                else if (t.Kind == Kind.Open)
                {
                    if (parens.Count == 0)
                    {
                        break;
                    }

                    braces++;
                }
                else if (t.Kind == Kind.Close)
                {
                    if (parens.Count == 0)
                    {
                        break;
                    }

                    if (braces == 0)
                    {
                        throw ITextFormatter.Malformed(text, tokens[parens.Peek()].Start, $"'{text[tokens[parens.Peek()].Start]}' is not closed");
                    }

                    braces--;
                }
                else if (t.Kind == Kind.Semi && parens.Count == 0)
                {
                    break;
                }
            }

            if (parens.Count > 0)
            {
                throw ITextFormatter.Malformed(text, tokens[parens.Peek()].Start, $"'{text[tokens[parens.Peek()].Start]}' is not closed");
            }

            var kind = Kind_(text, tokens, start, end);
            for (int i = start; i < end; i++)
            {
                statements[i] = kind;
            }

            if (kind == Statement.Declaration)
            {
                int depth = 0;
                for (int i = start; i < end; i++)
                {
                    var t = tokens[i];
                    depth += t.Kind == Kind.ParenOpen ? 1 : t.Kind == Kind.ParenClose ? -1 : 0;
                    if (t.Kind == Kind.Colon && depth == 0)
                    {
                        colons.Add(i);
                        break;
                    }
                }
            }

            k = end;
        }

        if (blocks.Count > 0)
        {
            throw ITextFormatter.Malformed(text, blocks.Peek(), "a '{' is not closed");
        }

        return [.. colons];
    }

    // What the statement from `start` to its end (a '{', ';' or '}' outside parentheses, or the text's end) is.
    private Statement Kind_(ReadOnlySpan<char> text, List<Token> tokens, int start, int end)
    {
        var first = tokens[start];
        bool block = end < tokens.Count && tokens[end].Kind == Kind.Open;
        var word = text[first.Start..first.End];
        if (first.Kind == Kind.Word && word[0] == '@' && !(word.Length > 1 && word[1] == '{'))
        {
            if (_less && start + 1 < end && tokens[start + 1].Kind == Kind.Colon)
            {
                return Statement.Declaration;
            }

            return block ? Statement.AtBlock : Statement.AtStatement;
        }

        if (block)
        {
            return Statement.Rule;
        }

        if (first.Kind != Kind.Word)
        {
            return Statement.Other;
        }

        char c = word[0];
        bool name = char.IsAsciiLetter(c) || c is '-' or '_' or '*' || c >= '\u0080' || (c == '$' && _scss) || (c == '@' && _less)
            || (c == '#' && word.Length > 1 && word[1] == '{');
        return name ? Statement.Declaration : Statement.Other;
    }

    private List<Token> Lex(ReadOnlySpan<char> text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c is ' ' or '\t' or '\r' or '\n' or '\f' or '﻿')
            {
                i++;
                continue;
            }

            int start = i;
            if (c == '/' && next == '*')
            {
                int close = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                {
                    throw ITextFormatter.Malformed(text, i, "a comment is not closed (*/)");
                }

                i += close + 4;
                tokens.Add(new Token(start, i, Kind.BlockComment));
                continue;
            }

            if (c == '/' && next == '/' && _lineComments)
            {
                while (i < text.Length && text[i] is not ('\n' or '\r'))
                {
                    i++;
                }

                tokens.Add(new Token(start, i, Kind.LineComment));
                continue;
            }

            Kind? single = c switch
            {
                '{' => Kind.Open,
                '}' => Kind.Close,
                ';' => Kind.Semi,
                ',' => Kind.Comma,
                ':' => Kind.Colon,
                '(' or '[' => Kind.ParenOpen,
                ')' or ']' => Kind.ParenClose,
                _ => null,
            };
            if (single is { } kind)
            {
                tokens.Add(new Token(i, ++i, kind));
                continue;
            }

            if (c is '"' or '\'')
            {
                i = StringEnd(text, i);
                tokens.Add(new Token(start, i, Kind.String));
                continue;
            }

            if ((c is 'u' or 'U') && text[i..].StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                int arg = i + 4;
                while (arg < text.Length && text[arg] is ' ' or '\t')
                {
                    arg++;
                }

                if (arg < text.Length && text[arg] is not ('"' or '\''))
                {
                    int close = text[arg..].IndexOfAny(')', '\n');
                    if (close < 0 || text[arg + close] != ')')
                    {
                        throw ITextFormatter.Malformed(text, i, "a url( is not closed on its line");
                    }

                    i = arg + close + 1;
                    tokens.Add(new Token(start, i, Kind.Word));
                    continue;
                }
            }

            while (i < text.Length)
            {
                c = text[i];
                next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (next == '{' && ((c == '#' && _scss) || (c == '@' && _less)))
                {
                    i = InterpolationEnd(text, i);
                    continue;
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }

                if (c is ' ' or '\t' or '\r' or '\n' or '\f' or '{' or '}' or ';' or ',' or ':' or '(' or ')' or '[' or ']' or '"' or '\''
                    || (c == '/' && (next == '*' || (next == '/' && _lineComments))))
                {
                    break;
                }

                i++;
            }

            if (i == start)
            {
                i++;
            }

            tokens.Add(new Token(start, i, Kind.Word));
        }

        return tokens;
    }

    // From an opening quote: the index after its close (a '\' escapes the next character, a line break too).
    private int StringEnd(ReadOnlySpan<char> text, int start)
    {
        char quote = text[start];
        int i = start + 1;
        while (i < text.Length && text[i] != quote)
        {
            char c = text[i];
            if (c is '\n' or '\r')
            {
                throw ITextFormatter.Malformed(text, start, "a string is not closed on its line");
            }

            if (c == '\\')
            {
                i += i + 1 < text.Length && text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 3 : 2;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '{' && ((c == '#' && _scss) || (c == '@' && _less)))
            {
                i = InterpolationEnd(text, i);
                continue;
            }

            i++;
        }

        if (i >= text.Length)
        {
            throw ITextFormatter.Malformed(text, start, "a string is not closed");
        }

        return i + 1;
    }

    // From an interpolation's '#' (or LESS's '@'): the index after its closing '}' (nested braces and strings skipped).
    private int InterpolationEnd(ReadOnlySpan<char> text, int start)
    {
        int depth = 0;
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i = StringEnd(text, i);
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i + 1;
            }

            i++;
        }

        throw ITextFormatter.Malformed(text, start, "an interpolation is not closed");
    }
}
