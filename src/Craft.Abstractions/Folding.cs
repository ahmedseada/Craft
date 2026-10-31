// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What a folding range holds (the editor folds them alike; a plug-in may fold only some kinds).</summary>
public enum FoldingRangeKind
{
    /// <summary>A block of code: a type, a member, a statement's body, an element, an object or an array.</summary>
    Region,

    /// <summary>A comment over several lines (a block comment, consecutive line comments, a documentation comment).</summary>
    Comment,

    /// <summary>A run of imports (C#'s usings).</summary>
    Imports,

    /// <summary>A region the author marked (C#'s <c>#region</c> to <c>#endregion</c>).</summary>
    Marked,
}

/// <summary>
/// A range of lines the editor can fold: <paramref name="StartLine"/> stays shown (the header, with a marker after its
/// text), the lines after it through <paramref name="EndLine"/> are hidden. Lines count from 0.
/// </summary>
/// <param name="StartLine">The header line, shown when folded.</param>
/// <param name="EndLine">The last line hidden when folded (greater than <paramref name="StartLine"/>).</param>
/// <param name="Kind">What it holds.</param>
public readonly record struct FoldingRange(int StartLine, int EndLine, FoldingRangeKind Kind = FoldingRangeKind.Region)
{
    /// <summary>The lines hidden when it is folded.</summary>
    public int HiddenLines => EndLine - StartLine;
}

/// <summary>What a folding range provider is asked for: a snapshot of a document in a language, with its session.</summary>
/// <param name="Snapshot">The text to fold (the ranges are in its lines).</param>
/// <param name="Language">The document's language name (<see cref="IKind.Name"/>).</param>
/// <param name="Session">The document's language session, which may hold a parse of the snapshot to reuse, or null.</param>
/// <param name="TabSize">The editor's tab size (for a provider that folds by indentation).</param>
public sealed record FoldingRequest(ITextSnapshot Snapshot, string Language, ILanguageSession? Session, int TabSize);

/// <summary>
/// Gives the folding ranges of the documents of its languages (registered in <see cref="FoldingRangeProviders"/>): C#'s
/// from its syntax tree, XML's from its elements, JSON's from its objects and arrays, or any plug-in's for any language.
/// The editor asks off the UI thread, once per text version it settles on, and folds by what comes back.
/// </summary>
public interface IFoldingRangeProvider : IKind
{
    /// <summary>The language names it folds (<see cref="ILanguage"/>'s names, ignoring case).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>
    /// The ranges of <paramref name="request"/>'s snapshot, in any order (the editor sorts them and keeps one per header
    /// line, the longest). Called on the thread pool, never the UI thread; it may take its time and must honour the token.
    /// </summary>
    Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The folding range providers, by name (ignoring case). Craft's library registers none; the C# plug-in registers C#'s
/// and its project languages' (MSBuild, XML, XAML, JSON). A document whose language no provider folds shows no fold
/// chevrons. A name that is not registered fails with "not registered", naming those that are.
/// </summary>
public static class FoldingRangeProviders
{
    private static readonly KindTable<IFoldingRangeProvider> Table = new(nameof(FoldingRangeProviders), "Folding range provider",
        "add it with FoldingRangeProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "each provider folds its own languages; the editor joins what every provider of a language gives");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(IFoldingRangeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IFoldingRangeProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IFoldingRangeProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IFoldingRangeProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The providers that fold <paramref name="language"/>, in registration order (none for plain text).</summary>
    public static IReadOnlyList<IFoldingRangeProvider> For(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return [];
        }

        List<IFoldingRangeProvider>? found = null;
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
