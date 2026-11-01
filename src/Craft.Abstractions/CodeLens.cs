// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// One code lens (X part 9): a short clickable title on a row above a member ("12 references", "Run test"), at the
/// member's declaration. A click runs <see cref="RunAsync"/> on the UI thread (its work off it); a lens without it is
/// only shown. Immutable; any thread.
/// </summary>
/// <param name="Start">The declaration it stands over (a <see cref="DocumentSymbol.Start"/>), in the snapshot asked about.</param>
/// <param name="Title">What it shows ("3 references").</param>
public sealed record CodeLens(int Start, string Title)
{
    /// <summary>What hovering shows, or null.</summary>
    public string? Tooltip { get; init; }

    /// <summary>What a click does, or null for a lens that only informs.</summary>
    public Func<CancellationToken, Task>? RunAsync { get; init; }

    /// <summary>The lenses of one member are ordered by it (lower first), then by provider.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Makes the lens's final title (a reference count) when it costs more than finding the lens: the editor shows
    /// <see cref="Title"/> (a placeholder) and asks this, off the UI thread, only for the lenses on screen, keeping the
    /// answer for the text version. Null for a lens that is final as it is.
    /// </summary>
    public Func<CancellationToken, Task<CodeLens>>? ResolveAsync { get; init; }
}

/// <summary>What code lenses are asked for: a snapshot's symbols, with the document's language and session.</summary>
/// <param name="Snapshot">The text (the lenses' positions are its).</param>
/// <param name="Language">The document's language name.</param>
/// <param name="Session">The document's language session (a semantic one counts references), or null.</param>
/// <param name="FilePath">The document's file, or null for a new one.</param>
/// <param name="Members">The document's symbols (the outline's tree) the lenses are asked for, in the order of the text.</param>
public sealed record CodeLensRequest(ITextSnapshot Snapshot, string Language, ILanguageSession? Session, string? FilePath, IReadOnlyList<DocumentSymbol> Members)
{
    /// <summary>The open folder, or null.</summary>
    public string? WorkspaceRoot { get; init; }

    /// <summary>The host, for a lens that runs something through it (a test, through <see cref="IEditorHost.RunTestAsync"/>), or null.</summary>
    public IEditorHost? Host { get; init; }
}

/// <summary>
/// Gives code lenses for the members of its languages' documents (registered in <see cref="CodeLensProviders"/>): C#'s
/// reference counts and its tests' Run and Debug. Asked off the UI thread once per text version for the document's
/// members, so each member's lens row is there before it scrolls into view; what is costly (a count) is left to
/// <see cref="CodeLens.ResolveAsync"/>, which the editor asks only for the lenses on screen.
/// </summary>
public interface ICodeLensProvider : IKind
{
    /// <summary>The language names it gives lenses for (ignoring case).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>The lenses of <paramref name="request"/>'s members (any order); called on the thread pool, honouring the token.</summary>
    Task<IReadOnlyList<CodeLens>> GetLensesAsync(CodeLensRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The code lens providers, by name (ignoring case). Craft's library registers none; the C# plug-in registers C#'s. A
/// document whose language no provider serves shows no lens row. A name that is not registered fails with "not
/// registered", naming those that are.
/// </summary>
public static class CodeLensProviders
{
    private static readonly KindTable<ICodeLensProvider> Table = new(nameof(CodeLensProviders), "Code lens provider",
        "add it with CodeLensProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "each provider adds its own lenses; the editor shows every provider's of a language");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ICodeLensProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ICodeLensProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ICodeLensProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ICodeLensProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The providers that serve <paramref name="language"/>, in registration order (none for plain text).</summary>
    public static IReadOnlyList<ICodeLensProvider> For(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return [];
        }

        List<ICodeLensProvider>? found = null;
        foreach (var provider in Table.Kinds)
        {
            foreach (var name in provider.Languages)
            {
                if (string.Equals(name, language, StringComparison.OrdinalIgnoreCase))
                {
                    (found ??= []).Add(provider);
                    break;
                }
            }
        }

        return found is null ? [] : found;
    }
}
