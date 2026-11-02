// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Core.Diagnostics;

/// <summary>
/// The UI thread's synchronization context, wrapped so that nothing posted to it ends the app (the owner, 2026-11-02:
/// "a global exception handler, the crash kept apart from the UI thread, the error reported and the UI still running").
/// Every callback posted through it (an <c>await</c>'s continuation on the UI thread, an <c>async void</c> handler's
/// exception, which .NET posts back to the context it started on) runs under a catch: what it throws goes to
/// <see cref="CallbackGuard.Report"/> (written as a report off the UI thread, shown in Craft's dialog) and the thread goes
/// on. Without it such an exception leaves the platform's dispatcher callback, and WinUI ends the process with a native
/// fail-fast (0xc000027b) that no managed handler sees. <see cref="Send"/> is passed through as is: its caller waits for
/// it and gets its exception. Installed once on the UI thread (<see cref="Install"/>); any platform, no platform API.
/// </summary>
public sealed class GuardedSynchronizationContext : SynchronizationContext
{
    private const string DefaultWhere = "the UI thread: an awaited continuation or an async handler";
    private readonly SynchronizationContext _inner;
    private readonly string _where;

    /// <summary>A context posting through <paramref name="inner"/>, each callback guarded and its failure reported as <paramref name="where"/>.</summary>
    public GuardedSynchronizationContext(SynchronizationContext inner, string where)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(where);
        _where = where;
    }

    /// <summary>The platform's context it posts through.</summary>
    public SynchronizationContext Inner => _inner;

    /// <summary>
    /// Wraps the calling thread's context (the UI thread's, set by the platform before the app starts), once: the guarded
    /// context set, or the one already set; null when the thread has no context to wrap.
    /// </summary>
    public static GuardedSynchronizationContext? Install()
    {
        switch (Current)
        {
            case null:
                return null;
            case GuardedSynchronizationContext guarded:
                return guarded;
            case var current:
                var wrapped = new GuardedSynchronizationContext(current, DefaultWhere);
                SetSynchronizationContext(wrapped);
                return wrapped;
        }
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _inner.Post(static posted => ((Posted)posted!).Run(), new Posted(d, state, _where));
    }

    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state) => _inner.Send(d, state);

    /// <inheritdoc />
    public override SynchronizationContext CreateCopy() => new GuardedSynchronizationContext(_inner.CreateCopy(), _where);

    /// <inheritdoc />
    public override void OperationStarted() => _inner.OperationStarted();

    /// <inheritdoc />
    public override void OperationCompleted() => _inner.OperationCompleted();

    // A posted callback and its state: run under the catch, on the context's thread.
    private sealed class Posted(SendOrPostCallback callback, object? state, string where)
    {
        public void Run()
        {
            try
            {
                callback(state);
            }
#pragma warning disable CA1031 // Nothing may leave the dispatcher's callback: reported, and the UI thread goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                CallbackGuard.Report(where, error);
            }
        }
    }
}
