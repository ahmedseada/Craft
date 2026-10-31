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
/// Made against an older version (<see cref="Since"/>), it answers with one range covering every edit between the two
/// (several versions' edits composed, <see cref="Compose"/>): an over-approximation Roslyn re-parses correctly. It keeps
/// no reference to the previous text (only its version), so versions do not chain in memory.
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

    /// <summary>
    /// The text of <paramref name="after"/>, knowing that it differs from version <paramref name="previousVersion"/> of
    /// the same buffer only inside <paramref name="change"/> (in the older version's positions: its start, the old length,
    /// the new length), so a tree of that older version re-parses incrementally to it in one step.
    /// </summary>
    public static SnapshotSourceText Since(ITextSnapshot after, int previousVersion, TextChangeRange change)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (previousVersion < 0 || previousVersion >= after.Version)
        {
            throw new ArgumentOutOfRangeException(nameof(previousVersion), previousVersion, $"The previous version must be older than the snapshot's {after.Version}.");
        }

        if (change.Span.Start < 0 || change.NewLength < 0 || change.Span.Start + change.NewLength > after.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(change), change, $"The change runs outside the text of {after.Length}.");
        }

        return new SnapshotSourceText(after, previousVersion, [change]);
    }

    /// <summary>One range covering a change's edits (in its <see cref="TextChangedEventArgs.Before"/> positions), or null when it has none.</summary>
    public static TextChangeRange? Collapse(IReadOnlyList<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
        {
            return null;
        }

        int start = edits[0].Start;
        int end = edits[^1].End;
        int delta = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            delta += edits[i].Delta;
        }

        return new TextChangeRange(new TextSpan(start, end - start), end - start + delta);
    }

    /// <summary>
    /// The one range covering <paramref name="first"/> (text A to B, in A's positions) then <paramref name="second"/>
    /// (B to C, in B's positions), in A's positions: from the earlier start to the later end, so every character that
    /// either changed is inside it. Either may be null (no change); both null gives null.
    /// </summary>
    public static TextChangeRange? Compose(TextChangeRange? first, TextChangeRange? second)
    {
        if (first is not { } a)
        {
            return second;
        }

        if (second is not { } b)
        {
            return a;
        }

        int deltaA = a.NewLength - a.Span.Length;
        int deltaB = b.NewLength - b.Span.Length;
        int start = Math.Min(a.Span.Start, b.Span.Start);
        int endInB = Math.Max(a.Span.Start + a.NewLength, b.Span.End);
        int oldEnd = endInB - deltaA;
        int newEnd = endInB + deltaB;
        return new TextChangeRange(new TextSpan(start, oldEnd - start), newEnd - start);
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
