// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// X part 9 on the session's workspace: inlay hints of a range (<see cref="CSharpInlayHints"/>), the call and type
/// hierarchies (<see cref="CSharpHierarchy"/>) and the reference count a code lens shows. Each runs on the thread pool,
/// the document brought to the snapshot asked for first (an older snapshot gets a solution of its own for the answer).
/// </summary>
public sealed partial class CSharpLanguageSession
{
    /// <inheritdoc />
    public Task<IReadOnlyList<InlayHint>> InlayHintsAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken)
    {
        Validate(snapshot, start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        length = Math.Min(length, snapshot.Length - start);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpInlayHints.ComputeAsync(document, new TextSpan(start, length), cancellationToken).ConfigureAwait(false)
            : (IReadOnlyList<InlayHint>)[], cancellationToken);
    }

    /// <inheritdoc />
    public Task<HierarchyItem?> PrepareHierarchyAsync(ITextSnapshot snapshot, int position, HierarchyKind kind, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpHierarchy.PrepareAsync(document, position, kind, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HierarchyItem>> HierarchyAsync(HierarchyItem item, HierarchyDirection direction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(async () => await DocumentForAsync(Volatile.Read(ref _latest), cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpHierarchy.WalkAsync(document.Project.Solution, item, direction, cancellationToken).ConfigureAwait(false)
            : (IReadOnlyList<HierarchyItem>)[], cancellationToken);
    }

    /// <summary>How many places refer to the symbol at <paramref name="position"/> (its declarations left out), or null when there is none: a code lens's count.</summary>
    internal Task<int?> CountReferencesAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        Validate(snapshot, position);
        return Task.Run(async () => await DocumentForAsync(snapshot, cancellationToken).ConfigureAwait(false) is { } document
            ? await CSharpHierarchy.CountReferencesAsync(document, position, cancellationToken).ConfigureAwait(false)
            : null, cancellationToken);
    }
}
