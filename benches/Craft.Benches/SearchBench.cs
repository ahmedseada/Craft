// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using Craft.Plugins.Search;
using Idrak.Text.Abstractions;
using static Craft.Benches.BenchSupport;

namespace Craft.Benches;

/// <summary>
/// Plan phase P6: the code search's index over folders (CRAFT_BENCH_SEARCH_FOLDERS, ';' between them; else Craft's own src
/// and tests): the cold build (every file parsed, the words indexed, written to disk), its size on disk and in chunks, the
/// warm start (the kept index read back and checked against the files: nothing parsed), one changed file brought up to date,
/// and the query latency (the median and p99 of a set of queries, warm) and the managed memory the index holds. Keywords
/// only (code-bm25); CRAFT_PLUGIN_SEARCH_EMBEDDINGMODEL adds the vectors (the encoder's own time then counts).
/// </summary>
internal static class SearchBench
{
    private static readonly string[] Queries = ["parse text", "TextDocument", "undo", "piece table", "MCP server", "inline completion", "propose edit", "ghost text",
        "bracket match", "RegisterParts", "semantic session", "workspace reader", "terminal grid", "theme colors", "quick open", "fuzzy match", "diff hunk", "keyBinding"];

    public static async Task RunAsync()
    {
        KeywordIndexes.Register(CodeKeywordIndexKind.Instance);
        try
        {
            var results = new StringBuilder("P6 code search (keywords: code-bm25" + (Environment.GetEnvironmentVariable("CRAFT_PLUGIN_SEARCH_EMBEDDINGMODEL") is { Length: > 0 } m ? $"; vectors: {m}" : "") + ")\n");
            string root = RepositoryRoot();
            var folders = Environment.GetEnvironmentVariable("CRAFT_BENCH_SEARCH_FOLDERS") is { Length: > 0 } set
                ? set.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [Path.Combine(root, "src"), Path.Combine(root, "tests")];
            foreach (var folder in folders)
            {
                await One(folder, results).ConfigureAwait(false);
            }

            Report("search.txt", results);
        }
        finally
        {
            KeywordIndexes.Unregister(CodeKeywordIndexKind.IndexName);
        }
    }

    private static async Task One(string folder, StringBuilder results)
    {
        var settings = SearchSettings.FromEnvironment();
        string kept = Path.Combine(OutputFolder(), "search-index");
        string file = CodeIndex.FileFor(kept, folder);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        using var cold = new CodeIndex(folder, settings, file);
        var build = await cold.BuildAsync().ConfigureAwait(false);
        long held = GC.GetTotalMemory(forceFullCollection: true) - before;
        long bytes = cold.Files.Sum(f => f.Length);
        results.AppendLine($"{folder}: {build.Files:N0} files ({bytes / 1024.0:N0} KiB of source), {build.Chunks:N0} chunks");
        results.AppendLine($"  cold build {F(build.Elapsed.TotalMilliseconds, 0)} ms; index file {build.Bytes / 1024.0:N0} KiB; managed memory held {held / 1048576.0:F1} MiB");

        using var warm = new CodeIndex(folder, settings, file);
        var watch = Stopwatch.StartNew();
        bool loaded = warm.Load();
        var check = await warm.BuildAsync().ConfigureAwait(false);
        results.AppendLine($"  warm start (read back {loaded}, then checked) {F(watch.Elapsed.TotalMilliseconds, 0)} ms, {check.Parsed} files parsed");

        // One file changed: only it is parsed again; the keyword index is made again over every chunk.
        string changed = Path.Combine(folder, warm.Files[warm.Files.Count / 2].Path.Replace('/', Path.DirectorySeparatorChar));
        var stamp = File.GetLastWriteTimeUtc(changed);
        File.SetLastWriteTimeUtc(changed, stamp.AddSeconds(1));
        try
        {
            var update = await warm.BuildAsync().ConfigureAwait(false);
            results.AppendLine($"  one file changed: {F(update.Elapsed.TotalMilliseconds, 1)} ms ({update.Parsed} parsed, the save included)");
        }
        finally
        {
            File.SetLastWriteTimeUtc(changed, stamp);
        }

        foreach (var query in Queries)
        {
            _ = warm.Query(query, 50);                                      // warm-up
        }

        var times = new double[Queries.Length * 20];
        for (int i = 0; i < times.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = warm.Query(Queries[i % Queries.Length], 50);
            times[i] = Microseconds(start);
        }

        results.AppendLine($"  query (top 50, warm, {times.Length} queries): median {F(Median(times) / 1000, 3)} ms, p99 {F(P99(times) / 1000, 3)} ms");
        var hit = warm.Query("TextDocument", 1);
        results.AppendLine($"  'TextDocument' first: {(hit.Count > 0 ? $"{hit[0].Chunk.Kind} {hit[0].Chunk.FullName} ({hit[0].RelativePath}:{hit[0].Chunk.Line})" : "none")}");
    }
}
