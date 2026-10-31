// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// A view with an input above its items (the code search's "Search" rail view, registered in <see cref="EditorSideViews"/>
/// and drawn in the explorer's column): the host draws the input with <see cref="Placeholder"/>, hands what the user types to <see cref="SetQuery"/> (on the UI thread, as typed;
/// the panel does its work off it and raises <see cref="IEditorPanel.Changed"/> with the new items), and a tap on an
/// item to <see cref="Activate"/>. The host keeps the input across the panel's changes, so typing is never interrupted.
/// </summary>
public interface IQueryPanel : IEditorPanel
{
    /// <summary>The input's placeholder ("Search the code: a name, words, camelCase parts").</summary>
    string Placeholder { get; }

    /// <summary>The query the items answer.</summary>
    string Query { get; }

    /// <summary>A line under the input ("12 results · 3 ms · BM25 and vectors"), or null.</summary>
    string? Status { get; }

    /// <summary>The user typed <paramref name="text"/>: the panel searches (off the UI thread) and raises Changed.</summary>
    void SetQuery(string text);

    /// <summary>The user tapped the item at <paramref name="index"/> of <see cref="IEditorPanel.Items"/> (open it, go to it).</summary>
    void Activate(int index);
}

/// <summary>A rail side view (<see cref="EditorSideViews"/>): a query view drawn in the explorer's column while it is chosen.</summary>
/// <param name="View">The view (its <see cref="IEditorPanel.Id"/> is the side view's name).</param>
public sealed record SideView(IQueryPanel View) : IKind
{
    /// <inheritdoc />
    public string Name => View.Id;

    /// <inheritdoc />
    public string Summary => View.Title;
}

/// <summary>
/// The rail's side views, by id (ignoring case): views a plug-in registers (the code search's "search.code") that the shell
/// draws in the explorer's column, in the tree's place, when <see cref="IEditorHost.ShowPanel"/> names one (a rail item's
/// command does); naming the one shown again brings the tree back. A view unregistered while shown gives the tree back too.
/// </summary>
public static class EditorSideViews
{
    private static readonly KindTable<SideView> Table = new(nameof(EditorSideViews), "Side view", "add it with EditorSideViews.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a side view is the plug-in's own view, drawn as it is");

    /// <summary>Raised after a view was registered or removed (on the registering thread: the UI thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="view"/> under its id.</summary>
    public static void Register(IQueryPanel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(view.Id);
        Table.Register(new SideView(view), System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the view of that id; false when there was none.</summary>
    public static bool Unregister(string id)
    {
        bool removed = Table.Unregister(id);
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered ids, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The view of that id, or null.</summary>
    public static IQueryPanel? Find(ReadOnlySpan<char> id) => Table.Find(id)?.View;

    /// <summary>The view of that id.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IQueryPanel Get(ReadOnlySpan<char> id) => Table.Get(id).View;
}
