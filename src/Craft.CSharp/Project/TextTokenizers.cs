// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Frozen;
using Craft.Abstractions;

namespace Craft.CSharp.Project;

/// <summary>
/// JSON's colours by lines, as the files of a C# project write it (appsettings.json, launchSettings.json, global.json):
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

/// <summary>
/// .editorconfig's colours (an INI file): comments (<c>#</c>, <c>;</c>), a section's glob (<c>[*.cs]</c>), a key, its
/// value (true, false, a number, a word) and an analyzer severity after ':' (none, silent, suggestion, warning, error).
/// Every line stands alone.
/// </summary>
internal sealed class EditorConfigTokenizer : LineTokenizer
{
    private static readonly FrozenSet<string> Words = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ["true", "false", "none", "silent", "suggestion", "warning", "error", "default", "unset", "crlf", "lf", "cr", "space", "tab"]);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> WordLookup = Words.GetAlternateLookup<ReadOnlySpan<char>>();
    private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789.");

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        if (into is null)
        {
            return 0;
        }

        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        if (i >= line.Length)
        {
            return 0;
        }

        if (line[i] is '#' or ';')
        {
            into.Add(lineStart + i, line.Length - i, TokenClasses.Comment);
            return 0;
        }

        if (line[i] == '[')
        {
            int close = line.LastIndexOf(']');
            into.Add(lineStart + i, 1, TokenClasses.Punctuation);
            if (close > i)
            {
                into.Add(lineStart + i + 1, close - i - 1, TokenClasses.ClassName);
                into.Add(lineStart + close, 1, TokenClasses.Punctuation);
            }
            else
            {
                into.Add(lineStart + i + 1, line.Length - i - 1, TokenClasses.ClassName);
            }

            return 0;
        }

        int equals = line.IndexOf('=');
        if (equals < 0)
        {
            return 0;
        }

        var key = line[i..equals].TrimEnd(" \t");
        into.Add(lineStart + i, key.Length, TokenClasses.PropertyName);
        into.Add(lineStart + equals, 1, TokenClasses.Operator);
        int at = equals + 1;
        while (at < line.Length)
        {
            while (at < line.Length && line[at] is ' ' or '\t')
            {
                at++;
            }

            int end = at;
            while (end < line.Length && line[end] is not (':' or ','))
            {
                end++;
            }

            var value = line[at..end].TrimEnd(" \t");
            if (!value.IsEmpty)
            {
                string tokenClass = WordLookup.Contains(value) ? TokenClasses.Keyword
                    : char.IsAsciiDigit(value[0]) && !value.ContainsAnyExcept(Digits) ? TokenClasses.Number
                    : TokenClasses.String;
                into.Add(lineStart + at, value.Length, tokenClass);
            }

            if (end < line.Length)
            {
                into.Add(lineStart + end, 1, TokenClasses.Punctuation);
            }

            at = end + 1;
        }

        return 0;
    }
}

/// <summary>
/// A Visual Studio solution's colours (.sln): its words (Project, EndProject, Global, GlobalSection, preSolution), strings,
/// GUIDs as numbers, a section's name, a setting's key before its '=', and the header's comments. Every line stands alone.
/// </summary>
internal sealed class SolutionTokenizer : LineTokenizer
{
    private static readonly FrozenSet<string> Words = FrozenSet.Create(StringComparer.Ordinal,
    [
        "Project", "EndProject", "Global", "EndGlobal", "GlobalSection", "EndGlobalSection", "ProjectSection", "EndProjectSection",
        "preSolution", "postSolution", "preProject", "postProject",
    ]);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> WordLookup = Words.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        if (into is null)
        {
            return 0;
        }

        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        if (i >= line.Length)
        {
            return 0;
        }

        if (line[i] == '#' || line[i..].StartsWith("Microsoft Visual Studio Solution File"))
        {
            into.Add(lineStart + i, line.Length - i, TokenClasses.Comment);
            return 0;
        }

        int equals = line.IndexOf('=');
        int wordEnd = i;
        while (wordEnd < line.Length && char.IsAsciiLetter(line[wordEnd]))
        {
            wordEnd++;
        }

        bool keyed = !WordLookup.Contains(line[i..wordEnd]);
        if (keyed && equals > i)
        {
            // A setting: its key ("{GUID}.Debug|Any CPU.ActiveCfg", "VisualStudioVersion") before the '='.
            Values(line, i, equals, lineStart, into, key: true);
            into.Add(lineStart + equals, 1, TokenClasses.Operator);
            Values(line, equals + 1, line.Length, lineStart, into, key: false);
            return 0;
        }

        Values(line, i, line.Length, lineStart, into, key: false);
        return 0;
    }

    private static void Values(ReadOnlySpan<char> line, int i, int end, int lineStart, TokenSpanBuffer into, bool key)
    {
        int keyFrom = -1;
        while (i < end)
        {
            char c = line[i];
            if (c == '"')
            {
                int close = line[(i + 1)..end].IndexOf('"');
                int length = close < 0 ? end - i : close + 2;
                Flush(ref keyFrom, i, lineStart, into);
                into.Add(lineStart + i, length, TokenClasses.String);
                i += length;
            }
            else if (c == '{' && i + 37 < end && line[i + 37] == '}')
            {
                Flush(ref keyFrom, i, lineStart, into);
                into.Add(lineStart + i, 38, TokenClasses.Number);
                i += 38;
            }
            else if (c is '(' or ')' or ',' or '=')
            {
                Flush(ref keyFrom, i, lineStart, into);
                into.Add(lineStart + i, 1, TokenClasses.Punctuation);
                i++;
                if (c == '(')
                {
                    int close = line[i..end].IndexOf(')');
                    if (close > 0)
                    {
                        into.Add(lineStart + i, close, TokenClasses.ClassName);   // GlobalSection(SolutionConfigurationPlatforms)
                        i += close;
                    }
                }
            }
            else if (char.IsAsciiLetter(c))
            {
                int j = i;
                while (j < end && char.IsAsciiLetterOrDigit(line[j]))
                {
                    j++;
                }

                if (WordLookup.Contains(line[i..j]))
                {
                    Flush(ref keyFrom, i, lineStart, into);
                    into.Add(lineStart + i, j - i, TokenClasses.Keyword);
                }
                else if (key && keyFrom < 0)
                {
                    keyFrom = i;
                }

                i = j;
            }
            else if (char.IsAsciiDigit(c) && !key)
            {
                int j = i;
                while (j < end && (char.IsAsciiDigit(line[j]) || line[j] == '.'))
                {
                    j++;
                }

                into.Add(lineStart + i, j - i, TokenClasses.Number);
                i = j;
            }
            else
            {
                if (key && keyFrom < 0 && c is not (' ' or '\t'))
                {
                    keyFrom = i;
                }

                i++;
            }
        }

        if (key && keyFrom >= 0)
        {
            var text = line[keyFrom..end].TrimEnd(" \t");
            into.Add(lineStart + keyFrom, text.Length, TokenClasses.PropertyName);
        }
    }

    private static void Flush(ref int keyFrom, int at, int lineStart, TokenSpanBuffer into)
    {
        if (keyFrom >= 0)
        {
            into.Add(lineStart + keyFrom, at - keyFrom, TokenClasses.PropertyName);
            keyFrom = -1;
        }
    }
}
