// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;

namespace Craft.Editor.Tools;

/// <summary>A tag found in a comment: its file, its place, its tag and the comment's words after it.</summary>
/// <param name="File">The file's full path.</param>
/// <param name="Line">Its line, from 0.</param>
/// <param name="Column">The tag's column, from 0.</param>
/// <param name="Tag">The tag ("TODO").</param>
/// <param name="Text">What follows it in the comment (its ':' and the comment's closing left out).</param>
public sealed record TodoEntry(string File, int Line, int Column, string Tag, string Text);

/// <summary>
/// Finds the TODO list's tags (<c>CRAFT_TODO_TAGS</c>: TODO, FIXME, HACK, NOTE) in a file: as whole words with their case,
/// in comments only where the file has a language (its session's classification of the lines that hold a tag), on any
/// line of a plain text file. A file is read as bytes and passed over without being decoded when none of the tags' bytes
/// are in it (rule 74), or when it is binary (a zero byte in its first 8,000) or larger than the search's limit.
/// </summary>
public static class TodoScanner
{
    /// <summary>The entries of the file <paramref name="path"/> read from disk (none when it cannot be read, is binary or too large).</summary>
    public static List<TodoEntry> ScanFile(string path, IReadOnlyList<string> tags, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(tags);
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maxBytes)
            {
                return [];
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var span = bytes.AsSpan();
        if (span[..Math.Min(span.Length, 8000)].IndexOf((byte)0) >= 0 || !HoldsTag(span, tags))
        {
            return [];
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        return Scan(path, Encoding.UTF8.GetString(span), EditorLanguages.ForFile(path), tags, cancellationToken);
    }

    /// <summary>Whether <paramref name="bytes"/> (UTF-8) hold one of <paramref name="tags"/> anywhere.</summary>
    public static bool HoldsTag(ReadOnlySpan<byte> bytes, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Span<byte> tag = stackalloc byte[64];
        foreach (var name in tags)
        {
            if (name.Length > 0 && name.Length <= tag.Length && Encoding.UTF8.TryGetBytes(name, tag, out int written) && bytes.IndexOf(tag[..written]) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The entries of <paramref name="text"/> (the file <paramref name="path"/>): in its comments when
    /// <paramref name="language"/> is given (classified line by line where a tag stands), else on any line.
    /// </summary>
    public static List<TodoEntry> Scan(string path, string text, ILanguage? language, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tags);
        var entries = new List<TodoEntry>();
        if (tags.Count == 0)
        {
            return entries;
        }

        var search = SearchValues.Create([.. tags], StringComparison.Ordinal);
        var all = text.AsSpan();
        int at = all.IndexOfAny(search);
        if (at < 0)
        {
            return entries;
        }

        var buffer = new PieceTableBuffer(text);
        var snapshot = buffer.Current;
        ILanguageSession? session = null;
        using var spans = new TokenSpanBuffer();
        try
        {
            if (language is not null)
            {
                session = language.Open(buffer);
                session.WaitUntilClassified(snapshot, TimeSpan.FromSeconds(10));
            }

            int classifiedLine = -1;
            while (at >= 0 && at < all.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? tag = TagAt(all, at, tags);
                int length = tag?.Length ?? 1;
                if (tag is not null && IsWordEdge(all, at - 1) && IsWordEdge(all, at + tag.Length))
                {
                    int lineIndex = snapshot.GetLineIndex(at);
                    var line = snapshot.GetLine(lineIndex);
                    int end = line.End;
                    bool found = true;
                    if (session is not null)
                    {
                        if (classifiedLine != lineIndex)
                        {
                            spans.Clear();
                            session.Classify(snapshot, line.Start, line.Length, spans);
                            classifiedLine = lineIndex;
                        }

                        found = false;
                        foreach (var span in spans.Spans)
                        {
                            if (span.Start <= at && span.End >= at + tag.Length && IsComment(span.Class))
                            {
                                found = true;
                                end = Math.Min(end, span.End);
                                break;
                            }
                        }
                    }

                    if (found)
                    {
                        entries.Add(new TodoEntry(path, lineIndex, at - line.Start, tag, After(all[(at + tag.Length)..end])));
                    }
                }

                int next = all[(at + length)..].IndexOfAny(search);
                at = next < 0 ? -1 : at + length + next;
            }
        }
        finally
        {
            session?.Dispose();
        }

        return entries;
    }

    /// <summary>Whether a span of <paramref name="tokenClass"/> is a comment (a documentation comment's text too).</summary>
    public static bool IsComment(string tokenClass)
    {
        ArgumentNullException.ThrowIfNull(tokenClass);
        return tokenClass == TokenClasses.Comment || tokenClass == TokenClasses.DocComment || tokenClass.Contains("comment", StringComparison.Ordinal);
    }

    private static string? TagAt(ReadOnlySpan<char> text, int at, IReadOnlyList<string> tags)
    {
        string? longest = null;
        foreach (var tag in tags)
        {
            if (text[at..].StartsWith(tag, StringComparison.Ordinal) && (longest is null || tag.Length > longest.Length))
            {
                longest = tag;
            }
        }

        return longest;
    }

    private static bool IsWordEdge(ReadOnlySpan<char> text, int index) =>
        index < 0 || index >= text.Length || !(char.IsLetterOrDigit(text[index]) || text[index] == '_');

    // The comment's words after its tag: "(name)", ':' and '-' before them, and a block comment's close after, left out.
    private static string After(ReadOnlySpan<char> rest)
    {
        rest = rest.Trim();
        if (rest.StartsWith('('))
        {
            int close = rest.IndexOf(')');
            if (close > 0)
            {
                rest = rest[(close + 1)..].TrimStart();
            }
        }

        rest = rest.TrimStart(":-– ").TrimEnd();
        if (rest.EndsWith("*/", StringComparison.Ordinal))
        {
            rest = rest[..^2].TrimEnd();
        }
        else if (rest.EndsWith("-->", StringComparison.Ordinal))
        {
            rest = rest[..^3].TrimEnd();
        }

        return rest.Length <= 300 ? rest.ToString() : string.Concat(rest[..299], "…");
    }
}

/// <summary>
/// The TODO list (X part 9, item 21), a tab of the bottom panel: the tags (<c>CRAFT_TODO_TAGS</c>) in the workspace's
/// comments, found off the UI thread (the files in parallel, an activity with Cancel), grouped by file, refreshed for each
/// saved file (that file read again alone) and in full with Refresh or another folder; a click opens the place. Headless;
/// every member is called on the UI thread.
/// </summary>
public sealed class TodoListView : IEditorViewPanel, IDisposable
{
    /// <summary>Its panel id.</summary>
    public const string PanelId = "craft.todo";

    /// <summary>A checked box (the tab's icon).</summary>
    public const string Icon = "M5 5h14v14H5zM8.5 12l2.5 2.5L16 9.5";

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private readonly EditorHost _host;
    private readonly Func<string?> _root;
    private readonly Func<CraftOptions> _options;
    private readonly SortedDictionary<string, List<TodoEntry>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private CancellationTokenSource? _scan;
    private TaskCompletionSource _scanned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _filter = "";
    private bool _scanning;
    private bool _capped;
    private int _filesRead;
    private double _lastMilliseconds;
    private int _version;
    private bool _disposed;

    /// <summary>The list over <paramref name="host"/> for the folder <paramref name="root"/> gives, with <paramref name="options"/>.</summary>
    public TodoListView(EditorHost host, Func<string?> root, Func<CraftOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host.DocumentSaved += OnDocumentSaved;
        Publish();
    }

    /// <inheritdoc />
    public string Id => PanelId;

    /// <inheritdoc />
    public string Title => "TODO";

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public string? Badge => Count > 0 ? Count.ToString(Culture) : null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>The entries by file (full path), the files in path order.</summary>
    public IReadOnlyDictionary<string, List<TodoEntry>> Files => _files;

    /// <summary>How many entries it holds.</summary>
    public int Count => _files.Values.Sum(f => f.Count);

    /// <summary>Completes when the scan asked last is in.</summary>
    public Task Scanned => _scanned.Task;

    /// <summary>The last full scan's time (milliseconds) and the files it read.</summary>
    public (double Milliseconds, int FilesRead) LastScan => (_lastMilliseconds, _filesRead);

    /// <summary>The workspace changed: it is scanned again.</summary>
    public void WorkspaceChanged() => _ = RefreshAsync();

    /// <summary>Scans the workspace again (off the UI thread, the files in parallel); completes when the entries are in.</summary>
    public Task RefreshAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var previous = _scan;
        var cancel = _scan = new CancellationTokenSource();
        if (previous is not null)
        {
            _ = previous.CancelAsync();
        }

        if (_scanned.Task.IsCompleted)
        {
            _scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var done = _scanned;
        if (_root() is not { } root)
        {
            _files.Clear();
            _scanning = false;
            Changed2();
            done.TrySetResult();
            return done.Task;
        }

        _scanning = true;
        Publish();
        var options = _options();
        var tags = options.TodoTags;
        int max = options.TodoMaxResults;
        long maxBytes = (long)options.SearchMaxFileMegabytes * 1024 * 1024;
        var activity = _host.Activities.Start("TODO list: scanning the workspace", ActivityKinds.Search, cancellable: true, cancel.Token, keep: false);
        _ = Task.Run(() =>
        {
            var found = new ConcurrentDictionary<string, List<TodoEntry>>(StringComparer.OrdinalIgnoreCase);
            int read = 0;
            int total = 0;
            var clock = Stopwatch.StartNew();
            try
            {
                var files = WorkspaceFiles.Enumerate(root, options.WorkspaceIgnore, useIgnoreFiles: true, null, null, activity.CancellationToken).ToList();
                Parallel.ForEach(files, new ParallelOptions { CancellationToken = activity.CancellationToken }, (file, state) =>
                {
                    var entries = TodoScanner.ScanFile(file, tags, maxBytes, activity.CancellationToken);
                    int count = Interlocked.Increment(ref read);
                    if ((count & 63) == 0)
                    {
                        activity.Report((double)count / Math.Max(1, files.Count), Path.GetFileName(file));
                    }

                    if (entries.Count > 0)
                    {
                        found[file] = entries;
                        if (Interlocked.Add(ref total, entries.Count) >= max)
                        {
                            state.Stop();
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                activity.Complete("cancelled");
                activity.Dispose();
                return;
            }

            double elapsed = clock.Elapsed.TotalMilliseconds;
            activity.Complete(string.Create(Culture, $"{total} in {found.Count} files"));
            activity.Dispose();
            _host.Dispatch("todo: the workspace's entries", () =>
            {
                if (cancel.IsCancellationRequested || _disposed)
                {
                    done.TrySetResult();
                    return;
                }

                _files.Clear();
                foreach (var (file, entries) in found)
                {
                    _files[file] = entries;
                }

                _capped = total >= max;
                _filesRead = read;
                _lastMilliseconds = elapsed;
                _scanning = false;
                _host.Record("todo.scan", elapsed, "ms");
                Changed2();
                done.TrySetResult();
            });
        }, CancellationToken.None);
        return done.Task;
    }

    /// <summary>Reads the file <paramref name="path"/> again (off the UI thread) and replaces its entries; completes when they are in.</summary>
    public async Task RescanFileAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_root() is not { } root || !IsUnder(root, path))
        {
            return;
        }

        var options = _options();
        string full = Path.GetFullPath(path);
        var entries = await Task.Run(() => TodoScanner.ScanFile(full, options.TodoTags, (long)options.SearchMaxFileMegabytes * 1024 * 1024)).ConfigureAwait(true);
        if (_disposed)
        {
            return;
        }

        if (entries.Count > 0)
        {
            _files[full] = entries;
        }
        else if (!_files.Remove(full))
        {
            return;
        }

        Changed2();
    }

    /// <inheritdoc />
    public async Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        switch (actionId)
        {
            case "todo.refresh":
                await RefreshAsync().ConfigureAwait(true);
                break;
            case "todo.filter":
                _filter = value ?? "";
                Changed2();
                break;
            case "todo.collapseAll":
                foreach (var file in _files.Keys)
                {
                    _collapsed.Add(file);
                }

                Changed2();
                break;
            case "todo.toggle" when value is not null:
                if (!_collapsed.Remove(value))
                {
                    _collapsed.Add(value);
                }

                Changed2();
                break;
            case "todo.open" when value is not null:
                int bar = value.LastIndexOf('|');
                int colon = bar > 0 ? value.LastIndexOf('|', bar - 1) : -1;
                if (colon > 0 && int.TryParse(value.AsSpan(colon + 1, bar - colon - 1), NumberStyles.None, Culture, out int line)
                    && int.TryParse(value.AsSpan(bar + 1), NumberStyles.None, Culture, out int column)
                    && await _host.OpenDocumentAsync(value[..colon], cancellationToken).ConfigureAwait(true) is { } document)
                {
                    var snapshot = document.Document.Buffer.Current;
                    var at = snapshot.GetLine(Math.Clamp(line, 0, snapshot.LineCount - 1));
                    _host.Select(document, SelectionState.At(at.Start + Math.Min(column, at.Length)));
                }

                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.DocumentSaved -= OnDocumentSaved;
        _scan?.Cancel();
        _scan?.Dispose();
        _scanned.TrySetResult();
    }

    private void OnDocumentSaved(object? sender, EditorDocumentEventArgs e)
    {
        if (e.Document.FilePath is { } path)
        {
            _ = RescanFileAsync(path);
        }
    }

    private static bool IsUnder(string root, string file)
    {
        var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

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
        int count = Count;
        string status = _root() is null ? "Open a folder to list its TODO comments."
            : _scanning && count == 0 ? "Scanning the workspace's comments…"
            : count == 0 ? $"No {string.Join(", ", options.TodoTags)} in this workspace's comments."
            : string.Create(Culture, $"{count} in {_files.Count} {(_files.Count == 1 ? "file" : "files")}{(_capped ? $" (stopped at {options.TodoMaxResults}: CRAFT_TODO_MAX_RESULTS)" : "")}{(_scanning ? " · scanning…" : "")}");
        var blocks = new List<PanelBlock>
        {
            new PanelQuery("todo.filter", "Filter by text, tag or file", _filter)
            {
                Buttons =
                [
                    new PanelButton("Refresh", "todo.refresh", PanelButtonKind.Flat) { Icon = RailIcons.Refresh, Tooltip = "Scan the workspace again" },
                    new PanelButton("Collapse", "todo.collapseAll", PanelButtonKind.Flat) { Icon = RailIcons.Collapse, Tooltip = "Fold every file" },
                ],
            },
            new PanelText(status, PanelTextStyle.Caption)
            {
                Tone = _scanning ? PanelTone.Accent : PanelTone.Normal,
                Tooltip = string.Create(Culture, $"The words: {string.Join(", ", options.TodoTags)} (CRAFT_TODO_TAGS); the last scan read {_filesRead} files in {_lastMilliseconds:0} ms"),
                Variable = "CRAFT_TODO_TAGS",
            },
        };
        var root = _root();
        string filter = _filter.Trim();
        var groups = new List<PanelResultGroup>();
        foreach (var (file, entries) in _files)
        {
            string relative = root is null ? file : WorkspaceFiles.Relative(root, file);
            var rows = new List<PanelResultRow>();
            foreach (var entry in entries)
            {
                if (filter.Length > 0 && !entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) && !entry.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !relative.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = entry.Text.Length == 0 ? entry.Tag : entry.Tag + ": " + entry.Text;
                rows.Add(new PanelResultRow(string.Create(Culture, $"{file}|{entry.Line}|{entry.Column}"), text)
                {
                    Prefix = (entry.Line + 1).ToString(Culture),
                    Highlights = [new PanelSpan(0, entry.Tag.Length)],
                    Tone = ToneOf(entry.Tag),
                    ActionId = "todo.open",
                    Value = string.Create(Culture, $"{file}|{entry.Line}|{entry.Column}"),
                });
            }

            if (rows.Count == 0)
            {
                continue;
            }

            var icon = FileIcons.For(file);
            int slash = relative.LastIndexOf('/');
            groups.Add(new PanelResultGroup(file, Path.GetFileName(file), rows)
            {
                Detail = slash > 0 ? relative[..slash] : null,
                Badge = rows.Count.ToString(Culture),
                Glyph = icon.Badge,
                GlyphArgb = icon.Argb,
                Collapsed = _collapsed.Contains(file),
                ToggleActionId = "todo.toggle",
                Tooltip = file,
            });
        }

        blocks.Add(new PanelResults("todo.entries", groups)
        {
            Version = _version,
            Empty = root is null ? "No folder is open." : _scanning ? null : filter.Length > 0 ? "Nothing matches the filter." : "No entries.",
        });
        return blocks;
    }

    private static PanelTone ToneOf(string tag) => tag switch
    {
        "FIXME" => PanelTone.Error,
        "HACK" => PanelTone.Warning,
        "TODO" => PanelTone.Accent,
        _ => PanelTone.Normal,
    };
}
