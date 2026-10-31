// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Text;
using Craft.CSharp;
using Microsoft.CodeAnalysis;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// E5's semantic layer, cold and warm apart: the workspace of Craft's own src/Craft.Editor project (read by the "csproj"
/// reader, its project references and restored packages), its compilation's diagnostics; the first and warm diagnostics
/// of a 1,000-line and a 10,000-line file; completion after '.', cold and warm; and the memory each holds.
/// </summary>
internal static class SemanticBench
{
    private const int WarmRuns = 15;

    public static async Task RunAsync()
    {
        CSharpLanguage.Register();
        var results = new StringBuilder();
        results.Append(CultureInfo.InvariantCulture, $"Craft semantic bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, .NET {Environment.Version}, {Environment.ProcessorCount} cores, {RuntimeInfo()}\n");
        await WorkspaceAsync(results).ConfigureAwait(false);
        foreach (int lines in (int[])[1_000, 10_000])
        {
            await DocumentAsync(results, lines).ConfigureAwait(false);
        }

        Report("semantic.txt", results);
    }

    private static string RuntimeInfo() => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>The csproj reader and the Roslyn workspace of src/Craft.Editor, then its compilation's diagnostics (the reader's proof).</summary>
    private static async Task WorkspaceAsync(StringBuilder results)
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Craft.Editor");
        var settings = LanguageSettings.Default;
        long heap = GC.GetTotalMemory(forceFullCollection: true);
        long start = Stopwatch.GetTimestamp();
        var workspace = CSharpWorkspace.Enter(root, settings);
        Solution? solution = await workspace.BuildAsync(null, CancellationToken.None).ConfigureAwait(false);
        double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var telemetry = workspace.Telemetry(TimeSpan.Zero);
        results.Append(CultureInfo.InvariantCulture, $"workspace of src/Craft.Editor (csproj reader): {telemetry.Projects} projects ({string.Join(", ", solution.Projects.Select(p => p.Name))}), {telemetry.Documents} documents, {telemetry.References} references ({telemetry.ReferenceBytes / 1024.0 / 1024:F1} MB of files)\n");
        results.Append(CultureInfo.InvariantCulture, $"  build cold: {F(cold)} ms (reading the projects and the references, composing Roslyn's MEF host once)\n");

        start = Stopwatch.GetTimestamp();
        Project? editor = solution.Projects.First(p => p.Name == "Craft.Editor");
        Compilation? compilation = await editor.GetCompilationAsync().ConfigureAwait(false);
        double compileCold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        int errors = 0;
        int warnings = 0;
        var first = new List<string>();
        foreach (var project in solution.Projects)
        {
            var projectCompilation = await project.GetCompilationAsync().ConfigureAwait(false);
            foreach (var diagnostic in projectCompilation!.GetDiagnostics())
            {
                if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                {
                    errors++;
                    if (first.Count < 5)
                    {
                        first.Add($"{project.Name}: {diagnostic.Id} {diagnostic.GetMessage(CultureInfo.InvariantCulture)} at {diagnostic.Location.GetLineSpan()}");
                    }
                }
                else if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                {
                    warnings++;
                }
            }
        }

        double diagnose = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long held = GC.GetTotalMemory(forceFullCollection: true) - heap;
        results.Append(CultureInfo.InvariantCulture, $"  Craft.Editor's compilation cold: {F(compileCold)} ms ({compilation!.SyntaxTrees.Count()} trees); every project's diagnostics: {F(diagnose)} ms, {errors} errors, {warnings} warnings{(first.Count > 0 ? " — " + string.Join(" | ", first) : "")}\n");
        results.Append(CultureInfo.InvariantCulture, $"  memory held with the compilations: {held / 1024.0 / 1024:F1} MB of managed heap (the references' metadata included)\n");

        // Warm: a release drops the solution and the shared references; the next build reads them again (methods JIT-ed).
        workspace.Release();
        start = Stopwatch.GetTimestamp();
        solution = await workspace.BuildAsync(null, CancellationToken.None).ConfigureAwait(false);
        double rebuilt = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        // Warm with the references still read: a second workspace (another configuration) of the same folder.
        var other = CSharpWorkspace.Enter(root, settings with { Configuration = "Release" });
        start = Stopwatch.GetTimestamp();
        await other.BuildAsync(null, CancellationToken.None).ConfigureAwait(false);
        double shared = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        await solution.Projects.First(p => p.Name == "Craft.Editor").GetCompilationAsync().ConfigureAwait(false);
        double compileWarm = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        results.Append(CultureInfo.InvariantCulture, $"  build warm after a release (references read again): {F(rebuilt)} ms; another workspace with the references shared: {F(shared)} ms; Craft.Editor's compilation warm: {F(compileWarm)} ms\n");
        other.Leave(other);
        workspace.Leave(workspace);
        solution = null;
        compilation = null;
        editor = null;
        long after = GC.GetTotalMemory(forceFullCollection: true) - heap;
        results.Append(CultureInfo.InvariantCulture, $"  after both workspaces are left (released): {after / 1024.0 / 1024:F1} MB still held over the start\n");
    }

    /// <summary>A loose generated file of <paramref name="lines"/> lines: first and warm diagnostics, completion cold and warm, memory.</summary>
    private static async Task DocumentAsync(StringBuilder results, int lines)
    {
        var folder = Path.Combine(OutputFolder(), "semantic-tmp");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Generated{lines}.cs");
        var text = GenerateCSharp(lines);
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        var settings = LanguageSettings.Default with { DiagnosticsDelayMilliseconds = 0, SemanticMaxCharacters = 0 };
        long heap = GC.GetTotalMemory(forceFullCollection: true);
        var buffer = new PieceTableBuffer(text);
        text = null;
        long start = Stopwatch.GetTimestamp();
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(path, folder, settings));
        await session.WhenDiagnosticsIdle().ConfigureAwait(false);
        double first = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var firstSet = session.Diagnostics;
        long held = GC.GetTotalMemory(forceFullCollection: true) - heap;
        results.Append(CultureInfo.InvariantCulture, $"file of {buffer.Current.LineCount:N0} lines ({buffer.Current.Length:N0} chars), a loose document: first diagnostics {F(first)} ms (the loose workspace, the parse, the compilation; {firstSet.Errors} errors, {firstSet.Warnings} warnings); memory then {held / 1024.0 / 1024:F1} MB = {F((double)held / (buffer.Current.Length * 2L), 1)} x the text\n");

        // Warm: a keystroke in a method body, then the pass it causes (the pause is 0 here).
        var snapshot = buffer.Current;
        var body = snapshot.GetText(0, snapshot.Length).IndexOf("_items.Add(item);", snapshot.Length / 2, StringComparison.Ordinal);
        var wall = new double[WarmRuns];
        var pass = new double[WarmRuns];
        for (int i = 0; i < WarmRuns; i++)
        {
            start = Stopwatch.GetTimestamp();
            buffer.Apply([TextEdit.Insert(body, i % 2 == 0 ? " " : "")]);
            if (i % 2 == 1)
            {
                buffer.Apply([TextEdit.Delete(body, 1)]);
            }

            await session.WhenDiagnosticsIdle().ConfigureAwait(false);
            wall[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            pass[i] = session.Telemetry.LastDiagnosticsTime.TotalMilliseconds;
        }

        results.Append(CultureInfo.InvariantCulture, $"  warm diagnostics after a keystroke: {F(Median(wall))} ms median, {F(P99(wall))} ms max (the pass itself {F(Median(pass))} ms median)\n");

        // Completion after "_items." typed on a new line of a method body: the first request, then warm ones, each after a keystroke.
        snapshot = buffer.Current;
        int line = snapshot.GetLineIndex(body);
        int at = snapshot.GetLine(line).End;
        const string Typed = "\r\n            _items.";
        buffer.Apply([TextEdit.Insert(at, Typed)]);
        int dot = at + Typed.Length;
        start = Stopwatch.GetTimestamp();
        var list = await session.GetCompletionsAsync(buffer.Current, dot, CompletionTrigger.Typed('.'), CancellationToken.None).ConfigureAwait(false);
        double completionCold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var warm = new double[WarmRuns];
        for (int i = 0; i < WarmRuns; i++)
        {
            buffer.Apply([TextEdit.Insert(dot, "A")]);
            buffer.Apply([TextEdit.Delete(dot, 1)]);
            start = Stopwatch.GetTimestamp();
            list = await session.GetCompletionsAsync(buffer.Current, dot, CompletionTrigger.Typed('.'), CancellationToken.None).ConfigureAwait(false);
            warm[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        results.Append(CultureInfo.InvariantCulture, $"  completion after '.': first {F(completionCold)} ms, warm {F(Median(warm))} ms median, {F(P99(warm))} ms max ({list?.Items.Count ?? 0} items), each keystroke's diagnostics pass running beside it\n");

        // The same with no diagnostics pass beside it: a session that diagnoses only when asked, on the same buffer.
        await session.WhenDiagnosticsIdle().ConfigureAwait(false);
        using (var quiet = new CSharpLanguage().Open(buffer))
        {
            list = await quiet.GetCompletionsAsync(buffer.Current, dot, CompletionTrigger.Typed('.'), CancellationToken.None).ConfigureAwait(false);
            for (int i = 0; i < WarmRuns; i++)
            {
                buffer.Apply([TextEdit.Insert(dot, "A")]);
                buffer.Apply([TextEdit.Delete(dot, 1)]);
                await session.WhenDiagnosticsIdle().ConfigureAwait(false);
                start = Stopwatch.GetTimestamp();
                list = await quiet.GetCompletionsAsync(buffer.Current, dot, CompletionTrigger.Typed('.'), CancellationToken.None).ConfigureAwait(false);
                warm[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            results.Append(CultureInfo.InvariantCulture, $"  completion after '.', warm, nothing else running: {F(Median(warm))} ms median, {F(P99(warm))} ms max\n");
        }
        var telemetry = session.Telemetry;
        results.Append(CultureInfo.InvariantCulture, $"  workspace: {telemetry.Projects} project, {telemetry.Documents} documents, {telemetry.References} references ({telemetry.ReferenceBytes / 1024.0 / 1024:F1} MB of files), build {F(telemetry.BuildTime.TotalMilliseconds)} ms\n");
        await session.WhenDiagnosticsIdle().ConfigureAwait(false);
    }
}
