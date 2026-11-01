// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Immutable;
using System.Text;
using System.Xml;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynCompletion = Microsoft.CodeAnalysis.Completion;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Craft.CSharp;

/// <summary>
/// Roslyn's semantic answers for one document, as Craft's contracts: diagnostics of its tree, the completion list (with
/// each item's edit), signature help (built from the semantic model: Roslyn's own is not public), hover (the symbol's
/// display and its documentation's summary), go to definition and formatting.
/// </summary>
internal static class CSharpSemantics
{
    private static readonly SymbolDisplayFormat HoverFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeRef | SymbolDisplayMemberOptions.IncludeConstantValue,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeExtensionThis,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        localOptions: SymbolDisplayLocalOptions.IncludeType | SymbolDisplayLocalOptions.IncludeConstantValue,
        kindOptions: SymbolDisplayKindOptions.IncludeNamespaceKeyword | SymbolDisplayKindOptions.IncludeTypeKeyword,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // ---- diagnostics ----

    /// <summary>The syntax and semantic diagnostics of the document's tree (no analyzers), not the suppressed ones.</summary>
    public static async Task<List<EditorDiagnostic>> DiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<EditorDiagnostic>();
        if (model is null)
        {
            return result;
        }

        var tree = model.SyntaxTree;
        foreach (var diagnostic in model.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.IsSuppressed || !diagnostic.Location.IsInSource || diagnostic.Location.SourceTree != tree)
            {
                continue;
            }

            var span = diagnostic.Location.SourceSpan;
            result.Add(new EditorDiagnostic(span.Start, span.Length, SeverityOf(diagnostic.Severity), diagnostic.Id,
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static Abstractions.DiagnosticSeverity SeverityOf(RoslynSeverity severity) => severity switch
    {
        RoslynSeverity.Error => Abstractions.DiagnosticSeverity.Error,
        RoslynSeverity.Warning => Abstractions.DiagnosticSeverity.Warning,
        RoslynSeverity.Info => Abstractions.DiagnosticSeverity.Info,
        _ => Abstractions.DiagnosticSeverity.Hidden,
    };

    // ---- completion ----

    /// <summary>Roslyn's completion list at <paramref name="position"/>, each item with the edit that commits it.</summary>
    public static async Task<CompletionList?> CompletionsAsync(Document document, ITextSnapshot snapshot, int position, Abstractions.CompletionTrigger trigger, CancellationToken cancellationToken)
    {
        if (RoslynCompletion.CompletionService.GetService(document) is not { } service)
        {
            return null;
        }

        var roslynTrigger = trigger.Kind switch
        {
            CompletionTriggerKind.Typing => RoslynCompletion.CompletionTrigger.CreateInsertionTrigger(trigger.Character),
            CompletionTriggerKind.Deletion => RoslynCompletion.CompletionTrigger.CreateDeletionTrigger(trigger.Character),
            _ => RoslynCompletion.CompletionTrigger.Invoke,
        };

        if (trigger.Kind == CompletionTriggerKind.Typing)
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!service.ShouldTriggerCompletion(text, position, roslynTrigger))
            {
                return null;
            }
        }

        var list = await service.GetCompletionsAsync(document, position, roslynTrigger, cancellationToken: cancellationToken).ConfigureAwait(false);
        var roslynItems = list.ItemsList;
        if (roslynItems.Count == 0)
        {
            return null;
        }

        var span = list.Span;
        var items = new List<CompletionItem>(roslynItems.Count);
        foreach (var item in roslynItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string label = item.DisplayTextPrefix.Length == 0 && item.DisplayTextSuffix.Length == 0 ? item.DisplayText : item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
            // The plain insertion now; an item whose change is complex (an override's body, a using added) is resolved by
            // Roslyn's GetChangeAsync only when it is committed (measured: doing it for each complex item here cost 80-240 ms a list).
            var insertion = item.Properties.TryGetValue("InsertionText", out var inserted) && inserted is not null ? inserted : item.DisplayText;
            items.Add(new CompletionItem(label, KindOf(item.Tags), item.InlineDescription.Length > 0 ? item.InlineDescription : null,
                new TextEdit(span.Start, span.Length, insertion), item.FilterText, item.SortText, -1, item.IsComplexTextEdit, item.IsComplexTextEdit ? item : null));
        }

        items.Sort(static (a, b) =>
        {
            int order = string.Compare(a.SortText, b.SortText, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(a.Label, b.Label);
        });
        var result = new CompletionList(snapshot, position, span.Start, items);
        Lists.AddOrUpdate(result, document);
        return result;
    }

    /// <summary>The document each list was made from, for resolving its complex items (held only while the list is).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompletionList, Document> Lists = new();

    /// <summary>The exact edit of a complex item, from Roslyn's GetChangeAsync on the document the list was made from.</summary>
    public static async Task<CompletionItem> ResolveAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken)
    {
        if (!item.NeedsResolve || item.Data is not RoslynCompletion.CompletionItem roslynItem || !Lists.TryGetValue(list, out var document)
            || RoslynCompletion.CompletionService.GetService(document) is not { } service)
        {
            return item;
        }

        var change = await service.GetChangeAsync(document, roslynItem, cancellationToken: cancellationToken).ConfigureAwait(false);
        var textChange = change.TextChange;
        return item with
        {
            Edit = new TextEdit(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? ""),
            CaretAfter = change.NewPosition ?? -1,
            NeedsResolve = false,
        };
    }

    private static CompletionItemKind KindOf(ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case "Keyword": return CompletionItemKind.Keyword;
                case "Namespace": return CompletionItemKind.Namespace;
                case "Class": return CompletionItemKind.Class;
                case "Structure": return CompletionItemKind.Struct;
                case "Interface": return CompletionItemKind.Interface;
                case "Enum": return CompletionItemKind.Enum;
                case "EnumMember": return CompletionItemKind.EnumMember;
                case "Delegate": return CompletionItemKind.Delegate;
                case "ExtensionMethod": return CompletionItemKind.ExtensionMethod;
                case "Method": return CompletionItemKind.Method;
                case "Property": return CompletionItemKind.Property;
                case "Field": return CompletionItemKind.Field;
                case "Constant": return CompletionItemKind.Constant;
                case "Event": return CompletionItemKind.Event;
                case "Local": return CompletionItemKind.Local;
                case "Parameter": return CompletionItemKind.Parameter;
                case "TypeParameter": return CompletionItemKind.TypeParameter;
                case "Snippet": return CompletionItemKind.Snippet;
            }
        }

        return CompletionItemKind.Other;
    }

    // ---- signature help ----

    /// <summary>The overloads of the call (or object creation, or attribute) whose argument list holds <paramref name="position"/>.</summary>
    public static async Task<SignatureHelp?> SignatureHelpAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return null;
        }

        var token = root.FindToken(Math.Max(0, Math.Min(position, root.FullSpan.End - 1)));
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            BaseArgumentListSyntax? arguments = node switch
            {
                InvocationExpressionSyntax invocation => invocation.ArgumentList,
                BaseObjectCreationExpressionSyntax creation => creation.ArgumentList,
                ElementAccessExpressionSyntax access => access.ArgumentList,
                _ => null,
            };
            if (arguments is null || !Inside(arguments, position))
            {
                if (node is StatementSyntax or MemberDeclarationSyntax)
                {
                    break;
                }

                continue;
            }

            var (methods, chosen) = Candidates(model, node, cancellationToken);
            if (methods.Count == 0)
            {
                return null;
            }

            int active = 0;
            foreach (var separator in arguments.Arguments.GetSeparators())
            {
                if (separator.SpanStart < position)
                {
                    active++;
                }
            }

            var signatures = new List<SignatureInfo>(methods.Count);
            int activeSignature = -1;
            for (int i = 0; i < methods.Count; i++)
            {
                signatures.Add(Signature(methods[i], cancellationToken));
                if (activeSignature < 0 && chosen is not null && SymbolEqualityComparer.Default.Equals(methods[i], chosen))
                {
                    activeSignature = i;
                }
            }

            if (activeSignature < 0)
            {
                activeSignature = Math.Max(0, methods.FindIndex(m => m.Parameters.Length > active || (m.Parameters.Length > 0 && m.Parameters[^1].IsParams)));
            }

            return new SignatureHelp(signatures, activeSignature, active, arguments.GetFirstToken().Span.End);
        }

        return null;
    }

    private static bool Inside(BaseArgumentListSyntax arguments, int position)
    {
        var open = arguments.GetFirstToken();
        var close = arguments.GetLastToken();
        return position >= open.Span.End && (close.IsMissing || position <= close.SpanStart);
    }

    private static (List<IMethodSymbol> Methods, IMethodSymbol? Chosen) Candidates(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        var methods = new List<IMethodSymbol>();
        IMethodSymbol? chosen = null;
        var info = model.GetSymbolInfo(node, cancellationToken);
        chosen = info.Symbol as IMethodSymbol;
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                foreach (var member in model.GetMemberGroup(invocation.Expression, cancellationToken))
                {
                    if (member is IMethodSymbol method)
                    {
                        methods.Add(method);
                    }
                }

                break;
            case BaseObjectCreationExpressionSyntax:
                var type = model.GetTypeInfo(node, cancellationToken).Type as INamedTypeSymbol;
                if (type is not null)
                {
                    var within = model.GetEnclosingSymbol(node.SpanStart, cancellationToken);
                    foreach (var constructor in type.InstanceConstructors)
                    {
                        if (within is null || model.Compilation.IsSymbolAccessibleWithin(constructor, within.ContainingType ?? (ISymbol)within.ContainingAssembly))
                        {
                            methods.Add(constructor);
                        }
                    }
                }

                break;
            case ElementAccessExpressionSyntax access:
                if (model.GetTypeInfo(access.Expression, cancellationToken).Type is { } indexed)
                {
                    foreach (var member in indexed.GetMembers())
                    {
                        if (member is IPropertySymbol { IsIndexer: true, GetMethod: { } getter })
                        {
                            methods.Add(getter);
                        }
                    }
                }

                break;
        }

        foreach (var candidate in info.CandidateSymbols)
        {
            if (candidate is IMethodSymbol method && !methods.Contains(method, SymbolEqualityComparer.Default))
            {
                methods.Add(method);
            }
        }

        if (chosen is not null && !methods.Contains(chosen, SymbolEqualityComparer.Default))
        {
            methods.Insert(0, chosen);
        }

        return (methods, chosen);
    }

    private static SignatureInfo Signature(IMethodSymbol method, CancellationToken cancellationToken)
    {
        var label = new StringBuilder();
        var parameters = new List<SignatureParameter>(method.Parameters.Length);
        var docs = Documentation(method.OriginalDefinition, cancellationToken);
        if (method.MethodKind == MethodKind.Constructor)
        {
            label.Append(method.ContainingType.ToDisplayString(TypeFormat));
        }
        else if (method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } indexer)
        {
            label.Append(indexer.Type.ToDisplayString(TypeFormat)).Append(' ').Append(indexer.ContainingType.ToDisplayString(TypeFormat)).Append('[');
        }
        else
        {
            label.Append(method.ReturnType.ToDisplayString(TypeFormat)).Append(' ').Append(method.ContainingType.ToDisplayString(TypeFormat)).Append('.').Append(method.Name);
            if (method.IsGenericMethod)
            {
                label.Append('<').AppendJoin(", ", method.TypeArguments.Select(t => t.ToDisplayString(TypeFormat))).Append('>');
            }
        }

        bool indexerLabel = label.Length > 0 && label[^1] == '[';
        if (!indexerLabel)
        {
            label.Append('(');
        }

        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
            {
                label.Append(", ");
            }

            var parameter = method.Parameters[i];
            int start = label.Length;
            label.Append(parameter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat
                .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue)
                .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)));
            parameters.Add(new SignatureParameter(parameter.Name, start, label.Length - start, docs?.Parameters.GetValueOrDefault(parameter.Name)));
        }

        label.Append(indexerLabel ? ']' : ')');
        return new SignatureInfo(label.ToString(), docs?.Summary, parameters);
    }

    // ---- hover ----

    /// <summary>The symbol at <paramref name="position"/>: its display ("(method) void Console.WriteLine(string? value)") and its summary.</summary>
    public static async Task<HoverInfo?> HoverAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var (symbol, span) = await SymbolAtAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append('(').Append(KindName(symbol)).Append(") ").Append(symbol.ToDisplayString(HoverFormat));
        var docs = Documentation(symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol.OriginalDefinition, cancellationToken);
        if (docs?.Summary is { Length: > 0 } summary)
        {
            text.Append("\n\n").Append(summary);
        }

        return new HoverInfo(text.ToString(), span.Start, span.Length);
    }

    // ---- symbols ----

    /// <summary>
    /// The source declarations of <paramref name="solution"/> whose names match <paramref name="query"/> (Roslyn's pattern
    /// matcher: ignoring case, a name's capitals too), ranked exact name, then name start, then the rest, then by name and
    /// file; one per declaration (a partial type's parts each), at most <paramref name="max"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SymbolLocation>> FindSymbolsAsync(Solution solution, string query, int max, CancellationToken cancellationToken)
    {
        var found = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(solution, query, SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(false);
        var ranked = new List<(int Rank, SymbolLocation Location)>();
        var seen = new HashSet<(string?, int, int)>();
        foreach (var symbol in found)
        {
            int rank = symbol.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : symbol.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource)
                {
                    continue;
                }

                var span = location.GetLineSpan();
                string? path = string.IsNullOrEmpty(span.Path) ? null : span.Path;
                int line = span.StartLinePosition.Line + 1, column = span.StartLinePosition.Character + 1;
                if (seen.Add((path, line, column)))
                {
                    var container = symbol.ContainingType ?? (ISymbol?)(symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns : null);
                    ranked.Add((rank, new SymbolLocation(symbol.Name, KindName(symbol), container?.ToDisplayString(), path, line, column, symbol.ToDisplayString(HoverFormat))));
                }
            }
        }

        ranked.Sort((a, b) =>
        {
            int order = a.Rank.CompareTo(b.Rank);
            order = order != 0 ? order : string.CompareOrdinal(a.Location.Name, b.Location.Name);
            order = order != 0 ? order : string.CompareOrdinal(a.Location.FilePath, b.Location.FilePath);
            return order != 0 ? order : a.Location.Line.CompareTo(b.Location.Line);
        });
        return [.. ranked.Take(max).Select(r => r.Location)];
    }

    private static string KindName(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
        IMethodSymbol { IsExtensionMethod: true } or IMethodSymbol { ReducedFrom: not null } => "extension",
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol { IsConst: true } => "constant",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "enum member",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        ILocalSymbol => "local",
        IParameterSymbol => "parameter",
        ITypeParameterSymbol => "type parameter",
        INamespaceSymbol => "namespace",
        INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        INamedTypeSymbol { IsRecord: true } => "record",
        INamedTypeSymbol type => type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => "class",
        },
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    internal static async Task<(ISymbol? Symbol, TextSpan Span)> SymbolAtAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return (null, default);
        }

        // The identifier at the position, or the one that ends just before it (the caret after a name).
        var token = root.FindToken(Math.Max(0, Math.Min(position, root.FullSpan.End - 1)));
        if (!IsName(token) && position > 0)
        {
            var before = root.FindToken(position - 1);
            if (IsName(before))
            {
                token = before;
            }
        }

        if (!IsName(token))
        {
            return (null, default);
        }

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, token.SpanStart, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model is not null && token.Parent is { } parent)
            {
                var info = model.GetSymbolInfo(parent, cancellationToken);
                symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : model.GetDeclaredSymbol(parent, cancellationToken));
            }
        }

        return (symbol, token.Span);
    }

    private static bool IsName(SyntaxToken token) =>
        token.IsKind(SyntaxKind.IdentifierToken) || token.IsKind(SyntaxKind.ThisKeyword) || token.IsKind(SyntaxKind.BaseKeyword)
        || (token.IsKeyword() && token.Parent is PredefinedTypeSyntax);

    // ---- definition ----

    /// <summary>Where the symbol at <paramref name="position"/> is defined: a source span, or a description for a metadata symbol.</summary>
    public static async Task<DefinitionLocation?> DefinitionAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var (symbol, _) = await SymbolAtAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        symbol = symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol;
        var description = $"({KindName(symbol)}) {symbol.ToDisplayString(HoverFormat)}" + (symbol.ContainingAssembly is { } assembly ? $" — in {assembly.Identity.Name}" : "");
        var source = symbol.Locations.Any(l => l.IsInSource) ? symbol
            : await SymbolFinder.FindSourceDefinitionAsync(symbol.OriginalDefinition, document.Project.Solution, cancellationToken).ConfigureAwait(false);
        var location = source?.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is not { } tree)
        {
            return new DefinitionLocation(null, 0, 0, false, description);
        }

        var span = location.SourceSpan;
        var documentTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        bool same = tree == documentTree || (document.FilePath is not null && string.Equals(tree.FilePath, document.FilePath, StringComparison.OrdinalIgnoreCase));
        return new DefinitionLocation(same ? document.FilePath : tree.FilePath, span.Start, span.Length, same, description);
    }

    // ---- references ----

    /// <summary>
    /// The places that refer to the symbol at <paramref name="position"/> (its source declarations and every reference in
    /// the solution), by file and place, each with its line's text, at most <paramref name="max"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SymbolLocation>> ReferencesAsync(Document document, int position, int max, CancellationToken cancellationToken)
    {
        var (symbol, _) = await SymbolAtAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        symbol = symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol;
        var solution = document.Project.Solution;
        var found = await SymbolFinder.FindReferencesAsync(symbol.OriginalDefinition, solution, cancellationToken).ConfigureAwait(false);
        var places = new List<Location>();
        foreach (var reference in found)
        {
            places.AddRange(reference.Definition.Locations.Where(l => l.IsInSource));
            foreach (var location in reference.Locations)
            {
                places.Add(location.Location);
            }
        }

        var seen = new HashSet<(string?, int)>();
        var result = new List<SymbolLocation>();
        string kind = KindName(symbol);
        string? container = symbol.ContainingType?.ToDisplayString();
        foreach (var location in places.OrderBy(l => l.SourceTree?.FilePath, StringComparer.Ordinal).ThenBy(l => l.SourceSpan.Start))
        {
            if (location.SourceTree is not { } tree || !seen.Add((tree.FilePath, location.SourceSpan.Start)))
            {
                continue;
            }

            var text = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var line = text.Lines.GetLineFromPosition(location.SourceSpan.Start);
            string path = tree.FilePath;
            result.Add(new SymbolLocation(symbol.Name, kind, container, string.IsNullOrEmpty(path) ? null : path, line.LineNumber + 1,
                location.SourceSpan.Start - line.Start + 1, text.ToString(line.Span).Trim()));
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    // ---- format ----

    /// <summary>The edits that format the document (or its span), in its positions.</summary>
    public static async Task<IReadOnlyList<TextEdit>> FormatAsync(Document document, TextSpan? span, LanguageSettings settings, string? documentNewLine, CancellationToken cancellationToken)
    {
#pragma warning disable CS0618 // The option set is the public way to give Formatter the editor's indentation.
        var options = document.Project.Solution.Options
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, !settings.InsertSpaces)
            .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, settings.TabSize)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, settings.TabSize)
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, settings.NewLine ?? documentNewLine ?? Environment.NewLine);
        var formatted = span is { } range
            ? await Formatter.FormatAsync(document, range, options, cancellationToken).ConfigureAwait(false)
            : await Formatter.FormatAsync(document, options, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        var changes = await formatted.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false);
        var edits = new List<TextEdit>();
        foreach (var change in changes)
        {
            edits.Add(new TextEdit(change.Span.Start, change.Span.Length, change.NewText ?? ""));
        }

        edits.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return edits;
    }

    // ---- documentation ----

    /// <summary>A symbol's documentation: its summary and its parameters', as plain text.</summary>
    internal sealed record Docs(string? Summary, Dictionary<string, string> Parameters);

    internal static Docs? Documentation(ISymbol symbol, CancellationToken cancellationToken)
    {
        string? xml;
        try
        {
            xml = symbol.GetDocumentationCommentXml(System.Globalization.CultureInfo.InvariantCulture, expandIncludes: false, cancellationToken);
        }
        catch (Exception e) when (e is IOException or XmlException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(xml) ? null : ParseDocs(xml);
    }

    internal static Docs? ParseDocs(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            string? summary = null;
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name == "summary")
                {
                    summary = Flatten(reader);
                }
                else if (reader.Name == "param" && reader.GetAttribute("name") is { } name)
                {
                    parameters[name] = Flatten(reader);
                }
            }

            return new Docs(summary, parameters);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>The text of the element the reader is on, its references (see, paramref, typeparamref, c) as their names, whitespace collapsed.</summary>
    private static string Flatten(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return "";
        }

        var text = new StringBuilder();
        int depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    text.Append(reader.Value);
                    break;
                case XmlNodeType.Element:
                    var reference = reader.GetAttribute("cref") ?? reader.GetAttribute("langword") ?? reader.GetAttribute("name") ?? reader.GetAttribute("href");
                    if (reference is not null)
                    {
                        int colon = reference.IndexOf(':', StringComparison.Ordinal);
                        var name = colon >= 0 && colon <= 2 ? reference[(colon + 1)..] : reference;
                        int paren = name.IndexOf('(', StringComparison.Ordinal);
                        name = paren >= 0 ? name[..paren] : name;
                        int dot = name.LastIndexOf('.');
                        text.Append(dot >= 0 && reader.Name is "see" or "seealso" ? name[(dot + 1)..] : name);
                    }

                    if (reader.Name is "para" or "br")
                    {
                        text.Append(' ');
                    }

                    break;
            }
        }

        var collapsed = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text.ToString())
        {
            if (char.IsWhiteSpace(c))
            {
                space = collapsed.Length > 0;
                continue;
            }

            if (space)
            {
                collapsed.Append(' ');
                space = false;
            }

            collapsed.Append(c);
        }

        return collapsed.ToString();
    }
}
