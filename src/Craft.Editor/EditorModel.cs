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
    private readonly ITextMeasurer _measurer;
    private readonly IEditorClipboard _clipboard;
    private readonly TimeProvider _time;
    private readonly string _indentUnit;
    private readonly TokenSpanBuffer _bracketSpans = new(64);
    private SelectionState _selection;
    private int _desiredColumn = -1;
    private bool _applying;
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

    /// <summary>An editor over <paramref name="document"/>.</summary>
    /// <param name="document">The document it edits (the caller keeps owning it).</param>
    /// <param name="language">Its language, or null for plain text (<see cref="LanguageName"/> then says so and why).</param>
    /// <param name="options">The settings (the defaults when null).</param>
    /// <param name="theme">The colours (the options' theme when null).</param>
    /// <param name="measurer">Measures the options' font on the platform.</param>
    /// <param name="clipboard">The clipboard for copy, cut and paste.</param>
    /// <param name="time">The clock for click counting and the input-to-paint telemetry (the system's when null).</param>
    /// <param name="workspaceRoot">The open folder the document belongs to (its language's workspace), or null.</param>
    public EditorModel(ITextDocument document, ILanguage? language, CraftOptions? options, ITheme? theme, ITextMeasurer measurer,
        IEditorClipboard clipboard, TimeProvider? time = null, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(clipboard);
        _document = document;
        _language = language;
        Options = options ?? new CraftOptions();
        _theme = theme ?? EditorThemes.Get(Options.Theme);
        _measurer = measurer;
        _clipboard = clipboard;
        _time = time ?? TimeProvider.System;
        _indentUnit = Options.InsertSpaces ? new string(' ', Options.TabSize) : "\t";
        Metrics = Measure();
        LanguageName = language?.Name ?? PlainTextName(document.FilePath);
        Find = new EditorFind(this);
        _session = language?.Open(document.Buffer, new LanguageOpenContext(document.FilePath, workspaceRoot, Options.ToLanguageSettings()));
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
        document.Buffer.Changed += OnBufferChanged;
    }

    /// <summary>The document it edits.</summary>
    public ITextDocument Document => _document;

    /// <summary>The document's current text.</summary>
    public ITextSnapshot Snapshot => _document.Buffer.Current;

    /// <summary>Its language, or null for plain text.</summary>
    public ILanguage? Language => _language;

    /// <summary>The language session over the document's buffer (null for plain text); disposed with the editor.</summary>
    public ILanguageSession? LanguageSession => _session;

    /// <summary>The language's comment and bracket rules (<see cref="LanguageRules.Plain"/> for plain text).</summary>
    public LanguageRules Rules => _language?.Rules ?? LanguageRules.Plain;

    /// <summary>The language's name, or "Plain text" with the reason ("Plain text — no language for .xyz").</summary>
    public string LanguageName { get; }

    /// <summary>The settings.</summary>
    public CraftOptions Options { get; }

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

    /// <summary>The selection (its caret is where typing goes).</summary>
    public SelectionState Selection => _selection;

    /// <summary>Whether typing replaces the character after the caret (Insert toggles it).</summary>
    public bool Overwrite { get; private set; }

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

    /// <summary>Raised when <see cref="ContentWidth"/>, <see cref="ContentHeight"/> or <see cref="GutterWidth"/> changed.</summary>
    public event EventHandler? ContentSizeChanged;

    /// <summary>Raised on Ctrl+S.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised on Ctrl+F (find) and Ctrl+H (replace).</summary>
    public event EventHandler<FindRequestedEventArgs>? FindRequested;

    /// <summary>Raised when a copy or a paste failed on the platform's clipboard.</summary>
    public event EventHandler<ClipboardFailedEventArgs>? ClipboardFailed;

    /// <summary>The caret's line index.</summary>
    public int CaretLine => Snapshot.GetLineIndex(_selection.Caret);

    /// <summary>The caret's column (tabs expanded), from 0.</summary>
    public int CaretColumn => ColumnOf(_selection.Caret);

    /// <summary>The line-number gutter's width: the line count's digits plus the padding, in cells.</summary>
    public float GutterWidth => (TextReading.Digits(Snapshot.LineCount) + Options.GutterPaddingCells) * Metrics.CellWidth;

    /// <summary>Where column 0 of the text starts, in content coordinates (the gutter's right edge).</summary>
    public float TextLeft => GutterWidth;

    /// <summary>The content's height: every line plus the bottom margin.</summary>
    public float ContentHeight => (Snapshot.LineCount + Options.BottomMarginLines) * Metrics.LineHeight;

    /// <summary>
    /// The content's width: the longest line seen while drawing (or with the caret on it) plus the margin. It only grows:
    /// no whole-file scan is made to find the longest line.
    /// </summary>
    public float ContentWidth => TextLeft + (_maxColumns + Options.ScrollMarginColumns + 1) * Metrics.CellWidth;

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

    /// <summary>The lines a viewport at <paramref name="scrollY"/> of <paramref name="viewportHeight"/> shows, plus one.</summary>
    public LineRange VisibleLines(float scrollY, float viewportHeight)
    {
        int last = Snapshot.LineCount - 1;
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
        int line = Snapshot.GetLineIndex(Math.Clamp(position, 0, Snapshot.Length));
        return new EditorPoint(TextLeft + ColumnOf(position) * Metrics.CellWidth, line * Metrics.LineHeight);
    }

    /// <summary>The position nearest to the content point (<paramref name="x"/>, <paramref name="y"/>), clamped to the text.</summary>
    public int PositionFromPoint(float x, float y)
    {
        var snapshot = Snapshot;
        int index = Math.Clamp((int)MathF.Floor(y / Metrics.LineHeight), 0, snapshot.LineCount - 1);
        if (y < 0)
        {
            index = 0;
        }

        float column = Math.Max(0, (x - TextLeft) / Metrics.CellWidth);
        return PositionAtColumn(snapshot.GetLine(index), column, nearest: true);
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
        ScrollX = Math.Max(0, scrollX);
        ScrollY = Math.Max(0, scrollY);
        ViewportWidth = Math.Max(0, viewportWidth);
        ViewportHeight = Math.Max(0, viewportHeight);
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
        int column = CaretColumn;
        NoteLineColumns(column);
        float caretX = column * metrics.CellWidth;
        float caretY = CaretLine * metrics.LineHeight;
        float textWidth = Math.Max(metrics.CellWidth, ViewportWidth - TextLeft);
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

        if (caretY - marginY < y)
        {
            y = caretY - marginY;
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
        DisposeSemantic();
        if (_session is not null)
        {
            _session.Reclassified -= OnSessionReclassified;
            _session.Dispose();
        }

        _bracketSpans.Dispose();
    }

    internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    internal void SetSelection(SelectionState selection, bool keepDesiredColumn = false, bool keepAutoClosed = false)
    {
        int length = Snapshot.Length;
        selection = new SelectionState(Math.Clamp(selection.Anchor, 0, length), Math.Clamp(selection.Caret, 0, length));
        if (!keepDesiredColumn)
        {
            _desiredColumn = -1;
        }

        if (!keepAutoClosed)
        {
            _autoClosed.Clear();
        }

        bool moved = selection != _selection;
        _selection = selection;
        if (moved)
        {
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
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
        if (!_applying)
        {
            // A change that did not come through this editor (a reload, another view): the selection follows the edits.
            var edits = e.Edits;
            SetSelection(new SelectionState(MapThrough(_selection.Anchor, edits), MapThrough(_selection.Caret, edits)));
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
