// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>Where a new editor group goes beside the one split.</summary>
public enum SplitDirection
{
    /// <summary>To its right (Split right, Ctrl+\).</summary>
    Right,

    /// <summary>Under it (Split down).</summary>
    Down,
}

/// <summary>How a split lays its children out.</summary>
public enum GroupOrientation
{
    /// <summary>Side by side, left to right.</summary>
    Row,

    /// <summary>One over the other, top to bottom.</summary>
    Column,
}

/// <summary>Where a tab dropped on an editor group goes (the drop zones a drag shows).</summary>
public enum EditorDropZone
{
    /// <summary>Not over the group.</summary>
    None,

    /// <summary>Into the group (its tabs).</summary>
    Centre,

    /// <summary>A new group to its right.</summary>
    Right,

    /// <summary>A new group under it.</summary>
    Down,
}

/// <summary>One editor group: its own tabs and active tab (<see cref="DocumentTabs"/>), and the UI's pane for it.</summary>
public sealed class EditorGroup
{
    internal EditorGroup(int id, DocumentTabs tabs)
    {
        Id = id;
        Tabs = tabs;
    }

    /// <summary>The group's id, stable while it is open (the first group is 1; a new group takes the next unused).</summary>
    public int Id { get; }

    /// <summary>Its tabs.</summary>
    public DocumentTabs Tabs { get; }

    /// <summary>The UI's own object for the group (its pane); the groups never read it.</summary>
    public object? View { get; set; }

    internal EditorGroupNode? Node { get; set; }
}

/// <summary>
/// A node of the editor area's layout: a group (a leaf), or a split of two or more children side by side
/// (<see cref="GroupOrientation.Row"/>) or stacked (<see cref="GroupOrientation.Column"/>), each with its share of the room
/// (<see cref="Weights"/>, summing to 1).
/// </summary>
public sealed class EditorGroupNode
{
    private readonly List<EditorGroupNode> _children = [];
    private readonly List<double> _weights = [];

    internal EditorGroupNode(EditorGroup group)
    {
        Group = group;
        group.Node = this;
    }

    internal EditorGroupNode(GroupOrientation orientation) => Orientation = orientation;

    /// <summary>The group of a leaf, or null for a split.</summary>
    public EditorGroup? Group { get; }

    /// <summary>How a split lays its children out (a leaf's is <see cref="GroupOrientation.Row"/>).</summary>
    public GroupOrientation Orientation { get; internal set; }

    /// <summary>A split's children, in order (none for a leaf).</summary>
    public IReadOnlyList<EditorGroupNode> Children => _children;

    /// <summary>Each child's share of the split's room, in order, summing to 1.</summary>
    public IReadOnlyList<double> Weights => _weights;

    /// <summary>The split holding it, or null for the root.</summary>
    public EditorGroupNode? Parent { get; private set; }

    internal void Insert(int index, EditorGroupNode child, double weight)
    {
        child.Parent = this;
        _children.Insert(index, child);
        _weights.Insert(index, weight);
    }

    internal void RemoveAt(int index)
    {
        _children[index].Parent = null;
        _children.RemoveAt(index);
        _weights.RemoveAt(index);
    }

    internal void Replace(int index, EditorGroupNode child)
    {
        _children[index].Parent = null;
        child.Parent = this;
        _children[index] = child;
    }

    internal void SetWeight(int index, double weight) => _weights[index] = weight;

    internal void Detach() => Parent = null;

    internal void Normalize()
    {
        double sum = 0;
        foreach (var weight in _weights)
        {
            sum += weight;
        }

        for (int i = 0; i < _weights.Count; i++)
        {
            _weights[i] = sum > 0 ? _weights[i] / sum : 1.0 / _weights.Count;
        }
    }
}

/// <summary>
/// The editor area's groups (the X restart's split editor, piece 3a): each group its own tabs and active tab, one focused.
/// The focused group's active tab is the host's active document, so find, the breadcrumb, quick open, the status bar and
/// the agent's active document follow the focus. A document shown in two groups is one text document and one host
/// document in two tabs (<see cref="DocumentTabs"/>), closed with the last of them; its views share one language session
/// (<see cref="LanguageSessionPool"/>) and keep their own carets, scroll and folds. Split right, split down, a tab moved to
/// the next group or dropped on a group's zone, close a group, focus a group by its number; at most <see cref="Max"/>
/// groups (<c>CRAFT_EDITOR_GROUPS_MAX</c>). The layout (a tree of rows and columns with each child's share) is kept per
/// workspace (<see cref="Capture()"/>, <see cref="EditorLayoutFile"/>). Used on the UI thread.
/// </summary>
public sealed class EditorGroups : IDisposable
{
    private readonly EditorHost _host;
    private readonly List<EditorGroup> _groups = [];
    private EditorGroupNode _root;
    private EditorGroup _focused;
    private int _nextUntitled = 1;
    private int _nextId = 1;
    private int _max;

    /// <summary>The groups of <paramref name="host"/>: one at first, at most <paramref name="max"/>.</summary>
    public EditorGroups(EditorHost host, int max = 4)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Max = max;
        _focused = NewGroup();
        _root = new EditorGroupNode(_focused);
        _groups.Add(_focused);
    }

    /// <summary>The most groups (<c>CRAFT_EDITOR_GROUPS_MAX</c>; at least 1).</summary>
    public int Max
    {
        get => _max;
        set => _max = Math.Max(1, value);
    }

    /// <summary>Whether a group closes when its last tab closes while another is open (<c>CRAFT_EDITOR_GROUPS_CLOSE_EMPTY</c>).</summary>
    public bool CloseEmpty { get; set; } = true;

    /// <summary>The groups in the layout's order (left to right, top to bottom); never empty.</summary>
    public IReadOnlyList<EditorGroup> Groups => _groups;

    /// <summary>The focused group (its tabs are <see cref="ShellModel.Tabs"/>).</summary>
    public EditorGroup Focused => _focused;

    /// <summary>The layout's root (a single group's leaf, or a split).</summary>
    public EditorGroupNode Root => _root;

    /// <summary>Whether a tab of any group has unsaved changes.</summary>
    public bool HasUnsaved => _groups.Exists(g => g.Tabs.HasUnsaved);

    /// <summary>Raised after a group was added or closed (the layout's shape changed).</summary>
    public event EventHandler? GroupsChanged;

    /// <summary>Raised after the focus moved to another group.</summary>
    public event EventHandler? FocusChanged;

    /// <summary>Raised after a split's shares changed (a splitter dragged).</summary>
    public event EventHandler? LayoutResized;

    /// <summary>Raised when a group's tabs were added or removed.</summary>
    public event EventHandler<EditorGroup>? TabsChanged;

    /// <summary>Raised when a group's active tab changed.</summary>
    public event EventHandler<EditorGroup>? ActiveChanged;

    /// <summary>Raised when a tab's title or dirty marker changed, with its group.</summary>
    public event EventHandler<(EditorGroup Group, DocumentTab Tab)>? TabChanged;

    /// <summary>Every tab of every group, group by group.</summary>
    public IEnumerable<DocumentTab> AllTabs
    {
        get
        {
            foreach (var group in _groups)
            {
                foreach (var tab in group.Tabs.Tabs)
                {
                    yield return tab;
                }
            }
        }
    }

    /// <summary>One tab per open document (the first group's that shows it), in the groups' order.</summary>
    public IReadOnlyList<DocumentTab> DocumentTabs()
    {
        var seen = new HashSet<ITextDocument>(ReferenceEqualityComparer.Instance);
        var tabs = new List<DocumentTab>();
        foreach (var tab in AllTabs)
        {
            if (seen.Add(tab.Document))
            {
                tabs.Add(tab);
            }
        }

        return tabs;
    }

    /// <summary>The group holding <paramref name="tab"/>, or null.</summary>
    public EditorGroup? GroupOf(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        foreach (var group in _groups)
        {
            if (group.Tabs.Contains(tab))
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>The group's number, from 1 in the layout's order (Ctrl+1 focuses the first).</summary>
    public int NumberOf(EditorGroup group) => _groups.IndexOf(group) + 1;

    /// <summary>The tabs showing <paramref name="document"/>, in every group.</summary>
    public int CountOf(ITextDocument document)
    {
        int count = 0;
        foreach (var tab in AllTabs)
        {
            if (ReferenceEquals(tab.Document, document))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether another tab (another group's) shows <paramref name="tab"/>'s document.</summary>
    public bool IsShared(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        foreach (var other in AllTabs)
        {
            if (!ReferenceEquals(other, tab) && ReferenceEquals(other.Document, tab.Document))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A tab of the file <paramref name="path"/> in any group (the focused group's first), or null.</summary>
    public DocumentTab? FindOpen(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_focused.Tabs.Find(path) is { } here)
        {
            return here;
        }

        foreach (var group in _groups)
        {
            if (group.Tabs.Find(path) is { } tab)
            {
                return tab;
            }
        }

        return null;
    }

    /// <summary>The new document's tab made for <paramref name="path"/> in any group, or null.</summary>
    public DocumentTab? FindSuggested(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        foreach (var group in _groups)
        {
            if (group.Tabs.FindSuggested(path) is { } tab)
            {
                return tab;
            }
        }

        return null;
    }

    /// <summary>Focuses <paramref name="group"/> (its active tab becomes the host's active document); false when it is focused already or not open.</summary>
    public bool Focus(EditorGroup group) => FocusCore(group, setHost: true);

    /// <summary>
    /// Focuses the group numbered <paramref name="number"/> (Ctrl+1, Ctrl+2, Ctrl+3); the number after the last makes a
    /// new group to the right of the last showing the focused tab (VS Code's), within <see cref="Max"/>. False when there is
    /// no such group and none can be made.
    /// </summary>
    public bool FocusNumber(int number)
    {
        if (number >= 1 && number <= _groups.Count)
        {
            FocusCore(_groups[number - 1], setHost: true);
            return true;
        }

        if (number == _groups.Count + 1 && _groups.Count < Max)
        {
            return Split(_groups[^1], SplitDirection.Right, _focused.Tabs.Active) is not null;
        }

        return false;
    }

    /// <summary>Focuses the next group (after the last, the first); false with one group.</summary>
    public bool FocusNext()
    {
        if (_groups.Count < 2)
        {
            return false;
        }

        return FocusCore(_groups[(_groups.IndexOf(_focused) + 1) % _groups.Count], setHost: true);
    }

    /// <summary>
    /// A new group beside <paramref name="from"/> (to its right or under it), focused, showing <paramref name="tab"/> when
    /// given: moved there when <paramref name="move"/> (its view, carets and scroll go with it), else shown there too (one
    /// document in two groups). Null when <see cref="Max"/> groups are open.
    /// </summary>
    public EditorGroup? Split(EditorGroup from, SplitDirection direction, DocumentTab? tab = null, bool move = false)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (_groups.Count >= Max || from.Node is not { } leaf)
        {
            return null;
        }

        var orientation = direction == SplitDirection.Right ? GroupOrientation.Row : GroupOrientation.Column;
        var group = NewGroup();
        var node = new EditorGroupNode(group);
        if (leaf.Parent is { } parent && parent.Orientation == orientation)
        {
            // Beside it in the same row (or column): the new group takes half of the split one's share.
            int at = IndexIn(parent, leaf);
            double half = parent.Weights[at] / 2;
            parent.SetWeight(at, half);
            parent.Insert(at + 1, node, half);
        }
        else
        {
            var split = new EditorGroupNode(orientation);
            if (leaf.Parent is { } holder)
            {
                holder.Replace(IndexIn(holder, leaf), split);
            }
            else
            {
                _root = split;
            }

            split.Insert(0, leaf, 0.5);
            split.Insert(1, node, 0.5);
        }

        Reorder();
        FocusCore(group, setHost: true);
        GroupsChanged?.Invoke(this, EventArgs.Empty);
        if (tab is not null && GroupOf(tab) is { } source)
        {
            group.Tabs.AddShared(tab, activate: true, takeView: move);
            if (move)
            {
                source.Tabs.Remove(tab);                  // shown by the new group: the document stays open
                CloseIfEmpty(source);
            }
        }

        return group;
    }

    /// <summary>
    /// Moves <paramref name="tab"/> into <paramref name="target"/> (at <paramref name="index"/>, else after its tabs), its
    /// view with it, and focuses it there; a target already showing the document activates its own tab. The source group
    /// closes when it is left empty (<see cref="CloseEmpty"/>). False when it is already there.
    /// </summary>
    public bool MoveTab(DocumentTab tab, EditorGroup target, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(target);
        if (GroupOf(tab) is not { } source || ReferenceEquals(source, target) || !_groups.Contains(target))
        {
            return false;
        }

        if (target.Tabs.Showing(tab.Document) is { } existing)
        {
            target.Tabs.Activate(existing);
        }
        else
        {
            target.Tabs.AddShared(tab, activate: true, takeView: true, index);
        }

        source.Tabs.Remove(tab);
        CloseIfEmpty(source);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="tab"/> to the next group (after the focused one), or to a new group to its right when it is
    /// the last (within <see cref="Max"/>); the group it lands in, or null.
    /// </summary>
    public EditorGroup? MoveToNext(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (GroupOf(tab) is not { } source)
        {
            return null;
        }

        int at = _groups.IndexOf(source);
        if (at + 1 < _groups.Count)
        {
            var target = _groups[at + 1];
            return MoveTab(tab, target) ? target : null;
        }

        if (source.Tabs.Tabs.Count < 2)
        {
            return null;                                  // its only tab: a new group would leave this one empty
        }

        return Split(source, SplitDirection.Right, tab, move: true);
    }

    /// <summary>
    /// A tab dropped on <paramref name="target"/>'s <paramref name="zone"/>: the centre moves it into the group, the right
    /// or bottom zone splits the group and moves it to the new one. False when nothing changed.
    /// </summary>
    public bool Drop(DocumentTab tab, EditorGroup target, EditorDropZone zone)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(target);
        switch (zone)
        {
            case EditorDropZone.Centre:
                return MoveTab(tab, target);
            case EditorDropZone.Right:
            case EditorDropZone.Down:
                if (GroupOf(tab) is { } source && ReferenceEquals(source, target) && source.Tabs.Tabs.Count < 2)
                {
                    return false;                         // its only tab beside itself: nothing would change
                }

                return Split(target, zone == EditorDropZone.Right ? SplitDirection.Right : SplitDirection.Down, tab, move: true) is not null;
            default:
                return false;
        }
    }

    /// <summary>
    /// The drop zone of a point over a group of <paramref name="width"/> × <paramref name="height"/>: its right
    /// <paramref name="edge"/> share is the right zone, its bottom share the bottom zone (the nearer edge wins where they
    /// meet), the rest the centre.
    /// </summary>
    public static EditorDropZone ZoneAt(double x, double y, double width, double height, double edge = 0.3)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x > width || y > height)
        {
            return EditorDropZone.None;
        }

        double right = (x - (width * (1 - edge))) / (width * edge);
        double down = (y - (height * (1 - edge))) / (height * edge);
        if (right <= 0 && down <= 0)
        {
            return EditorDropZone.Centre;
        }

        return right >= down ? EditorDropZone.Right : EditorDropZone.Down;
    }

    /// <summary>
    /// Removes <paramref name="tab"/> at once from its group (unsaved changes are lost unless another group shows the
    /// document: ask first), closing the group when it is left empty (<see cref="CloseEmpty"/>).
    /// </summary>
    public void Remove(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (GroupOf(tab) is { } group)
        {
            group.Tabs.Remove(tab);
            CloseIfEmpty(group);
        }
    }

    /// <summary>
    /// Closes <paramref name="group"/> when another is open: its remaining tabs close at once (ask about unsaved changes
    /// first), the room goes to its neighbour, the focus to the group before it. False with one group.
    /// </summary>
    public bool Close(EditorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (_groups.Count < 2 || !_groups.Contains(group) || group.Node is not { Parent: { } parent } leaf)
        {
            return false;
        }

        int order = _groups.IndexOf(group);
        group.Tabs.Dispose();
        int at = IndexIn(parent, leaf);
        double share = parent.Weights[at];
        parent.RemoveAt(at);
        parent.SetWeight(Math.Max(0, at - 1), parent.Weights[Math.Max(0, at - 1)] + share);
        if (parent.Children.Count == 1)
        {
            // A split of one: its child takes its place.
            var only = parent.Children[0];
            parent.RemoveAt(0);
            if (parent.Parent is { } grand)
            {
                int place = IndexIn(grand, parent);
                if (only.Group is null && only.Orientation == grand.Orientation)
                {
                    // Its children join the grandparent's row (or column), sharing the place's room.
                    double room = grand.Weights[place];
                    grand.RemoveAt(place);
                    for (int i = 0; i < only.Children.Count; i++)
                    {
                        grand.Insert(place + i, only.Children[i], only.Weights[i] * room);
                    }
                }
                else
                {
                    grand.Replace(place, only);
                }
            }
            else
            {
                only.Detach();
                _root = only;
            }
        }

        group.Node = null;
        Reorder();
        if (ReferenceEquals(_focused, group))
        {
            FocusCore(_groups[Math.Clamp(order - 1, 0, _groups.Count - 1)], setHost: true);
        }

        GroupsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Sets the shares of the two children either side of splitter <paramref name="splitter"/> of <paramref name="split"/>
    /// (a drag): the first takes <paramref name="first"/> of what the two hold together (clamped to 0.05-0.95).
    /// </summary>
    public void Resize(EditorGroupNode split, int splitter, double first)
    {
        ArgumentNullException.ThrowIfNull(split);
        if (splitter < 0 || splitter + 1 >= split.Children.Count)
        {
            return;
        }

        double pair = split.Weights[splitter] + split.Weights[splitter + 1];
        double share = Math.Clamp(first, 0.05, 0.95);
        split.SetWeight(splitter, pair * share);
        split.SetWeight(splitter + 1, pair * (1 - share));
        LayoutResized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives <paramref name="tab"/> the language <paramref name="language"/> (Save As to a new extension): its document is
    /// opened again in the host in that language, and every other group's tab of the document follows.
    /// </summary>
    public void SetLanguage(DocumentTab tab, ILanguage? language, string? languageName = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (GroupOf(tab) is not { } group)
        {
            return;
        }

        group.Tabs.SetLanguage(tab, language, languageName);
        foreach (var other in _groups)
        {
            if (!ReferenceEquals(other, group) && other.Tabs.Showing(tab.Document) is { } sibling && !ReferenceEquals(sibling.HostDocument, tab.HostDocument))
            {
                other.Tabs.Adopt(sibling, tab.HostDocument, language);
            }
        }
    }

    /// <summary>The layout now, for the workspace's file: each group's files (new documents are not kept) and active file, the shares and the focus.</summary>
    public EditorLayoutState Capture() => Capture(null);

    /// <summary>
    /// The layout now, as <see cref="Capture()"/>, with each file's tab state (<paramref name="tabState"/>: its pin, caret,
    /// scroll and folds) beside it when given: the last session (X part 9).
    /// </summary>
    public EditorLayoutState Capture(Func<DocumentTab, EditorTabState?>? tabState) => new(CaptureNode(_root, tabState), Math.Max(0, _groups.IndexOf(_focused)));

    /// <summary>
    /// Shapes the groups as <paramref name="state"/> (a later session's restore): with one group open, the splits are made
    /// (at most <see cref="Max"/> groups; its first group is the one open) and each new group returned with the files it
    /// is to open, in order, and its active file; the caller opens them. Null when more than one group is open.
    /// </summary>
    public IReadOnlyList<(EditorGroup Group, IReadOnlyList<string> Files, string? Active)>? Restore(EditorLayoutState state) =>
        Shape(state) is { } shaped ? [.. shaped.Select(s => (s.Group, s.Node.Files, s.Node.Active))] : null;

    /// <summary>
    /// Shapes the groups as <paramref name="state"/>, as <see cref="Restore"/> does, each new group returned with its node
    /// (its files, active file and the files' tab states); null when more than one group is open.
    /// </summary>
    public IReadOnlyList<(EditorGroup Group, EditorLayoutNodeState Node)>? Shape(EditorLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_groups.Count != 1)
        {
            return null;
        }

        var first = _groups[0];
        var opened = new List<(EditorGroup, EditorLayoutNodeState)>();
        bool usedFirst = false;
        EditorGroupNode Build(EditorLayoutNodeState node)
        {
            if (node.Children.Count == 0 || opened.Count >= Max)
            {
                EditorGroup group;
                if (!usedFirst)
                {
                    usedFirst = true;
                    group = first;
                }
                else
                {
                    group = NewGroup();
                }

                opened.Add((group, node));
                return new EditorGroupNode(group);
            }

            var split = new EditorGroupNode(node.Orientation);
            for (int i = 0; i < node.Children.Count && opened.Count < Max; i++)
            {
                split.Insert(split.Children.Count, Build(node.Children[i]), i < node.Weights.Count ? node.Weights[i] : 1);
            }

            split.Normalize();
            return split.Children.Count == 1 ? Unwrap(split) : split;
        }

        var root = Build(state.Root);
        _root = root;
        Reorder();
        FocusCore(_groups[Math.Clamp(state.Focused, 0, _groups.Count - 1)], setHost: true);
        GroupsChanged?.Invoke(this, EventArgs.Empty);
        return opened;
    }

    /// <summary>Closes every tab of every group at once (unsaved changes are lost: ask first).</summary>
    public void Dispose()
    {
        foreach (var group in _groups.ToArray())
        {
            group.Tabs.Dispose();
        }
    }

    // A group left with no tab closes while another is open (CRAFT_EDITOR_GROUPS_CLOSE_EMPTY).
    internal void CloseIfEmpty(EditorGroup group)
    {
        if (CloseEmpty && group.Tabs.Tabs.Count == 0 && _groups.Count > 1 && _groups.Contains(group))
        {
            Close(group);
        }
    }

    private static EditorGroupNode Unwrap(EditorGroupNode split)
    {
        var only = split.Children[0];
        split.RemoveAt(0);
        return only;
    }

    private static int IndexIn(EditorGroupNode parent, EditorGroupNode child)
    {
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }

    private EditorGroup NewGroup()
    {
        var tabs = new DocumentTabs(_host);
        var group = new EditorGroup(_nextId++, tabs);
        tabs.SharedElsewhere = tab => IsShared(tab);
        tabs.IsFocused = () => ReferenceEquals(_focused, group);
        tabs.FocusRequested = _ => FocusCore(group, setHost: false);
        tabs.NextUntitled = () => _nextUntitled++;
        tabs.TabsChanged += (_, _) => TabsChanged?.Invoke(this, group);
        tabs.ActiveChanged += (_, _) => ActiveChanged?.Invoke(this, group);
        tabs.TabChanged += (_, tab) => TabChanged?.Invoke(this, (group, tab));
        return group;
    }

    private bool FocusCore(EditorGroup group, bool setHost)
    {
        if (ReferenceEquals(_focused, group) || !_groups.Contains(group))
        {
            return false;
        }

        _focused = group;
        if (setHost)
        {
            group.Tabs.TookFocus();
        }

        FocusChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // The groups in the layout's order: a walk of the tree, left to right and top to bottom.
    private void Reorder()
    {
        _groups.Clear();
        Walk(_root);

        void Walk(EditorGroupNode node)
        {
            if (node.Group is { } group)
            {
                group.Node = node;
                _groups.Add(group);
                return;
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }
    }

    private static EditorLayoutNodeState CaptureNode(EditorGroupNode node, Func<DocumentTab, EditorTabState?>? tabState)
    {
        if (node.Group is { } group)
        {
            var files = new List<string>();
            var states = tabState is null ? null : new List<EditorTabState>();
            foreach (var tab in group.Tabs.Tabs)
            {
                if (tab.FilePath is { } path)
                {
                    files.Add(path);
                    states?.Add(tabState!(tab) ?? new EditorTabState(path));
                }
            }

            return new EditorLayoutNodeState(GroupOrientation.Row, [], [], files, group.Tabs.Active?.FilePath) { Tabs = states ?? [] };
        }

        var children = new List<EditorLayoutNodeState>(node.Children.Count);
        foreach (var child in node.Children)
        {
            children.Add(CaptureNode(child, tabState));
        }

        return new EditorLayoutNodeState(node.Orientation, children, [.. node.Weights], [], null);
    }
}

/// <summary>A node of a kept layout: a group's files and active file (no children), or a split's orientation, children and shares.</summary>
/// <param name="Orientation">How a split lays its children out.</param>
/// <param name="Children">A split's children (none for a group).</param>
/// <param name="Weights">Each child's share.</param>
/// <param name="Files">A group's files, in its tabs' order.</param>
/// <param name="Active">A group's active file, or null.</param>
public sealed record EditorLayoutNodeState(GroupOrientation Orientation, IReadOnlyList<EditorLayoutNodeState> Children, IReadOnlyList<double> Weights,
    IReadOnlyList<string> Files, string? Active)
{
    /// <summary>A group's tabs' states in the order of <see cref="Files"/> (the last session's), or none (the workspace's layout keeps none).</summary>
    public IReadOnlyList<EditorTabState> Tabs { get; init; } = [];

    /// <summary>The state kept for <paramref name="path"/>'s tab, or null.</summary>
    public EditorTabState? TabOf(string path)
    {
        foreach (var tab in Tabs)
        {
            if (Core.Workspaces.PathKeys.Comparer.Equals(tab.Path, path))
            {
                return tab;
            }
        }

        return null;
    }
}

/// <summary>
/// A tab as the last session left it (X part 9): its file, its pin, its selection, its scroll offsets and its folded
/// lines (each a fold's first line).
/// </summary>
/// <param name="Path">Its file.</param>
public sealed record EditorTabState(string Path)
{
    /// <summary>Whether it was pinned.</summary>
    public bool Pinned { get; init; }

    /// <summary>Its selection (the caret where it was).</summary>
    public SelectionState Selection { get; init; }

    /// <summary>Its horizontal scroll offset.</summary>
    public float ScrollX { get; init; }

    /// <summary>Its vertical scroll offset.</summary>
    public float ScrollY { get; init; }

    /// <summary>The first lines of its folds, in order.</summary>
    public IReadOnlyList<int> Folds { get; init; } = [];
}

/// <summary>The editor area's layout as kept per workspace: the tree of groups and the focused group's number (from 0).</summary>
/// <param name="Root">The layout's root.</param>
/// <param name="Focused">The focused group's place in the layout's order, from 0.</param>
public sealed record EditorLayoutState(EditorLayoutNodeState Root, int Focused)
{
    /// <summary>The groups the layout holds.</summary>
    public int GroupCount => Count(Root);

    private static int Count(EditorLayoutNodeState node)
    {
        if (node.Children.Count == 0)
        {
            return 1;
        }

        int count = 0;
        foreach (var child in node.Children)
        {
            count += Count(child);
        }

        return count;
    }
}

/// <summary>
/// The editor layout's file in a workspace (<c>.craft/editor-layout.json</c>): written on the thread pool, a later copy
/// never overwritten by an earlier one; read off the UI thread. Paths are kept relative to the workspace when inside it.
/// </summary>
public sealed class EditorLayoutFile
{
    /// <summary>The file's name in the workspace's <c>.craft</c> folder.</summary>
    public const string FileName = "editor-layout.json";

    private readonly object _writing = new();
    private readonly bool _outside;
    private long _version;
    private long _written;

    /// <summary>The file of the workspace <paramref name="root"/>.</summary>
    public EditorLayoutFile(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        FilePath = Path.Combine(Root, Core.Settings.CraftSettings.WorkspaceFolderName, FileName);
    }

    /// <summary>
    /// A layout of the workspace <paramref name="root"/> kept at <paramref name="filePath"/> (X part 9: the last session, in
    /// the user's data folder), its tabs' states written too.
    /// </summary>
    public EditorLayoutFile(string root, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Root = Path.GetFullPath(root);
        FilePath = Path.GetFullPath(filePath);
        _outside = true;
    }

    /// <summary>The workspace's folder.</summary>
    public string Root { get; }

    /// <summary>The file's full path.</summary>
    public string FilePath { get; }

    /// <summary>Writes <paramref name="state"/> on the thread pool (the latest call wins); the task ends when it is on disk.</summary>
    public Task SaveAsync(EditorLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        long version = Interlocked.Increment(ref _version);
        return Task.Run(() =>
        {
            lock (_writing)
            {
                if (version <= _written)
                {
                    return;
                }

                Write(state);
                _written = version;
            }
        });
    }

    /// <summary>Reads the kept layout, or null when there is none or it does not parse (call it off the UI thread).</summary>
    public EditorLayoutState? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            using var stream = File.OpenRead(FilePath);
            using var json = JsonDocument.Parse(stream);
            var top = json.RootElement;
            int focused = top.TryGetProperty("focused", out var f) && f.TryGetInt32(out var number) ? number : 0;
            return top.TryGetProperty("root", out var root) ? new EditorLayoutState(ReadNode(root), focused) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private void Write(EditorLayoutState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            if (_outside)
            {
                writer.WriteString("workspace", Root);          // the session's file names its workspace (never the workspace's own file)
            }

            writer.WriteNumber("focused", state.Focused);
            writer.WritePropertyName("root");
            WriteNode(writer, state.Root);
            writer.WriteEndObject();
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    private void WriteNode(Utf8JsonWriter writer, EditorLayoutNodeState node)
    {
        writer.WriteStartObject();
        if (node.Children.Count == 0)
        {
            writer.WriteStartArray("files");
            foreach (var file in node.Files)
            {
                writer.WriteStringValue(Relative(file));
            }

            writer.WriteEndArray();
            if (node.Active is { } active)
            {
                writer.WriteString("active", Relative(active));
            }

            if (node.Tabs.Count > 0)
            {
                writer.WriteStartArray("tabs");
                foreach (var tab in node.Tabs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("file", Relative(tab.Path));
                    if (tab.Pinned)
                    {
                        writer.WriteBoolean("pinned", true);
                    }

                    writer.WriteNumber("anchor", tab.Selection.Anchor);
                    writer.WriteNumber("caret", tab.Selection.Caret);
                    writer.WriteNumber("scrollX", Math.Round(tab.ScrollX, 1));
                    writer.WriteNumber("scrollY", Math.Round(tab.ScrollY, 1));
                    if (tab.Folds.Count > 0)
                    {
                        writer.WriteStartArray("folds");
                        foreach (int line in tab.Folds)
                        {
                            writer.WriteNumberValue(line);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
        }
        else
        {
            writer.WriteString("orientation", node.Orientation == GroupOrientation.Row ? "row" : "column");
            writer.WriteStartArray("weights");
            foreach (var weight in node.Weights)
            {
                writer.WriteNumberValue(Math.Round(weight, 4));
            }

            writer.WriteEndArray();
            writer.WriteStartArray("children");
            foreach (var child in node.Children)
            {
                WriteNode(writer, child);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private EditorLayoutNodeState ReadNode(JsonElement element)
    {
        if (element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var nodes = new List<EditorLayoutNodeState>();
            foreach (var child in children.EnumerateArray())
            {
                nodes.Add(ReadNode(child));
            }

            var weights = new List<double>();
            if (element.TryGetProperty("weights", out var shares) && shares.ValueKind == JsonValueKind.Array)
            {
                foreach (var share in shares.EnumerateArray())
                {
                    weights.Add(share.TryGetDouble(out var w) && w > 0 ? w : 1);
                }
            }

            var orientation = element.TryGetProperty("orientation", out var o) && string.Equals(o.GetString(), "column", StringComparison.Ordinal)
                ? GroupOrientation.Column : GroupOrientation.Row;
            return new EditorLayoutNodeState(orientation, nodes, weights, [], null);
        }

        var files = new List<string>();
        if (element.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in list.EnumerateArray())
            {
                if (file.GetString() is { Length: > 0 } name)
                {
                    files.Add(Absolute(name));
                }
            }
        }

        string? activeFile = element.TryGetProperty("active", out var a) && a.GetString() is { Length: > 0 } active ? Absolute(active) : null;
        var tabs = new List<EditorTabState>();
        if (element.TryGetProperty("tabs", out var kept) && kept.ValueKind == JsonValueKind.Array)
        {
            foreach (var tab in kept.EnumerateArray())
            {
                if (tab.ValueKind != JsonValueKind.Object || !tab.TryGetProperty("file", out var file) || file.GetString() is not { Length: > 0 } name)
                {
                    continue;
                }

                var folds = new List<int>();
                if (tab.TryGetProperty("folds", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        if (line.TryGetInt32(out int number) && number >= 0)
                        {
                            folds.Add(number);
                        }
                    }
                }

                tabs.Add(new EditorTabState(Absolute(name))
                {
                    Pinned = tab.TryGetProperty("pinned", out var pinned) && pinned.ValueKind == JsonValueKind.True,
                    Selection = new SelectionState(Whole(tab, "anchor"), Whole(tab, "caret")),
                    ScrollX = Offset(tab, "scrollX"),
                    ScrollY = Offset(tab, "scrollY"),
                    Folds = folds,
                });
            }
        }

        return new EditorLayoutNodeState(GroupOrientation.Row, [], [], files, activeFile) { Tabs = tabs };
    }

    private static int Whole(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out int number) ? Math.Max(0, number) : 0;

    private static float Offset(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out double number) && double.IsFinite(number) ? (float)Math.Max(0, number) : 0;

    private string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative.Replace('\\', '/');
    }

    private string Absolute(string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Root, path));

    /// <summary>The share written for <paramref name="weight"/> (4 decimals, invariant), for the tests' expectations.</summary>
    public static string ShareText(double weight) => Math.Round(weight, 4).ToString(CultureInfo.InvariantCulture);
}
