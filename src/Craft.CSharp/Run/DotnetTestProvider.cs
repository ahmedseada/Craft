// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text;
using Craft.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Craft.CSharp.Run;

/// <summary>
/// "dotnet" tests (X part 9, the Test explorer): the test projects (<c>IsTestProject</c> or a test framework's package, as
/// the Run view reads them) and, in each project's C# files, the methods marked as xUnit's (<c>[Fact]</c>, <c>[Theory]</c>,
/// <c>[SkippableFact]</c>), NUnit's (<c>[Test]</c>, <c>[TestCase]</c>, <c>[TestCaseSource]</c>) or MSTest's
/// (<c>[TestMethod]</c>, <c>[DataTestMethod]</c>) tests, read from Roslyn's syntax tree (no build, nothing runs): a file is
/// parsed only when its bytes hold one of the attributes' words, the files in parallel. A run is <c>dotnet test PROJECT
/// --filter</c> (the tests' fully qualified names) with a TRX logger writing the request's results file and the console
/// logger at normal verbosity (what the results are read from when no file came).
/// </summary>
public sealed class DotnetTestProvider : ITestProvider
{
    /// <summary>Its name in <see cref="TestProviders"/>.</summary>
    public const string ProviderName = "dotnet";

    private static readonly System.Buffers.SearchValues<char> FilterSpecials = System.Buffers.SearchValues.Create(@"\()&|=!~");

    // Above this many tests a filter names classes (FullyQualifiedName~Class.) rather than each method: a command line stays short.
    private const int MaxNamedTests = 40;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Title => "C# (.NET)";

    /// <inheritdoc />
    public string Summary => "xUnit, NUnit and MSTest tests of the workspace's test projects, found in the syntax; run with dotnet test --filter, read from a TRX file";

    /// <inheritdoc />
    public bool Watches(ReadOnlySpan<char> fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<TestItem> Discover(RunWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        // Every project's folder (a file belongs to the deepest project above it); the test projects among them.
        var projects = new List<(string Folder, string File, bool Tests)>();
        foreach (var file in workspace.Files)
        {
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                int slash = file.LastIndexOf('/');
                bool tests;
                try
                {
                    tests = DotnetRunProvider.ReadProject(Path.Combine(workspace.Root, file)).Tests;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    tests = false;
                }

                projects.Add((slash < 0 ? "" : file[..(slash + 1)], file, tests));
            }
        }

        if (!projects.Exists(p => p.Tests))
        {
            return [];
        }

        projects.Sort(static (a, b) => b.Folder.Length.CompareTo(a.Folder.Length));
        var sources = new List<(string File, string Project)>();
        foreach (var file in workspace.Files)
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (folder, project, tests) in projects)
            {
                if (file.StartsWith(folder, StringComparison.Ordinal))
                {
                    if (tests)
                    {
                        sources.Add((file, project));
                    }

                    break;
                }
            }
        }

        sources.Sort(static (a, b) => string.CompareOrdinal(a.File, b.File));    // the same order every time, whatever the walk's
        var found = new ConcurrentBag<(int Order, List<TestItem> Items)>();
        Parallel.For(0, sources.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            var (file, project) = sources[i];
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(Path.Combine(workspace.Root, file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }

            if (!MayHoldTests(bytes))
            {
                return;                                        // most files hold no test attribute: never decoded, never parsed
            }

            var items = InText(Decode(bytes), project, file, cancellationToken);
            if (items.Count > 0)
            {
                found.Add((i, items));
            }
        });

        return [.. found.OrderBy(f => f.Order).SelectMany(f => f.Items)];
    }

    /// <summary>The tests declared in <paramref name="text"/> (a C# file of <paramref name="project"/>, <paramref name="file"/> relative to the root).</summary>
    public static List<TestItem> InText(string text, string project, string? file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(project);
        var tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview), cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var items = new List<TestItem>();
        foreach (var method in root.DescendantNodes(static n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax).OfType<MethodDeclarationSyntax>())
        {
            if (Framework(method) is not { } framework || method.Parent is not TypeDeclarationSyntax type || Skipped(type))
            {
                continue;
            }

            string className = ClassName(type);
            string name = method.Identifier.ValueText;
            int line = tree.GetLineSpan(method.Identifier.Span, cancellationToken).StartLinePosition.Line;
            items.Add(new TestItem(ProviderName, className + "." + name, project, className, name) { Framework = framework, File = file, Line = line });
        }

        return items;
    }

    /// <inheritdoc />
    public RunCommand Command(TestRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string project = Path.GetFullPath(request.Project, request.Root);
        string folder = Path.GetDirectoryName(project) ?? request.Root;
        if (DotnetRunProvider.Dotnet() is not { } dotnet)
        {
            return RunCommand.Unavailable("The .NET SDK is not installed here (no dotnet on the PATH): install it from dot.net to run tests.", folder);
        }

        string results = Path.GetFullPath(request.ResultsFile);
        var command = new List<string>
        {
            dotnet, "test", project, "-nologo",
            "--logger", "trx;LogFileName=" + Path.GetFileName(results),
            "--logger", "console;verbosity=normal",
            "--results-directory", Path.GetDirectoryName(results) ?? folder,
        };
        if (Filter(request.Tests) is { } filter)
        {
            command.Add("--filter");
            command.Add(filter);
        }

        return new RunCommand(command, folder);
    }

    /// <summary>
    /// The <c>--filter</c> of <paramref name="tests"/>: each fully qualified name (<c>FullyQualifiedName=A.B.C|...</c>), or
    /// past <see cref="MaxNamedTests"/> each class (<c>FullyQualifiedName~A.B.</c>); null for none (every test).
    /// </summary>
    public static string? Filter(IReadOnlyList<TestItem> tests)
    {
        ArgumentNullException.ThrowIfNull(tests);
        if (tests.Count == 0)
        {
            return null;
        }

        var parts = tests.Count <= MaxNamedTests
            ? tests.Select(t => "FullyQualifiedName=" + Escape(t.Id)).Distinct(StringComparer.Ordinal)
            : tests.Select(t => t.Class).Distinct(StringComparer.Ordinal).Select(c => "FullyQualifiedName~" + Escape(c + "."));
        return string.Join('|', parts);
    }

    /// <inheritdoc />
    public IReadOnlyList<TestResult> ReadResults(TestRunRequest request, string output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        if (File.Exists(request.ResultsFile))
        {
            try
            {
                using var stream = new FileStream(request.ResultsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
                return TrxResults.Read(stream, cancellationToken);
            }
            catch (System.Xml.XmlException)
            {
                // A file cut short (the run was stopped): its console lines say what ran.
            }
        }

        return TestConsoleResults.Read(output);
    }

    // The filter's special characters escaped with a backslash (the test platform's rule).
    private static string Escape(string name)
    {
        if (name.AsSpan().IndexOfAny(FilterSpecials) < 0)
        {
            return name;
        }

        var text = new StringBuilder(name.Length + 4);
        foreach (char c in name)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~')
            {
                text.Append('\\');
            }

            text.Append(c);
        }

        return text.ToString();
    }

    // The words of the test attributes in the bytes (UTF-8 or ASCII): a file without one is not parsed.
    private static bool MayHoldTests(ReadOnlySpan<byte> bytes) =>
        bytes.IndexOf("Fact"u8) >= 0 || bytes.IndexOf("Theory"u8) >= 0 || bytes.IndexOf("Test"u8) >= 0;

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        var span = bytes.AsSpan();
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        return Encoding.UTF8.GetString(span);
    }

    // The framework of a test method's attribute, or null when it has none.
    private static string? Framework(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                string name = attribute.Name switch
                {
                    QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
                    SimpleNameSyntax simple => simple.Identifier.ValueText,
                    _ => "",
                };
                if (name.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    name = name[..^"Attribute".Length];
                }

                switch (name)
                {
                    case "Fact" or "Theory" or "SkippableFact" or "SkippableTheory" or "StaFact" or "UIFact":
                        return "xunit";
                    case "Test" or "TestCase" or "TestCaseSource":
                        return "nunit";
                    case "TestMethod" or "DataTestMethod":
                        return "mstest";
                }
            }
        }

        return null;
    }

    // An abstract or generic class's tests run in the classes that derive from it: not listed under it.
    private static bool Skipped(TypeDeclarationSyntax type) =>
        type.Modifiers.Any(SyntaxKind.AbstractKeyword) || type.TypeParameterList is { Parameters.Count: > 0 };

    // "Ns.Outer+Inner": the namespaces (block or file scoped) joined by '.', the containing types by '+'.
    private static string ClassName(TypeDeclarationSyntax type)
    {
        var types = new List<string>();
        var names = new List<string>();
        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case TypeDeclarationSyntax declared:
                    types.Add(declared.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax space:
                    names.Add(space.Name.ToString());
                    break;
            }
        }

        types.Reverse();
        names.Reverse();
        string nested = string.Join('+', types);
        return names.Count == 0 ? nested : string.Join('.', names) + "." + nested;
    }
}
