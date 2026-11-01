// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The document's symbols for this view (the X restart's piece 3b): asked of the language once per version, off the UI
/// thread, and flattened once (<see cref="SymbolIndex"/>); quick open's '@', the breadcrumb and the chat's '@' read it.
/// </summary>
public sealed partial class EditorModel
{
    private SymbolIndex? _symbolIndex;
    private Task<SymbolIndex?>? _symbolTask;
    private int _symbolTaskVersion = -1;

    /// <summary>The last symbols that landed (possibly of an older version than <see cref="Snapshot"/>), or null before the first.</summary>
    public SymbolIndex? Symbols => Volatile.Read(ref _symbolIndex);

    /// <summary>
    /// The symbols of the current snapshot: the kept index when it is of this version, else one request to the language
    /// for this version (shared by every caller of the version), flattened on the thread pool. Null when the document has
    /// no language. The token only stops this caller's wait. Called on the UI thread.
    /// </summary>
    public Task<SymbolIndex?> DocumentSymbolsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot;
        if (Volatile.Read(ref _symbolIndex) is { } kept && kept.Version == snapshot.Version)
        {
            return Task.FromResult<SymbolIndex?>(kept);
        }

        if (_session is not { } session || _disposed)
        {
            return Task.FromResult<SymbolIndex?>(null);
        }

        if (_symbolTask is not { } task || _symbolTaskVersion != snapshot.Version || task.IsFaulted || task.IsCanceled)
        {
            _symbolTaskVersion = snapshot.Version;
            task = _symbolTask = BuildSymbolsAsync(session, snapshot);
        }

        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    /// <summary>
    /// Asks for the current version's symbols and runs <paramref name="landed"/> on the UI thread once they are in
    /// <see cref="Symbols"/> (at once when they are already); a picker that showed the older ones lists them again.
    /// </summary>
    public void RequestSymbols(Action landed)
    {
        ArgumentNullException.ThrowIfNull(landed);
        var request = DocumentSymbolsAsync();
        if (request.IsCompletedSuccessfully)
        {
            return;                                              // already in Symbols: what the caller read is current
        }

        Track(request, _ => landed());
    }

    private async Task<SymbolIndex?> BuildSymbolsAsync(ILanguageSession session, ITextSnapshot snapshot)
    {
        var symbols = await session.GetDocumentSymbolsAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
        var index = await Task.Run(() => SymbolIndex.Build(symbols, snapshot)).ConfigureAwait(false);
        // Kept only when no newer version's landed first (two requests may finish out of order).
        var current = Volatile.Read(ref _symbolIndex);
        while (current is null || current.Version < index.Version)
        {
            var seen = Interlocked.CompareExchange(ref _symbolIndex, index, current);
            if (ReferenceEquals(seen, current))
            {
                break;
            }

            current = seen;
        }

        return index;
    }
}
