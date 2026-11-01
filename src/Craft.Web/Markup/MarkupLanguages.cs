// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Web.Script;

namespace Craft.Web.Markup;

/// <summary>
/// The HTML plug-in's settings (<c>CRAFT_PLUGIN_HTML_*</c>, Settings › Editor › HTML): <c>embedded</c> (colour and format
/// <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> with their languages) and <c>attributesAsCode</c> (<c>style="…"</c> as CSS,
/// <c>on*="…"</c> as JavaScript).
/// </summary>
public sealed record HtmlSettings
{
    /// <summary>Whether <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> read as their languages.</summary>
    public bool Embedded { get; init; } = true;

    /// <summary>Whether <c>style="…"</c> reads as CSS and <c>on*="…"</c> as JavaScript.</summary>
    public bool AttributesAsCode { get; init; } = true;

    /// <summary>Whether the built-in completion offers elements, attributes, values, end tags and entities (<c>completion</c>).</summary>
    public bool Completion { get; init; } = true;

    /// <summary>The settings the HTML plug-in declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("embedded", "true", "Colour and format <script> as JavaScript and <style> as CSS") { Shape = SettingShape.Toggle(SettingSections.Editor, "Embedded scripts and styles", "HTML") },
        new("attributesAsCode", "true", "Colour style=\"…\" as CSS and on*=\"…\" as JavaScript") { Shape = SettingShape.Toggle(SettingSections.Editor, "Attributes as code", "HTML") },
        new("completion", "true", "Complete elements, attributes, values, end tags and entities (built in, no Node), and CSS in <style> and style=\"…\"") { Shape = SettingShape.Toggle(SettingSections.Editor, "Completion", "HTML") },
    ];

    /// <summary>The settings <paramref name="get"/> gives by key (null: the defaults).</summary>
    public static HtmlSettings Read(Func<string, string?>? get)
    {
        var defaults = new HtmlSettings();
        if (get is null)
        {
            return defaults;
        }

        return new HtmlSettings
        {
            Embedded = MarkupSettings.Toggle(get("embedded"), defaults.Embedded),
            AttributesAsCode = MarkupSettings.Toggle(get("attributesAsCode"), defaults.AttributesAsCode),
            Completion = MarkupSettings.Toggle(get("completion"), defaults.Completion),
        };
    }
}

/// <summary>
/// The Angular plug-in's settings (<c>CRAFT_PLUGIN_ANGULAR_*</c>, Settings › Editor › Angular): <c>templates</c> (project: an
/// angular.json project's .html and every *.component.html; suffix: *.component.html only; off: none), <c>inlineTemplates</c>
/// (an Angular project's .ts reads its <c>template:</c> and <c>styles:</c>), <c>version</c> (latest, or "17.3") and
/// <c>projectVersion</c> (read it from <c>@angular/core</c> when the version is latest).
/// </summary>
public sealed record AngularSettings
{
    /// <summary>Which .html files are Angular templates: project, suffix, off.</summary>
    public string Templates { get; init; } = "project";

    /// <summary>Whether an Angular project's .ts reads its inline templates and styles.</summary>
    public bool InlineTemplates { get; init; } = true;

    /// <summary>The Angular version read ("latest", or "17.3").</summary>
    public string Version { get; init; } = "latest";

    /// <summary>Whether the project's <c>@angular/core</c> names the version when <see cref="Version"/> is latest.</summary>
    public bool ProjectVersion { get; init; } = true;

    /// <summary>Whether a template completes (HTML's names, the blocks after '@', bindings after '[' and '(') (<c>completion</c>).</summary>
    public bool Completion { get; init; } = true;

    /// <summary>The settings the Angular plug-in declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("templates", "project", "Which .html files are Angular templates: project (an angular.json project's, and *.component.html), suffix (*.component.html), off") { Shape = SettingShape.Choice(SettingSections.Editor, "Angular templates", ["project", "suffix", "off"], "Angular") },
        new("inlineTemplates", "true", "An Angular project's .ts reads its template: as an Angular template and styles: as its style language") { Shape = SettingShape.Toggle(SettingSections.Editor, "Inline templates and styles", "Angular") },
        new("version", "latest", "The Angular version read: latest, or 16.2, 17.3, ...; before 17 @if and @for are text, before 18.1 @let") { Shape = SettingShape.Text(SettingSections.Editor, "Angular version", "Angular") },
        new("projectVersion", "true", "When the version is latest, read it from the project (@angular/core)") { Shape = SettingShape.Toggle(SettingSections.Editor, "Version from the project", "Angular") },
        new("completion", "true", "Complete a template's elements, attributes, the blocks after @ and the bindings after [ and ( (built in, no Node)") { Shape = SettingShape.Toggle(SettingSections.Editor, "Completion", "Angular") },
    ];

    /// <summary>The settings <paramref name="get"/> gives by key (null: the defaults).</summary>
    public static AngularSettings Read(Func<string, string?>? get)
    {
        var defaults = new AngularSettings();
        if (get is null)
        {
            return defaults;
        }

        string templates = (get("templates") ?? defaults.Templates).Trim().ToLowerInvariant();
        return new AngularSettings
        {
            Templates = templates is "project" or "suffix" or "off" ? templates : defaults.Templates,
            InlineTemplates = MarkupSettings.Toggle(get("inlineTemplates"), defaults.InlineTemplates),
            Version = get("version") is { Length: > 0 } version ? version.Trim() : defaults.Version,
            ProjectVersion = MarkupSettings.Toggle(get("projectVersion"), defaults.ProjectVersion),
            Completion = MarkupSettings.Toggle(get("completion"), defaults.Completion),
        };
    }

    /// <summary>The Angular version read for <paramref name="workspace"/>: the setting's, else the project's when <see cref="ProjectVersion"/>, else the latest.</summary>
    public int VersionFor(AngularWorkspace? workspace)
    {
        if (!Version.Equals("latest", StringComparison.OrdinalIgnoreCase) && ScriptVersion.ParseTypeScript(Version) is { } set)
        {
            return set;
        }

        return ProjectVersion && workspace?.Version is { } project ? project : ScriptVersion.Latest;
    }

    // Whether a file's version may come from its project (a read off the UI thread then).
    internal bool WantsProject => ProjectVersion && Version.Equals("latest", StringComparison.OrdinalIgnoreCase);
}

internal static class MarkupSettings
{
    public static bool Toggle(string? text, bool fallback) =>
        bool.TryParse(text?.Trim(), out bool value) ? value : text?.Trim() switch { "1" or "on" or "yes" => true, "0" or "off" or "no" => false, _ => fallback };
}

/// <summary>
/// The "html" language (W-markup): .html and .htm on <see cref="HtmlTokenizer"/>, its settings choosing the embedded
/// languages; Format document and Format Selection through <see cref="HtmlFormatter"/>.
/// </summary>
public sealed class HtmlLanguage : LineStateLanguage
{
    private Func<HtmlSettings> _settings = static () => new HtmlSettings();

    /// <summary>The language named <paramref name="name"/> opening <paramref name="extensions"/>.</summary>
    public HtmlLanguage(string name = "html", IReadOnlyList<string>? extensions = null)
        : base(name, "HTML (living standard): colours of tags, attributes and entities, <script> as JavaScript, <style> as CSS; brackets, folding and format",
            extensions ?? [".html", ".htm"], HtmlTokenizer.HtmlRules, HtmlTokenizer.For(HtmlOptions.Html), new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.Html)))
    {
    }

    /// <summary>Where its settings come from now (the plug-in's host once active; the defaults before).</summary>
    public Func<HtmlSettings> Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The lexer of <paramref name="settings"/>.</summary>
    public static HtmlTokenizer TokenizerOf(HtmlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return HtmlTokenizer.For(HtmlOptions.Html with { Embedded = settings.Embedded, AttributesAsCode = settings.AttributesAsCode });
    }

    /// <inheritdoc />
    protected override LineTokenizer TokenizerFor(LanguageOpenContext context) => TokenizerOf(Settings());

    /// <inheritdoc />
    protected override LineStateSession OpenSession(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = Settings();
        var tokenizer = TokenizerOf(settings);
        return new LineStateSession(buffer, tokenizer, new HtmlFormatter(tokenizer), context.Settings, settings.Completion ? HtmlCompletion.Instance : null);
    }
}

/// <summary>
/// The "angular-html" language (W-markup): an Angular template (an Angular project's .html by the selector, and every
/// *.component.html by its name), in the version the setting or the project's <c>@angular/core</c> names; a file whose
/// project was not read opens at the latest and changes when it is (<see cref="ILanguageSession.Reclassified"/>).
/// </summary>
public sealed class AngularHtmlLanguage : LineStateLanguage, ILanguage
{
    private Func<AngularSettings> _settings = static () => new AngularSettings();

    /// <summary>The language named <paramref name="name"/>.</summary>
    public AngularHtmlLanguage(string name = "angular-html")
        : base(name, "Angular templates: bindings, events, {{ }}, @if, @for, @switch, @defer, @let, pipes; colours, folding and format",
            [], HtmlTokenizer.HtmlRules, HtmlTokenizer.For(HtmlOptions.AngularTemplate), new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.AngularTemplate)))
    {
    }

    /// <summary>*.component.html, by name.</summary>
    public IReadOnlyList<string> FilePatterns { get; } = ["*.component.html"];

    /// <summary>Where its settings come from now.</summary>
    public Func<AngularSettings> Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The last project read a session started (to wait on in a test), or null.</summary>
    internal Task? LastRead { get; private set; }

    /// <summary>The template lexer of Angular <paramref name="version"/>.</summary>
    public static HtmlTokenizer TokenizerOf(int version) => HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = version });

    /// <inheritdoc />
    protected override LineTokenizer TokenizerFor(LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TokenizerOf(Settings().VersionFor(WorkspaceOf(context)));
    }

    /// <inheritdoc />
    protected override LineStateSession OpenSession(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = Settings();
        var workspace = WorkspaceOf(context);
        var tokenizer = TokenizerOf(settings.VersionFor(workspace));
        var session = new LineStateSession(buffer, tokenizer, new HtmlFormatter(tokenizer), context.Settings, settings.Completion ? HtmlCompletion.Instance : null);
        if (workspace is null && settings.WantsProject && FolderOf(context) is { } folder)
        {
            LastRead = Learn(session, folder);
        }

        return session;
    }

    internal static AngularWorkspace? WorkspaceOf(LanguageOpenContext context) => FolderOf(context) is { } folder ? AngularWorkspaces.Find(folder) : null;

    internal static string? FolderOf(LanguageOpenContext context) =>
        context.WorkspaceRoot ?? (context.FilePath is { } path ? Path.GetDirectoryName(Path.GetFullPath(path)) : null);

    // Reads the folder's Angular off the UI thread, then gives the session the version it names (a template formats the same).
    private async Task Learn(LineStateSession session, string folder)
    {
        try
        {
            var workspace = await AngularWorkspaces.ReadAsync(folder).ConfigureAwait(false);
            session.ChangeDialect(TokenizerOf(Settings().VersionFor(workspace)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The project could not be read: the file keeps the version it opened with.
        }
    }
}

/// <summary>
/// The "angular-typescript" language (W-markup): an Angular project's TypeScript, its components' <c>template:</c> read as
/// Angular templates and <c>styles:</c> in the project's inline style language (<see cref="AngularTypeScriptTokenizer"/>);
/// chosen by the Angular plug-in's selector only, so removing the plug-in gives the files back to "typescript". Formatted
/// as TypeScript (an inline template's text kept as written).
/// </summary>
public sealed class AngularTypeScriptLanguage : LineStateLanguage
{
    private Func<AngularSettings> _settings = static () => new AngularSettings();

    /// <summary>The language named <paramref name="name"/>.</summary>
    public AngularTypeScriptLanguage(string name = "angular-typescript")
        : base(name, "TypeScript of an Angular project, its inline templates and styles coloured as Angular and CSS; folding and format as TypeScript",
            [], ScriptLanguage.ScriptRules, AngularTypeScriptTokenizer.For(ScriptVersion.Latest), new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest)))
    {
    }

    /// <summary>Where its settings come from now.</summary>
    public Func<AngularSettings> Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <inheritdoc />
    protected override LineTokenizer TokenizerFor(LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var workspace = AngularHtmlLanguage.WorkspaceOf(context);
        var project = context.FilePath is { } path ? workspace?.ProjectOf(path) : null;
        return AngularTypeScriptTokenizer.For(Settings().VersionFor(workspace), project?.InlineStyles ?? Style.StyleDialect.Css);
    }
}

/// <summary>
/// The Angular plug-in's language selector (W-markup): in a folder whose angular.json names projects, an .html under a
/// project's root is "angular-html" (templates = project) and a .ts (not .d.ts) is "angular-typescript"
/// (inlineTemplates); from its cache only, the folder read off the UI thread when it opens and again when the host says the
/// workspace changed. With templates off, a *.component.html is given to "html" when that language is there.
/// </summary>
public sealed class AngularSelector : ILanguageSelector
{
    private readonly Func<AngularSettings> _settings;
    private readonly ILanguage _templates;
    private readonly ILanguage _typeScript;

    /// <summary>The selector named <paramref name="name"/> choosing <paramref name="templates"/> and <paramref name="typeScript"/> by <paramref name="settings"/>.</summary>
    public AngularSelector(string name, Func<AngularSettings> settings, ILanguage templates, ILanguage typeScript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(typeScript);
        Name = name;
        _settings = settings;
        _templates = templates;
        _typeScript = typeScript;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary => "Angular: an angular.json project's .html as Angular templates and its .ts with inline templates and styles";

    /// <inheritdoc />
    public ILanguage? Select(string path, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        var settings = _settings();
        var extension = Path.GetExtension(path.AsSpan());
        bool html = extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        if (settings.Templates == "off" && html && path.EndsWith(".component.html", StringComparison.OrdinalIgnoreCase))
        {
            return EditorLanguages.Find("html");
        }

        if (workspaceRoot is null || AngularWorkspaces.Find(workspaceRoot) is not { Projects.Count: > 0 } workspace || workspace.ProjectOf(path) is null)
        {
            return null;
        }

        if (html && settings.Templates == "project")
        {
            return _templates;
        }

        bool typeScript = extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);
        return typeScript && settings.InlineTemplates ? _typeScript : null;
    }

    /// <inheritdoc />
    public Task PrepareAsync(string workspaceRoot, CancellationToken cancellationToken) => AngularWorkspaces.ReadAsync(workspaceRoot, cancellationToken);
}

/// <summary>
/// A markup plug-in's editor part (W-markup): it declares the plug-in's settings and, once active in a host, gives them to
/// its languages through <c>apply</c> (null when deactivated: the defaults again); <c>attach</c> may follow the host's
/// events (the Angular plug-in reads angular.json again when the workspace changes).
/// </summary>
public sealed class MarkupEditorPlugin : IEditorPlugin
{
    private readonly Action<Func<string, string?>?> _apply;
    private readonly Func<IEditorHost, IDisposable?>? _attach;

    /// <summary>The editor part of the plug-in <paramref name="name"/>.</summary>
    public MarkupEditorPlugin(string name, string summary, IReadOnlyList<PluginSetting> settings, Action<Func<string, string?>?> apply, Func<IEditorHost, IDisposable?>? attach = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(apply);
        Name = name;
        Summary = summary;
        Settings = settings;
        _apply = apply;
        _attach = attach;
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
        _apply(key => declared.Any(s => s.Key == key) ? host.Settings.Get(plugin, key) : null);
        var attached = _attach?.Invoke(host);
        return new Deactivation(this, attached);
    }

    /// <summary>Follows <paramref name="host"/>'s <see cref="IEditorHost.WorkspaceChanged"/>: the folders it names are read again (angular.json, @angular/core), off the UI thread.</summary>
    public static IDisposable RereadAngularOnWorkspaceChanged(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        EventHandler<WorkspaceChangedEventArgs> handler = (_, e) =>
        {
            if (e.Previous is { } previous)
            {
                AngularWorkspaces.Forget(previous);
            }

            if (e.Current is { } current)
            {
                AngularWorkspaces.Forget(current);
                _ = AngularWorkspaces.ReadAsync(current);
            }
        };
        host.WorkspaceChanged += handler;
        return new Unsubscribe(host, handler);
    }

    private sealed class Deactivation(MarkupEditorPlugin plugin, IDisposable? attached) : IDisposable
    {
        public void Dispose()
        {
            plugin._apply(null);
            attached?.Dispose();
        }
    }

    private sealed class Unsubscribe(IEditorHost host, EventHandler<WorkspaceChangedEventArgs> handler) : IDisposable
    {
        public void Dispose() => host.WorkspaceChanged -= handler;
    }
}
