// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers.Text;
using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>A local branch: its name, its upstream and how far it is from it.</summary>
/// <param name="Name">Its name ("main").</param>
/// <param name="Upstream">Its upstream ("origin/main"), or null.</param>
/// <param name="Ahead">Commits the upstream does not have.</param>
/// <param name="Behind">Commits on the upstream it does not have.</param>
public sealed record GitBranchInfo(string Name, string? Upstream, int Ahead, int Behind);

/// <summary>
/// git's machine-readable output parsed as bytes (rule 74): <c>status --porcelain=v2 -z --branch --show-stash</c>, the
/// branches of <c>for-each-ref</c>, and the hunks of a unified diff; text is made once per path or name, at the edge.
/// </summary>
public static class GitParsers
{
    /// <summary>
    /// The status of the repository at <paramref name="root"/> from <c>git status --porcelain=v2 -z --branch
    /// --show-stash</c>'s <paramref name="output"/>: the branch headers, then each entry (ordinary "1", renamed or copied
    /// "2" with its old path after a NUL, unmerged "u", untracked "?"; ignored "!" skipped), its path relative to the root.
    /// </summary>
    public static SourceControlStatus Status(string root, ReadOnlySpan<byte> output)
    {
        ArgumentNullException.ThrowIfNull(root);
        string? branch = null, upstream = null, oid = null;
        int ahead = 0, behind = 0, stashes = 0;
        bool detached = false;
        var staged = new List<SourceControlFile>();
        var changes = new List<SourceControlFile>();
        var conflicts = new List<SourceControlFile>();
        int at = 0;
        while (at < output.Length)
        {
            int end = output[at..].IndexOf((byte)0);
            var record = end < 0 ? output[at..] : output.Slice(at, end);
            at = end < 0 ? output.Length : at + end + 1;
            if (record.Length < 2)
            {
                continue;
            }

            switch (record[0])
            {
                case (byte)'#':
                    Header(record, ref branch, ref upstream, ref oid, ref ahead, ref behind, ref stashes, ref detached);
                    break;
                case (byte)'1':
                    {
                        // 1 XY sub mH mI mW hH hI path
                        if (Field(record, 8) is not int pathAt)
                        {
                            break;
                        }

                        Entry(root, record[2], record[3], Path(record[pathAt..]), null, staged, changes);
                        break;
                    }

                case (byte)'2':
                    {
                        // 2 XY sub mH mI mW hH hI Xscore path NUL origPath
                        if (Field(record, 9) is not int pathAt)
                        {
                            break;
                        }

                        string? old = null;
                        if (at < output.Length)
                        {
                            int oldEnd = output[at..].IndexOf((byte)0);
                            var original = oldEnd < 0 ? output[at..] : output.Slice(at, oldEnd);
                            old = Path(original);
                            at = oldEnd < 0 ? output.Length : at + oldEnd + 1;
                        }

                        Entry(root, record[2], record[3], Path(record[pathAt..]), old, staged, changes);
                        break;
                    }

                case (byte)'u':
                    {
                        // u XY sub m1 m2 m3 mW h1 h2 h3 path
                        if (Field(record, 10) is not int pathAt)
                        {
                            break;
                        }

                        var relative = Path(record[pathAt..]);
                        conflicts.Add(new SourceControlFile(Full(root, relative), SourceControlState.Conflict, false) { RelativePath = relative });
                        break;
                    }

                case (byte)'?':
                    {
                        var relative = Path(record[2..]);
                        changes.Add(new SourceControlFile(Full(root, relative), SourceControlState.Untracked, false) { RelativePath = relative });
                        break;
                    }
            }
        }

        if (detached && oid is { Length: > 0 })
        {
            branch = oid.Length > 8 ? oid[..8] : oid;
        }

        return new SourceControlStatus(root, branch)
        {
            Upstream = upstream,
            Ahead = ahead,
            Behind = behind,
            Detached = detached,
            Staged = staged,
            Changes = changes,
            Conflicts = conflicts,
            Stashes = stashes,
        };
    }

    /// <summary>
    /// The local branches from <c>git for-each-ref --format=%(refname:short)%09%(upstream:short)%09%(upstream:track)
    /// refs/heads</c>'s <paramref name="output"/> (one a line: the name, a tab, the upstream, a tab, "[ahead 1, behind 2]").
    /// </summary>
    public static IReadOnlyList<GitBranchInfo> Branches(ReadOnlySpan<byte> output)
    {
        var branches = new List<GitBranchInfo>();
        foreach (var range in output.Split((byte)'\n'))
        {
            var line = output[range].TrimEnd((byte)'\r');
            if (line.IsEmpty)
            {
                continue;
            }

            int tab = line.IndexOf((byte)'\t');
            var name = tab < 0 ? line : line[..tab];
            string? upstream = null;
            int ahead = 0, behind = 0;
            if (tab >= 0)
            {
                var rest = line[(tab + 1)..];
                int second = rest.IndexOf((byte)'\t');
                var up = second < 0 ? rest : rest[..second];
                upstream = up.IsEmpty ? null : Encoding.UTF8.GetString(up);
                if (second >= 0)
                {
                    var track = rest[(second + 1)..];
                    ahead = Count(track, "ahead "u8);
                    behind = Count(track, "behind "u8);
                }
            }

            branches.Add(new GitBranchInfo(Encoding.UTF8.GetString(name), upstream, ahead, behind));
        }

        return branches;
    }

    /// <summary>
    /// The hunks of a unified diff (<c>git diff -U0</c>'s <paramref name="output"/>) as (old start, old count, new start,
    /// new count), read from each "@@ -a,b +c,d @@" line (a count left out is 1).
    /// </summary>
    public static IReadOnlyList<(int OldStart, int OldCount, int NewStart, int NewCount)> Hunks(ReadOnlySpan<byte> output)
    {
        var hunks = new List<(int, int, int, int)>();
        foreach (var range in output.Split((byte)'\n'))
        {
            var line = output[range];
            if (!line.StartsWith("@@ -"u8))
            {
                continue;
            }

            var rest = line[4..];
            if (!Range(ref rest, out int oldStart, out int oldCount) || rest.Length < 2 || rest[0] != (byte)' ' || rest[1] != (byte)'+')
            {
                continue;
            }

            rest = rest[2..];
            if (Range(ref rest, out int newStart, out int newCount))
            {
                hunks.Add((oldStart, oldCount, newStart, newCount));
            }
        }

        return hunks;
    }

    /// <summary>
    /// The gutter's marks of hunks given in a unified diff's numbers with no context lines: lines only added are
    /// <see cref="LineMarkKind.Added"/>, lines only removed a <see cref="LineMarkKind.Deleted"/> wedge before the line that
    /// follows them, the rest <see cref="LineMarkKind.Changed"/> (0-based lines of the new text).
    /// </summary>
    public static IReadOnlyList<LineMark> Marks(IEnumerable<(int OldStart, int OldCount, int NewStart, int NewCount)> hunks)
    {
        ArgumentNullException.ThrowIfNull(hunks);
        var marks = new List<LineMark>();
        foreach (var (_, oldCount, newStart, newCount) in hunks)
        {
            if (newCount == 0)
            {
                // A side with no line starts at the line before the hunk: the removed lines stood before line newStart (0-based).
                marks.Add(new LineMark(Math.Max(0, newStart), oldCount, LineMarkKind.Deleted));
            }
            else
            {
                marks.Add(new LineMark(newStart - 1, newCount, oldCount == 0 ? LineMarkKind.Added : LineMarkKind.Changed));
            }
        }

        return marks;
    }

    /// <summary>The marks of <paramref name="diff"/> (worked out with no context lines).</summary>
    public static IReadOnlyList<LineMark> Marks(TextDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return Marks(diff.Hunks.Select(h => (h.OldStart, h.OldCount, h.NewStart, h.NewCount)));
    }

    /// <summary>
    /// The merge conflicts' blocks of <paramref name="text"/>: each from its "&lt;&lt;&lt;&lt;&lt;&lt;&lt;" line to its
    /// "&gt;&gt;&gt;&gt;&gt;&gt;&gt;" line (0-based lines), with its "=======" line and a diff3 base's "|||||||" line.
    /// </summary>
    public static IReadOnlyList<ConflictBlock> Conflicts(ReadOnlySpan<char> text)
    {
        var blocks = new List<ConflictBlock>();
        int line = 0, start = -1, middle = -1, bases = -1;
        int at = 0;
        while (at <= text.Length)
        {
            int newline = text[at..].IndexOf('\n');
            var current = newline < 0 ? text[at..] : text.Slice(at, newline);
            if (current.StartsWith("<<<<<<<"))
            {
                start = line;
                middle = bases = -1;
            }
            else if (start >= 0 && current.StartsWith("|||||||") && middle < 0)
            {
                bases = line;
            }
            else if (start >= 0 && current.StartsWith("=======") && middle < 0)
            {
                middle = line;
            }
            else if (start >= 0 && middle >= 0 && current.StartsWith(">>>>>>>"))
            {
                blocks.Add(new ConflictBlock(start, bases, middle, line));
                start = middle = bases = -1;
            }

            if (newline < 0)
            {
                break;
            }

            at += newline + 1;
            line++;
        }

        return blocks;
    }

    /// <summary>
    /// <paramref name="text"/>'s lines with their breaks kept (so lines put back together give the text exactly): a split
    /// after each "\n" (a "\r\n" stays with its line), the last line as it is.
    /// </summary>
    public static List<string> LinesWithBreaks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        int at = 0;
        while (at < text.Length)
        {
            int newline = text.IndexOf('\n', at);
            int end = newline < 0 ? text.Length : newline + 1;
            lines.Add(text[at..end]);
            at = end;
        }

        return lines;
    }

    // "# branch.oid X", "# branch.head X", "# branch.upstream X", "# branch.ab +A -B", "# stash N".
    private static void Header(ReadOnlySpan<byte> record, ref string? branch, ref string? upstream, ref string? oid, ref int ahead, ref int behind, ref int stashes, ref bool detached)
    {
        if (record.StartsWith("# branch.head "u8))
        {
            var head = record["# branch.head "u8.Length..];
            detached = head.SequenceEqual("(detached)"u8);
            branch = detached ? null : Encoding.UTF8.GetString(head);
        }
        else if (record.StartsWith("# branch.oid "u8))
        {
            var value = record["# branch.oid "u8.Length..];
            oid = value.SequenceEqual("(initial)"u8) ? null : Encoding.UTF8.GetString(value);
        }
        else if (record.StartsWith("# branch.upstream "u8))
        {
            upstream = Encoding.UTF8.GetString(record["# branch.upstream "u8.Length..]);
        }
        else if (record.StartsWith("# branch.ab +"u8))
        {
            var value = record["# branch.ab +"u8.Length..];
            if (Utf8Parser.TryParse(value, out int a, out int used) && used + 2 <= value.Length && value[used] == (byte)' ' && value[used + 1] == (byte)'-'
                && Utf8Parser.TryParse(value[(used + 2)..], out int b, out _))
            {
                ahead = a;
                behind = b;
            }
        }
        else if (record.StartsWith("# stash "u8) && Utf8Parser.TryParse(record["# stash "u8.Length..], out int count, out _))
        {
            stashes = count;
        }
    }

    // An ordinary or renamed entry: X is the index's state (a staged change), Y the working copy's ('.' for none).
    private static void Entry(string root, byte x, byte y, string relative, string? oldRelative, List<SourceControlFile> staged, List<SourceControlFile> changes)
    {
        string full = Full(root, relative);
        string? oldFull = oldRelative is null ? null : Full(root, oldRelative);
        if (x != (byte)'.' && State(x) is { } indexState)
        {
            staged.Add(new SourceControlFile(full, indexState, true) { RelativePath = relative, OldPath = indexState is SourceControlState.Renamed or SourceControlState.Copied ? oldFull : null });
        }

        if (y != (byte)'.' && State(y) is { } workState)
        {
            changes.Add(new SourceControlFile(full, workState, false) { RelativePath = relative, OldPath = workState is SourceControlState.Renamed or SourceControlState.Copied ? oldFull : null });
        }
    }

    private static SourceControlState? State(byte code) => code switch
    {
        (byte)'M' => SourceControlState.Modified,
        (byte)'A' => SourceControlState.Added,
        (byte)'D' => SourceControlState.Deleted,
        (byte)'R' => SourceControlState.Renamed,
        (byte)'C' => SourceControlState.Copied,
        (byte)'T' => SourceControlState.TypeChanged,
        (byte)'U' => SourceControlState.Conflict,
        _ => null,
    };

    // The offset after the record's n-th space (where its path starts), or null when it has fewer fields.
    private static int? Field(ReadOnlySpan<byte> record, int spaces)
    {
        int at = 0;
        for (int i = 0; i < spaces; i++)
        {
            int space = record[at..].IndexOf((byte)' ');
            if (space < 0)
            {
                return null;
            }

            at += space + 1;
        }

        return at < record.Length ? at : null;
    }

    private static string Path(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    private static string Full(string root, string relative) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    private static int Count(ReadOnlySpan<byte> track, ReadOnlySpan<byte> word)
    {
        int at = track.IndexOf(word);
        return at >= 0 && Utf8Parser.TryParse(track[(at + word.Length)..], out int value, out _) ? value : 0;
    }

    // "a,b" or "a" (a count of 1) at the start of `rest`, which is moved past it.
    private static bool Range(ref ReadOnlySpan<byte> rest, out int start, out int count)
    {
        count = 1;
        if (!Utf8Parser.TryParse(rest, out start, out int used))
        {
            return false;
        }

        rest = rest[used..];
        if (rest.Length > 0 && rest[0] == (byte)',')
        {
            if (!Utf8Parser.TryParse(rest[1..], out count, out used))
            {
                return false;
            }

            rest = rest[(used + 1)..];
        }

        return true;
    }
}

/// <summary>A merge conflict's block (0-based lines): its start marker, its base marker (diff3, or -1), its separator and its end marker.</summary>
/// <param name="Start">The "&lt;&lt;&lt;&lt;&lt;&lt;&lt;" line: the current (ours) lines follow.</param>
/// <param name="Base">The "|||||||" line of a diff3 conflict, or -1.</param>
/// <param name="Middle">The "=======" line: the incoming (theirs) lines follow.</param>
/// <param name="End">The "&gt;&gt;&gt;&gt;&gt;&gt;&gt;" line.</param>
public readonly record struct ConflictBlock(int Start, int Base, int Middle, int End);
