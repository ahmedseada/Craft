// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Abstractions;
using Craft.Editor.Activities;

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
    private readonly Dictionary<(KeyChord Chord, KeyChord Then), string> _userBindings = [];
    private readonly HashSet<KeyChord> _userPrefixes = [];
    private readonly Dictionary<(KeyChord Chord, KeyChord Then), KeyBinding> _keyMapBindings = [];
    private readonly HashSet<KeyChord> _keyMapPrefixes = [];
    private KeyChord? _pendingChord;
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
    private readonly HandlerList<EventHandler<EditorKeyEventArgs>> _keyPressed = new();
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
        Labels = new UiWorkLabels();
        ActivityCenter = new ActivityCenter(action => Dispatch("activities: show", action), Options);
        Notifications = new NotificationCenter(ActivityCenter, action => Dispatch("notifications: show", action), Options);
        Presenter = presenter;
        Settings = new PluginSettingsReader(readVariable ?? Environment.GetEnvironmentVariable);
        SetKeyBindings(Options.KeyBindings, Options.KeyMap);
    }

    /// <summary>The first chord of a two-key chord pressed, while the second is awaited (the status bar shows it); null otherwise.</summary>
    public KeyChord? PendingChord => _pendingChord;

    /// <summary>Raised when <see cref="PendingChord"/> changes.</summary>
    public event EventHandler? PendingChordChanged;

    /// <summary>
    /// Takes every chord before any binding or the editor while set (the keyboard page recording a shortcut): true when
    /// it took the chord. The page sets it while it records and clears it after.
    /// </summary>
    public Func<KeyChord, bool>? KeyCapture { get; set; }

    /// <summary>The keymap in effect (<see cref="CraftOptions.KeyMap"/>), or null when its name is not registered.</summary>
    public IKeyMap? KeyMap { get; private set; }

    /// <summary>Why no keymap applies (its name is not registered), or null.</summary>
    public string? KeyMapProblem { get; private set; }

    /// <summary>
    /// Takes <paramref name="user"/> (the user's bindings, <c>CRAFT_KEY_BINDINGS</c>) and the keymap <paramref name="keyMap"/>
    /// (by name in <see cref="EditorKeyMaps"/>) in place of the ones in use: the settings page's changes apply at once.
    /// A keymap not registered is reported in <see cref="KeyMapProblem"/> and the log, and the registered bindings apply alone.
    /// </summary>
    public void SetKeyBindings(IReadOnlyList<KeyBinding> user, string? keyMap)
    {
        ArgumentNullException.ThrowIfNull(user);
        _userBindings.Clear();
        _userPrefixes.Clear();
        foreach (var binding in user)
        {
            _userBindings[(binding.Chord, binding.Then ?? default)] = binding.CommandId;
            if (binding.Then is not null && binding.CommandId.Length > 0)
            {
                _userPrefixes.Add(binding.Chord);
            }
        }

        _keyMapBindings.Clear();
        _keyMapPrefixes.Clear();
        KeyMap = null;
        KeyMapProblem = null;
        if (string.IsNullOrWhiteSpace(keyMap))
        {
            return;
        }

        try
        {
            KeyMap = EditorKeyMaps.Get(keyMap.Trim());
        }
        catch (NotRegisteredException e)
        {
            KeyMapProblem = $"CRAFT_KEYMAP: {e.Message}";
            Log(HostSource, EditorLogLevel.Warning, KeyMapProblem, null);
            return;
        }

        foreach (var binding in KeyMap.Bindings)
        {
            _keyMapBindings[(binding.Chord, binding.Then ?? default)] = binding;
            if (binding.Then is not null && binding.CommandId.Length > 0)
            {
                _keyMapPrefixes.Add(binding.Chord);
            }
        }
    }

    /// <summary>The settings.</summary>
    public CraftOptions Options { get; }

    /// <summary>The labels of the UI work running (every dispatch of the host runs under one; the UI watchdog names them).</summary>
    public UiWorkLabels Labels { get; }

    /// <summary>The running operations (phase U): the status bar's activity and its list.</summary>
    public ActivityCenter ActivityCenter { get; }

    /// <summary>The notification centre (phase U): the bell, the centre's list, the toasts.</summary>
    public NotificationCenter Notifications { get; }

    /// <inheritdoc />
    public IEditorActivities Activities => ActivityCenter;

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

    /// <inheritdoc />
    /// <remarks>The settings' data folder (<see cref="CraftOptions.ResolveDataFolder"/>: <c>CRAFT_DATA_FOLDER</c>, else the local application data's "Craft").</remarks>
    public string? DataFolder => Options.ResolveDataFolder();

    /// <summary>How <see cref="OpenExternalAsync"/> opens a folder or a file outside the editor (the page sets it); null: it cannot, and the call gives false.</summary>
    public Func<string, Task<bool>>? OpenExternal { get; set; }

    /// <inheritdoc />
    public Task<bool> OpenExternalAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return OpenExternal is { } open ? open(Path.GetFullPath(path)) : Task.FromResult(false);
    }

    /// <summary>
    /// How <see cref="OpenDocumentAsync"/> opens a file in the editor (the shell sets it: a tab, a new document for a path
    /// with no file yet); null: the host opens nothing and the call gives null.
    /// </summary>
    public Func<string, CancellationToken, Task<IEditorDocument?>>? OpenFile { get; set; }

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

    /// <inheritdoc />
    /// <remarks>Raised by <see cref="HandleKey"/> before it routes the key; nothing is made for it while no one listens.</remarks>
    public event EventHandler<EditorKeyEventArgs>? KeyPressed { add => _keyPressed.Add(value); remove => _keyPressed.Remove(value); }

    /// <summary>
    /// A plug-in moved a document's selection (<see cref="Select"/>) or typed into it (<see cref="TypeText"/>): the shell
    /// sets that selection on the document's editor and scrolls it into view.
    /// </summary>
    public event EventHandler<EditorSelectionRequestEventArgs>? SelectionRequested;

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

    /// <summary>Saves <paramref name="document"/> (to <paramref name="path"/> when given, its folders made), then raises <see cref="DocumentSaved"/>.</summary>
    public async Task SaveAsync(IEditorDocument document, string? path = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        string name = Path.GetFileName(path ?? document.FilePath ?? "untitled");
        using var activity = ActivityCenter.Start($"Saving {name}", ActivityKinds.Save, false, cancellationToken, true, HostSource);
        try
        {
            if (path is not null && Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);              // a new file's folders (the agent's new file in a new folder)
            }

            await document.Document.SaveAsync(path, cancellationToken);
            activity.Complete(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{document.Document.Buffer.Current.Length:N0} characters"));
        }
        catch (Exception error)
        {
            activity.Fail(error.Message, error);
            throw;
        }

        if (document is OpenDocument { IsClosed: false } open)
        {
            Raise(_saved, new EditorDocumentEventArgs(open), nameof(DocumentSaved));
        }
    }

    /// <inheritdoc />
    public bool TypeText(IEditorDocument document, string text) => TypeCore(document, text, HostSource);

    /// <inheritdoc />
    public bool Select(IEditorDocument document, SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not OpenDocument { IsClosed: false } open || open.Host != this)
        {
            return false;
        }

        int length = open.Document.Buffer.Current.Length;
        var clamped = new SelectionState(Math.Clamp(selection.Anchor, 0, length), Math.Clamp(selection.Caret, 0, length));
        UpdateSelection(open, clamped);
        SelectionRequested?.Invoke(this, new EditorSelectionRequestEventArgs(open, clamped));
        return true;
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
    /// runs its command; the first chord of a two-key chord ("Ctrl+K Ctrl+C", any layer's) waits for the second, which
    /// then runs that chord's command (a second key no layer binds ends the wait and does nothing); the keymap's binding
    /// (<see cref="CraftOptions.KeyMap"/>) stands in for the registered one of the same keys; a binding that overrides
    /// the editor, or one for the text editor (<see cref="KeyContext.TextEditor"/>, only when <paramref name="editor"/> is
    /// the text editor), runs its command; then the editor's own keys (<paramref name="editor"/>, the
    /// <see cref="EditorModel"/>); then a binding for a chord the editor did not handle. So the built-in keys keep working
    /// and plug-ins add new chords. A user's or a keymap's binding with an empty command removes the chord's binding
    /// below it (the editor's own key stays). A terminal (any <paramref name="editor"/> that is not the text editor) gets
    /// no two-key wait and no text-editor binding: its keys stay the shell's. No string is made per keystroke.
    /// </summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
    {
        if (key == EditorKey.None)
        {
            return false;
        }

        var chord = new KeyChord(key, modifiers);
        if (!_keyPressed.IsEmpty)
        {
            Raise(_keyPressed, new EditorKeyEventArgs(chord), nameof(KeyPressed));
        }

        if (KeyCapture is { } capture && capture(chord))
        {
            return true;
        }

        bool textEditor = editor is EditorModel;
        if (_pendingChord is { } first)
        {
            SetPending(null);
            if (ResolveSecond(first, chord) is { Length: > 0 } command)
            {
                Run(command);
            }
            else
            {
                Log(HostSource, EditorLogLevel.Information, $"{first.DisplayText} {chord.DisplayText} is not bound to a command.", null);
            }

            return true;
        }

        if (_userBindings.TryGetValue((chord, default), out var userCommand))
        {
            if (userCommand.Length == 0)
            {
                return editor?.OnKey(key, modifiers) ?? false;
            }

            if (textEditor || !IsTextEditorCommand(userCommand))
            {
                Run(userCommand);
                return true;
            }
        }

        if ((editor is null || textEditor) && (_userPrefixes.Contains(chord) || _keyMapPrefixes.Contains(chord) || EditorKeyBindings.IsPrefix(chord)))
        {
            SetPending(chord);
            return true;
        }

        var binding = _keyMapBindings.TryGetValue((chord, default), out var mapped) ? mapped : EditorKeyBindings.Find(chord);
        if (binding is { CommandId.Length: > 0 } && (binding.OverridesEditor || (binding.When == KeyContext.TextEditor && textEditor)))
        {
            Run(binding.CommandId);
            return true;
        }

        if (editor?.OnKey(key, modifiers) == true)
        {
            return true;
        }

        if (binding is { CommandId.Length: > 0, When: KeyContext.Anywhere })
        {
            Run(binding.CommandId);
            return true;
        }

        return false;
    }

    /// <summary>Ends a two-key chord's wait (Escape, the focus moved); true when one was waiting.</summary>
    public bool CancelPendingChord()
    {
        if (_pendingChord is null)
        {
            return false;
        }

        SetPending(null);
        return true;
    }

    // The command of first-then: the user's, else the keymap's, else the registered one; empty or null when unbound.
    private string? ResolveSecond(KeyChord first, KeyChord then)
    {
        if (_userBindings.TryGetValue((first, then), out var user))
        {
            return user;
        }

        if (_keyMapBindings.TryGetValue((first, then), out var mapped))
        {
            return mapped.CommandId;
        }

        return EditorKeyBindings.Find(first, then)?.CommandId;
    }

    // Whether a command's default binding answers in the text editor only (a user's binding of it then does too).
    private bool IsTextEditorCommand(string commandId)
    {
        var all = EditorKeyBindings.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].When == KeyContext.TextEditor && string.Equals(all[i].CommandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var binding in _keyMapBindings.Values)
        {
            if (binding.When == KeyContext.TextEditor && string.Equals(binding.CommandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SetPending(KeyChord? chord)
    {
        _pendingChord = chord;
        PendingChordChanged?.Invoke(this, EventArgs.Empty);
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
    public void Dispatch(Action action) => Dispatch("host: dispatch", action);

    /// <inheritdoc />
    public void Dispatch(string label, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        string named = string.IsNullOrWhiteSpace(label) ? "host: dispatch" : label;
        _dispatch(() => Labels.Run(named, action));
    }

    /// <inheritdoc />
    public IEditorNotification Notify(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null) =>
        Notify(HostSource, title, text, severity, actions);

    /// <inheritdoc />
    public IEditorNotification ShowStatus(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null) =>
        Notifications.ShowStatus(HostSource, title, text, severity, actions);

    /// <summary>Posts a message under <paramref name="source"/>, logged too (the Output tab keeps every one).</summary>
    public IEditorNotification Notify(string source, string title, string text, NotificationSeverity severity, IReadOnlyList<NotificationAction>? actions)
    {
        var level = severity switch
        {
            NotificationSeverity.Error => EditorLogLevel.Error,
            NotificationSeverity.Warning => EditorLogLevel.Warning,
            _ => EditorLogLevel.Information,
        };
        Dispatch("notify: log", () => Log(source, level, $"{title}: {text}", null));    // any thread: the log is the UI thread's
        return Notifications.Post(source, title, text, severity, actions);
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
        Notifications.Dispose();
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

    // The user's own text typed at the document's caret over its selection: one undo step, the caret after it.
    internal bool TypeCore(IEditorDocument document, string text, string source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);
        if (document is not OpenDocument { IsClosed: false } open || open.Host != this)
        {
            Log(source, EditorLogLevel.Warning, "Typed text for a document that is not open was dropped.", null);
            return false;
        }

        var snapshot = open.Document.Buffer.Current;
        var before = open.Selection;
        int start = Math.Clamp(before.Start, 0, snapshot.Length);
        int end = Math.Clamp(before.End, start, snapshot.Length);
        if (text.Length == 0 && start == end)
        {
            return false;
        }

        var after = SelectionState.At(start + text.Length);
        open.Document.Edit([new TextEdit(start, end - start, text)], EditKind.Command, before, after);
        UpdateSelection(open, after);
        SelectionRequested?.Invoke(this, new EditorSelectionRequestEventArgs(open, after));
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
        var item = new EditorProgressItem(this, title, source, ActivityCenter.Start(title, ActivityKinds.General, false, CancellationToken.None, true, source));
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
