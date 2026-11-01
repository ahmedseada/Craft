// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;
using Craft.CSharp.Project;

namespace Craft.CSharp
{
    /// <summary>X part 9: Markdown beside the project languages (README.md, docs), coloured and folded.</summary>
    public static partial class ProjectLanguages
    {
        /// <summary>Markdown: .md, .markdown.</summary>
        public const string Markdown = "markdown";

        private static readonly LanguageRules MarkdownRules = new(null, "<!--", "-->", [new('(', ')'), new('[', ']')]);

        /// <summary>Markdown's language (made once): headings, emphasis marks, lists, quotes, code spans and fenced blocks, links, tables.</summary>
        internal static ILanguage MarkdownLanguage { get; } = new LineStateLanguage(Markdown, "Markdown: headings, emphasis, lists, quotes, code (inline and fenced), links and tables; folded by heading",
            [".md", ".markdown"], MarkdownRules, new MarkdownTokenizer(), null);
    }

    /// <summary>
    /// Markdown's folds: each heading over the lines up to the next heading of its level or above (the blank lines before
    /// it left out), and each fenced code block.
    /// </summary>
    public sealed class MarkdownFolding : IFoldingRangeProvider
    {
        /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
        public const string ProviderName = "markdown";

        /// <inheritdoc />
        public string Name => ProviderName;

        /// <inheritdoc />
        public string Summary => "Markdown: headings and fenced code blocks";

        /// <inheritdoc />
        public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.Markdown];

        /// <inheritdoc />
        public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
        }

        /// <summary>The ranges of <paramref name="snapshot"/>, by their first line.</summary>
        public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var ranges = new List<FoldingRange>();
            var open = new List<(int Level, int Line)>();
            int lastContent = -1;
            int fenceLine = -1;
            char fence = '\0';
            int fenceLength = 0;
            char[] buffer = ArrayPool<char>.Shared.Rent(256);
            try
            {
                for (int index = 0; index < snapshot.LineCount; index++)
                {
                    if ((index & 1023) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var line = LineText.Read(snapshot, index, ref buffer).Trim();
                    if (fenceLine >= 0)
                    {
                        int closing = 0;
                        while (closing < line.Length && line[closing] == fence)
                        {
                            closing++;
                        }

                        if (closing >= fenceLength && line[closing..].IsWhiteSpace())
                        {
                            if (index > fenceLine)
                            {
                                ranges.Add(new FoldingRange(fenceLine, index));
                            }

                            fenceLine = -1;
                        }

                        lastContent = index;
                        continue;
                    }

                    if (line.Length >= 3 && line[0] is '`' or '~' && line[1] == line[0] && line[2] == line[0])
                    {
                        fence = line[0];
                        fenceLength = 0;
                        while (fenceLength < line.Length && line[fenceLength] == fence)
                        {
                            fenceLength++;
                        }

                        fenceLine = index;
                        lastContent = index;
                        continue;
                    }

                    int level = 0;
                    while (level < line.Length && level < 7 && line[level] == '#')
                    {
                        level++;
                    }

                    if (level is > 0 and <= 6 && (level == line.Length || line[level] == ' '))
                    {
                        Close(level, index);
                        open.Add((level, index));
                    }

                    if (!line.IsEmpty)
                    {
                        lastContent = index;
                    }
                }

                Close(1, snapshot.LineCount);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            ranges.Sort(static (a, b) => a.StartLine.CompareTo(b.StartLine));
            return ranges;

            // Every open heading of this level or deeper ends at the last line with text before the next heading.
            void Close(int level, int next)
            {
                for (int i = open.Count - 1; i >= 0 && open[i].Level >= level; i--)
                {
                    int end = Math.Min(lastContent, next - 1);
                    if (end > open[i].Line)
                    {
                        ranges.Add(new FoldingRange(open[i].Line, end));
                    }

                    open.RemoveAt(i);
                }
            }
        }
    }
}

namespace Craft.CSharp.Project
{
    /// <summary>
    /// Markdown's colours, line by line from a state kept per line (a fenced block or an HTML comment that runs on): a
    /// heading's marks and text (keyword), a quote (comment), list and emphasis marks and a table's bars (punctuation), code
    /// spans and fenced blocks (string; the fence's language a type name), a link's address (string). Prose stays plain, so
    /// the spell check reads it.
    /// </summary>
    internal sealed class MarkdownTokenizer : LineTokenizer
    {
        // The state: 0 prose; FenceBacktick or FenceTilde with the fence's length in the bits above; Comment inside <!-- -->.
        private const ulong FenceBacktick = 1;
        private const ulong FenceTilde = 2;
        private const ulong Comment = 3;

        /// <inheritdoc />
        public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
        {
            int indent = 0;
            while (indent < line.Length && line[indent] is ' ' or '\t')
            {
                indent++;
            }

            var trimmed = line[indent..];
            ulong kind = state & 3;
            if (kind is FenceBacktick or FenceTilde)
            {
                char fence = kind == FenceBacktick ? '`' : '~';
                int length = (int)(state >> 2);
                int run = Run(trimmed, fence);
                if (run >= length && trimmed[run..].IsWhiteSpace())
                {
                    Add(into, lineStart + indent, run, TokenClasses.Punctuation);
                    return 0;
                }

                Add(into, lineStart, line.Length, TokenClasses.String);
                return state;
            }

            if (kind == Comment)
            {
                int close = line.IndexOf("-->", StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(into, lineStart, line.Length, TokenClasses.Comment);
                    return state;
                }

                Add(into, lineStart, close + 3, TokenClasses.Comment);
                return Inline(line, lineStart, close + 3, into);
            }

            if (trimmed.IsEmpty)
            {
                return 0;
            }

            char first = trimmed[0];
            if (first is '`' or '~')
            {
                int run = Run(trimmed, first);
                if (run >= 3 && (first == '~' || trimmed[run..].IndexOf('`') < 0))
                {
                    Add(into, lineStart + indent, run, TokenClasses.Punctuation);
                    var info = trimmed[run..].Trim();
                    if (!info.IsEmpty)
                    {
                        Add(into, lineStart + indent + trimmed.IndexOf(info), info.Length, TokenClasses.ClassName);
                    }

                    return (first == '`' ? FenceBacktick : FenceTilde) | ((ulong)run << 2);
                }
            }

            if (first == '#')
            {
                int hashes = Run(trimmed, '#');
                if (hashes <= 6 && (hashes == trimmed.Length || trimmed[hashes] == ' '))
                {
                    Add(into, lineStart + indent, hashes, TokenClasses.Punctuation);
                    Add(into, lineStart + indent + hashes, trimmed.Length - hashes, TokenClasses.Keyword);
                    return 0;
                }
            }

            if (IsRule(trimmed))
            {
                Add(into, lineStart + indent, trimmed.Length, TokenClasses.Punctuation);
                return 0;
            }

            int at = indent;
            if (first == '>')
            {
                Add(into, lineStart + indent, 1, TokenClasses.Punctuation);
                Add(into, lineStart + indent + 1, line.Length - indent - 1, TokenClasses.Comment);
                return 0;
            }

            if (first is '-' or '*' or '+' && trimmed.Length > 1 && trimmed[1] == ' ')
            {
                Add(into, lineStart + indent, 1, TokenClasses.Punctuation);
                at = indent + 2;
                if (trimmed.Length >= 5 && trimmed[2] == '[' && trimmed[4] == ']' && trimmed[3] is ' ' or 'x' or 'X')
                {
                    Add(into, lineStart + indent + 2, 3, TokenClasses.Keyword);          // a task's box
                    at = indent + 5;
                }
            }
            else
            {
                int digits = 0;
                while (digits < trimmed.Length && digits < 9 && char.IsAsciiDigit(trimmed[digits]))
                {
                    digits++;
                }

                if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
                {
                    Add(into, lineStart + indent, digits + 1, TokenClasses.Number);
                    at = indent + digits + 2;
                }
            }

            return Inline(line, lineStart, at, into);
        }

        // The rest of a line from at: code spans, emphasis marks, links and images, a table's bars, an HTML comment.
        private static ulong Inline(ReadOnlySpan<char> line, int lineStart, int at, TokenSpanBuffer? into)
        {
            int i = at;
            while (i < line.Length)
            {
                char c = line[i];
                switch (c)
                {
                    case '\\' when i + 1 < line.Length:
                        i += 2;
                        continue;
                    case '`':
                        int run = Run(line[i..], '`');
                        int close = line[(i + run)..].IndexOf(line.Slice(i, run));
                        if (close >= 0)
                        {
                            Add(into, lineStart + i, run + close + run, TokenClasses.String);
                            i += run + close + run;
                            continue;
                        }

                        i += run;
                        continue;
                    case '*' or '_':
                        int marks = Run(line[i..], c);
                        if (marks <= 3 && (c == '*' || i == 0 || !char.IsLetterOrDigit(line[i - 1]) || i + marks >= line.Length || !char.IsLetterOrDigit(line[i + marks])))
                        {
                            Add(into, lineStart + i, marks, TokenClasses.Punctuation);
                        }

                        i += marks;
                        continue;
                    case '|':
                        Add(into, lineStart + i, 1, TokenClasses.Punctuation);
                        i++;
                        continue;
                    case '<' when line[i..].StartsWith("<!--", StringComparison.Ordinal):
                        int end = line[(i + 4)..].IndexOf("-->", StringComparison.Ordinal);
                        if (end < 0)
                        {
                            Add(into, lineStart + i, line.Length - i, TokenClasses.Comment);
                            return Comment;
                        }

                        Add(into, lineStart + i, end + 7, TokenClasses.Comment);
                        i += end + 7;
                        continue;
                    case '!' when i + 1 < line.Length && line[i + 1] == '[':
                        Add(into, lineStart + i, 1, TokenClasses.Punctuation);
                        i++;
                        continue;
                    case '[':
                        int middle = line[i..].IndexOf("](", StringComparison.Ordinal);
                        int closing = middle > 0 ? line[(i + middle + 2)..].IndexOf(')') : -1;
                        if (middle > 0 && closing >= 0)
                        {
                            Add(into, lineStart + i, 1, TokenClasses.Punctuation);
                            Add(into, lineStart + i + 1, middle - 1, TokenClasses.MethodName);
                            Add(into, lineStart + i + middle, 2, TokenClasses.Punctuation);
                            Add(into, lineStart + i + middle + 2, closing, TokenClasses.String);
                            Add(into, lineStart + i + middle + 2 + closing, 1, TokenClasses.Punctuation);
                            i += middle + 3 + closing;
                            continue;
                        }

                        i++;
                        continue;
                    default:
                        i++;
                        continue;
                }
            }

            return 0;
        }

        private static int Run(ReadOnlySpan<char> text, char c)
        {
            int n = 0;
            while (n < text.Length && text[n] == c)
            {
                n++;
            }

            return n;
        }

        private static bool IsRule(ReadOnlySpan<char> trimmed)
        {
            if (trimmed.Length < 3 || trimmed[0] is not ('-' or '*' or '_'))
            {
                return false;
            }

            int count = 0;
            foreach (char c in trimmed)
            {
                if (c == trimmed[0])
                {
                    count++;
                }
                else if (c != ' ')
                {
                    return false;
                }
            }

            return count >= 3;
        }
    }
}
