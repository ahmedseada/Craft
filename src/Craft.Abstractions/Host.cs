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

/// <summary>Long work a plug-in shows (in the status bar): report as it goes, dispose when it is done.</summary>
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
/// <see cref="Dispatch"/> from another thread). A plug-in gets it in <see cref="IEditorPlugin.Activate"/>.
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
    /// Opens <paramref name="path"/> (a folder or a file) outside the editor, in the system's own way (a folder in the file
    /// manager); false when the host cannot (a plug-in then shows the path instead).
    /// </summary>
    Task<bool> OpenExternalAsync(string path) => Task.FromResult(false);

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
    /// The open document of the file <paramref name="path"/>, opening it in the editor when it is not open (a path with no
    /// file yet opens a new, empty document the user saves there); null when the host cannot open documents or the file
    /// cannot be read. A plug-in that proposes edits to a closed file (the agent's) opens it this way first.
    /// </summary>
    Task<IEditorDocument?> OpenDocumentAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IEditorDocument?>(null);

    /// <summary>
    /// Brings the panel <paramref name="panelId"/> (one the plug-in added) into view: the host shows its area (the chat
    /// column, the sheet in the narrow layout) and selects it. A host that cannot does nothing.
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

    /// <summary>Shows <paramref name="panel"/> in the side area until the result is disposed (or the plug-in is deactivated).</summary>
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
}
