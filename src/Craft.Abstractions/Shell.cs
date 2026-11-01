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

    /// <summary>
    /// Its icon (X part 7): SVG path data on a 24 by 24 box drawn as a stroke in the design's style (<see cref="MenuIcons"/>
    /// has the design's), <see cref="MenuIcons.File"/>'s "file:Name.cs" for that file type's icon (<see cref="FileIcons"/>),
    /// or <see cref="MenuIcons.Folder"/>; null for none.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// The menu whose rows open beside it (X part 7: hover or Right opens it, Left closes it), or null for a row that runs
    /// its command. A submenu's row needs no registered command: <see cref="CommandId"/> is its id; it shows disabled when
    /// its submenu has no row that can run.
    /// </summary>
    public string? Submenu { get; init; }

    /// <summary>When it shows, over what the menu was opened on (a file, a folder, a language, a project kind); null: always.</summary>
    public MenuWhen? When { get; init; }

    /// <summary>
    /// What its command acts on beside the target (a language's name for "Open with", a run target's key): set as
    /// <see cref="ContextMenuTarget.Argument"/> while the command starts, so one command serves many rows; null for none.
    /// </summary>
    public string? Argument { get; init; }
}

/// <summary>What a context menu's row can be shown on (<see cref="MenuWhen.Targets"/>).</summary>
[Flags]
public enum MenuTargetKinds
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A file (a tree row's, a tab's, the editor's).</summary>
    File = 1,

    /// <summary>A folder of the workspace (not its own folder).</summary>
    Folder = 2,

    /// <summary>The workspace's own folder, or the tree's empty space.</summary>
    Root = 4,

    /// <summary>Anything.</summary>
    Any = File | Folder | Root,
}

/// <summary>
/// When a menu's row shows (X part 7), over its <see cref="ContextMenuTarget"/>: the kinds of target, a file's names or
/// endings, its language, the projects it belongs to (the run providers that have a target for it), and any test of the
/// plug-in's own. Every condition given must hold; one left empty holds always. Immutable, any thread.
/// </summary>
public sealed record MenuWhen
{
    /// <summary>On files only.</summary>
    public static MenuWhen Files { get; } = new() { Targets = MenuTargetKinds.File };

    /// <summary>On folders of the workspace only.</summary>
    public static MenuWhen Folders { get; } = new() { Targets = MenuTargetKinds.Folder };

    /// <summary>On the workspace's own folder (and the tree's empty space) only.</summary>
    public static MenuWhen Root { get; } = new() { Targets = MenuTargetKinds.Root };

    /// <summary>On files and folders of the workspace, not its own folder.</summary>
    public static MenuWhen Entries { get; } = new() { Targets = MenuTargetKinds.File | MenuTargetKinds.Folder };

    /// <summary>On folders and the workspace's own folder.</summary>
    public static MenuWhen AnyFolder { get; } = new() { Targets = MenuTargetKinds.Folder | MenuTargetKinds.Root };

    /// <summary>The kinds of target it shows on.</summary>
    public MenuTargetKinds Targets { get; init; } = MenuTargetKinds.Any;

    /// <summary>A file's name endings, ignoring case (".csproj", ".d.ts"); empty: any. A folder never matches a non-empty list.</summary>
    public IReadOnlyList<string> Endings { get; init; } = [];

    /// <summary>The languages (<see cref="ILanguage"/>'s names, ignoring case) of the files it shows on; empty: any.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>The project kinds (run providers' names, ignoring case: "dotnet", "npm") one of which must have a target for it; empty: any.</summary>
    public IReadOnlyList<string> ProjectKinds { get; init; } = [];

    /// <summary>A test of the plug-in's own, on the UI thread while the menu opens (no disk, no wait), or null.</summary>
    public Func<ContextMenuTarget, bool>? Test { get; init; }

    /// <summary>Whether a row of this condition shows on <paramref name="target"/> (null: a menu opened on nothing, such as the title bar's).</summary>
    public bool Matches(ContextMenuTarget? target)
    {
        if (target is null)
        {
            return Targets == MenuTargetKinds.Any && Endings.Count == 0 && Languages.Count == 0 && ProjectKinds.Count == 0 && Test is null;
        }

        if ((Targets & target.Kind) == 0)
        {
            return false;
        }

        if (Endings.Count > 0 && (target.Kind != MenuTargetKinds.File || target.Path is not { } path || !EndsWithAny(path, Endings)))
        {
            return false;
        }

        if (Languages.Count > 0 && (target.Language is not { } language || !Contains(Languages, language)))
        {
            return false;
        }

        if (ProjectKinds.Count > 0 && !target.ProjectKinds.Any(kind => Contains(ProjectKinds, kind)))
        {
            return false;
        }

        return Test?.Invoke(target) ?? true;
    }

    private static bool EndsWithAny(string path, IReadOnlyList<string> endings)
    {
        foreach (var ending in endings)
        {
            if (path.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        foreach (var each in names)
        {
            if (each.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Rows a menu gets as it opens (X part 7), made for what it was opened on: the run targets of a file, the languages of
/// "Open with". Registered in <see cref="EditorMenuSources"/>; its rows are <see cref="MenuItem"/>s of its
/// <see cref="Menu"/> (usually a submenu), each running a command with its <see cref="MenuItem.Argument"/>.
/// </summary>
/// <param name="Name">Its name ("craft.run").</param>
/// <param name="Menu">The menu it adds rows to.</param>
/// <param name="Items">The rows for a target, asked on the UI thread while the menu opens (from what is in memory: no disk, no wait).</param>
public sealed record MenuItemSource(string Name, string Menu, Func<ContextMenuTarget?, IReadOnlyList<MenuItem>> Items) : IKind
{
    /// <inheritdoc />
    public string Summary => $"{Menu}: rows made as it opens";
}

/// <summary>The menus' sources of rows made as a menu opens (<see cref="MenuItemSource"/>), by name (ignoring case).</summary>
public static class EditorMenuSources
{
    private static readonly KindTable<MenuItemSource> Table = new(nameof(EditorMenuSources), "Menu source", "add it with EditorMenuSources.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a menu source is asked on the UI thread as a menu opens");

    /// <summary>Registers <paramref name="source"/> under its name.</summary>
    public static void Register(MenuItemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Menu);
        ArgumentNullException.ThrowIfNull(source.Items);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the source of that name; false when there was none.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>Every registered source.</summary>
    public static IReadOnlyList<MenuItemSource> All => Table.Kinds;

    /// <summary>The source of that name, or null.</summary>
    public static MenuItemSource? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static MenuItemSource Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The rows every source of <paramref name="menu"/> gives for <paramref name="target"/>, sources in order (a source that throws gives none).</summary>
    public static IReadOnlyList<MenuItem> ItemsFor(string menu, ContextMenuTarget? target)
    {
        ArgumentNullException.ThrowIfNull(menu);
        List<MenuItem>? items = null;
        foreach (var source in Table.Kinds)
        {
            if (!source.Menu.Equals(menu, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<MenuItem> made;
            try
            {
                made = source.Items(target);
            }
#pragma warning disable CA1031 // A source that fails gives no rows; the menu still opens.
            catch (Exception)
#pragma warning restore CA1031
            {
                continue;
            }

            (items ??= []).AddRange(made);
        }

        return items is null ? [] : items;
    }
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

    /// <summary>
    /// The title bar's menus that have items: the standard ones first (each even when empty), then others by first
    /// registration; the context menus (<see cref="EditorContextMenus"/>) are not the title bar's.
    /// </summary>
    public static IReadOnlyList<string> Menus()
    {
        var menus = new List<string>(Standard);
        foreach (var item in Table.Kinds)
        {
            if (!EditorContextMenus.IsContextMenu(item.Menu) && !menus.Contains(item.Menu, StringComparer.OrdinalIgnoreCase))
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
/// The context menus (phase X): the menus a right-click (or the menu key) opens on the editor, a row of the folder tree
/// and a tab. Their items are <see cref="MenuItem"/>s of these menu names in <see cref="EditorMenus"/>, each running a
/// command, so a plug-in adds its own the same way it adds a title-bar item. While a picked item's command starts, the
/// host sets <see cref="Target"/> to what the menu was opened on; a command reads it before its first <c>await</c>.
/// </summary>
public static class EditorContextMenus
{
    /// <summary>The prefix of every context menu's name (such a menu is never a title-bar menu).</summary>
    public const string Prefix = "context:";

    /// <summary>The text editor's menu (the selection's commands: cut, copy, paste, go to definition, the AI actions).</summary>
    public const string Editor = "context:editor";

    /// <summary>The folder tree's menu (a file's or a folder's row, or the tree's empty space).</summary>
    public const string Tree = "context:tree";

    /// <summary>A document tab's menu.</summary>
    public const string Tab = "context:tab";

    /// <summary>
    /// A read-only text's menu (the bottom panel's Output): copy, select all and find, acting on the text it was opened on
    /// (its editor is the target's <see cref="ContextMenuTarget.Item"/>); nothing that edits.
    /// </summary>
    public const string ReadOnlyText = "context:readonly-text";

    /// <summary>A message of the chat or the agent (X part 10: copy, copy as Markdown, quote, retry, edit and resend, delete from here).</summary>
    public const string Conversation = "context:conversation";

    /// <summary>The conversation panel's own menu (its header's "⋯": copy and export the conversation, the past conversations).</summary>
    public const string ConversationPanel = "context:conversation-panel";

    /// <summary>
    /// A terminal's menu (B9: copy, paste, select all, clear, split, rename, kill), acting on the terminal it was opened on
    /// (the target's <see cref="ContextMenuTarget.Item"/>), else the active one.
    /// </summary>
    public const string Terminal = "context:terminal";

    /// <summary>The terminal's new-terminal menu (B9: the "+" button's chevron): one row per profile, then "Set default profile…".</summary>
    public const string TerminalProfiles = "context:terminal-profiles";

    /// <summary>
    /// The tree's and the tab's submenu "Open with" (X part 7): one row per language, the file opened in it. Its rows come
    /// from a source (<see cref="EditorMenuSources"/>); a plug-in adds its own rows the same way.
    /// </summary>
    public const string OpenWith = "context:open-with";

    /// <summary>
    /// The tree's and the tab's submenu "Run" (X part 7): the run, build and test of the file's or the folder's run targets
    /// (the Run view's providers), made as the menu opens.
    /// </summary>
    public const string RunTargets = "context:run";

    private static ContextMenuTarget? _target;

    /// <summary>What the context menu whose item is starting was opened on, or null (the command acts on the active document).</summary>
    public static ContextMenuTarget? Target
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, value);
    }

    /// <summary>Whether <paramref name="menu"/> names a context menu (<see cref="Prefix"/>).</summary>
    public static bool IsContextMenu(string menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return menu.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>What a context menu was opened on (<see cref="EditorContextMenus.Target"/>).</summary>
/// <param name="Menu">The menu (<see cref="EditorContextMenus.Editor"/>, <see cref="EditorContextMenus.Tree"/>, <see cref="EditorContextMenus.Tab"/>).</param>
/// <param name="Path">The file or folder it is about (a tree row's, a tab's file), or null.</param>
/// <param name="IsFolder">Whether <paramref name="Path"/> is a folder.</param>
/// <param name="Item">The host's own object it was opened on (a tab), or null.</param>
public sealed record ContextMenuTarget(string Menu, string? Path = null, bool IsFolder = false, object? Item = null)
{
    /// <summary>Whether it is the workspace's own folder (or the tree's empty space: then <see cref="Path"/> is the workspace's folder).</summary>
    public bool IsRoot { get; init; }

    /// <summary>The file's language (its <see cref="ILanguage"/>'s name), or null (a folder, plain text, not described).</summary>
    public string? Language { get; init; }

    /// <summary>The project kinds it belongs to: the run providers' names that have a target for it (a file's own, else its nearest folder's; a folder's within it).</summary>
    public IReadOnlyList<string> ProjectKinds { get; init; } = [];

    /// <summary>The picked row's <see cref="MenuItem.Argument"/> while its command starts, else null.</summary>
    public string? Argument { get; init; }

    /// <summary>What kind of target it is for <see cref="MenuWhen"/>: the root, a folder, else a file (an untitled tab's too).</summary>
    public MenuTargetKinds Kind => IsRoot || (Path is null && Menu.Equals(EditorContextMenus.Tree, StringComparison.OrdinalIgnoreCase)) ? MenuTargetKinds.Root
        : IsFolder ? MenuTargetKinds.Folder
        : MenuTargetKinds.File;
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

    /// <summary>A plug-in panel's side view (three lines of a list): the rail item the shell gives a panel that brings none.</summary>
    public const string Panel = "M4 6h16M4 12h16M4 18h10";

    /// <summary>The Run view (a triangle pointing right: play; X part 6).</summary>
    public const string Run = "M7 4.5v15l12-7.5z";

    /// <summary>A build (a hammer; the Run view's Build).</summary>
    public const string Build = "M14 6l4 4M4 20l9-9M12 4l8 8l-2 2l-8-8z";

    /// <summary>Stop (a square; a run's Stop).</summary>
    public const string Stop = "M6 6h12v12H6z";

    /// <summary>A test (a flask; the Run view's Test).</summary>
    public const string Test = "M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3";

    /// <summary>Replace (an arrow turning back; the Search view's Replace).</summary>
    public const string Replace = "M4 7h11a4 4 0 0 1 0 8H8M8 15l3-3M8 15l3 3";

    /// <summary>Close or dismiss (a cross).</summary>
    public const string Close = "M6 6l12 12M18 6L6 18";

    /// <summary>Edit (a pencil).</summary>
    public const string Edit = "M4 20h4L19 9l-4-4L4 16zM14 6l4 4";

    /// <summary>Add (a plus).</summary>
    public const string Add = "M12 5v14M5 12h14";

    /// <summary>Folding everything (lines drawn together).</summary>
    public const string Collapse = "M4 6h16M8 12h8M10 18h4";

    /// <summary>Refresh (a circular arrow).</summary>
    public const string Refresh = "M20 11a8 8 0 1 0-2.3 5.7M20 5v6h-6";
}

/// <summary>
/// The design's command icons for menus (X part 7), SVG path data on a 24 by 24 box drawn as 1.5-wide strokes like the
/// rail's (<see cref="RailIcons"/>), and the forms that show a file type's or a folder's icon instead.
/// </summary>
public static class MenuIcons
{
    /// <summary>The prefix of an icon that is a file type's (<see cref="FileIcons"/>): "file:" and a file name.</summary>
    public const string FilePrefix = "file:";

    /// <summary>A folder's icon.</summary>
    public const string Folder = RailIcons.Explorer;

    /// <summary>The icon of the file type of <paramref name="name"/> ("Program.cs").</summary>
    public static string File(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return FilePrefix + name;
    }

    /// <summary>The file name a file type's icon names, or null for a stroke icon.</summary>
    public static string? FileOf(string? icon) => icon is not null && icon.StartsWith(FilePrefix, StringComparison.Ordinal) ? icon[FilePrefix.Length..] : null;

    /// <summary>A page with a folded corner (open a file).</summary>
    public const string Open = "M6 3h8l4 4v14H6zM14 3v4h4";

    /// <summary>A page with a plus (a new file).</summary>
    public const string NewFile = "M6 3h8l4 4v14H6zM14 3v4h4M12 11v6M9 14h6";

    /// <summary>A folder with a plus (a new folder).</summary>
    public const string NewFolder = "M3 6h6l2 2h10v11H3zM12 11v6M9 14h6";

    /// <summary>A page with an arrow (open with).</summary>
    public const string OpenWith = "M6 3h8l4 4v14H6zM14 3v4h4M9 14h6M12 11l3 3l-3 3";

    /// <summary>A window split in two columns (open to the side, split right).</summary>
    public const string SplitRight = "M4 5h16v14H4zM12 5v14";

    /// <summary>A window split in two rows (split down).</summary>
    public const string SplitDown = "M4 5h16v14H4zM4 12h16";

    /// <summary>A window with an arrow into its other half (move to the other group).</summary>
    public const string MoveToGroup = "M4 5h16v14H4zM12 5v14M6 12h8M11 9l3 3l-3 3";

    /// <summary>A prompt (open in terminal).</summary>
    public const string Terminal = "M4 5h16v14H4zM7 10l3 2l-3 2M12 15h5";

    /// <summary>A folder with an arrow out (reveal in the system's file manager).</summary>
    public const string Reveal = "M3 6h6l2 2h10v11H3zM13 14h6M16 11l3 3l-3 3";

    /// <summary>A crosshair on a list (reveal in the explorer).</summary>
    public const string RevealInTree = "M4 6h7M4 12h5M4 18h7M17 12a3 3 0 1 0 0.01 0M17 7v2M17 15v2M12 12h2M20 12h2";

    /// <summary>Lines made even (format).</summary>
    public const string Format = "M4 6h16M8 10h12M8 14h12M4 18h16";

    /// <summary>A speech bubble (the chat).</summary>
    public const string Chat = "M4 5h16v11H9l-5 4z";

    /// <summary>Scissors (cut).</summary>
    public const string Cut = "M8.5 6a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0M8.5 18a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0M8 7.5L20 18M8 16.5L20 6";

    /// <summary>Two pages (copy).</summary>
    public const string Copy = "M8 8h12v12H8zM16 8V4H4v12h4";

    /// <summary>A clipboard (paste).</summary>
    public const string Paste = "M9 3h6v3H9zM7 5H5v16h14V5h-2";

    /// <summary>Two pages with a plus (duplicate).</summary>
    public const string Duplicate = "M8 8h12v12H8zM16 8V4H4v12h4M14 11v6M11 14h6";

    /// <summary>Two columns (compare).</summary>
    public const string Compare = "M4 4h7v16H4zM13 4h7v16h-7zM6 9h3M15 14h3";

    /// <summary>Two columns, one marked (select for compare).</summary>
    public const string SelectForCompare = "M4 4h7v16H4zM13 4h7v16h-7zM6 12l1.5 1.5L10 11";

    /// <summary>A letter T (copy the name).</summary>
    public const string Name = "M5 7V5h14v2M12 5v14M9 19h6";

    /// <summary>A link (copy a path).</summary>
    public const string Path = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1";

    /// <summary>A pencil (rename).</summary>
    public const string Rename = RailIcons.Edit;

    /// <summary>A bin (delete).</summary>
    public const string Delete = "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6";

    /// <summary>A cross (close).</summary>
    public const string Close = RailIcons.Close;

    /// <summary>Two pages crossed (close others, close all).</summary>
    public const string CloseMany = "M8 8h12v12H8zM16 8V4H4v12h4M11 11l6 6M17 11l-6 6";

    /// <summary>A page with a tick (close the saved ones).</summary>
    public const string CloseSaved = "M6 3h8l4 4v14H6zM14 3v4h4M9 14l2 2l4-4";

    /// <summary>An arrow turning back up (reopen the closed tab).</summary>
    public const string Reopen = "M4 12a8 8 0 1 0 2.3-5.7M4 4v5h5";

    /// <summary>A pin (pin a tab).</summary>
    public const string Pin = "M9 4h6l-1 6l4 4H6l4-4zM12 14v7";

    /// <summary>A pin struck through (unpin).</summary>
    public const string Unpin = "M9 4h6l-1 6l4 4H6l4-4zM12 14v7M4 4l16 16";

    /// <summary>An arrow turning back (revert).</summary>
    public const string Revert = "M4 8h10a6 6 0 0 1 0 12H8M4 8l4-4M4 8l4 4";

    /// <summary>A magnifier (find in folder).</summary>
    public const string Find = RailIcons.Search;
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
