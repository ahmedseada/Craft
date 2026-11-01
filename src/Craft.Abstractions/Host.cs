// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>An open document as a plug-in sees it: its text document, its file, its language and its selection.</summary>
public interface IEditorDocument
{
    /// <summary>The document (its buffer, undo history and file). A plug-in reads it; it changes it only through <see cref="IEditorHost.ProposeEditsAsync"/>.</summary>
    ITextDocument Document { get; }

    /// <summary>Its file, or null for a new document.</summary>
    string? FilePath { get; }

    /// <summary>Its language's name ("csharp"), or the plain-text name the editor shows.</summary>
    string LanguageName { get; }

    /// <summary>Its language, or null for plain text.</summary>
    ILanguage? Language { get; }

    /// <summary>Its selection, as the editor last reported it.</summary>
    SelectionState Selection { get; }

    /// <summary>
    /// Its language's last diagnostics as the editor shows them (in the positions of <see cref="DiagnosticSet.Snapshot"/>);
    /// empty when its language has no semantic layer or the host was not told any.
    /// </summary>
    DiagnosticSet Diagnostics => DiagnosticSet.Empty;

    /// <summary>Its language's semantic session (symbols, completion, diagnostics; any thread), or null when it has none or the host was not told it.</summary>
    ISemanticLanguageSession? SemanticSession => null;
}

/// <summary>An event about one open document.</summary>
public class EditorDocumentEventArgs(IEditorDocument document) : EventArgs
{
    /// <summary>The document.</summary>
    public IEditorDocument Document { get; } = document;
}

/// <summary>A document's text changed: the change as its buffer reported it (the snapshots before and after, and the edits).</summary>
public sealed class EditorDocumentChangedEventArgs(IEditorDocument document, TextChangedEventArgs change) : EditorDocumentEventArgs(document)
{
    /// <summary>The change.</summary>
    public TextChangedEventArgs Change { get; } = change;
}

/// <summary>Edits a plug-in proposes (<see cref="IEditorHost.ProposeAsync"/>), with what the diff view shows beside them.</summary>
/// <param name="Edits">The edits, in the positions of the document's current snapshot, in order, not overlapping.</param>
/// <param name="Title">What they do ("Add doc comments"): the undo step's name.</param>
public sealed record ProposedEdits(IReadOnlyList<TextEdit> Edits, string Title)
{
    /// <summary>How the edits were placed ("exact text match", "csharp-tokens · whitespace differed"), or null.</summary>
    public string? Placement { get; init; }

    /// <summary>Where they came from ("Chat · store:coder-q4", "MCP client"), or null for the plug-in's name.</summary>
    public string? Origin { get; init; }
}

/// <summary>A key that reached the editor (<see cref="IEditorHost.KeyPressed"/>): its chord.</summary>
public sealed class EditorKeyEventArgs(KeyChord chord) : EventArgs
{
    /// <summary>The key and its modifiers.</summary>
    public KeyChord Chord { get; } = chord;
}

/// <summary>How much a log line matters.</summary>
public enum EditorLogLevel
{
    /// <summary>Detail for debugging.</summary>
    Debug,

    /// <summary>Something that happened.</summary>
    Information,

    /// <summary>Something that went wrong and was worked around.</summary>
    Warning,

    /// <summary>Something that failed.</summary>
    Error,
}

/// <summary>Long work a plug-in shows (in the status bar, as an activity of <see cref="IEditorHost.Activities"/>): report as it goes, dispose when it is done.</summary>
public interface IEditorProgress : IDisposable
{
    /// <summary>What it is doing.</summary>
    string Title { get; }

    /// <summary>How far, from 0 to 1, or null when not known; and what it does now.</summary>
    void Report(double? fraction, string? message = null);
}

/// <summary>
/// What a plug-in reaches: the open documents and their events, the active document's selection, edits proposed to the
/// user (never applied silently), commands, status bar items, panels, its settings, progress, the log and telemetry, and
/// the UI thread. Every event is raised on the host's UI thread, and every member is called there (use
/// <see cref="Dispatch(Action)"/> from another thread). A plug-in gets it in <see cref="IEditorPlugin.Activate"/>.
/// </summary>
public interface IEditorHost
{
    /// <summary>The open documents, in the order they opened.</summary>
    IReadOnlyList<IEditorDocument> Documents { get; }

    /// <summary>The document the user edits now, or null.</summary>
    IEditorDocument? ActiveDocument { get; }

    /// <summary>The active document's selection (a caret at 0 when there is none).</summary>
    SelectionState Selection { get; }

    /// <summary>The folder open in the editor (its full path), or null when none is.</summary>
    string? WorkspaceFolder => null;

    /// <summary>
    /// The integrated terminals (phase T): a plug-in opens one and writes to it, never reads what the user types; null when
    /// the host has none.
    /// </summary>
    IEditorTerminals? Terminals => null;

    /// <summary>
    /// The app's data folder (its full path: settings, recovery copies, what plug-ins keep for the user), or null when the
    /// host has none; a plug-in keeps its own files in a folder of its name under it.
    /// </summary>
    string? DataFolder => null;

    /// <summary>
    /// What the app knows about itself (its version, its reports, the crash trail, Output, its telemetry), for a plug-in
    /// that diagnoses it (the Claude Code plug-in's MCP tools); null when the host keeps none.
    /// </summary>
    IEditorDiagnostics? Diagnostics => null;

    /// <summary>
    /// Opens <paramref name="path"/> (a folder or a file) outside the editor, in the system's own way (a folder in the file
    /// manager); false when the host cannot (a plug-in then shows the path instead).
    /// </summary>
    Task<bool> OpenExternalAsync(string path) => Task.FromResult(false);

    /// <summary>
    /// Asks the user for a folder with the system's folder dialog, starting in <paramref name="startFolder"/>; the folder
    /// picked (its full path), or null when the user cancelled or the host has no dialog (the default).
    /// </summary>
    Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>
    /// Shows the shell's inline bar with Undo after a change the plug-in has already made and can take back (B11: delete
    /// with Undo instead of a confirmation): <paramref name="text"/> says what happened, <paramref name="undo"/> takes it
    /// back when the user presses Undo, <paramref name="kept"/> runs when the bar goes without an Undo (its time is up,
    /// another bar takes its place, the window closes), so a change held for Undo is made for good then. Both run on the UI
    /// thread. False when the host has no such bar (the default): the plug-in asks before the change instead.
    /// </summary>
    bool ShowUndo(string text, Action undo, Action? kept = null) => false;

    /// <summary>A document opened.</summary>
    event EventHandler<EditorDocumentEventArgs>? DocumentOpened;

    /// <summary>A document's text changed (typed, undone, an accepted edit, a reload): raised once per change, right after it.</summary>
    event EventHandler<EditorDocumentChangedEventArgs>? DocumentChanged;

    /// <summary>A document was saved to its file.</summary>
    event EventHandler<EditorDocumentEventArgs>? DocumentSaved;

    /// <summary>A document closed (its owner may dispose its text document once this returns).</summary>
    event EventHandler<EditorDocumentEventArgs>? DocumentClosed;

    /// <summary><see cref="ActiveDocument"/> changed.</summary>
    event EventHandler? ActiveDocumentChanged;

    /// <summary>A document's selection changed.</summary>
    event EventHandler<EditorDocumentEventArgs>? SelectionChanged;

    /// <summary>
    /// Proposes <paramref name="edits"/> to <paramref name="document"/> (in the positions of its current snapshot, in order,
    /// non-overlapping) under <paramref name="title"/>. The user sees them and accepts or rejects; accepted, they are
    /// applied as one undo step (<see cref="EditKind.Command"/>). True when they were accepted and applied; false when
    /// rejected, when the document changed or closed meanwhile, or when the host has no way to ask the user. There is no
    /// other way for a plug-in to edit.
    /// </summary>
    /// <exception cref="ArgumentException">An edit runs outside the text, or the edits overlap or are out of order.</exception>
    Task<bool> ProposeEditsAsync(IEditorDocument document, IReadOnlyList<TextEdit> edits, string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="ProposeEditsAsync"/> with what the diff view shows beside the edits (how they were placed, where they
    /// came from). The user may accept some of the hunks only: those are applied, as one undo step, and the result is true.
    /// </summary>
    /// <exception cref="ArgumentException">An edit runs outside the text, or the edits overlap or are out of order.</exception>
    Task<bool> ProposeAsync(IEditorDocument document, ProposedEdits proposal, CancellationToken cancellationToken = default) =>
        ProposeEditsAsync(document, (proposal ?? throw new ArgumentNullException(nameof(proposal))).Edits, proposal.Title, cancellationToken);

    /// <summary>
    /// Proposes <paramref name="edit"/> (edits to several files: a rename across the workspace) under
    /// <paramref name="title"/> as one diff that lists every file, with Accept all. Accepted, each open document takes its
    /// edits as one undo step, and each file that is not open is opened in the background, edited and saved; the shell then
    /// offers Undo for the whole change. True when the user accepted and it was applied; false when rejected, when an open
    /// document changed since the edit was made (<see cref="WorkspaceFileEdit.Version"/>), when the edit has a
    /// <see cref="WorkspaceEdit.Refusal"/> or no file, or when the host cannot ask the user (the default). Contract 1.1.
    /// </summary>
    Task<bool> ProposeWorkspaceAsync(WorkspaceEdit edit, string title, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// The open document of the file <paramref name="path"/>, opening it in the editor when it is not open (a path with no
    /// file yet opens a new, empty document the user saves there); null when the host cannot open documents or the file
    /// cannot be read. A plug-in that proposes edits to a closed file (the agent's) opens it this way first.
    /// </summary>
    Task<IEditorDocument?> OpenDocumentAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IEditorDocument?>(null);

    /// <summary>
    /// Saves <paramref name="document"/> to its file (or to <paramref name="path"/>, which becomes its file) and raises
    /// <see cref="DocumentSaved"/>, so the editor's tab, its language and the folder tree follow at once. A plug-in saves
    /// only what the user accepted to write (the agent's accepted new file); by default the document saves itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no file and no path was given.</exception>
    Task SaveAsync(IEditorDocument document, string? path = null, CancellationToken cancellationToken = default) =>
        (document ?? throw new ArgumentNullException(nameof(document))).Document.SaveAsync(path, cancellationToken);

    /// <summary>
    /// Brings the panel <paramref name="panelId"/> (one the plug-in added) into view: the host shows its area (a
    /// conversation panel in the chat column, or the sheet in the narrow layout; any other panel as the side view of its
    /// rail item, in the explorer's place) and selects it. A host that cannot does nothing.
    /// </summary>
    void ShowPanel(string panelId)
    {
    }

    /// <summary>
    /// Types <paramref name="text"/> into <paramref name="document"/> at its caret, replacing its selection, as the user's
    /// own input: one undo step (<see cref="EditKind.Command"/>), the caret after it, the editor following. It is for an
    /// input method a plug-in brings (dictation: the user says what the keyboard would type), never for text a model
    /// wrote, which goes through <see cref="ProposeAsync"/> as a diff the user accepts. A plug-in that types declares the
    /// capability "input" in its plugin.json. False when the host takes no input from plug-ins or the document is not open.
    /// </summary>
    bool TypeText(IEditorDocument document, string text) => false;

    /// <summary>
    /// Moves <paramref name="document"/>'s selection (clamped to its text) and has its editor show it, scrolled into view:
    /// a caret move ("go to line 40", the next problem), never an edit. False when the host cannot.
    /// </summary>
    bool Select(IEditorDocument document, SelectionState selection) => false;

    /// <summary>
    /// Runs the one test <paramref name="testName"/> (a fully qualified name, "App.Tests.MathTests.Adds") of the project
    /// that holds <paramref name="filePath"/> through the Run view's providers (<see cref="IRunProvider.TestCommand"/>):
    /// asked of the user and run in the registered sandbox like any run, its output in its own terminal tab (X part 9: a
    /// code lens's Run test). False when the host has no Run view or no provider can run that test (contract 1.1).
    /// </summary>
    Task<bool> RunTestAsync(string filePath, string testName, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// The panels the plug-ins added, in order: a plug-in reads another's (the last reply of a chat, through
    /// <see cref="IConversationPanel"/>) without referencing it. None by default.
    /// </summary>
    IReadOnlyList<IEditorPanel> Panels => [];

    /// <summary>
    /// A key reached the editor: raised before the key is routed, and the key still does what it does (a plug-in that
    /// stops its work on any key, as reading aloud does, listens only while that work runs). Never by default.
    /// </summary>
    event EventHandler<EditorKeyEventArgs>? KeyPressed
    {
        add { }
        remove { }
    }

    /// <summary>The registered commands (<see cref="EditorCommands"/>), in order.</summary>
    IReadOnlyList<IEditorCommand> Commands { get; }

    /// <summary>
    /// Runs the command <paramref name="commandId"/> when it can run; true when it ran to the end. A command that is not
    /// registered, cannot run now, or throws is logged and gives false.
    /// </summary>
    Task<bool> ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default);

    /// <summary>Shows <paramref name="item"/> in the status bar until the result is disposed (or the plug-in is deactivated).</summary>
    IDisposable AddStatusItem(IStatusItem item);

    /// <summary>
    /// Shows <paramref name="panel"/> until the result is disposed (or the plug-in is deactivated): an
    /// <see cref="IConversationPanel"/> as a tab of the chat column; any other panel as a side view of the activity rail
    /// (its own rail item, the panel's title, opens it in the explorer's place; a rail item the plug-in registers under
    /// the panel's id replaces that item). There is no fixed column of panels.
    /// </summary>
    IDisposable AddPanel(IEditorPanel panel);

    /// <summary>The plug-ins' declared settings.</summary>
    IPluginSettings Settings { get; }

    /// <summary>Starts showing long work named <paramref name="title"/>; dispose the result when it ends.</summary>
    IEditorProgress StartProgress(string title);

    /// <summary>Shows a message to the user (a plug-in's answer to a command); completes when the user dismissed it.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Asks the user <paramref name="question"/> and gives the index of the answer picked, or
    /// <see cref="EditorQuestion.Dismissed"/> when the question was dismissed or cancelled; -1 when the host has no way to ask
    /// (the default): a plug-in that needs the user's yes treats that as no.
    /// </summary>
    Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken = default) => Task.FromResult(-1);

    /// <summary>Writes a line to the editor's log, under the plug-in's name.</summary>
    void Log(EditorLogLevel level, string message, Exception? error = null);

    /// <summary>Records a measure (telemetry: "wordcount.update", 0.004, "ms"); free when nobody listens.</summary>
    void Record(string metric, double value, string unit);

    /// <summary>Runs <paramref name="action"/> on the host's UI thread (later, never inline from another thread).</summary>
    void Dispatch(Action action);

    /// <summary>
    /// <see cref="Dispatch(Action)"/> under <paramref name="label"/> ("chat: show the reply"): the UI watchdog names the
    /// label when that work blocks the window. The label alone by default.
    /// </summary>
    void Dispatch(string label, Action action) => Dispatch(action);

    /// <summary>
    /// The running operations (phase U): every operation that can outlast a moment starts one, so the status bar shows it
    /// with its progress, rate, time left and Cancel, and the notification centre keeps how it ended. Any thread. A host
    /// without them gives <see cref="NoActivities.Instance"/>.
    /// </summary>
    IEditorActivities Activities => NoActivities.Instance;

    /// <summary>
    /// Posts a message to the notification centre (the bell's count rises): <paramref name="title"/>, its
    /// <paramref name="text"/>, how much it matters (an error or a question also shows a toast) and its
    /// <paramref name="actions"/> (buttons). Kept for the session, grouped under the plug-in's name, until the user
    /// dismisses it or the result is disposed. Any thread. A host without a centre logs it.
    /// </summary>
    IEditorNotification Notify(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null)
    {
        Log(severity switch
        {
            NotificationSeverity.Error => EditorLogLevel.Error,
            NotificationSeverity.Warning => EditorLogLevel.Warning,
            _ => EditorLogLevel.Information,
        }, $"{title}: {text}");
        return NoNotification.Instance;
    }

    /// <summary>
    /// Shows a status entry in the notification centre: a sticky line at the top of the plug-in's group (a model's state,
    /// the device, a sandbox's last command) that the result updates in place and that stays until it is disposed (or the
    /// plug-in is deactivated); it never raises the bell's count or a toast. Any thread. Nothing by default.
    /// </summary>
    IEditorNotification ShowStatus(string title, string text, NotificationSeverity severity = NotificationSeverity.Information, IReadOnlyList<NotificationAction>? actions = null) =>
        NoNotification.Instance;

    /// <summary>
    /// <see cref="WorkspaceFolder"/> changed (a folder opened or closed), on the UI thread (X part 8: the git plug-in opens
    /// the folder's repository). Never by default.
    /// </summary>
    event EventHandler? WorkspaceFolderChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// The workspace changed (W, contract 10), on the UI thread: a folder opened, closed or took the open one's place
    /// (with <see cref="WorkspaceFolderChanged"/>), or the user asked for it to be read again (the explorer's Refresh), so a
    /// plug-in that keeps what it found in the folder (the npm plug-in's scripts) finds it again. The arguments say which
    /// folder it was and is. Never by default.
    /// </summary>
    event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Shows <paramref name="marks"/> in the gutter and on the overview ruler of every editor of <paramref name="document"/>
    /// (X part 8: the lines added, changed and removed against the repository's index), in place of the marks
    /// <paramref name="source"/> set before; an empty list takes them away. False when the host draws no marks (the default).
    /// </summary>
    bool SetLineMarks(IEditorDocument document, string source, IReadOnlyList<LineMark> marks) => false;

    /// <summary>A gutter mark a plug-in set was clicked (on the UI thread). Never by default.</summary>
    event EventHandler<LineMarkEventArgs>? LineMarkClicked
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Shows <paramref name="card"/> under its line in the editor of <paramref name="document"/> the user last clicked a
    /// mark of (else its focused one): a change inline with Revert and Stage, a conflict's choices. Its actions run only
    /// when the user presses them; one that changes the document still proposes the edit (<see cref="ProposeAsync"/>).
    /// False when the host cannot (the default).
    /// </summary>
    bool ShowInlineCard(IEditorDocument document, InlineCard card) => false;

    /// <summary>
    /// Publishes <paramref name="repository"/> until the result is disposed (or the plug-in is deactivated): the shell shows
    /// its branch, ahead and behind in the status bar and colours the folder tree's names by their state. Nothing by default.
    /// </summary>
    IDisposable AddRepository(ISourceControlRepository repository) => NoRepository.Instance;

    /// <summary>
    /// Shows the change from <paramref name="before"/> to <paramref name="after"/> of the file <paramref name="path"/> in
    /// the diff view, read-only (X part 8: a changed file's working copy against the index or the last commit), under
    /// <paramref name="title"/>; completes when the user closed it. False when the host has no diff view (the default).
    /// </summary>
    Task<bool> ShowDiffAsync(string title, string path, string before, string after, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// <see cref="ShowDiffAsync(string, string, string, string, CancellationToken)"/> with <paramref name="notes"/> under
    /// their lines (X part 11 item 4: a review's findings on the changes). By default the diff alone.
    /// </summary>
    Task<bool> ShowDiffAsync(string title, string path, string before, string after, IReadOnlyList<DiffNote> notes, CancellationToken cancellationToken = default) =>
        ShowDiffAsync(title, path, before, after, cancellationToken);

    /// <summary>
    /// Puts <paramref name="text"/> on the system's clipboard (a plug-in's Copy all: a review's findings); false when the
    /// host reaches no clipboard (the default).
    /// </summary>
    bool CopyText(string text) => false;

    /// <summary>
    /// Publishes <paramref name="items"/> as <paramref name="source"/>'s diagnostics of the file <paramref name="path"/> (W,
    /// contract 6: a linter's findings), in place of what that source published for it before; none takes them away. The
    /// items are in the lines and columns of <paramref name="snapshot"/> (the open document's text they were found in; the
    /// editor moves them through the edits made since) or, when it is null, of the file as it is on disk. An open document
    /// shows them beside its language's own (squiggles, the line-end message, the lightbulb, its Problems rows), each id
    /// read "source(id)" ("eslint(no-unused-vars)"); a closed file's are listed in Problems by line and column. False when
    /// the host shows no published diagnostics (the default).
    /// </summary>
    bool PublishDiagnostics(string path, string source, ITextSnapshot? snapshot, IReadOnlyList<FileDiagnostic> items) => false;

    /// <summary>Takes every diagnostic <paramref name="source"/> published away, from every file (a linter turned off, another folder opened). Nothing by default.</summary>
    void ClearDiagnostics(string source)
    {
    }
}

/// <summary>How the workspace changed (<see cref="IEditorHost.WorkspaceChanged"/>).</summary>
public enum WorkspaceChangeKind
{
    /// <summary>A folder opened where none was.</summary>
    Opened,

    /// <summary>The folder closed; none is open now.</summary>
    Closed,

    /// <summary>Another folder took the open one's place.</summary>
    Switched,

    /// <summary>The same folder, asked to be read again (the explorer's Refresh): what is on its disk may have changed.</summary>
    Refreshed,
}

/// <summary>The workspace's change: the folder it was, the folder it is (full paths, or null for none), and how.</summary>
/// <param name="previous">The folder open before, or null.</param>
/// <param name="current">The folder open now, or null.</param>
/// <param name="kind">How it changed.</param>
public sealed class WorkspaceChangedEventArgs(string? previous, string? current, WorkspaceChangeKind kind) : EventArgs
{
    /// <summary>The folder open before, or null.</summary>
    public string? Previous { get; } = previous;

    /// <summary>The folder open now, or null.</summary>
    public string? Current { get; } = current;

    /// <summary>How it changed.</summary>
    public WorkspaceChangeKind Kind { get; } = kind;

    /// <summary>The change from <paramref name="previous"/> to <paramref name="current"/>: opened, closed, switched, or (the same folder) refreshed.</summary>
    public static WorkspaceChangedEventArgs Between(string? previous, string? current) => new(previous, current,
        previous is null ? WorkspaceChangeKind.Opened
        : current is null ? WorkspaceChangeKind.Closed
        : string.Equals(previous, current, StringComparison.Ordinal) ? WorkspaceChangeKind.Refreshed
        : WorkspaceChangeKind.Switched);
}

/// <summary>What <see cref="IEditorHost.AddRepository"/> gives when the host shows no repository.</summary>
public sealed class NoRepository : IDisposable
{
    private NoRepository()
    {
    }

    /// <summary>The one instance.</summary>
    public static NoRepository Instance { get; } = new();

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
