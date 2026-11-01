// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// A document's symbols (<see cref="ILanguageSession.GetDocumentSymbolsAsync"/>) made once per version into flat arrays in
/// the order of the text (rule 75): the names quick open's '@' matches, each symbol's parent, depth and line, and a lookup
/// by name that takes a span (rule 15: an alternate lookup, no string made per question). Immutable; any thread.
/// </summary>
public sealed class SymbolIndex
{
    private readonly Dictionary<string, int> _byName;
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    private SymbolIndex(int version, IReadOnlyList<DocumentSymbol> roots, DocumentSymbol[] symbols, string[] names, int[] parents, int[] depths, int[] lines, int[] columns)
    {
        Version = version;
        Roots = roots;
        Symbols = symbols;
        Names = names;
        Parents = parents;
        Depths = depths;
        Lines = lines;
        Columns = columns;
        _byName = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            _byName.TryAdd(names[i], i);                         // the first declaration of a name (an overload's first)
        }

        _lookup = _byName.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>An index of no symbols, of no version.</summary>
    public static SymbolIndex Empty { get; } = new(-1, [], [], [], [], [], [], []);

    /// <summary>The version of the snapshot the symbols are of.</summary>
    public int Version { get; }

    /// <summary>The symbols as the language gave them (the tree).</summary>
    public IReadOnlyList<DocumentSymbol> Roots { get; }

    /// <summary>Every symbol, each before its children, in the order of the text.</summary>
    public IReadOnlyList<DocumentSymbol> Symbols { get; }

    /// <summary>Each symbol's name (what '@' matches).</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Each symbol's parent's index, or -1 for a root.</summary>
    public IReadOnlyList<int> Parents { get; }

    /// <summary>Each symbol's depth (0 for a root).</summary>
    public IReadOnlyList<int> Depths { get; }

    /// <summary>Each symbol's name's line (from 0).</summary>
    public IReadOnlyList<int> Lines { get; }

    /// <summary>Each symbol's name's character in its line (from 0).</summary>
    public IReadOnlyList<int> Columns { get; }

    /// <summary>The symbols.</summary>
    public int Count => Names.Count;

    /// <summary>Flattens <paramref name="roots"/>, the symbols of <paramref name="snapshot"/> (any thread).</summary>
    public static SymbolIndex Build(IReadOnlyList<DocumentSymbol> roots, ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(snapshot);
        var symbols = new List<DocumentSymbol>();
        var parents = new List<int>();
        var depths = new List<int>();
        Flatten(roots, -1, 0, symbols, parents, depths);
        var names = new string[symbols.Count];
        var lines = new int[symbols.Count];
        var columns = new int[symbols.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = symbols[i].Name;
            int at = Math.Clamp(symbols[i].NameStart, 0, snapshot.Length);
            lines[i] = snapshot.GetLineIndex(at);
            columns[i] = at - snapshot.GetLine(lines[i]).Start;
        }

        return new SymbolIndex(snapshot.Version, roots, [.. symbols], names, [.. parents], [.. depths], lines, columns);
    }

    /// <summary>The first symbol named <paramref name="name"/> (an overload's first), or -1; no string is made.</summary>
    public int IndexOf(ReadOnlySpan<char> name) => _lookup.TryGetValue(name, out int index) ? index : -1;

    /// <summary>The symbols whose declarations hold <paramref name="position"/>, outermost first (indices).</summary>
    public IReadOnlyList<int> PathAt(int position)
    {
        var path = new List<int>();
        int parent = -1;
        for (int i = 0; i < Symbols.Count; i++)
        {
            if (Parents[i] != parent)
            {
                continue;
            }

            var symbol = Symbols[i];
            if (symbol.Start > position)
            {
                break;                                           // in order of the text: nothing later holds it
            }

            if (symbol.Contains(position))
            {
                path.Add(i);
                parent = i;
            }
        }

        return path;
    }

    /// <summary>The children of <paramref name="parent"/> (-1: the roots), in the order of the text (indices).</summary>
    public IReadOnlyList<int> ChildrenOf(int parent)
    {
        var children = new List<int>();
        for (int i = parent + 1; i < Parents.Count; i++)
        {
            if (Parents[i] == parent)
            {
                children.Add(i);
            }
            else if (parent >= 0 && Depths[i] <= Depths[parent])
            {
                break;                                           // past the parent's last descendant
            }
        }

        return children;
    }

    /// <summary>
    /// The parent whose children a breadcrumb's symbol part named <paramref name="name"/> at <paramref name="position"/>
    /// lists as its siblings (-1: the roots): the innermost symbol of the path at the position with that name, its parent.
    /// A name the path does not have (a local function) lists the innermost symbol's siblings.
    /// </summary>
    public int SiblingsParentAt(int position, ReadOnlySpan<char> name)
    {
        var path = PathAt(position);
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if (name.SequenceEqual(Names[path[i]]))
            {
                return Parents[path[i]];
            }
        }

        return path.Count > 0 ? Parents[path[^1]] : -1;
    }

    private static void Flatten(IReadOnlyList<DocumentSymbol> level, int parent, int depth, List<DocumentSymbol> symbols, List<int> parents, List<int> depths)
    {
        foreach (var symbol in level)
        {
            int index = symbols.Count;
            symbols.Add(symbol);
            parents.Add(parent);
            depths.Add(depth);
            Flatten(symbol.Children, index, depth + 1, symbols, parents, depths);
        }
    }
}

/// <summary>The words for a symbol's kind (<see cref="ScopeKind"/>): one, and a group's title as '@:' lists them.</summary>
public static class SymbolKinds
{
    /// <summary>The order '@:' groups the kinds in.</summary>
    public static IReadOnlyList<ScopeKind> GroupOrder { get; } =
        [ScopeKind.Namespace, ScopeKind.Type, ScopeKind.Method, ScopeKind.Property, ScopeKind.Field, ScopeKind.Event, ScopeKind.EnumMember, ScopeKind.Member];

    /// <summary>The kind's place in <see cref="GroupOrder"/>.</summary>
    public static int Rank(ScopeKind kind) => kind switch
    {
        ScopeKind.Namespace => 0,
        ScopeKind.Type => 1,
        ScopeKind.Method => 2,
        ScopeKind.Property => 3,
        ScopeKind.Field => 4,
        ScopeKind.Event => 5,
        ScopeKind.EnumMember => 6,
        _ => 7,
    };

    /// <summary>One of the kind ("method", "enum member").</summary>
    public static string Word(ScopeKind kind) => kind switch
    {
        ScopeKind.Namespace => "namespace",
        ScopeKind.Type => "type",
        ScopeKind.Method => "method",
        ScopeKind.Property => "property",
        ScopeKind.Field => "field",
        ScopeKind.Event => "event",
        ScopeKind.EnumMember => "enum member",
        _ => "member",
    };

    /// <summary>A group's title ("methods").</summary>
    public static string Plural(ScopeKind kind) => kind switch
    {
        ScopeKind.Namespace => "namespaces",
        ScopeKind.Type => "types",
        ScopeKind.Method => "methods",
        ScopeKind.Property => "properties",
        ScopeKind.Field => "fields",
        ScopeKind.Event => "events",
        ScopeKind.EnumMember => "enum members",
        _ => "members",
    };

    /// <summary>A short badge the lists draw before the name ("M" a method).</summary>
    public static string Badge(ScopeKind kind) => kind switch
    {
        ScopeKind.Namespace => "{}",
        ScopeKind.Type => "T",
        ScopeKind.Method => "M",
        ScopeKind.Property => "P",
        ScopeKind.Field => "F",
        ScopeKind.Event => "E",
        ScopeKind.EnumMember => "#",
        _ => "·",
    };
}
