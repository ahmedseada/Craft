// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Documents;

namespace Craft.Editor.Shell;

/// <summary>The local history's command ids (X part 9, item 16).</summary>
public static class LocalHistoryCommands
{
    /// <summary>View: Timeline (the active file's local history).</summary>
    public const string Show = "craft.localHistory.show";
}

// X part 9, item 16: the local history. Each save through the host keeps a snapshot of the file's text (on the thread
// pool, in the user's data folder); the Timeline side view lists the active file's, a row's click shows its diff with the
// text now, Restore takes the text back as one undo step with Undo in the inline bar.
public sealed partial class ShellModel
{
    /// <summary>The Timeline view's panel id (and its rail item's).</summary>
    public const string TimelinePanelId = "craft.timeline";

    private const string TimelineCompareAction = "timeline.compare";
    private const string TimelineRestoreAction = "timeline.restore";
    private IDisposable? _timelinePanel;
    private IReadOnlyList<LocalHistoryEntry> _timeline = [];
    private string? _timelineFile;
    private int _timelineRead;
    private int _timelineShown;

    /// <summary>The local history (snapshots per save, in the user's data folder).</summary>
    public LocalHistory History { get; private set; } = null!;

    /// <summary>The Timeline side view (the active file's snapshots).</summary>
    public ViewPanel TimelineView { get; private set; } = null!;

    /// <summary>The last snapshot taken (the tests wait on it); completed when none is pending.</summary>
    public Task<LocalHistoryEntry?> HistorySaved { get; private set; } = Task.FromResult<LocalHistoryEntry?>(null);

    /// <summary>The Timeline's last read of the active file's snapshots (the tests wait on it).</summary>
    public Task TimelineRead { get; private set; } = Task.CompletedTask;

    /// <summary>The snapshots the Timeline shows (the active file's, newest first).</summary>
    public IReadOnlyList<LocalHistoryEntry> Timeline => _timeline;

    /// <summary>Shows the diff of <paramref name="entry"/> with its file's text now (the open document's, else the file's), its text read off the UI thread.</summary>
    public async Task<bool> CompareWithSnapshotAsync(LocalHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var open = Groups.FindOpen(entry.FilePath);
        var snapshot = open?.Document.Buffer.Current;
        var (before, now) = await Task.Run(() => (LocalHistory.Read(entry), snapshot is not null ? snapshot.GetText(0, snapshot.Length) : File.Exists(entry.FilePath) ? File.ReadAllText(entry.FilePath) : ""), cancellationToken);
        return await Host.ShowDiffAsync($"{Path.GetFileName(entry.FilePath)}: {When(entry.Time)} against now", entry.FilePath, before, now, cancellationToken);
    }

    /// <summary>
    /// Takes <paramref name="entry"/>'s file back to the snapshot's text (opened first when it is not), as one undo step, not
    /// saved; Undo in the inline bar (or Ctrl+Z) brings the text back. False when it could not.
    /// </summary>
    public async Task<bool> RestoreSnapshotAsync(LocalHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var activity = Host.Activities.Start($"Restoring {Path.GetFileName(entry.FilePath)} from {When(entry.Time)}");
        var tab = Groups.FindOpen(entry.FilePath) ?? await OpenFileAsync(entry.FilePath, cancellationToken: cancellationToken);
        if (tab is null)
        {
            activity.Fail("the file could not be opened");
            return false;
        }

        var snapshot = tab.Document.Buffer.Current;
        int lineLimit = Options.DiffLineLimit;
        var edits = await Task.Run(() => LineEdits.Between(snapshot, LocalHistory.Read(entry), lineLimit), cancellationToken);
        if (Groups.GroupOf(tab) is null || tab.Document.Buffer.Current.Version != snapshot.Version)
        {
            activity.Complete("the text changed meanwhile: not restored");
            return false;
        }

        if (edits.Length == 0)
        {
            activity.Complete("the text is the snapshot's already");
            Host.ShowStatus("Local history", $"{tab.Title} is the same as at {When(entry.Time)}.");
            return true;
        }

        var before = tab.HostDocument.Selection;
        var after = SelectionState.At(Math.Min(before.Caret, edits[0].Start + edits[0].Text.Length));
        tab.Document.Edit(edits, EditKind.Command, before, after);
        Host.Select(tab.HostDocument, after);
        int version = tab.Document.Buffer.Current.Version;
        activity.Complete("restored");
        UndoBar.Show($"Restored {tab.Title} from {When(entry.Time)}", () => UndoRevert(tab, version));
        return true;
    }

    /// <summary>Reads the active file's snapshots again for the Timeline (off the UI thread).</summary>
    public Task RefreshTimeline()
    {
        var path = Host.ActiveDocument?.Document.FilePath;
        _timelineFile = path;
        int read = ++_timelineRead;
        var history = History;
        TimelineRead = ReadAsync();
        return TimelineRead;

        async Task ReadAsync()
        {
            var entries = path is null ? [] : await Task.Run(() => history.Entries(path)).ConfigureAwait(true);
            if (read != _timelineRead || _disposed)
            {
                return;
            }

            _timeline = entries;
            PublishTimeline();
        }
    }

    private void CreateTimeline()
    {
        History = LocalHistory.InDataFolder(Options);
        TimelineView = new ViewPanel(TimelinePanelId, "Timeline", ActOnTimelineAsync);
        _timelinePanel = Host.AddPanel(TimelineView);
        Host.ActiveDocumentChanged += OnTimelineActiveChanged;
        PublishTimeline();
    }

    private void RegisterTimelineCommands()
    {
        Command(LocalHistoryCommands.Show, "View: Timeline (local history)", async (_, _) =>
        {
            await RefreshTimeline();
            if (!Layout.IsNarrow)
            {
                await Panels.ShowSide(TimelinePanelId);
            }
        }, null, null);
        Rail(new RailItem(TimelinePanelId, "Timeline", SideViewCommand(TimelinePanelId), RailIcons.History, 26));
        Menu(new MenuItem(EditorMenus.View, LocalHistoryCommands.Show, null, 1, 6) { Icon = RailIcons.History });
        Menu(new MenuItem(EditorContextMenus.Tab, LocalHistoryCommands.Show, "Open timeline", 1, 7)
        {
            Icon = RailIcons.History,
            When = new MenuWhen { Test = static target => target.Item is DocumentTab { FilePath: not null } },   // a file's tab: its snapshots
        });
    }

    private void OnTimelineActiveChanged(object? sender, EventArgs e)
    {
        if (!string.Equals(Host.ActiveDocument?.Document.FilePath, _timelineFile, StringComparison.Ordinal))
        {
            _ = RefreshTimeline();
        }
    }

    // A save through the host: the file's text kept as a snapshot, on the thread pool; the Timeline reads it again when it
    // shows that file.
    private void OnHistorySaved(object? sender, EditorDocumentEventArgs e)
    {
        if (_disposed || e.Document.Document.FilePath is not { } path)
        {
            return;
        }

        History.Options = Options;
        if (!History.IsOn)
        {
            return;
        }

        var add = History.AddAsync(path, e.Document.Document.Buffer.Current);
        HistorySaved = add;
        var refresh = KeepSnapshotAsync(add, path);
        if (string.Equals(_timelineFile, path, StringComparison.Ordinal))
        {
            TimelineRead = refresh;                           // the Timeline shows this file: it reads it again after the write
        }
    }

    // After the snapshot's write: the Timeline reads the file's snapshots again when it shows that file; a write that failed
    // is logged (the save itself stands).
    private async Task KeepSnapshotAsync(Task<LocalHistoryEntry?> add, string path)
    {
        try
        {
            if (await add.ConfigureAwait(true) is not null && string.Equals(_timelineFile, path, StringComparison.Ordinal))
            {
                await RefreshTimeline().ConfigureAwait(true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Host.Dispatch("shell: a local history snapshot", () => Host.Log(EditorLogLevel.Warning, $"The local history did not keep {path}: {error.Message}", error));
        }
    }

    private void PublishTimeline()
    {
        if (_disposed || TimelineView is null)
        {
            return;
        }

        var blocks = new List<PanelBlock>();
        if (!History.IsOn)
        {
            blocks.Add(new PanelText("The local history is off (CRAFT_LOCAL_HISTORY_MAX_MB is 0).", PanelTextStyle.Secondary));
        }
        else if (_timelineFile is not { } path)
        {
            blocks.Add(new PanelText("Open a file: each save keeps a snapshot of it here.", PanelTextStyle.Secondary));
        }
        else
        {
            blocks.Add(new PanelText(_timeline.Count == 0
                ? $"No snapshots of {Path.GetFileName(path)} yet: each save keeps one (in the data folder, never the workspace)."
                : string.Create(CultureInfo.InvariantCulture, $"{_timeline.Count} {(_timeline.Count == 1 ? "snapshot" : "snapshots")} of {Path.GetFileName(path)}; a click shows the change since"), PanelTextStyle.Secondary));
            var rows = new List<PanelResultRow>(_timeline.Count);
            for (int i = 0; i < _timeline.Count; i++)
            {
                var entry = _timeline[i];
                string id = i.ToString(CultureInfo.InvariantCulture);
                rows.Add(new PanelResultRow(id, When(entry.Time))
                {
                    Detail = Size(entry.Bytes),
                    Icon = RailIcons.History,
                    ActionId = TimelineCompareAction,
                    Value = id,
                    Buttons =
                    [
                        new PanelButton("Compare with now", TimelineCompareAction, PanelButtonKind.Flat) { Icon = RailIcons.Search, Value = id },
                        new PanelButton("Restore", TimelineRestoreAction, PanelButtonKind.Flat) { Icon = RailIcons.Refresh, Value = id },
                    ],
                });
            }

            var icon = FileIcons.For(path);
            blocks.Add(new PanelResults("timeline.list", rows.Count == 0 ? [] : [new PanelResultGroup(path, Path.GetFileName(path), rows)
            {
                Glyph = icon.Badge,
                GlyphArgb = icon.Argb,
                FileIcon = icon,
                Badge = rows.Count.ToString(CultureInfo.InvariantCulture),
                Tooltip = path,
            }])
            { Version = unchecked(++_timelineShown) });
        }

        TimelineView.SetBlocks(blocks);
    }

    private async Task ActOnTimelineAsync(string action, string? value, CancellationToken cancellationToken)
    {
        if (value is null || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= _timeline.Count)
        {
            return;
        }

        var entry = _timeline[index];
        if (action == TimelineRestoreAction)
        {
            await RestoreSnapshotAsync(entry, cancellationToken);
        }
        else if (action == TimelineCompareAction)
        {
            await CompareWithSnapshotAsync(entry, cancellationToken);
        }
    }

    // "Today 14:03", "Yesterday 09:12", "Oct 30 18:40", "2025 Oct 30 18:40" in the local time.
    private static string When(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var culture = CultureInfo.InvariantCulture;
        return local.Date == today ? "Today " + local.ToString("HH:mm:ss", culture)
            : local.Date == today.AddDays(-1) ? "Yesterday " + local.ToString("HH:mm", culture)
            : local.Year == today.Year ? local.ToString("MMM d HH:mm", culture) : local.ToString("yyyy MMM d HH:mm", culture);
    }

    private static string Size(long bytes) => bytes < 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
        : bytes < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");

    private void DisposeTimeline()
    {
        Host.ActiveDocumentChanged -= OnTimelineActiveChanged;
        _timelinePanel?.Dispose();
    }
}
