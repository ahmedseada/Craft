// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.Loader;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Text;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Json;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;
using static Craft.Tests.ProjectLanguageTests;

namespace Craft.Tests;

/// <summary>
/// Phase W's plug-ins, W0's part: JSON as its own pre-installed, removable plug-in (Craft.Plugins.Json), moved out of the
/// C# plug-in: its colours, folding, format and icons, from its own folder, with the C# plug-in disabled; its format tests
/// moved here from the ProjectLanguages group unchanged.
/// </summary>
[TestGroup("WebPlugins")]
public static class WebPluginTests
{
    private static readonly string[] JsonFiles = ["package.json", "tsconfig.json", "appsettings.json", "Properties/launchSettings.json", "global.json", ".vscode/settings.jsonc"];

    [Test]
    public static void TheJsonPluginLoadsFromItsFolderAndRemovedOrDisabledItsFilesOpenPlain()
    {
        using var temp = new TempFolder();
        var plugins = Path.Combine(temp.Path, "plugins");
        var folder = Path.Combine(plugins, JsonPackage.PackageName);
        CopyJsonPlugin(folder);
        PluginTests.CopyCSharpPlugin(Path.Combine(plugins, "Craft.CSharp"));
        IdrakPlugins.Unregister(JsonLanguage.LanguageName);
        IdrakPlugins.Unregister("csharp");
        CSharpLanguage.Unregister();
        foreach (var file in JsonFiles)
        {
            Check.True(EditorLanguages.ForFile(file) is null, $"without the JSON plug-in {file} is plain text");
        }

        try
        {
            var result = CraftPlugins.LoadFolder(folder);
            Check.True(result.Loaded, $"the JSON plug-in loads from its folder ({result.Error})");
            Check.True(result is { Name: JsonPackage.PackageName, Outdated: false, Manifest.ContractVersion: "1.1" }, $"its plugin.json is read: built against contract 1.1 ({result.Name}, {result.Manifest?.ContractVersion})");
            Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Craft.CSharp.dll")),
                "the plug-in carries no copy of the shared contracts, and needs nothing of C#'s");
            foreach (var file in JsonFiles)
            {
                var language = EditorLanguages.ForFile(file);
                Check.True(language is LineStateLanguage { Name: JsonLanguage.LanguageName, Tokenizer: var tokenizer } && AssemblyLoadContext.GetLoadContext(tokenizer.GetType().Assembly) != AssemblyLoadContext.Default,
                    $"with the plug-in, {file} opens as JSON on the public kit, its tokenizer from the plug-in's own context ({language?.Name})");
            }

            Check.True(EditorLanguages.ForFile("Program.cs") is null, "the C# plug-in is not loaded: .cs stays plain text");
            CheckPackageJsonColours(EditorLanguages.ForFile("package.json")!);
            Check.True(FoldingRangeProviders.Find(JsonFolding.ProviderName) is { } folding && folding.Languages.Contains(JsonLanguage.LanguageName),
                "its folding provider is registered for \"json\"");
            Check.Equal("JSON with comments", FileIcons.Find(".jsonc")?.Language, "its icons are registered (.jsonc, which the library has none for)");
            Check.True(FileIcons.For("data.json") is { Language: "JSON", Shape: not null }, "a .json file shows the plug-in's JSON icon, drawn (W-files: Craft's shape kept)");
            Check.Equal("npm package", FileIcons.For("package.json").Language, "package.json shows its own icon (W-files: an exact name over the plug-in's .json)");

            Check.True(IdrakPlugins.Unregister(JsonLanguage.LanguageName), "the plug-in unregisters");
            foreach (var file in JsonFiles)
            {
                Check.True(EditorLanguages.ForFile(file) is null, $"removed, {file} is plain text again");
            }

            Check.True(FoldingRangeProviders.Find(JsonFolding.ProviderName) is null && FileIcons.Find(".jsonc") is null, "removed, its folding and its .jsonc icon go with it");
            Check.Equal("{}", FileIcons.For("a.json").Badge, "the library's own .json icon answers again");

            // The app's way: every installed folder, the C# plug-in disabled: JSON still coloured.
            var options = CraftOptions.FromVariables(n => n switch
            {
                "CRAFT_PLUGIN_FOLDERS" => plugins,
                "CRAFT_PLUGINS_DISABLED" => "craft.csharp",
                _ => null,
            });
            var installed = CraftPlugins.LoadInstalled(options);
            Check.True(installed.Single(r => r.Name == "Craft.CSharp") is { Loaded: false } csharp && csharp.Error!.Contains("disabled", StringComparison.Ordinal), "the C# plug-in is disabled");
            Check.True(installed.Single(r => r.Name == JsonPackage.PackageName).Loaded, "the JSON plug-in loads beside it");
            Check.True(EditorLanguages.ForFile("Program.cs") is null && EditorLanguages.ForFile("App.csproj") is null, "nothing of C#'s is registered");
            var json = EditorLanguages.ForFile("package.json");
            Check.Equal(JsonLanguage.LanguageName, json?.Name, "package.json opens as JSON with the C# plug-in disabled");
            CheckPackageJsonColours(json!);
            IdrakPlugins.Unregister(JsonLanguage.LanguageName);

            var disabled = CraftPlugins.LoadInstalled(options with { DisabledPlugins = ["Craft.CSharp", JsonPackage.PackageName] });
            Check.True(disabled.Single(r => r.Name == JsonPackage.PackageName) is { Loaded: false } off && off.Error!.Contains("disabled", StringComparison.Ordinal), "the JSON plug-in disabled is skipped and said so");
            Check.True(JsonFiles.All(f => EditorLanguages.ForFile(f) is null), "disabled, .json files open as plain text");
        }
        finally
        {
            IdrakPlugins.Unregister(JsonLanguage.LanguageName);
            IdrakPlugins.Unregister("csharp");
            CSharpLanguage.Unregister();
        }
    }

    [Test]
    public static void TheCSharpPluginKeepsOnlyWhatIsCSharps()
    {
        ProjectLanguages.Register();
        try
        {
            Check.True(!ProjectLanguages.Names.Contains(JsonLanguage.LanguageName, StringComparer.OrdinalIgnoreCase), $"the C# plug-in's project languages hold no JSON ({string.Join(", ", ProjectLanguages.Names)})");
            Check.True(EditorLanguages.ForFile("appsettings.json") is null && FoldingRangeProviders.Find(JsonFolding.ProviderName) is null,
                "with the C# plug-in's languages alone, appsettings.json is plain text and no JSON folding is registered");
            Check.True(ProjectLanguages.Names.All(n => EditorLanguages.Get(n) is LineStateLanguage), "every project language is the public kit's LineStateLanguage");
        }
        finally
        {
            ProjectLanguages.Unregister();
        }

        Check.True(!CSharpPlugin.Summary.Contains(".json;", StringComparison.Ordinal), "the C# plug-in's summary no longer claims .json");
    }

    [Test]
    public static void JsonHasItsColours()
    {
        const string json = """
            {
              // the log levels
              "Logging": { "LogLevel": { "Default": "Information", "Path": "c:\\temp\n" } },
              "Enabled": true, "Count": -3.5e2, "Nothing": null, /* trailing */
            }
            """;
        var spans = Classify(JsonLanguage.LanguageName, json);
        Expect(spans, "// the log levels", TokenClasses.Comment, "a line comment");
        Expect(spans, "\"Logging\"", TokenClasses.PropertyName, "a property's name");
        Expect(spans, "\"Information\"", TokenClasses.String, "a string value");
        Expect(spans, "\\\\", TokenClasses.StringEscape, "an escape");
        Expect(spans, "\\n", TokenClasses.StringEscape, "another escape");
        Expect(spans, "true", TokenClasses.Keyword, "true");
        Expect(spans, "null", TokenClasses.Keyword, "null");
        Expect(spans, "-3.5e2", TokenClasses.Number, "a number");
        Expect(spans, "/* trailing */", TokenClasses.Comment, "a block comment");
        Expect(spans, "{", TokenClasses.Punctuation, "a brace");
        Check.Equal("//", JsonLanguage.Rules.LineComment, "JSON's comment (JSON with comments, as tsconfig.json and .NET's configuration read it)");
        Check.Equal("\"", JsonLanguage.Rules.StringDelimiters, "its strings, for the bracket colours");
    }

    [Test]
    public static void JsonFormatsKeepsCommentsAndTrailingCommasAndIsIdempotent()
    {
        const string messy = "{\n// the logs\n\"Logging\":{\"LogLevel\":{\"Default\":\"Information\",   \"Microsoft\" :\"Warning\" , }}, // after\n\n\n\"Empty\": { }, \"List\":[1,2,\n3],\"None\":[]\n}";
        const string formatted = "{\n    // the logs\n    \"Logging\": {\n        \"LogLevel\": {\n            \"Default\": \"Information\",\n            \"Microsoft\": \"Warning\",\n        }\n    }, // after\n\n    \"Empty\": {},\n    \"List\": [\n        1,\n        2,\n        3\n    ],\n    \"None\": []\n}";
        var style = new FormatStyle("    ", "\n");
        Check.Equal(formatted, Format(new JsonFormatter(), messy, style), "JSON formatted: indented, comments and the trailing comma kept, one empty line kept");
        Check.Equal(formatted, Format(new JsonFormatter(), formatted, style), "formatting again changes nothing");
        var edits = new List<TextEdit>();
        new JsonFormatter().Format(formatted, style, edits);
        Check.Equal(0, edits.Count, "and makes no edits");
        Check.Equal("{\n\t\"a\": [\n\t\t1\n\t]\n}\n", Format(new JsonFormatter(), "{\"a\":[1]}\n", new FormatStyle("\t", "\n")), "tabs when the editor indents with tabs; the last line break kept");
        Check.Equal("{\r\n  \"a\": 1\r\n}", Format(new JsonFormatter(), "{ \"a\" : 1 }", new FormatStyle("  ", "\r\n")), "the tab size and the document's line break");

        foreach (var (bad, where) in new[] { ("{\"a\": 1 \"b\": 2}", "line 1, column 9"), ("{\"a\": [1, 2}", "line 1, column 12"), ("{\n\"a\" 1}", "line 2"), ("{\"a\": tru}", "line 1"), ("[1, 2", "line 1, column 1"), ("{\"a\": \"open", "line 1") })
        {
            var error = Check.Throws<FormatException>(() => Format(new JsonFormatter(), bad, style), $"malformed JSON is refused: {bad}");
            Check.True(error.Message.StartsWith(where, StringComparison.Ordinal), $"the message says where ({error.Message})");
        }
    }

    [Test]
    public static async Task FormatIsOneUndoStepAndAMalformedFileIsLeftWithAMessage()
    {
        var json = JsonLanguage.Language;
        var options = new CraftOptions { TabSize = 2, InsertSpaces = true };
        var (model, document, _) = Editors.Make("{\"a\":[1,2],\"b\":{}}", options, json);
        model.Dispatch = action => action();
        int steps = document.Kinds.Count;
        Check.True(model.FormattingSession is LineStateSession && model.SemanticSession is null, "JSON formats through the public kit's session, without a semantic layer");
        Check.True(await model.FormatDocumentAsync(), "the format applied edits");
        Check.Equal("{\n  \"a\": [\n    1,\n    2\n  ],\n  \"b\": {}\n}".Replace("\n", Environment.NewLine, StringComparison.Ordinal), document.Text, "formatted with the editor's tab size and spaces (a one-line file takes the platform's line break)");
        Check.Equal(steps + 1, document.Kinds.Count, "as one step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("{\"a\":[1,2],\"b\":{}}", document.Text, "one undo takes the whole format back");

        model.OnKey(EditorKey.F, KeyModifiers.Shift | KeyModifiers.Alt);
        for (int wait = 0; wait < 500 && model.SemanticRequestsInFlight > 0; wait++)
        {
            await Task.Delay(10);
        }

        Check.True(document.Text.StartsWith("{" + Environment.NewLine + "  \"a\"", StringComparison.Ordinal), "Shift+Alt+F formats it too");

        var (broken, brokenDocument, _) = Editors.Make("{\"a\": 1 \"b\": 2}", options, json);
        broken.Dispatch = action => action();
        Check.True(!await broken.FormatDocumentAsync(), "a malformed file is not formatted");
        Check.Equal("{\"a\": 1 \"b\": 2}", brokenDocument.Text, "and left unchanged");
        Check.True(broken.LastFormatMessage is { } message && message.Contains("line 1, column 9", StringComparison.Ordinal), $"with a message saying where ({broken.LastFormatMessage})");
    }

    [Test]
    public static async Task FormatOnSaveFormatsFirstWhenItIsOn()
    {
        EditorLanguages.Register(JsonLanguage.Language);
        try
        {
            foreach (bool on in new[] { false, true })
            {
                using var folder = new TempFolder();
                string file = folder.File("appsettings.json");
                await File.WriteAllTextAsync(file, "{\"a\":1}");
                var settings = new CraftSettings(folder.Isolated(name => name == "CRAFT_FORMAT_ON_SAVE" ? (on ? "true" : "false") : null), SettingsFile.Load(folder.File("data/settings.json")));
                var recent = RecentList.Load(folder.File("data/recent.json"), 10);
                using var host = new EditorHost();
                using var shell = new ShellModel(host, settings, recent, new NoDialogs(), new SilentUi());
                Check.Equal(on, shell.Options.FormatOnSave, "CRAFT_FORMAT_ON_SAVE reaches the shell's options");
                var tab = (await shell.OpenFileAsync(file))!;
                Check.Equal(JsonLanguage.LanguageName, tab.Language?.Name, "appsettings.json opens as JSON");
                var model = new EditorModel(tab.Document, tab.Language, shell.Options, null, new FixedMeasurer(), new FakeClipboard()) { Dispatch = action => action() };
                shell.EditorOf = t => ReferenceEquals(t, tab) ? model : null;
                ((TextDocument)tab.Document).Edit([TextEdit.Insert(6, ",\"b\":2")], EditKind.Paste, SelectionState.At(6), SelectionState.At(12));
                Check.True(await shell.SaveAsync(tab), "saved");
                string saved = await File.ReadAllTextAsync(file);
                Check.Equal(on ? "{\n    \"a\": 1,\n    \"b\": 2\n}" : "{\"a\":1,\"b\":2}", saved.Replace("\r\n", "\n", StringComparison.Ordinal), on ? "formatted before it was written" : "written as it is when the option is off");
                if (on)
                {
                    ((TextDocument)tab.Document).Undo();
                    Check.Equal("{\"a\":1,\"b\":2}", tab.Document.Buffer.Current.GetText(0, tab.Document.Buffer.Current.Length), "the format on save is one undo step");
                }

                model.Dispose();
            }
        }
        finally
        {
            EditorLanguages.Unregister(JsonLanguage.LanguageName);
        }
    }

    // A package.json's colours from whichever copy of the language is registered (the plug-in's own context).
    private static void CheckPackageJsonColours(ILanguage language)
    {
        const string text = "{\n  \"name\": \"web-test\",\n  // a comment\n  \"private\": true,\n  \"scripts\": { \"build\": \"ng build\" },\n  \"version\": 1.5,\n}\n";
        var buffer = new PieceTableBuffer(text);
        using var session = language.Open(buffer);
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        string? ClassOf(string token)
        {
            int at = text.IndexOf(token, StringComparison.Ordinal);
            return spans.Spans.ToArray().FirstOrDefault(s => s.Start == at && s.Length == token.Length).Class;
        }

        Check.Equal(TokenClasses.PropertyName, ClassOf("\"name\""), "package.json: a property's name");
        Check.Equal(TokenClasses.String, ClassOf("\"web-test\""), "package.json: a string value");
        Check.Equal(TokenClasses.Comment, ClassOf("// a comment"), "package.json: a comment");
        Check.Equal(TokenClasses.Keyword, ClassOf("true"), "package.json: true");
        Check.Equal(TokenClasses.Number, ClassOf("1.5"), "package.json: a number");
    }

    // plugins/Craft.Plugins.Json/bin/<Configuration>/net10.0/ copied whole, the host's contracts left out, as the app's targets do.
    private static void CopyJsonPlugin(string folder)
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TestRunner.RepositoryPath("plugins", JsonPackage.PackageName, "bin", testOutput.Parent!.Name, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, JsonPackage.PackageName + ".dll")) && File.Exists(Path.Combine(output, "plugin.json")), $"the JSON plug-in's build output, with its plugin.json, is at {output}");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(output))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("Craft.Abstractions.", StringComparison.Ordinal) && !name.StartsWith("Idrak.Abstraction.", StringComparison.Ordinal) && !name.StartsWith("Craft.Core.", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(folder, name));
            }
        }
    }
}
