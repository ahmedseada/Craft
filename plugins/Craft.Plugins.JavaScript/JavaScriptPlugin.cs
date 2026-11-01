// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.JavaScript;
using Craft.Web.Script;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(JavaScriptPackage))]

namespace Craft.Plugins.JavaScript;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.JavaScript\.</summary>
public sealed class JavaScriptPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.JavaScript";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new JavaScriptPlugin()];
}

/// <summary>
/// JavaScript as a pre-installed, removable plug-in (phase W, W-script): the "javascript" language (.js, .mjs, .cjs,
/// .jsx; jsconfig.json stays JSON), its colours (ES2025 and later: templates nested across lines, regular expressions with
/// the v and d flags and modifiers, decorators, <c>using</c>, import attributes, JSX), brackets, folding, Format document
/// and Format Selection, its icons, its settings (<c>CRAFT_PLUGIN_JAVASCRIPT_*</c>: version, projectVersion, jsx,
/// formatLineWidth) and a selector that reads a folder's project when it opens. On the public kit and Craft.Web only, as a
/// third party's language is; removing its folder (or naming it in <c>CRAFT_PLUGINS_DISABLED</c>) opens the files as plain text.
/// </summary>
public sealed class JavaScriptPlugin() : IdrakPlugin(JavaScriptLanguage.LanguageName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(JavaScriptLanguage.Language), () => EditorLanguages.Unregister(JavaScriptLanguage.LanguageName));
        Part(() => FoldingRangeProviders.Register(JavaScriptLanguage.Folding), () => FoldingRangeProviders.Unregister(JavaScriptLanguage.LanguageName));
        Part(() => LanguageSelectors.Register(JavaScriptLanguage.Selector), () => LanguageSelectors.Unregister(JavaScriptLanguage.LanguageName));
        Part(() => EditorPlugins.Register(JavaScriptLanguage.Editor), () => EditorPlugins.Unregister(JavaScriptLanguage.LanguageName));
        foreach (var icon in JavaScriptLanguage.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>The "javascript" language and its parts, made once.</summary>
public static class JavaScriptLanguage
{
    /// <summary>Its name in <see cref="EditorLanguages"/> (and the plug-in's, its settings' <c>CRAFT_PLUGIN_JAVASCRIPT_*</c>).</summary>
    public const string LanguageName = "javascript";

    /// <summary>The language: .js, .mjs, .cjs, .jsx.</summary>
    public static ScriptLanguage Language { get; } = new(LanguageName,
        "JavaScript (ES2025 and later) and JSX: colours, brackets, folding and format of .js, .mjs, .cjs and .jsx",
        [".js", ".mjs", ".cjs", ".jsx"], typeScript: false);

    /// <summary>Its folding.</summary>
    public static ScriptFolding Folding { get; } = new(LanguageName, [LanguageName], _ => Language.Tokenizer as ScriptTokenizer ?? ScriptTokenizer.For(ScriptDialect.JavaScript, ScriptVersion.Newest));

    /// <summary>Its selector (reads a folder's project when it opens).</summary>
    public static ScriptProjectSelector Selector { get; } = new(LanguageName);

    /// <summary>Its editor part: the settings.</summary>
    public static ScriptEditorPlugin Editor { get; } = new(LanguageName, "JavaScript and JSX: the version read, JSX in .js files, Format's line width",
        Language, ScriptSettings.Declared(javaScript: true, "JavaScript"));

    /// <summary>The icons of its files, over the library's with Craft's drawn shapes (W-files' set).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        Drawn(new(".js", "JS", 0xFFE8D44D, "JavaScript")),
        Drawn(new(".mjs", "JS", 0xFFE8D44D, "JavaScript module")),
        Drawn(new(".cjs", "JS", 0xFFE8D44D, "CommonJS module")),
        Drawn(new(".jsx", "JSX", 0xFF61DAFB, "JavaScript with JSX")),
    ];

    // The library's drawn icon of the same key (made before the plug-in registers its own over it).
    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(icon.Extension) is { } library ? icon with { Argb = library.Argb, Shape = library.Shape, LightArgb = library.LightArgb } : icon;
}
