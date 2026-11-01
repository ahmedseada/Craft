// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Craft.Abstractions;

/// <summary>What a workspace search looks for (the Search view's query box and its filters).</summary>
/// <param name="Text">The text or the regular expression (one line).</param>
/// <param name="MatchCase">Whether case must match.</param>
/// <param name="WholeWord">Whether a match must be a whole word.</param>
/// <param name="Regex">Whether <paramref name="Text"/> is a regular expression.</param>
public sealed record WorkspaceSearchQuery(string Text, bool MatchCase = false, bool WholeWord = false, bool Regex = false)
{
    /// <summary>The files to search, as globs separated by ',' (<c>*.cs, src/**</c>); empty: every file.</summary>
    public string Include { get; init; } = "";

    /// <summary>The files to leave out, as globs separated by ','; empty: none beyond the ignored folders.</summary>
    public string Exclude { get; init; } = "";

    /// <summary>Whether the workspace's <c>.gitignore</c> files leave files out (on by default).</summary>
    public bool UseIgnoreFiles { get; init; } = true;

    /// <summary>The matcher of this query (the find overlay's <see cref="TextPattern"/>).</summary>
    public TextPattern Pattern(TimeSpan regexTimeout, bool compiled = false) => new(Text, MatchCase, WholeWord, Regex, regexTimeout, compiled);
}

/// <summary>An open document searched in its current text instead of its file.</summary>
/// <param name="Path">Its file's full path.</param>
/// <param name="Text">Its text now (a snapshot: any thread may read it).</param>
public sealed record WorkspaceSearchDocument(string Path, ITextSnapshot Text);

/// <summary>A search over a workspace: where, what, and its limits.</summary>
/// <param name="Root">The workspace's folder (a full path).</param>
/// <param name="Query">What to look for.</param>
public sealed record WorkspaceSearchRequest(string Root, WorkspaceSearchQuery Query)
{
    /// <summary>Folder names never entered, ignoring case (<c>CRAFT_WORKSPACE_IGNORE</c>).</summary>
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [];

    /// <summary>The open documents: searched in their current text, their files on disk not read.</summary>
    public IReadOnlyList<WorkspaceSearchDocument> OpenDocuments { get; init; } = [];

    /// <summary>The most matches reported (<c>CRAFT_SEARCH_MAX_RESULTS</c>); the search stops there and says so.</summary>
    public int MaxResults { get; init; } = 20_000;

    /// <summary>Files larger than this are passed over and counted (<c>CRAFT_SEARCH_MAX_FILE_MB</c>).</summary>
    public long MaxFileBytes { get; init; } = 20L * 1024 * 1024;

    /// <summary>The files searched at once; 0 or less: one per processor (<c>CRAFT_SEARCH_THREADS</c>).</summary>
    public int Threads { get; init; }

    /// <summary>The longest a regular expression may take on one line (<c>CRAFT_FIND_REGEX_TIMEOUT_MS</c>).</summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The characters of a line a result shows around its match.</summary>
    public int PreviewCharacters { get; init; } = 240;

    /// <summary>
    /// Told how far the search is (the owner, 2026-11-01: the status bar's bar): the files searched, and the files to search
    /// once the folder's listing has ended (null before: no share is known yet); every few files and once at the end, on
    /// the searching threads. Null for none.
    /// </summary>
    public Action<WorkspaceSearchProgress>? Progress { get; init; }
}

/// <summary>How far a workspace search is: the files searched and, once the listing has ended, the files to search.</summary>
/// <param name="Searched">The files (and open documents) searched so far.</param>
/// <param name="Total">The files (and open documents) to search, or null while the folder is still being listed.</param>
public sealed record WorkspaceSearchProgress(int Searched, int? Total)
{
    /// <summary>The share searched, from 0 to 1, or null while the total is not known.</summary>
    public double? Fraction => Total is > 0 and int total ? Math.Min(1, (double)Searched / total) : null;
}

/// <summary>One match: its place (from 0) and the line it is on.</summary>
/// <param name="Line">Its line, from 0.</param>
/// <param name="Column">Its first character in the line, from 0.</param>
/// <param name="Length">Its characters.</param>
/// <param name="Preview">The line, or the part of a long line around the match.</param>
/// <param name="PreviewColumn">Where the match starts in <paramref name="Preview"/>.</param>
public sealed record WorkspaceSearchHit(int Line, int Column, int Length, string Preview, int PreviewColumn);

/// <summary>A file's matches, in order, as a provider reports them.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Hits">Its matches, in order.</param>
public sealed record WorkspaceSearchFile(string Path, IReadOnlyList<WorkspaceSearchHit> Hits)
{
    /// <summary>Whether the matches are in the open document's text (not its file's).</summary>
    public bool FromOpenDocument { get; init; }
}

/// <summary>How a search ended: what it searched, what it found, what it passed over and why.</summary>
/// <param name="FilesSearched">The files searched.</param>
/// <param name="FilesMatched">The files with a match.</param>
/// <param name="Matches">The matches reported.</param>
public sealed record WorkspaceSearchSummary(int FilesSearched, int FilesMatched, int Matches)
{
    /// <summary>Whether it stopped at <see cref="WorkspaceSearchRequest.MaxResults"/>.</summary>
    public bool Capped { get; init; }

    /// <summary>Files passed over for their size.</summary>
    public int SkippedLarge { get; init; }

    /// <summary>Files passed over as binary (a zero byte near their start).</summary>
    public int SkippedBinary { get; init; }

    /// <summary>Files that could not be read (in use, no access).</summary>
    public int Unreadable { get; init; }

    /// <summary>How long it took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>How long until the first file with a match was reported, or null when none was.</summary>
    public TimeSpan? FirstResult { get; init; }

    /// <summary>Why it could not search (an expression that does not parse, a missing folder), or null.</summary>
    public string? Problem { get; init; }

    /// <summary>Whether a regular expression ran past its time limit on some line (that line was taken as having no match).</summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// A way to search a workspace, registered in <see cref="WorkspaceSearchProviders"/>: the library's "text" (every file's
/// text, matched as the find overlay matches), and a plug-in's (P6's code search) beside it, so the one Search view shows
/// them all. A provider runs off the UI thread and reports each file's matches as it finds them, from any thread.
/// </summary>
public interface IWorkspaceSearchProvider : IKind
{
    /// <summary>Its title in the Search view ("Text").</summary>
    string Title { get; }

    /// <summary>
    /// Searches <paramref name="request"/>'s workspace, calling <paramref name="found"/> (on any thread, possibly several
    /// at once) for each file with matches as soon as it has them; the summary when it is done. Cancelling stops it soon
    /// (<see cref="OperationCanceledException"/>).
    /// </summary>
    Task<WorkspaceSearchSummary> SearchAsync(WorkspaceSearchRequest request, Action<WorkspaceSearchFile> found, CancellationToken cancellationToken = default);
}

/// <summary>
/// The workspace search providers by name (ignoring case): the library's <see cref="Text"/>; a plug-in registers its own
/// beside it (P6's code search) or over it. An unknown name fails with "not registered".
/// </summary>
public static class WorkspaceSearchProviders
{
    /// <summary>Every file's text, matched as the find overlay matches (the library's).</summary>
    public const string Text = "text";

    private static readonly KindTable<IWorkspaceSearchProvider> Table = BuiltIn();

    private static KindTable<IWorkspaceSearchProvider> BuiltIn()
    {
        var table = new KindTable<IWorkspaceSearchProvider>(nameof(WorkspaceSearchProviders), "Workspace search provider", "add it with WorkspaceSearchProviders.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a provider is picked by the user in the Search view, never swapped for another");
        table.RegisterDefault(new TextWorkspaceSearch());
        return table;
    }

    /// <summary>Registers <paramref name="provider"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IWorkspaceSearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's provider of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IWorkspaceSearchProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IWorkspaceSearchProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IWorkspaceSearchProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>
/// Globs over a workspace's relative paths ('/' between folders, case ignored), separated by ',': <c>*</c> any characters
/// but '/', <c>?</c> one, <c>**</c> any folders, <c>[abc]</c> and <c>{a,b}</c>. A glob without '/' matches a file or folder
/// name anywhere (<c>*.cs</c>, <c>obj</c>: everything in an obj folder); one with '/' is taken from the root
/// (<c>src/**/*.cs</c>, <c>src/Craft.Core</c>: everything under it). Immutable, any thread.
/// </summary>
public sealed class WorkspaceGlob
{
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, WorkspaceGlob?> Cache = new(StringComparer.Ordinal);
    private readonly System.Text.RegularExpressions.Regex _regex;

    private WorkspaceGlob(string text, System.Text.RegularExpressions.Regex regex)
    {
        Text = text;
        _regex = regex;
    }

    /// <summary>The globs as written.</summary>
    public string Text { get; }

    /// <summary>The globs of <paramref name="text"/>, or null when it holds none (made once per text and kept: the next keystroke's search reuses it).</summary>
    public static WorkspaceGlob? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        lock (CacheGate)
        {
            if (Cache.TryGetValue(text, out var kept))
            {
                return kept;
            }
        }

        var alternatives = new List<string>();
        foreach (var part in SplitOutsideBraces(text))
        {
            var glob = part.Trim().Replace('\\', '/');
            if (glob.StartsWith("./", StringComparison.Ordinal))
            {
                glob = glob[2..];
            }

            glob = glob.TrimEnd('/');
            if (glob.Length == 0)
            {
                continue;
            }

            bool anchored = glob.Contains('/', StringComparison.Ordinal);
            glob = glob.TrimStart('/');
            alternatives.Add((anchored ? "^" : "(?:^|/)") + ToRegex(glob) + "(?:/|$)");
        }

        var made = alternatives.Count == 0 ? null
            : new WorkspaceGlob(text, new System.Text.RegularExpressions.Regex(string.Join('|', alternatives),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.ExplicitCapture));
        lock (CacheGate)
        {
            if (Cache.Count >= 32)
            {
                Cache.Clear();
            }

            Cache[text] = made;
        }

        return made;
    }

    // The globs of a list: separated by the commas that are not inside braces ("*.{cs,md}, docs/**" is two).
    private static List<string> SplitOutsideBraces(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}' when depth > 0:
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Whether <paramref name="relativePath"/> (from the root, '/' between folders) matches one of the globs.</summary>
    public bool Matches(ReadOnlySpan<char> relativePath) => _regex.IsMatch(relativePath);

    /// <summary>A glob (no ',') as a regular expression's text, unanchored.</summary>
    internal static string ToRegex(string glob)
    {
        var text = new StringBuilder(glob.Length * 2);
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            switch (c)
            {
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/')
                    {
                        i++;
                        text.Append("(?:.*/)?");                 // **/ : any folders, or none
                    }
                    else
                    {
                        text.Append(".*");
                    }

                    break;
                case '*':
                    text.Append("[^/]*");
                    break;
                case '?':
                    text.Append("[^/]");
                    break;
                case '[':
                    int close = glob.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        text.Append("\\[");
                        break;
                    }

                    var set = glob.AsSpan(i + 1, close - i - 1);
                    text.Append('[');
                    if (set.Length > 0 && (set[0] == '!' || set[0] == '^'))
                    {
                        text.Append('^');
                        set = set[1..];
                    }

                    foreach (char member in set)
                    {
                        if (member is '\\' or '[' or ']')
                        {
                            text.Append('\\');
                        }

                        text.Append(member);
                    }

                    text.Append(']');
                    i = close;
                    break;
                case '{':
                    int end = glob.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        text.Append("\\{");
                        break;
                    }

                    text.Append("(?:");
                    bool first = true;
                    foreach (var part in glob[(i + 1)..end].Split(','))
                    {
                        text.Append(first ? "" : "|").Append(ToRegex(part));
                        first = false;
                    }

                    text.Append(')');
                    i = end;
                    break;
                default:
                    text.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                    break;
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// The rules of one <c>.gitignore</c> file, as git reads them: blank lines and '#' lines skipped, '!' takes a path back,
/// a trailing '/' matches folders only, a '/' at the start or in the middle ties the pattern to the file's own folder,
/// <c>*</c>, <c>?</c>, <c>[...]</c> and <c>**</c> as globs; the last rule that matches decides, and a deeper file's rules
/// come after its parents'. Immutable, any thread.
/// </summary>
public sealed class GitIgnore
{
    private readonly List<(System.Text.RegularExpressions.Regex Rule, bool Negate, bool FoldersOnly)> _rules;

    private GitIgnore(string folder, List<(System.Text.RegularExpressions.Regex, bool, bool)> rules, GitIgnore? parent)
    {
        Folder = folder;
        _rules = rules;
        Parent = parent;
    }

    /// <summary>The folder its file is in, relative to the workspace's root ('/' between folders; "" for the root).</summary>
    public string Folder { get; }

    /// <summary>The rules of the folders above it, or null.</summary>
    public GitIgnore? Parent { get; }

    /// <summary>The rules of <paramref name="text"/> (a <c>.gitignore</c> in <paramref name="folder"/>, relative to the root), after <paramref name="parent"/>'s; null when it holds none.</summary>
    public static GitIgnore? Parse(string text, string folder, GitIgnore? parent)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folder);
        var rules = new List<(System.Text.RegularExpressions.Regex, bool, bool)>();
        foreach (var range in text.AsSpan().Split('\n'))
        {
            var line = text.AsSpan()[range].TrimEnd('\r').TrimEnd(' ');
            if (line.IsEmpty || line[0] == '#')
            {
                continue;
            }

            bool negate = line[0] == '!';
            if (negate)
            {
                line = line[1..];
            }

            if (line.Length > 0 && line[0] == '\\')
            {
                line = line[1..];                                 // "\#name", "\!name": the character itself
            }

            bool foldersOnly = line.EndsWith('/');
            line = line.TrimEnd('/');
            if (line.IsEmpty)
            {
                continue;
            }

            bool anchored = line.IndexOf('/') >= 0;
            var glob = line.TrimStart('/').ToString();
            string pattern = (anchored ? "^" : "(?:^|/)") + WorkspaceGlob.ToRegex(glob) + "$";
            try
            {
                rules.Add((new System.Text.RegularExpressions.Regex(pattern,
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.ExplicitCapture
                    | (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None)),
                    negate, foldersOnly));
            }
            catch (ArgumentException)
            {
                // A line git would not read either: skipped.
            }
        }

        return rules.Count == 0 ? parent : new GitIgnore(folder.Trim('/'), rules, parent);
    }

    /// <summary>Whether <paramref name="relativePath"/> (from the workspace's root) is ignored by these rules and their parents'.</summary>
    public bool Ignores(ReadOnlySpan<char> relativePath, bool isFolder)
    {
        bool? decided = Decide(relativePath, isFolder);
        return decided == true;
    }

    // The last rule that matches, deepest file last; null when none matched.
    private bool? Decide(ReadOnlySpan<char> relativePath, bool isFolder)
    {
        bool? decided = Parent?.Decide(relativePath, isFolder);
        var local = relativePath;
        if (Folder.Length > 0)
        {
            if (!local.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) || local.Length <= Folder.Length || local[Folder.Length] != '/')
            {
                return decided;
            }

            local = local[(Folder.Length + 1)..];
        }

        for (int i = _rules.Count - 1; i >= 0; i--)
        {
            var (rule, negate, foldersOnly) = _rules[i];
            if ((!foldersOnly || isFolder) && rule.IsMatch(local))
            {
                return !negate;
            }
        }

        return decided;
    }
}

/// <summary>
/// The files of a workspace as a search walks them: folder by folder, the ignored folder names
/// (<c>CRAFT_WORKSPACE_IGNORE</c>) and links to folders never entered, each folder's <c>.gitignore</c> read as it is
/// reached (when asked), the include and exclude globs applied (an excluded folder is not entered). Paths are full;
/// nothing is listed ahead of the caller.
/// </summary>
public static class WorkspaceFiles
{
    /// <summary>The files of <paramref name="root"/> a search reads, as full paths.</summary>
    public static IEnumerable<string> Enumerate(string root, IReadOnlyList<string> ignoredFolders, bool useIgnoreFiles, WorkspaceGlob? include, WorkspaceGlob? exclude,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(ignoredFolders);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var ignored = new HashSet<string>(ignoredFolders, StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };
        var folders = new Stack<(string Path, GitIgnore? Rules)>();
        folders.Push((root, null));
        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folder, rules) = folders.Pop();
            if (useIgnoreFiles)
            {
                rules = ReadIgnoreFile(root, folder, rules);
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var relative = Relative(root, entry.FullName);
                if (entry is DirectoryInfo directory)
                {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || ignored.Contains(directory.Name)
                        || rules?.Ignores(relative, isFolder: true) == true || exclude?.Matches(relative) == true)
                    {
                        continue;
                    }

                    folders.Push((directory.FullName, rules));
                }
                else if (rules?.Ignores(relative, isFolder: false) != true && exclude?.Matches(relative) != true && include?.Matches(relative) != false)
                {
                    yield return entry.FullName;
                }
            }
        }
    }

    /// <summary><paramref name="path"/> relative to <paramref name="root"/> with '/' between folders ("src/Program.cs").</summary>
    public static string Relative(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        root = Path.TrimEndingDirectorySeparator(root);
        if (path.Length > root.Length + 1 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (path[root.Length] == '/' || path[root.Length] == '\\'))
        {
            var tail = path.AsSpan(root.Length + 1);
            return Path.DirectorySeparatorChar == '/' ? tail.ToString() : string.Create(tail.Length, path, (span, full) =>
            {
                full.AsSpan(full.Length - span.Length).CopyTo(span);
                span.Replace('\\', '/');
            });
        }

        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static GitIgnore? ReadIgnoreFile(string root, string folder, GitIgnore? rules)
    {
        var file = Path.Combine(folder, ".gitignore");
        if (!File.Exists(file))
        {
            return rules;
        }

        try
        {
            return GitIgnore.Parse(File.ReadAllText(file), folder.Length == root.Length ? "" : Relative(root, folder), rules);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return rules;
        }
    }
}

/// <summary>
/// The matching a text search does over one text (a file's or an open document's), shared by every provider that
/// searches text: plain text is found over the whole text at once (vectorized <c>IndexOf</c>), a regular expression one
/// line at a time on the line's span; lines are counted as it goes. Nothing is allocated for a line without a match.
/// </summary>
public static class WorkspaceTextScan
{
    /// <summary>
    /// Adds the matches of <paramref name="pattern"/> in <paramref name="text"/> to <paramref name="hits"/>, at most
    /// <paramref name="limit"/>; the number added. A match's preview is its line (the part around the match of a line
    /// longer than <paramref name="previewCharacters"/>), one string for the matches of a line.
    /// </summary>
    public static int Scan(ReadOnlySpan<char> text, TextPattern pattern, int previewCharacters, int limit, List<WorkspaceSearchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(hits);
        if (!pattern.Searchable || limit <= 0)
        {
            return 0;
        }

        int added = 0;
        int previewLine = -1;
        string? preview = null;
        int previewStart = 0;
        if (pattern.IsText)
        {
            int lineIndex = 0, lineStart = 0, counted = 0, from = 0;
            int found;
            while (added < limit && (found = pattern.NextIn(text, from, out int length)) >= 0)
            {
                var between = text[counted..found];
                int breaks = between.Count('\n');
                if (breaks > 0)
                {
                    lineIndex += breaks;
                    lineStart = counted + between.LastIndexOf('\n') + 1;
                }

                counted = found;
                int lineEnd = text[found..].IndexOf('\n');
                lineEnd = lineEnd < 0 ? text.Length : found + lineEnd;
                var line = text[lineStart..lineEnd].TrimEnd('\r');
                int column = found - lineStart;
                Add(line, lineIndex, column, length);
                from = found + length;
            }

            return added;
        }

        int index = 0;
        for (int start = 0; start <= text.Length && added < limit; index++)
        {
            int newline = text[start..].IndexOf('\n');
            int end = newline < 0 ? text.Length : start + newline;
            var line = text[start..end].TrimEnd('\r');
            int at = 0;
            int found;
            while (added < limit && at <= line.Length && (found = pattern.NextIn(line, at, out int length)) >= 0)
            {
                Add(line, index, found, length);
                at = found + length;
            }

            if (newline < 0)
            {
                break;
            }

            start = end + 1;
        }

        return added;

        void Add(ReadOnlySpan<char> line, int lineIndex, int column, int length)
        {
            if (lineIndex != previewLine)
            {
                previewLine = lineIndex;
                previewStart = 0;
                if (line.Length > previewCharacters && previewCharacters > 0)
                {
                    previewStart = Math.Clamp(column - (previewCharacters / 4), 0, line.Length - previewCharacters);
                    preview = line.Slice(previewStart, previewCharacters).ToString();
                }
                else
                {
                    preview = line.ToString();
                }
            }

            int shown = column - previewStart;
            if (shown < 0 || shown + length > preview!.Length)
            {
                // A second match on a long line past the first's window: its own window.
                int windowStart = Math.Clamp(column - (previewCharacters / 4), 0, Math.Max(0, line.Length - previewCharacters));
                var window = line.Slice(windowStart, Math.Min(previewCharacters, line.Length - windowStart)).ToString();
                hits.Add(new WorkspaceSearchHit(lineIndex, column, length, window, column - windowStart));
            }
            else
            {
                hits.Add(new WorkspaceSearchHit(lineIndex, column, length, preview, shown));
            }

            added++;
        }
    }
}

/// <summary>
/// "text": every file of the workspace (<see cref="WorkspaceFiles"/>) searched in parallel, one file per processor (or
/// <see cref="WorkspaceSearchRequest.Threads"/>), each read as bytes into a buffer its worker keeps, passed over when
/// binary or too large, decoded once into a pooled character buffer (UTF-8, with or without its mark, or UTF-16 by its
/// mark) and matched by <see cref="WorkspaceTextScan"/>; plain text whose case matters is first looked for in the UTF-8
/// bytes themselves, so a file without it is never decoded. Open documents are searched in their current text.
/// </summary>
internal sealed class TextWorkspaceSearch : IWorkspaceSearchProvider
{
    private const int BinaryProbeBytes = 8000;

    public string Name => WorkspaceSearchProviders.Text;

    public string Title => "Text";

    public string Summary => "every file's text, matched as the find overlay matches; open documents in their current text";

    public Task<WorkspaceSearchSummary> SearchAsync(WorkspaceSearchRequest request, Action<WorkspaceSearchFile> found, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(found);
        return Task.Run(() => Search(request, found, cancellationToken), cancellationToken);
    }

    private static WorkspaceSearchSummary Search(WorkspaceSearchRequest request, Action<WorkspaceSearchFile> found, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        var query = request.Query;
        var pattern = query.Pattern(request.RegexTimeout, compiled: query.Regex);
        if (!pattern.Searchable)
        {
            return new WorkspaceSearchSummary(0, 0, 0) { Problem = pattern.Problem ?? (query.Text.Length == 0 ? null : "A search looks within one line: the query holds a line break.") };
        }

        if (!Directory.Exists(request.Root))
        {
            return new WorkspaceSearchSummary(0, 0, 0) { Problem = $"The folder '{request.Root}' does not exist." };
        }

        WorkspaceGlob? include, exclude;
        try
        {
            include = WorkspaceGlob.Parse(query.Include);
            exclude = WorkspaceGlob.Parse(query.Exclude);
        }
        catch (ArgumentException e)
        {
            return new WorkspaceSearchSummary(0, 0, 0) { Problem = "A glob does not parse: " + e.Message };
        }

        var state = new State(request, pattern, found, started);
        // Open documents first, in their current text (their files are not read).
        var open = new HashSet<string>(PathComparer);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root));
        var openHits = new List<WorkspaceSearchHit>();
        foreach (var document in request.OpenDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(document.Path);
            open.Add(full);
            var relative = WorkspaceFiles.Relative(root, full);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                || exclude?.Matches(relative) == true || include?.Matches(relative) == false)
            {
                continue;
            }

            var snapshot = document.Text;
            char[] chars = ArrayPool<char>.Shared.Rent(Math.Max(1, snapshot.Length));
            try
            {
                snapshot.CopyTo(0, chars.AsSpan(0, snapshot.Length));
                Interlocked.Increment(ref state.Searched);
                state.Report(full, chars.AsSpan(0, snapshot.Length), openHits, fromOpenDocument: true);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }

        state.Opened = Volatile.Read(ref state.Searched);
        var files = state.Counted(WorkspaceFiles.Enumerate(root, request.IgnoredFolders, query.UseIgnoreFiles, include, exclude, cancellationToken).Where(path => !open.Contains(path)));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        state.Stop = stop;
        var parallel = new ParallelOptions
        {
            CancellationToken = stop.Token,
            MaxDegreeOfParallelism = request.Threads > 0 ? request.Threads : Environment.ProcessorCount,
        };
        try
        {
            // Rule 79: the files are independent; each worker keeps its buffers across its files (rules 10, 44).
            Parallel.ForEach(files, parallel, () => new Worker(state), static (path, _, worker) =>
            {
                worker.Search(path);
                return worker;
            }, static worker => worker.Dispose());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && state.Capped)
        {
            // Stopped at the cap: the summary says so.
        }

        state.Told(final: true);
        return state.Summary(Stopwatch.GetElapsedTime(started));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // A worker's buffers and hit list, kept across its files and returned when it ends.
    private sealed class Worker(State state) : IDisposable
    {
        private readonly List<WorkspaceSearchHit> _hits = [];
        private byte[] _bytes = ArrayPool<byte>.Shared.Rent(64 * 1024);
        private char[] _chars = ArrayPool<char>.Shared.Rent(64 * 1024);

        public void Search(string path)
        {
            int length;
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
                long size = RandomAccess.GetLength(handle);
                if (size > state.Request.MaxFileBytes || size > Array.MaxLength)
                {
                    Interlocked.Increment(ref state.SkippedLarge);
                    return;
                }

                if (size > _bytes.Length)
                {
                    ArrayPool<byte>.Shared.Return(_bytes);
                    _bytes = ArrayPool<byte>.Shared.Rent((int)size);
                }

                length = 0;
                int read;
                while (length < size && (read = RandomAccess.Read(handle, _bytes.AsSpan(length, (int)size - length), length)) > 0)
                {
                    length += read;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref state.Unreadable);
                return;
            }

            state.Told(Interlocked.Increment(ref state.Searched));
            var bytes = _bytes.AsSpan(0, length);
            Encoding encoding = Encoding.UTF8;
            int mark = 0;
            if (bytes.StartsWith("﻿"u8))
            {
                mark = 3;
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                mark = 2;
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                mark = 2;
            }

            var body = bytes[mark..];
            if (mark != 2 && body[..Math.Min(body.Length, BinaryProbeBytes)].Contains((byte)0))
            {
                Interlocked.Increment(ref state.SkippedBinary);
                return;
            }

            if (mark != 2 && state.Utf8Query is { } literal && body.IndexOf(literal) < 0)
            {
                return;                                          // plain text whose case matters, not in the bytes: never decoded
            }

            if (body.Length > _chars.Length)
            {
                ArrayPool<char>.Shared.Return(_chars);
                _chars = ArrayPool<char>.Shared.Rent(body.Length);
            }

            int count = encoding.GetChars(body, _chars);
            state.Report(path, _chars.AsSpan(0, count), _hits, fromOpenDocument: false);
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_bytes);
            ArrayPool<char>.Shared.Return(_chars);
        }
    }

    // What the workers share: the request, the pattern, the counters and the cap.
    private sealed class State(WorkspaceSearchRequest request, TextPattern pattern, Action<WorkspaceSearchFile> found, long started)
    {
        private int _left = Math.Max(0, request.MaxResults);
        private long _firstResult;
        private volatile bool _capped;

        public int Searched;
        public int Listed;
        public int ListingEnded;
        public int Matched;
        public int Matches;
        public int SkippedLarge;
        public int SkippedBinary;
        public int Unreadable;

        public WorkspaceSearchRequest Request { get; } = request;

        public TextPattern Pattern { get; } = pattern;

        // Plain text whose case matters is looked for in the UTF-8 bytes first.
        public byte[]? Utf8Query { get; } = pattern.IsText && pattern.MatchCase ? Encoding.UTF8.GetBytes(pattern.Query) : null;

        public CancellationTokenSource? Stop { get; set; }

        public bool Capped => _capped;

        // The files to search as they are listed (the partitioner takes them one at a time, under its lock): counted, and
        // the total known once the listing ends.
        public IEnumerable<string> Counted(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                Interlocked.Increment(ref Listed);
                yield return file;
            }

            Volatile.Write(ref ListingEnded, 1);
        }

        // The progress every 32 files searched, and at the end: the files searched, and the total once the listing ended.
        public void Told(int searched = 0, bool final = false)
        {
            if (Request.Progress is not { } progress || (!final && (searched & 31) != 0))
            {
                return;
            }

            int done = Volatile.Read(ref Searched) + Volatile.Read(ref SkippedLarge) + Volatile.Read(ref Unreadable);   // passed over counts as done
            int? total = Volatile.Read(ref ListingEnded) == 1 || final ? Volatile.Read(ref Listed) + Opened : null;
            progress(new WorkspaceSearchProgress(done, total is { } t ? Math.Max(t, done) : null));
        }

        // The open documents searched first, in their current text (part of the total).
        public int Opened { get; set; }

        // The matches of one text into the caller's reused list; a copy of them is reported (nothing is made for a text without a match).
        public void Report(string path, ReadOnlySpan<char> text, List<WorkspaceSearchHit> hits, bool fromOpenDocument)
        {
            int limit = Volatile.Read(ref _left);
            if (limit <= 0)
            {
                return;
            }

            hits.Clear();
            WorkspaceTextScan.Scan(text, Pattern, Request.PreviewCharacters, limit, hits);
            if (hits.Count == 0)
            {
                return;
            }

            int taken = Take(hits.Count);
            if (taken == 0)
            {
                return;
            }

            var kept = hits.GetRange(0, taken);
            Interlocked.Increment(ref Matched);
            Interlocked.Add(ref Matches, taken);
            Interlocked.CompareExchange(ref _firstResult, Stopwatch.GetTimestamp(), 0);
            found(new WorkspaceSearchFile(path, kept) { FromOpenDocument = fromOpenDocument });
        }

        public WorkspaceSearchSummary Summary(TimeSpan elapsed)
        {
            long first = Volatile.Read(ref _firstResult);
            return new WorkspaceSearchSummary(Searched, Matched, Matches)
            {
                Capped = Capped,
                SkippedLarge = SkippedLarge,
                SkippedBinary = SkippedBinary,
                Unreadable = Unreadable,
                Elapsed = elapsed,
                FirstResult = first == 0 ? null : Stopwatch.GetElapsedTime(started, first),
                TimedOut = Pattern.TimedOut,
            };
        }

        // Takes up to want results from what is left; when nothing is left, the search stops there.
        private int Take(int want)
        {
            while (true)
            {
                int left = Volatile.Read(ref _left);
                int take = Math.Min(left, want);
                if (Interlocked.CompareExchange(ref _left, left - take, left) == left)
                {
                    if (left - take == 0)
                    {
                        _capped = true;
                        Stop?.Cancel();
                    }

                    return take;
                }
            }
        }
    }
}
