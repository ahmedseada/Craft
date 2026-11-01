// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>What restoring the last session did (<see cref="ShellModel.RestoreSessionAsync"/>).</summary>
/// <param name="Opened">The tabs opened again.</param>
/// <param name="Missing">The files that are gone (not opened, said once).</param>
/// <param name="Recovered">The files left for their recovery copies (not opened, said once).</param>
public sealed record SessionRestoreResult(int Opened, IReadOnlyList<string> Missing, IReadOnlyList<string> Recovered);

/// <summary>The preview tab's and the session's command ids (X part 9, items 14 and 15).</summary>
public static class NavigationCommands
{
    /// <summary>Tab: Keep open (the preview tab becomes an ordinary one; Ctrl+K Enter).</summary>
    public const string KeepTab = "craft.keepTab";

    /// <summary>File: Reopen the last session (the folder's groups and tabs as they were).</summary>
    public const string RestoreSession = "craft.restoreSession";
}

// X part 9, items 14 and 15: preview tabs (a single click in the explorer) and the last session, kept per workspace in the
// user's data folder and reopened when the same folder opens at start.
public sealed partial class ShellModel
{
    private EditorLayoutFile? _sessionFile;
    private bool _restoringSession;

    /// <summary>The last write of the session (the tests and the window's close wait on it); completed when none is pending.</summary>
    public Task SessionSaved { get; private set; } = Task.CompletedTask;

    /// <summary>The open workspace's session file (in the user's data folder), or null with no folder open.</summary>
    public EditorLayoutFile? SessionFile => _sessionFile;

    /// <summary>Whether the window's close should write the session first (a folder is open and <c>CRAFT_RESTORE_SESSION</c> is on).</summary>
    public bool SessionToWrite => !_disposed && Options.RestoreSession && _sessionFile is not null;

    /// <summary>The file the session of the workspace <paramref name="root"/> is kept in: the data folder's "sessions", named by the folder's hash.</summary>
    public static string SessionPathOf(CraftOptions options, string root)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToUpperInvariant() : full;
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return Path.Combine(options.ResolveDataFolder(), "sessions", Convert.ToHexStringLower(hash[..12]) + ".json");
    }

    /// <summary>
    /// Opens <paramref name="path"/> from a single click in the explorer (X part 9): with <c>CRAFT_PREVIEW_TABS</c> on, in
    /// the focused group's preview tab (italic), which the next preview replaces until it is edited, double-clicked or
    /// pinned; a file already open there is only shown. Off, as <see cref="OpenFileAsync"/>. Null when it cannot be read.
    /// </summary>
    public async Task<DocumentTab?> OpenPreviewAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Options.PreviewTabs)
        {
            return await OpenFileAsync(path, fromTree: true, cancellationToken);
        }

        var full = Path.GetFullPath(path);
        var tabs = Tabs;
        var tab = tabs.Find(full);
        if (tab is null && Groups.FindOpen(full) is { } elsewhere)
        {
            tab = tabs.AddSharedPreview(elsewhere);
        }

        if (tab is null)
        {
            ITextDocument document;
            try
            {
                document = await _openDocument(full, Options, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
            {
                await _ui.ShowMessageAsync("Craft: could not open", $"{full}: {e.Message}");
                return null;
            }

            if (Tabs.Find(full) is { } opened)
            {
                document.Dispose();                     // opened meanwhile (a second click while the first read)
                tab = opened;
            }
            else if (Groups.FindOpen(full) is { } openedElsewhere)
            {
                document.Dispose();
                tab = Tabs.AddSharedPreview(openedElsewhere);
            }
            else
            {
                tab = Tabs.AddPreview(document, LanguageFor(full));
            }
        }

        Groups.GroupOf(tab)?.Tabs.Activate(tab);
        Recent.Add(full, RecentKind.File);
        Layout.FileOpenedFromTree();
        return tab;
    }

    /// <summary>
    /// Keeps <paramref name="tab"/> (the active one when null) open: no longer a preview (a double click on it or on its
    /// file in the explorer, Ctrl+K Enter); false when it was not one.
    /// </summary>
    public bool KeepTab(DocumentTab? tab = null)
    {
        tab ??= TargetTab() ?? Tabs.Active;
        return tab is not null && Groups.GroupOf(tab) is { } group && group.Tabs.Keep(tab);
    }

    /// <summary>
    /// Opens <paramref name="path"/> from a double click in the explorer: as <see cref="OpenFileAsync"/>, the tab kept (a
    /// preview of it is kept, not opened twice).
    /// </summary>
    public async Task<DocumentTab?> OpenKeptAsync(string path, CancellationToken cancellationToken = default)
    {
        var tab = await OpenFileAsync(path, fromTree: true, cancellationToken);
        if (tab is not null)
        {
            KeepTab(tab);
        }

        return tab;
    }

    /// <summary>
    /// The state a tab's session keeps: its pin, the selection, scroll and folds of its editor (the view the app made for
    /// it), or what the last session left for a tab not shown since; null for a tab with no file.
    /// </summary>
    public EditorTabState? TabStateOf(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.FilePath is not { } path)
        {
            return null;
        }

        var pending = tab.PendingView;
        if (EditorOf?.Invoke(tab) is not { } editor)
        {
            return (pending ?? new EditorTabState(path) { Selection = tab.HostDocument.Selection }) with { Path = path, Pinned = tab.IsPinned };
        }

        var folded = editor.Folding.Map.Folded;
        var folds = new List<int>(folded.Count);
        foreach (var range in folded)
        {
            folds.Add(range.StartLine);
        }

        return new EditorTabState(path)
        {
            Pinned = tab.IsPinned,
            Selection = editor.Selection,
            ScrollX = editor.ScrollX,
            ScrollY = editor.ScrollY,
            Folds = folds.Count == 0 && pending is not null ? pending.Folds : folds,      // folds not applied yet stay as they were
        };
    }

    /// <summary>
    /// Writes the session now (the groups, their tabs, active tabs, the focus, each tab's pin, caret, scroll and folds) on the
    /// thread pool, latest wins; nothing when no folder is open or <c>CRAFT_RESTORE_SESSION</c> is off. The window's close
    /// awaits it.
    /// </summary>
    public Task SaveSessionAsync()
    {
        if (!SessionToWrite || _restoringSession || _sessionFile is not { } file)
        {
            return Task.CompletedTask;
        }

        var write = file.SaveAsync(Groups.Capture(TabStateOf));
        SessionSaved = write;
        _ = write.ContinueWith(done => Host.Dispatch("shell: the session was not written", () =>
            Host.Log(EditorLogLevel.Warning, $"The session was not written to {file.FilePath}: {done.Exception?.GetBaseException().Message}", done.Exception)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return write;
    }

    /// <summary>
    /// Reopens the open folder's last session (X part 9, item 15): its session file (else the workspace's kept layout,
    /// piece 3a's) read off the UI thread; the groups shaped, each file still on disk opened in its group in order, pinned
    /// again when it was, its caret, scroll and folds applied when its editor is made (<see cref="ApplyViewState"/>), the
    /// active tabs and the focus as they were. Files that are gone are skipped and said once; a file with a recovery copy is
    /// left for its recovery (never opened past it), said too. Null when nothing was restored (off, no folder, a tab open
    /// already, no session kept).
    /// </summary>
    public async Task<SessionRestoreResult?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.RestoreSession || Workspace is null || Groups.Groups.Count != 1 || Groups.AllTabs.Any())
        {
            return null;
        }

        var session = _sessionFile;
        var layout = _layoutFile;
        var recoveryFolder = RecoveryStore.FolderOf(Options);
        var (state, recovered) = await Task.Run(() => (session?.Load() ?? layout?.Load(), RecoveryStore.RecoveredFiles(recoveryFolder).ToHashSet(PathKeys.Comparer)), cancellationToken);
        if (state is null || Groups.Groups.Count != 1 || Groups.AllTabs.Any())
        {
            return null;
        }

        var wanted = new List<string>();
        Collect(state.Root, wanted);
        var present = await Task.Run(() => wanted.Where(File.Exists).ToHashSet(PathKeys.Comparer), cancellationToken);
        if (Groups.Groups.Count != 1 || Groups.AllTabs.Any() || Groups.Shape(state) is not { } shaped)
        {
            return null;
        }

        _restoringSession = true;
        int opened = 0;
        var missing = new List<string>();
        var left = new List<string>();
        try
        {
            var focused = Groups.Focused;
            foreach (var (group, node) in shaped)
            {
                DocumentTab? activeTab = null;
                foreach (var path in node.Files)
                {
                    if (recovered.Contains(path))
                    {
                        left.Add(path);
                        continue;
                    }

                    if (!present.Contains(path))
                    {
                        missing.Add(path);
                        continue;
                    }

                    if (await OpenInGroupAsync(group, path, activate: false, cancellationToken) is not { } tab)
                    {
                        missing.Add(path);
                        continue;
                    }

                    opened++;
                    if (node.TabOf(path) is { } kept)
                    {
                        if (kept.Pinned)
                        {
                            group.Tabs.SetPinned(tab, true);
                        }

                        tab.PendingView = kept;
                        int length = tab.Document.Buffer.Current.Length;
                        Host.UpdateSelection(tab.HostDocument, new SelectionState(Math.Clamp(kept.Selection.Anchor, 0, length), Math.Clamp(kept.Selection.Caret, 0, length)));
                    }

                    if (node.Active is { } active && PathKeys.Comparer.Equals(Path.GetFullPath(active), tab.FilePath ?? ""))
                    {
                        activeTab = tab;
                    }
                }

                if (activeTab is not null)
                {
                    group.Tabs.Select(activeTab);
                }
            }

            foreach (var (group, _) in shaped)
            {
                Groups.CloseIfEmpty(group);                     // a group whose files are all gone
            }

            if (Groups.Groups.Contains(focused))
            {
                Groups.Focus(focused);
            }

            Groups.Focused.Tabs.TookFocus();
        }
        finally
        {
            _restoringSession = false;
        }

        if (missing.Count > 0 || left.Count > 0)
        {
            var said = new List<string>();
            if (missing.Count > 0)
            {
                said.Add(string.Create(CultureInfo.InvariantCulture, $"{missing.Count} {(missing.Count == 1 ? "file is" : "files are")} gone: {Names(missing)}"));
            }

            if (left.Count > 0)
            {
                said.Add(string.Create(CultureInfo.InvariantCulture, $"{left.Count} {(left.Count == 1 ? "file has" : "files have")} a recovery copy, left for the recovery: {Names(left)}"));
            }

            Host.ShowStatus("Last session", string.Join("; ", said) + ".", NotificationSeverity.Warning);
        }

        return new SessionRestoreResult(opened, missing, left);

        static string Names(List<string> paths) => string.Join(", ", paths.Take(5).Select(Path.GetFileName)) + (paths.Count > 5 ? ", …" : "");
    }

    /// <summary>
    /// Applies the view state the last session left for <paramref name="tab"/> to its new editor (the app calls it when it
    /// makes the tab's view): the selection now, the scroll once the editor knows its viewport, the folds once its fold
    /// ranges arrive (then the scroll again, so the same lines show). Nothing when there is none.
    /// </summary>
    public void ApplyViewState(DocumentTab tab, EditorModel editor)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(editor);
        if (tab.PendingView is not { } state)
        {
            return;
        }

        int length = editor.Snapshot.Length;
        editor.Select(Math.Clamp(state.Selection.Anchor, 0, length), Math.Clamp(state.Selection.Caret, 0, length));
        void Scroll() => editor.ScrollToOffsets(state.ScrollX, state.ScrollY, instant: true);
        if (editor.ViewportHeight > 0)
        {
            Scroll();
        }
        else
        {
            void OnViewport(object? sender, EventArgs e)
            {
                if (editor.ViewportHeight > 0)
                {
                    editor.ViewportChanged -= OnViewport;
                    Scroll();
                }
            }

            editor.ViewportChanged += OnViewport;
        }

        if (state.Folds.Count == 0 || Fold(editor, state.Folds))
        {
            tab.PendingView = null;
            return;
        }

        void OnRanges(object? sender, EventArgs e)
        {
            if (Fold(editor, state.Folds))
            {
                editor.Folding.RangesChanged -= OnRanges;
                if (ReferenceEquals(tab.PendingView, state))
                {
                    tab.PendingView = null;
                }

                Scroll();
            }
        }

        editor.Folding.RangesChanged += OnRanges;
    }

    // Folds the lines the session kept once the editor has its ranges; false while it has none yet.
    private static bool Fold(EditorModel editor, IReadOnlyList<int> lines)
    {
        if (editor.Folding.Ranges.Length == 0)
        {
            return false;
        }

        foreach (int line in lines)
        {
            if (line < editor.Snapshot.LineCount && !editor.Folding.IsFolded(line) && editor.Folding.IsFoldable(line))
            {
                editor.Folding.Toggle(line);
            }
        }

        return true;
    }

    private static void Collect(EditorLayoutNodeState node, List<string> into)
    {
        into.AddRange(node.Files);
        foreach (var child in node.Children)
        {
            Collect(child, into);
        }
    }

    private void RegisterNavigationCommands()
    {
        Command(NavigationCommands.KeepTab, "Tab: Keep open", (_, _) => { KeepTab(); return Task.CompletedTask; }, _ => (TargetTab() ?? Tabs.Active) is { IsPreview: true }, "Ctrl+K Enter",
            _ => "The tab is not a preview.");
        Command(NavigationCommands.RestoreSession, "File: Reopen the last session", async (_, token) =>
        {
            if (await RestoreSessionAsync(token) is null)
            {
                Host.ShowStatus("Last session", Groups.AllTabs.Any() ? "Close the open tabs first: the last session opens into an empty editor area." : "No session is kept for this folder.");
            }
        }, _ => Workspace is not null && Options.RestoreSession, null, _ => Options.RestoreSession ? "Open a folder first." : "CRAFT_RESTORE_SESSION is off.");
        Menu(new MenuItem(EditorContextMenus.Tab, NavigationCommands.KeepTab, "Keep open", 0, 7)
        {
            Icon = MenuIcons.Pin,
            When = new MenuWhen { Test = static target => target.Item is DocumentTab { IsPreview: true } },      // only on a preview tab
        });
        Menu(EditorMenus.File, NavigationCommands.RestoreSession, 0, 4);
    }

    // The workspace opened or closed: the session of the one left is written, the new one's file taken.
    private void UseSessionFile(string? root)
    {
        _ = SaveSessionAsync();
        _sessionFile = root is null ? null : new EditorLayoutFile(root, SessionPathOf(Options, root));
    }

    // A change of the groups, tabs, focus or shares: the session is kept (latest wins), unless a restore is filling it.
    private void KeepSession() => _ = SaveSessionAsync();
}
