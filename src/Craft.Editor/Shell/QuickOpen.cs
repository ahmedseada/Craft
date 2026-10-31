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

        return Math.Max(Walk(query, text, preferStarts: false), Walk(query, text, preferStarts: true));
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

    private static int Walk(ReadOnlySpan<char> query, ReadOnlySpan<char> text, bool preferStarts)
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
            previous = at;
            i = at + 1;
        }

        return score;
    }
}

/// <summary>A row of quick open: a file (its name, its folder, its path) or a command (its title, its chord, its id).</summary>
/// <param name="Text">What it shows first (the file's name, the command's title).</param>
/// <param name="Detail">What it shows after, quieter (the folder, the chord), or null.</param>
/// <param name="Path">The file's full path, or null for a command.</param>
/// <param name="CommandId">The command's id, or null for a file.</param>
/// <param name="Score">Its score (higher first).</param>
public sealed record QuickOpenItem(string Text, string? Detail, string? Path, string? CommandId, int Score);

/// <summary>
/// Quick open (Ctrl+P, the title bar's search box): the workspace's files by fuzzy name, or, after a leading '&gt;', the
/// commands that can run, by title. The files are listed once per workspace off the UI thread
/// (<see cref="IndexAsync"/>, at most <see cref="CraftOptionsLimit"/>); <see cref="Query"/> runs on the UI thread over
/// the list it holds, keeping only the best <c>rows</c> as it goes (no sort of the whole list).
/// </summary>
public sealed class QuickOpen
{
    private readonly EditorHost _host;
    private string? _root;
    private string[] _files = [];
    private int _nameStarts;
    private int[] _nameStart = [];
    private Task _indexing = Task.CompletedTask;
    private CancellationTokenSource? _cancel;

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

    /// <summary>The files listed, as paths relative to the workspace's root.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Whether the listing stopped at the limit.</summary>
    public bool Capped { get; private set; }

    /// <summary>Whether the listing is still running.</summary>
    public bool IsIndexing => !_indexing.IsCompleted;

    /// <summary>The workspace listed, or null.</summary>
    public string? Root => _root;

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
            _nameStarts = 0;
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
        _nameStarts = starts.Length;
        Capped = capped;
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

        files.Sort(StringComparer.OrdinalIgnoreCase);
        var array = files.ToArray();
        var starts = new int[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            starts[i] = array[i].AsSpan().LastIndexOfAny('/', '\\') + 1;
        }

        return (array, starts, capped);
    }

    /// <summary>
    /// The best rows for <paramref name="text"/>: files by fuzzy name (the file's name scored first, its path after,
    /// shorter paths ahead on a tie), or after '&gt;' the commands that can run by fuzzy title, with their chords.
    /// </summary>
    public IReadOnlyList<QuickOpenItem> Query(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var span = text.AsSpan().Trim();
        return span.Length > 0 && span[0] == '>' ? Commands(span[1..].Trim()) : FilesFor(span);
    }

    private List<QuickOpenItem> FilesFor(ReadOnlySpan<char> query)
    {
        var best = new Best(Rows);
        var files = _files;
        var starts = _nameStart;
        for (int i = 0; i < files.Length && i < _nameStarts; i++)
        {
            var path = files[i].AsSpan();
            int name = FuzzyMatch.Score(query, path[starts[i]..]);
            int score = name != FuzzyMatch.None ? (name * 2) + 1000 : FuzzyMatch.Score(query, path);
            if (score != FuzzyMatch.None)
            {
                best.Offer(i, score - (path.Length / 8));
            }
        }

        var items = new List<QuickOpenItem>(best.Count);
        foreach (var (index, score) in best.Ordered())
        {
            var relative = files[index];
            int start = starts[index];
            string folder = start > 0 ? relative[..(start - 1)] : "";
            items.Add(new QuickOpenItem(relative[start..], folder.Length == 0 ? null : folder, _root is null ? relative : Path.Combine(_root, relative), null, score));
        }

        return items;
    }

    private List<QuickOpenItem> Commands(ReadOnlySpan<char> query)
    {
        var commands = _host.Commands;
        var best = new Best(Math.Max(Rows, commands.Count));
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
            if (score != FuzzyMatch.None)
            {
                best.Offer(i, score);
            }
        }

        var items = new List<QuickOpenItem>(best.Count);
        foreach (var (index, score) in best.Ordered())
        {
            var command = commands[index];
            items.Add(new QuickOpenItem(command.Title, ShellMenus.ChordOf(command.Name), null, command.Name, score));
        }

        if (query.IsEmpty)
        {
            items.Sort(static (a, b) => string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase));
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
