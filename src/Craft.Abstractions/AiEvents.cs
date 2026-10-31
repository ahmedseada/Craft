// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Abstractions;

/// <summary>What an AI plug-in answered with: the kinds of <see cref="AiAnswer.Kind"/>.</summary>
public static class AiAnswerKinds
{
    /// <summary>Ghost text at the caret (inline completion).</summary>
    public const string Completion = "completion";

    /// <summary>A chat reply's code applied as a diff.</summary>
    public const string Chat = "chat";

    /// <summary>A code action's rewrite (fix, refactor, doc comments).</summary>
    public const string CodeAction = "code-action";

    /// <summary>An agent's task: the files it changed.</summary>
    public const string Agent = "agent";
}

/// <summary>What the user did with an answer (<see cref="AiVerdict.Verdict"/>): the words Idrak's preference pairs read.</summary>
public static class AiVerdicts
{
    /// <summary>Accepted and kept as it was.</summary>
    public const string Accepted = "accepted";

    /// <summary>Accepted, then edited: <see cref="AiVerdict.Kept"/> holds the text after the edit.</summary>
    public const string Edited = "edited";

    /// <summary>Rejected: <see cref="AiVerdict.Kept"/> holds the text written or kept instead, when known.</summary>
    public const string Rejected = "rejected";

    /// <summary>Accepted, then undone.</summary>
    public const string Undone = "undone";
}

/// <summary>
/// An answer an AI plug-in gave (<see cref="AiEvents.Answered"/>): what the model was shown, what it answered, and where it
/// would go. Raised once per answer, before the user decides; what the user did follows as an <see cref="AiVerdict"/> of
/// the same <see cref="Id"/> (or, for an inline completion, is read from the document: the ghost inserted at
/// <see cref="Position"/>, or something else typed there).
/// </summary>
/// <param name="Id">The answer's id (<see cref="AiEvents.NewId"/>), unique in the process.</param>
/// <param name="Kind">What it is (<see cref="AiAnswerKinds"/>).</param>
/// <param name="Prompt">The text the model was shown (the laid-out prompt, or the conversation's last message with its context).</param>
/// <param name="Answer">What the model answered (the ghost text, the code proposed, the agent's changes).</param>
public sealed record AiAnswer(string Id, string Kind, string Prompt, string Answer)
{
    /// <summary>The plug-in that answered ("ai").</summary>
    public string Plugin { get; init; } = "";

    /// <summary>The model's name as the plug-in asked for it, or null.</summary>
    public string? Model { get; init; }

    /// <summary>The text the answer was made for (its buffer names the document), or null (an agent's task).</summary>
    public ITextSnapshot? Snapshot { get; init; }

    /// <summary>Where in <see cref="Snapshot"/> the answer goes (the caret of a completion, the start of a replaced range); -1 when none.</summary>
    public int Position { get; init; } = -1;

    /// <summary>The text the answer would replace (a rewrite's original lines), or null.</summary>
    public string? Before { get; init; }

    /// <summary>The document's file, or null.</summary>
    public string? FilePath { get; init; }

    /// <summary>Its language's name, or "".</summary>
    public string LanguageName { get; init; } = "";

    /// <summary>The open folder, or null.</summary>
    public string? WorkspaceFolder { get; init; }

    /// <summary>The prompt's token ids as the model saw them, when the plug-in has them; null otherwise.</summary>
    public IReadOnlyList<int>? PromptIds { get; init; }

    /// <summary>The answer's token ids as the model sampled them, when the plug-in has them; null otherwise.</summary>
    public IReadOnlyList<int>? AnswerIds { get; init; }

    /// <summary>When it was answered.</summary>
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>What the user did with the answer <see cref="AnswerId"/> (<see cref="AiEvents.Judged"/>).</summary>
/// <param name="AnswerId">The answer's <see cref="AiAnswer.Id"/>.</param>
/// <param name="Verdict">What the user did (<see cref="AiVerdicts"/>).</param>
public sealed record AiVerdict(string AnswerId, string Verdict)
{
    /// <summary>The text kept in the answer's place (what the user wrote or left instead), or null when not known.</summary>
    public string? Kept { get; init; }

    /// <summary>The document the answer was applied to, or null.</summary>
    public IEditorDocument? Document { get; init; }

    /// <summary>Where the applied answer starts in the document's current text; -1 when it was not applied.</summary>
    public int Start { get; init; } = -1;

    /// <summary>The applied answer's length in the document's current text; -1 when not applied.</summary>
    public int Length { get; init; } = -1;
}

/// <summary>
/// What hears the AI plug-ins' answers and the user's verdicts on them, registered in <see cref="AiEvents"/> (the learning
/// plug-in records them as episodes, under the user's consent). Called on the thread that raised the event (an inline
/// completion's is the thread pool); a sink that touches the editor dispatches to its UI thread. A sink that throws is
/// left out of that event; the others still hear it.
/// </summary>
public interface IAiEventSink : IKind
{
    /// <summary>An AI plug-in answered.</summary>
    void Answered(AiAnswer answer);

    /// <summary>The user decided about an answer.</summary>
    void Judged(AiVerdict verdict);
}

/// <summary>
/// The AI plug-ins' answers and the user's verdicts (plan 1, phase R): an AI plug-in raises <see cref="Answered"/> when it
/// answers and <see cref="Judged"/> when the user accepts or rejects; every registered <see cref="IAiEventSink"/> hears
/// them. Craft's library registers no sink, so nothing is heard (or kept) until a plug-in registers one; a name nobody
/// registered fails "not registered".
/// </summary>
public static class AiEvents
{
    private static readonly KindTable<IAiEventSink> Table = new(nameof(AiEvents), "AI event sink", "add it with AiEvents.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "every sink hears every event; none stands in for another");

    private static long s_next;

    /// <summary>Registers <paramref name="sink"/> under its name.</summary>
    public static void Register(IAiEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Table.Register(sink, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the sink of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The sink of that name, or null.</summary>
    public static IAiEventSink? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The sink of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IAiEventSink Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>Whether a sink hears the events (a plug-in skips gathering what nobody reads).</summary>
    public static bool Listening => Table.Names.Count > 0;

    /// <summary>A new answer id, unique in the process.</summary>
    public static string NewId() => "a" + Interlocked.Increment(ref s_next).ToString(CultureInfo.InvariantCulture);

    /// <summary>Tells every sink that an AI plug-in answered.</summary>
    public static void Answered(AiAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        foreach (var sink in Table.Kinds)
        {
            try
            {
                sink.Answered(answer);
            }
#pragma warning disable CA1031 // A sink's failure never reaches the plug-in that answered.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>Tells every sink what the user did with an answer.</summary>
    public static void Judged(AiVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        foreach (var sink in Table.Kinds)
        {
            try
            {
                sink.Judged(verdict);
            }
#pragma warning disable CA1031 // A sink's failure never reaches the plug-in that raised it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }
}

/// <summary>
/// Where a model's adapter comes from (registered in <see cref="ModelAdapters"/>): the learning plug-in names the adapter it
/// promoted for a model; a model provider attaches it when it loads the model (<see cref="ModelRequest.Adapter"/>).
/// </summary>
public interface IModelAdapterSource : IKind
{
    /// <summary>The adapter folder (PEFT layout) to attach to <paramref name="model"/>, or null for none.</summary>
    string? AdapterFor(string model);
}

/// <summary>
/// The adapters attached to the models the AI plug-ins load (plan 1, phase R): the newest registered source that names one
/// for a model wins. <see cref="Changed"/> is raised when a source's answer changes (a promotion, a rollback), so the
/// plug-ins that hold a model load it again with the new adapter. Craft's library registers no source: no adapter is
/// attached until a plug-in names one.
/// </summary>
public static class ModelAdapters
{
    private static readonly KindTable<IModelAdapterSource> Table = new(nameof(ModelAdapters), "Model adapter source", "add it with ModelAdapters.Register",
        comparer: StringComparer.OrdinalIgnoreCase, newestFirst: true, unguarded: "a source names a file the provider loads; none stands in for another");

    /// <summary>Raised (on the thread that changed it) when an adapter source's answer changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="source"/> under its name; it is asked before the sources registered earlier.</summary>
    public static void Register(IModelAdapterSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the source of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, the newest first.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The source of that name, or null.</summary>
    public static IModelAdapterSource? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IModelAdapterSource Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The adapter the newest source names for <paramref name="model"/>, or null for none.</summary>
    public static string? For(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        foreach (var source in Table.Kinds)
        {
            if (source.AdapterFor(model) is { Length: > 0 } adapter)
            {
                return adapter;
            }
        }

        return null;
    }

    /// <summary>Raises <see cref="Changed"/>: a source's answer changed.</summary>
    public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
