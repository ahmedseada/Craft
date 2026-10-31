// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Craft.CSharp;

/// <summary>
/// C#'s folding ranges (phase X), from Roslyn's syntax tree: namespaces, types, members and accessors with bodies,
/// statements' blocks (if, else, loops, try, catch, finally, using, lock, switch), lambdas' and local functions' bodies,
/// initializers and collection expressions over several lines, runs of usings, <c>#region</c> to <c>#endregion</c>, and
/// comments over several lines. Each range runs from its declaration's (or statement's) first line to its closing
/// brace's. The editor asks off the UI thread: the session's last parse is reused when it is of the snapshot asked for,
/// else the snapshot is parsed here (the UI thread never waits for it).
/// </summary>
public sealed class CSharpFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "csharp";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "C#: namespaces, types, members, blocks, initializers, usings, #region and comments, from Roslyn's syntax tree";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [CSharpLanguage.RegularName, CSharpLanguage.ScriptName];

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot;
        SyntaxNode root;
        if (request.Session is CSharpLanguageSession session && session.LastParse is { } parse
            && parse.Snapshot.Version == snapshot.Version && ReferenceEquals(parse.Snapshot.Buffer, snapshot.Buffer))
        {
            root = await parse.Tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var kind = string.Equals(request.Language, CSharpLanguage.ScriptName, StringComparison.OrdinalIgnoreCase) ? SourceCodeKind.Script : SourceCodeKind.Regular;
            var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, kind);
            root = await CSharpSyntaxTree.ParseText(new SnapshotSourceText(snapshot), options, cancellationToken: cancellationToken).GetRootAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ranges(root, snapshot, cancellationToken);
    }

    /// <summary>The ranges of <paramref name="root"/>, the tree of <paramref name="snapshot"/>.</summary>
    public static List<FoldingRange> Ranges(SyntaxNode root, ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(snapshot);
        var ranges = new List<FoldingRange>();
        int length = snapshot.Length;
        int Line(int position) => snapshot.GetLineIndex(Math.Clamp(position, 0, length));
        void Add(int start, int end, FoldingRangeKind kind = FoldingRangeKind.Region)
        {
            int first = Line(start);
            int last = Line(end);
            if (last > first)
            {
                ranges.Add(new FoldingRange(first, last, kind));
            }
        }

        int visited = 0;
        foreach (var node in root.DescendantNodes())
        {
            if ((++visited & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            switch (node)
            {
                case BaseNamespaceDeclarationSyntax ns when ns is NamespaceDeclarationSyntax block && !block.CloseBraceToken.IsMissing:
                    Add(HeaderStart(ns), block.CloseBraceToken.SpanStart);
                    break;
                case BaseTypeDeclarationSyntax type when !type.CloseBraceToken.IsMissing && type.OpenBraceToken.Span.Length > 0:
                    Add(HeaderStart(type), type.CloseBraceToken.SpanStart);
                    break;
                case BaseMethodDeclarationSyntax method when method.Body is { } body:
                    Add(HeaderStart(method), body.CloseBraceToken.SpanStart);
                    break;
                case BaseMethodDeclarationSyntax method when method.ExpressionBody is not null:
                    Add(HeaderStart(method), method.Span.End);
                    break;
                case BasePropertyDeclarationSyntax property when property.AccessorList is { } accessors:
                    Add(HeaderStart(property), accessors.CloseBraceToken.SpanStart);
                    break;
                case AccessorDeclarationSyntax accessor when accessor.Body is { } body:
                    Add(HeaderStart(accessor), body.CloseBraceToken.SpanStart);
                    break;
                case LocalFunctionStatementSyntax local when local.Body is { } body:
                    Add(local.SpanStart, body.CloseBraceToken.SpanStart);
                    break;
                case BlockSyntax block:
                    AddBlock(block);
                    break;
                case SwitchStatementSyntax @switch:
                    Add(@switch.SpanStart, @switch.CloseBraceToken.SpanStart);
                    break;
                case SwitchExpressionSyntax @switch:
                    Add(@switch.SpanStart, @switch.CloseBraceToken.SpanStart);
                    break;
                case InitializerExpressionSyntax initializer:
                    Add(initializer.Parent is BaseObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax
                        ? initializer.Parent.SpanStart : initializer.OpenBraceToken.SpanStart, initializer.CloseBraceToken.SpanStart);
                    break;
                case AnonymousObjectCreationExpressionSyntax anonymous:
                    Add(anonymous.SpanStart, anonymous.CloseBraceToken.SpanStart);
                    break;
                case CollectionExpressionSyntax collection:
                    Add(collection.OpenBracketToken.SpanStart, collection.CloseBracketToken.SpanStart);
                    break;
            }
        }

        AddUsings(root, Add);
        AddRegions(root, Line, ranges);
        AddComments(root, Line, ranges, cancellationToken);
        return ranges;

        void AddBlock(BlockSyntax block)
        {
            switch (block.Parent)
            {
                case BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax:
                    return;     // the member folds from its declaration
                case BlockSyntax or GlobalStatementSyntax or null:
                    Add(block.OpenBraceToken.SpanStart, block.CloseBraceToken.SpanStart);
                    return;
                case { } owner:
                    Add(owner.SpanStart, block.CloseBraceToken.SpanStart);
                    return;
            }
        }
    }

    // A member's first line after its attributes (an attribute list on lines of its own stays shown above the fold's header).
    private static int HeaderStart(SyntaxNode node)
    {
        if (node is MemberDeclarationSyntax { AttributeLists.Count: > 0 } member)
        {
            var next = member.AttributeLists[^1].GetLastToken().GetNextToken();
            if (next.SpanStart < node.Span.End)
            {
                return next.SpanStart;
            }
        }

        if (node is AccessorDeclarationSyntax { AttributeLists.Count: > 0 } accessor)
        {
            return accessor.Keyword.SpanStart;
        }

        return node.SpanStart;
    }

    // Each run of using directives over more than one line, under the compilation unit or a namespace.
    private static void AddUsings(SyntaxNode root, Action<int, int, FoldingRangeKind> add)
    {
        foreach (var node in root.DescendantNodesAndSelf(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
        {
            var usings = node switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default,
            };
            if (usings.Count > 1)
            {
                add(usings[0].SpanStart, usings[^1].Span.End, FoldingRangeKind.Imports);
            }
        }
    }

    private static void AddRegions(SyntaxNode root, Func<int, int> line, List<FoldingRange> ranges)
    {
        var open = new Stack<int>();
        for (var directive = (root as CSharpSyntaxNode)?.GetFirstDirective(); directive is not null; directive = directive.GetNextDirective())
        {
            if (directive is RegionDirectiveTriviaSyntax)
            {
                open.Push(line(directive.SpanStart));
            }
            else if (directive is EndRegionDirectiveTriviaSyntax && open.Count > 0)
            {
                int first = open.Pop();
                int last = line(directive.SpanStart);
                if (last > first)
                {
                    ranges.Add(new FoldingRange(first, last, FoldingRangeKind.Marked));
                }
            }
        }
    }

    // Block comments and documentation comments over several lines, and runs of line comments on consecutive lines.
    private static void AddComments(SyntaxNode root, Func<int, int> line, List<FoldingRange> ranges, CancellationToken cancellationToken)
    {
        int runFirst = -1;
        int runLast = -1;
        SyntaxToken runToken = default;
        int visited = 0;
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if ((++visited & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                    // A run is the line comments of one token's leading trivia (code between two comments ends it).
                    int at = line(trivia.SpanStart);
                    var token = trivia.Token;
                    bool leading = trivia.SpanStart < token.SpanStart;
                    if (runFirst >= 0 && leading && at == runLast + 1 && token == runToken)
                    {
                        runLast = at;
                    }
                    else
                    {
                        Flush();
                        if (leading)
                        {
                            runFirst = runLast = at;
                            runToken = token;
                        }
                    }

                    break;
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    Flush();
                    int first = line(trivia.SpanStart);
                    int last = line(Math.Max(trivia.SpanStart, trivia.Span.End - 1));
                    if (last > first)
                    {
                        ranges.Add(new FoldingRange(first, last, FoldingRangeKind.Comment));
                    }

                    break;
                case SyntaxKind.WhitespaceTrivia:
                case SyntaxKind.EndOfLineTrivia:
                    break;
                default:
                    Flush();
                    break;
            }
        }

        Flush();

        void Flush()
        {
            if (runFirst >= 0 && runLast > runFirst)
            {
                ranges.Add(new FoldingRange(runFirst, runLast, FoldingRangeKind.Comment));
            }

            runFirst = runLast = -1;
        }
    }
}
