// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace Craft.Abstractions;

/// <summary>How a test ended (X part 9: the Test explorer's marks).</summary>
public enum TestOutcome
{
    /// <summary>Not run yet (or its last result is gone: the test changed).</summary>
    NotRun,

    /// <summary>Running now.</summary>
    Running,

    /// <summary>It passed.</summary>
    Passed,

    /// <summary>It failed (an assertion, an exception, a timeout).</summary>
    Failed,

    /// <summary>It was skipped (ignored, inconclusive, not executed).</summary>
    Skipped,
}

/// <summary>A test as a provider found it: its project, its class and its method, and where it is declared.</summary>
/// <param name="Provider">The provider that found it (its name in <see cref="TestProviders"/>).</param>
/// <param name="Id">Its id, unique within the provider: the fully qualified method ("Ns.Class.Method"), what a filter names.</param>
/// <param name="Project">Its project's file, relative to the workspace's root ('/' between folders).</param>
/// <param name="Class">Its class, fully qualified ("Ns.Outer+Inner" as a filter names a nested one).</param>
/// <param name="Method">Its method's name.</param>
public sealed record TestItem(string Provider, string Id, string Project, string Class, string Method)
{
    /// <summary>Its framework ("xunit", "nunit", "mstest", or a provider's own), or "".</summary>
    public string Framework { get; init; } = "";

    /// <summary>The file that declares it, relative to the workspace's root, or null.</summary>
    public string? File { get; init; }

    /// <summary>The line of its declaration (its name's line, from 0), or -1.</summary>
    public int Line { get; init; } = -1;

    /// <summary>The class's short name ("Inner" of "Ns.Outer+Inner").</summary>
    public string ClassName
    {
        get
        {
            int at = Class.AsSpan().LastIndexOfAny('.', '+');
            return at < 0 ? Class : Class[(at + 1)..];
        }
    }
}

/// <summary>What a test run ran and how it ended: one result per test (a theory's cases joined into their method's).</summary>
/// <param name="Id">The test's <see cref="TestItem.Id"/>.</param>
/// <param name="Outcome">How it ended.</param>
public sealed record TestResult(string Id, TestOutcome Outcome)
{
    /// <summary>How long it took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The failure's message (or why it was skipped), or null.</summary>
    public string? Message { get; init; }

    /// <summary>The failure's stack trace (its frames name <c>file:line</c>), or null.</summary>
    public string? StackTrace { get; init; }

    /// <summary>What it wrote, or null.</summary>
    public string? Output { get; init; }
}

/// <summary>A run of tests a provider is asked to start: the project, the tests (none: all of it) and where the results go.</summary>
/// <param name="Root">The workspace's folder (a full path).</param>
/// <param name="Project">The project's file, relative to the root.</param>
/// <param name="Tests">The tests to run; empty runs every test of the project.</param>
/// <param name="ResultsFile">The results file the run writes (a full path; a TRX file for .NET), read by <see cref="ITestProvider.ReadResults"/>.</param>
public sealed record TestRunRequest(string Root, string Project, IReadOnlyList<TestItem> Tests, string ResultsFile);

/// <summary>
/// The tests of one kind of project, registered in <see cref="TestProviders"/> (X part 9): the C# plug-in's "dotnet" (xUnit,
/// NUnit and MSTest, found in the syntax off the UI thread, run with <c>dotnet test --filter</c>), a web plug-in's npm
/// tests later. The Test explorer asks every provider off the UI thread and again when a file it <see cref="Watches"/>
/// changes; every run goes through the Run view's approval and registered sandbox, never the provider itself.
/// </summary>
public interface ITestProvider : IKind
{
    /// <summary>Its title in the Test explorer ("C# (.NET)").</summary>
    string Title { get; }

    /// <summary>Whether a file of this name (a source file, a project) changes what <see cref="Discover"/> gives.</summary>
    bool Watches(ReadOnlySpan<char> fileName);

    /// <summary>The tests of <paramref name="workspace"/> (called off the UI thread; it reads files; nothing runs).</summary>
    IReadOnlyList<TestItem> Discover(RunWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// The command that runs <paramref name="request"/> (its tests filtered, its results written to
    /// <see cref="TestRunRequest.ResultsFile"/>); one whose <see cref="RunCommand.Problem"/> says why when it cannot run here.
    /// </summary>
    RunCommand Command(TestRunRequest request);

    /// <summary>
    /// The results of a finished run of <paramref name="request"/>: from its results file when there is one, else from its
    /// <paramref name="output"/> (the console's lines). Called off the UI thread.
    /// </summary>
    IReadOnlyList<TestResult> ReadResults(TestRunRequest request, string output, CancellationToken cancellationToken = default);
}

/// <summary>
/// The test providers by name (ignoring case), the newest first. Craft's library registers none (the C# plug-in registers
/// "dotnet"); an unknown name fails with "not registered".
/// </summary>
public static class TestProviders
{
    private static readonly KindTable<ITestProvider> Table = new(nameof(TestProviders), "Test provider", "add it with TestProviders.Register",
        comparer: StringComparer.OrdinalIgnoreCase, newestFirst: true, unguarded: "each provider finds and runs its own tests; none stands in for another");

    /// <summary>Raised when a provider is registered or unregistered (on the registering thread): the Test explorer looks again.</summary>
    public static event EventHandler? Changed;

    /// <summary>Registers <paramref name="provider"/> under its name.</summary>
    public static void Register(ITestProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the provider of that name; false when none was registered.</summary>
    public static bool Unregister(string name)
    {
        bool removed = Table.Unregister(name);
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<ITestProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static ITestProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ITestProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>
/// A Visual Studio test results file (TRX, what <c>dotnet test --logger trx</c> writes) read as a stream: each
/// <c>UnitTestResult</c>'s outcome, duration, message, stack trace and output, named by its test definition's class and
/// method (<c>TestMethod className</c> and <c>name</c>), so xUnit's, NUnit's and MSTest's results name their tests alike; a
/// theory's or a test case's several results are joined into their method's (failed when any failed).
/// </summary>
public static class TrxResults
{
    /// <summary>The results in <paramref name="trx"/>, in the order their methods first appear.</summary>
    /// <exception cref="XmlException">The file is not XML.</exception>
    public static IReadOnlyList<TestResult> Read(Stream trx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trx);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);   // test id → Class.Method
        var raw = new List<(string TestId, string Name, TestResult Result)>();
        using var reader = XmlReader.Create(trx, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? definition = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            switch (reader.LocalName)
            {
                case "UnitTest":
                    definition = reader.GetAttribute("id");
                    break;
                case "TestMethod" when definition is not null:
                    string className = reader.GetAttribute("className") ?? "";
                    int comma = className.IndexOf(',', StringComparison.Ordinal);
                    if (comma >= 0)
                    {
                        className = className[..comma].Trim();                  // "Ns.Class, Assembly" (MSTest's older files)
                    }

                    names[definition] = className.Length == 0 ? reader.GetAttribute("name") ?? "" : className + "." + (reader.GetAttribute("name") ?? "");
                    definition = null;
                    break;
                case "UnitTestResult":
                    raw.Add(ReadResult(reader));
                    break;
            }
        }

        var order = new List<string>();
        var joined = new Dictionary<string, TestResult>(StringComparer.Ordinal);
        foreach (var (testId, name, result) in raw)
        {
            string id = names.TryGetValue(testId, out var known) && known.Length > 0 ? known : Plain(name);
            if (!joined.TryGetValue(id, out var before))
            {
                order.Add(id);
                joined[id] = result with { Id = id };
                continue;
            }

            joined[id] = Join(before, result with { Id = id });
        }

        return [.. order.Select(id => joined[id])];
    }

    /// <summary>The worse of two results of one method (a theory's cases): failed over skipped over passed, their times added.</summary>
    public static TestResult Join(TestResult first, TestResult second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        static int Rank(TestOutcome outcome) => outcome switch
        {
            TestOutcome.Failed => 3,
            TestOutcome.Skipped => 1,
            TestOutcome.Passed => 2,
            _ => 0,
        };

        var worse = Rank(second.Outcome) > Rank(first.Outcome) ? second : first;
        return worse with { Duration = first.Duration + second.Duration };
    }

    /// <summary>A TRX outcome word as an outcome ("Passed", "Failed", "NotExecuted", "Inconclusive", "Timeout", ...).</summary>
    public static TestOutcome OutcomeOf(ReadOnlySpan<char> outcome) =>
        outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? TestOutcome.Passed
        : outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Error", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("Timeout", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Aborted", StringComparison.OrdinalIgnoreCase) ? TestOutcome.Failed
        : outcome.IsEmpty ? TestOutcome.NotRun
        : TestOutcome.Skipped;

    // A theory's case is named "Method(x: 1)": its method's name is what a result is joined under.
    private static string Plain(string name)
    {
        int paren = name.IndexOf('(', StringComparison.Ordinal);
        return paren > 0 ? name[..paren] : name;
    }

    private static (string, string, TestResult) ReadResult(XmlReader reader)
    {
        string testId = reader.GetAttribute("testId") ?? "";
        string name = reader.GetAttribute("testName") ?? "";
        var outcome = OutcomeOf(reader.GetAttribute("outcome"));
        var duration = TimeSpan.TryParse(reader.GetAttribute("duration"), CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
        string? message = null, stack = null, output = null;
        if (!reader.IsEmptyElement)
        {
            // The result's own elements (Output › ErrorInfo › Message, StackTrace; Output › StdOut), up to its end.
            int depth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > depth)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "Message" or "StackTrace" or "StdOut")
                {
                    string which = reader.LocalName;
                    string value = reader.ReadElementContentAsString();       // moves past the element
                    switch (which)
                    {
                        case "Message":
                            message = value.Trim();
                            break;
                        case "StackTrace":
                            stack = value.TrimEnd();
                            break;
                        default:
                            output = value.TrimEnd();
                            break;
                    }

                    continue;
                }

                reader.Read();
            }
        }

        return (testId, name, new TestResult(name, outcome) { Duration = duration, Message = message, StackTrace = stack, Output = output });
    }
}

/// <summary>
/// The results <c>dotnet test</c>'s console logger writes (at its normal verbosity): "  Passed Ns.Class.Method [12 ms]",
/// "  Failed Ns.Class.Method [3 ms]" followed by its "Error Message:" and "Stack Trace:" lines, "  Skipped ...": what a run
/// without a results file is read from.
/// </summary>
public static class TestConsoleResults
{
    /// <summary>The results <paramref name="output"/> reports, a theory's cases joined into their method's.</summary>
    public static IReadOnlyList<TestResult> Read(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var order = new List<string>();
        var joined = new Dictionary<string, TestResult>(StringComparer.Ordinal);
        TestResult? open = null;
        string? section = null;
        var message = new System.Text.StringBuilder();
        var stack = new System.Text.StringBuilder();
        foreach (var range in output.AsSpan().Split('\n'))
        {
            var line = output.AsSpan()[range].TrimEnd('\r');
            var trimmed = line.Trim();
            if (TryHeader(trimmed, out var outcome, out var name, out var duration))
            {
                Close();
                open = new TestResult(name, outcome) { Duration = duration };
                section = null;
                continue;
            }

            if (open is null)
            {
                continue;
            }

            if (trimmed.SequenceEqual("Error Message:"))
            {
                section = "message";
            }
            else if (trimmed.SequenceEqual("Stack Trace:"))
            {
                section = "stack";
            }
            else if (trimmed.StartsWith("Standard Output Messages:", StringComparison.Ordinal) || trimmed.IsEmpty && section == "stack")
            {
                section = null;
            }
            else if (section == "message")
            {
                message.Append(message.Length > 0 ? "\n" : "").Append(trimmed);
            }
            else if (section == "stack")
            {
                stack.Append(stack.Length > 0 ? "\n" : "").Append(trimmed);
            }
        }

        Close();
        return [.. order.Select(id => joined[id])];

        void Close()
        {
            if (open is null)
            {
                return;
            }

            var result = open with
            {
                Message = message.Length > 0 ? message.ToString() : null,
                StackTrace = stack.Length > 0 ? stack.ToString() : null,
            };
            message.Clear();
            stack.Clear();
            open = null;
            string id = result.Id;
            int paren = id.IndexOf('(', StringComparison.Ordinal);
            if (paren > 0)
            {
                id = id[..paren];
                result = result with { Id = id };
            }

            if (joined.TryGetValue(id, out var before))
            {
                joined[id] = TrxResults.Join(before, result);
            }
            else
            {
                order.Add(id);
                joined[id] = result;
            }
        }
    }

    // "Passed Ns.Class.Method [12 ms]", "Failed X [< 1 ms]", "Skipped X [1 s]" (the time is optional).
    private static bool TryHeader(ReadOnlySpan<char> line, out TestOutcome outcome, out string name, out TimeSpan duration)
    {
        outcome = TestOutcome.NotRun;
        name = "";
        duration = TimeSpan.Zero;
        int space = line.IndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        var word = line[..space];
        outcome = word.SequenceEqual("Passed") ? TestOutcome.Passed : word.SequenceEqual("Failed") ? TestOutcome.Failed : word.SequenceEqual("Skipped") ? TestOutcome.Skipped : TestOutcome.NotRun;
        if (outcome == TestOutcome.NotRun)
        {
            return false;
        }

        var rest = line[(space + 1)..].Trim();
        int bracket = rest.LastIndexOf('[');
        if (bracket > 0 && rest[^1] == ']')
        {
            duration = Duration(rest[(bracket + 1)..^1]);
            rest = rest[..bracket].TrimEnd();
        }

        // "Failed!  - Failed: 1, Passed: 2" (the summary) is not a test.
        if (rest.IsEmpty || rest[0] is '!' or '-' || rest.Contains(' ') && !rest.Contains('('))
        {
            return false;
        }

        name = rest.ToString();
        return true;
    }

    private static TimeSpan Duration(ReadOnlySpan<char> text)
    {
        text = text.TrimStart('<').Trim();
        int space = text.IndexOf(' ');
        if (space <= 0 || !double.TryParse(text[..space], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return TimeSpan.Zero;
        }

        var unit = text[(space + 1)..];
        return unit.SequenceEqual("ms") ? TimeSpan.FromMilliseconds(value) : unit.SequenceEqual("s") ? TimeSpan.FromSeconds(value) : unit.SequenceEqual("m") ? TimeSpan.FromMinutes(value) : TimeSpan.Zero;
    }
}

/// <summary>A stack trace's frame that names its place: ".NET's "at Ns.Class.Method() in /src/File.cs:line 42".</summary>
/// <param name="Text">The frame's line as written.</param>
/// <param name="File">The file it names, or null.</param>
/// <param name="Line">Its line (from 1), or 0.</param>
public sealed record TestStackFrame(string Text, string? File, int Line)
{
    /// <summary>The frames of <paramref name="stackTrace"/>, each with its file and line when it names them.</summary>
    public static IReadOnlyList<TestStackFrame> Parse(string? stackTrace)
    {
        var frames = new List<TestStackFrame>();
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return frames;
        }

        foreach (var range in stackTrace.AsSpan().Split('\n'))
        {
            var line = stackTrace.AsSpan()[range].Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            frames.Add(TryPlace(line, out var file, out int number) ? new TestStackFrame(line.ToString(), file, number) : new TestStackFrame(line.ToString(), null, 0));
        }

        return frames;
    }

    /// <summary>The file and line a frame names (" in FILE:line N"); false when it names none.</summary>
    public static bool TryPlace(ReadOnlySpan<char> frame, [NotNullWhen(true)] out string? file, out int line)
    {
        file = null;
        line = 0;
        int at = frame.LastIndexOf(" in ", StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var place = frame[(at + 4)..];
        int colon = place.LastIndexOf(":line ", StringComparison.Ordinal);
        if (colon <= 0 || !int.TryParse(place[(colon + 6)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out line))
        {
            return false;
        }

        file = place[..colon].ToString();
        return true;
    }
}
