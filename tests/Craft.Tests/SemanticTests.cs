// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.CSharp.Workspaces;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// C#'s semantic layer on Roslyn: a temporary workspace (a small .csproj and two files), the "csproj" and "loose"
/// readers, diagnostics after a change (waited on through the session's idle hook, never a sleep), completion, signature
/// help, hover, go to definition and formatting.
/// </summary>
[TestGroup("Semantic")]
public static class SemanticTests
{
    internal const string Shapes = """
        namespace Demo;

        /// <summary>A circle of a radius.</summary>
        public sealed class Circle
        {
            /// <summary>The circle's area, <paramref name="scale"/> times.</summary>
            public double Area(double scale) => 3.14 * scale;

            /// <summary>Makes a circle.</summary>
            /// <param name="radius">How big.</param>
            /// <param name="name">What it is called.</param>
            public static Circle Make(int radius, string name) => new();
        }
        """;

    internal const string Program = """
        namespace Demo;

        public static class Program
        {
            public static void Run()
            {
                var circle = Circle.Make(1, "a");
                Console.WriteLine(circle.Area(2));
            }
        }
        """;

    private static readonly LanguageSettings Fast = new() { DiagnosticsDelayMilliseconds = 0 };

    /// <summary>A temporary project: Demo.csproj, Shapes.cs, Program.cs, and a file under obj/ that is not a source.</summary>
    internal sealed class Project : IDisposable
    {
        private readonly TempFolder _folder = new();

        public Project()
        {
            CSharpLanguage.Register();
            File.WriteAllText(_folder.File("Demo.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{ReferenceAssemblies.RunningFramework}</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <DefineConstants Condition="'$(Configuration)' == 'Debug'">$(DefineConstants);DEMO</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Using Include="System.Text" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(ShapesPath, Shapes);
            File.WriteAllText(ProgramPath, Program);
            Directory.CreateDirectory(_folder.File("obj"));
            File.WriteAllText(_folder.File(Path.Combine("obj", "Junk.cs")), "this is not C#");
            Buffer = new PieceTableBuffer(Program.ReplaceLineEndings("\n"));
            Session = new CSharpLanguage().Open(Buffer, new LanguageOpenContext(ProgramPath, Root, Fast));
        }

        public string Root => _folder.Path;

        public string ShapesPath => _folder.File("Shapes.cs");

        public string ProgramPath => _folder.File("Program.cs");

        public PieceTableBuffer Buffer { get; }

        public CSharpLanguageSession Session { get; }

        public string Text => Buffer.Current.GetText(0, Buffer.Current.Length);

        public int At(string what, int offset = 0)
        {
            int at = Text.IndexOf(what, StringComparison.Ordinal);
            Check.True(at >= 0, $"the text holds \"{what}\"");
            return at + offset;
        }

        public void Replace(string what, string with)
        {
            int at = At(what);
            Buffer.Apply([new TextEdit(at, what.Length, with)]);
        }

        public async Task<DiagnosticSet> SettledDiagnostics()
        {
            await Session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
            Check.True(Session.SemanticError is null, $"the diagnostics pass did not throw ({Session.SemanticError})");
            return Session.Diagnostics;
        }

        public void Dispose()
        {
            Session.Dispose();
            _folder.Dispose();
        }
    }

    private static string Describe(DiagnosticSet set) => string.Join("; ", set.Select(d => $"{d.Severity} {d.Id} {d.Message}"));

    [Test]
    public static void TheCsprojReaderReadsTheProject()
    {
        using var project = new Project();
        var model = WorkspaceReaders.Get("csproj").Read(new WorkspaceReadRequest(project.Root, project.ProgramPath, Fast), CancellationToken.None);
        Check.True(model is { Reader: "csproj", Projects.Count: 1 }, "csproj finds the file's project");
        var demo = model!.Projects[0];
        Check.Equal("Demo", demo.Name, "named after its file");
        Check.SequenceEqual([project.ProgramPath, project.ShapesPath], demo.SourceFiles.Order(StringComparer.Ordinal).ToList(), "the default **/*.cs, obj left out");
        Check.True(demo.DefineConstants.Contains("DEBUG") && demo.DefineConstants.Contains("DEMO") && demo.DefineConstants.Contains("NET"), $"the SDK's defines and a conditional one ({string.Join(';', demo.DefineConstants)})");
        Check.True(demo.GlobalUsings.Contains("System") && demo.GlobalUsings.Contains("System.Text"), "the implicit usings and the Using item");
        Check.Equal("enable", demo.Nullable, "the nullable context");
        Check.True(demo.References.Any(r => Path.GetFileName(r).Equals("System.Console.dll", StringComparison.OrdinalIgnoreCase)), "the framework's references");
        Check.True(demo.Notes.Any(n => n.Contains("project.assets.json", StringComparison.Ordinal)), "without a restore it says so");

        var loose = WorkspaceReaders.Get("loose").Read(new WorkspaceReadRequest(null, Path.Combine(project.Root, "elsewhere.cs"), Fast), CancellationToken.None);
        Check.True(loose is { Projects: [{ ProjectFile: null, Name: LooseWorkspaceReader.ProjectName }] }, "loose makes one project of its own");
        Check.Throws<NotRegisteredException>(() => WorkspaceReaders.Get("no-such-reader"), "an unknown reader is not registered");
    }

    [Test]
    public static async Task DiagnosticsAppearForAnErrorAndClearAfterTheFix()
    {
        using var project = new Project();
        var clean = await project.SettledDiagnostics();
        Check.True(clean.Errors == 0 && clean.Warnings == 0, $"the project compiles clean ({Describe(clean)})");
        Check.True(project.Session.Telemetry is { Projects: 1, Documents: >= 3, References: > 10, ReferenceBytes: > 0 }, $"the workspace's size is in the telemetry ({project.Session.Telemetry})");

        int raised = 0;
        project.Session.DiagnosticsChanged += (_, _) => Interlocked.Increment(ref raised);
        project.Replace("circle.Area(2)", "circle.Arae(2)");
        var broken = await project.SettledDiagnostics();
        Check.True(ReferenceEquals(broken.Snapshot, project.Buffer.Current), "the diagnostics are of the current snapshot");
        var error = broken.FirstOrDefault(d => d.Id == "CS1061");
        Check.True(error.Severity == DiagnosticSeverity.Error, $"the misspelt member is an error ({Describe(broken)})");
        Check.Equal("Arae", project.Text.Substring(error.Start, error.Length), "on the misspelt name");
        Check.True(raised >= 1, "DiagnosticsChanged was raised");

        project.Replace("circle.Arae(2)", "circle.Area(2)");
        var fixedSet = await project.SettledDiagnostics();
        Check.Equal(0, fixedSet.Errors, $"fixed, the error is gone ({Describe(fixedSet)})");
    }

    [Test]
    public static async Task CompletionAfterConsoleOffersWriteLineAndApplies()
    {
        using var project = new Project();
        project.Replace("Console.WriteLine(circle.Area(2));", "Console.WriteLine(circle.Area(2));\n        Console.");
        int position = project.At("        Console.\n", "        Console.".Length);
        var list = await project.Session.GetCompletionsAsync(project.Buffer.Current, position, CompletionTrigger.Typed('.'), CancellationToken.None);
        Check.True(list is not null, "'.' after Console gives a list");
        var writeLine = list!.Items.FirstOrDefault(i => i.Label == "WriteLine");
        Check.True(writeLine is not null, $"it offers WriteLine ({list.Items.Count} items)");
        Check.Equal(CompletionItemKind.Method, writeLine!.Kind, "as a method");
        project.Buffer.Apply([writeLine.Edit]);
        Check.True(project.Text.Contains("        Console.WriteLine\n", StringComparison.Ordinal), "applying it writes Console.WriteLine");

        project.Replace("        Console.WriteLine\n", "        circle.Ar\n");
        position = project.At("circle.Ar\n", "circle.Ar".Length);
        var members = await project.Session.GetCompletionsAsync(project.Buffer.Current, position, CompletionTrigger.Invoke, CancellationToken.None);
        Check.True(members is not null && members.Items.Any(i => i.Label == "Area"), "a local of a type from the other file completes its members");
        Check.Equal(position - 2, members!.SpanStart, "the list's span is the typed word");
        var area = members.Items.First(i => i.Label == "Area");
        project.Buffer.Apply([area.Edit]);
        Check.True(project.Text.Contains("        circle.Area\n", StringComparison.Ordinal), "and commits over the typed prefix");
    }

    [Test]
    public static async Task SignatureHelpHoverAndDefinition()
    {
        using var project = new Project();
        var snapshot = project.Buffer.Current;
        var help = await project.Session.GetSignatureHelpAsync(snapshot, project.At("\"a\")"), CancellationToken.None);
        Check.True(help is not null, "inside Make's arguments there is signature help");
        var signature = help!.Signatures[help.ActiveSignature];
        Check.Equal("Circle Circle.Make(int radius, string name)", signature.Label, "the signature");
        Check.Equal(1, help.ActiveParameter, "after the comma, the second argument");
        Check.Equal("name", signature.Parameters[1].Name, "its name");
        Check.Equal("string name", signature.Label.Substring(signature.Parameters[1].Start, signature.Parameters[1].Length), "its place in the label");
        Check.Equal("What it is called.", signature.Parameters[1].Documentation, "its documentation");
        Check.Equal("Makes a circle.", signature.Documentation, "the summary");

        var hover = await project.Session.GetHoverAsync(snapshot, project.At("Area(2)", 1), CancellationToken.None);
        Check.True(hover is not null && hover.Text.StartsWith("(method) double Circle.Area(double scale)", StringComparison.Ordinal), $"hover names the symbol ({hover?.Text})");
        Check.True(hover!.Text.EndsWith("The circle's area, scale times.", StringComparison.Ordinal), $"and its summary ({hover.Text})");
        Check.Equal("Area", project.Text.Substring(hover.Start, hover.Length), "over the identifier");

        var definition = await project.Session.GetDefinitionAsync(snapshot, project.At("Make(1"), CancellationToken.None);
        Check.True(definition is { IsSameDocument: false, IsMetadata: false }, "Make is defined in the other file");
        Check.Equal(project.ShapesPath, definition!.FilePath, "its path");
        Check.Equal("Make", (await File.ReadAllTextAsync(project.ShapesPath)).Substring(definition.Start, definition.Length), "its span is the name");

        var local = await project.Session.GetDefinitionAsync(snapshot, project.At("circle.Area", 2), CancellationToken.None);
        Check.True(local is { IsSameDocument: true }, "a local is in the same document");
        Check.Equal("circle", project.Text.Substring(local!.Start, local.Length), "at its declaration");

        var metadata = await project.Session.GetDefinitionAsync(snapshot, project.At("Console", 2), CancellationToken.None);
        Check.True(metadata is { IsMetadata: true } && metadata.Description.Contains("Console", StringComparison.Ordinal), $"Console is metadata, described ({metadata?.Description})");
    }

    [Test]
    public static async Task FormatDocumentProducesTheExpectedEdits()
    {
        CSharpLanguage.Register();
        var buffer = new PieceTableBuffer("class A\n{\nvoid M( )\n{\nint x=1;\n}\n}\n");
        using var session = new CSharpLanguage().Open(buffer);
        var edits = await session.FormatAsync(buffer.Current, 0, -1, CancellationToken.None);
        Check.True(edits.Count > 0, "a badly laid-out document has edits");
        buffer.Apply([.. edits]);
        Check.Equal("class A\n{\n    void M()\n    {\n        int x = 1;\n    }\n}\n", buffer.Current.GetText(0, buffer.Current.Length), "the formatted text (a loose document, no project)");
        var again = await session.FormatAsync(buffer.Current, 0, -1, CancellationToken.None);
        Check.Equal(0, again.Count, "formatted text has no edits");
        Check.True(session.Diagnostics.Count == 0 && session.DiagnosticsPasses == 0, "without a context nothing is diagnosed on its own");
    }

    [Test]
    public static async Task ReleaseDropsTheWorkspaceAndTheNextRequestRebuildsIt()
    {
        using var project = new Project();
        await project.SettledDiagnostics();
        int generation = project.Session.Telemetry.Generation;
        project.Session.Release();
        var hover = await project.Session.GetHoverAsync(project.Buffer.Current, project.At("Area(2)", 1), CancellationToken.None);
        Check.True(hover is not null, "after a release the next request still answers");
        Check.True(project.Session.Telemetry.Generation > generation, $"on a rebuilt workspace ({project.Session.Telemetry.Generation} after {generation})");
        project.Replace("circle.Area(2)", "circle.Arae(2)");
        var broken = await project.SettledDiagnostics();
        Check.True(broken.Any(d => d.Id == "CS1061"), "and the live text is the one diagnosed");
    }

    [Test]
    public static async Task ALargeDocumentWaitsForTheCommand()
    {
        CSharpLanguage.Register();
        using var temp = new TempFolder();
        var path = temp.File("Big.cs");
        var buffer = new PieceTableBuffer("class Big { void M() { int x = \"no\"; } }");
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(path, temp.Path, Fast with { SemanticMaxCharacters = 10 }));
        await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.IsSemanticDeferred, "above the limit the layer waits");
        buffer.Apply([TextEdit.Insert(0, " ")]);
        await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.Diagnostics.Count == 0, "a change computes nothing on its own");
        Check.True(await session.GetCompletionsAsync(buffer.Current, 1, CompletionTrigger.Typed('c'), CancellationToken.None) is null, "nor does typing ask for completion");
        session.EnableSemantics();
        await session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(session.Diagnostics.Any(d => d.Id == "CS0029"), $"the command computes the diagnostics (a loose file) ({string.Join("; ", session.Diagnostics.Select(d => d.Id))}; {session.SemanticError})");
    }
}
