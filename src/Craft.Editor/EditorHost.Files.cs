// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Documents;
using Craft.Core.Workspaces;

namespace Craft.Editor;

/// <summary>A change to one file of a review across files: its path and the edits, made from the text the host reads it in.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Edits">
/// The edits to its text (in that text's positions, in order, not overlapping), given the text: the open document's
/// current text, or the file's when it is not open. Called off the UI thread; none: the file is left out.
/// </param>
public sealed record FileChange(string Path, Func<ITextSnapshot, IReadOnlyList<TextEdit>> Edits)
{
    /// <summary>What the review says the change came from ("Search", "Rename"), before where its text was read.</summary>
    public string Origin { get; init; } = "Search";
}

/// <summary>What a review across files applied, and how to take it back.</summary>
public sealed class FilesEditResult
{
    private readonly Func<Task<int>> _undo;
    private readonly Action _release;
    private int _ended;

    internal FilesEditResult(int files, int edits, int skipped, Func<Task<int>> undo, Action release)
    {
        Files = files;
        Edits = edits;
        Skipped = skipped;
        _undo = undo;
        _release = release;
    }

    /// <summary>The files changed.</summary>
    public int Files { get; }

    /// <summary>The edits applied in all.</summary>
    public int Edits { get; }

    /// <summary>The files accepted but left as they were because they changed while the user was asked.</summary>
    public int Skipped { get; }

    /// <summary>
    /// Takes the change back: each open document's undo step (while it is still its last change), each closed file written
    /// back as it was (while the disk still holds what was written); the files taken back. Once only.
    /// </summary>
    public Task<int> UndoAsync() => Interlocked.Exchange(ref _ended, 1) == 0 ? UndoThenRelease() : Task.FromResult(0);

    /// <summary>Keeps the change for good: what was held for Undo (the closed files' documents) is let go. Once only.</summary>
    public void Keep()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _release();
        }
    }

    private async Task<int> UndoThenRelease()
    {
        try
        {
            return await _undo().ConfigureAwait(true);
        }
        finally
        {
            _release();
        }
    }
}

public sealed partial class EditorHost
{
    /// <summary>
    /// Proposes <paramref name="changes"/> to several files as one review titled <paramref name="title"/> (X part 6: Replace
    /// all in files): each file's edits are made from its open document's current text, or from its file when it is not open
    /// (read off the UI thread into a document of its own, never a tab); the user sees every file's diff together
    /// (<see cref="Abstractions.IEditorPresenter.ChooseFilesAsync"/>) and accepts all, some hunks, or none. Accepted, each open document
    /// takes its hunks as one undo step, and each closed file is written with them (its encoding kept). A file that changed
    /// while the user was asked is left as it is and counted. Null when nothing was proposed or the user accepted nothing.
    /// </summary>
    public async Task<FilesEditResult?> ProposeFilesAsync(IReadOnlyList<FileChange> changes, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (Presenter is not { } presenter)
        {
            Log(HostSource, EditorLogLevel.Warning, $"'{title}': this host has no way to ask the user, so nothing was changed.", null);
            return null;
        }

        var held = new List<FileProposal>();
        try
        {
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await PrepareAsync(change, title, cancellationToken).ConfigureAwait(true) is { } prepared)
                {
                    held.Add(prepared);
                }
            }

            if (held.Count == 0)
            {
                return null;
            }

            var answers = await presenter.ChooseFilesAsync([.. held.Select(h => h.Proposal)], title, cancellationToken).ConfigureAwait(true);
            int files = 0, edits = 0, skipped = 0;
            var applied = new List<(FileProposal File, int Version, DateTime Written)>();
            for (int i = 0; i < held.Count; i++)
            {
                var file = held[i];
                var chosen = file.Proposal.Accepted(i < answers.Count ? answers[i] : []);
                if (chosen.Length == 0)
                {
                    continue;
                }

                if (!await file.StillAsAskedAsync().ConfigureAwait(true))
                {
                    skipped++;
                    Log(HostSource, EditorLogLevel.Warning, $"'{title}': {Path.GetFileName(file.Path)} changed while you were asked; it was left as it is.", null);
                    continue;
                }

                var document = file.Proposal.Document;
                var before = document.Selection;
                var after = new SelectionState(MapThrough(before.Anchor, chosen), MapThrough(before.Caret, chosen));
                var snapshot = document.Document.Edit(chosen, EditKind.Command, before, after);
                DateTime written = default;
                if (file.Open is { } open)
                {
                    UpdateSelection(open, after);
                }
                else
                {
                    await document.Document.SaveAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);
                    written = await Task.Run(() => File.GetLastWriteTimeUtc(file.Path), CancellationToken.None).ConfigureAwait(true);
                }

                applied.Add((file, snapshot.Version, written));
                files++;
                edits += chosen.Length;
            }

            if (files == 0)
            {
                Log(HostSource, EditorLogLevel.Information, $"'{title}': nothing was applied{(skipped > 0 ? "" : " (rejected by the user)")}.", null);
                ReleaseAll(held);
                return skipped == 0 ? null : new FilesEditResult(0, 0, skipped, () => Task.FromResult(0), () => { });
            }

            Log(HostSource, EditorLogLevel.Information, $"'{title}': {edits} {(edits == 1 ? "edit" : "edits")} in {files} {(files == 1 ? "file" : "files")}.", null);
            var kept = held;
            held = [];
            return new FilesEditResult(files, edits, skipped, () => UndoFilesAsync(applied), () => ReleaseAll(kept));
        }
        finally
        {
            ReleaseAll(held);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ProposeWorkspaceEditAsync"/>, then the shell's Undo bar for the whole change ("{title}: N files changed");
    /// without a bar the change is kept at once.
    /// </remarks>
    public async Task<bool> ProposeWorkspaceAsync(WorkspaceEdit edit, string title, CancellationToken cancellationToken = default)
    {
        var result = await ProposeWorkspaceEditAsync(edit, title, cancellationToken).ConfigureAwait(true);
        if (result is not { Files: > 0 } applied)
        {
            return false;
        }

        string text = applied.Files == 1 ? $"{title}: 1 file changed." : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{title}: {applied.Files} files changed.");
        if (!ShowUndo(text, () => _ = applied.UndoAsync(), applied.Keep))
        {
            applied.Keep();
        }

        return true;
    }

    /// <summary>
    /// Proposes <paramref name="edit"/> (edits to several files: a rename) as one review titled <paramref name="title"/>
    /// (<see cref="ProposeFilesAsync"/>: every file's diff together, Accept all; each open document one undo step; a file
    /// that is not open read off the UI thread, edited and saved on accept). Refused before the user is asked, and said in
    /// the log, when an open document changed since the edit was made (its <see cref="WorkspaceFileEdit.Version"/>), when a
    /// file the edit made against unsaved text is no longer open, or when an open document has unsaved changes the edit
    /// was not made against. Null when nothing was proposed, refused, or accepted.
    /// </summary>
    public async Task<FilesEditResult?> ProposeWorkspaceEditAsync(WorkspaceEdit edit, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (edit.Refusal is { } refusal)
        {
            Log(HostSource, EditorLogLevel.Information, $"'{title}': {refusal}", null);
            return null;
        }

        var changes = new List<FileChange>(edit.Files.Count);
        foreach (var file in edit.Files)
        {
            string path = Path.GetFullPath(file.Path);
            var open = _documents.FirstOrDefault(d => !d.IsClosed && d.FilePath is { } p && PathKeys.Comparer.Equals(Path.GetFullPath(p), path));
            string? stale = (open, file.Version) switch
            {
                (null, not null) => "was closed since the change was made",
                ({ } doc, { } version) when doc.Document.Buffer.Current.Version != version => "changed since the change was made",
                ({ } doc, null) when doc.Document.IsDirty => "has unsaved changes the change was not made against",
                _ => null,
            };
            if (stale is not null)
            {
                Log(HostSource, EditorLogLevel.Warning, $"'{title}': {Path.GetFileName(path)} {stale}; nothing was proposed (run it again).", null);
                LastWorkspaceEditRefusal = $"{Path.GetFileName(path)} {stale}; run it again.";
                return null;
            }

            var edits = file.Edits;
            changes.Add(new FileChange(path, _ => edits) { Origin = title });
        }

        LastWorkspaceEditRefusal = null;
        try
        {
            return await ProposeFilesAsync(changes, title, cancellationToken).ConfigureAwait(true);
        }
        catch (ArgumentException error)
        {
            // An edit outside a file's text: the file on disk changed since the edit was made.
            Log(HostSource, EditorLogLevel.Warning, $"'{title}': {error.Message} Nothing was changed.", error);
            LastWorkspaceEditRefusal = "A file changed on disk since the change was made; run it again.";
            return null;
        }
    }

    /// <summary>Why the last <see cref="ProposeWorkspaceEditAsync"/> refused before asking the user, or null when it did not.</summary>
    public string? LastWorkspaceEditRefusal { get; private set; }

    // A file's proposal: its open document, or its text read into a document of its own (off the UI thread).
    private async Task<FileProposal?> PrepareAsync(FileChange change, string title, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(change.Path);
        IEditorDocument document;
        OpenDocument? open = null;
        TextDocument? background = null;
        DateTime read = default;
        if (_documents.FirstOrDefault(d => !d.IsClosed && d.FilePath is { } p && PathKeys.Comparer.Equals(Path.GetFullPath(p), path)) is { } found)
        {
            open = found;
            document = found;
        }
        else
        {
            try
            {
                (background, read) = await Task.Run(async () =>
                {
                    var stamp = File.GetLastWriteTimeUtc(path);
                    return (await TextDocument.OpenAsync(path, Options, cancellationToken).ConfigureAwait(false), stamp);
                }, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log(HostSource, EditorLogLevel.Warning, $"'{title}': {path} could not be read ({e.Message}); it was left out.", e);
                return null;
            }

            document = new BackgroundDocument(background);
        }

        var snapshot = document.Document.Buffer.Current;
        TextEdit[] edits = [.. await Task.Run(() => change.Edits(snapshot), cancellationToken).ConfigureAwait(true)];
        if (edits.Length == 0)
        {
            background?.Dispose();
            return null;
        }

        Validate(edits, snapshot.Length);
        var diff = TextDiff.OfEdits(snapshot, edits, Options.DiffContextLines, Options.DiffLineLimit);
        var proposal = new EditProposal(document, edits, title, HostSource, snapshot.Version,
            EditProposal.Summarize(snapshot, path, edits, Options.ProposalPreviewCharacters))
        {
            Diff = diff,
            HunkOfEdit = EditProposal.HunksOf(snapshot, edits, diff),
            Origin = change.Origin + (open is null ? ": the file on disk (not open)" : ": the open document"),
        };
        return new FileProposal(path, proposal, open, background, read);
    }

    private async Task<int> UndoFilesAsync(List<(FileProposal File, int Version, DateTime Written)> applied)
    {
        int restored = 0;
        foreach (var (file, version, written) in applied)
        {
            var document = file.Proposal.Document;
            if (file.Open is { IsClosed: false } open)
            {
                if (open.Document.Buffer.Current.Version == version && open.Document.CanUndo && open.Document.Undo() is { } selection)
                {
                    UpdateSelection(open, selection);
                    restored++;
                }
            }
            else if (file.Background is { } background)
            {
                var now = await Task.Run(() => File.Exists(file.Path) ? File.GetLastWriteTimeUtc(file.Path) : default).ConfigureAwait(true);
                if (now == written && background.Undo() is not null)
                {
                    await background.SaveAsync().ConfigureAwait(true);
                    restored++;
                }
            }
        }

        return restored;
    }

    private static void ReleaseAll(List<FileProposal> files)
    {
        foreach (var file in files)
        {
            file.Background?.Dispose();
        }

        files.Clear();
    }

    // One file of a review across files.
    private sealed record FileProposal(string Path, EditProposal Proposal, OpenDocument? Open, TextDocument? Background, DateTime Read)
    {
        // The open document still at the version it was asked about, or the closed file not written since it was read.
        public async Task<bool> StillAsAskedAsync()
        {
            if (Open is { } open)
            {
                return !open.IsClosed && open.Document.Buffer.Current.Version == Proposal.Version;
            }

            var path = Path;
            var read = Read;
            return await Task.Run(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) == read).ConfigureAwait(true);
        }
    }

    // A closed file's text as a document of its own: what the review shows and what an accepted change is saved from.
    private sealed class BackgroundDocument(TextDocument document) : IEditorDocument
    {
        public ITextDocument Document { get; } = document;

        public string? FilePath => Document.FilePath;

        public string LanguageName => EditorLanguages.ForFile(Document.FilePath ?? "")?.Name ?? "text";

        public ILanguage? Language => EditorLanguages.ForFile(Document.FilePath ?? "");

        public SelectionState Selection => SelectionState.At(0);
    }
}
