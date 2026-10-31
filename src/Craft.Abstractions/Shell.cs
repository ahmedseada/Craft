// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// An item of a title-bar menu (<see cref="EditorMenus"/>): the menu it is in, the command it runs, and its place. It is
/// named "Menu/command" ("File/craft.openFile"), so one command can sit in several menus. The menu shows it only while its
/// command is registered, by <see cref="Title"/> or else the command's title, with the chord a key binding gives it, or
/// <see cref="Shortcut"/>.
/// </summary>
/// <param name="Menu">The menu ("File", or one of <see cref="EditorMenus"/>' names; another name makes a menu of its own after them).</param>
/// <param name="CommandId">The command it runs (<see cref="EditorCommands"/>).</param>
/// <param name="Title">What it shows; null for the command's title without its "Area: " prefix.</param>
/// <param name="Order">Its order in the menu: lower first.</param>
/// <param name="Group">Its group: items of one group sit together, a line between groups.</param>
/// <param name="Shortcut">The chord to show when no key binding runs the command (the editor's own keys: "Ctrl+Z").</param>
public sealed record MenuItem(string Menu, string CommandId, string? Title = null, int Order = 0, int Group = 0, string? Shortcut = null) : IKind
{
    /// <summary>"Menu/command".</summary>
    public string Name { get; } = $"{Menu}/{CommandId}";

    /// <inheritdoc />
    public string Summary => $"{Menu}: {Title ?? CommandId}";
}

/// <summary>
/// The title bar's menus, their items by "Menu/command" (ignoring case). The app registers its own items; a plug-in adds
/// one with <see cref="Register"/> in its registration's parts (and takes it back with <see cref="Unregister"/>).
/// </summary>
public static class EditorMenus
{
    /// <summary>The File menu.</summary>
    public const string File = "File";

    /// <summary>The Edit menu.</summary>
    public const string Edit = "Edit";

    /// <summary>The View menu.</summary>
    public const string View = "View";

    /// <summary>The Run menu.</summary>
    public const string Run = "Run";

    /// <summary>The Plug-ins menu (where a plug-in's items go when no other menu fits).</summary>
    public const string Plugins = "Plug-ins";

    /// <summary>The Help menu.</summary>
    public const string Help = "Help";

    /// <summary>The design's menus, in order.</summary>
    public static IReadOnlyList<string> Standard { get; } = [File, Edit, View, Run, Plugins, Help];

    private static readonly KindTable<MenuItem> Table = new(nameof(EditorMenus), "Menu item", "add it with EditorMenus.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a menu item is data, read as it is");

    /// <summary>Raised after an item was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="item"/> (over an item of the same menu and command).</summary>
    public static void Register(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.CommandId);
        Table.Register(item, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the item of <paramref name="menu"/> that runs <paramref name="commandId"/>; false when there was none.</summary>
    public static bool Unregister(string menu, string commandId)
    {
        bool removed = Table.Unregister($"{menu}/{commandId}");
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered items' names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>Every registered item.</summary>
    public static IReadOnlyList<MenuItem> All => Table.Kinds;

    /// <summary>The item of that name ("File/craft.openFile"), or null.</summary>
    public static MenuItem? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The item of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static MenuItem Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The menus that have items: the standard ones first (each even when empty), then others by first registration.</summary>
    public static IReadOnlyList<string> Menus()
    {
        var menus = new List<string>(Standard);
        foreach (var item in Table.Kinds)
        {
            if (!menus.Contains(item.Menu, StringComparer.OrdinalIgnoreCase))
            {
                menus.Add(item.Menu);
            }
        }

        return menus;
    }

    /// <summary>The items of <paramref name="menu"/>, by group, then order, then title.</summary>
    public static IReadOnlyList<MenuItem> ItemsOf(string menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var items = new List<MenuItem>();
        foreach (var item in Table.Kinds)
        {
            if (item.Menu.Equals(menu, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        items.Sort(static (a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group)
            : a.Order != b.Order ? a.Order.CompareTo(b.Order)
            : string.Compare(a.Title ?? a.CommandId, b.Title ?? b.CommandId, StringComparison.OrdinalIgnoreCase));
        return items;
    }
}

/// <summary>
/// An item of the activity rail (<see cref="EditorRailItems"/>): an icon button that runs a command. It shows only while
/// its command is registered (so "Source control" or "Agent" appear when a plug-in brings them).
/// </summary>
/// <param name="Id">Its id ("craft.explorer").</param>
/// <param name="Title">Its tooltip and accessible name ("Explorer").</param>
/// <param name="CommandId">The command a click runs.</param>
/// <param name="Icon">Its icon as SVG path data on a 24 by 24 box, drawn as a 1.6-wide stroke (<see cref="RailIcons"/> has the design's).</param>
/// <param name="Order">Its order: lower first.</param>
/// <param name="AtBottom">Whether it sits at the rail's bottom (Settings).</param>
public sealed record RailItem(string Id, string Title, string CommandId, string Icon, int Order = 0, bool AtBottom = false) : IKind
{
    /// <inheritdoc />
    public string Name => Id;

    /// <inheritdoc />
    public string Summary => $"{Title}: {CommandId}";
}

/// <summary>The design's rail icons (docs/design/Main.dc.html), SVG path data on a 24 by 24 box, drawn as strokes.</summary>
public static class RailIcons
{
    /// <summary>A folder.</summary>
    public const string Explorer = "M4 4h6l2 2h8v13H4z";

    /// <summary>A magnifier.</summary>
    public const string Search = "M5 11a6 6 0 1 0 12 0a6 6 0 1 0 -12 0M20 20l-4.5-4.5";

    /// <summary>A branch.</summary>
    public const string SourceControl = "M4 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M4 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M16 9a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M6 7v10M18 11c0 4-6 3-12 6";

    /// <summary>A spark.</summary>
    public const string Agent = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z";

    /// <summary>A plug.</summary>
    public const string Plugins = "M9 3v4M15 3v4M6 7h12v4a6 6 0 0 1-12 0zM12 17v4";

    /// <summary>A gear.</summary>
    public const string Settings = "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1";
}

/// <summary>The activity rail's items, by id (ignoring case). The app registers its own; a plug-in adds one in its registration's parts.</summary>
public static class EditorRailItems
{
    private static readonly KindTable<RailItem> Table = new(nameof(EditorRailItems), "Rail item", "add it with EditorRailItems.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a rail item is data, read as it is");

    /// <summary>Raised after an item was registered or removed (on the registering thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="item"/> under its id.</summary>
    public static void Register(RailItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        Table.Register(item, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the item of that id; false when there was none.</summary>
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

    /// <summary>The registered items, by order (the bottom ones last).</summary>
    public static IReadOnlyList<RailItem> All
    {
        get
        {
            var items = new List<RailItem>(Table.Kinds);
            items.Sort(static (a, b) => a.AtBottom != b.AtBottom ? a.AtBottom.CompareTo(b.AtBottom) : a.Order.CompareTo(b.Order));
            return items;
        }
    }

    /// <summary>The item of that id, or null.</summary>
    public static RailItem? Find(ReadOnlySpan<char> id) => Table.Find(id);

    /// <summary>The item of that id.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static RailItem Get(ReadOnlySpan<char> id) => Table.Get(id);
}
