// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Outline;

/// <summary>
/// The call and type hierarchies (X part 9), a tab of the bottom panel: the symbol at the caret as the root (asked of its
/// language, <see cref="ISemanticLanguageSession.PrepareHierarchyAsync"/>), then one level at a time as the user opens a
/// node (<see cref="ISemanticLanguageSession.HierarchyAsync"/>): its callers or the members it calls, its base types or
/// the types derived from it. Every request runs off the UI thread as an activity that can be cancelled; a click goes to
/// the place (a caller's call, else the declaration). Headless; called on the UI thread.
/// </summary>
public sealed class HierarchyView : IEditorViewPanel
{
    /// <summary>Its panel id.</summary>
    public const string PanelId = "craft.hierarchy";

    private const string DirectionAction = "hierarchy.direction";
    private const string ToggleAction = "hierarchy.toggle";
    private const string GoAction = "hierarchy.go";
    private const string RefreshAction = "hierarchy.refresh";

    private readonly EditorHost _host;
    private readonly Func<CraftOptions> _options;
    private readonly Func<string?, int, int, CancellationToken, Task> _goTo;
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private ISemanticLanguageSession? _session;
    private Node? _root;
    private HierarchyKind _kind;
    private HierarchyDirection _direction = HierarchyDirection.Incoming;
    private string? _note;
    private int _version;
    private Task _pending = Task.CompletedTask;

    /// <summary>
    /// The view over <paramref name="host"/>; <paramref name="goTo"/> opens a file (null: the root's document) at a line and
    /// column from 1.
    /// </summary>
    public HierarchyView(EditorHost host, Func<CraftOptions> options, Func<string?, int, int, CancellationToken, Task> goTo)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _goTo = goTo ?? throw new ArgumentNullException(nameof(goTo));
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => _kind == HierarchyKind.Call ? "Call hierarchy" : "Type hierarchy";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Which hierarchy it shows.</summary>
    public HierarchyKind Kind => _kind;

    /// <summary>Which way it walks from the root.</summary>
    public HierarchyDirection Direction => _direction;

    /// <summary>The root, or null before one was asked (or when there is no symbol there).</summary>
    public HierarchyItem? Root => _root?.Item;

    /// <summary>What the view says (no symbol at the caret, a failure), or null.</summary>
    public string? Note => _note;

    /// <summary>Completes when the last request is in.</summary>
    public Task Pending => _pending;

    /// <summary>The rows shown, in order: their id ("0", "0/2"), depth and item.</summary>
    public IReadOnlyList<(string Id, int Depth, HierarchyItem Item)> Rows
    {
        get
        {
            var rows = new List<(string, int, HierarchyItem)>();
            if (_root is { } root)
            {
                Walk(root, 0, rows);
            }

            return rows;
        }
    }

    /// <summary>
    /// Shows the <paramref name="kind"/> hierarchy of the symbol at <paramref name="position"/> of
    /// <paramref name="snapshot"/>, asked of <paramref name="session"/> off the UI thread (an activity), its first level
    /// open (callers for a call hierarchy, the base types for a type hierarchy).
    /// </summary>
    public Task ShowAsync(ISemanticLanguageSession session, ITextSnapshot snapshot, int position, HierarchyKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(snapshot);
        _session = session;
        _kind = kind;
        _direction = kind == HierarchyKind.Call ? HierarchyDirection.Incoming : HierarchyDirection.Supertypes;
        _root = null;
        _nodes.Clear();
        _note = "Looking for the symbol at the caret…";
        Publish();
        return _pending = PrepareAsync(session, snapshot, position, kind, cancellationToken);
    }

    /// <summary>Opens or closes the node <paramref name="id"/>; its children are asked the first time it opens.</summary>
    public Task ToggleAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return Task.CompletedTask;
        }

        node.Open = !node.Open;
        Publish();
        if (node.Open && node.Children is null && !node.Loading)
        {
            return _pending = LoadAsync(node, cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        switch (actionId)
        {
            case DirectionAction when Enum.TryParse<HierarchyDirection>(value, out var direction) && direction != _direction:
                _direction = direction;
                if (_root is { } root)
                {
                    _nodes.Clear();
                    var fresh = new Node("0", root.Item) { Open = true };
                    _root = fresh;
                    _nodes[fresh.Id] = fresh;
                    Publish();
                    return _pending = LoadAsync(fresh, cancellationToken);
                }

                Publish();
                break;
            case ToggleAction when value is not null:
                return ToggleAsync(value, cancellationToken);
            case GoAction when value is not null && _nodes.TryGetValue(value, out var node):
                return GoToAsync(node.Item, cancellationToken);
            case RefreshAction when _root is { } current:
                _nodes.Clear();
                var again = new Node("0", current.Item) { Open = true };
                _root = again;
                _nodes[again.Id] = again;
                Publish();
                return _pending = LoadAsync(again, cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>Goes to <paramref name="item"/>'s place: the first call it makes or receives (for a caller, its call), else its declaration.</summary>
    public Task GoToAsync(HierarchyItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Calls.Count > 0 && item.CallsFile is { } file)
        {
            return _goTo(file, item.Calls[0].Line, item.Calls[0].Column, cancellationToken);
        }

        return item.IsMetadata ? Task.CompletedTask : _goTo(item.FilePath, item.Line, item.Column, cancellationToken);
    }

    private async Task PrepareAsync(ISemanticLanguageSession session, ITextSnapshot snapshot, int position, HierarchyKind kind, CancellationToken cancellationToken)
    {
        string what = kind == HierarchyKind.Call ? "Call hierarchy" : "Type hierarchy";
        using var activity = _host.Activities.Start(what, ActivityKinds.General, cancellable: true, cancellationToken);
        HierarchyItem? item;
        try
        {
            item = await session.PrepareHierarchyAsync(snapshot, position, kind, activity.CancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _note = "Cancelled.";
            Publish();
            return;
        }
#pragma warning disable CA1031 // A language that fails is said in the view; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _note = $"{what} failed: {error.Message}";
            Publish();
            return;
        }

        if (item is null)
        {
            _note = kind == HierarchyKind.Call ? "No method, property, event or constructor at the caret." : "No type at the caret.";
            activity.Complete(_note);
            Publish();
            return;
        }

        _note = null;
        var root = new Node("0", item) { Open = true };
        _root = root;
        _nodes[root.Id] = root;
        Publish();
        activity.Complete(item.Name);
        await LoadAsync(root, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadAsync(Node node, CancellationToken cancellationToken)
    {
        if (_session is not { } session)
        {
            return;
        }

        node.Loading = true;
        Publish();
        string label = _direction switch
        {
            HierarchyDirection.Incoming => "callers of ",
            HierarchyDirection.Outgoing => "calls from ",
            HierarchyDirection.Supertypes => "base types of ",
            _ => "types derived from ",
        };
        using var activity = _host.Activities.Start("Hierarchy: " + label + node.Item.Name, ActivityKinds.General, cancellable: true, cancellationToken);
        try
        {
            var found = await session.HierarchyAsync(node.Item, _direction, activity.CancellationToken).ConfigureAwait(true);
            int limit = Math.Max(1, _options().HierarchyLimit);
            var children = new List<Node>(Math.Min(found.Count, limit));
            for (int i = 0; i < found.Count && i < limit; i++)
            {
                var child = new Node(node.Id + "/" + i.ToString(CultureInfo.InvariantCulture), found[i]);
                children.Add(child);
                _nodes[child.Id] = child;
            }

            node.Children = children;
            node.Capped = found.Count > limit;
            activity.Complete(string.Create(CultureInfo.InvariantCulture, $"{found.Count} found"));
        }
        catch (OperationCanceledException)
        {
            node.Open = false;
        }
#pragma warning disable CA1031 // A language that fails is said in the view; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _note = $"The hierarchy could not be walked: {error.Message}";
            node.Open = false;
        }
        finally
        {
            node.Loading = false;
            Publish();
        }
    }

    private static void Walk(Node node, int depth, List<(string, int, HierarchyItem)> rows)
    {
        rows.Add((node.Id, depth, node.Item));
        if (node.Open && node.Children is { } children)
        {
            foreach (var child in children)
            {
                Walk(child, depth + 1, rows);
            }
        }
    }

    private void Publish()
    {
        _version++;
        _items = null;
        var blocks = new List<PanelBlock>();
        var directions = _kind == HierarchyKind.Call
            ? new[] { new PanelOption(nameof(HierarchyDirection.Incoming), "Callers"), new PanelOption(nameof(HierarchyDirection.Outgoing), "Calls made") }
            : [new PanelOption(nameof(HierarchyDirection.Supertypes), "Base types"), new PanelOption(nameof(HierarchyDirection.Subtypes), "Derived types")];
        blocks.Add(new PanelPicker(DirectionAction, "Show", directions, _direction.ToString()));
        blocks.Add(new PanelButtons([new PanelButton("Refresh", RefreshAction, PanelButtonKind.Flat) { Icon = RailIcons.Refresh, Enabled = _root is not null }]));
        var rows = new List<PanelResultRow>();
        if (_root is { } root)
        {
            AddRows(root, 0, rows);
        }

        string title = _root is { } r ? (_direction switch
        {
            HierarchyDirection.Incoming => "Callers of ",
            HierarchyDirection.Outgoing => "Calls from ",
            HierarchyDirection.Supertypes => "Base types of ",
            _ => "Types derived from ",
        }) + r.Item.Name : Title;
        var groups = rows.Count == 0 ? (IReadOnlyList<PanelResultGroup>)[] : [new PanelResultGroup("hierarchy", title, rows)];
        blocks.Add(new PanelResults("hierarchy.tree", groups)
        {
            Version = _version,
            Empty = _note ?? "Shift+Alt+H on a member shows who calls it; Type hierarchy (the palette or the editor's menu) on a type shows its bases and derived types.",
        });
        _blocks = blocks;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddRows(Node node, int depth, List<PanelResultRow> rows)
    {
        var item = node.Item;
        bool expandable = !item.IsMetadata || depth == 0;
        string? where = item.FilePath is { } file
            ? string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(file)}:{item.Line}")
            : "metadata";
        rows.Add(new PanelResultRow(node.Id, item.Name)
        {
            Prefix = SymbolKinds.Badge(item.Kind),
            Detail = item.Detail is { } container ? $"{container} · {where}" : where,
            Depth = depth,
            Expanded = expandable && (node.Children is null || node.Children.Count > 0) ? node.Open : null,
            ToggleActionId = expandable ? ToggleAction : null,
            ActionId = GoAction,
            Value = node.Id,
            Code = true,
            Badge = node.Loading ? "…" : item.Calls.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{item.Calls.Count} calls") : null,
            Tone = item.IsMetadata ? PanelTone.Normal : PanelTone.Normal,
        });
        if (node.Open && node.Children is { } children)
        {
            foreach (var child in children)
            {
                AddRows(child, depth + 1, rows);
            }

            if (node.Capped)
            {
                rows.Add(new PanelResultRow(node.Id + "/more", $"(more than {_options().HierarchyLimit}: CRAFT_HIERARCHY_LIMIT)") { Depth = depth + 1 });
            }
        }
    }

    private sealed class Node(string id, HierarchyItem item)
    {
        public string Id { get; } = id;

        public HierarchyItem Item { get; } = item;

        public List<Node>? Children { get; set; }

        public bool Open { get; set; }

        public bool Loading { get; set; }

        public bool Capped { get; set; }
    }
}
