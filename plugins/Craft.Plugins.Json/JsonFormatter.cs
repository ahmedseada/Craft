// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Json;

/// <summary>
/// JSON's Format document (package.json, tsconfig.json, appsettings.json, …): each member and element on its own line, indented by the
/// editor's step; an empty <c>{}</c> or <c>[]</c> kept on one line; one space after ':'; comments kept where they were
/// (after a value on its line, or on their own line), trailing commas kept, one empty line between members kept where
/// there was one. Only the white space between tokens changes. A text that is not JSON (with comments and trailing
/// commas allowed) is left as it is, the message naming the line and column.
/// </summary>
internal sealed class JsonFormatter : ITextFormatter
{
    private const int MaxDepth = 1024;

    private enum Kind : byte
    {
        Open,
        Close,
        Comma,
        Colon,
        Value,
        LineComment,
        BlockComment,
    }

    private readonly record struct Token(int Start, int End, Kind Kind, char Char);

    /// <inheritdoc />
    public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
    {
        var tokens = Lex(text);
        Check(text, tokens);
        if (tokens.Count == 0)
        {
            return;
        }

        int depth = 0;
        for (int k = 0; k <= tokens.Count; k++)
        {
            int gapStart = k == 0 ? 0 : tokens[k - 1].End;
            int gapEnd = k == tokens.Count ? text.Length : tokens[k].Start;
            var gap = text[gapStart..gapEnd];
            string wanted;
            if (k == 0)
            {
                wanted = "";
            }
            else if (k == tokens.Count)
            {
                wanted = gap.ContainsAny('\n', '\r') ? style.NewLine : "";
            }
            else
            {
                wanted = Gap(tokens[k - 1], tokens[k], gap, depth, style);
            }

            if (!gap.SequenceEqual(wanted))
            {
                edits.Add(new TextEdit(gapStart, gapEnd - gapStart, wanted));
            }

            if (k < tokens.Count)
            {
                depth += tokens[k].Kind == Kind.Open ? 1 : tokens[k].Kind == Kind.Close ? -1 : 0;
            }
        }
    }

    // The white space wanted between prev and cur, at `depth` (the nesting after prev).
    private static string Gap(Token prev, Token cur, ReadOnlySpan<char> gap, int depth, FormatStyle style)
    {
        bool newline = gap.ContainsAny('\n', '\r');
        int blank = BlankLines(gap);
        if (prev.Kind == Kind.LineComment)
        {
            return cur.Kind == Kind.Close ? style.Break(depth - 1) : style.Break(depth, cur.Kind is Kind.Comma ? 0 : blank);
        }

        if (cur.Kind == Kind.Close)
        {
            return prev.Kind == Kind.Open ? "" : style.Break(depth - 1);
        }

        if (cur.Kind is Kind.Comma or Kind.Colon)
        {
            return "";
        }

        bool comment = cur.Kind is Kind.LineComment or Kind.BlockComment;
        if (comment && !newline)
        {
            return " ";                                      // a comment after a token on its line stays there
        }

        return prev.Kind switch
        {
            Kind.Open => style.Break(depth),
            Kind.Colon => " ",
            Kind.Comma => style.Break(depth, blank),
            Kind.BlockComment => newline ? style.Break(depth, blank) : " ",
            _ => comment ? style.Break(depth, blank) : " ",
        };
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

    private static List<Token> Lex(ReadOnlySpan<char> text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c is ' ' or '\t' or '\r' or '\n' or '﻿')
            {
                i++;
                continue;
            }

            int start = i;
            switch (c)
            {
                case '{' or '[':
                    tokens.Add(new Token(i, ++i, Kind.Open, c));
                    continue;
                case '}' or ']':
                    tokens.Add(new Token(i, ++i, Kind.Close, c));
                    continue;
                case ',':
                    tokens.Add(new Token(i, ++i, Kind.Comma, c));
                    continue;
                case ':':
                    tokens.Add(new Token(i, ++i, Kind.Colon, c));
                    continue;
                case '/' when next == '/':
                    while (i < text.Length && text[i] is not ('\n' or '\r'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(start, i, Kind.LineComment, c));
                    continue;
                case '/' when next == '*':
                {
                    int close = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0)
                    {
                        throw Malformed(text, i, "a comment is not closed (*/)");
                    }

                    i += close + 4;
                    tokens.Add(new Token(start, i, Kind.BlockComment, c));
                    continue;
                }

                case '"':
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] is '\n' or '\r')
                        {
                            throw Malformed(text, start, "a string is not closed on its line");
                        }

                        i += text[i] == '\\' ? 2 : 1;
                    }

                    if (i >= text.Length)
                    {
                        throw Malformed(text, start, "a string is not closed");
                    }

                    tokens.Add(new Token(start, ++i, Kind.Value, '"'));
                    continue;
            }

            while (i < text.Length && text[i] is not (' ' or '\t' or '\r' or '\n' or '{' or '}' or '[' or ']' or ',' or ':' or '"' or '/'))
            {
                i++;
            }

            var word = text[start..i];
            if (!(word.SequenceEqual("true") || word.SequenceEqual("false") || word.SequenceEqual("null")
                || double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                throw Malformed(text, start, $"'{word.ToString()}' is not a JSON value");
            }

            tokens.Add(new Token(start, i, Kind.Value, c));
        }

        return tokens;
    }

    // The structure: one value, objects of "name": value members and arrays of values, a comma between (one trailing allowed).
    private static void Check(ReadOnlySpan<char> text, List<Token> tokens)
    {
        int k = Next(tokens, 0);
        if (k >= tokens.Count)
        {
            return;
        }

        k = Value(text, tokens, k, 0);
        k = Next(tokens, k);
        if (k < tokens.Count)
        {
            throw Malformed(text, tokens[k].Start, "more after the document's value");
        }
    }

    private static int Value(ReadOnlySpan<char> text, List<Token> tokens, int k, int depth)
    {
        if (depth > MaxDepth)
        {
            throw Malformed(text, tokens[k].Start, $"nested deeper than {MaxDepth}");
        }

        var token = tokens[k];
        if (token.Kind == Kind.Value)
        {
            return k + 1;
        }

        if (token.Kind != Kind.Open)
        {
            throw Malformed(text, token.Start, $"expected a value, found '{token.Char}'");
        }

        bool isObject = token.Char == '{';
        char close = isObject ? '}' : ']';
        k = Next(tokens, k + 1);
        while (true)
        {
            if (k >= tokens.Count)
            {
                throw Malformed(text, token.Start, $"'{token.Char}' is not closed ('{close}')");
            }

            if (tokens[k].Kind == Kind.Close)
            {
                if (tokens[k].Char != close)
                {
                    throw Malformed(text, tokens[k].Start, $"'{tokens[k].Char}' closes '{token.Char}'");
                }

                return k + 1;
            }

            if (isObject)
            {
                if (tokens[k] is not { Kind: Kind.Value, Char: '"' })
                {
                    throw Malformed(text, tokens[k].Start, "expected a property's name in quotes");
                }

                k = Next(tokens, k + 1);
                if (k >= tokens.Count || tokens[k].Kind != Kind.Colon)
                {
                    throw Malformed(text, k < tokens.Count ? tokens[k].Start : text.Length, "expected ':' after a property's name");
                }

                k = Next(tokens, k + 1);
                if (k >= tokens.Count)
                {
                    throw Malformed(text, text.Length, "expected a property's value");
                }
            }

            k = Next(tokens, Value(text, tokens, k, depth + 1));
            if (k < tokens.Count && tokens[k].Kind == Kind.Comma)
            {
                k = Next(tokens, k + 1);
            }
            else if (k < tokens.Count && tokens[k].Kind != Kind.Close)
            {
                throw Malformed(text, tokens[k].Start, $"expected ',' or '{close}'");
            }
        }
    }

    private static int Next(List<Token> tokens, int k)
    {
        while (k < tokens.Count && tokens[k].Kind is Kind.LineComment or Kind.BlockComment)
        {
            k++;
        }

        return k;
    }

    /// <summary>A malformed text's error: its line and column (from 1) and what is wrong.</summary>
    private static FormatException Malformed(ReadOnlySpan<char> text, int position, string what) => ITextFormatter.Malformed(text, position, what);
}
