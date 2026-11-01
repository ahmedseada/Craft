// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Craft.CSharp;

/// <summary>
/// C#'s call and type hierarchies (X part 9) on Roslyn's public <see cref="SymbolFinder"/>: the callers
/// (<see cref="SymbolFinder.FindCallersAsync(ISymbol, Solution, CancellationToken)"/>, with each caller's call places),
/// the calls a member makes (its bodies' invocations, object creations and property and event uses, bound by the
/// semantic model), the base type and interfaces, and the derived classes, interfaces and implementations
/// (<c>FindDerivedClassesAsync</c>, <c>FindDerivedInterfacesAsync</c>, <c>FindImplementationsAsync</c>, one level).
/// A symbol travels in <see cref="HierarchyItem.Data"/> as its assembly's name and its documentation id, found again in
/// that project's compilation. Runs on the thread pool.
/// </summary>
internal static class CSharpHierarchy
{
    private static readonly SymbolDisplayFormat NameFormat = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat ContainerFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    public static async Task<HierarchyItem?> PrepareAsync(Document document, int position, HierarchyKind kind, CancellationToken cancellationToken)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        if (kind == HierarchyKind.Call)
        {
            symbol = symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet } accessor => accessor.AssociatedSymbol ?? accessor,
                IMethodSymbol or IPropertySymbol or IEventSymbol => symbol,
                _ => null,
            };
        }
        else
        {
            symbol = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        }

        return symbol is null ? null : Item(symbol.OriginalDefinition, null, []);
    }

    public static async Task<IReadOnlyList<HierarchyItem>> WalkAsync(Solution solution, HierarchyItem item, HierarchyDirection direction, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(solution, item, cancellationToken).ConfigureAwait(false) is not { } symbol)
        {
            return [];
        }

        var found = new List<HierarchyItem>();
        switch (direction)
        {
            case HierarchyDirection.Incoming:
            {
                var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
                var byCaller = new Dictionary<ISymbol, (string? File, List<(int, int)> Calls)>(SymbolEqualityComparer.Default);
                foreach (var caller in callers)
                {
                    var calling = caller.CallingSymbol.OriginalDefinition;
                    if (!byCaller.TryGetValue(calling, out var entry))
                    {
                        entry = (null, []);
                    }

                    foreach (var location in caller.Locations)
                    {
                        if (location.IsInSource)
                        {
                            var line = location.GetLineSpan();
                            entry.File ??= line.Path;
                            entry.Calls.Add((line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1));
                        }
                    }

                    byCaller[calling] = entry;
                }

                foreach (var (calling, (file, calls)) in byCaller)
                {
                    calls.Sort();
                    found.Add(Item(calling, file, calls));
                }

                break;
            }

            case HierarchyDirection.Outgoing:
                await OutgoingAsync(solution, symbol, found, cancellationToken).ConfigureAwait(false);
                break;
            case HierarchyDirection.Supertypes when symbol is INamedTypeSymbol type:
                if (type.BaseType is { SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType } baseType && type.TypeKind != TypeKind.Enum)
                {
                    found.Add(Item(baseType.OriginalDefinition, null, []));
                }

                foreach (var face in type.Interfaces)
                {
                    found.Add(Item(face.OriginalDefinition, null, []));
                }

                break;
            case HierarchyDirection.Subtypes when symbol is INamedTypeSymbol type:
            {
                var derived = new List<INamedTypeSymbol>();
                if (type.TypeKind == TypeKind.Class)
                {
                    derived.AddRange(await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false));
                }
                else if (type.TypeKind == TypeKind.Interface)
                {
                    derived.AddRange(await SymbolFinder.FindDerivedInterfacesAsync(type, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false));
                    derived.AddRange(await SymbolFinder.FindImplementationsAsync(type, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false));
                }

                foreach (var one in derived.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
                {
                    found.Add(Item(one.OriginalDefinition, null, []));
                }

                break;
            }
        }

        found.Sort(static (a, b) =>
        {
            int byFile = string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
            return byFile != 0 ? byFile : a.Line != b.Line ? a.Line.CompareTo(b.Line) : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
        return found;
    }

    // The members a member calls: its source bodies walked, each call bound, grouped by what it calls (in the order first called).
    private static async Task OutgoingAsync(Solution solution, ISymbol symbol, List<HierarchyItem> found, CancellationToken cancellationToken)
    {
        var order = new List<ISymbol>();
        var calls = new Dictionary<ISymbol, List<(int, int)>>(SymbolEqualityComparer.Default);
        string? file = null;
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            if (solution.GetDocument(reference.SyntaxTree) is not { } document || await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } model)
            {
                continue;
            }

            file ??= reference.SyntaxTree.FilePath;
            foreach (var node in syntax.DescendantNodes())
            {
                ISymbol? target = node switch
                {
                    InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation, cancellationToken).Symbol,
                    BaseObjectCreationExpressionSyntax creation => model.GetSymbolInfo(creation, cancellationToken).Symbol,
                    IdentifierNameSyntax name when node.Parent is not InvocationExpressionSyntax && !(node.Parent is MemberAccessExpressionSyntax access && access.Parent is InvocationExpressionSyntax && access.Name == node)
                        => model.GetSymbolInfo(name, cancellationToken).Symbol is { Kind: SymbolKind.Property or SymbolKind.Event } used ? used : null,
                    _ => null,
                };
                if (target is null || target.IsImplicitlyDeclared || target.Kind is SymbolKind.Local or SymbolKind.Parameter)
                {
                    continue;
                }

                target = target is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : target;
                target = target.OriginalDefinition;
                if (!calls.TryGetValue(target, out var places))
                {
                    places = [];
                    calls[target] = places;
                    order.Add(target);
                }

                var line = node.GetLocation().GetLineSpan();
                places.Add((line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1));
            }
        }

        foreach (var target in order)
        {
            found.Add(Item(target, file, calls[target]));
        }
    }

    private static HierarchyItem Item(ISymbol symbol, string? callsFile, List<(int Line, int Column)> calls)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        string? path = null;
        int line = 0;
        int column = 0;
        if (location is not null)
        {
            var span = location.GetLineSpan();
            path = span.Path;
            line = span.StartLinePosition.Line + 1;
            column = span.StartLinePosition.Character + 1;
        }

        string? container = symbol.ContainingType?.ToDisplayString(ContainerFormat) ?? (symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString(ContainerFormat) : null);
        string name = symbol.ToDisplayString(NameFormat);
        return new HierarchyItem(name, KindOf(symbol), container, path, line, column)
        {
            Data = (symbol.ContainingAssembly?.Name ?? "") + "\n" + (symbol.GetDocumentationCommentId() ?? ""),
            Calls = calls,
            CallsFile = calls.Count > 0 ? callsFile : null,
        };
    }

    private static ScopeKind KindOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol => ScopeKind.Type,
        IPropertySymbol => ScopeKind.Property,
        IEventSymbol => ScopeKind.Event,
        IFieldSymbol => ScopeKind.Field,
        INamespaceSymbol => ScopeKind.Namespace,
        _ => ScopeKind.Method,
    };

    // The symbol an item stands for, found again in its assembly's project (else any project that sees it).
    private static async Task<ISymbol?> ResolveAsync(Solution solution, HierarchyItem item, CancellationToken cancellationToken)
    {
        if (item.Data is not { } data)
        {
            return null;
        }

        int cut = data.IndexOf('\n');
        if (cut < 0)
        {
            return null;
        }

        string assembly = data[..cut];
        string id = data[(cut + 1)..];
        if (id.Length == 0)
        {
            return null;
        }

        var projects = solution.Projects.OrderBy(p => p.AssemblyName == assembly ? 0 : 1).ToList();
        foreach (var project in projects)
        {
            if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is { } compilation
                && DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) is { } symbol)
            {
                return symbol;
            }
        }

        return null;
    }

    /// <summary>The places that refer to the symbol at <paramref name="position"/> (its declarations left out), or null when there is none.</summary>
    public static async Task<int?> CountReferencesAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var references = await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(false);
        int count = 0;
        foreach (var referenced in references)
        {
            foreach (var location in referenced.Locations)
            {
                if (!location.IsImplicit)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
