// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;
using System.Reflection;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The registry inventory: every registry Craft's libraries hold (a static KindTable or SlotTable field), with its kind
/// and the names it has, listed in plans/inventory.md, which must match. CRAFT_UPDATE=1 rewrites it.
/// </summary>
[TestGroup("Inventory")]
public static class InventoryTests
{
    [Test]
    public static void RegistriesMatchTheInventory()
    {
        var actual = Inventory();
        var path = TestRunner.RepositoryPath("plans", "inventory.md");
        if (Environment.GetEnvironmentVariable("CRAFT_UPDATE") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Check.True(File.Exists(path), "plans/inventory.md exists (run with CRAFT_UPDATE=1 to write it)");
        Check.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual, "plans/inventory.md against the libraries' registries (a deliberate change: run with CRAFT_UPDATE=1)");
    }

    [Test]
    public static void EveryRegistryRefusesAnUnknownName()
    {
        Check.Throws<NotRegisteredException>(() => EditorLanguages.Get("no-such-language"), "EditorLanguages.Get of an unknown name");
        Check.Throws<NotRegisteredException>(() => EditorThemes.Get("no-such-theme"), "EditorThemes.Get of an unknown name");
        Check.True(EditorLanguages.ForFile("readme.no-such-extension") is null, "a file of no registered language opens as plain text");
    }

    [Test]
    public static void ThemesHaveTheLibraryDefaults()
    {
        Check.True(EditorThemes.Find(EditorThemes.Dark) is { IsDark: true }, "the library's dark theme is registered and dark");
        Check.True(EditorThemes.Find(EditorThemes.Light) is { IsDark: false }, "the library's light theme is registered and light");
        var dark = EditorThemes.Get("DARK");
        Check.Equal(dark.Colors.Foreground, dark.StyleOf("a class no theme knows").Argb, "an unknown class is drawn as plain text");
    }

    [Test]
    public static void OptionsReadEveryVariable()
    {
        var values = new Dictionary<string, string>
        {
            ["CRAFT_FONT"] = "Consolas", ["CRAFT_FONT_SIZE"] = "16.5", ["CRAFT_LINE_SPACING"] = "1.5", ["CRAFT_TAB_SIZE"] = "2",
            ["CRAFT_INSERT_SPACES"] = "false", ["CRAFT_THEME"] = "light", ["CRAFT_AUTOSAVE_SECONDS"] = "0", ["CRAFT_UNDO_GROUP_MS"] = "250",
            ["CRAFT_RECOVERY_FOLDER"] = "rec", ["CRAFT_BUFFER_CHUNK"] = "4096", ["CRAFT_LINE_ENDING"] = "crlf",
            ["CRAFT_WORKSPACE_IGNORE"] = "bin; out,dist", ["CRAFT_CARET_BLINK_MS"] = "0", ["CRAFT_CARET_WIDTH"] = "1.5",
            ["CRAFT_DOUBLE_CLICK_MS"] = "300", ["CRAFT_DOUBLE_CLICK_DISTANCE"] = "6", ["CRAFT_BRACKET_SCAN_LINES"] = "100",
            ["CRAFT_SCROLL_MARGIN_LINES"] = "1", ["CRAFT_SCROLL_MARGIN_COLUMNS"] = "3", ["CRAFT_BOTTOM_MARGIN_LINES"] = "0",
            ["CRAFT_GUTTER_PADDING"] = "1", ["CRAFT_AUTO_CLOSE_BRACKETS"] = "true", ["CRAFT_FIND_COUNT_LIMIT"] = "50", ["CRAFT_PARSE_WAIT_MS"] = "0",
            ["CRAFT_PLUGIN_FOLDERS"] = "p1; p2", ["CRAFT_PLUGINS_DISABLED"] = "Craft.CSharp, Other",
        };
        var options = CraftOptions.FromVariables(name => values.GetValueOrDefault(name));
        Check.Equal(new CraftOptions
        {
            FontFamily = "Consolas", FontSize = 16.5f, LineSpacing = 1.5f, TabSize = 2, InsertSpaces = false, Theme = "light",
            AutosaveSeconds = 0, UndoGroupMilliseconds = 250, RecoveryFolder = "rec", BufferChunkCharacters = 4096, NewLine = "\r\n",
            WorkspaceIgnore = options.WorkspaceIgnore, CaretBlinkMilliseconds = 0, CaretWidth = 1.5f, DoubleClickMilliseconds = 300,
            DoubleClickDistance = 6, BracketScanLines = 100, ScrollMarginLines = 1, ScrollMarginColumns = 3, BottomMarginLines = 0,
            GutterPaddingCells = 1, AutoCloseBrackets = true, FindCountLimit = 50, ParseWaitMilliseconds = 0,
            PluginFolders = options.PluginFolders, DisabledPlugins = options.DisabledPlugins,
        }, options, "options read from every variable");
        Check.SequenceEqual(["p1", "p2"], options.PluginFolders, "CRAFT_PLUGIN_FOLDERS split at ';'");
        Check.SequenceEqual(["Craft.CSharp", "Other"], options.DisabledPlugins, "CRAFT_PLUGINS_DISABLED split at ','");
        Check.SequenceEqual(["bin", "out", "dist"], options.WorkspaceIgnore, "CRAFT_WORKSPACE_IGNORE split at ';' and ','");
        Check.Equal(values.Count, CraftOptions.Variables.Count, "every variable is listed in CraftOptions.Variables");
        var error = Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_TAB_SIZE" ? "wide" : null), "a bad value");
        Check.True(error.Message.Contains("CRAFT_TAB_SIZE", StringComparison.Ordinal), "the error names the variable");
    }

    private static string Inventory()
    {
        var text = new StringBuilder("# Craft's registries\n\nGenerated by tests/Craft.Tests (Inventory group); CRAFT_UPDATE=1 rewrites it.\n\n| Registry | Assembly | Holds | Names |\n|---|---|---|---|\n");
        foreach (var assembly in ApiGuardTests.Libraries)
        {
            foreach (var type in assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
                {
                    var fieldType = field.FieldType;
                    if (!fieldType.IsGenericType)
                    {
                        continue;
                    }

                    var definition = fieldType.GetGenericTypeDefinition();
                    if (definition != typeof(KindTable<>) && definition != typeof(SlotTable<,>))
                    {
                        continue;
                    }

                    var held = fieldType.GetGenericArguments()[^1];
                    var names = (fieldType.GetProperty(definition == typeof(KindTable<>) ? "Names" : "Keys")!.GetValue(field.GetValue(null)) as IEnumerable)!
                        .Cast<object>().Select(n => n.ToString()).ToList();
                    text.Append($"| `{type.Name}` | {assembly.GetName().Name} | `{held.Name}` | {(names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"`{n}`")))} |\n");
                }
            }
        }

        return text.ToString();
    }
}
