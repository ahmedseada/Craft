// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Tests.Runner;

// A plug-in folder probed in this fresh process (the Voice group's from-a-folder check), before any test type is loaded.
if (args is ["--probe-plugin", var probed, ..])
{
    return Craft.Tests.PluginProbe.Run(probed);
}

// Flags (as Idrak's runner): --progress, --failures-only, --failures-file PATH, --filter TEXT (or CRAFT_FILTER), --list.
var filters = new List<string>(TestRunner.SplitFilter(Environment.GetEnvironmentVariable("CRAFT_FILTER")));
bool progress = false, failuresOnly = false, list = false;
string? failuresFile = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--progress":
            progress = true;
            break;
        case "--failures-only":
            failuresOnly = true;
            break;
        case "--failures-file" when i + 1 < args.Length:
            failuresFile = args[++i];
            break;
        case "--filter" or "-filter" when i + 1 < args.Length:
            filters.AddRange(TestRunner.SplitFilter(args[++i]));
            break;
        case "--list":
            list = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'. Options: --progress, --failures-only, --failures-file PATH, --filter TEXT, --list.");
            return 2;
    }
}

var all = TestRunner.Discover(typeof(Program).Assembly);
if (list)
{
    foreach (var test in all.Where(t => TestRunner.Matches(t.Name, filters)))
    {
        Console.WriteLine(test.Name);
    }

    return 0;
}

int timeout = int.TryParse(Environment.GetEnvironmentVariable("CRAFT_TIMEOUT"), out var seconds) && seconds > 0 ? seconds : 300;
return TestRunner.Run(all, new TestRunOptions
{
    Filters = filters,
    Progress = progress,
    FailuresOnly = failuresOnly,
    FailuresFile = failuresFile,
    TimeoutSeconds = timeout,
    FullErrors = Environment.GetEnvironmentVariable("CRAFT_TRACE") == "1",
}, Console.Out);

/// <summary>The test program (its assembly holds the tests).</summary>
public partial class Program;
