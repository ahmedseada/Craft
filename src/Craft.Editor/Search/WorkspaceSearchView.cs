// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Search;

/// <summary>A file of the Search view's results: its path, its matches (in order), whether they are in its open document's text.</summary>
public sealed class SearchFileResult
{
    internal SearchFileResult(string path, string relative, List<WorkspaceSearchHit> hits, bool fromOpenDocument)
    {
        Path = path;
        Relative = relative;
        Hits = hits;
        FromOpenDocument = fromOpenDocument;
    }

    /// <summary>Its full path.</summary>
    public string Path { get; }

    /// <summary>Its path relative to the workspace ('/' between folders).</summary>
    public string Relative { get; }

    /// <summary>Its matches still shown (a dismissed one is taken out), in order.</summary>
    public List<WorkspaceSearchHit> Hits { get; }

    /// <summary>Whether the matches are in its open document's text, not its file's.</summary>
    public bool FromOpenDocument { get; }
}

/// <summary>
/// The Search view (X part 6), the rail's Search and Ctrl+Shift+F: a query with match case, whole word and regular
/// expression (the find overlay's <see cref="TextPattern"/>), files to include and exclude (globs; the ignored folders and
/// the <c>.gitignore</c> files kept), results grouped by file (its icon, its folder, its count, folding) with each line's
/// match highlighted, a click opening the place; Replace (a match, a file) and Replace all as one review across files
/// (<see cref="EditorHost.ProposeFilesAsync"/>: open documents one undo step each), then the shell's Undo bar. The search
/// runs off the UI thread through the picked <see cref="IWorkspaceSearchProvider"/> (the library's "text": the files in
/// parallel, bytes and spans), its results arrive in batches (one UI turn each), the next keystroke cancels it after
/// <c>CRAFT_SEARCH_DELAY_MS</c>, it shows as an activity, and it stops at <c>CRAFT_SEARCH_MAX_RESULTS</c>. Open documents
/// are searched in their current text. Headless: it says what it holds as blocks (<see cref="IEditorViewPanel"/>), which
/// the shell draws as a plug-in's view would be; every member is called on the UI thread.
/// </summary>
public sealed class WorkspaceSearchView : IEditorViewPanel, IDisposable
{
    /// <summary>Its panel id (and its rail item's).</summary>
    public const string PanelId = "craft.search";

    /// <summary>The query field's action (the text typed).</summary>
    public const string QueryAction = "search.query";

    /// <summary>The replace field's action.</summary>
    public const string ReplaceAction = "search.replace";

    /// <summary>The include field's action.</summary>
    public const string IncludeAction = "search.include";

    /// <summary>The exclude field's action.</summary>
    public const string ExcludeAction = "search.exclude";

    /// <summary>A result row's click (its value: the row's id).</summary>
    public const string OpenAction = "search.open";

    /// <summary>A file's header click (its value: the file's path): folds or unfolds it.</summary>
    public const string ToggleAction = "search.toggle";

    /// <summary>Replace all (in every file listed).</summary>
    public const string ReplaceAllAction = "search.replaceAll";

    private const char Separator = '\n';
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly EditorHost _host;
    private readonly Func<string?> _root;
    private readonly Func<CraftOptions> _options;
    private readonly List<SearchFileResult> _results = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(int Generation, WorkspaceSearchFile File)> _arrived = new();
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private CancellationTokenSource? _search;
    private TaskCompletionSource? _done;
    private string _query = "";
    private string _replace = "";
    private string _include = "";
    private string _exclude = "";
    private string? _provider;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regex;
    private bool _useIgnoreFiles;
    private bool _showReplace;
    private bool _showFilters;
    private bool _searching;
    private int _generation;
    private int _version;
    private int _focus;
    private int _flushPending;
    private string? _problem;
    private string? _note;
    private WorkspaceSearchSummary? _summary;
    private bool _disposed;

    /// <summary>The view over <paramref name="host"/>'s documents, searching the folder <paramref name="root"/> gives (none: nothing to search) with <paramref name="options"/>.</summary>
    public WorkspaceSearchView(EditorHost host, Func<string?> root, Func<CraftOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _useIgnoreFiles = options().SearchIgnoreFiles;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => "Search";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>The query.</summary>
    public string Query => _query;

    /// <summary>The replacement.</summary>
    public string Replacement => _replace;

    /// <summary>The files with matches, sorted by their path (ignoring case).</summary>
    public IReadOnlyList<SearchFileResult> Results => _results;

    /// <summary>How the last search ended, or null while one runs or none has.</summary>
    public WorkspaceSearchSummary? Summary => _summary;

    /// <summary>Whether a search runs now.</summary>
    public bool Searching => _searching;

    /// <summary>The matches listed in all.</summary>
    public int MatchCount => _results.Sum(r => r.Hits.Count);

    /// <summary>Completes when the search asked last has ended and its results are in (at once when none runs).</summary>
    public Task Settled => _done?.Task ?? Task.CompletedTask;

    /// <summary>Sets the toggles (match case, whole word, regular expression) and searches again.</summary>
    public void SetOptions(bool matchCase, bool wholeWord, bool regex)
    {
        _matchCase = matchCase;
        _wholeWord = wholeWord;
        _regex = regex;
        Schedule(immediately: false);
    }

    /// <summary>Sets the include and exclude globs and searches again.</summary>
    public void SetFilters(string include, string exclude, bool? useIgnoreFiles = null)
    {
        _include = include ?? "";
        _exclude = exclude ?? "";
        _useIgnoreFiles = useIgnoreFiles ?? _useIgnoreFiles;
        _showFilters |= _include.Length > 0 || _exclude.Length > 0;
        Schedule(immediately: false);
    }

    /// <summary>
    /// Brings the keyboard to the query (Ctrl+Shift+F), taking <paramref name="selection"/> as the query when it is one
    /// line, and showing the replace field when <paramref name="replace"/>.
    /// </summary>
    public void Focus(string? selection = null, bool replace = false)
    {
        _focus++;
        _showReplace |= replace;
        if (!string.IsNullOrEmpty(selection) && selection.AsSpan().IndexOfAny('\r', '\n') < 0 && selection != _query)
        {
            _query = selection;
            Schedule(immediately: true);
            return;
        }

        Publish();
    }

    /// <summary>The workspace changed (another folder, or none): the results go, and the query searches the new one.</summary>
    public void WorkspaceChanged()
    {
        _results.Clear();
        _summary = null;
        Schedule(immediately: true);
    }

    /// <inheritdoc />
    public async Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        switch (actionId)
        {
            case QueryAction:
                if (!string.Equals(_query, value ?? "", StringComparison.Ordinal))
                {
                    _query = value ?? "";
                    Schedule(immediately: false);
                }

                break;
            case "search.now":
                _query = value ?? _query;
                Schedule(immediately: true);
                break;
            case "search.case":
                _matchCase = value == "on";
                Schedule(immediately: true);
                break;
            case "search.word":
                _wholeWord = value == "on";
                Schedule(immediately: true);
                break;
            case "search.regex":
                _regex = value == "on";
                Schedule(immediately: true);
                break;
            case "search.ignoreFiles":
                _useIgnoreFiles = value == "on";
                Schedule(immediately: true);
                break;
            case ReplaceAction:
                _replace = value ?? "";
                Publish();
                break;
            case IncludeAction:
                _include = value ?? "";
                Schedule(immediately: false);
                break;
            case ExcludeAction:
                _exclude = value ?? "";
                Schedule(immediately: false);
                break;
            case "search.showReplace":
                _showReplace = !_showReplace;
                Publish();
                break;
            case "search.showFilters":
                _showFilters = !_showFilters;
                Publish();
                break;
            case "search.provider":
                _provider = value;
                Schedule(immediately: true);
                break;
            case ToggleAction when value is not null:
                if (!_collapsed.Remove(value))
                {
                    _collapsed.Add(value);
                }

                Changed2();
                break;
            case "search.collapseAll":
                bool all = _results.Count > 0 && _results.All(r => _collapsed.Contains(r.Path));
                _collapsed.Clear();
                if (!all)
                {
                    foreach (var result in _results)
                    {
                        _collapsed.Add(result.Path);
                    }
                }

                Changed2();
                break;
            case "search.clear":
                _query = "";
                _focus++;
                Schedule(immediately: true);
                break;
            case "search.refresh":
                Schedule(immediately: true);
                break;
            case OpenAction when value is not null:
                await OpenAsync(value, cancellationToken).ConfigureAwait(true);
                break;
            case "search.dismiss" when value is not null:
                Dismiss(value);
                break;
            case "search.dismissFile" when value is not null:
                _results.RemoveAll(r => r.Path == value);
                Changed2();
                break;
            case ReplaceAllAction:
                await ReplaceAsync(null, null, cancellationToken).ConfigureAwait(true);
                break;
            case "search.replaceFile" when value is not null:
                await ReplaceAsync(value, null, cancellationToken).ConfigureAwait(true);
                break;
            case "search.replaceOne" when value is not null && Row(value) is var (file, hit) && file is not null:
                await ReplaceAsync(file.Path, hit, cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Opens the place of the result row <paramref name="rowId"/> (its file, the match selected); false when it is gone.</summary>
    public async Task<bool> OpenAsync(string rowId, CancellationToken cancellationToken = default)
    {
        if (Row(rowId) is not (SearchFileResult file, WorkspaceSearchHit hit))
        {
            return false;
        }

        if (await _host.OpenDocumentAsync(file.Path, cancellationToken).ConfigureAwait(true) is not { } document)
        {
            return false;
        }

        var snapshot = document.Document.Buffer.Current;
        int line = Math.Clamp(hit.Line, 0, snapshot.LineCount - 1);
        var text = snapshot.GetLine(line);
        int start = Math.Min(text.Start + hit.Column, text.Start + text.Length);
        int end = Math.Min(start + hit.Length, text.Start + text.Length);
        return _host.Select(document, new SelectionState(start, end));
    }

    /// <summary>The id of the result row of <paramref name="file"/>'s match number <paramref name="index"/>.</summary>
    public static string RowId(string file, int index) => string.Create(Culture, $"{file}{Separator}{index}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
        _done?.TrySetResult();
    }

    // ---- searching ----

    // The query changed: the running search stops; a new one starts after the typing delay (at once when asked).
    private void Schedule(bool immediately)
    {
        if (_disposed)
        {
            return;
        }

        _search?.Cancel();
        _search?.Dispose();
        _search = null;
        _generation++;
        _done?.TrySetResult();
        _done = null;
        _problem = null;
        _note = null;
        var options = _options();
        var query = CurrentQuery();
        var pattern = query.Pattern(TimeSpan.FromMilliseconds(options.FindRegexTimeoutMilliseconds));
        if (query.Text.Length == 0 || _root() is not { } root || !pattern.Searchable)
        {
            _problem = query.Text.Length == 0 ? null : _root() is null ? "Open a folder to search its files." : pattern.Problem ?? "A search looks within one line.";
            _results.Clear();
            _summary = null;
            _searching = false;
            _version++;
            Publish();
            return;
        }

        var cancel = new CancellationTokenSource();
        _search = cancel;
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delay = immediately ? 0 : options.SearchDelayMilliseconds;
        _ = RunAsync(root, query, options, delay, _generation, _done, cancel.Token);
    }

    private WorkspaceSearchQuery CurrentQuery() => new(_query, _matchCase, _wholeWord, _regex)
    {
        Include = _include,
        Exclude = _exclude,
        UseIgnoreFiles = _useIgnoreFiles,
    };

    private IWorkspaceSearchProvider Provider()
    {
        string name = _provider ?? _options().SearchProvider;
        return WorkspaceSearchProviders.Find(name) ?? WorkspaceSearchProviders.Get(WorkspaceSearchProviders.Text);
    }

    // The typing delay, then the search starts on the UI thread (it reads the open documents there).
    private async Task RunAsync(string root, WorkspaceSearchQuery query, CraftOptions options, int delay, int generation, TaskCompletionSource done, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            done.TrySetResult();                                       // a newer keystroke came within the delay
            return;
        }

        _host.Dispatch("search: start", () =>
        {
            if (cancellationToken.IsCancellationRequested || generation != _generation || _disposed)
            {
                done.TrySetResult();
                return;
            }

            _ = SearchAsync(root, query, options, generation, done, cancellationToken);
        });
    }

    // On the UI thread until the provider runs; its results and its end come back through the dispatcher.
    private async Task SearchAsync(string root, WorkspaceSearchQuery query, CraftOptions options, int generation, TaskCompletionSource done, CancellationToken cancellationToken)
    {
        try
        {
            var request = new WorkspaceSearchRequest(root, query)
            {
                IgnoredFolders = options.WorkspaceIgnore,
                OpenDocuments = OpenDocuments(),
                MaxResults = options.SearchMaxResults,
                MaxFileBytes = (long)options.SearchMaxFileMegabytes * 1024 * 1024,
                Threads = options.SearchThreads,
                RegexTimeout = TimeSpan.FromMilliseconds(options.FindRegexTimeoutMilliseconds),
            };
            IWorkspaceSearchProvider provider;
            try
            {
                provider = Provider();
            }
            catch (NotRegisteredException e)
            {
                Finish(generation, null, e.Message);
                return;
            }

            _results.Clear();
            _summary = null;
            _searching = true;
            _version++;
            Publish();
            using var activity = _host.Activities.Start($"Searching for '{Shorten(query.Text, 40)}'", ActivityKinds.Search, cancellable: true, cancellationToken, keep: false);
            int files = 0;
            var summary = await Task.Run(() => provider.SearchAsync(request, file =>
            {
                _arrived.Enqueue((generation, file));
                int count = Interlocked.Increment(ref files);
                activity.Report(null, string.Create(Culture, $"{count} {(count == 1 ? "file" : "files")} with matches"));
                if (Interlocked.Exchange(ref _flushPending, 1) == 0)
                {
                    _host.Dispatch("search: show a batch of results", Flush);       // one UI turn takes every file found since the last
                }
            }, activity.CancellationToken), activity.CancellationToken).ConfigureAwait(false);
            activity.Complete(string.Create(Culture, $"{summary.Matches} in {summary.FilesMatched} files"));
            _host.Dispatch("search: done", () => Finish(generation, summary, summary.Problem));
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke, or Cancel in the status bar: a newer search (if any) shows its own results.
            _host.Dispatch("search: cancelled", () => Finish(generation, null, null, cancelled: true));
        }
#pragma warning disable CA1031 // A provider's failure is said in the view; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Dispatch("search: failed", () => Finish(generation, null, $"The search failed: {error.Message}"));
        }
        finally
        {
            _host.Dispatch("search: settled", () => done.TrySetResult());
        }
    }

    private List<WorkspaceSearchDocument> OpenDocuments()
    {
        var documents = new List<WorkspaceSearchDocument>();
        foreach (var document in _host.Documents)
        {
            if (document.FilePath is { } path)
            {
                documents.Add(new WorkspaceSearchDocument(path, document.Document.Buffer.Current));
            }
        }

        return documents;
    }

    // The UI thread: every file found since the last turn joins the results, in path order.
    private void Flush()
    {
        Volatile.Write(ref _flushPending, 0);
        string? root = _root();
        bool any = false;
        while (_arrived.TryDequeue(out var arrived))
        {
            if (arrived.Generation != _generation || root is null)
            {
                continue;
            }

            var file = arrived.File;
            var result = new SearchFileResult(file.Path, WorkspaceFiles.Relative(root, file.Path), [.. file.Hits], file.FromOpenDocument);
            int at = _results.BinarySearch(result, ByPath.Instance);
            _results.Insert(at < 0 ? ~at : at, result);
            any = true;
        }

        if (any)
        {
            _version++;
            Publish();
        }
    }

    private void Finish(int generation, WorkspaceSearchSummary? summary, string? problem, bool cancelled = false)
    {
        if (generation != _generation)
        {
            return;
        }

        Flush();
        if (cancelled)
        {
            _searching = false;
            _note = "Search cancelled.";
            Publish();
            return;
        }

        _searching = false;
        _summary = summary;
        _problem = problem;
        Publish();
    }

    // ---- results ----

    private (SearchFileResult? File, WorkspaceSearchHit? Hit) Row(string rowId)
    {
        int cut = rowId.LastIndexOf(Separator);
        if (cut < 0 || !int.TryParse(rowId.AsSpan(cut + 1), NumberStyles.None, Culture, out int index))
        {
            return (null, null);
        }

        var path = rowId[..cut];
        var file = _results.FirstOrDefault(r => r.Path == path);
        return file is null || index < 0 || index >= file.Hits.Count ? (null, null) : (file, file.Hits[index]);
    }

    private void Dismiss(string rowId)
    {
        if (Row(rowId) is (SearchFileResult file, WorkspaceSearchHit hit))
        {
            file.Hits.Remove(hit);
            if (file.Hits.Count == 0)
            {
                _results.Remove(file);
            }

            Changed2();
        }
    }

    // ---- replacing ----

    // Replace in every file listed (path null), in one file, or one match: one review across the files, then Undo.
    private async Task ReplaceAsync(string? path, WorkspaceSearchHit? only, CancellationToken cancellationToken)
    {
        var options = _options();
        var pattern = CurrentQuery().Pattern(TimeSpan.FromMilliseconds(options.FindRegexTimeoutMilliseconds));
        if (!pattern.Searchable || _results.Count == 0)
        {
            return;
        }

        string replacement = _replace;
        var files = path is null ? [.. _results] : _results.Where(r => r.Path == path).ToList();
        var changes = new List<FileChange>(files.Count);
        foreach (var file in files)
        {
            // The matches listed for the file (a dismissed one is not replaced), found again in the text the host reads.
            var listed = new HashSet<(int Line, int Column)>(only is { } one ? [(one.Line, one.Column)] : file.Hits.Select(h => (h.Line, h.Column)));
            changes.Add(new FileChange(file.Path, snapshot => ReplaceEdits(snapshot, pattern, replacement, listed)));
        }

        int count = only is not null ? 1 : files.Sum(f => f.Hits.Count);
        string title = string.Create(Culture, $"Replace '{Shorten(_query, 30)}' with '{Shorten(replacement, 30)}' ({count} {(count == 1 ? "match" : "matches")} in {files.Count} {(files.Count == 1 ? "file" : "files")})");
        var result = await _host.ProposeFilesAsync(changes, title, cancellationToken).ConfigureAwait(true);
        if (result is null || result.Files == 0)
        {
            if (result is { Skipped: > 0 })
            {
                _note = string.Create(Culture, $"{result.Skipped} {(result.Skipped == 1 ? "file" : "files")} changed while you were asked and {(result.Skipped == 1 ? "was" : "were")} left as {(result.Skipped == 1 ? "it was" : "they were")}.");
                Publish();
            }

            return;
        }

        string said = string.Create(Culture, $"Replaced {result.Edits} {(result.Edits == 1 ? "match" : "matches")} in {result.Files} {(result.Files == 1 ? "file" : "files")}.")
            + (result.Skipped > 0 ? string.Create(Culture, $" {result.Skipped} changed meanwhile and were left.") : "");
        if (!_host.ShowUndo(said, () => _ = UndoAsync(result), result.Keep))
        {
            result.Keep();
        }

        _note = said;
        Schedule(immediately: true);
    }

    private async Task UndoAsync(FilesEditResult result)
    {
        int restored = await result.UndoAsync().ConfigureAwait(true);
        _note = string.Create(Culture, $"Undone: {restored} {(restored == 1 ? "file" : "files")} as they were.");
        Schedule(immediately: true);
    }

    /// <summary>
    /// The edits that replace <paramref name="pattern"/>'s matches in <paramref name="snapshot"/> with
    /// <paramref name="replacement"/> (a regular expression's groups put in), only those at a (line, column) in
    /// <paramref name="listed"/> when it is given. Any thread; each line read through a pooled buffer.
    /// </summary>
    public static IReadOnlyList<TextEdit> ReplaceEdits(ITextSnapshot snapshot, TextPattern pattern, string replacement, IReadOnlySet<(int Line, int Column)>? listed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);
        var edits = new List<TextEdit>();
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                var line = snapshot.GetLine(index);
                if (line.Length > buffer.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                var text = buffer.AsSpan(0, line.Length);
                snapshot.CopyTo(line.Start, text);
                int from = 0;
                int found;
                while (from <= text.Length && (found = pattern.NextIn(text, from, out int length)) >= 0)
                {
                    if (listed is null || listed.Contains((index, found)))
                    {
                        edits.Add(new TextEdit(line.Start + found, length, pattern.Replacement(text, found, replacement)));
                    }

                    from = found + length;
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return edits;
    }

    // ---- blocks ----

    private void Changed2()
    {
        _version++;
        Publish();
    }

    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        _blocks = Build();
        _items = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<PanelBlock> Build()
    {
        var options = _options();
        var blocks = new List<PanelBlock>();
        var providers = WorkspaceSearchProviders.All;
        if (providers.Count > 1)
        {
            var current = Provider();
            blocks.Add(new PanelPicker("search.provider", "Search with", [.. providers.Select(p => new PanelOption(p.Name, p.Title) { Detail = p.Summary })], current.Name)
            {
                Variable = "CRAFT_SEARCH_PROVIDER",
            });
        }

        string? keys(string command) => _host.KeysOf(command);
        blocks.Add(new PanelQuery(QueryAction, "Search", _query)
        {
            Toggles =
            [
                new PanelToggle("search.case", "Aa", "Match case", _matchCase) { Keys = "Alt+C" },
                new PanelToggle("search.word", "ab", "Match whole word", _wholeWord) { Keys = "Alt+W" },
                new PanelToggle("search.regex", ".*", "Use regular expression", _regex) { Keys = "Alt+R" },
            ],
            Buttons =
            [
                new PanelButton(_showReplace ? "Hide replace" : "Show replace", "search.showReplace", PanelButtonKind.Flat) { Icon = RailIcons.Replace, Tooltip = "Replace in files" + (keys(SearchCommands.Replace) is { } r ? $" ({r})" : "") },
                new PanelButton(_showFilters ? "Hide the files to include and exclude" : "Files to include and exclude", "search.showFilters", PanelButtonKind.Flat) { Icon = "M4 6h16M7 12h10M10 18h4" },
            ],
            SubmitActionId = "search.now",
            Problem = _problem is not null && _query.Length > 0 && _results.Count == 0 && !_searching ? _problem : null,
            Focus = _focus,
            Tooltip = "Search the workspace's files" + (keys(SearchCommands.Show) is { } k ? $" ({k})" : ""),
        });
        if (_showReplace)
        {
            blocks.Add(new PanelQuery(ReplaceAction, "Replace", _replace)
            {
                Buttons =
                [
                    new PanelButton("Replace all", ReplaceAllAction, PanelButtonKind.Primary)
                    {
                        Icon = RailIcons.Replace,
                        Enabled = _results.Count > 0 && !_searching,
                        Tooltip = "Replace all: one review of every file's changes, then Undo",
                    },
                ],
                SubmitActionId = ReplaceAllAction,
                Tooltip = _regex ? "The replacement: $1 or ${name} puts a group in, $$ a dollar sign" : "The replacement",
            });
        }

        if (_showFilters)
        {
            blocks.Add(new PanelQuery(IncludeAction, "Files to include (*.cs, src/**)", _include) { Code = true, Tooltip = "Globs separated by commas; empty: every file" });
            blocks.Add(new PanelQuery(ExcludeAction, "Files to exclude", _exclude)
            {
                Code = true,
                Toggles = [new PanelToggle("search.ignoreFiles", "⊘", "Leave out what .gitignore ignores", _useIgnoreFiles)],
                Tooltip = "Globs separated by commas; the ignored folders (" + string.Join(", ", options.WorkspaceIgnore) + ") are always left out",
                Variable = "CRAFT_WORKSPACE_IGNORE",
            });
        }

        blocks.Add(Status(options));
        if (_results.Count > 0)
        {
            blocks.Add(new PanelButtons(
            [
                new PanelButton(_results.All(r => _collapsed.Contains(r.Path)) ? "Expand all" : "Collapse all", "search.collapseAll", PanelButtonKind.Flat) { Icon = RailIcons.Collapse },
                new PanelButton("Refresh", "search.refresh", PanelButtonKind.Flat) { Icon = RailIcons.Refresh },
                new PanelButton("Clear", "search.clear", PanelButtonKind.Flat) { Icon = RailIcons.Close },
            ]));
        }

        blocks.Add(new PanelResults("search.results", Groups())
        {
            Version = _version,
            Empty = _query.Length == 0 ? "Type to search the workspace's files." : _searching ? null : "No results. Check the query, its toggles and the files to include and exclude.",
        });
        return blocks;
    }

    private PanelText Status(CraftOptions options)
    {
        int matches = MatchCount;
        if (_problem is not null)
        {
            return new PanelText(_problem, PanelTextStyle.Secondary) { Tone = PanelTone.Error };
        }

        if (_searching)
        {
            return new PanelText(string.Create(Culture, $"Searching… {matches} {(matches == 1 ? "result" : "results")} in {_results.Count} {(_results.Count == 1 ? "file" : "files")} so far"), PanelTextStyle.Secondary)
            {
                Tone = PanelTone.Accent,
            };
        }

        if (_summary is not { } summary)
        {
            return new PanelText(_note ?? (_root() is null ? "Open a folder to search its files." : "Search every file of the workspace; open files in their current text."), PanelTextStyle.Secondary);
        }

        var text = string.Create(Culture, $"{matches} {(matches == 1 ? "result" : "results")} in {_results.Count} {(_results.Count == 1 ? "file" : "files")}");
        var more = new List<string>();
        if (summary.Capped)
        {
            more.Add(string.Create(Culture, $"stopped at {options.SearchMaxResults} (CRAFT_SEARCH_MAX_RESULTS): narrow the search"));
        }

        if (summary.SkippedLarge > 0)
        {
            more.Add(string.Create(Culture, $"{summary.SkippedLarge} over {options.SearchMaxFileMegabytes} MB passed over (CRAFT_SEARCH_MAX_FILE_MB)"));
        }

        if (summary.TimedOut)
        {
            more.Add("the expression ran out of time on some lines (CRAFT_FIND_REGEX_TIMEOUT_MS)");
        }

        if (_note is not null)
        {
            more.Insert(0, _note);
        }

        var tone = summary.Capped || summary.TimedOut ? PanelTone.Warning : PanelTone.Normal;
        return new PanelText(more.Count == 0 ? text : text + " · " + string.Join(" · ", more), PanelTextStyle.Secondary)
        {
            Tone = tone,
            Tooltip = string.Create(Culture, $"{summary.FilesSearched} files searched in {summary.Elapsed.TotalMilliseconds:0} ms")
                + (summary.FirstResult is { } first ? string.Create(Culture, $", the first result after {first.TotalMilliseconds:0} ms") : "")
                + (summary.SkippedBinary > 0 ? string.Create(Culture, $", {summary.SkippedBinary} binary files passed over") : ""),
        };
    }

    private List<PanelResultGroup> Groups()
    {
        var groups = new List<PanelResultGroup>(_results.Count);
        foreach (var file in _results)
        {
            var icon = FileIcons.For(file.Path);
            int slash = file.Relative.LastIndexOf('/');
            var rows = new List<PanelResultRow>(file.Hits.Count);
            bool collapsed = _collapsed.Contains(file.Path);
            if (!collapsed)
            {
                for (int i = 0; i < file.Hits.Count; i++)
                {
                    rows.Add(Row(file, i));
                }
            }

            groups.Add(new PanelResultGroup(file.Path, slash < 0 ? file.Relative : file.Relative[(slash + 1)..], rows)
            {
                Detail = slash < 0 ? null : file.Relative[..slash],
                Badge = file.Hits.Count.ToString(Culture),
                Glyph = icon.Badge,
                GlyphArgb = icon.Argb,
                Collapsed = collapsed,
                ToggleActionId = ToggleAction,
                Tooltip = file.Relative + (file.FromOpenDocument ? " (the open document's text)" : ""),
                Buttons = _showReplace
                    ? [new PanelButton("Replace in this file", "search.replaceFile", PanelButtonKind.Flat) { Icon = RailIcons.Replace, Value = file.Path }, Dismiss("Dismiss the file", "search.dismissFile", file.Path)]
                    : [Dismiss("Dismiss the file", "search.dismissFile", file.Path)],
            });
        }

        return groups;
    }

    private PanelResultRow Row(SearchFileResult file, int index)
    {
        var hit = file.Hits[index];
        // The line's indentation is not shown: the match keeps its place in what is.
        var preview = hit.Preview.AsSpan();
        int indent = preview.Length - preview.TrimStart().Length;
        indent = Math.Min(indent, hit.PreviewColumn);
        string id = RowId(file.Path, index);
        var span = new PanelSpan(hit.PreviewColumn - indent, hit.Length) { Replacement = _showReplace ? ReplacementOf(hit) : null };
        return new PanelResultRow(id, hit.Preview[indent..])
        {
            Prefix = (hit.Line + 1).ToString(Culture),
            Highlights = [span],
            Code = true,
            ActionId = OpenAction,
            Value = id,
            Buttons = _showReplace
                ? [new PanelButton("Replace", "search.replaceOne", PanelButtonKind.Flat) { Icon = RailIcons.Replace, Value = id }, Dismiss("Dismiss", "search.dismiss", id)]
                : [Dismiss("Dismiss", "search.dismiss", id)],
        };
    }

    private string ReplacementOf(WorkspaceSearchHit hit)
    {
        if (!_regex || _replace.AsSpan().IndexOf('$') < 0)
        {
            return _replace;
        }

        var pattern = CurrentQuery().Pattern(TimeSpan.FromMilliseconds(_options().FindRegexTimeoutMilliseconds));
        return pattern.Replacement(hit.Preview, hit.PreviewColumn, _replace);
    }

    private static PanelButton Dismiss(string title, string action, string value) => new(title, action, PanelButtonKind.Flat) { Icon = RailIcons.Close, Value = value };

    private static string Shorten(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private sealed class ByPath : IComparer<SearchFileResult>
    {
        public static ByPath Instance { get; } = new();

        public int Compare(SearchFileResult? x, SearchFileResult? y)
        {
            int order = string.Compare(x?.Relative, y?.Relative, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(x?.Relative, y?.Relative);
        }
    }
}

/// <summary>The Search view's commands (X part 6).</summary>
public static class SearchCommands
{
    /// <summary>"Search: Find in files" (Ctrl+Shift+F): the Search view with the keyboard in its query.</summary>
    public const string Show = "craft.search.show";

    /// <summary>"Search: Replace in files" (Ctrl+Shift+H): the Search view with its replace field.</summary>
    public const string Replace = "craft.search.replaceInFiles";
}
