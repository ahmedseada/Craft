// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Plugins.Learning;

/// <summary>
/// The test a caret is in (B11: "Mark the test at the caret" shows only then): the nearest test attribute before the
/// caret ([Fact], [Theory], [Test], [TestMethod], [TestCase], [DataTestMethod], [SkippableFact]) whose method's body the
/// caret has not left, read from at most <see cref="Window"/> characters before the caret (a pooled buffer, returned in
/// <c>finally</c>), so a caret's move costs the same in any file. Braces in strings and comments are not told apart: a
/// test that holds an unbalanced one in a string may read as left early.
/// </summary>
public static class TestAtCaret
{
    /// <summary>How far before the caret the attribute is looked for, in characters.</summary>
    public const int Window = 8192;

    private static readonly SearchValues<string> Attributes = SearchValues.Create(
        ["[Fact", "[Theory", "[Test", "[DataTestMethod", "[SkippableFact", "[SkippableTheory"], StringComparison.Ordinal);

    /// <summary>The test at <paramref name="caret"/> in <paramref name="snapshot"/> ("Class.Method", or the method alone when no class is in reach), or null.</summary>
    public static string? Find(ITextSnapshot snapshot, int caret)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        caret = Math.Clamp(caret, 0, snapshot.Length);
        int start = Math.Max(0, caret - Window);
        int length = caret - start;
        if (length == 0)
        {
            return null;
        }

        char[] buffer = ArrayPool<char>.Shared.Rent(length);
        try
        {
            snapshot.CopyTo(start, buffer.AsSpan(0, length));
            return Find(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>The test whose text runs up to the end of <paramref name="before"/> (the text before the caret), or null.</summary>
    public static string? Find(ReadOnlySpan<char> before)
    {
        // The last test attribute before the caret.
        int attribute = -1;
        for (int from = 0; from < before.Length;)
        {
            int at = before[from..].IndexOfAny(Attributes);
            if (at < 0)
            {
                break;
            }

            int found = from + at;
            int after = found + 1;
            while (after < before.Length && char.IsLetter(before[after]))
            {
                after++;
            }

            // "[Test" must end its name ([Tests] is not one); "[TestMethod", "[TestCase" are.
            if (after >= before.Length || before[after] is ']' or '(' or ',' or ' ')
            {
                var name = before[(found + 1)..after];
                if (name is "Fact" or "Theory" or "Test" or "TestMethod" or "TestCase" or "DataTestMethod" or "SkippableFact" or "SkippableTheory")
                {
                    attribute = found;
                }
            }

            from = found + 1;
        }

        if (attribute < 0)
        {
            return null;
        }

        // The method's name: the identifier before the first '(' after the attributes end.
        var rest = before[attribute..];
        int close = ClosingBracket(rest);
        if (close < 0)
        {
            return null;                                            // the caret is inside the attribute: no method yet
        }

        int open = -1;
        for (int i = close + 1; i < rest.Length; i++)
        {
            if (rest[i] == '[')
            {
                int next = ClosingBracket(rest[i..]);               // another attribute of the same method
                if (next < 0)
                {
                    return null;
                }

                i += next;
                continue;
            }

            if (rest[i] == '(')
            {
                open = i;
                break;
            }
        }

        if (open < 0)
        {
            return null;                                            // the caret is on the signature before its name
        }

        int end = open;
        while (end > 0 && char.IsWhiteSpace(rest[end - 1]))
        {
            end--;
        }

        int begin = end;
        while (begin > 0 && (char.IsLetterOrDigit(rest[begin - 1]) || rest[begin - 1] == '_'))
        {
            begin--;
        }

        if (begin == end)
        {
            return null;
        }

        string method = rest[begin..end].ToString();

        // Still inside it: its body not closed yet (or not opened: the caret is on its signature).
        int depth = 0;
        bool opened = false;
        for (int i = open; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '{')
            {
                depth++;
                opened = true;
            }
            else if (c == '}')
            {
                depth--;
                if (opened && depth == 0)
                {
                    return null;                                    // its body closed before the caret
                }
            }
            else if (c == ';' && !opened && depth == 0)
            {
                return null;                                        // an expression body ended before the caret
            }
        }

        string? type = ClassBefore(before[..attribute]);
        return type is null ? method : type + "." + method;
    }

    // The index of the ']' closing the attribute at the start of `text`, nested brackets and parentheses counted; -1 when open.
    private static int ClosingBracket(ReadOnlySpan<char> text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '[' or '(')
            {
                depth++;
            }
            else if (text[i] is ']' or ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // The name after the last "class " before the attribute, or null.
    private static string? ClassBefore(ReadOnlySpan<char> text)
    {
        int at = text.LastIndexOf("class ", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        int begin = at + 6;
        while (begin < text.Length && char.IsWhiteSpace(text[begin]))
        {
            begin++;
        }

        int end = begin;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return end > begin ? text[begin..end].ToString() : null;
    }
}
