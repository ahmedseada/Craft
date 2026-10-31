// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Craft.Tests;

/// <summary>C# on Roslyn: the snapshot as SourceText, fixed snippets' spans, and incremental parses against fresh ones.</summary>
[TestGroup("CSharp")]
public static class CSharpTests
{
    internal const string Realistic = """"
        // A realistic file for the incremental tests.
        using System;
        using System.Collections.Generic;

        namespace Sample.Code;

        /// <summary>A store of <see cref="Item"/>s.</summary>
        public sealed class Store<T> where T : notnull
        {
            private readonly List<T> _items = [];
            private int _count;

            public int Count { get => _count; init => _count = value; }

            public string Describe(int index, T item)
            {
        #if DEBUG
                Console.WriteLine($"debug {index}");
        #else
                var unused = 0;
        #endif
                if (index > 0 && _count++ < 0x10)
                {
                    _items.Add(item); // keep it
                    return $"{index,4:D2} of {nameof(Describe)}: \t{item}";
                }

                var raw = """
                    raw "text" {index}
                    """;
                /* a block
                   comment */
                return raw + @"c:\path ""quoted""" + 'c' + 1.5m.ToString() + $$"""{{index}} {not}""";
            }

            public IEnumerable<int> Numbers()
            {
                foreach (var n in _items) { yield return n.GetHashCode(); }
                switch (_count) { case 1: yield break; default: break; }
            }
        }

        public record Item(string Name, int Size);
        """";

    [Test]
    public static void SnippetsHaveTheExpectedSpans()
    {
        foreach (var (name, snippet) in new[]
        {
            ("strings", CSharpSnippets.Strings), ("comments, docs and directives", CSharpSnippets.CommentsDocsAndDirectives),
            ("keywords in and out of position", CSharpSnippets.KeywordsInAndOutOfPosition), ("an interpolated raw string's holes", CSharpSnippets.InterpolatedRawStringHoles),
        })
        {
            var spans = Classify(snippet.Code, 0, snippet.Code.Length);
            var actual = spans.Select(s => (snippet.Code.Substring(s.Start, s.Length), s.Class)).ToList();
            Check.SequenceEqual(snippet.Spans, actual, $"the spans of {name}");
            CheckOrdered(spans, name);
        }
    }

    [Test]
    public static void ARangeGetsTheSpansThatOverlapIt()
    {
        const string Code = "int a;\n/* one\ntwo\nthree */ int b;\nint c;\n";
        int start = Code.IndexOf("two", StringComparison.Ordinal);
        var spans = Classify(Code, start, 4);
        Check.True(spans.Count == 1 && spans[0].Class == TokenClasses.Comment && spans[0].Start < start, "a block comment that began above the range is given whole");
        var line = Classify(Code, Code.IndexOf("int c", StringComparison.Ordinal), 6);
        Check.SequenceEqual(["int", "c", ";"], line.Select(s => Code.Substring(s.Start, s.Length)).ToList(), "only the line's tokens");
    }

    [Test]
    public static void SnapshotSourceTextEqualsSourceTextFrom()
    {
        var random = new Random(31);
        var buffer = new PieceTableBuffer(Realistic, new CraftOptions { BufferChunkCharacters = 64 });
        var previous = new SnapshotSourceText(buffer.Current);
        SnapshotSourceText? next = null;
        buffer.Changed += (_, change) => next = SnapshotSourceText.FromChange(change);
        for (int step = 0; step < 500; step++)
        {
            var edits = RandomEdits(random, buffer.Current.Length);
            buffer.Apply(edits);
            var text = buffer.Current.GetText(0, buffer.Current.Length);
            var expected = SourceText.From(text);
            var actual = next!;
            Check.Equal(expected.Length, actual.Length, $"step {step}: Length");
            Check.Equal(expected.ToString(), actual.ToString(), $"step {step}: ToString");
            for (int i = 0; i < 5; i++)
            {
                int at = random.Next(0, text.Length + 1);
                int count = random.Next(0, Math.Min(200, text.Length - at) + 1);
                var a = new char[count];
                var b = new char[count];
                expected.CopyTo(at, a, 0, count);
                actual.CopyTo(at, b, 0, count);
                Check.True(a.AsSpan().SequenceEqual(b), $"step {step}: CopyTo({at}, {count})");
                Check.Equal(expected.ToString(new TextSpan(at, count)), actual.ToString(new TextSpan(at, count)), $"step {step}: ToString of a span");
                if (at < text.Length)
                {
                    Check.Equal(expected[at], actual[at], $"step {step}: this[{at}]");
                }
            }

            Check.Equal(expected.Lines.Count, actual.Lines.Count, $"step {step}: Lines.Count");
            var ranges = actual.GetChangeRanges(previous);
            var wanted = edits.Select(e => new TextChangeRange(new TextSpan(e.Start, e.Length), e.Text.Length)).ToList();
            Check.SequenceEqual(wanted, ranges.ToList(), $"step {step}: the change ranges against the previous version are the buffer's edits");
            Check.Equal(0, actual.GetChangeRanges(actual).Count, $"step {step}: no change against itself");
            previous = actual;
        }

        var unrelated = new SnapshotSourceText(new PieceTableBuffer("other").Current);
        Check.Equal(1, previous.GetChangeRanges(unrelated).Count, "against an unrelated text, one change of the whole (the base answer)");
    }

    [Test]
    public static async Task IncrementalParsesEqualFreshParses()
    {
        var random = new Random(2026);
        var buffer = new PieceTableBuffer(Realistic);
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        await session.WhenIdle();
        for (int step = 0; step < 2500; step++)
        {
            buffer.Apply(RandomEdits(random, buffer.Current.Length));
            await session.WhenIdle();
            var snapshot = buffer.Current;
            var text = snapshot.GetText(0, snapshot.Length);
            Check.True(session.ParseError is null, $"step {step}: the parse threw {session.ParseError}");
            Check.True(ReferenceEquals(session.Snapshot, snapshot), $"step {step}: the session follows the buffer once its worker is idle");
            Check.Equal(text, TreeText(session), $"step {step}: the session's tree is of the snapshot's text");
            var fresh = FreshRoot(text, language);
            Check.True(SameTree(session, fresh), $"step {step}: the incremental tree equals a fresh parse");
            for (int r = 0; r < 3; r++)
            {
                int start = random.Next(0, text.Length + 1);
                int length = random.Next(0, Math.Min(400, text.Length - start) + 1);
                using var incremental = new TokenSpanBuffer();
                using var full = new TokenSpanBuffer();
                session.Classify(snapshot, start, length, incremental);
                CSharpClassifier.Classify(fresh, start, length, full);
                Check.SequenceEqual(full.Spans.ToArray(), incremental.Spans.ToArray(), $"step {step}: the spans of ({start}, {length})");
            }
        }

        Check.True(session.Updates == 2501, $"one parse a change when each is waited for, the first included (were {session.Updates})");
    }

    [Test]
    public static async Task SettledSpansEqualAFreshParse()
    {
        // Thousands of random edits with no wait between them: the worker composes whatever versions arrived into one
        // parse; once it is idle, the spans equal a fresh parse of the same text.
        var random = new Random(4242);
        var buffer = new PieceTableBuffer(Realistic + Realistic.Replace("Store", "Other", StringComparison.Ordinal));
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        for (int step = 1; step <= 4000; step++)
        {
            buffer.Apply(RandomEdits(random, buffer.Current.Length));
            if (step % 40 != 0 && random.Next(25) != 0)
            {
                continue;
            }

            await session.WhenIdle();
            var snapshot = buffer.Current;
            var text = snapshot.GetText(0, snapshot.Length);
            Check.True(session.ParseError is null, $"step {step}: the parse threw {session.ParseError}");
            Check.True(ReferenceEquals(session.Snapshot, snapshot), $"step {step}: idle, the session's tree is of the newest snapshot");
            Check.Equal(text, TreeText(session), $"step {step}: the tree's text is the snapshot's");
            var fresh = FreshRoot(text, language);
            Check.True(SameTree(session, fresh), $"step {step}: the composed incremental tree equals a fresh parse");
            Check.True(session.WaitUntilClassified(snapshot, TimeSpan.Zero), $"step {step}: idle, the snapshot is classified without a wait");
            for (int r = 0; r < 5; r++)
            {
                int start = random.Next(0, text.Length + 1);
                int length = random.Next(0, Math.Min(600, text.Length - start) + 1);
                using var settled = new TokenSpanBuffer();
                using var full = new TokenSpanBuffer();
                session.Classify(snapshot, start, length, settled);
                CSharpClassifier.Classify(fresh, start, length, full);
                Check.SequenceEqual(full.Spans.ToArray(), settled.Spans.ToArray(), $"step {step}: the settled spans of ({start}, {length})");
            }
        }

        Check.True(session.Updates < 4001, $"versions that arrived during a parse were composed into fewer parses ({session.Updates} for 4,000 changes)");
    }

    [Test]
    public static async Task StaleSpansKeepTheTextAwayFromTheEdit()
    {
        // The worker held (as if its parse were slow) and no wait: spans away from the edited lines are the fresh parse's,
        // shifted, and none overlaps or touches the changed text, which draws plain until the parse lands.
        var code = new StringBuilder("namespace N;\n\npublic sealed class C\n{\n");
        for (int i = 0; i < 200; i++)
        {
            code.Append(CultureInfo.InvariantCulture, $"    public int M{i}(int value) {{ int local{i} = value + {i}; return local{i} * 2; }} // note {i}\n");
        }

        code.Append("}\n");
        var random = new Random(77);
        var buffer = new PieceTableBuffer(code.ToString());
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        await session.WhenIdle();
        int parses = session.Updates;
        var before = buffer.Current;
        session.PauseParsing();
        try
        {
            // Letters typed into (and deleted from) the locals' names of lines 100 to 110: the meaning of the rest stays.
            for (int k = 0; k < 60; k++)
            {
                var snapshot = buffer.Current;
                var line = snapshot.GetLine(random.Next(104, 115));
                var text = snapshot.GetText(line.Start, line.Length);
                int at = line.Start + text.IndexOf("local", StringComparison.Ordinal) + 3;
                buffer.Apply(random.Next(3) == 0 && snapshot[at] == 'q' ? [TextEdit.Delete(at, 1)] : [TextEdit.Insert(at, "q")]);
            }

            var now = buffer.Current;
            var newText = now.GetText(0, now.Length);
            var oldText = before.GetText(0, before.Length);
            int prefix = 0;
            while (prefix < Math.Min(oldText.Length, newText.Length) && oldText[prefix] == newText[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < Math.Min(oldText.Length, newText.Length) - prefix && oldText[^(suffix + 1)] == newText[^(suffix + 1)])
            {
                suffix++;
            }

            int changedEnd = newText.Length - suffix;
            Check.True(!session.WaitUntilClassified(now, TimeSpan.Zero), "with no wait and the worker held, the newest snapshot is not classified yet");
            Check.True(ReferenceEquals(session.Snapshot, before), "the session still holds the tree of the text before the edits");
            var fresh = FreshRoot(newText, language);
            // Every edit was on lines 104 to 114 (no line break typed), whatever the final difference is.
            const int firstChangedLine = 104;
            const int lastChangedLine = 114;
            for (int r = 0; r < 300; r++)
            {
                int first = random.Next(0, now.LineCount);
                int last = Math.Min(now.LineCount - 1, first + random.Next(0, 6));
                if (last >= firstChangedLine - 1 && first <= lastChangedLine + 1)
                {
                    continue;
                }

                int start = now.GetLine(first).Start;
                int length = now.GetLine(last).EndIncludingBreak - start;
                using var stale = new TokenSpanBuffer();
                using var full = new TokenSpanBuffer();
                session.Classify(now, start, length, stale);
                CSharpClassifier.Classify(fresh, start, length, full);
                Check.SequenceEqual(full.Spans.ToArray(), stale.Spans.ToArray(), $"the stale spans of lines {first}-{last}, away from the edited lines {firstChangedLine}-{lastChangedLine}");
            }

            using var whole = new TokenSpanBuffer();
            session.Classify(now, 0, now.Length, whole);
            foreach (var span in whole.Spans)
            {
                Check.True(span.End < prefix || span.Start > changedEnd, $"the stale span {span} overlaps or touches the changed text {prefix}-{changedEnd}");
            }

            Check.True(whole.Count > 1000, $"the text away from the edit keeps its spans ({whole.Count})");
        }
        finally
        {
            session.ResumeParsing();
        }

        await session.WhenIdle();
        Check.Equal(parses + 1, session.Updates, "the 60 versions that arrived while the worker was held are one parse");
        Check.True(ReferenceEquals(session.Snapshot, buffer.Current), "and it is of the newest snapshot");
    }

    [Test]
    public static async Task ManyVersionsComposeIntoOneParse()
    {
        var random = new Random(9);
        var buffer = new PieceTableBuffer(Realistic);
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        int events = 0;
        session.Reclassified += (_, _) => Interlocked.Increment(ref events);
        await session.WhenIdle();
        for (int round = 0; round < 40; round++)
        {
            int parses = session.Updates;
            int raised = Volatile.Read(ref events);
            session.PauseParsing();
            int versions = random.Next(2, 30);
            for (int v = 0; v < versions; v++)
            {
                buffer.Apply(RandomEdits(random, buffer.Current.Length));
            }

            Check.True(session.IsParsing, $"round {round}: a held worker is still parsing");
            session.ResumeParsing();
            await session.WhenIdle();
            var text = buffer.Current.GetText(0, buffer.Current.Length);
            Check.Equal(parses + 1, session.Updates, $"round {round}: {versions} versions, one parse");
            Check.Equal(raised + 1, Volatile.Read(ref events), $"round {round}: Reclassified raised once for the landed parse");
            Check.Equal(text, TreeText(session), $"round {round}: the tree's text is the snapshot's");
            Check.True(SameTree(session, FreshRoot(text, language)), $"round {round}: the composed parse equals a fresh one");
        }

        for (int i = 0; i < 5; i++)
        {
            int raised = Volatile.Read(ref events);
            buffer.Apply([TextEdit.Insert(0, " ")]);
            await session.WhenIdle();
            Check.Equal(raised + 1, Volatile.Read(ref events), $"edit {i}: one Reclassified per landed parse");
        }
    }

    [Test]
    public static async Task TheFirstParseLandsOffTheCallingThreadAndDisposeStopsTheWorker()
    {
        var big = string.Concat(Enumerable.Repeat(Realistic.Replace("namespace Sample.Code;", "", StringComparison.Ordinal), 300));
        var buffer = new PieceTableBuffer(big);
        var language = new CSharpLanguage();
        var session = language.Open(buffer);
        int events = 0;
        session.Reclassified += (_, _) => Interlocked.Increment(ref events);
        session.PauseParsing();
        using (var spans = new TokenSpanBuffer())
        {
            session.Classify(buffer.Current, 0, 200, spans);
            Check.Equal(0, spans.Count, "before the first parse lands the text is plain");
        }

        Check.True(!session.WaitUntilClassified(buffer.Current, TimeSpan.FromMilliseconds(5)), "a wait for a held parse ends at its budget");
        session.ResumeParsing();
        Check.True(session.WaitUntilClassified(buffer.Current, Timeout.InfiniteTimeSpan), "the first parse lands");
        await session.WhenIdle();
        Check.Equal(1, Volatile.Read(ref events), "its landing raised Reclassified once");

        buffer.Apply([TextEdit.Insert(0, "/*")]);
        session.Dispose();
        var stopped = await Task.WhenAny(session.WhenIdle(), Task.Delay(TimeSpan.FromSeconds(30)));
        Check.True(stopped == session.WhenIdle(), "Dispose stops the worker (its idle task completes)");
        Check.True(!session.IsParsing, "no worker runs after Dispose");
        int updates = session.Updates;
        buffer.Apply([TextEdit.Insert(0, "x")]);
        Check.True(!session.IsParsing && session.Updates == updates, "a change after Dispose starts no parse");
        Check.Throws<ObjectDisposedException>(() => session.Classify(buffer.Current, 0, 1, new TokenSpanBuffer()), "Classify after Dispose");
        session.Dispose();
    }

    [Test]
    public static async Task AParseThatThrowsIsReportedNotThrown()
    {
        var buffer = new PieceTableBuffer(Realistic);
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        await session.WhenIdle();
        var parsed = session.Snapshot;
        int fail = 1;
        session.BeforeParse = () =>
        {
            if (Interlocked.Exchange(ref fail, 0) == 1)
            {
                throw new InvalidOperationException("a parse failure from the test");
            }
        };
        int at = Realistic.IndexOf("_items.Add", StringComparison.Ordinal);
        buffer.Apply([TextEdit.Insert(at, "q")]);
        await session.WhenIdle();
        Check.True(session.ParseError is InvalidOperationException { Message: "a parse failure from the test" }, $"the error is kept ({session.ParseError})");
        Check.True(ReferenceEquals(session.Snapshot, parsed), "the last tree stays in use");
        using (var spans = new TokenSpanBuffer())
        {
            session.Classify(buffer.Current, 0, 40, spans);
            Check.True(spans.Count > 0, "the text away from the change still classifies from the last tree");
        }

        buffer.Apply([TextEdit.Insert(0, " ")]);
        await session.WhenIdle();
        Check.True(session.ParseError is null, "the next parse succeeds and clears the error");
        var text = buffer.Current.GetText(0, buffer.Current.Length);
        Check.True(ReferenceEquals(session.Snapshot, buffer.Current) && SameTree(session, FreshRoot(text, language)),
            "it parses the file afresh to the newest snapshot");
    }

    [Test]
    public static void TheTokenWalkEqualsDescendantTokens()
    {
        // The classifier walks from the range's first token (FindToken, GetNextToken); Roslyn's DescendantTokens(range) is
        // the reference. Random texts (edits of every kind, broken code included) and random ranges, empty ones too.
        var random = new Random(1234);
        var buffer = new PieceTableBuffer(Realistic + CSharpSnippets.Strings.Code + CSharpSnippets.CommentsDocsAndDirectives.Code + CSharpSnippets.InterpolatedRawStringHoles.Code);
        var language = new CSharpLanguage();
        using var walked = new TokenSpanBuffer();
        using var reference = new TokenSpanBuffer();
        for (int step = 0; step < 600; step++)
        {
            buffer.Apply(RandomEdits(random, buffer.Current.Length));
            var text = buffer.Current.GetText(0, buffer.Current.Length);
            var root = FreshRoot(text, language);
            for (int r = 0; r < 20; r++)
            {
                int start = random.Next(0, text.Length + 1);
                int length = r % 7 == 0 ? 0 : random.Next(0, Math.Min(r % 2 == 0 ? 60 : 900, text.Length - start) + 1);
                walked.Clear();
                reference.Clear();
                CSharpClassifier.Classify(root, start, length, walked);
                CSharpClassifier.ClassifyByDescendants(root, start, length, reference);
                Check.SequenceEqual(reference.Spans.ToArray(), walked.Spans.ToArray(), $"step {step}: the walked spans of ({start}, {length})");
            }
        }

        walked.Clear();
        reference.Clear();
        var whole = FreshRoot(Realistic, language);
        CSharpClassifier.Classify(whole, 0, Realistic.Length, walked);
        CSharpClassifier.ClassifyByDescendants(whole, 0, Realistic.Length, reference);
        Check.SequenceEqual(reference.Spans.ToArray(), walked.Spans.ToArray(), "the whole text's spans");
    }

    [Test]
    public static void ComposedRangesCoverEveryEdit()
    {
        // Random versions' edits composed into one range: outside it, the oldest and the newest texts are the same.
        var random = new Random(5);
        for (int round = 0; round < 500; round++)
        {
            var buffer = new PieceTableBuffer(Realistic);
            var first = buffer.Current;
            var old = first.GetText(0, first.Length);
            TextChangeRange? composed = null;
            buffer.Changed += (_, change) => composed = SnapshotSourceText.Compose(composed, SnapshotSourceText.Collapse(change.Edits));
            int versions = random.Next(1, 12);
            for (int v = 0; v < versions; v++)
            {
                buffer.Apply(RandomEdits(random, buffer.Current.Length));
            }

            var now = buffer.Current.GetText(0, buffer.Current.Length);
            var range = composed!.Value;
            Check.Equal(now.Length - old.Length, range.NewLength - range.Span.Length, $"round {round}: the range's delta is the texts'");
            Check.Equal(old[..range.Span.Start], now[..range.Span.Start], $"round {round}: the text before the range is unchanged");
            Check.Equal(old[range.Span.End..], now[(range.Span.Start + range.NewLength)..], $"round {round}: the text after the range is unchanged, shifted");
            var since = SnapshotSourceText.Since(buffer.Current, 0, range);
            Check.SequenceEqual([range], since.GetChangeRanges(new SnapshotSourceText(first)).ToList(), $"round {round}: the text made Since version 0 answers the range against it");
        }
    }

    [Test]
    public static void TheLanguagesAreRegisteredByExtension()
    {
        CSharpLanguage.Unregister();
        try
        {
            CSharpLanguage.Register();
            Check.Equal(CSharpLanguage.RegularName, EditorLanguages.ForFile("Program.cs")?.Name, ".cs opens as C#");
            Check.Equal(CSharpLanguage.ScriptName, EditorLanguages.ForFile("build.CSX")?.Name, ".csx opens as C# script");
            var language = EditorLanguages.Get("csharp");
            Check.Equal("//", language.Rules.LineComment, "the line comment");
            Check.Equal("/*", language.Rules.BlockCommentOpen, "the block comment's opening");
            Check.Equal(3, language.Rules.Brackets.Count, "(), [] and {}");

            var buffer = new PieceTableBuffer("#r \"lib.dll\"\nvar x = 1;\n");
            using var script = EditorLanguages.Get("csharp-script").Open(buffer);
            Check.True(script.WaitUntilClassified(buffer.Current, Timeout.InfiniteTimeSpan), "the script's first parse lands");
            using var spans = new TokenSpanBuffer();
            script.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            Check.Equal(TokenClasses.PreprocessorKeyword, spans.Spans[0].Class, "a script's #r is a directive");
        }
        finally
        {
            CSharpLanguage.Unregister();
        }
    }

    private static string TreeText(CSharpLanguageSession session) => session.Tree!.GetText().ToString();

    private static SyntaxNode FreshRoot(string text, CSharpLanguage language) => CSharpSyntaxTree.ParseText(text, language.ParseOptions).GetRoot();

    private static bool SameTree(CSharpLanguageSession session, SyntaxNode fresh) => session.Tree!.GetRoot().IsEquivalentTo(fresh, topLevel: false);

    internal static List<TokenSpan> Classify(string code, int start, int length)
    {
        var buffer = new PieceTableBuffer(code);
        using var session = new CSharpLanguage().Open(buffer);
        Check.True(session.WaitUntilClassified(buffer.Current, Timeout.InfiniteTimeSpan), "the first parse lands");
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, start, length, spans);
        return [.. spans.Spans.ToArray()];
    }

    private static void CheckOrdered(List<TokenSpan> spans, string what)
    {
        for (int i = 1; i < spans.Count; i++)
        {
            Check.True(spans[i].Start >= spans[i - 1].End, $"{what}: span {i} starts after span {i - 1} ends");
        }
    }

    private static readonly string[] Fragments =
    [
        "{", "}", "(", ")", ";", "\"", "'", "/*", "*/", "//", "\n", "\r\n", "#if false\n", "#endif\n", "#region r\n", "$\"", "$\"\"\"", "\"\"\"",
        "@\"", "{x}", "\\n", "var ", "if (a) ", "return ", "x", "Foo(", "1.5", "0x1F", "<T>", "/// <summary>", "u8", "await ", "async ", "=> ",
        " ", "\t", "class K { }", "record R(int A);", "field", "value", "nameof(x)",
    ];

    private static TextEdit[] RandomEdits(Random random, int length)
    {
        int count = random.Next(1, 3);
        var edits = new List<TextEdit>();
        int at = 0;
        for (int i = 0; i < count && at <= length; i++)
        {
            int start = random.Next(at, Math.Min(length, at + 400) + 1);
            int remove = random.Next(4) == 0 ? random.Next(0, Math.Min(length - start, 30) + 1) : 0;
            var text = random.Next(5) == 0 ? "" : Fragments[random.Next(Fragments.Length)];
            if (remove == 0 && text.Length == 0)
            {
                text = "x";
            }

            edits.Add(new TextEdit(start, remove, text));
            at = start + remove;
        }

        return [.. edits];
    }
}

/// <summary>Plug-ins loaded from folders: C#, pre-installed and removable.</summary>
[TestGroup("Plugins")]
public static class PluginTests
{
    [Test]
    public static void CSharpLoadsFromItsFolderAndCanBeRemoved()
    {
        using var temp = new TempFolder();
        var plugins = Path.Combine(temp.Path, "plugins");
        var folder = Path.Combine(plugins, "Craft.CSharp");
        CopyCSharpPlugin(folder);
        CSharpLanguage.Unregister();
        IdrakPlugins.Unregister("csharp");
        Check.True(EditorLanguages.ForFile("a.cs") is null, "without the plug-in a .cs file is plain text");
        try
        {
            var result = CraftPlugins.LoadFolder(folder);
            Check.True(result.Loaded, $"the C# plug-in loads from its folder ({result.Error})");
            Check.Equal("Craft.CSharp", result.Name, "the result names the plug-in");
            Check.True(result is { Outdated: false, Manifest.ContractVersion: "1.0" }, "its plugin.json is read: built against this contract version");
            var language = EditorLanguages.ForFile("a.cs");
            Check.Equal("csharp", language?.Name, "with it, .cs opens as C#");
            Check.True(AssemblyLoadContext.GetLoadContext(language!.GetType().Assembly) != AssemblyLoadContext.Default, "the language comes from the plug-in's own load context");
            Check.True(AssemblyLoadContext.GetLoadContext(typeof(ILanguage).Assembly) == AssemblyLoadContext.Default, "the contracts stay the host's");

            var buffer = new PieceTableBuffer("class A { }");
            using var session = language.Open(buffer);
            Check.True(session.WaitUntilClassified(buffer.Current, Timeout.InfiniteTimeSpan), "the plug-in's first parse lands");
            using var spans = new TokenSpanBuffer();
            session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            Check.Equal(TokenClasses.ClassName, spans.Spans[1].Class, "the plug-in's session classifies");
            Check.True(File.Exists(Path.Combine(folder, "Microsoft.CodeAnalysis.CSharp.Features.dll")) && File.Exists(Path.Combine(folder, "System.Composition.Hosting.dll")),
                "the plug-in's folder carries Roslyn's Features and MEF");

            // The semantic layer in the plug-in's context: MEF composes from the plug-in's assemblies and completion answers.
            var stray = Path.Combine(temp.Path, "Stray.cs");
            var code = new PieceTableBuffer("class A { void M() { System.Console. } }");
            using var semantic = (ISemanticLanguageSession)language.Open(code, new LanguageOpenContext(stray, temp.Path, LanguageSettings.Default));
            int dot = code.Current.GetText(0, code.Current.Length).IndexOf("Console.", StringComparison.Ordinal) + "Console.".Length;
            var list = semantic.GetCompletionsAsync(code.Current, dot, CompletionTrigger.Typed('.'), CancellationToken.None).GetAwaiter().GetResult();
            Check.True(list is not null && list.Items.Any(i => i.Label == "WriteLine"), "the plug-in's completion offers Console.WriteLine");

            Check.True(IdrakPlugins.Unregister("csharp"), "the plug-in unregisters");
            Check.True(EditorLanguages.ForFile("a.cs") is null, "removed, a .cs file is plain text again");
            Check.True(CraftPlugins.LoadFolder(folder).Loaded && EditorLanguages.ForFile("a.cs") is not null, "loading the folder again registers it again");
            IdrakPlugins.Unregister("csharp");

            var options = CraftOptions.FromVariables(n => n switch
            {
                "CRAFT_PLUGIN_FOLDERS" => plugins,
                "CRAFT_PLUGINS_DISABLED" => "craft.csharp",
                _ => null,
            });
            var installed = CraftPlugins.LoadInstalled(options);
            var disabled = installed.Single(r => r.Name == "Craft.CSharp");
            Check.True(!disabled.Loaded && disabled.Error!.Contains("disabled", StringComparison.Ordinal), "a disabled plug-in is skipped and said so");
            Check.True(EditorLanguages.ForFile("a.cs") is null, "a disabled plug-in registers nothing");

            var broken = Path.Combine(plugins, "Broken");
            Directory.CreateDirectory(broken);
            File.WriteAllBytes(Path.Combine(broken, "Broken.dll"), Encoding.ASCII.GetBytes("not an assembly"));
            var empty = Path.Combine(plugins, "Empty");
            Directory.CreateDirectory(empty);
            var results = CraftPlugins.LoadInstalled(options with { DisabledPlugins = [] });
            Check.True(results.Single(r => r.Name == "Broken") is { Loaded: false, Error: not null }, "a broken dll is reported, not thrown");
            Check.True(results.Single(r => r.Name == "Empty") is { Loaded: false, Error: not null }, "a folder without its dll is reported");
            Check.True(results.Single(r => r.Name == "Craft.CSharp").Loaded, "the good plug-in still loads beside them");
        }
        finally
        {
            IdrakPlugins.Unregister("csharp");
            CSharpLanguage.Unregister();
        }
    }

    /// <summary>Copies Craft.CSharp's build output (its dll, its deps.json and Roslyn's dlls) into <paramref name="folder"/>.</summary>
    private static void CopyCSharpPlugin(string folder)
    {
        // tests/Craft.Tests/bin/<Configuration>/net10.0/ -> src/Craft.CSharp/bin/<Configuration>/net10.0/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = testOutput.Parent!.Name;
        var output = TestRunner.RepositoryPath("src", "Craft.CSharp", "bin", configuration, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, "Craft.CSharp.deps.json")), $"Craft.CSharp's build output is at {output}");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(output))
        {
            var name = Path.GetFileName(file);
            // Everything the plug-in's build put there (Roslyn's Workspaces and Features, MEF's System.Composition, their
            // dependencies, its plugin.json), except the contracts the host shares, as the app's copy does.
            if (!name.StartsWith("Craft.Abstractions.", StringComparison.Ordinal) && !name.StartsWith("Idrak.Abstraction.", StringComparison.Ordinal)
                && !name.StartsWith("Craft.Core.", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(folder, name));
            }
        }

        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")), "the plug-in carries no copy of the shared contracts");
    }
}
