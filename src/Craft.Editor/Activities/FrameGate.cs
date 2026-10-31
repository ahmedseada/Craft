// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Activities;

/// <summary>
/// Turns requests from any thread into at most one call of an action on the UI thread per interval: a request while one
/// is pending joins it; a request sooner than the interval after the last call is delayed to the interval's end. The
/// action runs through the dispatcher, never inline from another thread (inline when the dispatcher itself runs inline).
/// </summary>
internal sealed class FrameGate
{
    private readonly Action<Action> _dispatch;
    private readonly Action _action;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly Lock _gate = new();
    private long _last = long.MinValue;
    private bool _pending;
    private ITimer? _timer;

    public FrameGate(Action<Action> dispatch, Action action, TimeSpan interval, TimeProvider? time = null)
    {
        _dispatch = dispatch;
        _action = action;
        _interval = interval;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How many times the action ran.</summary>
    public int Runs { get; private set; }

    /// <summary>Asks for the action (any thread).</summary>
    public void Request()
    {
        TimeSpan wait;
        lock (_gate)
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            long now = _time.GetTimestamp();
            wait = _last == long.MinValue ? TimeSpan.Zero : _interval - _time.GetElapsedTime(_last, now);
            if (wait > TimeSpan.Zero)
            {
                _timer?.Dispose();
                _timer = _time.CreateTimer(static state => ((FrameGate)state!).Post(), this, wait, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        Post();
    }

    /// <summary>A request after this many milliseconds (an activity that may outlast the minimum shows then).</summary>
    public ITimer RequestAfter(TimeSpan delay) => _time.CreateTimer(static state => ((FrameGate)state!).Request(), this, delay, Timeout.InfiniteTimeSpan);

    private void Post() => _dispatch(Run);

    private void Run()
    {
        lock (_gate)
        {
            _pending = false;
            _last = _time.GetTimestamp();
        }

        Runs++;
        _action();
    }
}
