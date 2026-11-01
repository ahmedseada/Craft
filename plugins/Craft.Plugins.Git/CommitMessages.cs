// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>A diff kept within its budget: its text, how many files it holds and how many of them were summarised.</summary>
/// <param name="Text">The diff, each summarised file one line of its name and counts.</param>
/// <param name="Files">The files it holds.</param>
/// <param name="Summarised">The files summarised by name and counts (the largest first) to keep it within the budget.</param>
/// <param name="Tokens">Its estimated tokens.</param>
public sealed record TrimmedDiff(string Text, int Files, int Summarised, int Tokens);

/// <summary>
/// How a commit message is asked for (X part 8): the changes trimmed to the budget file by file, the instructions in the
/// style asked (the repository's own recent subjects, Conventional Commits, or plain), and the model's reply cleaned into a
/// message (no fences, no quotes). A token is estimated as four bytes of the diff (the plug-in has no tokenizer: the
/// budget is a setting, <c>commitDiffTokens</c>, and the model's context bounds it).
/// </summary>
public static class CommitMessages
{
    /// <summary>The bytes a token is estimated at.</summary>
    public const int BytesPerToken = 4;

    /// <summary>The plug-in's own instructions (<c>commitPrompt</c> replaces them).</summary>
    public const string DefaultPrompt =
        "You write git commit messages. Write one for the changes below: a subject line of at most 72 characters in the imperative mood "
        + "(\"Add\", \"Fix\", \"Remove\"), then, only when the change needs it, a blank line and a short body that says why. "
        + "Reply with the message only: no quotes, no code fences, no preamble.";

    /// <summary>
    /// <paramref name="diff"/> (a unified diff of several files) within <paramref name="budgetTokens"/>: when it is larger, its
    /// largest files are replaced, one by one, by a line of their name and counts ("src/A.cs: +120 −4 lines, summarised")
    /// until it fits; the files keep their order. Split and counted as bytes (rule 74).
    /// </summary>
    public static TrimmedDiff Trim(ReadOnlySpan<byte> diff, int budgetTokens)
    {
        var files = SplitFiles(diff);
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
        }

        var summarised = new bool[files.Count];
        int summaries = 0;
        long budget = (long)Math.Max(1, budgetTokens) * BytesPerToken;
        if (total > budget)
        {
            // The largest first: each one summarised takes off its bytes and adds its one line.
            var order = Enumerable.Range(0, files.Count).OrderByDescending(i => files[i].Length).ToArray();
            foreach (int i in order)
            {
                if (total <= budget)
                {
                    break;
                }

                total -= files[i].Length;
                total += Summary(files[i], diff).Length + 1;
                summarised[i] = true;
                summaries++;
            }
        }

        var text = new StringBuilder((int)Math.Min(total + 64, int.MaxValue / 2));
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (summarised[i])
            {
                text.Append(Summary(file, diff)).Append('\n');
            }
            else
            {
                text.Append(Encoding.UTF8.GetString(diff.Slice(file.Start, file.Length)));
            }
        }

        string result = text.ToString();
        return new TrimmedDiff(result, files.Count, summaries, (Encoding.UTF8.GetByteCount(result) + BytesPerToken - 1) / BytesPerToken);
    }

    /// <summary>
    /// The instructions for <paramref name="settings"/>' style and prompt: the prompt, then the style's line (Conventional
    /// Commits' types; the repository's own style when <paramref name="hasExamples"/>), then whether the changes are every
    /// change (nothing staged) and whether some files were summarised.
    /// </summary>
    public static string Instructions(GitSettings settings, bool hasExamples, bool allChanges, int summarised)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var text = new StringBuilder(settings.CommitPrompt.Length > 0 ? settings.CommitPrompt : DefaultPrompt);
        switch (settings.CommitStyle)
        {
            case "conventional":
                text.Append(" Start the subject with a Conventional Commits type and, when it helps, a scope: feat, fix, docs, style, refactor, perf, test, build, ci or chore (\"fix(parser): keep the last line\").");
                break;
            case "auto" when hasExamples:
                text.Append(" Follow the style of the repository's recent subjects listed below (their form, their length, a prefix they share), not their content.");
                break;
        }

        if (allChanges)
        {
            text.Append(" Nothing is staged: the changes are every change of the working copy.");
        }

        if (summarised > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" {summarised} large {(summarised == 1 ? "file is" : "files are")} given by name and line counts only.");
        }

        return text.ToString();
    }

    /// <summary>
    /// The model's reply as a message: the text inside a code fence when it wrote one, quotes around the whole taken off, a
    /// leading "Commit message:" taken off, the lines' trailing spaces and the blank lines at both ends removed.
    /// </summary>
    public static string Clean(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var text = reply.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        int fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            int bodyStart = text.IndexOf('\n', fence);
            int close = bodyStart < 0 ? -1 : text.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (bodyStart >= 0)
            {
                text = (close < 0 ? text[(bodyStart + 1)..] : text[(bodyStart + 1)..close]).Trim();
            }
        }

        foreach (var lead in (ReadOnlySpan<string>)["Commit message:", "Commit Message:", "commit message:"])
        {
            if (text.StartsWith(lead, StringComparison.Ordinal))
            {
                text = text[lead.Length..].TrimStart();
            }
        }

        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'') || (text[0] == '`' && text[^1] == '`')))
        {
            text = text[1..^1].Trim();
        }

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        return string.Join('\n', lines).Trim('\n');
    }

    // The files of a diff: each from its "diff --git " line to the next one (bytes before the first are their own part).
    private static List<FilePart> SplitFiles(ReadOnlySpan<byte> diff)
    {
        var files = new List<FilePart>();
        int start = 0;
        int at = 0;
        while (at < diff.Length)
        {
            int newline = diff[at..].IndexOf((byte)'\n');
            int next = newline < 0 ? diff.Length : at + newline + 1;
            if (at > start && diff[at..].StartsWith("diff --git "u8))
            {
                files.Add(Part(diff, start, at - start));
                start = at;
            }

            at = next;
        }

        if (diff.Length > start)
        {
            files.Add(Part(diff, start, diff.Length - start));
        }

        return files;
    }

    private static FilePart Part(ReadOnlySpan<byte> diff, int start, int length)
    {
        var part = diff.Slice(start, length);
        int added = 0, removed = 0;
        string name = "";
        foreach (var range in part.Split((byte)'\n'))
        {
            var line = part[range];
            if (line.StartsWith("diff --git "u8))
            {
                // "diff --git a/PATH b/PATH": the path after " b/".
                int b = line.LastIndexOf(" b/"u8);
                name = b >= 0 ? Encoding.UTF8.GetString(line[(b + 3)..]).TrimEnd('\r') : Encoding.UTF8.GetString(line["diff --git "u8.Length..]);
            }
            else if (line.StartsWith("+++"u8) || line.StartsWith("---"u8))
            {
                continue;
            }
            else if (line.Length > 0 && line[0] == (byte)'+')
            {
                added++;
            }
            else if (line.Length > 0 && line[0] == (byte)'-')
            {
                removed++;
            }
        }

        return new FilePart(start, length, name, added, removed);
    }

    private static string Summary(FilePart file, ReadOnlySpan<byte> diff) =>
        string.Create(CultureInfo.InvariantCulture, $"{(file.Name.Length > 0 ? file.Name : "(changes)")}: +{file.Added} −{file.Removed} lines, summarised");

    private readonly record struct FilePart(int Start, int Length, string Name, int Added, int Removed);
}
