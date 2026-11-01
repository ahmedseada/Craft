// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Web.Style;

namespace Craft.Plugins.Css;

/// <summary>The CSS plug-in's languages ("css", "scss", "less"), their rules and the icons of their files.</summary>
public static class StyleLanguages
{
    /// <summary>CSS's name in <see cref="EditorLanguages"/>.</summary>
    public const string Css = "css";

    /// <summary>SCSS's name (.scss, and .sass as SCSS-like).</summary>
    public const string Scss = "scss";

    /// <summary>LESS's name.</summary>
    public const string Less = "less";

    private static readonly IReadOnlyList<BracketPair> Brackets = [new('{', '}'), new('(', ')'), new('[', ']')];

    /// <summary>CSS's rules: <c>/* */</c> comments only (toggle comment wraps a line in them), <c>{}</c> <c>()</c> <c>[]</c>, strings in either quote.</summary>
    public static LanguageRules CssRules { get; } = new(null, "/*", "*/", Brackets) { StringDelimiters = "\"'" };

    /// <summary>SCSS's and LESS's rules: <c>//</c> and <c>/* */</c> comments, the same brackets and strings.</summary>
    public static LanguageRules ScriptedRules { get; } = new("//", "/*", "*/", Brackets) { StringDelimiters = "\"'" };

    /// <summary>The languages, made once (they hold nothing per document: their sessions do).</summary>
    public static IReadOnlyList<StyleLanguage> All { get; } =
    [
        new(Css, "CSS (the latest syntax: nesting, @layer, @container, @scope, :has(), color-mix(), oklch(), anchor positioning): colours, folding and format",
            [".css"], CssRules, StyleDialect.Css),
        new(Scss, "SCSS ($variables, #{} interpolation, @use, @mixin, @include, control rules, maps) and .sass as SCSS-like: colours, folding and format (.scss)",
            [".scss", ".sass"], ScriptedRules, StyleDialect.Scss),
        new(Less, "LESS (@variables, mixins and guards, @{} interpolation, ~\"escapes\"): colours, folding and format",
            [".less"], ScriptedRules, StyleDialect.Less),
    ];

    /// <summary>The icons of their files, registered over the library's.</summary>
    public static IReadOnlyList<FileIcon> Icons { get; } =
    [
        new(".css", "#", 0xFF42A5F5, "CSS"),
        new(".scss", "S", 0xFFE06C9F, "SCSS"),
        new(".sass", "S", 0xFFE06C9F, "Sass (indented)"),
        new(".less", "L", 0xFF6D8FD8, "LESS"),
    ];

    /// <summary>The dialect of a file of the language <paramref name="language"/> (.sass: Sass's indented syntax).</summary>
    public static StyleDialect DialectOf(string? language, string? filePath)
    {
        if (filePath is not null && Path.GetExtension(filePath.AsSpan()).Equals(".sass", StringComparison.OrdinalIgnoreCase))
        {
            return StyleDialect.Sass;
        }

        return language switch
        {
            _ when string.Equals(language, Scss, StringComparison.OrdinalIgnoreCase) => StyleDialect.Scss,
            _ when string.Equals(language, Less, StringComparison.OrdinalIgnoreCase) => StyleDialect.Less,
            _ => StyleDialect.Css,
        };
    }
}

/// <summary>
/// A style language (CSS, SCSS, LESS) on the public line-state kit: its sessions lex with the dialect of their file and
/// the version the settings name (or, when that is "latest", the Baseline year of the project's browserslist, read off
/// the opening thread once per folder and applied when it lands, through <see cref="LineStateSession.ChangeDialect"/>),
/// and format with the built-in formatter while the <c>format</c> setting is on (never .sass).
/// </summary>
public sealed class StyleLanguage : LineStateLanguage
{
    private const int MaxFolders = 1024;
    private static readonly ConcurrentDictionary<string, int> Folders = new(StringComparer.OrdinalIgnoreCase);

    internal StyleLanguage(string name, string summary, IReadOnlyList<string> extensions, LanguageRules rules, StyleDialect dialect)
        : base(name, summary, extensions, rules, StyleTokenizer.For(dialect), new StyleFormatter(dialect))
    {
        Dialect = dialect;
    }

    /// <summary>Its dialect.</summary>
    public StyleDialect Dialect { get; }

    /// <summary>The project folders whose browserslist was read so far (bounded: cleared past 1,024).</summary>
    public static int FoldersKnown => Folders.Count;

    /// <summary>Forgets the project versions read (a browserslist changed, a test).</summary>
    public static void ForgetProjectVersions() => Folders.Clear();

    /// <inheritdoc />
    protected override LineTokenizer TokenizerFor(LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return StyleTokenizer.For(StyleLanguages.DialectOf(Name, context.FilePath), CssSettings.Current.Year ?? StyleNames.Latest);
    }

    /// <inheritdoc />
    protected override LineStateSession OpenSession(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = CssSettings.Current;
        var dialect = StyleLanguages.DialectOf(Name, context.FilePath);
        var formatter = settings.Format && dialect != StyleDialect.Sass ? Formatter : null;
        int year = settings.Year ?? StyleNames.Latest;
        string? folder = settings.Year is null && settings.ProjectVersion && context.FilePath is { Length: > 0 } path ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;
        bool learn = false;
        if (folder is not null)
        {
            if (Folders.TryGetValue(folder, out int known))
            {
                year = known;
            }
            else
            {
                learn = true;
            }
        }

        var session = new LineStateSession(buffer, StyleTokenizer.For(dialect, year), formatter, context.Settings, settings.Completion ? StyleCompletion.Instance : null);
        if (learn)
        {
            string workspace = context.WorkspaceRoot ?? "";
            _ = Task.Run(() => Learn(session, dialect, folder!, workspace, year));
        }

        return session;
    }

    // Off the opening thread: the folder's project version, kept, and the session's dialect changed when it differs.
    private static void Learn(LineStateSession session, StyleDialect dialect, string folder, string workspaceRoot, int opened)
    {
        int year;
        try
        {
            year = StyleProjectVersion.Read(folder, workspaceRoot.Length == 0 ? null : workspaceRoot) ?? StyleNames.Latest;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            year = StyleNames.Latest;
        }

        if (Folders.Count >= MaxFolders)
        {
            Folders.Clear();
        }

        Folders[folder] = year;
        if (year != opened)
        {
            session.ChangeDialect(StyleTokenizer.For(dialect, year));
        }
    }
}
