// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Abstractions;

/// <summary>What a file's change is in a repository (X part 8: its letter and colour in the Source Control view and the tree).</summary>
public enum SourceControlState
{
    /// <summary>Its text changed ("M").</summary>
    Modified,

    /// <summary>New to the repository, staged ("A").</summary>
    Added,

    /// <summary>Removed ("D").</summary>
    Deleted,

    /// <summary>Moved from <see cref="SourceControlFile.OldPath"/> ("R").</summary>
    Renamed,

    /// <summary>Copied from <see cref="SourceControlFile.OldPath"/> ("C").</summary>
    Copied,

    /// <summary>Not tracked yet ("U").</summary>
    Untracked,

    /// <summary>In a merge conflict ("!").</summary>
    Conflict,

    /// <summary>Its kind changed (a file became a link: "T").</summary>
    TypeChanged,
}

/// <summary>A changed file of a repository: where it is, what its change is, and whether the change is staged.</summary>
/// <param name="Path">Its full path.</param>
/// <param name="State">What its change is.</param>
/// <param name="Staged">Whether this entry is the staged change (the index), else the working copy's.</param>
public sealed record SourceControlFile(string Path, SourceControlState State, bool Staged)
{
    /// <summary>Where a renamed or copied file came from (its full path), or null.</summary>
    public string? OldPath { get; init; }

    /// <summary>Its path relative to the repository's root, with '/' (as the repository writes it).</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>The state's one letter (M, A, D, R, C, U, !, T).</summary>
    public string Letter => SourceControlStates.Letter(State);
}

/// <summary>A repository's state at one moment: its branch, how far it is from its upstream, and its changes.</summary>
/// <param name="Root">The repository's root folder (its full path).</param>
/// <param name="Branch">The branch checked out, or the short commit when detached, or null before the first read.</param>
public sealed record SourceControlStatus(string Root, string? Branch)
{
    /// <summary>No repository read yet.</summary>
    public static SourceControlStatus None { get; } = new("", null);

    /// <summary>The upstream branch ("origin/main"), or null when none is set.</summary>
    public string? Upstream { get; init; }

    /// <summary>Commits on the branch the upstream does not have.</summary>
    public int Ahead { get; init; }

    /// <summary>Commits on the upstream the branch does not have.</summary>
    public int Behind { get; init; }

    /// <summary>Whether HEAD is detached (no branch checked out).</summary>
    public bool Detached { get; init; }

    /// <summary>The staged changes, by path.</summary>
    public IReadOnlyList<SourceControlFile> Staged { get; init; } = [];

    /// <summary>The working copy's changes (untracked files among them), by path.</summary>
    public IReadOnlyList<SourceControlFile> Changes { get; init; } = [];

    /// <summary>The files in a merge conflict, by path.</summary>
    public IReadOnlyList<SourceControlFile> Conflicts { get; init; } = [];

    /// <summary>The stashed changes kept.</summary>
    public int Stashes { get; init; }

    /// <summary>Every changed file once (a file both staged and changed counts once): the rail's badge.</summary>
    public int Count
    {
        get
        {
            if (Staged.Count == 0)
            {
                return Changes.Count + Conflicts.Count;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in (ReadOnlySpan<IReadOnlyList<SourceControlFile>>)[Staged, Changes, Conflicts])
            {
                foreach (var file in list)
                {
                    paths.Add(file.Path);
                }
            }

            return paths.Count;
        }
    }

    /// <summary>The branch as the status bar shows it: its name, then ↑ahead and ↓behind when not zero ("main ↑1 ↓2").</summary>
    public string BranchText =>
        Branch is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{Branch}{(Ahead > 0 ? $" ↑{Ahead}" : "")}{(Behind > 0 ? $" ↓{Behind}" : "")}");

    /// <summary>
    /// What the folder tree shows for <paramref name="path"/>: a conflict first, then the working copy's change, then the
    /// staged one; null when the file did not change.
    /// </summary>
    public SourceControlFile? FileState(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (var list in (ReadOnlySpan<IReadOnlyList<SourceControlFile>>)[Conflicts, Changes, Staged])
        {
            foreach (var file in list)
            {
                if (string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }
}

/// <summary>The letters and colour roles of <see cref="SourceControlState"/>.</summary>
public static class SourceControlStates
{
    /// <summary>The state's one letter.</summary>
    public static string Letter(SourceControlState state) => state switch
    {
        SourceControlState.Modified => "M",
        SourceControlState.Added => "A",
        SourceControlState.Deleted => "D",
        SourceControlState.Renamed => "R",
        SourceControlState.Copied => "C",
        SourceControlState.Untracked => "U",
        SourceControlState.Conflict => "!",
        _ => "T",
    };

    /// <summary>The state's colour role: added and untracked are a success, deleted and conflicts an error, the rest a warning.</summary>
    public static PanelTone Tone(SourceControlState state) => state switch
    {
        SourceControlState.Added or SourceControlState.Untracked => PanelTone.Success,
        SourceControlState.Deleted or SourceControlState.Conflict => PanelTone.Error,
        SourceControlState.Renamed or SourceControlState.Copied => PanelTone.Accent,
        _ => PanelTone.Warning,
    };

    /// <summary>The state in words ("Modified", "Untracked").</summary>
    public static string Words(SourceControlState state) => state switch
    {
        SourceControlState.TypeChanged => "Type changed",
        SourceControlState.Conflict => "Conflict",
        _ => state.ToString(),
    };
}

/// <summary>
/// An open repository (X part 8): its last status, refreshed by its provider (the folder's changes, a save, the user's
/// actions), and what the user does to it. A plug-in publishes it through <see cref="IEditorHost.AddRepository"/>, so the
/// shell shows the branch and colours the tree. Every member may be called on any thread; the work runs off the caller's.
/// </summary>
public interface ISourceControlRepository
{
    /// <summary>The provider's name ("git").</summary>
    string Provider { get; }

    /// <summary>The repository's root folder.</summary>
    string Root { get; }

    /// <summary>Its status as last read (<see cref="SourceControlStatus.None"/> before the first read).</summary>
    SourceControlStatus Status { get; }

    /// <summary>Raised after <see cref="Status"/> changed, on the host's UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Reads the status again (now, not after the debounce).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages <paramref name="paths"/> (full paths).</summary>
    Task StageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    /// <summary>Takes <paramref name="paths"/> out of the staged changes (their working copies stay).</summary>
    Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes with <paramref name="message"/> (amending the last commit when <paramref name="amend"/>).</summary>
    Task CommitAsync(string message, bool amend, CancellationToken cancellationToken = default);

    /// <summary>The text of <paramref name="path"/> as staged (the index), or null when it is not in the index.</summary>
    Task<string?> ReadStagedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The text of <paramref name="path"/> at the last commit, or null when it is not there.</summary>
    Task<string?> ReadCommittedAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// A source control system (X part 8), registered in <see cref="SourceControlProviders"/>: Craft's git plug-in registers
/// "git"; another system registers its own. It opens the repository a folder is in.
/// </summary>
public interface ISourceControlProvider : IKind
{
    /// <summary>Why it cannot work on this machine now (its program not found, with what to do), or null when it can.</summary>
    string? Unavailable { get; }

    /// <summary>The repository <paramref name="folder"/> is in, or null when it is in none; off the calling thread.</summary>
    Task<ISourceControlRepository?> OpenAsync(string folder, CancellationToken cancellationToken = default);
}

/// <summary>
/// The source control providers, by name (ignoring case). Craft's library registers none (the git plug-in registers
/// "git"); a name that is not registered fails with "not registered", naming those that are.
/// </summary>
public static class SourceControlProviders
{
    /// <summary>
    /// The command the status bar's branch item runs ("craft.sourceControl.branch"): a source control plug-in registers it
    /// (the branches to switch to, a new one); while none does, the item does nothing.
    /// </summary>
    public const string BranchCommand = "craft.sourceControl.branch";

    /// <summary>The command that shows the Source Control view ("craft.sourceControl.show"), registered by the plug-in that draws it.</summary>
    public const string ShowCommand = "craft.sourceControl.show";

    private static readonly KindTable<ISourceControlProvider> Table = new(nameof(SourceControlProviders), "Source control provider",
        "add it with SourceControlProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider runs the user's own program; another system's would act on a different repository");

    /// <summary>Raised after a provider was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ISourceControlProvider provider)
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
    public static IReadOnlyList<ISourceControlProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ISourceControlProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ISourceControlProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>A model a commit message can be written with (the Generate button's menu): a loaded model, by role or name.</summary>
/// <param name="Value">What the setting <c>commitModel</c> takes to pick it ("chat", "agent", "completion", or the model's name).</param>
/// <param name="Title">What the menu shows ("Chat · Qwen2.5-Coder-7B-Instruct").</param>
/// <param name="Loaded">Whether it is loaded now (only a loaded model writes; none is loaded for it).</param>
public sealed record CommitModelChoice(string Value, string Title, bool Loaded);

/// <summary>Which model a commit message would be written with now, or why none: what the Generate button's tooltip says.</summary>
/// <param name="Model">The model's name, or null when none is loaded for the choice.</param>
/// <param name="Role">The role whose lease and settings it uses ("chat", "agent", "completion"), or null.</param>
/// <param name="Why">Why none can write (nothing loaded, the model named is not loaded), or null.</param>
public sealed record CommitModelPlan(string? Model, string? Role, string? Why)
{
    /// <summary>The command that loads the chat's model ("Load the chat model": only when the user asks, note 23), or null.</summary>
    public string? LoadCommand { get; init; }

    /// <summary>The model's context window in tokens (the diff's budget is kept within it), or 0 when not known.</summary>
    public int ContextTokens { get; init; }
}

/// <summary>What a commit message is written from: the instructions, the repository's recent subjects and the changes.</summary>
/// <param name="Instructions">What to write and how (the system's words: the style, the length).</param>
/// <param name="Diff">The changes, already trimmed to the budget (the largest files summarised by name and counts).</param>
public sealed record CommitMessageRequest(string Instructions, string Diff)
{
    /// <summary>The repository's recent subjects, newest first (examples of its style), or none.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Which model (<c>commitModel</c>: "loaded", "chat", "agent", "completion" or a model's name).</summary>
    public string Model { get; init; } = "loaded";

    /// <summary>The most tokens the message takes (0: the role's own reply limit).</summary>
    public int MaxTokens { get; init; }
}

/// <summary>
/// Writes a commit message (X part 8, "Generate commit message"), registered in <see cref="CommitMessageProviders"/>: the
/// AI plug-in registers "ai", which writes with a model already loaded, through its existing lease and that role's own
/// settings, never loading one by itself. A provider never commits: the message is the user's draft.
/// </summary>
public interface ICommitMessageProvider : IKind
{
    /// <summary>The models it could write with now (the loaded ones first), for the button's menu.</summary>
    IReadOnlyList<CommitModelChoice> Models();

    /// <summary>Which model <paramref name="choice"/> (<c>commitModel</c>) takes now, or why none; cheap, on the UI thread.</summary>
    CommitModelPlan Plan(string choice);

    /// <summary>Streams the message's pieces as the model writes them; stops when <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <exception cref="InvalidOperationException">No model is loaded for the choice (<see cref="Plan"/> says why).</exception>
    IAsyncEnumerable<string> WriteAsync(CommitMessageRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The commit message providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers
/// "ai"); the git plug-in asks the first registered one. A name that is not registered fails with "not registered".
/// </summary>
public static class CommitMessageProviders
{
    private static readonly KindTable<ICommitMessageProvider> Table = new(nameof(CommitMessageProviders), "Commit message provider",
        "add it with CommitMessageProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider streams a model's text; another would write with a model the user did not choose");

    /// <summary>Raised after a provider was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ICommitMessageProvider provider)
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
    public static IReadOnlyList<ICommitMessageProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ICommitMessageProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ICommitMessageProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}
