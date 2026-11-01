// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>Where go to definition leads outside the document: another file to open at a span, or a symbol of metadata.</summary>
public sealed class DefinitionRequestedEventArgs(DefinitionLocation location) : EventArgs
{
    /// <summary>The definition (its file and span, or its description for a metadata symbol).</summary>
    public DefinitionLocation Location { get; } = location;
}

/// <summary>A tooltip the editor draws: its text and the position it belongs to.</summary>
/// <param name="Text">The text (lines separated by '\n').</param>
/// <param name="Position">The position it is anchored at.</param>
/// <param name="Highlight">A range of <paramref name="Text"/> drawn bold (the active parameter), or (0, 0).</param>
public readonly record struct EditorTooltip(string Text, int Position, (int Start, int Length) Highlight = default)
{
    /// <summary>What it is: plain (signature help, a description), a hover card that stays under the pointer, or a fold's preview.</summary>
    public EditorTooltipKind Kind { get; init; }

    /// <summary>An inline card's actions (its row of buttons), or null.</summary>
    public IReadOnlyList<Craft.Abstractions.InlineCardAction>? Actions { get; init; }
}

public sealed partial class EditorModel
{
    private const int MaxHistory = 4096;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly List<(int Version, TextEdit[] Edits)> _history = [];
    private EditorDiagnostic[] _diagnostics = [];
    private EditorDiagnostic[] _semanticDiagnostics = [];
    private DocumentDiagnostics? _published;

    /// <summary>The diagnostics' array as it is now (replaced, never changed in place: the overview ruler keys on it).</summary>
    internal EditorDiagnostic[] DiagnosticsArray => _diagnostics;
    private int _diagnosticsLongest;
    private DiagnosticSet _appliedSet = DiagnosticSet.Empty;
    private CancellationTokenSource? _signatureRequest;
    private CancellationTokenSource? _hoverRequest;
    private DebounceTimer? _hoverTimer;
    private int _hoverPosition = -1;
    private bool _chordK;
    private int _semanticRequests;

    /// <summary>
    /// Runs an action on the UI thread: the host sets it (EditorView dispatches to MAUI's dispatcher; a test queues). When
    /// null, the synchronization context the model was made on posts it, and without one it runs at once.
    /// </summary>
    public Action<Action>? Dispatch { get; set; }

    /// <summary>The language's semantic layer, or null (plain text, or a language with none).</summary>
    public ISemanticLanguageSession? SemanticSession => _session as ISemanticLanguageSession;

    /// <summary>The completion list's state.</summary>
    public CompletionController Completion => _completion ??= new CompletionController(this);

    private CompletionController? _completion;

    /// <summary>The diagnostics, in the current snapshot's positions, in order (those an edit touched since they were computed are gone).</summary>
    public ReadOnlySpan<EditorDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The errors among <see cref="Diagnostics"/>.</summary>
    public int ErrorCount { get; private set; }

    /// <summary>The warnings among <see cref="Diagnostics"/>.</summary>
    public int WarningCount { get; private set; }

    /// <summary>The signature help shown (on '(' and ','), or null.</summary>
    public SignatureHelp? SignatureHelp { get; private set; }

    /// <summary>The tooltip shown (hover, signature help, a definition's description), or null.</summary>
    public EditorTooltip? Tooltip { get; private set; }

    /// <summary>The semantic results dropped because the text or the caret moved on before they arrived.</summary>
    public long LateResultsDropped { get; internal set; }

    /// <summary>The error the last semantic request threw (the editor went on), or null.</summary>
    public Exception? LastSemanticError { get; internal set; }

    /// <summary>Raised on the UI thread when <see cref="Diagnostics"/> changed (new ones arrived, or an edit moved or dropped some).</summary>
    public event EventHandler? DiagnosticsChanged;

    /// <summary>
    /// Raised on F12 when the definition is in another file (the host opens it and selects the span) or only in metadata
    /// (the host may show the description, which the editor also shows as a tooltip).
    /// </summary>
    public event EventHandler<DefinitionRequestedEventArgs>? DefinitionRequested;

    /// <summary>Raised when a semantic request finished or was dropped (the tests' hook; the status bar may show it).</summary>
    public event EventHandler? SemanticRequestCompleted;

    /// <summary>The semantic requests in flight (completion, signature help, hover, definition, format).</summary>
    public int SemanticRequestsInFlight => Volatile.Read(ref _semanticRequests);

    /// <summary>The diagnostics at <paramref name="position"/> (the problem text the pointer shows), in order.</summary>
    public IReadOnlyList<EditorDiagnostic> DiagnosticsAt(int position)
    {
        var found = new List<EditorDiagnostic>();
        foreach (var diagnostic in _diagnostics)
        {
            if (diagnostic.Start > position)
            {
                break;
            }

            if (position <= diagnostic.End && diagnostic.Severity != DiagnosticSeverity.Hidden)
            {
                found.Add(diagnostic);
            }
        }

        return found;
    }

    /// <summary>The index of the first diagnostic that may reach <paramref name="position"/> (those before it end earlier).</summary>
    internal int FirstDiagnosticReaching(int position)
    {
        int from = position - _diagnosticsLongest;
        int low = 0;
        int high = _diagnostics.Length;
        while (low < high)
        {
            int mid = (low + high) >>> 1;
            if (_diagnostics[mid].Start < from)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>On the UI thread: takes the session's latest diagnostics, mapped to the current snapshot through the edits since theirs.</summary>
    public void ApplyDiagnostics()
    {
        if (_disposed || SemanticSession is not { } session)
        {
            return;
        }

        var set = session.Diagnostics;
        if (ReferenceEquals(set, _appliedSet) || set.Snapshot is not { } of || !ReferenceEquals(of.Buffer, _document.Buffer))
        {
            return;
        }

        _appliedSet = set;
        var current = Snapshot.Version;
        if (of.Version > current)
        {
            return;
        }

        // A span past its own snapshot (a language's slip) is clamped to it first, so no mapping or drawing reads past the end.
        EditorDiagnostic[] mapped = ClampTo([.. set.Items], of.Length);
        bool known = of.Version == current;
        for (int i = 0; i < _history.Count && !known; i++)
        {
            var (version, edits) = _history[i];
            if (version == of.Version)
            {
                known = true;
            }

            if (known)
            {
                mapped = MapThrough(mapped, edits);
            }
        }

        if (!known)
        {
            // Older than the edits kept: nothing can be placed for sure.
            LateResultsDropped++;
            return;
        }

        _history.RemoveAll(h => h.Version < of.Version);
        SetDiagnostics(mapped);
    }

    /// <summary>
    /// Diagnostics moved through a change's edits (in its before positions, in order): one wholly before an edit stays,
    /// one wholly after it shifts by the edit's delta, and one an edit overlaps or touches is dropped (its text changed).
    /// </summary>
    public static EditorDiagnostic[] MapThrough(ReadOnlySpan<EditorDiagnostic> diagnostics, IReadOnlyList<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var kept = new List<EditorDiagnostic>(diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            int delta = 0;
            bool touched = false;
            for (int i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                if (edit.End < diagnostic.Start)
                {
                    delta += edit.Delta;
                }
                else if (edit.Start > diagnostic.End)
                {
                    break;
                }
                else
                {
                    touched = true;
                    break;
                }
            }

            if (!touched)
            {
                kept.Add(diagnostic with { Start = diagnostic.Start + delta });
            }
        }

        return [.. kept];
    }

    /// <summary>F12: where the symbol at the caret is defined. The same document moves the caret; another file raises <see cref="DefinitionRequested"/>; metadata shows its description.</summary>
    public void GoToDefinition()
    {
        if (SemanticSession is not { } session)
        {
            return;
        }

        var snapshot = Snapshot;
        int caret = _selection.Caret;
        Track(session.GetDefinitionAsync(snapshot, caret, CancellationToken.None), location =>
        {
            if (location is null || Snapshot.Version != snapshot.Version)
            {
                LateResultsDropped += location is null ? 0 : 1;
                return;
            }

            if (location.IsSameDocument)
            {
                SetSelection(new SelectionState(location.Start + location.Length, location.Start));
                EnsureCaretVisible();
                return;
            }

            if (location.IsMetadata)
            {
                Tooltip = new EditorTooltip(location.Description, caret);
                Invalidate();
            }

            DefinitionRequested?.Invoke(this, new DefinitionRequestedEventArgs(location));
        });
    }

    /// <summary>
    /// Raised by <see cref="PeekDefinition"/> with where the symbol at the caret is defined (this document, or another
    /// file): the shell shows it in an overlay under the caret's line (the X restart's peek). A metadata symbol shows its
    /// description as a tooltip instead, and nothing is raised.
    /// </summary>
    public event EventHandler<DefinitionRequestedEventArgs>? PeekRequested;

    /// <summary>
    /// Alt+F12: asks the language where the symbol at the caret is defined, off the UI thread, and raises
    /// <see cref="PeekRequested"/> with it on the UI thread (the caret does not move); false when the language has no
    /// semantic layer.
    /// </summary>
    public bool PeekDefinition()
    {
        if (SemanticSession is not { } session)
        {
            return false;
        }

        var snapshot = Snapshot;
        int caret = _selection.Caret;
        Track(session.GetDefinitionAsync(snapshot, caret, CancellationToken.None), location =>
        {
            if (location is null || Snapshot.Version != snapshot.Version || _selection.Caret != caret)
            {
                LateResultsDropped += location is null ? 0 : 1;
                return;
            }

            if (location.IsMetadata)
            {
                Tooltip = new EditorTooltip(location.Description, caret);
                Invalidate();
                return;
            }

            PeekRequested?.Invoke(this, new DefinitionRequestedEventArgs(location));
        });
        return true;
    }

    /// <summary>The language's formatter (C#'s semantic session, or a language that only formats, such as XML or JSON), or null.</summary>
    public IFormattingLanguageSession? FormattingSession => _session as IFormattingLanguageSession;

    /// <summary>
    /// Why the last format left the document unchanged (a malformed file: the language's message, with where), or null
    /// when it formatted (or there was nothing to change). The activities show it too, with a toast.
    /// </summary>
    public string? LastFormatMessage { get; private set; }

    /// <summary>
    /// Whether Format Document can do anything here: the language formats, or a registered document formatter formats files
    /// like this one (W, contract 8: Prettier where the project has it).
    /// </summary>
    public bool CanFormat => FormattingSession is not null || (_document.FilePath is { } path && DocumentFormatters.Handles(path, LanguageName));

    /// <summary>What formats this document, in words, as far as known without reading (the status bar's tooltip), or null when nothing does.</summary>
    public string? FormatterDescription =>
        _document.FilePath is { } path && DocumentFormatters.Describe(path, LanguageName) is { } fit ? fit.Description
        : FormattingSession is not null ? $"the {LanguageName} language's own"
        : null;

    /// <summary>Shift+Alt+F (or Ctrl+K Ctrl+D): formats the document, applied as one undo step if the text is still the one formatted.</summary>
    public void FormatDocument() => _ = FormatDocumentAsync();

    /// <summary>
    /// Formats the document off the UI thread and applies the edits on it as one undo step, when the text is still the one
    /// formatted: true when edits were applied. A registered document formatter that applies here (W, contract 8: the
    /// project's Prettier) is asked first, else the language's own. A malformed document is left unchanged, and why is said
    /// (<see cref="LastFormatMessage"/>, a failed activity); a language with no formatter changes nothing. Format on save
    /// awaits it before it writes. The task never faults.
    /// </summary>
    public Task<bool> FormatDocumentAsync(CancellationToken cancellationToken = default) => FormatDocumentAsync(automatic: false, cancellationToken);

    /// <summary>
    /// <see cref="FormatDocumentAsync(CancellationToken)"/>, saying whether nobody asked just now (<paramref name="automatic"/>:
    /// format on save), which a document formatter may treat differently (a run it must be allowed to make on its own).
    /// </summary>
    public Task<bool> FormatDocumentAsync(bool automatic, CancellationToken cancellationToken = default) => FormatRangeAsync(0, -1, automatic, cancellationToken);

    /// <summary>Ctrl+K Ctrl+F: formats the lines of the selection (the caret's line when nothing is selected); see <see cref="FormatSelectionAsync"/>.</summary>
    public bool FormatSelection()
    {
        _ = FormatSelectionAsync();
        return FormattingSession is not null;
    }

    /// <summary>
    /// Formats the selection's lines (the caret's line when nothing is selected) as <see cref="FormatDocumentAsync(CancellationToken)"/>
    /// formats the document, with the language's own formatter (a registered document formatter formats whole documents
    /// only): the formatter's edits that lie within those lines, from the end of the text before the first one (so the
    /// first line's indentation is set too) to the end of the last one, applied as one undo step, the selection kept on its
    /// characters. A malformed document is left unchanged with the reason, as for the document. The task never faults.
    /// </summary>
    public Task<bool> FormatSelectionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot;
        if (FormattingSession is null || snapshot.Length == 0)
        {
            return Task.FromResult(false);
        }

        int firstLine = snapshot.GetLineIndex(Math.Clamp(_selection.Start, 0, snapshot.Length));
        int end = Math.Clamp(_selection.End, 0, snapshot.Length);
        int lastLine = snapshot.GetLineIndex(end);
        if (lastLine > firstLine && end == snapshot.GetLine(lastLine).Start)
        {
            lastLine--;                                   // a selection that ends at a line's start does not take that line
        }

        int start = snapshot.GetLine(firstLine).Start;
        while (start > 0 && char.IsWhiteSpace(snapshot[start - 1]))
        {
            start--;
        }

        int stop = snapshot.GetLine(lastLine).End;
        return FormatRangeAsync(start, stop - start, automatic: false, cancellationToken);
    }

    // Formats `length` characters at `start` (the whole document when length is -1); see FormatDocumentAsync. A registered
    // document formatter (W, contract 8: the project's Prettier) is asked first for a whole document, else the language's own.
    private Task<bool> FormatRangeAsync(int start, int length, bool automatic, CancellationToken cancellationToken)
    {
        var session = FormattingSession;
        string? path = length < 0 && _document.FilePath is { } file && DocumentFormatters.Handles(file, LanguageName) ? Path.GetFullPath(file) : null;
        if (session is null && path is null)
        {
            return Task.FromResult(false);
        }

        var snapshot = Snapshot;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string name = Path.GetFileName(_document.FilePath) is { Length: > 0 } fileName ? fileName : "the document";
        var activity = _activities.Start(length < 0 ? $"Formatting {name}" : $"Formatting the selection of {name}", ActivityKinds.General, cancellable: false, cancellationToken);
        var selection = _selection;
        var request = path is null ? null : new DocumentFormatRequest(path, snapshot, WorkspaceRoot)
        {
            TabSize = Options.TabSize,
            InsertSpaces = Options.InsertSpaces,
            Automatic = automatic,
        };
        Track(FormatOrReasonAsync(session, request, LanguageName, Options.DiffLineLimit, automatic, snapshot, start, length, cancellationToken), result =>
        {
            var (edits, reason, error, by) = result;
            if (reason is not null)
            {
                LastFormatMessage = reason;
                activity.Fail(reason, error);
                done.TrySetResult(false);
                return;
            }

            LastFormatMessage = null;
            if (Snapshot.Version != snapshot.Version)
            {
                LateResultsDropped++;
                activity.Complete("the text changed meanwhile: not applied");
                done.TrySetResult(false);
                return;
            }

            if (edits.Count == 0)
            {
                activity.Complete(by is null ? "already formatted" : "already formatted · " + by);
                done.TrySetResult(false);
                return;
            }

            int caret = MapThroughEdits(_selection.Caret, edits);
            var after = length < 0 ? SelectionState.At(caret) : new SelectionState(MapThroughEdits(selection.Anchor, edits), MapThroughEdits(selection.Caret, edits));
            ApplyEdits([.. edits], EditKind.Command, after);
            EnsureCaretVisible();
            string changes = edits.Count == 1 ? "1 change" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{edits.Count} changes");
            activity.Complete(by is null ? changes : changes + " · " + by);
            done.TrySetResult(true);
        }, () =>
        {
            activity.Dispose();
            done.TrySetResult(false);
        });
        return done.Task;
    }

    // The formatter's answer, or why there is none: for a whole document a registered document formatter that applies here
    // first (its whole text turned into one edit per changed run of lines), else the language's own; a malformed text's
    // message (FormatException), or the error it threw.
    private static async Task<(IReadOnlyList<TextEdit> Edits, string? Reason, Exception? Error, string? By)> FormatOrReasonAsync(
        IFormattingLanguageSession? session, DocumentFormatRequest? request, string languageName, int lineLimit, bool automatic, ITextSnapshot snapshot,
        int start, int length, CancellationToken cancellationToken)
    {
        string? by = null;
        try
        {
            if (request is not null)
            {
                var (formatter, description) = await DocumentFormatters.ChooseAsync(request.FilePath, languageName, request.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
                if (formatter is not null)
                {
                    by = formatter.Title;
                    string text = await formatter.FormatAsync(request, cancellationToken).ConfigureAwait(false);
                    return (Shell.LineEdits.Between(snapshot, text, lineLimit), null, null, by);
                }

                if (session is null)
                {
                    // Nothing formats it here: said when the user asked, quiet on save.
                    return ([], automatic ? null : $"Not formatted: {(description.Length > 0 ? description : "no formatter applies here")}", null, null);
                }
            }

            return (await session!.FormatAsync(snapshot, start, length, cancellationToken).ConfigureAwait(false), null, null, null);
        }
        catch (FormatException malformed)
        {
            return ([], $"Not formatted: {malformed.Message}", null, by);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return ([], $"Not formatted: {(by is null ? "the formatter" : by)} failed ({error.Message})", error, by);
        }
    }

    /// <summary>Asks for the signature help of the call around the caret (on '(' and ',', and Ctrl+Shift+Space).</summary>
    public void RequestSignatureHelp()
    {
        if (SemanticSession is not { } session)
        {
            return;
        }

        _signatureRequest?.Cancel();
        var cancel = new CancellationTokenSource();
        _signatureRequest = cancel;
        var snapshot = Snapshot;
        int caret = _selection.Caret;
        Track(session.GetSignatureHelpAsync(snapshot, caret, cancel.Token), help =>
        {
            if (!ReferenceEquals(_signatureRequest, cancel) || Snapshot.Version != snapshot.Version || _selection.Caret != caret)
            {
                LateResultsDropped++;
                return;
            }

            ShowSignatureHelp(help);
        });
    }

    /// <summary>Closes the signature help and the tooltip.</summary>
    public void CloseTooltips()
    {
        _signatureRequest?.Cancel();
        _signatureRequest = null;
        _hoverRequest?.Cancel();
        _hoverRequest = null;
        _hoverPosition = -1;
        _hoverHideTimer?.Cancel();
        _cardSelecting = false;
        if (SignatureHelp is not null || Tooltip is not null)
        {
            SignatureHelp = null;
            Tooltip = null;
            Invalidate();
        }
    }

    /// <summary>
    /// The pointer rests at a point of the view: after the hover delay, the diagnostics there and the symbol's description
    /// show as a tooltip. Moving to another position starts again; <see cref="PointerExited"/> closes it.
    /// </summary>
    public void PointerHovered(float viewX, float viewY)
    {
        _pointerX = viewX;
        _pointerY = viewY;
        NoteHover(viewX, viewY);
        if (HoverCardHovered(viewX, viewY) || Tooltip is { Kind: EditorTooltipKind.Card })
        {
            return;                                         // an inline card stays until it is closed: no hover meanwhile
        }

        if (FoldPreviewAt(viewX, viewY) is int header)
        {
            // The pointer rests on a folded block's "..." or its chevron: its hidden lines show after the hover delay.
            int marker = -2 - header;
            if (marker != _hoverPosition)
            {
                _hoverPosition = marker;
                _hoverTimer ??= new DebounceTimer(this);
                _hoverTimer.Schedule(Options.HoverDelayMilliseconds, () => ShowFoldPreview(header, marker));
            }

            return;
        }

        if (viewX < TextLeft || viewX >= ViewportWidth - OverviewRulerWidth || (StickyScroll.Count > 0 && viewY < StickyScroll.Height))
        {
            // The gutter, the ruler, the sticky headers: nothing to describe; a card shown goes after the hide delay.
            _hoverTimer?.Cancel();
            _hoverPosition = -1;
            ScheduleHoverHide();
            return;
        }

        int position = PositionFromView(viewX, viewY);
        if (position == _hoverPosition)
        {
            return;
        }

        _hoverPosition = position;
        if (Tooltip is { Kind: EditorTooltipKind.Hover } card && SameWord(card.Position, position))
        {
            _hoverTimer?.Cancel();
            _hoverHideTimer?.Cancel();          // still on the word the card describes
            return;
        }

        ScheduleHoverHide();
        _hoverTimer ??= new DebounceTimer(this);
        _hoverTimer.Schedule(Options.HoverDelayMilliseconds, () => StartHover(position));
    }

    /// <summary>The pointer left the editor: a hover card closes after <see cref="Core.CraftOptions.HoverHideMilliseconds"/> (at once for a plain tooltip).</summary>
    public void PointerExited()
    {
        NoteExit();
        ClearLinkHover();
        _hoverTimer?.Cancel();
        _hoverRequest?.Cancel();
        _hoverPosition = -1;
        _pointerX = _pointerY = -1;
        if (Tooltip is { Kind: EditorTooltipKind.Card })
        {
            return;
        }

        if (Tooltip is { Kind: not EditorTooltipKind.Plain } && SignatureHelp is null)
        {
            ScheduleHoverHide();
        }
        else if (Tooltip is not null && SignatureHelp is null)
        {
            Tooltip = null;
            Invalidate();
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread (see <see cref="Dispatch"/>).</summary>
    internal void Post(Action action)
    {
        if (Dispatch is { } dispatch)
        {
            dispatch(action);
        }
        else if (_uiContext is { } context)
        {
            context.Post(static state => ((Action)state!)(), action);
        }
        else
        {
            action();
        }
    }

    /// <summary>Awaits a semantic request off the UI thread and hands its answer to <paramref name="apply"/> on the UI thread (a failure kept, a cancel dropped).</summary>
    internal void Track<T>(Task<T> request, Action<T> apply, Action? dropped = null)
    {
        Interlocked.Increment(ref _semanticRequests);
        request.ContinueWith(done => Post(() =>
        {
            try
            {
                if (_disposed)
                {
                    dropped?.Invoke();
                    return;
                }

                if (done.IsCompletedSuccessfully)
                {
                    apply(done.Result);
                }
                else
                {
                    if (done.IsFaulted)
                    {
                        LastSemanticError = done.Exception?.GetBaseException();
                    }

                    dropped?.Invoke();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _semanticRequests);
                SemanticRequestCompleted?.Invoke(this, EventArgs.Empty);
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void StartHover(int position)
    {
        if (_disposed || position != _hoverPosition)
        {
            return;
        }

        if (position > Snapshot.Length)
        {
            // The text under the pointer went while the hover waited: nothing to describe (the session would refuse it).
            _hoverPosition = -1;
            return;
        }

        var problems = DiagnosticsAt(position);
        string problemText = problems.Count == 0 ? "" : string.Join('\n', problems.Select(p => $"{p.Id}: {p.Message}"));
        if (SemanticSession is not { } session)
        {
            ShowHover(problemText, null, position);
            return;
        }

        _hoverRequest?.Cancel();
        var cancel = new CancellationTokenSource();
        _hoverRequest = cancel;
        var snapshot = Snapshot;
        Track(session.GetHoverAsync(snapshot, position, cancel.Token), info =>
        {
            if (!ReferenceEquals(_hoverRequest, cancel) || position != _hoverPosition || Snapshot.Version != snapshot.Version)
            {
                LateResultsDropped++;
                return;
            }

            ShowHover(problemText, info, position);
        });
    }

    private void ShowHover(string problems, HoverInfo? info, int position)
    {
        var text = info is null ? problems : problems.Length == 0 ? info.Text : problems + "\n\n" + info.Text;
        _hoverHasSymbol = info is not null;
        _hoverHideTimer?.Cancel();
        Tooltip = text.Length == 0 ? null : new EditorTooltip(text, position) { Kind = EditorTooltipKind.Hover };
        Invalidate();
    }

    private void ShowSignatureHelp(SignatureHelp? help)
    {
        SignatureHelp = help;
        if (help is null || help.Signatures.Count == 0)
        {
            SignatureHelp = null;
            Tooltip = null;
            Invalidate();
            return;
        }

        var signature = help.Signatures[Math.Clamp(help.ActiveSignature, 0, help.Signatures.Count - 1)];
        var text = signature.Label;
        (int, int) highlight = default;
        if (help.ActiveParameter < signature.Parameters.Count)
        {
            var parameter = signature.Parameters[help.ActiveParameter];
            highlight = (parameter.Start, parameter.Length);
            if (parameter.Documentation is { Length: > 0 } parameterDocs)
            {
                text += $"\n{parameter.Name}: {parameterDocs}";
            }
        }

        if (signature.Documentation is { Length: > 0 } docs)
        {
            text += "\n" + docs;
        }

        if (help.Signatures.Count > 1)
        {
            text += $"\n({help.ActiveSignature + 1} of {help.Signatures.Count} overloads)";
        }

        Tooltip = new EditorTooltip(text, help.ArgumentsStart, highlight);
        Invalidate();
    }

    /// <summary>
    /// <paramref name="diagnostics"/> inside a text of <paramref name="length"/>: each start in [0, length], each end in
    /// [start, length] (the same array when all already are; the order is kept, clamping keeps it).
    /// </summary>
    public static EditorDiagnostic[] ClampTo(EditorDiagnostic[] diagnostics, int length)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        EditorDiagnostic[]? clamped = null;
        for (int i = 0; i < diagnostics.Length; i++)
        {
            var d = diagnostics[i];
            if (d.Start >= 0 && d.Length >= 0 && d.Start <= length - d.Length)
            {
                continue;
            }

            clamped ??= (EditorDiagnostic[])diagnostics.Clone();
            int start = Math.Clamp(d.Start, 0, length);
            int end = (int)Math.Clamp((long)d.Start + Math.Max(0, d.Length), start, length);
            clamped[i] = d with { Start = start, Length = end - start };
        }

        return clamped ?? diagnostics;
    }

    private void SetDiagnostics(EditorDiagnostic[] diagnostics)
    {
        // The owner's crash report hunt (2026-10-31): whatever arrives, the diagnostics shown lie inside the current text.
        _semanticDiagnostics = ClampTo(diagnostics, Snapshot.Length);
        JoinDiagnostics(always: true);
    }

    // W, contract 6: the language's diagnostics joined with what plug-ins published for the document (both sorted by
    // position), once the published ones are in the current text's positions (an edit moves them right after this one).
    private void JoinDiagnostics(bool always = false)
    {
        var published = _published is { } shared && shared.TextVersion == Snapshot.Version ? shared.All : [];
        EditorDiagnostic[] diagnostics;
        if (published.IsEmpty)
        {
            diagnostics = _semanticDiagnostics;
        }
        else
        {
            var own = _semanticDiagnostics;
            diagnostics = new EditorDiagnostic[own.Length + published.Length];
            int a = 0, b = 0, at = 0;
            while (a < own.Length || b < published.Length)
            {
                bool takeOwn = b >= published.Length
                    || (a < own.Length && (own[a].Start < published[b].Start || (own[a].Start == published[b].Start && own[a].Length <= published[b].Length)));
                diagnostics[at++] = takeOwn ? own[a++] : published[b++];
            }

            diagnostics = ClampTo(diagnostics, Snapshot.Length);
        }

        if (!always && ReferenceEquals(diagnostics, _diagnostics))
        {
            return;
        }

        _diagnostics = diagnostics;
        int longest = 0;
        int errors = 0;
        int warnings = 0;
        foreach (var diagnostic in diagnostics)
        {
            longest = Math.Max(longest, diagnostic.Length);
            errors += diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 0;
            warnings += diagnostic.Severity == DiagnosticSeverity.Warning ? 1 : 0;
        }

        _diagnosticsLongest = longest;
        ErrorCount = errors;
        WarningCount = warnings;
        Invalidate();
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called by the buffer's change: the diagnostics follow the edits, and the edits are kept for the sets still being computed.</summary>
    private void OnSemanticBufferChanged(TextChangedEventArgs change)
    {
        if (SemanticSession is null)
        {
            return;
        }

        var edits = change.Edits as TextEdit[] ?? [.. change.Edits];
        _history.Add((change.Before.Version, edits));
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(0, _history.Count - MaxHistory);
        }

        if (_semanticDiagnostics.Length > 0)
        {
            var mapped = MapThrough(_semanticDiagnostics, edits);
            SetDiagnostics(mapped);
        }

        if (Tooltip is not null && SignatureHelp is null)
        {
            // A hover belongs to the text it described.
            Tooltip = null;
        }

        // A hover still waiting for its pause belongs to the text before the edit: it is dropped, the pointer's next move asks again.
        _hoverTimer?.Cancel();
        _hoverRequest?.Cancel();
        _hoverPosition = -1;
    }

    /// <summary>Inserts an accepted inline completion at <paramref name="position"/> as one undo step, the caret after it.</summary>
    internal void ApplyAcceptedText(int position, string text)
    {
        var edit = new TextEdit(position, 0, text);
        ApplyEdits(new ReadOnlySpan<TextEdit>(in edit), EditKind.Command, SelectionState.At(position + text.Length));
        EnsureCaretVisible();
    }

    /// <summary>
    /// Applies a committed completion item at every matching caret (the X restart, piece 2b): <paramref name="before"/> and
    /// <paramref name="after"/> characters around each caret replaced by <paramref name="text"/>, the caret then
    /// <paramref name="caretOffset"/> into it; one undo step.
    /// </summary>
    internal void ApplyCompletionAtCarets(int before, int after, string text, int caretOffset)
    {
        ApplyAtMatchingCarets(CaretMatch.ReplacedText, before, after, text, caretOffset, "Completion");
        if (text.EndsWith('('))
        {
            RequestSignatureHelp();
        }
    }

    /// <summary>Applies a committed completion item as one undo step, the caret at <paramref name="caret"/>.</summary>
    internal void ApplyCompletion(TextEdit edit, int caret)
    {
        ApplyEdits(new ReadOnlySpan<TextEdit>(in edit), EditKind.Command, SelectionState.At(caret));
        EnsureCaretVisible();
        if (edit.Text.EndsWith('('))
        {
            RequestSignatureHelp();
        }
    }

    private void OnSessionDiagnosticsChanged(object? sender, EventArgs e) => Post(ApplyDiagnostics);

    // W, contract 6: what plug-ins published for this document (shared by its editors), joined with the language's own.
    private void AttachPublishedDiagnostics()
    {
        _published = DocumentDiagnostics.Of(_document);
        _published.Changed += OnPublishedDiagnosticsChanged;
        JoinDiagnostics();
    }

    private void DetachPublishedDiagnostics()
    {
        if (_published is { } published)
        {
            published.Changed -= OnPublishedDiagnosticsChanged;
            _published = null;
        }
    }

    private void OnPublishedDiagnosticsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            JoinDiagnostics();
        }
    }

    /// <summary>The keys of the semantic layer (completion's own while its list is open first); true when handled.</summary>
    private bool OnSemanticKey(EditorKey key, KeyModifiers modifiers)
    {
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        bool control = (modifiers & KeyModifiers.Control) != 0;
        bool alt = (modifiers & KeyModifiers.Alt) != 0;
        if (_chordK)
        {
            _chordK = false;
            if (control && key == EditorKey.D)
            {
                FormatDocument();
                return true;
            }

            if (control && key == EditorKey.F)
            {
                FormatSelection();
                return true;
            }
        }

        if (Completion.IsOpen && Completion.HandleKey(key, modifiers))
        {
            return true;
        }

        switch (key)
        {
            case EditorKey.Space when control && shift:
                RequestSignatureHelp();
                return true;
            case EditorKey.Space when control:
                Completion.Invoke();
                return true;
            case EditorKey.F12 when !control && !alt:
                GoToDefinition();
                return true;
            case EditorKey.F when shift && alt && !control:
                FormatDocument();
                return true;
            case EditorKey.K when control && !alt:
                _chordK = true;
                return true;
            case EditorKey.Escape when SignatureHelp is not null || Tooltip is not null:
                CloseTooltips();
                return true;
        }

        return false;
    }

    /// <summary>After a key the editor handled: the completion list and signature help follow the caret.</summary>
    private void AfterKey(EditorKey key)
    {
        if (key == EditorKey.Backspace || key == EditorKey.Delete)
        {
            Completion.OnDeleted();
            InlineCompletion.OnEdited();
        }
        else
        {
            Completion.OnCaretMoved();
            if (key == EditorKey.Enter)
            {
                // A new line asks too (the owner's run, 2026-11-01): the code a comment describes is written on the line
                // under it, which only Enter reaches; before, Enter never asked and that line stayed empty.
                InlineCompletion.OnEdited();
            }
        }

        if (SignatureHelp is not null)
        {
            RequestSignatureHelp();
        }
    }

    /// <summary>After typed text: completion opens or filters, signature help opens on '(' and ',' and closes on ')'.</summary>
    private void AfterTyped(string text)
    {
        Completion.OnTyped(text);
        InlineCompletion.OnEdited();
        char last = text[^1];
        if (last is '(' or ',')
        {
            RequestSignatureHelp();
        }
        else if (last == ')' && SignatureHelp is not null)
        {
            RequestSignatureHelp();
        }
    }

    private static int MapThroughEdits(int position, IReadOnlyList<TextEdit> edits)
    {
        int delta = 0;
        foreach (var edit in edits)
        {
            if (edit.End <= position)
            {
                delta += edit.Delta;
            }
            else if (edit.Start < position)
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

    private void DisposeSemantic()
    {
        if (SemanticSession is { } session)
        {
            session.DiagnosticsChanged -= OnSessionDiagnosticsChanged;
        }

        _signatureRequest?.Cancel();
        _hoverRequest?.Cancel();
        _hoverTimer?.Dispose();
        _hoverHideTimer?.Dispose();
        _completion?.Dispose();
    }

    /// <summary>A one-shot timer whose action runs on the UI thread (<see cref="Post"/>); scheduling again replaces it. 0 runs it at once.</summary>
    internal sealed class DebounceTimer(EditorModel model) : IDisposable
    {
        private ITimer? _timer;
        private Action? _action;
        private int _generation;

        public void Schedule(int milliseconds, Action action)
        {
            int generation = ++_generation;
            if (milliseconds <= 0)
            {
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                action();
                return;
            }

            _action = action;
            _timer ??= model._time.CreateTimer(static state =>
            {
                var self = (DebounceTimer)state!;
                int due = Volatile.Read(ref self._generation);
                self.Fire(due);
            }, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(TimeSpan.FromMilliseconds(milliseconds), Timeout.InfiniteTimeSpan);
            _ = generation;
        }

        public void Cancel()
        {
            _generation++;
            _action = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            Cancel();
            _timer?.Dispose();
            _timer = null;
        }

        private void Fire(int generation) => model.Post(() =>
        {
            if (generation == _generation && _action is { } action)
            {
                _action = null;
                action();
            }
        });
    }
}
