// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Reflection;

namespace Craft.Tests.Runner;

/// <summary>Marks a class whose public static <see cref="TestAttribute"/> methods are tests; its name is the group's (the first part of each test's name).</summary>
/// <param name="name">The group's name ("Buffer").</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TestGroupAttribute(string name) : Attribute
{
    /// <summary>The group's name.</summary>
    public string Name { get; } = name;
}

/// <summary>Marks a public static method (no parameters, returning void or <see cref="Task"/>) as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

/// <summary>A test that cannot run here (it needs the repository, and there is none), with what it needs.</summary>
public sealed class NotRunHereException(string message) : Exception(message);

/// <summary>One test: its full name ("Buffer.InsertsAtStart") and how to run it.</summary>
/// <param name="Name">"Group.Method".</param>
/// <param name="Run">Runs it; a thrown exception is a failure.</param>
public sealed record TestCase(string Name, Func<Task> Run);

/// <summary>How a run goes: which tests, where the lines go, the failures file, the progress line, the timeout.</summary>
public sealed class TestRunOptions
{
    /// <summary>Texts a test's name must contain one of (ignoring case); empty runs every test (<c>CRAFT_FILTER</c>, split at "|").</summary>
    public IReadOnlyList<string> Filters { get; init; } = [];

    /// <summary>Only the FAIL, HANG and NOT RUN lines and the summary.</summary>
    public bool FailuresOnly { get; init; }

    /// <summary>A file the header, the failures and the summary are written to as they happen (its folder made); null for none.</summary>
    public string? FailuresFile { get; init; }

    /// <summary>Draw a live progress line on the error output.</summary>
    public bool Progress { get; init; }

    /// <summary>Seconds a test may run before it is reported as hung (<c>CRAFT_TIMEOUT</c>, default 300).</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>The whole exception (its stack) in a FAIL line (<c>CRAFT_TRACE=1</c>).</summary>
    public bool FullErrors { get; init; }
}

/// <summary>The tests found in an assembly, and a run over them with Idrak's runner's lines and flags.</summary>
public static class TestRunner
{
    /// <summary>Every test of <paramref name="assembly"/>, ordered by name.</summary>
    public static IReadOnlyList<TestCase> Discover(Assembly assembly)
    {
        var tests = new List<TestCase>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<TestGroupAttribute>() is not { } group)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<TestAttribute>() is null)
                {
                    continue;
                }

                if (method.GetParameters().Length != 0)
                {
                    throw new InvalidOperationException($"Test {type.Name}.{method.Name} takes parameters; tests take none.");
                }

                var name = $"{group.Name}.{method.Name}";
                tests.Add(new TestCase(name, method.ReturnType == typeof(Task)
                    ? () => (Task)method.Invoke(null, null)!
                    : () =>
                    {
                        method.Invoke(null, null);
                        return Task.CompletedTask;
                    }));
            }
        }

        tests.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return tests;
    }

    /// <summary>Whether <paramref name="name"/> passes <paramref name="filters"/> (any of them, ignoring case; none passes all).</summary>
    public static bool Matches(string name, IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
        {
            return true;
        }

        foreach (var filter in filters)
        {
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Splits a <c>CRAFT_FILTER</c> value at "|", dropping empty parts.</summary>
    public static IReadOnlyList<string> SplitFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Runs the tests that pass the filters; returns the exit code (0 all passed, 1 a failure, 3 a hang).</summary>
    public static int Run(IReadOnlyList<TestCase> all, TestRunOptions options, TextWriter output)
    {
        var tests = all.Where(t => Matches(t.Name, options.Filters)).ToList();
        StreamWriter? failures = null;
        if (options.FailuresFile is { } path)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            failures = new StreamWriter(full, append: false) { AutoFlush = true };
        }

        var header = $"Craft tests: {tests.Count} of {all.Count}" +
                     (options.Filters.Count > 0 ? $" (filter: {string.Join("|", options.Filters)})" : "") +
                     $", .NET {Environment.Version}, {Environment.OSVersion.VersionString}";
        Line(header, failure: true);

        int passed = 0, failed = 0, notRunHere = 0;
        var start = Stopwatch.GetTimestamp();
        try
        {
            for (int i = 0; i < tests.Count; i++)
            {
                var test = tests[i];
                DrawProgress(i, test.Name);
                var testStart = Stopwatch.GetTimestamp();
                var task = Task.Run(test.Run);
                bool finished;
                try
                {
                    finished = task.Wait(TimeSpan.FromSeconds(options.TimeoutSeconds));
                }
                catch (AggregateException)
                {
                    finished = true;
                }

                var elapsed = Stopwatch.GetElapsedTime(testStart);
                if (!finished)
                {
                    ClearProgress();
                    Line($"HANG {test.Name}: still running after {options.TimeoutSeconds} s", failure: true);
                    Line(Summary(passed, failed, notRunHere, Stopwatch.GetElapsedTime(start)) + $", hung: {test.Name}", failure: true);
                    return 3;
                }

                var error = task.Exception?.InnerException is TargetInvocationException { InnerException: { } inner } ? inner : task.Exception?.InnerException;
                if (error is null)
                {
                    passed++;
                    if (!options.FailuresOnly)
                    {
                        ClearProgress();
                        Line($"PASS {test.Name} ({elapsed.TotalMilliseconds:F0} ms)", failure: false);
                    }
                }
                else if (error is NotRunHereException notHere)
                {
                    notRunHere++;
                    ClearProgress();
                    Line($"NOT RUN {test.Name}: {notHere.Message}", failure: true);
                }
                else
                {
                    failed++;
                    ClearProgress();
                    Line($"FAIL {test.Name}: {(options.FullErrors ? error.ToString() : $"{error.GetType().Name}: {error.Message}")}", failure: true);
                }
            }
        }
        finally
        {
            ClearProgress();
            Line(Summary(passed, failed, notRunHere, Stopwatch.GetElapsedTime(start)), failure: true);
            failures?.Dispose();
        }

        return failed == 0 ? 0 : 1;

        void Line(string text, bool failure)
        {
            output.WriteLine(text);
            if (failure)
            {
                failures?.WriteLine(text);
            }
        }

        void DrawProgress(int index, string name)
        {
            if (options.Progress)
            {
                var line = $"[{index + 1}/{tests.Count}] {passed} passed, {failed} failed · {name}";
                var width = Console.IsErrorRedirected ? 120 : Math.Max(20, ConsoleWidth() - 1);
                Console.Error.Write("\r" + (line.Length > width ? line[..width] : line.PadRight(width)));
            }
        }

        void ClearProgress()
        {
            if (options.Progress)
            {
                var width = Console.IsErrorRedirected ? 120 : Math.Max(20, ConsoleWidth() - 1);
                Console.Error.Write("\r" + new string(' ', width) + "\r");
            }
        }
    }

    private static int ConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            return 120;
        }
    }

    private static string Summary(int passed, int failed, int notRunHere, TimeSpan elapsed) =>
        $"{passed} passed, {failed} failed" + (notRunHere > 0 ? $", {notRunHere} not run here" : "") + $" in {elapsed.TotalSeconds:F1} s";

    /// <summary>
    /// The repository's root (the folder holding Craft.slnx), found above the program's folder, or from
    /// <c>CRAFT_TEST_REPOSITORY</c>; throws <see cref="NotRunHereException"/> naming <paramref name="what"/> when there is none.
    /// </summary>
    public static string RepositoryPath(params string[] what)
    {
        var root = Environment.GetEnvironmentVariable("CRAFT_TEST_REPOSITORY");
        if (string.IsNullOrEmpty(root))
        {
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "Craft.slnx")))
                {
                    root = folder.FullName;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, "Craft.slnx")))
        {
            throw new NotRunHereException($"needs the repository ({string.Join('/', what)}); set CRAFT_TEST_REPOSITORY to its folder");
        }

        return Path.Combine([root, .. what]);
    }
}
