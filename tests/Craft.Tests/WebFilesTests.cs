// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// W-files: languages by file name (an exact name, the longest pattern, then the extension) and by the workspace (language
/// selectors from their own cache), Craft's rich icon set (files by name, compound suffix, glob and extension; folders by
/// name, open and closed; every shape parses; light and dark), an icon theme a plug-in registers picked by
/// <c>CRAFT_ICON_THEME</c>, matching and warm icon frames that allocate nothing, and node_modules shown dimmed in the tree
/// but never listed for quick open or search.
/// </summary>
[TestGroup("WebFiles")]
public static class WebFilesTests
{
    [Test]
    public static void ForFileRanksAnExactNameThenTheLongestPatternThenTheExtension()
    {
        var json = new FakeLanguage("test-json", [".json", ".html"]);
        var glob = new FakeLanguage("test-tsconfig", [], ["tsconfig*.json"]);
        var longer = new FakeLanguage("test-tsconfig-app", [], ["tsconfig.app*.json"]);
        var named = new FakeLanguage("test-tsconfig-exact", [], ["TSCONFIG.json"]);
        var suffix = new FakeLanguage("test-template", [], ["*.component.html"]);
        var docker = new FakeLanguage("test-docker", [], ["Dockerfile", "Dockerfile.*"]);
        var late = new FakeLanguage("test-json-late", [".json"]);
        FakeLanguage[] all = [json, glob, longer, named, suffix, docker, late];
        foreach (var language in all)
        {
            EditorLanguages.Register(language);
        }

        try
        {
            Check.True(ReferenceEquals(named, EditorLanguages.ForFile(Path.Combine("a", "b", "tsconfig.json"))), "an exact name first (ignoring case), over every pattern and the extension");
            Check.True(ReferenceEquals(longer, EditorLanguages.ForFile("tsconfig.app.json")), "the longer of two matching patterns");
            Check.True(ReferenceEquals(glob, EditorLanguages.ForFile("TSConfig.Base.JSON")), "a pattern over the extension, ignoring case");
            Check.True(ReferenceEquals(json, EditorLanguages.ForFile("data.json")), "the extension when no name or pattern matches; the first language registered wins a tie");
            Check.True(ReferenceEquals(suffix, EditorLanguages.ForFile("app.component.html")) && ReferenceEquals(json, EditorLanguages.ForFile("index.html")),
                "a star and a suffix is a compound suffix ('*.component.html'), above the plain '.html'");
            Check.True(ReferenceEquals(docker, EditorLanguages.ForFile("Dockerfile")) && ReferenceEquals(docker, EditorLanguages.ForFile("Dockerfile.dev")), "a name with no extension, and its glob");
            Check.True(EditorLanguages.ForFile("readme") is null && EditorLanguages.ForFile("x.no-such-extension") is null && EditorLanguages.ForFile("trailing.") is null, "no language: plain text");
            EditorLanguages.Unregister(named.Name);
            Check.True(ReferenceEquals(glob, EditorLanguages.ForFile("tsconfig.json")), "a registration changed: the table is made again (the glob now)");

            // The table's own grammar.
            Check.True(FilePatternTable<string>.KindOf(".cs") == FilePatternKind.Extension && FilePatternTable<string>.KindOf(".d.ts") == FilePatternKind.Suffix
                && FilePatternTable<string>.KindOf("*.spec.ts") == FilePatternKind.Suffix && FilePatternTable<string>.KindOf("tsconfig*.json") == FilePatternKind.Glob
                && FilePatternTable<string>.KindOf("package.json") == FilePatternKind.Name && FilePatternTable<string>.KindOf(".eslintrc*") == FilePatternKind.Glob,
                "an extension, a compound suffix, a glob and a name by their shape");
            Check.True(FilePatternTable<string>.GlobMatches("eslint.config.*", "ESLINT.CONFIG.mjs") && FilePatternTable<string>.GlobMatches("a?c*", "abc") && !FilePatternTable<string>.GlobMatches("tsconfig*.json", "tsconfig.jsonc"),
                "globs: '*' any run, '?' one character, ignoring case, anchored at both ends");
            var table = new FilePatternTable<string>([KeyValuePair.Create(".ts", "ts"), KeyValuePair.Create(".d.ts", "dts"), KeyValuePair.Create(".spec.ts", "spec"), KeyValuePair.Create(".component.spec.ts", "component spec")]);
            Check.True(table.Find("x.component.spec.ts") == "component spec" && table.Find("x.spec.ts") == "spec" && table.Find("lib.d.ts") == "dts" && table.Find("a.b.ts") == "ts",
                "the longest compound suffix by the walk over the dots");
        }
        finally
        {
            foreach (var language in all)
            {
                EditorLanguages.Unregister(language.Name);
            }
        }
    }

    [Test]
    public static void MatchingNamesAllocatesNothing()
    {
        var language = new FakeLanguage("test-alloc", [".alloc"], ["alloc*.cfg", "Allocfile"]);
        EditorLanguages.Register(language);
        try
        {
            string[] paths = [Path.Combine("src", "a.alloc"), "alloc.base.cfg", "Allocfile", "x.nothing", "noext", "app.component.spec.ts", "package.json"];
            string[] folders = ["node_modules", "src", "unknown-folder"];
            void Round()
            {
                foreach (var path in paths)
                {
                    _ = EditorLanguages.ForFile(path, null);
                    _ = FileIcons.For(path);
                }

                foreach (var folder in folders)
                {
                    _ = FileIcons.ForFolder(folder);
                }
            }

            for (int i = 0; i < 2000; i++)
            {
                Round();                                  // warm: the tables made, the methods optimized
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                Round();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(0L, allocated, $"languages and icons by name allocate nothing warm (rules 15, 17, 63): {allocated} bytes over {10_000 * (paths.Length * 2 + folders.Length)} lookups; types: {AllocatedTypes.Of(Round)}");
        }
        finally
        {
            EditorLanguages.Unregister(language.Name);
        }
    }

    [Test]
    public static async Task SelectorsAnswerFirstFromTheirCacheAndTheShellPassesTheWorkspace()
    {
        using var folder = new TempFolder();
        await File.WriteAllTextAsync(folder.File("angular.json"), "{}");
        Directory.CreateDirectory(folder.File("src"));
        await File.WriteAllTextAsync(folder.File(Path.Combine("src", "app.html")), "<p></p>");
        var html = new FakeLanguage("test-html", [".html"]);
        var template = new FakeLanguage("test-template", []);
        var selector = new ProjectSelector(template);
        var failing = new FailingSelector();
        EditorLanguages.Register(html);
        EditorLanguages.Register(template);
        LanguageSelectors.Register(selector);
        LanguageSelectors.Register(failing);
        try
        {
            string page = folder.File(Path.Combine("src", "app.html"));
            Check.True(ReferenceEquals(html, EditorLanguages.ForFile(page, folder.Path)), "before the selector learned the folder: by the name");
            Check.Equal(0, selector.Reads, "asking reads nothing (the selector answers from its cache)");
            var problems = await LanguageSelectors.PrepareAsync(folder.Path);
            Check.True(problems is [var problem] && problem.StartsWith(failing.Name, StringComparison.Ordinal), $"a selector that fails is named, the others prepared ({string.Join("; ", problems)})");
            Check.True(ReferenceEquals(template, EditorLanguages.ForFile(page, folder.Path)), "the selector first, for the workspace it learned");
            Check.True(ReferenceEquals(html, EditorLanguages.ForFile(page)) && ReferenceEquals(html, EditorLanguages.ForFile(page, Path.GetTempPath())), "without that workspace: by the name");

            // The shell passes its workspace and re-picks the open tabs once the selectors read the folder.
            selector.Forget();
            var settings = new CraftSettings(folder.Isolated(), SettingsFile.Load(folder.File("data/settings.json")));
            using var host = new EditorHost();
            using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi());
            var early = (await shell.OpenFileAsync(page))!;
            Check.True(ReferenceEquals(html, early.Language), "no folder open: by the name");
            Check.True(await shell.OpenFolderAsync(folder.Path), "the folder opens");
            Check.True(await Eventually(() => ReferenceEquals(template, early.Language)), "the open tab takes the selector's language once it read the folder (off the UI thread)");
            Check.True(ReferenceEquals(template, shell.LanguageFor(page)), "the shell's call sites pass the workspace");
            var again = (await shell.OpenFileAsync(page))!;
            Check.True(ReferenceEquals(template, again.Language), "a file opened in the folder: the selector's language");
        }
        finally
        {
            LanguageSelectors.Unregister(selector.Name);
            LanguageSelectors.Unregister(failing.Name);
            EditorLanguages.Unregister(html.Name);
            EditorLanguages.Unregister(template.Name);
        }
    }

    [Test]
    public static void IconsByNameSuffixGlobExtensionAndFolder()
    {
        Check.Equal("npm package", FileIcons.For("package.json").Language, "an exact name over the .json extension");
        Check.Equal("Angular component", FileIcons.For(Path.Combine("src", "app", "app.component.ts")).Language, "a compound suffix over .ts, from a path");
        Check.Equal("Test", FileIcons.For("app.component.spec.ts").Language, "the longest suffix: .spec.ts over .component.ts' later dot");
        Check.Equal("TypeScript declarations", FileIcons.For("lib.d.ts").Language, ".d.ts");
        Check.True(FileIcons.For("tsconfig.json").Language == "TypeScript project" && FileIcons.For("tsconfig.app.json").Language == "TypeScript project", "the tsconfig glob");
        Check.True(FileIcons.For("eslint.config.mjs").Language == "ESLint settings" && FileIcons.For(".eslintrc.json").Language == "ESLint settings", "ESLint's flat config and the old .eslintrc*");
        Check.True(FileIcons.For("appsettings.Development.json").Language == "App settings" && FileIcons.For("launchSettings.json").Language == "Launch settings", "the C# set's settings files");
        Check.True(FileIcons.For("Dockerfile").Language == "Dockerfile" && FileIcons.For(".gitignore").Language == "Git ignore", "names without an extension and dot files");
        Check.True(FileIcons.For("main.TS").Badge == "TS" && FileIcons.For("a.cs").Badge == "C#" && FileIcons.For("README.md").Badge == "M↓", "extensions ignoring case; the badges kept as the fallback");
        Check.True(ReferenceEquals(FileIcons.Plain, FileIcons.For("x.no-such-extension")) && ReferenceEquals(FileIcons.Plain, FileIcons.For("")), "no icon: the plain one");

        var modules = FileIcons.ForFolder("node_modules");
        Check.True(modules.Language == "Installed packages" && modules.Shape is not null && modules.OpenShape is not null && !ReferenceEquals(modules.Shape, modules.OpenShape),
            "a folder by name, with open and closed shapes");
        Check.True(ReferenceEquals(modules.OpenShape, modules.ShapeFor(true)) && ReferenceEquals(modules.Shape, modules.ShapeFor(false)), "the open shape for an open folder");
        Check.True(ReferenceEquals(FileIcons.ForFolder("src"), FileIcons.ForFolder(Path.Combine("repo", "SRC") + Path.DirectorySeparatorChar)), "a folder's path: its last part, ignoring case");
        Check.True(ReferenceEquals(FileIcons.PlainFolder, FileIcons.ForFolder("unknown")), "an unknown folder: the plain folder");
        Check.True(FileIcons.For("a.cs").Shape!.Layers[0].ColorFor(true) != FileIcons.For("a.cs").Shape!.Layers[0].ColorFor(false), "light and dark variants");
        foreach (var icon in FileIcons.All.Where(i => i.Shape is not null))
        {
            uint light = icon.Shape!.Layers[0].ColorFor(false);
            Check.True(ColorContrast.Ratio(light, 0xFFFFFFFF) >= ColorContrast.Line - 0.01, $"{icon.Extension}'s light base reads at 3:1 on the light theme ({ColorContrast.Ratio(light, 0xFFFFFFFF):0.00})");
        }

        // A plug-in's icons over the library's, by any kind of key; Unregister brings the library's back.
        var library = FileIcons.For("x.ts");
        FileIcons.Register(new FileIcon(".ts", "T", 0xFF112233, "Plug-in TypeScript"));
        FileIcons.Register(new FileIcon("*.stories.ts", "St", 0xFF112233, "Stories"));
        FileIcons.RegisterFolder(new FileIcon("stories", "St", 0xFF112233, "Stories folder"));
        try
        {
            Check.True(FileIcons.For("x.ts").Language == "Plug-in TypeScript" && FileIcons.For("button.stories.ts").Language == "Stories" && FileIcons.ForFolder("stories").Language == "Stories folder",
                "a plug-in's extension, suffix and folder");
            Check.Throws<ArgumentException>(() => FileIcons.Register(new FileIcon(".bad", "B", 0, "Bad") { Shape = new FileIconShape([new FileIconLayer("L1 1", 0)]) }), "a shape that cannot be drawn fails loudly");
        }
        finally
        {
            FileIcons.Unregister(".ts");
            FileIcons.Unregister("*.stories.ts");
            FileIcons.UnregisterFolder("stories");
        }

        Check.True(ReferenceEquals(library, FileIcons.For("x.ts")) && ReferenceEquals(FileIcons.Plain, FileIcons.For("a.stories")), "the library's back");
        Check.Throws<NotRegisteredException>(() => FileIcons.Get(".no-such"), "an unregistered key fails with not registered");
    }

    [Test]
    public static void EveryBuiltInShapeParsesAndTheSetCoversTheCAndWebFiles()
    {
        int layers = 0;
        foreach (var icon in FileIcons.All.Concat(FileIcons.AllFolders).Append(FileIcons.Plain).Append(FileIcons.PlainFolder))
        {
            Check.True(icon.Shape is not null, $"{icon.Extension}: drawn, not a badge alone");
            foreach (var shape in new[] { icon.Shape, icon.OpenShape })
            {
                if (shape is null)
                {
                    continue;
                }

                Check.True(shape.Problem() is null, $"{icon.Extension}: every layer is path data the drawing parses ({shape.Problem()})");
                foreach (var layer in shape.Layers)
                {
                    layers++;
                    var once = FileIconShape.Transform(layer.Path, 1, 0, 0);
                    Check.Equal(once, FileIconShape.Transform(once, 1, 0, 0), $"{icon.Extension}: written again the same (the walk reads every number)");
                }
            }
        }

        Check.True(layers > 300, $"the set's layers ({layers})");
        Check.True(FileIconShape.Problem("L1 1") is not null && FileIconShape.Problem("M0 0L1") is not null && FileIconShape.Problem("M0 0A1 1 0 2 0 1 1") is not null
            && FileIconShape.Problem("M0 0Q1") is not null && FileIconShape.Problem("") is not null && FileIconShape.Problem("M0 0X1") is not null,
            "a missing move, a short set, a bad arc flag, an unknown command and nothing are refused");
        Check.True(FileIconShape.Problem("M1.5.5l-1-1e1zm0 0a1 1 0 011 1H2v3c0 0 1 1 2 2s1 1 1 1q1 1 2 2t1 1Z") is null, "SVG's compact forms read (1.5.5, -1e1, arc flags 01)");
        Check.Equal("M 3 4L 5 4h 2V 6a 2 2 0 0 1 2 2", FileIconShape.Transform("M1 1L2 1h1V2a1 1 0 0 1 1 1", 2, 1, 2), "a transform scales relative points, moves absolute ones and scales an arc's radii");

        string[] files =
        [
            "a.cs", "a.csx", "a.csproj", "a.props", "a.targets", "a.sln", "a.slnx", "a.razor", "a.cshtml", "a.xaml", "a.xml", "app.config", "a.resx", "a.json",
            "appsettings.json", "launchSettings.json", "global.json", ".editorconfig", "a.md", "a.png", "a.jpg", "a.svg", "a.ps1", "a.cmd", "a.sh", ".gitignore",
            ".gitattributes", "Dockerfile", "a.yml",
            "a.html", "a.htm", "a.css", "a.scss", "a.sass", "a.less", "a.js", "a.mjs", "a.cjs", "a.jsx", "jsconfig.json", "a.ts", "a.mts", "a.cts", "a.tsx", "a.d.ts",
            "tsconfig.json", "tsconfig.spec.json", "a.component.ts", "a.component.html", "a.component.scss", "a.service.ts", "a.module.ts", "a.directive.ts", "a.pipe.ts",
            "a.guard.ts", "angular.json", "package.json", "package-lock.json", ".npmrc", ".nvmrc", "eslint.config.js", ".eslintrc.cjs", "tslint.json", ".prettierrc",
            ".prettierrc.json", "a.spec.ts",
        ];
        foreach (var file in files)
        {
            Check.True(!ReferenceEquals(FileIcons.Plain, FileIcons.For(file)), $"{file} has its own icon");
        }

        foreach (var name in new[] { "src", "test", "tests", "docs", ".git", ".github", ".vscode", ".craft", "node_modules", "dist", "bin", "obj", "assets", "public", "components", "services", "scripts", ".angular" })
        {
            Check.True(!ReferenceEquals(FileIcons.PlainFolder, FileIcons.ForFolder(name)), $"the folder {name} has its own icon");
        }
    }

    [Test]
    public static async Task QuickOpenTheTreeAndProblemsUseTheSameTable()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("src"));
        Directory.CreateDirectory(folder.File("node_modules"));
        await File.WriteAllTextAsync(folder.File("package.json"), "{}");
        await File.WriteAllTextAsync(folder.File("app.component.ts"), "");
        using var host = new EditorHost();
        var quick = new QuickOpen(host, 20);
        quick.SetFiles(folder.Path, ["package.json", "app.component.ts"]);
        var found = quick.Query("app");
        Check.True(found is [{ Text: "app.component.ts" } item, ..] && ReferenceEquals(item.Icon, FileIcons.For("app.component.ts")), "quick open's file icon is the table's (a compound suffix)");
        await quick.BrowseAsync(folder.Path);
        var rows = quick.Query("");
        var src = rows.Single(r => r.Text == "src");
        Check.True(src.IsFolder && ReferenceEquals(src.Icon, FileIcons.ForFolder("src")), "quick open's folder rows take the folders' icons");

        var tree = new FolderTree(new Workspace(folder.Path));
        foreach (var row in tree.Rows)
        {
            var shown = row.Entry.IsFolder ? FileIcons.ForFolder(row.Entry.Name) : FileIcons.For(row.Entry.Name);
            var browsed = rows.FirstOrDefault(r => r.Text == row.Entry.Name);
            Check.True(browsed is null || ReferenceEquals(browsed.Icon, shown), $"{row.Entry.Name}: the tree's icon is quick open's");
        }

        // Problems: the file's icon before its header (the badge on a canvas that draws no shapes, the shape where it can).
        var list = new ProblemsList();
        list.Update([new ProblemSource("Program.cs", "Program.cs", new StringBuffer("x").Current, [new EditorDiagnostic(0, 1, DiagnosticSeverity.Error, "CS1", "bad")])], 100, false);
        Check.True(list.Rows is [ProblemGroup { Icon.Badge: "C#" }, ..], "a group carries its file's icon");
        var text = new CountingCanvas();
        list.Draw(text, new FontMetrics(8, 18, 14), EditorThemes.Get(EditorThemes.Dark).Colors, 0, 400, 200);
        Check.True(text.Texts.Contains("C#"), "a canvas without shapes gets the badge");
        var cache = new IconPathCache<object>((_, _) => new object());
        var icons = new IconCanvas(cache);
        list.Draw(icons, new FontMetrics(8, 18, 14), EditorThemes.Get(EditorThemes.Dark).Colors, 0, 400, 200);
        Check.True(icons.Drawn is [{ Badge: "C#" }] && !icons.Texts.Contains("C#"), "a canvas that draws shapes draws the icon, not the badge");
    }

    [Test]
    public static void WarmIconFramesParseOnceAndAllocateNothing()
    {
        var cache = new IconPathCache<object>((_, _) => new object());
        var canvas = new IconCanvas(cache);
        FileIcon[] icons = [.. FrameFiles.Select(n => FileIcons.For(n)),
            FileIcons.ForFolder("src"), FileIcons.ForFolder("node_modules")];
        int layers = icons.Sum(i => i.Shape!.Layers.Count) + icons.Sum(i => i.OpenShape?.Layers.Count ?? i.Shape!.Layers.Count);
        void Frame(float size)
        {
            foreach (var icon in icons)
            {
                FileIconDrawing.Draw(canvas, icon, 0, 0, size, false, true);
                FileIconDrawing.Draw(canvas, icon, 0, 0, size, true, false);
            }
        }

        Frame(16);
        int cold = cache.Parses;
        Check.True(cold > 0 && cold <= layers, $"the first frame parses each layer once ({cold} of {layers})");
        for (int i = 0; i < 200; i++)
        {
            Frame(16);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Frame(16);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.Equal(0L, allocated, $"0 bytes a warm frame of {icons.Length * 2} icons (rule 63); types: {AllocatedTypes.Of(() => Frame(16))}");
        Check.Equal(cold, cache.Parses, "no parse on a warm frame (rule 75)");
        Frame(20);
        Check.Equal(cold * 2, cache.Parses, "a new size parses each layer once more (kept per size)");
        Frame(20);
        Check.Equal(cold * 2, cache.Parses, "and then none");
        var bounded = new IconPathCache<object>((_, _) => new object(), limit: 4);
        foreach (var size in new[] { 10f, 11f, 12f })
        {
            FileIconDrawing.Draw(new IconCanvas(bounded), FileIcons.For("a.cs"), 0, 0, size, false, true);
        }

        Check.True(bounded.Count <= 4, $"the cache is bounded ({bounded.Count})");
    }

    [Test]
    public static void APluginsIconThemeIsPickedByTheSetting()
    {
        using var folder = new TempFolder();
        var mono = new FileIconTheme("test-mono", "One colour", [new FileIcon(".cs", "cs", 0xFF808080, "Mono C#"), new FileIcon("package.json", "np", 0xFF808080, "Mono package")],
            [new FileIcon("src", "s", 0xFF808080, "Mono sources")]);
        FileIconThemes.Register(mono);
        try
        {
            Check.True(SettingChoiceSources.Get(SettingChoiceSources.IconThemes).Choices.Contains("test-mono") && SettingChoiceSources.Get(SettingChoiceSources.IconThemes).Choices.Contains(FileIconThemes.Craft),
                "the settings page's choices list the registered themes");
            Check.True(CraftOptions.Variables.Any(v => v.Name == "CRAFT_ICON_THEME" && v.Shape?.ChoicesFrom == SettingChoiceSources.IconThemes), "CRAFT_ICON_THEME is on the settings page with the themes as its choices");
            var settings = new CraftSettings(folder.Isolated(n => n == "CRAFT_ICON_THEME" ? "test-mono" : null), SettingsFile.Load(folder.File("data/settings.json")));
            using (var host = new EditorHost())
            using (var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi()))
            {
                Check.Equal("test-mono", FileIconThemes.Current, "the shell uses the theme the setting names");
                Check.True(FileIcons.For("a.cs").Language == "Mono C#" && FileIcons.ForFolder("src").Language == "Mono sources", "the theme's icons");
                Check.Equal("TypeScript", FileIcons.For("a.ts").Language, "what the theme does not cover: Craft's own");
            }

            var unknown = new CraftSettings(folder.Isolated(n => n == "CRAFT_ICON_THEME" ? "no-such-theme" : null), SettingsFile.Load(folder.File("data/settings2.json")));
            using (var host = new EditorHost())
            using (var shell = new ShellModel(host, unknown, RecentList.Load(folder.File("data/recent2.json"), 10), new LightbulbTests.NoDialogs(), new LightbulbTests.SilentUi()))
            {
                Check.Equal(FileIconThemes.Craft, FileIconThemes.Current, "an unregistered name keeps Craft's own (said in the log)");
                Check.Equal("C# source", FileIcons.For("a.cs").Language, "Craft's icons");
            }

            Check.Throws<NotRegisteredException>(() => FileIconThemes.Use("no-such-theme"), "Use of an unregistered theme fails loudly");
            Check.Throws<ArgumentException>(() => _ = new FileIconTheme("bad", "", [new FileIcon(".x", "x", 0, "x") { Shape = new FileIconShape([new FileIconLayer("Q", 0)]) }]), "a theme's shape that cannot be drawn fails loudly");
        }
        finally
        {
            FileIconThemes.Use(FileIconThemes.Craft);
            FileIconThemes.Unregister(mono.Name);
        }
    }

    [Test]
    public static async Task NodeModulesShowsDimmedButIsNeverListedOrSearched()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File(Path.Combine("node_modules", "left-pad")));
        Directory.CreateDirectory(folder.File("src"));
        await File.WriteAllTextAsync(folder.File(Path.Combine("node_modules", "left-pad", "index.js")), "needle");
        await File.WriteAllTextAsync(folder.File(Path.Combine("src", "main.ts")), "needle");
        var options = new CraftOptions();
        Check.True(options.WorkspaceShowIgnored, "CRAFT_WORKSPACE_SHOW_IGNORED is on by default");
        var workspace = new Workspace(folder.Path, options);
        var root = workspace.ListFolder();
        Check.True(root.Single(e => e.Name == "node_modules") is { IsFolder: true, IsIgnored: true } && root.Single(e => e.Name == "src") is { IsIgnored: false },
            "the root lists node_modules, marked ignored (dimmed in the tree)");
        Check.True(workspace.ListFolder("node_modules") is [{ Name: "left-pad", IsIgnored: true }] && workspace.ListFolder(Path.Combine("node_modules", "left-pad")) is [{ Name: "index.js", IsIgnored: true }],
            "what lies inside is marked too");
        Check.True(workspace.IsInIgnoredFolder(folder.File(Path.Combine("node_modules", "left-pad", "index.js"))) && !workspace.IsInIgnoredFolder(folder.File(Path.Combine("src", "main.ts"))),
            "a path in a skipped folder");

        var tree = new FolderTree(workspace);
        var modules = tree.Rows.Single(r => r.Entry.Name == "node_modules");
        Check.True(modules is { IsExpanded: false, Entry.IsIgnored: true } && tree.Rows.All(r => r.Depth == 0), "the tree shows it closed and dimmed; nothing under it is read until it is opened");

        Check.True(workspace.EnumerateFiles().Select(Path.GetFileName).SequenceEqual(["main.ts"]), "the workspace's files (quick open's list) leave it out");
        var searched = WorkspaceFiles.Enumerate(folder.Path, options.WorkspaceIgnore, useIgnoreFiles: true, null, null).Select(Path.GetFileName).ToList();
        Check.SequenceEqual(["main.ts"], searched, "a search never enters it");

        var hidden = new Workspace(folder.Path, CraftOptions.FromVariables(n => n == "CRAFT_WORKSPACE_SHOW_IGNORED" ? "false" : null));
        Check.True(hidden.ListFolder().All(e => e.Name != "node_modules"), "CRAFT_WORKSPACE_SHOW_IGNORED off: not shown at all, as before");
    }

    private static readonly string[] FrameFiles = ["a.cs", "package.json", "app.component.ts", "a.d.ts", "tsconfig.app.json", "a.md", "a.png", "Dockerfile", "x.unknown"];

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    private sealed class FakeLanguage(string name, IReadOnlyList<string> extensions, IReadOnlyList<string>? patterns = null) : ILanguage
    {
        public string Name { get; } = name;

        public string Summary => "a test language";

        public IReadOnlyList<string> Extensions { get; } = extensions;

        public IReadOnlyList<string> FilePatterns { get; } = patterns ?? [];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer) => new PlainSession();
    }

    private sealed class PlainSession : ILanguageSession
    {
        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
        }

        public void Dispose()
        {
        }
    }

    // Like an Angular selector: a folder with angular.json makes its .html files templates; learned in PrepareAsync.
    private sealed class ProjectSelector(ILanguage template) : ILanguageSelector
    {
        private readonly ConcurrentDictionary<string, bool> _roots = new(PathKeys.Comparer);
        private int _reads;

        public string Name => "test-project-selector";

        public string Summary => "templates in a project with angular.json";

        public int Reads => _reads;

        public ILanguage? Select(string path, string? workspaceRoot) =>
            workspaceRoot is not null && _roots.TryGetValue(Path.TrimEndingDirectorySeparator(workspaceRoot), out bool yes) && yes && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? template : null;

        public Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            _roots[Path.TrimEndingDirectorySeparator(workspaceRoot)] = File.Exists(Path.Combine(workspaceRoot, "angular.json"));
            return Task.CompletedTask;
        }

        public void Forget() => _roots.Clear();
    }

    private sealed class FailingSelector : ILanguageSelector
    {
        public string Name => "test-failing-selector";

        public string Summary => "fails to read";

        public ILanguage? Select(string path, string? workspaceRoot) => null;

        public Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken) => throw new InvalidOperationException("cannot read");
    }

    // A canvas that draws icons' shapes through the path cache, as MAUI's does, and records what it drew.
    private sealed class IconCanvas(IconPathCache<object> cache) : IEditorCanvas, IIconCanvas
    {
        public List<FileIcon> Drawn { get; } = [];

        public HashSet<string> Texts { get; } = new(StringComparer.Ordinal);

        public int Layers { get; private set; }

        public bool DrawIcon(FileIcon icon, float x, float y, float size, bool open, bool dark)
        {
            var shape = icon.ShapeFor(open)!;
            var layers = shape.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                _ = cache.Get(shape, layers[i], size);
                _ = layers[i].ColorFor(dark);
                Layers++;
            }

            if (Drawn.Count < 64 && !Drawn.Contains(icon))
            {
                Drawn.Add(icon);
            }

            return true;
        }

        public void FillRect(float x, float y, float width, float height, uint argb)
        {
        }

        public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness)
        {
        }

        public void DrawText(string text, float x, float top, in TextStyle style) => Texts.Add(text);
    }
}
