// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>A gutter mark was clicked in an editor: the editor, the mark's source and the mark.</summary>
public sealed class LineMarkClickEventArgs(EditorModel editor, string source, LineMark mark) : EventArgs
{
    /// <summary>The editor clicked.</summary>
    public EditorModel Editor { get; } = editor;

    /// <summary>Who set the mark.</summary>
    public string Source { get; } = source;

    /// <summary>The mark.</summary>
    public LineMark Mark { get; } = mark;
}

/// <summary>
/// The line marks of one document (X part 8), shared by every editor of it: each source's marks (a plug-in's, through
/// <see cref="IEditorHost.SetLineMarks"/>) joined into one list sorted by line, a version that changes with them (the
/// renderer and the overview ruler key on it), the clicks on them, and the inline card a source asked to show. On the UI
/// thread.
/// </summary>
public sealed class DocumentLineMarks
{
    private static readonly ConditionalWeakTable<ITextDocument, DocumentLineMarks> Table = new();
    private readonly Dictionary<string, LineMark[]> _bySource = new(StringComparer.Ordinal);
    private LineMark[] _marks = [];
    private string[] _sources = [];
    private WeakReference<EditorModel>? _lastClicked;

    private DocumentLineMarks()
    {
    }

    /// <summary>The marks of <paramref name="document"/> (made on first use, kept while the document lives).</summary>
    public static DocumentLineMarks Of(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Table.GetValue(document, static _ => new DocumentLineMarks());
    }

    /// <summary>Every source's marks, sorted by line.</summary>
    public ReadOnlySpan<LineMark> Marks => _marks;

    /// <summary>Changes whenever the marks change.</summary>
    public int Version { get; private set; }

    /// <summary>The card a source asked to show last (<see cref="ShowCard"/>), or null.</summary>
    public InlineCard? Card { get; private set; }

    /// <summary>Raised after the marks changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a mark was clicked in an editor.</summary>
    public event EventHandler<LineMarkClickEventArgs>? Clicked;

    /// <summary>Raised when a source asked to show <see cref="Card"/>.</summary>
    public event EventHandler? CardRequested;

    /// <summary>The source of mark <paramref name="index"/> of <see cref="Marks"/>.</summary>
    public string SourceOf(int index) => _sources[index];

    /// <summary>Replaces <paramref name="source"/>'s marks with <paramref name="marks"/> (none takes them away).</summary>
    public void Set(string source, IReadOnlyList<LineMark> marks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(marks);
        if (marks.Count == 0)
        {
            if (!_bySource.Remove(source))
            {
                return;
            }
        }
        else
        {
            if (_bySource.TryGetValue(source, out var old) && Same(old, marks))
            {
                return;
            }

            _bySource[source] = [.. marks];
        }

        int count = 0;
        foreach (var list in _bySource.Values)
        {
            count += list.Length;
        }

        var joined = new LineMark[count];
        var sources = new string[count];
        int at = 0;
        foreach (var (name, list) in _bySource)
        {
            list.CopyTo(joined, at);
            sources.AsSpan(at, list.Length).Fill(name);
            at += list.Length;
        }

        Array.Sort(joined, sources, MarkOrder.Instance);
        _marks = joined;
        _sources = sources;
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The index in <see cref="Marks"/> of the mark that covers <paramref name="line"/> (a deletion: the line it stands before), or -1.</summary>
    public int IndexAt(int line)
    {
        // The marks are sorted by their first line: a binary search for the last one starting at or before the line.
        var marks = _marks;
        int low = 0, high = marks.Length - 1, found = -1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            if (marks[middle].Line <= line)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        for (int i = found; i >= 0 && i > found - 5; i--)
        {
            if (marks[i].Line <= line && marks[i].LastLine >= line && marks[i].Kind != LineMarkKind.Bookmark)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Asks the editor the user last clicked a mark in (else every editor of the document, the focused one shows it) to show <paramref name="card"/>.</summary>
    public void ShowCard(InlineCard card)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        CardRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether <paramref name="editor"/> is the one a card goes to: the one last clicked, or any when none was.</summary>
    internal bool IsCardTarget(EditorModel editor) =>
        _lastClicked is null || !_lastClicked.TryGetTarget(out var last) || last.IsDisposed || ReferenceEquals(last, editor);

    internal void Click(EditorModel editor, int index)
    {
        _lastClicked = new WeakReference<EditorModel>(editor);
        Clicked?.Invoke(this, new LineMarkClickEventArgs(editor, _sources[index], _marks[index]));
    }

    private static bool Same(LineMark[] old, IReadOnlyList<LineMark> marks)
    {
        if (old.Length != marks.Count)
        {
            return false;
        }

        for (int i = 0; i < old.Length; i++)
        {
            if (old[i] != marks[i])
            {
                return false;
            }
        }

        return true;
    }

    private sealed class MarkOrder : IComparer<LineMark>
    {
        public static readonly MarkOrder Instance = new();

        public int Compare(LineMark x, LineMark y) => x.Line != y.Line ? x.Line.CompareTo(y.Line) : x.Kind.CompareTo(y.Kind);
    }
}
