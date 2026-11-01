// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.RegularExpressions;

namespace Craft.Abstractions;

/// <summary>
/// What a find matches (the find overlay's, the workspace search's, a search provider's): a query on one line, matched by
/// case or not, as a whole word or not, as text or as a regular expression. Immutable, any thread. A regular expression
/// runs one line at a time on the line's span (no string), a match of no characters is never one, <c>$1</c> and
/// <c>${name}</c> go into a replacement, and a line that runs past the time limit is taken as having no match
/// (<see cref="TimedOut"/> says so). Text runs on any span (a whole file too: a query holds no line break).
/// </summary>
public sealed class TextPattern
{
    private readonly Regex? _regex;
    private volatile bool _timedOut;

    /// <summary>A pattern for <paramref name="query"/>; a regular expression stops after <paramref name="regexTimeout"/> on one line.</summary>
    /// <param name="query">The text or the expression (one line; a query with a line break finds nothing).</param>
    /// <param name="matchCase">Whether case must match.</param>
    /// <param name="wholeWord">Whether a match must be a whole word.</param>
    /// <param name="regex">Whether <paramref name="query"/> is a regular expression.</param>
    /// <param name="regexTimeout">The longest one line's search may take.</param>
    /// <param name="compiled">
    /// Compiles the expression to IL: slower to make, faster over many lines (a workspace search); the matches are the same.
    /// </param>
    public TextPattern(string query, bool matchCase, bool wholeWord, bool regex, TimeSpan regexTimeout, bool compiled = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query;
        MatchCase = matchCase;
        WholeWord = wholeWord;
        IsRegex = regex;
        if (query.Length == 0 || query.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return;
        }

        if (regex)
        {
            try
            {
                var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | (compiled ? RegexOptions.Compiled : RegexOptions.None);
                _regex = new Regex(query, options, regexTimeout);
            }
            catch (ArgumentException e)
            {
                Problem = "The regular expression does not parse: " + e.Message;
                return;
            }
        }

        Searchable = true;
    }

    /// <summary>The text (or the expression) to find.</summary>
    public string Query { get; }

    /// <summary>Whether case must match.</summary>
    public bool MatchCase { get; }

    /// <summary>Whether a match must be a whole word (no letter, digit or underscore just before or after it).</summary>
    public bool WholeWord { get; }

    /// <summary>Whether <see cref="Query"/> is a regular expression.</summary>
    public bool IsRegex { get; }

    /// <summary>Whether it can find anything (a query on one line that parses).</summary>
    public bool Searchable { get; }

    /// <summary>Why it cannot search (the expression does not parse), or null.</summary>
    public string? Problem { get; }

    /// <summary>Whether a line's search ran past the time limit (that line was taken as having no match).</summary>
    public bool TimedOut => _timedOut;

    /// <summary>Whether the query is plain text, so it can be searched over a whole text at once (no line is needed).</summary>
    public bool IsText => Searchable && _regex is null;

    private StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The first match in <paramref name="line"/> at or after <paramref name="from"/>, or -1; its length in <paramref name="length"/>.</summary>
    public int NextIn(ReadOnlySpan<char> line, int from, out int length)
    {
        length = 0;
        if (!Searchable || from > line.Length)
        {
            return -1;
        }

        if (_regex is { } regex)
        {
            try
            {
                foreach (var match in regex.EnumerateMatches(line, from))
                {
                    if (match.Length > 0 && (!WholeWord || IsWholeWord(line, match.Index, match.Length)))
                    {
                        length = match.Length;
                        return match.Index;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _timedOut = true;
            }

            return -1;
        }

        var query = Query.AsSpan();
        while (from <= line.Length - query.Length)
        {
            int found = line[from..].IndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            found += from;
            if (!WholeWord || IsWholeWord(line, found, query.Length))
            {
                length = query.Length;
                return found;
            }

            from = found + 1;
        }

        return -1;
    }

    /// <summary>The last match in <paramref name="line"/> that ends at or before <paramref name="limit"/>, or -1; its length in <paramref name="length"/>.</summary>
    public int PreviousIn(ReadOnlySpan<char> line, int limit, out int length)
    {
        length = 0;
        if (!Searchable)
        {
            return -1;
        }

        limit = Math.Min(limit, line.Length);
        if (_regex is not null)
        {
            int last = -1;
            int from = 0;
            int found;
            while ((found = NextIn(line, from, out int matched)) >= 0 && found + matched <= limit)
            {
                last = found;
                length = matched;
                from = found + 1;
            }

            return last;
        }

        var query = Query.AsSpan();
        while (limit >= query.Length)
        {
            int found = line[..limit].LastIndexOf(query, Comparison);
            if (found < 0)
            {
                return -1;
            }

            if (!WholeWord || IsWholeWord(line, found, query.Length))
            {
                length = query.Length;
                return found;
            }

            limit = found + query.Length - 1;
        }

        return -1;
    }

    /// <summary>
    /// The text that replaces the match at <paramref name="start"/> of <paramref name="line"/>: <paramref name="replacement"/>
    /// as it is, or for a regular expression with its groups put in ($1, ${name}, $$).
    /// </summary>
    public string Replacement(ReadOnlySpan<char> line, int start, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_regex is not { } regex || replacement.AsSpan().IndexOf('$') < 0)
        {
            return replacement;
        }

        try
        {
            var match = regex.Match(line.ToString(), start);
            return match.Success && match.Index == start ? match.Result(replacement) : replacement;
        }
        catch (RegexMatchTimeoutException)
        {
            _timedOut = true;
            return replacement;
        }
    }

    /// <summary>Whether <paramref name="c"/> is part of a word (a letter, a digit, '_', or half of a surrogate pair).</summary>
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c);

    private static bool IsWholeWord(ReadOnlySpan<char> line, int start, int length) =>
        (start == 0 || !IsWordChar(line[start - 1])) && (start + length >= line.Length || !IsWordChar(line[start + length]));
}
