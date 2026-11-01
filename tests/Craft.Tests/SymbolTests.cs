// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Activities;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>A language whose sessions give a fixed outline and count how often they were asked (the symbol tests' fake).</summary>
internal sealed class OutlineLanguage(string name, string extension, IReadOnlyList<DocumentSymbol> symbols) : ILanguage
{
    private int _asked;

    public string Name { get; } = name;

    public string Summary => "Test: a fixed outline";

    public IReadOnlyList<string> Extensions { get; } = [extension];

    public LanguageRules Rules => LanguageRules.Plain;

    public IReadOnlyList<DocumentSymbol> Symbols { get; set; } = symbols;

    public int Asked => Volatile.Read(ref _asked);

    public ILanguageSession Open(ITextBuffer buffer) => new Session(this);

    private sealed class Session(OutlineLanguage language) : ILanguageSession
    {
        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
        }

        public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(ITextSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref language._asked);
            return Task.Run(() => language.Symbols, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>Accepts every hunk of every file, and keeps what it was shown.</summary>
internal sealed class AcceptAllPresenter : IEditorPresenter
{
    public List<(string Title, IReadOnlyList<EditProposal> Files)> Reviews { get; } = [];

    public bool Accept { get; set; } = true;

    public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(Accept);

    public Task<IReadOnlyList<IReadOnlyList<bool>>> ChooseFilesAsync(IReadOnlyList<EditProposal> proposals, string title, CancellationToken cancellationToken)
    {
        Reviews.Add((title, proposals));
        var answers = new List<IReadOnlyList<bool>>();
        foreach (var proposal in proposals)
        {
            var each = new bool[Math.Max(1, proposal.Diff.Hunks.Count)];
            Array.Fill(each, Accept);
            answers.Add(each);
        }

        return Task.FromResult<IReadOnlyList<IReadOnlyList<bool>>>(answers);
    }

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
}

/// <summary>Shared pieces of the piece 3b tests: C# sources, the measures' file.</summary>
internal static class SymbolSamples
{
    internal const string Outline = """
        namespace Demo.Shapes;

        /// <summary>A shape.</summary>
        [Serializable]
        public sealed class Circle : IEquatable<Circle>
        {
            private const double Pi = 3.14, Tau = 6.28;
            private readonly int _radius;

            public Circle(int radius) => _radius = radius;

            ~Circle() { }

            public event EventHandler? Changed;

            public event EventHandler Moved { add { } remove { } }

            public double Area { get; }

            public int this[int index, string name] => index;

            public double Scale(double by, string why) => by;

            public static Circle operator +(Circle a, Circle b) => a;

            public static implicit operator double(Circle c) => c.Area;

            public bool Equals(Circle? other) => other is not null;

            public enum Kind
            {
                Round = 1,
                Oval,
            }

            public delegate void Drawn(int x);
        }

        public record Point(int X, int Y);

        internal interface IShape<T>
        {
            T Make();
        }
        """;

    /// <summary>A C# file of about five thousand lines: 80 classes of 10 fields, properties and methods each (2,480 symbols).</summary>
    internal static string Large(int classes = 80)
    {
        var text = new StringBuilder("namespace Big;\n\n");
        for (int c = 0; c < classes; c++)
        {
            text.Append(CultureInfo.InvariantCulture, $"public sealed class Type{c}\n{{\n");
            for (int m = 0; m < 10; m++)
            {
                text.Append(CultureInfo.InvariantCulture, $"    public int Field{m};\n    public int Prop{m} {{ get; set; }}\n    public void Method{m}(int a, string b)\n    {{\n        Field{m} = a;\n    }}\n");
            }

            text.Append("}\n\n");
        }

        return text.ToString();
    }

    internal static async Task WriteMeasureAsync(string file, string line)
    {
        Console.WriteLine(line);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        await File.AppendAllTextAsync(Path.Combine(output, file), line + Environment.NewLine);
    }

    internal static async Task<IReadOnlyList<DocumentSymbol>> SymbolsOf(string code)
    {
        var buffer = new PieceTableBuffer(code);
        using var session = (CSharpLanguageSession)new CSharpLanguage().Open(buffer);
        return await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
    }

    internal static string Describe(IReadOnlyList<DocumentSymbol> symbols) => string.Join(", ", symbols.Select(s => $"{s.Kind} {s.Name}"));
}

/// <summary>The X restart's piece 3b: the document-symbols contract, C#'s outline, the flat index and the editor's per-version cache.</summary>
[TestGroup("DocSymbols")]
public static class DocSymbolTests
{
    [Test]
    public static async Task CSharpGivesItsOutlineInTheOrderOfTheTextWithKindsNamesAndDetails()
    {
        string code = SymbolSamples.Outline.ReplaceLineEndings("\n");
        var buffer = new PieceTableBuffer(code);
        using var session = (CSharpLanguageSession)new CSharpLanguage().Open(buffer);
        var symbols = await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
        Check.True(symbols is [{ Kind: ScopeKind.Namespace, Name: "Demo.Shapes" }], $"one file-scoped namespace at the top ({SymbolSamples.Describe(symbols)})");
        var types = symbols[0].Children;
        Check.SequenceEqual(["Circle", "Point", "IShape<T>"], types.Select(t => t.Name).ToList(), "its types in order, a generic with its parameters");
        Check.True(types.All(t => t.Kind == ScopeKind.Type) && types[0].Detail == "class" && types[1].Detail == "record" && types[2].Detail == "interface", "types with their keyword as the detail");
        var circle = types[0];
        Check.SequenceEqual(
            ["Pi", "Tau", "_radius", "Circle", "~Circle", "Changed", "Moved", "Area", "this[]", "Scale", "operator +", "implicit operator double", "Equals", "Kind", "Drawn"],
            circle.Children.Select(c => c.Name).ToList(), "every member, each variable of a field, in the order of the text");
        Check.SequenceEqual(
            [ScopeKind.Field, ScopeKind.Field, ScopeKind.Field, ScopeKind.Method, ScopeKind.Method, ScopeKind.Event, ScopeKind.Event, ScopeKind.Property, ScopeKind.Property,
                ScopeKind.Method, ScopeKind.Method, ScopeKind.Method, ScopeKind.Method, ScopeKind.Type, ScopeKind.Type],
            circle.Children.Select(c => c.Kind).ToList(), "the new kinds: Field, Event, Property");
        var scale = circle.Children.First(c => c.Name == "Scale");
        Check.Equal("(double by, string why) : double", scale.Detail, "a method's parameters and its type");
        Check.Equal("Scale", code.Substring(scale.NameStart, scale.NameLength), "the name's span is the name");
        Check.True(code.Substring(scale.Start, scale.Length).StartsWith("public double Scale", StringComparison.Ordinal) && code.Substring(scale.Start, scale.Length).EndsWith("=> by;", StringComparison.Ordinal),
            "the declaration's span, modifiers in, no leading comment");
        Check.True(code.Substring(circle.Start, 13) == "[Serializable" && circle.Contains(scale.Start), "a type's span holds its attributes and its members");
        var kind = circle.Children.First(c => c.Name == "Kind");
        Check.True(kind.Children is [{ Name: "Round", Kind: ScopeKind.EnumMember, Detail: "1" }, { Name: "Oval", Kind: ScopeKind.EnumMember, Detail: null }], "an enum's members with their values");
        Check.Equal("[int index, string name] : int", circle.Children.First(c => c.Name == "this[]").Detail, "an indexer's parameters");
        Check.Equal("int", circle.Children.First(c => c.Name == "_radius").Detail, "a field's type");

        // Once per version: the same list again; an edit gives a new one; an older snapshot answers for its own text.
        var again = await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
        Check.True(ReferenceEquals(symbols, again), "the same version gives the same list (walked once, kept with its parse)");
        var old = buffer.Current;
        buffer.Apply([new TextEdit(code.IndexOf("    public double Area", StringComparison.Ordinal), 0, "    public int Added;\n")]);
        var edited = await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
        Check.True(!ReferenceEquals(edited, symbols) && edited[0].Children[0].Children.Any(c => c.Name == "Added"), "a new version walks the new parse");
        var older = await session.GetDocumentSymbolsAsync(old, CancellationToken.None);
        Check.True(!older[0].Children[0].Children.Any(c => c.Name == "Added"), "an older snapshot is answered for its own text");

        // The contract's defaults: a session without symbols gives none.
        var plain = new CountingLanguage("plain-symbols", ".pls").Open(new PieceTableBuffer("x"));
        Check.Equal(0, (await plain.GetDocumentSymbolsAsync(new PieceTableBuffer("x").Current, CancellationToken.None)).Count, "none by default");
    }

    [Test]
    public static async Task TheIndexFlattensOnceAndFindsPathsChildrenSiblingsAndNamesWithoutAStringMade()
    {
        string code = SymbolSamples.Outline.ReplaceLineEndings("\n");
        var buffer = new PieceTableBuffer(code);
        var symbols = await SymbolSamples.SymbolsOf(code);
        var index = SymbolIndex.Build(symbols, buffer.Current);
        Check.Equal(1 + 3 + 15 + 2 + 1, index.Count, "every symbol once, the enum's members and the interface's method among them");
        Check.True(index.Names[0] == "Demo.Shapes" && index.Parents[0] == -1 && index.Depths[1] == 1 && index.Parents[1] == 0, "each before its children, parents and depths");
        int scale = index.IndexOf("Scale".AsSpan());
        Check.True(scale > 0 && index.Names[scale] == "Scale", "a name found by span (the alternate lookup)");
        Check.Equal(-1, index.IndexOf("Nope".AsSpan()), "an unknown name");
        int at = code.IndexOf("=> by;", StringComparison.Ordinal);
        var path = index.PathAt(at);
        Check.SequenceEqual(["Demo.Shapes", "Circle", "Scale"], path.Select(i => index.Names[i]).ToList(), "the path at a position, outermost first");
        Check.Equal(code.Split('\n').ToList().FindIndex(l => l.Contains("double Scale(", StringComparison.Ordinal)), index.Lines[scale], "the name's line");
        int circle = index.IndexOf("Circle".AsSpan());
        Check.Equal(15, index.ChildrenOf(circle).Count, "a type's children");
        Check.SequenceEqual([0], index.ChildrenOf(-1).ToList(), "the roots");
        Check.Equal(circle, index.SiblingsParentAt(at, "Scale".AsSpan()), "a member's siblings are its type's children");
        Check.Equal(0, index.SiblingsParentAt(at, "Circle".AsSpan()), "a type's siblings are its namespace's children");

        // Built once per version in the editor: one request a version, however many ask.
        var language = new OutlineLanguage("outline-test", ".otl", symbols);
        var (model, document, _) = Editors.Make(code, language: language);
        using var _m = model;
        var first = model.DocumentSymbolsAsync();
        var second = model.DocumentSymbolsAsync();
        var landed = await first;
        Check.True(ReferenceEquals(landed, await second) && language.Asked == 1, $"two callers of one version share one request ({language.Asked})");
        Check.True(ReferenceEquals(model.Symbols, landed) && ReferenceEquals(await model.DocumentSymbolsAsync(), landed), "kept: asked again it is the kept index");
        model.Select(0, 0);
        model.OnText("x");
        var next = await model.DocumentSymbolsAsync();
        Check.True(next is not null && next.Version == model.Snapshot.Version && language.Asked == 2, "an edit asks again, once");
        _ = document;
    }

    [Test]
    public static async Task CSharpSymbolsOfAFiveThousandLineFileAreWalkedOffTheCallingThread()
    {
        string code = SymbolSamples.Large();
        var buffer = new PieceTableBuffer(code);
        Check.True(buffer.Current.LineCount >= 5000, $"about five thousand lines ({buffer.Current.LineCount})");
        using var session = (CSharpLanguageSession)new CSharpLanguage().Open(buffer);
        await session.WhenIdle();
        long start = Stopwatch.GetTimestamp();
        var pending = session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
        var returned = Stopwatch.GetElapsedTime(start);
        var cold = await pending;
        var coldTime = Stopwatch.GetElapsedTime(start);
        int count = SymbolIndex.Build(cold, buffer.Current).Count;
        Check.Equal(80 + (80 * 30) + 1, count, "every class, field, property and method, and the namespace");

        var warm = new List<double>();
        for (int round = 0; round < 10; round++)
        {
            int at = code.IndexOf("    public int Field3;", StringComparison.Ordinal);
            buffer.Apply([new TextEdit(at, 0, " ")]);
            code = buffer.Current.GetText(0, buffer.Current.Length);
            await session.WhenIdle();
            long s = Stopwatch.GetTimestamp();
            _ = await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
            warm.Add(Stopwatch.GetElapsedTime(s).TotalMilliseconds);
        }

        warm.Sort();
        long again = Stopwatch.GetTimestamp();
        _ = await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None);
        var kept = Stopwatch.GetElapsedTime(again);
        string line = string.Create(CultureInfo.InvariantCulture,
            $"C# symbols of a {buffer.Current.LineCount:N0}-line file ({count:N0} symbols): the call returned in {returned.TotalMilliseconds:F2} ms, cold {coldTime.TotalMilliseconds:F1} ms, warm (a new version) median {warm[warm.Count / 2]:F2} ms, worst {warm[^1]:F2} ms, the same version again {kept.TotalMilliseconds:F3} ms");
        await SymbolSamples.WriteMeasureAsync("piece-3b.txt", line);
        Check.True(returned < TimeSpan.FromMilliseconds(50), line + ": the walk runs off the calling thread");
        Check.True(warm[warm.Count / 2] < 200, line + ": a version's walk is well under a fifth of a second warm");
    }
}

/// <summary>Quick open's '@' (piece 3b): the editor's symbols, fuzzy, grouped by kind, a level only, previewed, and a folder's entries.</summary>
[TestGroup("Inline")]
public static class SymbolQuickOpenTests
{
    [Test]
    public static async Task AtListsTheEditorsSymbolsGroupsThemByKindAndPreviewsUntilEscape()
    {
        Check.Equal(QuickOpenMode.Symbols, QuickOpen.ModeOf("@sca"), "'@' first asks for symbols");
        Check.Equal("@", QuickOpen.PrefixOf(QuickOpenMode.Symbols), "its prefix");
        string code = SymbolSamples.Outline.ReplaceLineEndings("\n");
        var symbols = await SymbolSamples.SymbolsOf(code);
        var (model, _, _) = Editors.Make(code, language: new OutlineLanguage("outline-quick", ".oq", symbols));
        using var _m = model;
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 50) { SymbolSource = token => model.DocumentSymbolsAsync(token) };

        var all = await quick.QueryInBackgroundAsync("@");
        Check.True(all.Count == quick.Symbols.Count && all[0].Text == "Demo.Shapes" && all[1].Text == "Circle", "with nothing typed, every symbol in the order of the text");
        Check.True(!quick.LastQueryInBackground, "a short list is matched on the calling thread");
        var found = await quick.QueryInBackgroundAsync("@scl");
        Check.True(found.Count > 0 && found[0] is { Text: "Scale", SymbolKind: ScopeKind.Method } && found[0].TextHighlights.Count > 0, $"fuzzy by name, highlighted ({string.Join(", ", found.Select(f => f.Text))})");
        var scale = found[0];
        Check.True(code.Substring(scale.SymbolStart, scale.SymbolLength) == "Scale" && scale.Detail == "(double by, string why) : double  ·  Circle" && scale.Line > 0,
            $"where its name is, its detail and its container ({scale.Detail})");
        var exact = await quick.QueryInBackgroundAsync("@Area");
        Check.Equal("Area", exact[0].Text, "the name itself first");

        var grouped = await quick.QueryInBackgroundAsync("@:");
        var groups = grouped.Where(g => g.Group is not null).Select(g => g.Group!).ToList();
        Check.SequenceEqual(["namespaces (1)", "types (5)", "methods (7)", "properties (2)", "fields (3)", "events (2)", "enum members (2)"], groups, "\"@:\" groups by kind, each group's title on its first row");
        Check.True(grouped.Select(g => SymbolKinds.Rank(g.SymbolKind!.Value)).SequenceEqual(grouped.Select(g => SymbolKinds.Rank(g.SymbolKind!.Value)).Order()), "in the kinds' order");
        var groupedMethods = await quick.QueryInBackgroundAsync("@:e");
        Check.True(groupedMethods.All(g => g.Text.Contains('e', StringComparison.OrdinalIgnoreCase)), "\"@:name\" filters, then groups");

        quick.SymbolsUnder = quick.Symbols.IndexOf("Circle".AsSpan());
        var siblings = await quick.QueryInBackgroundAsync("@");
        Check.Equal(15, siblings.Count, "a level only: the type's members");
        quick.SymbolsUnder = null;

        // Up and Down preview the place; Escape goes back to where the editor was.
        model.Select(3, 3);
        var preview = new SymbolPreview();
        preview.Begin(model);
        Check.True(preview.Show(scale), "a symbol row previews");
        Check.True(model.Selection.Caret == scale.SymbolStart && model.Selection.Anchor == scale.SymbolStart + scale.SymbolLength, "its name selected");
        Check.True(!preview.Show(new QuickOpenItem("file", null, "x", null, 0)), "a row that is no symbol does not");
        Check.True(preview.Restore() && model.Selection.Caret == 3 && model.Selection.Anchor == 3, "Escape: back where it was");
        preview.Begin(model);
        Check.True(!preview.Restore(), "nothing previewed: nothing put back");
    }

    [Test]
    public static async Task AtMatchesTwoThousandSymbolsAKeystrokeOffTheCallingThreadWithTheSameRows()
    {
        string code = SymbolSamples.Large();
        var buffer = new PieceTableBuffer(code);
        var index = SymbolIndex.Build(await SymbolSamples.SymbolsOf(code), buffer.Current);
        Check.True(index.Count >= 2000, $"over two thousand symbols ({index.Count})");
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 120) { BackgroundFiles = 2000 };
        quick.SetSymbols(index);
        string[] keys = ["@m", "@me", "@met", "@meth", "@method", "@method7", "@p", "@pr", "@prop", "@prop3", "@:f", "@:fi", "@:field", "@ty", "@type4"];
        foreach (var key in keys)
        {
            var rows = await quick.QueryInBackgroundAsync(key);
            Check.True(quick.LastQueryInBackground, $"'{key}': over the threshold, matched off the calling thread");
            var same = quick.Query(key);
            Check.True(rows.Select(r => (r.Text, r.SymbolStart, r.Score, r.Group)).SequenceEqual(same.Select(r => (r.Text, r.SymbolStart, r.Score, r.Group))),
                $"'{key}': the background rows are the calling thread's, in the same order");
        }

        var times = new List<double>();
        long bytes = 0;
        for (int round = 0; round < 6; round++)
        {
            foreach (var key in keys)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long s = Stopwatch.GetTimestamp();
                _ = quick.Query(key);
                times.Add(Stopwatch.GetElapsedTime(s).TotalMilliseconds);
                if (round == 5)
                {
                    bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
        }

        times.Sort();
        string line = string.Create(CultureInfo.InvariantCulture,
            $"'@' over {index.Count:N0} symbols, per key ({keys.Length} keys x 6 rounds, 120 rows kept): median {times[times.Count / 2]:F2} ms, worst {times[^1]:F2} ms, {bytes / keys.Length:N0} B a key (the rows made)");
        await SymbolSamples.WriteMeasureAsync("piece-3b.txt", line);
        Check.True(times[times.Count / 2] < 50, line + ": a key's match is well inside a frame budget off the UI thread");
    }

    [Test]
    public static async Task AFoldersEntriesAreListedFoldersFirstAndAFolderPickedListsItsOwn()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        Directory.CreateDirectory(folder.File("docs"));
        await File.WriteAllTextAsync(folder.File("b.cs"), "");
        await File.WriteAllTextAsync(folder.File("a.txt"), "");
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 20);
        quick.SetFiles(folder.Path, ["b.cs", "a.txt"]);
        await quick.BrowseAsync(folder.Path);
        var rows = quick.Query("");
        Check.SequenceEqual(["docs", "src", "a.txt", "b.cs"], rows.Select(r => r.Text).ToList(), "folders first, then files, each by name");
        Check.True(rows[0].IsFolder && !rows[2].IsFolder && rows[3].Icon is not null && rows[0].Path == Path.Combine(folder.Path, "docs"), "folders marked, files with their icons and paths");
        Check.SequenceEqual(["b.cs"], quick.Query("b.").Select(r => r.Text).ToList(), "typing filters the folder's entries");
        quick.EndBrowse();
        Check.True(quick.BrowsedFolder is null && quick.Query("").All(r => !r.IsFolder), "ended: the workspace's files again");
    }
}

/// <summary>The breadcrumb (piece 3b): the path in the workspace, the file, the scopes; folded by width; a symbol's siblings.</summary>
[TestGroup("Breadcrumb")]
public static class BreadcrumbTests
{
    [Test]
    public static void PartsAreEveryFolderFromTheWorkspaceTheFileAndTheScopes()
    {
        string root = Path.Combine(Path.GetTempPath(), "ws-root");
        string file = Path.Combine(root, "src", "Tools", "EditorTools.cs");
        var parts = BreadcrumbLayout.Parts(file, root, [new ScopeName("EditorTools", ScopeKind.Type), new ScopeName("Get", ScopeKind.Method)]);
        Check.SequenceEqual(["ws-root", "src", "Tools", "EditorTools.cs", "EditorTools", "Get"], parts.Select(p => p.Text).ToList(), "each folder from the workspace's root, the file, the scopes");
        Check.SequenceEqual([BreadcrumbPartKind.Folder, BreadcrumbPartKind.Folder, BreadcrumbPartKind.Folder, BreadcrumbPartKind.File, BreadcrumbPartKind.Symbol, BreadcrumbPartKind.Symbol],
            parts.Select(p => p.Kind).ToList(), "what each part is");
        Check.True(parts[1].Path == Path.Combine(root, "src") && parts[3].Path == file && parts[5].Scope == ScopeKind.Method, "folders and the file carry their paths, symbols their kinds");
        var outside = BreadcrumbLayout.Parts(Path.Combine(Path.GetTempPath(), "elsewhere", "a.cs"), root, []);
        Check.True(outside is [{ Kind: BreadcrumbPartKind.Folder }, { Text: "a.cs" }] && outside[0].Text == Path.Combine(Path.GetTempPath(), "elsewhere"), "a file outside the workspace: its whole folder");
        Check.Equal(0, BreadcrumbLayout.Parts(null, root, []).Count, "no file, no parts");
    }

    [Test]
    public static void TheMiddlePartsFoldByTheMeasuredWidths()
    {
        string root = Path.Combine(Path.GetTempPath(), "ws");
        var parts = BreadcrumbLayout.Parts(Path.Combine(root, "src", "deep", "deeper", "File.cs"), root, [new ScopeName("Type", ScopeKind.Type), new ScopeName("Member", ScopeKind.Method)]);
        double[] widths = [20, 30, 40, 50, 60, 40, 60];
        double separator = 10, ellipsis = 12;
        double total = widths.Sum() + (separator * (widths.Length - 1));
        Check.True(ReferenceEquals(BreadcrumbLayout.Fold(parts, widths, separator, ellipsis, total), parts), "everything fits: nothing folds");
        var folded = BreadcrumbLayout.Fold(parts, widths, separator, ellipsis, total - 50);
        Check.SequenceEqual(["ws", "…", "deeper", "File.cs", "Type", "Member"], folded.Select(p => p.Text).ToList(), "the fewest middle parts fold from the second on");
        Check.True(folded[1] is { Kind: BreadcrumbPartKind.Ellipsis, Folded: [{ Text: "src" }, { Text: "deep" }] } && folded[1].Target.Text == "deep", "'…' holds the folded parts and acts as the last");
        double sum = 20 + 12 + 50 + 60 + 40 + 60 + (5 * separator);
        Check.True(sum <= total - 50, "and it fits");
        var narrow = BreadcrumbLayout.Fold(parts, widths, separator, ellipsis, 100);
        Check.True(narrow[0].Text == "ws" && narrow.Any(p => p.Kind == BreadcrumbPartKind.File) && narrow[^1].Text == "Member", $"too narrow: the first, the file and the last symbol stay ({string.Join(" › ", narrow.Select(p => p.Text))})");
        Check.Throws<ArgumentException>(() => BreadcrumbLayout.Fold(parts, [1, 2], separator, ellipsis, 10), "a width for each part");
    }

    [Test]
    public static async Task ASymbolPartListsItsSiblingsAndTheCaretsScopesComeFromTheParse()
    {
        string code = SymbolSamples.Outline.ReplaceLineEndings("\n");
        var buffer = new PieceTableBuffer(code);
        using var session = (CSharpLanguageSession)new CSharpLanguage().Open(buffer);
        await session.WhenIdle();
        int at = code.IndexOf("=> by;", StringComparison.Ordinal);
        var scopes = session.ScopeAt(buffer.Current, at);
        var parts = BreadcrumbLayout.Parts(Path.Combine(Path.GetTempPath(), "ws", "Shapes.cs"), Path.Combine(Path.GetTempPath(), "ws"), scopes);
        Check.SequenceEqual(["ws", "Shapes.cs", "Circle", "Scale"], parts.Select(p => p.Text).ToList(), "the caret's scopes after the file");
        var index = SymbolIndex.Build(await session.GetDocumentSymbolsAsync(buffer.Current, CancellationToken.None), buffer.Current);
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 50);
        quick.SetSymbols(index);
        quick.SymbolsUnder = index.SiblingsParentAt(at, parts[3].Text);
        var siblings = quick.Query("@");
        Check.True(siblings.Count == 15 && siblings.Any(s => s.Text == "Area") && siblings.All(s => s.Detail?.EndsWith("Circle", StringComparison.Ordinal) ?? true), "the member's part lists the type's members");
        quick.SymbolsUnder = index.SiblingsParentAt(at, parts[2].Text);
        Check.SequenceEqual(["Circle", "Point", "IShape<T>"], quick.Query("@").Select(s => s.Text).ToList(), "the type's part lists the namespace's types");
    }
}

/// <summary>F2 rename (piece 3b): Roslyn's rename as a workspace edit, one diff of every file, each open document one undo step, Undo for all.</summary>
[TestGroup("SymbolRename")]
public static class SymbolRenameTests
{
    private static string ProjectFile() => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{Craft.CSharp.Workspaces.ReferenceAssemblies.RunningFramework}</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    [Test]
    public static async Task F2RenamesAcrossFilesAsOneDiffEachOpenDocumentOneUndoStepAndUndoTakesItAllBack()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        CSharpLanguage.Register();
        fixture.Write("Demo.csproj", ProjectFile());
        var shapes = fixture.Write("Shapes.cs", SemanticTests.Shapes.ReplaceLineEndings("\n"));
        var program = fixture.Write("Program.cs", SemanticTests.Program.ReplaceLineEndings("\n"));
        var shell = await fixture.OpenWorkspaceAsync();
        var presenter = new AcceptAllPresenter();
        fixture.Host.Presenter = presenter;
        var tab = (await shell.OpenFileAsync(program))!;
        var editor = fixture.EditorOf(tab);
        string before = editor.Snapshot.GetText(0, editor.Snapshot.Length);
        int make = before.IndexOf("Make(1", StringComparison.Ordinal) + 2;
        editor.Select(make, make);

        RenameRequest? asked = null;
        shell.RenameRequested += (_, request) => asked = request;
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.RenameSymbol), "F2 runs");
        await TreeMoveTests.WaitFor(() => asked is not null, "the rename box is asked for, the name found off the UI thread");
        Check.True(asked!.Target is { Name: "Make", CanRename: true } && before.Substring(asked.Target.Start, asked.Target.Length) == "Make" && shell.PendingRename == asked, "the name under the caret");

        var outcome = await shell.CommitRenameAsync(asked, "Create");
        Check.True(outcome is { Applied: true, Files: 2, Message: "Renamed in 2 files" }, $"applied in both files ({outcome})");
        Check.True(presenter.Reviews is [{ Files.Count: 2 }] && presenter.Reviews[0].Title == "Rename Make to Create", $"one review of every file ({string.Join("; ", presenter.Reviews.Select(r => r.Title))})");
        string after = editor.Snapshot.GetText(0, editor.Snapshot.Length);
        Check.True(after.Contains("Circle.Create(1, \"a\")", StringComparison.Ordinal) && !after.Contains("Make", StringComparison.Ordinal), "the open document changed");
        Check.True((await File.ReadAllTextAsync(shapes)).Contains("public static Circle Create(int radius, string name)", StringComparison.Ordinal), "the file that was not open: edited and saved");
        Check.True(shell.Groups.FindOpen(shapes) is null, "and not opened in a tab");
        Check.True(fixture.Host.ActivityCenter.Recent.Any(a => a.Title == "Rename"), "the edit was computed as the activity \"Rename\"");
        Check.Equal("Renamed in 2 files", shell.UndoBar.Current?.Text, "the undo bar says it");

        // One undo step for the open document; Undo on the bar takes the whole change back.
        Check.True(tab.Document.CanUndo, "the open document's change is one step");
        Check.True(shell.UndoBar.Undo(), "Undo");
        await TreeMoveTests.WaitFor(() => File.ReadAllText(shapes).Contains("Make(int radius", StringComparison.Ordinal), "the saved file is written back");
        Check.Equal(before, editor.Snapshot.GetText(0, editor.Snapshot.Length), "the open document is back");
    }

    [Test]
    public static async Task NamesAreCheckedAMetadataSymbolIsRefusedAndConflictsAreSaid()
    {
        using var project = new SemanticTests.Project();
        var snapshot = project.Buffer.Current;
        var console = await project.Session.PrepareRenameAsync(snapshot, project.At("Console.") + 2, CancellationToken.None);
        Check.True(console is { CanRename: false } && console.Refusal!.Contains("not in the workspace", StringComparison.Ordinal), $"a symbol of a referenced assembly is refused, said ({console?.Refusal})");
        Check.True(await project.Session.PrepareRenameAsync(snapshot, project.At("namespace") + 3, CancellationToken.None) is null or { CanRename: false }, "a keyword renames nothing");
        var area = await project.Session.PrepareRenameAsync(snapshot, project.At("Area(2)") + 1, CancellationToken.None);
        Check.True(area is { Name: "Area", CanRename: true }, "a workspace symbol can be renamed");

        int at = project.At("Area(2)") + 1;
        var digit = await project.Session.RenameAsync(snapshot, at, "1st", CancellationToken.None);
        Check.True(digit.Refusal?.Contains("not a C# name", StringComparison.Ordinal) == true && digit.Files.Count == 0, $"a name that is not C#'s ({digit.Refusal})");
        var keyword = await project.Session.RenameAsync(snapshot, at, "class", CancellationToken.None);
        Check.True(keyword.Refusal?.Contains("@class", StringComparison.Ordinal) == true, $"a keyword is said with its escape ({keyword.Refusal})");
        var escaped = await project.Session.RenameAsync(snapshot, at, "@class", CancellationToken.None);
        Check.True(escaped.Refusal is null && escaped.Files.Count == 2, "'@class' is a name");
        var same = await project.Session.RenameAsync(snapshot, at, "Area", CancellationToken.None);
        Check.True(same is { Refusal: null, Files.Count: 0 }, "the same name changes nothing");

        var edit = await project.Session.RenameAsync(snapshot, at, "Surface", CancellationToken.None);
        Check.True(edit is { Refusal: null, Conflicts.Count: 0, Files.Count: 2 } && edit.EditCount == 2, $"the declaration and the call ({edit.EditCount} edits, {edit.Files.Count} files)");
        var open = edit.Files.Single(f => f.Path == project.ProgramPath);
        var closed = edit.Files.Single(f => f.Path == project.ShapesPath);
        Check.True(open.Version == snapshot.Version && closed.Version is null, "the open document's edits carry its version; the file on disk's none");
        Check.Equal("Surface", open.Edits[0].Text, "the new name");

        // A clash: two fields, one renamed to the other's name.
        project.Replace("public static void Run()", "static int Count;\n    static int Total;\n\n    public static void Run()");
        var clash = await project.Session.RenameAsync(project.Buffer.Current, project.At("Count;") + 1, "Total", CancellationToken.None);
        Check.True(clash.Conflicts.Count > 0 && clash.Conflicts[0].Contains("CS0102", StringComparison.Ordinal), $"the conflict is said ({string.Join("; ", clash.Conflicts)})");

        // The contract's defaults.
        var plain = (ISemanticLanguageSession)new CountingLanguage("rename-defaults", ".rnd").Open(new PieceTableBuffer("x"));
        Check.True(await plain.PrepareRenameAsync(new PieceTableBuffer("x").Current, 0, CancellationToken.None) is null, "no rename by default");
        Check.True((await plain.RenameAsync(new PieceTableBuffer("x").Current, 0, "y", CancellationToken.None)).Refusal is not null, "refused, said, by default");
        Check.True(WorkspaceEdit.Empty.EditCount == 0 && WorkspaceEdit.Refused("why").Refusal == "why", "an empty and a refused edit");
    }

    [Test]
    public static async Task AStaleDocumentIsRefusedBeforeTheUserIsAsked()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var presenter = new AcceptAllPresenter();
        fixture.Host.Presenter = presenter;
        var path = fixture.Write("a.grp", "one two\n");
        var tab = (await shell.OpenFileAsync(path))!;
        int version = tab.Document.Buffer.Current.Version;
        var editor = fixture.EditorOf(tab);
        editor.Select(0, 0);
        editor.OnText("x");
        var stale = new WorkspaceEdit([new WorkspaceFileEdit(path, [new TextEdit(0, 3, "ONE")], version)]);
        Check.True(await fixture.Host.ProposeWorkspaceEditAsync(stale, "Rename one") is null && presenter.Reviews.Count == 0, "an open document that changed since: nothing asked");
        Check.True(fixture.Host.LastWorkspaceEditRefusal?.Contains("changed since", StringComparison.Ordinal) == true, $"said ({fixture.Host.LastWorkspaceEditRefusal})");
        var dirtyNoVersion = new WorkspaceEdit([new WorkspaceFileEdit(path, [new TextEdit(0, 1, "y")])]);
        Check.True(await fixture.Host.ProposeWorkspaceEditAsync(dirtyNoVersion, "Rename") is null && fixture.Host.LastWorkspaceEditRefusal!.Contains("unsaved", StringComparison.Ordinal), "unsaved text the edit was not made against: refused");

        // Through the contract, accepted: true, and the shell's undo bar takes it back.
        var fresh = new WorkspaceEdit([new WorkspaceFileEdit(path, [new TextEdit(1, 3, "ONE")], tab.Document.Buffer.Current.Version)]);
        Check.True(await ((IEditorHost)fixture.Host).ProposeWorkspaceAsync(fresh, "Upper"), "ProposeWorkspaceAsync applies what the user accepts");
        Check.True(tab.Document.Buffer.Current.GetText(0, 4) == "xONE" && shell.UndoBar.Current?.Text == "Upper: 1 file changed.", $"applied, Undo offered ({shell.UndoBar.Current?.Text})");
        Check.True(await ((IEditorHost)fixture.Host).ProposeWorkspaceAsync(WorkspaceEdit.Refused("no"), "Nothing") is false, "a refused edit proposes nothing");
    }

    [Test]
    public static async Task AWidelyUsedSymbolIsRenamedWithTheUiThreadNeverStalled()
    {
        const int Files = 30;
        const int UsesPerFile = 20;
        using var folder = new TempFolder();
        using var ui = new FakeUiThread();
        var labels = new UiWorkLabels();
        using var watchdog = new UiWatchdog(ui.Dispatch, labels, 20, 250);
        ui.Run(() => SynchronizationContext.SetSynchronizationContext(new DispatchContext(ui)));
        SplitFixture? fixture = null;
        string widely = "";
        ui.Run(() =>
        {
            fixture = new SplitFixture(folder);
            CSharpLanguage.Register();
            fixture.Write("Demo.csproj", ProjectFile());
            widely = fixture.Write("Widely.cs", "namespace Demo;\n\npublic static class Widely\n{\n    public static int Used(int x) => x;\n}\n");
            for (int f = 0; f < Files; f++)
            {
                var body = new StringBuilder().Append(CultureInfo.InvariantCulture, $"namespace Demo;\n\npublic static class User{f}\n{{\n    public static int Sum()\n    {{\n        int total = 0;\n");
                for (int u = 0; u < UsesPerFile; u++)
                {
                    body.Append(CultureInfo.InvariantCulture, $"        total += Widely.Used({u});\n");
                }

                fixture.Write($"User{f}.cs", body.Append("        return total;\n    }\n}\n").ToString());
            }
        });
        try
        {
            var done = new TaskCompletionSource<(RenameOutcome Outcome, TimeSpan Took)>(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Dispatch(async () =>
            {
                try
                {
                    var shell = await fixture!.OpenWorkspaceAsync();
                    fixture.Host.Presenter = new AcceptAllPresenter();
                    var tab = (await shell.OpenFileAsync(widely))!;
                    var editor = fixture.EditorOf(tab);
                    int at = editor.Snapshot.GetText(0, editor.Snapshot.Length).IndexOf("Used", StringComparison.Ordinal) + 1;
                    editor.Select(at, at);
                    var request = await shell.BeginRenameAsync();
                    watchdog.Start();
                    long start = Stopwatch.GetTimestamp();
                    var outcome = await shell.CommitRenameAsync(request!, "Applied");
                    done.SetResult((outcome, Stopwatch.GetElapsedTime(start)));
                }
                catch (Exception error)
                {
                    done.SetException(error);
                }
            });
            var (outcome, took) = await done.Task.WaitAsync(TimeSpan.FromMinutes(3));
            ui.Run(() => { });
            Check.True(outcome is { Applied: true, Files: Files + 1 }, $"every file renamed ({outcome})");
            string line = string.Create(CultureInfo.InvariantCulture,
                $"Rename of a symbol used {Files * UsesPerFile} times in {Files + 1} files (one open): {took.TotalMilliseconds:F0} ms from Enter to applied (the workspace built, Roslyn's rename, the review, {Files} files saved); watchdog: {watchdog.Ticks} ticks, worst {watchdog.Worst.TotalMilliseconds:F0} ms, {watchdog.StallCount} stalls over 250 ms");
            await SymbolSamples.WriteMeasureAsync("piece-3b.txt", line);
            Check.Equal(0L, watchdog.StallCount, line + $": the UI thread never stalled ({string.Join("; ", watchdog.Stalls.Select(s => $"{s.Label} {s.Duration.TotalMilliseconds:F0} ms"))})");
            Check.True((await File.ReadAllTextAsync(Path.Combine(fixture!.Workspace, "User7.cs"))).Contains("Widely.Applied(19)", StringComparison.Ordinal), "a closed file's every use renamed and saved");
        }
        finally
        {
            ui.Run(() => fixture?.Dispose());
        }
    }

    private sealed class DispatchContext(FakeUiThread ui) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => ui.Dispatch(() => d(state));

        public override SynchronizationContext CreateCopy() => this;
    }
}

/// <summary>The composer's '@' symbols (X part 10's, now from the document-symbols contract).</summary>
[TestGroup("Conversation")]
public static class ComposerSymbolTests
{
    [Test]
    public static async Task TheComposersAtListsTheEditorsDocumentSymbolsOnceTheyLand()
    {
        string code = SymbolSamples.Outline.ReplaceLineEndings("\n");
        var symbols = await SymbolSamples.SymbolsOf(code);
        var language = new OutlineLanguage("outline-composer", ".ocm", symbols);
        var (model, _, _) = Editors.Make(code, language: language, path: Path.Combine(Path.GetTempPath(), "Shapes.ocm"));
        using var _m = model;
        var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Check.Equal(0, ConversationComposer.SymbolsOf(model, landed.SetResult).Count, "nothing yet: the symbols are asked off the UI thread");
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var rows = ConversationComposer.SymbolsOf(model);
        Check.True(rows.Any(r => r is { Label: "Scale", Insert: "@Scale ", Detail: "method in Shapes.ocm" }) && rows.Any(r => r is { Label: "Round", Detail: "enum member in Shapes.ocm" }),
            $"each symbol with its kind and file ({string.Join(", ", rows.Take(6).Select(r => r.Label + ": " + r.Detail))})");
        Check.True(!rows.Any(r => r.Label == "Demo.Shapes") && rows.Select(r => r.Label).Distinct().Count() == rows.Count, "no namespace, each name once");
        Check.Equal(1, language.Asked, "asked once for the version");

        var composer = new ConversationComposer { Symbols = () => ConversationComposer.SymbolsOf(model) };
        composer.Typed("see @Sca", 8);
        Check.True(composer.Suggestions.Any(s => s.Label == "Scale"), "the picker lists it");
        composer.Symbols = () => [];
        composer.RefreshSuggestions();
        Check.True(!composer.Suggestions.Any(s => s.Label == "Scale"), "listed again when the symbols change");
    }
}
