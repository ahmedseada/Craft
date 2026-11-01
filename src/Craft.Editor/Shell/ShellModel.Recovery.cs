// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Documents;

namespace Craft.Editor.Shell;

/// <summary>A file the last run left unsaved, as the recovery prompt lists it.</summary>
/// <param name="Entry">The recovery copy.</param>
/// <param name="Name">The file's name, or "Untitled" for a document never saved.</param>
/// <param name="Folder">The file's folder from the workspace (or its full folder), or null for an untitled document.</param>
/// <param name="Bytes">The copy's size on the disk.</param>
/// <param name="FileExists">Whether the file is on the disk (Restore opens it and puts the recovered text over it as one undo step).</param>
public sealed record RecoveredFile(RecoveryEntry Entry, string Name, string? Folder, long Bytes, bool FileExists)
{
    /// <summary>The file's path, or null for an untitled document.</summary>
    public string? Path => Entry.OriginalPath;

    /// <summary>"Program.cs · src · 14:32 · 2 KB": what a row of the card says.</summary>
    public string Describe(DateTimeOffset now)
    {
        var at = Entry.SavedAt.ToLocalTime();
        string when = at.Date == now.ToLocalTime().Date ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string where = Folder is { Length: > 0 } folder ? folder + " · " : Path is null ? "never saved · " : FileExists ? "" : "the file is gone · ";
        return $"{Name} · {where}{when} · {ShellText.Bytes(Bytes)}";
    }
}

/// <summary>
/// The recovery prompt's state (the card in the editor area and the notification): the recovered files still offered,
/// and whether the card shows. Used on the UI thread; <see cref="Changed"/> is raised there.
/// </summary>
public sealed class RecoveryOffer
{
    private List<RecoveredFile> _files = [];

    /// <summary>The files offered, newest first.</summary>
    public IReadOnlyList<RecoveredFile> Files => _files;

    /// <summary>Whether the card shows (files are offered and it was not closed; the notification keeps them).</summary>
    public bool CardVisible { get; private set; }

    /// <summary>"2 unsaved files were recovered".</summary>
    public string Title => _files.Count == 1 ? "1 unsaved file was recovered" : string.Create(CultureInfo.InvariantCulture, $"{_files.Count} unsaved files were recovered");

    /// <summary>Raised when the files or the card's visibility changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows the card again (the notification's Show).</summary>
    public void ShowCard() => SetCard(_files.Count > 0);

    /// <summary>Hides the card (its ×); the files stay offered in the notification centre.</summary>
    public void HideCard() => SetCard(false);

    internal void Set(List<RecoveredFile> files)
    {
        _files = files;
        CardVisible = files.Count > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal bool Remove(RecoveredFile file)
    {
        if (!_files.Remove(file))
        {
            return false;
        }

        CardVisible &= _files.Count > 0;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal void PutBack(IEnumerable<RecoveredFile> files)
    {
        foreach (var file in files)
        {
            if (!_files.Contains(file))
            {
                _files.Add(file);
            }
        }

        _files.Sort(static (a, b) => b.Entry.SavedAt.CompareTo(a.Entry.SavedAt));
        CardVisible = _files.Count > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetCard(bool visible)
    {
        if (CardVisible != visible)
        {
            CardVisible = visible;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

// The recovery prompt at start (E1's autosave, offered at last): the shell keeps recovery copies of every open document
// once the app starts it, and after the folder opens lists the copies an unclean end left for this workspace (and the
// untitled ones): a card in the editor area and a notification, with Restore, Compare and Discard.
public sealed partial class ShellModel
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextDocument, object> _recoveryTracked = [];
    private RecoveryStore? _recoveryStore;
    private IEditorNotification? _recoveryNotice;

    /// <summary>The recovery copies' store (null until <see cref="StartRecovery"/>).</summary>
    public RecoveryStore? RecoveryStore => _recoveryStore;

    /// <summary>The files the recovery prompt offers (the card and the notification).</summary>
    public RecoveryOffer Recovery { get; } = new();

    /// <summary>
    /// Starts keeping recovery copies (the app does at start; a shell under test when it asks): every open document's copy
    /// written every <c>CRAFT_AUTOSAVE_SECONDS</c> on the thread pool while it has unsaved changes, deleted when it is saved
    /// or closed. Idempotent.
    /// </summary>
    public RecoveryStore StartRecovery()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recoveryStore is { } started)
        {
            return started;
        }

        var store = new RecoveryStore(Options);
        _recoveryStore = store;
        foreach (var tab in Groups.AllTabs)
        {
            Track(tab.Document);
        }

        Host.DocumentOpened += OnRecoveryDocumentOpened;
        return store;
    }

    /// <summary>
    /// Offers the recovery copies an unclean end left (<c>CRAFT_RECOVERY_PROMPT</c>): listed off the UI thread, those of
    /// files in the open workspace and of untitled documents (every one with no folder open), newest first, with each
    /// copy's size and whether its file is on the disk; the card shows and a notification lists them. The files offered.
    /// </summary>
    public async Task<int> OfferRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.RecoveryPrompt || _disposed)
        {
            return 0;
        }

        var store = StartRecovery();
        string? root = Workspace?.Root;
        var files = await Task.Run(() => List(store, root), cancellationToken);
        if (_disposed)
        {
            return 0;
        }

        Recovery.Set(files);
        UpdateRecoveryNotice();
        return files.Count;
    }

    /// <summary>
    /// Restores <paramref name="file"/>: a file on the disk is opened (or found open) and the recovered text put over it as
    /// one undo step (unsaved; Ctrl+Z goes back to the disk's text); an untitled document, or a file that is gone, opens as
    /// a new unsaved tab of the recovered text. The copy then goes (the open document keeps its own); the tab joins the
    /// session. False when it could not.
    /// </summary>
    public async Task<bool> RestoreRecoveredAsync(RecoveredFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (_recoveryStore is not { } store)
        {
            return false;
        }

        using var activity = Host.Activities.Start($"Restoring {file.Name}");
        string text;
        try
        {
            text = await Task.Run(() => store.ReadText(file.Entry), cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            activity.Fail("the recovery copy could not be read", e);
            Host.ShowStatus("Recovery", $"{file.Name}'s recovery copy could not be read: {e.Message}", NotificationSeverity.Warning);
            return false;
        }

        if (file.Path is { } path && file.FileExists)
        {
            var tab = Groups.FindOpen(path) ?? await OpenFileAsync(path, cancellationToken: cancellationToken);
            if (tab is null)
            {
                activity.Fail("the file could not be opened");
                return false;
            }

            var snapshot = tab.Document.Buffer.Current;
            int lineLimit = Options.DiffLineLimit;
            var edits = await Task.Run(() => LineEdits.Between(snapshot, text, lineLimit), cancellationToken);
            if (Groups.GroupOf(tab) is null || tab.Document.Buffer.Current.Version != snapshot.Version)
            {
                activity.Complete("the text changed meanwhile: not restored");
                return false;
            }

            if (edits.Length > 0)
            {
                var before = tab.HostDocument.Selection;
                var after = SelectionState.At(Math.Min(before.Caret, edits[0].Start + edits[0].Text.Length));
                tab.Document.Edit(edits, EditKind.Command, before, after);
                Host.Select(tab.HostDocument, after);
            }

            Groups.GroupOf(tab)?.Tabs.Activate(tab);
        }
        else
        {
            TextDocument document;
            try
            {
                document = await Task.Run(() => store.Restore(file.Entry), cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                activity.Fail("the recovery copy could not be read", e);
                return false;
            }

            var language = file.Path is { } gone ? EditorLanguages.ForFile(gone, Workspace?.Root) : null;
            Tabs.Add(document, language);
        }

        Recovery.Remove(file);
        UpdateRecoveryNotice();
        _ = DiscardCopyAsync(store, file);
        activity.Complete("restored");
        return true;
    }

    /// <summary>Restores every file offered, in order; the number restored.</summary>
    public async Task<int> RestoreAllRecoveredAsync(CancellationToken cancellationToken = default)
    {
        int restored = 0;
        foreach (var file in Recovery.Files.ToArray())
        {
            if (await RestoreRecoveredAsync(file, cancellationToken))
            {
                restored++;
            }
        }

        return restored;
    }

    /// <summary>Shows the diff of <paramref name="file"/>'s text on the disk (none for an untitled one) against the recovered text, read off the UI thread.</summary>
    public async Task<bool> CompareRecoveredAsync(RecoveredFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (_recoveryStore is not { } store)
        {
            return false;
        }

        string? path = file.Path;
        (string Disk, string Recovered) texts;
        try
        {
            texts = await Task.Run(() => (path is not null && File.Exists(path) ? File.ReadAllText(path) : "", store.ReadText(file.Entry)), cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Host.ShowStatus("Recovery", $"{file.Name} could not be compared: {e.Message}", NotificationSeverity.Warning);
            return false;
        }

        return await Host.ShowDiffAsync($"{file.Name}: on the disk against recovered", path ?? file.Name, texts.Disk, texts.Recovered, cancellationToken);
    }

    /// <summary>
    /// Takes <paramref name="file"/> off the offer at once, with Undo in the inline bar; its copy is deleted (off the UI
    /// thread) when the bar goes without an Undo.
    /// </summary>
    public bool DiscardRecovered(RecoveredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (_recoveryStore is not { } store || !Recovery.Remove(file))
        {
            return false;
        }

        UpdateRecoveryNotice();
        UndoBar.Show($"Discarded the recovered {file.Name}", () =>
        {
            Recovery.PutBack([file]);
            UpdateRecoveryNotice();
        }, () => _ = DiscardCopyAsync(store, file));
        return true;
    }

    /// <summary>Discards every file offered, with one Undo for all of them.</summary>
    public int DiscardAllRecovered()
    {
        if (_recoveryStore is not { } store || Recovery.Files.Count == 0)
        {
            return 0;
        }

        var all = Recovery.Files.ToArray();
        foreach (var file in all)
        {
            Recovery.Remove(file);
        }

        UpdateRecoveryNotice();
        UndoBar.Show(all.Length == 1 ? $"Discarded the recovered {all[0].Name}" : string.Create(CultureInfo.InvariantCulture, $"Discarded {all.Length} recovered files"), () =>
        {
            Recovery.PutBack(all);
            UpdateRecoveryNotice();
        }, () =>
        {
            foreach (var file in all)
            {
                _ = DiscardCopyAsync(store, file);
            }
        });
        return all.Length;
    }

    /// <summary>The last copy deleted by a discard or a restore (the tests wait on it); completed when none is pending.</summary>
    public Task RecoveryDiscarded { get; private set; } = Task.CompletedTask;

    // The copies of this workspace's files and of untitled documents (every one with no folder), with their sizes; off the UI thread.
    private static List<RecoveredFile> List(RecoveryStore store, string? root)
    {
        var files = new List<RecoveredFile>();
        foreach (var entry in store.ListRecoverable())
        {
            string? path = entry.OriginalPath;
            string? folder = null;
            if (path is not null)
            {
                if (root is not null)
                {
                    var relative = System.IO.Path.GetRelativePath(root, path);
                    if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
                    {
                        continue;                                // another workspace's: offered when that folder opens
                    }

                    folder = System.IO.Path.GetDirectoryName(relative) is { Length: > 0 } inside ? inside.Replace('\\', '/') : null;
                }
                else
                {
                    folder = System.IO.Path.GetDirectoryName(path);
                }
            }

            files.Add(new RecoveredFile(entry, path is null ? "Untitled" : System.IO.Path.GetFileName(path), folder, store.SizeOf(entry), path is not null && File.Exists(path)));
        }

        return files;
    }

    private Task DiscardCopyAsync(RecoveryStore store, RecoveredFile file)
    {
        var discard = Task.Run(() =>
        {
            try
            {
                store.Discard(file.Entry);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A copy that cannot be deleted now is offered again at the next start.
            }
        });
        RecoveryDiscarded = Task.WhenAll(RecoveryDiscarded, discard);
        return discard;
    }

    // The notification centre's entry: the count and the files' names, with Restore all and Show; gone when none is left.
    private void UpdateRecoveryNotice()
    {
        var files = Recovery.Files;
        if (files.Count == 0)
        {
            _recoveryNotice?.Dispose();
            _recoveryNotice = null;
            return;
        }

        string text = string.Join(", ", files.Take(5).Select(f => f.Name)) + (files.Count > 5 ? ", …" : "") + ". Restore opens each with its unsaved changes (one undo step back to the disk's text).";
        if (_recoveryNotice is { } notice)
        {
            notice.Update(text);
            return;
        }

        _recoveryNotice = Host.Notify(Recovery.Title, text, NotificationSeverity.Warning,
        [
            new NotificationAction("Restore all", async token => await RestoreAllRecoveredAsync(token), Primary: true),
            new NotificationAction("Show", _ => { Recovery.ShowCard(); return Task.CompletedTask; }, Dismisses: false),
        ]);
    }

    private void OnRecoveryDocumentOpened(object? sender, EditorDocumentEventArgs e) => Track(e.Document.Document);

    private void Track(ITextDocument document)
    {
        // Once per document: a tab's language change opens the same document in the host again.
        if (_recoveryStore is { } store && document is TextDocument text && _recoveryTracked.TryAdd(text, store))
        {
            store.Track(text);
        }
    }

    private void DisposeRecovery()
    {
        _recoveryNotice?.Dispose();
        _recoveryNotice = null;
        if (_recoveryStore is { } store)
        {
            Host.DocumentOpened -= OnRecoveryDocumentOpened;
            store.Dispose();
            _recoveryStore = null;
        }
    }
}
