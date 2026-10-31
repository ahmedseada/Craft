// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;

namespace Craft.Editor.Activities;

/// <summary>
/// The label of the UI work running now (phase U): every Craft dispatch runs under one (<see cref="Run"/>), and the
/// handlers of the window's own events that can take time (a key, a paint, a pointer) enter one (<see cref="Enter"/>),
/// so the watchdog names what blocked the window. Written on the UI thread, read by the watchdog's thread; nothing is
/// allocated per label.
/// </summary>
public sealed class UiWorkLabels
{
    private const int RecentCount = 16;
    private readonly string?[] _recent = new string?[RecentCount];
    private string? _current;
    private string? _last;
    private long _lastTicks;
    private int _next;

    /// <summary>The label of the UI work running now, or null when the UI thread is idle or runs unlabelled work.</summary>
    public string? Current => Volatile.Read(ref _current);

    /// <summary>The label of the last labelled work that ended, and how long it ran.</summary>
    public (string? Label, TimeSpan Duration) Last => (Volatile.Read(ref _last), TimeSpan.FromTicks(Volatile.Read(ref _lastTicks)));

    /// <summary>The last labels that ran, the newest first.</summary>
    public IReadOnlyList<string> Recent()
    {
        var list = new List<string>(RecentCount);
        int next = Volatile.Read(ref _next);
        for (int i = 1; i <= RecentCount; i++)
        {
            if (_recent[(next - i + RecentCount * 2) % RecentCount] is { } label)
            {
                list.Add(label);
            }
        }

        return list;
    }

    /// <summary>Runs <paramref name="action"/> under <paramref name="label"/> (on the UI thread).</summary>
    public void Run(string label, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var scope = Enter(label);
        action();
    }

    /// <summary>Enters <paramref name="label"/> until the scope is disposed (nested labels restore the outer one).</summary>
    public Scope Enter(string label)
    {
        var outer = Volatile.Read(ref _current);
        Volatile.Write(ref _current, label);
        int at = _next;
        _recent[at % RecentCount] = label;
        Volatile.Write(ref _next, (at + 1) % RecentCount);
        return new Scope(this, label, outer, Stopwatch.GetTimestamp());
    }

    private void Leave(string label, string? outer, long started)
    {
        Volatile.Write(ref _last, label);
        Volatile.Write(ref _lastTicks, Stopwatch.GetElapsedTime(started).Ticks);
        Volatile.Write(ref _current, outer);
    }

    /// <summary>A label entered: dispose it when the work ends.</summary>
#pragma warning disable CA1815 // A scope is never compared.
    public readonly struct Scope : IDisposable
    {
        private readonly UiWorkLabels? _labels;
        private readonly string _label;
        private readonly string? _outer;
        private readonly long _started;

        internal Scope(UiWorkLabels labels, string label, string? outer, long started)
        {
            _labels = labels;
            _label = label;
            _outer = outer;
            _started = started;
        }

        /// <inheritdoc />
        public void Dispose() => _labels?.Leave(_label, _outer, _started);
    }
#pragma warning restore CA1815
}

/// <summary>A stall of the UI thread the watchdog saw.</summary>
/// <param name="At">When it began.</param>
/// <param name="Duration">How long the UI thread did not answer.</param>
/// <param name="Label">The UI work running while it stalled (or the last that ran, when it ended between two looks).</param>
/// <param name="RecentWork">The UI work that ran before it, the newest first.</param>
public sealed record UiStall(DateTimeOffset At, TimeSpan Duration, string Label, IReadOnlyList<string> RecentWork)
{
    /// <summary>Full (generation 2) collections during the stall: a blocking collection is a stall's cause too.</summary>
    public int FullCollections { get; init; }

    /// <summary>The time the runtime paused every thread for collections during the stall.</summary>
    public TimeSpan CollectionPause { get; init; }

    /// <summary>The stall report's file (under the crash folder), or null when none was written.</summary>
    public string? ReportPath { get; init; }
}

/// <summary>
/// The UI watchdog (phase U): a background thread posts a tick through the dispatcher every <c>CRAFT_UI_WATCHDOG_MS</c>
/// and measures how late it runs. While a tick waits longer than <c>CRAFT_UI_STALL_MS</c> it notes the label of the UI
/// work running then (<see cref="UiWorkLabels"/>); when the tick runs, a stall that long is recorded, its report written
/// (<see cref="WriteReport"/>, on the watchdog's thread) and <see cref="Stalled"/> raised on the watchdog's thread (the
/// page shows it in the status bar and Output, and counts it in the telemetry tab, through the dispatcher). It never
/// touches the UI thread but for the tick, and it stops with <see cref="Dispose"/>.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    private const int KeptStalls = 50;
    private readonly Action<Action> _dispatch;
    private readonly UiWorkLabels _labels;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _stall;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Lock _gate = new();
    private readonly List<UiStall> _stalls = [];
    private readonly Action _tickAction;
    private Thread? _thread;
    private long _ran;
    private long _ticks;
    private long _worstTicks;
    private long _stallCount;

    /// <summary>A watchdog posting through <paramref name="dispatch"/> every <paramref name="tickMilliseconds"/>, reporting ticks later than <paramref name="stallMilliseconds"/>.</summary>
    public UiWatchdog(Action<Action> dispatch, UiWorkLabels labels, int tickMilliseconds, int stallMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stallMilliseconds);
        _dispatch = dispatch;
        _labels = labels;
        _tick = TimeSpan.FromMilliseconds(tickMilliseconds);
        _stall = TimeSpan.FromMilliseconds(stallMilliseconds);
        _tickAction = () => Volatile.Write(ref _ran, Stopwatch.GetTimestamp());
    }

    /// <summary>Writes a stall's report and gives its path (or null); called on the watchdog's thread. None by default.</summary>
    public Func<UiStall, string?>? WriteReport { get; init; }

    /// <summary>A stall was seen: raised on the watchdog's thread (dispatch to show it).</summary>
    public event EventHandler<UiStall>? Stalled;

    /// <summary>The ticks that ran.</summary>
    public long Ticks => Interlocked.Read(ref _ticks);

    /// <summary>The stalls seen.</summary>
    public long StallCount => Interlocked.Read(ref _stallCount);

    /// <summary>The latest a tick ran so far (the plan's "worst stall").</summary>
    public TimeSpan Worst => TimeSpan.FromTicks(Interlocked.Read(ref _worstTicks));

    /// <summary>The last stalls, the newest first.</summary>
    public IReadOnlyList<UiStall> Stalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _stalls];
            }
        }
    }

    /// <summary>Starts the watchdog's thread (once).</summary>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "Craft UI watchdog", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <inheritdoc />
    public void Dispose() => _stop.Set();       // the thread sees it at its next look and ends; nobody waits for it (the UI thread disposes it)

    // The watchdog's own thread: it sleeps on its stop signal (never on the UI) between looks.
    private void Loop()
    {
        while (!_stop.IsSet)
        {
            Volatile.Write(ref _ran, 0);
            long posted = Stopwatch.GetTimestamp();
            var at = DateTimeOffset.Now;
            int gen2 = GC.CollectionCount(2);
            var pause = GC.GetTotalPauseDuration();
            try
            {
                _dispatch(_tickAction);
            }
#pragma warning disable CA1031 // A dispatcher that is gone (the window closed) ends the watchdog, never the app.
            catch (Exception)
#pragma warning restore CA1031
            {
                return;
            }

            string? label = null;
            IReadOnlyList<string>? recent = null;
            long ran;
            while ((ran = Volatile.Read(ref _ran)) == 0)
            {
                if (_stop.Wait(_tick))
                {
                    return;
                }

                if (label is null && Stopwatch.GetElapsedTime(posted) >= _stall)
                {
                    // Blocked now: the work running is the one to name.
                    label = _labels.Current ?? "unlabelled UI work (a window event's handler, or a dispatch without a label)";
                    recent = _labels.Recent();
                }
            }

            Interlocked.Increment(ref _ticks);
            var late = Stopwatch.GetElapsedTime(posted, ran);
            if (late.Ticks > Interlocked.Read(ref _worstTicks))
            {
                Interlocked.Exchange(ref _worstTicks, late.Ticks);
            }

            if (late >= _stall)
            {
                if (label is null)
                {
                    // It ended between two looks: the work that ran last is the likely one.
                    var (last, took) = _labels.Last;
                    label = last is null ? "unlabelled UI work" : $"{last} ({ActivityText.Duration(took)})";
                    recent = _labels.Recent();
                }

                Report(new UiStall(at, late, label, recent ?? [])
                {
                    FullCollections = GC.CollectionCount(2) - gen2,
                    CollectionPause = GC.GetTotalPauseDuration() - pause,
                });
            }

            var rest = _tick - Stopwatch.GetElapsedTime(posted);
            if (rest > TimeSpan.Zero && _stop.Wait(rest))
            {
                return;
            }
        }
    }

    private void Report(UiStall stall)
    {
        Interlocked.Increment(ref _stallCount);
        try
        {
            if (WriteReport?.Invoke(stall) is { } path)
            {
                stall = stall with { ReportPath = path };
            }
        }
#pragma warning disable CA1031 // A report that cannot be written still shows.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        lock (_gate)
        {
            _stalls.Insert(0, stall);
            if (_stalls.Count > KeptStalls)
            {
                _stalls.RemoveAt(_stalls.Count - 1);
            }
        }

        Stalled?.Invoke(this, stall);
    }
}
