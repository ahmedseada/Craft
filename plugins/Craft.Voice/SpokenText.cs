// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Voice;

/// <summary>A word of a phrase as heard ("Paren,") and as matched: lower case, the recognizer's marks off its ends ("paren").</summary>
/// <param name="Heard">The word as the recognizer wrote it.</param>
/// <param name="Normal">Lower case, without the punctuation at its ends; empty when the word was only punctuation.</param>
/// <param name="Bare">As heard, without the punctuation at its ends (what dictation types when the punctuation is the spoken one).</param>
internal readonly record struct SpokenWord(string Heard, string Normal, string Bare);

/// <summary>The words of a phrase, number words, the dictation of words and spoken symbols into text, and the voice commands a phrase names.</summary>
internal static class SpokenText
{
    private static readonly SearchValues<char> Marks = SearchValues.Create(".,!?;:\"'()[]{}«»“”‘’…");

    private static readonly string[] Articles = ["the", "a", "an", "please"];

    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.Ordinal)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>The words of <paramref name="phrase"/>, split at white space; words that are only punctuation are dropped.</summary>
    public static List<SpokenWord> Split(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        var words = new List<SpokenWord>();
        foreach (var heard in phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var span = heard.AsSpan();
            int start = span.IndexOfAnyExcept(Marks);
            if (start < 0)
            {
                continue;
            }

            int end = span.LastIndexOfAnyExcept(Marks) + 1;
            string bare = start == 0 && end == span.Length ? heard : heard[start..end];
            words.Add(new SpokenWord(heard, bare.ToLowerInvariant(), bare));
        }

        return words;
    }

    /// <summary>
    /// The number said at <paramref name="at"/> ("forty two", "one hundred and five", "40"): its value and how many words it
    /// took; false when no number starts there. A unit after a unit ends the number ("four two" is 4, then 2).
    /// </summary>
    public static bool TryNumber(IReadOnlyList<string> words, int at, out long value, out int consumed)
    {
        value = 0;
        consumed = 0;
        if (at >= words.Count)
        {
            return false;
        }

        if (long.TryParse(words[at], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            consumed = 1;
            return true;
        }

        long total = 0, current = 0;
        int i = at;
        // What came last: 0 nothing, 1 a unit or teen, 2 a ten, 3 a scale (hundred, thousand, million).
        int last = 0;
        while (i < words.Count)
        {
            var word = words[i];
            if (Units.TryGetValue(word, out int unit) && last != 1 && !(last == 2 && unit >= 10))
            {
                current += unit;
                last = 1;
            }
            else if (Tens.TryGetValue(word, out int ten) && last is 0 or 3)
            {
                current += ten;
                last = 2;
            }
            else if (word == "hundred" && last is 1 or 2 && current < 100)
            {
                current *= 100;
                last = 3;
            }
            else if (word is "thousand" or "million" && last != 0)
            {
                total += Math.Max(current, 1) * (word == "thousand" ? 1000 : 1000000);
                current = 0;
                last = 3;
            }
            else if (word == "and" && last == 3 && i + 1 < words.Count && (Units.ContainsKey(words[i + 1]) || Tens.ContainsKey(words[i + 1])))
            {
                // "one hundred and five"
            }
            else
            {
                break;
            }

            i++;
        }

        if (last == 0)
        {
            return false;
        }

        value = total + current;
        consumed = i - at;
        return true;
    }

    /// <summary>
    /// The text <paramref name="words"/> dictate: the spoken symbols of <paramref name="symbols"/> (the longest match first)
    /// typed as their text with their spaces, number words as digits when <paramref name="digits"/>, the other words as
    /// heard (without the recognizer's punctuation unless <paramref name="keepPunctuation"/>), one space between words;
    /// a leading space when <paramref name="before"/> (the character before the caret) is part of a word.
    /// </summary>
    public static string Dictate(IReadOnlyList<SpokenWord> words, SymbolTable symbols, bool digits, bool keepPunctuation, char? before)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(symbols);
        var normals = new string[words.Count];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = words[i].Normal;
        }

        var text = new StringBuilder();
        bool space = before is { } c && !char.IsWhiteSpace(c) && c is not ('(' or '[' or '{' or '.' or '"' or '\'');
        bool started = false;
        for (int i = 0; i < words.Count;)
        {
            if (symbols.Match(normals, i, out var symbol, out int taken))
            {
                if (symbol.SpaceBefore && (started || space) && !EndsWithSpace(text, before))
                {
                    text.Append(' ');
                }

                text.Append(symbol.Text);
                space = symbol.SpaceAfter;
                started = true;
                i += taken;
                continue;
            }

            string word;
            if (digits && TryNumber(normals, i, out long number, out int consumed))
            {
                word = number.ToString(CultureInfo.InvariantCulture);
                i += consumed;
            }
            else
            {
                word = keepPunctuation ? words[i].Heard : words[i].Bare;
                i++;
            }

            if (space)
            {
                text.Append(' ');
            }

            text.Append(word);
            space = true;
            started = true;
        }

        return text.ToString();
    }

    /// <summary>
    /// The spoken command <paramref name="words"/> name: a registered <see cref="SpokenCommand"/> (its phrase, a number where
    /// it says "#"), else a registered command whose title says them ("save" is "File: Save"; "format document" is "Edit:
    /// Format document"), "the", "a", "an" and "please" left out on both sides; null when none does.
    /// </summary>
    public static (SpokenCommand? Spoken, IEditorCommand? Command, int Number)? FindCommand(IReadOnlyList<SpokenWord> words, IReadOnlyList<IEditorCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(commands);
        var said = new List<string>(words.Count);
        foreach (var word in words)
        {
            if (word.Normal.Length > 0 && Array.IndexOf(Articles, word.Normal) < 0)
            {
                said.Add(word.Normal);
            }
        }

        if (said.Count == 0)
        {
            return null;
        }

        foreach (var spoken in SpokenCommands.All)
        {
            if (MatchPhrase(Words(spoken.Phrase), said, out int number))
            {
                return (spoken, null, number);
            }
        }

        foreach (var command in commands)
        {
            var title = command.Title;
            int colon = title.IndexOf(": ", StringComparison.Ordinal);
            if (Same(Words(colon < 0 ? title : title[(colon + 2)..]), said) || (colon >= 0 && Same(Words(title), said)))
            {
                return (null, command, 0);
            }
        }

        return null;
    }

    // A title or a phrase as matched words: lower case, the marks gone, the articles left out.
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        foreach (var word in Split(text))
        {
            if (word.Normal.Length > 0 && Array.IndexOf(Articles, word.Normal) < 0)
            {
                words.Add(word.Normal);
            }
        }

        return words;
    }

    private static bool Same(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchPhrase(List<string> pattern, List<string> said, out int number)
    {
        number = 0;
        int at = 0;
        foreach (var part in pattern)
        {
            if (part == SpokenCommand.NumberWord)
            {
                if (!TryNumber(said, at, out long value, out int consumed) || value is < int.MinValue or > int.MaxValue)
                {
                    return false;
                }

                number = (int)value;
                at += consumed;
            }
            else if (at < said.Count && string.Equals(said[at], part, StringComparison.Ordinal))
            {
                at++;
            }
            else
            {
                return false;
            }
        }

        return at == said.Count;
    }

    private static bool EndsWithSpace(StringBuilder text, char? before) =>
        text.Length > 0 ? char.IsWhiteSpace(text[^1]) : before is null || char.IsWhiteSpace(before.Value);
}

/// <summary>The spoken symbols of one language, by their words, built once per language and registry version.</summary>
internal sealed class SymbolTable
{
    private readonly Dictionary<string, SpokenSymbol> _byWords = new(StringComparer.Ordinal);
    private readonly int _longest;

    public SymbolTable(IReadOnlyList<SpokenSymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        foreach (var symbol in symbols)
        {
            var words = string.Join(' ', symbol.Words.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (_byWords.TryAdd(words, symbol))
            {
                _longest = Math.Max(_longest, words.Count(c => c == ' ') + 1);
            }
        }
    }

    /// <summary>The language it was built for, and the registry's version then.</summary>
    public (string Language, int Version) Key { get; init; }

    /// <summary>How many symbols it holds.</summary>
    public int Count => _byWords.Count;

    /// <summary>The longest symbol said at <paramref name="at"/> of <paramref name="words"/>, and how many words it takes.</summary>
    public bool Match(string[] words, int at, out SpokenSymbol symbol, out int taken)
    {
        for (int n = Math.Min(_longest, words.Length - at); n >= 1; n--)
        {
            if (words[at].Length == 0)
            {
                break;
            }

            var key = n == 1 ? words[at] : string.Join(' ', words, at, n);
            if (_byWords.TryGetValue(key, out symbol!))
            {
                taken = n;
                return true;
            }
        }

        symbol = null!;
        taken = 0;
        return false;
    }
}
