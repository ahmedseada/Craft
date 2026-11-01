// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Plugins.JavaScript;
using Craft.Plugins.TypeScript;
using Craft.Tests.Runner;
using Craft.Web.Script;
using Idrak.Abstraction.Plugins;

namespace Craft.Tests;

/// <summary>
/// Phase W's plug-ins, W-script's part: JavaScript and TypeScript as pre-installed, removable plug-ins, each carrying its
/// own copy of Craft.Web; their languages, colours from their own load context, folding, selectors, icons and settings;
/// removed or disabled, their files open as plain text; their settings reach the language through the host.
/// </summary>
[TestGroup("WebPlugins")]
public static class WebScriptPluginTests
{
    private static readonly string[] JavaScriptFiles = ["app.js", "lib/util.mjs", "config.cjs", "src/App.jsx"];
    private static readonly string[] TypeScriptFiles = ["main.ts", "lib/util.mts", "config.cts", "src/App.tsx", "types/index.d.ts"];

    [Test]
    public static async Task TheScriptPluginsLoadFromTheirFoldersEachWithItsOwnCraftWeb()
    {
        using var temp = new TempFolder();
        var plugins = Path.Combine(temp.Path, "plugins");
        var javaScriptFolder = Path.Combine(plugins, JavaScriptPackage.PackageName);
        var typeScriptFolder = Path.Combine(plugins, TypeScriptPackage.PackageName);
        CopyPlugin(JavaScriptPackage.PackageName, javaScriptFolder);
        CopyPlugin(TypeScriptPackage.PackageName, typeScriptFolder);
        IdrakPlugins.Unregister(JavaScriptLanguage.LanguageName);
        IdrakPlugins.Unregister(TypeScriptLanguage.LanguageName);
        Check.True(JavaScriptFiles.Concat(TypeScriptFiles).All(f => EditorLanguages.ForFile(f) is null), "without the plug-ins, script files are plain text");
        try
        {
            foreach (var (folder, name) in new[] { (javaScriptFolder, JavaScriptPackage.PackageName), (typeScriptFolder, TypeScriptPackage.PackageName) })
            {
                var result = CraftPlugins.LoadFolder(folder);
                Check.True(result.Loaded, $"{name} loads from its folder ({result.Error})");
                Check.True(result is { Outdated: false, Manifest.ContractVersion: "1.1" }, $"{name}'s plugin.json: contract 1.1 ({result.Manifest?.ContractVersion})");
                Check.True(File.Exists(Path.Combine(folder, "Craft.Web.dll")) && !File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")),
                    $"{name} carries its own Craft.Web and no copy of the shared contracts");
            }

            foreach (var file in JavaScriptFiles)
            {
                var language = EditorLanguages.ForFile(file);
                Check.True(language is LineStateLanguage { Name: JavaScriptLanguage.LanguageName } && FromThePlugin(language, JavaScriptLanguage.Language),
                    $"{file} opens as JavaScript, the language the loaded plug-in made ({language?.Name})");
            }

            foreach (var file in TypeScriptFiles)
            {
                var language = EditorLanguages.ForFile(file);
                Check.True(language is LineStateLanguage { Name: TypeScriptLanguage.LanguageName } && FromThePlugin(language, TypeScriptLanguage.Language),
                    $"{file} opens as TypeScript, the language the loaded plug-in made ({language?.Name})");
            }

            Check.True(EditorLanguages.ForFile("jsconfig.json")?.Name != JavaScriptLanguage.LanguageName && EditorLanguages.ForFile("tsconfig.json")?.Name != TypeScriptLanguage.LanguageName,
                "jsconfig.json and tsconfig.json are not script files (they stay JSON)");

            // Colours through the loaded copy: a .tsx reads JSX, a .ts never.
            var typeScript = EditorLanguages.ForFile("a.tsx")!;
            const string tsx = "const el = <div className=\"x\">{n satisfies number}</div>;";
            var tsxSpans = Classify(typeScript, tsx, Path.Combine(temp.Path, "a.tsx"));
            Check.True(tsxSpans.Contains(("div", TokenClasses.Keyword)) && tsxSpans.Contains(("className", TokenClasses.PropertyName)) && tsxSpans.Contains(("satisfies", TokenClasses.Keyword)),
                $"a .tsx through the plug-in: an element, an attribute, satisfies ({string.Join(", ", tsxSpans)})");
            var tsSpans = Classify(typeScript, "const y = <string>value;", Path.Combine(temp.Path, "a.ts"));
            Check.True(!tsSpans.Any(s => s.Class == TokenClasses.PropertyName) && tsSpans.Contains(("string", TokenClasses.Keyword)), "a .ts never reads JSX");

            var folding = FoldingRangeProviders.Find(TypeScriptLanguage.LanguageName);
            Check.True(folding is not null && folding.Languages.Contains(TypeScriptLanguage.LanguageName), "TypeScript's folding is registered");
            var ranges = await folding!.GetRangesAsync(new FoldingRequest(new PieceTableBuffer("function f() {\n  return 1;\n}\n").Current, TypeScriptLanguage.LanguageName, null, 4), CancellationToken.None);
            Check.True(ranges.Contains(new FoldingRange(0, 2)), "and folds a function's braces");
            Check.True(FoldingRangeProviders.Find(JavaScriptLanguage.LanguageName) is not null, "JavaScript's folding is registered");
            Check.True(LanguageSelectors.Find(JavaScriptLanguage.LanguageName) is not null && LanguageSelectors.Find(TypeScriptLanguage.LanguageName) is not null, "their project selectors are registered");

            var javaScriptPlugin = EditorPlugins.Find(JavaScriptLanguage.LanguageName);
            var typeScriptPlugin = EditorPlugins.Find(TypeScriptLanguage.LanguageName);
            Check.True(javaScriptPlugin is not null && javaScriptPlugin.Settings.Select(s => s.Key).SequenceEqual(["version", "projectVersion", "jsx", "formatLineWidth", "stateCapacity", "stateDepth"]),
                $"JavaScript's settings ({string.Join(", ", javaScriptPlugin?.Settings.Select(s => s.Key) ?? [])})");
            Check.True(typeScriptPlugin is not null && !typeScriptPlugin.Settings.Any(s => s.Key == "jsx") && typeScriptPlugin.Settings.Any(s => s.Key == "formatLineWidth"), "TypeScript's (no jsx: .tsx always, .ts never)");
            Check.True(javaScriptPlugin!.Settings.All(s => s.Shape is { Section: SettingSections.Editor }), "on the settings page's Editor section");
            Check.Equal("CRAFT_PLUGIN_JAVASCRIPT_JSX", PluginSetting.VariableName(JavaScriptLanguage.LanguageName, "jsx"), "JSX's variable");
            Check.Equal("CRAFT_PLUGIN_TYPESCRIPT_FORMATLINEWIDTH", PluginSetting.VariableName(TypeScriptLanguage.LanguageName, "formatLineWidth"), "the line width's variable");

            Check.True(FileIcons.For("App.tsx") is { Language: "TypeScript with JSX", Shape: not null }, "a .tsx shows the plug-in's icon, drawn (W-files' shape)");
            Check.True(FileIcons.For("util.mjs") is { Language: "JavaScript module", Shape: not null }, "a .mjs too");
            Check.True(FileIcons.For("index.d.ts") is { Language: "TypeScript declarations" }, ".d.ts by its compound suffix");

            Check.True(IdrakPlugins.Unregister(JavaScriptLanguage.LanguageName) && IdrakPlugins.Unregister(TypeScriptLanguage.LanguageName), "the plug-ins unregister");
            Check.True(JavaScriptFiles.Concat(TypeScriptFiles).All(f => EditorLanguages.ForFile(f) is null), "removed, script files are plain text again");
            Check.True(FoldingRangeProviders.Find(TypeScriptLanguage.LanguageName) is null && LanguageSelectors.Find(JavaScriptLanguage.LanguageName) is null && EditorPlugins.Find(TypeScriptLanguage.LanguageName) is null,
                "their folding, selectors and settings go with them");
            Check.True(FileIcons.For("main.ts") is { Badge: "TS", Shape: not null }, "the library's own drawn icon answers again");

            var options = CraftOptions.FromVariables(n => n switch
            {
                "CRAFT_PLUGIN_FOLDERS" => plugins,
                "CRAFT_PLUGINS_DISABLED" => $"{JavaScriptPackage.PackageName}, {TypeScriptPackage.PackageName}",
                _ => null,
            });
            var installed = CraftPlugins.LoadInstalled(options);
            Check.True(installed.Where(r => r.Name is JavaScriptPackage.PackageName or TypeScriptPackage.PackageName).All(r => !r.Loaded && r.Error!.Contains("disabled", StringComparison.Ordinal)),
                "disabled, both are skipped and said so");
            Check.True(JavaScriptFiles.Concat(TypeScriptFiles).All(f => EditorLanguages.ForFile(f) is null), "disabled, script files open as plain text");
        }
        finally
        {
            IdrakPlugins.Unregister(JavaScriptLanguage.LanguageName);
            IdrakPlugins.Unregister(TypeScriptLanguage.LanguageName);
        }
    }

    [Test]
    public static void TheSettingsReachTheLanguageThroughTheHost()
    {
        using var temp = new TempFolder();
        EditorPlugins.Register(JavaScriptLanguage.Editor);
        EditorPlugins.Register(TypeScriptLanguage.Editor);
        try
        {
            var host = new EditorHost(readVariable: n => n switch
            {
                "CRAFT_PLUGIN_JAVASCRIPT_JSX" => "on",
                "CRAFT_PLUGIN_TYPESCRIPT_VERSION" => "4.8",
                "CRAFT_PLUGIN_TYPESCRIPT_FORMATLINEWIDTH" => "40",
                _ => null,
            });
            host.RefreshPlugins();
            using (var js = (LineStateSession)JavaScriptLanguage.Language.Open(new PieceTableBuffer("const a = <b />;"), new LanguageOpenContext(Path.Combine(temp.Path, "a.js"), temp.Path, LanguageSettings.Default)))
            {
                Check.True(js.Tokenizer is ScriptTokenizer { Dialect: ScriptDialect.Jsx }, "CRAFT_PLUGIN_JAVASCRIPT_JSX=on: a .js reads JSX");
            }

            using (var ts = (LineStateSession)TypeScriptLanguage.Language.Open(new PieceTableBuffer("x satisfies T;"), new LanguageOpenContext(Path.Combine(temp.Path, "a.ts"), temp.Path, LanguageSettings.Default)))
            {
                Check.True(ts.Tokenizer is ScriptTokenizer { Version.TypeScript: 408 }, "CRAFT_PLUGIN_TYPESCRIPT_VERSION=4.8: TypeScript 4.8 is read");
            }

            Check.Equal(40, TypeScriptLanguage.Language.Settings().FormatLineWidth, "CRAFT_PLUGIN_TYPESCRIPT_FORMATLINEWIDTH reaches the formatter");
            host.Dispose();
            Check.True(JavaScriptLanguage.Language.Settings() is { Jsx: "auto", FormatLineWidth: 0 } && TypeScriptLanguage.Language.Settings().Version == "latest",
                "the host closed: the languages read their defaults again");
        }
        finally
        {
            EditorPlugins.Unregister(JavaScriptLanguage.LanguageName);
            EditorPlugins.Unregister(TypeScriptLanguage.LanguageName);
            JavaScriptLanguage.Language.Settings = static () => new ScriptSettings();
            TypeScriptLanguage.Language.Settings = static () => new ScriptSettings();
        }
    }

    // The language the plug-in loaded from its folder made (its own static, not the test's): Craft.Web itself is shared here
    // only because the test runner has a Craft.Web.dll beside it (the loader shares Craft.* assemblies the host has); the app
    // has none, so each plug-in loads the copy in its folder.
    private static bool FromThePlugin(ILanguage language, ILanguage testsOwn) => !ReferenceEquals(language, testsOwn);

    private static List<(string Text, string Class)> Classify(ILanguage language, string text, string path)
    {
        var buffer = new PieceTableBuffer(text);
        using var session = language.Open(buffer, new LanguageOpenContext(path, null, LanguageSettings.Default));
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        return [.. spans.Spans.ToArray().Select(s => (text.Substring(s.Start, s.Length), s.Class))];
    }

    // plugins/<package>/bin/<Configuration>/net10.0/ copied whole, the host's contracts left out, as the app's targets do.
    private static void CopyPlugin(string package, string folder)
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var output = TestRunner.RepositoryPath("plugins", package, "bin", testOutput.Parent!.Name, testOutput.Name);
        Check.True(File.Exists(Path.Combine(output, package + ".dll")) && File.Exists(Path.Combine(output, "plugin.json")) && File.Exists(Path.Combine(output, "Craft.Web.dll")),
            $"{package}'s build output, with its plugin.json and its Craft.Web, is at {output}");
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
