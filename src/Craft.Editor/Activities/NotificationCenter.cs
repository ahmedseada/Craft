// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Activities;

/// <summary>What a notification centre entry is.</summary>
public enum NotificationKind
{
    /// <summary>An operation (<see cref="IEditorActivities"/>): running, or how it ended.</summary>
    Activity,

    /// <summary>A message posted (<see cref="IEditorHost.Notify"/>).</summary>
    Message,

    /// <summary>A sticky status line of a source (<see cref="IEditorHost.ShowStatus"/>): a model's state, the device, a sandbox's last command.</summary>
    Status,
}

/// <summary>One entry of the notification centre.</summary>
/// <param name="Id">Its number (an activity's entry has the activity's <see cref="ActivitySnapshot.Id"/> in <see cref="Activity"/>).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Source">Who it is from (a plug-in's name, "host", "watchdog").</param>
/// <param name="Title">Its title.</param>
/// <param name="Text">Its text (an activity's progress or outcome, a failure's reason).</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Time">When it was posted (or the activity started).</param>
public sealed record NotificationEntry(long Id, NotificationKind Kind, string Source, string Title, string Text, NotificationSeverity Severity, DateTimeOffset Time)
{
    /// <summary>The activity, for an activity's entry (live while it runs).</summary>
    public ActivitySnapshot? Activity { get; init; }

    /// <summary>How long it took, for an ended activity.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Its buttons (Retry, Open log, a plug-in's own).</summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

    /// <summary>Whether the user has seen it (opened the centre since it came); the bell counts the rest.</summary>
    public bool Seen { get; init; }

    /// <summary>Whether it is running (an activity that has not ended).</summary>
    public bool IsRunning => Activity is { State: ActivityState.Running };
}

/// <summary>A source's entries, as the centre groups them: the running first, then the status lines, then the rest, newest first.</summary>
/// <param name="Source">The source.</param>
/// <param name="Entries">Its entries.</param>
public sealed record NotificationGroup(string Source, IReadOnlyList<NotificationEntry> Entries);

/// <summary>
/// The notification centre (phase U, the owner's request): every operation (<see cref="ActivityCenter"/>: running ones
/// with their live progress and Cancel, ended ones with their outcome and time taken, failures with their reason and
/// Retry) and every message and status line plug-ins post, grouped by source, kept for the session (at most
/// <see cref="CraftOptions.NotificationsKeep"/> messages and ended operations), each dismissable. The bell counts what
/// the user has not seen; a failure or a question also raises <see cref="Toast"/>. Any thread; <see cref="Changed"/>
/// and <see cref="Toast"/> are raised on the UI thread, <see cref="Changed"/> at most once a frame.
/// </summary>
public sealed class NotificationCenter : IDisposable
{
    /// <summary>The source of the UI watchdog's entries.</summary>
    public const string WatchdogSource = "watchdog";

    private readonly ActivityCenter _activities;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly FrameGate _frame;
    private readonly Lock _gate = new();
    private readonly List<NotificationEntry> _entries = [];                 // messages and ended activities, newest first
    private readonly List<NotificationEntry> _statuses = [];                // sticky, in the order they came
    private long _next = 1L << 40;                                          // apart from the activities' numbers
    private bool _disposed;

    /// <summary>A centre over <paramref name="activities"/>, showing through <paramref name="dispatch"/> (inline when null).</summary>
    public NotificationCenter(ActivityCenter activities, Action<Action>? dispatch = null, CraftOptions? options = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(activities);
        _activities = activities;
        _dispatch = dispatch ?? (static action => action());
        _time = time ?? TimeProvider.System;
        var settings = options ?? new CraftOptions();
        Keep = settings.NotificationsKeep;
        _frame = new FrameGate(_dispatch, () => Changed?.Invoke(this, EventArgs.Empty), TimeSpan.FromMilliseconds(settings.ActivityFrameMilliseconds), _time);
        activities.Changed += OnActivitiesChanged;
        activities.Ended += OnActivityEnded;
    }

    /// <summary>The messages and ended operations kept.</summary>
    public int Keep { get; }

    /// <summary>What the page does for a failure's "Open log" (shows the Output tab); null leaves the button out.</summary>
    public Action? OpenLog { get; set; }

    /// <summary>Something changed: raised on the UI thread, at most once a frame.</summary>
    public event EventHandler? Changed;

    /// <summary>An entry that needs the user (a failure, a question): raised on the UI thread, once each.</summary>
    public event EventHandler<NotificationEntry>? Toast;

    /// <summary>The bell's count: the messages and ended operations not seen yet (status lines never count).</summary>
    public int Unseen
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count(e => !e.Seen);
            }
        }
    }

    /// <summary>Every entry: the running operations (newest first), the status lines, then the messages and ended operations (newest first).</summary>
    public IReadOnlyList<NotificationEntry> Entries
    {
        get
        {
            var running = _activities.Running.Select(RunningEntry);
            lock (_gate)
            {
                return [.. running, .. _statuses, .. _entries];
            }
        }
    }

    /// <summary>The entries by source, the source with the newest entry first.</summary>
    public IReadOnlyList<NotificationGroup> Groups
    {
        get
        {
            var all = Entries;
            var order = new List<string>();
            var bySource = new Dictionary<string, List<NotificationEntry>>(StringComparer.Ordinal);
            foreach (var entry in all.OrderBy(e => e.IsRunning ? 0 : e.Kind == NotificationKind.Status ? 1 : 2).ThenByDescending(e => e.Time))
            {
                if (!bySource.TryGetValue(entry.Source, out var list))
                {
                    bySource[entry.Source] = list = [];
                    order.Add(entry.Source);
                }

                list.Add(entry);
            }

            return [.. order.Select(source => new NotificationGroup(source, bySource[source]))];
        }
    }

    /// <summary>Posts a message under <paramref name="source"/>; an error or a question also toasts.</summary>
    public IEditorNotification Post(string source, string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var entry = new NotificationEntry(Interlocked.Increment(ref _next), NotificationKind.Message, Source(source), title, text ?? "", severity, _time.GetLocalNow())
        {
            Actions = WithLog(actions ?? [], severity),
        };
        lock (_gate)
        {
            if (_disposed)
            {
                return NoNotification.Instance;
            }

            Add(entry);
        }

        if (severity is NotificationSeverity.Error or NotificationSeverity.Question)
        {
            RaiseToast(entry);
        }

        _frame.Request();
        return new Handle(this, entry.Id);
    }

    /// <summary>
    /// Keeps an operation that already ended before the centre could see it start (a plug-in's load at start-up, before
    /// the window): its outcome and time taken, seen (the bell does not count it) unless it failed, which also toasts.
    /// </summary>
    public void AddEnded(string source, string title, string text, NotificationSeverity severity, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        bool failed = severity == NotificationSeverity.Error;
        var entry = new NotificationEntry(Interlocked.Increment(ref _next), NotificationKind.Activity, Source(source), title, text ?? "", severity, _time.GetLocalNow() - duration)
        {
            Duration = duration,
            Seen = !failed,
            Actions = WithLog([], severity),
        };
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Add(entry);
        }

        if (failed)
        {
            RaiseToast(entry);
        }

        _frame.Request();
    }

    /// <summary>Shows a sticky status line under <paramref name="source"/>, updated in place by the result, gone when it is disposed.</summary>
    public IEditorNotification ShowStatus(string source, string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var entry = new NotificationEntry(Interlocked.Increment(ref _next), NotificationKind.Status, Source(source), title, text ?? "", severity, _time.GetLocalNow())
        {
            Actions = actions ?? [],
            Seen = true,
        };
        lock (_gate)
        {
            if (_disposed)
            {
                return NoNotification.Instance;
            }

            _statuses.Add(entry);
        }

        _frame.Request();
        return new Handle(this, entry.Id);
    }

    /// <summary>The status lines of <paramref name="source"/> (U2's Models page reads the model's).</summary>
    public IReadOnlyList<NotificationEntry> StatusOf(string source)
    {
        lock (_gate)
        {
            return [.. _statuses.Where(s => s.Source == source)];
        }
    }

    /// <summary>Marks every entry seen (the centre was opened): the bell's count goes to 0.</summary>
    public void MarkSeen()
    {
        bool changed = false;
        lock (_gate)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!_entries[i].Seen)
                {
                    _entries[i] = _entries[i] with { Seen = true };
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _frame.Request();
        }
    }

    /// <summary>Takes entry <paramref name="id"/> away (a message, an ended operation, a status line); false for a running operation (cancel it instead) or one not there.</summary>
    public bool Dismiss(long id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => e.Id == id) + _statuses.RemoveAll(e => e.Id == id) > 0;
        }

        if (removed)
        {
            _frame.Request();
        }

        return removed;
    }

    /// <summary>Takes away every message and ended operation of <paramref name="source"/> (every source when null); running ones and status lines stay.</summary>
    public int DismissAll(string? source = null)
    {
        int removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => source is null || e.Source == source);
        }

        if (removed > 0)
        {
            _frame.Request();
        }

        return removed;
    }

    /// <summary>
    /// As <see cref="DismissAll"/>, returning what it took away (newest first), so an inline Undo can put it back with
    /// <see cref="Restore"/>.
    /// </summary>
    public IReadOnlyList<NotificationEntry> TakeAll(string? source = null)
    {
        NotificationEntry[] taken;
        lock (_gate)
        {
            taken = [.. _entries.Where(e => source is null || e.Source == source)];
            _entries.RemoveAll(e => source is null || e.Source == source);
        }

        if (taken.Length > 0)
        {
            _frame.Request();
        }

        return taken;
    }

    /// <summary>Puts back entries <see cref="TakeAll"/> took (newer ones that came meanwhile stay; the list keeps its newest first and its limit).</summary>
    public void Restore(IReadOnlyList<NotificationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var entry in entries)
            {
                if (!_entries.Exists(e => e.Id == entry.Id))
                {
                    _entries.Add(entry);
                }
            }

            _entries.Sort(static (a, b) => b.Time != a.Time ? b.Time.CompareTo(a.Time) : b.Id.CompareTo(a.Id));
            if (_entries.Count > Keep)
            {
                _entries.RemoveRange(Keep, _entries.Count - Keep);
            }
        }

        _frame.Request();
    }

    /// <summary>Takes away the status lines of <paramref name="source"/> (a plug-in that was deactivated).</summary>
    public void RemoveStatuses(string source)
    {
        int removed;
        lock (_gate)
        {
            removed = _statuses.RemoveAll(e => e.Source == source);
        }

        if (removed > 0)
        {
            _frame.Request();
        }
    }

    /// <summary>Runs <paramref name="action"/> of entry <paramref name="id"/> (on the UI thread, where it is called); a failure is posted, never thrown.</summary>
    public async Task RunAsync(long id, NotificationAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action.RunAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // A button's failure is shown in the centre; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Post(EditorHost.HostSource, $"'{action.Label}' failed", error.Message, NotificationSeverity.Error);
        }

        if (action.Dismisses)
        {
            Dismiss(id);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _activities.Changed -= OnActivitiesChanged;
        _activities.Ended -= OnActivityEnded;
    }

    internal void Update(long id, string text, NotificationSeverity? severity)
    {
        NotificationEntry? toast = null;
        lock (_gate)
        {
            int at = _statuses.FindIndex(e => e.Id == id);
            if (at >= 0)
            {
                _statuses[at] = _statuses[at] with { Text = text ?? "", Severity = severity ?? _statuses[at].Severity };
            }
            else if ((at = _entries.FindIndex(e => e.Id == id)) >= 0)
            {
                var before = _entries[at];
                var after = before with { Text = text ?? "", Severity = severity ?? before.Severity, Actions = WithLog(before.Actions, severity ?? before.Severity) };
                _entries[at] = after;
                if (severity is NotificationSeverity.Error or NotificationSeverity.Question && before.Severity != severity)
                {
                    toast = after;
                }
            }
            else
            {
                return;
            }
        }

        if (toast is not null)
        {
            RaiseToast(toast);
        }

        _frame.Request();
    }

    private static string Source(string? source) => string.IsNullOrWhiteSpace(source) ? EditorHost.HostSource : source;

    private static NotificationEntry RunningEntry(ActivitySnapshot activity) =>
        new(activity.Id, NotificationKind.Activity, activity.Source, activity.Title, ActivityText.Progress(activity), NotificationSeverity.Information, activity.Started)
        {
            Activity = activity,
            Seen = true,
        };

    // Called holding the gate: newest first, the oldest dropped past the limit.
    private void Add(NotificationEntry entry)
    {
        if (Keep == 0)
        {
            return;
        }

        _entries.Insert(0, entry);
        if (_entries.Count > Keep)
        {
            _entries.RemoveRange(Keep, _entries.Count - Keep);
        }
    }

    private IReadOnlyList<NotificationAction> WithLog(IReadOnlyList<NotificationAction> actions, NotificationSeverity severity)
    {
        if (severity != NotificationSeverity.Error || OpenLog is not { } open || actions.Any(a => a.Label == ActivityText.OpenLog))
        {
            return actions;
        }

        return [.. actions, new NotificationAction(ActivityText.OpenLog, _ =>
        {
            open();
            return Task.CompletedTask;
        }, Dismisses: false)];
    }

    private void RaiseToast(NotificationEntry entry)
    {
        if (Toast is not null)
        {
            _dispatch(() => Toast?.Invoke(this, entry));
        }
    }

    private void OnActivitiesChanged(object? sender, EventArgs e) => _frame.Request();

    private void OnActivityEnded(object? sender, ActivitySnapshot activity)
    {
        var severity = activity.State switch
        {
            ActivityState.Failed => NotificationSeverity.Error,
            ActivityState.Cancelled => NotificationSeverity.Warning,
            _ => NotificationSeverity.Success,
        };
        var actions = new List<NotificationAction>(2);
        if (activity.CanRetry)
        {
            long id = activity.Id;
            actions.Add(new NotificationAction(ActivityText.Retry, _ =>
            {
                _activities.Retry(id);
                return Task.CompletedTask;
            }, Primary: true));
        }

        var entry = new NotificationEntry(Interlocked.Increment(ref _next), NotificationKind.Activity, activity.Source, activity.Title, ActivityText.Outcome(activity), severity, activity.Started)
        {
            Activity = activity,
            Duration = activity.Elapsed,
            Actions = WithLog(actions, severity),
        };
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Add(entry);
        }

        if (severity == NotificationSeverity.Error)
        {
            RaiseToast(entry);
        }

        _frame.Request();
    }

    private sealed class Handle(NotificationCenter center, long id) : IEditorNotification
    {
        private int _disposed;

        public long Id { get; } = id;

        public void Update(string text, NotificationSeverity? severity = null)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                center.Update(Id, text, severity);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                center.Dismiss(Id);
            }
        }
    }
}
