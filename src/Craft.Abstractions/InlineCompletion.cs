// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What an inline completion is asked for: the text, the caret, the file and its language.</summary>
/// <param name="Snapshot">The text as it is when the request starts (immutable: read it on any thread).</param>
/// <param name="Position">The caret, where the completion is inserted.</param>
/// <param name="FilePath">The document's file, or null for a new one.</param>
/// <param name="LanguageName">Its language's name ("csharp"), or the plain-text name the editor shows.</param>
/// <param name="WorkspaceFolder">The open folder, or null.</param>
public sealed record InlineCompletionRequest(ITextSnapshot Snapshot, int Position, string? FilePath, string LanguageName, string? WorkspaceFolder);

/// <summary>A piece of an inline completion as it streams: its text and how many tokens the model sampled for it.</summary>
/// <param name="Text">Text to append to the completion so far (may be empty).</param>
/// <param name="Tokens">The tokens it took (0 when the provider does not count them): the editor shows the rate.</param>
public readonly record struct InlineCompletionPiece(string Text, int Tokens = 1);

/// <summary>
/// Ghost text at the caret (plan 1, "Inline completion"), registered in <see cref="InlineCompletionProviders"/>. The editor
/// owns the request's life: it asks after a pause in typing (<see cref="DelayMilliseconds"/>), on the thread pool, never
/// on the UI thread; it cancels the request at the next keystroke (a provider stops within a token of the cancellation);
/// it draws the pieces as ghost text and inserts them only when the user accepts (Tab: all, one undo step; Ctrl+Right: a
/// word); Escape dismisses. A provider never edits.
/// </summary>
public interface IInlineCompletionProvider : IKind
{
    /// <summary>The pause after typing before the editor asks, in milliseconds (the provider's setting).</summary>
    int DelayMilliseconds { get; }

    /// <summary>Whether the provider completes <paramref name="request"/> (its language, its setting); cheap, on the UI thread.</summary>
    bool Accepts(InlineCompletionRequest request);

    /// <summary>Streams the completion of <paramref name="request"/> at its position; stops (throws or ends) when <paramref name="cancellationToken"/> is cancelled.</summary>
    IAsyncEnumerable<InlineCompletionPiece> CompleteAsync(InlineCompletionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The inline completion providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers
/// "ai"); the editor asks the first registered one that accepts a request, or the one the editor's setting names. A name
/// that is not registered fails with "not registered", naming those that are.
/// </summary>
public static class InlineCompletionProviders
{
    private static readonly KindTable<IInlineCompletionProvider> Table = new(nameof(InlineCompletionProviders), "Inline completion provider",
        "add it with InlineCompletionProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider streams a model's text; falling back to another would show text from a model the user did not choose");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(IInlineCompletionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IInlineCompletionProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IInlineCompletionProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IInlineCompletionProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The provider for <paramref name="request"/>: the one named <paramref name="name"/> when it is given (it must be
    /// registered) and accepts the request, else the first registered that accepts it; null when none does.
    /// </summary>
    /// <exception cref="NotRegisteredException"><paramref name="name"/> is given and not registered.</exception>
    public static IInlineCompletionProvider? For(InlineCompletionRequest request, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var named = Get(name.AsSpan().Trim());
            return named.Accepts(request) ? named : null;
        }

        foreach (var provider in Table.Kinds)
        {
            if (provider.Accepts(request))
            {
                return provider;
            }
        }

        return null;
    }
}
