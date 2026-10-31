// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// Roslyn's <see cref="SourceText"/> over an <see cref="ITextSnapshot"/>: it reads the snapshot (no copy of the text),
/// and, made from a buffer change (<see cref="FromChange"/>), it answers <see cref="GetChangeRanges"/> against the
/// previous version's text with the change's exact edits, so <c>SyntaxTree.WithChangedText</c> re-parses incrementally.
/// It keeps no reference to the previous text (only its version), so versions do not chain in memory.
/// </summary>
public sealed class SnapshotSourceText : SourceText
{
    private readonly int _previousVersion;
    private readonly TextChangeRange[]? _changes;

    /// <summary>The text of <paramref name="snapshot"/>.</summary>
    public SnapshotSourceText(ITextSnapshot snapshot) : this(snapshot, -1, null)
    {
    }

    private SnapshotSourceText(ITextSnapshot snapshot, int previousVersion, TextChangeRange[]? changes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        _previousVersion = previousVersion;
        _changes = changes;
    }

    /// <summary>The text of a change's <see cref="TextChangedEventArgs.After"/>, knowing its edits from <see cref="TextChangedEventArgs.Before"/>.</summary>
    public static SnapshotSourceText FromChange(TextChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var edits = change.Edits;
        var ranges = new TextChangeRange[edits.Count];
        for (int i = 0; i < ranges.Length; i++)
        {
            var edit = edits[i];
            ranges[i] = new TextChangeRange(new TextSpan(edit.Start, edit.Length), edit.Text.Length);
        }

        return new SnapshotSourceText(change.After, change.Before.Version, ranges);
    }

    /// <summary>The snapshot it reads.</summary>
    public ITextSnapshot Snapshot { get; }

    /// <inheritdoc />
    public override Encoding? Encoding => null;

    /// <inheritdoc />
    public override int Length => Snapshot.Length;

    /// <inheritdoc />
    public override char this[int position] => Snapshot[position];

    /// <inheritdoc />
    public override void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count) =>
        Snapshot.CopyTo(sourceIndex, destination.AsSpan(destinationIndex, count));

    /// <inheritdoc />
    public override IReadOnlyList<TextChangeRange> GetChangeRanges(SourceText oldText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        if (ReferenceEquals(oldText, this))
        {
            return [];
        }

        if (_changes is not null && oldText is SnapshotSourceText old && ReferenceEquals(old.Snapshot.Buffer, Snapshot.Buffer) && old.Snapshot.Version == _previousVersion)
        {
            return _changes;
        }

        return base.GetChangeRanges(oldText);
    }
}
