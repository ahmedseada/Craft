// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Craft.Abstractions;

/// <summary>
/// What a file's own configuration says about it (X part 9: <c>.editorconfig</c>): each value null where it says nothing,
/// so the editor's options stand there. Where it says something it wins over the editor's options for that file.
/// </summary>
public sealed record FileSettings
{
    /// <summary>Settings that say nothing.</summary>
    public static FileSettings None { get; } = new();

    /// <summary>The columns a tab spans and an indent level takes (<c>indent_size</c>, <c>tab_width</c>), or null.</summary>
    public int? TabSize { get; init; }

    /// <summary>Whether Tab and the auto-indent insert spaces (<c>indent_style = space</c>) or a tab (<c>tab</c>), or null.</summary>
    public bool? InsertSpaces { get; init; }

    /// <summary>The line break the editor inserts ("\n", "\r\n" or "\r"; <c>end_of_line</c>), or null.</summary>
    public string? NewLine { get; init; }

    /// <summary>The encoding a new file is saved in: "utf-8", "utf-8-bom", "latin1", "utf-16le" or "utf-16be" (<c>charset</c>), or null.</summary>
    public string? Charset { get; init; }

    /// <summary>Whether Save removes the white space at the ends of the lines first (<c>trim_trailing_whitespace</c>), or null.</summary>
    public bool? TrimTrailingWhitespace { get; init; }

    /// <summary>Whether Save ends the text with a line break when it has none (<c>insert_final_newline</c>), or null.</summary>
    public bool? InsertFinalNewline { get; init; }

    /// <summary>The column a vertical ruler marks (<c>max_line_length</c>), or null.</summary>
    public int? MaxLineLength { get; init; }

    /// <summary>The configuration files that said something, nearest last (shown in the status bar's tooltips).</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>Whether it says nothing.</summary>
    public bool IsEmpty => TabSize is null && InsertSpaces is null && NewLine is null && Charset is null && TrimTrailingWhitespace is null
        && InsertFinalNewline is null && MaxLineLength is null;

    /// <summary>These settings where they say something, <paramref name="under"/>'s elsewhere.</summary>
    public FileSettings Over(FileSettings under)
    {
        ArgumentNullException.ThrowIfNull(under);
        return new FileSettings
        {
            TabSize = TabSize ?? under.TabSize,
            InsertSpaces = InsertSpaces ?? under.InsertSpaces,
            NewLine = NewLine ?? under.NewLine,
            Charset = Charset ?? under.Charset,
            TrimTrailingWhitespace = TrimTrailingWhitespace ?? under.TrimTrailingWhitespace,
            InsertFinalNewline = InsertFinalNewline ?? under.InsertFinalNewline,
            MaxLineLength = MaxLineLength ?? under.MaxLineLength,
            Sources = [.. under.Sources, .. Sources],
        };
    }
}

/// <summary>
/// A source of per-file settings (X part 9), registered in <see cref="FileSettingsProviders"/>: the library's
/// <see cref="FileSettingsProviders.EditorConfig"/> reads <c>.editorconfig</c> files; a plug-in may add another. It reads
/// off the calling thread and keeps what it read, so the editor asks it again for free while nothing changed.
/// </summary>
public interface IFileSettingsProvider : IKind
{
    /// <summary>The settings of the file at <paramref name="path"/> (it need not exist yet), reading what it must off the calling thread.</summary>
    Task<FileSettings> ForFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The settings of <paramref name="path"/> when they are known without reading a file (its folders were read already), else null.</summary>
    FileSettings? Cached(string path);

    /// <summary>Whether <paramref name="path"/> is one of its configuration files (a change to it changes what it answers).</summary>
    bool IsConfigurationFile(string path);

    /// <summary>Forgets what it read (a configuration file changed): the next question reads again.</summary>
    void Invalidate();
}

/// <summary>
/// The per-file settings providers, by name (ignoring case): the library's <see cref="EditorConfig"/>; a plug-in may
/// register another. Where several say something of one file, the earlier registered wins. A name that is not registered
/// fails with "not registered".
/// </summary>
public static class FileSettingsProviders
{
    /// <summary>The library's <c>.editorconfig</c> reader.</summary>
    public const string EditorConfig = "editorconfig";

    private static readonly KindTable<IFileSettingsProvider> Table = BuiltIn();

    private static KindTable<IFileSettingsProvider> BuiltIn()
    {
        var table = new KindTable<IFileSettingsProvider>(nameof(FileSettingsProviders), "File settings provider", "add it with FileSettingsProviders.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "each provider answers for the files it knows; the first that says something of a setting wins");
        table.RegisterDefault(new EditorConfigProvider());
        return table;
    }

    /// <summary>Registers <paramref name="provider"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IFileSettingsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's provider of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IFileSettingsProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IFileSettingsProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IFileSettingsProvider Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The settings of <paramref name="path"/> from every provider <paramref name="use"/> takes (all when null), the earlier
    /// registered winning where two say something, read off the calling thread.
    /// </summary>
    public static async Task<FileSettings> ForFileAsync(string path, Func<IFileSettingsProvider, bool>? use = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = FileSettings.None;
        var providers = Table.Kinds;
        for (int i = providers.Count - 1; i >= 0; i--)
        {
            if (use is null || use(providers[i]))
            {
                result = (await providers[i].ForFileAsync(path, cancellationToken).ConfigureAwait(false)).Over(result);
            }
        }

        return result;
    }

    /// <summary>The settings of <paramref name="path"/> when every provider <paramref name="use"/> takes knows them without reading, else null.</summary>
    public static FileSettings? Cached(string path, Func<IFileSettingsProvider, bool>? use = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = FileSettings.None;
        var providers = Table.Kinds;
        for (int i = providers.Count - 1; i >= 0; i--)
        {
            if (use is null || use(providers[i]))
            {
                if (providers[i].Cached(path) is not { } settings)
                {
                    return null;
                }

                result = settings.Over(result);
            }
        }

        return result;
    }
}

/// <summary>
/// "editorconfig": the <c>.editorconfig</c> files from a file's folder up to the first that says <c>root = true</c>, their
/// sections' globs matched against the file's path (<c>*</c>, <c>**</c>, <c>?</c>, <c>[set]</c>, <c>[!set]</c>,
/// <c>{a,b}</c>, <c>{1..9}</c>; a glob without a '/' matches at any depth), the nearer file and the later section winning.
/// Each folder's file is read once off the calling thread and kept with its write time; a question about a file whose
/// folders are all kept is answered at once (<see cref="Cached"/>), and <see cref="ForFileAsync"/> reads again a file whose
/// write time changed.
/// </summary>
internal sealed class EditorConfigProvider : IFileSettingsProvider
{
    private const string FileName = ".editorconfig";
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Folder> _folders = new(StringComparer.Ordinal);

    public string Name => FileSettingsProviders.EditorConfig;

    public string Summary => ".editorconfig files: indentation, line endings, charset, trimming, the final newline, the line length";

    public bool IsConfigurationFile(string path) => string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);

    public void Invalidate()
    {
        lock (_gate)
        {
            _folders.Clear();
        }
    }

    public FileSettings? Cached(string path)
    {
        string full = Path.GetFullPath(path);
        var chain = new List<Folder>();
        lock (_gate)
        {
            for (string? folder = Path.GetDirectoryName(full); folder is not null; folder = Path.GetDirectoryName(folder))
            {
                if (!_folders.TryGetValue(folder, out var entry))
                {
                    return null;
                }

                chain.Add(entry);
                if (entry.File is { Root: true })
                {
                    break;
                }
            }
        }

        return Resolve(full, chain);
    }

    public Task<FileSettings> ForFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() =>
        {
            string full = Path.GetFullPath(path);
            var chain = new List<Folder>();
            for (string? folder = Path.GetDirectoryName(full); folder is not null; folder = Path.GetDirectoryName(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = Read(folder);
                chain.Add(entry);
                if (entry.File is { Root: true })
                {
                    break;
                }
            }

            return Resolve(full, chain);
        }, cancellationToken);
    }

    // The folder's file, read again when its write time is not the one kept.
    private Folder Read(string folder)
    {
        string file = Path.Combine(folder, FileName);
        DateTime stamp;
        try
        {
            stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stamp = DateTime.MinValue;
        }

        lock (_gate)
        {
            if (_folders.TryGetValue(folder, out var kept) && kept.Stamp == stamp)
            {
                return kept;
            }
        }

        EditorConfigFile? parsed = null;
        if (stamp != DateTime.MinValue)
        {
            try
            {
                parsed = EditorConfigFile.Parse(File.ReadAllText(file), folder, file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                parsed = null;
            }
        }

        var entry = new Folder(parsed, stamp);
        lock (_gate)
        {
            _folders[folder] = entry;
        }

        return entry;
    }

    // The nearest file last: walk the chain from the root-most down, sections in order, later values over earlier ones.
    private static FileSettings Resolve(string full, List<Folder> chain)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new List<string>();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].File is not { } file)
            {
                continue;
            }

            string relative = Path.GetRelativePath(file.Folder, full).Replace('\\', '/');
            bool said = false;
            foreach (var section in file.Sections)
            {
                if (!section.Matches(relative))
                {
                    continue;
                }

                foreach (var (key, value) in section.Pairs)
                {
                    if (value == "unset")
                    {
                        values.Remove(key);
                    }
                    else
                    {
                        values[key] = value;
                    }

                    said = true;
                }
            }

            if (said)
            {
                sources.Add(file.Path);
            }
        }

        return values.Count == 0 ? FileSettings.None with { Sources = sources } : Map(values, sources);
    }

    private static FileSettings Map(Dictionary<string, string> values, List<string> sources)
    {
        bool? spaces = values.GetValueOrDefault("indent_style") switch { "space" => true, "tab" => false, _ => null };
        int? tabWidth = Number(values.GetValueOrDefault("tab_width"), 1, 32);
        string? indent = values.GetValueOrDefault("indent_size");
        int? indentSize = indent == "tab" ? tabWidth : Number(indent, 1, 32);
        int? tabSize = spaces == false ? tabWidth ?? indentSize : indentSize ?? tabWidth;
        return new FileSettings
        {
            TabSize = tabSize,
            InsertSpaces = spaces,
            NewLine = values.GetValueOrDefault("end_of_line") switch { "lf" => "\n", "crlf" => "\r\n", "cr" => "\r", _ => null },
            Charset = values.GetValueOrDefault("charset") is "utf-8" or "utf-8-bom" or "latin1" or "utf-16le" or "utf-16be" ? values["charset"] : null,
            TrimTrailingWhitespace = Flag(values.GetValueOrDefault("trim_trailing_whitespace")),
            InsertFinalNewline = Flag(values.GetValueOrDefault("insert_final_newline")),
            MaxLineLength = Number(values.GetValueOrDefault("max_line_length"), 1, 10_000),
            Sources = sources,
        };
    }

    private static bool? Flag(string? value) => value switch { "true" => true, "false" => false, _ => null };

    private static int? Number(string? value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max ? number : null;

    private sealed record Folder(EditorConfigFile? File, DateTime Stamp);
}

/// <summary>One parsed <c>.editorconfig</c>: whether it is the root, and its sections in order.</summary>
internal sealed class EditorConfigFile
{
    private EditorConfigFile(string folder, string path, bool root, List<EditorConfigSection> sections)
    {
        Folder = folder;
        Path = path;
        Root = root;
        Sections = sections;
    }

    /// <summary>The folder it is in (its globs are relative to it).</summary>
    public string Folder { get; }

    /// <summary>Its own path.</summary>
    public string Path { get; }

    /// <summary>Whether it says <c>root = true</c> (no file above it is read).</summary>
    public bool Root { get; }

    /// <summary>Its sections, in order.</summary>
    public IReadOnlyList<EditorConfigSection> Sections { get; }

    /// <summary>
    /// Reads <paramref name="text"/>: comments from '#' or ';' at a line's start, <c>[glob]</c> sections, <c>key = value</c>
    /// pairs (keys and the known values in lower case); a pair before the first section is the preamble (<c>root</c>).
    /// </summary>
    public static EditorConfigFile Parse(string text, string folder, string path)
    {
        bool root = false;
        var sections = new List<EditorConfigSection>();
        EditorConfigSection? current = null;
        foreach (var raw in text.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.IsEmpty || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                int close = line.LastIndexOf(']');
                if (close > 1)
                {
                    current = new EditorConfigSection(line[1..close].ToString());
                    sections.Add(current);
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals].Trim().ToString().ToLowerInvariant();
            string value = line[(equals + 1)..].Trim().ToString();
            if (KnownValues.Contains(key))
            {
                value = value.ToLowerInvariant();
            }

            if (current is null)
            {
                root |= key == "root" && value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                current.Pairs.Add((key, value));
            }
        }

        return new EditorConfigFile(folder, path, root, sections);
    }

    private static readonly HashSet<string> KnownValues = new(StringComparer.Ordinal)
    {
        "indent_style", "indent_size", "tab_width", "end_of_line", "charset", "trim_trailing_whitespace", "insert_final_newline", "max_line_length", "root",
    };
}

/// <summary>A section of an <c>.editorconfig</c>: its glob, made once into a pattern, and its pairs in order.</summary>
internal sealed class EditorConfigSection
{
    private readonly Regex? _pattern;
    private readonly List<(int Min, int Max)> _ranges = [];

    public EditorConfigSection(string glob)
    {
        Glob = glob;
        try
        {
            _pattern = new Regex(ToPattern(glob, _ranges), RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            _pattern = null;                                        // a glob that makes no pattern matches nothing
        }
    }

    /// <summary>The section's glob as written.</summary>
    public string Glob { get; }

    /// <summary>Its pairs, in order.</summary>
    public List<(string Key, string Value)> Pairs { get; } = [];

    /// <summary>Whether the glob matches <paramref name="relative"/> (a path relative to the file's folder, with '/').</summary>
    public bool Matches(string relative)
    {
        if (_pattern is null)
        {
            return false;
        }

        try
        {
            var match = _pattern.Match(relative);
            if (!match.Success)
            {
                return false;
            }

            for (int i = 0; i < _ranges.Count; i++)
            {
                var group = match.Groups["n" + i.ToString(CultureInfo.InvariantCulture)];
                if (!group.Success || !int.TryParse(group.ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n)
                    || n < _ranges[i].Min || n > _ranges[i].Max)
                {
                    return false;
                }
            }

            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    // The glob as a regular expression: anchored to the folder when it holds a '/', else matched at any depth.
    internal static string ToPattern(string glob, List<(int Min, int Max)> ranges)
    {
        bool anchored = HasSlash(glob);
        if (glob.StartsWith('/'))
        {
            glob = glob[1..];
        }

        var pattern = new StringBuilder("^");
        if (!anchored)
        {
            pattern.Append("(?:.*/)?");
        }

        int at = 0;
        Convert(glob, ref at, pattern, ranges, inBraces: false);
        return pattern.Append('$').ToString();
    }

    private static bool HasSlash(string glob)
    {
        bool inSet = false;
        foreach (char c in glob)
        {
            if (c == '[')
            {
                inSet = true;
            }
            else if (c == ']')
            {
                inSet = false;
            }
            else if (c == '/' && !inSet)
            {
                return true;
            }
        }

        return false;
    }

    private static void Convert(string glob, ref int at, StringBuilder pattern, List<(int Min, int Max)> ranges, bool inBraces)
    {
        while (at < glob.Length)
        {
            char c = glob[at];
            if (inBraces && (c == ',' || c == '}'))
            {
                return;
            }

            at++;
            switch (c)
            {
                case '\\' when at < glob.Length:
                    pattern.Append(Regex.Escape(glob[at].ToString()));
                    at++;
                    break;
                case '*' when at < glob.Length && glob[at] == '*':
                    at++;
                    pattern.Append(".*");
                    break;
                case '*':
                    pattern.Append("[^/]*");
                    break;
                case '?':
                    pattern.Append("[^/]");
                    break;
                case '[':
                    int close = glob.IndexOf(']', at);
                    if (close < 0)
                    {
                        pattern.Append("\\[");
                        break;
                    }

                    var set = glob.AsSpan(at, close - at);
                    pattern.Append('[');
                    if (set.Length > 0 && set[0] == '!')
                    {
                        pattern.Append('^');
                        set = set[1..];
                    }

                    foreach (char s in set)
                    {
                        pattern.Append(s is '\\' or '^' or '[' or ']' ? "\\" + s : s.ToString());
                    }

                    pattern.Append(']');
                    at = close + 1;
                    break;
                case '{':
                    int end = glob.IndexOf('}', at);
                    if (end > at && TryRange(glob.AsSpan(at, end - at), out int min, out int max))
                    {
                        pattern.Append("(?<n").Append(ranges.Count.ToString(CultureInfo.InvariantCulture)).Append(">[+-]?[0-9]+)");
                        ranges.Add((min, max));
                        at = end + 1;
                        break;
                    }

                    pattern.Append("(?:");
                    while (true)
                    {
                        Convert(glob, ref at, pattern, ranges, inBraces: true);
                        if (at >= glob.Length)
                        {
                            break;
                        }

                        char separator = glob[at++];
                        if (separator == '}')
                        {
                            break;
                        }

                        pattern.Append('|');
                    }

                    pattern.Append(')');
                    break;
                default:
                    pattern.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
    }

    private static bool TryRange(ReadOnlySpan<char> text, out int min, out int max)
    {
        min = max = 0;
        int dots = text.IndexOf("..", StringComparison.Ordinal);
        return dots > 0
            && int.TryParse(text[..dots], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out min)
            && int.TryParse(text[(dots + 2)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out max);
    }
}
