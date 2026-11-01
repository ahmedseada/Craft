// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>An earlier exchange of the same inline chat (a follow-up refines the same edit): what was asked and what came back.</summary>
/// <param name="Instruction">What the user asked.</param>
/// <param name="Answer">The replacement of the lines the model wrote for it.</param>
public sealed record InlineChatTurn(string Instruction, string Answer);

/// <summary>
/// What an inline chat asks (X part 9 item 23, Ctrl+I): the lines <see cref="Start"/> to <see cref="End"/> of the
/// document's text (whole lines: the selection's, or the caret's), what the user wants done with them, the file's
/// surroundings and the problems on those lines; the answer is their replacement.
/// </summary>
/// <param name="Snapshot">The document's text when it was asked (the positions are in it).</param>
/// <param name="Start">Where the lines start.</param>
/// <param name="End">Where they end (after the last line's break, or the text's end).</param>
/// <param name="Instruction">What the user asked ("make it async", "add a null check").</param>
/// <param name="FilePath">The document's file, or null for a new document.</param>
/// <param name="LanguageName">Its language's name ("csharp"), or the plain-text name.</param>
public sealed record InlineChatRequest(ITextSnapshot Snapshot, int Start, int End, string Instruction, string? FilePath, string LanguageName)
{
    /// <summary>The problems (errors, warnings) on the lines, in the snapshot's positions.</summary>
    public IReadOnlyList<EditorDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The earlier exchanges of this inline chat, oldest first (a follow-up refines the last answer); none for the first.</summary>
    public IReadOnlyList<InlineChatTurn> History { get; init; } = [];

    /// <summary>The workspace folder, or null.</summary>
    public string? WorkspaceFolder { get; init; }
}

/// <summary>
/// Which model an inline chat would answer with now, or why none can (nothing loaded, turned off), and how the user
/// loads it: the inline box says so and offers the load (a model loads only when the user asks, note 23).
/// </summary>
/// <param name="Model">The model's name, or null when none can answer now.</param>
/// <param name="Why">Why none can (with what to do), or null.</param>
public sealed record InlineChatPlan(string? Model, string? Why)
{
    /// <summary>Loads the model when the user presses the box's "Load the chat model" (null: nothing to load, <see cref="Why"/> says what to do).</summary>
    public Func<CancellationToken, Task>? LoadAsync { get; init; }

    /// <summary>What the load button says ("Load the chat model").</summary>
    public string LoadTitle { get; init; } = "Load the chat model";
}

/// <summary>
/// Answers an inline chat (Ctrl+I in the editor), registered in <see cref="InlineChatProviders"/>: the AI plug-in
/// registers "ai" (the chat role's loaded model). It streams the replacement of the request's lines; the editor shows it
/// as a ghost diff in place and applies it, as one undo step, only when the user accepts (no silent edit). A provider
/// never edits the document itself and never loads a model by itself.
/// </summary>
public interface IInlineChatProvider : IKind
{
    /// <summary>Which model would answer now, or why none can; cheap, on the UI thread, loads nothing.</summary>
    InlineChatPlan Plan();

    /// <summary>
    /// Streams the replacement of the request's lines as the model writes it (the text only: no fences, no prose); stops
    /// when <paramref name="cancellationToken"/> is cancelled. Runs on the thread pool.
    /// </summary>
    /// <exception cref="InvalidOperationException">No model can answer now (<see cref="Plan"/> says why).</exception>
    IAsyncEnumerable<string> EditAsync(InlineChatRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The inline chat providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers "ai");
/// the editor asks the first registered one. A name that is not registered fails with "not registered".
/// </summary>
public static class InlineChatProviders
{
    private static readonly KindTable<IInlineChatProvider> Table = new(nameof(InlineChatProviders), "Inline chat provider",
        "add it with InlineChatProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "a provider streams a model's text; another would answer with a model the user did not choose");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(IInlineChatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in order.</summary>
    public static IReadOnlyList<IInlineChatProvider> All => Table.Kinds;

    /// <summary>The first registered provider (the one the editor asks), or null.</summary>
    public static IInlineChatProvider? First => Table.Kinds is { Count: > 0 } all ? all[0] : null;

    /// <summary>The provider of that name, or null.</summary>
    public static IInlineChatProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IInlineChatProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}
