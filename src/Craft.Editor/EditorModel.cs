// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The editor without a UI: one document's selection, caret, scroll offsets and viewport, its commands (the keys and the
/// text of <see cref="IEditorInputSink"/>, the pointer), its layout on a monospaced grid, find and replace, and bracket
/// matching. A host (Craft.Maui's EditorView, or a test) feeds it input and draws it with <see cref="EditorRenderer"/>.
/// Every edit goes through <see cref="ITextDocument.Edit"/>, so each is undoable and restores its selection. Used on the
/// UI thread. One selection for now: the commands work on a <see cref="SelectionState"/> value and return the next, so
/// several carets later map the same commands over each.
/// </summary>
public sealed partial class EditorModel : IEditorInputSink, IDisposable
{
    private readonly ITextDocument _document;
    private readonly ILanguage? _language;
    private readonly ILanguageSession? _session;
    private readonly bool _ownsSession;
    private readonly ITextMeasurer _measurer;
    private readonly IEditorClipboard _clipboard;
    private readonly TimeProvider _time;
    private string _indentUnit;
    private readonly TokenSpanBuffer _bracketSpans = new(64);
    private SelectionState _selection;
    private int _desiredColumn = -1;
    private bool _applying;
    private SelectionState? _applyingAfter;
    private bool _disposed;
    private int _maxColumns;
    private int _lastLineCount;
    private int _lastDigits;
    private ITheme _theme;
    private BracketMatch? _bracket;
    private int _bracketVersion = -1;
    private int _bracketCaret = -1;
    private long _inputTimestamp;
    private bool _inputPending;
    private readonly IEditorActivities _activities;
    private bool _readOnly;
    private bool _lineNumbers = true;

    /// <summary>An editor over <paramref name="document"/>.</summary>
    /// <param name="document">The document it edits (the caller keeps owning it).</param>
    /// <param name="language">Its language, or null for plain text (<see cref="LanguageName"/> then says so and why).</param>
    /// <param name="options">The settings (the defaults when null).</param>
    /// <param name="theme">The colours (the options' theme when null).</param>
    /// <param name="measurer">Measures the options' font on the platform.</param>
    /// <param name="clipboard">The clipboard for copy, cut and paste.</param>
    /// <param name="time">The clock for click counting and the input-to-paint telemetry (the system's when null).</param>
    /// <param name="workspaceRoot">The open folder the document belongs to (its language's workspace), or null.</param>
    /// <param name="activities">Where its language shows long work (a workspace build), or null for nowhere.</param>
    public EditorModel(ITextDocument document, ILanguage? language, CraftOptions? options, ITheme? theme, ITextMeasurer measurer,
        IEditorClipboard clipboard, TimeProvider? time = null, string? workspaceRoot = null, IEditorActivities? activities = null)
        : this(document, language, options, theme, measurer, clipboard, time, workspaceRoot, activities, shared: false, session: null)
    {
    }

    /// <summary>
    /// An editor over <paramref name="document"/> that uses <paramref name="session"/>, a language session it does not own
    /// (the X restart's split editor: two views of one document share one session, counted by
    /// <see cref="LanguageSessionPool"/>; a peek's syntactic session): disposing the editor leaves it open.
    /// </summary>
    /// <param name="document">The document it edits (the caller keeps owning it).</param>
    /// <param name="session">The session over the document's buffer, or null for none (plain text).</param>
    /// <param name="language">The session's language, or null for plain text.</param>
    /// <param name="options">The settings (the defaults when null).</param>
    /// <param name="theme">The colours (the options' theme when null).</param>
    /// <param name="measurer">Measures the options' font on the platform.</param>
    /// <param name="clipboard">The clipboard for copy, cut and paste.</param>
    /// <param name="time">The clock (the system's when null).</param>
    /// <param name="workspaceRoot">The open folder the document belongs to, or null.</param>
    /// <param name="activities">Where long work shows, or null for nowhere.</param>
    public EditorModel(ITextDocument document, ILanguageSession? session, ILanguage? language, CraftOptions? options, ITheme? theme, ITextMeasurer measurer,
        IEditorClipboard clipboard, TimeProvider? time = null, string? workspaceRoot = null, IEditorActivities? activities = null)
        : this(document, language, options, theme, measurer, clipboard, time, workspaceRoot, activities, shared: true, session)
    {
    }

    private EditorModel(ITextDocument document, ILanguage? language, CraftOptions? options, ITheme? theme, ITextMeasurer measurer,
        IEditorClipboard clipboard, TimeProvider? time, string? workspaceRoot, IEditorActivities? activities, bool shared, ILanguageSession? session)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(clipboard);
        _document = document;
        _language = language;
        Options = options ?? new CraftOptions();
        _baseOptions = Options;
        _theme = theme ?? EditorThemes.Get(Options.Theme);
        _measurer = measurer;
        _clipboard = clipboard;
        _time = time ?? TimeProvider.System;
        _indentUnit = Options.InsertSpaces ? new string(' ', Options.TabSize) : "\t";
        Metrics = Measure();
        LanguageName = language?.Name ?? PlainTextName(document.FilePath);
        Find = new EditorFind(this);
        WorkspaceRoot = workspaceRoot;
        InlineCompletion = new InlineCompletionController(this);
        _activities = activities ?? NoActivities.Instance;
        _ownsSession = !shared;
        _session = shared ? session : language?.Open(document.Buffer, OpenContext(document, workspaceRoot, Options, _activities));
        if (_session is not null)
        {
            _session.Reclassified += OnSessionReclassified;
        }

        if (_session is ISemanticLanguageSession semantic)
        {
            semantic.DiagnosticsChanged += OnSessionDiagnosticsChanged;
        }

        _lastLineCount = Snapshot.LineCount;
        _lastDigits = TextReading.Digits(_lastLineCount);
        Folding = new EditorFolding(this);
        document.Buffer.Changed += OnBufferChanged;
        AttachLineMarks();
        AttachPublishedDiagnostics();
    }

    /// <summary>
    /// What a language session of <paramref name="document"/> is opened with by an editor with these settings: its file,
    /// the workspace, the options' language settings, where long work shows. Two views asking with equal arguments share
    /// one session (<see cref="LanguageSessionPool"/>).
    /// </summary>
    public static LanguageOpenContext OpenContext(ITextDocument document, string? workspaceRoot, CraftOptions options, IEditorActivities? activities)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return new LanguageOpenContext(document.FilePath, workspaceRoot, options.ToLanguageSettings()) { Activities = activities ?? NoActivities.Instance };
    }

    /// <summary>The document it edits.</summary>
    public ITextDocument Document => _document;

    /// <summary>The document's current text.</summary>
    public ITextSnapshot Snapshot => _document.Buffer.Current;

    /// <summary>Its language, or null for plain text.</summary>
    public ILanguage? Language => _language;

    /// <summary>The language session over the document's buffer (null for plain text); disposed with the editor unless it was given (<see cref="OwnsLanguageSession"/>).</summary>
    public ILanguageSession? LanguageSession => _session;

    /// <summary>Whether the editor opened its language session itself and disposes it (false for a shared one).</summary>
    public bool OwnsLanguageSession => _ownsSession;

    /// <summary>The language's comment and bracket rules (<see cref="LanguageRules.Plain"/> for plain text).</summary>
    public LanguageRules Rules => _language?.Rules ?? LanguageRules.Plain;

    /// <summary>
    /// The rules at <paramref name="position"/> (phase W): an embedded language's where the session says so
    /// (<see cref="ILanguageSession.RulesAt"/>: an HTML file's script comments with <c>//</c>), else <see cref="Rules"/>.
    /// </summary>
    public LanguageRules RulesAt(int position) => _session?.RulesAt(Snapshot, Math.Clamp(position, 0, Snapshot.Length)) ?? Rules;

    /// <summary>The language's name, or "Plain text" with the reason ("Plain text — no language for .xyz").</summary>
    public string LanguageName { get; }

    /// <summary>The settings in effect: the editor's options with the file's own settings over them (<see cref="ApplyFileSettings"/>).</summary>
    public CraftOptions Options { get; private set; }

    /// <summary>The colours; setting it redraws.</summary>
    public ITheme Theme
    {
        get => _theme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _theme = value;
            Invalidate();
        }
    }

    /// <summary>The font's measures, as the platform measured them.</summary>
    public FontMetrics Metrics { get; private set; }

    /// <summary>Find and replace.</summary>
    public EditorFind Find { get; }

    /// <summary>Code folding: the ranges, the folds, and the line-to-row mapping the layout and the paint follow.</summary>
    public EditorFolding Folding { get; }

    /// <summary>The cells of the gutter's fold column (its chevrons), when the document folds.</summary>
    public const int FoldGutterCells = 2;

    /// <summary>The rows drawn: the lines minus those the folds hide, each wrapped line counting its rows (X part 9).</summary>
    public int RowCount => WordWrap.IsActive ? WordWrap.RowCount : Folding.Map.HasHidden ? Folding.Map.RowCount : Snapshot.LineCount;

    /// <summary>The row <paramref name="line"/> is drawn on, its first when wrapped (its fold's header row when hidden).</summary>
    public int RowOfLine(int line) => WordWrap.IsActive ? WordWrap.RowOfLine(line) : Folding.Map.HasHidden ? Folding.Map.RowOf(line) : line;

    /// <summary>The line drawn on <paramref name="row"/> (any of its rows when wrapped).</summary>
    public int LineAtRow(int row) => WordWrap.IsActive ? WordWrap.LineAtRow(row, out _)
        : Folding.Map.HasHidden ? Folding.Map.LineAt(row) : Math.Clamp(row, 0, Snapshot.LineCount - 1);

    /// <summary>The next line drawn after <paramref name="line"/> (the line count when it is the last).</summary>
    public int NextShownLine(int line) => Folding.Map.HasHidden ? Folding.Map.NextShown(line) : line + 1;

    /// <summary>The ghost text of an inline completion provider at the caret.</summary>
    public InlineCompletionController InlineCompletion { get; }

    /// <summary>The open folder the document belongs to, or null.</summary>
    public string? WorkspaceRoot { get; }

    internal TimeProvider Time => _time;

    /// <summary>The selection (its caret is where typing goes).</summary>
    public SelectionState Selection => _selection;

    /// <summary>Whether typing replaces the character after the caret (Insert toggles it).</summary>
    public bool Overwrite { get; private set; }

    /// <summary>
    /// Whether the text is read-only (the owner's note 26: the bottom panel's Output): typing, paste, cut, delete, undo,
    /// redo, the line commands, formatting, the completions and every other edit are refused where edits enter
    /// (the one place every edit commits, undo and redo), so nothing the user, a key binding or a plug-in does through the editor
    /// changes it; selecting, copying, finding and scrolling work. Its owner changes the text through the buffer itself
    /// (<see cref="ITextBuffer.Apply"/>), outside the undo history; the selection follows such changes.
    /// </summary>
    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly != value)
            {
                _readOnly = value;
                InlineCompletion.Dismiss();
                Invalidate();
            }
        }
    }

    /// <summary>Whether the line numbers are drawn (true by default); without them the text starts a cell from the left.</summary>
    public bool ShowLineNumbers
    {
        get => _lineNumbers;
        set
        {
            if (_lineNumbers != value)
            {
                _lineNumbers = value;
                ContentSizeChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    /// <summary>How many edits <see cref="IsReadOnly"/> refused (the tests and the status read it).</summary>
    public int RefusedEdits { get; private set; }

    /// <summary>The horizontal scroll offset, as last drawn or requested.</summary>
    public float ScrollX { get; private set; }

    /// <summary>The vertical scroll offset, as last drawn or requested.</summary>
    public float ScrollY { get; private set; }

    /// <summary>The viewport's width, as last drawn.</summary>
    public float ViewportWidth { get; private set; }

    /// <summary>The viewport's height, as last drawn.</summary>
    public float ViewportHeight { get; private set; }

    /// <summary>The milliseconds from the last input to the end of the paint that showed it.</summary>
    public double LastInputToPaintMilliseconds { get; private set; }

    /// <summary>The most milliseconds from an input to its paint since the editor opened (or <see cref="ResetTelemetry"/>).</summary>
    public double MaxInputToPaintMilliseconds { get; private set; }

    /// <summary>Raised when what is drawn changed (the host redraws).</summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Raised when the language's spans changed without an edit (its parse off the UI thread landed), on the language's
    /// thread, not the UI thread: the host dispatches to its UI thread and calls <see cref="ApplyReclassification"/>.
    /// </summary>
    public event EventHandler? Reclassified;

    /// <summary>Raised when the caret or the selection moved.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised when the caret must be scrolled into view: the host scrolls its content to the offsets.</summary>
    public event EventHandler<EditorScrollEventArgs>? ScrollRequested;

    /// <summary>Raised when the viewport's offsets or size changed (<see cref="UpdateViewport"/>; inside a frame too: a handler that changes the UI dispatches it).</summary>
    public event EventHandler? ViewportChanged;

    /// <summary>Raised when <see cref="ContentWidth"/>, <see cref="ContentHeight"/> or <see cref="GutterWidth"/> changed.</summary>
    public event EventHandler? ContentSizeChanged;

    /// <summary>Raised on Ctrl+S.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised on Ctrl+F (find) and Ctrl+H (replace).</summary>
    public event EventHandler<FindRequestedEventArgs>? FindRequested;

    /// <summary>Raised when a copy or a paste failed on the platform's clipboard.</summary>
    public event EventHandler<ClipboardFailedEventArgs>? ClipboardFailed;

    /// <summary>The caret's line index.</summary>
    public int CaretLine => Snapshot.GetLineIndex(CaretPosition);

    /// <summary>The caret's column (tabs expanded), from 0.</summary>
    public int CaretColumn => ColumnOf(CaretPosition);

    // The caret in the current snapshot. While an edit is applied, the document's own events (StateChanged, the buffer's
    // Changed) run with the new text but before the selection moves: they get the selection the edit ends with, so a
    // listener that reads the caret then (the status bar, a plug-in) never sees a position past the new text's end.
    private int CaretPosition => Math.Min((_applyingAfter ?? _selection).Caret, Snapshot.Length);

    /// <summary>The line-number gutter's width: the line count's digits plus the padding, in cells.</summary>
    public float GutterWidth => _lineNumbers
        ? (TextReading.Digits(Snapshot.LineCount) + Options.GutterPaddingCells + (Folding.IsAvailable ? FoldGutterCells : 0)) * Metrics.CellWidth
        : (1 + (Folding.IsAvailable ? FoldGutterCells : 0)) * Metrics.CellWidth;

    /// <summary>Where column 0 of the text starts, in content coordinates (the gutter's right edge).</summary>
    public float TextLeft => GutterWidth;

    /// <summary>The content's height: every line plus the bottom margin.</summary>
    public float ContentHeight => (RowCount + Options.BottomMarginLines) * Metrics.LineHeight;

    /// <summary>
    /// The content's width: the longest line seen while drawing (or with the caret on it) plus the margin. It only grows:
    /// no whole-file scan is made to find the longest line.
    /// </summary>
    public float ContentWidth => WordWrap.Mode == "on" && WordWrap.IsActive ? ViewportWidth
        : TextLeft + (_maxColumns + Options.ScrollMarginColumns + 1) * Metrics.CellWidth;

    /// <summary>The bracket at or just before the caret and its partner, or null (searched once per caret and version).</summary>
    public BracketMatch? MatchingBrackets
    {
        get
        {
            var snapshot = Snapshot;
            if (_bracketVersion != snapshot.Version || _bracketCaret != _selection.Caret)
            {
                _bracket = BracketMatcher.Find(snapshot, _selection.Caret, Rules, _session, Options.BracketScanLines, _bracketSpans);
                _bracketVersion = snapshot.Version;
                _bracketCaret = _selection.Caret;
            }

            return _bracket;
        }
    }

    /// <summary>
    /// The word at the caret whose other places are highlighted (a word of letters, digits and '_' touching the caret, with
    /// nothing selected and one caret), or null; found once per caret and version.
    /// </summary>
    public string? HighlightedWord
    {
        get
        {
            var snapshot = Snapshot;
            if (_wordVersion != snapshot.Version || _wordCaret != _selection.Caret || _wordMany != HasMultipleCarets || _wordEmpty != _selection.IsEmpty)
            {
                _wordVersion = snapshot.Version;
                _wordCaret = _selection.Caret;
                _wordMany = HasMultipleCarets;
                _wordEmpty = _selection.IsEmpty;
                _word = null;
                if (_selection.IsEmpty && !_wordMany && snapshot.Length > 0)
                {
                    var (start, end) = WordAt(Math.Min(_selection.Caret, snapshot.Length));
                    if (end > start && end - start <= MaxHighlightedWord && TextReading.KindOf(snapshot[start]) == CharKind.Word)
                    {
                        _word = snapshot.GetText(start, end - start);
                    }
                }
            }

            return _word;
        }
    }

    private const int MaxHighlightedWord = 256;
    private string? _word;
    private int _wordVersion = -1;
    private int _wordCaret = -1;
    private bool _wordMany;
    private bool _wordEmpty;

    /// <summary>
    /// The lines a viewport at <paramref name="scrollY"/> of <paramref name="viewportHeight"/> shows, plus one: the first and
    /// the last drawn (with folds, the lines between them that are hidden are not drawn; step with <see cref="NextShownLine"/>).
    /// </summary>
    public LineRange VisibleLines(float scrollY, float viewportHeight)
    {
        var rows = VisibleRows(scrollY, viewportHeight);
        return new LineRange(LineAtRow(rows.First), LineAtRow(rows.Last));
    }

    /// <summary>The rows a viewport at <paramref name="scrollY"/> of <paramref name="viewportHeight"/> shows, plus one.</summary>
    public LineRange VisibleRows(float scrollY, float viewportHeight)
    {
        int last = RowCount - 1;
        float height = Metrics.LineHeight;
        int first = Math.Clamp((int)MathF.Floor(Math.Max(0, scrollY) / height), 0, last);
        int end = Math.Clamp((int)MathF.Ceiling((Math.Max(0, scrollY) + Math.Max(0, viewportHeight)) / height), first, last);
        return new LineRange(first, end);
    }

    /// <summary>The column (tabs expanded) of <paramref name="position"/> on its line.</summary>
    public int ColumnOf(int position)
    {
        var snapshot = Snapshot;
        position = Math.Clamp(position, 0, snapshot.Length);
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            return TextReading.ColumnOf(text, position - line.Start, Options.TabSize);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>The top-left of the cell of <paramref name="position"/>, in content coordinates.</summary>
    public EditorPoint PointFromPosition(int position)
    {
        var (row, column) = VisualOf(position);
        return new EditorPoint(TextLeft + column * Metrics.CellWidth, row * Metrics.LineHeight);
    }

    /// <summary>The position nearest to the content point (<paramref name="x"/>, <paramref name="y"/>), clamped to the text.</summary>
    public int PositionFromPoint(float x, float y)
    {
        int row = y < 0 ? 0 : Math.Clamp((int)MathF.Floor(y / Metrics.LineHeight), 0, Math.Max(0, RowCount - 1));
        float column = Math.Max(0, (x - TextLeft) / Metrics.CellWidth);
        return PositionAtVisual(row, column, nearest: true);
    }

    /// <summary>Measures the options' font again (after the platform measured it for real) and redraws.</summary>
    public void Remeasure()
    {
        var metrics = Measure();
        if (metrics != Metrics)
        {
            Metrics = metrics;
            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Records the viewport the host shows (the renderer calls it on every draw).</summary>
    public void UpdateViewport(float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        float x = Math.Max(0, scrollX);
        float y = Math.Max(0, scrollY);
        float width = Math.Max(0, viewportWidth);
        float height = Math.Max(0, viewportHeight);
        bool changed = x != ScrollX || y != ScrollY || width != ViewportWidth || height != ViewportHeight;
        ScrollX = x;
        ScrollY = y;
        ViewportWidth = width;
        ViewportHeight = height;
        if (changed)
        {
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Asks the host to scroll to the content offsets (clamped to the content), raising <see cref="ScrollRequested"/>; with
    /// <paramref name="instant"/> the host jumps there (a log following its end) instead of easing.
    /// </summary>
    public void ScrollToOffsets(float x, float y, bool instant = false)
    {
        x = Math.Clamp(x, 0, Math.Max(0, ContentWidth - ViewportWidth));
        y = Math.Clamp(y, 0, Math.Max(0, ContentHeight - ViewportHeight));
        // The offsets stay what the host last showed (UpdateViewport): a listener of ViewportChanged sees the host's scroll
        // land, not this request.
        ScrollRequested?.Invoke(this, new EditorScrollEventArgs(x, y) { Instant = instant });
        Invalidate();
    }

    /// <summary>Records that a drawn line spans <paramref name="columns"/> columns, growing <see cref="ContentWidth"/>.</summary>
    public void NoteLineColumns(int columns)
    {
        if (columns > _maxColumns)
        {
            _maxColumns = columns;
            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends the input-to-paint measure of the last input (the renderer calls it at the end of each draw).</summary>
    public void NotePainted()
    {
        if (_inputPending)
        {
            _inputPending = false;
            double ms = _time.GetElapsedTime(_inputTimestamp).TotalMilliseconds;
            LastInputToPaintMilliseconds = ms;
            MaxInputToPaintMilliseconds = Math.Max(MaxInputToPaintMilliseconds, ms);
        }
    }

    /// <summary>
    /// On the UI thread, after <see cref="Reclassified"/>: forgets the bracket match (its strings and comments may have
    /// moved) and raises <see cref="Invalidated"/>. The renderer's line cache keys on each line's spans, so it rebuilds
    /// the lines whose spans changed, and only those.
    /// </summary>
    public void ApplyReclassification()
    {
        if (_disposed)
        {
            return;
        }

        _bracketVersion = -1;
        NoteReclassified();
        Invalidate();
    }

    /// <summary>
    /// Waits up to <see cref="CraftOptions.ParseWaitMilliseconds"/> for the language's parse of <paramref name="snapshot"/>
    /// (the renderer calls it before it classifies a frame); true when its spans are final, or there is no language.
    /// </summary>
    public bool WaitForClassification(ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session is null || _session.WaitUntilClassified(snapshot, TimeSpan.FromMilliseconds(Options.ParseWaitMilliseconds));
    }

    /// <summary>Clears the input-to-paint maximum.</summary>
    public void ResetTelemetry() => MaxInputToPaintMilliseconds = 0;

    /// <summary>Scrolls so the caret shows with the margins of the settings (raises <see cref="ScrollRequested"/> when it must move).</summary>
    public void EnsureCaretVisible()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        var metrics = Metrics;
        var (caretRow, column) = VisualOf(CaretPosition);
        NoteLineColumns(column);
        float caretX = column * metrics.CellWidth;
        float caretY = caretRow * metrics.LineHeight;
        // The overview ruler covers the right edge, the sticky headers the top rows: the caret is kept out from under them.
        float textWidth = Math.Max(metrics.CellWidth, ViewportWidth - TextLeft - OverviewRulerWidth);
        float stickyTop = StickyScroll.Height;
        float marginX = Math.Min(Options.ScrollMarginColumns * metrics.CellWidth, Math.Max(0, (textWidth - metrics.CellWidth) / 2));
        float marginY = Math.Min(Options.ScrollMarginLines * metrics.LineHeight, Math.Max(0, (ViewportHeight - metrics.LineHeight) / 2));
        float x = ScrollX;
        float y = ScrollY;
        if (caretX - marginX < x)
        {
            x = caretX - marginX;
        }
        else if (caretX + metrics.CellWidth + marginX > x + textWidth)
        {
            x = caretX + metrics.CellWidth + marginX - textWidth;
        }

        if (caretY - marginY - stickyTop < y)
        {
            y = caretY - marginY - stickyTop;
        }
        else if (caretY + metrics.LineHeight + marginY > y + ViewportHeight)
        {
            y = caretY + metrics.LineHeight + marginY - ViewportHeight;
        }

        x = Math.Clamp(x, 0, Math.Max(0, ContentWidth - ViewportWidth));
        y = Math.Clamp(y, 0, Math.Max(0, ContentHeight - ViewportHeight));
        if (x != ScrollX || y != ScrollY)
        {
            ScrollTo(x, y);
        }
    }

    /// <summary>Sets the selection (clamped to the text) and redraws.</summary>
    public void Select(int anchor, int caret)
    {
        SetSelection(new SelectionState(anchor, caret));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _document.Buffer.Changed -= OnBufferChanged;
        _lightbulb?.Dispose();
        DetachLineMarks();
        DetachPublishedDiagnostics();
        Folding.Dispose();
        InlineCompletion.Dispose();
        _inlineChat?.Dispose();
        DisposeSemantic();
        if (_session is not null)
        {
            _session.Reclassified -= OnSessionReclassified;
            if (_ownsSession)
            {
                _session.Dispose();
            }
        }

        _bracketSpans.Dispose();
    }

    internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    /// <summary>Changes whenever the folds change (the rows move: the overview ruler and sticky scroll key on it).</summary>
    internal int FoldsStamp { get; private set; }

    internal void SetSelection(SelectionState selection, bool keepDesiredColumn = false, bool keepAutoClosed = false)
    {
        // One selection: the other carets, a column selection and Ctrl+D's whole-word mode end.
        _occurrenceWhole = false;
        SetSelectionsCore(new ReadOnlySpan<SelectionState>(in selection), 0, keepDesiredColumn, keepAutoClosed, keepBox: false);
    }

    /// <summary>
    /// The folds changed (a fold, an unfold, an edit that unfolded one): the content's height and the paint follow; with
    /// <paramref name="movedOutOfFolds"/>, a caret the new fold hides goes to the end of its header line.
    /// </summary>
    internal void OnFoldsChanged(bool movedOutOfFolds = false)
    {
        FoldsStamp++;
        if (movedOutOfFolds && Folding.Map.HasHidden)
        {
            var snapshot = Snapshot;
            var all = Selections;
            bool hidden = false;
            foreach (var s in all)
            {
                hidden |= Folding.Map.IsHidden(snapshot.GetLineIndex(s.Caret));
            }

            if (hidden)
            {
                var moved = System.Buffers.ArrayPool<SelectionState>.Shared.Rent(all.Length);
                try
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        int line = snapshot.GetLineIndex(all[i].Caret);
                        if (Folding.Map.IsHidden(line))
                        {
                            int header = LineAtRow(RowOfLine(line));
                            moved[i] = SelectionState.At(snapshot.GetLine(header).End);
                        }
                        else
                        {
                            moved[i] = all[i];
                        }
                    }

                    SetSelectionsCore(moved.AsSpan(0, all.Length), PrimarySelectionIndex, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
                }
                finally
                {
                    System.Buffers.ArrayPool<SelectionState>.Shared.Return(moved);
                }
            }
        }

        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>The line whose fold chevron is at the view point (the gutter's fold column), or null.</summary>
    internal int? FoldChevronAt(float viewX, float viewY)
    {
        if (!Folding.IsAvailable)
        {
            return null;
        }

        float right = GutterWidth;
        if (viewX < right - (FoldGutterCells * Metrics.CellWidth) || viewX >= right)
        {
            return null;
        }

        int row = (int)MathF.Floor((viewY + ScrollY) / Metrics.LineHeight);
        if (row < 0 || row >= RowCount)
        {
            return null;
        }

        int line = LineAtRow(row);
        if (RowOfLine(line) != row)
        {
            return null;                                          // a wrapped line's chevron is on its first row
        }

        return Folding.IsFoldable(line) || Folding.IsFolded(line) ? line : null;
    }

    private void OnSessionReclassified(object? sender, EventArgs e) => Reclassified?.Invoke(this, EventArgs.Empty);

    private void ScrollTo(float x, float y)
    {
        ScrollX = x;
        ScrollY = y;
        ScrollRequested?.Invoke(this, new EditorScrollEventArgs(x, y));
        Invalidate();
    }

    private void NoteInput()
    {
        if (!_inputPending)
        {
            _inputPending = true;
            _inputTimestamp = _time.GetTimestamp();
        }
    }

    private FontMetrics Measure()
    {
        var metrics = _measurer.Measure(new EditorFont(Options.FontFamily, Options.FontSize), Options.LineSpacing);
        if (!(metrics.CellWidth > 0) || !(metrics.LineHeight > 0))
        {
            throw new InvalidOperationException($"The text measurer returned {metrics} for {Options.FontFamily} {Options.FontSize}: a cell and a line need a positive size.");
        }

        return metrics;
    }

    private static string PlainTextName(string? path)
    {
        if (path is null)
        {
            return "Plain text";
        }

        var extension = Path.GetExtension(path);
        return extension.Length == 0 ? "Plain text — no language for a file without an extension" : $"Plain text — no language for {extension}";
    }

    private int PositionAtColumn(in TextLine line, float column, bool nearest)
    {
        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(Snapshot, line, ref buffer);
            return line.Start + TextReading.IndexAtColumn(text, column, Options.TabSize, nearest);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        NoteLineChange(e);                                   // the minimap moves its kept lines through it (piece 2b)
        // The folds first: the selections mapped below may land on lines they hide; then the rows and the bracket states.
        Folding.OnBufferChanged(e);
        _wordWrap?.OnBufferChanged(e);
        _bracketColors?.OnBufferChanged(e);
        if (!_applying)
        {
            // A change that did not come through this editor (a reload, another view): the selection follows the edits.
            var edits = e.Edits;
            if (HasMultipleCarets)
            {
                var mapped = System.Buffers.ArrayPool<SelectionState>.Shared.Rent(_carets.Count);
                try
                {
                    for (int i = 0; i < _carets.Count; i++)
                    {
                        mapped[i] = new SelectionState(MapThrough(_carets[i].Anchor, edits), MapThrough(_carets[i].Caret, edits));
                    }

                    SetSelectionsCore(mapped.AsSpan(0, _carets.Count), _primary, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
                }
                finally
                {
                    System.Buffers.ArrayPool<SelectionState>.Shared.Return(mapped);
                }
            }
            else
            {
                SetSelection(new SelectionState(MapThrough(_selection.Anchor, edits), MapThrough(_selection.Caret, edits)));
            }
        }


        int lines = e.After.LineCount;
        int digits = TextReading.Digits(lines);
        if (lines != _lastLineCount || digits != _lastDigits)
        {
            _lastLineCount = lines;
            _lastDigits = digits;
            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        }

        OnSemanticBufferChanged(e);
        InlineCompletion.OnChanged();
        _inlineChat?.OnBufferChanged(e);
        Invalidate();
    }

    private static int MapThrough(int position, IReadOnlyList<TextEdit> edits)
    {
        int delta = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
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
}
