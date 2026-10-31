// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>The kinds of code action the editor shows in fixed places (the Problems row's buttons, the palette's groups).</summary>
public static class CodeActionKinds
{
    /// <summary>Fixes a diagnostic.</summary>
    public const string Fix = "fix";

    /// <summary>Explains a diagnostic or a selection (an answer, not an edit).</summary>
    public const string Explain = "explain";

    /// <summary>Rewrites a selection.</summary>
    public const string Refactor = "refactor";

    /// <summary>Adds documentation comments.</summary>
    public const string Document = "document";

    /// <summary>Writes tests for a selection.</summary>
    public const string Tests = "tests";
}

/// <summary>Where code actions are asked for: a document, a range of its current text, and the diagnostics there.</summary>
/// <param name="Document">The document.</param>
/// <param name="Start">The range's start, in the document's current snapshot.</param>
/// <param name="End">Its end (equal to <paramref name="Start"/> for a caret).</param>
/// <param name="Diagnostics">The diagnostics in the range (one for a Problems row), in order.</param>
public sealed record CodeActionContext(IEditorDocument Document, int Start, int End, IReadOnlyList<EditorDiagnostic> Diagnostics);

/// <summary>
/// One code action offered for a <see cref="CodeActionContext"/>: its title and kind, and what it does. Running it never
/// edits silently: an edit it makes goes through <see cref="IEditorHost.ProposeAsync"/>, a diff the user accepts.
/// </summary>
/// <param name="Id">Its id, unique among the provider's ("ai.fix").</param>
/// <param name="Title">What the user sees ("Fix with the model").</param>
/// <param name="Kind">One of <see cref="CodeActionKinds"/>, or a provider's own.</param>
/// <param name="RunAsync">Runs it (on the UI thread; its work off it); completes when done or cancelled.</param>
public sealed record CodeAction(string Id, string Title, string Kind, Func<CancellationToken, Task> RunAsync)
{
    /// <summary>The provider that offered it (set by <see cref="CodeActionProviders.ActionsFor"/>).</summary>
    public string Provider { get; init; } = "";
}

/// <summary>
/// Code actions over a selection or a diagnostic (explain, fix, refactor, document, write tests), registered in
/// <see cref="CodeActionProviders"/>. Their edits go through the diff view.
/// </summary>
public interface ICodeActionProvider : IKind
{
    /// <summary>The actions for <paramref name="context"/> (cheap, on the UI thread: the work is in <see cref="CodeAction.RunAsync"/>).</summary>
    IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context);
}

/// <summary>
/// The code action providers, by name (ignoring case). Craft's library registers none (the AI plug-in registers "ai"; the
/// C# plug-in may register Roslyn's). A name that is not registered fails with "not registered", naming those that are.
/// </summary>
public static class CodeActionProviders
{
    private static readonly KindTable<ICodeActionProvider> Table = new(nameof(CodeActionProviders), "Code action provider",
        "add it with CodeActionProviders.Register", comparer: StringComparer.OrdinalIgnoreCase,
        unguarded: "each provider adds its own actions; none stands in for another");

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ICodeActionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ICodeActionProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ICodeActionProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static ICodeActionProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// Every registered provider's actions for <paramref name="context"/>, in the providers' order, each marked with its
    /// provider. A provider that throws is left out (<paramref name="failed"/> hears of it); the others still answer.
    /// </summary>
    public static IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context, Action<string, Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actions = new List<CodeAction>();
        foreach (var provider in Table.Kinds)
        {
            try
            {
                foreach (var action in provider.ActionsFor(context))
                {
                    actions.Add(action with { Provider = provider.Name });
                }
            }
#pragma warning disable CA1031 // One provider's failure must not take the others' actions away; the caller logs it.
            catch (Exception error)
#pragma warning restore CA1031
            {
                failed?.Invoke(provider.Name, error);
            }
        }

        return actions;
    }
}
