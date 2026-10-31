// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.CSharp.Workspaces;
using Craft.Tests.Runner;
using Microsoft.CodeAnalysis;
using RoslynClassifier = Microsoft.CodeAnalysis.Classification.Classifier;

namespace Craft.Tests;

/// <summary>
/// E5's remainder: the projects' source generators run in the workspace (no false CS8795 on Craft's own solution), the
/// semantic colours equal Roslyn's own classification and follow edits, and the memory-pressure call frees the workspace.
/// </summary>
[TestGroup("Semantic")]
public static class SemanticGeneratorTests
{
    private const string Generated = """
        using System.Runtime.InteropServices;
        using System.Text.RegularExpressions;

        namespace Demo;

        public static partial class Native
        {
            [LibraryImport("libc")]
            internal static partial int getpid();

            [GeneratedRegex("[0-9]+")]
            internal static partial Regex Digits();

            public static bool Check(string text) => Digits().IsMatch(text) && getpid() > 0;
        }
        """;

    private static async Task<DiagnosticSet> DiagnosticsOf(LanguageSettings settings)
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File("Gen.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{ReferenceAssemblies.RunningFramework}</TargetFramework>
                <Nullable>enable</Nullable>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        var path = folder.File("Native.cs");
        await File.WriteAllTextAsync(path, Generated);
        CSharpLanguage.Register();
        var buffer = new PieceTableBuffer(Generated.ReplaceLineEndings("\n"));
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(path, folder.Path, settings));
        await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.SemanticError is null, $"the diagnostics pass did not throw ({session.SemanticError})");
        return session.Diagnostics;
    }

    private static string Describe(IEnumerable<EditorDiagnostic> set) => string.Join("; ", set.Select(d => $"{d.Severity} {d.Id} {d.Message}"));

    [Test]
    public static async Task SourceGeneratorsCompleteLibraryImportAndGeneratedRegex()
    {
        var framework = ReferenceAssemblies.For(null);
        Check.True(framework.Analyzers.Any(a => Path.GetFileName(a) == "System.Text.RegularExpressions.Generator.dll")
            && framework.Analyzers.Any(a => Path.GetFileName(a) == "Microsoft.Interop.LibraryImportGenerator.dll"),
            $"the targeting pack's FrameworkList.xml gives its generators ({string.Join(", ", framework.Analyzers.Select(Path.GetFileName))}; notes: {string.Join("; ", framework.Notes)})");

        var on = await DiagnosticsOf(new LanguageSettings { DiagnosticsDelayMilliseconds = 0 });
        Check.True(on.Errors == 0, $"with the generators the partial methods are implemented: no error ({Describe(on)})");

        var off = await DiagnosticsOf(new LanguageSettings { DiagnosticsDelayMilliseconds = 0, SourceGenerators = false });
        Check.True(off.Count(d => d.Id == "CS8795") == 2, $"without them (CRAFT_SOURCE_GENERATORS=false) both partials are CS8795, which proves the generators made the difference ({Describe(off)})");
    }

    [Test]
    public static async Task CraftsOwnSolutionCompilesWithoutFalseErrors()
    {
        // src/Craft.Editor's workspace holds Idrak.Abstraction (LibraryImport and GeneratedRegex partials), Craft.Abstractions,
        // Craft.Core and Craft.Editor, read from their restores: the reader's model must compile as the build does.
        var root = TestRunner.RepositoryPath("src", "Craft.Editor");
        Check.True(File.Exists(Path.Combine(root, "obj", "project.assets.json")), "src/Craft.Editor is restored (the test build restores it)");
        var model = WorkspaceReaders.Get("csproj").Read(new WorkspaceReadRequest(root, null, LanguageSettings.Default), CancellationToken.None);
        var idrak = model?.Projects.FirstOrDefault(p => p.Name == "Idrak.Abstraction");
        Check.True(idrak is not null && idrak.Analyzers.Any(a => Path.GetFileName(a) == "Microsoft.Interop.LibraryImportGenerator.dll"),
            "Idrak.Abstraction is in the model with the targeting pack's generators");

        var workspace = CSharpWorkspace.Enter(root, LanguageSettings.Default);
        try
        {
            var solution = await workspace.BuildAsync(null, CancellationToken.None);
            var errors = new List<string>();
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                foreach (var diagnostic in compilation!.GetDiagnostics())
                {
                    if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && errors.Count < 10)
                    {
                        errors.Add($"{project.Name}: {diagnostic.Id} {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)} at {diagnostic.Location.GetLineSpan()}");
                    }
                }
            }

            Check.True(solution.Projects.Count() >= 4, $"the four projects ({string.Join(", ", solution.Projects.Select(p => p.Name))})");
            Check.True(errors.Count == 0, $"Craft's own projects compile with 0 errors in the reader's model ({string.Join(" | ", errors)})");
        }
        finally
        {
            await workspace.LeaveAsync(workspace);
        }
    }

    [Test]
    public static async Task SemanticColorsAreRoslynsOwnClassification()
    {
        using var project = new SemanticTests.Project(new LanguageSettings { DiagnosticsDelayMilliseconds = 0, SemanticColorsDelayMilliseconds = 0 });
        var session = project.Session;
        int reclassified = 0;
        session.Reclassified += (_, _) => Interlocked.Increment(ref reclassified);
        var snapshot = project.Buffer.Current;
        await session.WhenIdle();
        using var spans = new TokenSpanBuffer();
        session.Classify(snapshot, 0, snapshot.Length, spans);
        await session.WhenColorsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.SemanticError is null, $"the colour pass did not throw ({session.SemanticError})");
        Check.True(session.ColorsPasses >= 1 && reclassified >= 1, $"a colour pass landed and raised Reclassified ({session.ColorsPasses} passes, {reclassified} raised)");

        spans.Clear();
        session.Classify(snapshot, 0, snapshot.Length, spans);
        var document = await session.CurrentDocumentAsync(CancellationToken.None);
        var roslyn = CSharpLanguageSession.Convert(await RoslynClassifier.GetClassifiedSpansAsync(document!, new Microsoft.CodeAnalysis.Text.TextSpan(0, snapshot.Length)));
        var ours = spans.Spans.ToArray();
        Check.SequenceEqual(roslyn, ours, "the session's spans of the whole file are Roslyn's semantic classification");

        string text = project.Text;
        string ClassAt(string what, int offset = 0)
        {
            int at = text.IndexOf(what, StringComparison.Ordinal) + offset;
            return ours.FirstOrDefault(s => s.Start == at).Class ?? "(none)";
        }

        Check.Equal(TokenClasses.ClassName, ClassAt("Circle.Make"), "a type of the other file is a class name");
        Check.Equal(TokenClasses.WithStaticSymbol(TokenClasses.MethodName), ClassAt("Make(1", 0), "a static method is a method name with static symbol");
        Check.Equal(TokenClasses.LocalName, ClassAt("circle.Area"), "a local's use is a local name");
        Check.Equal(TokenClasses.MethodName, ClassAt("Area(2"), "an instance method");
    }

    [Test]
    public static async Task SemanticColorsFollowAnEditAndLandAgain()
    {
        using var project = new SemanticTests.Project(new LanguageSettings { DiagnosticsDelayMilliseconds = 0, SemanticColorsDelayMilliseconds = 0 });
        var session = project.Session;
        await session.WhenIdle();
        using var spans = new TokenSpanBuffer();
        session.Classify(project.Buffer.Current, 0, project.Buffer.Current.Length, spans);
        await session.WhenColorsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        int passes = session.ColorsPasses;

        // An edit above the call: the landed colours shift with it before the next pass lands.
        project.Replace("var circle", "var  circle");
        await session.WhenIdle();
        var after = project.Buffer.Current;
        spans.Clear();
        session.Classify(after, 0, after.Length, spans);
        int make = project.At("Make(1");
        Check.True(spans.Spans.ToArray().Any(s => s.Start == make && s.Class == TokenClasses.WithStaticSymbol(TokenClasses.MethodName)),
            "the colours after the edit are shifted with it (Make keeps its semantic class at its new place)");
        await session.WhenColorsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.ColorsPasses > passes, "the edit asked for a new pass, which landed");
        Check.True(session.ColorsRange is { } range && range.Version == after.Version, "the new colours are of the edited text");
    }

    [Test]
    public static void MergeKeepsRoslynsSpansAndFillsTheChangedText()
    {
        // Roslyn's spans of "aaa bbb ccc" (old positions); the edit replaced "bbb" by "bbbbb" (+2).
        var colors = new CSharpLanguageSession.Colors(1, 0, 11,
            [new TokenSpan(0, 3, TokenClasses.ClassName), new TokenSpan(4, 3, TokenClasses.LocalName), new TokenSpan(8, 3, TokenClasses.FieldName)]);
        var change = new Microsoft.CodeAnalysis.Text.TextChangeRange(new Microsoft.CodeAnalysis.Text.TextSpan(4, 3), 5);
        TokenSpan[] syntactic = [new(0, 3, TokenClasses.Identifier), new(4, 5, TokenClasses.Identifier), new(10, 3, TokenClasses.Identifier), new(14, 2, TokenClasses.Keyword)];
        using var into = new TokenSpanBuffer();
        CSharpLanguageSession.Merge(colors, change, syntactic, 0, 16, into);
        Check.SequenceEqual(
            [new TokenSpan(0, 3, TokenClasses.ClassName), new TokenSpan(4, 5, TokenClasses.Identifier), new TokenSpan(10, 3, TokenClasses.FieldName), new TokenSpan(14, 2, TokenClasses.Keyword)],
            into.Spans.ToArray(), "Roslyn's spans before and after the edit (shifted), the syntactic one in it, the syntactic ones past the classified range");

        into.Clear();
        CSharpLanguageSession.Merge(colors, null, [new(0, 3, TokenClasses.Identifier), new(4, 3, TokenClasses.Identifier), new(8, 3, TokenClasses.Identifier)], 0, 11, into);
        Check.SequenceEqual(colors.Spans, into.Spans.ToArray(), "fresh colours are Roslyn's spans exactly");
    }

    [Test]
    public static async Task ReleaseMemoryDropsTheWorkspaceAndItIsBuiltAgain()
    {
        using var project = new SemanticTests.Project(new LanguageSettings { DiagnosticsDelayMilliseconds = 0 });
        await project.SettledDiagnostics();
        int generation = project.Session.Telemetry.Generation;
        new CSharpLanguage().ReleaseMemory();
        project.Replace("circle.Area(2)", "circle.Arae(2)");
        var broken = await project.SettledDiagnostics();
        Check.True(project.Session.Telemetry.Generation > generation, $"the next request built the workspace again (generation {generation} to {project.Session.Telemetry.Generation})");
        Check.True(broken.Any(d => d.Id == "CS1061"), "and it answers as before (the misspelt member)");
    }
}
