// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Craft.CSharp;

/// <summary>
/// The document's symbols (the X restart's piece 3b: quick open's '@', the breadcrumb, the chat's '@'): one walk of the
/// declarations of the last parse, on the thread pool, once per parse (rule 75): the answer is kept with the parse it is of,
/// and asked again for the same version it is the same list.
/// </summary>
public sealed partial class CSharpLanguageSession
{
    private SymbolsOfParse? _symbols;

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _symbols) is { } kept && IsOf(snapshot, kept.Parsed))
        {
            return Task.FromResult(kept.Symbols);
        }

        return Task.Run(() => SymbolsOfAsync(snapshot, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<DocumentSymbol>> SymbolsOfAsync(ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        // The parse of this snapshot: the last one when it is of it, else after the worker caught up with it.
        var parsed = Volatile.Read(ref _parsed);
        if ((parsed is null || !IsOf(snapshot, parsed)) && ReferenceEquals(snapshot.Buffer, _buffer))
        {
            Task idle;
            lock (_gate)
            {
                idle = _idle.Task;
            }

            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
            parsed = Volatile.Read(ref _parsed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _symbols) is { } kept && parsed is not null && ReferenceEquals(kept.Parsed, parsed) && IsOf(snapshot, parsed))
        {
            return kept.Symbols;
        }

        if (parsed is not null && IsOf(snapshot, parsed))
        {
            var symbols = Walk(parsed.Root, cancellationToken);
            Volatile.Write(ref _symbols, new SymbolsOfParse(parsed, symbols));
            return symbols;
        }

        // An older snapshot than the last parse (or another buffer's): parsed for this answer only, not kept.
        var tree = CSharpSyntaxTree.ParseText(new SnapshotSourceText(snapshot), _options, cancellationToken: cancellationToken);
        return Walk(await tree.GetRootAsync(cancellationToken).ConfigureAwait(false), cancellationToken);
    }

    /// <summary>The declarations under <paramref name="root"/>, outermost first, in the order of the text.</summary>
    internal static IReadOnlyList<DocumentSymbol> Walk(SyntaxNode root, CancellationToken cancellationToken = default)
    {
        var detail = new StringBuilder();
        return Children(root, detail, cancellationToken);
    }

    private static DocumentSymbol[] Children(SyntaxNode node, StringBuilder detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<DocumentSymbol>? found = null;
        var members = node switch
        {
            CompilationUnitSyntax unit => unit.Members,
            BaseNamespaceDeclarationSyntax ns => ns.Members,
            TypeDeclarationSyntax type => type.Members,
            _ => default,
        };
        foreach (var member in members)
        {
            Add(member, ref found, detail, cancellationToken);
        }

        if (node is EnumDeclarationSyntax @enum)
        {
            foreach (var value in @enum.Members)
            {
                (found ??= []).Add(Leaf(value, value.Identifier, ScopeKind.EnumMember, value.EqualsValue is { } equals ? equals.Value.ToString() : null));
            }
        }

        return found is null ? [] : [.. found];
    }

    private static void Add(MemberDeclarationSyntax member, ref List<DocumentSymbol>? found, StringBuilder detail, CancellationToken cancellationToken)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax ns:
                (found ??= []).Add(Node(ns, ns.Name.ToString(), ns.Name.Span, ScopeKind.Namespace, null, Children(ns, detail, cancellationToken)));
                break;
            case BaseTypeDeclarationSyntax type:
                (found ??= []).Add(Node(type, type.Identifier.ValueText + TypeParameters(type), type.Identifier.Span, ScopeKind.Type, KindWord(type), Children(type, detail, cancellationToken)));
                break;
            case DelegateDeclarationSyntax @delegate:
                (found ??= []).Add(Leaf(@delegate, @delegate.Identifier, ScopeKind.Type, "delegate " + Parameters(@delegate.ParameterList, detail)));
                break;
            case MethodDeclarationSyntax method:
                (found ??= []).Add(Leaf(method, method.Identifier, ScopeKind.Method, Parameters(method.ParameterList, detail) + " : " + method.ReturnType));
                break;
            case ConstructorDeclarationSyntax constructor:
                (found ??= []).Add(Leaf(constructor, constructor.Identifier, ScopeKind.Method, Parameters(constructor.ParameterList, detail)));
                break;
            case DestructorDeclarationSyntax destructor:
                (found ??= []).Add(Node(destructor, "~" + destructor.Identifier.ValueText, destructor.Identifier.Span, ScopeKind.Method, "()", []));
                break;
            case OperatorDeclarationSyntax @operator:
                (found ??= []).Add(Node(@operator, "operator " + @operator.OperatorToken.ValueText, @operator.OperatorToken.Span, ScopeKind.Method, Parameters(@operator.ParameterList, detail), []));
                break;
            case ConversionOperatorDeclarationSyntax conversion:
                (found ??= []).Add(Node(conversion, conversion.ImplicitOrExplicitKeyword.ValueText + " operator " + conversion.Type, conversion.Type.Span, ScopeKind.Method,
                    Parameters(conversion.ParameterList, detail), []));
                break;
            case PropertyDeclarationSyntax property:
                (found ??= []).Add(Leaf(property, property.Identifier, ScopeKind.Property, property.Type.ToString()));
                break;
            case IndexerDeclarationSyntax indexer:
                (found ??= []).Add(Node(indexer, "this[]", indexer.ThisKeyword.Span, ScopeKind.Property, BracketParameters(indexer.ParameterList, detail) + " : " + indexer.Type, []));
                break;
            case EventDeclarationSyntax @event:
                (found ??= []).Add(Leaf(@event, @event.Identifier, ScopeKind.Event, @event.Type.ToString()));
                break;
            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                {
                    (found ??= []).Add(Leaf(eventField, variable.Identifier, ScopeKind.Event, eventField.Declaration.Type.ToString()));
                }

                break;
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    (found ??= []).Add(Leaf(field, variable.Identifier, ScopeKind.Field, field.Declaration.Type.ToString()));
                }

                break;
            case GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax local }:
                (found ??= []).Add(Leaf(local, local.Identifier, ScopeKind.Method, Parameters(local.ParameterList, detail) + " : " + local.ReturnType));
                break;
        }
    }

    // A declaration with its name token: its span without leading trivia (comments, blank lines), its name's span.
    private static DocumentSymbol Leaf(SyntaxNode node, SyntaxToken name, ScopeKind kind, string? detail) =>
        Node(node, name.ValueText, name.Span, kind, detail, []);

    private static DocumentSymbol Node(SyntaxNode node, string name, Microsoft.CodeAnalysis.Text.TextSpan nameSpan, ScopeKind kind, string? detail, DocumentSymbol[] children)
    {
        var span = node.Span;
        return new DocumentSymbol(name, kind, span.Start, span.Length, nameSpan.Start, nameSpan.Length, detail, children);
    }

    private static string TypeParameters(BaseTypeDeclarationSyntax type) =>
        type is TypeDeclarationSyntax { TypeParameterList: { Parameters.Count: > 0 } list } ? list.ToString() : "";

    private static string KindWord(BaseTypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        _ => type is TypeDeclarationSyntax declared ? declared.Keyword.ValueText : "type",
    };

    // "(int line, int column)": each parameter's type and name, as written, on one line.
    private static string Parameters(BaseParameterListSyntax? list, StringBuilder text) => Join(list, text, '(', ')');

    private static string BracketParameters(BaseParameterListSyntax? list, StringBuilder text) => Join(list, text, '[', ']');

    private static string Join(BaseParameterListSyntax? list, StringBuilder text, char open, char close)
    {
        text.Clear().Append(open);
        if (list is not null)
        {
            for (int i = 0; i < list.Parameters.Count; i++)
            {
                var parameter = list.Parameters[i];
                if (i > 0)
                {
                    text.Append(", ");
                }

                if (parameter.Type is { } type)
                {
                    text.Append(type).Append(' ');
                }

                text.Append(parameter.Identifier.ValueText);
            }
        }

        return text.Append(close).ToString();
    }

    // The symbols of one parse (swapped as one immutable value).
    private sealed record SymbolsOfParse(Parsed Parsed, IReadOnlyList<DocumentSymbol> Symbols);
}
