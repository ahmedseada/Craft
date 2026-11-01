// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Web.Script;

/// <summary>
/// A script plug-in's settings (W-script): <c>version</c> ("latest", or "es2022" for JavaScript, "5.4" for TypeScript),
/// <c>projectVersion</c> (read the version from the project's files when <c>version</c> is latest), <c>jsx</c> (JavaScript:
/// auto, on, off), <c>formatLineWidth</c> (0: the formatter breaks no line), <c>stateCapacity</c> and <c>stateDepth</c>
/// (the lexer's interned states: rule 77). Each is a <c>CRAFT_PLUGIN_&lt;PLUGIN&gt;_&lt;KEY&gt;</c> variable and a
/// <c>.craft/settings.json</c> entry, on the settings page's Editor section.
/// </summary>
public sealed record ScriptSettings
{
    /// <summary>The language version ("latest", or a version: "es2022", "5.4").</summary>
    public string Version { get; init; } = "latest";

    /// <summary>Whether the project's files name the version when <see cref="Version"/> is latest.</summary>
    public bool ProjectVersion { get; init; } = true;

    /// <summary>JSX in .js files: auto (a React or Preact project), on, off; .jsx and .tsx always read JSX, .ts never.</summary>
    public string Jsx { get; init; } = "auto";

    /// <summary>The width past which Format breaks a line after a comma; 0 breaks none.</summary>
    public int FormatLineWidth { get; init; }

    /// <summary>The stacks of frames the lexer's table holds (templates and JSX nested across lines).</summary>
    public int StateCapacity { get; init; } = LineStates.DefaultCapacity;

    /// <summary>The frames a stack may have.</summary>
    public int StateDepth { get; init; } = LineStates.DefaultMaxDepth;

    /// <summary>The settings a script plug-in declares (<paramref name="javaScript"/>: with <c>jsx</c>), in the Editor section under <paramref name="group"/>.</summary>
    public static IReadOnlyList<PluginSetting> Declared(bool javaScript, string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        var settings = new List<PluginSetting>
        {
            Setting("version", "latest", javaScript ? "The ECMAScript version read: latest, or es2015 to es2026; an older one reads a newer word as a name" : "The TypeScript version read: latest, or 4.9, 5.4, ...; an older one reads a newer word as a name",
                SettingShape.Text(SettingSections.Editor, "Language version", group)),
            Setting("projectVersion", "true", javaScript ? "When the version is latest, read it from the project (jsconfig.json or tsconfig.json's target)" : "When the version is latest, read it from the project (node_modules/typescript, or package.json's devDependencies)",
                SettingShape.Toggle(SettingSections.Editor, "Version from the project", group)),
        };
        if (javaScript)
        {
            settings.Add(Setting("jsx", "auto", "JSX in .js files: auto (react or preact in package.json), on, off; .jsx files always",
                SettingShape.Choice(SettingSections.Editor, "JSX in .js files", ["auto", "on", "off"], group)));
        }

        settings.Add(Setting("formatLineWidth", "0", "Format breaks a line after a comma past this width (0: no line is broken)",
            SettingShape.Whole(SettingSections.Editor, "Format line width", 0, 1000, "columns", group)));
        settings.Add(Setting("stateCapacity", LineStates.DefaultCapacity.ToString(CultureInfo.InvariantCulture), "The nested template and JSX states the lexer keeps (past them a line starts over)",
            SettingShape.Whole(SettingSections.Editor, "Nested states kept", 64, 1 << 20, null, group)));
        settings.Add(Setting("stateDepth", LineStates.DefaultMaxDepth.ToString(CultureInfo.InvariantCulture), "The deepest nesting of templates and JSX the lexer follows across lines",
            SettingShape.Whole(SettingSections.Editor, "Nesting followed", 2, 127, null, group)));
        return settings;
    }

    /// <summary>The settings <paramref name="get"/> gives by key (null: the default); a value that does not parse keeps its default.</summary>
    public static ScriptSettings Read(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);
        var defaults = new ScriptSettings();
        string jsx = (get("jsx") ?? defaults.Jsx).Trim().ToLowerInvariant();
        return new ScriptSettings
        {
            Version = get("version") is { Length: > 0 } version ? version.Trim() : defaults.Version,
            ProjectVersion = Toggle(get("projectVersion"), defaults.ProjectVersion),
            Jsx = jsx is "on" or "off" or "auto" ? jsx : defaults.Jsx,
            FormatLineWidth = Whole(get("formatLineWidth"), defaults.FormatLineWidth, 0, 1000),
            StateCapacity = Whole(get("stateCapacity"), defaults.StateCapacity, 64, 1 << 20),
            StateDepth = Whole(get("stateDepth"), defaults.StateDepth, 2, 127),
        };
    }

    private static PluginSetting Setting(string key, string value, string description, SettingShape shape) => new(key, value, description) { Shape = shape };

    private static int Whole(string? text, int fallback, int least, int most) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= least && value <= most ? value : fallback;

    private static bool Toggle(string? text, bool fallback) =>
        bool.TryParse(text?.Trim(), out bool value) ? value : text?.Trim() switch { "1" or "on" or "yes" => true, "0" or "off" or "no" => false, _ => fallback };
}

/// <summary>
/// JavaScript or TypeScript as a line-state language (W-script): the dialect per file (.jsx and .tsx read JSX, .ts never,
/// .js by the <c>jsx</c> setting or the project), the version by the setting, then the project's files, then the latest.
/// A file whose project was not read yet opens at once at the setting's version (latest by default) and its project is
/// read off the UI thread; when it names another version the session changes dialect and raises
/// <see cref="ILanguageSession.Reclassified"/>. Format document and Format Selection through <see cref="ScriptFormatter"/>.
/// </summary>
public sealed class ScriptLanguage : LineStateLanguage, ILanguage
{
    private Func<ScriptSettings> _settings;

    /// <summary>The language <paramref name="name"/> (TypeScript when <paramref name="typeScript"/>) opening <paramref name="extensions"/>.</summary>
    public ScriptLanguage(string name, string summary, IReadOnlyList<string> extensions, bool typeScript, Func<ScriptSettings>? settings = null)
        : base(name, summary, extensions, ScriptRules, ScriptTokenizer.For(typeScript ? ScriptDialect.TypeScript : ScriptDialect.JavaScript, ScriptVersion.Newest),
            new ScriptFormatter(ScriptTokenizer.For(typeScript ? ScriptDialect.TypeScript : ScriptDialect.JavaScript, ScriptVersion.Newest)))
    {
        IsTypeScript = typeScript;
        _settings = settings ?? (static () => new ScriptSettings());
    }

    /// <summary>The rules of JavaScript and TypeScript: <c>//</c> and <c>/* */</c> comments, <c>{}</c> <c>[]</c> <c>()</c> brackets, <c>" ' `</c> strings.</summary>
    public static LanguageRules ScriptRules { get; } = new("//", "/*", "*/", [new('{', '}'), new('[', ']'), new('(', ')')]) { StringDelimiters = "\"'`" };

    /// <summary>Whether it is TypeScript (else JavaScript).</summary>
    public bool IsTypeScript { get; }

    /// <summary>Where its settings come from now (the plug-in's host once active; the defaults before).</summary>
    public Func<ScriptSettings> Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The last project read a session started (to wait on in a test), or null.</summary>
    internal Task? LastRead { get; private set; }

    /// <inheritdoc cref="ILanguage.Open(ITextBuffer)" />
    public new ILanguageSession Open(ITextBuffer buffer) => Open(buffer, LanguageOpenContext.None);

    /// <inheritdoc cref="ILanguage.Open(ITextBuffer, LanguageOpenContext)" />
    public new ILanguageSession Open(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);
        var settings = Settings();
        var project = ScriptProjects.Find(context.FilePath);
        var tokenizer = TokenizerFor(context.FilePath, project, settings);
        var formatter = new ScriptFormatter(tokenizer, () => Settings().FormatLineWidth);
        var session = new LineStateSession(buffer, tokenizer, formatter, context.Settings);
        if (project is null && context.FilePath is { } path && NeedsProject(path, settings))
        {
            LastRead = Learn(session, formatter, path, context.WorkspaceRoot);
        }

        return session;
    }

    /// <summary>The lexer a file opens with: its dialect and version from the path, its project (when read) and the settings.</summary>
    public ScriptTokenizer TokenizerFor(string? path, ScriptProject? project, ScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ScriptTokenizer.For(DialectFor(path, project, settings), VersionFor(project, settings), settings.StateCapacity, settings.StateDepth);
    }

    /// <summary>The dialect of a file: .tsx TSX, other TypeScript plain; .jsx JSX, .js by the <c>jsx</c> setting (auto: the project uses React or Preact).</summary>
    public ScriptDialect DialectFor(string? path, ScriptProject? project, ScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var extension = Path.GetExtension(path.AsSpan());
        if (IsTypeScript)
        {
            return extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ? ScriptDialect.Tsx : ScriptDialect.TypeScript;
        }

        if (extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptDialect.Jsx;
        }

        return settings.Jsx switch
        {
            "on" => ScriptDialect.Jsx,
            "off" => ScriptDialect.JavaScript,
            _ => project is { UsesJsx: true } ? ScriptDialect.Jsx : ScriptDialect.JavaScript,
        };
    }

    /// <summary>The version read: the setting's when it names one, else the project's when <c>projectVersion</c> is on and it says, else the latest.</summary>
    public ScriptVersion VersionFor(ScriptProject? project, ScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool latest = settings.Version.Equals("latest", StringComparison.OrdinalIgnoreCase);
        if (!latest)
        {
            int? set = IsTypeScript ? ScriptVersion.ParseTypeScript(settings.Version) : ScriptVersion.ParseEcmaScript(settings.Version);
            if (set is { } level)
            {
                return IsTypeScript ? ScriptVersion.Newest with { TypeScript = level } : ScriptVersion.Newest with { EcmaScript = level };
            }
        }

        if (settings.ProjectVersion && project is not null)
        {
            if (IsTypeScript && project.TypeScript is { } typeScript)
            {
                return ScriptVersion.Newest with { TypeScript = typeScript };
            }

            if (!IsTypeScript && project.EcmaScript is { } ecmaScript)
            {
                return ScriptVersion.Newest with { EcmaScript = ecmaScript };
            }
        }

        return ScriptVersion.Newest;
    }

    /// <inheritdoc />
    protected override LineTokenizer TokenizerFor(LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TokenizerFor(context.FilePath, ScriptProjects.Find(context.FilePath), Settings());
    }

    // Whether the project can change the file's lexer: a version from it, or JSX in a .js file.
    private bool NeedsProject(string path, ScriptSettings settings) =>
        (settings.ProjectVersion && settings.Version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        || (!IsTypeScript && settings.Jsx == "auto" && Path.GetExtension(path.AsSpan()).Equals(".js", StringComparison.OrdinalIgnoreCase));

    // Reads the file's project off the UI thread, then gives the session (and its formatter) the lexer it names.
    private async Task Learn(LineStateSession session, ScriptFormatter formatter, string path, string? workspaceRoot)
    {
        try
        {
            var project = await ScriptProjects.ReadAsync(path, workspaceRoot).ConfigureAwait(false);
            var tokenizer = TokenizerFor(path, project, Settings());
            if (!ReferenceEquals(tokenizer, session.Tokenizer))
            {
                formatter.Tokenizer = tokenizer;
                session.ChangeDialect(tokenizer);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The project could not be read: the file keeps the version it opened with.
        }
    }
}

/// <summary>
/// A script plug-in's language selector (W-script): it picks no language (the names do) but reads the workspace's project
/// (its version, React or Preact) off the UI thread when a folder opens, so the files opened after start in their project's
/// dialect without a switch.
/// </summary>
/// <param name="name">Its name in <see cref="LanguageSelectors"/>.</param>
public sealed class ScriptProjectSelector(string name) : ILanguageSelector
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string Summary => "Reads a script project's version (tsconfig.json, jsconfig.json, node_modules/typescript) and its React or Preact when a folder opens";

    /// <inheritdoc />
    public ILanguage? Select(string path, string? workspaceRoot) => null;

    /// <inheritdoc />
    public Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken) => ScriptProjects.PrepareAsync(workspaceRoot, cancellationToken);
}

/// <summary>
/// A script plug-in's editor part (W-script): it declares the plug-in's settings and, once active in a host, gives the
/// language the host's values (<see cref="IEditorHost.Settings"/>); deactivated, the language reads its defaults again.
/// </summary>
public sealed class ScriptEditorPlugin : IEditorPlugin
{
    private readonly ScriptLanguage _language;

    /// <summary>The editor part of the plug-in <paramref name="name"/> for <paramref name="language"/>, declaring <paramref name="settings"/>.</summary>
    public ScriptEditorPlugin(string name, string summary, ScriptLanguage language, IReadOnlyList<PluginSetting> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(settings);
        Name = name;
        Summary = summary;
        _language = language;
        Settings = settings;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; }

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        string plugin = Name;
        var declared = Settings;
        Func<ScriptSettings> read = () => ScriptSettings.Read(key => declared.Any(s => s.Key == key) ? host.Settings.Get(plugin, key) : null);
        _language.Settings = read;
        return new Deactivation(_language, read);
    }

    private sealed class Deactivation(ScriptLanguage language, Func<ScriptSettings> read) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(language.Settings, read))
            {
                language.Settings = static () => new ScriptSettings();
            }
        }
    }
}
