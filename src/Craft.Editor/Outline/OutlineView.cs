// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Outline;

/// <summary>
/// The Outline view (X part 9), a side view of the rail: the active editor's document symbols
/// (<see cref="ILanguageSession.GetDocumentSymbolsAsync"/>, flattened once per version by <see cref="SymbolIndex"/>) as a
/// tree that opens and closes, filtered as the user types (a symbol whose name holds the text, or matches its capitals,
/// with its parents), ordered by position, name or kind (<c>CRAFT_OUTLINE_SORT</c>), following the caret (the deepest
/// symbol holding it highlighted and scrolled into view, <c>CRAFT_OUTLINE_FOLLOW_CARET</c>); a click selects the symbol's
/// name in the editor. A language that gives no symbols shows an empty state that says so. The symbols are asked off the
/// UI thread, one request in flight (a change meanwhile asks again when it lands). Headless; called on the UI thread.
/// </summary>
public sealed class OutlineView : IEditorViewPanel, IDisposable
{
    /// <summary>Its panel id (and its rail item's).</summary>
    public const string PanelId = "craft.outline";

    private const string FilterAction = "outline.filter";
    private const string SortAction = "outline.sort";
    private const string FollowAction = "outline.follow";
    private const string GoAction = "outline.go";
    private const string ToggleAction = "outline.toggle";
    private const string CollapseAction = "outline.collapse";

    private readonly EditorHost _host;
    private readonly Func<EditorModel?> _active;
    private readonly Func<CraftOptions> _options;
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal);
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private EditorModel? _model;
    private SymbolIndex? _index;
    private string _filter = "";
    private string? _sort;
    private bool? _follow;
    private bool _asking;
    private bool _again;
    private int _version;
    private int _selected = -1;
    private List<(int Symbol, string Key)> _rows = [];
    private bool _disposed;

    /// <summary>The view over <paramref name="host"/>, showing <paramref name="active"/>'s symbols with <paramref name="options"/>.</summary>
    public OutlineView(EditorHost host, Func<EditorModel?> active, Func<CraftOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _active = active ?? throw new ArgumentNullException(nameof(active));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host.ActiveDocumentChanged += OnActiveChanged;
        _host.DocumentChanged += OnDocumentChanged;
        _host.SelectionChanged += OnSelectionChanged;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => "Outline";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>The order shown: "position", "name" or "kind".</summary>
    public string Sort => _sort ?? _options().OutlineSort;

    /// <summary>Whether the view follows the caret.</summary>
    public bool FollowsCaret => _follow ?? _options().OutlineFollowCaret;

    /// <summary>The filter typed.</summary>
    public string Filter => _filter;

    /// <summary>The symbols shown, in order: their index in <see cref="Symbols"/> and their depth (filter, sort and folds applied).</summary>
    public IReadOnlyList<(int Symbol, int Depth)> Rows
    {
        get
        {
            var rows = new List<(int, int)>(_rows.Count);
            foreach (var (symbol, _) in _rows)
            {
                rows.Add((symbol, _index!.Depths[symbol]));
            }

            return rows;
        }
    }

    /// <summary>The symbols of the document shown, or null when there is none.</summary>
    public SymbolIndex? Symbols => _index;

    /// <summary>The symbol highlighted for the caret (its index in <see cref="Symbols"/>), or -1.</summary>
    public int SelectedSymbol => _selected;

    /// <summary>Completes when the symbols asked for last are in.</summary>
    public Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>Shows the active editor's symbols again (a tab was switched, a document opened).</summary>
    public void Refresh()
    {
        _model = _active();
        if (_model is null)
        {
            _index = null;
            Publish();
            return;
        }

        _index = _model.Symbols is { } kept && kept.Version == _model.Snapshot.Version ? kept : _model.Symbols;
        Publish();
        Ask();
    }

    /// <inheritdoc />
    public Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        switch (actionId)
        {
            case FilterAction:
                _filter = value ?? "";
                Publish();
                break;
            case SortAction when value is "position" or "name" or "kind":
                _sort = value;
                Publish();
                break;
            case FollowAction:
                _follow = value == "on";
                Publish();
                break;
            case ToggleAction when value is not null:
                if (!_closed.Remove(value))
                {
                    _closed.Add(value);
                }

                Publish();
                break;
            case CollapseAction:
                if (_index is { } index)
                {
                    for (int i = 0; i < index.Count; i++)
                    {
                        if (index.Symbols[i].Children.Count > 0)
                        {
                            _closed.Add(KeyOf(index, i));
                        }
                    }
                }

                Publish();
                break;
            case GoAction when value is not null && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int symbol):
                GoTo(symbol);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Selects symbol <paramref name="symbol"/>'s name (its index in <see cref="Symbols"/>) in the editor, scrolled into view.</summary>
    public bool GoTo(int symbol)
    {
        if (_model is not { } model || _index is not { } index || symbol < 0 || symbol >= index.Count || index.Version != model.Snapshot.Version)
        {
            return false;
        }

        var target = index.Symbols[symbol];
        int start = Math.Min(target.NameStart, model.Snapshot.Length);
        int end = Math.Min(target.NameStart + target.NameLength, model.Snapshot.Length);
        model.Select(start, end);
        model.EnsureCaretVisible();
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.ActiveDocumentChanged -= OnActiveChanged;
        _host.DocumentChanged -= OnDocumentChanged;
        _host.SelectionChanged -= OnSelectionChanged;
    }

    private void OnActiveChanged(object? sender, EventArgs e) => Refresh();

    private void OnDocumentChanged(object? sender, EditorDocumentChangedEventArgs e)
    {
        if (_model is { } model && ReferenceEquals(e.Document.Document, model.Document))
        {
            Ask();
        }
    }

    private void OnSelectionChanged(object? sender, EditorDocumentEventArgs e)
    {
        if (FollowsCaret && _model is { } model && ReferenceEquals(e.Document.Document, model.Document) && _index is not null)
        {
            int selected = AtCaret();
            if (selected != _selected)
            {
                Publish();
            }
        }
    }

    // One request in flight: a change meanwhile asks again when it lands.
    private void Ask()
    {
        if (_model is not { } model || _disposed)
        {
            return;
        }

        if (_asking)
        {
            _again = true;
            return;
        }

        var request = model.DocumentSymbolsAsync();
        if (request.IsCompletedSuccessfully)
        {
            _index = request.Result;
            Publish();
            return;
        }

        _asking = true;
        Loaded = Landed(model, request);
    }

    private async Task Landed(EditorModel model, Task<SymbolIndex?> request)
    {
        try
        {
            var index = await request.ConfigureAwait(true);
            if (ReferenceEquals(model, _model) && !_disposed)
            {
                _index = index;
                Publish();
            }
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // A language that fails to give its symbols leaves the last ones; it is logged.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, $"Outline: the symbols could not be read: {error.Message}", error);
        }
        finally
        {
            _asking = false;
        }

        if (_again && !_disposed)
        {
            _again = false;
            Ask();
        }
    }

    // The deepest symbol holding the caret, or -1.
    private int AtCaret()
    {
        if (_model is not { } model || _index is not { } index || index.Version != model.Snapshot.Version)
        {
            return -1;
        }

        var path = index.PathAt(model.Selection.Caret);
        return path.Count == 0 ? -1 : path[^1];
    }

    private static string KeyOf(SymbolIndex index, int symbol)
    {
        var parts = new Stack<string>();
        for (int at = symbol; at >= 0; at = index.Parents[at])
        {
            parts.Push(index.Names[at]);
        }

        return string.Join('/', parts);
    }

    private void Publish()
    {
        _version++;
        _items = null;
        var blocks = new List<PanelBlock>
        {
            new PanelQuery(FilterAction, "Filter symbols", _filter)
            {
                Toggles = [new PanelToggle(FollowAction, "⌖", "Follow the caret", FollowsCaret)],
                Buttons = [new PanelButton("Collapse all", CollapseAction) { Icon = RailIcons.Collapse, Kind = PanelButtonKind.Flat }],
            },
            new PanelPicker(SortAction, "Order", [new PanelOption("position", "Position"), new PanelOption("name", "Name"), new PanelOption("kind", "Kind")], Sort)
            {
                Variable = "CRAFT_OUTLINE_SORT",
            },
        };

        _rows = [];
        _selected = FollowsCaret ? AtCaret() : -1;
        string? empty;
        var groupRows = new List<PanelResultRow>();
        string title = _model?.Document.FilePath is { } file ? Path.GetFileName(file) : "Outline";
        if (_model is null)
        {
            empty = "Open a file to see its outline.";
        }
        else if (_index is not { } index)
        {
            empty = _model.LanguageSession is null ? $"{_model.LanguageName} has no symbols." : "Reading the symbols…";
        }
        else if (index.Count == 0)
        {
            empty = $"{_model.Language?.Name ?? _model.LanguageName} gives no symbols for this file.";
        }
        else
        {
            AddRows(index, groupRows);
            empty = groupRows.Count == 0 ? $"No symbol matches '{_filter}'." : null;
        }

        var groups = groupRows.Count == 0 ? (IReadOnlyList<PanelResultGroup>)[] : [new PanelResultGroup("outline", title, groupRows) { Badge = groupRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }];
        blocks.Add(new PanelResults("outline.tree", groups) { Version = _version, Empty = empty });
        _blocks = blocks;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The rows: the tree in the order chosen, a closed node's children left out; with a filter, the matches and their parents (all open).
    private void AddRows(SymbolIndex index, List<PanelResultRow> rows)
    {
        bool filtering = _filter.Length > 0;
        bool[]? keep = null;
        if (filtering)
        {
            keep = new bool[index.Count];
            for (int i = 0; i < index.Count; i++)
            {
                if (Matches(index.Names[i], _filter))
                {
                    for (int at = i; at >= 0 && !keep[at]; at = index.Parents[at])
                    {
                        keep[at] = true;
                    }
                }
            }
        }

        var roots = new List<int>();
        for (int i = 0; i < index.Count; i++)
        {
            if (index.Parents[i] < 0)
            {
                roots.Add(i);
            }
        }

        Add(roots);

        void Add(List<int> level)
        {
            Order(index, level);
            foreach (int symbol in level)
            {
                if (keep is not null && !keep[symbol])
                {
                    continue;
                }

                var children = new List<int>(index.ChildrenOf(symbol));
                string key = KeyOf(index, symbol);
                bool open = filtering || !_closed.Contains(key);
                var s = index.Symbols[symbol];
                _rows.Add((symbol, key));
                rows.Add(new PanelResultRow(key, index.Names[symbol])
                {
                    Prefix = SymbolKinds.Badge(s.Kind),
                    Detail = s.Detail,
                    Depth = index.Depths[symbol],
                    Expanded = children.Count == 0 ? null : open,
                    ToggleActionId = children.Count == 0 ? null : ToggleAction,
                    ActionId = GoAction,
                    Value = symbol.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Selected = symbol == _selected,
                    Code = true,
                    Highlights = filtering ? Highlight(index.Names[symbol], _filter) : [],
                });
                if (open && children.Count > 0)
                {
                    Add(children);
                }
            }
        }
    }

    private void Order(SymbolIndex index, List<int> level)
    {
        switch (Sort)
        {
            case "name":
                level.Sort((a, b) =>
                {
                    int byName = string.Compare(index.Names[a], index.Names[b], StringComparison.OrdinalIgnoreCase);
                    return byName != 0 ? byName : a.CompareTo(b);
                });
                break;
            case "kind":
                level.Sort((a, b) =>
                {
                    int byKind = SymbolKinds.Rank(index.Symbols[a].Kind).CompareTo(SymbolKinds.Rank(index.Symbols[b].Kind));
                    if (byKind != 0)
                    {
                        return byKind;
                    }

                    int byName = string.Compare(index.Names[a], index.Names[b], StringComparison.OrdinalIgnoreCase);
                    return byName != 0 ? byName : a.CompareTo(b);
                });
                break;
            default:
                level.Sort((a, b) => index.Symbols[a].Start.CompareTo(index.Symbols[b].Start));
                break;
        }
    }

    /// <summary>Whether <paramref name="name"/> holds <paramref name="filter"/> (ignoring case) or matches its capitals ("GDA" for GetDiagnosticsAsync).</summary>
    public static bool Matches(string name, string filter)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(filter);
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) || CompletionController.Score(name, filter) >= 2;
    }

    private static IReadOnlyList<PanelSpan> Highlight(string name, string filter)
    {
        int at = name.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? [] : [new PanelSpan(at, filter.Length)];
    }
}
