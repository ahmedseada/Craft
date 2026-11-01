// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Plugins.Json;

/// <summary>
/// JSON's colours by lines (package.json, tsconfig.json, appsettings.json, launchSettings.json, global.json, .jsonc):
/// a property's name as a property, strings with their escapes, numbers, true, false and null as keywords, and the
/// comments a reader that allows them takes (<c>//</c> and <c>/* */</c>, the one state that spans lines).
/// </summary>
internal sealed class JsonTokenizer : LineTokenizer
{
    private const ulong InComment = 1;

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        int i = 0;
        while (i < line.Length)
        {
            if (state == InComment)
            {
                int close = line[i..].IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(into, lineStart + i, line.Length - i, TokenClasses.Comment);
                    return InComment;
                }

                Add(into, lineStart + i, close + 2, TokenClasses.Comment);
                i += close + 2;
                state = 0;
                continue;
            }

            char c = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';
            if (c is ' ' or '\t')
            {
                i++;
            }
            else if (c == '/' && next == '/')
            {
                Add(into, lineStart + i, line.Length - i, TokenClasses.Comment);
                return 0;
            }
            else if (c == '/' && next == '*')
            {
                int close = line[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(into, lineStart + i, line.Length - i, TokenClasses.Comment);
                    return InComment;
                }

                Add(into, lineStart + i, close + 4, TokenClasses.Comment);
                i += close + 4;
            }
            else if (c == '"')
            {
                i = String(line, i, lineStart, into);
            }
            else if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                Add(into, lineStart + i, 1, TokenClasses.Punctuation);
                i++;
            }
            else if (char.IsAsciiDigit(c) || c is '-' or '+' or '.')
            {
                int j = i + 1;
                while (j < line.Length && (char.IsAsciiLetterOrDigit(line[j]) || line[j] is '.' or '-' or '+'))
                {
                    j++;
                }

                Add(into, lineStart + i, j - i, TokenClasses.Number);
                i = j;
            }
            else if (char.IsAsciiLetter(c))
            {
                int j = i + 1;
                while (j < line.Length && char.IsAsciiLetterOrDigit(line[j]))
                {
                    j++;
                }

                var word = line[i..j];
                if (word.SequenceEqual("true") || word.SequenceEqual("false") || word.SequenceEqual("null"))
                {
                    Add(into, lineStart + i, j - i, TokenClasses.Keyword);
                }

                i = j;
            }
            else
            {
                i++;
            }
        }

        return state;
    }

    // A string from its opening quote: a name when a ':' follows it, else a value; its escapes as escapes.
    private static int String(ReadOnlySpan<char> line, int i, int lineStart, TokenSpanBuffer? into)
    {
        int end = i + 1;
        while (end < line.Length && line[end] != '"')
        {
            end += line[end] == '\\' ? 2 : 1;
        }

        end = Math.Min(line.Length, end + 1);
        int after = end;
        while (after < line.Length && line[after] is ' ' or '\t')
        {
            after++;
        }

        string tokenClass = after < line.Length && line[after] == ':' ? TokenClasses.PropertyName : TokenClasses.String;
        if (into is null)
        {
            return end;
        }

        int from = i;
        for (int j = i + 1; j < end - 1; j++)
        {
            if (line[j] != '\\')
            {
                continue;
            }

            int length = j + 1 < end && line[j + 1] == 'u' ? 6 : 2;
            length = Math.Min(length, end - j);
            into.Add(lineStart + from, j - from, tokenClass);
            into.Add(lineStart + j, length, TokenClasses.StringEscape);
            j += length - 1;
            from = j + 1;
        }

        into.Add(lineStart + from, end - from, tokenClass);
        return end;
    }
}
