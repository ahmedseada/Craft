// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The bookmarks' command ids (X part 9, item 13).</summary>
public static class BookmarkCommands
{
    /// <summary>Bookmarks: Toggle at the caret (Ctrl+K Ctrl+K; Visual Studio's keymap Ctrl+F2 too).</summary>
    public const string Toggle = "craft.bookmarks.toggle";

    /// <summary>Bookmarks: Next (Ctrl+K Ctrl+N).</summary>
    public const string Next = "craft.bookmarks.next";

    /// <summary>Bookmarks: Previous (Ctrl+K Ctrl+P).</summary>
    public const string Previous = "craft.bookmarks.previous";

    /// <summary>Bookmarks: Label the bookmark at the caret.</summary>
    public const string Label = "craft.bookmarks.label";

    /// <summary>Bookmarks: Clear all (with Undo).</summary>
    public const string Clear = "craft.bookmarks.clear";

    /// <summary>View: Bookmarks (the side view; Visual Studio's keymap Ctrl+K Ctrl+W).</summary>
    public const string Show = "craft.bookmarks.show";
}

// X part 9, item 13: bookmarks. The list follows the open documents (their edits move it), the gutter shows it through the
// documents' line marks, the side view lists it, the workspace's .craft/bookmarks.json keeps it.
public sealed partial class ShellModel
{
    /// <summary>The Bookmarks view's panel id (and its rail item's).</summary>
    public const string BookmarksPanelId = "craft.bookmarks";

    private const string BookmarkOpenAction = "bookmarks.open";
    private const string BookmarkRemoveAction = "bookmarks.remove";
    private const string BookmarkLabelAction = "bookmarks.label";
    private const string BookmarkClearAction = "bookmarks.clear";
    private BookmarksFile? _bookmarksFile;
    private IDisposable? _bookmarksPanel;
    private int _bookmarksVersion;
    private bool _bookmarksLoading;

    /// <summary>The workspace's bookmarks.</summary>
    public BookmarkList Bookmarks { get; } = new();

    /// <summary>The Bookmarks side view.</summary>
    public ViewPanel BookmarksView { get; private set; } = null!;

    /// <summary>The last write of the bookmarks file (the tests wait on it); completed when none is pending.</summary>
    public Task BookmarksSaved { get; private set; } = Task.CompletedTask;

    /// <summary>The last read of the workspace's bookmarks (the tests wait on it).</summary>
    public Task BookmarksLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Toggles a bookmark on the active tab's caret line (the focused view's caret); true when one was added, false when
    /// one was taken away or the tab has no file.
    /// </summary>
    public bool ToggleBookmark()
    {
        if (Tabs.Active is not { FilePath: { } path } tab)
        {
            Host.ShowStatus("Bookmarks", "Save the file first: a bookmark marks a line of a file.");
            return false;
        }

        Bookmarks.Attach(tab.Document);
        return Bookmarks.Toggle(path, CaretLineOf(tab), tab.Document.Buffer.Current);
    }

    /// <summary>Goes to the next (or previous) bookmark from the active tab's caret, across files; false when there is none.</summary>
    public async Task<bool> GoToBookmarkAsync(bool forward, CancellationToken cancellationToken = default)
    {
        var tab = Tabs.Active;
        var next = Bookmarks.Next(tab?.FilePath, tab is null ? -1 : CaretLineOf(tab), forward, Options.BookmarksWrap);
        if (next is null)
        {
            Host.ShowStatus("Bookmarks", Bookmarks.Count == 0 ? "No bookmarks yet: Ctrl+K Ctrl+K marks the caret's line." : "No more bookmarks that way (CRAFT_BOOKMARKS_WRAP is off).");
            return false;
        }

        return await OpenAtLineAsync(next.Path, next.Line, 0, cancellationToken);
    }

    /// <summary>Asks for a label for the bookmark at the caret (one is added first when there is none).</summary>
    public async Task LabelBookmarkAsync(CancellationToken cancellationToken = default)
    {
        if (Tabs.Active is not { FilePath: { } path } tab)
        {
            return;
        }

        int line = CaretLineOf(tab);
        if (!Bookmarks.Has(path, line))
        {
            Bookmarks.Toggle(path, line, tab.Document.Buffer.Current);
        }

        var current = Bookmarks.Of(path).FirstOrDefault(b => b.Line == line)?.Label ?? "";
        if (await _ui.PromptAsync("Label the bookmark", string.Create(CultureInfo.InvariantCulture, $"{tab.Title}, line {line + 1}"), "A label (empty takes it away)", current, "Label", cancellationToken) is { } label)
        {
            Bookmarks.SetLabel(path, line, label);
        }
    }

    /// <summary>Takes every bookmark away at once, with Undo in the inline bar; the number taken.</summary>
    public int ClearBookmarks()
    {
        var taken = Bookmarks.Clear();
        if (taken.Count > 0)
        {
            UndoBar.Show(taken.Count == 1 ? "Cleared 1 bookmark." : string.Create(CultureInfo.InvariantCulture, $"Cleared {taken.Count} bookmarks."),
                () => Bookmarks.Set(taken, replace: false));
        }

        return taken.Count;
    }

    // The caret's line in the tab's focused editor, else the host's selection of its document.
    private int CaretLineOf(DocumentTab tab)
    {
        var snapshot = tab.Document.Buffer.Current;
        int caret = EditorOf?.Invoke(tab) is { } editor ? editor.Selection.Caret : tab.HostDocument.Selection.Caret;
        return snapshot.GetLineIndex(Math.Clamp(caret, 0, snapshot.Length));
    }

    /// <summary>Opens <paramref name="path"/> at its line <paramref name="line"/> (from 0) and column (from 0), the caret there; false when it cannot be read.</summary>
    public async Task<bool> OpenAtLineAsync(string path, int line, int column, CancellationToken cancellationToken = default)
    {
        if (await OpenFileAsync(path, cancellationToken: cancellationToken) is not { } tab)
        {
            return false;
        }

        var snapshot = tab.Document.Buffer.Current;
        var text = snapshot.GetLine(Math.Clamp(line, 0, snapshot.LineCount - 1));
        return Host.Select(tab.HostDocument, SelectionState.At(text.Start + Math.Clamp(column, 0, text.Length)));
    }

    private void CreateBookmarks()
    {
        BookmarksView = new ViewPanel(BookmarksPanelId, "Bookmarks", ActOnBookmarkAsync);
        _bookmarksPanel = Host.AddPanel(BookmarksView);
        Bookmarks.Changed += (_, _) => PublishBookmarks();
        Bookmarks.Edited += (_, _) => KeepBookmarks();
        Groups.TabsChanged += (_, _) => FollowBookmarks();
        Groups.TabChanged += (_, _) => FollowBookmarks();
        PublishBookmarks();
    }

    private void RegisterBookmarkCommands()
    {
        Command(BookmarkCommands.Toggle, "Bookmarks: Toggle at the caret", (_, _) => { ToggleBookmark(); return Task.CompletedTask; }, _ => Tabs.Active is not null, "Ctrl+K Ctrl+K");
        Command(BookmarkCommands.Next, "Bookmarks: Next", (_, token) => GoToBookmarkAsync(true, token), _ => Bookmarks.Count > 0, "Ctrl+K Ctrl+N", _ => "No bookmarks yet.");
        Command(BookmarkCommands.Previous, "Bookmarks: Previous", (_, token) => GoToBookmarkAsync(false, token), _ => Bookmarks.Count > 0, "Ctrl+K Ctrl+P", _ => "No bookmarks yet.");
        Command(BookmarkCommands.Label, "Bookmarks: Label the bookmark at the caret", (_, token) => LabelBookmarkAsync(token), _ => Tabs.Active?.FilePath is not null, null);
        Command(BookmarkCommands.Clear, "Bookmarks: Clear all", (_, _) => { ClearBookmarks(); return Task.CompletedTask; }, _ => Bookmarks.Count > 0, null, _ => "No bookmarks.");
        Command(BookmarkCommands.Show, "View: Bookmarks", (_, _) => Layout.IsNarrow ? Task.CompletedTask : Panels.ShowSide(BookmarksPanelId), null, null);
        Rail(new RailItem(BookmarksPanelId, "Bookmarks", SideViewCommand(BookmarksPanelId), RailIcons.Bookmark, 25));
        Menu(new MenuItem(EditorMenus.View, BookmarkCommands.Show, null, 0, 6) { Icon = RailIcons.Bookmark });
        Menu(new MenuItem(EditorContextMenus.Editor, BookmarkCommands.Toggle, "Toggle bookmark", 0, 8) { Icon = RailIcons.Bookmark });
    }

    // The open documents with a file are followed (their edits move their bookmarks); closed ones are let go.
    private void FollowBookmarks()
    {
        if (_disposed)
        {
            return;
        }

        var open = new HashSet<ITextDocument>(ReferenceEqualityComparer.Instance);
        foreach (var tab in Groups.AllTabs)
        {
            if (tab.FilePath is not null && open.Add(tab.Document))
            {
                Bookmarks.Attach(tab.Document);
            }
        }

        foreach (var document in Bookmarks.Followed.ToArray())
        {
            if (!open.Contains(document))
            {
                Bookmarks.Detach(document);
            }
        }
    }

    // The workspace opened or closed: its bookmarks read off the UI thread (a later folder's read wins).
    private void LoadBookmarks(string? root)
    {
        _bookmarksFile = root is null ? null : new BookmarksFile(root);
        int version = ++_bookmarksVersion;
        if (_bookmarksFile is not { } file)
        {
            _bookmarksLoading = true;
            Bookmarks.Set([], replace: true);
            _bookmarksLoading = false;
            return;
        }

        BookmarksLoaded = LoadAsync();

        async Task LoadAsync()
        {
            var kept = await Task.Run(file.Load).ConfigureAwait(true);
            if (version != _bookmarksVersion || _disposed)
            {
                return;
            }

            _bookmarksLoading = true;
            Bookmarks.Set(kept, replace: true);
            _bookmarksLoading = false;
            FollowBookmarks();
        }
    }

    // A change the user made (a toggle, a label, a clear) and a save of a followed document are written, on the thread pool.
    private void KeepBookmarks()
    {
        if (_disposed || _bookmarksLoading || _bookmarksFile is not { } file)
        {
            return;
        }

        var write = file.SaveAsync(Bookmarks.All());
        BookmarksSaved = write;
        _ = write.ContinueWith(done => Host.Dispatch("shell: the bookmarks were not written", () =>
            Host.Log(EditorLogLevel.Warning, $"The bookmarks were not written to {file.FilePath}: {done.Exception?.GetBaseException().Message}", done.Exception)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private void OnBookmarksSaved(object? sender, EditorDocumentEventArgs e)
    {
        // The lines a document's edits moved its bookmarks to are its file's lines once it is saved.
        if (e.Document.Document.FilePath is { } path && Bookmarks.Of(path).Count > 0)
        {
            KeepBookmarks();
        }
    }

    private void PublishBookmarks()
    {
        if (_disposed || BookmarksView is null)
        {
            return;
        }

        var all = Bookmarks.All();
        var groups = new List<PanelResultGroup>();
        foreach (var file in all.GroupBy(b => b.Path, Core.Workspaces.PathKeys.Comparer))
        {
            var path = file.Key;
            var relative = Workspace?.Root is { } root && Path.GetRelativePath(root, path) is var r && !r.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(r)
                ? r.Replace('\\', '/') : path;
            int slash = relative.LastIndexOf('/');
            var icon = FileIcons.For(path);
            var rows = file.Select(b => new PanelResultRow(Key(b.Path, b.Line), b.Text.Length == 0 ? "(an empty line)" : b.Text)
            {
                Prefix = (b.Line + 1).ToString(CultureInfo.InvariantCulture),
                Detail = b.Label,
                Code = true,
                Icon = RailIcons.Bookmark,
                ActionId = BookmarkOpenAction,
                Value = Key(b.Path, b.Line),
                Buttons =
                [
                    new PanelButton("Label", BookmarkLabelAction, PanelButtonKind.Flat) { Icon = RailIcons.Edit, Value = Key(b.Path, b.Line) },
                    new PanelButton("Remove", BookmarkRemoveAction, PanelButtonKind.Flat) { Icon = RailIcons.Close, Value = Key(b.Path, b.Line) },
                ],
            }).ToList();
            groups.Add(new PanelResultGroup(path, slash < 0 ? relative : relative[(slash + 1)..], rows)
            {
                Detail = slash < 0 ? null : relative[..slash],
                Badge = rows.Count.ToString(CultureInfo.InvariantCulture),
                Glyph = icon.Badge,
                GlyphArgb = icon.Argb,
                Tooltip = path,
            });
        }

        string? keys = Host.KeysOf(BookmarkCommands.Toggle);
        var blocks = new List<PanelBlock>
        {
            new PanelText(all.Count == 0
                ? $"No bookmarks. {(keys is null ? "Bookmarks: Toggle" : keys)} marks the caret's line; Ctrl+K Ctrl+N and Ctrl+K Ctrl+P go through them."
                : string.Create(CultureInfo.InvariantCulture, $"{all.Count} {(all.Count == 1 ? "bookmark" : "bookmarks")} in {groups.Count} {(groups.Count == 1 ? "file" : "files")}"), PanelTextStyle.Secondary),
        };
        if (all.Count > 0)
        {
            blocks.Add(new PanelButtons([new PanelButton("Clear all", BookmarkClearAction, PanelButtonKind.Flat) { Icon = RailIcons.Close }]));
        }

        blocks.Add(new PanelResults("bookmarks.list", groups) { Version = unchecked(++_bookmarksVersionShown), Empty = null });
        BookmarksView.SetBlocks(blocks);
        BookmarksView.Badge = all.Count == 0 ? null : all.Count.ToString(CultureInfo.InvariantCulture);
    }

    private int _bookmarksVersionShown;

    private async Task ActOnBookmarkAsync(string action, string? value, CancellationToken cancellationToken)
    {
        if (action == BookmarkClearAction)
        {
            ClearBookmarks();
            return;
        }

        if (value is null || value.LastIndexOf('\n') is not (> 0 and var cut) || !int.TryParse(value.AsSpan(cut + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int line))
        {
            return;
        }

        var path = value[..cut];
        switch (action)
        {
            case BookmarkOpenAction:
                await OpenAtLineAsync(path, line, 0, cancellationToken);
                break;
            case BookmarkRemoveAction:
                Bookmarks.Remove(path, line);
                break;
            case BookmarkLabelAction:
                var current = Bookmarks.Of(path).FirstOrDefault(b => b.Line == line)?.Label ?? "";
                if (await _ui.PromptAsync("Label the bookmark", string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}, line {line + 1}"), "A label (empty takes it away)", current, "Label", cancellationToken) is { } label)
                {
                    Bookmarks.SetLabel(path, line, label);
                }

                break;
        }
    }

    private static string Key(string path, int line) => string.Create(CultureInfo.InvariantCulture, $"{path}\n{line}");

    private void DisposeBookmarks()
    {
        _bookmarksPanel?.Dispose();
        foreach (var document in Bookmarks.Followed.ToArray())
        {
            Bookmarks.Detach(document);
        }
    }
}
