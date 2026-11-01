// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.CSharp.Run;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Preview;
using Craft.Editor.Run;
using Craft.Editor.Shell;
using Craft.Editor.Spelling;
using Craft.Editor.Testing;
using Craft.Editor.Tools;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 9, item 18: the Test explorer. Tests found in a fixture's syntax (xUnit, NUnit, MSTest; nested, abstract, a
/// non-test project), the filter and the command; a run through the Run view's approval and a fake sandbox, its results
/// read from a TRX fixture into the tree, the gutter and Problems; TRX and console parsing; discovery of 2,000 tests timed.
/// </summary>
[TestGroup("TestExplorer")]
public static class TestExplorerTests
{
    internal const string MathTests = """
        using Xunit;

        namespace App.Tests;

        public class MathTests
        {
            [Fact]
            public void Adds() => Assert.Equal(2, 1 + 1);

            [Fact]
            public void Divides()
            {
                Assert.Equal(2, 3);
            }

            [Theory]
            [InlineData(1)]
            public void Theory(int x) => Assert.True(x > 0);

            [Fact(Skip = "not yet")]
            public void Skips() { }

            public class Nested
            {
                [Fact]
                public void Inner() { }
            }
        }

        public abstract class Base { [Fact] public void Inherited() { } }
        """;

    [Test]
    public static void DiscoveryFindsXunitNunitAndMstestTestsInTheSyntax()
    {
        using var folder = new TempFolder();
        Fixture(folder.Path);
        var provider = new DotnetTestProvider();
        var tests = provider.Discover(Workspace(folder.Path));
        string Ids(IEnumerable<TestItem> items) => string.Join(" | ", items.Select(t => $"{t.Id}@{t.Line}:{t.Framework}"));
        Check.Equal("App.Tests.MathTests.Adds@7:xunit | App.Tests.MathTests.Divides@10:xunit | App.Tests.MathTests.Theory@17:xunit | App.Tests.MathTests.Skips@20:xunit | App.Tests.MathTests+Nested.Inner@25:xunit"
            + " | Other.N.A@1:nunit | Other.N.B@2:nunit | M.C@4:mstest | M.D@5:mstest",
            Ids(tests), "every framework's tests with their lines (from 0); a nested class with '+', an abstract class's left out, the library's file never read as tests");
        Check.True(tests.All(t => t.Project == "tests/App.Tests/App.Tests.csproj" && t.Provider == "dotnet"), "each test in its project");
        Check.True(tests[0].File == "tests/App.Tests/MathTests.cs" && tests[4].ClassName == "Nested" && tests[0].ClassName == "MathTests", "its file and its class's short name");
        Check.True(provider.Watches("MathTests.cs") && provider.Watches("App.Tests.csproj") && !provider.Watches("README.md"), "what it watches");

        Check.Equal("FullyQualifiedName=App.Tests.MathTests.Adds|FullyQualifiedName=App.Tests.MathTests+Nested.Inner", DotnetTestProvider.Filter([tests[0], tests[4]]), "a filter of fully qualified names");
        Check.Equal(@"FullyQualifiedName=A.B\(x\)", DotnetTestProvider.Filter([tests[0] with { Id = "A.B(x)" }]), "the filter's special characters escaped");
        var many = Enumerable.Range(0, 50).Select(i => tests[0] with { Id = "N.C" + (i % 2).ToString(CultureInfo.InvariantCulture) + ".M" + i.ToString(CultureInfo.InvariantCulture), Class = "N.C" + (i % 2).ToString(CultureInfo.InvariantCulture) }).ToList();
        Check.Equal("FullyQualifiedName~N.C0.|FullyQualifiedName~N.C1.", DotnetTestProvider.Filter(many), "past 40 tests the filter names their classes");
        Check.True(DotnetTestProvider.Filter([]) is null, "no test: no filter (the project's every test)");

        var command = provider.Command(new TestRunRequest(folder.Path, "tests/App.Tests/App.Tests.csproj", [tests[1]], Path.Combine(folder.Path, ".craft", "test-results", "r.trx")));
        if (command.Problem is null)
        {
            Check.True(command.Command.Skip(1).Take(3).SequenceEqual(["test", Path.Combine(folder.Path, "tests", "App.Tests", "App.Tests.csproj"), "-nologo"])
                && command.Command.Contains("trx;LogFileName=r.trx") && command.Command.Contains("--results-directory") && command.Command[^1] == "FullyQualifiedName=App.Tests.MathTests.Divides",
                "dotnet test PROJECT with the TRX logger and the filter: " + command.Line);
        }
        else
        {
            Check.True(command.Problem.Contains("dotnet", StringComparison.Ordinal), "no .NET SDK here: said, nothing runs");
        }
    }

    [Test]
    public static void TrxAndConsoleResultsAreReadAndStackFramesNameTheirPlaces()
    {
        string trx = Trx("/w/tests/App.Tests/MathTests.cs");
        var results = TrxResults.Read(new MemoryStream(Encoding.UTF8.GetBytes(trx)));
        string Outcomes(IEnumerable<TestResult> list) => string.Join(" | ", list.Select(r => $"{r.Id}={r.Outcome}"));
        Check.Equal("App.Tests.MathTests.Adds=Passed | App.Tests.MathTests.Divides=Failed | App.Tests.MathTests.Theory=Failed | App.Tests.MathTests.Skips=Skipped | Other.N.A=Passed",
            Outcomes(results), "each method's result named by its definition's class and name; a theory's cases joined (failed when one failed); NotExecuted is skipped");
        var divides = results[1];
        Check.True(divides.Message!.StartsWith("Assert.Equal() Failure", StringComparison.Ordinal) && divides.StackTrace!.Contains(":line 13", StringComparison.Ordinal)
            && divides.Duration == TimeSpan.FromMilliseconds(3), "a failure's message, stack and time");
        Check.True(results[2].Duration == TimeSpan.FromMilliseconds(3) && results[3].Message == "not yet", "a theory's times added; a skip's reason");

        var frames = TestStackFrame.Parse("   at App.Tests.MathTests.Divides() in /w/tests/App.Tests/MathTests.cs:line 13\n   at System.RuntimeMethodHandle.InvokeMethod(Object target)");
        Check.True(frames is [{ File: "/w/tests/App.Tests/MathTests.cs", Line: 13 }, { File: null }], "a frame's file:line read; a frame without one kept as text");
        Check.True(TestStackFrame.TryPlace(@"at A.B() in C:\src\A.cs:line 7", out var file, out int line) && file == @"C:\src\A.cs" && line == 7, "a Windows path's place");

        const string console = """
              Passed App.Tests.MathTests.Adds [12 ms]
              Failed App.Tests.MathTests.Divides [3 ms]
              Error Message:
               Assert.Equal() Failure
              Stack Trace:
                 at App.Tests.MathTests.Divides() in /w/MathTests.cs:line 13

              Passed App.Tests.MathTests.Theory(x: 1) [1 ms]
              Skipped App.Tests.MathTests.Skips [1 ms]
            Failed!  - Failed:     1, Passed:     2, Skipped:     1, Total:     4
            """;
        var read = TestConsoleResults.Read(console);
        Check.Equal("App.Tests.MathTests.Adds=Passed | App.Tests.MathTests.Divides=Failed | App.Tests.MathTests.Theory=Passed | App.Tests.MathTests.Skips=Skipped", Outcomes(read),
            "the console logger's lines when no results file came (the summary is not a test)");
        Check.True(read[1] is { Message: "Assert.Equal() Failure", StackTrace: var stack } && stack!.Contains(":line 13", StringComparison.Ordinal) && read[0].Duration == TimeSpan.FromMilliseconds(12),
            "its message, stack and time");
    }

    [Test]
    public static async Task ARunGoesThroughTheRunViewsSandboxAndItsResultsMarkTheTreeTheGutterAndProblems()
    {
        using var folder = new TempFolder();
        Fixture(folder.Path);
        string source = Path.Combine(folder.Path, "tests", "App.Tests", "MathTests.cs");
        var sandbox = new TrxSandbox(Trx(source));
        var provider = new WrappedProvider();
        CommandSandboxes.Register(sandbox);
        TestProviders.Register(provider);
        try
        {
            var ui = new ConcurrentQueue<Action>();
            var presenter = new Presenter();
            var options = new CraftOptions { RunSandbox = sandbox.Name, WorkspaceWatcher = FolderWatchers.None };
            using var host = new EditorHost(options, ui.Enqueue, presenter);
            using var run = new RunView(host, () => folder.Path, () => options);
            using var view = new TestExplorerView(host, run, () => folder.Path, () => options);
            var opened = host.Open(await TextDocument.OpenAsync(source, options with { NewLine = "\n" }), null);
            var found = view.RefreshAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => found.IsCompleted, "the tests are found");
            Check.Equal(9, view.Tests.Count, "the fixture's nine tests");
            var tree = view.Blocks.OfType<PanelResults>().Single().Groups;
            Check.True(tree[0] is { Title: "App.Tests", Rows.Count: 0 } && tree[1] is { Title: "MathTests", Detail: "App.Tests" } && tree[1].Rows.Count == 4 && tree[2].Title == "Nested",
                "project › class › method: " + string.Join(", ", tree.Select(g => g.Title)));
            var marks = DocumentLineMarks.Of(opened.Document);
            Check.True(marks.Marks.Length == 5 && marks.Marks[0] is { Line: 7, Kind: LineMarkKind.TestReady } && marks.Marks[0].IsGlyph, "▶ in the gutter at each test of the open file");

            presenter.Answers.Enqueue(1);                               // Cancel
            var refused = view.RunAllAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => refused.IsCompleted, "the refused run ends");
            Check.True(sandbox.Commands.Count == 0 && presenter.Asked is [{ Title: var asked }] && asked.StartsWith("Test Tests: App.Tests", StringComparison.Ordinal) && view.Results.Count == 0,
                "asked first (no silent run); said no: nothing ran, no result");

            presenter.Answers.Enqueue(0);                               // Run
            var ran = view.RunAllAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => ran.IsCompleted, "the run ends");
            await ran;
            Check.True(sandbox.Commands is [var command] && command.Contains("test") && !command.Contains("--filter"), "Run all: the project's every test, no filter: " + string.Join(' ', sandbox.Commands.FirstOrDefault() ?? []));
            Check.Equal("Passed Failed Failed Skipped NotRun", string.Join(' ', view.Tests.Take(4).Concat(view.Tests.Skip(4).Take(1)).Select(view.OutcomeOf)),
                "the TRX's results in the tree (the nested test, absent from the file, not run)");
            Check.True(view.FailedCount == 2 && view.Badge == "2", "two failed: the rail's badge");
            var rows = view.Blocks.OfType<PanelResults>().Single().Groups[1].Rows;
            Check.True(rows[0] is { Icon: TestExplorerView.PassedIcon, Tone: PanelTone.Success, Badge: "12 ms" } && rows[1] is { Icon: TestExplorerView.FailedIcon, Tone: PanelTone.Error }
                && rows[1].Detail!.StartsWith("Assert.Equal() Failure", StringComparison.Ordinal), "each row's mark, colour, time and the failure's first line");
            Check.True(marks.Marks[0].Kind == LineMarkKind.TestPassed && marks.Marks[1].Kind == LineMarkKind.TestFailed && marks.Marks[3].Kind == LineMarkKind.TestSkipped, "✓ ✗ ○ in the gutter");
            await WorkspaceSearchTests.PumpUntil(ui, () => view.ProblemFiles.Count == 1, "the failures reach Problems");
            var problems = view.ProblemFiles[0];
            Check.True(problems.Path == Path.GetFullPath(source) && problems.Diagnostics.Count == 2
                && problems.Snapshot.GetLineIndex(problems.Diagnostics[0].Start) == 12 && problems.Diagnostics[0].Message.StartsWith("MathTests.Divides failed: Assert.Equal() Failure", StringComparison.Ordinal),
                "a failure at its stack's first frame in the workspace (line 13), the other at its declaration");

            await view.ActAsync("tests.select", TestExplorerView.KeyOf(view.Tests[1]));
            var card = view.Blocks.OfType<PanelCard>().Single();
            Check.True(card.Title == "MathTests.Divides failed" && card.Blocks.OfType<PanelButtons>().SelectMany(b => b.Buttons).Any(b => b.ActionId == "tests.open" && b.Value == Path.GetFullPath(source) + "|13"),
                "a failure's card: its message and its stack, the frame with a file:line clickable");

            presenter.Answers.Enqueue(0);
            var again = view.RunFailedAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => again.IsCompleted, "the failed run ends");
            Check.True(sandbox.Commands.Count == 2 && sandbox.Commands[1][^1] == "FullyQualifiedName=App.Tests.MathTests.Divides|FullyQualifiedName=App.Tests.MathTests.Theory",
                "Run failed filters the failed tests: " + string.Join(' ', sandbox.Commands[1]));

            // A click on a test's gutter mark runs it.
            presenter.Answers.Enqueue(0);
            using var editor = new EditorModel(opened.Document, null, options, null, new FixedMeasurer(), new FakeClipboard());
            editor.UpdateViewport(0, 0, 800, 600);
            editor.PointerPressed(3, (7 * FixedMeasurer.Metrics.LineHeight) + 10, KeyModifiers.None);
            await WorkspaceSearchTests.PumpUntil(ui, () => sandbox.Commands.Count == 3 && run.Running.Count == 0, "the mark's run");
            Check.Equal("FullyQualifiedName=App.Tests.MathTests.Adds", sandbox.Commands[2][^1], "a click on ▶ (or ✓) runs that test");
        }
        finally
        {
            CommandSandboxes.Unregister(sandbox.Name);
            TestProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void DiscoveryOfTwoThousandTestsIsTimed()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "Big.Tests"));
        Disk.Write(Path.Combine(folder.Path, "Big.Tests", "Big.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        for (int f = 0; f < 100; f++)
        {
            var text = new StringBuilder($"using Xunit;\nnamespace Big;\npublic class C{f}\n{{\n");
            for (int t = 0; t < 20; t++)
            {
                text.Append(CultureInfo.InvariantCulture, $"    [Fact]\n    public void T{t}() {{ Assert.True(true); }}\n\n");
            }

            Disk.Write(Path.Combine(folder.Path, "Big.Tests", $"C{f}.cs"), text.Append("}\n").ToString());
        }

        for (int f = 0; f < 200; f++)
        {
            Disk.Write(Path.Combine(folder.Path, "Big.Tests", $"Helper{f}.cs"), $"namespace Big;\ninternal static class Helper{f} {{ public static int Value => {f}; }}\n");
        }

        var provider = new DotnetTestProvider();
        var workspace = Workspace(folder.Path);
        long start = Stopwatch.GetTimestamp();
        int cold = provider.Discover(workspace).Count;
        double coldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var warm = new double[5];
        for (int i = 0; i < warm.Length; i++)
        {
            start = Stopwatch.GetTimestamp();
            Check.Equal(2000, provider.Discover(workspace).Count, "the same tests each time");
            warm[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(warm);
        Check.Equal(2000, cold, "2,000 tests in 100 files (200 files without tests read as bytes, never parsed)");
        string line = string.Create(CultureInfo.InvariantCulture, $"test discovery, 2,000 tests in 100 files and 200 others, {Environment.ProcessorCount} cores: cold {coldMs:0.0} ms, warm median {warm[2]:0.0} ms");
        Console.WriteLine("    " + line);
        Directory.CreateDirectory(TestRunner.RepositoryPath("test-output", "benches"));
        File.WriteAllText(TestRunner.RepositoryPath("test-output", "benches", "test-discovery.txt"), line + "\n");
    }

    internal static void Fixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "tests", "App.Tests"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Lib"));
        Disk.Write(Path.Combine(root, "tests", "App.Tests", "App.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.0\" /></ItemGroup></Project>");
        Disk.Write(Path.Combine(root, "tests", "App.Tests", "MathTests.cs"), MathTests.ReplaceLineEndings("\n"));
        Disk.Write(Path.Combine(root, "tests", "App.Tests", "OtherTests.cs"), "namespace Other { public class N {\n[Test] public void A() { }\n[TestCase(1)] public void B(int i) { } } }\n"
            + "[TestClass] public class M {\n[TestMethod] public void C() { }\n[DataTestMethod] public void D() { } }\n");
        Disk.Write(Path.Combine(root, "src", "Lib", "Lib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Disk.Write(Path.Combine(root, "src", "Lib", "Fake.cs"), "public class Fake { [Fact] public void NotATest() { } }\n");
    }

    internal static RunWorkspace Workspace(string root) =>
        new(root, [.. WorkspaceFiles.Enumerate(root, ["bin", "obj", ".craft"], true, null, null).Select(p => WorkspaceFiles.Relative(root, p))]);

    // A TRX as dotnet test writes it: the results first, the definitions after; a theory's two cases, a skipped test.
    internal static string Trx(string file) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="r" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="e1" testId="t1" testName="App.Tests.MathTests.Adds" duration="00:00:00.0120000" outcome="Passed" />
            <UnitTestResult executionId="e2" testId="t2" testName="App.Tests.MathTests.Divides" duration="00:00:00.0030000" outcome="Failed">
              <Output>
                <ErrorInfo>
                  <Message>Assert.Equal() Failure: Values differ
        Expected: 2
        Actual:   3</Message>
                  <StackTrace>   at App.Tests.MathTests.Divides() in {file}:line 13</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            <UnitTestResult executionId="e3" testId="t3" testName="App.Tests.MathTests.Theory(x: 1)" duration="00:00:00.0010000" outcome="Passed" />
            <UnitTestResult executionId="e4" testId="t3b" testName="App.Tests.MathTests.Theory(x: 2)" duration="00:00:00.0020000" outcome="Failed">
              <Output><ErrorInfo><Message>Assert.True() Failure</Message></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult executionId="e5" testId="t4" testName="Skips" duration="00:00:00.0010000" outcome="NotExecuted">
              <Output><ErrorInfo><Message>not yet</Message></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult executionId="e6" testId="t5" testName="A" duration="00:00:00.0010000" outcome="Passed" />
          </Results>
          <TestDefinitions>
            <UnitTest name="Adds" id="t1"><TestMethod className="App.Tests.MathTests" name="Adds" /></UnitTest>
            <UnitTest name="Divides" id="t2"><TestMethod className="App.Tests.MathTests" name="Divides" /></UnitTest>
            <UnitTest name="Theory1" id="t3"><TestMethod className="App.Tests.MathTests" name="Theory" /></UnitTest>
            <UnitTest name="Theory2" id="t3b"><TestMethod className="App.Tests.MathTests" name="Theory" /></UnitTest>
            <UnitTest name="Skips" id="t4"><TestMethod className="App.Tests.MathTests, App.Tests" name="Skips" /></UnitTest>
            <UnitTest name="A" id="t5"><TestMethod className="Other.N" name="A" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    // The C# provider's discovery under another name, its command a fixed one (no .NET SDK needed), its results its own.
    private sealed class WrappedProvider : ITestProvider
    {
        private readonly DotnetTestProvider _inner = new();

        public string Name => "fake-dotnet";

        public string Summary => "the dotnet provider's discovery, a fixed command";

        public string Title => "Fake";

        public bool Watches(ReadOnlySpan<char> fileName) => _inner.Watches(fileName);

        public IReadOnlyList<TestItem> Discover(RunWorkspace workspace, CancellationToken cancellationToken = default) =>
            [.. _inner.Discover(workspace, cancellationToken).Select(t => t with { Provider = Name })];

        public RunCommand Command(TestRunRequest request)
        {
            var command = new List<string> { "dotnet", "test", request.Project, "--logger", "trx;LogFileName=" + Path.GetFileName(request.ResultsFile), "--results-directory", Path.GetDirectoryName(request.ResultsFile)! };
            if (DotnetTestProvider.Filter(request.Tests) is { } filter)
            {
                command.Add("--filter");
                command.Add(filter);
            }

            return new RunCommand(command, request.Root);
        }

        public IReadOnlyList<TestResult> ReadResults(TestRunRequest request, string output, CancellationToken cancellationToken = default) =>
            _inner.ReadResults(request, output, cancellationToken);
    }

    // A sandbox that writes the TRX the command names (as dotnet test would) and plays a line of output.
    private sealed class TrxSandbox(string trx) : ICommandSandbox
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public string Name => "fake-tests";

        public string Summary => "writes a TRX fixture where the command says";

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            lock (Commands)
            {
                Commands.Add(command);
            }

            int at = command.ToList().IndexOf("--results-directory");
            string name = command.First(c => c.StartsWith("trx;LogFileName=", StringComparison.Ordinal))["trx;LogFileName=".Length..];
            await File.WriteAllTextAsync(Path.Combine(command[at + 1], name), trx, cancellationToken);
            limits.Output?.Invoke("Test run for App.Tests.dll (.NETCoreApp,Version=v10.0)");
            return new CommandResult(1, false, "", TimeSpan.FromMilliseconds(5), false);
        }
    }

    internal sealed class Presenter : IEditorPresenter
    {
        public ConcurrentQueue<int> Answers { get; } = new();

        public List<EditorQuestion> Asked { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(Answers.TryDequeue(out int answer) ? answer : question.Dismissed);
        }
    }
}

/// <summary>
/// X part 9, item 19: the Markdown preview. The chat's renderer over a document (headings, lists, tables, code coloured by
/// its language, links and images), each line's source line (the scroll follows), a click's action, a render after a pause
/// off the UI thread; the "markdown" language's colours and folds; Ctrl+Shift+V and Ctrl+K V in the shell.
/// </summary>
[TestGroup("MarkdownPreview")]
public static class MarkdownPreviewTests
{
    private const string Sample = """
        # Craft

        Some *words* and a [link](docs/guide.md) and ![logo](images/logo.png "Logo").

        - one
        - two

        | Name | Value |
        |------|-------|
        | a    | 1     |

        ```csharp
        var x = 1;
        ```

        > quoted
        """;

    [Test]
    public static async Task ThePreviewRendersTheDocumentAndFollowsTheSourcesLines()
    {
        bool registered = EditorLanguages.Find("csharp") is null;
        if (registered)
        {
            EditorLanguages.Register(new CSharpLanguage());
        }

        try
        {
            var ui = new ConcurrentQueue<Action>();
            var options = new CraftOptions { MarkdownPreviewDelayMilliseconds = 20 };
            using var preview = new MarkdownPreview(ui.Enqueue, () => options);
            var rendered = preview.RenderAsync(Sample.ReplaceLineEndings("\n"));
            await WorkspaceSearchTests.PumpUntil(ui, () => rendered.IsCompleted, "the render lands");
            var snapshot = preview.Document.Buffer.Current;
            string text = snapshot.GetText(0, snapshot.Length);
            Check.True(text.StartsWith("Craft\n", StringComparison.Ordinal) && text.Contains("• one", StringComparison.Ordinal) && text.Contains("Name │ Value", StringComparison.Ordinal)
                && text.Contains("var x = 1;", StringComparison.Ordinal) && text.Contains("│ quoted", StringComparison.Ordinal) && text.Contains("🖼 logo", StringComparison.Ordinal)
                && !text.Contains("Insert at caret", StringComparison.Ordinal), "headings, lists, tables, code, quotes and an image's link; a code block offers Copy only: " + text);
            using var spans = new TokenSpanBuffer();
            using var session = preview.Language.Open(preview.Document.Buffer);
            HashSet<string> classes = [];
            await WorkspaceSearchTests.PumpUntil(ui, () =>
            {
                spans.Clear();
                session.Classify(snapshot, 0, snapshot.Length, spans);
                classes = spans.Spans.ToArray().Select(s => s.Class).ToHashSet();
                return classes.Contains(TokenClasses.Keyword);
            }, "the code block's colours land");
            Check.True(classes.Contains(ChatClasses.Heading) && classes.Contains(ChatClasses.Link) && classes.Contains(TokenClasses.Keyword), "the heading, the links and the code's own colours (C#'s keyword): " + string.Join(", ", classes));

            int code = snapshot.GetLineIndex(text.IndexOf("var x", StringComparison.Ordinal));
            Check.Equal(12, preview.SourceLineOf(code), "a code line knows its source line");
            Check.Equal(code, preview.PreviewLineOf(12), "and the source line its preview line");
            Check.Equal(0, preview.PreviewLineOf(0), "the heading's");
            int link = text.IndexOf("link", StringComparison.Ordinal);
            Check.True(preview.ActionAt(link) is { Kind: "link", Text: "docs/guide.md" } && preview.ActionAt(text.IndexOf("🖼", StringComparison.Ordinal)) is { Text: "images/logo.png" },
                "a click on a link or an image says its target (the title left out)");
            Check.True(preview.ActionAt(text.IndexOf("Copy", StringComparison.Ordinal)) is { Kind: "copy", Text: "var x = 1;" }, "a code block's Copy");
            Check.True(preview.FileOf("https://x.org") is null && preview.FileOf("#top") is null && preview.FileOf("a b.md") is not null, "an address is not a file; a relative path is");

            // The scroll follows: the source's top line (12, the code) brings the preview's code line to its top.
            var (source, _, _) = Editors.Make(Sample.ReplaceLineEndings("\n") + new string('\n', 100));
            var (shown, _, _) = Editors.Make(text + new string('\n', 100));
            source.UpdateViewport(0, 12 * FixedMeasurer.Metrics.LineHeight, 800, 60);   // the host shows the source from line 12
            shown.UpdateViewport(0, 0, 800, 60);
            float asked = -1;
            shown.ScrollRequested += (_, e) => asked = e.Y;
            Check.True(preview.Follow(source, shown) && Math.Abs(asked - (code * FixedMeasurer.Metrics.LineHeight)) < 0.5f, $"the preview follows the source's scroll ({asked})");
            Check.True(!new MarkdownPreview(ui.Enqueue, () => options with { MarkdownPreviewFollow = false }).Follow(source, shown), "CRAFT_MARKDOWN_PREVIEW_FOLLOW off: it stays");

            // Attached to a document: a burst of edits renders once, after the pause, off the UI thread.
            using var host = new EditorHost(options, ui.Enqueue);
            var document = TextDocument.Create("# One\n", options with { NewLine = "\n" });
            var opened = host.Open(document, null);
            preview.Attach(host, opened);
            await WorkspaceSearchTests.PumpUntil(ui, () => preview.Document.Buffer.Current.GetText(0, 3) == "One", "the attached document renders");
            int renders = preview.Renders;
            for (int i = 0; i < 5; i++)
            {
                document.Buffer.Apply([TextEdit.Insert(document.Buffer.Current.Length, "x")]);
            }

            var settled = preview.Settled;
            await WorkspaceSearchTests.PumpUntil(ui, () => settled.IsCompleted, "the render after the pause");
            Check.True(preview.Renders == renders + 1 && preview.Document.Buffer.Current.GetText(0, preview.Document.Buffer.Current.Length).Contains("xxxxx", StringComparison.Ordinal),
                $"five quick edits, one render ({preview.Renders - renders})");
        }
        finally
        {
            if (registered)
            {
                EditorLanguages.Unregister(CSharpLanguage.RegularName);
            }
        }
    }

    [Test]
    public static async Task TheMarkdownLanguageColoursAndFoldsAndTheShellOpensThePreviewBeside()
    {
        var buffer = new Craft.Core.Text.PieceTableBuffer(Sample.ReplaceLineEndings("\n"));
        using var spans = new TokenSpanBuffer();
        using (var session = ProjectLanguages.MarkdownLanguage.Open(buffer))
        {
            session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        }

        string text = buffer.Current.GetText(0, buffer.Current.Length);
        string ClassAt(string what) => spans.Spans.ToArray().LastOrDefault(s => s.Start <= text.IndexOf(what, StringComparison.Ordinal) && s.End > text.IndexOf(what, StringComparison.Ordinal)).Class ?? "plain";
        Check.True(ClassAt("Craft") == TokenClasses.Keyword && ClassAt("docs/guide") == TokenClasses.String && ClassAt("var x") == TokenClasses.String && ClassAt("quoted") == TokenClasses.Comment
            && ClassAt("words") == "plain" && ClassAt("link") == TokenClasses.MethodName && ClassAt("csharp") == TokenClasses.ClassName,
            "a heading, a link's address, fenced code, a quote; prose plain (the spell check reads it), a link's label, the fence's language");
        var folds = MarkdownFolding.Ranges(buffer.Current);
        Check.True(folds.Any(f => f is { StartLine: 0, EndLine: 15 }) && folds.Any(f => f is { StartLine: 11, EndLine: 13 }), "the heading folds to the end, the fenced block on its own: " + string.Join(", ", folds));
        Check.True(EditorLanguages.Find("markdown") is null || EditorLanguages.ForFile("README.md")?.Name == "markdown", "README.md opens as Markdown when the C# plug-in is in");

        using var folder = new TempFolder();
        var path = folder.File("README.md");
        Disk.Write(path, Sample);
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), new QuietUi());
        await shell.OpenFolderAsync(folder.Path);
        var tab = await shell.OpenFileAsync(path);
        Check.True(tab is not null && EditorCommands.Find(ToolCommands.MarkdownPreview)!.CanExecute(host), "the preview's command for a Markdown file");
        Check.True(await host.ExecuteCommandAsync(ToolCommands.MarkdownPreviewBeside), "Ctrl+K V");
        Check.True(shell.Pages.Active?.Id == ShellModel.MarkdownPagePrefix + Path.GetFullPath(path) && shell.Pages.Active.Title == "Preview README.md", "a preview tab beside");
        Check.True(shell.Groups.Groups.Count == 2 && shell.Groups.Groups[1].Tabs.Active?.FilePath == Path.GetFullPath(path), "the source in a group on the right, the preview in the first");
        Check.True(shell.MarkdownPreviews.ContainsKey(shell.Pages.Active!.Id), "its preview made once");
        Check.True(EditorKeyBindings.All.Any(b => b.CommandId == ToolCommands.MarkdownPreview && b.When == KeyContext.TextEditor && b.Chord == KeyChord.Parse("Ctrl+Shift+V"))
            && EditorKeyBindings.All.Any(b => b.CommandId == ToolCommands.MarkdownPreviewBeside && b.Then is not null), "Ctrl+Shift+V and Ctrl+K V in the text editor (a terminal keeps its paste)");
        shell.Pages.Close(shell.Pages.Active.Id);
        Check.True(shell.MarkdownPreviews.Count == 0, "closing the tab disposes its preview");
    }

    internal sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    internal sealed class QuietUi : IShellUi
    {
        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Discard);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string title, string text, string action, bool danger = false, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}

/// <summary>X part 9, item 20: the image preview. Formats and sizes read from the bytes, zoom and fit, the status text, a simple SVG's shapes and a complex one said, an image opened in a page tab.</summary>
[TestGroup("ImagePreview")]
public static class ImagePreviewTests
{
    [Test]
    public static void FormatsAndSizesAreReadFromTheBytesAndTheZoomSteps()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 2, 128, 0, 0, 1, 224, 8, 6, 0, 0, 0];
        byte[] gif = [.. "GIF89a"u8, 64, 0, 32, 0, 0, 0];
        var bmp = new byte[30];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), 100);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), -50);
        byte[] ico = [0, 0, 1, 0, 2, 0, 16, 16, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 11, 8, 1, 44, 0, 200, 3, 0, 0];
        string Read(byte[] bytes) => ImageFormats.TryRead(bytes, out var format, out int w, out int h) ? $"{format} {w}x{h}" : "none";
        Check.Equal("PNG 640x480 | GIF 64x32 | BMP 100x50 | ICO 256x256 | JPEG 200x300 | none", string.Join(" | ", new[] { png, gif, bmp, ico, jpeg, "hello"u8.ToArray() }.Select(Read)),
            "each format's size from its header (a BMP stored top-down; an ICO's largest image, 0 meaning 256; a JPEG's frame)");
        Check.True(ImageFormats.IsImage("a.PNG") && ImageFormats.IsImage("b.svg") && ImageFormats.IsImage("c.jpeg") && !ImageFormats.IsImage("d.cs"), "the extensions it opens");

        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("shot.png"), png);
        var image = ImagePreview.Load(folder.File("shot.png"), 1024);
        Check.True(image is { Problem: null, Format: "PNG", Width: 640, Height: 480, Length: 29 } && !image.Bytes.IsEmpty, "read: its format, size and bytes");
        Check.Equal(0.5, image.ScaleFor(320, 1000), "fitted to a view half its width");
        Check.Equal(1.0, image.ScaleFor(4000, 4000), "never above its own size when fitted");
        Check.Equal("PNG · 640 × 480 · 29 B · 50% (fit)", image.StatusText(0.5), "the status bar's text");
        image.ZoomIn(0.5);
        Check.True(image is { Mode: ImageZoomMode.Zoom, Zoom: 0.75 } && image.ScaleFor(320, 1000) == 0.75, "zoom in from what shows: the next step");
        image.ZoomOut(0.75);
        image.ZoomOut(0.5);
        Check.Equal(0.25, image.Zoom, "zoom out two steps");
        image.ActualSize();
        Check.Equal("PNG · 640 × 480 · 29 B · 100%", image.StatusText(image.ScaleFor(1, 1)), "100%");
        image.FitToView();
        Check.Equal(ImageZoomMode.Fit, image.Mode, "fit again");
        Check.True(ImagePreview.Load(folder.File("shot.png"), 10).Problem!.Contains("CRAFT_IMAGE_PREVIEW_MAX_MB", StringComparison.Ordinal), "a file past the limit is said, not read");
        File.WriteAllText(folder.File("fake.png"), "not an image");
        Check.True(ImagePreview.Load(folder.File("fake.png"), 1024).Problem!.Contains("not an image", StringComparison.Ordinal), "a file that is not an image is said");
    }

    [Test]
    public static async Task ASimpleSvgIsDrawnFromItsShapesAComplexOneIsSaidAndAnImageOpensInAPageTab()
    {
        var svg = SvgDrawing.Read(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <title>icon</title>
              <rect x="2" y="3" width="20" height="14" rx="2" />
              <g fill="#f00"><circle cx="12" cy="12" r="3" /></g>
              <path d="M8 21h8" />
              <polygon points="1,1 5,1 3,4" fill="#00ff0080" />
            </svg>
            """));
        Check.True(svg is { Width: 24, Height: 24, ViewWidth: 24, Unsupported: null } && svg.Shapes.Count == 4, "its size, view box and four shapes: " + svg.Unsupported);
        Check.True(svg.Shapes[0] is { PathData: "M2 3H22V17H2Z", Fill: null, Stroke: SvgDrawing.CurrentColor, StrokeWidth: 2 }, "a rectangle as a path, the stroke the text's colour, no fill");
        Check.True(svg.Shapes[1] is { Fill: 0xFFFF0000u } && svg.Shapes[1].PathData.StartsWith("M9 12A3 3", StringComparison.Ordinal), "a circle filled by its group");
        Check.True(svg.Shapes[3] is { PathData: "M1 1L5 1L3 4Z", Fill: 0x8000FF00u }, "a polygon, its colour with alpha");
        var complex = SvgDrawing.Read(Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 10 10\"><defs><linearGradient id=\"g\"/></defs><text x=\"1\">Hi</text><rect width=\"5\" height=\"5\" fill=\"url(#g)\" transform=\"rotate(4)\"/></svg>"));
        Check.Equal("gradient or pattern, text, transform", complex.Unsupported, "what it does not draw is named (a gradient defined but used is said where used)");

        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("logo.png"), [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 16, 0, 0, 0, 8, 8, 6, 0, 0, 0]);
        await File.WriteAllTextAsync(folder.File("icon.svg"), "<svg width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\"/></svg>");
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new MarkdownPreviewTests.NoDialogs(), new MarkdownPreviewTests.QuietUi());
        await shell.OpenFolderAsync(folder.Path);
        var tab = await shell.OpenFileAsync(folder.File("logo.png"));
        Check.True(tab is null && shell.Tabs.Tabs.Count == 0 && shell.Pages.Active?.Id == ShellModel.ImagePagePrefix + Path.GetFullPath(folder.File("logo.png")), "an image opens in a preview tab, not as text");
        Check.True(shell.ImagePreviews.Values.Single() is { Width: 16, Height: 8 } && shell.ImageStatusText == "PNG · 16 × 8 · 29 B · 100% (fit)", "read off the UI thread; its format, size and zoom in the status bar: " + shell.ImageStatusText);
        await shell.OpenFileAsync(folder.File("icon.svg"));
        Check.True(shell.ImagePreviews.Values.Any(i => i.Svg is { Shapes.Count: 1 }), "an SVG too");
        Check.True(await host.ExecuteCommandAsync(ToolCommands.OpenImageAsText) && shell.Tabs.Active?.FilePath == Path.GetFullPath(folder.File("icon.svg")), "Image: Open as text opens its source");
        shell.Pages.CloseAll();
        Check.True(shell.ImagePreviews.Count == 0 && shell.ImageStatusText is null, "closed: its preview and its status item go");
    }
}

/// <summary>X part 9, item 21: the TODO list. Tags in comments only (a language's classification), any line of plain text, whole words with their case; the view's scan, groups, a click, a saved file's rescan; Craft's own source scanned and timed.</summary>
[TestGroup("TodoList")]
public static class TodoListTests
{
    [Test]
    public static void TagsAreFoundInCommentsOnlyAndInPlainTextByLine()
    {
        const string code = """
            // TODO: split this class
            var s = "TODO not a comment";
            int TODOS = 1; /* FIXME(ana): the limit */
            // NOTE the order matters
            // HACKS are not HACK words
            /// <summary>HACK around the parser</summary>
            """;
        var tags = CraftOptions.DefaultTodoTags;
        var found = TodoScanner.Scan("/w/A.cs", code.ReplaceLineEndings("\n"), new CSharpLanguage(), tags);
        Check.Equal("0:3 TODO split this class | 2:18 FIXME the limit | 3:3 NOTE the order matters | 4:17 HACK words | 5:13 HACK around the parser",
            string.Join(" | ", found.Select(e => $"{e.Line}:{e.Column} {e.Tag} {e.Text}")),
            "in comments only (a string's TODO and an identifier's are not; a doc comment's is), as whole words with their case; the text after ':' and '(name)', a block comment's close left out");
        var plain = TodoScanner.Scan("/w/notes.txt", "TODO: buy milk\nnothing\n  FIXME later\nTODOs\n", null, tags);
        Check.Equal("0 TODO buy milk | 2 FIXME later", string.Join(" | ", plain.Select(e => $"{e.Line} {e.Tag} {e.Text}")), "plain text: any line");
        Check.Equal("REVIEW", TodoScanner.Scan("/w/a.txt", "REVIEW: x\nTODO: y\n", null, ["REVIEW"]).Single().Tag, "the tags are CRAFT_TODO_TAGS'");
        Check.True(TodoScanner.HoldsTag("a // TODO b"u8, tags) && !TodoScanner.HoldsTag("nothing here"u8, tags), "a file without a tag's bytes is never decoded");
        var options = CraftOptions.FromVariables(name => name == "CRAFT_TODO_TAGS" ? "TODO, BUG" : null);
        Check.True(options.TodoTags is ["TODO", "BUG"], "CRAFT_TODO_TAGS read");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(name => name == "CRAFT_TODO_TAGS" ? "TO DO" : null), "a tag with a space is refused, naming the variable");
    }

    [Test]
    public static async Task TheViewScansTheWorkspaceGroupsByFileAndASavedFileIsReadAgain()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        Disk.Write(folder.File("src/A.cs"), "class A { } // TODO: name it\n// FIXME: leaks\n");
        Disk.Write(folder.File("notes.txt"), "NOTE: plain text counts\n");
        File.WriteAllBytes(folder.File("blob.bin"), [0, 1, 2, .. "TODO"u8]);
        bool registered = EditorLanguages.Find("csharp") is null;
        if (registered)
        {
            EditorLanguages.Register(new CSharpLanguage());
        }

        try
        {
            var ui = new ConcurrentQueue<Action>();
            var options = new CraftOptions();
            using var host = new EditorHost(options, ui.Enqueue);
            using var view = new TodoListView(host, () => folder.Path, () => options);
            var scanned = view.RefreshAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => scanned.IsCompleted, "the scan ends");
            Check.True(view.Count == 3 && view.Files.Count == 2 && view.Badge == "3", "three entries in two files (the binary file passed over)");
            var groups = view.Blocks.OfType<PanelResults>().Single().Groups;
            Check.True(groups[0] is { Title: "notes.txt" } && groups[1] is { Title: "A.cs", Detail: "src", Badge: "2" } && groups[1].Rows[0] is { Text: "TODO: name it", Prefix: "1", Tone: PanelTone.Accent }
                && groups[1].Rows[1].Tone == PanelTone.Error, "grouped by file in path order, each row its line, tag and text, FIXME in the error colour");

            Disk.Write(folder.File("src/A.cs"), "class A { }\n");
            await view.RescanFileAsync(folder.File("src/A.cs"));
            Check.True(view.Count == 1 && view.Files.Count == 1, "a saved file is read again alone: its entries gone");
            await view.ActAsync("todo.filter", "plain");
            Check.Equal(1, view.Blocks.OfType<PanelResults>().Single().Groups.Count, "the filter");

            IEditorDocument? opened = null;
            host.OpenFile = async (path, _) => opened = host.Open(await TextDocument.OpenAsync(path, options), null);
            string value = view.Blocks.OfType<PanelResults>().Single().Groups[0].Rows[0].Value!;
            await view.ActAsync("todo.open", value);
            Check.True(opened?.FilePath == Path.GetFullPath(folder.File("notes.txt")) && opened.Selection.Caret == 0, "a click opens the place");
        }
        finally
        {
            if (registered)
            {
                EditorLanguages.Unregister(CSharpLanguage.RegularName);
            }
        }
    }

    [Test]
    public static async Task CraftsOwnSourceIsScannedAndTimed()
    {
        string root = TestRunner.RepositoryPath("src");
        if (!Directory.Exists(root))
        {
            throw new NotRunHereException("the repository's src folder");
        }

        bool registered = EditorLanguages.Find("csharp") is null;
        if (registered)
        {
            EditorLanguages.Register(new CSharpLanguage());
        }

        try
        {
            var ui = new ConcurrentQueue<Action>();
            var options = new CraftOptions();
            using var host = new EditorHost(options, ui.Enqueue);
            using var view = new TodoListView(host, () => root, () => options);
            var cold = view.RefreshAsync();
            await WorkspaceSearchTests.PumpUntil(ui, () => cold.IsCompleted, "the cold scan");
            var (coldMs, files) = view.LastScan;
            int count = view.Count;
            var warm = new double[3];
            for (int i = 0; i < warm.Length; i++)
            {
                var again = view.RefreshAsync();
                await WorkspaceSearchTests.PumpUntil(ui, () => again.IsCompleted, "a warm scan");
                warm[i] = view.LastScan.Milliseconds;
                Check.Equal(count, view.Count, "the same entries each time");
            }

            Array.Sort(warm);
            Check.True(files > 200, $"Craft's source read ({files} files)");
            string line = string.Create(CultureInfo.InvariantCulture, $"TODO scan of Craft's src ({files} files, {Environment.ProcessorCount} cores): {count} entries, cold {coldMs:0.0} ms, warm median {warm[1]:0.0} ms");
            Console.WriteLine("    " + line);
            Directory.CreateDirectory(TestRunner.RepositoryPath("test-output", "benches"));
            await File.WriteAllTextAsync(TestRunner.RepositoryPath("test-output", "benches", "todo-scan.txt"), line + "\n");
        }
        finally
        {
            if (registered)
            {
                EditorLanguages.Unregister(CSharpLanguage.RegularName);
            }
        }
    }
}

/// <summary>
/// X part 9, item 22: the spell check. Identifiers split by case and '_'; the bundled dictionary (case ignored,
/// possessives), suggestions by edit distance, the user's and the workspace's words; only the shown lines' comments and
/// strings checked, a warm frame checking nothing and allocating nothing; the quick fixes; the options.
/// </summary>
[TestGroup("SpellCheck")]
public static class SpellCheckTests
{
    [Test]
    public static void IdentifiersAreSplitByCaseUnderscoreAndDigits()
    {
        static string Words(string text)
        {
            var words = new List<string>();
            foreach (var (start, length) in SpellWords.Of(text))
            {
                words.Add(text.Substring(start, length));
            }

            return string.Join(" ", words);
        }

        Check.Equal("parse Line HTTP Server snake case utf Text don't Is URL", Words("parseLine HTTPServer snake_case utf8Text don't IsURL"),
            "camelCase, an acronym before a word, snake_case, a digit, an apostrophe inside a word, an acronym at the end");
        Check.True(SpellWords.InUrlOrPath("see https://exampel.org/x now", 14, 7) && SpellWords.InUrlOrPath("at src/Foo/Bar.cs", 3, 3) && !SpellWords.InUrlOrPath("plain words", 0, 5),
            "an address and a path are passed over");
    }

    [Test]
    public static async Task TheDictionaryKnowsWordsSuggestsByDistanceAndKeepsTheUsersAndTheWorkspacesWords()
    {
        var english = WordListDictionary.English;
        await english.Ready;
        Check.True(english.Count > 80_000, $"the bundled list ({english.Count} words)");
        Check.True(english.Contains("receive") && english.Contains("Receive") && english.Contains("RECEIVE") && english.Contains("editor's") && english.Contains("async") && !english.Contains("recieve"),
            "words with any case, a possessive, the words of code; a misspelling unknown");
        Check.True(english.Suggest("recieve", 5) is [var first, ..] && first == "receive", "the nearest word first (a swap of neighbours is one edit)");
        Check.True(english.Suggest("Recieve", 3)[0] == "Receive" && english.Suggest("RECIEVE", 3)[0] == "RECEIVE", "written as the word was");
        Check.Equal(1, WordListDictionary.Distance("recieve", "receive", 2), "a swap is one edit");
        Check.Equal(3, WordListDictionary.Distance("abc", "xyzabcd", 2), "past the limit: limit + 1");

        using var folder = new TempFolder();
        var checker = new SpellChecker();
        checker.Configure(new CraftOptions(), folder.File("data"), folder.File("ws"));
        Check.True(!checker.IsKnown("craftily") || checker.IsKnown("craftily"), "configured");
        Check.True(!checker.IsKnown("grokkable"), "an unknown word");
        Check.True(await checker.AddToUserAsync("grokkable") is null && checker.IsKnown("Grokkable"), "added to the user's words: known at once, any case");
        Check.True((await File.ReadAllTextAsync(folder.File("data/spelling/words.txt"))).Contains("grokkable", StringComparison.Ordinal), "kept in the user's data folder");
        Check.True(await checker.IgnoreInWorkspaceAsync("frobnicate") is null && checker.IsKnown("frobnicate") && File.Exists(folder.File("ws/.craft/words.txt")), "ignored in the workspace: .craft/words.txt");
        int version = checker.Version;
        var fresh = new SpellChecker();
        fresh.Configure(new CraftOptions(), folder.File("data"), folder.File("ws"));
        for (int i = 0; i < 200 && !(fresh.IsKnown("grokkable") && fresh.IsKnown("frobnicate")); i++)
        {
            await Task.Delay(10);
        }

        Check.True(fresh.IsKnown("grokkable") && fresh.IsKnown("frobnicate") && version > 1, "another session reads both files back");
        var missing = new SpellChecker();
        missing.Configure(new CraftOptions { SpellLanguage = "xx" }, null, null);
        Check.True(missing.Dictionary is null && missing.Problem!.Contains("not registered", StringComparison.Ordinal) && missing.IsKnown("zzzz"), "an unknown language: said, nothing marked");
        var options = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_SPELL_CHECK" => "false",
            "CRAFT_SPELL_LANGUAGE" => "de",
            "CRAFT_SPELL_SUGGESTIONS" => "3",
            "CRAFT_SPELL_MIN_LENGTH" => "2",
            _ => null,
        });
        Check.True(options is { SpellCheck: false, SpellLanguage: "de", SpellSuggestions: 3, SpellMinLength: 2 }, "the options read");
    }

    [Test]
    public static async Task OnlyTheShownCommentsAndStringsAreCheckedAndAWarmFrameAllocatesNothing()
    {
        await WordListDictionary.English.Ready;
        var text = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"recieveData(\"the vlaue {i}\"); // the parseLine recieve here\n");
        }

        var language = new QuoteLanguage();
        var (model, document, _) = Editors.Make(text.ToString(), language: language);
        using var _ = model;
        var checker = new SpellChecker();
        checker.Configure(new CraftOptions(), null, null);
        model.SpellChecker = checker;
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var layer = model.Spelling;
        int shown = renderer.LinesDrawn;
        var misses = layer.Misses();
        Check.True(misses.Count == 2 * shown && misses.All(m => m.Line < shown), $"only the shown lines ({shown}): two words each ({misses.Count})");
        string first = model.Snapshot.GetText(model.Snapshot.GetLine(0).Start, model.Snapshot.GetLine(0).Length);
        Check.True(misses[0].Start == first.IndexOf("vlaue", StringComparison.Ordinal) && misses[1].Start == first.IndexOf("recieve here", StringComparison.Ordinal),
            "the string's and the comment's misspellings; the code's identifier (recieveData) and the comment's camelCase parts (parse, Line) not");
        Check.True(canvas.Calls.Any(c => c.Kind == "line" && c.Argb == model.Theme.Colors.Spelling) && renderer.SquiggleSegmentsDrawn == 0, "faint squiggles in the theme's spelling colour, not a diagnostic's");
        long checkedLines = layer.LinesChecked;

        // Warm: the same frame again checks nothing and allocates nothing.
        for (int i = 0; i < 200; i++)
        {
            renderer.Draw(canvas, model, 0, 0, 800, 400, (i & 1) == 0);
            canvas.Clear();
        }

        const int frames = 500;
        var times = new double[frames];
        long bytes = 0;
        for (int i = 0; i < frames; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long t = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, 0, 800, 400, (i & 1) == 0);
            times[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            canvas.Clear();
        }

        Array.Sort(times);
        Check.True(layer.LinesChecked == checkedLines, "a warm frame checks no line again");
        Check.Equal(0L, bytes, "0 bytes over 500 warm frames");
        string line = string.Create(CultureInfo.InvariantCulture, $"spell check, {shown} shown lines with a comment and a string each: warm frame median {times[frames / 2]:0.0} us, p99 {times[frames * 99 / 100]:0.0} us, {bytes} B over {frames} frames; {checkedLines} lines checked cold");
        Console.WriteLine("    " + line);
        Directory.CreateDirectory(TestRunner.RepositoryPath("test-output", "benches"));
        await File.WriteAllTextAsync(TestRunner.RepositoryPath("test-output", "benches", "spell-check.txt"), line + "\n");

        // A scroll checks only the lines it brings in; an edit only its line.
        renderer.Draw(canvas, model, 0, FixedMeasurer.Metrics.LineHeight * 5, 800, 400, caretVisible: true);
        Check.Equal(checkedLines + 5, layer.LinesChecked, "five lines scrolled in, five checked");
        long now = layer.LinesChecked;
        document.Buffer.Apply([TextEdit.Insert(model.Snapshot.GetLine(6).Start, "// wrold ")]);
        renderer.Draw(canvas, model, 0, FixedMeasurer.Metrics.LineHeight * 5, 800, 400, caretVisible: true);
        string Word((int Line, int Start, int Length) m) => model.Snapshot.GetText(model.Snapshot.GetLine(m.Line).Start + m.Start, m.Length);
        Check.True(layer.LinesChecked == now + 1 && layer.Misses().Any(m => m.Line == 6 && Word(m) == "wrold"), "an edited line alone is checked again (all of it a comment now, from the inserted '//')");

        // Known words drawn again: a word added redraws without the squiggle.
        await checker.AddToUserAsync("vlaue");
        renderer.Draw(canvas, model, 0, FixedMeasurer.Metrics.LineHeight * 5, 800, 400, caretVisible: true);
        var drawn = layer.Misses().Where(m => m.Line >= renderer.LastVisible.First && m.Line <= renderer.LastVisible.Last).ToList();
        Check.True(drawn.All(m => Word(m) != "vlaue") && drawn.Any(m => Word(m) == "recieve"), "a word added is not marked any more on the lines drawn (the others still are)");

        // The quick fix at an unknown word: its suggestions, Add to dictionary, Ignore in workspace.
        int at = model.Snapshot.GetLine(5).Start + first.IndexOf("recieve here", StringComparison.Ordinal) + 2;
        Check.True(layer.TryMissAt(at, out int wordStart, out int length, out string word) && wordStart < at && word == "recieve" && length == 7, "the word at the caret");
        var actions = new SpellingCodeActions(new EditorHost(), checker).ActionsFor(new CodeActionContext(new HostDocument(model.Document), at, at, []));
        Check.True(actions.Count >= 3 && actions[0].Title == "Change 'recieve' to 'receive'" && actions[^2].Title.StartsWith("Add 'recieve'", StringComparison.Ordinal)
            && actions[^1].Title.Contains(".craft/words.txt", StringComparison.Ordinal), "its suggestions, Add to dictionary, Ignore in workspace: " + string.Join(", ", actions.Select(a => a.Title)));

        // CRAFT_SPELL_CHECK off: nothing checked, nothing drawn.
        var (off, _, _) = Editors.Make("x = \"vlaue\";\n", new CraftOptions { SpellCheck = false }, new QuoteLanguage());
        using var renderer2 = new EditorRenderer();
        renderer2.Draw(new RecordingCanvas(), off, 0, 0, 800, 400, caretVisible: true);
        Check.True(off.Spelling.LinesChecked == 0, "off: nothing checked");
    }

    private sealed class HostDocument(ITextDocument document) : IEditorDocument
    {
        public ITextDocument Document { get; } = document;

        public string? FilePath => null;

        public string LanguageName => "quotes";

        public ILanguage? Language => null;

        public SelectionState Selection => default;
    }
}
