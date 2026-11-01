// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Search;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 6, the Search view: the "text" provider's results equal a plain scan (text, case, whole word, regular
/// expressions, encodings, CRLF), the include and exclude globs, the ignored folders and the <c>.gitignore</c> files, open
/// documents searched in their current text, the next keystroke cancelling a running search, Replace all in files as one
/// review (open documents one undo step each) then Undo, 0 bytes per line searched warm, and the cold and warm times on a
/// fixture of Craft's source size.
/// </summary>
[TestGroup("WorkspaceSearch")]
public static class WorkspaceSearchTests
{
    private static readonly string[] Words = ["alpha", "Beta", "gamma", "delta", "Search", "search", "searching", "var", "int", "return", "Foo", "foo_bar", "x1", "café", "日本"];

    [Test]
    public static async Task TheTextProvidersResultsEqualAPlainScan()
    {
        using var folder = new TempFolder();
        Fixture(folder.Path, files: 40, lines: 120, seed: 7);
        // Encodings and line breaks the scan must read as the editor reads them.
        Disk.Write(Path.Combine(folder.Path, "bom.cs"), "// search\r\nint search = 1;\r\nSearch(search);\r\n", new UTF8Encoding(true));
        Disk.Write(Path.Combine(folder.Path, "wide.txt"), "a search in UTF-16\nsearch again\n", Encoding.Unicode);
        File.WriteAllBytes(Path.Combine(folder.Path, "image.bin"), [0x89, 0x50, 0x00, 0x00, .. "search"u8]);
        Disk.Write(Path.Combine(folder.Path, "long.min.js"), string.Concat(Enumerable.Repeat("var a=search;", 400)));

        var cases = new (WorkspaceSearchQuery Query, string What)[]
        {
            (new("search"), "text, case ignored"),
            (new("Search", MatchCase: true), "text, case matched (found in the bytes first)"),
            (new("search", WholeWord: true), "whole word"),
            (new(@"sea\w+ing|\bx\d", Regex: true), "a regular expression"),
            (new("^\\s*int ", Regex: true), "an anchored expression on each line"),
            (new("café"), "a word with a non-ASCII letter"),
        };
        foreach (var (query, what) in cases)
        {
            var found = await SearchAsync(folder.Path, query);
            var expected = PlainScan(folder.Path, query);
            Check.SequenceEqual(expected, found.Hits, $"{what}: the provider's matches are a plain scan's ({expected.Count} expected)");
            Check.True(found.Summary.SkippedBinary == 1, $"{what}: the binary file is passed over and counted ({found.Summary.SkippedBinary})");
            Check.True(found.Previews.All(p => p.Ok), $"{what}: each preview holds its match where it says");
        }

        var invalid = await WorkspaceSearchProviders.Get(WorkspaceSearchProviders.Text).SearchAsync(new WorkspaceSearchRequest(folder.Path, new WorkspaceSearchQuery("(", Regex: true)), _ => { });
        Check.True(invalid.Problem?.Contains("does not parse", StringComparison.Ordinal) == true, "an expression that does not parse says why: " + invalid.Problem);
        var capped = await SearchAsync(folder.Path, new WorkspaceSearchQuery("search"), maxResults: 25);
        Check.True(capped.Hits.Count == 25 && capped.Summary.Capped, $"the search stops at CRAFT_SEARCH_MAX_RESULTS and says so ({capped.Hits.Count})");
        var small = await SearchAsync(folder.Path, new WorkspaceSearchQuery("var a=search"), maxFileBytes: 1000);
        Check.True(small.Hits.Count == 0 && small.Summary.SkippedLarge >= 1, "a file over CRAFT_SEARCH_MAX_FILE_MB is passed over and counted");
        Check.Throws<NotRegisteredException>(() => WorkspaceSearchProviders.Get("nope"), "an unknown provider is not registered");
    }

    [Test]
    public static async Task GlobsTheIgnoredFoldersAndGitIgnoreLeaveFilesOut()
    {
        using var folder = new TempFolder();
        void Write(string relative, string text)
        {
            var path = Path.Combine(folder.Path, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Write("src/App/Program.cs", "needle");
        Write("src/App/Generated/Model.g.cs", "needle");
        Write("src/App/notes.md", "needle");
        Write("bin/Debug/App.dll.txt", "needle");
        Write("obj/project.assets.json", "needle");
        Write("logs/run.log", "needle");
        Write("logs/keep.log", "needle");
        Write("secret/key.txt", "needle");
        Write("tests/sub/.gitignore", "*.tmp\n");
        Write("tests/sub/a.tmp", "needle");
        Write("tests/sub/b.cs", "needle");
        Write(".gitignore", "# the build's\n*.log\n!keep.log\nsecret/\n/src/App/notes.md\n");
        string[] ignore = ["bin", "obj", ".git"];

        static IReadOnlyList<string> Relative(string root, IEnumerable<WorkspaceSearchFile> files) =>
            [.. files.Select(f => WorkspaceFiles.Relative(root, f.Path)).OrderBy(p => p, StringComparer.Ordinal)];

        var all = await SearchAsync(folder.Path, new WorkspaceSearchQuery("needle"), ignore: ignore);
        Check.SequenceEqual(["logs/keep.log", "src/App/Generated/Model.g.cs", "src/App/Program.cs", "tests/sub/b.cs"], Relative(folder.Path, all.Files),
            "bin and obj never entered; .gitignore leaves out *.log (keep.log taken back), secret/, the anchored notes.md and the nested *.tmp");
        var withoutIgnoreFiles = await SearchAsync(folder.Path, new WorkspaceSearchQuery("needle") { UseIgnoreFiles = false }, ignore: ignore);
        Check.Equal(8, withoutIgnoreFiles.Files.Count, "with CRAFT_SEARCH_IGNORE_FILES off only the ignored folders stay out");
        var include = await SearchAsync(folder.Path, new WorkspaceSearchQuery("needle") { Include = "*.cs", Exclude = "**/Generated/**" }, ignore: ignore);
        Check.SequenceEqual(["src/App/Program.cs", "tests/sub/b.cs"], Relative(folder.Path, include.Files), "include *.cs, exclude a folder anywhere");
        var anchored = await SearchAsync(folder.Path, new WorkspaceSearchQuery("needle") { Include = "src/**", Exclude = "Generated" }, ignore: ignore);
        Check.SequenceEqual(["src/App/Program.cs"], Relative(folder.Path, anchored.Files), "a glob with '/' is taken from the root; a bare name excludes that folder anywhere");

        var glob = WorkspaceGlob.Parse("*.{cs,md}, docs/[ab]?.txt")!;
        Check.True(glob.Matches("x/y/Program.cs") && glob.Matches("readme.md") && glob.Matches("docs/a1.txt") && !glob.Matches("docs/c1.txt") && !glob.Matches("x.json"),
            "globs: braces, a name anywhere, a class and one character");
        Check.True(ReferenceEquals(glob, WorkspaceGlob.Parse("*.{cs,md}, docs/[ab]?.txt")) && WorkspaceGlob.Parse("  ") is null, "a glob is made once per text; an empty one is none");
        var rules = GitIgnore.Parse("build/\n*.user\n!keep.user\n/top.txt\n", "", null)!;
        Check.True(rules.Ignores("build", isFolder: true) && !rules.Ignores("build", isFolder: false) && rules.Ignores("a/b.user", false) && !rules.Ignores("a/keep.user", false)
            && rules.Ignores("top.txt", false) && !rules.Ignores("a/top.txt", false), ".gitignore: folders only, negation, anchoring");
    }

    [Test]
    public static async Task TheViewSearchesOpenDocumentsInTheirCurrentTextAndOpensTheResult()
    {
        using var folder = new TempFolder();
        var onDisk = folder.File("Open.cs");
        Disk.Write(onDisk, "class Open { }\n");
        Disk.Write(folder.File("Closed.cs"), "class Closed { int unsaved; }\n");
        var ui = new ConcurrentQueue<Action>();
        var options = new CraftOptions { SearchDelayMilliseconds = 0 };
        using var host = new EditorHost(options, ui.Enqueue);
        using var document = await TextDocument.OpenAsync(onDisk, options);
        var open = host.Open(document, null);
        document.Edit([TextEdit.Insert(11, "string unsaved; ")], EditKind.Typing, SelectionState.At(11), SelectionState.At(27));
        using var view = new WorkspaceSearchView(host, () => folder.Path, () => options);
        host.OpenFile = (path, _) => Task.FromResult<IEditorDocument?>(string.Equals(path, onDisk, StringComparison.Ordinal) ? open : null);
        await view.ActAsync(WorkspaceSearchView.QueryAction, "unsaved");
        await Settle(ui, view);
        Check.Equal(2, view.Results.Count, "both files: the open one in its unsaved text");
        var openResult = view.Results.Single(r => r.Path == onDisk);
        Check.True(openResult.FromOpenDocument && openResult.Hits is [{ Line: 0, Column: 18 }], "the open document's match is where its text has it now, not the disk's");
        var results = view.Blocks.OfType<PanelResults>().Single();
        Check.True(results.Groups is [{ Title: "Closed.cs", Glyph: "C#", Badge: "1" }, { Title: "Open.cs" }], "grouped by file in path order, each with its icon and count");
        Check.True(results.Groups[1].Rows is [{ Prefix: "1", Highlights: [{ Start: 18, Length: 7 }] }], "each line with its match highlighted and its number");
        Check.True(await view.OpenAsync(WorkspaceSearchView.RowId(onDisk, 0)) && open.Selection == new SelectionState(18, 25), "a click opens the place, the match selected");

        await view.ActAsync(WorkspaceSearchView.ToggleAction, onDisk);
        Check.True(view.Blocks.OfType<PanelResults>().Single().Groups[1] is { Collapsed: true, Rows.Count: 0 }, "a file's header folds its rows");
        await view.ActAsync("search.case", "on");
        await view.ActAsync(WorkspaceSearchView.QueryAction, "UNSAVED");
        await Settle(ui, view);
        Check.True(view.Results.Count == 0 && view.Blocks.OfType<PanelQuery>().First().Toggles[0].On, "match case on: no match for the other case");
        await view.ActAsync("search.regex", "on");
        await view.ActAsync(WorkspaceSearchView.QueryAction, "(");
        await Settle(ui, view);
        Check.True(view.Blocks.OfType<PanelText>().Any(t => t.Tone == PanelTone.Error && t.Text.Contains("does not parse", StringComparison.Ordinal)), "an expression that does not parse is said in the view");
    }

    [Test]
    public static async Task TheNextKeystrokeCancelsTheRunningSearch()
    {
        using var folder = new TempFolder();
        Disk.Write(folder.File("a.txt"), "first second\n");
        var slow = new SlowProvider();
        WorkspaceSearchProviders.Register(slow);
        try
        {
            var ui = new ConcurrentQueue<Action>();
            var options = new CraftOptions { SearchDelayMilliseconds = 30, SearchProvider = slow.Name };
            using var host = new EditorHost(options, ui.Enqueue);
            using var view = new WorkspaceSearchView(host, () => folder.Path, () => options);
            await view.ActAsync(WorkspaceSearchView.QueryAction, "fir");
            await PumpUntil(ui, () => slow.Started.Count == 1, "the first search starts after the typing delay");
            Check.True(host.ActivityCenter.Running.Any(a => a.Kind == ActivityKinds.Search), "a running search is an activity");
            await view.ActAsync(WorkspaceSearchView.QueryAction, "first");
            await view.ActAsync(WorkspaceSearchView.QueryAction, "second");
            await Settle(ui, view);
            Check.True(slow.Cancelled.Contains("fir"), "the next keystroke cancelled the running search");
            Check.True(!slow.Started.Contains("first"), "a keystroke within the delay never started its search");
            Check.True(view.Results is [{ Hits: [{ Column: 6 }] }], "the last query's results show, and only them");
            Check.True(view.Blocks.OfType<PanelPicker>().Any(p => p.ActionId == "search.provider"), "with a second provider registered, the view offers the choice");
        }
        finally
        {
            WorkspaceSearchProviders.Unregister(slow.Name);
        }
    }

    [Test]
    public static async Task ReplaceAllInFilesIsOneReviewThenUndo()
    {
        using var folder = new TempFolder();
        var openPath = folder.File("Open.cs");
        var closedPath = folder.File("sub/Closed.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(closedPath)!);
        Disk.Write(openPath, "var oldName = oldName + 1;\n");
        Disk.Write(closedPath, "\uFEFFint oldName;\r\n// oldName here\r\n", new UTF8Encoding(false));
        var ui = new ConcurrentQueue<Action>();
        var presenter = new ReviewPresenter();
        var options = new CraftOptions { SearchDelayMilliseconds = 0 };
        using var host = new EditorHost(options, ui.Enqueue, presenter);
        Action? undo = null, kept = null;
        host.UndoPresenter = (_, u, k) =>
        {
            undo = u;
            kept = k;
            return true;
        };
        using var document = await TextDocument.OpenAsync(openPath, options);
        host.Open(document, null);
        using var view = new WorkspaceSearchView(host, () => folder.Path, () => options);
        await view.ActAsync(WorkspaceSearchView.QueryAction, "oldName");
        await Settle(ui, view);
        Check.Equal(4, view.MatchCount, "four matches in two files");
        await view.ActAsync("search.showReplace", null);
        await view.ActAsync(WorkspaceSearchView.ReplaceAction, "newName");
        var row = view.Blocks.OfType<PanelResults>().Single().Groups[0].Rows[0];
        Check.True(row.Highlights is [{ Replacement: "newName" }], "with a replacement typed, each match shows what replaces it");
        await view.ActAsync(WorkspaceSearchView.ReplaceAllAction, null);
        await Settle(ui, view);
        Check.True(presenter.Reviews is [2], $"one review of both files ({string.Join(", ", presenter.Reviews)})");
        Check.Equal("var newName = newName + 1;\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "the open document changed");
        Check.True(document.IsDirty && document.CanUndo, "unsaved, as an edit of the editor's, with an undo step");
        Check.Equal("int newName;\r\n// newName here\r\n", Disk.Read(closedPath), "the closed file written, its line breaks kept");
        Check.True(Disk.Bytes(closedPath) is [0xEF, 0xBB, 0xBF, (byte)'i', ..], "and its byte-order mark");
        Check.True(undo is not null, "the Undo bar shows");
        Check.Equal(0, view.MatchCount, "searched again: nothing left");

        undo!();
        await PumpUntil(ui, () => File.ReadAllText(closedPath).Contains("oldName", StringComparison.Ordinal) && document.Buffer.Current.GetText(0, 7) == "var old", "Undo takes it back");
        Check.Equal("var oldName = oldName + 1;\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "the open document's one undo step undone");
        Check.Equal("int oldName;\r\n// oldName here\r\n", Disk.Read(closedPath), "the closed file as it was");

        // A dismissed match is not replaced; a rejected review changes nothing.
        await view.ActAsync("search.refresh", null);
        await Settle(ui, view);
        await view.ActAsync("search.dismiss", WorkspaceSearchView.RowId(openPath, 0));
        presenter.Accept = false;
        await view.ActAsync(WorkspaceSearchView.ReplaceAllAction, null);
        await Settle(ui, view);
        Check.Equal("var oldName = oldName + 1;\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "rejected: nothing changed");
        presenter.Accept = true;
        await view.ActAsync("search.replaceFile", openPath);
        await Settle(ui, view);
        Check.Equal("var oldName = newName + 1;\n", document.Buffer.Current.GetText(0, document.Buffer.Current.Length), "Replace in this file: the dismissed first match stays");
        kept?.Invoke();
    }

    [Test]
    public static void ZeroBytesPerLineSearchedWarm()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 100_000; i++)
        {
            text.Append("    var value").Append(i % 97).Append(" = Compute(value, other);\r\n");
        }

        var chars = text.ToString().ToCharArray();
        var patterns = new[]
        {
            new TextPattern("needle", false, false, false, TimeSpan.FromSeconds(1)),
            new TextPattern("needle", true, true, false, TimeSpan.FromSeconds(1)),
            new TextPattern(@"ne+dle\d", false, false, true, TimeSpan.FromSeconds(1), compiled: true),
        };
        var hits = new List<WorkspaceSearchHit>();
        foreach (var pattern in patterns)
        {
            for (int warm = 0; warm < 40; warm++)
            {
                WorkspaceTextScan.Scan(chars.AsSpan(0, 20_000), pattern, 240, 100, hits);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            int found = WorkspaceTextScan.Scan(chars, pattern, 240, 100, hits);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(found == 0 && bytes == 0, $"'{pattern.Query}' ({(pattern.IsRegex ? "expression" : "text")}): 100,000 lines without a match, {bytes} bytes");
        }

        var one = new TextPattern("Compute", true, true, false, TimeSpan.FromSeconds(1));
        hits.Clear();
        Check.Equal(100, WorkspaceTextScan.Scan(chars, one, 240, 100, hits), "a match per line, stopped at the limit");
        Check.True(hits[0] is { Line: 0, Column: 17, Length: 7, PreviewColumn: 17 } && hits[1].Line == 1, "each with its line and column");
    }

    [Test]
    public static async Task ColdAndWarmSearchesOverCraftsSourceSize()
    {
        // Craft's own source is about 300 files and 105,000 lines (4.6 MB); the fixture is of that size.
        using var folder = new TempFolder();
        int lines = Fixture(folder.Path, files: 300, lines: 350, seed: 11);
        var queries = new[] { new WorkspaceSearchQuery("search"), new WorkspaceSearchQuery("Search", MatchCase: true, WholeWord: true), new WorkspaceSearchQuery(@"x\d+ = ", Regex: true) };
        var report = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"Workspace search ({DateTime.UtcNow:yyyy-MM-dd}), 300 files, {lines} lines, {Environment.ProcessorCount} processors\n"));
        foreach (var query in queries)
        {
            var cold = await SearchAsync(folder.Path, query);
            var times = new List<double>();
            var firsts = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var warm = await SearchAsync(folder.Path, query);
                Check.SequenceEqual(cold.Hits, warm.Hits, "every search finds the same");
                times.Add(warm.Summary.Elapsed.TotalMilliseconds);
                firsts.Add(warm.Summary.FirstResult?.TotalMilliseconds ?? 0);
            }

            times.Sort();
            firsts.Sort();
            report.Append(CultureInfo.InvariantCulture, $"'{query.Text}'{(query.MatchCase ? " case" : "")}{(query.WholeWord ? " word" : "")}{(query.Regex ? " regex" : "")}: {cold.Hits.Count} matches; "
                + $"cold {cold.Summary.Elapsed.TotalMilliseconds:0.0} ms (first result {cold.Summary.FirstResult?.TotalMilliseconds:0.0} ms); "
                + $"warm median {times[2]:0.0} ms (first result {firsts[2]:0.0} ms)\n");
            Check.True(cold.Hits.Count > 0, $"'{query.Text}' finds matches");
        }

        var output = Path.Combine(RepositoryRoot(), "test-output", "benches");
        Directory.CreateDirectory(output);
        Disk.Write(Path.Combine(output, "workspace-search.txt"), report.ToString());
        Console.WriteLine(report.ToString().TrimEnd());
    }

    // ---- helpers ----

    private sealed record Found(IReadOnlyList<string> Hits, IReadOnlyList<WorkspaceSearchFile> Files, WorkspaceSearchSummary Summary, IReadOnlyList<(bool Ok, string Why)> Previews);

    private static async Task<Found> SearchAsync(string root, WorkspaceSearchQuery query, int maxResults = 1_000_000, long maxFileBytes = 64L * 1024 * 1024, string[]? ignore = null)
    {
        var files = new ConcurrentBag<WorkspaceSearchFile>();
        var summary = await WorkspaceSearchProviders.Get(WorkspaceSearchProviders.Text).SearchAsync(
            new WorkspaceSearchRequest(root, query) { MaxResults = maxResults, MaxFileBytes = maxFileBytes, IgnoredFolders = ignore ?? [], PreviewCharacters = 240 }, files.Add);
        var hits = files.SelectMany(f => f.Hits.Select(h => string.Create(CultureInfo.InvariantCulture, $"{WorkspaceFiles.Relative(root, f.Path)}:{h.Line}:{h.Column}:{h.Length}")))
            .Order(StringComparer.Ordinal).ToList();
        var previews = files.SelectMany(f => f.Hits.Select(h => (h.PreviewColumn >= 0 && h.PreviewColumn + h.Length <= h.Preview.Length, f.Path))).ToList();
        return new Found(hits, [.. files], summary, previews);
    }

    // The reference: each file read whole as the editor reads text, each line searched on its own with the BCL.
    private static List<string> PlainScan(string root, WorkspaceSearchQuery query)
    {
        var results = new List<string>();
        var regex = query.Regex ? new Regex(query.Text, query.MatchCase ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) : null;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(8000, bytes.Length)).Contains((byte)0) && !(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var lines = text.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimEnd('\r');
                IEnumerable<(int Start, int Length)> found;
                if (regex is not null)
                {
                    found = regex.Matches(line).Where(m => m.Length > 0).Select(m => (m.Index, m.Length)).ToList();
                }
                else
                {
                    var list = new List<(int, int)>();
                    int at = 0;
                    while (at <= line.Length - query.Text.Length && (at = line.IndexOf(query.Text, at, query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        list.Add((at, query.Text.Length));
                        at += query.Text.Length;
                    }

                    found = list;
                }

                int last = -1;
                foreach (var (start, length) in found)
                {
                    bool whole = !query.WholeWord || ((start == 0 || !TextPattern.IsWordChar(line[start - 1])) && (start + length >= line.Length || !TextPattern.IsWordChar(line[start + length])));
                    if (whole && start >= last)
                    {
                        results.Add(string.Create(CultureInfo.InvariantCulture, $"{WorkspaceFiles.Relative(root, path)}:{index}:{start}:{length}"));
                        last = start + length;
                    }
                }
            }
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    // A folder of generated code-like files (deterministic); the lines written in all.
    private static int Fixture(string root, int files, int lines, int seed)
    {
        var random = new Random(seed);
        int total = 0;
        for (int f = 0; f < files; f++)
        {
            var folder = Path.Combine(root, "src", "Part" + (f % 12).ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var text = new StringBuilder();
            string newline = f % 3 == 0 ? "\r\n" : "\n";
            for (int l = 0; l < lines; l++)
            {
                text.Append(' ', random.Next(0, 4) * 4);
                int count = random.Next(2, 9);
                for (int w = 0; w < count; w++)
                {
                    text.Append(Words[random.Next(Words.Length)]).Append(random.Next(4) == 0 ? "(" : " ");
                }

                text.Append(CultureInfo.InvariantCulture, $"x{random.Next(100)} = {random.Next(1000)};").Append(newline);
                total++;
            }

            File.WriteAllText(Path.Combine(folder, "File" + f.ToString(CultureInfo.InvariantCulture) + (f % 5 == 0 ? ".txt" : ".cs")), text.ToString());
        }

        return total;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Craft.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Path.GetTempPath();
    }

    internal static async Task Settle(ConcurrentQueue<Action> ui, WorkspaceSearchView view)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Pump(ui);
            var settled = view.Settled;
            if (settled.IsCompleted && !view.Searching && ui.IsEmpty)
            {
                await Task.Delay(20);
                Pump(ui);
                if (view.Settled.IsCompleted && !view.Searching && ui.IsEmpty)
                {
                    return;
                }
            }

            if (watch.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("the search did not settle");
            }

            await Task.Delay(5);
        }
    }

    internal static void Pump(ConcurrentQueue<Action> ui)
    {
        while (ui.TryDequeue(out var action))
        {
            action();
        }
    }

    internal static async Task PumpUntil(ConcurrentQueue<Action> ui, Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Pump(ui);
            if (watch.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("timed out waiting: " + what);
            }

            await Task.Delay(5);
        }

        Pump(ui);
    }

    // A provider that holds each search until it is cancelled, except "second", which it answers.
    private sealed class SlowProvider : IWorkspaceSearchProvider
    {
        public ConcurrentBag<string> Started { get; } = [];

        public ConcurrentBag<string> Cancelled { get; } = [];

        public string Name => "slow-test";

        public string Title => "Slow (test)";

        public string Summary => "holds each search until it is cancelled";

        public async Task<WorkspaceSearchSummary> SearchAsync(WorkspaceSearchRequest request, Action<WorkspaceSearchFile> found, CancellationToken cancellationToken = default)
        {
            Started.Add(request.Query.Text);
            if (request.Query.Text == "second")
            {
                found(new WorkspaceSearchFile(Path.Combine(request.Root, "a.txt"), [new WorkspaceSearchHit(0, 6, 6, "first second", 6)]));
                return new WorkspaceSearchSummary(1, 1, 1);
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Add(request.Query.Text);
                throw;
            }

            return new WorkspaceSearchSummary(0, 0, 0);
        }
    }

    // Accepts (or rejects) every review; counts the files of each multi-file review.
    private sealed class ReviewPresenter : IEditorPresenter
    {
        public List<int> Reviews { get; } = [];

        public bool Accept { get; set; } = true;

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(Accept);

        public Task<IReadOnlyList<IReadOnlyList<bool>>> ChooseFilesAsync(IReadOnlyList<EditProposal> proposals, string title, CancellationToken cancellationToken)
        {
            Reviews.Add(proposals.Count);
            IReadOnlyList<IReadOnlyList<bool>> answers = [.. proposals.Select(p => (IReadOnlyList<bool>)Enumerable.Repeat(Accept, Math.Max(1, p.Diff.Hunks.Count)).ToArray())];
            return Task.FromResult(answers);
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}

/// <summary>Synchronous file reads and writes for the tests' fixtures and checks (small files, the test's own thread).</summary>
internal static class Disk
{
    public static void Write(string path, string text, Encoding? encoding = null)
    {
        if (encoding is null)
        {
            File.WriteAllText(path, text);
        }
        else
        {
            File.WriteAllText(path, text, encoding);
        }
    }

    public static string Read(string path) => File.ReadAllText(path);

    public static byte[] Bytes(string path) => File.ReadAllBytes(path);
}
