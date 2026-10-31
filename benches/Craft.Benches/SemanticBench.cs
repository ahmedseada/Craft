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
        await GeneratorsAsync(results).ConfigureAwait(false);
        foreach (int lines in (int[])[1_000, 10_000])
        {
            await DocumentAsync(results, lines).ConfigureAwait(false);
        }


        await MemoryAsync(results).ConfigureAwait(false);

        Report("semantic.txt", results);
    }

    private static string RuntimeInfo() => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>The csproj reader and the Roslyn workspace of src/Craft.Editor, then its compilation's diagnostics (the reader's proof).</summary>
    private static async Task WorkspaceAsync(StringBuilder results)
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Craft.Editor");
        var settings = LanguageSettings.Default;
        long heap = await HeapAsync().ConfigureAwait(false);
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
        var (errors, warnings, first, _) = await DiagnoseAllAsync(solution).ConfigureAwait(false);

        double diagnose = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long held = await HeapAsync().ConfigureAwait(false) - heap;
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
        long after = await HeapAsync().ConfigureAwait(false) - heap;
        results.Append(CultureInfo.InvariantCulture, $"  after both workspaces are left (released): {after / 1024.0 / 1024:F1} MB still held over the start\n");
    }

    /// <summary>
    /// Semantic colours of 60 visible lines in the middle of the file: the first pass (cold) and warm passes after a
    /// keystroke, on the thread pool; and the UI thread's Classify with the colours merged against the syntactic one alone.
    /// </summary>
    private static async Task ColorsAsync(StringBuilder results, PieceTableBuffer buffer)
    {
        var folder = Path.Combine(OutputFolder(), "semantic-tmp");
        var settings = LanguageSettings.Default with { DiagnosticsDelayMilliseconds = 60_000, SemanticMaxCharacters = 0, SemanticColorsDelayMilliseconds = 0 };
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(Path.Combine(folder, "Colors.cs"), folder, settings));
        await session.WhenIdle().ConfigureAwait(false);
        var snapshot = buffer.Current;
        int firstLine = snapshot.LineCount / 2;
        int start = snapshot.GetLine(firstLine).Start;
        int length = snapshot.GetLine(firstLine + 59).End - start;
        using var spans = new TokenSpanBuffer();
        long begin = Stopwatch.GetTimestamp();
        session.Classify(snapshot, start, length, spans);
        await session.WhenColorsIdle().ConfigureAwait(false);
        double cold = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        var pass = new double[WarmRuns];
        var merged = new double[WarmRuns * 20];
        var plain = new double[WarmRuns * 20];
        int edit = snapshot.GetLine(firstLine + 30).Start;
        for (int i = 0; i < WarmRuns; i++)
        {
            buffer.Apply([TextEdit.Insert(edit, " ")]);
            buffer.Apply([TextEdit.Delete(edit, 1)]);
            await session.WhenIdle().ConfigureAwait(false);
            spans.Clear();
            session.Classify(buffer.Current, start, length, spans);
            await session.WhenColorsIdle().ConfigureAwait(false);
            pass[i] = session.LastColorsTime.TotalMilliseconds;
            for (int j = 0; j < 20; j++)
            {
                spans.Clear();
                long t = Stopwatch.GetTimestamp();
                session.Classify(buffer.Current, start, length, spans);
                merged[(i * 20) + j] = Stopwatch.GetElapsedTime(t).TotalMilliseconds * 1000;
            }
        }

        using var syntactic = new CSharpLanguage().Open(buffer);
        await syntactic.WhenIdle().ConfigureAwait(false);
        for (int j = 0; j < plain.Length; j++)
        {
            spans.Clear();
            long t = Stopwatch.GetTimestamp();
            syntactic.Classify(buffer.Current, start, length, spans);
            plain[j] = Stopwatch.GetElapsedTime(t).TotalMilliseconds * 1000;
        }

        results.Append(CultureInfo.InvariantCulture, $"  semantic colours of 60 lines (and a screen either side): first pass {F(cold)} ms, warm after a keystroke {F(Median(pass))} ms median, {F(P99(pass))} ms max (thread pool); the UI thread's Classify with them merged {F(Median(merged))} µs median, syntactic alone {F(Median(plain))} µs ({spans.Count} spans)\n");
    }

    /// <summary>
    /// Every project's compilation and its errors, warnings (the first five errors described) and generated documents. A
    /// method of its own, so no local of its state machine (a project, a compilation) outlives it and holds the solution
    /// while the caller measures the heap.
    /// </summary>
    private static async Task<(int Errors, int Warnings, List<string> First, int Generated)> DiagnoseAllAsync(Solution solution)
    {
        int errors = 0;
        int warnings = 0;
        int generated = 0;
        var first = new List<string>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            generated += (await project.GetSourceGeneratedDocumentsAsync().ConfigureAwait(false)).Count();
            foreach (var diagnostic in compilation!.GetDiagnostics())
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

        return (errors, warnings, first, generated);
    }

    /// <summary>One configuration of the generators bench, in a method of its own (nothing of it outlives it).</summary>
    private static async Task<string> GeneratorsOnceAsync(string root, bool generators)
    {
        CSharpWorkspace.ReleaseAll();
        long heap = await HeapAsync().ConfigureAwait(false);
        var workspace = CSharpWorkspace.Enter(root, LanguageSettings.Default with { SourceGenerators = generators });
        var solution = await workspace.BuildAsync(null, CancellationToken.None).ConfigureAwait(false);
        long start = Stopwatch.GetTimestamp();
        var (errors, _, _, generated) = await DiagnoseAllAsync(solution).ConfigureAwait(false);
        double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long held = await HeapAsync().ConfigureAwait(false) - heap;
        // Warm: a one-character change in Idrak.Abstraction (whose partials the generators complete), then every compilation again.
        var idrak = solution.Projects.First(p => p.Name == "Idrak.Abstraction");
        var document = idrak.Documents.FirstOrDefault(d => d.FilePath is { } f && f.EndsWith("PowerSource.cs", StringComparison.Ordinal)) ?? idrak.Documents.First();
        var text = await document.GetTextAsync().ConfigureAwait(false);
        var changed = solution.WithDocumentText(document.Id, text.WithChanges(new Microsoft.CodeAnalysis.Text.TextChange(new Microsoft.CodeAnalysis.Text.TextSpan(0, 0), " ")));
        start = Stopwatch.GetTimestamp();
        await DiagnoseAllAsync(changed).ConfigureAwait(false);
        double warm = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        workspace.Leave(workspace);
        return string.Create(CultureInfo.InvariantCulture, $"  {(generators ? "with" : "without")} generators: every compilation and its diagnostics {F(cold)} ms, {errors} errors, {generated} generated documents, {held / 1024.0 / 1024:F1} MB held; after a one-character change in {Path.GetFileName(document.FilePath)}, every compilation and its diagnostics again {F(warm)} ms\n");
    }

    /// <summary>Every project's compilation and diagnostics with and without the source generators, cold and warm, and their memory.</summary>
    private static async Task GeneratorsAsync(StringBuilder results)
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Craft.Editor");
        results.Append("source generators (every project of src/Craft.Editor's workspace, each compiled and diagnosed; analyzers' generators only):\n");
        foreach (bool generators in (bool[])[false, true, true])
        {
            results.Append(await GeneratorsOnceAsync(root, generators).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// The managed heap after full collections, measured on a thread-pool thread once the caller's stack unwound: with
    /// ConfigureAwait(false) a continuation runs inline under the frames that completed it, and a solution in one of their
    /// slots stays live to the collector, which counted a released workspace as held (the "56 MB after a release" of the
    /// first E5 bench).
    /// </summary>
    private static Task<long> HeapAsync() => Task.Run(static () => GC.GetTotalMemory(forceFullCollection: true));

    /// <summary>The live managed heap after a full compacting collection (the large-object heap compacted too, finalizers run): its size less its free space.</summary>
    private static long Live()
    {
        for (int i = 0; i < 3; i++)
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    /// <summary>What a workspace's memory is made of, and what stays after it is released: Roslyn's MEF host, the workspace, the caches.</summary>
    /// <summary>
    /// Builds the workspace (and compiles and diagnoses every project) in a frame of its own: a solution held in a stack
    /// slot of the measuring method stays live to the collector until that frame returns, which would count it as held.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task BuildAndDiagnoseAsync(CSharpWorkspace workspace, bool diagnose = true)
    {
        var solution = await workspace.BuildAsync(null, CancellationToken.None).ConfigureAwait(false);
        if (diagnose)
        {
            await DiagnoseAllAsync(solution).ConfigureAwait(false);
        }
    }

    private static async Task MemoryAsync(StringBuilder results)
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Craft.Editor");
        CSharpWorkspace.ReleaseAll();
        var stages = new List<(string What, long Total, long Live)>();
        // Each stage is measured on another thread after this one's stack unwound: a continuation run inline keeps the
        // frames that completed it (and the solution in their slots) on the stack beneath it.
        async Task Stage(string what) => stages.Add(await Task.Run(() => (what, GC.GetTotalMemory(forceFullCollection: true), Live())).ConfigureAwait(false));
        await Stage("start (nothing held)").ConfigureAwait(false);
        var workspace = CSharpWorkspace.Enter(root, LanguageSettings.Default);
        await BuildAndDiagnoseAsync(workspace).ConfigureAwait(false);
        await Stage("src/Craft.Editor's workspace built, every project compiled and diagnosed (generators on)").ConfigureAwait(false);
        workspace.Release();
        await Stage("the workspace and the shared references released (the MEF host kept, as the memory-pressure call does)").ConfigureAwait(false);
        CSharpWorkspace.ReleaseHost();
        await Stage("the MEF host released too").ConfigureAwait(false);
        long start = Stopwatch.GetTimestamp();
        await BuildAndDiagnoseAsync(workspace, diagnose: false).ConfigureAwait(false);
        double rebuilt = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        await Stage($"built again (the MEF host composed again, warm: {F(rebuilt)} ms)").ConfigureAwait(false);
        workspace.Leave(workspace);
        CSharpWorkspace.ReleaseAll();
        await Stage("all released again").ConfigureAwait(false);
        results.Append("memory, over the start (GC.GetTotalMemory, and the live heap after a compacting collection), each measured after the stack unwound:\n");
        foreach (var (what, total, live) in stages)
        {
            results.Append(CultureInfo.InvariantCulture, $"  {what}: {(total - stages[0].Total) / 1024.0 / 1024:F1} MB total, {(live - stages[0].Live) / 1024.0 / 1024:F1} MB live (live heap {live / 1024.0 / 1024:F1} MB)\n");
        }
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
        long heap = await HeapAsync().ConfigureAwait(false);
        var buffer = new PieceTableBuffer(text);
        text = null;
        long start = Stopwatch.GetTimestamp();
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(path, folder, settings));
        await session.WhenDiagnosticsIdle().ConfigureAwait(false);
        double first = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var firstSet = session.Diagnostics;
        long held = await HeapAsync().ConfigureAwait(false) - heap;
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
        await ColorsAsync(results, buffer).ConfigureAwait(false);
        var telemetry = session.Telemetry;
        results.Append(CultureInfo.InvariantCulture, $"  workspace: {telemetry.Projects} project, {telemetry.Documents} documents, {telemetry.References} references ({telemetry.ReferenceBytes / 1024.0 / 1024:F1} MB of files), build {F(telemetry.BuildTime.TotalMilliseconds)} ms\n");
        await session.WhenDiagnosticsIdle().ConfigureAwait(false);
    }
}
