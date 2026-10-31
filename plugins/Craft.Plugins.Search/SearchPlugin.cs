// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Plugins.Search;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Text.Abstractions;

[assembly: IdrakPluginPackage(typeof(SearchPackage))]

namespace Craft.Plugins.Search;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Search\.</summary>
public sealed class SearchPackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Search";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new SearchRegistration()];
}

/// <summary>
/// Registers "code-bm25" in Idrak.Text's <see cref="KeywordIndexes"/>, the tool set "search" (search_code) in
/// <see cref="EditorToolSets"/>, the editor plug-in "search", its command and key, each with how to take it back.
/// </summary>
public sealed class SearchRegistration() : IdrakPlugin(SearchSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => KeywordIndexes.Register(CodeKeywordIndexKind.Instance), () => KeywordIndexes.Unregister(CodeKeywordIndexKind.IndexName));
        Part(() => EditorToolSets.Register(SearchToolSet.Instance), () => EditorToolSets.Unregister(SearchToolSet.SetName));
        Part(() => EditorPlugins.Register(SearchPlugin.Instance), () => EditorPlugins.Unregister(SearchSettings.PluginName));
        foreach (var command in SearchPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        Part(() => EditorKeyBindings.Register(SearchPlugin.Binding), () => EditorKeyBindings.Unregister(SearchPlugin.Binding.Chord));
    }
}

/// <summary>
/// The editor plug-in "search": while active, the open folder's index (loaded from disk, then brought up to date off the UI
/// thread, then kept up to date as files change), the "Code search" side view of the rail's Search, and search_code.
/// </summary>
public sealed class SearchPlugin : IEditorPlugin
{
    /// <summary>Shows the Code search view (the rail's Search; again: the explorer).</summary>
    public const string ShowCommand = "search.show";

    /// <summary>Builds the index again from the files.</summary>
    public const string ReindexCommand = "search.reindex";

    private static SearchSession? s_current;

    private SearchPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static SearchPlugin Instance { get; } = new();

    /// <summary>Its commands.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        new EditorCommand(ShowCommand, "Search: Code search", (host, _) => { host.ShowPanel(SearchPanel.PanelId); return Task.CompletedTask; }, host => host.WorkspaceFolder is not null),
        new EditorCommand(ReindexCommand, "Search: Index the code again", (_, token) => Current?.ReindexAsync(full: true, token) ?? Task.CompletedTask, _ => Current is not null),
    ];

    /// <summary>Ctrl+Shift+F shows the Code search view.</summary>
    public static KeyBinding Binding { get; } = new(KeyChord.Parse("Ctrl+Shift+F"), ShowCommand);

    /// <summary>The session of the host the plug-in is active in, or null.</summary>
    internal static SearchSession? Current => Volatile.Read(ref s_current);

    /// <inheritdoc />
    public string Name => SearchSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Code search: Roslyn-shaped chunks, camelCase-split identifiers, BM25 and vectors; the Search view and search_code";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => SearchSettings.Declared;

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new SearchSession(host, SearchSettings.From(host.Settings));
        Volatile.Write(ref s_current, session);
        session.Closed += (_, _) => Interlocked.CompareExchange(ref s_current, null, session);
        return session;
    }
}

/// <summary>
/// The plug-in in one host. Everything that reads files (the index's load, its builds, the watcher's events) runs off the
/// UI thread; what the user sees (the side view, the progress) is reached through <see cref="IEditorHost.Dispatch"/>.
/// </summary>
internal sealed class SearchSession : IDisposable
{
    private readonly IEditorHost _host;
    private readonly SearchSettings _settings;
    private readonly SearchPanel _panel;
    private readonly Lock _gate = new();
    private readonly Timer _reindex;
    private CodeIndex? _index;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource _stop = new();
    private Task _building = Task.CompletedTask;
    private string? _folder;
    private bool _disposed;

    public SearchSession(IEditorHost host, SearchSettings settings)
    {
        _host = host;
        _settings = settings;
        _panel = new SearchPanel(this, host, settings);
        _reindex = new Timer(_ => _ = ReindexAsync(full: false, CancellationToken.None), null, Timeout.Infinite, Timeout.Infinite);
        EditorSideViews.Register(_panel);
        host.ActiveDocumentChanged += OnActiveChanged;
        host.DocumentSaved += OnSaved;
        Follow();
    }

    public event EventHandler? Closed;

    /// <summary>The open folder's index, or null.</summary>
    public CodeIndex? Index
    {
        get
        {
            lock (_gate)
            {
                return _index;
            }
        }
    }

    /// <summary>The build running or last run (the tests wait for it).</summary>
    public Task Building
    {
        get
        {
            lock (_gate)
            {
                return _building;
            }
        }
    }

    /// <summary>Brings the index up to date (or, with <paramref name="full"/>, from no index); its progress in the status bar.</summary>
    public Task ReindexAsync(bool full, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_index is not { } index || _disposed)
            {
                return Task.CompletedTask;
            }

            var token = _stop.Token;
            var previous = _building;
            _building = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await BuildAsync(index, full, token).ConfigureAwait(false);
            }, CancellationToken.None);
            return _building.WaitAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _host.ActiveDocumentChanged -= OnActiveChanged;
        _host.DocumentSaved -= OnSaved;
        EditorSideViews.Unregister(SearchPanel.PanelId);
        _reindex.Dispose();
        Close();
        _panel.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnActiveChanged(object? sender, EventArgs e) => Follow();

    private void OnSaved(object? sender, EditorDocumentEventArgs e) => Later();

    // The editor's folder, when it changed: its index loaded from disk and brought up to date, its files watched.
    private void Follow()
    {
        string? folder = _host.WorkspaceFolder;
        if (string.Equals(folder, _folder, StringComparison.Ordinal))
        {
            return;
        }

        _folder = folder;
        Close();
        if (folder is null)
        {
            _panel.Reset("Open a folder to search its code.");
            return;
        }

        var index = new CodeIndex(folder, _settings, CodeIndex.FileFor(IndexFolder(), folder));
        lock (_gate)
        {
            _index = index;
            _stop = new CancellationTokenSource();
        }

        _panel.Reset(null);
        if (_settings.Watch)
        {
            Watch(folder);
        }

        var token = _stop.Token;
        lock (_gate)
        {
            _building = Task.Run(async () =>
            {
                bool loaded = index.Load();                 // the index kept on disk first: searchable at once
                _host.Dispatch(() => _panel.Refresh(loaded ? $"{index.ChunkCount:N0} chunks from the kept index; checking for changes" : null));
                await BuildAsync(index, full: false, token).ConfigureAwait(false);
            }, CancellationToken.None);
        }
    }

    private async Task BuildAsync(CodeIndex index, bool full, CancellationToken token)
    {
        IEditorProgress? progress = null;
        _host.Dispatch(() => progress = _host.StartProgress("Indexing the code"));
        try
        {
            if (full && index.IndexFile is { } file && File.Exists(file))
            {
                File.Delete(file);
            }

            var reporter = new Progress<(int Done, int Total)>(p => _host.Dispatch(() =>
                progress?.Report(p.Total == 0 ? 1 : p.Done / (double)p.Total, string.Create(CultureInfo.InvariantCulture, $"{p.Done:N0} of {p.Total:N0} files parsed"))));
            var build = await index.BuildAsync(reporter, token).ConfigureAwait(false);
            _host.Dispatch(() =>
            {
                _host.Record("search.indexMs", build.Elapsed.TotalMilliseconds, "ms");
                _host.Record("search.chunks", build.Chunks, "chunks");
                _panel.Refresh(null);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The folder closed or the plug-in went.
        }
#pragma warning disable CA1031 // A failed build is said; the editor goes on, the last index still searches.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Dispatch(() =>
            {
                _host.Log(EditorLogLevel.Error, "The code index was not built.", error);
                _panel.Refresh("Not indexed: " + error.Message);
            });
        }
        finally
        {
            _host.Dispatch(() => progress?.Dispose());
        }
    }

    private void Watch(string folder)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Error += (_, _) => Later();                    // events lost: the next build looks at every file anyway
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception error) when (error is IOException or ArgumentException or PlatformNotSupportedException)
        {
            _host.Log(EditorLogLevel.Warning, $"The folder is not watched ({error.Message}); the index follows the files saved in the editor.");
        }
    }

    // On the watcher's thread: a change in an indexed kind of file, outside the ignored folders, brings a build later.
    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (_folder is not { } folder || Ignored(Path.GetRelativePath(folder, e.FullPath)))
        {
            return;
        }

        string extension = Path.GetExtension(e.FullPath);
        if (extension.Length == 0 || _settings.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            Later();                                                    // a folder's event too (a move, a delete)
        }
    }

    private bool Ignored(string relative)
    {
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (_settings.Ignore.Contains(part, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void Later()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _reindex.Change(_settings.ReindexDelayMilliseconds, Timeout.Infinite);   // events in a burst make one build
            }
        }
    }

    private void Close()
    {
        _watcher?.Dispose();
        _watcher = null;
        CodeIndex? index;
        Task building;
        lock (_gate)
        {
            _stop.Cancel();
            index = _index;
            _index = null;
            building = _building;
        }

        if (index is not null)
        {
            _ = building.ContinueWith(_ => index.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private string IndexFolder() => _settings.IndexFolder.Length > 0 ? _settings.IndexFolder
        : Path.Combine(_host.DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Craft"), "search");
}

/// <summary>
/// The "Code search" side view (<see cref="EditorSideViews"/> "search.code", the rail's Search): what the user types is
/// searched off the UI thread after <c>queryDelayMs</c> (a newer query wins), each hit a line "Name" with "kind ·
/// Container · path:line"; a tap opens the file at the line.
/// </summary>
internal sealed class SearchPanel : IQueryPanel, IDisposable
{
    public const string PanelId = "search.code";

    private readonly SearchSession _session;
    private readonly IEditorHost _host;
    private readonly SearchSettings _settings;
    private readonly Timer _timer;
    private IReadOnlyList<CodeHit> _hits = [];
    private IReadOnlyList<PanelItem> _items = [];
    private string _query = "";
    private string? _status = "Type to search the code: a name, words, camelCase parts.";
    private int _generation;

    public SearchPanel(SearchSession session, IEditorHost host, SearchSettings settings)
    {
        _session = session;
        _host = host;
        _settings = settings;
        _timer = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Id => PanelId;

    public string Title => "Search";

    public string Placeholder => "Search the code (a name, words, camelCase parts)";

    public string Query => _query;

    public string? Status => _status;

    public IReadOnlyList<PanelItem> Items => _items;

    /// <summary>The hits shown (the tests read them).</summary>
    public IReadOnlyList<CodeHit> Hits => _hits;

    /// <summary>The last search's task (the tests wait for it).</summary>
    public Task Searched { get; private set; } = Task.CompletedTask;

    public event EventHandler? Changed;

    public void SetQuery(string text)
    {
        _query = text ?? "";
        _generation++;
        _timer.Change(_settings.QueryDelayMilliseconds, Timeout.Infinite);
    }

    public void Activate(int index)
    {
        if (index >= 0 && index < _hits.Count)
        {
            var hit = _hits[index];
            _ = _host.GoToAsync(hit.FullPath, hit.Chunk.Line);
        }
    }

    /// <summary>Searches <paramref name="query"/> now (the tests; the view goes through the pause).</summary>
    public Task SearchNowAsync(string query)
    {
        _query = query;
        int generation = ++_generation;
        return Searched = SearchAsync(query, generation);
    }

    /// <summary>A new folder (or none): no hits, the status said.</summary>
    public void Reset(string? status)
    {
        _hits = [];
        _items = [];
        _status = status ?? "Indexing the folder…";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The index changed (built, loaded, failed): the query searched again, or the status said.</summary>
    public void Refresh(string? status)
    {
        if (status is not null)
        {
            _status = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_query.Trim().Length > 0)
        {
            _ = SearchNowAsync(_query);
        }
        else if (status is null && _session.Index is { IsReady: true } index)
        {
            _status = string.Create(CultureInfo.InvariantCulture, $"{index.Files.Count:N0} files, {index.ChunkCount:N0} chunks · {index.Search}");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Run()
    {
        string query = _query;
        int generation = _generation;
        _host.Dispatch(() => Searched = SearchAsync(query, generation));
    }

    private async Task SearchAsync(string query, int generation)
    {
        if (_session.Index is not { } index)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<CodeHit> hits;
        try
        {
            hits = await Task.Run(() => index.Query(query, _settings.Results)).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A failed search is said in the view.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _status = "Search failed: " + error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (generation != _generation)
        {
            return;                                                    // typed meanwhile: the newer query shows
        }

        double ms = watch.Elapsed.TotalMilliseconds;
        _host.Record("search.queryMs", ms, "ms");
        _hits = hits;
        _items = [.. hits.Select(h => new PanelItem(h.Chunk.Name, $"{h.Chunk.Kind} · {(h.Chunk.Container.Length > 0 ? h.Chunk.Container + " · " : "")}{h.RelativePath}:{h.Chunk.Line}"))];
        _status = query.Trim().Length == 0 ? null
            : string.Create(CultureInfo.InvariantCulture, $"{hits.Count} results · {ms:F1} ms · {index.Search}{(index.IsReady ? "" : " · indexing…")}");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The tool set "search" (<see cref="EditorToolSets"/>): <c>search_code</c>, for the chat (every registered set) and the agent
/// (the AI plug-in's setting agentTools): over the editor's open folder (the plug-in's index there) or, for a folder, an
/// index of that folder made when the tool is first called and kept on disk beside the editor's.
/// </summary>
public sealed class SearchToolSet : IEditorToolSet
{
    /// <summary>The set's name.</summary>
    public const string SetName = "search";

    /// <summary>The tool's name.</summary>
    public const string ToolName = "search_code";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, CodeIndex> Folders = new(StringComparer.Ordinal);

    private SearchToolSet()
    {
    }

    /// <summary>The set.</summary>
    public static SearchToolSet Instance { get; } = new();

    /// <inheritdoc />
    public string Name => SetName;

    /// <inheritdoc />
    public string Summary => "search_code: the workspace's code by name, words or camelCase parts (and meaning, with an embedding model)";

    /// <inheritdoc />
    public IReadOnlyList<ITool> Live(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = EditorPlugins.Find(SearchSettings.PluginName) is not null ? SearchSettings.From(host.Settings) : SearchSettings.FromEnvironment();
        return [Make(() => SearchPlugin.Current?.Index, settings)];
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Folder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var settings = SearchSettings.FromEnvironment();
        string root = Path.GetFullPath(folder);
        return [Make(() =>
        {
            lock (Gate)
            {
                if (!Folders.TryGetValue(root, out var index))
                {
                    string kept = settings.IndexFolder.Length > 0 ? settings.IndexFolder
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Craft", "search");
                    index = new CodeIndex(root, settings, CodeIndex.FileFor(kept, root));
                    index.Load();
                    Folders[root] = index;
                }

                return index;
            }
        }, settings)];
    }

    private static Tool Make(Func<CodeIndex?> index, SearchSettings settings) => Tool.Create(ToolName,
        "Searches the workspace's code by name, words or camelCase parts (and by meaning when an embedding model is set). "
        + "Gives the best types and members: path:line, kind and name. Read the file at the line for its text.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string", ["description"] = "What to find: a name (ParseAsync), words (parse text), or a description." },
                ["top"] = new JsonObject { ["type"] = "integer", ["description"] = $"How many results (default {settings.ToolResults})." },
            },
            ["required"] = new JsonArray("query"),
        },
        async (arguments, token) =>
        {
            string query = arguments["query"]?.GetValue<string>() ?? "";
            if (query.Trim().Length == 0)
            {
                return "Error: give a query.";
            }

            int top = arguments["top"] is JsonValue value && value.TryGetValue(out int asked) ? Math.Clamp(asked, 1, 200) : settings.ToolResults;
            if (index() is not { } searched)
            {
                return "No folder is open in the editor, so there is no code to search.";
            }

            await searched.WhenReadyAsync(token).ConfigureAwait(false);
            var hits = await Task.Run(() => searched.Query(query, top), token).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                return $"No code matches '{query}'.";
            }

            var text = new StringBuilder();
            foreach (var hit in hits)
            {
                text.Append(CultureInfo.InvariantCulture, $"{hit.RelativePath}:{hit.Chunk.Line}-{hit.Chunk.EndLine}  {hit.Chunk.Kind} {hit.Chunk.FullName}\n");
            }

            return text.ToString().TrimEnd();
        });
}
