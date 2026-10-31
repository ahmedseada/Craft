// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>How much a notification matters: an error or a question also shows a toast (it needs the user); the rest waits in the centre.</summary>
public enum NotificationSeverity
{
    /// <summary>Something that happened.</summary>
    Information,

    /// <summary>Something that ended well.</summary>
    Success,

    /// <summary>Something that went wrong and was worked around.</summary>
    Warning,

    /// <summary>Something that failed (a toast shows it).</summary>
    Error,

    /// <summary>Something the user is asked (a toast shows it; its actions are the answers).</summary>
    Question,
}

/// <summary>A button of a notification: its label and what it runs (on the UI thread); <paramref name="Dismisses"/> takes the notification away after it ran.</summary>
/// <param name="Label">What the button says ("Open log", "Retry").</param>
/// <param name="RunAsync">What it runs.</param>
/// <param name="Primary">Whether it is the main action (drawn in the accent).</param>
/// <param name="Dismisses">Whether the notification goes once it ran.</param>
public sealed record NotificationAction(string Label, Func<CancellationToken, Task> RunAsync, bool Primary = false, bool Dismisses = true);

/// <summary>
/// A notification a plug-in posted (<see cref="IEditorHost.Notify"/>) or a status entry it keeps
/// (<see cref="IEditorHost.ShowStatus"/>): update it in place; dispose it to take it away. Any thread.
/// </summary>
public interface IEditorNotification : IDisposable
{
    /// <summary>Its number in the session.</summary>
    long Id { get; }

    /// <summary>Changes its text (and its severity when given), in place.</summary>
    void Update(string text, NotificationSeverity? severity = null);
}

/// <summary>A notification that goes nowhere (a host with no notification centre).</summary>
public sealed class NoNotification : IEditorNotification
{
    /// <summary>The one instance.</summary>
    public static NoNotification Instance { get; } = new();

    /// <inheritdoc />
    public long Id => 0;

    /// <inheritdoc />
    public void Update(string text, NotificationSeverity? severity = null)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
