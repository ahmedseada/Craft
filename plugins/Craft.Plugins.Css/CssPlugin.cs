// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Css;
using Craft.Web.Style;
using Idrak.Abstraction;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(CssPackage))]

namespace Craft.Plugins.Css;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Css\.</summary>
public sealed class CssPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Css";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new CssRegistration()];
}

/// <summary>
/// CSS, SCSS and LESS as a pre-installed, removable plug-in (phase W): the languages "css" (.css), "scss" (.scss, and
/// .sass lexed as SCSS whose statements end at the line's end, not formatted) and "less" (.less), their folding, the icons
/// of their files and the editor plug-in "css" that carries the settings (<c>CRAFT_PLUGIN_CSS_VERSION</c>,
/// <c>…_PROJECTVERSION</c>, <c>…_FORMAT</c>). Built on <c>Craft.Abstractions</c>' public line-state kit and Craft.Web's
/// lexers (a copy in its own folder), as a third party's language is. Removing its folder (or naming it in
/// <c>CRAFT_PLUGINS_DISABLED</c>) opens these files as plain text.
/// </summary>
public sealed class CssRegistration() : IdrakPlugin(CssPlugin.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        foreach (var language in StyleLanguages.All)
        {
            Part(() => EditorLanguages.Register(language), () => EditorLanguages.Unregister(language.Name));
        }

        Part(() => FoldingRangeProviders.Register(CssFolding.Instance), () => FoldingRangeProviders.Unregister(CssFolding.ProviderName));
        foreach (var icon in StyleLanguages.Icons)
        {
            Part(() => FileIcons.Register(Drawn(icon)), () => FileIcons.Unregister(icon.Extension));
        }

        Part(() => EditorPlugins.Register(CssPlugin.Instance), () => EditorPlugins.Unregister(CssPlugin.PluginName));
    }

    // The library's drawn icon for the extension (W-files' vector set) kept on the plug-in's icon, so registering the
    // plug-in never turns a drawn icon back into a text badge (as the JSON plug-in does).
    private static FileIcon Drawn(FileIcon icon) =>
        FileIcons.Find(icon.Extension) is { Shape: { } shape } library ? icon with { Shape = shape, LightArgb = library.LightArgb } : icon;
}

/// <summary>
/// The editor plug-in "css": its settings on the settings page (the CSS version, whether a project's browserslist names
/// it, the built-in format), read by the languages each time a file opens.
/// </summary>
public sealed class CssPlugin : IEditorPlugin
{
    /// <summary>Its name, the prefix of its settings' variables (<c>CRAFT_PLUGIN_CSS_*</c>).</summary>
    public const string PluginName = "css";

    private CssPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static CssPlugin Instance { get; } = new();

    /// <inheritdoc />
    public string Name => PluginName;

    /// <inheritdoc />
    public string Summary => "CSS, SCSS and LESS: the version read, the project's browserslist, the built-in format";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => CssSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var source = host.Settings;
        CssSettings.Source = source;
        return new Deactivation(source);
    }

    private sealed class Deactivation(IPluginSettings source) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(CssSettings.Source, source))
            {
                CssSettings.Source = null;
            }
        }
    }
}

/// <summary>
/// The CSS plug-in's settings (plug-in "css"): <c>version</c> ("latest", or a Baseline year from 2020: a known name newer
/// than it reads as plain), <c>projectVersion</c> (a project's browserslist <c>baseline YYYY</c> sets the version when
/// <c>version</c> is "latest") and <c>format</c> (the built-in Format document and selection, and format on save).
/// </summary>
public sealed record CssSettings
{
    /// <summary>The defaults.</summary>
    public static CssSettings Default { get; } = new();

    /// <summary>The version read (<c>version</c>): null for "latest", else a Baseline year.</summary>
    public int? Year { get; init; }

    /// <summary>Whether a project's browserslist names the version when <see cref="Year"/> is "latest" (<c>projectVersion</c>).</summary>
    public bool ProjectVersion { get; init; } = true;

    /// <summary>Whether the built-in formatter formats (<c>format</c>); off, Format document changes nothing.</summary>
    public bool Format { get; init; } = true;

    /// <summary>The versions the setting offers.</summary>
    public static IReadOnlyList<string> Versions { get; } = ["latest", "2026", "2025", "2024", "2023", "2022", "2021", "2020"];

    /// <summary>The settings this plug-in declares, with their shapes on the settings page.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("version", "latest", "The CSS version read: latest, or a Baseline year (a name newer than it reads as plain)")
        {
            Shape = SettingShape.Choice(SettingSections.Editor, "CSS version", Versions, "CSS, SCSS, LESS"),
        },
        new("projectVersion", "true", "A project's browserslist (\"baseline 2024\") names the version when it is latest")
        {
            Shape = SettingShape.Toggle(SettingSections.Editor, "Version from the project", "CSS, SCSS, LESS"),
        },
        new("format", "true", "Format document and selection (and format on save) with the built-in formatter")
        {
            Shape = SettingShape.Toggle(SettingSections.Editor, "Built-in format", "CSS, SCSS, LESS"),
        },
    ];

    /// <summary>Where the settings are read from: the active host's (set while the plug-in is active), else none (the defaults).</summary>
    public static IPluginSettings? Source { get; set; }

    /// <summary>The settings now: <see cref="Source"/>'s, or the defaults.</summary>
    public static CssSettings Current => Source is { } source ? Read(source) : Default;

    /// <summary>The settings as <paramref name="settings"/> holds them; a value that does not parse keeps its default.</summary>
    public static CssSettings Read(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string Get(string key)
        {
            try
            {
                return settings.Get(CssPlugin.PluginName, key);
            }
            catch (NotRegisteredException)
            {
                return "";
            }
        }

        return new CssSettings
        {
            Year = ParseYear(Get("version")),
            ProjectVersion = Toggle(Get("projectVersion"), Default.ProjectVersion),
            Format = Toggle(Get("format"), Default.Format),
        };
    }

    /// <summary>A version setting's year: null for "latest", empty or a value that does not parse.</summary>
    public static int? ParseYear(string? text) =>
        int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year) && year is >= 2000 and <= 9999 ? year : null;

    private static bool Toggle(string text, bool fallback) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "on" or "1" or "yes" => true,
        "false" or "off" or "0" or "no" => false,
        _ => fallback,
    };
}

/// <summary>The folding provider of "css", "scss" and "less" (<see cref="StyleFolding"/>, by the file's own dialect).</summary>
public sealed class CssFolding : IFoldingRangeProvider
{
    /// <summary>Its name in <see cref="FoldingRangeProviders"/>.</summary>
    public const string ProviderName = "css";

    private CssFolding()
    {
    }

    /// <summary>The provider.</summary>
    public static CssFolding Instance { get; } = new();

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "CSS, SCSS, LESS: blocks, /* #region */ markers, block comments and runs of // comments over several lines";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; } = [StyleLanguages.Css, StyleLanguages.Scss, StyleLanguages.Less];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokenizer = (request.Session as LineStateSession)?.Tokenizer ?? StyleTokenizer.For(StyleLanguages.DialectOf(request.Language, null));
        return Task.FromResult<IReadOnlyList<FoldingRange>>(StyleFolding.Ranges(request.Snapshot, tokenizer, cancellationToken));
    }
}
