// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Finds the partner of the bracket just before or at the caret, counting depth over the same pair, up to a number of
/// lines each way. When the language classifies the text, brackets inside strings and comments are skipped; only the
/// lines scanned are classified, one at a time.
/// </summary>
internal static class BracketMatcher
{
    public static BracketMatch? Find(ITextSnapshot snapshot, int caret, LanguageRules rules, ILanguageSession? session, int scanLines, TokenSpanBuffer spans)
    {
        foreach (int at in (ReadOnlySpan<int>)[caret - 1, caret])
        {
            if (at < 0 || at >= snapshot.Length)
            {
                continue;
            }

            char c = snapshot[at];
            foreach (var pair in rules.Brackets)
            {
                if (c != pair.Open && c != pair.Close)
                {
                    continue;
                }

                if (session is not null && IsExcluded(snapshot, session, at, spans))
                {
                    return null;
                }

                int partner = c == pair.Open
                    ? Scan(snapshot, at, pair.Open, pair.Close, forward: true, session, scanLines, spans)
                    : Scan(snapshot, at, pair.Close, pair.Open, forward: false, session, scanLines, spans);
                if (partner >= 0)
                {
                    return c == pair.Open ? new BracketMatch(at, partner) : new BracketMatch(partner, at);
                }

                return null;
            }
        }

        return null;
    }

    private static int Scan(ITextSnapshot snapshot, int from, char self, char partner, bool forward, ILanguageSession? session, int scanLines, TokenSpanBuffer spans)
    {
        int first = snapshot.GetLineIndex(from);
        int depth = 1;
        char[]? buffer = null;
        try
        {
            for (int n = 0; n <= scanLines; n++)
            {
                int index = forward ? first + n : first - n;
                if (index < 0 || index >= snapshot.LineCount)
                {
                    return -1;
                }

                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                spans.Clear();
                if (session is not null && line.Length > 0)
                {
                    session.Classify(snapshot, line.Start, line.Length, spans);
                }

                int i = n == 0 ? from - line.Start + (forward ? 1 : -1) : forward ? 0 : text.Length - 1;
                for (; forward ? i < text.Length : i >= 0; i += forward ? 1 : -1)
                {
                    char c = text[i];
                    if (c != self && c != partner)
                    {
                        continue;
                    }

                    if (session is not null && InExcluded(spans.Spans, line.Start + i))
                    {
                        continue;
                    }

                    depth += c == self ? 1 : -1;
                    if (depth == 0)
                    {
                        return line.Start + i;
                    }
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return -1;
    }

    private static bool IsExcluded(ITextSnapshot snapshot, ILanguageSession session, int position, TokenSpanBuffer spans)
    {
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        spans.Clear();
        session.Classify(snapshot, line.Start, line.Length, spans);
        return InExcluded(spans.Spans, position);
    }

    private static bool InExcluded(ReadOnlySpan<TokenSpan> spans, int position)
    {
        foreach (var span in spans)
        {
            if (position >= span.Start && position < span.End && IsStringOrComment(span.Class))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a class is a string's or a comment's (the library's, or a language's own named with either word).</summary>
    internal static bool IsStringOrComment(string tokenClass) =>
        ReferenceEquals(tokenClass, TokenClasses.String) || ReferenceEquals(tokenClass, TokenClasses.Comment)
        || tokenClass.Contains("string", StringComparison.Ordinal) || tokenClass.Contains("comment", StringComparison.Ordinal)
        || tokenClass == TokenClasses.ExcludedCode;
}
