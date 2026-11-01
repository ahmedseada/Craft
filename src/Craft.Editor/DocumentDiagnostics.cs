// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The diagnostics plug-ins published for one open document (W, contract 6), shared by every editor of it: each
/// source's list (a linter's, through <see cref="IEditorHost.PublishDiagnostics"/>) in the current text's positions, moved
/// through every edit (one an edit touches is dropped, as the language's are), joined into one list sorted by position.
/// A list published for an older text is moved through the edits made since (kept for the last
/// <see cref="HistoryLimit"/> changes; older, it is placed by line and column in the current text). On the UI thread.
/// </summary>
public sealed class DocumentDiagnostics
{
    /// <summary>The changes kept to move a list published for an older text.</summary>
    public const int HistoryLimit = 512;

    private static readonly ConditionalWeakTable<ITextDocument, DocumentDiagnostics> Table = new();
    private readonly Dictionary<string, EditorDiagnostic[]> _bySource = new(StringComparer.Ordinal);
    private readonly List<(int Version, IReadOnlyList<TextEdit> Edits)> _history = [];
    private readonly ITextBuffer _buffer;
    private EditorDiagnostic[] _all = [];

    private DocumentDiagnostics(ITextBuffer buffer)
    {
        _buffer = buffer;
        TextVersion = buffer.Current.Version;
        buffer.Changed += OnChanged;
    }

    /// <summary>The published diagnostics of <paramref name="document"/> (made on first use, kept while the document lives).</summary>
    public static DocumentDiagnostics Of(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Table.GetValue(document, static d => new DocumentDiagnostics(d.Buffer));
    }

    /// <summary>Every source's diagnostics, sorted by position, in the positions of the text of <see cref="TextVersion"/>.</summary>
    public ReadOnlySpan<EditorDiagnostic> All => _all;

    /// <summary>The version of the text <see cref="All"/>'s positions are in.</summary>
    public int TextVersion { get; private set; }

    /// <summary>The sources with diagnostics, in no order.</summary>
    public IReadOnlyCollection<string> Sources => _bySource.Keys;

    /// <summary>Changes whenever the diagnostics change.</summary>
    public int Version { get; private set; }

    /// <summary>Raised after the diagnostics changed (a source published, or an edit moved them).</summary>
    public event EventHandler? Changed;

    /// <summary>The diagnostics <paramref name="source"/> published, in the current positions (none when it has none).</summary>
    public ReadOnlySpan<EditorDiagnostic> Of(string source) => _bySource.TryGetValue(source, out var list) ? list : [];

    /// <summary>
    /// Replaces <paramref name="source"/>'s diagnostics with <paramref name="items"/>, which are in the positions of
    /// <paramref name="of"/> (a text of this document; null: the current text); none takes them away. True when they could
    /// be moved to the current text through the kept edits (or were of it); false when they were placed by line and
    /// column instead (<paramref name="placeAgain"/>) or dropped (none given).
    /// </summary>
    public bool Set(string source, EditorDiagnostic[] items, ITextSnapshot? of = null, Func<ITextSnapshot, EditorDiagnostic[]>? placeAgain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(items);
        var current = _buffer.Current;
        bool moved = true;
        if (items.Length > 0 && of is not null && of.Version != current.Version)
        {
            var mapped = (EditorDiagnostic[]?)items;
            if (!ReferenceEquals(of.Buffer, _buffer) || of.Version > current.Version)
            {
                mapped = null;
            }
            else
            {
                int first = _history.FindIndex(h => h.Version == of.Version);
                if (first < 0)
                {
                    mapped = null;
                }
                else
                {
                    for (int i = first; i < _history.Count; i++)
                    {
                        mapped = EditorModel.MapThrough(mapped, _history[i].Edits);
                    }
                }
            }

            if (mapped is null)
            {
                moved = false;
                mapped = placeAgain?.Invoke(current) ?? [];
            }

            items = mapped;
        }

        items = EditorModel.ClampTo(items, current.Length);
        if (items.Length == 0)
        {
            if (!_bySource.Remove(source))
            {
                return moved;
            }
        }
        else
        {
            var sorted = (EditorDiagnostic[])items.Clone();
            Array.Sort(sorted, static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length.CompareTo(b.Length));
            _bySource[source] = sorted;
        }

        TextVersion = current.Version;
        Join();
        return moved;
    }

    /// <summary>Takes every source's diagnostics away.</summary>
    public void Clear()
    {
        if (_bySource.Count == 0)
        {
            return;
        }

        _bySource.Clear();
        Join();
    }

    // The buffer changed: the edits are kept (for a list still being computed for the text before), each source's list moved.
    private void OnChanged(object? sender, TextChangedEventArgs e)
    {
        _history.Add((e.Before.Version, e.Edits));
        if (_history.Count > HistoryLimit)
        {
            _history.RemoveRange(0, _history.Count - HistoryLimit);
        }

        TextVersion = e.After.Version;
        if (_bySource.Count == 0)
        {
            return;
        }

        foreach (var source in _bySource.Keys.ToArray())
        {
            _bySource[source] = EditorModel.MapThrough(_bySource[source], e.Edits);
        }

        Join();
    }

    private void Join()
    {
        int count = 0;
        foreach (var list in _bySource.Values)
        {
            count += list.Length;
        }

        var joined = new EditorDiagnostic[count];
        int at = 0;
        foreach (var list in _bySource.Values)
        {
            list.CopyTo(joined, at);
            at += list.Length;
        }

        Array.Sort(joined, static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length.CompareTo(b.Length));
        _all = joined;
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A closed file's published diagnostics for Problems (W, contract 6): its path, by line and column, every source's.</summary>
/// <param name="Path">The file (a full path).</param>
/// <param name="Diagnostics">Every source's diagnostics, each id read "source(id)".</param>
public sealed record ClosedProblemFile(string Path, IReadOnlyList<FileDiagnostic> Diagnostics);
