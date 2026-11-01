// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Craft.CSharp;

/// <summary>
/// C#'s code lenses (X part 9), the "csharp" provider: over each type, method, constructor, property and event a
/// "N references" lens (a placeholder until the editor resolves it on screen: Roslyn's find references counted, the
/// declarations left out; a click puts the caret on the name and runs Find references), and over each test method (an
/// xUnit, NUnit or MSTest test attribute) "Run test" (through the host's Run view: <see cref="IEditorHost.RunTestAsync"/>,
/// asked and run in the sandbox as any run) and "Debug test" (Craft has no debugger: it says so). Off the UI thread.
/// </summary>
public sealed class CSharpCodeLens : ICodeLensProvider
{
    /// <summary>Its name in <see cref="CodeLensProviders"/>.</summary>
    public const string ProviderName = "csharp";

    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "Test", "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod", "SkippableFact", "SkippableTheory",
    };

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "C#'s code lenses: references (Roslyn), Run test and Debug test";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [CSharpLanguage.RegularName];

    /// <inheritdoc />
    public Task<IReadOnlyList<CodeLens>> GetLensesAsync(CodeLensRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run<IReadOnlyList<CodeLens>>(() => Lenses(request, cancellationToken), cancellationToken);
    }

    private static List<CodeLens> Lenses(CodeLensRequest request, CancellationToken cancellationToken)
    {
        var lenses = new List<CodeLens>();
        var session = request.Session as CSharpLanguageSession;
        var snapshot = request.Snapshot;
        SyntaxNode? root = null;
        bool rootRead = false;
        Walk(request.Members);
        return lenses;

        void Walk(IReadOnlyList<DocumentSymbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (symbol.Kind)
                {
                    case ScopeKind.Type or ScopeKind.Method or ScopeKind.Property or ScopeKind.Event:
                        Add(symbol);
                        break;
                }

                if (symbol.Children.Count > 0)
                {
                    Walk(symbol.Children);
                }
            }
        }

        void Add(DocumentSymbol symbol)
        {
            if (session is not null && symbol.NameLength > 0)
            {
                int name = symbol.NameStart;
                lenses.Add(new CodeLens(symbol.Start, "… references")
                {
                    Tooltip = "Find references (Shift+F12)",
                    Order = 0,
                    ResolveAsync = async token =>
                    {
                        int? count = await session.CountReferencesAsync(snapshot, name, token).ConfigureAwait(false);
                        string title = count switch
                        {
                            null => "references",
                            1 => "1 reference",
                            _ => string.Create(CultureInfo.InvariantCulture, $"{count} references"),
                        };
                        return new CodeLens(symbol.Start, title) { Tooltip = "Find references (Shift+F12)", Order = 0, RunAsync = References(request, name) };
                    },
                });
            }

            if (symbol.Kind != ScopeKind.Method)
            {
                return;
            }

            if (!rootRead)
            {
                rootRead = true;
                root = RootOf(session, snapshot, cancellationToken);
            }

            if (root is null || TestNameOf(root, symbol) is not { } test)
            {
                return;
            }

            var host = request.Host;
            string? file = request.FilePath;
            lenses.Add(new CodeLens(symbol.Start, "Run test")
            {
                Tooltip = $"dotnet test --filter FullyQualifiedName={test} (asked first, in the Run view's sandbox)",
                Order = 1,
                RunAsync = host is null || file is null ? null : async token =>
                {
                    if (!await host.RunTestAsync(file, test, token).ConfigureAwait(true))
                    {
                        throw new InvalidOperationException($"No test project of the Run view runs '{test}'.");
                    }
                },
            });
            lenses.Add(new CodeLens(symbol.Start, "Debug test")
            {
                Tooltip = "Debugging is not available: Craft has no debugger yet",
                Order = 2,
                RunAsync = _ => throw new InvalidOperationException("Debugging is not available: Craft has no debugger yet; Run test runs it without one."),
            });
        }
    }

    // A references lens's click: the caret on the name, then Find references (the host's Shift+F12).
    private static Func<CancellationToken, Task>? References(CodeLensRequest request, int name)
    {
        if (request.Host is not { } host || request.FilePath is not { } file)
        {
            return null;
        }

        return async token =>
        {
            foreach (var document in host.Documents)
            {
                if (string.Equals(document.Document.FilePath, file, StringComparison.OrdinalIgnoreCase))
                {
                    host.Select(document, SelectionState.At(name));
                    break;
                }
            }

            await host.ExecuteCommandAsync("craft.findReferences", token).ConfigureAwait(true);
        };
    }

    // The session's last parse when it is of the snapshot, else a parse of it (once per request, off the UI thread).
    private static SyntaxNode? RootOf(CSharpLanguageSession? session, ITextSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (session?.LastParse is { } parse && parse.Snapshot.Version == snapshot.Version && ReferenceEquals(parse.Snapshot.Buffer, snapshot.Buffer))
        {
            return parse.Tree.GetRoot(cancellationToken);
        }

        return CSharpSyntaxTree.ParseText(new SnapshotSourceText(snapshot), new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None), cancellationToken: cancellationToken).GetRoot(cancellationToken);
    }

    /// <summary>The fully qualified name ("App.Tests.MathTests.Adds") of the test method <paramref name="symbol"/> declares, or null when it is not a test.</summary>
    internal static string? TestNameOf(SyntaxNode root, DocumentSymbol symbol)
    {
        if (symbol.NameStart + symbol.NameLength > root.FullSpan.End)
        {
            return null;
        }

        var node = root.FindNode(new TextSpan(symbol.NameStart, Math.Max(1, symbol.NameLength)), getInnermostNodeForTie: true);
        var method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null || !IsTest(method))
        {
            return null;
        }

        var name = new StringBuilder(method.Identifier.ValueText);
        for (var parent = method.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case TypeDeclarationSyntax type:
                    name.Insert(0, type.Identifier.ValueText + ".");
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    name.Insert(0, ns.Name.ToString() + ".");
                    break;
            }
        }

        return name.ToString();
    }

    private static bool IsTest(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                string name = attribute.Name switch
                {
                    QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                    SimpleNameSyntax simple => simple.Identifier.ValueText,
                    AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
                    _ => attribute.Name.ToString(),
                };
                if (name.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    name = name[..^"Attribute".Length];
                }

                if (TestAttributes.Contains(name))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
