// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;

namespace Craft.Abstractions;

/// <summary>What a file pattern of a <see cref="FilePatternTable{T}"/> is, by its shape.</summary>
public enum FilePatternKind
{
    /// <summary>An exact file name, ignoring case ("package.json", "Dockerfile").</summary>
    Name,

    /// <summary>
    /// A compound suffix: the name ends with it at one of its dots (".d.ts", ".component.ts"); a glob of a star and a suffix
    /// ("*.spec.ts") is one too.
    /// </summary>
    Suffix,

    /// <summary>A glob: '*' any run of characters, '?' one ("tsconfig*.json", "eslint.config.*").</summary>
    Glob,

    /// <summary>An extension with its dot (".cs", ".gitignore"): the name's part from its last dot.</summary>
    Extension,
}

/// <summary>
/// Values by file name pattern (W, contracts 3 and 5): an exact name, a compound suffix, a glob or an extension, each
/// ignoring case. <see cref="Find"/> ranks an exact name first, then the longest of the suffixes and globs (by their
/// characters other than wildcards), then the extension; it walks the name's dots with alternate lookups and allocates
/// nothing (docs/optimization.md rules 15, 17). Immutable once made; any thread.
/// </summary>
/// <typeparam name="T">The value (a language, an icon).</typeparam>
public sealed class FilePatternTable<T>
    where T : class
{
    private static readonly SearchValues<char> Wildcards = SearchValues.Create("*?");

    private readonly Dictionary<string, T> _names;
    private readonly Dictionary<string, T> _suffixes;
    private readonly Dictionary<string, T> _extensions;
    private readonly Dictionary<string, T>.AlternateLookup<ReadOnlySpan<char>> _nameLookup;
    private readonly Dictionary<string, T>.AlternateLookup<ReadOnlySpan<char>> _suffixLookup;
    private readonly Dictionary<string, T>.AlternateLookup<ReadOnlySpan<char>> _extensionLookup;
    private readonly (string Pattern, int Rank, T Value)[] _globs;

    /// <summary>
    /// A table of <paramref name="entries"/>, each a pattern and its value; the first value of a pattern wins (a later one
    /// of the same pattern is ignored), and globs of the same rank keep their order.
    /// </summary>
    public FilePatternTable(IEnumerable<KeyValuePair<string, T>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _names = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        _suffixes = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        _extensions = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var globs = new List<(string, int, T)>();
        var seenGlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pattern, value) in entries)
        {
            if (string.IsNullOrWhiteSpace(pattern) || value is null)
            {
                continue;
            }

            switch (KindOf(pattern))
            {
                case FilePatternKind.Name:
                    _names.TryAdd(pattern, value);
                    break;
                case FilePatternKind.Extension:
                    _extensions.TryAdd(pattern, value);
                    break;
                case FilePatternKind.Suffix:
                    _suffixes.TryAdd(pattern[0] == '*' ? pattern[1..] : pattern, value);
                    break;
                default:
                    if (seenGlobs.Add(pattern))
                    {
                        globs.Add((pattern, RankOf(pattern), value));
                    }

                    break;
            }
        }

        // Longest first; a stable sort keeps the registration order among globs of one rank.
        _globs = [.. globs.Select((g, i) => (g, i)).OrderByDescending(x => x.g.Item2).ThenBy(x => x.i).Select(x => x.g)];
        _nameLookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        _suffixLookup = _suffixes.GetAlternateLookup<ReadOnlySpan<char>>();
        _extensionLookup = _extensions.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>An empty table.</summary>
    public static FilePatternTable<T> Empty { get; } = new([]);

    /// <summary>How many patterns it holds.</summary>
    public int Count => _names.Count + _suffixes.Count + _extensions.Count + _globs.Length;

    /// <summary>
    /// The value of the file named <paramref name="fileName"/> (a name, not a path: <see cref="FindForPath"/> takes one):
    /// its exact name's, else the longest suffix's or glob's, else its extension's; null when none matches.
    /// </summary>
    public T? Find(ReadOnlySpan<char> fileName)
    {
        if (fileName.IsEmpty)
        {
            return null;
        }

        if (_names.Count > 0 && _nameLookup.TryGetValue(fileName, out var named))
        {
            return named;
        }

        T? best = null;
        int bestRank = -1;
        if (_suffixes.Count > 0)
        {
            // The leftmost dot whose rest is a suffix gives the longest one.
            int at = fileName.IndexOf('.');
            while (at >= 0)
            {
                var rest = fileName[at..];
                if (_suffixLookup.TryGetValue(rest, out var suffixed))
                {
                    best = suffixed;
                    bestRank = rest.Length;
                    break;
                }

                int next = rest[1..].IndexOf('.');
                at = next < 0 ? -1 : at + 1 + next;
            }
        }

        foreach (var (pattern, rank, value) in _globs)
        {
            if (rank <= bestRank)
            {
                break;                                   // the rest are shorter than what was found
            }

            if (GlobMatches(pattern, fileName))
            {
                best = value;
                break;
            }
        }

        if (best is not null)
        {
            return best;
        }

        if (_extensions.Count > 0)
        {
            int dot = fileName.LastIndexOf('.');
            if (dot >= 0 && dot < fileName.Length - 1 && _extensionLookup.TryGetValue(fileName[dot..], out var extended))
            {
                return extended;
            }
        }

        return null;
    }

    /// <summary>The value of the file at <paramref name="path"/> by its name (<see cref="Find"/>).</summary>
    public T? FindForPath(ReadOnlySpan<char> path) => Find(Path.GetFileName(path));

    /// <summary>
    /// What <paramref name="pattern"/> is: a glob when it has '*' or '?' (a star and a literal suffix that starts with a dot
    /// is a <see cref="FilePatternKind.Suffix"/>), a compound suffix when it starts with a dot and has another, an
    /// extension when it starts with a dot and has no other, else an exact name.
    /// </summary>
    public static FilePatternKind KindOf(ReadOnlySpan<char> pattern)
    {
        if (pattern.ContainsAny(Wildcards))
        {
            return pattern.Length > 2 && pattern[0] == '*' && pattern[1] == '.' && !pattern[1..].ContainsAny(Wildcards) ? FilePatternKind.Suffix : FilePatternKind.Glob;
        }

        if (pattern.Length > 1 && pattern[0] == '.')
        {
            return pattern[1..].Contains('.') ? FilePatternKind.Suffix : FilePatternKind.Extension;
        }

        return FilePatternKind.Name;
    }

    /// <summary>How specific <paramref name="pattern"/> is: its characters other than '*' and '?'.</summary>
    public static int RankOf(ReadOnlySpan<char> pattern)
    {
        int rank = 0;
        foreach (char c in pattern)
        {
            if (c is not ('*' or '?'))
            {
                rank++;
            }
        }

        return rank;
    }

    /// <summary>Whether <paramref name="name"/> matches the glob <paramref name="pattern"/> ('*' any run, '?' one character), ignoring case; allocates nothing.</summary>
    public static bool GlobMatches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        int p = 0, n = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], name[n])))
            {
                p++;
                n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool Same(char a, char b) => a == b || (char.ToUpperInvariant(a) == char.ToUpperInvariant(b));
}
