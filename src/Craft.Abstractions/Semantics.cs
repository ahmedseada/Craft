// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;

namespace Craft.Abstractions;

/// <summary>
/// What a language session is told when it opens: the file it edits (null for a new document), the workspace folder it
/// belongs to (null when the editor has none: the language then finds the file's project itself), and the settings of
/// the semantic layer. The host makes it from its options (Craft.Core's <c>CraftOptions.LanguageSettings</c>).
/// </summary>
/// <param name="FilePath">The document's file, as a full path, or null for a document not saved yet.</param>
/// <param name="WorkspaceRoot">The open folder, as a full path, or null.</param>
/// <param name="Settings">The semantic layer's settings.</param>
public sealed record LanguageOpenContext(string? FilePath, string? WorkspaceRoot, LanguageSettings Settings)
{
    /// <summary>No file, no workspace, the default settings.</summary>
    public static LanguageOpenContext None { get; } = new(null, null, LanguageSettings.Default);
}

/// <summary>
/// The semantic layer's settings, carried from the host's options (each one a <c>CRAFT_*</c> variable there) to the
/// language, which takes nothing from the environment itself.
/// </summary>
public sealed record LanguageSettings
{
    /// <summary>The defaults.</summary>
    public static LanguageSettings Default { get; } = new();

    /// <summary>Milliseconds after the last change before diagnostics are computed again (<c>CRAFT_DIAGNOSTICS_DELAY_MS</c>).</summary>
    public int DiagnosticsDelayMilliseconds { get; init; } = 400;

    /// <summary>
    /// Above this many characters a document's semantic layer does not run on its own (no automatic diagnostics, no
    /// completion while typing) until it is asked (<see cref="ISemanticLanguageSession.EnableSemantics"/>, a command);
    /// 0 never waits (<c>CRAFT_SEMANTIC_MAX_CHARS</c>).
    /// </summary>
    public int SemanticMaxCharacters { get; init; } = 1_000_000;

    /// <summary>The workspace readers tried in order, by name in <see cref="WorkspaceReaders"/> (<c>CRAFT_WORKSPACE_READERS</c>).</summary>
    public IReadOnlyList<string> WorkspaceReaders { get; init; } = ["csproj", "loose"];

    /// <summary>The build configuration a project is read for ("Debug" defines DEBUG) (<c>CRAFT_WORKSPACE_CONFIGURATION</c>).</summary>
    public string Configuration { get; init; } = "Debug";

    /// <summary>
    /// Whether the projects' source generators run in the semantic model (the analyzers' generators only, never their
    /// diagnostics), so generated code counts as the build counts it (<c>CRAFT_SOURCE_GENERATORS</c>).
    /// </summary>
    public bool SourceGenerators { get; init; } = true;

    /// <summary>
    /// Milliseconds after the last change (or the first paint of new lines) before the visible lines are classified by
    /// meaning, off the UI thread (type, member, local and parameter names); -1 turns semantic colours off
    /// (<c>CRAFT_SEMANTIC_COLORS_DELAY_MS</c>).
    /// </summary>
    public int SemanticColorsDelayMilliseconds { get; init; } = 30;

    /// <summary>The columns of an indentation step, for formatting (the editor's tab size).</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Whether formatting indents with spaces (the editor's setting).</summary>
    public bool InsertSpaces { get; init; } = true;

    /// <summary>The line break formatting inserts, or null to follow the document.</summary>
    public string? NewLine { get; init; }
}

/// <summary>How serious a diagnostic is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Not shown (an unnecessary using, say); kept for the list's filters.</summary>
    Hidden,

    /// <summary>Information (a suggestion).</summary>
    Info,

    /// <summary>A warning.</summary>
    Warning,

    /// <summary>An error: the code does not compile.</summary>
    Error,
}

/// <summary>A problem a language found: where (in the positions of the snapshot it was computed for), how serious, its id and message.</summary>
/// <param name="Start">The first character.</param>
/// <param name="Length">Its characters (0 for a point, such as a missing semicolon).</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Id">The language's id ("CS0103").</param>
/// <param name="Message">What is wrong.</param>
public readonly record struct EditorDiagnostic(int Start, int Length, DiagnosticSeverity Severity, string Id, string Message)
{
    /// <summary>The end (exclusive).</summary>
    public int End => Start + Length;
}

/// <summary>A snapshot's diagnostics, in order of position: the snapshot they were computed for travels with them.</summary>
public sealed class DiagnosticSet : IReadOnlyList<EditorDiagnostic>
{
    private readonly EditorDiagnostic[] _items;

    /// <summary>The diagnostics <paramref name="items"/> of <paramref name="snapshot"/> (sorted here by position).</summary>
    public DiagnosticSet(ITextSnapshot? snapshot, IEnumerable<EditorDiagnostic> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Snapshot = snapshot;
        _items = [.. items];
        Array.Sort(_items, static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length.CompareTo(b.Length));
        foreach (var item in _items)
        {
            switch (item.Severity)
            {
                case DiagnosticSeverity.Error:
                    Errors++;
                    break;
                case DiagnosticSeverity.Warning:
                    Warnings++;
                    break;
                case DiagnosticSeverity.Info:
                    Infos++;
                    break;
            }
        }
    }

    /// <summary>No diagnostics, of no snapshot.</summary>
    public static DiagnosticSet Empty { get; } = new(null, []);

    /// <summary>The snapshot they are of (null for <see cref="Empty"/>).</summary>
    public ITextSnapshot? Snapshot { get; }

    /// <summary>The errors among them.</summary>
    public int Errors { get; }

    /// <summary>The warnings among them.</summary>
    public int Warnings { get; }

    /// <summary>The informational ones among them.</summary>
    public int Infos { get; }

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public EditorDiagnostic this[int index] => _items[index];

    /// <summary>The diagnostics as a span.</summary>
    public ReadOnlySpan<EditorDiagnostic> Items => _items;

    /// <inheritdoc />
    public IEnumerator<EditorDiagnostic> GetEnumerator() => ((IEnumerable<EditorDiagnostic>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>Why completion was asked for.</summary>
public enum CompletionTriggerKind
{
    /// <summary>The user asked (Ctrl+Space): every item at the position.</summary>
    Invoke,

    /// <summary>A character was typed (<see cref="CompletionTrigger.Character"/>): the language decides whether a list is due.</summary>
    Typing,

    /// <summary>A character was deleted.</summary>
    Deletion,
}

/// <summary>Why completion was asked for, and the character typed when it was typing.</summary>
/// <param name="Kind">Invoked, typing or deletion.</param>
/// <param name="Character">The character typed or deleted ('\0' when invoked).</param>
public readonly record struct CompletionTrigger(CompletionTriggerKind Kind, char Character = '\0')
{
    /// <summary>Asked for by the user.</summary>
    public static CompletionTrigger Invoke => new(CompletionTriggerKind.Invoke);

    /// <summary>After <paramref name="c"/> was typed.</summary>
    public static CompletionTrigger Typed(char c) => new(CompletionTriggerKind.Typing, c);
}

/// <summary>What a completion item names (for its icon and the list's filters).</summary>
public enum CompletionItemKind
{
    /// <summary>Anything else.</summary>
    Other,

    /// <summary>A keyword.</summary>
    Keyword,

    /// <summary>A namespace.</summary>
    Namespace,

    /// <summary>A class or a record.</summary>
    Class,

    /// <summary>A struct or a record struct.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>An enum member.</summary>
    EnumMember,

    /// <summary>A delegate.</summary>
    Delegate,

    /// <summary>A method.</summary>
    Method,

    /// <summary>An extension method.</summary>
    ExtensionMethod,

    /// <summary>A property.</summary>
    Property,

    /// <summary>A field.</summary>
    Field,

    /// <summary>A constant.</summary>
    Constant,

    /// <summary>An event.</summary>
    Event,

    /// <summary>A local variable.</summary>
    Local,

    /// <summary>A parameter.</summary>
    Parameter,

    /// <summary>A type parameter.</summary>
    TypeParameter,

    /// <summary>A snippet.</summary>
    Snippet,
}

/// <summary>One completion item.</summary>
/// <param name="Label">What the list shows.</param>
/// <param name="Kind">What it names.</param>
/// <param name="Detail">A short description beside the label (its type, say), or null.</param>
/// <param name="Edit">The edit that commits it, in the positions of the snapshot the list was made for (it replaces the typed word up to the caret, and may reach further).</param>
/// <param name="FilterText">What typing is matched against.</param>
/// <param name="SortText">What the list is ordered by.</param>
/// <param name="CaretAfter">Where the caret goes after the edit (in the edited text), or -1 for the end of the inserted text.</param>
/// <param name="NeedsResolve">
/// Whether <paramref name="Edit"/> is only the plain insertion and the exact one (an override's body, a using added) comes
/// from <see cref="ISemanticLanguageSession.ResolveCompletionAsync"/>, asked for the item being committed only.
/// </param>
/// <param name="Data">The language's own handle on the item (what it resolves it from); the editor never reads it.</param>
public sealed record CompletionItem(string Label, CompletionItemKind Kind, string? Detail, TextEdit Edit, string FilterText, string SortText, int CaretAfter = -1,
    bool NeedsResolve = false, object? Data = null);

/// <summary>A completion list: its items (in their sort order), and the span of the word being completed.</summary>
/// <param name="Snapshot">The snapshot it was made for.</param>
/// <param name="Position">The caret it was made at.</param>
/// <param name="SpanStart">Where the word being completed starts (the filter is the text from here to the caret).</param>
/// <param name="Items">The items, sorted.</param>
public sealed record CompletionList(ITextSnapshot Snapshot, int Position, int SpanStart, IReadOnlyList<CompletionItem> Items);

/// <summary>A parameter of a signature: its place in the signature's label and its documentation.</summary>
/// <param name="Name">The parameter's name.</param>
/// <param name="Start">Where it starts in <see cref="SignatureInfo.Label"/>.</param>
/// <param name="Length">Its characters there.</param>
/// <param name="Documentation">Its documentation, or null.</param>
public sealed record SignatureParameter(string Name, int Start, int Length, string? Documentation);

/// <summary>One signature of a call: its label, documentation and parameters.</summary>
/// <param name="Label">The signature as text ("void Console.WriteLine(string? value)").</param>
/// <param name="Documentation">Its summary, or null.</param>
/// <param name="Parameters">Its parameters, in order.</param>
public sealed record SignatureInfo(string Label, string? Documentation, IReadOnlyList<SignatureParameter> Parameters);

/// <summary>The signatures of the call around the caret, the one that applies and the argument being typed.</summary>
/// <param name="Signatures">The candidates (the overloads).</param>
/// <param name="ActiveSignature">The index of the one that fits the arguments best.</param>
/// <param name="ActiveParameter">The index of the argument the caret is in.</param>
/// <param name="ArgumentsStart">Where the argument list starts (just after its opening parenthesis), in the snapshot asked for.</param>
public sealed record SignatureHelp(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter, int ArgumentsStart);

/// <summary>What the language says about the text at a position: plain text (a few lines, markdown-ish) and the span it is about.</summary>
/// <param name="Text">The description: the symbol's signature, then its summary.</param>
/// <param name="Start">The span it describes (the identifier).</param>
/// <param name="Length">Its characters.</param>
public sealed record HoverInfo(string Text, int Start, int Length);

/// <summary>
/// Where a symbol is defined: a span of a source file (<see cref="FilePath"/>; null with <see cref="IsSameDocument"/> for
/// the document asked about when it has no file), or, for a symbol only in metadata (a referenced assembly), a
/// <see cref="Description"/> and no file.
/// </summary>
/// <param name="FilePath">The source file, or null.</param>
/// <param name="Start">The definition's name, in that file's text.</param>
/// <param name="Length">Its characters.</param>
/// <param name="IsSameDocument">Whether it is the document asked about (the caret just moves).</param>
/// <param name="Description">The symbol's description (always set; the only answer for a metadata symbol).</param>
public sealed record DefinitionLocation(string? FilePath, int Start, int Length, bool IsSameDocument, string Description)
{
    /// <summary>Whether it is only in metadata (no source to open).</summary>
    public bool IsMetadata => FilePath is null && !IsSameDocument;
}

/// <summary>What a language's semantic layer holds now: its workspace's size and its last costs (telemetry).</summary>
/// <param name="WorkspaceRoot">The workspace it belongs to, or null before one is built.</param>
/// <param name="Reader">The workspace reader that made it ("csproj", "loose").</param>
/// <param name="Projects">Its projects.</param>
/// <param name="Documents">Its source documents.</param>
/// <param name="References">Its metadata references.</param>
/// <param name="ReferenceBytes">The bytes of the referenced files (read once, shared by every workspace).</param>
/// <param name="HeapBytes">The managed heap the workspace's build added, as measured around it (an estimate: other threads allocate too).</param>
/// <param name="BuildTime">What building the workspace cost.</param>
/// <param name="LastDiagnosticsTime">What the last diagnostics pass cost.</param>
/// <param name="Generation">How many times the workspace was built (it is rebuilt after <see cref="ISemanticLanguageSession.Release"/>).</param>
public sealed record SemanticTelemetry(string? WorkspaceRoot, string? Reader, int Projects, int Documents, int References, long ReferenceBytes,
    long HeapBytes, TimeSpan BuildTime, TimeSpan LastDiagnosticsTime, int Generation)
{
    /// <summary>Nothing built yet.</summary>
    public static SemanticTelemetry None { get; } = new(null, null, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, 0);
}

/// <summary>
/// The semantic layer of a language session, for languages that have one (a session that only classifies need not
/// implement it). Everything here runs off the UI thread and never blocks it: each request is a task that the caller
/// awaits or drops, cancelled by its token; diagnostics are computed on the session's own worker after a pause in the
/// changes (<see cref="LanguageSettings.DiagnosticsDelayMilliseconds"/>), cancelled by the next change, and announced
/// by <see cref="DiagnosticsChanged"/> on that worker's thread (the subscriber marshals to its UI thread). Requests may be
/// made from any thread with any snapshot of the session's buffer; the answer's positions are that snapshot's.
/// </summary>
public interface ISemanticLanguageSession : ILanguageSession
{
    /// <summary>The last diagnostics computed, with the snapshot they are of (possibly older than the buffer's current one).</summary>
    DiagnosticSet Diagnostics { get; }

    /// <summary>
    /// Raised when <see cref="Diagnostics"/> changed, on the session's worker thread, never the UI thread: the subscriber
    /// marshals to its UI thread before it reads them and redraws.
    /// </summary>
    event EventHandler? DiagnosticsChanged;

    /// <summary>Whether the document is too large for the layer to run on its own and it waits for <see cref="EnableSemantics"/>.</summary>
    bool IsSemanticDeferred { get; }

    /// <summary>The workspace's size and the last costs.</summary>
    SemanticTelemetry Telemetry { get; }

    /// <summary>Lets the layer run on its own for this document even when it is large (a command's answer), and computes its diagnostics now.</summary>
    void EnableSemantics();

    /// <summary>The completion list at <paramref name="position"/> of <paramref name="snapshot"/>, or null when there is none (a typed character that starts none, say).</summary>
    Task<CompletionList?> GetCompletionsAsync(ITextSnapshot snapshot, int position, CompletionTrigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="item"/> of <paramref name="list"/> with its exact edit (and caret), in the positions of the list's
    /// snapshot, for an item that <see cref="CompletionItem.NeedsResolve"/>; the item itself for one that does not.
    /// </summary>
    Task<CompletionItem> ResolveCompletionAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken);

    /// <summary>The signatures of the call whose arguments hold <paramref name="position"/>, or null.</summary>
    Task<SignatureHelp?> GetSignatureHelpAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken);

    /// <summary>What the symbol at <paramref name="position"/> is, or null.</summary>
    Task<HoverInfo?> GetHoverAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken);

    /// <summary>Where the symbol at <paramref name="position"/> is defined, or null when there is no symbol there.</summary>
    Task<DefinitionLocation?> GetDefinitionAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken);

    /// <summary>
    /// The edits that format <paramref name="snapshot"/> (the range of <paramref name="start"/> and <paramref name="length"/>
    /// when <paramref name="length"/> is not negative, the whole document otherwise), in its positions, in order, not
    /// overlapping: applied together as one change.
    /// </summary>
    Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the memory the layer holds (its workspace: the compilations, trees and references), as the host does on
    /// the platform's memory-pressure signal; the next request or change builds it again. Any thread.
    /// </summary>
    void Release();

    /// <summary>
    /// The diagnostics <paramref name="snapshot"/> would have with <paramref name="edits"/> applied (in its positions, in
    /// order, not overlapping), without changing the document: the diff view's "before you accept" check. The positions
    /// are the changed text's. Null when the layer cannot tell (the default).
    /// </summary>
    Task<DiagnosticSet?> DiagnoseEditsAsync(ITextSnapshot snapshot, IReadOnlyList<TextEdit> edits, CancellationToken cancellationToken) =>
        Task.FromResult<DiagnosticSet?>(null);
}
