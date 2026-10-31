// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Idrak.Abstraction.Diagnostics;

namespace Craft.Plugins.Telemetry;

/// <summary>
/// Idrak's progress reporter "editor" (in <see cref="ProgressReporters"/>, a new kind): each piece of work a library or a
/// plug-in starts with <c>ProgressReporters.Start(label, total, unit, new ProgressOptions { Reporter = "editor" })</c>
/// shows in the editor's status bar (the host's <see cref="IEditorHost.StartProgress"/>; phase U's activities take that
/// over) as the library's own line ("pull model.gguf  45%  1.2 GB / 2.7 GB  31 MB/s  48s left"), the share done when the
/// total is known, at most once per <c>progressMs</c>, and is taken off when it ends. With no editor plug-in active (the
/// idrak tool loading this assembly) a reporter counts only, like the library's "quiet".
/// </summary>
public sealed class EditorProgressReporterKind : IProgressReporterKind
{
    /// <summary>The reporter's name.</summary>
    public const string ReporterName = "editor";

    private static readonly Lock Gate = new();
    private static (IEditorHost Host, TimeSpan Interval)? s_target;

    private EditorProgressReporterKind()
    {
    }

    /// <summary>The kind.</summary>
    public static EditorProgressReporterKind Instance { get; } = new();

    /// <inheritdoc />
    public string Name => ReporterName;

    /// <inheritdoc />
    public string Summary => "The editor's status bar: the stage, the share done, the speed and the time left (the telemetry plug-in's)";

    /// <inheritdoc />
    public IReadOnlySet<string> Keys { get; } = new HashSet<string>();

    /// <summary>The host the reporters show in now, or null.</summary>
    internal static IEditorHost? Host
    {
        get
        {
            lock (Gate)
            {
                return s_target?.Host;
            }
        }
    }

    /// <inheritdoc />
    public IProgressReporter Create(ProgressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        (IEditorHost Host, TimeSpan Interval)? target;
        lock (Gate)
        {
            target = s_target;
        }

        return new EditorProgressReporter(request, target?.Host, request.Interval ?? target?.Interval ?? TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Shows the reporters made from now on in <paramref name="host"/> until the result is disposed.</summary>
    internal static IDisposable Attach(IEditorHost host, TimeSpan interval)
    {
        lock (Gate)
        {
            s_target = (host, interval);
        }

        return new Detach(host);
    }

    private sealed class Detach(IEditorHost host) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                if (s_target is { } current && ReferenceEquals(current.Host, host))
                {
                    s_target = null;
                }
            }
        }
    }
}

/// <summary>
/// One piece of work in the status bar. Its amounts are set from any thread without a lock (a field write); its line is
/// made only when it is shown (at most once per interval, the time so far redrawn on a timer for work measured in time),
/// and reaches the host on its UI thread through <see cref="IEditorHost.Dispatch"/>.
/// </summary>
internal sealed class EditorProgressReporter : IProgressReporter
{
    private readonly IEditorHost? _host;
    private readonly long _interval;
    private readonly Lock _gate = new();
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private IEditorProgress? _progress;   // the UI thread's
    private Timer? _ticker;
    private long _done;
    private long _shownAt = long.MinValue;
    private bool _ended;

    public EditorProgressReporter(ProgressRequest request, IEditorHost? host, TimeSpan interval)
    {
        Label = request.Label;
        Total = request.Total;
        Unit = request.Unit;
        _host = host;
        _interval = Math.Max(1, (long)(interval.TotalSeconds * Stopwatch.Frequency));
        Tick();
        Show();
    }

    public string Label { get; set; }

    public long? Total { get; set; }

    public ProgressUnit Unit { get; set; }

    public long Done => Interlocked.Read(ref _done);

    public bool Enabled => _host is not null;

    public Func<string>? Detail { get; set; }

    /// <summary>The last line shown (the tests read it), or null.</summary>
    public string? Shown { get; private set; }

    public void Report(long value)
    {
        Interlocked.Exchange(ref _done, value);
        Draw(force: false);
    }

    public void Advance(long amount = 1)
    {
        Interlocked.Add(ref _done, amount);
        Draw(force: false);
    }

    public void Show() => Draw(force: true);

    public void NextStage(string label, long? total, ProgressUnit unit, string? message = null, bool keepLine = true)
    {
        ArgumentNullException.ThrowIfNull(label);
        lock (_gate)
        {
            if (keepLine && _host is not null)
            {
                string kept = message ?? Line();
                _host.Dispatch(() => _host.Log(EditorLogLevel.Information, kept));
            }

            Label = label;
            Total = total;
            Unit = unit;
            Interlocked.Exchange(ref _done, 0);
            _watch.Restart();
        }

        Tick();
        Show();
    }

    public void Erase()
    {
        // A status bar item is never in the way of other output.
    }

    public void Finish(string? message = null)
    {
        string? last;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            last = message ?? Line();
            Shown = last;
        }

        End(last);
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
        }

        End(null);
    }

    public void Dispose() => Clear();

    // The library's own text for the state, with the detail when there is one.
    private string Line()
    {
        string line = ProgressText.Format(Label, Done, Total, _watch.Elapsed, Unit);
        return Detail is { } detail ? line + "  " + detail() : line;
    }

    private void Draw(bool force)
    {
        long now = Stopwatch.GetTimestamp();
        long shown = Interlocked.Read(ref _shownAt);
        if (!force && now - shown < _interval || _host is null)
        {
            return;                                                  // between showings: a field write and a compare
        }

        string line;
        double? fraction;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            Interlocked.Exchange(ref _shownAt, now);
            line = Line();
            Shown = line;
            fraction = Unit != ProgressUnit.Elapsed && Total is > 0 ? Math.Clamp(Done / (double)Total.Value, 0, 1) : null;
        }

        string title = Label;
        _host.Dispatch(() =>
        {
            if (_progress is null && !_ended)
            {
                _progress = _host.StartProgress(title);
            }

            _progress?.Report(fraction, line);
        });
    }

    // Work measured in time redraws its time so far on its own; other work redraws when it reports.
    private void Tick()
    {
        lock (_gate)
        {
            _ticker?.Dispose();
            _ticker = Unit == ProgressUnit.Elapsed && _host is not null && !_ended
                ? new Timer(_ => Draw(force: true), null, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5))
                : null;
        }
    }

    private void End(string? last)
    {
        lock (_gate)
        {
            _ticker?.Dispose();
            _ticker = null;
        }

        if (_host is null)
        {
            return;
        }

        _host.Dispatch(() =>
        {
            if (last is not null)
            {
                _host.Log(EditorLogLevel.Information, last);
            }

            _progress?.Dispose();
            _progress = null;
        });
    }
}
