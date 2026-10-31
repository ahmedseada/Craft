// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.CSharp;

/// <summary>
/// The folding ranges of MSBuild, XML and XAML files (phase X): each element whose start and end tags are on different
/// lines (from its start tag's line to its end tag's), a start tag over several lines (many attributes), and comments
/// over several lines. One pass over the lines, a state carried from line to line (no parse, no copy of the text).
/// </summary>
public sealed class MarkupFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "markup";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "MSBuild, XML and XAML: elements over several lines, long start tags and comments";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.MsBuild, ProjectLanguages.Xml, ProjectLanguages.Xaml];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var open = new Stack<int>();
        var state = State.Text;
        int markStart = 0;
        char quote = '\0';
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var text = LineText.Read(snapshot, index, ref buffer);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (state)
                    {
                        case State.Text when c == '<':
                            var rest = text[(i + 1)..];
                            markStart = index;
                            if (rest.StartsWith("!--"))
                            {
                                state = State.Comment;
                                i += 3;
                            }
                            else if (rest.StartsWith("![CDATA["))
                            {
                                state = State.CData;
                                i += 8;
                            }
                            else if (rest.StartsWith("?"))
                            {
                                state = State.Instruction;
                            }
                            else if (rest.StartsWith("!"))
                            {
                                state = State.Declaration;
                            }
                            else if (rest.StartsWith("/"))
                            {
                                state = State.EndTag;
                                i++;
                            }
                            else
                            {
                                state = State.StartTag;
                            }

                            break;
                        case State.Comment when c == '-' && text[i..].StartsWith("-->"):
                            if (index > markStart)
                            {
                                ranges.Add(new FoldingRange(markStart, index, FoldingRangeKind.Comment));
                            }

                            i += 2;
                            state = State.Text;
                            break;
                        case State.CData when c == ']' && text[i..].StartsWith("]]>"):
                            i += 2;
                            state = State.Text;
                            break;
                        case State.Instruction when c == '?' && text[i..].StartsWith("?>"):
                            i++;
                            state = State.Text;
                            break;
                        case State.Declaration when c == '>':
                            state = State.Text;
                            break;
                        case State.StartTag when quote != '\0':
                            if (c == quote)
                            {
                                quote = '\0';
                            }

                            break;
                        case State.StartTag when c is '"' or '\'':
                            quote = c;
                            break;
                        case State.StartTag when c == '/' && i + 1 < text.Length && text[i + 1] == '>':
                            if (index > markStart)
                            {
                                ranges.Add(new FoldingRange(markStart, index));
                            }

                            i++;
                            state = State.Text;
                            break;
                        case State.StartTag when c == '>':
                            open.Push(markStart);
                            state = State.Text;
                            break;
                        case State.EndTag when c == '>':
                            if (open.Count > 0)
                            {
                                int first = open.Pop();
                                if (index > first)
                                {
                                    ranges.Add(new FoldingRange(first, index));
                                }
                            }

                            state = State.Text;
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }

    private enum State
    {
        Text,
        Comment,
        CData,
        Instruction,
        Declaration,
        StartTag,
        EndTag,
    }
}

/// <summary>
/// JSON's folding ranges (phase X), comments and trailing commas allowed as .NET's configuration reads them: each object
/// and array over several lines (from its opening bracket's line to its closing one's) and block comments over several
/// lines. One pass over the lines; strings and comments skipped.
/// </summary>
public sealed class JsonFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "json";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "JSON: objects, arrays and block comments over several lines";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [ProjectLanguages.Json];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        var open = new Stack<int>();
        bool inComment = false;
        int commentStart = 0;
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var text = LineText.Read(snapshot, index, ref buffer);
                bool inString = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inComment)
                    {
                        if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                        {
                            inComment = false;
                            i++;
                            if (index > commentStart)
                            {
                                ranges.Add(new FoldingRange(commentStart, index, FoldingRangeKind.Comment));
                            }
                        }

                        continue;
                    }

                    if (inString)
                    {
                        if (c == '\\')
                        {
                            i++;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    switch (c)
                    {
                        case '"':
                            inString = true;
                            break;
                        case '/' when i + 1 < text.Length && text[i + 1] == '/':
                            i = text.Length;
                            break;
                        case '/' when i + 1 < text.Length && text[i + 1] == '*':
                            inComment = true;
                            commentStart = index;
                            i++;
                            break;
                        case '{' or '[':
                            open.Push(index);
                            break;
                        case '}' or ']' when open.Count > 0:
                            int first = open.Pop();
                            if (index > first)
                            {
                                ranges.Add(new FoldingRange(first, index));
                            }

                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return ranges;
    }
}

/// <summary>A line of a snapshot read into a pooled buffer (grown as needed), without its line break.</summary>
internal static class LineText
{
    public static ReadOnlySpan<char> Read(ITextSnapshot snapshot, int index, ref char[] buffer)
    {
        var line = snapshot.GetLine(index);
        if (buffer.Length < line.Length)
        {
            ArrayPool<char>.Shared.Return(buffer);
            buffer = ArrayPool<char>.Shared.Rent(line.Length);
        }

        var span = buffer.AsSpan(0, line.Length);
        snapshot.CopyTo(line.Start, span);
        return span;
    }
}
