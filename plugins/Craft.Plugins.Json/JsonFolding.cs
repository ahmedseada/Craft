// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Plugins.Json;

/// <summary>
/// JSON's folding ranges (phase X), comments and trailing commas allowed (JSON with comments, as tsconfig.json and .NET's configuration read it): each object
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
    public IReadOnlyList<string> Languages { get; } = [JsonLanguage.LanguageName];

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

                var text = ReadLine(snapshot, index, ref buffer);
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

    // A line of the snapshot read into a pooled buffer (grown as needed), without its line break.
    private static ReadOnlySpan<char> ReadLine(ITextSnapshot snapshot, int index, ref char[] buffer)
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
