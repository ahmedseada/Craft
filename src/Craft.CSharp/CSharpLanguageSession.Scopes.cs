// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Craft.CSharp;

/// <summary>The breadcrumb's scopes (phase D): the types and the member around a position, from the last parse.</summary>
public sealed partial class CSharpLanguageSession
{
    // Asked on the UI thread when the caret moves: the walk reuses one buffer, and the same scopes give the same array back.
    private readonly List<ScopeName> _scopeBuffer = [];
    private ScopeName[] _scopes = [];

    /// <inheritdoc />
    public IReadOnlyList<ScopeName> ScopeAt(ITextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Volatile.Read(ref _parsed) is not { } parsed)
        {
            return [];
        }

        var root = parsed.Root;
        int end = root.FullSpan.End;
        var token = root.FindToken(Math.Clamp(position, 0, Math.Max(0, end - 1)));
        _scopeBuffer.Clear();
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (ScopeOf(node) is { } scope)
            {
                _scopeBuffer.Add(scope);
            }
        }

        _scopeBuffer.Reverse();
        if (_scopeBuffer.Count == _scopes.Length)
        {
            bool same = true;
            for (int i = 0; i < _scopes.Length && same; i++)
            {
                same = _scopes[i] == _scopeBuffer[i];
            }

            if (same)
            {
                return _scopes;
            }
        }

        _scopes = [.. _scopeBuffer];
        return _scopes;
    }

    // The name a node gives the breadcrumb: types and members (namespaces are the path's, not the scope's).
    private static ScopeName? ScopeOf(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => new ScopeName(type.Identifier.ValueText, ScopeKind.Type),
        DelegateDeclarationSyntax @delegate => new ScopeName(@delegate.Identifier.ValueText, ScopeKind.Type),
        MethodDeclarationSyntax method => new ScopeName(method.Identifier.ValueText, ScopeKind.Method),
        ConstructorDeclarationSyntax constructor => new ScopeName(constructor.Identifier.ValueText, ScopeKind.Method),
        DestructorDeclarationSyntax destructor => new ScopeName(destructor.Identifier.ValueText, ScopeKind.Method),
        LocalFunctionStatementSyntax local => new ScopeName(local.Identifier.ValueText, ScopeKind.Method),
        OperatorDeclarationSyntax @operator => new ScopeName(@operator.OperatorToken.ValueText, ScopeKind.Method),
        PropertyDeclarationSyntax property => new ScopeName(property.Identifier.ValueText, ScopeKind.Member),
        EventDeclarationSyntax @event => new ScopeName(@event.Identifier.ValueText, ScopeKind.Member),
        IndexerDeclarationSyntax => new ScopeName("this[]", ScopeKind.Member),
        BaseFieldDeclarationSyntax { Declaration.Variables: [var first, ..] } => new ScopeName(first.Identifier.ValueText, ScopeKind.Member),
        _ => null,
    };
}
