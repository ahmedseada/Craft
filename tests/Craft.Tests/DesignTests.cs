// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Text;
using Craft.Core.Workspaces;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>Phase D: the design's theme tokens and their contrast, the accent, the shell's menus, rail, quick open, status texts, branch, breadcrumb, plug-in list and telemetry.</summary>
[TestGroup("Design")]
public static class DesignTests
{
    [Test]
    public static void TheDarkThemeIsTheDesignsAndEveryInterfacePairReads()
    {
        var dark = EditorThemes.Get(EditorThemes.Dark).Colors;
        Check.SequenceEqual(
            [0xFF101217u, 0xFF16181Du, 0xFF1A1D23u, 0xFF1F232Bu, 0xFF252A33u, 0xFF2A2E37u, 0xFF343944u, 0xFFD5D9E0u, 0xFF9AA2B1u, 0xFFE5A54Bu, 0xFF1A1206u, 0xFF5FC9B4u, 0xFFE3B27Au, 0xFFF07178u],
            [dark.Ground, dark.Surface, dark.Raised, dark.RaisedHigh, dark.InterfaceSelection, dark.Border, dark.BorderStrong, dark.Text, dark.SecondaryText, dark.Accent, dark.AccentInk, dark.Success, dark.Warning, dark.Error],
            "the dark theme's tokens are docs/design's (ground, surface, raised, raised high, selection, border, strong border, text, secondary, accent, accent ink, success, warning, error)");

        foreach (var name in (string[])[EditorThemes.Dark, EditorThemes.Light])
        {
            var theme = EditorThemes.Get(name);
            CheckReadable(name, theme.Colors);
            foreach (var accent in (uint[])[AccentColors.Amber, AccentColors.Teal, AccentColors.Blue, AccentColors.Rose, 0xFFFFFF00, 0xFF000080])
            {
                var accented = EditorThemes.WithAccent(theme, accent);
                Check.True(accented.Name == theme.Name && accented.IsDark == theme.IsDark && accented.StyleOf(TokenClasses.Keyword) == theme.StyleOf(TokenClasses.Keyword),
                    $"{name} with #{accent & 0xFFFFFF:X6}: the same theme, its accent changed");
                Check.True(accented.Colors.Caret == accented.Colors.Accent, $"{name} with #{accent & 0xFFFFFF:X6}: the caret is the accent");
                CheckReadable($"{name} with #{accent & 0xFFFFFF:X6}", accented.Colors);
            }
        }

        Check.Equal(AccentColors.Teal, EditorThemes.WithAccent(EditorThemes.Get(EditorThemes.Dark), AccentColors.Teal).Colors.Accent, "a design accent on the dark theme is kept exactly");

        // A theme made before the tokens (a plug-in's) draws the shell in its own colours.
        var old = new EditorColors(0xFF000000, 0xFFEEEEEE, 0xFF333333, 0xFF111111, 0xFFFF0000, 0xFF000000, 0xFF999999, 0xFFFFFFFF, 0xFF222222, 0xFF444444);
        Check.True(old.Raised == old.CurrentLine && old.Text == old.Foreground && old.InterfaceSelection == old.Selection && old.AccentInk == old.Background && old.BorderStrong == old.GutterText,
            "the new tokens default from the constructor's colours");
    }

    private static void CheckReadable(string name, EditorColors c)
    {
        uint[] grounds = [c.Ground, c.Surface, c.Raised, c.RaisedHigh, c.InterfaceSelection, c.CurrentLine];
        foreach (var (token, color) in (ReadOnlySpan<(string, uint)>)[("text", c.Text), ("secondary text", c.SecondaryText), ("accent", c.Accent), ("success", c.Success), ("warning", c.Warning), ("error", c.Error)])
        {
            double ratio = ColorContrast.MinRatio(color, grounds);
            Check.True(ratio >= ColorContrast.Text, $"{name}: {token} reads at 4.5:1 or more on the ground, the surfaces, the selected row and the caret line ({ratio:F2}:1)");
        }

        double ink = ColorContrast.Ratio(c.AccentInk, c.Accent);
        Check.True(ink >= ColorContrast.Text, $"{name}: the accent's ink reads on the accent ({ink:F2}:1)");
        double gutter = ColorContrast.Ratio(c.GutterText, c.Background);
        Check.True(gutter >= ColorContrast.Text, $"{name}: line numbers read on the editor ({gutter:F2}:1)");
    }

    [Test]
    public static void TheDarkCodeColoursAreTheDesignsClasses()
    {
        var dark = EditorThemes.Get(EditorThemes.Dark);
        // docs/design/Main.dc.html: .k keyword, .t type, .s string, .c comment (italic), .m method, .v variable, .p plain.
        foreach (var (tokenClass, argb) in (ReadOnlySpan<(string, uint)>)[
            (TokenClasses.Keyword, 0xFF82AAFF), (TokenClasses.ClassName, 0xFF5FC9B4), (TokenClasses.InterfaceName, 0xFF5FC9B4), (TokenClasses.StructName, 0xFF5FC9B4),
            (TokenClasses.String, 0xFFE3B27A), (TokenClasses.Comment, 0xFF8790A0), (TokenClasses.MethodName, 0xFFE8D58A), (TokenClasses.LocalName, 0xFFB8C7DC),
            (TokenClasses.ParameterName, 0xFFB8C7DC), (TokenClasses.Punctuation, 0xFFD5D9E0)])
        {
            Check.Equal(argb, dark.StyleOf(tokenClass).Argb, $"the dark theme's '{tokenClass}' is the design's #{argb & 0xFFFFFF:X6}");
        }

        Check.True(dark.StyleOf(TokenClasses.Comment).Italic, "comments are italic, as the design's .c");
    }

    [Test]
    public static void TheAccentAndTheShellsOptionsParse()
    {
        Check.True(AccentColors.TryParse("Teal", out uint teal) && teal == AccentColors.Teal, "a design name, any case");
        Check.True(AccentColors.TryParse("#123ABC", out uint hex) && hex == 0xFF123ABC, "#RRGGBB");
        Check.True(!AccentColors.TryParse("purple", out _) && !AccentColors.TryParse("#12345", out _), "anything else is not an accent");
        var options = CraftOptions.FromVariables(n => n switch { "CRAFT_ACCENT" => "rose", "CRAFT_TITLE_BAR" => "PAGE", _ => null });
        Check.True(options.Accent == AccentColors.Rose && options.TitleBar == "page", "CRAFT_ACCENT and CRAFT_TITLE_BAR read");
        var error = Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_ACCENT" ? "purple" : null), "a bad accent");
        Check.True(error.Message.Contains("amber, teal, blue, rose", StringComparison.Ordinal), "the error names what it takes");
        var defaults = new CraftOptions();
        Check.True(defaults is { FontFamily: "JetBrains Mono", FontSize: 13, InterfaceFont: "Inter", InterfaceFontSize: 13, TouchTarget: 44, Accent: null },
            "the design's fonts and sizes are the defaults");
        Check.Equal(22f, MathF.Ceiling(defaults.FontSize * defaults.LineSpacing), "13-unit code on 22-unit lines");
    }

    [Test]
    public static async Task TheShellsMenusAndRailAreRegistriesOfCommands()
    {
        using var folder = new TempFolder();
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        var recent = RecentList.Load(folder.File("data/recent.json"), 10);
        using var host = new EditorHost();
        var menusBefore = EditorMenus.Names.Count;
        var railBefore = EditorRailItems.Names.Count;
        var ui = new Ui();
        using (var shell = new ShellModel(host, settings, recent, new Dialogs(folder.Path), ui))
        {
            var file = ShellMenus.Rows(EditorMenus.File, host);
            Check.True(file.Any(r => r.CommandId == ShellCommandIds.OpenFile && r.Title == "Open a file" && r.Chord == "Ctrl+O"), "File has Open a file, its chord from its binding, its title without the palette's prefix");
            Check.True(file.Count(r => r.StartsGroup) >= 2, "the File menu's groups are separated");
            Check.True(!file.First(r => r.CommandId == ShellCommandIds.Save).Enabled, "no tab: Save shows disabled");
            Check.True(!ShellMenus.Rows(EditorMenus.Plugins, host).Any(), "the Plug-ins page's command is the app's: unregistered, its item does not show");
            Check.True(EditorMenus.Menus().Take(6).SequenceEqual(EditorMenus.Standard), "the design's six menus come first");

            // A plug-in adds a menu item and a rail item on the same contracts.
            EditorCommands.Register(new EditorCommand("test.hello", "Test: Say hello", (_, _) => Task.CompletedTask));
            EditorMenus.Register(new MenuItem(EditorMenus.Plugins, "test.hello"));
            EditorMenus.Register(new MenuItem("Tools", "test.hello", "Hello again"));
            try
            {
                Check.True(ShellMenus.Rows(EditorMenus.Plugins, host) is [{ Title: "Say hello", Enabled: true }], "a plug-in's item shows in its menu");
                Check.True(EditorMenus.Menus().Contains("Tools"), "a new menu comes after the design's");
                var rail = EditorRailItems.All;
                Check.True(rail[^1].Id == "craft.settings" && rail[^1].AtBottom, "Settings sits at the rail's foot");
                Check.True(EditorCommands.Find(ShellCommandIds.Agent) is null && rail.Any(r => r.CommandId == ShellCommandIds.Agent),
                    "the agent's item is registered; its command is A2's, so the rail does not show it yet");
                Check.True(!rail.Any(r => r.Icon == RailIcons.SourceControl), "source control shows only when a plug-in registers it");

                shell.ViewRequested += (_, view) => ui.Views.Add(view);
                Check.True(await host.ExecuteCommandAsync(ShellCommandIds.QuickOpen) && ui.Views.SequenceEqual([ShellViewNames.QuickOpen]), "Ctrl+P's command asks for quick open");
                Check.Equal(ShellCommandIds.QuickOpen, EditorKeyBindings.Find(KeyChord.Parse("Ctrl+P"))?.CommandId, "Ctrl+P runs it");
                Check.Equal(ShellCommandIds.TogglePanel, EditorKeyBindings.Find(KeyChord.Parse("Ctrl+J"))?.CommandId, "Ctrl+J shows or hides the bottom panel");
            }
            finally
            {
                EditorMenus.Unregister(EditorMenus.Plugins, "test.hello");
                EditorMenus.Unregister("Tools", "test.hello");
                EditorCommands.Unregister("test.hello");
            }

            using (var edit = new ShellEditCommands(() => null))
            {
                var rows = ShellMenus.Rows(EditorMenus.Edit, host);
                Check.True(rows.Any(r => r is { Title: "Undo", Chord: "Ctrl+Z", Enabled: false }), "Edit: Undo shows the editor's own key, disabled with no editor");
            }

            Check.True(!ShellMenus.Rows(EditorMenus.Edit, host).Any(), "the edit commands are taken back");
        }

        Check.Equal(menusBefore, EditorMenus.Names.Count, "the shell's menu items are taken back");
        Check.Equal(railBefore, EditorRailItems.Names.Count, "and its rail items");
    }

    [Test]
    public static async Task QuickOpenFindsFilesByFuzzyNameAndCommandsAfterAnArrow()
    {
        Check.True(FuzzyMatch.Score("edtool", "EditorTools.cs") > FuzzyMatch.Score("edtool", "editedtoolbox.txt"), "word starts and humps score above scattered letters");
        Check.True(FuzzyMatch.Score("et", "EditorTools.cs") > FuzzyMatch.Score("et", "Mettle.cs"), "a camel hump beats a letter inside a word");
        Check.Equal(FuzzyMatch.None, FuzzyMatch.Score("xyz", "EditorTools.cs"), "letters not in order: no match");
        Check.Equal(0, FuzzyMatch.Score("", "anything"), "an empty query matches everything equally");

        using var folder = new TempFolder();
        foreach (var path in (string[])["src/Craft.Ai/EditorTools.cs", "src/Craft.Ai/ToolText.cs", "src/Craft.Editor/EditorHost.cs", "bin/EditorTools.cs", "README.md"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder.File(path))!);
            await File.WriteAllTextAsync(folder.File(path), "x");
        }

        using var host = new EditorHost();
        using var workspace = new Workspace(folder.Path, new CraftOptions());
        var quick = new QuickOpen(host, 5);
        await quick.IndexAsync(workspace);
        Check.Equal(4, quick.Files.Count, "the workspace's files, bin left out");
        var found = quick.Query("edtool");
        Check.True(found.Count >= 1 && found[0].Text == "EditorTools.cs" && found[0].Detail == Path.Combine("src", "Craft.Ai") && found[0].Path == Path.GetFullPath(folder.File("src/Craft.Ai/EditorTools.cs")),
            "the best match first, with its folder and full path");
        Check.True(quick.Query("ehost")[0].Text == "EditorHost.cs", "another name");

        EditorCommands.Register(new EditorCommand("test.format", "Edit: Format the test", (_, _) => Task.CompletedTask));
        EditorCommands.Register(new EditorCommand("test.never", "Edit: Format never", (_, _) => Task.CompletedTask, _ => false));
        try
        {
            var commands = quick.Query("> fmt test");
            Check.True(commands.Count == 0 || commands.All(c => c.CommandId is not null), "after '>' only commands");
            var format = quick.Query(">format");
            Check.True(format.Any(c => c.CommandId == "test.format") && !format.Any(c => c.CommandId == "test.never"), "commands that can run, by fuzzy title");
        }
        finally
        {
            EditorCommands.Unregister("test.format");
            EditorCommands.Unregister("test.never");
        }

        var capped = new QuickOpen(host, 5) { CraftOptionsLimit = 2 };
        await capped.IndexAsync(workspace);
        Check.True(capped.Capped && capped.Files.Count == 2, "the listing stops at CRAFT_QUICK_OPEN_FILES and says so");
        await quick.IndexAsync(null);
        Check.True(quick.Files.Count == 0 && quick.Query("ed").Count == 0, "no folder: no files");
    }

    [Test]
    public static void TheBranchIsReadFromTheGitFolder()
    {
        Check.Equal("main", GitBranch.FromHead("ref: refs/heads/main\n"u8), "a branch");
        Check.Equal("feature/x", GitBranch.FromHead("ref: refs/heads/feature/x\r\n"u8), "a branch with a slash");
        Check.Equal("4bbf38a", GitBranch.FromHead("4bbf38a0123456789abcdef0123456789abcdef0\n"u8), "detached: the commit's short hash");
        Check.Equal(null, GitBranch.FromHead("not a head"u8), "anything else: none");

        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.File("repo/.git"));
        File.WriteAllText(folder.File("repo/.git/HEAD"), "ref: refs/heads/main\n");
        Directory.CreateDirectory(folder.File("repo/src/deep"));
        Check.Equal("main", GitBranch.Read(folder.File("repo/src/deep")), "found from a folder inside the repository");
        Directory.CreateDirectory(folder.File("worktree"));
        Directory.CreateDirectory(folder.File("repo/.git/worktrees/wt"));
        File.WriteAllText(folder.File("repo/.git/worktrees/wt/HEAD"), "ref: refs/heads/topic\n");
        File.WriteAllText(folder.File("worktree/.git"), "gitdir: ../repo/.git/worktrees/wt\n");
        Check.Equal("topic", GitBranch.Read(folder.File("worktree")), "a worktree's .git file is followed");
    }

    [Test]
    public static void TheStatusTextsAreTheDesigns()
    {
        Check.Equal("Spaces: 4", ShellText.Indentation(new CraftOptions()), "spaces");
        Check.Equal("Tab size: 2", ShellText.Indentation(new CraftOptions { InsertSpaces = false, TabSize = 2 }), "tabs");
        Check.Equal("CRLF", ShellText.LineEnding("\r\n"), "CRLF");
        Check.Equal("UTF-8", ShellText.EncodingName(new UTF8Encoding(false), false), "UTF-8");
        Check.Equal("Ln 22, Col 33", ShellText.Position(21, 32), "one-based");
        Check.Equal("412 MB", ShellText.Bytes(412L * 1024 * 1024), "MB");
        Check.Equal("3.1 GB", ShellText.Bytes((long)(3.1 * 1024 * 1024 * 1024)), "GB");
        Check.Equal("⚠ 1", ShellText.Problems(0, 1), "one warning");
        Check.Equal("✕ 2  ⚠ 1", ShellText.Problems(2, 1), "errors first");
        Check.Equal("", ShellText.Problems(0, 0), "none");
        Check.Equal("no model", ShellText.ModelChip(null), "no provider: no model");
        Check.Equal("no model", ShellText.ModelChip(ModelProviderStatus.None), "a provider with none set");
        var status = new ModelProviderStatus { Where = "local", Roles = [new ModelRole("Chat and agent", "store:coder-q4", "Loaded")], Device = "vulkan:0", DeviceMemoryBytes = (long)(3.1 * 1024 * 1024 * 1024), TokensPerSecond = 41.2 };
        Check.Equal("store:coder-q4 · vulkan:0", ShellText.ModelChip(status), "the chip");
        Check.Equal("local · vulkan:0 · 3.1 GB in use · 41 tok/s", ShellText.ModelUsage(status), "the status bar's model text, as the design's");
        Check.Equal("local", ShellText.ModelUsage(status with { Device = null, DeviceMemoryBytes = null, TokensPerSecond = null }), "only what was measured");
        Check.Equal(null, ShellText.Semantic(null), "no editor: no card");
        var parts = ShellText.Breadcrumb(Path.Combine("root", "src", "Tools", "EditorTools.cs"), Path.Combine("root"), [new ScopeName("EditorTools", ScopeKind.Type), new ScopeName("Get", ScopeKind.Method)]);
        Check.SequenceEqual(["Tools", "EditorTools.cs", "EditorTools", "Get"], [.. parts.Select(p => p.Name)], "folder › file › type › member");
    }

    [Test]
    public static async Task TheCSharpSessionGivesTheBreadcrumbsScopes()
    {
        const string Text = "namespace Craft.Plugins.Tools;\n\npublic sealed class EditorTools\n{\n    public string GetSelection()\n    {\n        return \"\";\n    }\n\n    public int Count { get; set; }\n}\n";
        var buffer = new PieceTableBuffer(Text);
        var language = new CSharpLanguage();
        using var session = language.Open(buffer);
        await session.WhenIdle();
        var snapshot = buffer.Current;
        int inMethod = Text.IndexOf("return", StringComparison.Ordinal);
        var scopes = session.ScopeAt(snapshot, inMethod);
        Check.True(scopes is [{ Name: "EditorTools", Kind: ScopeKind.Type }, { Name: "GetSelection", Kind: ScopeKind.Method }], "a method's body: its type and the method");
        Check.True(ReferenceEquals(scopes, session.ScopeAt(snapshot, inMethod + 2)), "the same scopes: the same list (nothing made on a caret move)");
        Check.True(session.ScopeAt(snapshot, Text.IndexOf("Count", StringComparison.Ordinal)) is [_, { Name: "Count", Kind: ScopeKind.Member }], "a property");
        Check.True(session.ScopeAt(snapshot, 0) is [], "outside a type: none");
        ILanguageSession plain = new PlainSession();
        Check.True(plain.ScopeAt(snapshot, 0).Count == 0, "a language without scopes gives none (the default)");
    }

    [Test]
    public static void ThePluginListShowsStatesSourcesAndFilters()
    {
        var installed = new List<PluginLoadResult>
        {
            new("Craft.Good", Path.Combine(CraftPlugins.InstalledFolder, "Craft.Good"), true, null) { Manifest = new PluginManifest { Name = "Craft.Good", Version = "1.2.0", Summary = "Good things", Capabilities = ["files"] } },
            new("Craft.Old", Path.Combine("elsewhere", "Craft.Old"), true, null) { Outdated = true, Note = "built against contracts 0.9" },
            new("Craft.Broken", Path.Combine("elsewhere", "Craft.Broken"), false, "plugin.json: contractVersion 9.0 is newer than this Craft's 1.0"),
            new("Craft.Off", Path.Combine("elsewhere", "Craft.Off"), false, null) { Disabled = true },
        };
        var rows = PluginList.Rows(installed, []);
        var good = rows.First(r => r.Name == "Craft.Good");
        Check.True(good is { State: PluginState.Active, Source: "Built in", Version: "1.2.0", Summary: "Good things" } && good.Capabilities.SequenceEqual(["files"]), "a loaded plug-in beside the app");
        Check.True(rows.First(r => r.Name == "Craft.Old") is { State: PluginState.Outdated, Source: "Folder" }, "an older contract is marked");
        Check.True(rows.First(r => r.Name == "Craft.Broken") is { State: PluginState.Problem, Problem: { } problem } && problem.Contains("newer", StringComparison.Ordinal), "a refused one, with its reason");
        Check.True(rows.First(r => r.Name == "Craft.Off") is { State: PluginState.Off }, "a disabled one");
        var (all, overrides, problems) = PluginList.Counts(rows);
        Check.True(all == rows.Count && overrides == 1 && problems == 1, "the filters' counts");
        Check.True(PluginList.Filter(rows, PluginFilter.Problems, null) is [{ Name: "Craft.Broken" }], "Problems keeps the refused one");
        Check.True(PluginList.Filter(rows, PluginFilter.All, "good") is [{ Name: "Craft.Good" }], "the filter text, ignoring case");
        Check.True(rows.Where(r => r.Source == "Idrak").Select(r => r.Name).SequenceEqual(Idrak.Abstraction.Plugins.IdrakPlugins.Names.Order(StringComparer.OrdinalIgnoreCase)),
            "Idrak's registered plug-ins follow, from its public registry");
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

    private sealed class Dialogs(string folder) : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(folder);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class Ui : IShellUi
    {
        public List<string> Views { get; } = [];

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}
