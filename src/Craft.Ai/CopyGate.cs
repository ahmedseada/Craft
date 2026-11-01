// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// One loaded copy's gate (B7): generations pass it side by side, as before; an exclusive hold (a training round on the
/// copy, <see cref="Craft.Abstractions.ModelLease.Exclusive"/>) waits for the running generations to end, and the
/// generations asked for while it is held or waiting wait until it is given back.
/// </summary>
internal sealed class CopyGate
{
    private readonly SemaphoreSlim _door = new(1, 1);
    private readonly Lock _gate = new();
    private int _running;
    private TaskCompletionSource? _drained;

    /// <summary>Passes a generation: it waits only while an exclusive hold is held or waiting.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _door.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _running++;
        }

        _door.Release();
        return new Release(this, exclusive: false);
    }

    /// <summary>
    /// Passes a generation only when nothing holds the copy alone or waits to: null otherwise, at once (never waits). For a
    /// draft (speculative decoding), which a reply does without rather than wait for.
    /// </summary>
    public IDisposable? TryEnter()
    {
        if (!_door.Wait(0))
        {
            return null;
        }

        lock (_gate)
        {
            _running++;
        }

        _door.Release();
        return new Release(this, exclusive: false);
    }

    /// <summary>Takes the copy alone: once the running generations have ended; the next ones wait until it is disposed.</summary>
    public async Task<IDisposable> ExclusiveAsync(CancellationToken cancellationToken)
    {
        await _door.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task drained;
        lock (_gate)
        {
            _drained = _running == 0 ? null : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _drained?.Task ?? Task.CompletedTask;
        }

        try
        {
            await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _door.Release();
            throw;
        }

        return new Release(this, exclusive: true);
    }

    private void Left()
    {
        lock (_gate)
        {
            if (--_running == 0 && _drained is { } drained)
            {
                _drained = null;
                drained.TrySetResult();
            }
        }
    }

    private sealed class Release(CopyGate gate, bool exclusive) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            if (exclusive)
            {
                gate._door.Release();
            }
            else
            {
                gate.Left();
            }
        }
    }
}

/// <summary>A copy's chat model whose every reply passes the copy's gate.</summary>
internal sealed class GatedChatModel(IChatModel inner, CopyGate gate) : IChatModel
{
    /// <inheritdoc />
    public IReadOnlySet<string> PartKinds => inner.PartKinds;

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var pass = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in inner.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}

/// <summary>A copy's text model whose every generation passes the copy's gate.</summary>
internal sealed class GatedTextModel(ITextModel inner, CopyGate gate) : ITextModel
{
    /// <inheritdoc />
    public async IAsyncEnumerable<GenerationChunk> StreamAsync(string prompt, GenerationOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var pass = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in inner.StreamAsync(prompt, options, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}

/// <summary>
/// A copy as a draft for another model (speculative decoding, <see cref="Craft.Abstractions.ModelLease.Draft"/>): each
/// drafting session passes the copy's gate while it lasts, as a generation does; while the copy is held alone (a training
/// round), a session proposes nothing and the reply goes on without drafting, never waiting.
/// </summary>
internal sealed class GatedDraftModel(IDraftModel inner, CopyGate gate) : IDraftModel
{
    /// <inheritdoc />
    public ITokenizer Tokenizer => inner.Tokenizer;

    /// <inheritdoc />
    public int Vocabulary => inner.Vocabulary;

    /// <inheritdoc />
    public IDraftSession StartDraft(IReadOnlyList<int> prompt, GenerationOptions options)
    {
        if (gate.TryEnter() is not { } pass)
        {
            return new Idle();
        }

        try
        {
            return new Session(inner.StartDraft(prompt, options), pass);
        }
        catch
        {
            pass.Dispose();
            throw;
        }
    }

    private sealed class Session(IDraftSession inner, IDisposable pass) : IDraftSession
    {
        public int Propose(Span<int> ids, Span<float> probabilities, int width, CancellationToken cancellationToken) =>
            inner.Propose(ids, probabilities, width, cancellationToken);

        public void Accept(int accepted, int next) => inner.Accept(accepted, next);

        public void Dispose()
        {
            try
            {
                inner.Dispose();
            }
            finally
            {
                pass.Dispose();
            }
        }
    }

    // A session that proposes nothing: the target generates alone.
    private sealed class Idle : IDraftSession
    {
        public int Propose(Span<int> ids, Span<float> probabilities, int width, CancellationToken cancellationToken) => 0;

        public void Accept(int accepted, int next)
        {
        }

        public void Dispose()
        {
        }
    }
}
