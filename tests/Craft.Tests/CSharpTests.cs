// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

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
    public static void IncrementalParsesEqualFreshParses()
    {
        var random = new Random(2026);
        var buffer = new PieceTableBuffer(Realistic);
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        for (int step = 0; step < 2500; step++)
        {
            buffer.Apply(RandomEdits(random, buffer.Current.Length));
            var snapshot = buffer.Current;
            var text = snapshot.GetText(0, snapshot.Length);
            Check.True(ReferenceEquals(session.Snapshot, snapshot), $"step {step}: the session follows the buffer");
            Check.Equal(text, session.Tree.GetText().ToString(), $"step {step}: the session's tree is of the snapshot's text");
            var fresh = CSharpSyntaxTree.ParseText(text, language.ParseOptions);
            Check.True(session.Tree.GetRoot().IsEquivalentTo(fresh.GetRoot(), topLevel: false), $"step {step}: the incremental tree equals a fresh parse");
            for (int r = 0; r < 3; r++)
            {
                int start = random.Next(0, text.Length + 1);
                int length = random.Next(0, Math.Min(400, text.Length - start) + 1);
                using var incremental = new TokenSpanBuffer();
                using var full = new TokenSpanBuffer();
                session.Classify(snapshot, start, length, incremental);
                CSharpClassifier.Classify(fresh.GetRoot(), start, length, full);
                Check.SequenceEqual(full.Spans.ToArray(), incremental.Spans.ToArray(), $"step {step}: the spans of ({start}, {length})");
            }
        }

        Check.True(session.Updates == 2501, "one parse a change, the first included");
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
            using var spans = new TokenSpanBuffer();
            script.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            Check.Equal(TokenClasses.PreprocessorKeyword, spans.Spans[0].Class, "a script's #r is a directive");
        }
        finally
        {
            CSharpLanguage.Unregister();
        }
    }

    internal static List<TokenSpan> Classify(string code, int start, int length)
    {
        var buffer = new PieceTableBuffer(code);
        using var session = new CSharpLanguage().Open(buffer);
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
            var language = EditorLanguages.ForFile("a.cs");
            Check.Equal("csharp", language?.Name, "with it, .cs opens as C#");
            Check.True(AssemblyLoadContext.GetLoadContext(language!.GetType().Assembly) != AssemblyLoadContext.Default, "the language comes from the plug-in's own load context");
            Check.True(AssemblyLoadContext.GetLoadContext(typeof(ILanguage).Assembly) == AssemblyLoadContext.Default, "the contracts stay the host's");

            var buffer = new PieceTableBuffer("class A { }");
            using var session = language.Open(buffer);
            using var spans = new TokenSpanBuffer();
            session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            Check.Equal(TokenClasses.ClassName, spans.Spans[1].Class, "the plug-in's session classifies");

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
            if (name.StartsWith("Craft.CSharp.", StringComparison.Ordinal) || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(folder, name));
            }
        }

        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")), "the plug-in carries no copy of the shared contracts");
    }
}
