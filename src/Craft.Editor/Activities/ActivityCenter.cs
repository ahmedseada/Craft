// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Activities;

/// <summary>
/// The host's running operations (<see cref="IEditorActivities"/>, phase U): started, reported and ended on any thread
/// under one short lock (no work is done under it), shown through the dispatcher at most once per
/// <see cref="CraftOptions.ActivityFrameMilliseconds"/>. An activity that ends within
/// <see cref="CraftOptions.ActivityMinimumMilliseconds"/> is never shown and never kept (a failure always is); the
/// ended ones are kept, the newest first, up to <see cref="CraftOptions.NotificationsKeep"/>.
/// </summary>
public sealed class ActivityCenter : IEditorActivities
{
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly FrameGate _frame;
    private readonly Lock _gate = new();
    private readonly List<Activity> _running = [];
    private readonly List<(ActivitySnapshot Snapshot, Action? Retry)> _recent = [];
    private long _next;

    /// <summary>A centre showing through <paramref name="dispatch"/> (inline when null) with <paramref name="options"/>' limits.</summary>
    public ActivityCenter(Action<Action>? dispatch = null, CraftOptions? options = null, TimeProvider? time = null)
    {
        _dispatch = dispatch ?? (static action => action());
        _time = time ?? TimeProvider.System;
        var settings = options ?? new CraftOptions();
        Minimum = TimeSpan.FromMilliseconds(settings.ActivityMinimumMilliseconds);
        Keep = settings.NotificationsKeep;
        _frame = new FrameGate(_dispatch, () => Changed?.Invoke(this, EventArgs.Empty), TimeSpan.FromMilliseconds(settings.ActivityFrameMilliseconds), _time);
    }

    /// <summary>An activity that ends sooner than this is not shown (a failure aside).</summary>
    public TimeSpan Minimum { get; }

    /// <summary>The ended activities kept.</summary>
    public int Keep { get; }

    /// <summary>How many times <see cref="Changed"/> was raised (the tests check it is coalesced).</summary>
    public int ChangedCount => _frame.Runs;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>A kept activity ended (completed after the minimum, failed, or cancelled after the minimum): raised on the UI thread, once each.</summary>
    public event EventHandler<ActivitySnapshot>? Ended;

    /// <inheritdoc />
    public IReadOnlyList<ActivitySnapshot> Running
    {
        get
        {
            long now = _time.GetTimestamp();
            lock (_gate)
            {
                var all = new ActivitySnapshot[_running.Count];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = _running[_running.Count - 1 - i].Snapshot(now);
                }

                return all;
            }
        }
    }

    /// <summary>The running ones that have run at least <see cref="Minimum"/> (what the status bar shows), the newest first.</summary>
    public IReadOnlyList<ActivitySnapshot> Visible
    {
        get
        {
            long now = _time.GetTimestamp();
            lock (_gate)
            {
                var shown = new List<ActivitySnapshot>(_running.Count);
                for (int i = _running.Count - 1; i >= 0; i--)
                {
                    var activity = _running[i];
                    if (_time.GetElapsedTime(activity.StartedTicks, now) >= Minimum)
                    {
                        shown.Add(activity.Snapshot(now));
                    }
                }

                return shown;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivitySnapshot> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent.Select(r => r.Snapshot)];
            }
        }
    }

    /// <inheritdoc />
    public IActivity Start(string title, string kind = ActivityKinds.General, bool cancellable = false, CancellationToken cancellationToken = default, bool keep = true) =>
        Start(title, kind, cancellable, cancellationToken, keep, EditorHost.HostSource);

    /// <summary><see cref="Start(string, string, bool, CancellationToken, bool)"/> under <paramref name="source"/> (a plug-in's name).</summary>
    public IActivity Start(string title, string kind, bool cancellable, CancellationToken cancellationToken, bool keep, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var activity = new Activity(this, Interlocked.Increment(ref _next), title, string.IsNullOrWhiteSpace(kind) ? ActivityKinds.General : kind, source ?? EditorHost.HostSource,
            cancellable, cancellationToken, keep, _time.GetTimestamp(), _time.GetUtcNow());
        lock (_gate)
        {
            _running.Add(activity);
        }

        // Shown once it has run the minimum (sooner, nothing would show but a flicker).
        activity.ShowTimer = Minimum > TimeSpan.Zero ? _frame.RequestAfter(Minimum) : null;
        _frame.Request();
        return activity;
    }

    /// <inheritdoc />
    public bool Cancel(long id)
    {
        Activity? found;
        lock (_gate)
        {
            found = _running.Find(a => a.Id == id);
        }

        return found is not null && found.RequestCancel();
    }

    /// <inheritdoc />
    public bool Retry(long id)
    {
        Action? retry = null;
        lock (_gate)
        {
            int at = _recent.FindIndex(r => r.Snapshot.Id == id);
            if (at >= 0 && _recent[at].Retry is { } found)
            {
                retry = found;
                _recent[at] = (_recent[at].Snapshot with { CanRetry = false }, null);
            }
        }

        if (retry is null)
        {
            return false;
        }

        _dispatch(retry);
        _frame.Request();
        return true;
    }

    /// <summary>Cancels every running activity of <paramref name="source"/> and ends it (a plug-in that was deactivated).</summary>
    public void EndAll(string source, string why)
    {
        Activity[] of;
        lock (_gate)
        {
            of = [.. _running.Where(a => a.Source == source)];
        }

        foreach (var activity in of)
        {
            activity.RequestCancel();
            activity.End(ActivityState.Cancelled, why, null, null);
        }
    }

    internal void Changed_() => _frame.Request();

    internal void OnEnded(Activity activity, ActivitySnapshot snapshot, Action? retry)
    {
        bool kept;
        lock (_gate)
        {
            _running.Remove(activity);
            kept = snapshot.Keep && (snapshot.State == ActivityState.Failed || snapshot.Elapsed >= Minimum);
            if (kept && Keep > 0)
            {
                _recent.Insert(0, (snapshot, snapshot.State == ActivityState.Failed ? retry : null));
                if (_recent.Count > Keep)
                {
                    _recent.RemoveRange(Keep, _recent.Count - Keep);
                }
            }
        }

        activity.ShowTimer?.Dispose();
        if (kept && Ended is { } ended)
        {
            _dispatch(() => ended(this, snapshot));
        }

        _frame.Request();
    }

    internal TimeSpan Elapsed(long startedTicks) => _time.GetElapsedTime(startedTicks);

    /// <summary>One activity: its state under the centre's lock, its cancellation its own.</summary>
    internal sealed class Activity : IActivity
    {
        private readonly ActivityCenter _center;
        private readonly CancellationTokenSource? _cancel;
        private double? _fraction;
        private string? _text;
        private ActivityRate? _rate;
        private int _ended;
        private int _global;

        public Activity(ActivityCenter center, long id, string title, string kind, string source, bool cancellable, CancellationToken token, bool keep, long startedTicks, DateTimeOffset started)
        {
            _center = center;
            Id = id;
            Title = title;
            Kind = kind;
            Source = source;
            Cancellable = cancellable;
            KeepWhenEnded = keep;
            StartedTicks = startedTicks;
            Started = started;
            if (cancellable)
            {
                _cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                CancellationToken = _cancel.Token;
            }
            else
            {
                CancellationToken = token;
            }
        }

        public long Id { get; }

        public string Title { get; }

        public string Kind { get; }

        public string Source { get; }

        public bool Cancellable { get; }

        public bool KeepWhenEnded { get; }

        public long StartedTicks { get; }

        public DateTimeOffset Started { get; }

        public ITimer? ShowTimer { get; set; }

        public CancellationToken CancellationToken { get; }

        public bool IsEnded => Volatile.Read(ref _ended) != 0;

        public bool Global
        {
            get => Volatile.Read(ref _global) != 0;
            set
            {
                if (Interlocked.Exchange(ref _global, value ? 1 : 0) != (value ? 1 : 0) && !IsEnded)
                {
                    _center.Changed_();                       // the loader takes it in (or lets it go) at the next frame
                }
            }
        }

        public void Report(double? fraction, string? text = null, ActivityRate? rate = null)
        {
            if (IsEnded)
            {
                return;
            }

            lock (_center._gate)
            {
                _fraction = fraction is { } f && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : null;
                if (text is not null)
                {
                    _text = text;
                }

                if (rate is not null)
                {
                    _rate = rate;
                }
            }

            _center.Changed_();
        }

        public void Complete(string? outcome = null) => End(ActivityState.Completed, outcome, null, null);

        public void Fail(string reason, Exception? error = null, Action? retry = null)
        {
            if (error is OperationCanceledException && CancellationToken.IsCancellationRequested)
            {
                End(ActivityState.Cancelled, "cancelled", null, null);
                return;
            }

            End(ActivityState.Failed, null, !string.IsNullOrWhiteSpace(reason) ? reason : Describe(error), retry);
        }

        // A failure always says something: its message, else its type and code (a platform error may carry no text).
        private static string Describe(Exception? error) => error switch
        {
            null => "failed (no reason given)",
            { Message: { } message } when !string.IsNullOrWhiteSpace(message) => message.Trim(),
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{error.GetType().FullName} (0x{error.HResult:X8}) with no message"),
        };

        public void Dispose()
        {
            if (!IsEnded)
            {
                if (CancellationToken.IsCancellationRequested)
                {
                    End(ActivityState.Cancelled, "cancelled", null, null);
                }
                else
                {
                    End(ActivityState.Completed, null, null, null);
                }
            }
        }

        public bool RequestCancel()
        {
            if (_cancel is null || IsEnded)
            {
                return false;
            }

            // Cancelled off the UI thread: the token's callbacks (the work's own) run on the thread pool.
            try
            {
                _ = _cancel.CancelAsync();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;                      // it ended meanwhile
            }
        }

        public void End(ActivityState state, string? outcome, string? error, Action? retry)
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0)
            {
                return;
            }

            ActivitySnapshot snapshot;
            lock (_center._gate)
            {
                if (outcome is not null)
                {
                    _text = outcome;
                }

                if (state == ActivityState.Completed && _fraction is not null)
                {
                    _fraction = 1;
                }

                snapshot = SnapshotLocked(_center._time.GetTimestamp(), state) with { Error = error, CanRetry = retry is not null, Left = null };
            }

            _center.OnEnded(this, snapshot, retry);
            if (_cancel is { IsCancellationRequested: false })
            {
                _cancel.Dispose();                 // a cancelled one may still run its callbacks (CancelAsync): left to the collector
            }
        }

        public ActivitySnapshot Snapshot(long now) => SnapshotLocked(now, ActivityState.Running);

        // Called holding the centre's lock.
        private ActivitySnapshot SnapshotLocked(long now, ActivityState state)
        {
            var elapsed = _center._time.GetElapsedTime(StartedTicks, now);
            TimeSpan? left = null;
            if (state == ActivityState.Running && _fraction is { } f && f >= 0.01 && f < 1 && elapsed >= TimeSpan.FromSeconds(1))
            {
                left = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - f) / f);
            }

            return new ActivitySnapshot(Id, Title, Kind, Source, state, _fraction, _text, _rate, Started, elapsed, Cancellable && state == ActivityState.Running)
            {
                Left = left,
                Keep = KeepWhenEnded,
                Global = Global,
            };
        }
    }
}
