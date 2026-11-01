// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Text;
using Craft.Plugins.Angular;
using Craft.Plugins.Html;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;

namespace Craft.Tests;

/// <summary>
/// Phase W's plug-ins, W-markup's part: HTML and Angular as pre-installed, removable plug-ins, each carrying its own copy of
/// Craft.Web; their languages from their folders (a script coloured with no JavaScript plug-in), folding, the selector,
/// settings and variables, icons; removed or disabled, their files open as plain text (or HTML).
/// </summary>
[TestGroup("WebPlugins")]
public static class WebMarkupPluginTests
{
    [Test]
    public static async Task TheHtmlAndAngularPluginsLoadFromTheirFoldersEachWithItsOwnCraftWeb()
    {
        using var temp = new TempFolder();
        var plugins = Path.Combine(temp.Path, "plugins");
        var htmlFolder = Path.Combine(plugins, HtmlPackage.PackageName);
        var angularFolder = Path.Combine(plugins, AngularPackage.PackageName);
        CopyPlugin(HtmlPackage.PackageName, htmlFolder);
        CopyPlugin(AngularPackage.PackageName, angularFolder);
        IdrakPlugins.Unregister(HtmlParts.LanguageName);
        IdrakPlugins.Unregister(AngularParts.PluginName);
        Check.True(EditorLanguages.ForFile("index.html") is null && EditorLanguages.ForFile("app.component.html") is null, "without the plug-ins, .html is plain text");
        try
        {
            foreach (var (folder, name) in new[] { (htmlFolder, HtmlPackage.PackageName), (angularFolder, AngularPackage.PackageName) })
            {
                var result = CraftPlugins.LoadFolder(folder);
                Check.True(result.Loaded, $"{name} loads from its folder ({result.Error})");
                Check.True(result is { Outdated: false, Manifest.ContractVersion: "1.1" }, $"{name}'s plugin.json: contract 1.1");
                Check.True(File.Exists(Path.Combine(folder, "Craft.Web.dll")) && !File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")), $"{name} carries its own Craft.Web and no shared contracts");
            }

            var html = EditorLanguages.ForFile("site/index.html");
            Check.True(html is LineStateLanguage { Name: HtmlParts.LanguageName } && !ReferenceEquals(html, HtmlParts.Language), "index.html opens as HTML, the language the loaded plug-in made");
            Check.Equal(HtmlParts.LanguageName, EditorLanguages.ForFile("old.htm")?.Name, ".htm too");
            Check.True(EditorLanguages.ForFile("app.component.html") is LineStateLanguage { Name: AngularParts.TemplatesName }, "a *.component.html is an Angular template, by name");
            const string page = "<script>const a = 1;</script>\n<style>p { color: red; }</style>";
            var buffer = new PieceTableBuffer(page);
            using (var session = html!.Open(buffer, new LanguageOpenContext(Path.Combine(temp.Path, "index.html"), null, LanguageSettings.Default)))
            {
                using var spans = new TokenSpanBuffer();
                session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
                var classes = spans.Spans.ToArray().Select(s => (page.Substring(s.Start, s.Length), s.Class)).ToList();
                Check.True(classes.Contains(("const", TokenClasses.Keyword)) && classes.Contains(("color", TokenClasses.PropertyName)),
                    "the script and style colour from the plug-in's own Craft.Web, no JavaScript or CSS plug-in loaded");
            }

            var folding = FoldingRangeProviders.Find(HtmlParts.LanguageName);
            Check.True(folding is not null, "HTML's folding is registered");
            var ranges = await folding!.GetRangesAsync(new FoldingRequest(new PieceTableBuffer("<div>\n  <p>a</p>\n</div>\n").Current, HtmlParts.LanguageName, null, 2), CancellationToken.None);
            Check.True(ranges.Contains(new FoldingRange(0, 2)), "and folds an element");
            Check.True(FoldingRangeProviders.Find(AngularParts.TemplatesName) is not null && FoldingRangeProviders.Find(AngularParts.TypeScriptName) is not null, "Angular's two foldings are registered");
            Check.True(LanguageSelectors.Find(AngularParts.PluginName) is not null, "Angular's selector is registered");

            var htmlPlugin = EditorPlugins.Find(HtmlParts.LanguageName);
            var angularPlugin = EditorPlugins.Find(AngularParts.PluginName);
            Check.True(htmlPlugin is not null && htmlPlugin.Settings.Select(s => s.Key).SequenceEqual(["embedded", "attributesAsCode"]), "HTML's settings");
            Check.True(angularPlugin is not null && angularPlugin.Settings.Select(s => s.Key).SequenceEqual(["templates", "inlineTemplates", "version", "projectVersion"]), "Angular's settings");
            Check.True(htmlPlugin!.Settings.Concat(angularPlugin!.Settings).All(s => s.Shape is { Section: SettingSections.Editor }), "on the settings page's Editor section");
            Check.Equal("CRAFT_PLUGIN_HTML_EMBEDDED", PluginSetting.VariableName(HtmlParts.LanguageName, "embedded"), "embedded's variable");
            Check.Equal("CRAFT_PLUGIN_HTML_ATTRIBUTESASCODE", PluginSetting.VariableName(HtmlParts.LanguageName, "attributesAsCode"), "attributesAsCode's variable");
            Check.Equal("CRAFT_PLUGIN_ANGULAR_TEMPLATES", PluginSetting.VariableName(AngularParts.PluginName, "templates"), "templates' variable");
            Check.Equal("CRAFT_PLUGIN_ANGULAR_INLINETEMPLATES", PluginSetting.VariableName(AngularParts.PluginName, "inlineTemplates"), "inlineTemplates' variable");

            Check.True(FileIcons.For("index.html") is { Language: "HTML", Shape: not null }, "an .html shows the plug-in's icon, drawn");
            Check.True(FileIcons.For("app.component.html") is { Language: "Angular template", Shape: not null }, "a component's template its own");

            Check.True(IdrakPlugins.Unregister(AngularParts.PluginName), "the Angular plug-in unregisters");
            Check.Equal(HtmlParts.LanguageName, EditorLanguages.ForFile("app.component.html")?.Name, "removed, a *.component.html is HTML");
            Check.True(IdrakPlugins.Unregister(HtmlParts.LanguageName), "the HTML plug-in unregisters");
            Check.True(EditorLanguages.ForFile("index.html") is null && FoldingRangeProviders.Find(HtmlParts.LanguageName) is null && EditorPlugins.Find(HtmlParts.LanguageName) is null,
                "removed, .html is plain text and its parts are gone");
            Check.True(FileIcons.For("index.html") is { Shape: not null }, "the library's own drawn icon answers again");

            var options = CraftOptions.FromVariables(n => n switch
            {
                "CRAFT_PLUGIN_FOLDERS" => plugins,
                "CRAFT_PLUGINS_DISABLED" => $"{HtmlPackage.PackageName}, {AngularPackage.PackageName}",
                _ => null,
            });
            var installed = CraftPlugins.LoadInstalled(options);
            Check.True(installed.Where(r => r.Name is HtmlPackage.PackageName or AngularPackage.PackageName).All(r => !r.Loaded && r.Error!.Contains("disabled", StringComparison.Ordinal)), "disabled, both are skipped and said so");
            Check.True(EditorLanguages.ForFile("index.html") is null && EditorLanguages.ForFile("app.component.html") is null, "disabled, .html opens as plain text");
        }
        finally
        {
            IdrakPlugins.Unregister(AngularParts.PluginName);
            IdrakPlugins.Unregister(HtmlParts.LanguageName);
        }
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
