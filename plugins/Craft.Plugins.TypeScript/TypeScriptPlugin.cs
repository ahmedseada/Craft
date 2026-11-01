// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.TypeScript;
using Craft.Web.Script;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(TypeScriptPackage))]

namespace Craft.Plugins.TypeScript;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.TypeScript\.</summary>
public sealed class TypeScriptPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.TypeScript";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new TypeScriptPlugin()];
}

/// <summary>
/// TypeScript as a pre-installed, removable plug-in (phase W, W-script): the "typescript" language (.ts, .mts, .cts, .tsx,
/// .d.ts), its colours (all of JavaScript's plus <c>satisfies</c>, <c>accessor</c>, const and in/out type parameters,
/// <c>declare</c>, <c>namespace</c>, <c>abstract</c>, <c>override</c>, <c>keyof</c>, <c>infer</c>, <c>asserts</c>,
/// <c>import type</c>, template literal types; JSX with generics in .tsx), brackets, folding, Format document and Format
/// Selection, its icons, its settings (<c>CRAFT_PLUGIN_TYPESCRIPT_*</c>: version, projectVersion, formatLineWidth) and a
/// selector that reads a folder's project when it opens. On the public kit and Craft.Web only; removing its folder opens the
/// files as plain text.
/// </summary>
public sealed class TypeScriptPlugin() : IdrakPlugin(TypeScriptLanguage.LanguageName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(TypeScriptLanguage.Language), () => EditorLanguages.Unregister(TypeScriptLanguage.LanguageName));
        Part(() => FoldingRangeProviders.Register(TypeScriptLanguage.Folding), () => FoldingRangeProviders.Unregister(TypeScriptLanguage.LanguageName));
        Part(() => LanguageSelectors.Register(TypeScriptLanguage.Selector), () => LanguageSelectors.Unregister(TypeScriptLanguage.LanguageName));
        Part(() => EditorPlugins.Register(TypeScriptLanguage.Editor), () => EditorPlugins.Unregister(TypeScriptLanguage.LanguageName));
        foreach (var icon in TypeScriptLanguage.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>The "typescript" language and its parts, made once.</summary>
public static class TypeScriptLanguage
{
    /// <summary>Its name in <see cref="EditorLanguages"/> (and the plug-in's, its settings' <c>CRAFT_PLUGIN_TYPESCRIPT_*</c>).</summary>
    public const string LanguageName = "typescript";

    /// <summary>The language: .ts, .mts, .cts, .tsx, .d.ts.</summary>
    public static ScriptLanguage Language { get; } = new(LanguageName,
        "TypeScript and TSX: colours, brackets, folding and format of .ts, .mts, .cts, .tsx and .d.ts",
        [".ts", ".mts", ".cts", ".tsx", ".d.ts"], typeScript: true);

    /// <summary>Its folding.</summary>
    public static ScriptFolding Folding { get; } = new(LanguageName, [LanguageName], _ => Language.Tokenizer as ScriptTokenizer ?? ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest));

    /// <summary>Its selector (reads a folder's project when it opens).</summary>
    public static ScriptProjectSelector Selector { get; } = new(LanguageName);

    /// <summary>Its editor part: the settings.</summary>
    public static ScriptEditorPlugin Editor { get; } = new(LanguageName, "TypeScript and TSX: the version read, Format's line width",
        Language, ScriptSettings.Declared(javaScript: false, "TypeScript"));

    /// <summary>The icons of its files, over the library's with Craft's drawn shapes (W-files' set).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        Drawn(new(".ts", "TS", 0xFF3178C6, "TypeScript")),
        Drawn(new(".mts", "TS", 0xFF3178C6, "TypeScript module")),
        Drawn(new(".cts", "TS", 0xFF3178C6, "CommonJS TypeScript")),
        Drawn(new(".tsx", "TSX", 0xFF61DAFB, "TypeScript with JSX")),
        Drawn(new(".d.ts", "TS", 0xFF3178C6, "TypeScript declarations")),
    ];

    // The library's drawn icon of the same key (made before the plug-in registers its own over it).
    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(icon.Extension) is { } library ? icon with { Argb = library.Argb, Shape = library.Shape, LightArgb = library.LightArgb } : icon;
}
