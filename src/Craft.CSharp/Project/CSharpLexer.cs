// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.CSharp.Project;

/// <summary>
/// C#'s words, strings, numbers, comments and punctuation, lexed a line at a time with no parse: the colours of Razor's C#
/// (an <c>@code { }</c> block, <c>@expr</c>, <c>@(expr)</c>, a directive's rest of line). Its state across lines is a
/// block comment, a verbatim string, a raw string (with its quote count) or a Razor comment, and the depth of braces.
/// Keywords are found by span in a frozen set, so nothing is allocated per token.
/// </summary>
internal static class CSharpLexer
{
    /// <summary>Not inside anything that spans lines.</summary>
    public const int Normal = 0;

    /// <summary>Inside <c>/* */</c>.</summary>
    public const int BlockComment = 1;

    /// <summary>Inside <c>@" "</c>.</summary>
    public const int Verbatim = 2;

    /// <summary>Inside a raw string (its quote count kept beside).</summary>
    public const int Raw = 3;

    /// <summary>Inside a Razor comment, <c>@* *@</c>.</summary>
    public const int RazorComment = 4;

    private static readonly FrozenSet<string> Keywords = FrozenSet.Create(StringComparer.Ordinal,
    [
        "abstract", "as", "base", "bool", "byte", "char", "checked", "class", "const", "decimal", "delegate", "double", "enum", "event",
        "explicit", "extern", "false", "fixed", "float", "implicit", "in", "int", "interface", "internal", "is", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "this", "true", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "var", "async", "nameof", "record", "get", "set", "init", "partial",
        "where", "dynamic", "global", "required", "file", "scoped", "not", "and", "or", "with", "lock",
    ]);

    private static readonly FrozenSet<string> ControlKeywords = FrozenSet.Create(StringComparer.Ordinal,
    [
        "if", "else", "switch", "case", "default", "for", "foreach", "while", "do", "break", "continue", "return", "goto", "throw",
        "try", "catch", "finally", "yield", "await", "when",
    ]);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup = Keywords.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> ControlLookup = ControlKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Whether <paramref name="word"/> is a C# control keyword (if, foreach, while).</summary>
    public static bool IsControlKeyword(ReadOnlySpan<char> word) => ControlLookup.Contains(word);

    /// <summary>
    /// Lexes <paramref name="line"/> from <paramref name="i"/> to <paramref name="end"/> as C#, from <paramref name="state"/>
    /// (and <paramref name="quotes"/>, a raw string's). <paramref name="braces"/> and <paramref name="parens"/> count the
    /// open braces and parentheses; with <paramref name="stopOnBraces"/> (a Razor block) the lexer stops at the brace that
    /// brings the count back to 0, unclassified, and with <paramref name="stopOnParens"/> (an <c>@( )</c>) at that
    /// parenthesis (<paramref name="closed"/> true, the index of the closing character returned). Otherwise it returns <paramref name="end"/>.
    /// </summary>
    public static int Lex(ReadOnlySpan<char> line, int i, int end, int lineStart, ref int state, ref int quotes, ref int braces, ref int parens,
        bool stopOnBraces, bool stopOnParens, out bool closed, TokenSpanBuffer? into)
    {
        closed = false;
        bool countBraces = stopOnBraces;
        bool countParens = stopOnParens;
        while (i < end)
        {
            switch (state)
            {
                case BlockComment:
                    i = Until(line, i, end, "*/", lineStart, TokenClasses.Comment, ref state, into);
                    continue;
                case RazorComment:
                    i = Until(line, i, end, "*@", lineStart, TokenClasses.Comment, ref state, into);
                    continue;
                case Verbatim:
                    i = VerbatimRest(line, i, end, lineStart, ref state, into);
                    continue;
                case Raw:
                    i = RawRest(line, i, end, lineStart, quotes, ref state, into);
                    continue;
            }

            char c = line[i];
            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }

            char next = i + 1 < end ? line[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                into?.Add(lineStart + i, end - i, TokenClasses.Comment);
                return end;
            }

            if ((c == '/' || c == '@') && next == '*')
            {
                state = c == '/' ? BlockComment : RazorComment;
                i = Until(line, i, end, c == '/' ? "*/" : "*@", lineStart, TokenClasses.Comment, ref state, into, opener: 2);
                continue;
            }

            if (c == '"' || ((c is '$' or '@') && (next == '"' || (next is '$' or '@' && i + 2 < end && line[i + 2] == '"'))))
            {
                i = String(line, i, end, lineStart, ref state, ref quotes, into);
                continue;
            }

            if (c == '\'')
            {
                int j = i + 1;
                while (j < end && line[j] != '\'')
                {
                    j += line[j] == '\\' ? 2 : 1;
                }

                j = Math.Min(end, j + 1);
                into?.Add(lineStart + i, j - i, TokenClasses.String);
                i = j;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(next)))
            {
                int j = i + 1;
                while (j < end && (char.IsAsciiLetterOrDigit(line[j]) || line[j] == '_' || (line[j] == '.' && j + 1 < end && char.IsAsciiDigit(line[j + 1]))))
                {
                    j++;
                }

                into?.Add(lineStart + i, j - i, TokenClasses.Number);
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_' || (c == '@' && (char.IsLetter(next) || next == '_')))
            {
                int j = i + 1;
                while (j < end && (char.IsLetterOrDigit(line[j]) || line[j] == '_'))
                {
                    j++;
                }

                var word = line[i..j];
                if (ControlLookup.Contains(word))
                {
                    into?.Add(lineStart + i, j - i, TokenClasses.ControlKeyword);
                }
                else if (KeywordLookup.Contains(word))
                {
                    into?.Add(lineStart + i, j - i, TokenClasses.Keyword);
                }

                i = j;
                continue;
            }

            switch (c)
            {
                case '{':
                    braces++;
                    into?.Add(lineStart + i, 1, TokenClasses.Punctuation);
                    i++;
                    continue;
                case '}':
                    braces--;
                    if (countBraces && braces <= 0)
                    {
                        braces = 0;
                        closed = true;
                        return i;
                    }

                    into?.Add(lineStart + i, 1, TokenClasses.Punctuation);
                    i++;
                    continue;
                case '(':
                    parens++;
                    into?.Add(lineStart + i, 1, TokenClasses.Punctuation);
                    i++;
                    continue;
                case ')':
                    parens--;
                    if (countParens && parens <= 0)
                    {
                        parens = 0;
                        closed = true;
                        return i;
                    }

                    into?.Add(lineStart + i, 1, TokenClasses.Punctuation);
                    i++;
                    continue;
                case '[' or ']' or ';' or ',' or '.':
                    into?.Add(lineStart + i, 1, TokenClasses.Punctuation);
                    i++;
                    continue;
                case '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>' or '!' or '&' or '|' or '^' or '~' or '?' or ':':
                    into?.Add(lineStart + i, 1, TokenClasses.Operator);
                    i++;
                    continue;
                default:
                    i++;
                    continue;
            }
        }

        return end;
    }

    // A string that starts at i: regular, interpolated, verbatim or raw.
    private static int String(ReadOnlySpan<char> line, int i, int end, int lineStart, ref int state, ref int quotes, TokenSpanBuffer? into)
    {
        int j = i;
        bool verbatim = false;
        while (j < end && line[j] is '$' or '@')
        {
            verbatim |= line[j] == '@';
            j++;
        }

        int run = 0;
        while (j + run < end && line[j + run] == '"')
        {
            run++;
        }

        if (run >= 3)
        {
            into?.Add(lineStart + i, j + run - i, TokenClasses.String);
            quotes = run;
            state = Raw;
            return RawRest(line, j + run, end, lineStart, run, ref state, into);
        }

        if (verbatim)
        {
            into?.Add(lineStart + i, j + 1 - i, TokenClasses.VerbatimString);
            state = Verbatim;
            return VerbatimRest(line, j + 1, end, lineStart, ref state, into);
        }

        int k = j + 1;
        while (k < end && line[k] != '"')
        {
            k += line[k] == '\\' ? 2 : 1;
        }

        k = Math.Min(end, k + 1);
        into?.Add(lineStart + i, k - i, TokenClasses.String);
        return k;
    }

    private static int VerbatimRest(ReadOnlySpan<char> line, int i, int end, int lineStart, ref int state, TokenSpanBuffer? into)
    {
        int k = i;
        while (k < end)
        {
            if (line[k] == '"')
            {
                if (k + 1 < end && line[k + 1] == '"')
                {
                    k += 2;
                    continue;
                }

                into?.Add(lineStart + i, k + 1 - i, TokenClasses.VerbatimString);
                state = Normal;
                return k + 1;
            }

            k++;
        }

        into?.Add(lineStart + i, end - i, TokenClasses.VerbatimString);
        return end;
    }

    private static int RawRest(ReadOnlySpan<char> line, int i, int end, int lineStart, int quotes, ref int state, TokenSpanBuffer? into)
    {
        int k = i;
        while (k < end)
        {
            if (line[k] == '"')
            {
                int run = 0;
                while (k + run < end && line[k + run] == '"')
                {
                    run++;
                }

                if (run >= quotes)
                {
                    into?.Add(lineStart + i, k + run - i, TokenClasses.String);
                    state = Normal;
                    return k + run;
                }

                k += run;
                continue;
            }

            k++;
        }

        into?.Add(lineStart + i, end - i, TokenClasses.String);
        return end;
    }

    // Classifies up to and through `close` as `tokenClass`, leaving `state` Normal when it closes on this line.
    private static int Until(ReadOnlySpan<char> line, int i, int end, string close, int lineStart, string tokenClass, ref int state, TokenSpanBuffer? into, int opener = 0)
    {
        int found = line[(i + opener)..end].IndexOf(close, StringComparison.Ordinal);
        if (found < 0)
        {
            into?.Add(lineStart + i, end - i, tokenClass);
            return end;
        }

        found += opener;
        into?.Add(lineStart + i, found + close.Length, tokenClass);
        state = Normal;
        return i + found + close.Length;
    }
}
