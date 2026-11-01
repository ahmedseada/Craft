// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

public sealed partial class EditorHost
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // W, contract 6: what each source published for each file that is not open, by line and column (ids read "source(id)").
    // An open document's lists live in its DocumentDiagnostics, moved through its edits; they come back here when it closes.
    private readonly Dictionary<string, Dictionary<string, FileDiagnostic[]>> _closedDiagnostics = new(PathComparer);

    /// <summary>Raised on the UI thread after a closed file's published diagnostics changed (Problems lists them).</summary>
    public event EventHandler? PublishedDiagnosticsChanged;

    /// <summary>The closed files with published diagnostics, in path order, each with every source's (W, contract 6).</summary>
    public IReadOnlyList<ClosedProblemFile> ClosedProblemFiles
    {
        get
        {
            var files = new List<ClosedProblemFile>(_closedDiagnostics.Count);
            foreach (var (path, sources) in _closedDiagnostics.OrderBy(p => p.Key, PathComparer))
            {
                var all = new List<FileDiagnostic>();
                foreach (var source in sources.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    all.AddRange(source.Value);
                }

                files.Add(new ClosedProblemFile(path, all));
            }

            return files;
        }
    }

    /// <inheritdoc />
    public bool PublishDiagnostics(string path, string source, ITextSnapshot? snapshot, IReadOnlyList<FileDiagnostic> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(items);
        string full = Path.GetFullPath(path);
        var named = new FileDiagnostic[items.Count];
        for (int i = 0; i < named.Length; i++)
        {
            named[i] = items[i] with { Id = Named(source, items[i].Id) };
        }

        if (OpenDocumentOf(full) is { } open)
        {
            var document = open.Document;
            var current = document.Buffer.Current;
            var of = snapshot is not null && ReferenceEquals(snapshot.Buffer, document.Buffer) && snapshot.Version <= current.Version ? snapshot : current;
            DocumentDiagnostics.Of(document).Set(source, Place(named, of, source), of, now => Place(named, now, source));
            return true;
        }

        bool changed;
        if (named.Length == 0)
        {
            changed = _closedDiagnostics.TryGetValue(full, out var sources) && sources.Remove(source);
            if (changed && sources!.Count == 0)
            {
                _closedDiagnostics.Remove(full);
            }
        }
        else
        {
            if (!_closedDiagnostics.TryGetValue(full, out var sources))
            {
                sources = new Dictionary<string, FileDiagnostic[]>(StringComparer.Ordinal);
                _closedDiagnostics[full] = sources;
            }

            changed = !(sources.TryGetValue(source, out var old) && old.AsSpan().SequenceEqual(named));
            sources[source] = named;
        }

        if (changed)
        {
            PublishedDiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <inheritdoc />
    public void ClearDiagnostics(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        foreach (var open in _documents)
        {
            var published = DocumentDiagnostics.Of(open.Document);
            if (published.Of(source).Length > 0)
            {
                published.Set(source, []);
            }
        }

        bool changed = false;
        foreach (var path in _closedDiagnostics.Keys.ToArray())
        {
            var sources = _closedDiagnostics[path];
            if (sources.Remove(source))
            {
                changed = true;
                if (sources.Count == 0)
                {
                    _closedDiagnostics.Remove(path);
                }
            }
        }

        if (changed)
        {
            PublishedDiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"eslint(no-unused-vars)", or the source alone for an item without an id.</summary>
    public static string Named(string source, string id)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(id))
        {
            return source;
        }

        return id.Length > source.Length && id.StartsWith(source, StringComparison.Ordinal) && id[source.Length] == '(' ? id : $"{source}({id})";
    }

    private static EditorDiagnostic[] Place(FileDiagnostic[] items, ITextSnapshot snapshot, string source)
    {
        var placed = new EditorDiagnostic[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            placed[i] = items[i].In(snapshot, source);
        }

        return placed;
    }

    private OpenDocument? OpenDocumentOf(string fullPath)
    {
        foreach (var open in _documents)
        {
            if (open.FilePath is { } file && PathComparer.Equals(Path.GetFullPath(file), fullPath))
            {
                return open;
            }
        }

        return null;
    }

    // A document opened: what was published for its file while it was closed goes onto its text.
    private void ApplyClosedDiagnostics(OpenDocument open)
    {
        if (open.FilePath is not { } file || !_closedDiagnostics.Remove(Path.GetFullPath(file), out var sources))
        {
            return;
        }

        var published = DocumentDiagnostics.Of(open.Document);
        var current = open.Document.Buffer.Current;
        foreach (var (source, items) in sources)
        {
            published.Set(source, Place(items, current, source));
        }

        PublishedDiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    // A document closes: its published diagnostics go back to its file, by line and column in its last text.
    private void KeepClosedDiagnostics(OpenDocument open)
    {
        var published = DocumentDiagnostics.Of(open.Document);
        if (published.Sources.Count == 0)
        {
            return;
        }

        if (open.FilePath is { } file && OpenDocumentOf(Path.GetFullPath(file)) is null)
        {
            var snapshot = open.Document.Buffer.Current;
            var sources = new Dictionary<string, FileDiagnostic[]>(StringComparer.Ordinal);
            foreach (var source in published.Sources)
            {
                var items = published.Of(source);
                var lines = new FileDiagnostic[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    lines[i] = FileDiagnostic.Of(snapshot, items[i]);
                }

                sources[source] = lines;
            }

            _closedDiagnostics[Path.GetFullPath(file)] = sources;
            PublishedDiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }

        published.Clear();
    }
}
