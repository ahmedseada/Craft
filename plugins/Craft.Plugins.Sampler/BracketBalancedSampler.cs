// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Idrak.Abstraction.Generation;

namespace Craft.Plugins.Sampler;

/// <summary>When the sampler masks (the setting <c>mode</c>).</summary>
public enum BracketMode
{
    /// <summary>On fill-in-the-middle completions only (a prompt in a registered FIM layout): the prefix is the text before the caret.</summary>
    Fim,

    /// <summary>On every generation that has a tokenizer: the whole prompt counts as the prefix (a chat reply too).</summary>
    Always,

    /// <summary>Never: the built-in sampler, as it is.</summary>
    Off,
}

/// <summary>
/// The bracket-balanced sampler of one generation: the built-in sampler (<paramref name="inner"/>) chooses every token,
/// but first, on a row whose next token could close a bracket the text so far never opened, the logits of those tokens are
/// set to -∞ (a copy of the last position's row: the model's tensor is never changed). Where no token is forbidden the
/// logits go to the built-in as they are. The depths start from the prompt's prefix (<see cref="SetHistory"/>) and follow
/// each sampled token (read back after each step, so a masking generation is not recorded as a graph; one that masks
/// nothing, a prompt that is not a completion, is).
/// </summary>
internal sealed class BracketBalancedSampler(ITokenSampler inner, BracketTable table, int rows, BracketMode mode, ITokenizer tokenizer) : ITokenSampler
{
    private readonly LexState[] _states = new LexState[rows];
    private readonly int[] _depths = new int[rows * BracketTable.Kinds];
    private LexState _startState;
    private readonly int[] _startDepths = new int[BracketTable.Kinds];
    private bool _active;
    private Tensor? _work;
    private bool _disposed;

    /// <summary>Steps where some token was forbidden (the tests and the bench read it).</summary>
    public int MaskedSteps { get; private set; }

    /// <summary>Tokens forbidden over the generation, summed over rows and steps.</summary>
    public long MaskedTokens { get; private set; }

    /// <summary>Whether this generation is watched (a completion in a FIM layout, or any prompt in <see cref="BracketMode.Always"/>).</summary>
    public bool Active => _active;

    public int Rows => inner.Rows;

    public int Vocabulary => inner.Vocabulary;

    public Tensor Ids => inner.Ids;

    public bool Recordable => !_active && inner.Recordable;

    public void SetHistory(IReadOnlyList<int> tokens)
    {
        inner.SetHistory(tokens);
        _startDepths.AsSpan().Clear();
        _startState = LexState.Code;
        _active = false;
        if (mode == BracketMode.Off)
        {
            return;
        }

        var (from, to) = FillInMiddlePrefix.Find(tokenizer, tokens);
        if (from < 0)
        {
            if (mode != BracketMode.Always)
            {
                return;                                     // not a completion: the built-in, as it is
            }

            (from, to) = (0, tokens.Count);
        }

        _active = true;
        for (int i = from; i < to; i++)
        {
            table.Advance(ref _startState, _startDepths, tokens[i]);
        }

        Restart();
    }

    public void Reset()
    {
        inner.Reset();
        Restart();
    }

    public void Sample(Tensor logits)
    {
        if (!_active)
        {
            inner.Sample(logits);
            return;
        }

        int vocabulary = logits.Shape[^1];
        int steps = logits.Rank == 3 ? logits.Shape[1] : 1;
        if (!AnyForbidden())
        {
            inner.Sample(logits);
        }
        else
        {
            // The last position's row of each sampled row, with the forbidden tokens at -∞ (a pooled buffer, returned in finally).
            int size = rows * vocabulary;
            float[] values = ArrayPool<float>.Shared.Rent(size);
            try
            {
                var buffer = values.AsSpan(0, size);
                for (int r = 0; r < rows; r++)
                {
                    var row = buffer.Slice(r * vocabulary, vocabulary);
                    logits.CopyTo(row, (r * steps + steps - 1) * vocabulary);
                    MaskedTokens += Forbid(r, row);
                }

                _work ??= Tensor.Empty([rows, vocabulary], logits.Device, track: false);   // the sampler's own, outside any step's scope
                _work.Load(buffer);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(values);
            }

            MaskedSteps++;
            inner.Sample(_work);
        }

        Follow();
    }

    public SampledToken[][] Read(int fromStep, int toStep) => inner.Read(fromStep, toStep);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _work?.Dispose();
        inner.Dispose();
    }

    private void Restart()
    {
        Array.Fill(_states, _startState);
        for (int r = 0; r < rows; r++)
        {
            _startDepths.CopyTo(_depths, r * BracketTable.Kinds);
        }
    }

    // Whether some row has a token that would go below its depth: the deepest closer of each kind decides it.
    private bool AnyForbidden()
    {
        for (int r = 0; r < rows; r++)
        {
            for (int k = 0; k < BracketTable.Kinds; k++)
            {
                var closers = table.Closers(_states[r], k);
                if (closers.Length > 0 && _depths[r * BracketTable.Kinds + k] + table.Lowest(_states[r], closers[0], k) < 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Sets the forbidden tokens of row r to -∞; the count set (a token forbidden by two kinds counts twice).
    private int Forbid(int r, Span<float> row)
    {
        int count = 0;
        var state = _states[r];
        for (int k = 0; k < BracketTable.Kinds; k++)
        {
            int depth = _depths[r * BracketTable.Kinds + k];
            foreach (int token in table.Closers(state, k))
            {
                if (depth + table.Lowest(state, token, k) >= 0)
                {
                    break;                                  // the deepest first: the rest stay within the depth
                }

                if ((uint)token < (uint)row.Length)
                {
                    row[token] = float.NegativeInfinity;
                    count++;
                }
            }
        }

        return count;
    }

    // The tokens just sampled move each row's state and depths.
    private void Follow()
    {
        Span<float> ids = rows <= 64 ? stackalloc float[rows] : new float[rows];
        inner.Ids.CopyTo(ids);
        for (int r = 0; r < rows; r++)
        {
            table.Advance(ref _states[r], _depths.AsSpan(r * BracketTable.Kinds, BracketTable.Kinds), (int)ids[r]);
        }
    }
}

/// <summary>Where a fill-in-the-middle prompt's prefix is among its ids, by the layout of the format registered for its tokenizer.</summary>
internal static class FillInMiddlePrefix
{
    /// <summary>
    /// The prefix's ids, [from, to), of <paramref name="tokens"/> when they hold a prompt of the FIM layout registered for
    /// <paramref name="tokenizer"/> (<see cref="FillInMiddleFormats"/>: prefix-suffix-middle, between the prefix and the
    /// suffix tokens; suffix first, after the middle token, or after the prefix token when there is no middle token);
    /// (-1, -1) when they do not, or the format is not a layout the library knows.
    /// </summary>
    public static (int From, int To) Find(ITokenizer tokenizer, IReadOnlyList<int> tokens)
    {
        if (tokenizer is not IPretrainedTokenizer pretrained || FillInMiddleFormats.Detect(pretrained) is not { } name
            || FillInMiddleFormats.Get(name) is not FillInMiddleLayout layout
            || pretrained.IdOf(layout.Tokens.Prefix) is not int prefix || pretrained.IdOf(layout.Tokens.Suffix) is not int suffix)
        {
            return (-1, -1);
        }

        int? middle = layout.Tokens.Middle is { } m ? pretrained.IdOf(m) : null;
        int Last(int id, int before)
        {
            for (int i = before - 1; i >= 0; i--)
            {
                if (tokens[i] == id)
                {
                    return i;
                }
            }

            return -1;
        }

        int Next(int id, int from)
        {
            for (int i = from; i < tokens.Count; i++)
            {
                if (tokens[i] == id)
                {
                    return i;
                }
            }

            return -1;
        }

        if (layout.Order == FillInMiddleOrder.PrefixSuffixMiddle)
        {
            int at = Last(prefix, tokens.Count);
            int end = at < 0 ? -1 : Next(suffix, at + 1);
            return at < 0 || end < 0 ? (-1, -1) : (at + 1, end);
        }

        if (middle is int mid)
        {
            int at = Last(mid, tokens.Count);
            return at < 0 || Last(suffix, at) < 0 ? (-1, -1) : (at + 1, tokens.Count);
        }

        int start = Last(prefix, tokens.Count);
        return start < 0 || Last(suffix, start) < 0 ? (-1, -1) : (start + 1, tokens.Count);
    }
}
