// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>A finding of the last review as the view keeps it: its id, the finding, and the file's full path.</summary>
/// <param name="Id">Its id in the view ("r3").</param>
/// <param name="Finding">The finding.</param>
/// <param name="FullPath">The file's full path in the repository.</param>
public sealed record ReviewItem(string Id, ReviewFinding Finding, string FullPath);

/// <summary>
/// How changes are given to a review (X part 11 item 4): each kept and added line of the diff numbered with its line in
/// the new text (so a finding names a line of the file), then the whole trimmed to the budget file by file (the largest
/// files summarised by name and counts, as the commit message's diff is), with the summarised files named.
/// </summary>
public static class CodeReviews
{
    /// <summary>
    /// <paramref name="diff"/> (a unified diff) with each context and added line prefixed by its number in the new text
    /// ("+  12| code", "   13| code"); removed lines keep no number ("-    | code"); headers as they are. Read as bytes.
    /// </summary>
    public static string Number(ReadOnlySpan<byte> diff)
    {
        var text = new StringBuilder(diff.Length + (diff.Length / 4));
        int next = 0;
        bool inHunk = false;
        foreach (var range in diff.Split((byte)'\n'))
        {
            var line = diff[range];
            if (range.Start.Value == diff.Length)
            {
                break;                                        // the empty part after the last break
            }

            if (line.StartsWith("@@"u8))
            {
                next = NewStart(line);
                inHunk = true;
                text.Append(Encoding.UTF8.GetString(line)).Append('\n');
                continue;
            }

            if (line.StartsWith("diff --git "u8))
            {
                inHunk = false;
            }

            if (inHunk && line.Length > 0 && line[0] is (byte)' ' or (byte)'+')
            {
                text.Append((char)line[0]).Append(next.ToString(CultureInfo.InvariantCulture).PadLeft(5)).Append("| ").Append(Encoding.UTF8.GetString(line[1..])).Append('\n');
                next++;
            }
            else if (inHunk && line.Length > 0 && line[0] == (byte)'-' && !line.StartsWith("---"u8))
            {
                text.Append("-     | ").Append(Encoding.UTF8.GetString(line[1..])).Append('\n');
            }
            else
            {
                text.Append(Encoding.UTF8.GetString(line)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The changes for a review within <paramref name="budgetTokens"/>: numbered, trimmed by file, and the files summarised.</summary>
    public static (TrimmedDiff Diff, IReadOnlyList<string> Summarised) Prepare(ReadOnlySpan<byte> diff, int budgetTokens)
    {
        string numbered = Number(diff);
        var trimmed = CommitMessages.Trim(Encoding.UTF8.GetBytes(numbered), budgetTokens);
        var names = new List<string>();
        if (trimmed.Summarised > 0)
        {
            foreach (var line in trimmed.Text.Split('\n'))
            {
                if (line.EndsWith(" lines, summarised", StringComparison.Ordinal) && line.IndexOf(": +", StringComparison.Ordinal) is > 0 and var colon)
                {
                    names.Add(line[..colon]);
                }
            }
        }

        return (trimmed, names);
    }

    /// <summary>The findings as Copy all writes them: one a line, by file and line.</summary>
    public static string CopyText(IEnumerable<ReviewItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return string.Join('\n', items.OrderBy(i => i.Finding.Path, StringComparer.Ordinal).ThenBy(i => i.Finding.Line).Select(i => i.Finding.Text)) + "\n";
    }

    /// <summary>What a finding's inline card says: its severity and message, then its reason.</summary>
    public static string CardText(IEnumerable<ReviewItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var text = new StringBuilder();
        foreach (var item in items)
        {
            var finding = item.Finding;
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(Mark(finding.Severity)).Append(' ').Append(ReviewFinding.SeverityWord(finding.Severity)).Append(" · ").Append(finding.Message);
            if (finding.Reason.Length > 0)
            {
                text.Append('\n').Append("  ").Append(finding.Reason);
            }
        }

        return text.ToString();
    }

    /// <summary>The mark a severity is shown with.</summary>
    public static string Mark(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "✕",
        DiagnosticSeverity.Warning => "⚠",
        _ => "ℹ",
    };

    // "@@ -a,b +c,d @@": c.
    private static int NewStart(ReadOnlySpan<byte> header)
    {
        int plus = header.IndexOf((byte)'+');
        if (plus < 0)
        {
            return 1;
        }

        int end = plus + 1;
        while (end < header.Length && header[end] >= (byte)'0' && header[end] <= (byte)'9')
        {
            end++;
        }

        return int.TryParse(header[(plus + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out int start) ? Math.Max(1, start) : 1;
    }
}
