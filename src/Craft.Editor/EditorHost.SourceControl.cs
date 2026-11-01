// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

// X part 8: what a source control plug-in reaches through the host: the workspace folder's changes, line marks in the
// gutter with their clicks, an inline card under a line, a published repository (the shell's branch and tree colours),
// and the read-only diff view.
public sealed partial class EditorHost
{
    private readonly HandlerList<EventHandler> _workspaceChanged = new();
    private readonly HandlerList<EventHandler<WorkspaceChangedEventArgs>> _workspaceChangedWith = new();
    private readonly HandlerList<EventHandler<LineMarkEventArgs>> _lineMarkClicked = new();
    private readonly List<ISourceControlRepository> _repositories = [];
    private string? _workspaceFolder;

    /// <inheritdoc />
    /// <remarks>Set by the shell when a folder opens or closes (the full path, or null); a change raises <see cref="WorkspaceFolderChanged"/>.</remarks>
    public string? WorkspaceFolder
    {
        get => _workspaceFolder;
        set
        {
            if (!string.Equals(_workspaceFolder, value, StringComparison.Ordinal))
            {
                var change = WorkspaceChangedEventArgs.Between(_workspaceFolder, value);
                _workspaceFolder = value;
                Raise(_workspaceChanged, nameof(WorkspaceFolderChanged));
                Raise(_workspaceChangedWith, change, nameof(WorkspaceChanged));
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? WorkspaceFolderChanged { add => _workspaceChanged.Add(value); remove => _workspaceChanged.Remove(value); }

    /// <inheritdoc />
    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged { add => _workspaceChangedWith.Add(value); remove => _workspaceChangedWith.Remove(value); }

    /// <summary>
    /// The open folder is to be read again (the explorer's Refresh): raises <see cref="WorkspaceChanged"/> with
    /// <see cref="WorkspaceChangeKind.Refreshed"/> (nothing when no folder is open).
    /// </summary>
    public void RefreshWorkspace()
    {
        if (_workspaceFolder is { } folder)
        {
            Raise(_workspaceChangedWith, new WorkspaceChangedEventArgs(folder, folder, WorkspaceChangeKind.Refreshed), nameof(WorkspaceChanged));
        }
    }

    /// <inheritdoc />
    public event EventHandler<LineMarkEventArgs>? LineMarkClicked { add => _lineMarkClicked.Add(value); remove => _lineMarkClicked.Remove(value); }

    /// <summary>The repositories the plug-ins published (<see cref="AddRepository(ISourceControlRepository)"/>), in order.</summary>
    public IReadOnlyList<ISourceControlRepository> Repositories => _repositories;

    /// <summary>Raised after a repository was published or taken back (each one's own <see cref="ISourceControlRepository.Changed"/> tells its status).</summary>
    public event EventHandler? RepositoriesChanged;

    /// <inheritdoc />
    public bool SetLineMarks(IEditorDocument document, string source, IReadOnlyList<LineMark> marks)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(marks);
        if (document is not OpenDocument { IsClosed: false } open)
        {
            return false;
        }

        DocumentLineMarks.Of(open.Document).Set(source, marks);
        return true;
    }

    /// <inheritdoc />
    public bool ShowInlineCard(IEditorDocument document, InlineCard card)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(card);
        if (document is not OpenDocument { IsClosed: false } open)
        {
            return false;
        }

        DocumentLineMarks.Of(open.Document).ShowCard(card);
        return true;
    }

    /// <inheritdoc />
    public IDisposable AddRepository(ISourceControlRepository repository) => AddRepository(repository, HostSource);

    /// <inheritdoc />
    public async Task<bool> ShowDiffAsync(string title, string path, string before, string after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (Presenter is not { } presenter)
        {
            return false;
        }

        // The diff of two whole texts is worked out on the thread pool; only the view is the UI thread's.
        int context = Options.DiffContextLines;
        int limit = Options.DiffLineLimit;
        var diff = await Task.Run(() => TextDiff.OfTexts(before, after, context, limit), cancellationToken).ConfigureAwait(true);
        return await presenter.ShowDiffAsync(title, path, diff, cancellationToken).ConfigureAwait(true);
    }

    internal IDisposable AddRepository(ISourceControlRepository repository, string source)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repositories.Add(repository);
        Log(source, EditorLogLevel.Debug, $"Repository {repository.Root} ({repository.Provider}) published.", null);
        RepositoriesChanged?.Invoke(this, EventArgs.Empty);
        return new Undo(() =>
        {
            if (_repositories.Remove(repository))
            {
                RepositoriesChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    // A document's marks are clicked in one of its editors: the plug-ins hear it with the host's document.
    private static void WatchLineMarks(OpenDocument open) => DocumentLineMarks.Of(open.Document).Clicked += open.OnLineMarkClicked;

    private static void UnwatchLineMarks(OpenDocument open)
    {
        var marks = DocumentLineMarks.Of(open.Document);
        marks.Clicked -= open.OnLineMarkClicked;
        if (marks.Marks.Length > 0)
        {
            // A closed document keeps no marks: a document opened again is marked afresh by its plug-in.
            foreach (var source in Sources(marks))
            {
                marks.Set(source, []);
            }
        }
    }

    private static List<string> Sources(DocumentLineMarks marks)
    {
        var sources = new List<string>();
        for (int i = 0; i < marks.Marks.Length; i++)
        {
            if (!sources.Contains(marks.SourceOf(i)))
            {
                sources.Add(marks.SourceOf(i));
            }
        }

        return sources;
    }

    private void OnLineMarkClicked(OpenDocument open, LineMarkClickEventArgs e) =>
        Raise(_lineMarkClicked, new LineMarkEventArgs(open, e.Source, e.Mark), nameof(LineMarkClicked));
}
