// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>
/// A fuzzy match of a typed query against a name or a path, as quick open ranks them: the query's characters in order
/// (ignoring case), each later than the one before. Matches at the start of the text, after a separator ('/', '\', '.',
/// '_', '-', ' ') or at a capital after a lower-case letter score more, a run of consecutive matches more again, and every
/// character skipped costs a little. No allocation.
/// </summary>
public static class FuzzyMatch
{
    /// <summary>No match.</summary>
    public const int None = int.MinValue;

    /// <summary>The score of <paramref name="query"/> in <paramref name="text"/>, or <see cref="None"/>; an empty query scores 0.</summary>
    /// <remarks>
    /// Two walks, the better kept: the first match of each character, and the first match at a word start (else the
    /// first match), so "edtool" takes EditorTools' capital T, not the t inside "Editor".
    /// </remarks>
    public static int Score(ReadOnlySpan<char> query, ReadOnlySpan<char> text)
    {
        if (query.IsEmpty)
        {
            return 0;
        }

        if (query.Length > text.Length)
        {
            return None;
        }

        return Math.Max(Walk(query, text, preferStarts: false, []), Walk(query, text, preferStarts: true, []));
    }

    /// <summary>
    /// As <see cref="Score(ReadOnlySpan{char}, ReadOnlySpan{char})"/>, writing where each query character matched (the
    /// better walk's) into <paramref name="positions"/>, which holds at least <paramref name="query"/>'s length: what a row
    /// highlights. Untouched when there is no match.
    /// </summary>
    public static int Score(ReadOnlySpan<char> query, ReadOnlySpan<char> text, Span<int> positions)
    {
        if (positions.Length < query.Length)
        {
            throw new ArgumentException("The positions hold fewer places than the query has characters.", nameof(positions));
        }

        if (query.IsEmpty)
        {
            return 0;
        }

        if (query.Length > text.Length)
        {
            return None;
        }

        int first = Walk(query, text, preferStarts: false, []);
        int second = Walk(query, text, preferStarts: true, []);
        if (first == None && second == None)
        {
            return None;
        }

        return Walk(query, text, preferStarts: second > first, positions[..query.Length]);
    }

    /// <summary>
    /// The runs of consecutive matched positions as (start, length), in order, for a highlight drawn as spans; the
    /// positions are ascending, as <see cref="Score(ReadOnlySpan{char}, ReadOnlySpan{char}, Span{int})"/> writes them.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Runs(ReadOnlySpan<int> positions, int offset = 0)
    {
        var runs = new List<(int, int)>();
        int i = 0;
        while (i < positions.Length)
        {
            int start = positions[i];
            int end = start + 1;
            i++;
            while (i < positions.Length && positions[i] == end)
            {
                end++;
                i++;
            }

            runs.Add((start + offset, end - start));
        }

        return runs;
    }

    private static bool IsStart(ReadOnlySpan<char> text, int i)
    {
        if (i == 0)
        {
            return true;
        }

        char before = text[i - 1];
        return before is '/' or '\\' or '.' or '_' or '-' or ' ' || (char.IsUpper(text[i]) && char.IsLower(before));
    }

    private static int Walk(ReadOnlySpan<char> query, ReadOnlySpan<char> text, bool preferStarts, Span<int> positions)
    {
        int score = 0;
        int previous = -2;
        int i = 0;
        for (int q = 0; q < query.Length; q++)
        {
            char wanted = char.ToLowerInvariant(query[q]);
            int found = -1;
            int start = -1;
            for (int j = i; j < text.Length; j++)
            {
                if (char.ToLowerInvariant(text[j]) != wanted)
                {
                    continue;
                }

                if (found < 0)
                {
                    found = j;
                }

                // The next query character right after the last match keeps a run; else a word start wins when asked.
                if (!preferStarts || j == previous + 1 || IsStart(text, j))
                {
                    start = j;
                    break;
                }
            }

            int at = start >= 0 ? start : found;
            if (at < 0)
            {
                return None;
            }

            int gain = 1;
            if (at == 0)
            {
                gain += 8;
            }
            else if (IsStart(text, at))
            {
                gain += char.IsUpper(text[at]) && char.IsLower(text[at - 1]) ? 5 : 6;
            }

            if (previous == at - 1)
            {
                gain += 4;
            }

            if (text[at] == query[q])
            {
                gain += 1;
            }

            score += gain - (Math.Max(0, at - previous - 1) / 4);
            if (!positions.IsEmpty)
            {
                positions[q] = at;
            }

            previous = at;
            i = at + 1;
        }

        return score;
    }
}

/// <summary>What quick open lists for a query: files by name, commands after '&gt;', a line of the active editor after ':'.</summary>
public enum QuickOpenMode
{
    /// <summary>The workspace's files by fuzzy name (no prefix).</summary>
    Files,

    /// <summary>The commands that can run, by fuzzy title ('&gt;' first: the command palette).</summary>
    Commands,

    /// <summary>A line (and column) of the active editor (':' first: go to line).</summary>
    Line,
}

/// <summary>A row of quick open: a file (its name, its folder, its path) or a command (its title, its keys, its id).</summary>
/// <param name="Text">What it shows first (the file's name, the command's title).</param>
/// <param name="Detail">What it shows after, quieter (the folder), or null.</param>
/// <param name="Path">The file's full path, or null for a command.</param>
/// <param name="CommandId">The command's id, or null for a file.</param>
/// <param name="Score">Its score (higher first).</param>
/// <param name="Line">For ':' (go to line): the line, from 1; 0 for a file or a command.</param>
/// <param name="Column">For ':': the column, from 1 (1 when none was typed).</param>
public sealed record QuickOpenItem(string Text, string? Detail, string? Path, string? CommandId, int Score, int Line = 0, int Column = 1)
{
    /// <summary>The runs of <see cref="Text"/> the query matched, as (start, length): drawn highlighted.</summary>
    public IReadOnlyList<(int Start, int Length)> TextHighlights { get; init; } = [];

    /// <summary>The runs of <see cref="Detail"/> the query matched (a file found by its folder).</summary>
    public IReadOnlyList<(int Start, int Length)> DetailHighlights { get; init; } = [];

    /// <summary>Whether it was picked recently (it shows first, marked).</summary>
    public bool IsRecent { get; init; }

    /// <summary>A command's keys as they are bound now (the user's, the keymap's or the default), or null.</summary>
    public string? Shortcut { get; init; }

    /// <summary>A file's icon by its language (<see cref="FileIcons"/>), or null for a command or a line.</summary>
    public FileIcon? Icon { get; init; }
}

/// <summary>
/// Quick open and the command palette, one list (Ctrl+P, Ctrl+Shift+P or F1, Ctrl+G, the title bar's search box): the
/// workspace's files by fuzzy name, '&gt;' the commands that can run by fuzzy title with their keys, ':' a line of the
/// active editor (":42", ":42:7"). Recent picks (<c>CRAFT_QUICK_OPEN_RECENT</c>) come first. The files are listed once per
/// workspace off the UI thread (<see cref="IndexAsync"/>, at most <see cref="CraftOptionsLimit"/>); a list longer than
/// <see cref="BackgroundFiles"/> is matched off the UI thread too (<see cref="QueryInBackgroundAsync"/>), keeping only the best
/// <see cref="Rows"/> as it goes (no sort of the whole list). Used on the UI thread.
/// </summary>
public sealed class QuickOpen
{
    private const int StackPositions = 128;
    private readonly EditorHost _host;
    private readonly List<string> _recentFiles = [];        // full paths, newest first
    private readonly List<string> _recentCommands = [];     // ids, newest first
    private string? _root;
    private string[] _files = [];
    private int[] _nameStart = [];
    private int[] _recentRank = [];                          // per file: 0, or its place in the recent files + 1
    private Task _indexing = Task.CompletedTask;
    private CancellationTokenSource? _cancel;
    private int _recentLimit = 20;

    /// <summary>Quick open over <paramref name="host"/>'s commands, showing <paramref name="rows"/> rows.</summary>
    public QuickOpen(EditorHost host, int rows)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Rows = Math.Max(1, rows);
    }

    /// <summary>The rows a query gives at most.</summary>
    public int Rows { get; set; }

    /// <summary>The most files listed (<c>CRAFT_QUICK_OPEN_FILES</c>).</summary>
    public int CraftOptionsLimit { get; set; } = 200_000;

    /// <summary>A file list longer than this is matched off the UI thread (<c>CRAFT_QUICK_OPEN_BACKGROUND_FILES</c>).</summary>
    public int BackgroundFiles { get; set; } = 2000;

    /// <summary>The recent files and commands kept (<c>CRAFT_QUICK_OPEN_RECENT</c>); 0 keeps none.</summary>
    public int RecentLimit
    {
        get => _recentLimit;
        set
        {
            _recentLimit = Math.Max(0, value);
            Trim(_recentFiles);
            Trim(_recentCommands);
            RankRecent();
        }
    }

    /// <summary>The files listed, as paths relative to the workspace's root.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>The recent files (full paths, newest first).</summary>
    public IReadOnlyList<string> RecentFiles => _recentFiles;

    /// <summary>The recent commands (ids, newest first).</summary>
    public IReadOnlyList<string> RecentCommands => _recentCommands;

    /// <summary>Whether the listing stopped at the limit.</summary>
    public bool Capped { get; private set; }

    /// <summary>Whether the listing is still running.</summary>
    public bool IsIndexing => !_indexing.IsCompleted;

    /// <summary>The workspace listed, or null.</summary>
    public string? Root => _root;

    /// <summary>Whether the last query was matched off the UI thread.</summary>
    public bool LastQueryInBackground { get; private set; }

    /// <summary>The mode <paramref name="text"/> asks for, by its first character ('&gt;' commands, ':' a line, else files).</summary>
    public static QuickOpenMode ModeOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.AsSpan().TrimStart() switch
        {
            ['>', ..] => QuickOpenMode.Commands,
            [':', ..] => QuickOpenMode.Line,
            _ => QuickOpenMode.Files,
        };
    }

    /// <summary>Whether <paramref name="text"/> asks for a line (':' first).</summary>
    public static bool IsLineQuery(string text) => ModeOf(text) == QuickOpenMode.Line;

    /// <summary>The text that opens <paramref name="mode"/> empty ("", "&gt;", ":").</summary>
    public static string PrefixOf(QuickOpenMode mode) => mode switch
    {
        QuickOpenMode.Commands => ">",
        QuickOpenMode.Line => ":",
        _ => "",
    };

    /// <summary>
    /// Takes <paramref name="relativePaths"/> under <paramref name="root"/> as the files listed, in place of a listing (an
    /// app that lists the files itself, a test); the list is sorted here.
    /// </summary>
    public void SetFiles(string? root, IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var (files, starts) = Prepare([.. relativePaths]);
        _root = root;
        _files = files;
        _nameStart = starts;
        Capped = false;
        RankRecent();
    }

    /// <summary>
    /// Lists <paramref name="workspace"/>'s files (null: none) off the calling thread, in place of the last listing (which
    /// is cancelled); the result is taken on the calling thread's context when it completes.
    /// </summary>
    public async Task IndexAsync(Workspace? workspace)
    {
        if (_cancel is { } previous)
        {
            _cancel = null;
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        if (workspace is null)
        {
            _root = null;
            _files = [];
            _nameStart = [];
            _recentRank = [];
            Capped = false;
            return;
        }

        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        _indexing = IndexCoreAsync(workspace, workspace.Root, CraftOptionsLimit, cancel.Token);
        try
        {
            await _indexing.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // A newer listing took its place.
        }
    }

    /// <summary>Notes <paramref name="item"/> as picked: it goes first among the recent ones (a line is not kept).</summary>
    public void NoteChosen(QuickOpenItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CommandId is { } command)
        {
            Remember(_recentCommands, command, StringComparer.OrdinalIgnoreCase);
        }
        else if (item.Path is { } path)
        {
            NoteFile(path);
        }
    }

    /// <summary>Notes the file <paramref name="path"/> as opened (from the tree, a tab, quick open): it goes first among the recent files.</summary>
    public void NoteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Remember(_recentFiles, Path.GetFullPath(path), PathKeys.Comparer);
        RankRecent();
    }

    /// <summary>
    /// The best rows for <paramref name="text"/> on the calling thread: files by fuzzy name (the name scored first, the
    /// path after, shorter paths ahead on a tie, recent ones ahead), after '&gt;' the commands that can run by fuzzy title
    /// with their keys (recent ones first), after ':' the line to go to.
    /// </summary>
    public IReadOnlyList<QuickOpenItem> Query(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        LastQueryInBackground = false;
        var span = text.AsSpan().Trim();
        return ModeOf(text) switch
        {
            QuickOpenMode.Line => GoToLine(span[1..].Trim()),
            QuickOpenMode.Commands => Commands(span[1..].Trim()),
            _ => FileItems(span, ScoreFiles(_files, _nameStart, _recentRank, span, Rows)),
        };
    }

    /// <summary>
    /// As <see cref="Query"/>, matching a file list longer than <see cref="BackgroundFiles"/> on the thread pool; the rows
    /// are made on the calling thread's context. A cancelled query throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<IReadOnlyList<QuickOpenItem>> QueryInBackgroundAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ModeOf(text) != QuickOpenMode.Files || _files.Length <= BackgroundFiles)
        {
            return Query(text);
        }

        var files = _files;
        var starts = _nameStart;
        var ranks = _recentRank;
        int rows = Rows;
        string query = text.Trim();
        var best = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ScoreFiles(files, starts, ranks, query, rows, cancellationToken);
        }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        LastQueryInBackground = true;
        if (!ReferenceEquals(files, _files))
        {
            return Query(text);                                  // a new listing landed meanwhile: matched again over it
        }

        return FileItems(query, best);
    }

    private async Task IndexCoreAsync(Workspace workspace, string root, int limit, CancellationToken token)
    {
        var (files, starts, capped) = await Task.Run(() => List(workspace, root, limit, token), token).ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        _root = root;
        _files = files;
        _nameStart = starts;
        Capped = capped;
        RankRecent();
    }

    private static (string[] Files, int[] NameStarts, bool Capped) List(Workspace workspace, string root, int limit, CancellationToken token)
    {
        var files = new List<string>();
        bool capped = false;
        int skip = root.Length + (Path.EndsInDirectorySeparator(root) ? 0 : 1);
        foreach (var path in workspace.EnumerateFiles())
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= limit)
            {
                capped = true;
                break;
            }

            files.Add(path.Length > skip ? path[skip..] : path);
        }

        var (array, starts) = Prepare(files);
        return (array, starts, capped);
    }

    private static (string[] Files, int[] NameStarts) Prepare(List<string> files)
    {
        files.Sort(StringComparer.OrdinalIgnoreCase);
        var array = files.ToArray();
        var starts = new int[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            starts[i] = array[i].AsSpan().LastIndexOfAny('/', '\\') + 1;
        }

        return (array, starts);
    }

    private void Remember(List<string> list, string value, StringComparer comparer)
    {
        if (_recentLimit == 0)
        {
            return;
        }

        int at = list.FindIndex(v => comparer.Equals(v, value));
        if (at >= 0)
        {
            list.RemoveAt(at);
        }

        list.Insert(0, value);
        Trim(list);
    }

    private void Trim(List<string> list)
    {
        if (list.Count > _recentLimit)
        {
            list.RemoveRange(_recentLimit, list.Count - _recentLimit);
        }
    }

    // Each listed file's place among the recent ones (a new array: a background match keeps the one it took).
    private void RankRecent()
    {
        var ranks = new int[_files.Length];
        if (_root is { } root && _recentFiles.Count > 0)
        {
            var byPath = new Dictionary<string, int>(PathKeys.Comparer);
            for (int r = 0; r < _recentFiles.Count; r++)
            {
                var relative = Path.GetRelativePath(root, _recentFiles[r]);
                byPath.TryAdd(relative.Replace('\\', '/'), r + 1);
                byPath.TryAdd(relative.Replace('/', '\\'), r + 1);
            }

            for (int i = 0; i < _files.Length; i++)
            {
                if (byPath.TryGetValue(_files[i], out int rank))
                {
                    ranks[i] = rank;
                }
            }
        }

        _recentRank = ranks;
    }

    // ":42" or ":42:7" (":42,7" too): one row that puts the caret there; nothing for text that is not a line number.
    private static List<QuickOpenItem> GoToLine(ReadOnlySpan<char> query)
    {
        int split = query.IndexOfAny(':', ',');
        var linePart = split < 0 ? query : query[..split];
        if (!int.TryParse(linePart, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int line) || line < 1)
        {
            return [];
        }

        int column = 1;
        if (split >= 0 && (!int.TryParse(query[(split + 1)..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out column) || column < 1))
        {
            column = 1;
        }

        string text = column > 1
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Go to line {line}, column {column}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Go to line {line}");
        return [new QuickOpenItem(text, "in the active editor", null, null, 0, line, column)];
    }

    // The best rows' (index, score): any thread (it reads only what it is given). An empty query lists the recent files
    // first, then the rest by path.
    private static (int Index, int Score)[] ScoreFiles(string[] files, int[] starts, int[] ranks, ReadOnlySpan<char> query, int rows, CancellationToken token = default)
    {
        var best = new Best(rows);
        bool anyRecent = ranks.Length == files.Length && ranks.AsSpan().IndexOfAnyExcept(0) >= 0;
        for (int i = 0; i < files.Length; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            var path = files[i].AsSpan();
            int score;
            if (query.IsEmpty)
            {
                score = -i;                                        // the sorted order
            }
            else
            {
                int name = FuzzyMatch.Score(query, path[starts[i]..]);
                score = name != FuzzyMatch.None ? (name * 2) + 1000 : FuzzyMatch.Score(query, path);
                if (score == FuzzyMatch.None)
                {
                    continue;
                }

                score -= path.Length / 8;
            }

            if (anyRecent && ranks[i] > 0)
            {
                // With no query the recent files lead in the order they were opened; with one they rank a little ahead.
                score = query.IsEmpty ? 1_000_000_000 - ranks[i] : score + 60 - Math.Min(40, ranks[i]);
            }

            best.Offer(i, score);
        }

        return best.Ordered().ToArray();
    }

    private List<QuickOpenItem> FileItems(ReadOnlySpan<char> query, (int Index, int Score)[] best)
    {
        var items = new List<QuickOpenItem>(best.Length);
        var files = _files;
        var starts = _nameStart;
        var ranks = _recentRank;
        int[]? rented = query.Length > StackPositions ? new int[query.Length] : null;
        Span<int> positions = rented ?? stackalloc int[StackPositions];
        positions = positions[..query.Length];
        foreach (var (index, score) in best)
        {
            var relative = files[index];
            int start = starts[index];
            string folder = start > 0 ? relative[..(start - 1)] : "";
            IReadOnlyList<(int, int)> textRuns = [];
            IReadOnlyList<(int, int)> detailRuns = [];
            if (!query.IsEmpty)
            {
                if (FuzzyMatch.Score(query, relative.AsSpan(start), positions) != FuzzyMatch.None)
                {
                    textRuns = FuzzyMatch.Runs(positions);
                }
                else if (FuzzyMatch.Score(query, relative, positions) != FuzzyMatch.None)
                {
                    int split = positions.IndexOfAnyInRange(start, int.MaxValue);
                    if (split < 0)
                    {
                        split = positions.Length;
                    }

                    detailRuns = FuzzyMatch.Runs(positions[..split]);
                    textRuns = FuzzyMatch.Runs(positions[split..], -start);
                }
            }

            items.Add(new QuickOpenItem(relative[start..], folder.Length == 0 ? null : folder, _root is null ? relative : Path.Combine(_root, relative), null, score)
            {
                TextHighlights = textRuns,
                DetailHighlights = detailRuns,
                IsRecent = ranks.Length == files.Length && ranks[index] > 0,
                Icon = FileIcons.For(relative),
            });
        }

        return items;
    }

    private List<QuickOpenItem> Commands(ReadOnlySpan<char> query)
    {
        var commands = _host.Commands;
        var best = new Best(Math.Max(Rows, commands.Count));
        int[]? rented = query.Length > StackPositions ? new int[query.Length] : null;
        Span<int> positions = rented ?? stackalloc int[StackPositions];
        positions = positions[..query.Length];
        for (int i = 0; i < commands.Count; i++)
        {
            bool can;
            try
            {
                can = commands[i].CanExecute(_host);
            }
#pragma warning disable CA1031 // A command whose check throws is left out of the list, never a crash.
            catch (Exception)
#pragma warning restore CA1031
            {
                can = false;
            }

            int score = can ? FuzzyMatch.Score(query, commands[i].Title) : FuzzyMatch.None;
            if (score == FuzzyMatch.None)
            {
                continue;
            }

            int rank = _recentCommands.FindIndex(id => id.Equals(commands[i].Name, StringComparison.OrdinalIgnoreCase));
            if (rank >= 0)
            {
                score += query.IsEmpty ? 10_000_000 - rank : 60 - Math.Min(40, rank + 1);
            }

            best.Offer(i, score);
        }

        var items = new List<QuickOpenItem>(best.Count);
        foreach (var (index, score) in best.Ordered())
        {
            var command = commands[index];
            IReadOnlyList<(int, int)> runs = !query.IsEmpty && FuzzyMatch.Score(query, command.Title, positions) != FuzzyMatch.None ? FuzzyMatch.Runs(positions) : [];
            items.Add(new QuickOpenItem(command.Title, null, null, command.Name, score)
            {
                TextHighlights = runs,
                IsRecent = _recentCommands.Exists(id => id.Equals(command.Name, StringComparison.OrdinalIgnoreCase)),
                Shortcut = _host.KeysOf(command.Name),
            });
        }

        if (query.IsEmpty)
        {
            // The recent ones first (as picked), then the rest by title.
            items.Sort(static (a, b) => a.IsRecent != b.IsRecent ? (a.IsRecent ? -1 : 1)
                : a.IsRecent ? b.Score.CompareTo(a.Score)
                : string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase));
        }

        return items;
    }

    // The best n (index, score) pairs, kept sorted by insertion: n is a screen of rows, so this beats sorting everything.
    private sealed class Best(int capacity)
    {
        private readonly (int Index, int Score)[] _items = new (int, int)[Math.Max(1, capacity)];

        public int Count { get; private set; }

        public void Offer(int index, int score)
        {
            if (Count == _items.Length && score <= _items[Count - 1].Score)
            {
                return;
            }

            int at = Count == _items.Length ? Count - 1 : Count++;
            while (at > 0 && _items[at - 1].Score < score)
            {
                _items[at] = _items[at - 1];
                at--;
            }

            _items[at] = (index, score);
        }

        public ReadOnlySpan<(int Index, int Score)> Ordered() => _items.AsSpan(0, Count);
    }
}

/// <summary>
/// The keyboard over a list of rows (the palette's, quick open's): the selected row, moved by the arrows (wrapping), Page
/// Up and Down (a page of <see cref="PageRows"/>, stopping at the ends), Home and End; a new list selects its first row.
/// </summary>
public sealed class OverlaySelection
{
    /// <summary>The rows in the list.</summary>
    public int Count { get; private set; }

    /// <summary>The selected row, or -1 when the list is empty.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The rows a page moves (the rows the list shows).</summary>
    public int PageRows { get; set; } = 10;

    /// <summary>A new list of <paramref name="count"/> rows: the first is selected.</summary>
    public void Reset(int count)
    {
        Count = Math.Max(0, count);
        Selected = Count > 0 ? 0 : -1;
    }

    /// <summary>Moves by <paramref name="step"/> rows (an arrow: 1 or -1), wrapping at the ends.</summary>
    public int Move(int step)
    {
        if (Count > 0)
        {
            Selected = (((Selected + step) % Count) + Count) % Count;
        }

        return Selected;
    }

    /// <summary>Moves a page down (1) or up (-1), stopping at the last or the first row.</summary>
    public int Page(int direction)
    {
        if (Count > 0)
        {
            Selected = Math.Clamp(Selected + (direction * Math.Max(1, PageRows - 1)), 0, Count - 1);
        }

        return Selected;
    }

    /// <summary>Selects <paramref name="index"/> (clamped to the list).</summary>
    public int Select(int index)
    {
        if (Count > 0)
        {
            Selected = Math.Clamp(index, 0, Count - 1);
        }

        return Selected;
    }

    /// <summary>
    /// The keys of an open list: Up, Down, Page Up, Page Down, Home and End (Ctrl+Home, Ctrl+End) move; true when the key
    /// was one of them.
    /// </summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        bool plain = (modifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) == 0;
        switch (key)
        {
            case EditorKey.Down when plain:
                Move(1);
                return true;
            case EditorKey.Up when plain:
                Move(-1);
                return true;
            case EditorKey.PageDown when plain:
                Page(1);
                return true;
            case EditorKey.PageUp when plain:
                Page(-1);
                return true;
            case EditorKey.End when (modifiers & KeyModifiers.Control) != 0:
                Select(Count - 1);
                return true;
            case EditorKey.Home when (modifiers & KeyModifiers.Control) != 0:
                Select(0);
                return true;
            default:
                return false;
        }
    }
}
