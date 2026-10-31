// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Craft.Plugins.Learning;

/// <summary>A test of the workspace that a task is made from: its full name and why it failed (or that the user marked it).</summary>
/// <param name="Name">The test's full name ("Lib.Tests.CalcTests.Adds").</param>
/// <param name="Message">Its failure, or why it was chosen.</param>
public sealed record WorkspaceTest(string Name, string Message);

/// <summary>
/// The workspace's tests as coding tasks (plan 1, phase R, "Tasks from the workspace"): the tests that fail
/// (<c>dotnet test</c> run through a registered command sandbox, its TRX results read) or that the user marked become
/// tasks in Idrak's <c>task.json</c> form, each over the workspace as its base, with no commands of its own so the C#
/// environment's defaults check it (restore, build, then <c>dotnet test</c>, whose share of passed tests is
/// <c>csharp-tests</c>; the test projects are protected, so a task is solved by the code, not by the test). Written so
/// <c>idrak reinforce --env csharp --env-option suite=FOLDER</c> reads the same suite.
/// </summary>
public static class WorkspaceTasks
{
    /// <summary>
    /// The tests of <paramref name="workspace"/> that fail now: <c>dotnet test</c> run in it through the sandbox
    /// <paramref name="sandbox"/> (Idrak's CommandSandboxes; "approve-each" asks the user first), its TRX results written to
    /// <paramref name="results"/> and read. Also gives the command's result, for the message when it did not run.
    /// </summary>
    /// <exception cref="NotRegisteredException">No sandbox of that name is registered.</exception>
    public static async Task<(IReadOnlyList<WorkspaceTest> Failing, CommandResult Run)> FindFailingAsync(string workspace, string sandbox, string results, TimeSpan time,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(results);
        var runner = CommandSandboxes.Get(sandbox);
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        Directory.CreateDirectory(results);
        var limits = new CommandLimits { Time = time, Programs = ["dotnet"], MaxOutputCharacters = 20_000 };
        var run = await runner.RunAsync(["dotnet", "test", "--logger", "trx", "--results-directory", results], Path.GetFullPath(workspace), limits, cancellationToken)
            .ConfigureAwait(false);
        return (Failed(results), run);
    }

    /// <summary>The failed tests of the TRX files under <paramref name="folder"/>, in name order, each once.</summary>
    public static IReadOnlyList<WorkspaceTest> Failed(string folder)
    {
        var failed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.trx", SearchOption.AllDirectories))
        {
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });
                string? current = null;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.LocalName == "UnitTestResult")
                    {
                        current = string.Equals(reader.GetAttribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase) ? reader.GetAttribute("testName") : null;
                        if (current is not null)
                        {
                            failed.TryAdd(current, "it fails");
                        }
                    }
                    else if (reader.LocalName == "Message" && current is not null)
                    {
                        failed[current] = reader.ReadElementContentAsString().Trim();
                        current = null;
                    }
                }
            }
            catch (XmlException)
            {
                // A results file cut short (a run stopped) counts no test.
            }
        }

        return [.. failed.Select(p => new WorkspaceTest(p.Key, p.Value))];
    }

    /// <summary>
    /// Writes a task for each of <paramref name="tests"/> into <paramref name="suite"/> (a folder per test, its
    /// <c>task.json</c> over <paramref name="workspace"/> as the base); the task ids. A suite written before is replaced.
    /// </summary>
    public static IReadOnlyList<string> Write(string suite, string workspace, IReadOnlyList<WorkspaceTest> tests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suite);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(tests);
        if (Directory.Exists(suite))
        {
            Directory.Delete(suite, recursive: true);
        }

        var ids = new List<string>();
        foreach (var test in tests)
        {
            string id = Safe(test.Name);
            string folder = Path.Combine(suite, id);
            Directory.CreateDirectory(folder);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("prompt", string.Create(CultureInfo.InvariantCulture,
                    $"The test {test.Name} fails. Change the code, not the tests, so that it passes and the other tests still pass.\n\nWhy it fails:\n{test.Message}"));
                writer.WriteString("language", "csharp");
                writer.WriteString("base", Path.GetRelativePath(folder, Path.GetFullPath(workspace)).Replace('\\', '/'));
                writer.WriteStartArray("tags");
                writer.WriteStringValue("workspace-test");
                writer.WriteStringValue(test.Name);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.WriteAllBytes(Path.Combine(folder, "task.json"), stream.ToArray());
            ids.Add(id);
        }

        return ids;
    }

    private static string Safe(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            text.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return text.Length == 0 ? "test" : text.ToString();
    }
}
