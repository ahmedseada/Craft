// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Shell;

/// <summary>What the inline bar says after a change that can be taken back, and how to take it back.</summary>
/// <param name="Id">Its number (a newer bar takes the place of an older one).</param>
/// <param name="Text">What happened ("Cleared 5 notifications.").</param>
public sealed record UndoBarEntry(long Id, string Text)
{
    internal Action Undo { get; init; } = static () => { };

    internal long Deadline { get; init; }
}

/// <summary>
/// The inline bar with Undo, in place of a confirmation dialog for a change that can be taken back (the notification
/// centre cleared, a recent folder removed): the change is made at once, the bar says so with Undo for
/// <c>CRAFT_UNDO_BAR_MS</c>, then the change stays. One bar at a time: a new change keeps the one before (its Undo is
/// gone) and shows its own. Used on the UI thread; <see cref="Changed"/> is raised there.
/// </summary>
public sealed class UndoBar : IDisposable
{
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private long _next;
    private bool _disposed;

    /// <summary>A bar that shows for <paramref name="milliseconds"/>, its expiry dispatched through <paramref name="dispatch"/> (inline when null).</summary>
    public UndoBar(int milliseconds, Action<Action>? dispatch = null, TimeProvider? time = null)
    {
        Milliseconds = Math.Max(1, milliseconds);
        _dispatch = dispatch ?? (static action => action());
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How long a bar shows (<c>CRAFT_UNDO_BAR_MS</c>).</summary>
    public int Milliseconds { get; set; }

    /// <summary>The bar shown, or null.</summary>
    public UndoBarEntry? Current { get; private set; }

    /// <summary>Raised when a bar shows, is undone or goes.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows <paramref name="text"/> with Undo running <paramref name="undo"/> (the change is already made).</summary>
    public UndoBarEntry Show(string text, Action undo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(undo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = new UndoBarEntry(++_next, text)
        {
            Undo = undo,
            Deadline = _time.GetTimestamp() + (long)(Milliseconds * (double)_time.TimestampFrequency / 1000),
        };
        Current = entry;
        _timer?.Dispose();
        _timer = _time.CreateTimer(_ => _dispatch(() => ExpireIfDue()), null, TimeSpan.FromMilliseconds(Milliseconds), Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>Takes the shown change back (its Undo runs) and hides the bar; false when none shows.</summary>
    public bool Undo()
    {
        if (Current is not { } entry)
        {
            return false;
        }

        Hide();
        entry.Undo();
        return true;
    }

    /// <summary>Hides the bar and keeps the change.</summary>
    public void Dismiss()
    {
        if (Current is not null)
        {
            Hide();
        }
    }

    /// <summary>Hides the bar when its time is up (the timer calls it; a test calls it after moving the clock); true when it went.</summary>
    public bool ExpireIfDue()
    {
        if (Current is not { } entry || _time.GetTimestamp() < entry.Deadline)
        {
            return false;
        }

        Hide();
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        Current = null;
    }

    private void Hide()
    {
        Current = null;
        _timer?.Dispose();
        _timer = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
