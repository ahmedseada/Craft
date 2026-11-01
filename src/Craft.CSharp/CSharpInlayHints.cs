// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// C#'s inlay hints (X part 9), from Roslyn's semantic model over a range: Roslyn's own inline hints service is internal
/// in the Features version Craft ships with (its types are not public), so its rules are followed here on the public API,
/// with its defaults: parameter names before literal arguments, object creations and indexer arguments (not before other
/// expressions), left out where the parameters differ only by a numeric suffix, where the method's name ends with the
/// parameter's (<c>SetColor(color)</c>), or where the argument already names it; inferred types after <c>var</c> names
/// (declarations, <c>foreach</c>, <c>out var</c>, deconstruction) and before implicitly typed lambda parameters.
/// Anonymous and erroneous types give none. Runs on the thread pool.
/// </summary>
internal static class CSharpInlayHints
{
    public static async Task<IReadOnlyList<InlayHint>> ComputeAsync(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return [];
        }

        var hints = new List<InlayHint>();
        foreach (var node in root.DescendantNodes(span))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.SpanStart < span.Start || node.SpanStart > span.End)
            {
                continue;
            }

            switch (node)
            {
                case ArgumentSyntax argument when argument.NameColon is null && argument.Parent is BaseArgumentListSyntax list:
                    Parameter(model, argument, list, hints, cancellationToken);
                    break;
                case VariableDeclarationSyntax { Type.IsVar: true } declaration:
                    foreach (var variable in declaration.Variables)
                    {
                        if (model.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol local)
                        {
                            Type(model, local.Type, variable.Identifier.Span.End, hints, before: false);
                        }
                    }

                    break;
                case ForEachStatementSyntax { Type.IsVar: true } loop:
                    if (model.GetDeclaredSymbol(loop, cancellationToken) is ILocalSymbol item)
                    {
                        Type(model, item.Type, loop.Identifier.Span.End, hints, before: false);
                    }

                    break;
                case DeclarationExpressionSyntax { Type.IsVar: true, Designation: SingleVariableDesignationSyntax single }:
                    if (model.GetDeclaredSymbol(single, cancellationToken) is ILocalSymbol outLocal)
                    {
                        Type(model, outLocal.Type, single.Identifier.Span.End, hints, before: false);
                    }

                    break;
                case SingleVariableDesignationSyntax designation when designation.Parent is ParenthesizedVariableDesignationSyntax:
                    if (model.GetDeclaredSymbol(designation, cancellationToken) is ILocalSymbol part)
                    {
                        Type(model, part.Type, designation.Identifier.Span.End, hints, before: false);
                    }

                    break;
                case ParameterSyntax { Type: null } parameter when parameter.Parent is SimpleLambdaExpressionSyntax || parameter.Parent?.Parent is ParenthesizedLambdaExpressionSyntax:
                    if (model.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol lambdaParameter)
                    {
                        Type(model, lambdaParameter.Type, parameter.Identifier.SpanStart, hints, before: true);
                    }

                    break;
            }
        }

        hints.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        return hints;
    }

    private static void Parameter(SemanticModel model, ArgumentSyntax argument, BaseArgumentListSyntax list, List<InlayHint> hints, CancellationToken cancellationToken)
    {
        if (!Shows(argument.Expression, list))
        {
            return;
        }

        var invoked = list.Parent switch
        {
            InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation, cancellationToken),
            BaseObjectCreationExpressionSyntax creation => model.GetSymbolInfo(creation, cancellationToken),
            ElementAccessExpressionSyntax access => model.GetSymbolInfo(access, cancellationToken),
            ConstructorInitializerSyntax initializer => model.GetSymbolInfo(initializer, cancellationToken),
            PrimaryConstructorBaseTypeSyntax primary => model.GetSymbolInfo(primary, cancellationToken),
            _ => default,
        };
        var symbol = invoked.Symbol ?? (invoked.CandidateSymbols.Length == 1 ? invoked.CandidateSymbols[0] : null);
        var parameters = symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol indexer => indexer.Parameters,
            _ => default,
        };
        if (parameters.IsDefaultOrEmpty)
        {
            return;
        }

        int index = list.Arguments.IndexOf(argument);
        IParameterSymbol? parameter = index < parameters.Length ? parameters[index]
            : parameters[^1].IsParams ? parameters[^1] : null;
        if (parameter is null || parameter.Name.Length == 0)
        {
            return;
        }

        if (index > parameters.Length - 1)
        {
            return;                                           // the params array's later items: the name is on the first one
        }

        if (DifferOnlyBySuffix(parameters) || MatchesMethodIntent(symbol!, parameter, parameters) || MatchesArgumentName(argument.Expression, parameter.Name))
        {
            return;
        }

        hints.Add(new InlayHint(argument.Expression.SpanStart, parameter.Name + ":", InlayHintKind.Parameter) { PaddingRight = true, Tooltip = parameter.ToDisplayString() });
    }

    // Roslyn's default kinds: literals (and their negation, a cast of one), object creations, and every indexer argument.
    private static bool Shows(ExpressionSyntax expression, BaseArgumentListSyntax list)
    {
        if (list is BracketedArgumentListSyntax)
        {
            return true;
        }

        while (expression is PrefixUnaryExpressionSyntax or CastExpressionSyntax or ParenthesizedExpressionSyntax)
        {
            expression = expression switch
            {
                PrefixUnaryExpressionSyntax unary => unary.Operand,
                CastExpressionSyntax cast => cast.Expression,
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                _ => expression,
            };
        }

        return expression is LiteralExpressionSyntax or InterpolatedStringExpressionSyntax or BaseObjectCreationExpressionSyntax
            or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax or DefaultExpressionSyntax;
    }

    // "arg1, arg2" or "x1, x2": names that differ only by a number at their end tell nothing.
    private static bool DifferOnlyBySuffix(System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length < 2)
        {
            return false;
        }

        string? stem = null;
        foreach (var parameter in parameters)
        {
            var name = parameter.Name.AsSpan();
            int end = name.Length;
            while (end > 0 && char.IsAsciiDigit(name[end - 1]))
            {
                end--;
            }

            if (end == name.Length || end == 0)
            {
                return false;
            }

            if (stem is null)
            {
                stem = name[..end].ToString();
            }
            else if (!name[..end].Equals(stem, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // A one-parameter method whose name ends with the parameter's (SetColor(color), WithName(name)).
    private static bool MatchesMethodIntent(ISymbol symbol, IParameterSymbol parameter, System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters) =>
        symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary } method && parameters.Length == 1
        && method.Name.EndsWith(parameter.Name, StringComparison.OrdinalIgnoreCase);

    // An argument that already says the name (a variable or member of that name, ignoring case).
    private static bool MatchesArgumentName(ExpressionSyntax expression, string name)
    {
        string? said = expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };
        return said is not null && string.Equals(said, name, StringComparison.OrdinalIgnoreCase);
    }

    private static void Type(SemanticModel model, ITypeSymbol? type, int position, List<InlayHint> hints, bool before)
    {
        if (type is null || type.TypeKind == TypeKind.Error || type.IsAnonymousType || (type is INamedTypeSymbol { IsTupleType: false } named && named.IsAnonymousType))
        {
            return;
        }

        string text = type.ToMinimalDisplayString(model, position, SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (text.Length == 0)
        {
            return;
        }

        hints.Add(before
            ? new InlayHint(position, text, InlayHintKind.Type) { PaddingRight = true }
            : new InlayHint(position, ": " + text, InlayHintKind.Type));
    }
}
