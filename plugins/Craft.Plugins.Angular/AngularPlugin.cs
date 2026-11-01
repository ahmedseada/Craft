// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Angular;
using Craft.Web.Markup;
using Craft.Web.Script;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(AngularPackage))]

namespace Craft.Plugins.Angular;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Angular\.</summary>
public sealed class AngularPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Angular";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new AngularPlugin()];
}

/// <summary>
/// Angular as a pre-installed, removable plug-in (phase W, W-markup): "angular-html" (an angular.json project's .html, by
/// the selector, and every *.component.html, by name: bindings, events, interpolation, the control flow blocks, @defer,
/// @let, pipes) and "angular-typescript" (an Angular project's .ts with its inline <c>template:</c> and <c>styles:</c>), their
/// folding and format, the selector reading angular.json off the UI thread (again when the host's workspace changes), icons
/// and settings (<c>CRAFT_PLUGIN_ANGULAR_TEMPLATES</c>, <c>_INLINETEMPLATES</c>, <c>_VERSION</c>, <c>_PROJECTVERSION</c>).
/// Removing it gives .html back to "html" and .ts back to "typescript".
/// </summary>
public sealed class AngularPlugin() : IdrakPlugin(AngularParts.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(AngularParts.Templates), () => EditorLanguages.Unregister(AngularParts.TemplatesName));
        Part(() => EditorLanguages.Register(AngularParts.TypeScript), () => EditorLanguages.Unregister(AngularParts.TypeScriptName));
        Part(() => FoldingRangeProviders.Register(AngularParts.TemplateFolding), () => FoldingRangeProviders.Unregister(AngularParts.TemplatesName));
        Part(() => FoldingRangeProviders.Register(AngularParts.TypeScriptFolding), () => FoldingRangeProviders.Unregister(AngularParts.TypeScriptName));
        Part(() => LanguageSelectors.Register(AngularParts.Selector), () => LanguageSelectors.Unregister(AngularParts.PluginName));
        Part(() => EditorPlugins.Register(AngularParts.Editor), () => EditorPlugins.Unregister(AngularParts.PluginName));
        foreach (var icon in AngularParts.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>The Angular languages and their parts, made once.</summary>
public static class AngularParts
{
    /// <summary>The plug-in's name (its settings' <c>CRAFT_PLUGIN_ANGULAR_*</c>, its selector's).</summary>
    public const string PluginName = "angular";

    /// <summary>The templates' language name.</summary>
    public const string TemplatesName = "angular-html";

    /// <summary>The TypeScript-with-inline-templates language name.</summary>
    public const string TypeScriptName = "angular-typescript";

    /// <summary>Angular templates.</summary>
    public static AngularHtmlLanguage Templates { get; } = new(TemplatesName);

    /// <summary>An Angular project's TypeScript.</summary>
    public static AngularTypeScriptLanguage TypeScript { get; } = new(TypeScriptName);

    /// <summary>The templates' folding.</summary>
    public static HtmlFolding TemplateFolding { get; } = new(TemplatesName, [TemplatesName], _ => HtmlTokenizer.For(HtmlOptions.AngularTemplate));

    /// <summary>The TypeScript's folding (TypeScript's: an inline template folds as its literal).</summary>
    public static ScriptFolding TypeScriptFolding { get; } = new(TypeScriptName, [TypeScriptName], _ => ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest));

    /// <summary>The selector: an angular.json project's .html and .ts.</summary>
    public static AngularSelector Selector { get; } = new(PluginName, () => Templates.Settings(), Templates, TypeScript);

    /// <summary>Its editor part: the settings, and angular.json read again when the workspace changes.</summary>
    public static MarkupEditorPlugin Editor { get; } = new(PluginName, "Angular: which .html files are templates, inline templates, the version read",
        AngularSettings.Declared, Apply, MarkupEditorPlugin.RereadAngularOnWorkspaceChanged);

    /// <summary>The icons of an Angular component's parts, over the library's with Craft's drawn shapes (W-files' set).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        Drawn(new(".component.html", "<>", 0xFFDD0031, "Angular template")),
        Drawn(new(".component.ts", "C", 0xFFDD0031, "Angular component")),
    ];

    private static void Apply(Func<string, string?>? get)
    {
        Func<AngularSettings> read = get is null ? static () => new AngularSettings() : () => AngularSettings.Read(get);
        Templates.Settings = read;
        TypeScript.Settings = read;
    }

    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(icon.Extension) is { } library ? icon with { Argb = library.Argb, Shape = library.Shape, LightArgb = library.LightArgb } : icon;
}
