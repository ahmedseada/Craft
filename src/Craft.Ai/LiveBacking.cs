// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json.Nodes;
using Craft.Abstractions;

namespace Craft.Ai;

/// <summary>
/// The live backing of the editor's tools: the host's documents are read on its UI thread (a snapshot is immutable, so
/// the lines are then read off it), and an edit is only ever proposed (<see cref="IEditorHost.ProposeAsync"/>): the user
/// accepts it in the diff view.
/// </summary>
internal sealed class LiveBacking(IEditorHost host, AiSettings settings)
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>An open document as the tools see it, taken on the UI thread.</summary>
    private sealed record DocumentView(IEditorDocument Document, string Name, string? Path, ITextSnapshot Snapshot, bool Active, bool Dirty, string Language,
        SelectionState Selection, DiagnosticSet Diagnostics);

    private sealed record State(string? Root, DocumentView[] Documents);

    public async Task<string> OpenDocumentsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        if (state.Documents.Length == 0)
        {
            return "No document is open.";
        }

        var list = new JsonArray();
        foreach (var document in state.Documents)
        {
            list.Add((JsonNode)new JsonObject
            {
                ["path"] = document.Name,
                ["language"] = document.Language,
                ["lines"] = document.Snapshot.LineCount,
                ["unsaved"] = document.Dirty,
                ["active"] = document.Active,
            });
        }

        return new JsonObject { ["folder"] = state.Root, ["documents"] = list }.ToJsonString();
    }

    public async Task<string> ReadAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string path = EditorTools.Text(arguments, "path");
        int start = EditorTools.Whole(arguments, "start_line") ?? 1;
        int count = Math.Clamp(EditorTools.Whole(arguments, "line_count") ?? settings.ReadLines, 1, settings.ReadLines);
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        if (Find(state, path) is { } open)
        {
            return ToolText.ReadLines(open.Name, open.Snapshot, start, count);
        }

        if (state.Root is { } root && ToolText.Inside(root, path) is { } full && File.Exists(full))
        {
            var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
            return ToolText.ReadLines(ToolText.Display(root, full), text, start, count);
        }

        return NotFound(state, path);
    }

    public async Task<string> SelectionAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        if (Array.Find(state.Documents, d => d.Active) is not { } active)
        {
            return "No document is active.";
        }

        var snapshot = active.Snapshot;
        var selection = active.Selection;
        int start = Math.Clamp(selection.Start, 0, snapshot.Length);
        int end = Math.Clamp(selection.End, start, snapshot.Length);
        var (startLine, startColumn) = Position(snapshot, start);
        var (endLine, endColumn) = Position(snapshot, end);
        return new JsonObject
        {
            ["path"] = active.Name,
            ["start_line"] = startLine,
            ["start_column"] = startColumn,
            ["end_line"] = endLine,
            ["end_column"] = endColumn,
            ["text"] = snapshot.GetText(start, end - start),
        }.ToJsonString();
    }

    public async Task<string> DiagnosticsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        var path = EditorTools.OptionalText(arguments, "path");
        var document = path is null ? Array.Find(state.Documents, d => d.Active) : Find(state, path);
        if (document is null)
        {
            return path is null ? "No document is active." : NotFound(state, path);
        }

        var set = document.Diagnostics;
        if (set.Count == 0)
        {
            return document.Document.Language is null
                ? $"{document.Name}: plain text, no language reports diagnostics for it."
                : $"{document.Name}: no diagnostics (none found, or its language has no semantic layer).";
        }

        // Positions are of the snapshot they were computed for (the editor maps them to its current text).
        var snapshot = set.Snapshot ?? document.Snapshot;
        var lines = new List<string>(set.Count + 1) { string.Create(Culture, $"{document.Name}: {set.Errors} errors, {set.Warnings} warnings, {set.Infos} infos") };
        foreach (var item in set.Items)
        {
            if (item.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            var (line, column) = Position(snapshot, Math.Clamp(item.Start, 0, snapshot.Length));
            lines.Add(string.Create(Culture, $"{document.Name}:{line}:{column}: {item.Severity.ToString().ToLowerInvariant()} {item.Id}: {item.Message}"));
        }

        return string.Join('\n', lines);
    }

    public async Task<string> SearchAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        return EditorTools.Search(arguments, settings, (query, comparison, utf8, hits, limit) =>
        {
            // The open documents first, as the editor has them (unsaved text included); then the folder's other files.
            var open = new HashSet<string>(StringComparer.Ordinal);
            foreach (var document in state.Documents)
            {
                ToolText.Search(document.Name, document.Snapshot, query, comparison, hits, limit);
                if (document.Path is { } p)
                {
                    open.Add(p);
                }
            }

            if (state.Root is { } root)
            {
                long maxBytes = settings.SearchFileKilobytes * 1024L;
                foreach (var file in ToolText.Files(root, settings.Ignore))
                {
                    if (hits.Count >= limit)
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!open.Contains(file))
                    {
                        ToolText.SearchFile(ToolText.Display(root, file), file, query, utf8, comparison, hits, limit, maxBytes);
                    }
                }
            }
        });
    }

    public async Task<string> ProposeAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        string path = EditorTools.Text(arguments, "path");
        string oldText = EditorTools.Text(arguments, "old_text");
        string newText = EditorTools.Text(arguments, "new_text");
        int? line = EditorTools.Whole(arguments, "line");
        string title = EditorTools.OptionalText(arguments, "title") is { Length: > 0 } given ? given : "Edit from a model";
        var state = await TakeAsync(cancellationToken).ConfigureAwait(false);
        if (Find(state, path) is not { } document)
        {
            return NotFound(state, path) + " Only an open document can be changed; ask the user to open it.";
        }

        var text = document.Snapshot.GetText(0, document.Snapshot.Length);
        if (ToolText.Find(text, oldText, newText, line, out var problem) is not { } edit)
        {
            return $"Not proposed: {problem}";
        }

        var proposal = new ProposedEdits([edit], title)
        {
            Placement = oldText.Length == 0 ? "an insertion at the line given" : "exact text match",
            Origin = "a model, through the editor's tools",
        };
        bool applied = await OnUiAsync(() => host.ProposeAsync(document.Document, proposal, cancellationToken), cancellationToken).Unwrap().ConfigureAwait(false);
        return applied
            ? $"Accepted by the user and applied to {document.Name} (one undo step)."
            : $"Not applied to {document.Name}: the user rejected it, or the document changed while they were asked.";
    }

    private Task<State> TakeAsync(CancellationToken cancellationToken) => OnUiAsync(() =>
    {
        var documents = host.Documents;
        var active = host.ActiveDocument;
        var root = host.WorkspaceFolder;
        var views = new DocumentView[documents.Count];
        int untitled = 0;
        for (int i = 0; i < views.Length; i++)
        {
            var document = documents[i];
            string? full = document.FilePath is { } p ? System.IO.Path.GetFullPath(p) : null;
            string name = full is null ? string.Create(Culture, $"untitled-{++untitled}") : ToolText.Display(root, full);
            views[i] = new DocumentView(document, name, full, document.Document.Buffer.Current, ReferenceEquals(document, active), document.Document.IsDirty,
                document.LanguageName, document.Selection, document.Diagnostics);
        }

        return new State(root, views);
    }, cancellationToken);

    private Task<T> OnUiAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Dispatch(() =>
        {
            try
            {
                done.TrySetResult(work());
            }
#pragma warning disable CA1031 // The tool's caller gets the failure (the registry reports it to the model).
            catch (Exception error)
#pragma warning restore CA1031
            {
                done.TrySetException(error);
            }
        });
        return done.Task.WaitAsync(cancellationToken);
    }

    private static DocumentView? Find(State state, string path)
    {
        foreach (var document in state.Documents)
        {
            if (string.Equals(document.Name, path, StringComparison.Ordinal))
            {
                return document;
            }
        }

        string? full = null;
        try
        {
            full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) || state.Root is null ? path : System.IO.Path.Combine(state.Root, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        DocumentView? ignoringCase = null;
        foreach (var document in state.Documents)
        {
            if (document.Path is null || full is null)
            {
                continue;
            }

            if (string.Equals(document.Path, full, StringComparison.Ordinal))
            {
                return document;
            }

            if (ignoringCase is null && string.Equals(document.Path, full, StringComparison.OrdinalIgnoreCase))
            {
                ignoringCase = document;
            }
        }

        return ignoringCase;
    }

    private static string NotFound(State state, string path) =>
        $"No open document or file in the folder is '{path}'. Open: {(state.Documents.Length == 0 ? "none" : string.Join(", ", state.Documents.Select(d => d.Name)))}.";

    private static (int Line, int Column) Position(ITextSnapshot snapshot, int position)
    {
        int line = snapshot.GetLineIndex(position);
        return (line + 1, position - snapshot.GetLine(line).Start + 1);
    }
}
