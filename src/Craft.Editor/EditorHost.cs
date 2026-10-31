// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The editor host without a UI: the open documents and their events, the active document's selection, edits proposed
/// by plug-ins (asked through the <see cref="Presenter"/>, applied as one undo step, never silently), commands and key
/// bindings, status items, panels, plug-in settings, progress, the log and telemetry; and the activation of every
/// registered <see cref="IEditorPlugin"/>, each through a scope of its own that takes back what the plug-in added when it
/// is deactivated. A plug-in whose activation throws is reported (<see cref="Plugins"/>) and skipped; a handler of a
/// plug-in that throws is logged, and the others still run. The UI (Craft.Maui, the app) tells it what the user does
/// (<see cref="Open"/>, <see cref="Close"/>, <see cref="SetActive"/>, <see cref="UpdateSelection"/>, <see cref="SaveAsync"/>,
/// <see cref="HandleKey"/>) and draws what it holds. Used on the UI thread.
/// </summary>
public sealed partial class EditorHost : IEditorHost, IEditorKeyHandler, IDisposable
{
    /// <summary>The source name of the host's own log lines and measures.</summary>
    public const string HostSource = "host";

    private readonly Action<Action> _dispatch;
    private readonly Dictionary<KeyChord, string> _userBindings = [];
    private readonly List<OpenDocument> _documents = [];
    private readonly List<IStatusItem> _statusItems = [];
    private readonly List<IEditorPanel> _panels = [];
    private readonly List<EditorProgressItem> _progress = [];
    private readonly Queue<EditorLogEntry> _log = new();
    private readonly List<ActivePlugin> _active = [];
    private readonly Dictionary<string, PluginActivation> _activations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HandlerList<EventHandler<EditorDocumentEventArgs>> _opened = new();
    private readonly HandlerList<EventHandler<EditorDocumentChangedEventArgs>> _changed = new();
    private readonly HandlerList<EventHandler<EditorDocumentEventArgs>> _saved = new();
    private readonly HandlerList<EventHandler<EditorDocumentEventArgs>> _closed = new();
    private readonly HandlerList<EventHandler> _activeChanged = new();
    private readonly HandlerList<EventHandler<EditorDocumentEventArgs>> _selectionChanged = new();
    private IStatusItem[] _sortedStatus = [];
    private OpenDocument? _activeDocument;
    private bool _disposed;

    /// <summary>A host with <paramref name="options"/> (the defaults when null).</summary>
    /// <param name="options">The settings: the user's key bindings, the log's length, the proposal's preview.</param>
    /// <param name="dispatch">Runs an action on the UI thread later (MAUI's dispatcher); null runs it at once on the calling thread (headless and tests).</param>
    /// <param name="presenter">How the user is asked; null rejects every proposed edit.</param>
    /// <param name="readVariable">Reads a plug-in setting's variable (the process environment when null).</param>
    public EditorHost(CraftOptions? options = null, Action<Action>? dispatch = null, IEditorPresenter? presenter = null, Func<string, string?>? readVariable = null)
    {
        Options = options ?? new CraftOptions();
        _dispatch = dispatch ?? (static action => action());
        Presenter = presenter;
        Settings = new PluginSettingsReader(readVariable ?? Environment.GetEnvironmentVariable);
        foreach (var binding in Options.KeyBindings)
        {
            _userBindings[binding.Chord] = binding.CommandId;
        }
    }

    /// <summary>The settings.</summary>
    public CraftOptions Options { get; }

    /// <summary>How the user is asked (a page's dialogs); null rejects every proposed edit.</summary>
    public IEditorPresenter? Presenter { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<IEditorDocument> Documents => _documents;

    /// <inheritdoc />
    public IEditorDocument? ActiveDocument => _activeDocument;

    /// <inheritdoc />
    public SelectionState Selection => _activeDocument?.Selection ?? default;

    /// <inheritdoc />
    /// <remarks>Set by the shell when a folder opens or closes (the full path, or null).</remarks>
    public string? WorkspaceFolder { get; set; }

    /// <summary>
    /// How <see cref="OpenDocumentAsync"/> opens a file in the editor (the shell sets it: a tab, a new document for a path
    /// with no file yet); null: the host opens nothing and the call gives null.
    /// </summary>
    public Func<string, CancellationToken, Task<IEditorDocument?>>? OpenFile { get; set; }

    /// <summary>
    /// How <see cref="GoToAsync"/> opens a file at a line and column (the shell sets it); null: the call gives false.
    /// </summary>
    public Func<string, int, int, CancellationToken, Task<bool>>? GoTo { get; set; }

    /// <summary>
    /// How <see cref="ReadImageAsync"/> reads a picture (the shell sets it: the platform's clipboard, the file picker); null:
    /// the call gives null.
    /// </summary>
    public Func<EditorImageSource, CancellationToken, Task<EditorImage?>>? ReadImage { get; set; }

    /// <inheritdoc />
    /// <remarks>Set by the shell to the app's data folder (<c>CRAFT_DATA_FOLDER</c>); null in a host without one.</remarks>
    public string? DataFolder { get; set; }

    /// <inheritdoc />
    public IEditorPanel? FindPanel(string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        foreach (var panel in _panels)
        {
            if (string.Equals(panel.Id, panelId, StringComparison.Ordinal))
            {
                return panel;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<bool> GoToAsync(string path, int line, int column = 1, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return GoTo is { } go ? go(Path.GetFullPath(path), Math.Max(1, line), Math.Max(1, column), cancellationToken) : Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<EditorImage?> ReadImageAsync(EditorImageSource source, CancellationToken cancellationToken = default) =>
        ReadImage is { } read ? read(source, cancellationToken) : Task.FromResult<EditorImage?>(null);

    /// <inheritdoc />
    public void ShowPanel(string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        PanelShowRequested?.Invoke(this, panelId);
    }

    /// <inheritdoc />
    public Task<IEditorDocument?> OpenDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        IEditorDocument? ignoringCase = null;
        foreach (var document in _documents)
        {
            if (document.FilePath is not { } file)
            {
                continue;
            }

            var own = Path.GetFullPath(file);
            if (string.Equals(own, full, StringComparison.Ordinal))
            {
                return Task.FromResult<IEditorDocument?>(document);
            }

            if (ignoringCase is null && string.Equals(own, full, StringComparison.OrdinalIgnoreCase))
            {
                ignoringCase = document;   // a file system that ignores case names the same file
            }
        }

        if (ignoringCase is not null)
        {
            return Task.FromResult<IEditorDocument?>(ignoringCase);
        }

        return OpenFile is { } open ? open(full, cancellationToken) : Task.FromResult<IEditorDocument?>(null);
    }

    /// <inheritdoc />
    /// <remarks>Set by the shell to its terminals (phase T); null while the app has none.</remarks>
    public IEditorTerminals? Terminals { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<IEditorCommand> Commands => EditorCommands.All;

    /// <inheritdoc />
    public IPluginSettings Settings { get; }

    /// <summary>The status items, in drawing order: the left end's then the right end's, each by priority (higher first), then as added.</summary>
    public IReadOnlyList<IStatusItem> StatusItems => _sortedStatus;

    /// <summary>The panels, as added.</summary>
    public IReadOnlyList<IEditorPanel> Panels => _panels;

    /// <summary>The long work running now, as started.</summary>
    public IReadOnlyList<EditorProgressItem> Progress => _progress;

    /// <summary>What activating each plug-in did (the latest of each), in name order.</summary>
    public IReadOnlyList<PluginActivation> Plugins => [.. _activations.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The last <see cref="CraftOptions.HostLogLines"/> lines of the log, oldest first.</summary>
    public IReadOnlyList<EditorLogEntry> RecentLog => [.. _log];

    /// <summary>Raised when a status item was added or removed (an item's own text changes raise its <see cref="IStatusItem.Changed"/>).</summary>
    public event EventHandler? StatusItemsChanged;

    /// <summary>Raised when a panel was added or removed.</summary>
    public event EventHandler? PanelsChanged;

    /// <summary>A plug-in asked to bring a panel into view (<see cref="ShowPanel"/>): the panel's id.</summary>
    public event EventHandler<string>? PanelShowRequested;

    /// <summary>Raised when long work started, reported or ended.</summary>
    public event EventHandler<EditorProgressItem>? ProgressChanged;

    /// <summary>Raised for each log line.</summary>
    public event EventHandler<EditorLogEntry>? Logged;

    /// <summary>Raised for each measure recorded (free when nobody listens).</summary>
    public event EventHandler<EditorMeasureEventArgs>? Recorded;

    /// <inheritdoc />
    public event EventHandler<EditorDocumentEventArgs>? DocumentOpened { add => _opened.Add(value); remove => _opened.Remove(value); }

    /// <inheritdoc />
    public event EventHandler<EditorDocumentChangedEventArgs>? DocumentChanged { add => _changed.Add(value); remove => _changed.Remove(value); }

    /// <inheritdoc />
    public event EventHandler<EditorDocumentEventArgs>? DocumentSaved { add => _saved.Add(value); remove => _saved.Remove(value); }

    /// <inheritdoc />
    public event EventHandler<EditorDocumentEventArgs>? DocumentClosed { add => _closed.Add(value); remove => _closed.Remove(value); }

    /// <inheritdoc />
    public event EventHandler? ActiveDocumentChanged { add => _activeChanged.Add(value); remove => _activeChanged.Remove(value); }

    /// <inheritdoc />
    public event EventHandler<EditorDocumentEventArgs>? SelectionChanged { add => _selectionChanged.Add(value); remove => _selectionChanged.Remove(value); }

    /// <summary>
    /// Opens <paramref name="document"/> (the caller keeps owning it) in <paramref name="language"/> (null for plain
    /// text, named <paramref name="languageName"/> or "Plain text"); the first one opened becomes the active one.
    /// </summary>
    public IEditorDocument Open(ITextDocument document, ILanguage? language, string? languageName = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var open = new OpenDocument(this, document, language, languageName ?? language?.Name ?? "Plain text");
        _documents.Add(open);
        document.Buffer.Changed += open.OnBufferChanged;
        Raise(_opened, new EditorDocumentEventArgs(open), nameof(DocumentOpened));
        if (_activeDocument is null)
        {
            SetActive(open);
        }

        return open;
    }

    /// <summary>Closes <paramref name="document"/> (the caller disposes its text document afterwards); the next open one becomes active.</summary>
    public void Close(IEditorDocument document)
    {
        if (document is not OpenDocument open || !_documents.Remove(open))
        {
            return;
        }

        open.Document.Buffer.Changed -= open.OnBufferChanged;
        open.IsClosed = true;
        Raise(_closed, new EditorDocumentEventArgs(open), nameof(DocumentClosed));
        if (ReferenceEquals(_activeDocument, open))
        {
            SetActive(_documents.Count > 0 ? _documents[^1] : null);
        }
    }

    /// <summary>Makes <paramref name="document"/> (an open one, or null for none) the active document.</summary>
    public void SetActive(IEditorDocument? document)
    {
        var open = document as OpenDocument;
        if (document is not null && (open is null || open.IsClosed))
        {
            throw new ArgumentException("The document is not open in this host.", nameof(document));
        }

        if (!ReferenceEquals(_activeDocument, open))
        {
            _activeDocument = open;
            Raise(_activeChanged, nameof(ActiveDocumentChanged));
        }
    }

    /// <summary>Records <paramref name="document"/>'s selection, as its editor reports it (raises <see cref="SelectionChanged"/> when it moved).</summary>
    public void UpdateSelection(IEditorDocument document, SelectionState selection)
    {
        if (document is OpenDocument { IsClosed: false } open && open.Selection != selection)
        {
            open.Selection = selection;
            Raise(_selectionChanged, new EditorDocumentEventArgs(open), nameof(SelectionChanged));
        }
    }

    /// <summary>
    /// Records <paramref name="document"/>'s diagnostics as its editor shows them (mapped to its current snapshot), so a
    /// plug-in reads them through <see cref="IEditorDocument.Diagnostics"/>; the editor calls it when its diagnostics change.
    /// </summary>
    public void SetDiagnostics(IEditorDocument document, ReadOnlySpan<EditorDiagnostic> diagnostics)
    {
        if (document is OpenDocument { IsClosed: false } open && open.Host == this)
        {
            open.Diagnostics = diagnostics.IsEmpty ? DiagnosticSet.Empty : new DiagnosticSet(open.Document.Buffer.Current, diagnostics.ToArray());
        }
    }

    /// <summary>Saves <paramref name="document"/> (to <paramref name="path"/> when given), then raises <see cref="DocumentSaved"/>.</summary>
    public async Task SaveAsync(IEditorDocument document, string? path = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await document.Document.SaveAsync(path, cancellationToken);
        if (document is OpenDocument { IsClosed: false } open)
        {
            Raise(_saved, new EditorDocumentEventArgs(open), nameof(DocumentSaved));
        }
    }

    /// <inheritdoc />
    public async Task<bool> ProposeEditsAsync(IEditorDocument document, IReadOnlyList<TextEdit> edits, string title, CancellationToken cancellationToken = default) =>
        await ProposeCoreAsync(document, new ProposedEdits(edits, title), HostSource, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ProposeAsync(IEditorDocument document, ProposedEdits proposal, CancellationToken cancellationToken = default) =>
        await ProposeCoreAsync(document, proposal, HostSource, cancellationToken);

    /// <summary>
    /// Records the semantic session of <paramref name="document"/>'s editor (or null), so a proposal's diff view can show
    /// what the language would report after the change (<see cref="ISemanticLanguageSession.DiagnoseEditsAsync"/>).
    /// </summary>
    public void SetSemanticSession(IEditorDocument document, ISemanticLanguageSession? session)
    {
        if (document is OpenDocument { IsClosed: false } open && open.Host == this)
        {
            open.SemanticSession = session;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (EditorCommands.Find(commandId) is not { } command)
        {
            Log(HostSource, EditorLogLevel.Warning, $"Command '{commandId}' is not registered (registered: {string.Join(", ", EditorCommands.Names)}).", null);
            return false;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            if (!command.CanExecute(this))
            {
                Log(HostSource, EditorLogLevel.Information, $"Command '{command.Name}' cannot run now.", null);
                return false;
            }

            await command.ExecuteAsync(this, cancellationToken);
            Record(HostSource, "command." + command.Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds, "ms");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log(HostSource, EditorLogLevel.Information, $"Command '{command.Name}' was cancelled.", null);
            return false;
        }
#pragma warning disable CA1031 // A command's failure, whatever it is, is logged and the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Log(HostSource, EditorLogLevel.Error, $"Command '{command.Name}' failed: {error.Message}", error);
            return false;
        }
    }

    /// <summary>
    /// A key the focused editor received, routed in this order: a user's binding (<see cref="CraftOptions.KeyBindings"/>)
    /// runs its command; a plug-in's or the app's binding that overrides the editor runs its command; then the editor's
    /// own keys (<paramref name="editor"/>, the <see cref="EditorModel"/>); then a plug-in's or the app's binding for a
    /// chord the editor did not handle. So the built-in keys keep working and plug-ins add new chords. A user's binding
    /// with an empty command removes the plug-ins' binding of the chord (the editor's own key stays).
    /// </summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
    {
        if (key == EditorKey.None)
        {
            return false;
        }

        var chord = new KeyChord(key, modifiers);
        if (_userBindings.TryGetValue(chord, out var userCommand))
        {
            if (userCommand.Length == 0)
            {
                return editor?.OnKey(key, modifiers) ?? false;
            }

            Run(userCommand);
            return true;
        }

        var binding = EditorKeyBindings.Find(chord);
        if (binding is { OverridesEditor: true, CommandId.Length: > 0 })
        {
            Run(binding.CommandId);
            return true;
        }

        if (editor?.OnKey(key, modifiers) == true)
        {
            return true;
        }

        if (binding is { CommandId.Length: > 0 })
        {
            Run(binding.CommandId);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IDisposable AddStatusItem(IStatusItem item) => AddStatusItem(item, HostSource);

    /// <inheritdoc />
    public IDisposable AddPanel(IEditorPanel panel) => AddPanel(panel, HostSource);

    /// <inheritdoc />
    public IEditorProgress StartProgress(string title) => StartProgress(title, HostSource);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message)
    {
        if (Presenter is { } presenter)
        {
            return presenter.ShowMessageAsync(title, message);
        }

        Log(HostSource, EditorLogLevel.Information, $"{title}: {message}", null);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken = default) => AskAsync(question, HostSource, cancellationToken);

    /// <summary>Asks <paramref name="question"/> for <paramref name="source"/> through the presenter; without one, -1 (logged: nobody could answer).</summary>
    internal async Task<int> AskAsync(EditorQuestion question, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (Presenter is not { } presenter)
        {
            Log(source, EditorLogLevel.Warning, $"{question.Title}: nobody could be asked (the host has no presenter), so it counts as no.", null);
            return -1;
        }

        int answer = await presenter.AskAsync(question, cancellationToken);
        return answer >= 0 && answer < question.Choices.Count ? answer : question.Dismissed;
    }

    /// <inheritdoc />
    public void Log(EditorLogLevel level, string message, Exception? error = null) => Log(HostSource, level, message, error);

    /// <inheritdoc />
    public void Record(string metric, double value, string unit) => Record(HostSource, metric, value, unit);

    /// <inheritdoc />
    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatch(action);
    }

    /// <summary>
    /// Activates every registered plug-in not active yet, and deactivates every active one no longer registered (removed
    /// or disabled); returns what each activation did, in name order. Call it after loading plug-ins.
    /// </summary>
    public IReadOnlyList<PluginActivation> RefreshPlugins()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var active = _active[i];
            if (!ReferenceEquals(EditorPlugins.Find(active.Plugin.Name), active.Plugin))
            {
                Deactivate(active, "no longer registered");
            }
        }

        foreach (var plugin in EditorPlugins.All)
        {
            if (!_active.Exists(a => ReferenceEquals(a.Plugin, plugin)))
            {
                Activate(plugin);
            }
        }

        return Plugins;
    }

    /// <summary>Deactivates the plug-in <paramref name="name"/> (it stays registered; <see cref="RefreshPlugins"/> activates it again); false when it is not active.</summary>
    public bool Deactivate(string name)
    {
        var active = _active.Find(a => a.Plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (active is null)
        {
            return false;
        }

        Deactivate(active, "deactivated");
        return true;
    }

    /// <summary>Deactivates every plug-in (the last activated first) and lets go of the documents (their owners dispose them).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Deactivate(_active[i], "the host closed");
        }

        foreach (var open in _documents)
        {
            open.Document.Buffer.Changed -= open.OnBufferChanged;
            open.IsClosed = true;
        }

        _documents.Clear();
        _activeDocument = null;
        _disposed = true;
    }

    internal async Task<bool> ProposeCoreAsync(IEditorDocument document, ProposedEdits proposed, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(proposed);
        var edits = proposed.Edits;
        string title = proposed.Title;
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (document is not OpenDocument { IsClosed: false } open || open.Host != this)
        {
            Log(source, EditorLogLevel.Warning, $"'{title}': the document is not open; nothing was proposed.", null);
            return false;
        }

        var snapshot = open.Document.Buffer.Current;
        TextEdit[] copy = [.. edits];
        Validate(copy, snapshot.Length);
        if (copy.Length == 0)
        {
            return false;
        }

        if (Presenter is not { } presenter)
        {
            Log(source, EditorLogLevel.Warning, $"'{title}': this host has no way to ask the user, so the edits were not applied.", null);
            return false;
        }

        var diff = TextDiff.OfEdits(snapshot, copy, Options.DiffContextLines, Options.DiffLineLimit);
        var proposal = new EditProposal(open, copy, title, source, snapshot.Version,
            EditProposal.Summarize(snapshot, open.FilePath, copy, Options.ProposalPreviewCharacters))
        {
            Diff = diff,
            HunkOfEdit = EditProposal.HunksOf(snapshot, copy, diff),
            Placement = proposed.Placement,
            Origin = proposed.Origin ?? source,
            Check = Options.DiffCheck && open.SemanticSession is { } session
                ? ProposalCheck.RunAsync(session, snapshot, copy, cancellationToken)
                : Task.FromResult<ProposalCheck?>(null),
        };
        var answers = await presenter.ChooseHunksAsync(proposal, cancellationToken);
        var chosen = proposal.Accepted(answers);
        if (chosen.Length == 0)
        {
            Log(source, EditorLogLevel.Information, $"'{title}': rejected by the user.", null);
            return false;
        }

        bool partial = chosen.Length < copy.Length;
        copy = chosen;

        if (open.IsClosed || open.Document.Buffer.Current.Version != snapshot.Version)
        {
            Log(source, EditorLogLevel.Warning, $"'{title}': the document {(open.IsClosed ? "closed" : "changed")} while the user was asked; nothing was applied.", null);
            return false;
        }

        var before = open.Selection;
        var after = new SelectionState(MapThrough(before.Anchor, copy), MapThrough(before.Caret, copy));
        open.Document.Edit(copy, EditKind.Command, before, after);
        UpdateSelection(open, after);
        Log(source, EditorLogLevel.Information, $"'{title}': applied ({copy.Length} {(copy.Length == 1 ? "edit" : "edits")}{(partial ? ", the hunks the user accepted" : "")}).", null);
        return true;
    }

    internal IDisposable AddStatusItem(IStatusItem item, string source)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_statusItems.Exists(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A status item '{item.Id}' is shown already ({source} added another).", nameof(item));
        }

        _statusItems.Add(item);
        SortStatus();
        StatusItemsChanged?.Invoke(this, EventArgs.Empty);
        return new Undo(() =>
        {
            if (_statusItems.Remove(item))
            {
                SortStatus();
                StatusItemsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    internal IDisposable AddPanel(IEditorPanel panel, string source)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (_panels.Exists(p => string.Equals(p.Id, panel.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A panel '{panel.Id}' is shown already ({source} added another).", nameof(panel));
        }

        _panels.Add(panel);
        PanelsChanged?.Invoke(this, EventArgs.Empty);
        return new Undo(() =>
        {
            if (_panels.Remove(panel))
            {
                PanelsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    internal EditorProgressItem StartProgress(string title, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var item = new EditorProgressItem(this, title, source);
        _progress.Add(item);
        ProgressChanged?.Invoke(this, item);
        return item;
    }

    internal void OnProgressChanged(EditorProgressItem item) => ProgressChanged?.Invoke(this, item);

    internal void OnProgressEnded(EditorProgressItem item)
    {
        if (_progress.Remove(item))
        {
            ProgressChanged?.Invoke(this, item);
        }
    }

    internal void Log(string source, EditorLogLevel level, string message, Exception? error)
    {
        var entry = new EditorLogEntry(DateTimeOffset.Now, level, source, message, error);
        if (Options.HostLogLines > 0)
        {
            if (_log.Count >= Options.HostLogLines)
            {
                _log.Dequeue();
            }

            _log.Enqueue(entry);
        }

        Logged?.Invoke(this, entry);
    }

    internal void Record(string source, string metric, double value, string unit)
    {
        if (Recorded is { } recorded)
        {
            recorded(this, new EditorMeasureEventArgs(source, metric, value, unit));
        }
    }

    private void Run(string commandId) => _ = ExecuteCommandAsync(commandId);

    private void Activate(IEditorPlugin plugin)
    {
        var scope = new PluginScope(this, plugin.Name);
        long start = Stopwatch.GetTimestamp();
        try
        {
            var handle = plugin.Activate(scope) ?? throw new InvalidOperationException("Activate returned null (it returns what deactivates the plug-in).");
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _active.Add(new ActivePlugin(plugin, scope, handle));
            _activations[plugin.Name] = new PluginActivation(plugin.Name, true, null, ms);
            Record(HostSource, "plugin.activate." + plugin.Name, ms, "ms");
            Log(HostSource, EditorLogLevel.Information, $"Plug-in '{plugin.Name}' activated in {ms:F1} ms.", null);
        }
#pragma warning disable CA1031 // A plug-in's failure, whatever it is, is reported and the host goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            scope.Close(this);
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var reason = $"{error.GetType().Name}: {error.Message}";
            _activations[plugin.Name] = new PluginActivation(plugin.Name, false, reason, ms);
            Log(HostSource, EditorLogLevel.Error, $"Plug-in '{plugin.Name}' did not activate and is skipped: {reason}", error);
        }
    }

    private void Deactivate(ActivePlugin active, string why)
    {
        _active.Remove(active);
        try
        {
            active.Handle.Dispose();
        }
#pragma warning disable CA1031 // A plug-in's failure, whatever it is, is reported and the host goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Log(HostSource, EditorLogLevel.Error, $"Plug-in '{active.Plugin.Name}' failed while deactivating: {error.Message}", error);
        }

        active.Scope.Close(this);
        _activations[active.Plugin.Name] = new PluginActivation(active.Plugin.Name, false, null, 0);
        Log(HostSource, EditorLogLevel.Information, $"Plug-in '{active.Plugin.Name}' {why}.", null);
    }

    private void SortStatus()
    {
        var sorted = _statusItems.ToArray();
        // A stable sort: equal items keep the order they were added in.
        _sortedStatus = [.. sorted.Select((item, index) => (item, index))
            .OrderBy(p => p.item.Alignment)
            .ThenByDescending(p => p.item.Priority)
            .ThenBy(p => p.index)
            .Select(p => p.item)];
    }

    private void Raise<TArgs>(HandlerList<EventHandler<TArgs>> handlers, TArgs args, string eventName)
    {
        foreach (var handler in handlers.Items)
        {
            try
            {
                handler(this, args);
            }
#pragma warning disable CA1031 // A plug-in's handler that throws is logged; the other handlers and the editor go on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                HandlerFailed(handler, eventName, error);
            }
        }
    }

    private void Raise(HandlerList<EventHandler> handlers, string eventName)
    {
        foreach (var handler in handlers.Items)
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
#pragma warning disable CA1031 // A plug-in's handler that throws is logged; the other handlers and the editor go on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                HandlerFailed(handler, eventName, error);
            }
        }
    }

    private void HandlerFailed(Delegate handler, string eventName, Exception error)
    {
        var source = handler.Method.DeclaringType?.Assembly.GetName().Name ?? "unknown";
        Log(source, EditorLogLevel.Error, $"A handler of {eventName} from {source} threw: {error.Message}", error);
    }

    private static void Validate(ReadOnlySpan<TextEdit> edits, int length)
    {
        int end = 0;
        for (int i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            if (edit.Start < end || edit.Length < 0 || edit.End > length || edit.Text is null)
            {
                throw new ArgumentException($"Edit {i} ({edit.Start}, {edit.Length}) runs outside the text (length {length}) or before the edit ahead of it ends ({end}): edits are in order and do not overlap.", nameof(edits));
            }

            end = edit.End;
        }
    }

    private static int MapThrough(int position, ReadOnlySpan<TextEdit> edits)
    {
        int delta = 0;
        foreach (var edit in edits)
        {
            if (position >= edit.End)
            {
                delta += edit.Delta;
            }
            else if (position > edit.Start)
            {
                return edit.Start + delta + edit.Text.Length;
            }
            else
            {
                break;
            }
        }

        return position + delta;
    }

    private sealed record ActivePlugin(IEditorPlugin Plugin, PluginScope Scope, IDisposable Handle);

    /// <summary>An action run once, when disposed.</summary>
    private sealed class Undo(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }

    /// <summary>An open document of this host.</summary>
    private sealed class OpenDocument(EditorHost host, ITextDocument document, ILanguage? language, string languageName) : IEditorDocument
    {
        public EditorHost Host { get; } = host;

        public ITextDocument Document { get; } = document;

        public string? FilePath => Document.FilePath;

        public string LanguageName { get; } = languageName;

        public ILanguage? Language { get; } = language;

        public SelectionState Selection { get; set; }

        public DiagnosticSet Diagnostics { get; set; } = DiagnosticSet.Empty;

        public ISemanticLanguageSession? SemanticSession { get; set; }

        public bool IsClosed { get; set; }

        public void OnBufferChanged(object? sender, TextChangedEventArgs e)
        {
            // The keystroke's path: nothing is made unless a plug-in listens.
            if (!Host._changed.IsEmpty)
            {
                Host.Raise(Host._changed, new EditorDocumentChangedEventArgs(this, e), nameof(DocumentChanged));
            }
        }
    }

    /// <summary>The plug-ins' settings: each declared key's variable, else its default.</summary>
    private sealed class PluginSettingsReader(Func<string, string?> read) : IPluginSettings
    {
        public IReadOnlyList<PluginSettingValue> All =>
        [
            .. EditorPlugins.All.SelectMany(plugin => plugin.Settings.Select(setting => Value(plugin.Name, setting))),
        ];

        public string Get(string plugin, string key)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(key);
            var owner = EditorPlugins.Get(plugin);
            foreach (var setting in owner.Settings)
            {
                if (setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Value(owner.Name, setting).Value;
                }
            }

            var declared = owner.Settings.Count == 0 ? "none" : string.Join(", ", owner.Settings.Select(s => s.Key));
            throw new ArgumentException($"Plug-in '{owner.Name}' declares no setting '{key}' (declared: {declared}).", nameof(key));
        }

        private PluginSettingValue Value(string plugin, PluginSetting setting)
        {
            var variable = PluginSetting.VariableName(plugin, setting.Key);
            var value = read(variable) is { Length: > 0 } set ? set.Trim() : setting.Default;
            return new PluginSettingValue(plugin, setting, variable, value);
        }
    }
}
