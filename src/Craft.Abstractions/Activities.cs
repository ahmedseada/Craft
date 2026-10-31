// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Abstractions;

/// <summary>
/// What kind of work an activity is (<see cref="IEditorActivities.Start"/>): the status bar and the notification centre
/// pick its icon and group by it. Any other name a plug-in uses is shown as <see cref="General"/>.
/// </summary>
public static class ActivityKinds
{
    /// <summary>Work of no particular kind.</summary>
    public const string General = "general";

    /// <summary>A download (a model's files): bytes and a rate.</summary>
    public const string Download = "download";

    /// <summary>A model's load into memory.</summary>
    public const string ModelLoad = "model-load";

    /// <summary>An agent's run (turns, tokens, tokens per second).</summary>
    public const string Agent = "agent";

    /// <summary>A model writing (a chat reply, a completion, a code action).</summary>
    public const string Generation = "generation";

    /// <summary>A training or reinforcement round (phase R).</summary>
    public const string Training = "training";

    /// <summary>An index being built (code search, phase P6).</summary>
    public const string Indexing = "indexing";

    /// <summary>A workspace's build (Roslyn's projects, source generators, a compilation).</summary>
    public const string Build = "build";

    /// <summary>A save of a file.</summary>
    public const string Save = "save";

    /// <summary>A command in a terminal or a sandbox.</summary>
    public const string Command = "command";

    /// <summary>A plug-in's load or start.</summary>
    public const string Plugin = "plugin";

    /// <summary>Voice: a recognizer or a voice being loaded, a transcription.</summary>
    public const string Voice = "voice";
}

/// <summary>How fast an activity goes: a value per second in a unit ("tok/s", "MB/s", "files/s").</summary>
/// <param name="Value">The amount per second.</param>
/// <param name="Unit">Its unit as shown ("tok/s").</param>
public readonly record struct ActivityRate(double Value, string Unit)
{
    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{(Value >= 100 ? Value.ToString("F0", CultureInfo.InvariantCulture) : Value.ToString("0.#", CultureInfo.InvariantCulture))} {Unit}");
}

/// <summary>Where an activity is.</summary>
public enum ActivityState
{
    /// <summary>It runs.</summary>
    Running,

    /// <summary>It ended as it should.</summary>
    Completed,

    /// <summary>It failed (<see cref="ActivitySnapshot.Error"/> says why).</summary>
    Failed,

    /// <summary>It was cancelled (by the user, or by its owner).</summary>
    Cancelled,
}

/// <summary>
/// One running operation (<see cref="IEditorActivities.Start"/>): report how far it is as it goes, from any thread, as
/// often as convenient (the UI shows the last state at most once a frame), and end it with <see cref="Complete"/>,
/// <see cref="Fail"/> or by disposing it (which completes it, or marks it cancelled when its token was cancelled). A
/// cancellable activity's <see cref="CancellationToken"/> is cancelled when the user presses Cancel; the work stops on it.
/// </summary>
public interface IActivity : IDisposable
{
    /// <summary>Its number in the session (unique, increasing).</summary>
    long Id { get; }

    /// <summary>What it is ("Loading Qwen2.5-Coder-7B").</summary>
    string Title { get; }

    /// <summary>Its kind (<see cref="ActivityKinds"/>).</summary>
    string Kind { get; }

    /// <summary>Cancelled when the user cancels it (or the token it was started with is); never when it is not cancellable.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Whether it ended.</summary>
    bool IsEnded { get; }

    /// <summary>
    /// How far it is: <paramref name="fraction"/> from 0 to 1 (null: not known, a spinner shows), what it does now
    /// (<paramref name="text"/>, null keeps the last), and how fast (<paramref name="rate"/>, null keeps the last). Any
    /// thread; ignored once it ended.
    /// </summary>
    void Report(double? fraction, string? text = null, ActivityRate? rate = null);

    /// <summary>Ends it as done, with what it came to (<paramref name="outcome"/>, "loaded in 41.2 s"), or null.</summary>
    void Complete(string? outcome = null);

    /// <summary>
    /// Ends it as failed with <paramref name="reason"/> (shown to the user, with a toast), the <paramref name="error"/>
    /// when there is one (the log keeps it), and <paramref name="retry"/> when it can be tried again (the notification
    /// centre's Retry runs it on the UI thread).
    /// </summary>
    void Fail(string reason, Exception? error = null, Action? retry = null);
}

/// <summary>What an activity is at one moment, as the status bar and the notification centre show it.</summary>
/// <param name="Id">Its number.</param>
/// <param name="Title">What it is.</param>
/// <param name="Kind">Its kind (<see cref="ActivityKinds"/>).</param>
/// <param name="Source">Who started it (a plug-in's name, "host").</param>
/// <param name="State">Where it is.</param>
/// <param name="Fraction">How far, from 0 to 1, or null when not known.</param>
/// <param name="Text">What it does now (or what it came to, once ended), or null.</param>
/// <param name="Rate">How fast, or null.</param>
/// <param name="Started">When it started.</param>
/// <param name="Elapsed">How long it has run (or ran).</param>
/// <param name="Cancellable">Whether the user can cancel it.</param>
public sealed record ActivitySnapshot(long Id, string Title, string Kind, string Source, ActivityState State, double? Fraction, string? Text,
    ActivityRate? Rate, DateTimeOffset Started, TimeSpan Elapsed, bool Cancellable)
{
    /// <summary>The time left, estimated from the fraction and the time so far; null when the fraction is not known or too small to tell.</summary>
    public TimeSpan? Left { get; init; }

    /// <summary>Why it failed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>Whether it can be tried again (<see cref="IEditorActivities.Retry"/>).</summary>
    public bool CanRetry { get; init; }

    /// <summary>Whether it is kept in the notification centre once it ends (false: shown while it runs only).</summary>
    public bool Keep { get; init; } = true;
}

/// <summary>
/// The host's running operations (<see cref="IEditorHost.Activities"/>): every operation that can outlast a moment (a
/// model's download or load, an agent's run, a reply being written, a training round, indexing, a workspace build, a
/// large save, a command in a terminal or a sandbox, a plug-in's load) starts one, so the status bar shows it (what, how
/// far, how fast, the time left, Cancel) and the notification centre keeps how it ended. Every member may be called on
/// any thread; <see cref="Changed"/> is raised on the UI thread, at most once a frame.
/// </summary>
public interface IEditorActivities
{
    /// <summary>
    /// Starts showing the operation <paramref name="title"/> of <paramref name="kind"/>; <paramref name="cancellable"/>
    /// gives it a Cancel (its <see cref="IActivity.CancellationToken"/>, linked to <paramref name="cancellationToken"/>);
    /// <paramref name="keep"/> false shows it only while it runs (a completion request), never in the centre's history.
    /// </summary>
    IActivity Start(string title, string kind = ActivityKinds.General, bool cancellable = false, CancellationToken cancellationToken = default, bool keep = true);

    /// <summary>The running ones, the newest first.</summary>
    IReadOnlyList<ActivitySnapshot> Running { get; }

    /// <summary>The ones that ended this session and are kept (the newest first, at most the host's limit).</summary>
    IReadOnlyList<ActivitySnapshot> Recent { get; }

    /// <summary>Cancels the running activity <paramref name="id"/>; false when it is not running or not cancellable.</summary>
    bool Cancel(long id);

    /// <summary>Runs the retry of the failed activity <paramref name="id"/> (on the UI thread); false when it has none.</summary>
    bool Retry(long id);

    /// <summary>Something changed (started, reported, ended): raised on the UI thread, at most once a frame.</summary>
    event EventHandler? Changed;
}

/// <summary>A host with no activities: every start gives an activity that shows nowhere (its token still cancels with the one given).</summary>
public sealed class NoActivities : IEditorActivities
{
    private long _next;

    /// <summary>The one instance.</summary>
    public static NoActivities Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ActivitySnapshot> Running => [];

    /// <inheritdoc />
    public IReadOnlyList<ActivitySnapshot> Recent => [];

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IActivity Start(string title, string kind = ActivityKinds.General, bool cancellable = false, CancellationToken cancellationToken = default, bool keep = true) =>
        new Silent(Interlocked.Increment(ref _next), title ?? "", kind ?? ActivityKinds.General, cancellationToken);

    /// <inheritdoc />
    public bool Cancel(long id) => false;

    /// <inheritdoc />
    public bool Retry(long id) => false;

    private sealed class Silent(long id, string title, string kind, CancellationToken token) : IActivity
    {
        public long Id { get; } = id;

        public string Title { get; } = title;

        public string Kind { get; } = kind;

        public CancellationToken CancellationToken { get; } = token;

        public bool IsEnded { get; private set; }

        public void Report(double? fraction, string? text = null, ActivityRate? rate = null)
        {
        }

        public void Complete(string? outcome = null) => IsEnded = true;

        public void Fail(string reason, Exception? error = null, Action? retry = null) => IsEnded = true;

        public void Dispose() => IsEnded = true;
    }
}
