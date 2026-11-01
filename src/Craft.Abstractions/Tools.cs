// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Idrak.Abstraction.Generation;

namespace Craft.Abstractions;

/// <summary>
/// A set of the editor's tools, registered in <see cref="EditorToolSets"/> under its <see cref="IKind.Name"/>: Idrak
/// <see cref="ITool"/>s with two backings behind the same definitions, <see cref="Live"/> over the running editor (its
/// open documents, their unsaved text, the selection, the diagnostics, edits proposed to the user) and
/// <see cref="Folder"/> over a folder for a process that is not the editor (the idrak tool). The pre-installed
/// Craft.Plugins.Tools registers "editor"; a plug-in adds tools by registering a set of its own. No tool of a set edits
/// or runs anything without the user: an edit is a proposal the user accepts in the diff view (live) or a patch file the
/// user applies (folder).
/// </summary>
public interface IEditorToolSet : IKind
{
    /// <summary>The tools over <paramref name="host"/>, each safe to call from any thread (they read the host on its UI thread).</summary>
    IReadOnlyList<ITool> Live(IEditorHost host);

    /// <summary>The tools over the files under <paramref name="folder"/> (a full path); empty when the set has none for a folder.</summary>
    IReadOnlyList<ITool> Folder(string folder);
}

/// <summary>
/// The editor's tool sets, by name (ignoring case). Craft's library registers none: the pre-installed tools plug-in
/// (Craft.Plugins.Tools) registers "editor"; without it the editor offers no tools, and a name nobody registered fails with
/// "not registered". <see cref="Live"/> and <see cref="Folder"/> join every registered set's tools by name, the newest
/// set first: a plug-in's tool of a name another set has shadows it, and unregistering the plug-in's set brings the
/// other back. What the AI plug-ins (chat, the agent, the MCP server of the running app) give a model.
/// </summary>
public static class EditorToolSets
{
    /// <summary>The name of the editor's own set (Craft.Plugins.Tools).</summary>
    public const string Editor = "editor";

    private static readonly KindTable<IEditorToolSet> Table = new(nameof(EditorToolSets), "Editor tool set", "add it with EditorToolSets.Register",
        comparer: StringComparer.OrdinalIgnoreCase, newestFirst: true,
        unguarded: "a set's tools are handed to a server or a conversation that keeps them for its life");

    /// <summary>Registers <paramref name="set"/> under its name; it is asked before the sets registered earlier.</summary>
    public static void Register(IEditorToolSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Table.Register(set, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the set of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, the newest first.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered sets, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IEditorToolSet> All => Table.Kinds;

    /// <summary>The set of that name, or null.</summary>
    public static IEditorToolSet? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The set of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it (the message lists those that are).</exception>
    public static IEditorToolSet Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>Who registered the set <paramref name="name"/> (its assembly's name); null when none is.</summary>
    public static string? Origin(string name) => Table.Origin(name);

    /// <summary>Every set's live tools over <paramref name="host"/>, one per name, the newest set's first.</summary>
    public static IReadOnlyList<ITool> Live(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return Join(set => set.Live(host));
    }

    /// <summary>Every set's tools over <paramref name="folder"/>, one per name, the newest set's first.</summary>
    public static IReadOnlyList<ITool> Folder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Join(set => set.Folder(folder));
    }

    private static List<ITool> Join(Func<IEditorToolSet, IReadOnlyList<ITool>> tools)
    {
        var joined = new List<ITool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in Table.Kinds)
        {
            foreach (var tool in tools(set))
            {
                if (names.Add(tool.Definition.Name))
                {
                    joined.Add(tool);
                }
            }
        }

        return joined;
    }
}

/// <summary>
/// A question a plug-in asks the user (<see cref="IEditorHost.AskAsync"/>): a title, what it is about, the facts the user
/// decides on (shown as lines: a command's program, its time limit, its network, its folder), and the answers to pick from.
/// </summary>
/// <param name="Title">The question's title ("The agent wants to run a command").</param>
/// <param name="Message">What it is about (the command line).</param>
/// <param name="Choices">The answers, in order ("Allow once", "Allow dotnet for this run", "Deny and tell the agent").</param>
public sealed record EditorQuestion(string Title, string Message, IReadOnlyList<string> Choices)
{
    /// <summary>The facts shown under the message, one a line ("Program: dotnet", "Time limit: 120 s", "Network: off").</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>The answer a dismissed question counts as (an index of <see cref="Choices"/>), or -1 for none.</summary>
    public int Dismissed { get; init; } = -1;

    /// <summary>
    /// What kind of question it is (<see cref="EditorQuestionKinds"/>), or null: a host may show a known kind where it
    /// belongs (a command to approve inline in the agent's timeline) rather than in a dialog.
    /// </summary>
    public string? Kind { get; init; }
}

/// <summary>The known kinds of <see cref="EditorQuestion"/> (<see cref="EditorQuestion.Kind"/>).</summary>
public static class EditorQuestionKinds
{
    /// <summary>A command the agent wants to run, asked by a sandbox: the conversation panel waiting on it may show it inline.</summary>
    public const string Command = "command";
}
