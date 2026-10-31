// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Idrak.Abstraction.Retrieval;
using Idrak.Text.Abstractions;

namespace Craft.Plugins.Search;

/// <summary>
/// The words code is searched by: each run of word characters (letters, digits, marks and connectors, as a regular
/// expression's \w) lower-cased and cut at its camelCase humps and underscores ("ParseTextAsync" gives "parse text async
/// parsetextasync", "MAX_VALUE" gives "max value maxvalue", "XMLReader" gives "xml reader xmlreader"); a part followed by
/// the whole run when there are several parts, so a query by the whole name and one by its words both find it. Punctuation
/// is left out. One pass over the text, no regular expression and no string per word; analysing words it gave gives them
/// again (lower case, no connectors), so stored terms can be indexed again.
/// </summary>
public static class CodeTerms
{
    /// <summary>The longest run taken as a word (longer ones: a hash, an encoded blob).</summary>
    public const int DefaultMaxWordLength = 64;

    /// <summary>The words of <paramref name="text"/>, separated by spaces.</summary>
    public static string Analyze(ReadOnlySpan<char> text, int maxWordLength = DefaultMaxWordLength)
    {
        var words = new StringBuilder(text.Length);
        Append(words, text, maxWordLength);
        return words.ToString();
    }

    /// <summary>Appends the words of <paramref name="text"/> to <paramref name="words"/> (a space before each when it is not empty).</summary>
    public static void Append(StringBuilder words, ReadOnlySpan<char> text, int maxWordLength = DefaultMaxWordLength)
    {
        ArgumentNullException.ThrowIfNull(words);
        int i = 0;
        while (i < text.Length)
        {
            if (!IsWord(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && IsWord(text[i]))
            {
                i++;
            }

            if (i - start <= maxWordLength)
            {
                AppendRun(words, text[start..i]);
            }
        }
    }

    // One run: its parts, then the whole run (without connectors) when it has several.
    private static void AppendRun(StringBuilder words, ReadOnlySpan<char> run)
    {
        int parts = 0;
        int partStart = -1;
        for (int k = 0; k < run.Length; k++)
        {
            char c = run[k];
            if (IsConnector(c))
            {
                if (partStart >= 0)
                {
                    Emit(words, run[partStart..k]);
                    parts++;
                    partStart = -1;
                }

                continue;
            }

            if (partStart < 0)
            {
                partStart = k;
            }
            else if (IsHump(run, k))
            {
                Emit(words, run[partStart..k]);
                parts++;
                partStart = k;
            }
        }

        if (partStart >= 0)
        {
            Emit(words, run[partStart..]);
            parts++;
        }

        if (parts > 1)
        {
            words.Append(' ');
            foreach (char c in run)
            {
                if (!IsConnector(c))
                {
                    words.Append(char.ToLowerInvariant(c));
                }
            }
        }
    }

    // A new part starts at k: an upper-case letter after a lower-case one or a digit ("parseText", "utf8Json"), or the
    // last upper-case letter of a run of them before a lower-case one ("XMLReader": "XML" then "Reader").
    private static bool IsHump(ReadOnlySpan<char> run, int k)
    {
        char c = run[k], p = run[k - 1];
        if (!char.IsUpper(c))
        {
            return false;
        }

        return char.IsLower(p) || char.IsDigit(p) || char.IsUpper(p) && k + 1 < run.Length && char.IsLower(run[k + 1]);
    }

    private static void Emit(StringBuilder words, ReadOnlySpan<char> part)
    {
        if (words.Length > 0)
        {
            words.Append(' ');
        }

        foreach (char c in part)
        {
            words.Append(char.ToLowerInvariant(c));
        }
    }

    private static bool IsConnector(char c) => c == '_' || c > 127 && char.GetUnicodeCategory(c) == UnicodeCategory.ConnectorPunctuation;

    private static bool IsWord(char c)
    {
        if (c < 128)
        {
            return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
        }

        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;
    }
}

/// <summary>
/// Idrak.Text's keyword index "code-bm25" (in <see cref="KeywordIndexes"/>, a new kind): the library's BM25
/// (<see cref="KeywordIndexes.Default"/>, whatever an app registered over it) over each text's <see cref="CodeTerms"/>, the
/// query analysed the same way, so "parse text" finds <c>ParseTextAsync</c> and "ParseText" finds <c>parse_text</c>. It
/// takes k1 and b as BM25 does, and "maxWordLength" (the longest run taken as a word, 64 unless set).
/// </summary>
public sealed class CodeKeywordIndexKind : IKeywordIndexKind
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "code-bm25";

    /// <summary>The setting of the longest word.</summary>
    public const string MaxWordLength = "maxWordLength";

    private CodeKeywordIndexKind()
    {
    }

    /// <summary>The kind.</summary>
    public static CodeKeywordIndexKind Instance { get; } = new();

    /// <inheritdoc />
    public string Name => IndexName;

    /// <inheritdoc />
    public string Summary => "BM25 over code's words: identifiers split at camelCase humps and underscores, lower-cased, punctuation left out";

    /// <inheritdoc />
    public IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal) { MaxWordLength };

    /// <inheritdoc />
    public IKeywordIndex Create(KeywordIndexRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int longest = request.Options.Parameters.TryGetValue(MaxWordLength, out double set)
            ? (int)Math.Clamp(set, 1, 1_000_000) : CodeTerms.DefaultMaxWordLength;
        var bm25 = KeywordIndexes.Default(KeywordIndexes.Bm25) ?? KeywordIndexes.Get(KeywordIndexes.Bm25);
        var inner = bm25.Create(new KeywordIndexRequest(request.Texts.Select(t => CodeTerms.Analyze(t, longest)), request.Options with { Parameters = new Dictionary<string, double>() }));
        return new Index(inner, request.Options, longest);
    }

    private sealed class Index(IKeywordIndex inner, KeywordIndexOptions options, int longest) : IKeywordIndex
    {
        public string Name => IndexName;

        public KeywordIndexOptions Options => options;

        public int Count => inner.Count;

        public IReadOnlyList<SearchHit> Search(string query, int top)
        {
            ArgumentNullException.ThrowIfNull(query);
            return inner.Search(CodeTerms.Analyze(query, longest), top);
        }
    }
}
