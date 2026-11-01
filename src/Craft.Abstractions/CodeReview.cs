// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// One finding of a code review (X part 11 item 4): where it is, how much it matters, what it says in one line and why.
/// </summary>
/// <param name="Path">The file, as the diff names it (relative to the repository's root, '/' between folders).</param>
/// <param name="Line">The line in the file's new text (from 1); 0 when the finding is about the file as a whole.</param>
/// <param name="Severity">How much it matters (an error is a bug; a warning a risk; info a suggestion).</param>
/// <param name="Message">What it says, in one line.</param>
/// <param name="Reason">Why (the reason the reviewer gives), or empty.</param>
public sealed record ReviewFinding(string Path, int Line, DiagnosticSeverity Severity, string Message, string Reason)
{
    /// <summary>The finding as a line of text ("src/A.cs:12: warning: message — reason"), as Copy all writes it.</summary>
    public string Text => Reason.Length > 0
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Path}:{Line}: {SeverityWord(Severity)}: {Message} — {Reason}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Path}:{Line}: {SeverityWord(Severity)}: {Message}");

    /// <summary>The word a severity is written with ("error", "warning", "info").</summary>
    public static string SeverityWord(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "info",
    };
}

/// <summary>What is reviewed: the changes (a unified diff, already trimmed to the budget, each new line numbered) and how they were chosen.</summary>
/// <param name="Diff">The changes, the largest files summarised by name and counts when they did not fit.</param>
public sealed record CodeReviewRequest(string Diff)
{
    /// <summary>Whether the changes are every change of the working copy (nothing was staged), not the staged ones.</summary>
    public bool AllChanges { get; init; }

    /// <summary>The files summarised by name and counts only (not reviewed: they did not fit the budget).</summary>
    public IReadOnlyList<string> Summarised { get; init; } = [];
}

/// <summary>Which model a review would run with now, or why none can: what the Review button's tooltip says.</summary>
/// <param name="Model">The model's name, or null when none is loaded for it.</param>
/// <param name="Role">The role whose lease and settings it uses ("chat", "agent", "completion"), or null.</param>
/// <param name="Why">Why none can review (nothing loaded, the model named is not loaded, turned off), or null.</param>
public sealed record CodeReviewPlan(string? Model, string? Role, string? Why)
{
    /// <summary>The command that loads the model (only when the user presses it, note 23), or null.</summary>
    public string? LoadCommand { get; init; }

    /// <summary>The model's context window in tokens (the changes' budget is kept within it), or 0 when not known.</summary>
    public int ContextTokens { get; init; }

    /// <summary>The most tokens the review's reply takes (kept out of the budget), or 0 when not known.</summary>
    public int ReplyTokens { get; init; }
}

/// <summary>
/// Reviews changes before a commit (X part 11 item 4, Source Control's "Review changes"), registered in
/// <see cref="CodeReviewProviders"/>: the AI plug-in registers "ai", which reviews with a model already loaded (the chat
/// role's by default, its own setting picks another), through its existing lease and that role's settings. The git
/// plug-in never names a model. A provider changes nothing: a finding's fix is a diff the user accepts.
/// </summary>
public interface ICodeReviewProvider : IKind
{
    /// <summary>Which model would review now, or why none can; cheap, on the UI thread, loads nothing.</summary>
    CodeReviewPlan Plan();

    /// <summary>
    /// Streams the findings as the model writes them (each once it is whole); stops when <paramref name="cancellationToken"/>
    /// is cancelled (the findings so far stand). Runs on the thread pool.
    /// </summary>
    /// <exception cref="InvalidOperationException">No model can review now (<see cref="Plan"/> says why).</exception>
    IAsyncEnumerable<ReviewFinding> ReviewAsync(CodeReviewRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The code review providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers "ai");
/// the git plug-in asks the first registered one. A name that is not registered fails with "not registered".
/// </summary>
public static class CodeReviewProviders
{
    private static readonly KindTable<ICodeReviewProvider> Table = new(nameof(CodeReviewProviders), "Code review provider",
        "add it with CodeReviewProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider reviews with a model; another would review with a model the user did not choose");

    /// <summary>Raised after a provider was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ICodeReviewProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name)
    {
        bool removed = Table.Unregister(name);
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in order.</summary>
    public static IReadOnlyList<ICodeReviewProvider> All => Table.Kinds;

    /// <summary>The first registered provider (the one Source Control asks), or null.</summary>
    public static ICodeReviewProvider? First => Table.Kinds is { Count: > 0 } all ? all[0] : null;

    /// <summary>The provider of that name, or null.</summary>
    public static ICodeReviewProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ICodeReviewProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}
