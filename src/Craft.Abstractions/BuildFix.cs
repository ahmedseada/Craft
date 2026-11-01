// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// A failed build or test to fix (X part 11 item 5, the Run view's "Fix with AI"): what ran, in which folder, and the
/// problems its output reported.
/// </summary>
/// <param name="Title">What ran ("Build: App").</param>
/// <param name="WorkspaceFolder">The workspace folder (the problems' files are under it).</param>
/// <param name="Problems">The problems to fix (the errors first; one for a Problems row's Fix with AI).</param>
public sealed record BuildFixRequest(string Title, string WorkspaceFolder, IReadOnlyList<RunProblem> Problems);

/// <summary>How a build fix ended: whether the user accepted a change, the documents it changed, and what to say.</summary>
/// <param name="Accepted">Whether the user accepted at least one change of the fix.</param>
/// <param name="Changed">The open documents an accepted change went into (the Run view saves them before it runs again).</param>
/// <param name="Summary">What happened, in a line ("1 file changed", "no change was accepted").</param>
public sealed record BuildFixResult(bool Accepted, IReadOnlyList<IEditorDocument> Changed, string Summary);

/// <summary>
/// Fixes a failed build or test (the Run view's and the Problems rows' "Fix with AI"), registered in
/// <see cref="BuildFixProviders"/>: the AI plug-in registers "ai" (one error to the chat's model, several to the agent).
/// Its fix arrives as a diff the user accepts (no silent edit); the Run view then offers "Run again".
/// </summary>
public interface IBuildFixProvider : IKind
{
    /// <summary>Whether it offers to fix <paramref name="request"/> now (turned on, a model to use); cheap, on the UI thread, loads nothing.</summary>
    bool Offers(BuildFixRequest request);

    /// <summary>
    /// Asks the model for a fix and proposes it as diffs (on the UI thread; the model's work off it); completes when the
    /// user has answered every diff, or when cancelled.
    /// </summary>
    Task<BuildFixResult> FixAsync(BuildFixRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The build fix providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers "ai");
/// the Run view asks the first registered one that offers. A name that is not registered fails with "not registered".
/// </summary>
public static class BuildFixProviders
{
    private static readonly KindTable<IBuildFixProvider> Table = new(nameof(BuildFixProviders), "Build fix provider",
        "add it with BuildFixProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider fixes with a model; another would fix with a model the user did not choose");

    /// <summary>Raised after a provider was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(IBuildFixProvider provider)
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
    public static IReadOnlyList<IBuildFixProvider> All => Table.Kinds;

    /// <summary>The first registered provider that offers to fix <paramref name="request"/>, or null (one that throws is passed over).</summary>
    public static IBuildFixProvider? For(BuildFixRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var provider in Table.Kinds)
        {
            try
            {
                if (provider.Offers(request))
                {
                    return provider;
                }
            }
#pragma warning disable CA1031 // One provider's failure must not hide the others' offer.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return null;
    }

    /// <summary>The provider of that name, or null.</summary>
    public static IBuildFixProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IBuildFixProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}
