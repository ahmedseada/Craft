// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// A language's words for the spell check of comments and strings (X part 9), registered in <see cref="SpellDictionaries"/>
/// under its language tag ("en", <c>CRAFT_SPELL_LANGUAGE</c>). It is asked on the paint's path only when a line changed,
/// from any thread, and never waits: a dictionary still loading answers <see cref="IsReady"/> false and every word is taken
/// as known until it is.
/// </summary>
public interface ISpellDictionary : IKind
{
    /// <summary>Whether its words are in (loading starts on the first call, off the calling thread).</summary>
    bool IsReady { get; }

    /// <summary>Completes when its words are in.</summary>
    Task Ready { get; }

    /// <summary>Whether <paramref name="word"/> is one of its words (ignoring case); allocates nothing.</summary>
    bool Contains(ReadOnlySpan<char> word);

    /// <summary>At most <paramref name="max"/> of its words closest to <paramref name="word"/> (by edit distance, then by how common), in order.</summary>
    IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, int max);
}

/// <summary>
/// The spell check's dictionaries, by language tag (ignoring case). Craft's editor registers "en" (a bundled English word list
/// and the words of code, plain text, no native library); a plug-in registers another language. An unknown tag fails with
/// "not registered".
/// </summary>
public static class SpellDictionaries
{
    private static readonly KindTable<ISpellDictionary> Table = new(nameof(SpellDictionaries), "Spell dictionary", "add it with SpellDictionaries.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a dictionary answers for its own language; none stands in for another");

    /// <summary>Registers <paramref name="dictionary"/> under its language tag.</summary>
    public static void Register(ISpellDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Table.Register(dictionary, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the dictionary of that tag; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered tags, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered dictionaries, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ISpellDictionary> All => Table.Kinds;

    /// <summary>The dictionary of that tag, or null.</summary>
    public static ISpellDictionary? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The dictionary of that tag.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ISpellDictionary Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>
/// The words of a run of text as a spell check reads them: letters (an apostrophe between two letters kept, "don't"), split
/// where an identifier's parts meet: camelCase ("parseLine" → "parse", "Line"), PascalCase with an acronym
/// ("HTTPServer" → "HTTP", "Server"), snake_case, kebab-case and digits ("utf8Text" → "utf", "Text"). Allocates nothing.
/// </summary>
public static class SpellWords
{
    /// <summary>The words of <paramref name="text"/>, each as its start and length in the text.</summary>
    public static Enumerator Of(ReadOnlySpan<char> text) => new(text);

    /// <summary>
    /// Whether <paramref name="text"/>'s run around <paramref name="start"/> is not prose to check: an address
    /// ("https://..."), a path, an e-mail, or a word with a digit or an underscore next to it is passed over.
    /// </summary>
    public static bool InUrlOrPath(ReadOnlySpan<char> text, int start, int length)
    {
        int from = start;
        while (from > 0 && !char.IsWhiteSpace(text[from - 1]) && text[from - 1] is not ('"' or '\'' or '(' or '<' or '`'))
        {
            from--;
        }

        int to = start + length;
        while (to < text.Length && !char.IsWhiteSpace(text[to]) && text[to] is not ('"' or '\'' or ')' or '>' or '`'))
        {
            to++;
        }

        var run = text[from..to];
        return run.Contains("://", StringComparison.Ordinal) || run.Contains('@') || run.Contains('\\') || (run.Contains('/') && run.Length > 1)
            || run.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The enumerator of <see cref="Of"/>: <see cref="Current"/> is a word's start and length.</summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private int _at;

        internal Enumerator(ReadOnlySpan<char> text)
        {
            _text = text;
            _at = 0;
            Current = default;
        }

        /// <summary>The word: its start and length in the text.</summary>
        public (int Start, int Length) Current { get; private set; }

        /// <summary>Itself (a foreach takes it).</summary>
        public readonly Enumerator GetEnumerator() => this;

        /// <summary>Moves to the next word; false at the end.</summary>
        public bool MoveNext()
        {
            var text = _text;
            int i = _at;
            while (i < text.Length && !char.IsLetter(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                _at = i;
                return false;
            }

            int start = i;
            bool upperRun = char.IsUpper(text[i]);
            i++;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\'' && i + 1 < text.Length && char.IsLower(text[i + 1]) && char.IsLetter(text[i - 1]))
                {
                    i++;                                      // don't, it's: the apostrophe belongs to the word
                    upperRun = false;
                    continue;
                }

                if (!char.IsLetter(c))
                {
                    break;
                }

                bool upper = char.IsUpper(c);
                if (upper && !upperRun)
                {
                    break;                                    // parseLine: a capital after lower case starts a word
                }

                if (!upper && upperRun && i - start > 1)
                {
                    i--;                                      // HTTPServer: the last capital starts the next word
                    break;
                }

                upperRun = upper;
                i++;
            }

            Current = (start, i - start);
            _at = i;
            return true;
        }
    }
}
