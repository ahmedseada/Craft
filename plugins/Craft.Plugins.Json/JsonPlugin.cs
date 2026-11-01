// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Json;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(JsonPackage))]

namespace Craft.Plugins.Json;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Json\.</summary>
public sealed class JsonPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Json";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new JsonPlugin()];
}

/// <summary>
/// JSON as a pre-installed, removable plug-in (phase W): the "json" language (.json, .jsonc: package.json, tsconfig.json,
/// appsettings.json, launchSettings.json, global.json), its colours, brackets, folding and Format document, and the
/// icons of its files. Built on the public line-state kit of <c>Craft.Abstractions</c> only (<see cref="LineStateLanguage"/>),
/// as a third party's language is; it was the C# plug-in's until phase W, so a web project's JSON keeps its colours without
/// C#. Removing its folder (or naming it in <c>CRAFT_PLUGINS_DISABLED</c>) opens .json files as plain text.
/// </summary>
public sealed class JsonPlugin() : IdrakPlugin(JsonLanguage.LanguageName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorLanguages.Register(JsonLanguage.Language), () => EditorLanguages.Unregister(JsonLanguage.LanguageName));
        Part(() => FoldingRangeProviders.Register(new JsonFolding()), () => FoldingRangeProviders.Unregister(JsonFolding.ProviderName));
        foreach (var icon in JsonLanguage.Icons)
        {
            Part(() => FileIcons.Register(icon), () => FileIcons.Unregister(icon.Extension));
        }
    }
}

/// <summary>
/// The "json" language: JSON with comments (<c>//</c>, <c>/* */</c>) and trailing commas, as tsconfig.json and .NET's
/// configuration read it; a property's name told from a value, escapes, numbers, true, false and null; brackets <c>{}</c>
/// and <c>[]</c>; Format document (members and elements one per line, comments and trailing commas kept).
/// </summary>
public static class JsonLanguage
{
    /// <summary>Its name in <see cref="EditorLanguages"/> (and the plug-in's).</summary>
    public const string LanguageName = "json";

    /// <summary>Its rules: <c>//</c> and <c>/* */</c> comments, <c>{}</c> and <c>[]</c> brackets, <c>"</c> strings with <c>\</c> escapes.</summary>
    public static LanguageRules Rules { get; } = new("//", "/*", "*/", [new('{', '}'), new('[', ']')]) { StringDelimiters = "\"" };

    /// <summary>The language, made once (it holds nothing per document: its sessions do).</summary>
    public static LineStateLanguage Language { get; } = new(LanguageName,
        "JSON with comments and trailing commas (package.json, tsconfig.json, appsettings.json, launchSettings.json, global.json, .jsonc): colours, folding and format",
        [".json", ".jsonc"], Rules, new JsonTokenizer(), new JsonFormatter());

    /// <summary>The icons of its files, registered over the library's (W: with Craft's drawn JSON shape, so they stay rich).</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        Drawn(new(".json", "{}", 0xFFE3B27A, "JSON")),
        Drawn(new(".jsonc", "{}", 0xFFE3B27A, "JSON with comments")),
    ];

    // The library's drawn .json icon's shape on the plug-in's icon (made before the plug-in registers its own over it).
    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(".json") is { Shape: { } shape } library ? icon with { Shape = shape, LightArgb = library.LightArgb } : icon;
}
