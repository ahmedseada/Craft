// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Idrak.Abstraction.Generation;

namespace Craft.Plugins.Sampler;

/// <summary>Where the text is, for the brackets: in code, in a string or a character literal, or just after a backslash in one.</summary>
internal enum LexState : byte
{
    Code,
    String,
    StringEscape,
    Character,
    CharacterEscape,
}

/// <summary>
/// What each token of a vocabulary does to the brackets, made once per tokenizer (and vocabulary width) and shared by
/// every sampler of it: for each state the token may start in (<see cref="LexState"/>), its net change to the depth of
/// each bracket kind ((), [], {}), the lowest depth it reaches on the way (a token that closes before it opens, ")(",
/// reaches -1), and the state it leaves. Brackets inside a string or a character literal are text; a backslash escapes
/// the next character there; a line break ends a string or a character literal (an apostrophe in a comment never opens
/// one past its line). Special tokens and ids the tokenizer does not have change nothing. Kept compact: a token's index
/// into its vocabulary's distinct behaviours (most share the neutral one). Also, per state and kind, the tokens that go
/// below zero, the deepest first, so a sampler finds the ones a depth forbids without walking the vocabulary.
/// </summary>
internal sealed class BracketTable
{
    /// <summary>The bracket kinds: (), [], {}.</summary>
    public const int Kinds = 3;

    /// <summary>The states a token may start in.</summary>
    public const int States = 5;

    private static readonly ConditionalWeakTable<ITokenizer, Dictionary<int, BracketTable>> Tables = new();

    // Most tokens hold no bracket, quote, backslash or line break: they share profile 0 (nothing changes, the state stays).
    // Each other distinct behaviour is one profile, kept once: for each start state, the lowest depth and the change of
    // each kind, and the state it leaves.
    private readonly int[] _profileOf;    // [token] → its profile
    private readonly sbyte[] _lowest;     // [profile, state, kind]
    private readonly sbyte[] _change;     // [profile, state, kind]
    private readonly byte[] _end;         // [profile, state]
    private readonly int[][] _closers;    // [state * Kinds + kind] → tokens whose lowest depth is below zero, the deepest first

    private BracketTable(ITokenizer tokenizer, int vocabulary)
    {
        Vocabulary = vocabulary;
        _profileOf = new int[vocabulary];
        var lowestOf = new List<sbyte>(States * Kinds * 64);
        var changeOf = new List<sbyte>(States * Kinds * 64);
        var endOf = new List<byte>(States * 64);
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        AddProfile(new sbyte[States * Kinds], new sbyte[States * Kinds], [0, 1, 2, 3, 4]);       // profile 0: neutral

        var pretrained = tokenizer as IPretrainedTokenizer;
        int decodable = Math.Min(vocabulary, tokenizer.VocabularySize);
        Span<int> lowest = stackalloc int[Kinds];
        Span<int> depth = stackalloc int[Kinds];
        var profileLowest = new sbyte[States * Kinds];
        var profileChange = new sbyte[States * Kinds];
        var profileEnd = new byte[States];
        var key = new char[States * (2 * Kinds + 1)];
        for (int token = 0; token < decodable; token++)
        {
            if (pretrained?.IsSpecial(token) == true)
            {
                continue;
            }

            string text = tokenizer.Decode([token]);
            if (text.AsSpan().IndexOfAny(Watched) < 0)
            {
                continue;
            }

            for (int s = 0; s < States; s++)
            {
                lowest.Clear();
                depth.Clear();
                profileEnd[s] = (byte)Walk(text, (LexState)s, depth, lowest);
                for (int k = 0; k < Kinds; k++)
                {
                    profileLowest[s * Kinds + k] = (sbyte)Math.Clamp(lowest[k], sbyte.MinValue, 0);
                    profileChange[s * Kinds + k] = (sbyte)Math.Clamp(depth[k], sbyte.MinValue, sbyte.MaxValue);
                }
            }

            int n = 0;
            for (int i = 0; i < profileLowest.Length; i++)
            {
                key[n++] = (char)(byte)profileLowest[i];
                key[n++] = (char)(byte)profileChange[i];
            }

            for (int s = 0; s < States; s++)
            {
                key[n++] = (char)profileEnd[s];
            }

            string name = new(key);
            if (!known.TryGetValue(name, out int profile))
            {
                profile = AddProfile(profileLowest, profileChange, profileEnd);
                known.Add(name, profile);
            }

            _profileOf[token] = profile;
        }

        Profiles = endOf.Count / States;
        _lowest = [.. lowestOf];
        _change = [.. changeOf];
        _end = [.. endOf];

        _closers = new int[States * Kinds][];
        for (int s = 0; s < States; s++)
        {
            for (int k = 0; k < Kinds; k++)
            {
                var list = new List<int>();
                for (int token = 0; token < vocabulary; token++)
                {
                    if (Lowest((LexState)s, token, k) < 0)
                    {
                        list.Add(token);
                    }
                }

                var state = (LexState)s;
                int kind = k;
                list.Sort((a, b) => Lowest(state, a, kind).CompareTo(Lowest(state, b, kind)));
                _closers[s * Kinds + k] = [.. list];
            }
        }

        int AddProfile(sbyte[] lowestValues, sbyte[] changeValues, byte[] ends)
        {
            lowestOf.AddRange(lowestValues);
            changeOf.AddRange(changeValues);
            endOf.AddRange(ends);
            return endOf.Count / States - 1;
        }
    }

    /// <summary>The distinct behaviours of the vocabulary's tokens (1 for a vocabulary without brackets, quotes or line breaks).</summary>
    public int Profiles { get; }

    /// <summary>The bytes the table holds (its per-token index, its profiles and its closer lists).</summary>
    public long Bytes => _profileOf.Length * 4L + _lowest.Length + _change.Length + _end.Length + _closers.Sum(c => c.Length * 4L);

    // The characters that matter: the brackets, the quotes, the backslash and the line break.
    private static readonly System.Buffers.SearchValues<char> Watched = System.Buffers.SearchValues.Create("()[]{}\"'\\\n");

    /// <summary>The vocabulary width it was made for (the logits' width).</summary>
    public int Vocabulary { get; }

    /// <summary>The table of <paramref name="tokenizer"/> for logits of <paramref name="vocabulary"/>, made on first need and kept while the tokenizer lives.</summary>
    public static BracketTable For(ITokenizer tokenizer, int vocabulary)
    {
        var byWidth = Tables.GetValue(tokenizer, static _ => []);
        lock (byWidth)
        {
            if (!byWidth.TryGetValue(vocabulary, out var table))
            {
                table = new BracketTable(tokenizer, vocabulary);
                byWidth.Add(vocabulary, table);
            }

            return table;
        }
    }

    /// <summary>The lowest depth of kind <paramref name="kind"/> <paramref name="token"/> reaches from <paramref name="state"/> (0 or below).</summary>
    public int Lowest(LexState state, int token, int kind) => _lowest[(_profileOf[token] * States + (int)state) * Kinds + kind];

    /// <summary>Its net change to the depth of kind <paramref name="kind"/>.</summary>
    public int Change(LexState state, int token, int kind) => _change[(_profileOf[token] * States + (int)state) * Kinds + kind];

    /// <summary>The state it leaves.</summary>
    public LexState End(LexState state, int token) => (LexState)_end[_profileOf[token] * States + (int)state];

    /// <summary>The tokens that go below zero on kind <paramref name="kind"/> from <paramref name="state"/>, the deepest first.</summary>
    public ReadOnlySpan<int> Closers(LexState state, int kind) => _closers[(int)state * Kinds + kind];

    /// <summary>Moves <paramref name="state"/> and <paramref name="depth"/> over <paramref name="token"/> (an id past the table changes nothing).</summary>
    public void Advance(ref LexState state, Span<int> depth, int token)
    {
        if ((uint)token >= (uint)Vocabulary)
        {
            return;
        }

        for (int k = 0; k < Kinds; k++)
        {
            depth[k] = Math.Max(0, depth[k] + Change(state, token, k));
        }

        state = End(state, token);
    }

    /// <summary>
    /// Walks <paramref name="text"/> from <paramref name="state"/>: the depth of each kind changes at its brackets in code
    /// (<paramref name="depth"/>, from zero), its lowest point kept (<paramref name="lowest"/>); returns the state at its end.
    /// </summary>
    public static LexState Walk(ReadOnlySpan<char> text, LexState state, Span<int> depth, Span<int> lowest)
    {
        foreach (char c in text)
        {
            switch (state)
            {
                case LexState.Code:
                    int kind = c switch { '(' or ')' => 0, '[' or ']' => 1, '{' or '}' => 2, _ => -1 };
                    if (kind >= 0)
                    {
                        depth[kind] += c is '(' or '[' or '{' ? 1 : -1;
                        lowest[kind] = Math.Min(lowest[kind], depth[kind]);
                    }
                    else if (c == '"')
                    {
                        state = LexState.String;
                    }
                    else if (c == '\'')
                    {
                        state = LexState.Character;
                    }

                    break;
                case LexState.String:
                    state = c switch { '\\' => LexState.StringEscape, '"' or '\n' => LexState.Code, _ => LexState.String };
                    break;
                case LexState.Character:
                    state = c switch { '\\' => LexState.CharacterEscape, '\'' or '\n' => LexState.Code, _ => LexState.Character };
                    break;
                case LexState.StringEscape:
                    state = c == '\n' ? LexState.Code : LexState.String;
                    break;
                default:
                    state = c == '\n' ? LexState.Code : LexState.Character;
                    break;
            }
        }

        return state;
    }
}
