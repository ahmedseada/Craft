// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Html;
using Craft.Web.Markup;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(HtmlPackage))]

namespace Craft.Plugins.Html;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Html\.</summary>
public sealed class HtmlPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Html";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new HtmlPlugin()];
}

/// <summary>
/// HTML as a pre-installed, removable plug-in (phase W, W-markup): the "html" language (.html, .htm), its colours (the living
/// standard; <c>&lt;script&gt;</c> as JavaScript, <c>&lt;style&gt;</c> as CSS, <c>style="…"</c> and <c>on*="…"</c> as code,
/// with the plug-in's own copy of Craft.Web, so they colour with the JavaScript and CSS plug-ins removed), brackets, folding
/// by elements, Format document and Format Selection, its icons and its settings (<c>CRAFT_PLUGIN_HTML_EMBEDDED</c>,
/// <c>CRAFT_PLUGIN_HTML_ATTRIBUTESASCODE</c>). Removing its folder opens .html files as plain text.
/// </summary>
public sealed class HtmlPlugin() : IdrakPlugin(HtmlParts.LanguageName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(HtmlParts.Language), () => EditorLanguages.Unregister(HtmlParts.LanguageName));
        Part(() => FoldingRangeProviders.Register(HtmlParts.Folding), () => FoldingRangeProviders.Unregister(HtmlParts.LanguageName));
        Part(() => EditorPlugins.Register(HtmlParts.Editor), () => EditorPlugins.Unregister(HtmlParts.LanguageName));
        foreach (var icon in HtmlParts.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>The "html" language and its parts, made once.</summary>
public static class HtmlParts
{
    /// <summary>Its name in <see cref="EditorLanguages"/> (and the plug-in's, its settings' <c>CRAFT_PLUGIN_HTML_*</c>).</summary>
    public const string LanguageName = "html";

    /// <summary>The language: .html, .htm.</summary>
    public static HtmlLanguage Language { get; } = new(LanguageName);

    /// <summary>Its folding.</summary>
    public static HtmlFolding Folding { get; } = new(LanguageName, [LanguageName], _ => HtmlLanguage.TokenizerOf(Language.Settings()));

    /// <summary>Its editor part: the settings.</summary>
    public static MarkupEditorPlugin Editor { get; } = new(LanguageName, "HTML: <script> and <style> as their languages, style and on* attributes as code",
        HtmlSettings.Declared, get => Language.Settings = get is null ? static () => new HtmlSettings() : () => HtmlSettings.Read(get));

    /// <summary>The icons of its files, over the library's with Craft's drawn shapes (W-files' set).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        Drawn(new(".html", "<>", 0xFFE8743B, "HTML")),
        Drawn(new(".htm", "<>", 0xFFE8743B, "HTML")),
    ];

    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(icon.Extension) is { } library ? icon with { Argb = library.Argb, Shape = library.Shape, LightArgb = library.LightArgb } : icon;
}
