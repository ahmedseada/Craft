// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Web.Style;

/// <summary>
/// The style languages' folding ranges: each block over several lines (a rule, an at-rule, a nested rule: from its '{'
/// line to its '}' line), each <c>/* #region name */</c> (or SCSS's and LESS's <c>// #region</c>) to its
/// <c>#endregion</c> (marked), each block comment over several lines and each run of two or more lines that hold only a
/// <c>//</c> comment (comments). One pass over the lines with the language's own tokenizer, so a brace inside a string, a
/// comment, a <c>url()</c> or an interpolation never folds.
/// </summary>
public static class StyleFolding
{
    /// <summary>The ranges of <paramref name="snapshot"/>, lexed by <paramref name="tokenizer"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, LineTokenizer tokenizer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tokenizer);
        var ranges = new List<FoldingRange>();
        var blocks = new Stack<int>();
        var regions = new Stack<int>();
        using var spans = new TokenSpanBuffer();
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            ulong state = tokenizer.Initial;
            int commentStart = -1;
            int runStart = -1;
            int runEnd = -1;
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var line = snapshot.GetLine(index);
                if (buffer.Length < line.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                var text = buffer.AsSpan(0, line.Length);
                snapshot.CopyTo(line.Start, text);
                bool startsInComment = StyleTokenizer.IsInComment(state);
                spans.Clear();
                ulong end = tokenizer.Tokenize(text, 0, state, spans);
                int firstCharacter = text.IndexOfAnyExcept(' ', '\t');
                bool onlyLineComment = false;
                foreach (var span in spans.Spans)
                {
                    if (span.Length == 1 && ReferenceEquals(span.Class, TokenClasses.Punctuation))
                    {
                        char c = text[span.Start];
                        if (c == '{')
                        {
                            blocks.Push(index);
                        }
                        else if (c == '}' && blocks.Count > 0)
                        {
                            int open = blocks.Pop();
                            if (index > open)
                            {
                                ranges.Add(new FoldingRange(open, index));
                            }
                        }

                        continue;
                    }

                    if (!ReferenceEquals(span.Class, TokenClasses.Comment))
                    {
                        continue;
                    }

                    var body = text.Slice(span.Start, span.Length);
                    int marker = Marker(body);
                    if (marker > 0)
                    {
                        regions.Push(index);
                    }
                    else if (marker < 0 && regions.Count > 0)
                    {
                        int open = regions.Pop();
                        if (index > open)
                        {
                            ranges.Add(new FoldingRange(open, index, FoldingRangeKind.Marked));
                        }
                    }

                    if (body.StartsWith("//") && span.Start == firstCharacter)
                    {
                        onlyLineComment = marker == 0;
                    }
                    else if (body.StartsWith("/*") && span.End == text.Length && StyleTokenizer.IsInComment(end))
                    {
                        commentStart = index;
                    }
                }

                if (startsInComment && !StyleTokenizer.IsInComment(end) && commentStart >= 0)
                {
                    if (index > commentStart)
                    {
                        ranges.Add(new FoldingRange(commentStart, index, FoldingRangeKind.Comment));
                    }

                    commentStart = -1;
                }

                if (onlyLineComment)
                {
                    runStart = runStart < 0 ? index : runStart;
                    runEnd = index;
                }
                else
                {
                    AddRun(ranges, ref runStart, runEnd);
                }

                state = end;
            }

            AddRun(ranges, ref runStart, runEnd);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    // +1 for a "#region" comment, -1 for "#endregion", 0 for any other.
    private static int Marker(ReadOnlySpan<char> comment)
    {
        var body = comment.StartsWith("/*") || comment.StartsWith("//") ? comment[2..] : comment;
        body = body.TrimStart();
        if (body.StartsWith("#region", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return body.StartsWith("#endregion", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
    }

    private static void AddRun(List<FoldingRange> ranges, ref int runStart, int runEnd)
    {
        if (runStart >= 0 && runEnd > runStart)
        {
            ranges.Add(new FoldingRange(runStart, runEnd, FoldingRangeKind.Comment));
        }

        runStart = -1;
    }
}
