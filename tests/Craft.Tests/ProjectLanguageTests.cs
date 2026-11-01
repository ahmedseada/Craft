// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Text;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.CSharp.Project;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Json;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's notes 17 and 18: the languages of a C# project's other files in the C# plug-in (their colours, by lines,
/// incremental, on the public line-state kit since phase W) and their Format document (XML). JSON's moved with JSON to
/// its own plug-in (group WebPlugins).
/// </summary>
[TestGroup("ProjectLanguages")]
public static class ProjectLanguageTests
{
    private static readonly (string File, string Language)[] Files =
    [
        ("App.csproj", ProjectLanguages.MsBuild), ("Directory.Build.props", ProjectLanguages.MsBuild), ("Directory.Build.TARGETS", ProjectLanguages.MsBuild),
        ("Craft.sln", ProjectLanguages.Solution), ("Craft.slnx", ProjectLanguages.Xml), ("data.xml", ProjectLanguages.Xml),
        ("MainPage.xaml", ProjectLanguages.Xaml), ("app.config", ProjectLanguages.Xml), ("Resources.resx", ProjectLanguages.Xml),
        ("Pages/Counter.razor", ProjectLanguages.Razor), ("Views/Index.cshtml", ProjectLanguages.Razor),
        ("/repo/.editorconfig", ProjectLanguages.EditorConfig),
    ];

    /// <summary>Each file of a C# project and the language it opens as with the C# plug-in loaded (the Plugins group checks them from its folder).</summary>
    internal static IReadOnlyList<(string File, string Language)> ProjectFiles => Files;

    [Test]
    public static void EachExtensionResolvesToItsLanguageAndToNoneWithout()
    {
        ProjectLanguages.Unregister();
        foreach (var (file, _) in Files)
        {
            Check.True(EditorLanguages.ForFile(file) is null, $"without the languages {file} is plain text");
        }

        ProjectLanguages.Register();
        try
        {
            foreach (var (file, language) in Files)
            {
                Check.Equal(language, EditorLanguages.ForFile(file)?.Name, $"{file} opens as {language}");
            }

            Check.True(EditorLanguages.Get(ProjectLanguages.MsBuild).Rules.Brackets.Any(p => p is { Open: '<', Close: '>' }), "markup matches < and >");
            Check.Equal("<!--", EditorLanguages.Get(ProjectLanguages.Xml).Rules.BlockCommentOpen, "XML's comment");
            Check.True(EditorLanguages.ForFile("appsettings.json") is null, "JSON is its own plug-in's since phase W, not the C# plug-in's");
            Check.Equal("#", EditorLanguages.Get(ProjectLanguages.EditorConfig).Rules.LineComment, ".editorconfig's comment");
        }
        finally
        {
            ProjectLanguages.Unregister();
        }

        Check.True(EditorLanguages.ForFile("App.csproj") is null && EditorLanguages.ForFile("appsettings.json") is null, "unregistered, they are gone");
    }

    [Test]
    public static void MsBuildAndXamlHaveTheirColours()
    {
        const string project = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- the framework,
                   over two lines -->
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <TargetFramework>net10.0</TargetFramework>
                <OutputPath>$(BaseDir)bin &amp; out</OutputPath>
              </PropertyGroup>
              <ItemGroup><Compile Remove="@(Excluded)" /></ItemGroup>
            </Project>
            """;
        var spans = Classify(ProjectLanguages.MsBuild, project);
        Expect(spans, "xml", TokenClasses.PreprocessorKeyword, "the declaration");
        Expect(spans, "\"1.0\"", TokenClasses.String, "the declaration's value");
        Expect(spans, "Project", TokenClasses.Keyword, "an element's name");
        Expect(spans, "Sdk", TokenClasses.PropertyName, "an attribute's name");
        Expect(spans, "\"Microsoft.NET.Sdk\"", TokenClasses.String, "an attribute's value");
        Expect(spans, "<!-- the framework,", TokenClasses.Comment, "a comment's first line");
        Expect(spans, "       over two lines -->", TokenClasses.Comment, "a comment's second line (the state across lines)");
        Expect(spans, "Configuration", TokenClasses.ParameterName, "$(Property) in a value");
        Expect(spans, "BaseDir", TokenClasses.ParameterName, "$(Property) in text");
        Expect(spans, "Excluded", TokenClasses.ParameterName, "@(Item)");
        Expect(spans, "&amp;", TokenClasses.StringEscape, "an entity");
        Expect(spans, "/>", TokenClasses.Punctuation, "an empty element's end");
        Check.True(!spans.Any(s => s.Text == "net10.0"), "an element's text is plain");

        var xaml = Classify(ProjectLanguages.Xaml, """<Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><x:Button x:Name="Go" /></Grid>""");
        Expect(xaml, "x", TokenClasses.NamespaceName, "a namespace prefix");
        Expect(xaml, "Name", TokenClasses.PropertyName, "a prefixed attribute's own name");
        Expect(xaml, "Button", TokenClasses.Keyword, "a prefixed element's own name");
    }

    [Test]
    public static void RazorColoursItsMarkupAndItsCSharp()
    {
        const string razor = """
            @page "/counter"
            @using System.Text
            <h1 class="title">Count: @currentCount</h1>
            <button @onclick="IncrementCount" title="@(Label + 1)">Click</button>
            @* a Razor comment *@
            @if (currentCount > 3)
            {
                <p>Big: @(currentCount * 2)</p>
            }
            else
            {
                <p>small</p>
            }
            @code {
                private int currentCount = 0;
                // a comment
                private string Label => "x";
                private void IncrementCount()
                {
                    currentCount++;
                }
            }
            <footer>after</footer>
            """;
        var spans = Classify(ProjectLanguages.Razor, razor);
        Expect(spans, "@page", TokenClasses.Keyword, "a directive");
        Expect(spans, "\"/counter\"", TokenClasses.String, "the directive's C#");
        Expect(spans, "@using", TokenClasses.Keyword, "@using");
        Expect(spans, "h1", TokenClasses.Keyword, "an element");
        Expect(spans, "\"title\"", TokenClasses.String, "an attribute's value");
        Expect(spans, "@", TokenClasses.PreprocessorKeyword, "@expr's transition");
        Expect(spans, "@onclick", TokenClasses.Keyword, "a directive attribute");
        Expect(spans, "@* a Razor comment *@", TokenClasses.Comment, "a Razor comment");
        Expect(spans, "@if", TokenClasses.ControlKeyword, "@if");
        Expect(spans, ">", TokenClasses.Operator, "@if's condition is C#");
        Expect(spans, "3", TokenClasses.Number, "a number in the condition");
        Expect(spans, "p", TokenClasses.Keyword, "markup inside the block");
        Expect(spans, "2", TokenClasses.Number, "@( ) inside the block's markup");
        Expect(spans, "*", TokenClasses.Operator, "@( )'s operator");
        Expect(spans, "else", TokenClasses.ControlKeyword, "else on the line after the block");
        Expect(spans, "@code", TokenClasses.Keyword, "@code");
        Expect(spans, "private", TokenClasses.Keyword, "@code's C# keyword");
        Expect(spans, "int", TokenClasses.Keyword, "a C# type keyword");
        Expect(spans, "0", TokenClasses.Number, "a C# number");
        Expect(spans, "// a comment", TokenClasses.Comment, "a C# comment");
        Expect(spans, "\"x\"", TokenClasses.String, "a C# string");
        Expect(spans, "void", TokenClasses.Keyword, "void");
        Expect(spans, "footer", TokenClasses.Keyword, "markup again after @code's closing brace");
        Check.True(!spans.Any(s => s.Text is "Click" or "after" or "small"), "markup's text is plain");
        Check.True(!spans.Any(s => s.Text == "@currentCount"), "the transition and its expression are separate spans");
    }

    [Test]
    public static void EditorConfigAndSolutionHaveTheirColours()
    {
        var config = Classify(ProjectLanguages.EditorConfig, """
            # top
            root = true
            [*.{cs,csx}]
            indent_size = 4
            dotnet_style_qualification_for_field = false:warning
            """);
        Expect(config, "# top", TokenClasses.Comment, "a comment");
        Expect(config, "*.{cs,csx}", TokenClasses.ClassName, "a section's glob");
        Expect(config, "indent_size", TokenClasses.PropertyName, "a key");
        Expect(config, "4", TokenClasses.Number, "a number");
        Expect(config, "false", TokenClasses.Keyword, "a flag");
        Expect(config, "warning", TokenClasses.Keyword, "a severity");

        var solution = Classify(ProjectLanguages.Solution, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            VisualStudioVersion = 17.0.31903.59
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{11111111-2222-3333-4444-555555555555}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		{11111111-2222-3333-4444-555555555555}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """);
        Expect(solution, "Microsoft Visual Studio Solution File, Format Version 12.00", TokenClasses.Comment, "the header");
        Expect(solution, "VisualStudioVersion", TokenClasses.PropertyName, "a setting's key");
        Expect(solution, "17.0.31903.59", TokenClasses.Number, "a version");
        Expect(solution, "Project", TokenClasses.Keyword, "Project");
        Expect(solution, "\"App\\App.csproj\"", TokenClasses.String, "a project's path");
        Expect(solution, "SolutionConfigurationPlatforms", TokenClasses.ClassName, "a section's name");
        Expect(solution, "preSolution", TokenClasses.Keyword, "preSolution");
        Expect(solution, "{11111111-2222-3333-4444-555555555555}", TokenClasses.Number, "a GUID");
        Expect(solution, ".Debug|Any CPU.ActiveCfg", TokenClasses.PropertyName, "a configuration's key");
        Expect(solution, "EndGlobal", TokenClasses.Keyword, "EndGlobal");
    }

    [Test]
    public static void ClassificationIsIncrementalAndEqualsAFreshSessionsAfterEveryEdit()
    {
        // Results proven unchanged (rule 71): after each random edit, every line's spans equal those of a session opened
        // on the edited text; and a change that does not move a state settles at once (only the lines on screen redone).
        var lines = new List<string>();
        for (int n = 0; n < 2000; n++)
        {
            lines.Add(n % 7 == 0 ? $"  <!-- note {n} -->" : $"  <Item Include=\"File{n}.cs\" Condition=\"'$(X)' == '{n}'\" />");
        }

        string text = "<Project>\n" + string.Join('\n', lines) + "\n</Project>\n";
        var language = EditorLanguages.Find(ProjectLanguages.MsBuild) ?? ProjectLanguages.All.First(l => l.Name == ProjectLanguages.MsBuild);
        var buffer = new PieceTableBuffer(text);
        using var session = (LineStateSession)language.Open(buffer);
        using var spans = new TokenSpanBuffer();
        var last = buffer.Current.GetLine(buffer.Current.LineCount - 2);
        session.Classify(buffer.Current, last.Start, last.Length, spans);
        Check.Equal(buffer.Current.LineCount - 1, session.KnownLines, "classifying the last line learns the states above it");
        long scanned = session.LinesScanned;

        int at = buffer.Current.GetLine(10).Start + 4;
        buffer.Apply([TextEdit.Insert(at, "x")]);
        var top = buffer.Current.GetLine(10);
        spans.Clear();
        session.Classify(buffer.Current, top.Start, top.Length, spans);
        Check.True(session.KnownLines >= buffer.Current.LineCount - 1, $"a change inside a name settles on the line below it ({session.KnownLines} lines known)");
        Check.True(session.LinesScanned - scanned <= 1, $"and redoes no line below the screen ({session.LinesScanned - scanned})");

        // Line 5 opens a comment that the note on line 8 closes: line 6 is a comment now, line 9 is markup again.
        buffer.Apply([TextEdit.Insert(buffer.Current.GetLine(5).Start, "<!--")]);
        var below = buffer.Current.GetLine(6);
        spans.Clear();
        session.Classify(buffer.Current, below.Start, below.Length, spans);
        Check.True(spans.Count == 1 && spans.Spans[0] == new TokenSpan(below.Start, below.Length, TokenClasses.Comment), "an opened comment reaches the lines below it");
        var after = buffer.Current.GetLine(9);
        spans.Clear();
        session.Classify(buffer.Current, after.Start, after.Length, spans);
        Check.True(spans.Count > 1 && spans.Spans[1].Class == TokenClasses.Keyword, "and ends where the next '-->' closes it");

        var random = new Random(17);
        string[] fragments = ["<", ">", "</Item>", "<!--", "-->", "\"", "'", "\n", "$(", ")", "&amp;", "<![CDATA[", "]]>", "<?x ", "?>", " a=\"b\"", "x"];
        foreach (var name in new[] { ProjectLanguages.MsBuild, ProjectLanguages.Razor, JsonLanguage.LanguageName })
        {
            var each = name == JsonLanguage.LanguageName ? JsonLanguage.Language : ProjectLanguages.All.First(l => l.Name == name);
            var edited = new PieceTableBuffer(name == JsonLanguage.LanguageName ? "{\n  \"a\": [1, 2, /* x */ 3],\n  \"b\": \"c\"\n}\n" : SampleRazorish);
            using var live = each.Open(edited);
            for (int round = 0; round < 150; round++)
            {
                int length = edited.Current.Length;
                int start = random.Next(0, length + 1);
                int remove = random.Next(4) == 0 ? random.Next(0, Math.Min(length - start, 12) + 1) : 0;
                string insert = random.Next(4) == 0 ? (name == ProjectLanguages.Razor ? RazorFragments[random.Next(RazorFragments.Length)] : "/*") : fragments[random.Next(fragments.Length)];
                edited.Apply([new TextEdit(start, remove, insert)]);
                int line = random.Next(0, edited.Current.LineCount);
                var shown = edited.Current.GetLine(line);
                spans.Clear();
                live.Classify(edited.Current, shown.Start, shown.Length, spans);   // classify part, as a paint does
                if (round % 10 == 9)
                {
                    var fresh = new PieceTableBuffer(edited.Current.GetText(0, edited.Current.Length));
                    using var again = each.Open(fresh);
                    using var expected = new TokenSpanBuffer();
                    spans.Clear();
                    live.Classify(edited.Current, 0, edited.Current.Length, spans);
                    again.Classify(fresh.Current, 0, fresh.Current.Length, expected);
                    Check.SequenceEqual(expected.Spans.ToArray(), spans.Spans.ToArray(), $"{name}, round {round}: the spans equal a fresh session's");
                }
            }
        }
    }

    private const string SampleRazorish = """
        @page "/x"
        <div class="a">@Model.Name</div>
        @code {
            /* a
               block */
            private string s = @"verbatim
        over lines";
            private string r = \"\"\"
                raw
                \"\"\";
        }
        <p>end</p>
        """;

    private static readonly string[] RazorFragments = ["@{", "}", "@code {", "@*", "*@", "@if (a) {", "<p>", "\"", "@\"", "/*", "*/", "\n", "@x.y", "@(1)", "{"];

    [Test]
    public static void XmlFormatsKeepsTextAndMixedContentAndIsIdempotent()
    {
        const string messy = "<?xml version=\"1.0\"?>\n<Project Sdk=\"Microsoft.NET.Sdk\">\n<PropertyGroup>\n      <TargetFramework>net10.0</TargetFramework> <!-- the framework -->\n<Nullable>enable</Nullable>\n</PropertyGroup>\n\n\n  <ItemGroup><PackageReference Include=\"A\"\n Version=\"1.0\" /><None></None>\n<Doc xml:space=\"preserve\">  <b>  kept </b>  </Doc><Mixed>text <i>in</i> it</Mixed>\n  </ItemGroup>\n</Project>\n";
        const string formatted = "<?xml version=\"1.0\"?>\n<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework> <!-- the framework -->\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n\n  <ItemGroup>\n    <PackageReference Include=\"A\"\n      Version=\"1.0\" />\n    <None></None>\n    <Doc xml:space=\"preserve\">  <b>  kept </b>  </Doc>\n    <Mixed>text <i>in</i> it</Mixed>\n  </ItemGroup>\n</Project>\n";
        var style = new FormatStyle("  ", "\n");
        Check.Equal(formatted, Format(new XmlFormatter(), messy, style), "XML formatted: indented, text, mixed and preserved content kept, an attribute's line indented past its tag");
        Check.Equal(formatted, Format(new XmlFormatter(), formatted, style), "formatting again changes nothing");
        var edits = new List<TextEdit>();
        new XmlFormatter().Format(formatted, style, edits);
        Check.Equal(0, edits.Count, "and makes no edits");
        Check.Equal("<a>\n\t<b />\n\t<!-- c -->\n</a>", Format(new XmlFormatter(), "<a><b /><!-- c --></a>", new FormatStyle("\t", "\n")), "tabs when the editor indents with tabs");

        foreach (var (bad, where) in new[] { ("<a><b></a>", "line 1, column 7"), ("<a>\n<b>", "line 2"), ("<a x=1></a>", "line 1"), ("<a></b>", "line 1, column 4"), ("<a><!-- open</a>", "line 1, column 4"), ("<a attr=\"x></a>", "line 1") })
        {
            var error = Check.Throws<FormatException>(() => Format(new XmlFormatter(), bad, style), $"malformed XML is refused: {bad}");
            Check.True(error.Message.StartsWith(where, StringComparison.Ordinal), $"the message says where ({error.Message})");
        }
    }

    internal static string Format(ITextFormatter formatter, string text, FormatStyle style)
    {
        var edits = new List<TextEdit>();
        formatter.Format(text, style, edits);
        var buffer = new PieceTableBuffer(text);
        buffer.Apply(edits.ToArray());
        return buffer.Current.GetText(0, buffer.Current.Length);
    }

    internal static List<(string Text, string Class)> Classify(string languageName, string text)
    {
        var language = languageName == JsonLanguage.LanguageName ? JsonLanguage.Language : ProjectLanguages.All.First(l => l.Name == languageName);
        var buffer = new PieceTableBuffer(text);
        using var session = language.Open(buffer);
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        var result = new List<(string, string)>();
        int end = 0;
        foreach (var span in spans.Spans)
        {
            Check.True(span.Start >= end, $"{languageName}: the spans are in order and do not overlap (at {span.Start})");
            end = span.End;
            result.Add((text.Substring(span.Start, span.Length), span.Class));
        }

        return result;
    }

    internal static void Expect(List<(string Text, string Class)> spans, string text, string tokenClass, string what)
    {
        Check.True(spans.Any(s => s.Text == text && s.Class == tokenClass),
            $"{what}: \"{text}\" is {tokenClass} (found: {string.Join(", ", spans.Where(s => s.Text == text).Select(s => s.Class).DefaultIfEmpty("no span"))})");
    }

    internal sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    internal sealed class SilentUi : IShellUi
    {
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);
    }
}
