// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;
using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Spelling;

/// <summary>
/// A spell dictionary from plain word lists (one word a line, UTF-8): the bundled "en" is SCOWL's English list (88,000 words,
/// its affixes expanded; its licence beside it in <c>Spelling/Words</c>) and the words of code ("async", "enum", "stdout").
/// The lists are read once, off the calling thread, from their bytes (rule 74) into a <see cref="FrozenSet{T}"/> ignoring
/// case, looked up through its span lookup (rule 15: a word is never made a string to be checked). Suggestions are the
/// words within two edits (Damerau's: a swap of neighbours is one) of a word, nearest first.
/// </summary>
public sealed class WordListDictionary : ISpellDictionary
{
    private readonly Func<IEnumerable<byte[]>> _lists;
    private readonly Lazy<Task> _load;
    private FrozenSet<string> _words = FrozenSet<string>.Empty;
    private FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private string[][] _byLength = [];
    private volatile bool _ready;

    /// <summary>A dictionary <paramref name="name"/> of the word lists <paramref name="lists"/> gives (each a UTF-8 file's bytes), read on first use.</summary>
    public WordListDictionary(string name, string summary, Func<IEnumerable<byte[]>> lists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _lookup = _words.GetAlternateLookup<ReadOnlySpan<char>>();
        _load = new Lazy<Task>(() => Task.Run(Load), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The bundled English dictionary ("en"): SCOWL's list and the words of code, from this assembly's resources.</summary>
    public static WordListDictionary English { get; } = new("en", "English (SCOWL size 60, 88,000 words, and the words of code), bundled", () =>
        [Resource("Craft.Editor.Spelling.Words.en.txt"), Resource("Craft.Editor.Spelling.Words.code.txt")]);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <summary>How many words it holds (0 until it is ready).</summary>
    public int Count => _words.Count;

    /// <inheritdoc />
    public bool IsReady
    {
        get
        {
            if (!_ready)
            {
                _ = _load.Value;
            }

            return _ready;
        }
    }

    /// <inheritdoc />
    public Task Ready => _load.Value;

    /// <inheritdoc />
    public bool Contains(ReadOnlySpan<char> word)
    {
        if (!IsReady)
        {
            return true;
        }

        if (_lookup.Contains(word))
        {
            return true;
        }

        // A possessive ("editor's", "users'") is its word's.
        if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            return _lookup.Contains(word[..^2]);
        }

        return word.EndsWith('\'') && _lookup.Contains(word[..^1]);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, int max)
    {
        if (!IsReady || word.IsEmpty || max <= 0 || word.Length > 64)
        {
            return [];
        }

        Span<char> lower = stackalloc char[word.Length];
        word.ToLowerInvariant(lower);
        var found = new List<(int Distance, int Rank, string Word)>();
        for (int length = Math.Max(1, word.Length - 2); length <= word.Length + 2 && length < _byLength.Length; length++)
        {
            foreach (var candidate in _byLength[length])
            {
                int distance = Distance(lower, candidate, 2);
                if (distance <= 2)
                {
                    int rank = (candidate[0] == lower[0] ? 0 : 2) + Math.Abs(candidate.Length - lower.Length);
                    found.Add((distance, rank, candidate));
                }
            }
        }

        found.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank)
            : string.CompareOrdinal(a.Word, b.Word));
        var suggestions = new List<string>(Math.Min(max, found.Count));
        foreach (var (_, _, candidate) in found)
        {
            if (suggestions.Count == max)
            {
                break;
            }

            suggestions.Add(CaseLike(word, candidate));
        }

        return suggestions;
    }

    /// <summary>
    /// Damerau's edit distance (a neighbour swap is one edit) of <paramref name="a"/> and <paramref name="b"/> (ignoring case
    /// of <paramref name="b"/>'s letters), or <paramref name="limit"/> + 1 as soon as it is past <paramref name="limit"/>.
    /// </summary>
    public static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        int columns = b.Length + 1;
        if (columns > 66)
        {
            return limit + 1;
        }

        Span<int> before = stackalloc int[columns];
        Span<int> previous = stackalloc int[columns];
        Span<int> current = stackalloc int[columns];
        for (int j = 0; j < columns; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int best = current[0];
            for (int j = 1; j < columns; j++)
            {
                int cost = a[i - 1] == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                int value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == char.ToLowerInvariant(b[j - 2]) && a[i - 2] == char.ToLowerInvariant(b[j - 1]))
                {
                    value = Math.Min(value, before[j - 2] + 1);
                }

                current[j] = value;
                best = Math.Min(best, value);
            }

            if (best > limit)
            {
                return limit + 1;
            }

            var spare = before;
            before = previous;
            previous = current;
            current = spare;
        }

        return previous[b.Length];
    }

    // A suggestion written as the word was: "Recieve" → "Receive", "RECIEVE" → "RECEIVE".
    private static string CaseLike(ReadOnlySpan<char> word, string suggestion)
    {
        bool allUpper = word.Length > 1;
        foreach (char c in word)
        {
            if (char.IsLetter(c) && !char.IsUpper(c))
            {
                allUpper = false;
                break;
            }
        }

        if (allUpper)
        {
            return suggestion.ToUpperInvariant();
        }

        return char.IsUpper(word[0]) && suggestion.Length > 0 && char.IsLower(suggestion[0])
            ? string.Create(suggestion.Length, suggestion, static (span, s) =>
            {
                s.AsSpan().CopyTo(span);
                span[0] = char.ToUpperInvariant(span[0]);
            })
            : suggestion;
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(WordListDictionary).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The word list '{name}' is not in Craft.Editor's resources.");
        using var copy = new MemoryStream((int)stream.Length);
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // The lists' lines as words (a line's '\r' and spaces trimmed, an empty line and a '#' line passed over).
    private void Load()
    {
        var words = new List<string>(100_000);
        foreach (var list in _lists())
        {
            var bytes = list.AsSpan();
            if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                bytes = bytes[3..];
            }

            while (!bytes.IsEmpty)
            {
                int end = bytes.IndexOf((byte)'\n');
                var line = end < 0 ? bytes : bytes[..end];
                bytes = end < 0 ? default : bytes[(end + 1)..];
                line = line.Trim((byte)'\r').Trim((byte)' ');
                if (!line.IsEmpty && line[0] != (byte)'#')
                {
                    words.Add(Encoding.UTF8.GetString(line));
                }
            }
        }

        var set = words.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        int longest = 0;
        foreach (var word in set)
        {
            longest = Math.Max(longest, word.Length);
        }

        var byLength = new List<string>[longest + 1];
        for (int i = 0; i <= longest; i++)
        {
            byLength[i] = [];
        }

        foreach (var word in set)
        {
            byLength[word.Length].Add(word.ToLowerInvariant());
        }

        _byLength = [.. byLength.Select(l => l.ToArray())];
        _lookup = set.GetAlternateLookup<ReadOnlySpan<char>>();
        _words = set;
        _ready = true;
    }
}
