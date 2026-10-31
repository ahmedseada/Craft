// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>The crash reports the app's crash dialog writes and names.</summary>
[TestGroup("CrashReports")]
public static class CrashReportTests
{
    [Test]
    public static void AReportHoldsTheExceptionItsCauseAndWhereItWasCaught()
    {
        using var folder = new TempFolder();
        Exception error;
        try
        {
            throw new InvalidOperationException("outer", new ArgumentException("the cause"));
        }
        catch (InvalidOperationException e)
        {
            error = e;
        }

        var path = CrashReports.Write(new CraftOptions { CrashFolder = folder.Path }, error, "the window", "WinUI message: extra");
        Check.True(path is not null && File.Exists(path), "the report was written to CRAFT_CRASH_FOLDER");
        Check.True(Path.GetDirectoryName(path) == Path.GetFullPath(folder.Path), "the report is in the crash folder");
        var text = File.ReadAllText(path!);
        foreach (var expected in (string[])["Caught by: the window", "WinUI message: extra", "InvalidOperationException: outer", "ArgumentException: the cause", nameof(AReportHoldsTheExceptionItsCauseAndWhereItWasCaught)])
        {
            Check.True(text.Contains(expected, StringComparison.Ordinal), $"the report holds '{expected}'");
        }
    }

    [Test]
    public static void AFolderThatCannotBeWrittenGivesNoPathAndNoThrow()
    {
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "a-file");
        File.WriteAllText(file, "");
        Check.True(CrashReports.Write(new CraftOptions { CrashFolder = Path.Combine(file, "below") }, new InvalidOperationException("x"), "test") is null,
            "a crash folder under a file: no path, no exception");
    }
}
