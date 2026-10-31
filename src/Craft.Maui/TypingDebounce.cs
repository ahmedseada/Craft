// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Maui;

/// <summary>
/// A search or filter box's debounce: <see cref="Restart"/> on each keystroke, and the search runs once, on the UI thread,
/// when <see cref="Core.CraftOptions.SearchDelayMilliseconds"/> passed without another (0 runs it at once). So typing never
/// waits on a search that rebuilds a page.
/// </summary>
internal sealed class TypingDebounce : IDisposable
{
    private readonly BindableObject _owner;
    private readonly Func<int> _delay;
    private readonly Action _action;
    private IDispatcherTimer? _timer;

    /// <summary>Runs <paramref name="action"/> on <paramref name="owner"/>'s dispatcher, <paramref name="delay"/> ms after the last restart.</summary>
    public TypingDebounce(BindableObject owner, Func<int> delay, Action action)
    {
        _owner = owner;
        _delay = delay;
        _action = action;
    }

    /// <summary>Starts the wait again (a keystroke).</summary>
    public void Restart()
    {
        int milliseconds = _delay();
        _timer?.Stop();
        if (milliseconds <= 0 || _owner.Dispatcher is not { } dispatcher)
        {
            _action();
            return;
        }

        if (_timer is null)
        {
            _timer = dispatcher.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                _action();
            };
        }

        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.Start();
    }

    /// <summary>Runs a waiting search now (Enter), or nothing when none waits.</summary>
    public void Flush()
    {
        if (_timer is { IsRunning: true } timer)
        {
            timer.Stop();
            _action();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Stop();
}
